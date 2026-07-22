"""B-roll stills for the editor — free stock photos, no account required.

The planner suggests b-roll moments ("gym weights close-up @ 12s"); the editor
calls fetch() to turn each suggestion into a real image, then overlays it with
a fade. Two providers, tried in order:

1. Pexels — if a free PEXELS_API_KEY is in .env (https://www.pexels.com/api/).
2. Openverse — NO key needed. Searched with license=cc0 only (public-domain
   dedication), so every image is safe to use in monetized videos with no
   attribution requirement.

Downloads are cached by query so repeat themes (gym, money, food…) cost zero
API calls. Any failure returns None and the clip renders without b-roll.
"""
from __future__ import annotations

import hashlib
import json
import urllib.parse
import urllib.request
from pathlib import Path

from ..config import ROOT, cfg

CACHE = ROOT / "assets" / "broll_cache"
_UA = {"User-Agent": "PodcastShortsFactory/1.0"}


def api_key() -> str:
    return (cfg.env("PEXELS_API_KEY", "") or "").strip()


def available() -> bool:
    """B-roll works out of the box now (Openverse needs no key)."""
    return True


def _get_json(url: str, headers: dict, timeout: int) -> dict:
    req = urllib.request.Request(url, headers={**_UA, **headers})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return json.loads(resp.read().decode())


def _download(img_url: str, dest: Path, timeout: int) -> Path | None:
    with urllib.request.urlopen(
            urllib.request.Request(img_url, headers=_UA), timeout=timeout) as img:
        data = img.read()
    if not data or not data[:3] in (b"\xff\xd8\xff", b"\x89PN"):   # jpg/png only
        return None
    dest.write_bytes(data)
    return dest


def _from_pexels(query: str, dest: Path, timeout: int) -> Path | None:
    q = urllib.parse.urlencode(
        {"query": query, "per_page": 3, "orientation": "portrait"})
    photos = _get_json(f"https://api.pexels.com/v1/search?{q}",
                       {"Authorization": api_key()}, timeout).get("photos", [])
    if not photos:
        return None
    src = photos[0]["src"]
    return _download(src.get("large2x") or src.get("large") or src["original"],
                     dest, timeout)


def _relevant(result: dict, query: str) -> bool:
    """CC0 search ranks the weirdest things (a medieval manuscript for 'blood
    test', a zombie for 'blood'). Tags are too loose — a Halloween photo is
    tagged 'blood' — so only trust the TITLE, which on stock sites literally
    names the subject ('Male Doctor'). A wrong picture is worse than none."""
    words = {w for w in query.lower().split() if len(w) > 2}
    title = (result.get("title") or "").lower()
    return any(w in title for w in words)


def _from_openverse(query: str, dest: Path, timeout: int) -> Path | None:
    # Source priority: stocksnap (pro CC0 stock photography) → rawpixel (stock-
    # grade but sometimes watermarked) → anywhere. Random general-pool CC0
    # (Flickr snapshots, museum scans) often looks amateur or absurd on screen.
    for extra in ({"source": "stocksnap"}, {"source": "rawpixel"}, {}):
        q = urllib.parse.urlencode({
            "q": query, "license": "cc0", "page_size": 8,
            "category": "photograph",       # real photos, not clipart/vectors
            "aspect_ratio": "tall,square", **extra})
        results = _get_json(f"https://api.openverse.org/v1/images/?{q}",
                            {}, timeout).get("results", [])
        for r in results:
            url = r.get("url") or ""
            if not _relevant(r, query):
                continue
            if url.lower().rsplit(".", 1)[-1] in ("jpg", "jpeg", "png"):
                try:
                    if _download(url, dest, timeout):
                        return dest
                except Exception:  # noqa: BLE001 - dead link → try next result
                    continue
    return None


def fetch_video(query: str, max_seconds: int = 15, timeout: int = 40) -> Path | None:
    """Portrait stock VIDEO clip for `query` (Pexels videos API — needs the
    key). Cached. Motion b-roll reads far more premium than a still photo."""
    query = " ".join((query or "").split())[:80]
    if not query or not api_key():
        return None
    CACHE.mkdir(parents=True, exist_ok=True)
    cached = CACHE / (hashlib.md5(("v:" + query.lower()).encode())
                      .hexdigest()[:16] + ".mp4")
    if cached.exists() and cached.stat().st_size > 0:
        return cached
    for q in _variants(query):
        try:
            qs = urllib.parse.urlencode({"query": q, "per_page": 5,
                                         "orientation": "portrait"})
            data = _get_json(f"https://api.pexels.com/videos/search?{qs}",
                             {"Authorization": api_key()}, timeout)
            for vid in data.get("videos", []):
                if vid.get("duration", 999) > max_seconds * 4:
                    continue                      # avoid huge downloads
                files = sorted((f for f in vid.get("video_files", [])
                                if f.get("height") and 600 <= f["height"] <= 1400
                                and (f.get("file_type") or "").endswith("mp4")),
                               key=lambda f: f["height"])
                if not files:
                    continue
                with urllib.request.urlopen(
                        urllib.request.Request(files[-1]["link"], headers=_UA),
                        timeout=timeout) as r:
                    cached.write_bytes(r.read())
                if cached.stat().st_size > 20_000:
                    return cached
        except Exception:  # noqa: BLE001 - fall through to next variant
            continue
    return None


def _variants(query: str) -> list[str]:
    """Planner suggestions can be too specific for CC0 search ('woman lifting
    weights gym' → 0 hits). Retry with progressively shorter queries."""
    words = query.split()
    out = [query]
    for n in (3, 2, 1):
        if len(words) > n:
            out.append(" ".join(words[:n]))
    return out


def fetch(query: str, timeout: int = 20) -> Path | None:
    """Stock photo for `query`. Cached. None on any failure."""
    query = " ".join((query or "").split())[:80]
    if not query:
        return None
    CACHE.mkdir(parents=True, exist_ok=True)
    cached = CACHE / (hashlib.md5(query.lower().encode()).hexdigest()[:16] + ".jpg")
    if cached.exists() and cached.stat().st_size > 0:
        return cached
    for q in _variants(query):
        for provider in ((_from_pexels,) if api_key() else ()) + (_from_openverse,):
            try:
                if provider(q, cached, timeout):
                    return cached
            except Exception:  # noqa: BLE001 - b-roll is a bonus, never break renders
                continue
    return None
