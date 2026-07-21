"""Report what QA actually decided on the day's clips, and why.

Written after a fail-closed QA guard flagged three good clips on 2026-07-21 and
emptied the day. The guard was right to exist; the problem was that nobody would
have known it misfired until the queue came up empty hours later.

Two failure shapes this surfaces, which look identical from outside:
  * clips genuinely bad      -> real defects, the editor needs fixing
  * the CHECK itself broken  -> "qa inconclusive", the guard needs fixing
The second is the dangerous one: it blocks good work while looking like caution.

Run after produce. Notifies only when something needs attention, so a clean day
stays quiet.
"""
from __future__ import annotations

import re
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

OUT = ROOT / "output"


def report(day: str = "") -> int:
    from factory import db, notify

    day = day or date.today().isoformat()
    with db.conn() as c:
        clips = c.execute(
            "SELECT id, title, status FROM clips WHERE created_at >= ? ORDER BY id",
            (day,)).fetchall()
    if not clips:
        print(f"no clips created on {day} — produce may not have run")
        notify.notify("No clips today",
                      f"nothing was created on {day}; check the produce run")
        return 1

    verdicts, inconclusive, defects = {}, [], {}
    for row in clips:
        qa = OUT / f"clip_{row['id']}.qa.md"
        if not qa.exists():
            verdicts["(no QA file)"] = verdicts.get("(no QA file)", 0) + 1
            continue
        txt = qa.read_text(encoding="utf-8", errors="ignore")
        m = re.search(r"clip \d+: .+? (PASS\*?|FLAG|FIX)", txt)
        v = m.group(1) if m else "?"
        verdicts[v] = verdicts.get(v, 0) + 1
        for kind in re.findall(r"— ([a-z ]+):", txt):
            defects[kind] = defects.get(kind, 0) + 1
        if "qa inconclusive" in txt:
            inconclusive.append(row["id"])

    print(f"QA verdicts for {day} ({len(clips)} clips): "
          + ", ".join(f"{k}={v}" for k, v in sorted(verdicts.items())))
    if defects:
        print("  defects: " + ", ".join(f"{k} x{n}" for k, n in
                                        sorted(defects.items(), key=lambda x: -x[1])))

    # The alarm that matters: the CHECK failed, not the clip.
    if inconclusive:
        msg = (f"QA could not run on clips {inconclusive} — this blocks good "
               f"clips and is a bug in the CHECK, not the content. "
               f"Verdicts: {verdicts}")
        print(msg)
        notify.notify("QA is misfiring", msg)
        return 2

    flagged = verdicts.get("FLAG", 0)
    if flagged and flagged == len(clips):
        msg = (f"ALL {flagged} clips flagged today. Either the editor broke or "
               f"the QA guard is over-firing. Defects: {defects}")
        print(msg)
        notify.notify("Every clip flagged", msg)
        return 2
    if flagged:
        notify.notify("Some clips flagged",
                      f"{flagged}/{len(clips)} flagged. Defects: {defects}")
    return 0


if __name__ == "__main__":
    raise SystemExit(report(sys.argv[1] if len(sys.argv) > 1 else ""))
