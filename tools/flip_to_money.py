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
    MONEY/BUSINESS CHANNEL (flipped 2026-07-20).
    THE RULE: DRAMA EARNS THE WATCH, THE LESSON EARNS THE FOLLOW. Every clip
    must teach the viewer ONE concrete thing they can use. A clip that is only
    a trainwreck is entertainment nobody follows for; a clip that is only
    advice is a lecture nobody watches. We want both, in that order.
    HARD REQUIREMENT - THE TAKEAWAY: before selecting a moment, state in one
    sentence what a viewer LEARNS from it, specific enough to act on this week.
    "Minimum payments on a 27% APR card mean the balance barely moves" is a
    takeaway. "Get your finances together" is not. IF YOU CANNOT NAME THE
    TAKEAWAY, DO NOT PICK THE MOMENT, however dramatic it is.
    THE 4-BEAT SHAPE:
      (1) HOOK on the number or the claim, no wind-up. A number to the cent
          beats a round one ("$15,586.97" beats "$15k").
      (2) THE MISTAKE, named specifically - what they actually did wrong.
      (3) THE MECHANISM - WHY it went wrong. This is the teaching beat and the
          one that makes a viewer feel smarter for having watched.
      (4) THE FIX or the cost - what to do instead, or what this actually cost
          them in money or years. End on the insight, not on the shouting.
    STRONGLY FAVOR: moments where an expert explains WHY something went wrong,
    a number that reframes how something works (interest, fees, leases,
    minimum payments, credit scores), a common mistake most viewers are also
    making, forbidden/under-known money facts that are TRUE and checkable,
    before-and-after turnarounds with the actual steps.
    STRONGLY AVOID: pure humiliation with nothing learnable, dry theory with no
    number or stakes, vague advice ("budget better"), anything financially
    WRONG or misleading - we are teaching real people about their money and a
    confidently wrong clip is worse than no clip. If the expert is speculating,
    do not present it as fact.
    LENGTH: TARGET 18-30s (MEASURED: clips finishing over ~34s lose 39
    retention points on this channel). The teaching beat is what earns the
    extra seconds - cut the setup and the shouting, never the mechanism.
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

    # 7b. money vocabulary for Whisper — the football names would actively
    #     mislead the decoder once every clip is about debt and credit scores.
    text = re.sub(
        r"  vocabulary: >\n(?:    .*\n)+",
        "  vocabulary: >\n"
        "    credit score, FICO, APR, minimum payment, collections,\n"
        "    401k, Roth IRA, index fund, S&P 500, compound interest,\n"
        "    Caleb Hammer, Dave Ramsey, Graham Stephan, debt snowball,\n"
        "    net worth, take-home pay, overdraft, repossession, escrow.\n",
        text)

    # 8. launch the recurring SERIES with the new niche. 48,888 football views
    #    converted 6 subs because nothing gave people a reason to come back; a
    #    named, numbered series does. The flip is the moment to start one, since
    #    numbering from #1 on day one of the new format costs nothing.
    text = re.sub(r"(series:\n  enabled: )false", r"\1true", text)
    # Series name matches the new channel brand. It was briefly "THE RECEIPTS"
    # until an availability check found an existing YouTube channel by that
    # name plus the well-known UK "The Receipts Podcast" — and 66% of our
    # viewers are British, so that collision would land on our own audience.
    text = re.sub(r'(series:\n(?:.*\n)*?  name: )""', r'\1"MUGSHOT"', text)

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
    assert d["series"]["enabled"] and d["series"]["name"] == "MUGSHOT"
    assert "credit score" in d["finder"]["vocabulary"]
    print("flip applied + config validates: niche=money, 4 money sources, "
          f"content_since={d['scheduler']['content_since']}")

    try:
        from factory import notify
        notify.notify(
            "CHANNEL FLIPPED TO MONEY/BUSINESS",
            "Clips now come from CalebHammer/Ramsey/DOAC/MFM, and videos already "
            "say MUGSHOT #N on screen. YOUR ONE MANUAL STEP in YouTube Studio "
            "(Customisation > Branding + Basic info):\n"
            "  1. Name: Money Mugshots\n"
            "  2. Handle: @moneymugshots (verified free 2026-07-19)\n"
            "  3. Picture: assets/brand/logo_800.png\n"
            "  4. Banner: assets/brand/banner_2048.png\n"
            "Until you do it the videos are branded and the channel is not.")
    except Exception:  # noqa: BLE001
        pass
    return True


if __name__ == "__main__":
    sys.exit(0 if flip() else 1)
