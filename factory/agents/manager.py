"""Agent 4 — MANAGER.

Pulls engagement metrics for uploaded clips, then asks Claude to turn the
results into concrete 'learnings' that steer the Finder next run. This is the
feedback loop that makes the other three agents improve over time.
"""
from __future__ import annotations

import json
from pathlib import Path

from rich.console import Console
from rich.table import Table

from .. import db, llm, notify, skills
from ..config import ROOT, cfg

console = Console()


def _creds():
    from google.oauth2.credentials import Credentials
    token = ROOT / "youtube_token.json"
    if not token.exists():
        return None
    return Credentials.from_authorized_user_file(str(token))


def _fetch_retention(creds, external_id: str) -> dict:
    """Watch analytics (needs the yt-analytics scope — falls back gracefully
    until the user re-runs auth-youtube). This is the Manager's most valuable
    signal: retention tells us WHERE clips lose people."""
    try:
        from googleapiclient.discovery import build
        ya = build("youtubeAnalytics", "v2", credentials=creds)
        resp = ya.reports().query(
            ids="channel==MINE", startDate="2026-01-01", endDate=db.now()[:10],
            metrics="averageViewPercentage,averageViewDuration",
            filters=f"video=={external_id}").execute()
        rows = resp.get("rows") or []
        if rows:
            return {"avg_watch_pct": float(rows[0][0]),
                    "avg_watch_sec": float(rows[0][1])}
    except Exception:  # noqa: BLE001 - missing scope / no data yet
        pass
    return {"avg_watch_pct": None, "avg_watch_sec": None}


def _fetch_youtube_metrics(external_id: str) -> dict | None:
    """Views/likes/comments via the Data API + retention via Analytics."""
    try:
        from googleapiclient.discovery import build
        creds = _creds()
        if not creds:
            return None
        yt = build("youtube", "v3", credentials=creds)
        resp = yt.videos().list(part="statistics", id=external_id).execute()
        items = resp.get("items", [])
        if not items:
            return None
        s = items[0]["statistics"]
        ret = _fetch_retention(creds, external_id)
        return {"views": int(s.get("viewCount", 0)),
                "likes": int(s.get("likeCount", 0)),
                "comments": int(s.get("commentCount", 0)),
                "shares": 0, "avg_watch_pct": ret["avg_watch_pct"]}
    except Exception:  # noqa: BLE001
        return None


def flag_attention(issue: str) -> None:
    """The Manager's direct line to the human (and to Claude): log the issue
    in attention.md, push a notification. attention.md is the file to show
    Claude when asking 'what needs improvement?'."""
    f = ROOT / "attention.md"
    stamp = db.now()[:16].replace("T", " ")
    entry = f"- **{stamp}** — {issue}\n"
    header = "# Needs attention\n\n*Written by the Manager. Show this to Claude or act on it.*\n\n"
    text = f.read_text(encoding="utf-8") if f.exists() else header
    f.write_text(text.rstrip() + "\n" + entry, encoding="utf-8")
    notify.notify("Manager: needs attention", issue)


REVIEW_TOOL_SCHEMA = {
    "type": "object",
    "properties": {
        "approved": {"type": "boolean"},
        "score": {"type": "number", "description": "0-100 quality estimate"},
        "notes": {"type": "string",
                  "description": "if not approved: 1-3 specific, actionable fixes"},
    },
    "required": ["approved", "notes"],
}


def _mechanical_checks(clip) -> list[str]:
    """Hard checks that need no AI: file, duration, loudness."""
    import subprocess
    problems = []
    path = clip["rendered_path"] or ""
    if not path or not Path(path).exists():
        return [f"rendered file missing: {path}"]
    try:
        p = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                            "format=duration", "-of", "csv=p=0", path],
                           capture_output=True, text=True)
        dur = float(p.stdout.strip() or 0)
        lo = cfg.get("finder.clip_min_seconds", 15) - 8
        hi = cfg.get("finder.clip_max_seconds", 90) + 10
        if not lo <= dur <= hi:
            problems.append(f"duration {dur:.0f}s outside sane range")
        v = subprocess.run(["ffmpeg", "-i", path, "-af", "volumedetect",
                            "-f", "null", "-"], capture_output=True, text=True)
        for line in v.stderr.splitlines():
            if "mean_volume" in line:
                mean = float(line.split("mean_volume:")[1].split("dB")[0])
                if mean < -30:
                    problems.append(f"audio too quiet ({mean:.0f} dB mean)")
    except Exception:  # noqa: BLE001 - a broken probe shouldn't crash review
        pass
    return problems


def _watch_frames(clip) -> str:
    """The Manager's EYES: pull 3 frames from the actual render and have a
    vision model describe problems (covered faces, broken framing, caption
    overflow, irrelevant b-roll). Returns its report, or '' if unavailable."""
    if not cfg.get("manager.vision_review", True):
        return ""
    import subprocess
    import tempfile
    path = clip["rendered_path"]
    dur = _probe_dur(path)
    frames = []
    for i, t in enumerate((min(1.0, dur * 0.1), dur * 0.45, dur * 0.85)):
        f = Path(tempfile.gettempdir()) / f"psf_rev_{clip['id']}_{i}.jpg"
        subprocess.run(["ffmpeg", "-y", "-ss", f"{t:.1f}", "-i", path,
                        "-frames:v", "1", "-q:v", "5", str(f)],
                       capture_output=True)
        if f.exists() and f.stat().st_size > 0:
            frames.append(f)
    if not frames:
        return ""
    report = llm.call_vision(
        "manager",
        "These are 3 frames (start/middle/end) from a vertical short we're about "
        "to publish. Report ONLY serious defects you are CERTAIN about: more "
        "than half a word of text outside the frame, captions covering a face, "
        "a person cropped in half, an obviously irrelevant/inappropriate b-roll "
        "image, black frames. Text near the frame center or slightly close to "
        "an edge is FINE — do not guess or nitpick. If nothing is definitely "
        "broken, reply exactly 'LOOKS CLEAN'. Max 3 bullets.",
        frames)
    for f in frames:
        f.unlink(missing_ok=True)
    return report.strip()


def _probe_dur(path) -> float:
    import subprocess
    p = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                        "format=duration", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True)
    try:
        return float(p.stdout.strip())
    except ValueError:
        return 30.0


def review_clip(clip) -> dict:
    """Pre-upload quality gate. Returns {approved, notes}. Mechanical checks
    first, then the Manager LOOKS at real frames (vision model), then the AI
    judges everything against editorial-standards. Fails OPEN on AI errors
    (mechanical problems always bounce)."""
    problems = _mechanical_checks(clip)
    if problems:
        return {"approved": False, "notes": "; ".join(problems)}
    if not llm.available():
        return {"approved": True, "notes": ""}
    try:
        notes_file = ROOT / "output" / f"clip_{clip['id']}.notes.md"
        edit_notes = notes_file.read_text(encoding="utf-8") if notes_file.exists() else ""
        seen = _watch_frames(clip)
        vision_block = (f"\nWhat the frames of the ACTUAL RENDER show "
                        f"(vision check):\n{seen}\n" if seen else "")
        skill_block = skills.load(cfg.get("skills.manager", []))
        prompt = (f"{skill_block}\n"
                  "You are the channel Manager doing the PRE-UPLOAD review of one short.\n"
                  f"Title: {clip['title']}\nCaption: {clip['caption']}\n"
                  f"Planned duration: {clip['end'] - clip['start']:.0f}s\n"
                  f"Editor's plan/notes:\n{edit_notes}\n{vision_block}\n"
                  "Judge against the MANAGER review checklist: does the title match "
                  "the content, is the hook strong, is the packaging honest, do the "
                  "frames show a visual defect, is anything a standards violation? "
                  "The vision check tends to over-report — treat its notes as hints "
                  "and bounce only for defects that are definite AND serious. "
                  "Mechanical checks already passed. Call submit_review. "
                  "When in doubt, approve.")
        result = llm.call_tool("manager", prompt, "submit_review",
                               REVIEW_TOOL_SCHEMA, max_tokens=500)
        if result is not None:
            return {"approved": bool(result.get("approved", True)),
                    "notes": result.get("notes", "")}
    except Exception:  # noqa: BLE001
        pass
    return {"approved": True, "notes": ""}


def collect() -> int:
    """Refresh metrics for every recorded upload."""
    with db.conn() as c:
        uploads = c.execute("SELECT * FROM uploads").fetchall()
    n = 0
    for up in uploads:
        m = None
        if up["platform"] == "youtube" and up["external_id"]:
            m = _fetch_youtube_metrics(up["external_id"])
        if not m:
            continue
        with db.conn() as c:
            c.execute(
                """INSERT INTO metrics(upload_id,views,likes,comments,shares,
                   avg_watch_pct,measured_at) VALUES(?,?,?,?,?,?,?)""",
                (up["id"], m["views"], m["likes"], m["comments"], m["shares"],
                 m["avg_watch_pct"], db.now()),
            )
        n += 1
    console.print(f"[bold blue]MANAGER[/] refreshed metrics for {n} uploads.")
    return n


def channel_ranking() -> dict[str, float]:
    """Average views per source channel (e.g. '@joerogan' → 12400.0), best data
    we have. run.py uses this to try the best-performing channel FIRST when
    picking the next source video."""
    with db.conn() as c:
        rows = c.execute("""
            SELECT s.channel, AVG(m.views) AS avg_views
            FROM metrics m
            JOIN uploads up ON up.id = m.upload_id
            JOIN clips cl ON cl.id = up.clip_id
            JOIN sources s ON s.id = cl.source_id
            WHERE m.id IN (SELECT MAX(id) FROM metrics GROUP BY upload_id)
              AND s.channel IS NOT NULL AND s.channel != ''
            GROUP BY s.channel
        """).fetchall()
    return {r["channel"]: float(r["avg_views"]) for r in rows}


def _leaderboard() -> list[dict]:
    """Latest metric per clip, joined with the clip's title/topic."""
    with db.conn() as c:
        rows = c.execute("""
            SELECT cl.id, cl.title, cl.reason, cl.score AS predicted,
                   ROUND(cl.end - cl.start, 1) AS clip_seconds,
                   s.channel AS source_channel,
                   up.platform, m.views, m.likes, m.comments, m.avg_watch_pct
            FROM metrics m
            JOIN uploads up ON up.id = m.upload_id
            JOIN clips cl ON cl.id = up.clip_id
            LEFT JOIN sources s ON s.id = cl.source_id
            WHERE m.id IN (SELECT MAX(id) FROM metrics GROUP BY upload_id)
            ORDER BY m.views DESC
        """).fetchall()
    return [dict(r) for r in rows]


def report():
    rows = _leaderboard()
    if not rows:
        console.print("[yellow]No metrics yet. Upload some clips and run "
                      "`python run.py stats` after they've aged.[/]")
        return
    t = Table(title="Clip performance")
    for col in ("id", "title", "len", "platform", "views", "likes",
                "comments", "watch%", "predicted"):
        t.add_column(col)
    for r in rows:
        watch = f"{r['avg_watch_pct']:.0f}%" if r.get("avg_watch_pct") else "—"
        t.add_row(str(r["id"]), r["title"][:36], f"{r['clip_seconds']:.0f}s",
                  r["platform"], str(r["views"]), str(r["likes"]),
                  str(r["comments"]), watch, f"{r['predicted']:.0f}")
    console.print(t)
    ranking = channel_ranking()
    if ranking:
        best = sorted(ranking.items(), key=lambda kv: -kv[1])
        console.print("[bold blue]MANAGER[/] channel performance (avg views): "
                      + ", ".join(f"{ch} {v:.0f}" for ch, v in best))
        console.print(f"  [dim]→ the picker will try {best[0][0]} first next run[/]")
    _write_learnings(rows)


def _write_learnings(rows: list[dict]):
    """The Manager's REASONING pass: analyze real performance (including
    retention when available) and issue specific directives to each teammate.
    learnings.md is injected into every agent's prompts, so this is how the
    whole team gets smarter."""
    if not llm.available():
        return
    skill_block = skills.load(cfg.get("skills.manager", []))
    prompt = (f"{skill_block}\n"
              "You are the channel Manager analyzing our shorts' REAL performance. "
              "Data per clip (clip_seconds = length; avg_watch_pct = % of the clip "
              "the average viewer watched — null means not yet available; "
              "predicted = what our Finder expected 0-100):\n\n"
              f"{json.dumps(rows, indent=2)}\n\n"
              "REASON step by step before concluding:\n"
              "1. RETENTION: where avg_watch_pct exists, compare it to clip length. "
              "Under ~50% = we lose people; look at what those clips have in common "
              "(length? topic? hook style?). Which LENGTHS hold attention best?\n"
              "2. PREDICTION ERROR: where did the Finder's `predicted` score most "
              "disagree with reality? What does that teach about our taste?\n"
              "3. SOURCE: which podcast channels/topics overperform?\n"
              "4. Small sample caution: with few clips, state hypotheses, not laws.\n\n"
              "Then write `learnings.md` (markdown, <350 words) in EXACTLY this shape:\n"
              "## For the Finder\n(2-4 directives: topics, hook styles, LENGTH guidance)\n"
              "## For the Editor\n(2-4 directives: pacing, captions, b-roll, music)\n"
              "## For the Uploader\n(1-3 directives: titles, captions, hashtags)\n"
              "## Experiment for the next clip\n(ONE concrete thing to try differently, "
              "so every day teaches us something new)\n"
              "Be specific — cite the data. No filler.")
    text = llm.call_text("manager", prompt, max_tokens=1500)
    if not text or not text.strip():
        return
    out = ROOT / cfg.get("manager.learnings_file", "learnings.md")
    out.write_text(text, encoding="utf-8")
    console.print(f"[green]✓ Updated {out.name}[/] — every agent reads this next run.")


DIGEST_TOOL = {
    "type": "object",
    "properties": {
        "digest": {"type": "string",
                   "description": "plain-language weekly report for the channel "
                                  "owner (markdown, <300 words): what we posted, "
                                  "what worked, what the experiments taught us, "
                                  "what changes next week"},
        "recommended_post_times": {
            "type": "array", "items": {"type": "string"},
            "description": "exactly 3 HH:MM times (24h, local) IF the data "
                           "clearly shows better posting hours; empty to keep "
                           "the current schedule"},
    },
    "required": ["digest"],
}


def weekly_digest():
    """Monday report: plain-language week summary to the human + (data
    permitting) a posting-schedule adjustment written to post_times.json,
    which schedule_day picks up automatically."""
    collect()
    rows = _leaderboard()
    if not rows or not llm.available():
        console.print("[yellow]No data or no LLM for a digest yet.[/]")
        return
    # publish hour per upload → lets the model reason about timing
    with db.conn() as c:
        hours = c.execute("""
            SELECT substr(created_at, 12, 5) AS hhmm, clip_id
            FROM uploads WHERE platform='youtube' ORDER BY id""").fetchall()
    prompt = ("You are the channel Manager writing the WEEKLY REPORT for the "
              "channel owner (a non-technical creator). Performance data:\n"
              f"{json.dumps(rows, indent=1)}\n\n"
              f"Publish times (UTC) per clip: {[dict(h) for h in hours]}\n"
              f"Current post schedule (local): "
              f"{cfg.get('uploader.post_times', ['09:00', '14:00', '19:00'])}\n\n"
              "Write the digest: friendly, concrete, no jargon. Only recommend "
              "new post times if ≥10 measured posts clearly support it. "
              "Call submit_digest.")
    result = llm.call_tool("manager", prompt, "submit_digest", DIGEST_TOOL,
                           max_tokens=1500)
    if not result or not result.get("digest"):
        return
    out = ROOT / "weekly_digest.md"
    out.write_text(f"# Weekly digest — {db.now()[:10]}\n\n{result['digest']}\n",
                   encoding="utf-8")
    times = result.get("recommended_post_times") or []
    import re as _re
    valid = [t for t in times if _re.fullmatch(r"([01]\d|2[0-3]):[0-5]\d", t or "")]
    if len(valid) == 3:
        (ROOT / "post_times.json").write_text(json.dumps(valid), encoding="utf-8")
        notify.notify("Schedule adjusted by Manager",
                      f"new post times: {', '.join(valid)} (based on your data)")
    notify.notify("Weekly digest ready",
                  result["digest"][:180] + "… — full report: weekly_digest.md")
    console.print(f"[green]✓ weekly digest written[/] → {out.name}")


def refresh_learnings() -> None:
    """Collect fresh metrics and re-reason the learnings brief. Called before
    each scheduled post so the team always works from today's data."""
    collect()
    rows = _leaderboard()
    if rows:
        _write_learnings(rows)
