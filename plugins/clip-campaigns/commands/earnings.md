---
description: Read submission statuses, tracked views and earnings from Vyro and Clipping.net into a report
argument-hint: "[vyro|clipping|all]"
---

Load the `clip-campaigns` skill and read `campaigns/site-notes.md` if it exists.

Site: `$ARGUMENTS` (default `all`). Read-only — never request a payout or open payment
settings beyond reading a balance.

1. For each site, open its submissions/earnings/balance views in `clip-browser` and
   collect per submission: campaign, post URL, status (pending/approved/rejected and
   reason), tracked views, earned amount. Also collect the account balance/pending
   total.
2. Update each matching `campaigns/<slug>/submissions.json` with the latest status.
3. Write `campaigns/earnings.md`: totals per site and per campaign, effective $ per
   1K views achieved, rejections with their reasons, and submissions stuck in
   pending unusually long.
4. Reply with a short summary and what to change: e.g. rejection patterns to feed
   back into the next brief, campaigns worth doubling down on, campaigns whose budget
   is about to run out.
