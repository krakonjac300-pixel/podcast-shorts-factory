"""Free neural voiceover via Microsoft Edge TTS (edge-tts package, no API key).

Used for the narrator cold-open ("Wait for what he says about your kidneys…")
that plays over the teaser montage before the clip starts.
"""
from __future__ import annotations

import asyncio
import subprocess
from pathlib import Path


def synth(text: str, out_path: Path, voice: str = "en-US-ChristopherNeural",
          rate: str = "+12%") -> float:
    """Synthesize `text` to mp3. Returns duration in seconds (0.0 on failure —
    voiceover is a bonus, never fatal)."""
    text = " ".join((text or "").split())
    if not text:
        return 0.0
    try:
        import edge_tts

        async def _run():
            await edge_tts.Communicate(text, voice, rate=rate).save(str(out_path))

        asyncio.run(_run())
        p = subprocess.run(
            ["ffprobe", "-v", "quiet", "-show_entries", "format=duration",
             "-of", "csv=p=0", str(out_path)], capture_output=True, text=True)
        return float(p.stdout.strip() or 0)
    except Exception:  # noqa: BLE001 - offline / service hiccup → no narrator
        return 0.0
