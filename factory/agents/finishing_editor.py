"""Agent 8 — FINISHING EDITOR (the assistant editor / QA reviewer).

Runs after the Editor renders and before anything posts. It is the second pair
of eyes the channel never had: it WATCHES each finished clip the way a picky
assistant editor would on playback and catches the exact flaws a human spots —
captions covering the speaker's face, black or frozen frames, dead-air gaps,
clipped or too-quiet audio, a wrong duration. Everything is measured on the
finished file itself, so it also guards against regressions in the Editor.

It is deliberately NON-duplicative: the Editor already grades, adds the progress
bar / CTA / punches / captions. This agent's job is REVIEW + cheap finishing:
- flag anything broken (ping the phone, optionally hold the clip back),
- auto-fix the cheap stuff (gain-up a too-quiet mix),
- optional last-stage 'film look' pass (grain + micro-sharpen), off by default.

Non-destructive by default: a clip that fails QA is left 'edited' so the Manager
review and you still decide — unless finisher.block_on_fail flips it to 'flagged'
so the Uploader skips it.
"""
from __future__ import annotations

import subprocess
from pathlib import Path

from rich.console import Console

from .. import db, notify
from ..config import ROOT, cfg

console = Console()

OUT_DIR = ROOT / "output"
OUT_DIR.mkdir(exist_ok=True)


# ── pure helpers (unit-tested; no ffmpeg/opencv needed) ─────────────────────

def _caption_band(h: int, font_size: int = 90, lift: float = 0.34) -> tuple[int, int]:
    """Vertical pixel band the burned captions occupy on the finished frame.
    Mirrors captions.py 'lower' mode: bottom-anchored, margin_v = h*lift, so the
    text sits ~1.6 line-heights tall just above that margin."""
    margin_v = int(h * lift)
    line_h = int(font_size * 1.6)
    bottom = h - margin_v
    top = max(0, bottom - line_h)
    return top, bottom + int(font_size * 0.4)


def _overlap_frac(box: tuple[float, float, float, float],
                  band: tuple[int, int]) -> float:
    """Fraction of the caption band's height that a face box covers (0..1)."""
    _, y, _, fh = box
    bt, bb = band
    lo, hi = max(y, bt), min(y + fh, bb)
    span = bb - bt
    return max(0.0, hi - lo) / span if span > 0 else 0.0


def _verdict(issues: list[dict]) -> str:
    """PASS / FIX / FLAG from the collected issues (most severe wins)."""
    sev = {i["sev"] for i in issues}
    if "critical" in sev:
        return "FLAG"
    if "fixable" in sev:
        return "FIX"
    if "warn" in sev:
        return "PASS*"        # posted, but with noted warnings
    return "PASS"


def _report_md(clip_id, verdict: str, issues: list[dict],
               actions: list[str]) -> str:
    icon = {"PASS": "✅", "PASS*": "✅", "FIX": "🔧", "FLAG": "🚩"}.get(verdict, "•")
    lines = [f"# Finishing-editor QA — clip {clip_id}: {icon} {verdict}\n"]
    if not issues:
        lines.append("No problems found — captions, frames, audio and duration "
                     "all clean.")
    else:
        for i in sorted(issues, key=lambda x: {"critical": 0, "fixable": 1,
                                               "warn": 2}.get(x["sev"], 3)):
            tag = {"critical": "🚩 CRITICAL", "fixable": "🔧 fixable",
                   "warn": "⚠ warning"}.get(i["sev"], i["sev"])
            lines.append(f"- **{tag}** — {i['kind']}: {i['msg']}")
    if actions:
        lines.append("\n## Actions taken")
        lines += [f"- {a}" for a in actions]
    return "\n".join(lines) + "\n"


# ── ffmpeg/opencv probes (defensive: never raise) ───────────────────────────

def _ff_stderr(args: list[str]) -> str:
    """Run an ffmpeg analysis pass, return combined stderr (filters log here)."""
    try:
        p = subprocess.run(["ffmpeg", "-hide_banner", *args, "-f", "null", "-"],
                           capture_output=True, text=True, timeout=180)
        return p.stderr or ""
    except Exception:  # noqa: BLE001 - QA must never crash the pipeline
        return ""


def _probe_duration(path: Path) -> float:
    try:
        p = subprocess.run(
            ["ffprobe", "-v", "quiet", "-show_entries", "format=duration",
             "-of", "csv=p=0", str(path)], capture_output=True, text=True)
        return float(p.stdout.strip() or 0)
    except Exception:  # noqa: BLE001
        return 0.0


def _check_black(path: Path) -> list[dict]:
    out = []
    log = _ff_stderr(["-i", str(path), "-vf", "blackdetect=d=0.5:pic_th=0.98", "-an"])
    spans = log.count("black_start")
    if spans:
        out.append({"kind": "black frames", "sev": "critical",
                    "msg": f"{spans} fully-black stretch(es) ≥0.5s — broken render"})
    return out


def _check_freeze(path: Path) -> list[dict]:
    out = []
    log = _ff_stderr(["-i", str(path), "-vf", "freezedetect=n=-55dB:d=2.5", "-an"])
    spans = log.count("freeze_start")
    if spans:
        out.append({"kind": "frozen frames", "sev": "warn",
                    "msg": f"{spans} frozen stretch(es) ≥2.5s (check b-roll / a stall)"})
    return out


def _check_silence(path: Path, dur: float) -> list[dict]:
    out = []
    log = _ff_stderr(["-i", str(path), "-af", "silencedetect=noise=-40dB:d=2.0", "-vn"])
    gaps = log.count("silence_start")
    if gaps:
        out.append({"kind": "dead air", "sev": "warn",
                    "msg": f"{gaps} silent gap(s) ≥2s — trim should have caught these"})
    return out


def _check_levels(path: Path) -> list[dict]:
    """Peak (clipping) + mean (too quiet). Returns issues; a quiet-but-audible
    mix is marked 'fixable' with the gain needed so review_clip can auto-correct."""
    out = []
    log = _ff_stderr(["-i", str(path), "-af", "volumedetect", "-vn"])
    mx = mean = None
    for line in log.splitlines():
        if "max_volume" in line:
            try: mx = float(line.split("max_volume:")[1].split("dB")[0])
            except Exception: pass  # noqa: BLE001,E701
        elif "mean_volume" in line:
            try: mean = float(line.split("mean_volume:")[1].split("dB")[0])
            except Exception: pass  # noqa: BLE001,E701
    if mx is not None and mx >= -0.3:
        out.append({"kind": "audio clipping", "sev": "warn",
                    "msg": f"peak {mx:.1f} dB — distortion risk"})
    if mean is not None:
        if mean < -45:
            out.append({"kind": "audio near-silent", "sev": "critical",
                        "msg": f"mean {mean:.0f} dB — effectively no sound"})
        elif mean < -30:
            out.append({"kind": "audio too quiet", "sev": "fixable",
                        "msg": f"mean {mean:.0f} dB — will gain up ~{-16 - mean:.0f} dB",
                        "gain": round(-16 - mean, 1)})
    return out


def _check_face_edge(path: Path, samples: int = 24) -> list[dict]:
    """Is a person cut in half by the frame edge? A punch-in crop that landed
    beside the speaker leaves a face hugging the left/right border. The defect
    lives in individual camera segments (2-4s), so sample densely and trigger
    on an absolute count — a 40s clip with one bad 3s shot must still flag."""
    try:
        import cv2
        cascade = cv2.CascadeClassifier(
            cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
        if cascade.empty():
            return []
        cap = cv2.VideoCapture(str(path))
        if not cap.isOpened():
            return []
    except Exception:  # noqa: BLE001
        return []
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) or 0
    fw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)) or 0
    idxs = [int(total * i / (samples + 1)) for i in range(1, samples + 1)] if total else []
    edge = seen = 0
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, frame = cap.read()
        if not ok:
            continue
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, 1.1, 5, minSize=(70, 70))
        if not len(faces):
            continue
        seen += 1
        if any(x <= 4 or x + w >= fw - 4 for x, _, w, _ in faces):
            edge += 1
    cap.release()
    if edge >= 3 and seen >= 3 and edge / seen >= 0.15:
        return [{"kind": "person cut by frame edge", "sev": "warn",
                 "msg": f"a face hugs the frame border in {edge}/{seen} sampled "
                        f"frames — the punch-in crop likely missed the speaker"}]
    return []


def _check_face_captions(path: Path, band: tuple[int, int],
                         samples: int = 10) -> list[dict]:
    """Do the burned captions sit on the speaker's face? Sample frames of the
    FINISHED vertical clip, detect faces, and see how often a face covers the
    caption band. Degrades to a no-op if OpenCV is unavailable."""
    try:
        import cv2
        cascade = cv2.CascadeClassifier(
            cv2.data.haarcascades + "haarcascade_frontalface_default.xml")
        if cascade.empty():
            return []
        cap = cv2.VideoCapture(str(path))
        if not cap.isOpened():
            return []
    except Exception:  # noqa: BLE001
        return []
    total = int(cap.get(cv2.CAP_PROP_FRAME_COUNT)) or 0
    idxs = [int(total * i / (samples + 1)) for i in range(1, samples + 1)] if total else []
    hits = seen = 0
    for fi in idxs:
        cap.set(cv2.CAP_PROP_POS_FRAMES, fi)
        ok, frame = cap.read()
        if not ok:
            continue
        seen += 1
        gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
        faces = cascade.detectMultiScale(gray, 1.1, 5, minSize=(80, 80))
        if any(_overlap_frac(tuple(f), band) > 0.35 for f in faces):
            hits += 1
    cap.release()
    if seen and hits / seen >= 0.5:
        return [{"kind": "captions on face", "sev": "warn",
                 "msg": f"a face overlaps the caption band in {hits}/{seen} frames "
                        f"— consider raising captions.caption_lift"}]
    return []


# ── fixes / finishing ───────────────────────────────────────────────────────

def _replace(src: Path, dst: Path) -> None:
    dst.unlink(missing_ok=True)
    src.replace(dst)


def _autofix_quiet(path: Path, gain_db: float) -> bool:
    tmp = path.with_name(path.stem + "_gain.mp4")
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-hide_banner", "-i", str(path),
             "-af", f"volume={gain_db:.1f}dB", "-c:v", "copy",
             "-c:a", "aac", "-b:a", "192k", str(tmp)],
            check=True, capture_output=True, timeout=300)
        _replace(tmp, path)
        return True
    except Exception:  # noqa: BLE001
        tmp.unlink(missing_ok=True)
        return False


def _film_finish(path: Path) -> bool:
    """Optional last-stage 'film look': micro-sharpen + a whisper of grain over
    the fully-composited frame. Off by default — the Editor already grades."""
    tmp = path.with_name(path.stem + "_fin.mp4")
    try:
        subprocess.run(
            ["ffmpeg", "-y", "-hide_banner", "-i", str(path),
             "-vf", "unsharp=5:5:0.4:5:5:0.0,noise=alls=5:allf=t+u",
             "-c:v", "libx264", "-preset", "medium", "-crf", "19",
             "-c:a", "copy", "-pix_fmt", "yuv420p", str(tmp)],
            check=True, capture_output=True, timeout=600)
        _replace(tmp, path)
        return True
    except Exception:  # noqa: BLE001
        tmp.unlink(missing_ok=True)
        return False


# ── orchestration ───────────────────────────────────────────────────────────

def review_clip(clip) -> tuple[str, list[dict]]:
    """Review one finished clip. Runs the enabled checks, applies cheap fixes,
    writes a QA sidecar, and returns (verdict, issues)."""
    path = Path(clip["rendered_path"]) if clip["rendered_path"] else None
    if not path or not path.exists():
        return "FLAG", [{"kind": "missing file", "sev": "critical",
                         "msg": "no rendered file to review"}]

    chk = lambda k: cfg.get(f"finisher.checks.{k}", True)  # noqa: E731
    dur = _probe_duration(path)
    issues: list[dict] = []

    # A broken render (streamless / zero-duration mp4) must FAIL, not slip through:
    # a 0-duration file would otherwise skip the duration check below and 'pass'.
    if dur < 1.0:
        return "FLAG", [{"kind": "broken render", "sev": "critical",
                         "msg": "file has no playable video/audio (zero duration)"}]

    lo = cfg.get("finder.min_seconds", 12)
    if not (lo - 2 <= dur <= 61):
        issues.append({"kind": "duration", "sev": "critical",
                       "msg": f"{dur:.0f}s is outside the Shorts window ({lo}-60s)"})
    if chk("black"):
        issues += _check_black(path)
    if chk("freeze"):
        issues += _check_freeze(path)
    if chk("silence"):
        issues += _check_silence(path, dur)
    if chk("levels"):
        issues += _check_levels(path)
    if chk("face_captions"):
        res = cfg.get("editor.resolution", [1080, 1920])
        capc = cfg.get("editor.captions", {})
        band = _caption_band(res[1], capc.get("font_size", 90),
                             capc.get("caption_lift", 0.34))
        issues += _check_face_captions(path, band)
    if chk("face_edge"):
        issues += _check_face_edge(path)

    actions: list[str] = []
    # cheap auto-fixes turn a 'fixable' issue into a resolved one
    if cfg.get("finisher.autofix_quiet", True):
        for i in [x for x in issues if x["kind"] == "audio too quiet"]:
            if _autofix_quiet(path, i.get("gain", 6)):
                i["sev"] = "warn"
                i["msg"] += " → gained up ✓"
                actions.append(f"gained audio +{i.get('gain', 6)} dB")

    if cfg.get("finisher.film_finish", False):
        if _film_finish(path):
            actions.append("applied film-finish (sharpen + grain)")

    verdict = _verdict(issues)
    (OUT_DIR / f"clip_{clip['id']}.qa.md").write_text(
        _report_md(clip["id"], verdict, issues, actions), encoding="utf-8")
    return verdict, issues


def finish_all(clip_ids=None) -> int:
    """Review queued ('edited') clips. Pass `clip_ids` to review only those
    (used by the produce backfill loop so already-passed clips aren't re-reviewed
    or double-finished); None reviews the whole queue. Returns the count reviewed.
    Critical failures ping the phone and — if finisher.block_on_fail — are held
    back (status 'flagged'); produce then backfills the freed slot, and
    ensure_floor() is the last-ditch guarantee the day is never fully empty."""
    if not cfg.get("finisher.enabled", True):
        return 0
    queue = db.clips_by_status("edited")
    if clip_ids is not None:
        want = set(clip_ids)
        queue = [c for c in queue if c["id"] in want]
    if not queue:
        console.print("[yellow]Finishing editor: nothing in the queue.[/]")
        return 0
    console.print(f"[bold blue]FINISHING EDITOR[/] reviewing {len(queue)} clip(s)…")
    block = cfg.get("finisher.block_on_fail", False)
    flagged = 0
    for clip in queue:
        verdict, issues = review_clip(clip)
        crit = [i for i in issues if i["sev"] == "critical"]
        warn = [i for i in issues if i["sev"] == "warn"]
        tag = {"PASS": "green", "PASS*": "green", "FIX": "cyan",
               "FLAG": "red"}.get(verdict, "white")
        console.print(f"  clip {clip['id']}: [{tag}]{verdict}[/]"
                      + (f" — {len(crit)} critical, {len(warn)} warn" if issues else ""))
        if crit:
            flagged += 1
            msg = f"clip {clip['id']}: " + "; ".join(i["msg"] for i in crit)
            notify.notify("Finishing editor: clip flagged", msg[:180])
            if block:
                db.set_clip_status(clip["id"], "flagged")
                console.print(f"  [red]→ held back (status 'flagged'), won't post[/]")
    if flagged:
        console.print(f"[red]⚠ {flagged} clip(s) flagged"
                      + (" and held back" if block else "") + ".[/]")
    else:
        console.print("[green]✓ all clips passed finishing review.[/]")
    return len(queue)


def ensure_floor(min_queue: int | None = None) -> int:
    """Last-ditch guarantee the schedule is never fully empty. Only relevant with
    block_on_fail: if QA blocked so many clips that fewer than `min_queue` remain
    postable, salvage the best-scoring flagged clip(s) that still have a playable
    file — a flawed post beats a dead channel day. Returns how many were salvaged.

    Produce's backfill (render the next-best fresh candidate for each blocked clip)
    is the primary defense; this only fires when the whole candidate pool failed."""
    if not cfg.get("finisher.block_on_fail", False):
        return 0
    floor = cfg.get("finisher.min_queue", 1) if min_queue is None else min_queue
    if floor <= 0:
        return 0
    have = len(db.clips_by_status("edited"))
    if have >= floor:
        return 0
    salvaged = 0
    for clip in db.clips_by_status("flagged"):        # best score first
        if have + salvaged >= floor:
            break
        p = Path(clip["rendered_path"]) if clip["rendered_path"] else None
        if not p or not p.exists():
            continue                                  # nothing to post → skip
        db.set_clip_status(clip["id"], "edited")
        salvaged += 1
        console.print(f"  [yellow]↺ salvaged flagged clip {clip['id']} so the "
                      f"day isn't empty[/]")
        notify.notify("Finishing editor: salvaged a flagged clip",
                      f"every clip failed QA — scheduling clip {clip['id']} anyway "
                      f"so the channel isn't dark today. Review it: "
                      f"{clip['title'][:80]}")
    return salvaged
