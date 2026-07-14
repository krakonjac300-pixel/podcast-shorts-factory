# Niche transition: Football (World Cup) → Money & Business

**Decision (2026-07-15):** football was a World Cup growth play, not the durable
niche. Owner's priority = **growth + monetization only** (topic-agnostic, automated).
Money/business is the pick: highest ad rates on YouTube, endless public-podcast
supply, evergreen, and money-drama is wildly viral on Shorts.

**Timeline:**
- **Now → Jul 19 (World Cup final):** stay FOOTBALL — it's live and working (Trent
  21k, 63 watch-hours banked toward YPP). Don't kill a running growth engine.
- **Jul 20:** FLIP to money/business (steps below). Fresh algorithm re-seed; at ~6
  subs the football audience is negligible, so the switch costs ~nothing.

## The flip (3 config edits + branding) — apply Jul 20

**1. `finder.niche_lock`:** `"football"` → `"money"`  (code already supports it —
keeps money terms, drops sport/health).

**2. `scheduler.sources`:** replace the football podcasts with money/business ones
that have rich, clippable, PUBLIC episodes:
```yaml
scheduler:
  sources:
    - "https://www.youtube.com/@CalebHammer/videos"       # Financial Audit — debt trainwreck DRAMA, gold for shorts
    - "https://www.youtube.com/@TheDiaryOfACEO/videos"     # rags-to-riches, $-success stories, big names
    - "https://www.youtube.com/@TheRamseyShow/videos"      # Dave Ramsey — debt-free screams, confrontation
    - "https://www.youtube.com/@myfirstmillionpod/videos"  # business ideas, "how they made $X"
```
(CalebHammer + Ramsey = confrontation/emotion = the money equivalent of the Keane/
Trent drama that won for us.)

**3. `finder.selection_brief`:** swap in the money brief:
```
MONEY/BUSINESS CHANNEL. Growth+monetization only. Our #1 lesson carries over:
EMOTION + a shocking STAKE wins. For money that means:
STRONGLY FAVOR: a shocking NUMBER stated out loud ("$300k in debt", "makes $2M a
year at 24", "spent $80k on that"), financial trainwrecks / people confronted about
terrible money habits, rags-to-riches, wealth flexes, "you're doing money wrong",
controversial money takes ("renting is smarter", "college is a scam"), raw emotion
(someone breaking down over debt, or bragging). The opening line must contain a
number or a bold claim.
AVOID: dry theory, slow setups, generic advice with no number/stakes, non-money
tangents (sport, health) — the niche-lock drops those.
Target 20-40s, open ON the number/claim, end on a comment-bait question
("Would you do this?", "Is this crazy or smart?").
```

**4. Branding (owner applies in YouTube Studio):** a money name + matching logo.
Name ideas: **"Money Unfiltered"**, **"The Money Cut"**, **"Bag Talk"**, **"Broke to
Rich"**. Logo: swap the football mark for a money mark (💸/📈 on bold green/black).
Assistant can generate the avatar; owner uploads it.

## Unchanged (niche-agnostic — all keeps working)
Editor, Finishing-Editor QA, Compiler (long-form), montage, scheduler, self-healing,
Manager/Trainer/Community loops. Only the SOURCE + what counts as "on-niche" + the
brand change. The machine is the asset; we just point it somewhere more profitable.
