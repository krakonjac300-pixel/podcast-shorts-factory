"""First-run setup wizard — `python run.py setup`.

Walks a new user through the only choices that matter (AI provider + key, niche,
platforms, call-to-action, notifications) and writes them into `.env` and
`config.yaml`. Config edits are line-targeted so all the explanatory comments in
config.yaml are preserved. Safe to re-run anytime to change settings.
"""
from __future__ import annotations

import re
import shutil
import sys
from pathlib import Path

from rich.console import Console
from rich.panel import Panel
from rich.prompt import Confirm, Prompt

ROOT = Path(__file__).resolve().parent.parent
ENV = ROOT / ".env"
ENV_EXAMPLE = ROOT / ".env.example"
CONFIG = ROOT / "config.yaml"

console = Console()

# provider -> (env var name, default llm.model, is_free)
PROVIDERS = {
    "openrouter": ("OPENROUTER_API_KEY", "nvidia/nemotron-3-ultra-550b-a55b:free", True),
    "groq":       ("GROQ_API_KEY",       "llama-3.3-70b-versatile", True),
    "gemini":     ("GEMINI_API_KEY",     "gemini-2.0-flash", True),
    "anthropic":  ("ANTHROPIC_API_KEY",  None, False),   # uses the models: block
    "ollama":     (None,                 "llama3.1", True),  # local, no key
}

KEY_URLS = {
    "openrouter": "https://openrouter.ai/keys",
    "groq":       "https://console.groq.com/keys",
    "gemini":     "https://aistudio.google.com/apikey",
    "anthropic":  "https://console.anthropic.com",
    "ollama":     "(no key — just install & run Ollama locally)",
}


# ── small file editors (comment-preserving) ─────────────────────────────────
def set_env_var(key: str, value: str) -> None:
    """Set KEY=value in .env, replacing an existing uncommented line or appending."""
    if not ENV.exists():
        if ENV_EXAMPLE.exists():
            shutil.copyfile(ENV_EXAMPLE, ENV)
        else:
            ENV.write_text("", encoding="utf-8")
    lines = ENV.read_text(encoding="utf-8").splitlines()
    out, found = [], False
    for ln in lines:
        s = ln.lstrip()
        if not s.startswith("#") and "=" in s and s.split("=", 1)[0].strip() == key:
            out.append(f"{key}={value}")
            found = True
        else:
            out.append(ln)
    if not found:
        out.append(f"{key}={value}")
    ENV.write_text("\n".join(out) + "\n", encoding="utf-8")


def set_yaml_scalar(key: str, new_value: str, indent: str = "  ") -> bool:
    """Replace the value of an `<indent><key>:` line in config.yaml, keeping any
    trailing inline # comment. Returns True if the key was found."""
    lines = CONFIG.read_text(encoding="utf-8").splitlines()
    pat = re.compile(rf"^({re.escape(indent)}{re.escape(key)}:)\s*([^#\n]*?)\s*(#.*)?$")
    for i, ln in enumerate(lines):
        m = pat.match(ln)
        if m:
            comment = m.group(3) or ""
            sep = "   " if comment else ""
            lines[i] = f"{m.group(1)} {new_value}{sep}{comment}".rstrip()
            CONFIG.write_text("\n".join(lines) + "\n", encoding="utf-8")
            return True
    return False


# ── steps ────────────────────────────────────────────────────────────────────
def _check_prereqs() -> None:
    console.print("[bold]Checking prerequisites…[/]")
    ok = True
    if sys.version_info < (3, 11):
        console.print(f"  [red]✗ Python {sys.version_info.major}.{sys.version_info.minor} "
                      f"— need 3.11+[/]")
        ok = False
    else:
        console.print(f"  [green]✓ Python {sys.version_info.major}.{sys.version_info.minor}[/]")
    if shutil.which("ffmpeg"):
        console.print("  [green]✓ ffmpeg found[/]")
    else:
        console.print("  [yellow]! ffmpeg not on PATH — install it before rendering "
                      "(https://ffmpeg.org/download.html)[/]")
    if not ok:
        console.print("[red]Please fix the above, then run setup again.[/]")
        sys.exit(1)


def _choose_provider() -> str:
    console.print("\n[bold]1) AI provider[/] — which service runs the agents' thinking?")
    console.print("   [dim]free options: openrouter, groq, gemini, ollama · paid/best: anthropic[/]")
    provider = Prompt.ask("   provider", choices=list(PROVIDERS), default="openrouter")
    env_key, default_model, is_free = PROVIDERS[provider]

    set_yaml_scalar("provider", provider)
    if default_model:
        set_yaml_scalar("model", default_model)

    if env_key is None:  # ollama
        console.print("   [green]Ollama needs no key.[/] Make sure `ollama serve` is running.")
        return provider

    console.print(f"   Get a {'FREE ' if is_free else ''}key: [cyan]{KEY_URLS[provider]}[/]")
    key = Prompt.ask(f"   paste your {env_key} (or leave blank to add later)",
                     default="", show_default=False).strip()
    if key:
        set_env_var(env_key, key)
        console.print("   [green]✓ key saved to .env[/]")
    else:
        console.print(f"   [yellow]skipped — add {env_key}=… to .env before running[/]")
    return provider


NICHE_PRESETS = {
    "1": ("Money & business", "money, entrepreneurship, side hustles, investing"),
    "2": ("Health & fitness", "health, fitness, nutrition, longevity, biohacking"),
    "3": ("Mindset & motivation", "mindset, motivation, self-improvement, discipline"),
    "4": ("Comedy & stories", "comedy, funny stories, wild moments, reactions"),
    "5": ("Sports", "sports hot takes, athlete stories, game reactions"),
    "6": ("Tech & AI", "tech, AI, startups, future trends"),
    "7": ("Relationships & dating", "relationships, dating, psychology"),
}


def _set_niche() -> None:
    console.print("\n[bold]2) Your niche[/] — steers what the Trend Scout & Finder look for.")
    for k, (name, topics) in NICHE_PRESETS.items():
        console.print(f"   [cyan]{k}[/]) {name}  [dim]({topics})[/]")
    console.print("   [cyan]8[/]) Custom — type your own")
    pick = Prompt.ask("   pick", choices=[*NICHE_PRESETS, "8"], default="1")
    if pick == "8":
        niche = Prompt.ask("   describe your niche (a few comma-separated topics)",
                           default="money, business, health, mindset, comedy")
    else:
        niche = NICHE_PRESETS[pick][1]
    set_yaml_scalar("niche", f'"viral podcast clips — {niche}"')
    console.print(f"   [green]✓ niche saved:[/] {niche}")


def _set_platforms() -> None:
    console.print("\n[bold]3) Platforms[/] — where should finished clips go?")
    console.print("   [dim]YouTube posts via API. TikTok/Instagram export files + captions "
                  "to ready_to_post\\ for manual/scheduler posting.[/]")
    picks = []
    for p in ("youtube", "tiktok", "instagram"):
        if Confirm.ask(f"   include {p}?", default=(p == "youtube")):
            picks.append(p)
    if not picks:
        picks = ["youtube"]
    flow = "[" + ", ".join(f'"{p}"' for p in picks) + "]"
    set_yaml_scalar("platforms", flow)
    console.print(f"   [green]✓ platforms: {', '.join(picks)}[/]")

    if "youtube" in picks:
        pub = Prompt.ask("   YouTube upload privacy",
                         choices=["private", "unlisted", "public"], default="private")
        set_yaml_scalar("privacy", f'"{pub}"')
        console.print(f"   [green]✓ uploads default to {pub}[/] "
                      "[dim](start private, review, then go public)[/]")


def _set_cta() -> None:
    console.print("\n[bold]4) Call-to-action[/] — the text shown in the last ~2s of each clip.")
    cta = Prompt.ask("   CTA text", default="FOLLOW FOR MORE")
    set_yaml_scalar("cta_text", f'"{cta}"')
    console.print("   [green]✓ CTA saved[/]")


def _set_notifications() -> None:
    console.print("\n[bold]5) Phone notifications[/] (optional) — free push via the ntfy app.")
    if Confirm.ask("   enable phone push?", default=False):
        topic = Prompt.ask("   pick a SECRET, unique topic name (e.g. myclips-8f3a2)")
        set_yaml_scalar("ntfy_topic", f'"{topic.strip()}"')
        console.print("   [green]✓ install the free 'ntfy' app and subscribe to that topic.[/]")
    else:
        set_yaml_scalar("ntfy_topic", '""')
        console.print("   [dim]phone push disabled (Windows toasts still work).[/]")


def _youtube_note() -> None:
    console.print("\n[bold]6) YouTube auto-upload[/] (optional, do later)")
    console.print("   To post to YouTube automatically you need your OWN Google OAuth file:")
    console.print("   • Follow [cyan]docs/youtube-setup.md[/] to create [italic]client_secret.json[/]")
    console.print("   • Then run [cyan]python run.py auth-youtube[/] once to sign in.")


def run() -> None:
    console.print(Panel.fit(
        "[bold]Podcast Shorts Factory — Setup[/]\n"
        "A few quick questions to get you running. Re-run anytime with "
        "[cyan]python run.py setup[/].",
        border_style="cyan"))

    _check_prereqs()
    _choose_provider()
    _set_niche()
    _set_platforms()
    _set_cta()
    _set_notifications()
    _youtube_note()

    console.print(Panel.fit(
        "[bold green]Setup complete![/]\n\n"
        "Try it now:\n"
        "  [cyan]python run.py auto \"https://www.youtube.com/watch?v=VIDEO_ID\"[/]\n\n"
        "Or go fully hands-free — set [italic]scheduler.sources[/] in config.yaml, then:\n"
        "  [cyan]python run.py daily[/]",
        border_style="green"))


if __name__ == "__main__":
    run()
