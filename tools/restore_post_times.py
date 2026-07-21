"""Put the posting slots back to the channel's normal schedule.

post_times.json gets temporarily overridden when a day has to be re-filled in
the afternoon (2026-07-21: produce had been dead for days, so the recovery clips
went to 16:30/17:30 rather than the passed 09:00/14:00 slots). That override
MUST NOT survive the day, or every following day silently posts in the afternoon
and the UK-timed morning slot is lost.

Restores the measured schedule: 09:00 and 14:00 local are our two best slots,
and 21:30 local = 20:30 UK evening prime (66% of viewers are British).
Idempotent.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

NORMAL = ["09:00", "14:00", "21:30"]


def restore() -> int:
    f = ROOT / "post_times.json"
    try:
        current = json.loads(f.read_text()) if f.exists() else None
    except Exception:  # noqa: BLE001
        current = None
    if current == NORMAL:
        print("post_times already normal — nothing to do")
        return 0
    f.write_text(json.dumps(NORMAL))
    print(f"post_times restored {current} -> {NORMAL}")
    try:
        from factory import notify
        notify.notify("Posting slots restored",
                      f"back to {', '.join(NORMAL)} for tomorrow")
    except Exception:  # noqa: BLE001
        pass
    return 0


if __name__ == "__main__":
    raise SystemExit(restore())
