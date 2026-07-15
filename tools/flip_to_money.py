"""One-shot niche flip: Football (World Cup play) -> Money/Business.

Strategy meeting 2026-07-15: one flagship channel, finance niche, all-in after
the World Cup final. This script applies the whole content flip in one shot —
scheduled for Jul 20 05:30 (before the 6AM produce) so the first finance batch
happens without anyone touching anything. Idempotent: safe to re-run.

Changes: finder.niche_lock -> money, scheduler.sources -> money podcasts,
finder.selection_brief -> money brief, trend_scout.niche -> money trends,
scheduler.content_since -> flip date (keeps old football moments out of new
montages/episodes), montage keeps working automatically once finance moments
accumulate. Branding (name/logo in YouTube Studio) stays a manual step — the
owner gets an ntfy with exactly what to do.
"""
from __future__ import annotations

import re
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

CONFIG = ROOT / "config.yaml"

MONEY_BRIEF = '''  selection_brief: |
    MONEY/BUSINESS CHANNEL (flipped 2026-07-20; growth+monetization only).
    Our #1 proven lesson carries over from the football era: EMOTION + a
    shocking STAKE wins (a star being raw/vulnerable got 21k). For money:
    STRONGLY FAVOR: a shocking NUMBER said out loud ("$300k in debt", "makes
    $2M a year at 24", "spent $80k on that"), financial trainwrecks / people
    confronted about terrible money habits, rags-to-riches, wealth flexes,
    "you're doing money wrong", controversial money takes ("renting is smarter",
    "college is a scam"), raw money emotion (breaking down over debt, bragging).
    The OPENING LINE must contain a number or a bold claim - open ON it, no
    wind-up. STRONGLY AVOID: dry theory, slow setups, generic advice with no
    number/stakes, non-money tangents (sport, health) - the niche-lock drops those.
    Target 20-40s, end on a comment-bait question ("Would you do this?",
    "Is this crazy or smart?").
'''

MONEY_SOURCES = '''  sources:
    # Money/Business era (flipped 2026-07-20). Confrontation + emotion first —
    # the money equivalents of the Keane/Trent drama that won for us.
    - "https://www.youtube.com/@CalebHammer/videos"        # Financial Audit — debt trainwreck drama
    - "https://www.youtube.com/@TheRamseyShow/videos"      # Ramsey — confrontation, debt-free screams
    - "https://www.youtube.com/@TheDiaryOfACEO/videos"     # DOAC — rags-to-riches, big names
    - "https://www.youtube.com/@MyFirstMillionPod/videos"  # MFM — business ideas, "how they made $X"
'''


def flip() -> bool:
    text = CONFIG.read_text(encoding="utf-8")
    if 'niche_lock: "money"' in text:
        print("already flipped — nothing to do")
        return True

    # 1. niche lock
    text = text.replace('niche_lock: "football"', 'niche_lock: "money"')

    # 2. sources block (replace the whole current block up to source_url)
    text = re.sub(r"  sources:\n(?:    .*\n|    #.*\n)*?(?=  source_url:)",
                  MONEY_SOURCES, text)

    # 3. selection brief (replace the block up to the next top-of-key at 2 spaces)
    text = re.sub(r"  selection_brief: \|\n(?:    .*\n)+", MONEY_BRIEF, text)

    # 4. trend scout niche
    text = re.sub(r'(trend_scout:\n(?:.*\n)*?  niche: ")[^"]*(")',
                  r"\1money, personal finance, business, wealth — viral money "
                  r"stories, debt drama, get-rich journeys\2", text)

    # 5. content watershed — old football moments stay out of new montages/episodes
    if "content_since:" in text:
        text = re.sub(r'content_since: "[^"]*"',
                      f'content_since: "{date.today().isoformat()}"', text)
    else:
        text = text.replace("scheduler:\n",
                            "scheduler:\n"
                            f'  content_since: "{date.today().isoformat()}"'
                            "   # niche flip watershed: montage/compiler only use clips after this\n", 1)

    CONFIG.write_text(text, encoding="utf-8")

    # validate the result parses and the keys landed
    import yaml
    d = yaml.safe_load(CONFIG.read_text(encoding="utf-8"))
    assert d["finder"]["niche_lock"] == "money"
    assert any("CalebHammer" in s for s in d["scheduler"]["sources"])
    assert "MONEY" in d["finder"]["selection_brief"][:60]
    assert d["scheduler"]["content_since"]
    print("flip applied + config validates: niche=money, 4 money sources, "
          f"content_since={d['scheduler']['content_since']}")

    try:
        from factory import notify
        notify.notify(
            "CHANNEL FLIPPED TO MONEY/BUSINESS",
            "The factory now clips CalebHammer/Ramsey/DOAC/MFM. Your one manual "
            "step: rename the channel + new logo in YouTube Studio (money brand). "
            "First finance batch schedules at the next produce run.")
    except Exception:  # noqa: BLE001
        pass
    return True


if __name__ == "__main__":
    sys.exit(0 if flip() else 1)
