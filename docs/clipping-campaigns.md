# Clipping campaigns (Agent 11: Campaigner)

Instead of downloading podcasts from YouTube channels, the factory can work
**paid clipping campaigns**. A creator or brand posts a brief, approved source
footage and a CPM (dollars per 1,000 views). You cut the clips and post them on
your own accounts, then submit the post links. The platform verifies the views
and pays you.

Two things make this better than ripping channels:

1. **You have the rights.** The footage is handed out *to be clipped*, so you
   avoid the reused-content and copyright risk of clipping Rogan or DOAC without
   permission.
2. **It pays per view from day one.** You don't need to hit the YouTube
   Partner Program thresholds first.

---

## The landscape (researched September 2026)

| Platform | Who runs campaigns there | Typical pay | Footage delivered via | Submit API? |
|---|---|---|---|---|
| **Whop Content Rewards** | The biggest marketplace. Streamers (Kai Cenat, Adin Ross and Druski were early users of clipping), podcasts, music labels (Harry Styles on Brittany Broski's *Royal Court* podcast), brands (Polymarket, ElevenLabs) | $0.20–$6 per 1K views, about $1 on average | Links in the brief (Drive, Dropbox, YouTube) | **Yes**: `POST /api/v1/bounty_submissions` with a *user* token |
| **Vyro** (MrBeast / ViewStats) | MrBeast, Mark Rober, brands | about $3 CPM headline | A campaign content folder (Drive, Frame.io, direct URLs) | No public API: you paste the link |
| **Clipping.net / Clipping.io** | Creators and podcasts | varies | Drive/Dropbox | No public API |
| **ClipAffiliates** | Brands and creators, no minimum views | often higher CPM | Links in the brief | No public API |
| **ClipFarm** | Agency-style volume campaigns (HBO Max, Druski) | brand-set CPM | Runs on Whop's rail | Via Whop |

Podcast clipping is one of the biggest niches on these platforms (Flagrant,
IMPAULSIVE, TBPN and similar shows). Whop tracked about $887K paid out to
clippers in February 2026 alone. Clippers report that reviewers reject clips
for rule breaks, flag suspected bot views, and hold payouts for up to 90 days.
Follow each brief exactly.

Sources: [Whop Content Rewards explained (OpusClip)](https://www.opus.pro/blog/whop-content-rewards),
[Whop clipping rates (OpenClip)](https://openclip.app/guides/whop-clipping-guide),
[Best clipping platforms 2026 (Ssemble)](https://www.ssemble.com/blog/best-clipping-platforms-2026),
[Vyro review (Ssemble)](https://www.ssemble.com/blog/vyro-review-2026),
[Clipping platforms compared (Cut.Pro)](https://cut.pro/en/blog/clipping-campaign-platforms-comparison-2026),
[Clipping in the music business (Variety)](https://variety.com/2026/music/news/clipping-marketing-tool-took-over-music-industry-1236699705/),
[The clip economy (CreatorDB)](https://creatordb.app/creator-news/the-rise-of-the-clip-economy/),
[MrBeast launches Vyro (Tubefilter)](https://www.tubefilter.com/2025/10/14/mrbeast-vyro-clipping-platform-viewstats-expansion/),
[Whop bounty API docs](https://docs.whop.com/developer/bounties/run-a-bounty-program).
The Whop endpoints were checked against the official `whop-sdk` 2.0.0 package.

---

## How it works

```
campaigns scan ──► campaigns pull ──► Finder (with the campaign's rules) ──► Editor ──► Finishing Editor ──► Uploader
      │                   │                                                                                     │
  every plugin      downloads the approved                                                 adds the campaign's required
  lists live        footage (Drive/Dropbox/                                                @mention, #tags and #ad
  campaigns         mp4/yt-dlp)                                                                                 │
                                                                                                                ▼
                                     campaigns submit ◄── the post goes live (publishAt + min_minutes_live)
                                            │
                          permission gate: off / review / auto
                                            │
                          Whop API submission, or a paste-list plus phone ping for Vyro etc.
```

* **Footage.** The Campaigner uses the links the brief hands out:
  Google Drive files work out of the box, Drive *folders* need `pip install gdown`,
  and Dropbox files and folders, direct `.mp4` links and anything yt-dlp can read
  are also supported. For hosts it can't reach (Frame.io, WeTransfer, Mega),
  save the files into `campaign_footage/<campaign-id>/` and it picks them up.
* **Rules.** The campaign's brief is appended to the Finder's selection brief
  as rules that override the house style. The Uploader always appends the
  campaign's required hashtags, @mentions and the `#ad` disclosure *after* the
  AI copywriter runs, so they can't get lost.
* **Only live posts are submitted.** Scheduled YouTube posts are submitted
  only after they go public (plus `min_minutes_live`). TikTok and Reels
  exports aren't real posts. Once you've posted them yourself, run
  `python run.py campaigns link <clip_id> <post_url>` and they get submitted too.

---

## Permissions: what the agent may do

The permissions are all in `config.yaml` → `campaigns.permissions`:

| Setting | Meaning |
|---|---|
| `submit: off` | Never submits anything. |
| `submit: review` *(default)* | Queues every due submission and pings you. Run `python run.py campaigns approve` (or `--yes`) to send them. |
| `submit: auto` | Submits by itself, but only for `allowed_providers`, and at most `max_submissions_per_day` API submissions in any 24 hours. |

Which campaigns it may *work on* is set under `campaigns.selection`:
minimum CPM, minimum budget left, include/exclude keywords, and an
allowlist or blocklist of campaign IDs. It also skips any campaign that
doesn't pay for the platforms you post to.

**Credentials.** Whop only lets a *user* credential create submissions (account
API keys can't). Put a user token in `.env` as `WHOP_API_KEY` and give it only
the bounty read and submission scopes it needs. The agent never needs payout
or transfer permissions: Whop pays your Whop balance and you withdraw it yourself.

Vyro, Clipping.net and ClipAffiliates have no public submit API. Automating
their websites with a stored password would likely break their terms and get
the account banned. So for those platforms the agent does everything else and
leaves you a one-click paste list in `campaign_submissions.md`, plus a phone
notification.

---

## Setup

1. `config.yaml` → `campaigns.enabled: true`.
2. **Whop:** set `campaigns.providers.whop.enabled: true` and add `WHOP_API_KEY=` to `.env`.
3. **Vyro and the others:** set `campaigns.providers.manual.enabled: true` and
   paste each campaign you've joined (footage links, rules, required tags) under
   `campaigns.providers.manual.campaigns`. There's an example in the config.
4. `python run.py campaigns scan` shows every live campaign and why each passes or fails your policy.
5. `python run.py campaigns pull` downloads the best campaign's footage and finds clips.
   After that the normal `review` / `edit` / `upload` commands take over, or
   `produce`/`daily` does it all.
6. `campaigns.source_mode` decides where `produce`/`daily` get footage:
   `campaigns`, `mixed` (campaigns first, then your YouTube sources) or `youtube`.
7. `produce`, `daily` and `post-next` call `campaigns submit` automatically.
   `python run.py campaigns status` shows each submission's outcome.

### Adding another platform

Subclass `CampaignProvider` in `factory/campaigns/base.py` (implement
`list_campaigns`, `submit` and optionally `refresh`), register it in
`factory/campaigns/__init__.py`, and give it a block under
`campaigns.providers` in `config.yaml`.
