"""One-off competitor teardown: find podcast-clip channels like ours, measure
what the successful ones actually do (niche, titles, length, cadence), and dump
the raw evidence for the team meeting. Quota-frugal: channel searches (100u each)
are capped, everything else uses 1-unit endpoints."""
from __future__ import annotations

import json
import re
import sys
from datetime import datetime, timezone
from pathlib import Path
from statistics import median

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from factory.agents import manager  # noqa: E402

from googleapiclient.discovery import build  # noqa: E402

QUERIES = ["stick to football clips", "podcast clips shorts",
           "football podcast clips", "diary of a ceo clips"]

ISO_DUR = re.compile(r"PT(?:(\d+)M)?(?:(\d+)S)?")


def _secs(iso: str) -> int:
    m = ISO_DUR.fullmatch(iso or "")
    if not m:
        return 0
    return int(m.group(1) or 0) * 60 + int(m.group(2) or 0)


def main() -> None:
    creds = manager._creds()
    if not creds:
        print("NO CREDS"); return
    yt = build("youtube", "v3", credentials=creds)

    # 1. find candidate clip channels
    chan_ids: dict[str, str] = {}
    for q in QUERIES:
        r = yt.search().list(q=q, part="snippet", type="channel",
                             maxResults=6).execute()
        for it in r.get("items", []):
            chan_ids[it["snippet"]["channelId"]] = it["snippet"]["title"]

    # 2. stats for each; keep plausible CLIP channels (not the podcasts themselves)
    ids = list(chan_ids)
    chans = []
    for i in range(0, len(ids), 50):
        r = yt.channels().list(part="snippet,statistics,contentDetails",
                               id=",".join(ids[i:i + 50])).execute()
        for it in r.get("items", []):
            st = it["statistics"]
            subs = int(st.get("subscriberCount", 0))
            vids = int(st.get("videoCount", 0)) or 1
            views = int(st.get("viewCount", 0))
            chans.append({
                "id": it["id"], "title": it["snippet"]["title"],
                "created": it["snippet"]["publishedAt"][:10],
                "subs": subs, "videos": vids, "views": views,
                "views_per_video": views // vids,
                "uploads_pl": it["contentDetails"]["relatedPlaylists"]["uploads"],
            })
    chans.sort(key=lambda c: -c["views_per_video"])

    # 3. deep-dive the most efficient clip channels (views/video is the tell)
    deep = []
    for ch in chans[:6]:
        try:
            pl = yt.playlistItems().list(part="contentDetails",
                                         playlistId=ch["uploads_pl"],
                                         maxResults=50).execute()
            vids = [x["contentDetails"]["videoId"] for x in pl.get("items", [])]
            dates = [x["contentDetails"].get("videoPublishedAt", "")
                     for x in pl.get("items", [])]
            v = yt.videos().list(part="snippet,statistics,contentDetails",
                                 id=",".join(vids[:50])).execute()
            rows = []
            for it in v.get("items", []):
                rows.append({
                    "title": it["snippet"]["title"],
                    "published": it["snippet"]["publishedAt"][:10],
                    "views": int(it.get("statistics", {}).get("viewCount", 0)),
                    "secs": _secs(it["contentDetails"]["duration"]),
                })
            rows.sort(key=lambda r: -r["views"])
            # cadence: uploads in the last 30 days
            now = datetime.now(timezone.utc)
            recent = [d for d in dates if d and
                      (now - datetime.fromisoformat(d.replace("Z", "+00:00"))).days <= 30]
            shorts = [r for r in rows if 0 < r["secs"] <= 61]
            longs = [r for r in rows if r["secs"] > 61]
            deep.append({
                **{k: ch[k] for k in ("title", "subs", "videos", "views",
                                      "views_per_video", "created")},
                "uploads_last_30d": len(recent),
                "n_shorts_sampled": len(shorts), "n_longs_sampled": len(longs),
                "median_short_secs": median([r["secs"] for r in shorts]) if shorts else None,
                "median_short_views": median([r["views"] for r in shorts]) if shorts else None,
                "top10_recent": rows[:10],
            })
        except Exception as ex:  # noqa: BLE001
            deep.append({"title": ch["title"], "error": str(ex)[:120]})

    out = {"generated": datetime.now().isoformat(timespec="seconds"),
           "all_candidates": chans, "deep_dive": deep}
    Path(__file__).with_name("competitors_raw.json").write_text(
        json.dumps(out, indent=1), encoding="utf-8")
    print(f"candidates: {len(chans)}, deep-dived: {len(deep)}")
    for d in deep:
        if "error" in d:
            print(f"  ! {d['title']}: {d['error']}"); continue
        print(f"  {d['title'][:34]:34} subs={d['subs']:>9,} v/vid={d['views_per_video']:>9,} "
              f"30d={d['uploads_last_30d']:>3} medShort={d['median_short_secs']}s")


if __name__ == "__main__":
    main()
