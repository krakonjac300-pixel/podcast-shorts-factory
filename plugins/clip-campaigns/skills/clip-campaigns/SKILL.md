---
name: clip-campaigns
description: Knowledge for working paid clipping campaigns on Vyro (app.vyro.com/campaigns) and Clipping.net (clipping.net/dashboard/campaigns) with the Podcast Shorts Factory. Use when the user mentions Vyro, Clipping.net, clipping campaigns, CPM/pay-per-view clip deals, campaign briefs, submitting clip links, or clipping earnings.
---

# Clipping campaigns (Vyro + Clipping.net)

Clipping platforms pay creators per view for short clips of a brand's or creator's
long-form content. Each **campaign** supplies the source content, a pay rate (usually
$ per 1,000 views), a budget, the allowed platforms, and rules the clip must follow.
You post the clip on your own account, then **submit the post URL** to the campaign;
the platform tracks views and pays from the campaign budget.

This fits the factory well: a campaign is an explicit licence to clip that content,
which is the permission the factory's README asks you to have.

| Platform | Campaign list | Notes |
|---|---|---|
| Vyro | https://app.vyro.com/campaigns | see `references/vyro.md` |
| Clipping.net | https://clipping.net/dashboard/campaigns | see `references/clipping-net.md` |

## How to drive the sites

Both dashboards are behind a login and neither has a public API this plugin relies on.
The plugin ships a browser MCP server, `clip-browser` (Playwright), with a
**persistent profile**: the user logs in once (`/clip-campaigns:login`) and the
session is reused afterwards.

- Navigate with `browser_navigate`, then read the page with `browser_snapshot`
  (accessibility tree). Prefer the snapshot over screenshots; take a screenshot only
  when layout matters (e.g. an example clip or a rate badge rendered as an image).
- **Never hard-code selectors.** These UIs change. Find elements by their visible
  text/role in the latest snapshot and use the `ref` it gives you.
- Lists are often paginated, lazy-loaded or tabbed ("Active", "Joined", "Ended").
  Scroll (`browser_press_key` End / `browser_evaluate` scroll) and re-snapshot until
  no new items appear, and check every relevant tab.
- If you land on a login page, a captcha, or a 2FA prompt: stop and ask the user to
  complete it in the open browser window, then continue. Never type the user's
  password or 2FA codes yourself, and never try to solve captchas.
- After a session, append anything you learned about the site's layout (tab names,
  where the rate is shown, how submission works) to `campaigns/site-notes.md` in the
  project, and read that file before starting next time.

## Safety rules (non-negotiable)

1. **Read-only by default.** Scanning, reading briefs and reading earnings never
   click anything that commits an action.
2. **Every commit needs the user's explicit OK in this conversation**: joining or
   applying to a campaign, submitting a clip link, accepting terms, requesting a
   payout, editing payout/tax/payment details, deleting a submission. Show exactly
   what will be sent first. The plugin's PreToolUse hook also forces a confirmation
   prompt on such clicks — do not try to route around it (e.g. with
   `browser_evaluate` clicks or form `.submit()`).
3. Never enter or change payment, wallet, bank, or tax details. Send the user there.
4. Never submit a URL that is not a real, live post of a clip made for that campaign,
   never resubmit the same post to several campaigns unless the rules allow it, and
   never do anything to inflate views (bots, view exchanges, reposting loops).
   Platforms ban accounts and claw back payouts for this.
5. Respect each campaign's rules and the platform's terms. When a rule is ambiguous,
   ask the user rather than guessing.

## Campaign record

Store what you scrape in `campaigns/index.json` (project root, git-ignored) as an
array of objects. Fill what the page shows; use `null` for anything not shown —
never invent numbers.

```json
{
  "id": "vyro:<slug-or-id-from-url>",
  "platform": "vyro | clipping.net",
  "url": "https://...",
  "name": "Campaign name",
  "brand": "Creator / brand",
  "status": "active | paused | ended | joined",
  "rate_per_1k_views_usd": 1.5,
  "max_payout_per_clip_usd": null,
  "min_views_to_qualify": null,
  "budget_total_usd": null,
  "budget_remaining_usd": null,
  "allowed_platforms": ["tiktok", "youtube_shorts", "instagram_reels"],
  "content_type": "podcast | stream | music | brand | other",
  "source_links": ["https://..."],
  "requirements": ["tag @brand", "#hashtag", "no watermark", "max 60s"],
  "deadline": null,
  "scraped_at": "ISO-8601"
}
```

## Ranking campaigns

Rank by expected value for *this* user, and show the reasoning:

1. **Eligible** – user posts on an allowed platform; campaign active; budget left.
2. **Fit** – source is long-form talk (podcast, interview, stream) the factory's
   Finder can mine; matches the user's niche in `config.yaml` (`niche`/`finder`).
3. **Money** – rate per 1k views, per-clip cap, qualification threshold, budget
   remaining (a nearly spent budget is a trap: views after it runs out earn nothing).
4. **Effort** – how strict the rules are (manual edits the factory can't do yet).

## Handing a campaign to the factory

`/clip-campaigns:brief` writes `campaigns/<id-slug>/overlay.yaml`. The factory reads
it when `PSF_CONFIG_OVERLAY` points at it, deep-merged over `config.yaml`
(nothing in `config.yaml` is changed). Map campaign rules like this:

| Campaign rule | Overlay key |
|---|---|
| what moments / topics they want, tone, do/don't | `finder.selection_brief` (write it as a brief, keep the user's existing taste rules) |
| min / max length | `finder.clip_min_seconds`, `finder.clip_max_seconds` |
| required hashtags / @tags | `uploader.hashtags` |
| required CTA / on-screen text | `editor.cta_text` (`editor.cta: false` if CTAs are banned) |
| allowed platforms | `uploader.platforms` (subset of youtube, tiktok, instagram) |
| no AI voice / no narrator | `editor.voiceover: false`, `editor.teaser: false` |
| no music | `editor.music_volume: 0` |

Rules the factory cannot enforce (e.g. "tag the brand in the first line", "link in
bio") go in `brief.md` under **Manual checklist** so the user does them when posting.

Run the factory from the project root, for example:

```bash
PSF_CONFIG_OVERLAY=campaigns/<slug>/overlay.yaml python run.py find "<source url>"
```

On Windows PowerShell: `$env:PSF_CONFIG_OVERLAY="campaigns\<slug>\overlay.yaml"; .\factory find "<url>"`.
Keep `uploader.privacy` as the user has it; for campaign clips, uploads usually need
to be **public** to count views — confirm with the user before changing it.
