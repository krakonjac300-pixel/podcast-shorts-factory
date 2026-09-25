"""Manual plugin — any clipping platform without a public submit API.

Vyro (MrBeast / Mark Rober), Clipping.net, ClipAffiliates, Discord clipping
servers, or a deal a podcast made with you directly: you copy the campaign's
brief + footage links into config.yaml once, and the whole pipeline (pull
footage, find, edit, post) runs on it automatically.

The only step a human does is the final "paste the post link into the
platform" click. The agent collects every due link in `campaign_submissions.md`
(with the campaign page) and pings your phone, so it takes seconds.

config.yaml:
  campaigns:
    providers:
      manual:
        enabled: true
        campaigns:
          - id: vyro-mrbeast-podcast          # any unique slug
            platform: vyro                    # label only
            title: "MrBeast podcast clips"
            page_url: "https://app.vyro.com/campaigns/..."
            cpm_usd: 3
            source_urls: ["https://drive.google.com/drive/folders/..."]
            platforms: [youtube, tiktok]
            required_hashtags: ["#mrbeast"]
            required_caption: "@mrbeast"
            brief: |
              Paste the campaign rules here — the Finder reads them.
"""
from __future__ import annotations

from datetime import datetime

from ..config import ROOT
from .base import Campaign, CampaignProvider, SubmitResult

CHECKLIST = ROOT / "campaign_submissions.md"


class ManualProvider(CampaignProvider):
    name = "manual"

    def available(self):
        if not self.settings.get("campaigns"):
            return False, "no campaigns listed under campaigns.providers.manual.campaigns"
        return True, ""

    def list_campaigns(self) -> list[Campaign]:
        out = []
        for c in self.settings.get("campaigns") or []:
            if not c.get("id") or c.get("enabled", True) is False:
                continue
            label = c.get("platform", "manual")
            out.append(Campaign(
                provider=self.name, native_id=str(c["id"]),
                title=f"[{label}] {c.get('title', c['id'])}",
                brief=c.get("brief", ""), cpm_usd=c.get("cpm_usd"),
                budget_left_usd=c.get("budget_left_usd"),
                source_urls=list(c.get("source_urls") or []),
                platforms=[p.lower() for p in c.get("platforms") or []],
                required_hashtags=list(c.get("required_hashtags") or []),
                required_caption=c.get("required_caption", ""),
                page_url=c.get("page_url", ""), can_submit_api=False))
        return out

    def submit(self, campaign: Campaign, post_url: str, caption: str = "") -> SubmitResult:
        """No API: add the link to the human's paste-list and report it."""
        stamp = datetime.now().strftime("%Y-%m-%d %H:%M")
        line = (f"- [ ] {stamp} **{campaign.title}** — submit {post_url}"
                + (f"  (campaign: {campaign.page_url})" if campaign.page_url else "")
                + "\n")
        try:
            head = "# Campaign links to submit\n\n*Tick them off once pasted.*\n\n"
            body = CHECKLIST.read_text(encoding="utf-8") if CHECKLIST.exists() else head
            if not body.startswith(head):
                body = head + body
            CHECKLIST.write_text(head + line + body[len(head):], encoding="utf-8")
        except OSError as ex:
            return SubmitResult("failed", detail=f"couldn't write checklist: {ex}")
        return SubmitResult("needs_manual", detail="added to campaign_submissions.md")
