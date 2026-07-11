"""Fetch the montage emoji-sticker pack: Google Noto emoji PNGs (512px,
Apache-2.0 — free for commercial use, no attribution required) into
assets/emoji/<name>.png. Idempotent; run once."""
from __future__ import annotations

import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "assets" / "emoji"
BASE = ("https://raw.githubusercontent.com/googlefonts/noto-emoji/"
        "main/png/512/emoji_u{code}.png")

# name → unicode codepoint (the planner picks by name)
PACK = {
    "laughing": "1f602", "heart": "2764", "fire": "1f525",
    "shocked": "1f631", "mindblown": "1f92f", "crying": "1f622",
    "goat": "1f410", "clap": "1f44f", "eyes": "1f440", "muscle": "1f4aa",
    "sad": "2639", "hundred": "1f4af",
}


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    ok = 0
    for name, code in PACK.items():
        dest = OUT / f"{name}.png"
        if dest.exists() and dest.stat().st_size > 1000:
            ok += 1
            continue
        try:
            urllib.request.urlretrieve(BASE.format(code=code), dest)
            print(f"  {name}.png ok")
            ok += 1
        except Exception as ex:  # noqa: BLE001
            print(f"  {name}: FAILED ({ex})")
    print(f"{ok}/{len(PACK)} emoji stickers in {OUT}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
