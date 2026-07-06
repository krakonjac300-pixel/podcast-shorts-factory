"""Shared access to the self-learning outputs so EVERY agent improves over time.

`learnings.md` is written by the Manager from real performance data; `trends.md`
by the Trend Scout. Finder, Editor (planner) and Uploader all read these so the
whole factory gets better at finding, editing, and packaging clips as results roll in.
"""
from __future__ import annotations

from .config import ROOT, cfg


def learnings() -> str:
    f = ROOT / cfg.get("manager.learnings_file", "learnings.md")
    if f.exists():
        return f.read_text(encoding="utf-8").strip()
    return "(no performance learnings yet — first runs)"


def trends() -> str:
    f = ROOT / cfg.get("trend_scout.trends_file", "trends.md")
    if f.exists():
        return f.read_text(encoding="utf-8").strip()
    return "(no trend scan yet)"
