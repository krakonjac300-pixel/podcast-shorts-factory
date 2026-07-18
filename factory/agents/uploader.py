"""Agent 3 — UPLOADER.

YouTube Shorts is fully implemented (official Data API v3). TikTok and
Instagram require approved API access; until then the uploader exports the
finished file + metadata so you can post via a scheduler. Nothing is posted
without your confirmation.
"""
from __future__ import annotations

import json
from pathlib import Path

from rich.console import Console
from rich.prompt import Confirm

from .. import db, insights, llm, notify, skills
from ..config import ROOT, cfg

console = Console()
EXPORT_DIR = ROOT / "ready_to_post"
EXPORT_DIR.mkdir(exist_ok=True)


def _hashtags(clip) -> list[str]:
    base = cfg.get("uploader.hashtags", [])
    try:
        return list(dict.fromkeys(base + json.loads(clip["hashtags"] or "[]")))
    except json.JSONDecodeError:
        return base


def post_copy(clip, platform: str) -> dict:
    """Title/caption/hashtags for a platform. Uses the uploader's copywriting +
    hashtag skills to tailor per platform when ai_optimize is on; otherwise
    falls back to what the Finder wrote. Never raises."""
    fallback = {"title": clip["title"], "caption": clip["caption"] or "",
                "hashtags": _hashtags(clip)}
    if not cfg.get("uploader.ai_optimize", True) or not llm.available():
        return fallback
    try:
        schema = {"type": "object", "properties": {
            "title": {"type": "string"},
            "caption": {"type": "string"},
            "hashtags": {"type": "array", "items": {"type": "string"}}},
            "required": ["title", "caption", "hashtags"]}
        skill_block = skills.load(cfg.get("skills.uploader", []))
        prompt = (f"{skill_block}\nWrite post copy for **{platform}** for this clip.\n"
                  f"Title idea: {clip['title']}\nCaption idea: {clip['caption']}\n"
                  f"What's worked on our channel so far:\n{insights.learnings()}\n"
                  "TITLE FORMULA (measured on 1M+ view clips in our niche): a "
                  "RECOGNIZABLE NAME + an emotional verb ('Keane SLAMS…', 'X humbled "
                  "Y') OR a pure question ('What's the biggest World Cup upset "
                  "ever?'). Front-load the name/claim in the FIRST 40 characters, "
                  "keep ≤60 chars, at most ONE emoji. Questions double as comment "
                  "bait. Tailor tone and hashtags to the platform. "
                  "End caption with ONE forced-choice question. Call submit_copy.")
        result = llm.call_tool("uploader", prompt, "submit_copy", schema, max_tokens=600)
        if result:
            out = dict(fallback)
            out.update({k: v for k, v in result.items() if v})
            return out
    except Exception:  # noqa: BLE001
        pass
    return fallback


YOUTUBE_SCOPES = ["https://www.googleapis.com/auth/youtube.upload",
                  "https://www.googleapis.com/auth/youtube.readonly",
                  # retention analytics for the Manager's learning loop
                  "https://www.googleapis.com/auth/yt-analytics.readonly",
                  # comment replies for the Community agent
                  "https://www.googleapis.com/auth/youtube.force-ssl"]


# ── YouTube ───────────────────────────────────────────────────────
def _youtube_credentials():
    """Load cached token or run the OAuth consent flow. Returns creds."""
    from google_auth_oauthlib.flow import InstalledAppFlow
    from google.auth.transport.requests import Request
    from google.oauth2.credentials import Credentials

    token_path = ROOT / "youtube_token.json"
    secrets = cfg.env("YOUTUBE_CLIENT_SECRETS", "client_secret.json")

    creds = None
    if token_path.exists():
        # Load with the token's OWN scopes — forcing YOUTUBE_SCOPES here makes
        # the refresh demand scopes the stored token never granted
        # (invalid_scope, broke posting on 2026-07-04). Upgraded scopes are
        # only requested in the explicit consent flow below.
        creds = Credentials.from_authorized_user_file(str(token_path))
    if not creds or not creds.valid:
        if creds and creds.expired and creds.refresh_token:
            creds.refresh(Request())
        else:
            if not Path(secrets).exists():
                raise FileNotFoundError(
                    f"YouTube OAuth client file not found: '{secrets}'. See "
                    "docs/youtube-setup.md, then set YOUTUBE_CLIENT_SECRETS in .env.")
            flow = InstalledAppFlow.from_client_secrets_file(secrets, YOUTUBE_SCOPES)
            creds = flow.run_local_server(port=0)
        token_path.write_text(creds.to_json())
    return creds


def authenticate() -> bool:
    """Run/verify YouTube auth and print the connected channel. Returns success."""
    try:
        from googleapiclient.discovery import build
        creds = _youtube_credentials()
        yt = build("youtube", "v3", credentials=creds)
        resp = yt.channels().list(part="snippet", mine=True).execute()
        items = resp.get("items", [])
        if items:
            name = items[0]["snippet"]["title"]
            console.print(f"[green]✓ YouTube connected as:[/] [bold]{name}[/]")
            console.print("  token cached → youtube_token.json")
            return True
        console.print("[yellow]Authenticated, but no channel found on this account.[/]")
        return True
    except Exception as ex:  # noqa: BLE001
        console.print(f"[red]YouTube auth failed:[/] {ex}")
        return False


def _series_number() -> int:
    """Next episode number in the running series (1-based, counts real uploads)."""
    start = int(cfg.get("series.number_from", 1))
    with db.conn() as c:
        n = c.execute("SELECT COUNT(*) FROM uploads WHERE platform='youtube'"
                      ).fetchone()[0]
    return start + int(n)


def _series_title(title: str) -> str:
    """Prefix the title with the recurring series name and episode number.

    48,888 views converted 6 subscribers because a one-off clip gives nobody a
    reason to come back. A NAMED, NUMBERED series does: it turns 'a video I
    watched' into 'a thing I follow', and the number itself implies there are
    more. Skipped if the title already carries the series name.
    """
    if not cfg.get("series.enabled", False):
        return title
    name = (cfg.get("series.name", "") or "").strip()
    if not name or name.lower() in title.lower():
        return title
    return f"{name} #{_series_number()}: {title}"


def _safe_title(title: str) -> str:
    """YouTube rejects '<' and '>' in titles (invalidTitle 400). Turn the
    common comparison shorthand into words and strip any stray brackets."""
    t = (title or "").replace(" > ", " OVER ").replace(" < ", " UNDER ")
    t = t.replace(">", "").replace("<", "")
    return " ".join(t.split()).strip() or "Watch This"


def upload_youtube(clip, publish_at=None) -> dict | None:
    """Upload now, or — with `publish_at` (aware datetime) — hand YouTube a
    scheduled premiere: the video sits private and YouTube flips it public at
    that exact time SERVER-SIDE, no PC required."""
    from googleapiclient.discovery import build
    from googleapiclient.http import MediaFileUpload

    creds = _youtube_credentials()
    yt = build("youtube", "v3", credentials=creds)
    copy = post_copy(clip, "youtube")
    tags = [h.lstrip("#") for h in copy["hashtags"]]
    title = _series_title(_safe_title(copy["title"]))[:95] + " #Shorts"
    status = {"privacyStatus": cfg.get("uploader.privacy", "private"),
              "selfDeclaredMadeForKids": False}
    if publish_at is not None:
        from datetime import timezone
        status["privacyStatus"] = "private"        # required with publishAt
        status["publishAt"] = publish_at.astimezone(
            timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    body = {
        "snippet": {"title": title,
                    "description": copy["caption"] + "\n\n" + " ".join(copy["hashtags"]),
                    "tags": tags, "categoryId": "22"},
        "status": status,
    }
    media = MediaFileUpload(clip["rendered_path"], chunksize=-1, resumable=True)
    req = yt.videos().insert(part="snippet,status", body=body, media_body=media)
    resp = req.execute()
    vid = resp["id"]
    return {"external_id": vid, "url": f"https://youtube.com/shorts/{vid}"}


# ── TikTok / Instagram (export-for-scheduler fallback) ────────────
def export_for_scheduler(clip, platform: str) -> dict:
    """Copy the rendered file + write a caption sidecar for manual/scheduled post."""
    import shutil
    src = Path(clip["rendered_path"])
    dest = EXPORT_DIR / f"{platform}_clip_{clip['id']}{src.suffix}"
    shutil.copy(src, dest)
    copy = post_copy(clip, platform)
    meta = {
        "platform": platform,
        "title": copy["title"],
        "caption": copy["caption"] + "\n" + " ".join(copy["hashtags"]),
        "hashtags": copy["hashtags"],
    }
    (dest.with_suffix(".json")).write_text(json.dumps(meta, indent=2))
    return {"external_id": None, "url": str(dest)}


def _upload_clip(clip, platforms, assume_yes: bool) -> int:
    """Post one rendered clip to each platform; mark it uploaded. Returns count."""
    console.print(f"\n[bold]Clip {clip['id']}[/]: {clip['title']}")
    console.print(f"  file: {clip['rendered_path']}")
    posted = 0
    for platform in platforms:
        if not assume_yes and not Confirm.ask(f"  Post to [cyan]{platform}[/]?",
                                              default=False):
            continue
        try:
            if platform == "youtube":
                res = upload_youtube(clip)
            else:
                res = export_for_scheduler(clip, platform)
                console.print(f"  [yellow]{platform}: exported for scheduler "
                              f"→ {res['url']}[/] (direct API not configured)")
            if res:
                db.record_upload(clip["id"], platform, res["external_id"], res["url"])
                posted += 1
                console.print(f"  [green]✓ {platform}: {res['url']}[/]")
                if platform == "youtube":
                    notify.notify("Posted to YouTube", clip["title"], res["url"])
        except Exception as ex:  # noqa: BLE001 - surface any upload error
            console.print(f"  [red]{platform} failed: {ex}[/]")
            notify.notify("Post FAILED", f"{platform}: {clip['title']} — {ex}")
    # only leave the queue if the MAIN platform actually took it — a failed
    # YouTube upload must stay queued for the next slot, not vanish silently
    yt_ok = "youtube" not in platforms or db.uploaded_to(clip["id"], "youtube")
    db.set_clip_status(clip["id"], "uploaded" if posted and yt_ok else "edited")
    if not (posted and yt_ok):
        notify.notify("Clip kept in queue",
                      f"clip {clip['id']} didn't reach YouTube — will retry next slot")
    return posted


def _taken_slots() -> set:
    """Future publishAt datetimes already scheduled on YouTube, so we never
    double-book a slot (e.g. the 6AM produce vs an already-scheduled day)."""
    out = set()
    try:
        from datetime import datetime, timezone
        from googleapiclient.discovery import build
        yt = build("youtube", "v3", credentials=_youtube_credentials())
        r = yt.search().list(part="id", forMine=True, type="video",
                             maxResults=25, order="date").execute()
        ids = [i["id"]["videoId"] for i in r.get("items", [])]
        if ids:
            v = yt.videos().list(part="status", id=",".join(ids)).execute()
            now = datetime.now(timezone.utc)
            for it in v.get("items", []):
                pub = it["status"].get("publishAt")
                if it["status"].get("privacyStatus") == "private" and pub:
                    dt = datetime.fromisoformat(pub.replace("Z", "+00:00"))
                    if dt > now:
                        out.add(dt.astimezone())
    except Exception:  # noqa: BLE001 - if we can't check, fall back to naive slots
        pass
    return out


def _next_slots(n: int, times: list[str], now=None, taken=None) -> list:
    """The next `n` posting datetimes from the daily schedule (local time),
    skipping slots already past (20min upload margin) AND slots already taken
    by a scheduled post (±30min), rolling into following days as needed."""
    from datetime import datetime, timedelta
    now = now or datetime.now().astimezone()
    taken = taken or set()
    slots, day = [], 0
    while len(slots) < n and day < 21:
        for hhmm in times:
            hh, mm = (int(x) for x in hhmm.split(":"))
            t = (now + timedelta(days=day)).replace(hour=hh, minute=mm,
                                                    second=0, microsecond=0)
            if t <= now + timedelta(minutes=20) or len(slots) >= n:
                continue
            if any(abs((t - tk).total_seconds()) < 1800 for tk in taken):
                continue                          # slot already has a post
            slots.append(t)
        day += 1
    return slots


def schedule_day(assume_yes: bool = True) -> int:
    """Upload every reviewed clip in the queue as a YouTube SCHEDULED post
    (9AM/2PM/7PM slots). YouTube publishes them server-side — the PC can be
    off all day and the posts still go out. This is the permanent fix for
    'the morning post failed because the PC was off/broken'."""
    edited = db.clips_by_status("edited")
    if not edited:
        console.print("[yellow]Nothing to schedule.[/]")
        return 0
    times = cfg.get("uploader.post_times", ["09:00", "14:00", "19:00"])
    override = ROOT / "post_times.json"       # Manager's data-driven schedule
    if override.exists():
        try:
            t = json.loads(override.read_text(encoding="utf-8"))
            if isinstance(t, list) and len(t) == 3:
                times = t
                console.print(f"  [dim]post times from Manager: {', '.join(t)}[/]")
        except (json.JSONDecodeError, OSError):
            pass
    slots = _next_slots(len(edited), times, taken=_taken_slots())
    platforms = cfg.get("uploader.platforms", ["youtube"])
    scheduled = 0
    for clip, when in zip(edited, slots):
        # re-check right before upload: the safety-net post task may have posted
        # this clip while we were processing earlier ones (the Mahabharata
        # double-post race of 2026-07-05) — never upload the same clip twice
        fresh = db.clip_by_id(clip["id"])
        if fresh["status"] != "edited" or db.uploaded_to(clip["id"], "youtube"):
            console.print(f"  [dim]clip {clip['id']} already handled — skipping[/]")
            continue
        ok = _review_and_fix(clip)
        if not ok:
            continue
        try:
            res = upload_youtube(ok, publish_at=when)
            db.record_upload(ok["id"], "youtube", res["external_id"], res["url"])
            for p in platforms:
                if p != "youtube":
                    r = export_for_scheduler(ok, p)
                    db.record_upload(ok["id"], p, r["external_id"], r["url"])
            db.set_clip_status(ok["id"], "uploaded")
            scheduled += 1
            stamp = when.strftime("%a %H:%M")
            console.print(f"[green]✓ scheduled clip {ok['id']} for {stamp}[/] "
                          f"→ {res['url']}")
            notify.notify(f"Scheduled for {stamp}", ok["title"], res["url"])
        except Exception as ex:  # noqa: BLE001
            console.print(f"[red]scheduling clip {clip['id']} failed: {ex}[/]")
            notify.notify("Scheduling FAILED",
                          f"clip {clip['id']}: {ex} — will retry at post time")
    console.print(f"[green]✓ {scheduled} post(s) locked in — YouTube publishes "
                  f"them even if this PC is off.[/]")
    return scheduled


def _review_and_fix(clip):
    """Manager's pre-upload gate. Approved → clip to post. Bounced → the Editor
    re-edits WITH the Manager's notes, then one re-review. Twice-failed clips
    are rejected and escalated to the human. Returns a postable clip or None."""
    from . import editor, manager                     # local: avoid import cycle
    if not cfg.get("manager.review_before_post", True):
        return clip
    review = manager.review_clip(clip)
    if review["approved"]:
        console.print(f"  [dim]MANAGER review: clip {clip['id']} approved[/]")
        return clip
    console.print(f"[yellow]MANAGER bounced clip {clip['id']}:[/] {review['notes']}")
    db.set_review(clip["id"], review["notes"])
    if (clip["review_attempts"] or 0) >= 1:
        db.set_clip_status(clip["id"], "rejected")
        manager.flag_attention(
            f"Clip {clip['id']} '{clip['title'][:50]}' rejected after a re-edit "
            f"also failed review: {review['notes']}")
        return None
    notify.notify("Manager bounced a clip",
                  f"clip {clip['id']}: {review['notes'][:140]} — re-editing now")
    try:
        fresh = db.clip_by_id(clip["id"])             # carries the notes
        out = editor.edit_clip(fresh)                 # planner reads the notes
        db.set_clip_status(clip["id"], "edited", rendered_path=str(out))
        fresh = db.clip_by_id(clip["id"])
        second = manager.review_clip(fresh)
        if second["approved"]:
            console.print(f"[green]MANAGER approved the re-edit of clip {clip['id']}[/]")
            return fresh
        db.set_review(clip["id"], second["notes"])
        db.set_clip_status(clip["id"], "rejected")
        manager.flag_attention(
            f"Clip {clip['id']} rejected twice. First: {review['notes']} "
            f"Then: {second['notes']}")
    except Exception as ex:  # noqa: BLE001
        manager.flag_attention(f"Re-edit of clip {clip['id']} crashed: {ex}")
    return None


def upload_all(assume_yes: bool = False) -> int:
    platforms = cfg.get("uploader.platforms", ["youtube"])
    edited = db.clips_by_status("edited")
    if not edited:
        console.print("[yellow]No edited clips ready. Run edit first.[/]")
        return 0
    posted = 0
    for c in edited:
        ok = _review_and_fix(c)
        if ok:
            posted += _upload_clip(ok, platforms, assume_yes)
    console.print(f"\n[green]✓ {posted} uploads/exports done.[/]")
    return posted


def build_post_package(transcript_excerpt: str,
                       platforms: list[str] | None = None,
                       niche: str | None = None) -> dict:
    """Stateless post-copy generation for headless/programmatic callers.

    Given a transcript excerpt, return platform-optimized title/caption/hashtags
    for each requested platform, plus a recommended posting time. Depends on no
    channel history or DB — safe to call in a hosted service. Never posts.
    """
    platforms = platforms or ["youtube", "tiktok", "instagram"]
    skill_block = skills.load(cfg.get("skills.uploader", []))
    schema = {"type": "object", "properties": {
        "title": {"type": "string"},
        "caption": {"type": "string"},
        "hashtags": {"type": "array", "items": {"type": "string"}}},
        "required": ["title", "caption", "hashtags"]}

    out: dict[str, dict] = {}
    for platform in platforms:
        fallback = {"title": "", "caption": transcript_excerpt[:150], "hashtags": []}
        prompt = (
            f"{skill_block}\nWrite short-form post copy for **{platform}** based on "
            f"this clip transcript excerpt.\nNiche: {niche or 'general'}\n\n"
            f"Transcript excerpt:\n\"\"\"\n{transcript_excerpt[:4000]}\n\"\"\"\n\n"
            "RULES: put the concrete hook/number in the FIRST 40 characters of the "
            "title. Tailor tone and hashtags to the platform. End the caption with ONE "
            "forced-choice question to drive comments. Call submit_copy.")
        # Free models occasionally return an empty tool call; a paid service must
        # not deliver an empty title, so retry once before falling back.
        result, err = None, None
        for _ in range(2):
            try:
                result = llm.call_tool("uploader", prompt, "submit_copy", schema,
                                       max_tokens=600)
                if result and result.get("title"):
                    break
            except Exception as ex:  # noqa: BLE001 - one platform failing shouldn't fail all
                err = str(ex)
        merged = {**fallback, **{k: v for k, v in (result or {}).items() if v}}
        if err and not merged.get("title"):
            merged["error"] = err
        out[platform] = merged

    return {
        "platforms": out,
        "recommended_post_time": "peak audience window; test 9AM / 2PM / 7PM local",
        "note": "This service never posts. Publish with your own platform credentials.",
    }


def upload_one(assume_yes: bool = True) -> bool:
    """Post the single best-scoring queued clip that passes the Manager's
    review. For staggered posting (e.g. 3x/day)."""
    edited = db.clips_by_status("edited")   # ordered by score desc → best first
    if not edited:
        console.print("[yellow]Post queue empty — nothing to post right now.[/]")
        return False
    platforms = cfg.get("uploader.platforms", ["youtube"])
    for clip in edited:
        if db.uploaded_to(clip["id"], "youtube"):    # already live/scheduled —
            db.set_clip_status(clip["id"], "uploaded")  # never double-post
            continue
        ok = _review_and_fix(clip)
        if ok:
            _upload_clip(ok, platforms, assume_yes)
            remaining = len(db.clips_by_status("edited"))
            console.print(f"[green]✓ posted 1 clip.[/] {remaining} left in the queue.")
            return True
    console.print("[yellow]No clip in the queue passed the Manager's review.[/]")
    return False
