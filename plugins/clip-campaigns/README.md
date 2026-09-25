# clip-campaigns — Claude Code plugin

Work paid clipping campaigns on **Vyro** (https://app.vyro.com/campaigns) and
**Clipping.net** (https://clipping.net/dashboard/campaigns) with the Podcast Shorts
Factory doing the editing.

## Install

In Claude Code, from anywhere:

```
/plugin marketplace add krakonjac300-pixel/podcast-shorts-factory
/plugin install clip-campaigns@podcast-shorts-factory
```

Requires Node.js (for `npx @playwright/mcp`) and the factory's own prerequisites.
Run Claude Code from the factory's project root so commands can find `config.yaml`,
`run.py` and `factory.db`.

## Commands

| Command | What it does |
|---|---|
| `/clip-campaigns:login [vyro\|clipping\|all]` | Opens the dashboards in the plugin's browser so you log in once (profile is persistent). |
| `/clip-campaigns:scan [site] [filter]` | Reads every active campaign, saves them to `campaigns/index.json`, ranks the best fits. |
| `/clip-campaigns:brief <campaign>` | Turns a campaign's rules into `campaigns/<slug>/brief.md` + a factory `overlay.yaml`. |
| `/clip-campaigns:produce <slug> [url]` | Runs find → review → edit → finish on the campaign's source with the overlay, then checks renders with the `campaign-compliance` agent. |
| `/clip-campaigns:submit <slug> [urls]` | Fills the campaign's submission form with your posted clip URLs; asks before every final submit. |
| `/clip-campaigns:earnings [site]` | Reads statuses, views and balances into `campaigns/earnings.md`. |

## How it works

- **Browser, not API.** Neither site is driven through a public API; the plugin
  bundles a Playwright MCP server (`clip-browser`) with a persistent profile and a
  skill that tells Claude how to read the pages by their visible text rather than
  brittle selectors. What Claude learns about each layout is kept in
  `campaigns/site-notes.md`.
- **Factory overlay.** Campaign rules become an `overlay.yaml` that the factory
  deep-merges over `config.yaml` when `PSF_CONFIG_OVERLAY` is set, so your normal
  config is never edited.
- **Safety.** A `PreToolUse` hook forces a confirmation prompt on any browser click
  that looks like submit / join / apply / withdraw / payout / delete, and on Enter or
  type-and-submit. Claude never types your password, solves captchas, touches payment
  or tax details, or does anything to inflate views.

Campaign data lives in `campaigns/` (git-ignored). Follow each campaign's rules and
each platform's terms — rejected clips earn nothing and view manipulation gets
accounts banned.
