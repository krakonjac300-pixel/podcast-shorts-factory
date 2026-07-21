"""Unlist a specific video, but ONLY once its replacement is actually live.

Used to retire a superseded cut without leaving a gap. On 2026-07-21 the
food-delivery clip went out with the sentence cut in half at both ends; the
recut publishes at 21:30 and the broken one should disappear at that point, not
before, or the channel briefly has neither.

Deliberately conservative:
  * refuses to unlist unless the replacement is PUBLIC (not merely scheduled),
    so a failed or delayed publish leaves the original up rather than taking the
    clip off the channel entirely
  * verifies the change by re-reading the video afterwards, because the YouTube
    API accepts writes it silently ignores (the channel title and description
    both behave that way)
  * idempotent

Usage: python tools/unlist_video.py <video_to_unlist> <replacement_video>
"""
from __future__ import annotations

import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))


def unlist(target_id: str, replacement_id: str) -> int:
    from googleapiclient.discovery import build
    from factory.agents import uploader
    from factory import notify

    yt = build("youtube", "v3", credentials=uploader._youtube_credentials())

    got = yt.videos().list(part="status,snippet", id=replacement_id).execute()
    if not got.get("items"):
        print(f"replacement {replacement_id} not found — leaving the original up")
        return 1
    rep = got["items"][0]["status"]["privacyStatus"]
    if rep != "public":
        msg = (f"replacement {replacement_id} is '{rep}', not public yet — "
               f"NOT unlisting {target_id}, the channel would be left with "
               f"neither version")
        print(msg)
        notify.notify("Unlist skipped", msg)
        return 1

    cur = yt.videos().list(part="status,snippet", id=target_id).execute()
    if not cur.get("items"):
        print(f"{target_id} not found")
        return 1
    st = cur["items"][0]["status"]
    title = cur["items"][0]["snippet"]["title"][:60]
    if st["privacyStatus"] == "unlisted":
        print(f"{target_id} already unlisted — nothing to do")
        return 0

    yt.videos().update(part="status", body={
        "id": target_id,
        "status": {"privacyStatus": "unlisted",
                   "selfDeclaredMadeForKids": st.get("selfDeclaredMadeForKids",
                                                     False)}}).execute()

    # verify: the API accepts writes it then ignores
    after = yt.videos().list(part="status", id=target_id).execute()
    now = after["items"][0]["status"]["privacyStatus"]
    if now != "unlisted":
        msg = f"tried to unlist {target_id} but it is still '{now}'"
        print(msg)
        notify.notify("Unlist FAILED", msg)
        return 1

    msg = f"unlisted the superseded cut: {title}"
    print(msg)
    notify.notify("Superseded video unlisted",
                  f"{msg}. The recut ({replacement_id}) is live in its place.")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 3:
        print(__doc__)
        raise SystemExit(2)
    raise SystemExit(unlist(sys.argv[1], sys.argv[2]))
