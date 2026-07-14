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
                      "description": "narrator cold-open, ≤45 words. MUST make ONE concrete "
                                     "PROMISE of what the viewer gets by the end ('by #1 "
                                     "you'll hear the prediction that could end a career — "
                                     "and whether he's right') and tease the finale."},
        "outro": {"type": "string",
                  "description": "narrator verdict, ≤55 words: DELIVER the cold-open promise, "
                                 "then OVERDELIVER with one bonus receipt/stat/take beyond it, "
                                 "then ONE forced-choice question for the comments"},
        "music_mood": {"type": "string",
                       "description": "music bed mood: tense | upbeat | lofi | ambient"},
        "thumbnail_text": {"type": "string",
                           "description": "2-4 ALL-CAPS words for the thumbnail, e.g. "
                                          "'THE LAST HONEST MAN'"},
        "segments": {
            "type": "array", "minItems": 3, "maxItems": 8,
            "items": {"type": "object", "properties": {
                "clip_id": {"type": "integer"},
                "card_title": {"type": "string",
                               "description": "3-6 word title card for this segment (no "
                                              "numbering — we add the countdown #)"},
                "narration": {"type": "string",
                              "description": "narrator script BEFORE this clip: 1-2 PUNCHY "
                                             "spoken sentences, ≤20 words. React to the "
                                             "previous moment, set up this one with a take. "
                                             "Never describe — argue."}},
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
            "cold_open": "This week the podcasts did not hold back. By the end "
                         "you'll hear the one take everyone will argue about — "
                         "and our verdict on it. Stay for number one.",
            "outro": "That's the take we promised — and the verdict: it holds "
                     "up better than anyone admits. Which side are you on? "
                     "Tell us below.",
            "music_mood": "tense", "thumbnail_text": "THE VERDICT",
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


def _grab_frame(video_path: str, at: float, out: Path) -> Path | None:
    """Freeze-frame from a source video — the animated card background."""
    try:
        _run(["ffmpeg", "-y", "-ss", f"{max(0.0, at):.2f}", "-i", str(video_path),
              "-frames:v", "1", "-q:v", "3", str(out)])
        return out if out.exists() else None
    except Exception:  # noqa: BLE001 - cards fall back to the flat-color look
        return None


def _sfx_file(kind: str) -> Path | None:
    hits = sorted((ROOT / cfg.get("editor.sfx_dir", "assets/sfx")).glob(f"{kind}*.wav"))
    return hits[0] if hits else None


def _burn_hook(path: Path, text: str, dur: float) -> None:
    """Overlay the episode hook as big text over the cold-open footage, so the
    open is raw drama + a promise — never a static talking slate (the drop-zone)."""
    out = path.with_name(path.stem + "_hk.mp4")
    font = _font()
    lines = _wrap(text.upper(), 20)[:2]
    draws = []
    for i, ln in enumerate(lines):
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(ln)}':fontsize=76"
                     f":fontcolor=white:borderw=6:bordercolor=black"
                     f":box=1:boxcolor=black@0.35:boxborderw=14"
                     f":x=(w-tw)/2:y={110 + i * 96}")
    try:
        _run(["ffmpeg", "-y", "-i", str(path), "-vf", ",".join(draws),
              "-c:a", "copy", "-c:v", "libx264", "-preset", "fast", "-crf", "20",
              str(out)])
        _replace(out, path)
    except Exception:  # noqa: BLE001 - hook text is a bonus, keep the raw cold open
        out.unlink(missing_ok=True)


def _card(out: Path, vo_text: str, title: str, sub: str = "",
          bg_frame: Path | None = None) -> float:
    """Narrator card v2 — built to NOT be boring: blurred slow-zooming footage
    of the upcoming clip as the background (flat slate only as fallback), title
    lines that slide-fade in one after another, a whoosh on entry, and the VO on
    top. Returns duration (0.0 → card failed, skip it)."""
    vo = WORK / f"{out.stem}_vo.mp3"
    vdur = voice.synth(vo_text, vo, cfg.get("compiler.voice",
                                            cfg.get("editor.voice",
                                                    "en-US-ChristopherNeural")),
                       rate="+13%")           # snappier VO = shorter cards = less bleed
    if vdur <= 0:
        return 0.0
    dur = vdur + 0.9
    font = _font()
    lines = _wrap(title, 24)
    n = len(lines)
    draws = []
    for i, ln in enumerate(lines):
        off = int(-(n / 2 - i) * 110 + 20)   # explicit sign — "(h/2)--55" is
        base = f"(h/2){'+' if off >= 0 else '-'}{abs(off)}"  # a parse error
        t0 = 0.18 + i * 0.22                 # staggered reveal, line by line
        alpha = f"min(1\\,max(0\\,(t-{t0:.2f})*4))"
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(ln.upper())}'"
                     f":fontsize=92:fontcolor=white:borderw=4:bordercolor=black"
                     f":alpha='{alpha}'"
                     f":x=(w-tw)/2:y='{base}+30*(1-{alpha})'")
    if sub:
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(sub)}'"
                     f":fontsize=40:fontcolor=0xd0d6dd"
                     f":alpha='min(1\\,max(0\\,(t-{0.18 + n * 0.22:.2f})*4))'"
                     f":x=(w-tw)/2:y=h-170")
    draws.append(f"drawtext=fontfile='{font}':text='CLIPSMANIA':fontsize=30"
                 f":fontcolor=0x8a93a0:x=(w-tw)/2:y=h-90")

    inputs = []
    if bg_frame and bg_frame.exists():
        # footage still: blur hard, darken, slow push-in — feels alive, teases
        # the clip behind the words
        inputs += ["-loop", "1", "-t", f"{dur:.2f}", "-i", str(bg_frame)]
        zoom = f"0.10*min(t/{dur:.2f}\\,1)"
        bg = (f"[0:v]scale={W}:{H}:force_original_aspect_ratio=increase,"
              f"crop={W}:{H},gblur=sigma=22,eq=brightness=-0.20:saturation=0.85,"
              f"crop=w='iw-iw*({zoom})':h='ih-ih*({zoom})':x='(iw-ow)/2':y='(ih-oh)/2',"
              f"scale={W}:{H},setsar=1,fps={FPS}")
    else:
        inputs += ["-f", "lavfi", "-i",
                   f"color=c=0x11161d:size={W}x{H}:rate={FPS}:duration={dur:.2f}"]
        bg = "[0:v]null"
    inputs += ["-i", str(vo)]

    whoosh = _sfx_file("whoosh") or _sfx_file("swoosh")
    amix = f"[1:a]{AFMT},apad[a]"
    if whoosh:
        inputs += ["-i", str(whoosh)]
        amix = (f"[1:a]{AFMT}[vo];[2:a]adelay=60|60,volume=0.30,{AFMT}[wh];"
                f"[vo][wh]amix=inputs=2:normalize=0:duration=first,apad[a]")

    vf = ",".join([bg, "fade=t=in:st=0:d=0.30",
                   f"fade=t=out:st={dur - 0.35:.2f}:d=0.35"] + draws)
    _run(["ffmpeg", "-y", *inputs, "-filter_complex",
          f"{vf}[v];{amix}",
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
    from . import editor                          # reuse the proven Ken Burns
    vf = (f"scale={W}:{H}:force_original_aspect_ratio=decrease,"
          f"pad={W}:{H}:(ow-iw)/2:(oh-ih)/2:color=black,fps={FPS},"
          f"{editor._motion(W, H, dur, 0.06)},"    # gentle push so the frame
          f"eq=contrast=1.05:saturation=1.15,"     # never sits dead still
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
    inputs, norm, chains = [], [], []
    for i, p in enumerate(parts):
        inputs += ["-i", str(p)]
        # image-sourced cards can carry an off-by-a-hair SAR (e.g. 1353:1352)
        # and concat refuses mismatched inputs — normalize every part first
        norm.append(f"[{i}:v]setsar=1[v{i}]")
        chains.append(f"[v{i}][{i}:a]")
    fc = (";".join(norm) + ";" + "".join(chains)
          + f"concat=n={len(parts)}:v=1:a=1[v][pre];"
          f"[pre]loudnorm=I=-14:TP=-1.5:LRA=11[a]")
    _run(["ffmpeg", "-y", *inputs, "-filter_complex", fc,
          "-map", "[v]", "-map", "[a]",
          "-c:v", "libx264", "-preset", "medium", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])


def _music_bed(master: Path, mood: str) -> None:
    """Continuous music bed under the WHOLE episode, sidechain-ducked so it
    dives under every spoken word and swells in the gaps — the single biggest
    'produced' feel for one cheap pass (video stream is copied, not re-encoded)."""
    from . import editor
    music = editor._pick_music(mood or "tense")
    if not music or not cfg.get("compiler.music", True):
        return
    tmp = master.with_name(master.stem + "_mx.mp4")
    vol = float(cfg.get("compiler.music_volume", 0.30))
    fc = (f"[1:a]volume={vol},{AFMT}[m];"
          f"[0:a]{AFMT},asplit=2[voice][key];"
          f"[m][key]sidechaincompress=threshold=0.02:ratio=14:attack=8:release=400[duck];"
          f"[voice][duck]amix=inputs=2:normalize=0:duration=first,"
          f"loudnorm=I=-14:TP=-1.5:LRA=11[a]")
    try:
        _run(["ffmpeg", "-y", "-i", str(master), "-stream_loop", "-1",
              "-i", str(music), "-filter_complex", fc,
              "-map", "0:v", "-map", "[a]", "-c:v", "copy",
              "-c:a", "aac", "-b:a", "192k", str(tmp)])
        tmp.replace(master)
        console.print(f"  [dim]music bed: {Path(music).stem} (ducked)[/]")
    except Exception:  # noqa: BLE001 - music is a bonus, never fatal
        tmp.unlink(missing_ok=True)


def _thumbnail(plan: dict, hero_clip: dict, out: Path) -> Path | None:
    """1280x720 thumbnail: a frame from the hero (#1) moment + 2-4 huge words.
    Uploaded automatically once the channel is phone-verified."""
    text = (plan.get("thumbnail_text") or plan["episode_title"]).upper()
    words = text.split()[:4]
    frame = _grab_frame(db.get_source(hero_clip["source_id"])["video_path"],
                        float(hero_clip["start"]) + 2.0,
                        WORK / f"{out.stem}_src.jpg")
    if not frame:
        return None
    font = _font()
    lines = _wrap(" ".join(words), 14)[:2]
    # auto-shrink so the widest line fits 1280px with margin — a fixed 150px
    # clipped 'BRUTAL TRUTHS' off the right edge of episode 1's thumbnail
    widest = max(len(ln) for ln in lines)
    size = min(150, int((1280 - 150) / (0.62 * widest)))
    draws = []
    for i, ln in enumerate(lines):
        color = "0x00E5FF" if i == len(lines) - 1 else "white"   # punch word pops
        draws.append(f"drawtext=fontfile='{font}':text='{_esc(ln)}'"
                     f":fontsize={size}:fontcolor={color}:borderw=8:bordercolor=black"
                     f":x=70:y={720 - int(size * 1.35) * (len(lines) - i) - 60}")
    vf = (f"scale=1280:720:force_original_aspect_ratio=increase,crop=1280:720,"
          f"eq=contrast=1.12:saturation=1.35,"
          f"vignette=angle=PI/4," + ",".join(draws))
    try:
        _run(["ffmpeg", "-y", "-i", str(frame), "-vf", vf,
              "-frames:v", "1", "-q:v", "3", str(out)])
        return out if out.exists() else None
    except Exception:  # noqa: BLE001 - thumbnail is a bonus
        return None


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

RULES — these keep the episode monetizable AND watchable:
- Pick ONE sharp theme with a THESIS (a debate, a ranking, a "who's right?") —
  not "best moments". {n_min}-{n_max} segments as a COUNTDOWN: we number them
  #{n_max}→#1 on screen, so order them weakest→strongest — #1 must be the payoff.
- RETENTION CONTRACT: the cold_open makes ONE concrete PROMISE about #1 ("by the
  end you'll hear X — and whether he's right"); the outro DELIVERS it and then
  OVERDELIVERS with a bonus receipt/stat/take the promise didn't include.
- Narration is 1-2 PUNCHY sentences per segment (≤20 words) — react, disagree,
  connect; never describe. Every card is a spot viewers can leave, so keep them
  SHORT and get to the clip fast. Punchy and opinionated.
- episode_title: NAME + angle/question, ≤70 chars, no clickbait lies.
- music_mood + thumbnail_text (2-4 ALL-CAPS words) are required.
Call submit_episode."""
    result = llm.call_tool("compiler", prompt, "submit_episode", PLAN_SCHEMA,
                           max_tokens=3000)
    if not result or not result.get("segments"):
        return _default_plan(pool, n_max)
    return result


def compile_episode(upload: bool = True, force: bool = False) -> Path | None:
    """Build this week's episode. Returns the rendered path (None = nothing to
    do or blocked). Uploads as a scheduled long-form post unless upload=False.
    Skips when this week's episode already exists (PSF-Compile also fires at
    startup as a missed-Sunday catch-up) unless force=True."""
    if not cfg.get("compiler.enabled", True):
        return None
    if not force and upload:
        try:
            with db.conn() as c:
                last = c.execute("SELECT MAX(created_at) FROM episodes").fetchone()[0]
            if last and (datetime.now() - datetime.fromisoformat(last)).days < 5:
                console.print("[dim]compiler: this week's episode already exists "
                              "— skipping (catch-up guard).[/]")
                return None
        except Exception:  # noqa: BLE001 - no episodes table yet → proceed
            pass
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

    # the finale (#1) is the hero — its footage backs the intro/outro cards,
    # visually teasing the promised payoff from second one
    hero = by_id[segments[-1]["clip_id"]]
    hero_bg = _grab_frame(db.get_source(hero["source_id"])["video_path"],
                          float(hero["start"]) + 1.0, WORK / f"ep{stamp}_hero.jpg")

    # COLD OPEN — the whole ballgame for long-form retention. Data (2026-07-15):
    # the old narrator-card open lost 43% of viewers in the first 17s. So open on
    # the raw #1 moment itself with the HOOK burned on as text (no talking slate
    # to bail on), then go straight into the countdown. The spoken "promise" card
    # is OFF by default now (compiler.intro_card).
    if cfg.get("compiler.cold_open", True):
        co = _segment(hero, WORK / f"ep{stamp}_cold.mp4",
                      float(cfg.get("compiler.cold_open_seconds", 7)))
        if co > 0:
            _burn_hook(WORK / f"ep{stamp}_cold.mp4",
                       plan.get("hook_text") or plan["episode_title"], co)
            parts.append({"path": WORK / f"ep{stamp}_cold.mp4", "dur": co,
                          "kind": "clip", "chapter": "Cold open"})

    if cfg.get("compiler.intro_card", False):
        d = _card(WORK / f"ep{stamp}_open.mp4", plan["cold_open"],
                  plan["episode_title"],
                  f"{len(segments)} moments. one verdict. stay for #1",
                  bg_frame=hero_bg)
        if d > 0:
            parts.append({"path": WORK / f"ep{stamp}_open.mp4", "dur": d,
                          "kind": "card", "chapter": "The promise"})

    used_sources = []
    for i, seg in enumerate(segments):
        clip = by_id[seg["clip_id"]]
        num = len(segments) - i                     # countdown …3, 2, #1
        card_title = f"#{num} — {seg['card_title']}"
        bg = _grab_frame(db.get_source(clip["source_id"])["video_path"],
                         float(clip["start"]) + 1.0, WORK / f"ep{stamp}_bg{i}.jpg")
        cd = _card(WORK / f"ep{stamp}_c{i}.mp4", seg["narration"],
                   card_title, f"from {clip['channel'] or 'the podcast'}",
                   bg_frame=bg)
        sd = _segment(clip, WORK / f"ep{stamp}_s{i}.mp4", max_excerpt)
        if sd <= 0:
            console.print(f"  [yellow]segment for clip {clip['id']} failed — skipped[/]")
            continue
        if cd > 0:
            parts.append({"path": WORK / f"ep{stamp}_c{i}.mp4", "dur": cd,
                          "kind": "card", "chapter": card_title[:40]})
        parts.append({"path": WORK / f"ep{stamp}_s{i}.mp4", "dur": sd,
                      "kind": "clip", "chapter": None})
        used_sources.append(clip["channel"] or clip["ep"])

    d = _card(WORK / f"ep{stamp}_out.mp4", plan["outro"], "The verdict",
              "as promised — and then some. tell us below", bg_frame=hero_bg)
    if d > 0:
        parts.append({"path": WORK / f"ep{stamp}_out.mp4", "dur": d,
                      "kind": "card", "chapter": "The verdict (as promised)"})

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
    _music_bed(out, plan.get("music_mood", "tense"))
    thumb = _thumbnail(plan, hero, OUT_DIR / f"episode_{stamp}_thumb.jpg")
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
        res = _upload_longform(out, plan["episode_title"], desc, thumb=thumb)
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


def _upload_longform(path: Path, title: str, description: str,
                     thumb: Path | None = None) -> dict | None:
    """Scheduled long-form upload (16:9, no #Shorts). Mirrors the uploader's
    server-side publishAt flow. Sets the custom thumbnail when the channel is
    phone-verified; until then it 403s and we nudge the owner instead."""
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
        if thumb and thumb.exists():
            try:
                yt.thumbnails().set(videoId=vid,
                                    media_body=MediaFileUpload(str(thumb))).execute()
                console.print("  [dim]custom thumbnail set ✓[/]")
            except Exception as tex:  # noqa: BLE001 - needs phone verification
                if "403" in str(tex) or "forbidden" in str(tex).lower():
                    notify.notify("Thumbnail needs channel verification",
                                  "Verify at youtube.com/verify (2 min) — then "
                                  "thumbnails attach automatically.")
                console.print(f"  [yellow]thumbnail not set: {str(tex)[:90]}[/]")
        return {"external_id": vid, "url": f"https://youtu.be/{vid}",
                "when": when.strftime("%a %H:%M")}
    except Exception as ex:  # noqa: BLE001
        console.print(f"[red]long-form upload failed: {ex}[/]")
        notify.notify("Episode upload FAILED",
                      f"{title[:80]}: {str(ex)[:120]} — file kept in output/")
        return None
