"""Measure the Finishing Editor's FALSE ACCEPTS against human labels.

The QA agent's dangerous failure is not the clip it wrongly flags (we see
those: they block the queue). It is the broken clip it PASSES, which publishes
unattended and quietly makes the channel look amateur. The only way to measure
that is to compare the agent's verdicts with human labels on the same clips.

Workflow (two steps, human in the middle):

  1. python tools/qa_audit.py pack
       Builds audit/ with, per recent rendered clip: a contact-sheet jpg, the
       agent's verdict + rules version, and one row in labels.csv with empty
       0/1 columns for the five defect classes that make a channel look broken:
         captions_on_face, frozen_or_black, dead_air, audio_bad, bad_crop
       A human watches each clip (or scans the sheet), fills the columns, saves.

  2. python tools/qa_audit.py score
       Compares the filled labels.csv with the agent's verdicts and writes
       audit/report.md: false-accept and false-reject rates per class, split by
       QA rules version. False accepts on any class are listed clip by clip.

The labels are the ground truth ONLY when a human wrote them. Running `score`
against an unfilled csv reports nothing rather than fabricating agreement.
"""
from __future__ import annotations

import csv
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OUT = ROOT / "output"
AUDIT = ROOT / "audit"
CLASSES = ["captions_on_face", "frozen_or_black", "dead_air", "audio_bad",
           "bad_crop"]


def _verdict_of(clip_id: int) -> tuple[str, str]:
    """(verdict, rules_version) from the clip's qa.md, or ('?', '?')."""
    f = OUT / f"clip_{clip_id}.qa.md"
    if not f.exists():
        return "?", "?"
    txt = f.read_text(encoding="utf-8", errors="ignore")
    m = re.search(r"clip \d+: .*? (PASS\*?|FLAG|FIX)", txt)
    v = re.search(r"rules v(\w+)", txt)
    return (m.group(1) if m else "?"), (v.group(1) if v else "pre-3")


def pack(last_n: int = 20) -> int:
    from factory import db

    AUDIT.mkdir(exist_ok=True)
    with db.conn() as c:
        rows = c.execute(
            """SELECT id, title, rendered_path FROM clips
               WHERE rendered_path IS NOT NULL AND status IN
                     ('uploaded', 'edited', 'flagged')
               ORDER BY id DESC LIMIT ?""", (last_n,)).fetchall()
    rows = [r for r in rows if r["rendered_path"]
            and Path(r["rendered_path"]).exists()]
    if not rows:
        print("no rendered clips found to audit")
        return 1

    csv_path = AUDIT / "labels.csv"
    file_existed = csv_path.exists()
    existing: set[str] = set()
    if file_existed:                            # never clobber human work
        with csv_path.open(newline="", encoding="utf-8") as f:
            existing = {row["clip_id"] for row in csv.DictReader(f)}

    new = 0
    with csv_path.open("a", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        if not file_existed:    # header only for a NEW file: a file with a
            # header but zero rows must not get a second header as a data row
            w.writerow(["clip_id", "title", "agent_verdict", "qa_rules"]
                       + CLASSES + ["notes"])
        for r in rows:
            cid = str(r["id"])
            if cid in existing:
                continue
            verdict, rules = _verdict_of(r["id"])
            sheet = AUDIT / f"clip_{cid}_sheet.jpg"
            subprocess.run(
                ["ffmpeg", "-y", "-i", r["rendered_path"], "-vf",
                 "fps=1/3,scale=180:320,tile=6x2", "-frames:v", "1",
                 str(sheet)], capture_output=True)
            w.writerow([cid, (r["title"] or "")[:60], verdict, rules]
                       + [""] * len(CLASSES) + [""])
            new += 1
    print(f"audit pack ready: {new} new clip(s) in {csv_path}")
    print("watch each clip (or scan its _sheet.jpg), put 1 in a defect column "
          "if a HUMAN would call it broken, 0 otherwise, then run: score")
    return 0


def score() -> int:
    csv_path = AUDIT / "labels.csv"
    if not csv_path.exists():
        print("no labels.csv - run `pack` first")
        return 1
    with csv_path.open(newline="", encoding="utf-8") as f:
        rows = list(csv.DictReader(f))
    labelled = [r for r in rows
                if any((r.get(c) or "").strip() in ("0", "1") for c in CLASSES)]
    if not labelled:
        print("labels.csv has no human labels yet - fill the 0/1 columns "
              "first. Refusing to score an empty sheet as agreement.")
        return 1

    false_accepts, false_rejects = [], []
    for r in labelled:
        human_bad = any((r.get(c) or "").strip() == "1" for c in CLASSES)
        agent_pass = r.get("agent_verdict", "").startswith("PASS")
        if human_bad and agent_pass:
            kinds = [c for c in CLASSES if (r.get(c) or "").strip() == "1"]
            false_accepts.append((r["clip_id"], r.get("qa_rules", "?"), kinds))
        if not human_bad and r.get("agent_verdict") == "FLAG":
            false_rejects.append((r["clip_id"], r.get("qa_rules", "?")))

    n = len(labelled)
    fa, fr = len(false_accepts), len(false_rejects)
    lines = [f"# QA audit - {n} human-labelled clip(s)",
             "",
             f"- false accepts (agent PASSED a clip a human calls broken): "
             f"{fa}/{n}",
             f"- false rejects (agent FLAGGED a clean clip): {fr}/{n}", ""]
    if false_accepts:
        lines.append("## False accepts - the ones that matter")
        for cid, rules, kinds in false_accepts:
            lines.append(f"- clip {cid} (rules v{rules}): {', '.join(kinds)}")
        lines.append("")
    lines.append("A false-accept rate above zero on captions_on_face, "
                 "frozen_or_black or bad_crop means the gate is not yet safe "
                 "to trust unattended for that class.")
    report = AUDIT / "report.md"
    report.write_text("\n".join(lines) + "\n", encoding="utf-8")
    print("\n".join(lines))
    print(f"\nwritten to {report}")
    return 0


if __name__ == "__main__":
    mode = sys.argv[1] if len(sys.argv) > 1 else "pack"
    raise SystemExit(pack() if mode == "pack" else score())
