---
description: Submit posted clip URLs to a campaign on Vyro or Clipping.net (always asks before the final click)
argument-hint: "<campaign slug> [post url ...]"
---

Load the `clip-campaigns` skill.

Arguments: `$ARGUMENTS` — a campaign slug and optionally one or more post URLs.

1. Gather the URLs to submit: the ones given, else the campaign's clips in
   `campaigns/<slug>/clips.json` joined with the factory's `uploads` table
   (`sqlite3 factory.db "select c.id, u.platform, u.url from uploads u join clips c on c.id=u.clip_id"`).
   Drop anything already in `campaigns/<slug>/submissions.json`.
2. Check each URL: it's on a platform the campaign allows, it's a real post URL (not a
   profile or a local file), and its clip passed compliance. Anything that fails is
   left out and reported.
3. Open the campaign in `clip-browser` and find its submission form. Fill it for the
   first URL but **do not press the final submit**. Show the user the filled values
   and ask for an explicit yes. Repeat per URL.
4. After each confirmed submission, snapshot to verify it registered, and append
   `{url, platform, submitted_at, status}` to `campaigns/<slug>/submissions.json`.
5. If the site requires a connected/verified social account or any payment/tax setup,
   stop and tell the user what they need to do themselves.
