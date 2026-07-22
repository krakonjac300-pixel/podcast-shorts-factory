"""Montage Shorts — the ARC7-style emotional-montage FORMAT EXPERIMENT
(user-approved 2026-07-11): 3-4 cross-episode micro-moments around ONE theme,
stitched with name tags, an emoji sticker on the emotional beat, whooshes at
the joins and a ducked music bed. Uses OUR podcast sources only — the style is
borrowed from fan-cam compilation channels, the rights position is ours.

The montage registers as a normal clip row (kind='montage', status='edited') so
the whole downstream pipeline treats it like any Short: Finishing-Editor QA,
Manager review, server-side scheduling, metrics. produce swaps it in for one of
the day's three clips; the Manager's leaderboard now carries `format`, so the
numbers decide whether the format lives after a week.
"""
from __future__ import annotations

import json
import subprocess
from datetime import datetime, timedelta
from pathlib import Path

from rich.console import Console

from .. import db, insights, llm, skills
from ..config import ROOT, cfg
from ..utils import captions
from . import editor

console = Console()

WORK = ROOT / "workdir"
OUT_DIR = ROOT / "output"
AFMT = "aformat=sample_rates=44100:channel_layouts=stereo"

EMOJI = ("laughing", "heart", "fire", "shocked", "mindblown", "crying",
         "goat", "clap", "eyes", "muscle", "sad", "hundred")

PLAN_SCHEMA = {
    "type": "object",
    "properties": {
        "theme": {"type": "string", "description": "the ONE emotional thread, e.g. "
                                                   "'Keane's softest moments'"},
        "hook_text": {"type": "string",
                      "description": "2-5 word on-screen hook for the first 2s"},
        "title": {"type": "string",
                  "description": "NAME + emotional angle, ≤60 chars"},
        "caption": {"type": "string",
                    "description": "post caption ending on ONE forced-choice question"},
        "hashtags": {"type": "array", "items": {"type": "string"}},
        "music_mood": {"type": "string",
                       "description": "tense | upbeat | lofi | ambient"},
        "moments": {
            "type": "array", "minItems": 3, "maxItems": 4,
            "items": {"type": "object", "properties": {
                "clip_id": {"type": "integer"},
                "label": {"type": "string",
                          "description": "speaker name tag, ALL-CAPS, ≤12 chars"},
                "emoji": {"type": "string", "enum": list(EMOJI),
                          "description": "sticker for this moment's emotion"},
                "take_seconds": {"type": "number",
                                 "description": "how much of the moment to use, "
                                                "6-14s — the hook line only"}},
                "required": ["clip_id", "label", "emoji", "take_seconds"]},
        },
    },
    "required": ["theme", "hook_text", "title", "caption", "moments"],
}


# ── pure helpers (unit-tested) ───────────────────────────────────────────────

def _take_window(clip_start: float, clip_end: float, take: float,
                 lo: float = 6.0, hi: float = 14.0) -> tuple[float, float]:
    """Clamp the requested take to [lo, hi] and to the moment's real length.
    Takes from the START — our clips open on the mic-drop line by doctrine."""
    dur = max(0.0, clip_end - clip_start)
    take = max(lo, min(hi, float(take or lo)))
    take = min(take, dur) if dur else take
    return clip_start, clip_start + max(2.0, take)


def _label_safe(label: str) -> str:
    """Name-tag text for drawtext (short, caps, no parser-hostile chars)."""
    lab = (label or "").upper().replace(":", "").replace("'", "").replace(",", "")
    return lab[:14].strip()


def _pool(max_rows: int = 30) -> list[dict]:
    """Moments a montage can reuse: any REGULAR scored clip whose source video
    is still on disk. Already-posted moments are fine — the montage is a new
    transformative recombination, and micro-slices don't compete with the post."""
    since = cfg.get("scheduler.content_since", "")   # niche-flip watershed:
    with db.conn() as c:                             # old-era moments stay out
        rows = c.execute("""
            SELECT cl.id, cl.title, cl.reason, cl.score, cl.start, cl.end,
                   cl.source_id, s.video_path, s.channel
            FROM clips cl JOIN sources s ON s.id = cl.source_id
            WHERE cl.kind IS NULL AND cl.status != 'flagged'
              AND cl.created_at >= ?
            ORDER BY cl.score DESC LIMIT ?""", (since, max_rows)).fetchall()
    return [dict(r) for r in rows if Path(r["video_path"] or "").exists()]


def _default_plan(pool: list[dict]) -> dict:
    """LLM-free fallback: top 3 moments, labels from the title's leading word."""
    moments = [{"clip_id": p["id"],
                "label": _label_safe(p["title"].split()[0] if p["title"] else "CLIP"),
                "emoji": "fire", "take_seconds": 10} for p in pool[:3]]
    return {"theme": "the moments everyone argued about",
            "hook_text": "3 MOMENTS. 1 THREAD",
            "title": "The Podcast Moments Everyone Argued About This Week",
            "caption": "Three moments, one thread. Which one wins?",
            "hashtags": ["#podcast", "#football", "#shorts"],
            "music_mood": "tense", "moments": moments}


# ── render ───────────────────────────────────────────────────────────────────

def _run(cmd: list[str]) -> None:
    subprocess.run(cmd, check=True, capture_output=True)


def _src_size(video_path: str) -> tuple[int, int]:
    p = subprocess.run(
        ["ffprobe", "-v", "error", "-select_streams", "v:0",
         "-show_entries", "stream=width,height", "-of", "csv=p=0", video_path],
        capture_output=True, text=True)
    try:
        w, h = (int(x) for x in p.stdout.strip().split(",")[:2])
        return w, h
    except Exception:  # noqa: BLE001
        return 0, 0


def _render_moment(clip: dict, take_s: float, take_e: float, label: str,
                   emoji: str, out: Path, w: int, h: int) -> float:
    """One micro-moment: face punch-in 9:16 + karaoke captions + name tag +
    emoji sticker fading in on the beat. Returns duration (0 = failed)."""
    src = db.get_source(clip["source_id"])
    dur = take_e - take_s
    sw, sh = _src_size(src["video_path"])
    # face for THIS window (segment-truthful, same guarantee as the editor)
    seg = editor._segment_face_cxs(src["video_path"], [take_s, take_e])
    cx = seg[0] if seg else None
    vf = editor._vf_face(w, h, sw, sh, cx) or editor._vf_vertical(w, h, "blur")
    vf += f",fps=30,{editor._motion(w, h, dur, 0.08)}"
    vf += ",eq=contrast=1.06:saturation=1.22"
    # captions for the window
    words = [wd for s in json.loads(src["transcript_json"]) for wd in s["words"]]
    style = dict(cfg.get("editor.captions", {}) or {})
    ass = WORK / f"{out.stem}.ass"
    captions.write_ass(ass, words, take_s, take_e, style, res=(w, h),
                       emphasis_words=[])
    vf += f",subtitles='{str(ass).replace(chr(92), '/').replace(':', chr(92) + ':')}'"
    # name tag pill (above the caption band)
    font = editor._font_escaped()
    if label:
        vf += (f",drawtext=" + (f"fontfile='{font}':" if font else "")
               + f"text='{label}':fontsize=46:fontcolor=white:borderw=0:"
               f"box=1:boxcolor=black@0.62:boxborderw=16:x=64:y={h - 900}")
    vf += f",fade=t=in:st=0:d=0.18,fade=t=out:st={dur - 0.18:.2f}:d=0.18"

    inputs = ["-ss", f"{take_s:.2f}", "-t", f"{dur:.2f}",
              "-i", str(src["video_path"])]
    fc_pre = f"[0:v]{vf}[base]"
    vmap = "[base]"
    sticker = ROOT / "assets" / "emoji" / f"{emoji}.png"
    if sticker.exists():
        t0 = min(1.0, dur / 3)               # sticker lands just after the line
        # -loop 1 is required: a bare PNG input is a single frame at t=0, so a
        # fade starting at 1s would fade a stream that already ended (invisible)
        inputs += ["-loop", "1", "-t", f"{dur:.2f}", "-i", str(sticker)]
        fc_pre += (f";[1:v]scale=210:210,format=rgba,"
                   f"fade=t=in:st={t0:.2f}:d=0.25:alpha=1,"
                   f"fade=t=out:st={min(t0 + 2.2, dur - 0.3):.2f}:d=0.3:alpha=1[st];"
                   f"[base][st]overlay=x={w - 280}:y=360[withst]")
        vmap = "[withst]"
    _run(["ffmpeg", "-y", *inputs, "-filter_complex",
          f"{fc_pre};[0:a]{AFMT}[a]",
          "-map", vmap, "-map", "[a]", "-t", f"{dur:.2f}",
          "-c:v", "libx264", "-preset", "fast", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])
    p = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                        "format=duration", "-of", "csv=p=0", str(out)],
                       capture_output=True, text=True)
    return float(p.stdout.strip() or 0)


def _assemble(parts: list[Path], durs: list[float], hook: str, out: Path,
              w: int, h: int) -> None:
    """Concat the moments; hook text over the open, whoosh at each join,
    progress bar + end CTA over the whole montage."""
    total = sum(durs)
    inputs, norm, chains = [], [], []
    for i, p in enumerate(parts):
        inputs += ["-i", str(p)]
        norm.append(f"[{i}:v]setsar=1[v{i}]")
        chains.append(f"[v{i}][{i}:a]")
    n = len(parts)
    vf = [editor._drawtext_block(hook, size=84, y_top=170,
                                 enable="between(t,0,2.2)")]
    if cfg.get("editor.progress_bar", True):
        vf.append(f"drawbox=x=0:y=ih-14:w=iw:h=14:color=black@0.45:t=fill,"
                  f"drawbox=x=0:y=ih-14:w='iw*t/{total:.2f}':h=14:"
                  f"color=white@0.85:t=fill")
    if cfg.get("editor.cta", True) and total > 20:
        vf.append(editor._drawtext(cfg.get("editor.cta_text", "FOLLOW FOR MORE"),
                                   size=58, y="h-320",
                                   enable=f"between(t,{total - 2.2:.2f},{total:.2f})"))
    # whoosh at each interior join
    fc = ";".join(norm) + ";" + "".join(chains) + \
        f"concat=n={n}:v=1:a=1[cv][ca];[cv]{','.join(vf)}[v]"
    amix, idx = ["[ca]"], n
    whoosh = None
    sfx_dir = ROOT / cfg.get("editor.sfx_dir", "assets/sfx")
    hits = sorted(sfx_dir.glob("whoosh*.wav")) + sorted(sfx_dir.glob("swoosh*.wav"))
    whoosh = hits[0] if hits else None
    if whoosh:
        t = 0.0
        for d in durs[:-1]:
            t += d
            inputs += ["-i", str(whoosh)]
            fc += (f";[{idx}:a]adelay={int(t * 1000)}|{int(t * 1000)},"
                   f"volume=0.30,{AFMT}[w{idx}]")
            amix.append(f"[w{idx}]")
            idx += 1
    if len(amix) > 1:
        fc += (";" + "".join(amix)
               + f"amix=inputs={len(amix)}:normalize=0:duration=first[a]")
    else:
        fc += ";[ca]anull[a]"
    _run(["ffmpeg", "-y", *inputs, "-filter_complex", fc,
          "-map", "[v]", "-map", "[a]",
          "-c:v", "libx264", "-preset", "medium", "-crf", "20",
          "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])


def build_daily(register: bool = True) -> int | None:
    """Build today's montage Short. Returns the new clip id (None = skipped).
    register=False renders to output/ without touching the DB (dry run)."""
    m = cfg.get("montage", {}) or {}
    if not m.get("enabled", True):
        return None
    pool = _pool()
    if len(pool) < 3:
        console.print("[yellow]montage: not enough moments with source on disk[/]")
        return None
    # one per day
    if register:
        with db.conn() as c:
            last = c.execute("SELECT MAX(created_at) FROM clips WHERE kind='montage'"
                             ).fetchone()[0]
        if last and datetime.utcnow() - datetime.fromisoformat(last) < timedelta(hours=20):
            console.print("[dim]montage: today's already made — skipping[/]")
            return None

    console.print(f"[bold blue]MONTAGE[/] planning from {len(pool)} moments "
                  f"({llm.describe()})…")
    plan = None
    if llm.available():
        menu = [{k: p[k] for k in ("id", "title", "reason", "score", "channel")}
                | {"secs": round(p["end"] - p["start"])} for p in pool]
        skill_block = skills.load(cfg.get("skills.editor", [])[:6])
        prompt = f"""You are cutting an ARC7-style EMOTIONAL MONTAGE Short from our podcast moments.
Pick ONE emotional thread and 3-4 moments that build it — cross-EPISODE when
possible, strongest LAST. Each moment contributes its opening 6-14s only (they
all start on their mic-drop line).

{skill_block}
What's working on our channel:
{insights.learnings()[:2500]}

AVAILABLE MOMENTS (id, title, why, score, source channel, seconds):
{json.dumps(menu, indent=1)[:3500]}

Rules: prefer moments whose speaker is a HOUSEHOLD NAME (per the channel
learnings above — montages of unknown names get zero distribution). label = the
SPEAKER's name tag (ALL-CAPS, ≤12 chars — the person ON SCREEN, not who they
talk about). emoji matches the moment's emotion. hook_text ≤5 words, names the
thread ("KEANE'S SOFT SIDE"). title = NAME + emotional angle ≤60 chars. caption
ends on ONE forced-choice question. Call submit_montage."""
        plan = llm.call_tool("editor", prompt, "submit_montage", PLAN_SCHEMA,
                             max_tokens=1200)
    if not plan or not plan.get("moments"):
        plan = _default_plan(pool)
    by_id = {p["id"]: p for p in pool}
    moments = [x for x in plan["moments"] if x.get("clip_id") in by_id][:4]
    if len(moments) < 3:
        plan = _default_plan(pool)
        moments = plan["moments"]

    w, h = cfg.get("editor.resolution", [1080, 1920])
    stamp = datetime.now().strftime("%Y%m%d")
    parts, durs = [], []
    for i, mo in enumerate(moments):
        clip = by_id[mo["clip_id"]]
        ts, te = _take_window(clip["start"], clip["end"],
                              mo.get("take_seconds", 10),
                              float(m.get("take_min", 6)),
                              float(m.get("take_max", 14)))
        out = WORK / f"mont{stamp}_{i}.mp4"
        try:
            d = _render_moment(clip, ts, te, _label_safe(mo.get("label", "")),
                               mo.get("emoji", "fire"), out, w, h)
        except subprocess.CalledProcessError as ex:
            console.print(f"  [yellow]moment {clip['id']} failed: "
                          f"{ex.stderr.decode(errors='ignore')[-140:]}[/]")
            continue
        if d > 0:
            parts.append(out)
            durs.append(d)
    if len(parts) < 3:
        console.print("[yellow]montage: too few moments rendered — skipping[/]")
        return None

    final = OUT_DIR / f"montage_{stamp}.mp4"
    _assemble(parts, durs, plan.get("hook_text", "THE MOMENTS"), final, w, h)
    from . import compiler
    compiler._music_bed(final, plan.get("music_mood", "tense"))
    console.print(f"[green]MONTAGE ready[/] {sum(durs):.0f}s x{len(parts)} "
                  f"→ {final.name}")

    if not register:
        return None
    primary = by_id[moments[-1]["clip_id"]]          # strongest moment's source
    cid = db.add_clip(primary["source_id"], 0, sum(durs), plan["title"],
                      f"montage experiment: {plan.get('theme', '')}",
                      float(m.get("score", 75)), plan.get("caption", ""),
                      plan.get("hashtags", []))
    with db.conn() as c:
        c.execute("UPDATE clips SET kind='montage', status='edited', "
                  "rendered_path=? WHERE id=?", (str(final), cid))
    console.print(f"  [dim]registered as clip {cid} (kind=montage) — flows "
                  f"through QA → Manager → scheduler like any Short[/]")
    return cid
