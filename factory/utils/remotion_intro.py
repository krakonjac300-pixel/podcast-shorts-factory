"""Render an animated kinetic-typography intro hook card via Remotion.

The Remotion project lives in ROOT/remotion (Node + a bundled Chromium). This
produces a ~2.3s transparent ProRes clip of the hook text flying in word-by-word
— the premium opener ffmpeg can't do. The editor overlays it on the first
seconds of the clip. Fully optional (config editor.intro_card): if Remotion or
Node isn't available, this returns None and the clip renders with the normal
ffmpeg drawtext hook instead — nothing breaks.
"""
from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from ..config import ROOT, WORK

REMOTION_DIR = ROOT / "remotion"


def available() -> bool:
    return (REMOTION_DIR / "node_modules" / ".bin").exists()


def render_intro(text: str, accent_words: list[str], out_path: Path,
                 font_size: int = 118, timeout: int = 180) -> Path | None:
    """Render the IntroHook composition to a transparent .mov. None on failure."""
    text = " ".join((text or "").split())[:60]
    if not text or not available():
        return None
    binary = REMOTION_DIR / "node_modules" / ".bin" / (
        "remotion.cmd" if os.name == "nt" else "remotion")
    props = {"text": text, "fontSize": int(font_size),
             "accentWords": [w.upper() for w in (accent_words or [])]}
    props_file = WORK / "intro_props.json"
    props_file.write_text(json.dumps(props), encoding="utf-8")
    try:
        subprocess.run(
            [str(binary), "render", "IntroHook", str(out_path),
             "--codec=prores", "--prores-profile=4444",
             # BOTH needed for a real alpha channel — profile alone renders
             # opaque yuv422 (the hook card would sit on a black screen).
             "--pixel-format=yuva444p10le", "--image-format=png",
             f"--props={props_file}"],
            cwd=str(REMOTION_DIR), check=True, capture_output=True,
            timeout=timeout)
        return out_path if out_path.exists() and out_path.stat().st_size > 0 else None
    except Exception:  # noqa: BLE001 - intro card is a bonus, never break a render
        return None
