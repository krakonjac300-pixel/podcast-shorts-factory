"""Agent 6 — COMMUNITY.

Comments are the #1 ranking signal on Shorts (2026). This agent:
1. Posts a PINNED-style seed comment with a debate question on new uploads.
2. Replies to viewer comments fast, on-brand (funny, curious, asks back).

Replying/posting needs the youtube.force-ssl scope — until the user re-consents,
replies are DRAFTED into comment_drafts.md and flagged, never lost.
"""
from __future__ import annotations

import json

from rich.console import Console

from .. import db, insights, llm, skills
from ..config import ROOT, cfg

console = Console()
DRAFTS = ROOT / "comment_drafts.md"

REPLY_TOOL = {
    "type": "object",
    "properties": {
        "replies": {
            "type": "array",
            "items": {"type": "object", "properties": {
                "comment_id": {"type": "string"},
                "reply": {"type": "string",
                          "description": "≤200 chars, human, on-brand: witty or "
                                         "curious, never corporate; ask back when "
                                         "natural to spark a thread"},
                "skip": {"type": "boolean",
                         "description": "true for spam/hate/nothing to add"}},
                "required": ["comment_id", "reply"]},
        },
        "seed_comment": {
            "type": "string",
            "description": "for videos with no seed yet: ONE debate question "
                           "≤120 chars that makes people pick a side",
        },
    },
    "required": ["replies"],
}


def _yt():
    from googleapiclient.discovery import build
    from factory.agents.uploader import _youtube_credentials
    return build("youtube", "v3", credentials=_youtube_credentials())


def _recent_videos(yt, limit: int = 6) -> list[dict]:
    with db.conn() as c:
        rows = c.execute(
            """SELECT up.external_id, cl.title FROM uploads up
               JOIN clips cl ON cl.id = up.clip_id
               WHERE up.platform='youtube' AND up.external_id IS NOT NULL
               ORDER BY up.id DESC LIMIT ?""", (limit * 3,)).fetchall()
    vids = [dict(r) for r in rows]
    if not vids:
        return []
    # Only PUBLIC videos can take comments — the newest uploads are often still
    # private (scheduled premieres), and commenting on those 403s and looks like
    # a permission problem. Filter to what's actually live.
    try:
        ids = [v["external_id"] for v in vids]
        public = set()
        for i in range(0, len(ids), 50):
            resp = yt.videos().list(part="status",
                                    id=",".join(ids[i:i + 50])).execute()
            for it in resp.get("items", []):
                if it["status"].get("privacyStatus") == "public":
                    public.add(it["id"])
        vids = [v for v in vids if v["external_id"] in public]
    except Exception:  # noqa: BLE001 - if the status check fails, fall through
        pass
    return vids[:limit]


def _fetch_comments(yt, video_id: str, limit: int = 20) -> list[dict]:
    try:
        resp = yt.commentThreads().list(
            part="snippet", videoId=video_id, maxResults=limit,
            order="time", textFormat="plainText").execute()
    except Exception:  # noqa: BLE001 - comments disabled / none yet
        return []
    out = []
    for item in resp.get("items", []):
        top = item["snippet"]["topLevelComment"]
        s = top["snippet"]
        out.append({"comment_id": top["id"],
                    "author": s.get("authorDisplayName", ""),
                    "text": s.get("textDisplay", "")[:300],
                    "replies": item["snippet"].get("totalReplyCount", 0)})
    return out


def _already_answered(comment_id: str) -> bool:
    with db.conn() as c:
        c.execute("""CREATE TABLE IF NOT EXISTS comment_log(
                     id INTEGER PRIMARY KEY, comment_id TEXT UNIQUE,
                     video_id TEXT, kind TEXT, text TEXT, created_at TEXT)""")
        return c.execute("SELECT 1 FROM comment_log WHERE comment_id=?",
                         (comment_id,)).fetchone() is not None


def _log(comment_id: str, video_id: str, kind: str, text: str):
    with db.conn() as c:
        c.execute("""INSERT OR IGNORE INTO comment_log
                     (comment_id, video_id, kind, text, created_at)
                     VALUES(?,?,?,?,?)""",
                  (comment_id, video_id, kind, text, db.now()))


def _post_reply(yt, comment_id: str, text: str) -> bool:
    yt.comments().insert(part="snippet", body={
        "snippet": {"parentId": comment_id, "textOriginal": text}}).execute()
    return True


def _post_seed(yt, video_id: str, text: str) -> bool:
    yt.commentThreads().insert(part="snippet", body={
        "snippet": {"videoId": video_id, "topLevelComment": {
            "snippet": {"textOriginal": text}}}}).execute()
    return True


def _draft(video_id: str, kind: str, target: str, text: str):
    """No write scope yet → save the draft so nothing is lost."""
    header = "# Comment drafts (need youtube.force-ssl scope to auto-post)\n\n"
    body = DRAFTS.read_text(encoding="utf-8") if DRAFTS.exists() else header
    body += f"- [{kind}] video {video_id} → {target}: {text}\n"
    DRAFTS.write_text(body, encoding="utf-8")


def engage(max_videos: int = 5) -> int:
    """One engagement pass over recent uploads. Returns actions taken."""
    if not llm.available():
        console.print("[yellow]COMMUNITY: no LLM available, skipping.[/]")
        return 0
    yt = _yt()
    skill_block = skills.load(cfg.get("skills.community",
                                      cfg.get("skills.uploader", [])))
    actions = 0
    can_post = True
    for vid in _recent_videos(yt, max_videos):
        comments = [c for c in _fetch_comments(yt, vid["external_id"])
                    if not _already_answered(c["comment_id"])]
        seeded = _already_answered(f"seed:{vid['external_id']}")
        if not comments and seeded:
            continue
        prompt = (f"{skill_block}\n"
                  f"You run the comments for our shorts channel. Video: "
                  f"\"{vid['title']}\"\n"
                  f"What's worked before:\n{insights.learnings()}\n\n"
                  f"New viewer comments (JSON):\n{json.dumps(comments, indent=1)}\n\n"
                  + ("Also write ONE seed_comment: a debate question for this "
                     "video that makes people pick a side.\n" if not seeded else "")
                  + "Reply to the comments worth replying to (skip spam). "
                    "Sound human. Call submit_replies.")
        result = llm.call_tool("community", prompt, "submit_replies",
                               REPLY_TOOL, max_tokens=1200)
        if not result:
            continue
        for r in result.get("replies", []):
            if r.get("skip") or not r.get("reply") or not r.get("comment_id"):
                continue
            try:
                if can_post:
                    _post_reply(yt, r["comment_id"], r["reply"])
                    console.print(f"  [green]replied:[/] {r['reply'][:60]}")
                else:
                    _draft(vid["external_id"], "reply", r["comment_id"], r["reply"])
            except Exception as ex:  # noqa: BLE001
                if "insufficient" in str(ex).lower() or "403" in str(ex):
                    can_post = False
                    _draft(vid["external_id"], "reply", r["comment_id"], r["reply"])
                else:
                    continue
            _log(r["comment_id"], vid["external_id"], "reply", r["reply"])
            actions += 1
        seed = (result.get("seed_comment") or "").strip()
        if seed and not seeded:
            try:
                if can_post:
                    _post_seed(yt, vid["external_id"], seed)
                    console.print(f"  [green]seeded:[/] {seed[:60]}")
                else:
                    _draft(vid["external_id"], "seed", vid["external_id"], seed)
            except Exception as ex:  # noqa: BLE001
                if "insufficient" in str(ex).lower() or "403" in str(ex):
                    can_post = False
                    _draft(vid["external_id"], "seed", vid["external_id"], seed)
            _log(f"seed:{vid['external_id']}", vid["external_id"], "seed", seed)
            actions += 1
    if not can_post and actions:
        from . import manager
        manager.flag_attention(
            f"COMMUNITY drafted {actions} comment(s) but can't post — needs the "
            "comments permission. Ask Claude for an auth link (adds youtube.force-ssl).")
    console.print(f"[bold cyan]COMMUNITY[/] {actions} engagement action(s).")
    return actions
