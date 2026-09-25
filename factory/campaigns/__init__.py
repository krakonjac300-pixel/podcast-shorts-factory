"""Clipping-campaign plugins.

Each module here teaches the factory one campaign marketplace. To add a new
platform: subclass `CampaignProvider` (see base.py), then add it to
PROVIDERS below and give it a block in config.yaml -> campaigns.providers.
"""
from __future__ import annotations

from ..config import cfg
from .base import Campaign, CampaignProvider, SubmitResult
from .manual import ManualProvider
from .whop import WhopProvider

PROVIDERS: dict[str, type[CampaignProvider]] = {
    WhopProvider.name: WhopProvider,
    ManualProvider.name: ManualProvider,
}

__all__ = ["Campaign", "CampaignProvider", "SubmitResult", "PROVIDERS",
           "enabled_providers", "provider"]


def provider(name: str) -> CampaignProvider | None:
    cls = PROVIDERS.get(name)
    if not cls:
        return None
    return cls(cfg.get(f"campaigns.providers.{name}", {}) or {})


def enabled_providers() -> list[CampaignProvider]:
    blocks = cfg.get("campaigns.providers", {}) or {}
    return [p for n, b in blocks.items()
            if (b or {}).get("enabled") and (p := provider(n))]
