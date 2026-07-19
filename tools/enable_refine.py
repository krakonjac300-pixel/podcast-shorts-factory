"""One-shot: switch on two-pass caption refinement, AFTER the niche flip lands.

Captions are burned in permanently, so a mangled proper noun ships forever
("Anfield, Kop end" went out as "Anfield Coppen"). The whole-episode transcript
does not need to be perfect (the finder only uses it to locate moments) but the
~90 seconds we actually burn on screen does. So pass 2 re-transcribes only the
chosen clip windows with a stronger model.

This is deliberately NOT part of flip_to_money.py. The flip already changes the
niche, sources, brief, vocabulary and branding in one unattended 05:30 run;
stacking a transcription change on top means a failure there has five possible
causes. This runs a day later so the variable is isolated.

Idempotent: safe to re-run.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

CONFIG = ROOT / "config.yaml"


def enable() -> bool:
    text = CONFIG.read_text(encoding="utf-8")
    if re.search(r"^  refine_clips: true", text, re.M):
        print("already enabled — nothing to do")
        return True
    if not re.search(r"^  refine_clips: false", text, re.M):
        print("refine_clips key not found — config changed shape, not touching it")
        return False

    text = re.sub(r"^  refine_clips: false", "  refine_clips: true", text,
                  count=1, flags=re.M)
    CONFIG.write_text(text, encoding="utf-8")

    import yaml
    d = yaml.safe_load(CONFIG.read_text(encoding="utf-8"))
    assert d["finder"]["refine_clips"] is True
    assert d["finder"]["refine_model"]
    print(f"two-pass captions ON (pass 2 = {d['finder']['refine_model']} "
          f"over clip windows only)")

    try:
        from factory import notify
        notify.notify(
            "Caption refinement is live",
            "Clip windows are now re-transcribed with the medium model, so "
            "burned-in captions stop mangling names. Whole-episode pass is "
            "unchanged, so produce timing barely moves.")
    except Exception:  # noqa: BLE001 - notification is not the point
        pass
    return True


if __name__ == "__main__":
    raise SystemExit(0 if enable() else 1)
