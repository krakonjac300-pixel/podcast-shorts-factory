"""Skill library — domain playbooks injected into each agent's AI prompts.

Each .md file in this folder is one "installed skill". Agents declare which
skills they use in config.yaml (`skills:` section); the relevant playbooks are
concatenated into the system context when that agent calls Claude.
"""
from __future__ import annotations

from pathlib import Path

SKILLS_DIR = Path(__file__).resolve().parent


def available() -> list[str]:
    """All installed skill names (file stems)."""
    return sorted(p.stem for p in SKILLS_DIR.glob("*.md"))


def load(names: list[str]) -> str:
    """Concatenate the named skill playbooks into one prompt block."""
    chunks = []
    for name in names or []:
        f = SKILLS_DIR / f"{name}.md"
        if f.exists():
            chunks.append(f.read_text(encoding="utf-8").strip())
    if not chunks:
        return ""
    body = "\n\n---\n\n".join(chunks)
    return ("You have the following expert skills installed. Apply them rigorously.\n\n"
            f"{body}\n")


def missing(names: list[str]) -> list[str]:
    """Names requested in config that have no matching playbook file."""
    have = set(available())
    return [n for n in (names or []) if n not in have]
