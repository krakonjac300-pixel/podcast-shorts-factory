"""SFX pack v2 — the viral-canon sounds (2026-07-17 research).

Evidence: the 1.35M-view money exemplar runs a hot master with a bass-heavy bed
and few, WEIGHTY sounds; the 2026 shorts canon = deep cinematic booms (the vine-
boom family), whoosh-HITS (whoosh that lands on a thump), notification-style
dings, and — for money content — cash register / coins. Our v1 pack (thin
generic mixkit picks) gets replaced in-place: same semantic stems, so the whole
pipeline (planner enum, _SFX_SYNONYMS, mixer) picks the new sounds with zero
code changes; plus two new money stems: cash, coin.

Mixkit license: free for commercial use, no attribution.
"""
from __future__ import annotations

import re
import subprocess
import sys
import urllib.request
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SFX = ROOT / "assets" / "sfx"

# role -> (mixkit category, keywords to prefer in the title, how many variants)
WANT = {
    "impact": ("boom", ["cinematic", "deep", "bass", "hit", "boom"], 2),
    "whoosh": ("hit", ["whoosh", "swoosh", "transition"], 2),
    "swoosh": ("swoosh", ["fast", "swoosh", "whoosh"], 2),
    "ding":   ("notification", ["positive", "bell", "correct", "achievement"], 2),
    "riser":  ("cinematic", ["riser", "rise", "transition", "suspense", "tension"], 2),
    "pop":    ("notification", ["pop", "click", "bubble", "soft"], 2),
    "cash":   ("money", ["cash register", "cash", "register", "money"], 2),
    "coin":   ("coin", ["coin", "coins", "clink", "drop"], 2),
}

UA = {"User-Agent": "Mozilla/5.0"}


def scrape(cat: str) -> list[tuple[str, str]]:
    """[(title, mp3_url)] for one category page."""
    url = f"https://mixkit.co/free-sound-effects/{cat}/"
    html = urllib.request.urlopen(
        urllib.request.Request(url, headers=UA), timeout=20).read().decode("utf-8", "ignore")
    out = []
    for m in re.finditer(r'data-audio-player-preview-url-value="([^"]+)"', html):
        chunk = html[m.start():m.start() + 1500]
        t = re.search(r'title="([^"]+)"|alt="([^"]+)"', chunk)
        title = (t.group(1) or t.group(2)) if t else ""
        out.append((title.lower(), m.group(1)))
    return out


def main() -> int:
    SFX.mkdir(parents=True, exist_ok=True)
    got = 0
    for role, (cat, prefer, n) in WANT.items():
        try:
            items = scrape(cat)
        except Exception as ex:  # noqa: BLE001
            print(f"{role}: category scrape failed ({ex})")
            continue
        # rank: titles matching preference keywords first
        items.sort(key=lambda it: -sum(k in it[0] for k in prefer))
        picked = 0
        for title, url in items:
            if picked >= n:
                break
            dest = SFX / (f"{role}.wav" if picked == 0 else f"{role}-{picked + 1}.wav")
            try:
                tmp = SFX / f"_{role}.mp3"
                urllib.request.urlretrieve(url, tmp)
                # normalize: mono-compat stereo wav, trimmed to 3s max, tail fade
                subprocess.run(
                    ["ffmpeg", "-y", "-i", str(tmp), "-t", "3",
                     "-af", "afade=t=out:st=2.6:d=0.4",
                     "-ar", "44100", "-ac", "2", str(dest)],
                    check=True, capture_output=True)
                tmp.unlink(missing_ok=True)
                picked += 1
                got += 1
                print(f"  {dest.name:14} <- {title[:60]}")
            except Exception as ex:  # noqa: BLE001
                print(f"  {role}: dl failed ({str(ex)[:50]})")
        if not picked:
            print(f"  {role}: NOTHING matched — keeping existing file")
    print(f"{got} sounds installed into {SFX}")
    return 0 if got else 1


if __name__ == "__main__":
    sys.exit(main())
