"""Agent 10 - FOOTAGE RESEARCHER.

Every reference outlier we have studied does the same thing: when the speaker
mentions something concrete, the edit cuts to ~2 seconds of exactly that thing
while the audio continues (someone says "he put it all on roulette" and you SEE
chips hitting a roulette table). The talking head returns before the viewer
can miss it.

This agent finds that shot. Pexels' free VIDEO api (same PEXELS_API_KEY the
photo b-roll already uses), portrait preferred, cached by query so repeated
themes cost zero calls, trimmed to a ready-to-overlay cut with no audio.

Quality rules, learned from why photo b-roll got disabled as slop:
  * only CONCRETE nouns arrive here (the planner is already instructed);
  * a weak match is worse than no insert, so any doubt returns None;
  * the editor caps inserts per clip and keeps them off the payoff beat.
Never raises: None always means "render without the insert".
"""
from __future__ import annotations

import hashlib
import json
import subprocess
import urllib.parse
import urllib.request
from pathlib import Path

from rich.console import Console

from ..config import ROOT, cfg

console = Console()
CACHE = ROOT / "assets" / "broll_cache"
_UA = {"User-Agent": "PodcastShortsFactory/1.0"}


def _key() -> str:
    return (cfg.env("PEXELS_API_KEY", "") or "").strip()


def available() -> bool:
    return bool(_key())


def _cache_path(query: str, secs: float) -> Path:
    h = hashlib.md5(f"{query.lower().strip()}|{secs:.1f}".encode()).hexdigest()[:16]
    return CACHE / f"vid_{h}.mp4"


def _search(query: str) -> dict | None:
    """Best portrait hit from Pexels Videos, or None."""
    url = ("https://api.pexels.com/videos/search?"
           + urllib.parse.urlencode({"query": query, "orientation": "portrait",
                                     "size": "medium", "per_page": 6}))
    req = urllib.request.Request(url, headers={**_UA, "Authorization": _key()})
    with urllib.request.urlopen(req, timeout=25) as r:
        data = json.loads(r.read().decode())
    for hit in data.get("videos", []):
        if not 3 <= (hit.get("duration") or 0) <= 60:
            continue
        files = [f for f in hit.get("video_files", [])
                 if (f.get("width") or 0) < (f.get("height") or 1)
                 and (f.get("height") or 0) >= 1000
                 and str(f.get("file_type", "")).endswith("mp4")]
        if not files:
            continue
        files.sort(key=lambda f: abs((f.get("height") or 0) - 1920))
        return {"link": files[0]["link"], "duration": hit["duration"]}
    return None


def find_clip(query: str, secs: float = 2.4) -> Path | None:
    """A trimmed, silent, 1080x1920 cut of `query`, or None."""
    query = (query or "").strip()
    if not query or not available():
        return None
    CACHE.mkdir(parents=True, exist_ok=True)
    out = _cache_path(query, secs)
    if out.exists() and out.stat().st_size > 10_000:
        return out
    try:
        hit = _search(query)
        if not hit:
            console.print(f"  [dim]footage: no good match for '{query[:40]}'[/]")
            return None
        raw = out.with_suffix(".raw.mp4")
        req = urllib.request.Request(hit["link"], headers=_UA)
        with urllib.request.urlopen(req, timeout=60) as r, raw.open("wb") as f:
            while chunk := r.read(1 << 16):
                f.write(chunk)
                if f.tell() > 60_000_000:       # runaway file: give up
                    raise ValueError("source video too large")
        # take the MIDDLE of the source (openings are often titles/logos),
        # cover-crop to 1080x1920, drop audio (the speaker keeps talking)
        ss = max(0.0, (float(hit["duration"]) - secs) / 2)
        p = subprocess.run(
            ["ffmpeg", "-y", "-ss", f"{ss:.2f}", "-t", f"{secs:.2f}",
             "-i", str(raw), "-vf",
             "scale=1080:1920:force_original_aspect_ratio=increase,"
             "crop=1080:1920,fps=25", "-an",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "21",
             "-pix_fmt", "yuv420p", str(out)],
            capture_output=True, timeout=120)
        raw.unlink(missing_ok=True)
        if p.returncode != 0 or not out.exists() or out.stat().st_size < 10_000:
            out.unlink(missing_ok=True)
            return None
        console.print(f"  [dim]footage: fetched '{query[:40]}' "
                      f"({out.stat().st_size // 1024}KB)[/]")
        return out
    except Exception as ex:  # noqa: BLE001 - an insert is never worth a crash
        console.print(f"  [dim]footage lookup failed for '{query[:30]}': "
                      f"{str(ex)[:60]}[/]")
        return None
