"""One-shot: reconstruct edit specs for clips rendered BEFORE the craft loop existed.

The loop needs ~10 clips with both a spec and retention data before it will
claim anything. Rendering that many takes days, but we already have 25 measured
clips on disk plus their `.notes.md` sidecars, so most of the spec is
recoverable right now:

  * duration, cuts_per_min  -> measured off the finished mp4 (ffmpeg scene detect)
  * hook_words, music_mood, sfx_count -> parsed from the notes sidecar

`punch_count`, `speaker_switches` and `caption_wpp` are NOT recoverable, so they
are simply left out. An absent field is skipped by the analyser; inventing a
plausible value would poison the very loop this is meant to seed.

Every backfilled row is tagged `backfilled: true` so the provenance is visible.
Idempotent: safe to re-run.
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from factory import config, db  # noqa: E402,F401  (config sets ffmpeg PATH)
from factory.config import ROOT, cfg  # noqa: E402

OUT = ROOT / "output"
SCENE_THRESHOLD = 0.4      # high enough to catch hard cuts, not zoom punches


def _probe_duration(p: Path) -> float:
    try:
        r = subprocess.run(
            ["ffprobe", "-v", "error", "-show_entries", "format=duration",
             "-of", "default=nw=1:nk=1", str(p)],
            capture_output=True, text=True, check=True)
        return float(r.stdout.strip())
    except (subprocess.CalledProcessError, ValueError, FileNotFoundError):
        return 0.0


def _count_cuts(p: Path) -> int | None:
    """Count hard cuts in the finished render via ffmpeg scene detection."""
    try:
        r = subprocess.run(
            ["ffmpeg", "-i", str(p), "-filter:v",
             f"select='gt(scene,{SCENE_THRESHOLD})',showinfo", "-f", "null", "-"],
            capture_output=True, text=True)
        return r.stderr.count("pts_time:")
    except FileNotFoundError:
        return None


def _parse_notes(p: Path) -> dict:
    if not p.exists():
        return {}
    txt = p.read_text(encoding="utf-8", errors="ignore")
    spec: dict = {}
    if m := re.search(r"\*\*On-screen hook:\*\*\s*(.+)", txt):
        spec["hook_words"] = len(m.group(1).split())
    if m := re.search(r"\*\*Music mood:\*\*\s*(.+)", txt):
        spec["music_mood"] = m.group(1).strip().lower()
    if m := re.search(r"## SFX cues\n(.*?)(\n##|\Z)", txt, re.S):
        spec["sfx_count"] = len([ln for ln in m.group(1).splitlines()
                                 if ln.strip().startswith("- ")])
    else:
        spec["sfx_count"] = 0
    return spec


def main() -> int:
    niche = cfg.get("finder.niche_lock") or ""
    done = skipped = 0

    with db.conn() as c:
        clips = c.execute(
            """SELECT DISTINCT c.id, c.rendered_path FROM clips c
                 JOIN uploads u ON u.clip_id = c.id
                 JOIN metrics m ON m.upload_id = u.id""").fetchall()

    for row in clips:
        cid = row["id"]
        if db.edit_spec(cid).get("backfilled") is None and db.edit_spec(cid):
            skipped += 1          # a real spec already exists — never overwrite it
            continue

        spec = _parse_notes(OUT / f"clip_{cid}.notes.md")
        mp4 = Path(row["rendered_path"]) if row["rendered_path"] else OUT / f"clip_{cid}.mp4"
        if mp4.exists():
            dur = _probe_duration(mp4)
            if dur > 1.0:
                spec["duration"] = round(dur, 1)
                cuts = _count_cuts(mp4)
                if cuts is not None:
                    spec["cuts_per_min"] = round(cuts / (dur / 60), 1)

        if not spec:
            skipped += 1
            continue

        spec["backfilled"] = True
        db.record_edit_spec(cid, spec, niche)
        done += 1
        print(f"  clip {cid}: {spec}")

    print(f"\nbackfilled {done} spec(s), skipped {skipped}")
    from factory import craft
    print("\n" + craft.update())
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
