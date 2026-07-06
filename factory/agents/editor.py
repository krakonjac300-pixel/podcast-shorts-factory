"""Agent 2 — EDITOR.

Cuts each approved clip with ffmpeg, reframes to vertical 9:16, burns in
karaoke captions, and mixes background music.
"""
from __future__ import annotations

import json
import random
import subprocess
from pathlib import Path

from rich.console import Console

from .. import db, notify
from ..config import ROOT, WORK, cfg
from ..utils import broll, captions, trimmer, voice
from . import planner

console = Console()
OUT_DIR = ROOT / "output"
OUT_DIR.mkdir(exist_ok=True)


def _font_escaped() -> str:
    """ffmpeg drawtext needs the font path with the drive colon escaped."""
    fp = cfg.get("editor.font_file", "") or ""
    return fp.replace("\\", "/").replace(":", "\\:")


def _drawtext(text: str, *, size: int, y: str, enable: str | None = None) -> str:
    """Build a drawtext filter fragment with a real fontfile (Windows-safe)."""
    safe = text.replace("'", "").replace(":", " ").replace("\\", "")
    font = _font_escaped()
    frag = (f"drawtext=" + (f"fontfile='{font}':" if font else "")
            + f"text='{safe}':fontcolor=white:fontsize={size}:"
            f"borderw=6:bordercolor=black:box=1:boxcolor=black@0.55:"
            f"boxborderw=22:x=(w-text_w)/2:y={y}")
    if enable:
        frag += f":enable='{enable}'"
    return frag


def _wrap(text: str, max_chars: int) -> list[str]:
    """Greedy word-wrap so text never runs off the frame edges."""
    lines, cur = [], ""
    for word in text.split():
        if cur and len(cur) + 1 + len(word) > max_chars:
            lines.append(cur)
            cur = word
        else:
            cur = f"{cur} {word}".strip()
    if cur:
        lines.append(cur)
    return lines


def _drawtext_block(text: str, *, size: int, y_top: int, enable: str | None,
                    max_chars: int = 16, max_lines: int = 3) -> str:
    """Multi-line centered text that fits the width — each line is its own
    drawtext stacked vertically. Prevents the hook from spilling off-screen."""
    lines = _wrap(text, max_chars)[:max_lines]
    line_h = int(size * 1.7)                       # room for the box padding
    return ",".join(
        _drawtext(ln, size=size, y=str(y_top + i * line_h), enable=enable)
        for i, ln in enumerate(lines))


def _trim_pass(src: str, clip_id, start: float, dur: float, trim: dict) -> Path:
    """First pass: cut down to the kept segments. Each cut gets a 15ms audio
    fade in/out so joins never pop or click (hard select() joins do)."""
    out = WORK / f"trim_{clip_id}.mp4"
    segs = trim.get("segments") or []
    if segs:
        F = 0.015
        parts, cc = [], []
        for i, (a, b) in enumerate(segs):
            d = b - a
            parts.append(f"[0:v]trim=start={a:.3f}:end={b:.3f},"
                         f"setpts=PTS-STARTPTS[v{i}]")
            fade_out = f",afade=t=out:st={max(d - F, 0):.3f}:d={F}" if d > 3 * F else ""
            parts.append(f"[0:a]atrim=start={a:.3f}:end={b:.3f},"
                         f"asetpts=PTS-STARTPTS,afade=t=in:st=0:d={F}"
                         f"{fade_out}[a{i}]")
            cc.append(f"[v{i}][a{i}]")
        graph = (";".join(parts) + ";" + "".join(cc)
                 + f"concat=n={len(segs)}:v=1:a=1[v][a]")
        args = ["-filter_complex", graph, "-map", "[v]", "-map", "[a]"]
    else:   # fallback: old select()-based path
        expr = trim["expr"]
        args = ["-vf", f"select='{expr}',setpts=N/FRAME_RATE/TB",
                "-af", f"aselect='{expr}',asetpts=N/SR/TB"]
    subprocess.run(
        ["ffmpeg", "-y", "-ss", f"{start}", "-t", f"{dur}", "-i", src] + args
        + ["-c:v", "libx264", "-preset", "veryfast", "-crf", "18",
           "-c:a", "aac", "-b:a", "192k", str(out)],
        check=True, capture_output=True,
    )
    return out


# Map whatever name the AI invents for a sound onto a file we actually have,
# so cues never get silently dropped. Keys are substrings matched in the cue type.
_SFX_SYNONYMS = {
    "whoosh": "whoosh", "woosh": "whoosh", "swoosh": "swoosh", "swipe": "swoosh",
    "transition": "swoosh", "slide": "swoosh", "air": "whoosh",
    "riser": "riser", "rise": "riser", "build": "riser", "suspense": "riser",
    "tension": "riser", "buildup": "riser", "drum roll": "riser",
    "impact": "impact", "boom": "impact", "hit": "impact", "punch": "impact",
    "bass": "impact", "slam": "impact", "thud": "impact", "drop": "impact",
    "ding": "ding", "chime": "ding", "bell": "ding", "ping": "ding",
    "success": "ding", "correct": "ding", "notification": "ding", "sparkle": "ding",
    "pop": "pop", "click": "pop", "tap": "pop", "bubble": "pop", "blip": "pop",
    "scratch": "pop", "record": "pop", "stop": "pop",
}


def _resolve_sfx(name: str, sfx_dir: Path) -> Path | None:
    """Resolve an arbitrary SFX name to a real file in the pack (fuzzy match).
    Each type can have variants (whoosh.wav, whoosh-2.wav…) — pick one at
    random so repeated cues don't sound copy-pasted."""
    key = (name or "").strip().lower()

    def pick(stem: str) -> Path | None:
        variants = sorted(sfx_dir.glob(f"{stem}.wav")) + \
            sorted(sfx_dir.glob(f"{stem}-*.wav"))
        return random.choice(variants) if variants else None

    hit = pick(key)
    if hit:
        return hit
    for token, target in _SFX_SYNONYMS.items():
        if token in key:
            hit = pick(target)
            if hit:
                return hit
    return None


# Map the planner's free-form music_mood onto our track names.
_MOOD_SYNONYMS = {
    "upbeat": "upbeat", "energetic": "upbeat", "happy": "upbeat", "fun": "upbeat",
    "motiv": "upbeat", "hype": "upbeat",
    "tense": "tense", "suspense": "tense", "dramatic": "tense", "dark": "tense",
    "serious": "tense", "urgent": "tense",
    "lofi": "lofi", "lo-fi": "lofi", "chill": "lofi", "relax": "lofi",
    "calm": "lofi", "laid": "lofi",
    "ambient": "ambient", "inspir": "ambient", "thought": "ambient",
    "emotional": "ambient", "reflect": "ambient",
}


# Map the planner's meme emotions onto files the user drops in assets/memes/
# (any .gif/.mp4/.webm whose filename contains the emotion word).
_MEME_SYNONYMS = {
    "mind-blown": "mindblown", "mindblown": "mindblown", "blown": "mindblown",
    "explod": "mindblown", "wow": "mindblown",
    "laugh": "laughing", "funny": "laughing", "lol": "laughing",
    "shock": "shocked", "surpris": "shocked", "gasp": "shocked", "what": "shocked",
    "facepalm": "facepalm", "fail": "facepalm", "bruh": "facepalm",
    "money": "money", "rich": "money", "cash": "money",
    "cry": "crying", "sad": "crying",
    "clap": "clapping", "applause": "clapping", "respect": "clapping",
}


def _resolve_meme(emotion: str, mdir: Path) -> Path | None:
    """Find a local reaction clip matching the planner's emotion."""
    if not mdir.exists():
        return None
    key = (emotion or "").strip().lower()
    target = next((t for token, t in _MEME_SYNONYMS.items() if token in key), key)
    hits = [p for p in mdir.iterdir()
            if p.suffix.lower() in (".gif", ".mp4", ".webm")
            and target in p.stem.lower()]
    return random.choice(hits) if hits else None


def _teaser_pass(render_src: str, clip_id, reframe_vf: str, plan: dict,
                 tscale: float, render_dur: float, w: int, h: int) -> Path | None:
    """The cold-open: an AI narrator line ('Wait for what he says…') over a
    quick flash-preview of the payoff moment — the strongest retention trick
    in short-form. Returns the teaser mp4, or None if not applicable."""
    text = (plan.get("narrator_intro") or "").strip()
    times = [float(t) * tscale for t in plan.get("teaser_times") or []]
    times = [t for t in times if 1.0 < t < render_dur - 1.0][:2]
    if not text or not times:
        return None
    vo = WORK / f"vo_{clip_id}.mp3"
    vo_dur = voice.synth(text, vo, cfg.get("editor.voice", "en-US-ChristopherNeural"))
    if vo_dur <= 0:
        return None
    total = min(max(1.6, vo_dur + 0.3), 3.4)
    seg = total / len(times)
    out = WORK / f"teaser_{clip_id}.mp4"

    vparts, aparts, labels = [], [], []
    for i, t in enumerate(times):
        a, b = max(0.0, t - seg / 2), max(0.0, t - seg / 2) + seg + 0.15
        # blur-fit reframe has internal labels — suffix them so two teaser
        # segments don't collide in one graph
        rvf = reframe_vf
        for lbl in ("bg", "fg", "bgb", "fgs"):
            rvf = rvf.replace(f"[{lbl}]", f"[{lbl}{i}]")
        vparts.append(f"[0:v]trim=start={a:.2f}:end={b:.2f},setpts=PTS-STARTPTS,"
                      f"{rvf}[tv{i}]")
        aparts.append(f"[0:a]atrim=start={a:.2f}:end={b:.2f},asetpts=PTS-STARTPTS,"
                      f"volume=0.15,aformat=sample_rates=44100:channel_layouts=stereo[ta{i}]")
        labels.append(i)
    if len(labels) == 2:
        vjoin = (f"[tv0][tv1]xfade=transition=hblur:duration=0.18:"
                 f"offset={seg - 0.05:.2f}[tvj];")
        ajoin = "[ta0][ta1]concat=n=2:v=0:a=1[taj];"
    else:
        vjoin = "[tv0]null[tvj];"
        ajoin = "[ta0]anull[taj];"
    txt = _drawtext_block(text.upper(), size=62, y_top=int(h * 0.12),
                          enable=None, max_chars=16, max_lines=3)
    graph = (";".join(vparts) + ";" + ";".join(aparts) + ";" + vjoin + ajoin
             + f"[tvj]{txt},trim=duration={total:.2f}[v];"
             f"[1:a]aformat=sample_rates=44100:channel_layouts=stereo,"
             f"adelay=120|120[vo];"
             f"[taj][vo]amix=inputs=2:normalize=0:duration=first,"
             f"atrim=duration={total:.2f}[a]")
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-i", render_src, "-i", str(vo),
             "-filter_complex", graph, "-map", "[v]", "-map", "[a]",
             "-c:v", "libx264", "-preset", "veryfast", "-crf", "20",
             "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)],
            check=True, capture_output=True)
        return out
    except subprocess.CalledProcessError as ex:
        console.print(f"  [yellow]teaser skipped:[/] "
                      f"{ex.stderr.decode(errors='ignore')[-200:]}")
        return None


def _pick_music(mood: str = "") -> Path | None:
    """Track matching the planner's mood; 'none' means silence on purpose."""
    mood = (mood or "").strip().lower()
    if mood in ("none", "silence", "no music"):
        return None
    mdir = ROOT / cfg.get("editor.music_dir", "assets/music")
    if not mdir.exists():
        return None
    tracks = [p for p in mdir.iterdir()
              if p.suffix.lower() in (".mp3", ".m4a", ".wav", ".ogg")]
    if not tracks:
        return None
    for token, target in _MOOD_SYNONYMS.items():
        if token in mood:
            match = [t for t in tracks if target in t.stem.lower()]
            if match:
                return random.choice(match)
    return random.choice(tracks)


def _vf_vertical(w: int, h: int, background: str) -> str:
    """Build a video filter that turns 16:9 into 9:16."""
    if background == "black":
        return (f"scale={w}:{h}:force_original_aspect_ratio=decrease,"
                f"pad={w}:{h}:(ow-iw)/2:(oh-ih)/2:black")
    # blurred fill behind a centered, fitted video
    return (
        f"split=2[bg][fg];"
        f"[bg]scale={w}:{h}:force_original_aspect_ratio=increase,"
        f"crop={w}:{h},boxblur=40:8[bgb];"
        f"[fg]scale={w}:{h}:force_original_aspect_ratio=decrease[fgs];"
        f"[bgb][fgs]overlay=(W-w)/2:(H-h)/2"
    )


def _face_center(video_path: str, samples: int = 16):
    """Sample frames for faces. Returns (cx_norm_or_None, src_w, src_h, n_faces)
    where n_faces is the typical number of faces on screen (2+ = interview shot).
    Safe if OpenCV is unavailable."""
    try:
        import cv2
    except Exception:  # noqa: BLE001
        return None, 0, 0, 0, 0.0
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        return None, 0, 0, 0, 0.0
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) or 0
    sw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    sh = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)) or 0
    cascade = cv2.CascadeClassifier(
        cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    centers, counts, fracs = [], [], []
    idxs = [int(total * i / (samples + 1)) for i in range(1, samples + 1)] if total else []
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, frame = cap.read()
        if not ok:
            continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, scaleFactor=1.1, minNeighbors=5,
                                         minSize=(60, 60))
        counts.append(len(faces))
        if len(faces) and sw:
            x, _, fw, fh = max(faces, key=lambda r: r[2] * r[3])  # largest face
            centers.append((x + fw / 2) / sw)
            fracs.append((fw * fh) / float(sw * sh))
    cap.release()
    counts.sort()
    fracs.sort()
    n_faces = counts[len(counts) // 2] if counts else 0     # typical (median) count
    face_frac = fracs[len(fracs) // 2] if fracs else 0.0    # typical face size
    if not centers:
        return None, sw, sh, n_faces, face_frac
    centers.sort()
    return centers[len(centers) // 2], sw, sh, n_faces, face_frac


# Fake-multicam shot pattern: zoom level per segment, cycled. 0 = wide,
# 0.14 = close-up, 0.07 = medium. Alternating reads as camera cuts.
_SHOT_CYCLE = (0.0, 0.14, 0.07, 0.14)


def _cut_points(cap_words, cap_start: float, dur: float,
                min_len: float = 3.0, max_len: float = 6.0) -> list[float]:
    """Camera-cut times (clip-local): at sentence ends / speech pauses, min
    segment length enforced, forced cut when a segment runs long. This is what
    makes a single static podcast angle feel like a multicam edit."""
    cuts, seg_start = [], 0.0
    for i, wd in enumerate(cap_words):
        t = wd["end"] - cap_start
        if t - seg_start < min_len or t > dur - 1.5:
            continue
        nxt = cap_words[i + 1] if i + 1 < len(cap_words) else None
        gap = (nxt["start"] - wd["end"]) if nxt else 0.0
        sentence_end = wd["word"].strip()[-1:] in ".?!"
        if sentence_end or gap > 0.45 or t - seg_start >= max_len:
            cut = t + min(gap / 2, 0.2)          # cut inside the pause
            cuts.append(round(cut, 2))
            seg_start = cut
    return cuts


def _motion(w: int, h: int, dur: float, amount: float, punches=(),
            cuts=()) -> str:
    """The whole 'camera' in one crop: slow Ken Burns push (amount), quick zoom
    PUNCHES on key words, and hard zoom CUTS at sentence boundaries (fake
    multicam: wide ↔ close-up). Applied BEFORE captions so text stays sharp."""
    d = max(dur, 0.1)
    if not punches and not cuts:
        z = f"{amount}*min(t/{d}\\,1)"
    else:
        # crop w/h are evaluated once at init where t is NaN — max/abs would
        # poison the result, so stash a NaN-guarded t in st(0)/ld(0) first.
        expr = f"{amount}*min(ld(0)/{d}\\,1)"
        for t in punches:
            expr += f"+0.16*max(0\\,1-abs(ld(0)-{t:.2f})/0.25)"
        bounds = [0.0] + list(cuts) + [d + 1]
        for i in range(len(bounds) - 1):
            level = _SHOT_CYCLE[i % len(_SHOT_CYCLE)]
            if level:
                expr += (f"+{level}*gte(ld(0)\\,{bounds[i]:.2f})"
                         f"*lt(ld(0)\\,{bounds[i + 1]:.2f})")
        z = f"st(0\\,if(isnan(t)\\,0\\,t));min(0.45\\,{expr})"
    return (f"crop=w='iw-iw*({z})':h='ih-ih*({z})':"
            f"x='(iw-ow)/2':y='(ih-oh)/2',scale={w}:{h}")


def _punch_times(cap_words, cap_start: float, emphasis_words, dur: float,
                 limit: int = 4, min_gap: float = 2.0) -> list[float]:
    """When each emphasis word is SPOKEN (clip-local seconds) → zoom punch there.
    Skips the first second, keeps punches spread out, caps the count."""
    emph = {captions._norm(x) for x in (emphasis_words or []) if captions._norm(x)}
    times, seen = [], set()
    for wd in cap_words:
        key = captions._norm(wd["word"])
        t = wd["start"] - cap_start
        if key in emph and key not in seen and 1.0 < t < dur - 0.6:
            if not times or t - times[-1] >= min_gap:
                times.append(round(t, 2))
                seen.add(key)
        if len(times) >= limit:
            break
    return times


def _flashes(times: list[float], dur: float, limit: int = 3) -> str:
    """White-flash accents at topic shifts (the planner's transition marks) —
    a quick brightness pop like a hard cut in a pro edit."""
    frags = []
    for t in times[:limit]:
        if 0.5 < t < dur - 0.5:
            frags.append(f"eq=brightness=0.28:enable='between(t\\,{t:.2f}\\,{t + 0.12:.2f})'")
    return ",".join(frags)


def _vf_face(w: int, h: int, sw: int, sh: int, cx) -> str | None:
    """Punch-in 9:16 crop centered on the speaker's face (fills frame, no bars)."""
    if not sw or not sh:
        return None
    scaled_w = int(round(h * sw / sh))
    scaled_w -= scaled_w % 2
    if scaled_w < w:                       # source narrower than 9:16 — can't crop
        return None
    if cx is None:
        cropx = (scaled_w - w) // 2
    else:
        cropx = int(cx * scaled_w - w / 2)
        cropx = max(0, min(cropx, scaled_w - w))
    return f"scale={scaled_w}:{h},crop={w}:{h}:{cropx}:0"


def _segment_face_cxs(video_path: str, bounds: list[float],
                      two_face: bool = False) -> list[float] | None:
    """Face x-position (0..1) per camera segment. `bounds` are segment edges
    in seconds. two_face=True → alternate the LEFT and RIGHT face positions
    per segment (a 2-shot becomes host-cam / guest-cam cuts)."""
    try:
        import cv2
    except Exception:  # noqa: BLE001
        return None
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        return None
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    sw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    if not sw:
        cap.release()
        return None
    cascade = cv2.CascadeClassifier(
        cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
    seg_cx: list[float | None] = []
    all_cx: list[float] = []
    for i in range(len(bounds) - 1):
        found = []
        for frac in (0.35, 0.65):
            tm = bounds[i] + (bounds[i + 1] - bounds[i]) * frac
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(tm * fps))
            ok, frame = cap.read()
            if not ok:
                continue
            gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
            faces = cascade.detectMultiScale(gray, scaleFactor=1.1,
                                             minNeighbors=5, minSize=(60, 60))
            all_cx.extend((x + fw / 2) / sw for x, _, fw, _ in faces)
            if len(faces):
                x, _, fw, _ = max(faces, key=lambda r: r[2] * r[3])
                found.append((x + fw / 2) / sw)
        found.sort()
        seg_cx.append(found[len(found) // 2] if found else None)
    cap.release()
    if not all_cx:
        return None
    all_cx.sort()
    med = all_cx[len(all_cx) // 2]
    if two_face:
        left = sorted(c for c in all_cx if c < med) or [med]
        right = sorted(c for c in all_cx if c >= med) or [med]
        lx, rx = left[len(left) // 2], right[len(right) // 2]
        if abs(rx - lx) < 0.12:              # faces too close — not a real 2-shot
            return None
        return [lx if i % 2 == 0 else rx for i in range(len(bounds) - 1)]
    # single face: fill gaps with the previous segment (camera holds position)
    out, last = [], med
    for c in seg_cx:
        last = c if c is not None else last
        out.append(last)
    return out


def _vf_face_steps(w: int, h: int, sw: int, sh: int,
                   seg_cx: list[float], bounds: list[float]) -> str | None:
    """Punch-in crop whose x RE-CENTERS per camera segment — every cut reframes
    on the subject like an operator following the speaker."""
    if not sw or not sh or not seg_cx:
        return None
    scaled_w = int(round(h * sw / sh))
    scaled_w -= scaled_w % 2
    if scaled_w < w:
        return None
    xs = [max(0, min(int(cx * scaled_w - w / 2), scaled_w - w)) for cx in seg_cx]
    if len(set(xs)) == 1:                    # nothing moves — use the simple crop
        return f"scale={scaled_w}:{h},crop={w}:{h}:{xs[0]}:0"
    terms = "+".join(
        f"{x}*gte(ld(0)\\,{bounds[i]:.2f})*lt(ld(0)\\,{bounds[i + 1]:.2f})"
        for i, x in enumerate(xs))
    expr = f"st(0\\,if(isnan(t)\\,0\\,t));({terms})"
    return f"scale={scaled_w}:{h},crop=w={w}:h={h}:x='{expr}':y=0"


def edit_clip(clip) -> Path:
    source = db.get_source(clip["source_id"])
    transcript = json.loads(source["transcript_json"])
    words = [w for seg in transcript for w in seg["words"]]

    e = cfg.editor
    w, h = e.get("resolution", [1080, 1920])
    start, end = clip["start"], clip["end"]
    dur = end - start
    out = OUT_DIR / f"clip_{clip['id']}.mp4"

    # 0. creative plan from the editor's installed skills (hooks/story/sfx/vfx)
    plan = planner.plan_clip(clip, words)
    (OUT_DIR / f"clip_{clip['id']}.notes.md").write_text(
        planner.render_notes(clip, plan), encoding="utf-8")
    console.print(f"  [dim]plan: hook='{plan['hook_text']}' "
                  f"music={plan['music_mood']} "
                  f"emphasis={len(plan.get('emphasis_words', []))} "
                  f"sfx={len(plan.get('sfx_cues', []))}[/]")

    # 0b. trim pass — cut filler words + dead air, then style the tightened clip
    render_src, render_start, render_dur = source["video_path"], start, dur
    cap_words, cap_start, cap_end = words, start, end
    trim = trimmer.compute(words, start, end, e.get("trim", {}))
    if trim:
        s = trim["stats"]
        console.print(f"  [dim]trim: {s['orig']:.1f}s → {s['trimmed']:.1f}s "
                      f"(-{s['removed']:.1f}s, {s['segments']} kept segments)[/]")
        render_src = str(_trim_pass(source["video_path"], clip["id"], start, dur, trim))
        render_start, render_dur = 0.0, trim["new_dur"]
        cap_words, cap_start, cap_end = trim["new_words"], 0.0, trim["new_dur"]

    # timing helper: plan times are pre-trim; remap them onto the trimmed timeline
    tscale = (render_dur / dur) if dur > 0 else 1.0

    # 0c. camera-cut plan first — the reframe below re-centers per shot
    punches, cuts = [], []
    if e.get("punch_zoom", True):
        punches = _punch_times(cap_words, cap_start,
                               plan.get("emphasis_words", []), render_dur)
    if e.get("camera_cuts", True):
        cuts = _cut_points(cap_words, cap_start, render_dur)
        if cuts:
            console.print(f"  [dim]camera: {len(cuts) + 1} shots "
                          f"(cuts at {', '.join(f'{c:.0f}s' for c in cuts)})[/]")
    bounds = [0.0] + cuts + [render_dur + 1]

    # 1. reframe to 9:16.
    #    "smart" (default): single face → punch-in that RE-CENTERS on the face
    #    at every camera cut; two faces → alternate host-cam/guest-cam crops
    #    (real multicam feel) or full-frame fit; tiny face = screen layout → fit.
    mode = e.get("reframe", "smart")
    bg = e.get("background", "blur")
    vf, static_vf = None, None
    if mode in ("smart", "face"):
        cx, sw, sh, nfaces, face_frac = _face_center(render_src)
        static_vf = _vf_face(w, h, sw, sh, cx)
        if mode == "smart" and cx is not None and face_frac < 0.02:
            # tiny face = screen-share / PiP layout (host reacting to a video):
            # punching in would crop a random slice of the collage — show it all
            vf = _vf_vertical(w, h, bg)
            static_vf = None
            console.print(f"  [dim]reframe: face only {face_frac * 100:.1f}% of frame "
                          f"→ screen layout, full-frame fit[/]")
        elif mode == "smart" and nfaces >= 2:
            seg_cx = (_segment_face_cxs(render_src, bounds, two_face=True)
                      if e.get("speaker_cuts", True) and cuts else None)
            vf = _vf_face_steps(w, h, sw, sh, seg_cx, bounds) if seg_cx else None
            if vf:
                console.print(f"  [dim]reframe: 2 faces → host/guest camera "
                              f"alternation across {len(bounds) - 1} shots[/]")
            else:
                vf = _vf_vertical(w, h, bg)  # fallback: show everyone
                static_vf = None
                console.print(f"  [dim]reframe: {nfaces} faces → full-frame fit[/]")
        else:
            seg_cx = (_segment_face_cxs(render_src, bounds)
                      if e.get("track_face_per_shot", True) and cuts else None)
            vf = (_vf_face_steps(w, h, sw, sh, seg_cx, bounds) if seg_cx
                  else None) or static_vf
            if vf:
                where = f"{cx:.2f}" if cx is not None else "n/a → centered"
                track = " (re-centered per shot)" if seg_cx else ""
                console.print(f"  [dim]reframe: 1 face → punch-in{track} "
                              f"(center {where})[/]")
    if vf is None:
        vf = _vf_vertical(w, h, bg)
    # teaser reuses a STATIC reframe (its local timeline would confuse step-x)
    reframe_vf = static_vf or _vf_vertical(w, h, bg)
    if e.get("motion_zoom", True) or punches or cuts:
        vf += "," + _motion(w, h, render_dur,
                            float(e.get("motion_amount", 0.10)), punches, cuts)

    # 1b2. cinematic grade: a touch of contrast + saturation + vignette makes
    #      flat interview footage pop on a phone screen.
    if e.get("grade", True):
        vf += ",eq=contrast=1.06:saturation=1.22,vignette=angle=PI/5.5"

    # 1c. flash accents at the planner's topic-shift transitions (hard-cut feel)
    trans_times = sorted(float(t.get("time", 0)) * tscale
                         for t in plan.get("transitions", []) if t)
    if e.get("flash_transitions", True) and trans_times:
        fl = _flashes(trans_times, render_dur)
        if fl:
            vf += "," + fl

    # 1d. b-roll stills (Pexels) at the planner's suggested moments — each fades
    #     in over the footage for ~2.6s with the voice continuing underneath.
    #     Overlays need a labeled graph, so collect them; captions go on TOP later.
    inputs = ["-i", render_src]
    idx = 1
    vparts, vlabel = [f"[0:v]{vf}[v0]"], "v0"
    if e.get("broll", True) and broll.available():
        bh = int(h * 0.62) // 2 * 2
        used = 0
        for item in plan.get("broll", []):
            if used >= int(e.get("max_broll", 2)):
                break
            t = float(item.get("time", 0)) * tscale
            if not 2.5 < t < render_dur - 3.0:      # never cover hook or ending
                continue
            suggestion = item.get("suggestion", "")
            vid = broll.fetch_video(suggestion) if e.get("broll_video", True) else None
            img = None if vid else broll.fetch(suggestion)
            if not vid and not img:
                continue
            if vid:                              # motion b-roll (premium look)
                inputs += ["-ss", "0", "-t", "3.2", "-an", "-i", str(vid)]
            else:                                # still photo fallback
                inputs += ["-loop", "1", "-t", "3.2", "-i", str(img)]
            vparts.append(
                f"[{idx}:v]scale={w}:{bh}:force_original_aspect_ratio=increase,"
                f"crop={w}:{bh},format=yuva420p,"
                f"fade=t=in:st=0:d=0.3:alpha=1,fade=t=out:st=2.2:d=0.4:alpha=1,"
                f"setpts=PTS+{t:.2f}/TB[b{used}]")
            vparts.append(
                f"[{vlabel}][b{used}]overlay=0:(H-h)/2:"
                f"enable='between(t\\,{t:.2f}\\,{t + 2.6:.2f})'[v{used + 1}]")
            vlabel = f"v{used + 1}"
            idx += 1
            used += 1
        if used:
            console.print(f"  [dim]b-roll: {used} image(s) overlaid[/]")

    # 1e. meme/reaction inserts at emotional peaks (burst-sequence style) —
    #     from the local pack in assets/memes (drop your favorites there).
    if e.get("memes", True):
        mdir = ROOT / e.get("memes_dir", "assets/memes")
        mh = int(h * 0.52) // 2 * 2
        mused = 0
        for item in plan.get("memes", []):
            if mused >= 2:
                break
            t = float(item.get("time", 0)) * tscale
            if not 2.5 < t < render_dur - 3.0:
                continue
            m = _resolve_meme(item.get("emotion", ""), mdir)
            if not m:
                continue
            mdur = 2.2
            if m.suffix.lower() == ".gif":
                inputs += ["-ignore_loop", "0", "-t", f"{mdur + 0.6}", "-i", str(m)]
            else:
                inputs += ["-ss", "0", "-t", f"{mdur + 0.6}", "-i", str(m)]
            j = f"m{mused}"
            vparts.append(
                f"[{idx}:v]scale={w}:{mh}:force_original_aspect_ratio=increase,"
                f"crop={w}:{mh},format=yuva420p,"
                f"fade=t=in:st=0:d=0.15:alpha=1,fade=t=out:st={mdur - 0.25}:d=0.25:alpha=1,"
                f"setpts=PTS+{t:.2f}/TB[{j}]")
            vparts.append(
                f"[{vlabel}][{j}]overlay=0:(H-h)/2:"
                f"enable='between(t\\,{t:.2f}\\,{t + mdur:.2f})'[v{100 + mused}]")
            vlabel = f"v{100 + mused}"
            idx += 1
            mused += 1
        if mused:
            console.print(f"  [dim]memes: {mused} reaction insert(s)[/]")

    # 2. captions overlay (.ass burned in), with skill-chosen emphasis words —
    #    applied AFTER b-roll so text always stays on top.
    post = ""
    if e.get("captions", {}).get("enabled", True):
        ass_path = WORK / f"clip_{clip['id']}.ass"
        captions.write_ass(ass_path, cap_words, cap_start, cap_end,
                           e.get("captions", {}), res=(w, h),
                           emphasis_words=plan.get("emphasis_words", []))
        ass_escaped = str(ass_path).replace("\\", "/").replace(":", "\\:")
        post += f"subtitles='{ass_escaped}'"

    # 3. on-screen hook for first ~2s (AI-written by the planner), wrapped so it
    #    always fits the frame instead of spilling off both edges.
    if e.get("add_intro_hook", True):
        hook = _drawtext_block(plan["hook_text"], size=64, y_top=200,
                               enable="lt(t,2.2)")
        post = f"{post},{hook}" if post else hook

    # 3b. end CTA — the follow ask, on screen for the last ~2s
    if e.get("cta", True) and render_dur > 12:
        cta = _drawtext(e.get("cta_text", "FOLLOW FOR MORE"), size=58,
                        y=str(h - 420), enable=f"gt(t,{render_dur - 2.2:.2f})")
        post = f"{post},{cta}" if post else cta

    # 3c. retention progress bar along the bottom (dark track + white fill)
    if e.get("progress_bar", True):
        bar = (f"drawbox=x=0:y=ih-14:w=iw:h=14:color=black@0.45:t=fill,"
               f"drawbox=x=0:y=ih-14:w='iw*t/{render_dur:.2f}':h=14:"
               f"color=white@0.9:t=fill")
        post = f"{post},{bar}" if post else bar
    if post:
        vparts.append(f"[{vlabel}]{post}[v]")
    else:
        vparts[-1] = vparts[-1].replace(f"[{vlabel}]", "[v]", 1) \
            if len(vparts) > 1 else f"[0:v]{vf}[v]"

    # 4. unified audio: voice + (optional) mood-matched ducked music + SFX cues
    afmt = "aformat=sample_rates=44100:channel_layouts=stereo"
    music = _pick_music(plan.get("music_mood", ""))
    music_vol = e.get("music_volume", 0.12)
    parts = [f"[0:a]{afmt}[a0]"]
    mix = ["[a0]"]

    if music and music_vol > 0:
        inputs += ["-stream_loop", "-1", "-i", str(music)]
        parts.append(f"[{idx}:a]volume={music_vol},{afmt}[am]")
        mix.append("[am]"); idx += 1

    if e.get("mix_sfx", True):
        sfx_dir = ROOT / e.get("sfx_dir", "assets/sfx")
        sfx_vol = e.get("sfx_volume", 0.5)
        # planner cue times are pre-trim (scale them); camera-cut whooshes are
        # already in render time (don't) — quieter so they read as texture.
        cue_list = [(float(c.get("time", 0)) * tscale, c.get("type", ""), sfx_vol)
                    for c in plan.get("sfx_cues", [])]
        cue_list += [(t, "swoosh", sfx_vol * 0.45) for t in cuts]
        for i, (t, kind, vol) in enumerate(cue_list):
            f = _resolve_sfx(kind, sfx_dir)
            if not f:
                continue
            t = max(0.0, min(t, render_dur - 0.05))
            ms = int(t * 1000)
            inputs += ["-i", str(f)]
            parts.append(f"[{idx}:a]adelay={ms}|{ms},volume={vol:.2f},{afmt}[s{i}]")
            mix.append(f"[s{i}]"); idx += 1

    vgraph = ";".join(vparts)
    # broadcast loudness (-14 LUFS = what YouTube normalizes to) so every clip
    # sounds consistent regardless of how quiet the source podcast was
    ln = ",loudnorm=I=-14:TP=-1.5:LRA=11" if e.get("loudnorm", True) else ""
    if len(mix) == 1:                       # voice only — no music/sfx to mix
        filter_complex = vgraph + f";[0:a]{afmt}{ln}[a]"
    else:
        filter_complex = (vgraph + ";" + ";".join(parts) + ";" + "".join(mix)
                          + f"amix=inputs={len(mix)}:normalize=0:duration=first"
                          + f"{ln}[a]")
    amaps = ["-map", "[v]", "-map", "[a]"]

    cmd = (["ffmpeg", "-y", "-ss", f"{render_start}", "-t", f"{render_dur}"]
           + inputs + ["-filter_complex", filter_complex] + amaps
           + ["-t", f"{render_dur}", "-c:v", "libx264", "-preset", "medium", "-crf", "20",
              "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p", str(out)])

    console.print(f"[bold magenta]EDITOR[/] rendering clip {clip['id']} "
                  f"({render_dur:.0f}s) → {out.name}")
    subprocess.run(cmd, check=True, capture_output=True)

    # 5. cold-open teaser: AI narrator over a flash-preview of the payoff,
    #    stitched in front of the main clip.
    total_dur = render_dur
    if e.get("teaser", True) and e.get("voiceover", True):
        teaser = _teaser_pass(render_src, clip["id"], reframe_vf, plan,
                              tscale, render_dur, w, h)
        if teaser:
            tdur = _probe_duration(teaser)
            joined = WORK / f"joined_{clip['id']}.mp4"
            try:
                subprocess.run(
                    ["ffmpeg", "-y", "-i", str(teaser), "-i", str(out),
                     "-filter_complex",
                     "[0:v][0:a][1:v][1:a]concat=n=2:v=1:a=1[v][a]",
                     "-map", "[v]", "-map", "[a]",
                     "-c:v", "libx264", "-preset", "medium", "-crf", "20",
                     "-c:a", "aac", "-b:a", "192k", "-pix_fmt", "yuv420p",
                     str(joined)],
                    check=True, capture_output=True)
                joined.replace(out)
                total_dur = render_dur + tdur
                console.print(f"  [dim]teaser: {tdur:.1f}s narrator cold-open "
                              f"('{plan.get('narrator_intro', '')[:40]}')[/]")
            except subprocess.CalledProcessError as ex:
                console.print(f"  [yellow]teaser join skipped:[/] "
                              f"{ex.stderr.decode(errors='ignore')[-200:]}")

    if e.get("qa", True):
        _qa_render(out, total_dur, clip["id"])
    if e.get("make_cover", True):
        _make_cover(clip, plan, source, start, dur, w, h)
    return out


def _probe_duration(path: Path) -> float:
    p = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                        "format=duration", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True)
    return float(p.stdout.strip() or 0)


def _qa_render(out: Path, expect_dur: float, clip_id) -> None:
    """Self-check the render before it can ever be posted: duration sane and
    audio actually audible (a silent/broken clip is worse than no clip)."""
    problems = []
    try:
        p = subprocess.run(
            ["ffprobe", "-v", "quiet", "-show_entries", "format=duration",
             "-of", "csv=p=0", str(out)], capture_output=True, text=True)
        got = float(p.stdout.strip() or 0)
        if abs(got - expect_dur) > 1.5:
            problems.append(f"duration {got:.1f}s ≠ expected {expect_dur:.1f}s")
        v = subprocess.run(
            ["ffmpeg", "-i", str(out), "-af", "volumedetect", "-f", "null", "-"],
            capture_output=True, text=True)
        for line in v.stderr.splitlines():
            if "mean_volume" in line:
                mean = float(line.split("mean_volume:")[1].split("dB")[0])
                if mean < -45:
                    problems.append(f"audio nearly silent ({mean:.0f} dB)")
    except Exception:  # noqa: BLE001 - QA must never block rendering itself
        return
    if problems:
        msg = f"clip {clip_id}: " + "; ".join(problems)
        console.print(f"  [red]QA FAILED — {msg}[/]")
        notify.notify("Render QA failed", msg)
    else:
        console.print("  [dim]QA: duration + audio OK[/]")


def _make_cover(clip, plan, source, start, dur, w, h):
    """Export a thumbnail/cover: an expressive frame + huge cover text."""
    cover = OUT_DIR / f"clip_{clip['id']}_cover.jpg"
    grab = start + min(dur * 0.3, max(dur - 0.2, 0))   # ~30% in, avoid the cold open
    text = (plan.get("cover_text") or clip["title"]).upper()
    vf = _vf_vertical(w, h, cfg.get("editor.background", "blur"))
    vf += "," + _drawtext_block(text, size=100, y_top=int(h * 0.08),
                                enable=None, max_chars=13, max_lines=3)
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-ss", f"{grab}", "-i", source["video_path"],
             "-vf", vf, "-frames:v", "1", "-q:v", "3", str(cover)],
            check=True, capture_output=True,
        )
        console.print(f"  [dim]cover → {cover.name}[/]")
    except subprocess.CalledProcessError as ex:
        console.print(f"  [yellow]cover skipped:[/] "
                      f"{ex.stderr.decode(errors='ignore')[-200:]}")


def edit_all() -> int:
    pending = db.clips_by_status("approved")
    if not pending:
        console.print("[yellow]No approved clips. Run review first.[/]")
        return 0
    n = 0
    for clip in pending:
        try:
            out = edit_clip(clip)
            db.set_clip_status(clip["id"], "edited", rendered_path=out)
            n += 1
        except subprocess.CalledProcessError as ex:
            console.print(f"[red]ffmpeg failed on clip {clip['id']}[/]: "
                          f"{ex.stderr.decode(errors='ignore')[-500:]}")
    console.print(f"[green]✓ {n} clips rendered to output/[/]")
    return n
