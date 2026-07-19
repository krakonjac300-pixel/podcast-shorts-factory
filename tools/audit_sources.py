"""Report what every configured source URL ACTUALLY is.

The 2026-07-19 incident was not a clever failure. `scheduler.sources` held a URL
commented "# Rio Ferdinand" that actually pointed at a Hindi vlog channel, and
nothing ever checked the comment against reality. The pipeline then spent an
hour downloading and transcribing it before the topic gate looked at the output.

This resolves each source and prints the real channel name, country and recent
video titles, so a mislabelled entry is obvious in seconds instead of after a
wasted produce run. Run it after ANY change to scheduler.sources, and before a
niche flip installs a new set.

Read-only: it never downloads a video or edits config.
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

from factory.config import cfg  # noqa: E402


def resolve(url: str) -> dict:
    """Channel metadata for a source URL, without downloading anything."""
    import yt_dlp
    opts = {"quiet": True, "no_warnings": True, "extract_flat": True,
            "playlist_items": "1-5", "skip_download": True}
    try:
        with yt_dlp.YoutubeDL(opts) as ydl:
            info = ydl.extract_info(url, download=False)
    except Exception as ex:  # noqa: BLE001
        return {"error": str(ex)[:120]}
    entries = [e for e in (info.get("entries") or []) if e]
    return {
        "channel": info.get("channel") or info.get("uploader") or info.get("title", ""),
        "id": info.get("channel_id") or info.get("uploader_id") or "",
        "titles": [(e.get("title") or "")[:64] for e in entries[:3]],
    }


def main(argv: list[str]) -> int:
    urls = argv[1:] or (cfg.get("scheduler.sources") or [])
    if not urls:
        print("no sources configured")
        return 1

    print(f"auditing {len(urls)} source(s)\n")
    bad = 0
    for u in urls:
        info = resolve(u)
        print(f"  {u}")
        if info.get("error"):
            print(f"      UNRESOLVABLE: {info['error']}")
            bad += 1
            print()
            continue
        print(f"      channel : {info['channel']}")
        for t in info["titles"]:
            print(f"      recent  : {t}")
        print()

    if bad:
        print(f"{bad} source(s) could not be resolved — fix before the next run")
    else:
        print("all sources resolved. CHECK THE CHANNEL NAMES AND TITLES ABOVE "
              "match what you intend: this tool proves what a URL IS, it cannot "
              "know what you MEANT.")
    return 1 if bad else 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv))
