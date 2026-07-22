"""Trend Scout — keeps the factory current.

Searches the web (free, via DuckDuckGo) for what's trending in short-form right
now for your niche, then has the LLM distill it into `trends.md`. The Finder
reads it so clip selection leans into live trends — the "follow the trends so
videos always perform" piece, alongside the Manager's performance-learning loop.

Works on ANY provider (Anthropic or a free one) — search is provider-independent.
"""
from __future__ import annotations

from rich.console import Console

from .. import llm
from ..config import ROOT, cfg

console = Console()


def _search(niche: str) -> str:
    """Gather fresh snippets from the web for the niche. Returns digest text."""
    try:
        from ddgs import DDGS
    except Exception:  # noqa: BLE001
        return ""
    queries = [
        f"trending {niche} short form video topics this week",
        f"viral TikTok Reels hooks formats {niche} 2026",
        "trending audio sounds short form creators now",
    ]
    lines = []
    try:
        with DDGS() as ddgs:
            for q in queries:
                for r in ddgs.text(q, max_results=5):
                    title = r.get("title", "")
                    body = r.get("body", "")
                    if title or body:
                        lines.append(f"- {title}: {body}")
    except Exception as ex:  # noqa: BLE001
        console.print(f"[yellow]web search hiccup (continuing): {ex}[/]")
    return "\n".join(lines[:30])


def scout() -> bool:
    """Run a trend scan and write trends.md. Returns success."""
    if not cfg.get("trend_scout.enabled", True):
        return False
    if not llm.available():
        console.print("[yellow]Trend Scout needs an LLM provider configured — skipping.[/]")
        return False

    niche = cfg.get("trend_scout.niche", "podcast clips")
    console.print(f"[bold yellow]TREND SCOUT[/] searching current trends for "
                  f"[bold]{niche}[/] ({llm.describe()})…")

    digest = _search(niche)
    if not digest:
        console.print("[yellow]No web results — writing a generic brief.[/]")

    prompt = f"""You are a short-form (TikTok/Reels/Shorts) trend analyst for the niche "{niche}".
Below are fresh web search snippets from this week. Synthesize them into an
actionable brief.

Search snippets:
{digest or "(no fresh results — use evergreen short-form best practices)"}

Write a concise markdown brief (<300 words) titled "# Current Trends" that a
clip-selection AI can act on: hot topics/themes, hook formats overperforming now,
trending audio styles (note: added in-app, not downloaded), angles to lean into,
and overused ones to avoid. We are a CURATOR channel, so prioritize CROSS-SHOW
THEMES — debates/questions multiple podcasts are arguing about this week (e.g.
"who stops France", "is X finished") — over single-show topics; name 2-3 such
themes explicitly. Output ONLY the brief."""

    try:
        text = llm.call_text("trend_scout", prompt, max_tokens=1500)
    except Exception as ex:  # noqa: BLE001 - never let a trend scan break the pipeline
        console.print(f"[yellow]Trend Scout failed (continuing without): {ex}[/]")
        return False

    if not text.strip():
        return False
    out = ROOT / cfg.get("trend_scout.trends_file", "trends.md")
    out.write_text(text.strip() + "\n", encoding="utf-8")
    console.print(f"[green]✓ Wrote {out.name}[/] — the Finder will use it next run.")
    return True
