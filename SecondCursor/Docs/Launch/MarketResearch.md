# SECOND CURSOR: Steam Market Research and Launch Pricing

Prepared 2026-09-29. Companion to `MarketingPack.md` (store copy, trailer, capsule brief) and `../StoryBible.md`.

**How to read this document.** Facts carry a source link (full list in Section 8). Anything labeled **(est.)** is an estimate, and the method or third party behind it is named. **Inference** marks reasoning where no direct source exists. Steam prices, release dates and review counts were pulled from Steam's public store and review APIs on 2026-09-29 (US store, all languages, all purchase types).

---

## 0. Decision summary

| Decision | Recommendation |
|---|---|
| Launch price (full game: 3 nights, 60 to 75 min, multiple endings) | **$6.99 USD** |
| Launch discount | **15% for 7 days ($5.94)**. Steam allows 10% to 40%; do not exceed 20%. |
| Current 15-minute build (Night 1) | **Free demo on the main game's app**, with its own demo page and demo reviews enabled. Not a paid release and not a separate free prologue app. |
| Release the paid game now? | **No.** A paid release now forfeits Next Fest eligibility (release must come after the fest) and burns Steam's one-time new-release visibility on a 15-minute product that every buyer can finish and refund. |
| Next Fest | **February 22 to March 1, 2027.** Register by January 10, 2027. Demo and store page final by January 25 for the Press Preview. The October 2026 fest closed registration on August 31. |
| Launch window | **Thursday, April 15, 2027** (after the Spring Sale, March 18 to 25). Plan B: June 2027 Next Fest (June 14 to 21), launch in early September 2027. |
| Demo go-live | **Monday, October 26, 2026** (start of Steam Scream V and Halloween week), no later than Thursday, October 29. |
| Wishlist targets at launch | Floor 7,000 (Popular Upcoming entry). Goal 10,000 to 15,000. Stretch 30,000+. |
| Regional pricing | Valve's refreshed 2026 recommended matrix, then a manual check of Turkey, Argentina, Poland, Brazil and CIS. |
| Top 5 tags | Psychological Horror, Horror, Retro, Pixel Graphics, 1990's (full ordered list of 20 in Section 5). |

**Top 3 comparables for pricing:**

| Game | US price | Length | Steam reviews | Sales signal |
|---|---|---|---|---|
| KinitoPET (2024) | $5.99 | about 2 h | 14,784 (93% positive) | 296k to 444k units (est., reviews x 20 to 30); third-party estimates $1.5M to $3.5M gross |
| Iron Lung (2022) | $7.99 (raised from $6 in Nov 2023) | about 1 h | 11,291 (92%) | SteamSpy 200k to 500k owners (est.); about $1.5M revenue (third-party est.) |
| The Exit 8 (2023) | $3.99 | 45 to 60 min | 11,653 (93%) | 3M+ units across all platforms (publisher, Sept 2026) |

---

## 1. Method and data quality

- **Steam data (fact):** `store.steampowered.com/api/appdetails` (price, release date, developer, short description) and `store.steampowered.com/appreviews` (review totals), pulled 2026-09-29.
- **Owner ranges (est.):** SteamSpy API. SteamSpy ranges are statistical estimates and are often wide.
- **Units from reviews (est.):** Steam reviews x 20 (low) to x 30 (high). The common "Boxleiter" baseline for recent releases is about 30 sales per review ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/boxleiter-method-explained)). For this niche the ratio looks lower: Mouthwashing announced 500,000 Steam sales when it had nearly 24,000 reviews, about 21 sales per review ([GamesRadar](https://www.gamesradar.com/games/horror/after-hitting-500-000-copies-sold-and-nearly-24-000-reviews-on-steam-viral-usd13-horror-game-mouthwashing-is-coming-to-consoles-this-year/)). GameDiscoverCo's newer "NB number" averaged about 63 for 2025 releases, so the high end may still be conservative for some games.
- **Gross revenue (est.):** units x current US price x 0.75 (effective price after discounts and regional pricing, per the Boxleiter convention). This is before Steam's 30% cut, refunds and taxes.
- **Blocked sources:** Gamalytic, VG Insights (Sensor Tower), SteamDB and HowLongToBeat block automated fetching (HTTP 403 or 429). Figures from them are quoted from search-indexed summaries and labeled as third-party estimates.
- **Game length:** from HowLongToBeat as quoted by secondary sites, TrueSteamAchievements, Steam discussions and reviews. Lengths marked "unverified" were not confirmed in this pass.
- **Known distortions:** review multipliers break for games that sell mostly on console or in Japan (The Exit 8 has 3M+ units but only 11,653 Steam reviews). All comparables are survivors; the median horror release earns far less (Section 2.5).

---

## 2. The niche and its demand signals

### 2.1 Where SECOND CURSOR sits

SECOND CURSOR sits where three clusters overlap:

1. **Interface or desktop meta-horror** (the UI is the world): KinitoPET, Buddy Simulator 1984, Pony Island, Inscryption (in part), Doki Doki Literature Club, Welcome to the Game I to III, Desktop Explorer, N-OS: Desktop Story. Non-horror relatives: Hypnospace Outlaw, Emily is Away, NEEDY STREAMER OVERLOAD, The Operator, Windowkill.
2. **One-sitting short horror** (under 2 hours): Iron Lung, The Exit 8, Buckshot Roulette, Unsorted Horror, Fears to Fathom episodes, Chilla's Art, I'm on Observation Duty, Bad Parenting 1.
3. **Job or night-shift horror** (boring work turns wrong): Papers, Please; Do Not Feed the Monkeys; No, I'm not a Human; I'm on Observation Duty; Welcome to the Game.

The hook (a hostile cursor that follows the same input rules as the player, plus a security camera that shows the player's own chair) has no direct equivalent among these titles (Inference, consistent with the gap analysis in `MarketingPack.md`).

### 2.2 Core comparables: short or desktop horror

Prices are current US list prices on 2026-09-29. Units and revenue are estimates (Section 1).

| Game | Released | US price | Length (approx.) | Steam reviews | Positive | SteamSpy owners (est.) | Units from reviews (est.) | Reported or third-party figures |
|---|---|---|---|---|---|---|---|---|
| KinitoPET | Jan 2024 | $5.99 | about 2 h (HLTB) | 14,784 | 93% | 200k to 500k | 296k to 444k | About 280k units and $1.5M gross by mid-2025 (games-stats, est.); $3.47M gross, $1.02M net (Steam Revenue Calculator, est.) |
| Iron Lung | Mar 2022 | $7.99 (was $6 until Nov 2023) | about 1 h | 11,291 | 92% | 200k to 500k | 226k to 339k | About $1.5M revenue (VG Insights, est.) |
| The Exit 8 | Nov 2023 | $3.99 | 45 to 60 min | 11,653 | 93% | 200k to 500k | 233k to 350k (undercounts) | 3M+ units, all platforms (Gematsu, Sept 2026) |
| Buckshot Roulette | Apr 2024 | $2.99 | 15 to 30 min main mode, replayable | 126,612 | 95% | 2M to 5M | 2.5M to 3.8M | 1M in two weeks, 2M by July 2024 (developer); about 3.1M units and $8.2M gross (third-party est.) |
| Mouthwashing | Sep 2024 | $12.99 | about 3 h (HLTB) | 38,848 | 95% | 500k to 1M | 777k to 1.17M | 300k by Jan 2025, 500k by Feb 2025 (developer) |
| Pony Island | Jan 2016 | $4.99 | 2 to 4 h | 18,152 | 95% | 500k to 1M | 363k to 545k | |
| Buddy Simulator 1984 | Feb 2021 | $9.99 | 5 to 6 h per ending | 4,365 | 93% | 200k to 500k | 87k to 131k | Had a Steam demo and a Game Jolt demo |
| Unsorted Horror | Aug 2023 | Free | about 1.5 h (5 games of 10 to 15 min) | 3,723 | 97% | n/a | n/a | Same developer as Buckshot Roulette (Mike Klubnika) |
| Welcome to the Game | Jun 2016 | $4.99 | unverified | 2,596 | 84% | 100k to 200k | 52k to 78k | |
| Welcome to the Game II | Apr 2018 | $9.99 | unverified | 3,536 | 84% | 100k to 200k | 71k to 106k | |
| Welcome to the Game III | Jul 2026 | $19.99 | about 11 h | 695 | 78% | n/a | 14k to 21k | Mostly Positive after 10 weeks |
| Desktop Explorer | Jul 2026 | $17.99 | 8 to 10 h (reported) | 2,306 | 96% | n/a | 46k to 69k | Demo Oct 2025; June 2026 Next Fest; featured in Polygon's best Next Fest demos; "100k+ wishlists" reported but unverified |
| N-OS: Desktop Story | Aug 2026 | $5.99 | unverified | 0 | n/a | n/a | n/a | No Steam reviews about 8 weeks after release |

### 2.3 One-hour horror price anchors

| Game | Released | US price | Steam reviews | Positive | Units from reviews (est.) |
|---|---|---|---|---|---|
| Fears to Fathom: Home Alone (Ep. 1) | Jul 2021 | Free | 11,162 | Very Positive | n/a |
| Fears to Fathom: Carson House | Jan 2023 | $4.99 | 2,950 | 92% | 59k to 88k |
| Fears to Fathom: Ironbark Lookout | Oct 2023 | $9.99 | 6,450 | Very Positive | 129k to 194k |
| Fears to Fathom: Woodbury Getaway | Sep 2024 | $9.99 | 3,648 | Very Positive | 73k to 109k |
| Fears to Fathom: Scratch Creek | Jun 2026 | $7.99 | 8,375 | Mostly Positive | 168k to 251k |
| Chilla's Art: The Closing Shift | Mar 2022 | $5.99 | 3,222 | 88% | 64k to 97k |
| I'm on Observation Duty | Apr 2019 | $2.99 | 2,258 | 95% | 45k to 68k |
| Bad Parenting 1: Mr. Red Face | Oct 2024 | $1.99 | 3,100 | 86% | 62k to 93k |
| Anemoiapolis: Chapter 1 | Mar 2023 | $8.99 | 1,207 | 81% | 24k to 36k |
| IMSCARED | Feb 2016 | $3.99 | 3,994 | 95% | 80k to 120k |

Episode lengths for these titles are about one hour each by common report but were not individually verified.

### 2.4 Broader reference set (longer or larger games)

| Game | Released | US list price | Steam reviews | Positive | SteamSpy owners (est.) | Reported figures |
|---|---|---|---|---|---|---|
| Inscryption | Oct 2021 | $19.99 | 148,817 | 97% | 2M to 5M | 1M sold by Jan 2022 (Devolver) |
| Doki Doki Literature Club! | Oct 2017 | Free | 228,460 | 96% | 5M to 10M | |
| Doki Doki Literature Club Plus! | Jun 2021 | $14.99 | 32,541 | 97% | n/a | |
| Papers, Please | Aug 2013 | $9.99 | 80,632 | 97% | 2M to 5M | |
| NEEDY STREAMER OVERLOAD | Jan 2022 | $15.99 | 58,300 | 94% | 1M to 2M | 3M+ units, all platforms (publisher, Nov 2025) |
| The Operator | Jul 2024 | $13.99 | 8,889 | 92% | 500k to 1M | |
| Hypnospace Outlaw | Mar 2019 | $19.99 | 5,578 | 97% | 200k to 500k | |
| Emily is Away | Nov 2015 | Free | 32,336 | 89% | 1M to 2M | Free first game, paid sequels |
| Emily is Away Too | May 2017 | $4.99 | 6,246 | 93% | 200k to 500k | |
| Emily is Away <3 | Apr 2021 | $9.99 | 6,803 | 91% | n/a | |
| Do Not Feed the Monkeys | Oct 2018 | $15.99 | 12,785 | 94% | 500k to 1M | |
| Lost in Vivo | Nov 2018 | $11.99 | 4,006 | 91% | 200k to 500k | |
| Please, Touch The Artwork | Jan 2022 | $9.99 | 510 | 84% | 0 to 20k | |
| Please, Touch The Artwork 2 | Feb 2024 | Free | 6,203 | 99% | n/a | Donation DLC at $1.99 and $2.99 |
| Windowkill | Feb 2024 | $4.99 | 3,743 | 96% | 200k to 500k | Uses real OS windows as the arena |
| No, I'm not a Human | Sep 2025 | $14.99 | 31,051 | 93% | n/a | |
| Look Outside | Mar 2025 | $9.99 | 14,906 | 98% | n/a | |

### 2.5 What went viral and why

- **KinitoPET:** built around fourth-wall moments that feel personal ("it knows me"). YouTube is full of reaction compilations of the game appearing to show creators' own faces, and the official plush sold out within two weeks of its June 2024 release. Lesson: a character plus personal scares make reaction clips. SECOND CURSOR's equivalent is the CAM 03 arm that follows the player's real mouse, delivered without touching the real computer.
- **Iron Lung:** a one-sentence premise and a single oppressive mechanic, sold openly as a short game. Sales spiked around the Titan submersible news in June 2023. Markiplier's self-financed film adaptation opened January 30, 2026 and grossed $51.2M on a $3M budget, which keeps short indie horror in mainstream conversation. Lesson: a premise that fits in one sentence travels.
- **The Exit 8:** anomaly spotting turns stream chat into co-players, the $3.99 price removes friction, and it spawned a wave of imitators. 3M+ units across platforms. Lesson: participation beats spectacle for streams.
- **Buckshot Roulette:** the developer first built an audience with the free Unsorted Horror collection, then released a tense 20-minute loop for $2.99 that sold 1M copies on Steam in two weeks. Lesson: free work builds a following, and one clear mechanic spreads.
- **Mouthwashing:** a 3-hour story with a strong fandom sold 500k at $12.99 within five months. Lesson: for acclaimed narrative horror, price is not the main barrier.
- **Desktop Explorer (2026):** a fake-OS mystery with psychological horror elements. Demo in October 2025, June 2026 Next Fest (Polygon best demos), launch in July 2026 at $17.99, then 2,306 reviews at 96% in about 10 weeks. Lesson: the fake-OS audience is active in 2026 and demos work for the format.
- **Counter-signals:** N-OS: Desktop Story ($5.99, August 2026) has no Steam reviews after about 8 weeks, so the format alone does not sell. Welcome to the Game III ($19.99, July 2026) sits at 78% positive on 695 reviews, so a higher-priced sequel in this niche converts modestly.

### 2.6 Demand signals summary

- Horror was the #1 genre in the top 3% of Steam sellers for three years running, and 6.5% of 2022 horror releases reached 1,000+ reviews versus 2.2% of platformers ([How To Market A Game, 2023](https://howtomarketagame.com/2023/10/02/every-indie-game-developer-should-make-a-horror-game/)). The same analysis notes players accept 30 to 60 minute horror sessions.
- Horror shows strong day-one wishlist conversion ([Steam Page Analyzer, July 2026](https://www.steampageanalyzer.com/blog/how-many-wishlists-before-launch)).
- Indie hits cluster at $5 to $15, and median launch prices of top Steam releases have fallen since late 2023 ([GameDiscoverCo, Nov 2025](https://newsletter.gamediscover.co/p/are-steam-game-prices-dropping-and)).
- The fake-OS sub-niche had three notable 2026 releases (Desktop Explorer, N-OS, Welcome to the Game III): demand exists, and so does competition.
- Streamers drive discovery in this niche. KinitoPET, Iron Lung, Buckshot Roulette and The Exit 8 all broke out through creators, not paid ads (Inference from the coverage cited above).

### 2.7 Contrarian evidence

- In the same 2022 dataset, median horror game revenue was $1,200 ($13,271 for horror priced above $9.99). Most horror games do not break out.
- Every comparable above is a survivor. The desktop-horror format is not a guarantee (N-OS).
- Review multipliers can mislead in both directions (The Exit 8 undercounts; bundles and giveaways inflate SteamSpy).

---

## 3. Pricing

### 3.1 Price versus length

Price per 10 minutes of a first playthrough (lengths from Section 2):

| Game | Length | US price | Price per 10 min |
|---|---|---|---|
| The Exit 8 | 45 to 60 min | $3.99 | $0.67 to $0.89 |
| Iron Lung | reports range from under 1 h to 1 h 35 min | $7.99 | about $0.84 to $1.33 |
| KinitoPET | about 2 h | $5.99 | about $0.50 |
| Mouthwashing | about 3 h | $12.99 | about $0.72 |
| Pony Island | 2 to 4 h | $4.99 | $0.21 to $0.42 |
| Buckshot Roulette | 15 to 30 min plus replay | $2.99 | not meaningful (replay game) |
| **SECOND CURSOR at $4.99** | 60 to 75 min | $4.99 | $0.67 to $0.83 |
| **SECOND CURSOR at $5.99** | 60 to 75 min | $5.99 | $0.80 to $1.00 |
| **SECOND CURSOR at $6.99** | 60 to 75 min | $6.99 | $0.93 to $1.17 |
| **SECOND CURSOR at $7.99** | 60 to 75 min | $7.99 | $1.07 to $1.33 |
| **SECOND CURSOR at $9.99** | 60 to 75 min | $9.99 | $1.33 to $1.67 |

Findings:

- One-sitting horror (under 1.5 h) clusters at $1.99 to $7.99, most often $2.99 to $5.99.
- **$7.99 for about an hour is the observed ceiling.** Iron Lung reached it by raising the price from $6 on November 2, 2023, which drew public criticism.
- 2 to 3 hour narrative horror sells at $5.99 (KinitoPET) to $12.99 (Mouthwashing).
- Desktop and fake-OS games priced above $10 run 5 to 11 hours (Buddy Simulator 1984, The Operator, Desktop Explorer, Welcome to the Game III).
- A 2026 pricing guide maps $4.99 to 1 to 3 hour narrative games and $9.99 to 4 to 8 hours of play ([Steam Page Analyzer, Feb 2026](https://www.steampageanalyzer.com/blog/steam-pricing-strategy)).

### 3.2 Value corridor (pricing-strategy method, adapted from SaaS to a premium game)

- **Floor (next-best alternative):** free (Unsorted Horror, Doki Doki Literature Club, Fears to Fathom Ep. 1, any free demo) and the $2.99 to $3.99 hits (Buckshot Roulette, The Exit 8).
- **Ceiling (perceived value for about an hour of horror):** Iron Lung at $7.99, Fears to Fathom: Scratch Creek at $7.99 (2026).
- **Corridor for 60 to 75 minutes:** $4.99 to $7.99. The recommendation sits in the upper middle.

### 3.3 Price point psychology

- **$4.99:** impulse tier; a 50% sale lands at $2.49. Lowest revenue per unit, and it signals "small or jam game".
- **$5.99:** the KinitoPET price. Players in this niche will compare against KinitoPET, which is longer (about 2 h).
- **$6.99:** stays under $7 (left-digit bias), a 50% sale lands at $3.49, and a 15% launch discount lands under $6 ($5.94). It reads as a step above a jam game while staying below Iron Lung.
- **$7.99:** Iron Lung's price. Defensible only at 90+ minutes or with an established audience.
- **$9.99:** the most common indie price point, but players expect 4 to 8 hours at it. At 60 to 75 minutes it invites "too short" reviews and refunds.
- Wishlist conversion drops above $10: a median of 0.10x first-week conversion versus 0.15x overall ([GameDiscoverCo, Oct 2025](https://newsletter.gamediscover.co/p/the-state-of-steam-wishlist-conversions)). Staying well under $10 matters more than the step from $5.99 to $6.99 (Inference).

### 3.4 Recommendation: $6.99 USD

1. It sits in the upper middle of the $4.99 to $7.99 corridor, matching a polished game with unusual mechanics and multiple endings.
2. Price per minute falls between The Exit 8 and Iron Lung, both Very Positive at their prices.
3. Estimated net per unit is about $3.23 at $6.99 versus $2.77 at $5.99 and $2.31 at $4.99 (est.: list x 0.75 effective price, minus 12% refunds, minus Steam's 30%). That is 40% more per unit than $4.99, and nothing shows conversion falling that much between $4.99 and $6.99 for streamer-driven horror (Inference).
4. It leaves room for a launch price under $6 and a 50% sale at $3.49 that is still worth running.
5. It avoids a later price increase (Iron Lung's raise drew backlash, and any increase triggers a 30-day discount cooldown).

**Decision rules once the 3-night build is timed by fresh playtesters:**

| Median first playthrough | Price |
|---|---|
| Under 50 min | $4.99 to $5.99 |
| 50 to 90 min (current plan: 60 to 75 min) | **$6.99** |
| 90 min or more, with endings that reward a replay and strong demo sentiment | $7.99 |
| Any length at this scope | Never $9.99 or more |

Demo signals to watch before locking the price: the share of demo reviews that mention length or price, and whether players describe Night 1 as "complete" or "a teaser".

### 3.5 Launch discount and discount ladder

- **Steam rules:** a launch discount of 10% to 40% that runs 7 to 14 days; no other discount for 30 days after release; any price increase triggers a 30-day discount cooldown; themed fests follow the cooldown with no exceptions ([Steamworks Discounting](https://partner.steamgames.com/doc/marketing/discounts), [Game Developer](https://www.gamedeveloper.com/business/steam-devs-can-now-tweak-the-length-of-launch-discounts), [Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-halloween-sale-2026)).
- **Norm:** 10% to 20% for indies, with 15% as the typical choice ([Steam Page Analyzer, Feb 2026](https://www.steampageanalyzer.com/blog/steam-pricing-strategy)).
- **Recommendation:** 15% for 7 days, which gives $5.94 and covers the first weekend. Fallback: 10% ($6.29).
- **Ladder after launch (Inference):** Summer Sale 2027 (June 24 to July 8) at 20% to 25%; Scream Fest 2027 at 30% to 40%; 50% only after about 12 months or alongside a content update.

### 3.6 Regional pricing

- Valve refreshed its recommended regional price matrix in 2026, the first refresh since October 2022, and now offers exchange-rate, purchasing-power and hybrid conversions ([SteamDB](https://steamdb.info/blog/valve-price-matrix-2026-update/)).
- Typical bands versus USD ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-regional-pricing-guide)): EUR and GBP roughly 1:1 by convention; CIS 40% to 60% lower; Brazil and South America 50% to 70% lower; China 40% to 60% lower; Japan and Korea 10% to 20% lower; Turkey about 25% to 30% of the US price; Argentina 75% to 85% lower. Turkey and Argentina moved to regionalized USD pricing in November 2023.
- **Action:** apply Valve's recommendation, then review Poland (GameDiscoverCo advised going 20% to 25% below Valve's older suggestion; re-check against the 2026 matrix, because that advice predates it: [GameDiscoverCo](https://newsletter.gamediscover.co/p/does-steam-have-its-regional-pricing)). Round to clean local price points.
- Why it matters: an estimated 30% to 40% of indie revenue comes from outside the US and Western Europe ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-pricing-strategy)).
- Language note: the typed conversation in the in-game text editor (Jotter) matches English keywords, so non-English players get a weaker version of the core mechanic. List English only at launch and see the risk table (Section 6.4).

### 3.7 Price for the current 15-minute build

- **As a demo: free.** **As a separate prologue: also free.** Do not sell it.
- Why not paid: at 15 minutes every buyer can finish and refund within Steam's 2-hour window; a paid release now makes the game "released", which ends Next Fest eligibility and spends the launch visibility; and "too short" reviews would stay on the main app permanently.
- For reference only: 15 to 30 minute horror sells at $1.99 to $2.99 (Bad Parenting 1 at $1.99; Buckshot Roulette at $2.99, but with heavy replay value). This is not recommended for SECOND CURSOR.

---

## 4. Launch strategy

### 4.1 Free demo or free prologue: decision

**Recommendation: ship Night 1 as the demo of the full game.** `MarketingPack.md` reaches the same conclusion from the Next Fest rules.

| Factor | Demo (recommended) | Separate free prologue app |
|---|---|---|
| Where wishlists land | Directly on the main game | On a separate app; players must find and wishlist the main game |
| Steam visibility | Since the July 2024 Great Steam Demo Update, demos get their own store page, optional user reviews, placement in New and Trending and tag pages, and wishlisters are notified at launch ([GamesRadar](https://www.gamesradar.com/games/valve-overhauls-steam-demos-to-act-more-like-full-games-complete-with-user-reviews-and-chart-positions/), [Game World Observer](https://gameworldobserver.com/2024/07/26/steam-game-demos-update-reviews-store-pages-charts)) | Its own free-to-play listing and charts, but that advantage shrank after the 2024 changes; the "prologue trick" is reportedly becoming obsolete ([How To Market A Game](https://howtomarketagame.com/2023/04/03/how-to-use-the-prologue-trick-to-potentially-earn-thousands-of-wishlists/)) |
| Wishlist quality | Players try, then wishlist the product they will buy | Prologue downloads were found not to be a major source of high-quality wishlists; short teaser prologues converted better than long ones ([GameDiscoverCo, Jan 2021, older data](https://newsletter.gamediscover.co/p/steam-prologues-do-they-really-help)) |
| Next Fest | The main game enters with its demo | A prologue or chapter listed as its own product cannot be the Next Fest entry ([Steamworks, Feb 2027](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027)) |
| Cost and effort | One extra build | A $100 app fee and a third build to maintain |
| Best precedents | Buddy Simulator 1984, Desktop Explorer, most Next Fest horror | Episodic series and fully free games: Fears to Fathom Ep. 1 is free (11,162 reviews) and later episodes sell at $4.99 to $9.99 (2,950 to 8,375 reviews each); Emily is Away (free, then paid sequels); Unsorted Horror (free) before Buckshot Roulette |

Revisit a free standalone prologue only if SECOND CURSOR becomes an episodic series. The Story Bible sketches Nights 1 to 7 and four endings, so the Fears to Fathom model is a possible later path.

**Demo design notes:**

- Keep Night 1 as it is (10 to 15 min), ending on the blackout and the end card with the Wishlist button (already implemented according to `MarketingPack.md`).
- Enable user reviews on the demo page. Demo reviews at 90%+ act as social proof before launch.
- Keep CAM 03 in the demo, because it is the most clippable moment. Nights 2 and 3 must then escalate with new mechanics rather than reuse the tug-of-war, and the store page should say so without spoilers.
- Optional (Inference, feasibility unchecked because demo and full game have different app IDs): if the full game can detect a finished demo save, let the second cursor acknowledge it ("you came back"). That is a safe version of the "it remembers me" beat that made KinitoPET spread.
- Mirror the demo on itch.io and Game Jolt. Buddy Simulator 1984 ran a Game Jolt demo, and Buckshot Roulette began on itch.io.

### 4.2 Timeline

| Date | Action |
|---|---|
| This week (by Fri, Oct 2, 2026) | Pay the $100 Steam Direct fee if not already paid (30-day wait before a release is possible). Publish the Coming Soon page; it must be public for at least 2 weeks before release and is needed for wishlists and themed-fest invitations ([Steamworks Release Process](https://partner.steamgames.com/doc/store/releasing)). |
| Oct 1 to 8 | Steam Autumn Sale. No action. |
| Oct 19 to 26 | October Next Fest. Not available (registration closed Aug 31). Do not launch the demo into this week's demo flood (Inference). |
| **Mon, Oct 26, 2026** | **Demo goes live.** Steam Scream V runs Oct 26 to Nov 2 and has Coming Soon and demo sections; unreleased games need no discount. Opt in if invited (invitations usually arrive about six weeks ahead, so a newly public page may miss this one). Pitch horror creators for Halloween week. |
| Nov to Dec | Iterate the demo from feedback and reviews. Grow a Discord and mailing list. Run creator outreach. |
| Dec 8 | Valve's Next Fest Q&A (optional). |
| Dec 17 to Jan 4 | Steam Winter Sale. |
| **Sun, Jan 10, 2027** | **Next Fest registration deadline** (11:59 pm PST). |
| Jan 14 to 18 | Desktop Companion Fest. **Not eligible:** it is for idle companion apps that sit on part of the real desktop. |
| Jan 18 | Steam pulls trailers for the official Next Fest video. |
| **Jan 25** | **Demo and store page final for the Press Preview.** |
| Feb 8 | All Next Fest items due. |
| Feb 11 | Press Preview begins. Creators can play the demo early. |
| **Feb 22 to Mar 1** | **Steam Next Fest.** |
| Mar 18 to 25 | Spring Sale. Do not launch into it (Inference). |
| **Thu, Apr 15, 2027** | **Launch at $6.99 with 15% off for 7 days.** |
| From May 15 | First non-launch discount allowed (30 days after release). |
| Jun 24 to Jul 8 | Summer Sale: first seasonal discount. |
| Late Oct 2027 | Scream Fest 2027 (dates to be announced): deeper discount. |

**Plan B (if Nights 2 and 3 slip):** enter the June 14 to 21, 2027 Next Fest and launch in early September 2027 (for example Thursday, September 9), so the game is past its 30-day discount lock in time to take part in Scream Fest 2027.

Sources: [Steamworks Next Fest Oct 2026](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/2026october), [Steamworks Next Fest Feb 2027](https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027), [Steamworks upcoming events](https://partner.steamgames.com/doc/marketing/upcoming_events), [Steam Page Analyzer on Scream V](https://www.steampageanalyzer.com/blog/steam-halloween-sale-2026), [Steamworks Desktop Companion Fest 2027](https://partner.steamgames.com/doc/marketing/upcoming_events/themed_sales/desktop_companion_2027).

### 4.3 Wishlist targets and sales scenarios

**Targets at launch:**

- **Floor: 7,000.** This is the reported entry point for Steam's Popular Upcoming list ([Steam Page Analyzer, July 2026](https://www.steampageanalyzer.com/blog/how-many-wishlists-before-launch)).
- **Goal: 10,000 to 15,000.** Above Zukowski's "Silver" tier (8,000), where visibility features start to work (same source).
- **Stretch: 30,000+.**

**Internal checkpoints** (planning targets, not benchmarks): 1,500 by Dec 31, 2026 (demo plus Scream V); 3,000 by Jan 25, 2027 (Press Preview); 7,000+ after Next Fest; 10,000+ on launch day. One data point supports going early: games with a demo live months before Next Fest reportedly earned about 2.5x the median wishlists of demos released in fest week ([Steam Page Analyzer](https://www.steampageanalyzer.com/blog/steam-next-fest-february-2027)).

**Week-one scenarios (est.).** Conversion bands: median 0.15x first-week sales per wishlist ([GameDiscoverCo](https://newsletter.gamediscover.co/p/the-state-of-steam-wishlist-conversions)) to 0.20x for 5k to 40k wishlists (Steam Page Analyzer). Net per unit at the $5.94 launch price is about $2.74 (x 0.75 effective price, minus 12% refunds, minus Steam's 30%).

| Wishlists at launch | Week-one units (est.) | Week-one net revenue (est.) |
|---|---|---|
| 3,000 (downside) | about 450 | about $1.2k |
| 7,000 (floor) | 1,050 to 1,400 | $2.9k to $3.8k |
| 12,000 (goal) | 1,800 to 2,400 | $4.9k to $6.6k |
| 30,000 (stretch) | 4,500 to 6,000 | $12.3k to $16.5k |
| 50,000 (breakout) | 7,500 to 11,500 | $20.6k to $31.6k |

Creator-driven breakouts do not follow wishlist math. GameDiscoverCo's 2024 to 2025 data shows viral titles converting at 6x to 13x+ the expected rate. A KinitoPET-scale outcome (hundreds of thousands of units) is possible in this niche but should not be planned for.

### 4.4 Bundles and add-ons

- **Supporter Pack DLC at launch ($2.99, Inference):** this niche commonly carries a small add-on: N-OS Supporter Pack at $2.99, donation DLC for Please, Touch The Artwork 2 at $1.99 and $2.99, soundtracks at $1.99 (Iron Lung, Pony Island) and $2.99 (Windowkill). Because the game's audio is procedural, a soundtrack may be thin; a "Letheworth Employee Handbook" digital artbook or lore PDF is a cheaper option. Having one DLC enables a "complete the set" bundle on Steam.
- **Cross-developer bundle (from 30+ days after launch, Inference):** propose a "night shift" or "interface horror" bundle with 2 to 4 short games of similar price. Every developer involved has to agree.
- **Future nights:** if Nights 4 to 7 from the Story Bible get made, either ship them as a free update (adds value and review momentum; do not raise the price) or sell them as a second paid "shift" at a similar price with a franchise bundle, following the Fears to Fathom episodic model.

### 4.5 Creator plan (summary)

`MarketingPack.md` covers the clips, trailer and creator notes in detail. Priorities from this research:

1. **Halloween week 2026:** the demo plus the stream-safety line (never touches files, webcam, microphone or the real mouse). Desktop-horror viewers fear that a game will touch their real machine, and a stated guarantee makes the game safe for streamers.
2. **Press Preview window (Feb 11 to 22, 2027):** send creators the demo together with the "five things to try" note.
3. **Launch week:** send keys to the creators who covered the demo first.

---

## 5. Steam tags and short description

### 5.1 Tags (20, ordered by priority)

Steam gives more weight to earlier tags, so genre-defining tags go first and generic tags go last.

| # | Tag | Why |
|---|---|---|
| 1 | Psychological Horror | Top tag on KinitoPET, Mouthwashing, The Exit 8, Buddy Simulator 1984 |
| 2 | Horror | Core genre |
| 3 | Retro | 1998 OS; shared with KinitoPET, Mouthwashing, Papers, Please |
| 4 | Pixel Graphics | Shared with KinitoPET, Iron Lung, Pony Island |
| 5 | 1990's | Setting; shared with KinitoPET, Hypnospace Outlaw |
| 6 | Short | Sets expectations honestly (Iron Lung, Pony Island use it); likely lowers refunds from length surprise (Inference) |
| 7 | Atmospheric | Shared with most comparables |
| 8 | Mystery | Letheworth, employee 017 |
| 9 | Simulation | Office job simulation; shared with KinitoPET, Welcome to the Game |
| 10 | Multiple Endings | Full game has several endings; shared with KinitoPET |
| 11 | Story Rich | Emails, notes, personnel records |
| 12 | Surreal | Shared with Iron Lung, Mouthwashing, The Exit 8 |
| 13 | Point & Click | Mouse-driven UI; `MarketingPack.md` says to test it first, because it can set adventure-game expectations |
| 14 | Dark | Tone |
| 15 | Experimental | Cursor mechanics; shared with Pony Island, Inscryption |
| 16 | Thriller | Escalation structure |
| 17 | 2D | Presentation |
| 18 | Choices Matter | Only if the endings depend on player choices; otherwise swap in "Lore-Rich" |
| 19 | Indie | Generic, kept low |
| 20 | Singleplayer | Generic, kept low |

Avoid: "Hacking" (misleading; `MarketingPack.md` agrees), "Immersive Sim" (players applied it to KinitoPET and The Exit 8, but the genre means something else), "Gore" and "Jump Scare" (not this game's style, and they would attract the wrong audience), "Visual Novel", "FMV".

### 5.2 Short description style that works in this niche

Patterns seen on comparable store pages (paraphrased; Steam short descriptions run to about 300 characters):

1. **Premise, then a twist or denial.** Pony Island's copy ends with one flat line: "It is not a game about ponies."
2. **In-world product voice.** KinitoPET and Buddy Simulator 1984 describe their fictional software as if selling it, which puts the player inside the fiction before the scare.
3. **Honest about length.** Iron Lung's copy opens by calling itself a short horror game, which sets expectations before purchase.
4. **Terse orders.** No, I'm not a Human uses short imperative rules, which read as instructions the player must follow.
5. **Situation plus one line of dread.** Mouthwashing and The Exit 8 use one sentence for where you are and one for what is wrong.

What to take from this (Inference): name the interface in the first clause, show the hook (the second cursor) by the second sentence, and reassure players that nothing touches their real PC (in the long description if there is no room above).

**Assessment of the current copy in `MarketingPack.md` (256 characters):** it follows patterns 5 and 1, puts the hook in sentence one, and ends on the interface premise. Keep it as the primary.

**Alternates to rotate between Next Fest and launch** (original drafts, character counts checked):

- **Alt A, in-world voice (256 characters):** NEXUS OS 4.1, night shift at Letheworth Data Reclamation. Read the mail, archive the files, shred what you are told to shred. Then a second cursor appears on your screen. It is not yours. It wants the file you are deleting, and it pulls harder than you do.
- **Alt B, honest-short (278 characters):** A short retro horror game played entirely on a 1998 office computer. Work the night shift, sort and shred files, and fight a second cursor for control of your own screen. It types back. It remembers how you move. And the security camera shows someone standing behind your chair.

Note: Alt B names the camera payoff. Under the spoiler policy in `MarketingPack.md` it is acceptable (the doorway figure is shown in the trailer), but it must not mention the head turn.

---

## 6. Risks and refunds

### 6.1 Steam's refund rules and short games

- **Policy:** refunds within 14 days of purchase with under 2 hours of playtime, no questions asked. Valve says refunds are not a way to get free games and can stop refunding abusive accounts ([Steam Refunds](https://store.steampowered.com/steam_refunds/)).
- **Typical rate:** most indie releases see about 10% to 12% of sales refunded, and first releases more ([XDA, June 2025](https://www.xda-developers.com/steams-two-hour-refund-window-killing-niche-indie-games/)).
- **Recent case (July 2026):** Paddle Paddle Paddle ($4.99, often $2.99, median playtime under 2 hours) reported over 55,000 refunds, about 21% of sales, while holding a Very Positive rating. Some reviews openly say players finished and refunded. The developer proposed tying the refund window to price or expected playtime. Valve had not responded ([GAMES.GG](https://games.gg/news/paddle-paddle-paddle-refunds-steam-policy/), [Kotaku](https://kotaku.com/steam-indie-short-pc-refund-paddle-paddle-paddle-zoroarts-2000712822), [Dexerto](https://www.dexerto.com/gaming/developer-claims-55000-steam-players-refunded-game-after-beating-it-too-fast-3383390/)).
- **For SECOND CURSOR:** every buyer can finish and still refund. **Plan for 12% to 20% refunds (est.:** low end from the XDA average, high end from the Paddle Paddle Paddle case).

### 6.2 How comparable short games handled it

- **Iron Lung:** says it is short in the first words of its store copy and keeps a low price. The developer publicly defended the value of the game when he raised the price to $7.99.
- **The Exit 8 and Buckshot Roulette:** very low prices ($3.99 and $2.99) plus replay loops (randomized anomalies; endless and multiplayer modes), so a refund saves little and the game keeps pulling players back (Inference).
- **Pony Island and Inscryption:** secrets and meta layers extend play well past the first ending (Pony Island runs 2 to 4 hours).
- **Fears to Fathom:** a free first episode lets players self-select before they pay for later episodes.
- **KinitoPET:** about 2 hours with multiple endings at $5.99, giving strong value per minute.

### 6.3 Refund mitigations for SECOND CURSOR

1. **Demo first.** People who finish the demo know the tone and pacing before buying, so fewer buy by mistake (Inference).
2. **State the length on the store page**, for example "about an hour, three nights, multiple endings", and use the "Short" tag.
3. **Price in the short tier** ($6.99, launch price $5.94).
4. **Make endings visible** (for example "Ending 1 of 3" on the end card) and tie them to player actions, so a second run is tempting.
5. **Add optional depth, not padding:** lore files, Restricted-folder secrets, achievements for alternate outcomes.
6. **Hardware fallback for the tug-of-war.** A mouse yank is hard on laptop trackpads. Add an alternative input (hold a key or repeated clicks) and normalize for high-DPI mice, so nobody refunds because a scene was unplayable on their setup.
7. **Read refund reasons weekly** after launch in Steamworks, and fix the top reason first.

### 6.4 Other risks

| Risk | Evidence | Mitigation |
|---|---|---|
| "Too short for the price" reviews | Iron Lung's price backlash; Paddle Paddle Paddle's refund-focused reviews | $6.99, honest length, demo, visible endings |
| Crowded fake-OS niche in 2026 | Desktop Explorer, N-OS, Welcome to the Game III all released Jul to Aug 2026; N-OS has 0 reviews | Lead every asset with the unique hook (two cursors, one file). The capsule must read at 120x45 (`MarketingPack.md`) |
| Viewers watch instead of buying | Common for short narrative horror (Inference) | Personal reactivity (it replays your own mouse and answers what you type), so a viewer's playthrough differs from the streamer's; three endings |
| English-only typed conversation | The Jotter parser uses English keyword groups (Story Bible, dialogue.json) | English only at launch; localize UI and mail later; add keyword sets for the largest horror-streaming languages if sales warrant |
| Nights 2 and 3 slip past April 2027 | Solo or small-team scope | Plan B (June 2027 Next Fest, September launch); promise three nights only |
| Price-change trap | Iron Lung backlash; a price increase blocks discounts for 30 days | Launch at the intended price; add value with free updates instead |
| The demo spends the best moment | CAM 03 is in Night 1 | Nights 2 and 3 need new mechanics (Story Bible: CAM 04, the door log, Sublevel C) and store copy that promises escalation |
| Photosensitivity | Glitches, static, flash at blackout | Already covered by the notice and the Reduce flashing option in `MarketingPack.md`; run a flash analysis |
| Weak Next Fest showing | Fest rankings reward demos that play smoothly and hook early (Inference) | Polish the first 2 minutes; enable demo reviews and fix complaints before Jan 25 |

---

## 7. Action checklist

1. This week: pay the Steam Direct fee if needed, publish the Coming Soon page with the tags in Section 5.1 and the short description from `MarketingPack.md`, and set the release date to "Coming soon" or "Spring 2027" rather than a fixed date.
2. By Oct 26, 2026: publish the Night 1 demo on the demo page with reviews enabled; opt into Scream V if invited; run the Halloween creator push.
3. Nov to Dec 2026: iterate the demo and track wishlist checkpoints (Section 4.3).
4. By Jan 10, 2027: register for the February Next Fest. By Jan 25: final demo and store page for the Press Preview.
5. Before launch: confirm $6.99 using the decision rules in Section 3.4 (time fresh playtesters on the full 3-night build). Set Valve's 2026 regional recommendations and review Poland, Turkey, Argentina, Brazil and CIS.
6. Launch Thu, Apr 15, 2027 with 15% off for 7 days. Prepare the Supporter Pack DLC for day one.
7. After launch: review refund reasons weekly; take the first seasonal discount in the Summer Sale 2027 and a deeper one in Scream Fest 2027.

---

## 8. Sources

**Steam data (pulled 2026-09-29)**
- Steam store API (prices, release dates, developers, short descriptions): https://store.steampowered.com/api/appdetails
- Steam reviews API (review totals): https://store.steampowered.com/appreviews/
- SteamSpy API (owner ranges and tags, estimates): https://steamspy.com/api.php
- Steam Refunds policy: https://store.steampowered.com/steam_refunds/

**Steamworks rules and events**
- Next Fest October 2026: https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/2026october
- Next Fest February 2027: https://partner.steamgames.com/doc/marketing/upcoming_events/nextfest/feb_2027
- Upcoming Steam events (seasonal sales, Scream V, 2027 fests, Next Fest June 2027): https://partner.steamgames.com/doc/marketing/upcoming_events
- Desktop Companion Fest 2027 eligibility: https://partner.steamgames.com/doc/marketing/upcoming_events/themed_sales/desktop_companion_2027
- Discounting rules: https://partner.steamgames.com/doc/marketing/discounts
- Launch discount length now configurable (Game Developer): https://www.gamedeveloper.com/business/steam-devs-can-now-tweak-the-length-of-launch-discounts
- Release process (Coming Soon page, 30-day wait): https://partner.steamgames.com/doc/store/releasing
- Steam Scream V guide (demos, cooldown): https://www.steampageanalyzer.com/blog/steam-halloween-sale-2026
- Next Fest February 2027 prep (early-demo data point): https://www.steampageanalyzer.com/blog/steam-next-fest-february-2027

**Sales, reviews and viral history**
- KinitoPET revenue estimate (Steam Revenue Calculator): https://steam-revenue-calculator.com/app/2075070/kinitopet
- KinitoPET estimate (games-stats): https://games-stats.com/steam/game/kinitopet/
- KinitoPET plush and wiki: https://kinitopedia.fandom.com/wiki/Kinito_The_Axolotl
- KinitoPET creator reaction compilation: https://www.youtube.com/watch?v=5sVFScw11d0
- Iron Lung price raise (GamesRadar): https://www.gamesradar.com/dev-of-horror-hit-iron-lung-raises-price-from-dollar6-to-dollar8-because-i-want-to-earn-more-money-and-says-if-you-dont-like-it-go-pirate-it-or-something/
- Iron Lung price raise (PCGamesN): https://www.pcgamesn.com/iron-lung/steam-price-increase
- Iron Lung sales spike after the Titan news (PCGamesN): https://www.pcgamesn.com/iron-lung/sales-spike
- Iron Lung on SteamSpy: https://steamspy.com/app/1846170
- Iron Lung on Video Game Insights: https://app.sensortower.com/vgi/game/iron-lung
- Iron Lung film box office (Forbes): https://www.forbes.com/sites/timlammers/2026/05/29/markipliers-iron-lung-arrives-on-streaming-early-following-51-million-run-at-box-office/
- Iron Lung film opening (Variety): https://variety.com/2026/film/box-office/markiplier-cries-iron-lung-indie-film-global-box-office-debut-1236649636/
- Iron Lung length (TrueSteamAchievements): https://truesteamachievements.com/game/Iron-Lung/completiontime
- The Exit 8 tops 3 million (Gematsu): https://www.gematsu.com/2026/09/the-exit-8-sales-top-three-million
- The Exit 8 length (Playgama): https://playgama.com/blog/game-faqs/how-long-does-it-take-to-beat-exit-8/
- Buckshot Roulette 1M in two weeks (GamesRadar): https://www.gamesradar.com/games/horror/team-behind-steams-latest-mega-hit-was-just-joking-when-it-said-it-would-double-our-sales-but-then-its-horror-gambling-game-actually-sold-1-million-copies/
- Buckshot Roulette 2M (Steam news): https://store.steampowered.com/news/app/2835570/view/4271182997389122964
- Buckshot Roulette estimates (Raijin): https://raijin.gg/app/2835570/Buckshot_Roulette
- Buckshot Roulette history (Wikipedia): https://en.wikipedia.org/wiki/Buckshot_Roulette
- Buckshot Roulette length (TrueSteamAchievements): https://truesteamachievements.com/game/Buckshot-Roulette/completiontime
- Mouthwashing 500k (GamesRadar): https://www.gamesradar.com/games/horror/after-hitting-500-000-copies-sold-and-nearly-24-000-reviews-on-steam-viral-usd13-horror-game-mouthwashing-is-coming-to-consoles-this-year/
- Mouthwashing 500k (Game Developer): https://www.gamedeveloper.com/business/mouthwashing-sales-top-500k-copies-ps5-and-switch-ports-in-tow
- Mouthwashing 300k (Steam news): https://store.steampowered.com/news/app/2475490/view/541092629308245391
- Mouthwashing length (GINX): https://www.ginx.tv/en/mouthwashing-how-long-to-beat
- KinitoPET length (IsThereAnyDeal, shows HowLongToBeat): https://isthereanydeal.com/game/kinitopet/info/
- Pony Island length (TrueSteamAchievements): https://truesteamachievements.com/game/Pony-Island/completiontime
- Buddy Simulator 1984 review and length (GameGrin): https://www.gamegrin.com/reviews/buddy-simulator-1984-review/
- Buddy Simulator 1984 demo (Game Jolt): https://gamejolt.com/games/BuddySim1984/470578
- Unsorted Horror (itch.io): https://mikeklubnika.itch.io/unsorted-horror
- Welcome to the Game III (GAMES.GG): https://games.gg/welcome-to-the-game-iii/
- Desktop Explorer launch (COGconnected): https://cogconnected.com/2026/07/desktop-explorer-is-out-now-on-steam-with-a-nostalgic-fake-os-horror-experience/
- Desktop Explorer demo announcement (Recurring Dream): https://recurringdreamstudio.substack.com/p/desktop-explorer-demo-out-now
- Desktop Explorer Next Fest analysis (Venn Studios): https://venn-studios.com/research/next-fest-demos/desktop-explorer/
- NEEDY STREAMER OVERLOAD 3M (VGChartz): https://www.vgchartz.com/article/466223/needy-streamer-overload-sales-top-3-million-units/
- Inscryption 1M (GamesRadar): https://www.gamesradar.com/inscryption-celebrates-one-million-copies-sold-with-a-message-from-its-very-angry-stoat/
- Fears to Fathom Episode 1 free (Steam): https://store.steampowered.com/app/1671340/Fears_to_Fathom__Home_Alone/

**Pricing, wishlists and market data**
- GameDiscoverCo, Steam price trends (Nov 2025): https://newsletter.gamediscover.co/p/are-steam-game-prices-dropping-and
- GameDiscoverCo, wishlist conversions 2024 to 2025 (Oct 2025): https://newsletter.gamediscover.co/p/the-state-of-steam-wishlist-conversions
- GameDiscoverCo, regional pricing recommendations: https://newsletter.gamediscover.co/p/does-steam-have-its-regional-pricing
- GameDiscoverCo, Steam prologues (Jan 2021, older data): https://newsletter.gamediscover.co/p/steam-prologues-do-they-really-help
- SteamDB, 2026 price matrix update: https://steamdb.info/blog/valve-price-matrix-2026-update/
- Steam Page Analyzer, regional pricing guide: https://www.steampageanalyzer.com/blog/steam-regional-pricing-guide
- Steam Page Analyzer, pricing strategy (Feb 2026): https://www.steampageanalyzer.com/blog/steam-pricing-strategy
- Steam Page Analyzer, wishlists before launch (Jul 2026): https://www.steampageanalyzer.com/blog/how-many-wishlists-before-launch
- Steam Page Analyzer, Boxleiter method: https://www.steampageanalyzer.com/blog/boxleiter-method-explained
- How To Market A Game, horror genre data (Oct 2023): https://howtomarketagame.com/2023/10/02/every-indie-game-developer-should-make-a-horror-game/
- How To Market A Game, prologue trick (Apr 2023): https://howtomarketagame.com/2023/04/03/how-to-use-the-prologue-trick-to-potentially-earn-thousands-of-wishlists/
- Great Steam Demo Update 2024 (GamesRadar): https://www.gamesradar.com/games/valve-overhauls-steam-demos-to-act-more-like-full-games-complete-with-user-reviews-and-chart-positions/
- Great Steam Demo Update 2024 (Game World Observer): https://gameworldobserver.com/2024/07/26/steam-game-demos-update-reviews-store-pages-charts

**Refunds**
- XDA on the 2-hour refund window (Jun 2025): https://www.xda-developers.com/steams-two-hour-refund-window-killing-niche-indie-games/
- Paddle Paddle Paddle refunds (GAMES.GG, Jul 2026): https://games.gg/news/paddle-paddle-paddle-refunds-steam-policy/
- Paddle Paddle Paddle refunds (Kotaku): https://kotaku.com/steam-indie-short-pc-refund-paddle-paddle-paddle-zoroarts-2000712822
- Paddle Paddle Paddle refunds (Dexerto): https://www.dexerto.com/gaming/developer-claims-55000-steam-players-refunded-game-after-beating-it-too-fast-3383390/
