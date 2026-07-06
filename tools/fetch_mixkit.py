"""Download real sound effects + music from Mixkit into the asset packs.

Mixkit License: free for commercial use, no attribution required — safe for
monetized videos (https://mixkit.co/license/#sfxFree).

- SFX land in assets/sfx as <type>.wav + <type>-2.wav variants (the editor's
  _resolve_sfx picks randomly among variants for variety).
- Music lands in assets/music as <mood>-<title>.mp3; the synth placeholder
  tracks are removed once a real track exists for that mood.

Run:  .venv\\Scripts\\python.exe tools\\fetch_mixkit.py
"""
from __future__ import annotations

import re
import subprocess
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SFX_DIR = ROOT / "assets" / "sfx"
MUSIC_DIR = ROOT / "assets" / "music"
UA = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}

# SFX: category page + title keywords to curate, mapped to our pack names.
SFX = {
    "whoosh": ("whoosh", ["fast whoosh transition", "air woosh"]),
    "swoosh": ("whoosh", ["cinematic whoosh fast transition", "swirling whoosh"]),
    "impact": ("impact", ["cinematic", "deep", "epic", "hit"]),
    "ding":   ("bell",   ["notification", "correct", "achievement", "bell"]),
    "pop":    ("pop",    ["pop", "bubble", "click"]),
    "riser":  ("cinematic", ["riser", "build", "trailer", "tension", "suspense"]),
}

# Music: our planner moods → Mixkit genre slugs to try, in order.
MUSIC = {
    "ambient": ["ambient", "atmospheres"],
    "lofi":    ["chillout", "acid-jazz", "ambient"],
    "tense":   ["cinematic", "trailer", "drum-n-bass", "atmospheres"],
    "upbeat":  ["funk", "dance", "pop", "edm"],
}


def _page_items(url: str) -> list[tuple[str, str]]:
    """(title, mp3_url) pairs from a server-rendered Mixkit listing page."""
    try:
        req = urllib.request.Request(url, headers=UA)
        with urllib.request.urlopen(req, timeout=30) as r:
            html = r.read().decode("utf-8", errors="ignore")
    except Exception:  # noqa: BLE001
        return []
    out = []
    for m in re.finditer(
            r'data-audio-player-preview-url-value="(https://assets\.mixkit\.co[^"]+\.mp3)"',
            html):
        ctx = html[m.end():m.end() + 1500]
        cands = re.findall(r">\s*([A-Za-z][^<>{}\n]{3,60}?)\s*<", ctx)
        title = next((c.strip() for c in cands
                      if not c.strip().startswith(("Download", "Add", "Free", "0:"))),
                     "")
        if title:
            out.append((title, m.group(1)))
    return out


def _download(url: str, dest: Path) -> bool:
    try:
        req = urllib.request.Request(url, headers=UA)
        with urllib.request.urlopen(req, timeout=60) as r:
            dest.write_bytes(r.read())
        return dest.stat().st_size > 5000
    except Exception:  # noqa: BLE001
        return False


def fetch_sfx() -> None:
    SFX_DIR.mkdir(parents=True, exist_ok=True)
    for name, (category, keywords) in SFX.items():
        items = _page_items(f"https://mixkit.co/free-sound-effects/{category}/")
        picks = [(t, u) for t, u in items
                 if any(k in t.lower() for k in keywords)][:2] or items[:2]
        for i, (title, url) in enumerate(picks):
            wav = SFX_DIR / (f"{name}.wav" if i == 0 else f"{name}-{i + 1}.wav")
            mp3 = wav.with_suffix(".tmp.mp3")
            if _download(url, mp3):
                subprocess.run(["ffmpeg", "-y", "-i", str(mp3), "-t", "3",
                                str(wav)], capture_output=True)
                mp3.unlink(missing_ok=True)
                print(f"sfx  {wav.name:14} <- {title}")


def fetch_music(per_mood: int = 2) -> None:
    MUSIC_DIR.mkdir(parents=True, exist_ok=True)
    for mood, genres in MUSIC.items():
        got = 0
        for genre in genres:
            if got >= per_mood:
                break
            for title, url in _page_items(
                    f"https://mixkit.co/free-stock-music/{genre}/"):
                if got >= per_mood:
                    break
                slug = re.sub(r"[^a-z0-9]+", "-", title.lower()).strip("-")[:30]
                dest = MUSIC_DIR / f"{mood}-{slug}.mp3"
                if dest.exists() or _download(url, dest):
                    got += 1
                    print(f"music {dest.name:40} <- {title} [{genre}]")
        if got:                                  # real music beats synth pads
            synth = MUSIC_DIR / f"{mood}.wav"
            if synth.exists():
                synth.unlink()
                print(f"      removed synth placeholder {mood}.wav")


if __name__ == "__main__":
    fetch_sfx()
    fetch_music()
    print("done — Mixkit License: commercial use OK, no attribution needed.")
