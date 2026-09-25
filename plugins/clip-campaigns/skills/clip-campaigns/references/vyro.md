# Vyro — app.vyro.com

Campaign list: https://app.vyro.com/campaigns

These notes describe what to look for, not fixed selectors. Confirm against the live
page with `browser_snapshot`, and record what you actually find in
`campaigns/site-notes.md`.

## Reading the campaign list
- Expect a grid or list of campaign cards. For each card capture: name, brand/creator,
  pay rate (look for "$X / 1K views", "CPM", or "per 1,000 views"), budget or
  "remaining" indicator, platform icons (TikTok / YouTube / Instagram / X), and the
  link to its detail page. The id is the last path segment of that link.
- Check for filters or tabs (e.g. all / joined / ended) and category filters; scan
  each tab the user cares about.

## Campaign detail page
Capture everything under the campaign's rules/requirements/guidelines section
verbatim into `brief.md` — the exact wording matters when a submission is reviewed.
Look for: source content links (Drive/Dropbox/YouTube folders), example clips,
required tags/hashtags/captions, forbidden content, length limits, per-clip max
payout, minimum views, and whether you must "join" before submitting.

## Submitting
Submission is usually a form on the campaign page asking for the post URL (and
sometimes the platform/account). Fill it, show the user the filled values, and only
click the final submit after they confirm.

## Earnings
Look for a submissions / earnings / wallet area in the main navigation. Read
per-submission status (pending, approved, rejected + reason), tracked views, and
amount earned. Never open payout or payment-method settings on the user's behalf
except to read a balance.
