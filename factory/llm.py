"""Provider-switchable LLM layer, with automatic fallback.

Every agent calls EITHER Anthropic (Claude, paid) OR any OpenAI-compatible
free provider (OpenRouter, NVIDIA NIM, Groq, Google Gemini, local Ollama)
without changing agent code. Pick the primary in config.yaml -> llm.provider,
and list fallbacks in llm.fallbacks. When the primary is rate-limited or errors
(the #1 failure that used to ship degraded default-plan clips), the next
provider with a key is tried automatically — so a run never silently drops to a
dumb fallback while a working provider sits unused.

Two helpers cover everything the agents need:
  - call_tool(agent, prompt, name, schema)  -> dict   (structured output)
  - call_text(agent, prompt)                -> str     (free-form text)
"""
from __future__ import annotations

import json

from .config import cfg

# provider -> (api-key env var, base_url, default model with good tool-calling)
PROVIDERS = {
    "openrouter": ("OPENROUTER_API_KEY", "https://openrouter.ai/api/v1",
                   "meta-llama/llama-3.3-70b-instruct"),
    "nvidia": ("NVIDIA_API_KEY", "https://integrate.api.nvidia.com/v1",
               "meta/llama-3.3-70b-instruct"),
    "groq":   ("GROQ_API_KEY", "https://api.groq.com/openai/v1",
               "llama-3.3-70b-versatile"),
    "gemini": ("GEMINI_API_KEY", "https://generativelanguage.googleapis.com/v1beta/openai/",
               "gemini-2.5-flash"),
    "ollama": (None, "http://localhost:11434/v1", "llama3.1"),
    "openai": ("OPENAI_API_KEY", None, "gpt-4o-mini"),  # base from OPENAI_BASE_URL
}


def provider() -> str:
    return (cfg.get("llm.provider", "anthropic") or "anthropic").lower()


def _has_key(p: str) -> bool:
    if p == "anthropic":
        return bool(cfg.env("ANTHROPIC_API_KEY"))
    if p == "ollama":
        return True
    env = PROVIDERS.get(p, (None,))[0]
    return bool(env and cfg.env(env))


def _chain() -> list[str]:
    """Primary provider first, then each configured fallback that has a key."""
    prim = provider()
    chain = [prim]
    for f in cfg.get("llm.fallbacks", []) or []:
        f = str(f).lower()
        if f != prim and f not in chain and _has_key(f):
            chain.append(f)
    return chain


def model(agent: str, p: str | None = None) -> str:
    """Model for `agent` on provider `p` (defaults to the primary). The primary
    honors llm.model; fallbacks use their own sensible default."""
    p = p or provider()
    if p == "anthropic":
        return cfg.model_for(agent)
    if p == provider():
        return cfg.get("llm.model") or PROVIDERS.get(p, (None, None, ""))[2]
    return PROVIDERS.get(p, (None, None, ""))[2]


def available() -> bool:
    """True if ANY provider in the chain can be called."""
    return any(_has_key(p) for p in _chain())


def describe() -> str:
    chain = _chain()
    tail = f" (+{','.join(chain[1:])} fallback)" if len(chain) > 1 else ""
    return f"{chain[0]}:{model('finder', chain[0])}{tail}"


# ── OpenAI-compatible client (OpenRouter / NVIDIA / Groq / Gemini / Ollama) ──
def _openai_client(p: str):
    from openai import OpenAI
    key_env, base, _ = PROVIDERS[p]
    api_key = cfg.env(key_env) if key_env else "ollama"
    base_url = cfg.env("OPENAI_BASE_URL") if p == "openai" else base
    # A hard timeout is essential: free providers sometimes HANG (not error),
    # which would block forever and stop the fallback chain from ever reaching
    # the next provider. Fail fast → the next provider takes over.
    return OpenAI(api_key=api_key or "none", base_url=base_url,
                  max_retries=1, timeout=float(cfg.get("llm.timeout_s", 45)))


def _try_tool(p, agent, prompt, name, schema, max_tokens):
    """One provider's attempt at a structured call. Raises on transport error;
    returns dict or None."""
    if p == "anthropic":
        from anthropic import Anthropic
        client = Anthropic(api_key=cfg.env("ANTHROPIC_API_KEY"))
        msg = client.messages.create(
            model=model(agent, p), max_tokens=max_tokens,
            tools=[{"name": name, "description": f"Return {name}.",
                    "input_schema": schema}],
            tool_choice={"type": "tool", "name": name},
            messages=[{"role": "user", "content": prompt}])
        for block in msg.content:
            if block.type == "tool_use":
                return dict(block.input)
        return None
    client = _openai_client(p)
    resp = client.chat.completions.create(
        model=model(agent, p), max_tokens=max_tokens,
        messages=[{"role": "user", "content": prompt}],
        tools=[{"type": "function", "function": {
            "name": name, "description": f"Return {name}.", "parameters": schema}}],
        tool_choice={"type": "function", "function": {"name": name}})
    choice = (resp.choices or [None])[0]
    calls = choice.message.tool_calls if choice and choice.message else None
    if calls:
        try:
            return json.loads(calls[0].function.arguments)
        except (json.JSONDecodeError, TypeError):
            return None
    return None


def call_tool(agent: str, prompt: str, name: str, schema: dict,
              max_tokens: int = 2000) -> dict | None:
    """Force a structured (tool/function) call. Walks the provider chain: a
    rate-limit or error on one provider falls through to the next."""
    result = None
    for p in _chain():
        try:
            result = _try_tool(p, agent, prompt, name, schema, max_tokens)
            if result is not None:
                return result
        except Exception:  # noqa: BLE001 - try the next provider in the chain
            continue
    return result


def _try_text(p, agent, prompt, max_tokens):
    if p == "anthropic":
        from anthropic import Anthropic
        client = Anthropic(api_key=cfg.env("ANTHROPIC_API_KEY"))
        msg = client.messages.create(
            model=model(agent, p), max_tokens=max_tokens,
            messages=[{"role": "user", "content": prompt}])
        return "".join(b.text for b in msg.content if b.type == "text")
    client = _openai_client(p)
    resp = client.chat.completions.create(
        model=model(agent, p), max_tokens=max_tokens,
        messages=[{"role": "user", "content": prompt}])
    choice = (resp.choices or [None])[0]
    return (choice.message.content if choice and choice.message else "") or ""


def call_text(agent: str, prompt: str, max_tokens: int = 1500) -> str:
    """Plain text completion, with the same provider-chain fallback."""
    for p in _chain():
        try:
            out = _try_text(p, agent, prompt, max_tokens)
            if out and out.strip():
                return out
        except Exception:  # noqa: BLE001 - fall through to the next provider
            continue
    return ""


def call_vision(agent: str, prompt: str, image_paths: list,
                max_tokens: int = 800) -> str:
    """Look at images + answer in text. Walks the chain; empty string on total
    failure — vision is an enhancement, never a blocker."""
    import base64
    imgs = image_paths[:4]
    for p in _chain():
        try:
            if p == "anthropic":
                from anthropic import Anthropic
                client = Anthropic(api_key=cfg.env("ANTHROPIC_API_KEY"))
                content = [{"type": "text", "text": prompt}] + [
                    {"type": "image", "source": {"type": "base64",
                     "media_type": "image/jpeg",
                     "data": base64.b64encode(open(ip, "rb").read()).decode()}}
                    for ip in imgs]
                msg = client.messages.create(model=model(agent, p),
                                             max_tokens=max_tokens,
                                             messages=[{"role": "user",
                                                        "content": content}])
                out = "".join(b.text for b in msg.content if b.type == "text")
            else:
                parts = [{"type": "text", "text": prompt}]
                for ip in imgs:
                    b64 = base64.b64encode(open(ip, "rb").read()).decode()
                    parts.append({"type": "image_url",
                                  "image_url": {"url": f"data:image/jpeg;base64,{b64}"}})
                client = _openai_client(p)
                vmodel = cfg.get("llm.vision_model") or model(agent, p)
                resp = client.chat.completions.create(
                    model=vmodel, max_tokens=max_tokens,
                    messages=[{"role": "user", "content": parts}])
                choice = (resp.choices or [None])[0]
                out = (choice.message.content if choice and choice.message else "") or ""
            if out and out.strip():
                return out
        except Exception:  # noqa: BLE001
            continue
    return ""
