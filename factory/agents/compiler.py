"""Agent 9 — COMPILER (the showrunner).

Weekly long-form episodes for the monetization flywheel. Takes the best moments
we've already found (across ALL source podcasts), picks ONE theme, and builds a
16:9 episode: narrator cold-open → [title card + setup VO → clip excerpt]… →
narrator verdict + a question to the comments.

Monetization-safe BY CONSTRUCTION (2026 reused-content/inauthentic rules):
- The narrator layer is a real editorial spine (thesis, per-clip setup, analysis,
  verdict) — "creative ownership", not decoration. AI narration alone doesn't
  qualify; the STRUCTURE is the originality.
- Commentary share is measured on the finished cut; below the configured floor
  (~30% of runtime triggers review) the episode is NOT uploaded — flagged instead.
- Continuous source excerpts are capped (compiler.max_excerpt, default 75s) and
  always interleaved with commentary. Credits + disclosure in the description.
- Chapters in the description (watch-time + viewer navigation).

The episode is uploaded as a normal horizontal video (never #Shorts) and
scheduled server-side like the daily posts — the PC can be off when it publishes.
"""
from __future__ import annotations

import json
import re
import subprocess
from datetime import datetime, timedelta
from pathlib import Path

from rich.console import Console

from .. import db, insights, llm, notify, skills
from ..config import ROOT, cfg
from ..utils import captions, voice

console = Console()

WORK = ROOT / "workdir"
OUT_DIR = ROOT / "output"
OUT_DIR.mkdir(exist_ok=True)

# uniform part spec so the final concat is trivial and lossless-safe
W, H, FPS = 1920, 1080, 30
AFMT = "aformat=sample_rates=44100:channel_layouts=stereo"

PLAN_SCHEMA = {
    "type": "object",
    "properties": {
        "episode_title": {"type": "string",
                          "description": "long-form title, NAME + angle/question, ≤70 chars"},
        "theme": {"type": "string", "description": "the ONE editorial thesis of the episode"},
        "description": {"type": "string",
                        "description": "2-4 sentence episode description (no hashtags)"},
        "cold_open": {"type": "string",
                      "description": "narrator cold-open script, 3-5 sentences: the thesis, "
                                     "why it matters, what's coming. Spoken, punchy."},
        "outro": {"type": "string",
                  "description": "narrator verdict, 3-5 sentences, ending on ONE "
                                 "forced-choice question for the comments"},
        "segments": {
            "type": "array", "minItems": 3, "maxItems": 8,
            "items": {"type": "object", "properties": {
                "clip_id": {"type": "integer"},
                "card_title": {"type": "string",
                               "description": "3-6 word title card for this segment"},
                "narration": {"type": "string",
                              "description": "narrator script BEFORE this clip: react to "
                                             "the previous moment, set up this one, add "
                                             "an opinion. 3-5 sentences — this carries "
                                             "the commentary share."}},
                "required": ["clip_id", "card_title", "narration"]},
        },
    },
    "required": ["episode_title", "theme", "description", "cold_open",
                 "outro", "segments"],
}


# ── pure helpers (unit-tested) ───────────────────────────────────────────────

def _wrap(text: str, width: int = 26) -> list[str]:
    """Greedy word-wrap for title cards."""
    words, lines, cur = (text or "").split(), [], ""
    for w in words:
        if cur and len(cur) + 1 + len(w) > width:
            lines.append(cur)
            cur = w
        else:
            cur = f"{cur} {w}".strip()
    if cur:
        lines.append(cur)
    return lines[:4]


def _chapters(parts: list[dict]) -> str:
    """YouTube chapter lines ('M:SS Title') from ordered parts with durations.
    Only 'chapter' parts get a line; times accumulate over everything."""
    out, t = [], 0.0
    for p in parts:
        if p.get("chapter"):
            m, s = int(t // 60), int(t % 60)
            out.append(f"{m}:{s:02d} {p['chapter']}")
        t += p["dur"]
    return "\n".join(out)


def _commentary_share(parts: list[dict]) -> float:
    """Narrator/commentary fraction of total runtime (cards are commentary)."""
    total = sum(p["dur"] for p in parts) or 1.0
    talk = sum(p["dur"] for p in parts if p["kind"] == "card")
    return talk / total


def _description(plan: dict, parts: list[dict], sources: list[str]) -> str:
    creds = ", ".join(sorted(set(sources)))
    return (f"{plan['description'].strip()}\n\n"
            f"⏱ Chapters:\n{_chapters(parts)}\n\n"
            f"🎙 Original commentary, curation and editing by ClipsMania.\n"
            f"Clips discussed from: {creds}. All rights to the original "
            f"creators.\n\n"
            f"#football #podcast #worldcup2026")


def _candidate_pool(max_rows: int = 24) -> list[dict]:
    """Moments the episode can use: any scored clip whose SOURCE video file is
    still on disk (we re-cut 16:9 from the source, not the vertical render).
    Rejected-by-quota candidates are fine — they were still good moments."""
    with db.conn() as c:
        rows = c.execute("""
            SELECT cl.id, cl.title, cl.reason, cl.score, cl.start, cl.end,
                   s.id AS source_id, s.video_path, s.channel, s.title AS ep
            FROM clips cl JOIN sources s ON s.id = cl.source_id
            WHERE cl.status != 'flagged'
            ORDER BY cl.score DESC LIMIT ?""", (max_rows,)).fetchall()
    return [dict(r) for r in rows if Path(r["video_path"] or "").exists()]


def _default_plan(pool: list[dict], n: int) -> dict:
    """LLM-free fallback: top-scoring moments, generic framing. Keeps the weekly
    episode alive even if every model call fails."""
    segs = [{"clip_id": p["id"], "card_title": p["title"][:40],
             "narration": f"Next: {p['title']}. Here's the moment."} for p in pool[:n]]
    return {"episode_title": "The Best Podcast Moments This Week",
            "theme": "this week's strongest moments",
            "description": "The strongest podcast moments of the week, curated "
                           "with commentary.",
            "cold_open": "This week the podcasts did not hold back. We've pulled "
                         "the moments worth arguing about — and by the end you'll "
                         "have a side. Stay for the last one.",
            "outro": "That's the week. Some takes will age well, some won't. "
                     "Which one was right? Tell us below.",
            "segments": segs}


# ── ffmpeg part builders ─────────────────────────────────────────────────────

def _run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=True, capture_output=True)


def _probe(path: Path) -> float:
    p = subprocess.run(
        ["ffprobe", "-v", "quiet", "-show_entries", "format=duration",
         "-of", "csv=p=0", str(path)], capture_output=True, text=True)
    return float(p.stdout.strip() or 0)


def _esc(text: str) -> str:
    """drawtext-safe (mirrors the editor: strip the characters ffmpeg's
    filtergraph parser fights over rather than out-escaping it)."""
    return (text or "").replace("\\", "").replace("'", "’") \
        .replace(":", " —").replace("%", "").replace(",", "\\,")


def _font() -> str:
    """Font path with the drive colon escaped for drawtext (Windows-safe)."""
    fp = cfg.get("editor.font_file", "C:/Windows/Fonts/arialbd.ttf") or ""
    return fp.replace("\\", "/").replace(":", "\\:")


def _card(out: Path, vo_text: str, title: str, sub: str = "") -> float:
    """Narrator card: dark slate bg, big title, brand line, narrator VO.
    Returns duration (0.0 → card failed, skip it)."""
    vo = WORK / f"{out.stem}_vo.mp3"
    vdur = voice.synth(vo_text, vo, cfg.get("compiler.voice",
                                            cfg.get("editor.voice",
                                                    "en-US-ChristopherNeural")),
                       rate="+6%")
    if vdur <= 0:
        return 0.0
    dur = vdur + 0.9
    font = _font()
    lines = _wrap(title, 24)
    n = len(lines)
    draws = []
    for i, ln in enumerate(lines):
        off = int(-(n / 2 - i) * 110 + 20)   # explicit sign — "(h/2)--55" is
        y = f"(h/2){'+' if off >= 0 else '-'}{abs(off)}"  # an ffmpeg parse error
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(ln.upper())}'"
                     f":fontsize=92:fontcolor=white:borderw=4:bordercolor=black"
                     f":x=(w-tw)/2:y={y}")
    if sub:
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(sub)}'"
                     f":fontsize=40:fontcolor=0xd0d6dd:x=(w-tw)/2:y=h-170")
    draws.append(f"drawtext=fontfile='{font}':text='CLIPSMANIA':fontsize=30"
                 f":fontcolor=0x8a93a0:x=(w-tw)/2:y=h-90")
    vf = ",".join(["fade=t=in:st=0:d=0.35",
                   f"fade=t=out:st={dur - 0.35:.2f}:d=0.35"] + draws)
    _run(["ffmpeg", "-y", "-f", "lavfi", "-i",
          f"color=c=0x11161d:size={W}x{H}:rate={FPS}:duration={dur:.2f}",
          "-i", str(vo), "-filter_complex",
          f"[0:v]{vf}[v];[1:a]{AFMT},apad[a]",
          "-map", "[v]", "-map", "[a]", "-t", f"{dur:.2f}",
          "-c:v", "libx264", "-preset", "fast", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])
    return _probe(out)


def _segment(clip: dict, out: Path, max_excerpt: float) -> float:
    """16:9 excerpt re-cut from the SOURCE video with bottom captions burned in.
    Returns duration (0.0 → failed)."""
    start, end = float(clip["start"]), float(clip["end"])
    dur = min(end - start, max_excerpt)
    if dur <= 3:
        return 0.0
    src_row = db.get_source(clip["source_id"])
    words = []
    try:
        transcript = json.loads(src_row["transcript_json"] or "[]")
        words = [w for seg in transcript for w in seg.get("words", [])]
    except Exception:  # noqa: BLE001 - captions are a bonus on long-form
        pass
    ass = None
    if words:
        style = dict(cfg.get("editor.captions", {}) or {})
        style.update({"font_size": 54, "position": "bottom",
                      "words_per_page": 5, "outline": 4})
        ass = WORK / f"{out.stem}.ass"
        captions.write_ass(ass, words, start, start + dur, style, res=(W, H))
    vf = (f"scale={W}:{H}:force_original_aspect_ratio=decrease,"
          f"pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:color=black,fps={FPS},"
          f"eq=contrast=1.05:saturation=1.15,"
          f"fade=t=in:st=0:d=0.25,fade=t=out:st={dur - 0.25:.2f}:d=0.25")
    if ass:
        vf += f",subtitles='{str(ass).replace(chr(92), '/').replace(':', chr(92) + ':')}'"
    _run(["ffmpeg", "-y", "-ss", f"{start:.2f}", "-t", f"{dur:.2f}",
          "-i", str(src_row["video_path"]),
          "-vf", vf, "-af", AFMT,
          "-c:v", "libx264", "-preset", "fast", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])
    return _probe(out)


def _concat(parts: list[Path], out: Path) -> None:
    """Join uniform parts; single loudnorm pass on the master."""
    inputs, chains = [], []
    for i, p in enumerate(parts):
        inputs += ["-i", str(p)]
        chains.append(f"[{i}:v][{i}:a]")
    fc = ("".join(chains) + f"concat=n={len(parts)}:v=1:a=1[v][pre];"
          f"[pre]loudnorm=I=-14:TP=-1.5:LRA=11[a]")
    _run(["ffmpeg", "-y", *inputs, "-filter_complex", fc,
          "-map", "[v]", "-map", "[a]",
          "-c:v", "libx264", "-preset", "medium", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])


# ── plan → episode ───────────────────────────────────────────────────────────

def _plan_episode(pool: list[dict]) -> dict | None:
    n_min = int(cfg.get("compiler.min_segments", 4))
    n_max = int(cfg.get("compiler.max_segments", 6))
    if not llm.available():
        return _default_plan(pool, n_max)
    menu = [{k: p[k] for k in ("id", "title", "reason", "score", "channel", "ep")}
            | {"secs": round(p["end"] - p["start"])} for p in pool]
    skill_block = skills.load(cfg.get("skills.compiler",
                                      ["editorial-standards", "storytelling",
                                       "youtube-monetization", "growth-strategy"]))
    prompt = f"""You are the SHOWRUNNER of a weekly podcast-clips commentary show.
Build ONE themed episode from the available moments below.

{skill_block}
What's working on our channel:
{insights.learnings()}

AVAILABLE MOMENTS (clip_id, title, why it was picked, score, source channel/episode, seconds):
{json.dumps(menu, indent=1)[:5000]}

RULES — these keep the episode monetizable (YouTube reused-content policy):
- Pick ONE sharp theme with a THESIS (a debate, a ranking, a "who's right?") —
  not "best moments". {n_min}-{n_max} segments, strongest LAST (save the best).
- Your narration IS the product: react, disagree, connect segments, add context
  the clips don't have. 3-5 full sentences per segment narration — thin one-line
  narration gets the episode DEMONETIZED. Never describe the clip; ARGUE with it.
- cold_open states the thesis and promises the payoff. outro gives YOUR verdict
  and ends on one forced-choice question.
- episode_title: NAME + angle/question, ≤70 chars, no clickbait lies.
Call submit_episode."""
    result = llm.call_tool("compiler", prompt, "submit_episode", PLAN_SCHEMA,
                           max_tokens=3000)
    if not result or not result.get("segments"):
        return _default_plan(pool, n_max)
    return result


def compile_episode(upload: bool = True) -> Path | None:
    """Build this week's episode. Returns the rendered path (None = nothing to
    do or blocked). Uploads as a scheduled long-form post unless upload=False."""
    if not cfg.get("compiler.enabled", True):
        return None
    pool = _candidate_pool()
    if len(pool) < int(cfg.get("compiler.min_segments", 4)):
        console.print("[yellow]Compiler: not enough moments with source video "
                      f"on disk ({len(pool)}) — skipping this week.[/]")
        return None
    console.print(f"[bold blue]COMPILER[/] planning an episode from "
                  f"{len(pool)} moments ({llm.describe()})…")
    plan = _plan_episode(pool)
    by_id = {p["id"]: p for p in pool}
    segments = [s for s in plan["segments"] if s.get("clip_id") in by_id]
    if len(segments) < int(cfg.get("compiler.min_segments", 4)):
        console.print("[yellow]Compiler: plan referenced unknown clips — "
                      "falling back to top-scored.[/]")
        plan = _default_plan(pool, int(cfg.get("compiler.max_segments", 6)))
        segments = plan["segments"]

    stamp = datetime.now().strftime("%Y%m%d")
    max_excerpt = float(cfg.get("compiler.max_excerpt", 75))
    parts: list[dict] = []          # {path, dur, kind, chapter}

    d = _card(WORK / f"ep{stamp}_open.mp4", plan["cold_open"],
              plan["episode_title"], "This week's clips, one argument")
    if d > 0:
        parts.append({"path": WORK / f"ep{stamp}_open.mp4", "dur": d,
                      "kind": "card", "chapter": "Intro"})

    used_sources = []
    for i, seg in enumerate(segments):
        clip = by_id[seg["clip_id"]]
        cd = _card(WORK / f"ep{stamp}_c{i}.mp4", seg["narration"],
                   seg["card_title"], f"from {clip['channel'] or 'the podcast'}")
        sd = _segment(clip, WORK / f"ep{stamp}_s{i}.mp4", max_excerpt)
        if sd <= 0:
            console.print(f"  [yellow]segment for clip {clip['id']} failed — skipped[/]")
            continue
        if cd > 0:
            parts.append({"path": WORK / f"ep{stamp}_c{i}.mp4", "dur": cd,
                          "kind": "card", "chapter": seg["card_title"][:40]})
        parts.append({"path": WORK / f"ep{stamp}_s{i}.mp4", "dur": sd,
                      "kind": "clip", "chapter": None})
        used_sources.append(clip["channel"] or clip["ep"])

    d = _card(WORK / f"ep{stamp}_out.mp4", plan["outro"], "The verdict",
              "tell us in the comments")
    if d > 0:
        parts.append({"path": WORK / f"ep{stamp}_out.mp4", "dur": d,
                      "kind": "card", "chapter": "Verdict"})

    n_clips = sum(1 for p in parts if p["kind"] == "clip")
    if n_clips < int(cfg.get("compiler.min_segments", 4)) - 1:
        console.print("[red]Compiler: too few segments rendered — aborting.[/]")
        return None

    share = _commentary_share(parts)
    total = sum(p["dur"] for p in parts)
    floor = float(cfg.get("compiler.min_commentary_share", 0.30))
    console.print(f"  episode: {total / 60:.1f} min, {n_clips} clips, "
                  f"commentary share {share:.0%} (floor {floor:.0%})")

    out = OUT_DIR / f"episode_{stamp}.mp4"
    _concat([p["path"] for p in parts], out)
    desc = _description(plan, parts, used_sources)
    (OUT_DIR / f"episode_{stamp}.notes.md").write_text(
        f"# {plan['episode_title']}\n\ntheme: {plan['theme']}\n"
        f"commentary share: {share:.0%}\n\n{desc}\n", encoding="utf-8")

    if share < floor:
        # policy risk — never upload a compilation below the commentary floor
        notify.notify("Episode held back (policy risk)",
                      f"commentary share {share:.0%} < {floor:.0%} floor — "
                      f"rendered to {out.name} but NOT uploaded")
        console.print(f"[red]⚠ commentary share {share:.0%} below the "
                      f"{floor:.0%} floor — episode rendered but NOT uploaded.[/]")
        return out

    if upload:
        res = _upload_longform(out, plan["episode_title"], desc)
        if res:
            with db.conn() as c:
                c.execute("""CREATE TABLE IF NOT EXISTS episodes (
                    id INTEGER PRIMARY KEY, title TEXT, theme TEXT,
                    video_id TEXT, url TEXT, path TEXT, created_at TEXT)""")
                c.execute("INSERT INTO episodes(title,theme,video_id,url,path,created_at) "
                          "VALUES(?,?,?,?,?,?)",
                          (plan["episode_title"], plan["theme"],
                           res["external_id"], res["url"], str(out), db.now()))
            notify.notify("Weekly episode scheduled",
                          f"{plan['episode_title']} → {res['when']}", res["url"])
            console.print(f"[green]✓ episode scheduled for {res['when']}[/] "
                          f"→ {res['url']}")
    return out


def _next_publish_slot() -> datetime:
    """Next occurrence of compiler.publish_day/time (local, aware)."""
    days = {"mon": 0, "tue": 1, "wed": 2, "thu": 3, "fri": 4, "sat": 5, "sun": 6}
    want = days.get(str(cfg.get("compiler.publish_day", "sun")).lower()[:3], 6)
    hh, mm = (int(x) for x in str(cfg.get("compiler.publish_time", "15:00")).split(":"))
    now = datetime.now().astimezone()
    t = now.replace(hour=hh, minute=mm, second=0, microsecond=0)
    ahead = (want - now.weekday()) % 7
    if ahead == 0 and t <= now + timedelta(minutes=30):
        ahead = 7
    return t + timedelta(days=ahead)


def _upload_longform(path: Path, title: str, description: str) -> dict | None:
    """Scheduled long-form upload (16:9, no #Shorts). Mirrors the uploader's
    server-side publishAt flow."""
    try:
        from googleapiclient.discovery import build
        from googleapiclient.http import MediaFileUpload

        from . import uploader
        creds = uploader._youtube_credentials()
        yt = build("youtube", "v3", credentials=creds)
        when = _next_publish_slot()
        from datetime import timezone
        body = {"snippet": {"title": uploader._safe_title(title)[:95],
                            "description": description[:4900],
                            "tags": ["podcast", "football", "world cup 2026",
                                     "clips", "commentary"],
                            "categoryId": str(cfg.get("compiler.category_id", "24"))},
                "status": {"privacyStatus": "private",
                           "publishAt": when.astimezone(timezone.utc)
                           .strftime("%Y-%m-%dT%H:%M:%SZ"),
                           "selfDeclaredMadeForKids": False}}
        media = MediaFileUpload(str(path), chunksize=-1, resumable=True)
        resp = yt.videos().insert(part="snippet,status", body=body,
                                  media_body=media).execute()
        vid = resp["id"]
        return {"external_id": vid, "url": f"https://youtu.be/{vid}",
                "when": when.strftime("%a %H:%M")}
    except Exception as ex:  # noqa: BLE001
        console.print(f"[red]long-form upload failed: {ex}[/]")
        notify.notify("Episode upload FAILED",
                      f"{title[:80]}: {str(ex)[:120]} — file kept in output/")
        return None
