"""Agent 11 — CAMPAIGNER.

Runs the factory on paid clipping campaigns (Whop Content Rewards, Vyro,
Clipping.net, ...) instead of ripping YouTube channels:

  scan     list live campaigns from every enabled plugin, filter by policy
  pull     download the best campaign's APPROVED footage and hand it to the
           Finder along with the campaign's rules
  submit   once a clip cut from campaign footage is LIVE, submit its post URL
           back to the campaign for payout — behind a permission gate
  approve  the human half of the gate (campaigns.permissions.submit: review)
  status   refresh what the platforms decided on earlier submissions

The permission gate (config.yaml -> campaigns.permissions):
  submit: off     never submits anything
  submit: review  queues each due submission; you approve with
                  `python run.py campaigns approve`   (the default)
  submit: auto    submits on its own, within allowed_providers,
                  allowed_campaigns and max_submissions_per_day
"""
from __future__ import annotations

from datetime import datetime, timedelta

from rich.console import Console
from rich.prompt import Prompt
from rich.table import Table

from .. import campaigns as plugins
from .. import db, notify
from ..campaigns import Campaign, footage
from ..config import cfg

console = Console()


def _perm(key: str, default=None):
    return cfg.get(f"campaigns.permissions.{key}", default)


def _sel(key: str, default=None):
    return cfg.get(f"campaigns.selection.{key}", default)


# ── scan ──────────────────────────────────────────────────────────
def _why_not(c: Campaign) -> str | None:
    """None if the campaign passes the selection policy, else the reason."""
    if c.status != "open":
        return f"status {c.status}"
    blocked = _sel("blocked_campaigns", []) or []
    allowed = _sel("allowed_campaigns", []) or []
    if c.id in blocked or c.native_id in blocked:
        return "blocked in config"
    if allowed and c.id not in allowed and c.native_id not in allowed:
        return "not in allowed_campaigns"
    if c.cpm_usd is not None and c.cpm_usd < float(_sel("min_cpm_usd", 0) or 0):
        return f"CPM ${c.cpm_usd} below min"
    if c.budget_left_usd is not None and \
            c.budget_left_usd < float(_sel("min_budget_left_usd", 0) or 0):
        return f"only ${c.budget_left_usd:.0f} budget left"
    ours = {p.lower() for p in cfg.get("uploader.platforms", ["youtube"])}
    if c.platforms and not ours & set(c.platforms):
        return f"wants {'/'.join(c.platforms)}, we post {'/'.join(sorted(ours))}"
    text = f"{c.title}\n{c.brief}".lower()
    inc = [k.lower() for k in _sel("include_keywords", []) or []]
    exc = [k.lower() for k in _sel("exclude_keywords", []) or []]
    if inc and not any(k in text for k in inc):
        return "no include_keywords match"
    if any(k in text for k in exc):
        return "matches exclude_keywords"
    if not c.source_urls and not footage.dropped_files(c.id):
        return "no footage links found (drop files in campaign_footage/)"
    return None


def _rank_key(c: Campaign):
    return (c.cpm_usd or 0, c.budget_left_usd or 0)


def scan(show: bool = True) -> list[Campaign]:
    """Every live campaign across enabled plugins → DB; returns the eligible
    ones, best paying first."""
    found: list[Campaign] = []
    for p in plugins.enabled_providers():
        ok, why = p.available()
        if not ok:
            console.print(f"[yellow]{p.name}: skipped — {why}[/]")
            continue
        try:
            got = p.list_campaigns()
        except Exception as ex:  # noqa: BLE001 - one broken plugin mustn't stop the rest
            console.print(f"[red]{p.name}: {ex}[/]")
            continue
        for c in got:
            db.save_campaign(c.id, c.provider, c.title, c.to_dict())
        found += got
    if not found:
        console.print("[yellow]No campaigns found. Enable a provider under "
                      "campaigns.providers in config.yaml.[/]")
        return []
    verdicts = [(c, _why_not(c)) for c in found]
    good = sorted((c for c, why in verdicts if not why), key=_rank_key, reverse=True)
    if show:
        t = Table(title=f"{len(good)}/{len(found)} campaigns eligible")
        for col in ("id", "title", "CPM", "budget left", "footage", "verdict"):
            t.add_column(col)
        for c, why in sorted(verdicts, key=lambda v: (v[1] is not None, -_rank_key(v[0])[0])):
            t.add_row(c.id, c.title[:40],
                      f"${c.cpm_usd:g}" if c.cpm_usd is not None else "?",
                      f"${c.budget_left_usd:,.0f}" if c.budget_left_usd is not None else "?",
                      str(len(c.source_urls)),
                      "[green]eligible[/]" if not why else f"[dim]{why}[/]")
        console.print(t)
    return good


# ── pull ──────────────────────────────────────────────────────────
def _campaign(cid: str) -> Campaign | None:
    d = db.get_campaign(cid)
    return Campaign.from_dict(d) if d else None


def pull(campaign_id: str | None = None, max_videos: int | None = None,
         refresh: bool = False) -> int:
    """Download fresh footage for a campaign and run the Finder on it with the
    campaign's rules. Picks the best eligible campaign unless one is named.
    Returns the number of clip candidates created."""
    max_videos = max_videos or int(_sel("videos_per_run", 1) or 1)
    if campaign_id:
        scan(show=False)                          # make sure it's cached/fresh
        chosen = [c for c in [_campaign(campaign_id)] if c]
        if not chosen:
            console.print(f"[red]unknown campaign {campaign_id} — run "
                          "`python run.py campaigns scan`[/]")
            return 0
    else:
        chosen = scan(show=False)
    from . import finder
    done_videos, cands = 0, 0
    for camp in chosen:
        pulled = db.campaign_sources(camp.id)
        brief = _brief_for_finder(camp)
        for key, path in _fresh_footage(camp, pulled, refresh):
            if key in pulled:
                continue
            console.print(f"[bold magenta]CAMPAIGNER[/] {camp.title} → {path.name}")
            cands += finder.find(key, local=(path, f"{camp.title} — {path.stem}", camp.id),
                                 brief_extra=brief, campaign_id=camp.id)
            done_videos += 1
            if done_videos >= max_videos:
                return cands
        if done_videos:
            return cands                          # one campaign per run keeps it focused
    console.print("[yellow]No fresh campaign footage to clip.[/]")
    return cands


def _fresh_footage(camp: Campaign, pulled: set, refresh: bool):
    """Yield (source key, local file) lazily, so a multi-GB link is only
    downloaded when the run actually needs more footage."""
    for p in footage.dropped_files(camp.id):
        yield f"file://{p.resolve()}", p
    for link in camp.source_urls:
        if not refresh and any(k == link or k.startswith(link + "#") for k in pulled):
            continue                              # this link was already clipped
        console.print(f"[bold magenta]CAMPAIGNER[/] fetching {link}")
        try:
            files = footage.fetch(link, camp.id)
        except Exception as ex:  # noqa: BLE001
            console.print(f"  [yellow]couldn't fetch: {ex}[/]")
            notify.notify("Campaign footage needs you",
                          f"{camp.title}: {ex}", camp.page_url or link)
            continue
        multi = len(files) > 1
        for p in files:
            yield (f"{link}#{p.name}" if multi else link), p


def _brief_for_finder(c: Campaign) -> str:
    extra = []
    if c.platforms:
        extra.append(f"Posts accepted on: {', '.join(c.platforms)}.")
    if c.required_hashtags or c.required_caption:
        extra.append("Every post will carry: "
                     f"{' '.join(c.required_hashtags)} {c.required_caption}".strip())
    return f"Campaign: {c.title}\n{c.brief}\n" + "\n".join(extra)


# ── submit (the permission gate) ─────────────────────────────────
def _is_live(row) -> bool:
    """Campaigns only accept public posts: wait until the scheduled publish
    time (plus a margin) has passed."""
    when = row["publish_at"] or row["created_at"]
    try:
        t = datetime.fromisoformat(when)
    except (TypeError, ValueError):
        return False
    margin = timedelta(minutes=int(_perm("min_minutes_live", 30) or 0))
    return datetime.utcnow() >= t + margin


def _cap_left() -> int:
    cap = int(_perm("max_submissions_per_day", 10) or 0)
    since = (datetime.utcnow() - timedelta(days=1)).isoformat()
    return max(0, cap - db.submitted_count_since(since))


def _send(sub_id: int, camp: Campaign, post_url: str, caption: str) -> str:
    prov = plugins.provider(camp.provider)
    allowed = _perm("allowed_providers", []) or []
    if not prov or (allowed and camp.provider not in allowed):
        db.update_submission(sub_id, "skipped",
                             detail=f"provider '{camp.provider}' not allowed to submit")
        return "skipped"
    if not camp.can_submit_api and camp.provider != "manual":
        prov = plugins.provider("manual")         # route to the human paste-list
    if prov.name != "manual" and _cap_left() <= 0:
        db.update_submission(sub_id, "pending_approval",
                             detail="daily cap reached — will retry")
        return "capped"
    res = prov.submit(camp, post_url, caption)
    db.update_submission(sub_id, res.status, external_id=res.external_id,
                         detail=res.detail)
    if res.status == "submitted":
        notify.notify("Submitted to campaign", f"{camp.title}", post_url)
    elif res.status == "needs_manual":
        notify.notify("Paste this into the campaign",
                      f"{camp.title}: {post_url}", camp.page_url or None)
    else:
        notify.notify("Campaign submit FAILED", f"{camp.title}: {res.detail}")
    return res.status


def submit_due() -> int:
    """Find live posts of campaign clips that haven't been submitted and push
    them through the permission gate. Returns how many were acted on."""
    mode = (_perm("submit", "review") or "review").lower()
    if mode == "off":
        console.print("[dim]campaigns.permissions.submit is off — not submitting.[/]")
        return 0
    queued = 0
    for row in db.campaign_uploads():
        camp = _campaign(row["campaign_id"])
        if not camp or not _is_live(row):
            continue
        if camp.platforms and row["platform"] not in camp.platforms:
            continue                              # this campaign doesn't pay for that platform
        if db.add_submission(camp.id, row["clip_id"], row["upload_id"],
                             row["url"], "pending_approval") is not None:
            queued += 1
    if mode != "auto":
        if queued:
            notify.notify("Campaign submissions need your OK",
                          f"{queued} live clip(s) ready — run "
                          "`python run.py campaigns approve`")
        console.print(f"[green]✓ campaigns: {queued} new submission(s) awaiting "
                      "your approval[/]")
        return queued
    # auto: everything pending is pre-approved (incl. ones a cap held back)
    sent = 0
    for s in db.submissions("pending_approval"):
        camp = _campaign(s["campaign_id"])
        if not camp:
            continue
        clip = db.clip_by_id(s["clip_id"])
        status = _send(s["id"], camp, s["post_url"], clip["title"] if clip else "")
        if status == "capped":
            console.print("[yellow]daily submission cap reached — rest wait "
                          "for the next run[/]")
            break
        sent += status in ("submitted", "needs_manual")
    console.print(f"[green]✓ campaigns: {sent} submitted automatically[/]")
    return sent


def approve(assume_yes: bool = False) -> int:
    """The human side of `submit: review` — approve each queued submission."""
    pend = db.submissions("pending_approval")
    if not pend:
        console.print("[yellow]Nothing waiting for approval.[/]")
        return 0
    n = 0
    for s in pend:
        camp = _campaign(s["campaign_id"])
        if not camp:
            continue
        clip = db.clip_by_id(s["clip_id"])
        title = clip["title"] if clip else ""
        console.print(f"\n[bold]{camp.title}[/]  (${camp.cpm_usd or '?'} CPM)\n"
                      f"  clip: {title}\n  post: {s['post_url']}")
        choice = "y" if assume_yes else Prompt.ask(
            "  submit? [y]es / [n]o (never) / [s]kip / [q]uit",
            choices=["y", "n", "s", "q"], default="y")
        if choice == "q":
            break
        if choice == "n":
            db.update_submission(s["id"], "skipped", detail="declined by you")
        elif choice == "y":
            status = _send(s["id"], camp, s["post_url"], title)
            console.print(f"  → {status}")
            n += status in ("submitted", "needs_manual")
            if status == "capped":
                console.print("[yellow]daily submission cap reached — stopping.[/]")
                break
    return n


def refresh_status() -> None:
    for s in db.submissions("submitted"):
        camp = _campaign(s["campaign_id"])
        prov = plugins.provider(camp.provider) if camp else None
        if not prov or not s["external_id"]:
            continue
        new = prov.refresh(s["external_id"])
        if new and new != "submitted":
            db.update_submission(s["id"], new)
            notify.notify(f"Campaign submission {new}", camp.title, s["post_url"])


def link(clip_id: int, url: str, platform: str | None = None) -> None:
    """Register a post you made by hand (e.g. TikTok/Reels) so it gets
    submitted to the clip's campaign like the auto-posted ones."""
    if not platform:
        import urllib.parse
        host = urllib.parse.urlsplit(url).netloc.lower().removeprefix("www.")
        platform = {"tiktok.com": "tiktok", "vm.tiktok.com": "tiktok",
                    "instagram.com": "instagram", "youtube.com": "youtube",
                    "youtu.be": "youtube", "m.youtube.com": "youtube",
                    "x.com": "x", "twitter.com": "x"}.get(host, "other")
    db.record_upload(clip_id, platform, None, url)
    console.print(f"[green]✓ linked clip {clip_id} → {url} ({platform})[/]")


def report() -> None:
    subs = db.submissions()
    if not subs:
        console.print("[dim]No campaign submissions yet.[/]")
        return
    t = Table(title="Campaign submissions")
    for col in ("#", "campaign", "post", "status", "detail"):
        t.add_column(col)
    for s in subs[-40:]:
        t.add_row(str(s["id"]), s["campaign_id"], s["post_url"], s["status"],
                  (s["detail"] or "")[:60])
    console.print(t)
