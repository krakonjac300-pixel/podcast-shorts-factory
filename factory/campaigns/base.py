"""The contract every campaign platform plugin implements.

A *campaign* is a paid clipping brief: a creator/brand hands out approved
source footage, a rulebook and a CPM ("$X per 1,000 views"). Clippers cut and
post clips on their OWN accounts, then submit the post URLs back to the
platform, which verifies the views and pays out.

A provider plugin does three things:
  1. list_campaigns()  -> what's live right now (brief, CPM, budget, footage links)
  2. submit()          -> hand a live post URL to the campaign for payout
  3. refresh()         -> check what happened to earlier submissions

Everything else (picking campaigns, downloading footage, clipping, the
permission gate on submitting) is shared and lives in agents/campaigner.py.
"""
from __future__ import annotations

from dataclasses import asdict, dataclass, field


@dataclass
class Campaign:
    provider: str                     # "whop", "manual", ...
    native_id: str                    # the platform's own id (e.g. bnty_...)
    title: str
    brief: str = ""                   # full rules/instructions shown to clippers
    cpm_usd: float | None = None      # payout per 1,000 verified views
    budget_left_usd: float | None = None
    source_urls: list = field(default_factory=list)   # approved footage links
    platforms: list = field(default_factory=list)     # where posts are accepted
                                                      # (youtube/tiktok/instagram/x); [] = any
    required_hashtags: list = field(default_factory=list)
    required_caption: str = ""        # e.g. "@creator #ad" — appended to every post
    page_url: str = ""                # where a human can open the campaign
    status: str = "open"
    can_submit_api: bool = False      # False = submission needs a human click

    @property
    def id(self) -> str:
        return f"{self.provider}:{self.native_id}"

    def to_dict(self) -> dict:
        return asdict(self)

    @classmethod
    def from_dict(cls, d: dict) -> "Campaign":
        known = {k: d[k] for k in cls.__dataclass_fields__ if k in d}
        return cls(**known)


@dataclass
class SubmitResult:
    status: str                       # submitted | needs_manual | failed
    external_id: str | None = None
    detail: str = ""


class CampaignProvider:
    """Subclass, set `name`, implement the three methods, and register it in
    factory/campaigns/__init__.py. `settings` is this provider's block from
    config.yaml -> campaigns.providers.<name>."""
    name = "base"

    def __init__(self, settings: dict | None = None):
        self.settings = settings or {}

    def available(self) -> tuple[bool, str]:
        """(ready?, why-not). Called before any network work."""
        return True, ""

    def list_campaigns(self) -> list[Campaign]:
        raise NotImplementedError

    def submit(self, campaign: Campaign, post_url: str, caption: str = "") -> SubmitResult:
        raise NotImplementedError

    def refresh(self, external_id: str) -> str | None:
        """Current status of an earlier submission (submitted/approved/denied),
        or None if the platform can't tell us."""
        return None
