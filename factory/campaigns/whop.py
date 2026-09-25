"""Whop plugin — the biggest clipping marketplace (Content Rewards / bounties).

Uses Whop's public REST API (https://api.whop.com/api/v1, the same endpoints
the official `whop-sdk` wraps):

  GET  /bounties?business_goal_type=clipping&status=open   live clipping briefs
  POST /bounty_submissions {bounty_id, deliverable:{urls, caption}}
  GET  /bounty_submissions/{id}                             review outcome

AUTH: put a USER credential in .env as WHOP_API_KEY. Whop only lets a *user*
author submissions ("account API keys cannot author submissions"), so a
company/account API key will list campaigns fine but every submit will 403.
Grant it only the scopes this plugin needs (bounty read + submission write).

Campaigns that exist only in Whop's web UI (not exposed to the API) can still
be run through the `manual` plugin — same pipeline, you click submit.
"""
from __future__ import annotations

import json
import re
import urllib.error
import urllib.parse
import urllib.request

from ..config import cfg
from .base import Campaign, CampaignProvider, SubmitResult

API = "https://api.whop.com/api/v1"

_URL = re.compile(r"https?://[^\s<>()\"'\]]+")
# footage hosts campaigns hand out (Drive/Dropbox/Frame.io/...) plus any
# yt-dlp-able video page; social *profile* links in a brief are not footage
_FOOTAGE = re.compile(
    r"(drive\.google\.com|docs\.google\.com/.*/d/|dropbox\.com|frame\.io|f\.io/|"
    r"we\.tl|wetransfer\.com|mega\.nz|box\.com|onedrive|1drv\.ms|sharepoint\.com|"
    r"youtube\.com/watch|youtu\.be/|youtube\.com/live|vimeo\.com/\d|twitch\.tv/videos|"
    r"kick\.com/.+/videos|\.mp4\b|\.mov\b|\.m4v\b)", re.I)
_CPM = re.compile(r"\$\s?(\d+(?:\.\d+)?)\s*(?:/|per)\s*(?:1,?000|1k|k|thousand|cpm)", re.I)
_PLATFORMS = {
    "tiktok": re.compile(r"\btik ?tok\b", re.I),
    "instagram": re.compile(r"\b(instagram|reels?|ig)\b", re.I),
    "youtube": re.compile(r"\b(youtube|yt ?shorts|shorts)\b", re.I),
    "x": re.compile(r"\btwitter\b|\bon x\b|\bx/twitter\b", re.I),
}
_HASHTAG = re.compile(r"(?<![\w/])#\w{2,}")
_MENTION = re.compile(r"(?<![\w/.])@\w[\w.]{1,29}")


def footage_links(text: str) -> list[str]:
    """Approved-footage URLs mentioned in a brief, in order, de-duplicated."""
    urls = [u.rstrip(".,;:!") for u in _URL.findall(text or "")]
    return list(dict.fromkeys(u for u in urls if _FOOTAGE.search(u)))


def parse_brief(text: str) -> dict:
    """Best-effort extraction of the machine-usable bits of a free-text brief.
    The full brief still goes to the Finder verbatim, so misses here only cost
    automation, never correctness of clip selection."""
    text = text or ""
    prose = _URL.sub(" ", text)          # x.com/tiktok.com in a LINK isn't a rule
    cpm = _CPM.search(prose)
    return {
        "source_urls": footage_links(text),
        "cpm_usd": float(cpm.group(1)) if cpm else None,
        "platforms": [p for p, rx in _PLATFORMS.items() if rx.search(prose)],
        "required_hashtags": list(dict.fromkeys(_HASHTAG.findall(prose)))[:8],
        "required_caption": " ".join(dict.fromkeys(_MENTION.findall(prose)))[:120],
    }


class WhopProvider(CampaignProvider):
    name = "whop"

    def _token(self) -> str:
        return (cfg.env(self.settings.get("token_env", "WHOP_API_KEY")) or "").strip()

    def available(self):
        if not self._token():
            return False, "set WHOP_API_KEY in .env (a Whop *user* token)"
        return True, ""

    def _req(self, method: str, path: str, params: dict | None = None,
             body: dict | None = None) -> dict:
        url = f"{API}/{path}"
        if params:
            q = {k: v for k, v in params.items() if v is not None}
            url += "?" + urllib.parse.urlencode(q)
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(url, data=data, method=method, headers={
            "Authorization": f"Bearer {self._token()}",
            "Accept": "application/json",
            **({"Content-Type": "application/json"} if data else {}),
            "User-Agent": "podcast-shorts-factory/1.0"})
        try:
            with urllib.request.urlopen(req, timeout=30) as r:
                return json.loads(r.read() or b"{}")
        except urllib.error.HTTPError as ex:
            msg = ex.read().decode("utf-8", "replace")[:300]
            if ex.code in (401, 403):
                msg += (" — check WHOP_API_KEY is a USER token with bounty "
                        "permissions (account API keys cannot submit)")
            raise RuntimeError(f"Whop {method} {path}: HTTP {ex.code} {msg}") from None

    def list_campaigns(self) -> list[Campaign]:
        out, after = [], None
        max_pages = int(self.settings.get("max_pages", 3))
        for _ in range(max_pages):
            page = self._req("GET", "bounties", params={
                "business_goal_type": "clipping", "status": "open",
                "country": self.settings.get("country"),
                "query": self.settings.get("query"),
                "order": "created_at", "direction": "desc",
                "first": 50, "after": after})
            for b in page.get("data", []):
                out.append(self._to_campaign(b))
            info = page.get("page_info") or {}
            after = info.get("end_cursor")
            if not info.get("has_next_page") or not after:
                break
        return out

    def _to_campaign(self, b: dict) -> Campaign:
        desc = b.get("description") or ""
        parsed = parse_brief(desc)
        budget = b.get("budget_amount")
        paid = b.get("gross_paid_out_amount") or 0
        cur = (b.get("currency") or "usd").lower()
        left = (float(budget) - float(paid)) if budget is not None and cur == "usd" else None
        host = (b.get("hosting_account") or {}).get("route")
        reward = b.get("net_reward_amount")
        brief = desc
        if reward:
            brief += (f"\n\n[Whop] pays {reward} {cur.upper()} per accepted submission; "
                      f"{b.get('spots_remaining', '?')} winner slot(s) left.")
        return Campaign(
            provider=self.name, native_id=b["id"], title=b.get("title", b["id"]),
            brief=brief, cpm_usd=parsed["cpm_usd"], budget_left_usd=left,
            source_urls=parsed["source_urls"], platforms=parsed["platforms"],
            required_hashtags=parsed["required_hashtags"],
            required_caption=parsed["required_caption"],
            page_url=f"https://whop.com/{host}/" if host else "https://whop.com/discover/",
            status=b.get("status", "open"),
            can_submit_api="content_url" in (b.get("accepted_deliverable_types") or
                                             ["content_url"]))

    def submit(self, campaign: Campaign, post_url: str, caption: str = "") -> SubmitResult:
        body = {"bounty_id": campaign.native_id,
                "deliverable": {"urls": [post_url], **({"caption": caption[:500]}
                                                       if caption else {})}}
        try:
            r = self._req("POST", "bounty_submissions", body=body)
        except RuntimeError as ex:
            return SubmitResult("failed", detail=str(ex))
        return SubmitResult("submitted", external_id=r.get("id"),
                            detail=f"whop status: {r.get('status', '?')}")

    def refresh(self, external_id: str) -> str | None:
        try:
            r = self._req("GET", f"bounty_submissions/{external_id}")
        except RuntimeError:
            return None
        return {"in_progress": "submitted", "submitted": "submitted",
                "approved": "approved", "denied": "denied"}.get(r.get("status"))
