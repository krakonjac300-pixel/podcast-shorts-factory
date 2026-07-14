"""Branded design scenes for the editor — AI-designed visuals, no key required.

The planner's scene_map marks the crucial lines of a clip and gives each one a
design brief ("person stepping out of a line, word EXTRAORDINARY above"). This
module turns a brief into a finished motion overlay in two phases, mirroring
how a pro edit is made (and keeping the expensive step gated on the cheap one):

1. DESIGN  — generate a branded still via the free Pollinations image API
             (no account, no key). The brand style/palette from config.yaml is
             appended to every prompt so all scenes share one visual identity.
             The still is sanity-checked (real image, big enough) before we
             spend any encode time on it — mock-up first, motion after.
2. ANIMATE — a slow constant zoom-in (ffmpeg zoompan) turns the approved still
             into a ~3.2s motion clip. Constant gentle camera movement is the
             motion language that reads "premium" in short-form.

Stills are cached by (brief + brand) hash so a re-render costs zero API calls.
Any failure returns None and the editor falls back to stock b-roll.
"""
from __future__ import annotations

import hashlib
import re
import subprocess
import urllib.parse
import urllib.request
from pathlib import Path

from ..config import ROOT, cfg

CACHE = ROOT / "assets" / "design_cache"
_UA = {"User-Agent": "PodcastShortsFactory/1.0"}

# Pollinations serves plain GET image generation — free, anonymous, CC0 output.
_API = "https://image.pollinations.ai/prompt/"


def available() -> bool:
    """Design scenes work out of the box (Pollinations needs no key)."""
    return bool(cfg.get("editor.design_scenes", True))


def _brand_suffix() -> str:
    """One consistent look per channel: style descriptor + palette from config."""
    style = cfg.get("editor.brand_style",
                    "clean minimal flat illustration, bold shapes, high contrast")
    colors = cfg.get("editor.brand_colors", [])
    palette = f", color palette {' and '.join(colors)}" if colors else ""
    # image models garble lettering — text is burned separately with drawtext
    return (f"{style}{palette}, vertical composition, "
            f"no text, no words, no lettering, no watermark, no logo")


def _design_still(brief: str, dest: Path, timeout: int = 90) -> Path | None:
    """Phase 1 — generate + sanity-check the branded still (the cheap mock-up)."""
    if dest.exists() and dest.stat().st_size > 20_000:
        return dest                                   # cache hit
    prompt = urllib.parse.quote(f"{brief}. {_brand_suffix()}")
    seed = int(hashlib.sha1(brief.encode()).hexdigest()[:6], 16)  # reproducible
    url = f"{_API}{prompt}?width=1080&height=1246&nologo=true&seed={seed}"
    try:
        req = urllib.request.Request(url, headers=_UA)
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            data = resp.read()
    except Exception:  # noqa: BLE001 - a missing design must never kill a render
        return None
    # mock-up gate: only a real, non-trivial image earns the animation encode
    if len(data) < 20_000 or data[:3] not in (b"\xff\xd8\xff", b"\x89PN"):
        return None
    dest.write_bytes(data)
    return dest


def _text_overlay(text: str, w: int, h: int) -> str:
    """drawtext filter for the 1-4 word label: crisp, bold, fading in top-center.
    The image model can't render lettering, so the text is burned here instead."""
    clean = re.sub(r"[^A-Za-z0-9 !?.-]", "", text).strip().upper()[:28]
    if not clean:
        return ""
    font = str(cfg.get("editor.font_file", "C:/Windows/Fonts/arialbd.ttf"))
    font = font.replace("\\", "/").replace(":", "\\:")
    size = max(48, int(w / 11))
    return (f",drawtext=fontfile='{font}':text='{clean}':fontsize={size}:"
            f"fontcolor=white:borderw={max(3, size // 14)}:bordercolor=black:"
            f"x=(w-text_w)/2:y=h*0.10:"
            f"alpha='min(1\\,max(0\\,(t-0.35)/0.4))'")


def _animate(still: Path, out: Path, w: int, h: int, text: str = "",
             dur: float = 3.4, zoom: float = 0.12) -> Path | None:
    """Phase 2 — slow constant zoom-in on the approved still (dynamic-camera feel).
    Upscale 2x before zoompan to avoid the classic sub-pixel jitter."""
    frames = int(dur * 30)
    vf = (f"scale={w * 2}:-2,"
          f"zoompan=z='1+{zoom}*on/{frames}':d={frames}:"
          f"x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':s={w}x{h}:fps=30"
          + _text_overlay(text, w, h))
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-hide_banner", "-loop", "1", "-t", f"{dur:.1f}",
             "-i", str(still), "-vf", vf, "-t", f"{dur:.1f}",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
             "-pix_fmt", "yuv420p", "-an", str(out)],
            check=True, capture_output=True, timeout=180)
        return out if out.exists() and out.stat().st_size > 10_000 else None
    except Exception:  # noqa: BLE001
        out.unlink(missing_ok=True)
        return None


def fetch(brief: str, w: int, h: int, text: str = "") -> Path | None:
    """Design brief → branded, animated ~3.2s overlay clip (or None to fall back).
    `w`x`h` is the overlay band size the editor composites (both must be even);
    `text` is the planner's 1-4 word label, burned crisply over the design."""
    if not brief or not brief.strip():
        return None
    CACHE.mkdir(parents=True, exist_ok=True)
    key = hashlib.sha1(f"{brief}|{text}|{_brand_suffix()}".encode()).hexdigest()[:16]
    still = CACHE / f"{key}.jpg"
    clip = CACHE / f"{key}_{w}x{h}.mp4"
    if clip.exists() and clip.stat().st_size > 10_000:
        return clip                                   # animated cache hit
    if not _design_still(brief, still):
        return None
    return _animate(still, clip, w, h, text)
