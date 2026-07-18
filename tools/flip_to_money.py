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
    Apply the MILLION-VIEW 5-BEAT FORMULA (reverse-engineered from 1M+ view
    money clips - see money-formula skill): (1) open on a QUESTION-HOOK with
    stakes ("How much debt is your girlfriend in?"), (2) a NUMBER TO THE CENT
    spoken by ~5s ("$15,586.97" beats "$15k"), (3) an ESCALATION LADDER - each
    reveal worse than the last, (4) the EMOTIONAL TURN - money becomes human
    conflict (hidden debt from a partner, shame, ultimatums, tears), (5) END
    UNRESOLVED so the comments finish the story.
    STRONGLY FAVOR: debt confrontations, hidden-debt relationship drama, absurd
    spending exposed, forbidden money knowledge ("bankruptcy wipes hospital
    bills"), rich-people absurdity, archetype money clashes.
    STRONGLY AVOID: dry theory, slow setups, generic advice with no number or
    stakes, non-money tangents (sport, health) - the niche-lock drops those.
    LENGTH: confrontation arcs with an escalation ladder earn 35-55s; single-
    beat moments 20-40s. Open ON the question/number - zero wind-up. Prefer
    take windows that include the LISTENER'S REACTION to the bombshell.
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

    # 6. trainer studies the MONEY winners from now on (from the 2026-07-17
    #    teardown: 900k-287k views/video channels)
    text = re.sub(
        r"  study_channels:\n(?:    - .*\n|    #.*\n)*",
        "  study_channels:\n"
        "    # Money-niche winners (teardown 2026-07-17, views/video)\n"
        '    - "UCLe_q9axMaeTbjN0hy1Z9xA"   # Caleb Hammer (902k)\n'
        '    - "UCV6KDgJskWaEckne5aPA0aQ"   # Graham Stephan (868k)\n'
        '    - "UC7eBNeDW1GQf2NJQ6G6gAxw"   # The Ramsey Show Highlights (287k)\n'
        '    - "UCmMsCFzAufSYef6tA8h1hzQ"   # You Should Know Podcast (399k)\n'
        '    - "UCeBQ24VfikOriqSdKtomh0w"   # The Iced Coffee Hour Clips (125k)\n',
        text)
    text = re.sub(r'study_queries: \[[^\]]*\]',
                  'study_queries: ["financial audit clips", "money podcast '
                  'shorts", "debt confrontation clips"]', text)

    # 7. the million-view money formula joins the working agents' skill decks
    for agent in ("finder", "editor", "uploader"):
        text = re.sub(rf"(  {agent}: +\[)", r"\1money-formula, ", text, count=1)

    # 8. launch the recurring SERIES with the new niche. 48,888 football views
    #    converted 6 subs because nothing gave people a reason to come back; a
    #    named, numbered series does. The flip is the moment to start one, since
    #    numbering from #1 on day one of the new format costs nothing.
    text = re.sub(r"(series:\n  enabled: )false", r"\1true", text)
    text = re.sub(r'(series:\n(?:.*\n)*?  name: )""', r'\1"THE RECEIPTS"', text)

    CONFIG.write_text(text, encoding="utf-8")

    # validate the result parses and the keys landed
    import yaml
    d = yaml.safe_load(CONFIG.read_text(encoding="utf-8"))
    assert d["finder"]["niche_lock"] == "money"
    assert any("CalebHammer" in s for s in d["scheduler"]["sources"])
    assert "MONEY" in d["finder"]["selection_brief"][:60]
    assert d["scheduler"]["content_since"]
    assert any("UCLe_q9axMaeTbjN0hy1Z9xA" in c
               for c in d["trainer"]["study_channels"])
    assert "money-formula" in d["skills"]["finder"]
    assert "money-formula" in d["skills"]["editor"]
    assert "money-formula" in d["skills"]["uploader"]
    assert d["series"]["enabled"] and d["series"]["name"] == "THE RECEIPTS"
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
