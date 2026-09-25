---
description: Scan campaigns on Vyro and/or Clipping.net, save them, and rank the best fits for the factory
argument-hint: "[vyro|clipping|all] [extra filter, e.g. 'podcasts only']"
---

Load the `clip-campaigns` skill and read `campaigns/site-notes.md` if it exists.

Arguments: `$ARGUMENTS` (first word picks the site, default `all`; the rest is an
optional filter in plain language).

1. For each selected site, open its campaign list with the `clip-browser` tools. If
   not logged in, stop and tell the user to run `/clip-campaigns:login`.
2. Collect every active campaign across all relevant tabs/pages. Open a campaign's
   detail page only when the list doesn't show the rate, budget or platforms.
   Read-only: do not click join/apply/submit.
3. Write/merge the records into `campaigns/index.json` using the schema in the skill
   (merge on `id`, update `scraped_at`, keep campaigns that disappeared but mark
   `status: "ended"`). Create the `campaigns/` folder if needed.
4. Read `config.yaml` (niche, `uploader.platforms`) and rank campaigns with the
   skill's ranking rules plus the user's filter.
5. Reply with a table of the top ~10: platform, name, rate per 1K, budget left,
   allowed platforms, fit (1-5), and a one-line why. Flag traps (budget nearly spent,
   platform the user doesn't post to, rules the factory can't meet). Suggest
   `/clip-campaigns:brief <id>` for the best one.
6. Append any new layout knowledge to `campaigns/site-notes.md`.
