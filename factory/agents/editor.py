"""Agent 2 — EDITOR.

Cuts each approved clip with ffmpeg, reframes to vertical 9:16, burns in
karaoke captions, and mixes background music.
"""
from __future__ import annotations

import json
import random
import re
import subprocess
from dataclasses import dataclass
from pathlib import Path

from rich.console import Console

from .. import db, notify
from ..config import ROOT, WORK, cfg
from ..utils import (broll, captions, design_scenes, media, remotion_intro,
                     trimmer, voice)
from ..utils import faces as faces_util
from . import planner

console = Console()
OUT_DIR = ROOT / "output"
OUT_DIR.mkdir(exist_ok=True)


@dataclass
class EditResult:
    """Return value of the headless edit_clip_range() render API."""
    path: str          # absolute path to the finished MP4 on disk
    url: str           # download URL (filled in by the service layer)
    plan: dict         # creative plan / notes for the buyer agent


# Named look presets exposed to programmatic callers. Each preset fully specifies
# the toggles it cares about so repeated service calls never leak state between
# styles. "bold-captions" is the default (everything on).
_STYLE_PRESETS = {
    "bold-captions": {"reframe": "smart", "punch_zoom": True, "camera_cuts": True,
                      "flash_transitions": True, "memes": True, "broll": True,
                      "teaser": True, "motion_zoom": True},
    "minimal": {"reframe": "smart", "punch_zoom": False, "camera_cuts": False,
                "flash_transitions": False, "memes": False, "broll": False,
                "teaser": False, "motion_zoom": True},
    "podcast-frame": {"reframe": "blur", "punch_zoom": False, "camera_cuts": False,
                      "flash_transitions": False, "memes": False, "broll": False,
                      "teaser": False, "motion_zoom": False},
}


def _apply_style(style: str) -> None:
    """Mutate the in-process editor config to match a named style preset.
    NOTE: cfg is process-global, so callers running multiple styles at once must
    serialize edit jobs (a host service should take a lock around each edit)."""
    preset = _STYLE_PRESETS.get(style) or _STYLE_PRESETS["bold-captions"]
    cfg.editor.update(preset)


def edit_clip_range(source_url: str, start_s: float, end_s: float,
                    style: str = "bold-captions") -> EditResult:
    """Stateless single-clip render for headless/programmatic callers.

    Downloads the source, reuses the same editor pipeline as the interactive flow
    for one arbitrary [start_s, end_s] range, and returns the finished MP4 path
    plus the creative plan. The service layer turns `path` into a signed URL.
    """
    from ..utils import media

    _apply_style(style)
    video, title, channel = media.download(source_url)
    audio = media.extract_audio(video)
    transcript = media.transcribe(audio)
    source_id = db.upsert_source(source_url, title, video, transcript, channel=channel)
    clip_id = db.add_clip(source_id, float(start_s), float(end_s),
                          (title or "clip")[:90], "a2a edit-clip", 0.0, "", [])
    clip = db.clip_by_id(clip_id)
    out = edit_clip(clip)
    db.set_clip_status(clip_id, "edited", rendered_path=out)

    notes_path = OUT_DIR / f"clip_{clip_id}.notes.md"
    plan = {"notes": notes_path.read_text(encoding="utf-8")} if notes_path.exists() else {}
    plan["style"] = style
    return EditResult(path=str(out), url="", plan=plan)


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
    # money sounds FIRST — most specific wins ('coin drop' must hit coin, not
    # the generic 'drop'->impact rule below; dict order is the match order)
    "cash": "cash", "register": "cash", "cha-ching": "cash", "chaching": "cash",
    "kaching": "cash", "ka-ching": "cash", "money": "cash", "dollar": "cash",
    "coin": "coin", "coins": "coin", "clink": "coin", "tick": "pop",
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


def _anchor_time(anchor: str, cap_words: list[dict]) -> float | None:
    """When is `anchor` (a word/short phrase the AI quoted from the transcript)
    actually spoken? Returns its render-time start so an SFX lands on the moment
    it refers to instead of a guessed timestamp. None if the words aren't found."""
    toks = [captions._norm(t) for t in (anchor or "").split() if captions._norm(t)]
    if not toks or not cap_words:
        return None
    norm = [(captions._norm(w["word"]), w) for w in cap_words]
    # exact contiguous phrase match first
    for i in range(len(norm)):
        if all(i + k < len(norm) and norm[i + k][0] == toks[k]
               for k in range(len(toks))):
            return float(norm[i][1]["start"])
    # fall back to the most distinctive single token anywhere in the clip
    for tok in sorted(toks, key=len, reverse=True):
        for n, w in norm:
            if n == tok:
                return float(w["start"])
    return None


def _dedupe_sfx(primary, secondary, render_dur: float,
                gap: float = 0.7, cap: int = 6):
    """Stop sounds piling up into noise: drop any cue over the hook (first ~1s)
    or the tail, thin out cues landing within `gap`s of one already kept, and cap
    the total. `primary` (anchored planner cues) claim their slots before the
    `secondary` camera-cut texture fills whatever gaps remain."""
    kept: list[tuple] = []

    def add(cues):
        for t, kind, vol in sorted(cues, key=lambda x: x[0]):
            if len(kept) >= cap:
                break
            if t < 1.0 or t > render_dur - 0.4:
                continue
            if any(abs(t - kt) < gap for kt, _, _ in kept):
                continue
            kept.append((t, kind, vol))

    add(primary)
    add(secondary)
    return sorted(kept, key=lambda x: x[0])


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
    except Exception:  # noqa: BLE001 - no face detection → reframe falls back to fit
        return None, 0, 0, 0, 0.0
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        return None, 0, 0, 0, 0.0
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) or 0
    sw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    sh = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)) or 0
    centers, counts, fracs = [], [], []
    idxs = [int(total * i / (samples + 1)) for i in range(1, samples + 1)] if total else []
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, frame = cap.read()
        if not ok:
            continue
        detected = faces_util.detect(frame)          # YuNet (Haar fallback)
        counts.append(len(detected))
        if detected and sw:
            x, _, fw, fh = max(detected, key=lambda r: r[2] * r[3])  # largest
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
# Shot-cycle zoom levels. RETUNED DOWN 2026-07-18 (were 0.14/0.07): these were
# picked by eye while the zoom was silently frozen at 1.0x, so they had never
# actually been seen. Once live, that much zoom against a single per-shot face
# anchor drifted off a moving subject and left them out of frame entirely.
# Restraint is the point: the viewer should feel the camera, not fight it.
_SHOT_CYCLE = (0.0, 0.07, 0.035, 0.07)

# A small face the detector is CONFIDENT about is a real person filmed wide,
# not furniture. Measured on clip 52: distant faces scored 0.84-0.94 while the
# close-ups scored 0.78, so 0.75 cleanly separates "punch into this" from
# "ignore this". Below it, a small detection is still treated as spurious.
WIDE_SHOT_CONF = 0.75
# Target face size after the punch. Our close-ups sit at ~42% of frame width and
# read well on a phone; wide shots at ~10% do not. 0.24 pulls a distant subject
# to a comfortable mid-shot without the mush of a 4x upscale.
WIDE_SHOT_TARGET = 0.24


def _cut_points(cap_words, cap_start: float, dur: float,
                min_len: float = 2.0, max_len: float = 3.5) -> list[float]:
    """Camera-cut times (clip-local): at sentence ends / speech pauses. This is
    what makes a single static podcast angle feel like a multicam edit.

    BURST-THEN-HOLD rhythm (measured on a 1.35M-view money clip, 2026-07-17):
    the winners cut every 0.8-1.1s through the HOOK (first ~5s: four cuts), then
    HOLD 5-8s shots through the emotional/escalation beats with a continuous
    push-in. A metronome bores; the rhythm must follow the story."""
    cuts, seg_start = [], 0.0
    for i, wd in enumerate(cap_words):
        t = wd["end"] - cap_start
        lo, hi = (0.9, 1.7) if t < 5.5 else (2.2, 5.5)   # burst → hold
        if t - seg_start < lo or t > dur - 1.5:
            continue
        nxt = cap_words[i + 1] if i + 1 < len(cap_words) else None
        gap = (nxt["start"] - wd["end"]) if nxt else 0.0
        sentence_end = wd["word"].strip()[-1:] in ".?!"
        # in the burst window any word gap earns a cut; later only real pauses
        pause = gap > (0.12 if t < 5.5 else 0.45)
        if sentence_end or pause or t - seg_start >= hi:
            cut = t + min(gap / 2, 0.2)          # cut inside the pause
            cuts.append(round(cut, 2))
            seg_start = cut
    return cuts


def _scene_cuts(video_path: str, dur: float, min_gap: float = 1.4) -> list[float]:
    """Timestamps (clip-local sec) where the SOURCE actually changed camera,
    via PySceneDetect. These are REAL visual changes — the strongest cut points
    (the reference's energy comes from cutting on real shots, not just sentence
    ends). Merging these makes our reframe re-center on the new face the instant
    the source cuts to a different host. Empty on any failure — purely additive."""
    try:
        from scenedetect import detect, ContentDetector
        scenes = detect(str(video_path), ContentDetector(threshold=27))
    except Exception:  # noqa: BLE001 - scene detect is a bonus, never block a render
        return []
    cuts, last = [], -min_gap
    for start, _end in scenes[1:]:              # skip the first scene (starts at 0)
        t = start.get_seconds()
        if 1.0 < t < dur - 1.0 and t - last >= min_gap:
            cuts.append(round(t, 2))
            last = t
    return cuts


def _merge_cuts(*lists, min_gap: float = 1.4) -> list[float]:
    """Union of cut lists, sorted, thinned so none are closer than min_gap."""
    out, last = [], -min_gap
    for t in sorted({round(x, 2) for lst in lists for x in lst}):
        if t - last >= min_gap:
            out.append(t)
            last = t
    return out


def _wide_boost(fw_frac: float) -> float:
    """Extra zoom needed to pull a distant subject up to a comfortable size.

    Zoom `z` crops to (1-z) of the frame, so magnification is 1/(1-z) and
    z = 1 - actual/target. Capped at 0.55 (about 2.2x): beyond that we are
    upscaling a small region of a 1080p source and the softness costs more than
    the framing gains.
    """
    if fw_frac <= 0 or fw_frac >= WIDE_SHOT_TARGET:
        return 0.0
    return min(0.55, 1.0 - (fw_frac / WIDE_SHOT_TARGET))


def _motion(w: int, h: int, dur: float, amount: float, punches=(),
            cuts=(), wide=None) -> str:
    """The whole 'camera' in one crop: slow Ken Burns push (amount), quick zoom
    PUNCHES on key words, and hard zoom CUTS at sentence boundaries (fake
    multicam: wide ↔ close-up). Applied BEFORE captions so text stays sharp.

    `wide` is an optional per-segment [(boost, cy), ...] aligned to the segments
    formed by `cuts`. It PUNCHES INTO WIDE SHOTS: when the source cuts to an
    establishing wide, the subject would otherwise stay a speck (measured: 12%
    of frame width vs 42% in our close-ups), so we zoom in on them and anchor
    the crop on their face height instead of the frame's dead centre.
    """
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
            # A wide-shot segment's framing is already decided by its boost —
            # stacking the shot cycle and the push-in on top of it overshot to
            # 3.5x and threw the subject clean out of frame. Boost wins alone.
            if _seg_boost(wide, i) > 0:
                continue
            level = _SHOT_CYCLE[i % len(_SHOT_CYCLE)]
            if level:
                expr += (f"+{level}*gte(ld(0)\\,{bounds[i]:.2f})"
                         f"*lt(ld(0)\\,{bounds[i + 1]:.2f})")
            # WITHIN-HOLD PUSH-IN (measured on a 1.35M-view clip: long emotional
            # holds carry a relentless ~3%/s creep toward the face — a static
            # hold reads dead). Only on shots >3s; capped by the global 0.45.
            seg_len = min(bounds[i + 1], d) - bounds[i]
            if seg_len > 3.0 and _seg_boost(wide, i) <= 0:
                # Gentler creep with a hard 0.07 ceiling (was 0.03/s to 0.18):
                # the anchor is one fixed point per shot, so a long push-in
                # accumulates error against a subject who leans or shifts, and
                # at the old strength it walked clean off them.
                expr += (f"+min(0.07\\,0.012*(ld(0)-{bounds[i]:.2f}))"
                         f"*gte(ld(0)\\,{bounds[i]:.2f})"
                         f"*lt(ld(0)\\,{bounds[i + 1]:.2f})")
        for t in cuts:                       # snap transient AT each cut so the
            if 0.3 < t < d - 0.3:            # shot change HITS, not drifts
                expr += f"+0.06*max(0\\,1-abs(ld(0)-{t:.2f})/0.12)"
        # WIDE-SHOT PUNCH: extra zoom only on the segments that need it.
        for i in range(len(bounds) - 1):
            boost = _seg_boost(wide, i)
            if boost > 0:
                expr += (f"+{boost:.3f}*gte(ld(0)\\,{bounds[i]:.2f})"
                         f"*lt(ld(0)\\,{bounds[i + 1]:.2f})")
        cap = max(0.26, min(0.58, max((_seg_boost(wide, i)
                                       for i in range(len(bounds) - 1)),
                                      default=0.0)))
        z = f"st(0\\,if(isnan(t)\\,0\\,t));min({cap}\\,{expr})"
    ypos = _wide_y_expr(wide, list(cuts), d)
    # ZOOM MUST LIVE ON `scale`, NOT `crop` (bug found 2026-07-18).
    # ffmpeg evaluates crop's w/h ONCE at filter init, where t is NaN — so the
    # old `crop=w='iw-iw*(z)'` form pinned the zoom at its t=0 value (z=0) and
    # every zoom this function produced was silently DEAD: no Ken Burns push, no
    # punch on emphasis words, no wide/close shot cycle, no push-in on holds.
    # (crop's x/y DO evaluate per frame, which is why per-shot re-centering
    # always worked and hid the problem.) `scale` with eval=frame re-evaluates
    # every frame, so we magnify by 1/(1-z) there and crop a fixed window out of
    # the enlarged frame — the crop x/y stay animated for the wide-shot anchor.
    mag = f"1/(1-({z}))"
    return (f"scale=w='max({w}\\,trunc(iw*({mag})/2)*2)':"
            f"h='max({h}\\,trunc(ih*({mag})/2)*2)':eval=frame,"
            f"crop={w}:{h}:x='(iw-ow)/2':y='{ypos}'")


def _seg_boost(wide, i: int) -> float:
    """Zoom boost for segment i, or 0 when there is none."""
    try:
        return float(wide[i][0])
    except (TypeError, IndexError, KeyError, ValueError):
        return 0.0


# Where the face should sit in the finished frame. Dead centre reads oddly and
# wastes headroom; a little above centre is how the shot is actually composed.
FACE_Y_TARGET = 0.42


def _wide_y_expr(wide, cuts: list, d: float) -> str:
    """Vertical crop position: rides the subject's face on EVERY zoomed segment.

    This has to apply to all zoom, not just the wide-shot punch. Faces in this
    footage sit around 27-43% of frame height, so any centre-anchored zoom
    crops the head off — which is exactly what appeared the moment the zoom
    started working (it had been frozen at 1.0x, so nothing was ever cropped
    and the bad anchoring stayed invisible).

    Segments with no zoom are unaffected: their crop fills the frame, so
    oh == ih and the clip() pins y to 0 regardless.
    """
    if not wide:
        return "(ih-oh)/2"
    bounds = [0.0] + list(cuts) + [d + 1]
    terms = []
    for i in range(len(bounds) - 1):
        try:
            cy = float(wide[i][1])
        except (TypeError, IndexError, ValueError):
            continue
        if not (0.0 < cy < 1.0):
            continue
        # put the face at FACE_Y_TARGET of the output, as a delta from centre
        terms.append(f"+(ih*{cy:.3f}-oh*{FACE_Y_TARGET}-(ih-oh)/2)"
                     f"*gte(ld(1)\\,{bounds[i]:.2f})"
                     f"*lt(ld(1)\\,{bounds[i + 1]:.2f})")
    if not terms:
        return "(ih-oh)/2"
    return (f"st(1\\,if(isnan(t)\\,0\\,t));"
            f"clip((ih-oh)/2{''.join(terms)}\\,0\\,ih-oh)")


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
                      two_face: bool = False,
                      shots_out: list | None = None) -> list[float] | None:
    """Face x-position (0..1) per camera segment. `bounds` are segment edges
    in seconds. two_face=True → alternate the LEFT and RIGHT face positions
    per segment (a 2-shot becomes host-cam / guest-cam cuts).

    Pass `shots_out` to also receive per-segment geometry (cx, cy, face width
    as a fraction of frame, detector score) — that is what drives the wide-shot
    punch-in."""
    try:
        import cv2
    except Exception:  # noqa: BLE001 - no opencv → per-shot tracking disabled
        return None
    cap = cv2.VideoCapture(str(video_path))
    if not cap.isOpened():
        return None
    fps = cap.get(cv2.CAP_PROP_FPS) or 30.0
    sw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    sh = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)) or 0
    if not sw or not sh:
        cap.release()
        return None
    seg_faces: list[list[float]] = []        # ALL face centers seen per segment
    seg_cx: list[float | None] = []          # largest face per segment
    seg_w: list[float] = []                  # its width (px) — spurious filter
    seg_cy: list[float | None] = []          # its vertical center (0-1)
    seg_score: list[float] = []              # detector confidence (real vs lamp)
    seg_asd: list[float | None] = []         # ACTIVE SPEAKER (mouth motion)
    all_cx: list[float] = []
    for i in range(len(bounds) - 1):
        found, here, samples = [], [], []
        for frac in (0.25, 0.5, 0.75):
            tm = bounds[i] + (bounds[i + 1] - bounds[i]) * frac
            cap.set(cv2.CAP_PROP_POS_FRAMES, int(tm * fps))
            ok, frame = cap.read()
            if not ok:
                continue
            scored = faces_util.detect_scored(frame)   # YuNet (Haar fallback)
            faces = [(x, y, fw, fh) for x, y, fw, fh, _ in scored]
            here.extend((x + fw / 2) / sw for x, _, fw, _ in faces)
            if faces:
                samples.append((cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY), faces))
                x, y, fw, fh, sc = max(scored, key=lambda r: r[2] * r[3])
                found.append(((x + fw / 2) / sw, fw, (y + fh / 2) / sh, sc))
        all_cx.extend(here)
        seg_faces.append(sorted(here))
        # who is TALKING in this shot? lips-motion beats size/rhythm guessing
        seg_asd.append(faces_util.active_speaker_cx(samples, sw))
        found.sort()
        if found:
            c, fw, cy, sc = found[len(found) // 2]
            seg_cx.append(seg_asd[-1] if seg_asd[-1] is not None else c)
            seg_w.append(fw)
            seg_cy.append(cy)
            seg_score.append(sc)
        else:
            seg_cx.append(None)
            seg_w.append(0.0)
            seg_cy.append(None)
            seg_score.append(0.0)
    cap.release()
    # Spurious-detection filter (the 'crop parked on a lamp' bug, 2026-07-17).
    # SIZE ALONE WAS THE WRONG TEST (2026-07-18): a person filmed in a genuine
    # wide shot has a small face too, and discarding them left the subject a
    # 12%-of-frame speck with no punch-in — measured on clip 52 at 20s. Use the
    # detector's CONFIDENCE to tell the two apart: on that shot the distant
    # faces scored 0.84-0.94, HIGHER than the close-ups (0.78), so they are
    # unmistakably real. Only a small face the detector is also unsure about is
    # treated as furniture.
    real = sorted(wd for wd in seg_w if wd > 0)
    if real:
        med_w = real[len(real) // 2]
        for i, wd in enumerate(seg_w):
            if wd and wd < 0.45 * med_w and seg_score[i] < WIDE_SHOT_CONF:
                seg_cx[i] = None
                seg_cy[i] = None
    # Hand the per-segment geometry back so the caller can PUNCH INTO the wide
    # shots it just kept (a small real face is exactly what needs extra zoom).
    if shots_out is not None:
        shots_out.clear()
        shots_out.extend(
            {"cx": seg_cx[i], "cy": seg_cy[i],
             "fw_frac": (seg_w[i] / sw) if sw else 0.0,
             "score": seg_score[i]}
            for i in range(len(seg_cx)))
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
        # Alternate host-cam/guest-cam, but the crop must land on a face that is
        # REALLY THERE in that segment. The source itself intercuts wide 2-shots
        # with solo close-ups; blindly applying the global left/right position to
        # a close-up put the crop beside the speaker — the person-cut-in-half bug
        # (user report, 2026-07-11). Per segment: snap the alternation target to
        # the nearest face detected IN that segment; no detection → hold the
        # previous crop (a camera hold), never a global guess.
        out, last = [], None
        for i, faces in enumerate(seg_faces):
            target = lx if i % 2 == 0 else rx
            if seg_asd[i] is not None:       # lips moving = the shot belongs to
                last = seg_asd[i]            # the SPEAKER, not the rhythm guess
            elif faces:
                last = min(faces, key=lambda c: abs(c - target))
            elif last is None:
                last = target                # leading no-detection segments only
            out.append(last)
        return out
    # single face: fill gaps by holding the previous segment's position (camera
    # holds). Seed from the first real detection — the global median could sit
    # on the OTHER person in a two-person frame.
    seed = next((c for c in seg_cx if c is not None), med)
    out, last = [], seed
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

    # 0a. animated intro card (Remotion) — rendered UP FRONT so we only skip the
    #     static ffmpeg hook if the card actually succeeds. If Chromium/Node
    #     fails (e.g. in a bare scheduled-task session), intro_card stays None
    #     and the clip keeps its normal static hook — no silent hook loss.
    intro_card_path = None
    if e.get("intro_card", False) and remotion_intro.available():
        _accent = plan.get("emphasis_words", [])[:2] or plan["hook_text"].split()[-1:]
        intro_card_path = remotion_intro.render_intro(
            plan["hook_text"], _accent, WORK / f"intro_{clip['id']}.mov")
        if intro_card_path:
            console.print("  [dim]intro: animated hook card ready[/]")
        else:
            console.print("  [yellow]intro card failed → keeping static hook[/]")

    # 0a2. CAPTION REFINEMENT — re-transcribe just this clip's window with a
    # stronger model. Captions are burned in permanently, so a mangled proper
    # noun ships forever ("Kop end" went out as "Coppen"), but running the big
    # model over the whole episode would cost ~99 minutes and blow the produce
    # window. Over one 40s window it costs seconds. Done BEFORE the trim so the
    # trim and the captions both work from the better words.
    if cfg.get("finder.refine_clips", False):
        refined = media.refine_words(source["video_path"], start, end)
        if refined:
            outside = [w for w in words
                       if w["end"] <= start or w["start"] >= end]
            words = sorted(outside + refined, key=lambda w: w["start"])
            console.print(f"  [dim]captions: refined {len(refined)} words with "
                          f"{cfg.get('finder.refine_model', 'medium')}[/]")

    # 0b. trim pass — cut filler words + dead air, then style the tightened clip
    render_src, render_start, render_dur = source["video_path"], start, dur
    cap_words, cap_start, cap_end = words, start, end
    trim = trimmer.compute(words, start, end, e.get("trim", {}))
    if trim:
        s = trim["stats"]
        console.print(f"  [dim]trim: {s['orig']:.1f}s → {s['trimmed']:.1f}s "
                      f"(-{s['removed']:.1f}s, {s['segments']} kept segments)[/]")
        trimmed = str(_trim_pass(source["video_path"], clip["id"], start, dur, trim))
        # The trim pass is a best-effort optimization; it occasionally emits a
        # broken/streamless file (a 262-byte mp4 with no audio), which then made
        # the main render fail with "[a] matches no streams" and LOSE the clip.
        # Validate it: only adopt the trim if it has BOTH a video and an audio
        # stream — otherwise render the raw range untrimmed.
        if _has_av_streams(trimmed):
            render_src = trimmed
            render_start, render_dur = 0.0, trim["new_dur"]
            cap_words, cap_start, cap_end = trim["new_words"], 0.0, trim["new_dur"]
        else:
            console.print("  [yellow]trim output was invalid (no A/V streams) — "
                          "rendering the untrimmed clip instead[/]")

    # timing helper: plan times are pre-trim; remap them onto the trimmed timeline
    tscale = (render_dur / dur) if dur > 0 else 1.0

    # 0c. camera-cut plan first — the reframe below re-centers per shot
    #     (seg_cx is seeded here so the craft-spec record at the end of the
    #      render is always bound, whichever reframe branch ran)
    punches, cuts, seg_cx = [], [], []
    if e.get("punch_zoom", True):
        punches = _punch_times(cap_words, cap_start,
                               plan.get("emphasis_words", []), render_dur)
    if e.get("camera_cuts", True):
        cuts = _cut_points(cap_words, cap_start, render_dur)
        if e.get("scene_cuts", True):
            scene = _scene_cuts(render_src, render_dur)
            if scene:
                cuts = _merge_cuts(cuts, scene)   # align to real source cuts too
                console.print(f"  [dim]scene-detect: {len(scene)} real source cut(s) merged[/]")
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
    shots: list[dict] = []      # per-segment face geometry → wide-shot punch
    reframe_ratio = 1.0         # source→output face magnification of the reframe
    if mode in ("smart", "face"):
        cx, sw, sh, nfaces, face_frac = _face_center(render_src)
        static_vf = _vf_face(w, h, sw, sh, cx)
        # The 9:16 reframe itself already magnifies: it shows only w/scaled_w of
        # the source width, so a face measured against the SOURCE appears this
        # many times bigger in the OUTPUT. The wide-shot punch must reason in
        # output terms or it "fixes" every shot (a 16:9 source magnifies ~3.2x).
        if sh:
            reframe_ratio = max(1.0, (h * sw / sh) / w)
        if mode == "smart" and cx is not None and face_frac < 0.02 and nfaces < 2:
            # ONE tiny face = screen-share / PiP layout (host reacting to a
            # video): punching in would crop a random slice of the collage.
            # Multiple tiny faces is NOT a collage — it's a wide multi-person
            # podcast shot (The Overlap's 4-man table: faces ~1.5% of frame) and
            # it must be punched, not blur-fitted; that mistake produced the
            # letterboxed 'mess' viewers roasted (2026-07-16).
            vf = _vf_vertical(w, h, bg)
            static_vf = None
            reframe_ratio = 1.0
            console.print(f"  [dim]reframe: single face only {face_frac * 100:.1f}% "
                          f"of frame → screen layout, full-frame fit[/]")
        elif mode == "smart" and nfaces >= 2:
            # ALWAYS punch in on people. The old fallback for multi-face shots was
            # full-frame blur-fit — a tiny letterboxed strip in a blur sandwich —
            # and viewers roasted it in the comments ("what a mess of a clip",
            # "who edited this bs", 2026-07-16) on our two most-served videos.
            # Chain: host/guest alternation → largest-face-per-shot punch →
            # static face punch. Blur-fit survives ONLY for screen-share layouts
            # (handled above via face_frac) or when no face is ever detected.
            seg_cx = (_segment_face_cxs(render_src, bounds, two_face=True,
                                        shots_out=shots)
                      if e.get("speaker_cuts", True) and cuts else None)
            how = "host/guest camera alternation"
            if not seg_cx and cuts:
                seg_cx = _segment_face_cxs(render_src, bounds, shots_out=shots)
                how = "largest-face punch per shot"
            vf = _vf_face_steps(w, h, sw, sh, seg_cx, bounds) if seg_cx else None
            if vf:
                console.print(f"  [dim]reframe: {nfaces} faces → {how} "
                              f"across {len(bounds) - 1} shots[/]")
            elif static_vf:
                vf = static_vf
                console.print(f"  [dim]reframe: {nfaces} faces → static face "
                              f"punch-in (per-shot tracking unavailable)[/]")
            else:
                vf = _vf_vertical(w, h, bg)  # no face found at all: show frame
                static_vf = None
                reframe_ratio = 1.0
                console.print(f"  [dim]reframe: {nfaces} faces → full-frame fit[/]")
        else:
            seg_cx = (_segment_face_cxs(render_src, bounds, shots_out=shots)
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
    # Wide-shot punch: a real but distant subject gets extra zoom anchored on
    # their face, so an establishing wide never leaves them a speck on a phone.
    wide = None
    if e.get("wide_shot_punch", True) and shots:
        wide = [(_wide_boost(sh.get("fw_frac", 0.0) * reframe_ratio)
                 if sh.get("cx") is not None else 0.0, sh.get("cy"))
                for sh in shots]
        n = sum(1 for b, _ in wide if b > 0)
        # Keep `wide` even when nothing needs a punch: it also carries each
        # shot's face height, which anchors EVERY zoom so no head gets cropped.
        anchored = sum(1 for _, cy in wide if cy is not None)
        console.print(f"  [dim]camera anchor: {anchored}/{len(wide)} shots "
                      f"track the face vertically"
                      + (f", punching into {n} distant shot(s)" if n else "") + "[/]")
    if e.get("motion_zoom", True) or punches or cuts:
        vf += "," + _motion(w, h, render_dur,
                            float(e.get("motion_amount", 0.10)), punches, cuts,
                            wide=wide)

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

    # Breather discipline (scene-mapping skill): segments the planner marked
    # 'breather' stay PURE subtitles — no b-roll, no memes, no design scenes.
    # Rest is a retention tool; anything on screen there fights the speaker.
    breathers = [(float(s.get("start", 0)) * tscale, float(s.get("end", 0)) * tscale)
                 for s in plan.get("scene_map", []) if s.get("role") == "breather"]

    def _in_breather(t: float, span: float = 2.6) -> bool:
        return any(bs < t + span and t < be for bs, be in breathers)

    # 1d0. designed scenes (scene_map 'visual' segments) — an AI-designed,
    #      on-brand still animated with a slow zoom, overlaid where the planner
    #      says the viewer NEEDS a visual to understand the line. Falls back to
    #      generic stock b-roll below when generation fails.
    covered: list[float] = []            # visual times already served, so stock
    if design_scenes.available():        # b-roll doesn't double-cover the line
        bh = int(h * 0.62) // 2 * 2
        dused = 0
        for seg in plan.get("scene_map", []):
            if dused >= int(e.get("max_design_scenes", 3)):
                break
            if seg.get("role") != "visual":
                continue
            t = float(seg.get("start", 0)) * tscale
            if not 2.5 < t < render_dur - 3.0:      # never cover hook or ending
                continue
            vid = design_scenes.fetch(seg.get("design_brief", ""), w, bh,
                                      seg.get("overlay_text", ""))
            if not vid:
                continue
            inputs += ["-ss", "0", "-t", "3.2", "-an", "-i", str(vid)]
            vparts.append(
                f"[{idx}:v]format=yuva420p,"
                f"fade=t=in:st=0:d=0.3:alpha=1,fade=t=out:st=2.6:d=0.4:alpha=1,"
                f"setpts=PTS+{t:.2f}/TB[d{dused}]")
            vparts.append(
                f"[{vlabel}][d{dused}]overlay=0:(H-h)/2:"
                f"enable='between(t\\,{t:.2f}\\,{t + 3.0:.2f})'[v{200 + dused}]")
            vlabel = f"v{200 + dused}"
            covered.append(t)
            idx += 1
            dused += 1
        if dused:
            console.print(f"  [dim]design scenes: {dused} branded visual(s) overlaid[/]")

    if e.get("broll", True) and broll.available():
        bh = int(h * 0.62) // 2 * 2
        used = 0
        for item in plan.get("broll", []):
            if used >= int(e.get("max_broll", 2)):
                break
            t = float(item.get("time", 0)) * tscale
            if not 2.5 < t < render_dur - 3.0:      # never cover hook or ending
                continue
            if _in_breather(t):                     # breathers stay captions-only
                continue
            if any(abs(t - c) < 3.5 for c in covered):   # a design scene is
                continue                                  # already on this line
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
            if _in_breather(t, span=2.2):           # breathers stay captions-only
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
    #    always fits the frame. Skipped ONLY when the animated intro card was
    #    successfully pre-rendered (that replaces the static hook).
    if e.get("add_intro_hook", True) and not intro_card_path:
        hook = _drawtext_block(plan["hook_text"], size=64, y_top=200,
                               enable="lt(t,2.2)")
        post = f"{post},{hook}" if post else hook

    # 3b. SUBSCRIBE ask at the PEAK, not the exit. Analytics 2026-07-18: 48,888
    # views -> 6 subscribers (0.012%). The only ask was this end-card, which
    # lands in the last 2s when the viewer is already swiping. Subscribers are
    # the binding YPP constraint, so the ask now fires mid-clip at the emotional
    # peak (just after the last emphasis punch, else ~58% through) while
    # attention is still high — then a short end reinforcement.
    if e.get("cta", True) and render_dur > 12:
        peak = None
        if punches:
            cand = [p for p in punches if 0.35 * render_dur < p < 0.8 * render_dur]
            peak = (cand[-1] if cand else None)
        if peak is None:
            peak = render_dur * 0.58
        peak = min(peak + 0.6, render_dur - 4.0)      # just AFTER the beat lands
        if peak > 3.0:
            mid = _drawtext(e.get("cta_mid_text", "SUBSCRIBE"), size=54,
                            y=str(int(h * 0.11)),
                            enable=f"between(t,{peak:.2f},{peak + 2.0:.2f})")
            post = f"{post},{mid}" if post else mid
        cta = _drawtext(e.get("cta_text", "FOLLOW FOR MORE"), size=58,
                        y=str(h - 420), enable=f"gt(t,{render_dur - 2.2:.2f})")
        post = f"{post},{cta}" if post else cta

    # 3b0. THE TAKEAWAY — the lesson, burned on screen so the value is not just
    # implied. The channel's promise is that you leave knowing something, and a
    # viewer skims; if the lesson only exists in the audio it does not land.
    # Placed in the last third but clear of the closing CTA, and only when the
    # planner actually named one (no filler card on a clip that teaches nothing).
    tk = (plan.get("takeaway") or "").strip()
    if e.get("takeaway_card", True) and tk and render_dur > 10:
        t0 = max(3.0, render_dur * 0.62)
        t1 = min(t0 + 3.2, render_dur - 2.6)
        if t1 > t0 + 1.0:
            card = _drawtext_block(tk.upper(), size=46, y_top=int(h * 0.17),
                                   enable=f"between(t,{t0:.2f},{t1:.2f})",
                                   max_chars=20, max_lines=3)
            post = f"{post},{card}" if post else card

    # 3b1. SERIES BADGE — small, persistent, top of frame. Titles carry the
    # series name but the Shorts feed hides titles behind a tap, so without an
    # in-frame mark a scroller never learns this is a recurring thing. Kept
    # small and high so it never fights the captions (lower third) or the face.
    if cfg.get("series.enabled", False) and cfg.get("series.badge", True):
        sname = (cfg.get("series.name", "") or "").strip()
        if sname:
            badge = _drawtext(sname, size=34, y=str(int(h * 0.045)))
            post = f"{post},{badge}" if post else badge

    # 3b2. the DEBATE QUESTION on screen (comments were 19 per 48,888 views).
    # The Finder already ends every caption with a forced-choice question, but it
    # only lived in the description where nobody reads it. Burning it over the
    # last beats turns a passive watch into a reply.
    if e.get("comment_prompt", True) and render_dur > 12:
        # the planner now returns a dedicated comment_question (required field).
        # Fall back to a question parsed out of the caption, then to a generic
        # ask — only 16% of captions actually carried one, which is why we saw
        # 19 comments per 48,888 views.
        q = (plan.get("comment_question") or "").strip()
        if not q:
            try:
                for part in re.split(r"(?<=\?)\s+", (clip["caption"] or "").strip()):
                    if part.strip().endswith("?") and 12 <= len(part.strip()) <= 60:
                        q = part.strip()
            except (KeyError, IndexError, TypeError):
                q = ""
        if not q:
            q = e.get("comment_prompt_default", "AGREE?")
        if len(q) > 60:
            q = ""
        if q:
            qs = _drawtext_block(q, size=46, y_top=int(h * 0.20), max_chars=22,
                                 max_lines=2,
                                 enable=f"gt(t,{render_dur - 4.5:.2f})")
            post = f"{post},{qs}" if post else qs

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
        # Anchor each planner cue to the render-time of the word it names, so the
        # sound lands on the actual moment — not a guessed timestamp. If the word
        # isn't found, fall back to any (scaled, pre-trim) time hint, else drop.
        planner_cues = []
        for c in plan.get("sfx_cues", []):
            at = _anchor_time(c.get("anchor", ""), cap_words)
            if at is None:
                if "time" not in c:
                    continue
                at = float(c.get("time", 0)) * tscale
            planner_cues.append((at, c.get("type", ""), sfx_vol))
        # emphasis punch-ins get a bass IMPACT so the zoom lands with weight —
        # viewers called the old near-silent edit out ("what a mess", 2026-07-16)
        planner_cues += [(t, "impact", sfx_vol * 0.55) for t in punches]
        # camera-cut whooshes at render-time — now actually audible, not texture
        cut_cues = [(t, "swoosh", sfx_vol * 0.65) for t in cuts]
        cue_list = _dedupe_sfx(planner_cues, cut_cues, render_dur,
                               cap=e.get("sfx_max", 8))
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
    # -12 LUFS: viral shorts master HOT (the 1.35M-view exemplar measures -10.4
    # dB mean). YouTube normalizes loud audio DOWN gracefully, but a quiet
    # master (-14) just sounds thin next to the feed. Configurable.
    tgt = e.get("loudnorm_i", -12)
    ln = f",loudnorm=I={tgt}:TP=-1.5:LRA=11" if e.get("loudnorm", True) else ""
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

    # 5b. overlay the pre-rendered animated intro card on the opening ~2.4s.
    if intro_card_path:
        withcard = WORK / f"card_{clip['id']}.mp4"
        try:
            subprocess.run(
                ["ffmpeg", "-y", "-i", str(out), "-i", str(intro_card_path),
                 "-filter_complex",
                 "[0:v][1:v]overlay=0:0:enable='lt(t,2.4)':format=auto[v]",
                 "-map", "[v]", "-map", "0:a", "-c:v", "libx264",
                 "-preset", "medium", "-crf", "20", "-c:a", "copy",
                 "-pix_fmt", "yuv420p", str(withcard)],
                check=True, capture_output=True)
            withcard.replace(out)
            console.print("  [dim]intro: animated Remotion hook card overlaid[/]")
        except subprocess.CalledProcessError as ex:
            console.print(f"  [yellow]intro card overlay skipped:[/] "
                          f"{ex.stderr.decode(errors='ignore')[-160:]}")

    # Final gate: a post-step (teaser concat / intro-card overlay) can silently
    # emit a streamless file on exit 0, which would then be marked 'edited' and
    # slip past QA (a 0-duration file skips the duration check). Refuse to return
    # a broken render — raising here makes edit_all skip the clip and the produce
    # backfill fill the slot with a good one instead.
    if not _has_av_streams(str(out)) or _probe_duration(out) < 1.0:
        raise RuntimeError(f"clip {clip['id']} render produced an invalid file "
                           f"(no A/V streams or zero duration)")
    if e.get("qa", True):
        _qa_render(out, total_dur, clip["id"])
    if e.get("make_cover", True):
        _make_cover(clip, plan, source, start, dur, w, h)

    # Record WHAT THIS EDIT ACTUALLY DID so the craft loop can score it against
    # the retention it earns (factory/craft.py). Never let bookkeeping break a
    # finished render.
    try:
        _record_spec(clip, plan, e, total_dur, render_dur, cuts, punches, seg_cx)
    except Exception as ex:  # noqa: BLE001
        console.print(f"  [yellow]craft spec not recorded:[/] {ex}")
    return out


def _record_spec(clip, plan, e, total_dur, render_dur, cuts, punches, seg_cx) -> None:
    """Snapshot the measurable craft parameters of a finished cut.

    Only things that VARY between clips are worth storing: a knob that is
    constant across the library can never correlate with anything, it just
    dilutes the report.
    """
    # seg_cx is seeded to [] but three reframe branches reassign it to None when
    # there are no cuts / no opencv / no faces. Subscripting None raised here and
    # the broad except above swallowed it, so ~1 in 4 renders silently recorded
    # NO craft spec: the loop was starved exactly on the clips it most wants.
    seg_cx = seg_cx or []
    switches = sum(1 for a, b in zip(seg_cx, seg_cx[1:])
                   if a is not None and b is not None and abs(a - b) > 0.06)
    spec = {
        "duration": round(total_dur, 1),
        "cuts_per_min": round(len(cuts) / (render_dur / 60), 1) if render_dur else 0,
        "shot_count": len(cuts) + 1,
        "punch_count": len(punches),
        "sfx_count": len(plan.get("sfx_cues") or []),
        "hook_words": len((plan.get("hook_text") or "").split()),
        "caption_wpp": e.get("words_per_page", cfg.get("captions.words_per_page", 2)),
        "speaker_switches": switches,
        "teaser_dur": round(max(0.0, total_dur - render_dur), 1),
        "reframe": e.get("reframe", "smart"),
        "music_mood": (plan.get("music_mood") or "none").lower().strip(),
        "has_comment_q": bool((plan.get("comment_question") or "").strip()),
    }
    db.record_edit_spec(clip["id"], spec, cfg.get("finder.niche_lock") or "")


def _probe_duration(path: Path) -> float:
    p = subprocess.run(["ffprobe", "-v", "quiet", "-show_entries",
                        "format=duration", "-of", "csv=p=0", str(path)],
                       capture_output=True, text=True)
    return float(p.stdout.strip() or 0)


def _has_av_streams(path: str) -> bool:
    """True only if `path` holds BOTH a video and an audio stream — a cheap guard
    against a subprocess that 'succeeded' but wrote a streamless/corrupt file."""
    try:
        p = subprocess.run(["ffprobe", "-v", "error", "-show_entries",
                            "stream=codec_type", "-of", "csv=p=0", str(path)],
                           capture_output=True, text=True)
        kinds = p.stdout.split()
        return any("video" in k for k in kinds) and any("audio" in k for k in kinds)
    except Exception:  # noqa: BLE001
        return False


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
        except RuntimeError as ex:               # invalid render caught by the gate
            console.print(f"[red]clip {clip['id']} skipped:[/] {ex}")
    console.print(f"[green]✓ {n} clips rendered to output/[/]")
    return n
