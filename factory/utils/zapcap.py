"""ZapCap caption backend (optional, paid, off by default).

Our own karaoke captions are legible but read a notch below the viral
reference. ZapCap ($0.10/min) burns professionally-designed viral-template
captions with keyword highlighting. When enabled we render the clip through our
FULL pipeline (framing, footage inserts, hook, CTA, music) with our own caption
burn DISABLED, then hand the finished clip to ZapCap for the caption layer only.
Everything we built is kept; only the captions are upgraded.

Flow (https://platform.zapcap.ai/docs): x-api-key auth,
  POST /videos (multipart file)            -> videoId
  GET  /templates                          -> pick a templateId
  POST /videos/{id}/task {autoApprove}     -> taskId
  GET  /videos/{id}/task/{taskId}          -> poll to 'completed', downloadUrl

Set ZAPCAP_API_KEY in .env and captions.provider: zapcap in config to turn on.
Never raises: any failure returns False and the caller keeps our own captions,
so a ZapCap outage can never take the pipeline down or block a post.
"""
from __future__ import annotations

import time
from pathlib import Path

from rich.console import Console

from ..config import cfg

console = Console()
BASE = "https://api.zapcap.ai"


def _key() -> str:
    return (cfg.env("ZAPCAP_API_KEY", "") or "").strip()


def available() -> bool:
    return bool(_key())


def _headers() -> dict:
    return {"x-api-key": _key()}


def _template_id(sess) -> str | None:
    """Configured template, or the first one the account offers."""
    tid = (cfg.get("editor.captions.zapcap_template", "") or "").strip()
    if tid:
        return tid
    r = sess.get(f"{BASE}/templates", headers=_headers(), timeout=30)
    r.raise_for_status()
    items = r.json()
    if isinstance(items, dict):
        items = items.get("templates") or items.get("data") or []
    return items[0]["id"] if items else None


def caption_video(in_path: Path, out_path: Path,
                  poll_s: int = 6, max_wait_s: int = 300) -> bool:
    """Upload `in_path`, caption it via ZapCap, write the result to `out_path`.
    Returns True on success; False (and keeps our captions) on any failure."""
    if not available():
        return False
    try:
        import requests
    except Exception:  # noqa: BLE001
        return False

    in_path, out_path = Path(in_path), Path(out_path)
    if not in_path.exists():
        return False
    try:
        with requests.Session() as sess:
            # 1. upload
            with in_path.open("rb") as f:
                up = sess.post(f"{BASE}/videos", headers=_headers(),
                               files={"file": (in_path.name, f, "video/mp4")},
                               timeout=180)
            up.raise_for_status()
            video_id = up.json().get("id") or up.json().get("videoId")
            if not video_id:
                console.print("  [yellow]zapcap: upload returned no video id[/]")
                return False

            # 2. template + 3. task (autoApprove so no manual transcript step)
            tid = _template_id(sess)
            if not tid:
                console.print("  [yellow]zapcap: no template available[/]")
                return False
            task = sess.post(
                f"{BASE}/videos/{video_id}/task", headers=_headers(),
                json={"templateId": tid, "autoApprove": True,
                      "language": cfg.get("finder.expect_language", "en") or "en"},
                timeout=60)
            task.raise_for_status()
            task_id = task.json().get("taskId") or task.json().get("id")
            if not task_id:
                console.print("  [yellow]zapcap: task creation returned no id[/]")
                return False

            # 4. poll
            url = f"{BASE}/videos/{video_id}/task/{task_id}"
            waited, download = 0, None
            while waited < max_wait_s:
                st = sess.get(url, headers=_headers(), timeout=30).json()
                status = str(st.get("status", "")).lower()
                if status in ("completed", "complete", "done"):
                    download = st.get("downloadUrl") or st.get("url")
                    break
                if status in ("failed", "error"):
                    console.print(f"  [yellow]zapcap: task {status}[/]")
                    return False
                time.sleep(poll_s)
                waited += poll_s
            if not download:
                console.print(f"  [yellow]zapcap: not ready after {max_wait_s}s[/]")
                return False

            # 5. download to a temp then swap (never leave a half-written file)
            tmp = out_path.with_suffix(".zap.mp4")
            with sess.get(download, timeout=180, stream=True) as dl:
                dl.raise_for_status()
                with tmp.open("wb") as f:
                    for chunk in dl.iter_content(1 << 16):
                        f.write(chunk)
            if tmp.stat().st_size < 10_000:
                tmp.unlink(missing_ok=True)
                return False
            tmp.replace(out_path)
            console.print("  [dim]zapcap: captions applied[/]")
            return True
    except Exception as ex:  # noqa: BLE001 - captions are an upgrade, never a gate
        msg = str(ex)
        if "401" in msg or "403" in msg:
            console.print("  [yellow]zapcap: key rejected (401/403) - "
                          "keeping our own captions[/]")
        else:
            console.print(f"  [yellow]zapcap failed ({msg[:60]}) - "
                          f"keeping our own captions[/]")
        return False


def active() -> bool:
    """True when ZapCap should own the caption layer (config + key present)."""
    return (cfg.get("editor.captions.provider", "internal") == "zapcap"
            and available())
