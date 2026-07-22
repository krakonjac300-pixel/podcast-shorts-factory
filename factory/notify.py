"""How the agents talk to the human.

Two channels, both best-effort (a notification failure never breaks the pipeline):

1. `activity.md` in the project root — a plain-language, newest-first feed of
   everything the agents did ("Posted X → url", "Queue ready with 3 clips",
   "Upload failed: ..."). Open it anytime to catch up.
2. Windows toast notifications — pop up in the corner even when the pipeline
   runs from the Task Scheduler, so posts/failures surface immediately.
   Disable with notify.toast: false in config.yaml.
"""
from __future__ import annotations

import subprocess
from datetime import datetime

from .config import ROOT, cfg

FEED = ROOT / "activity.md"
_HEADER = "# Factory activity feed\n\n*Newest first. Written by the agents.*\n\n"


def _append_feed(line: str) -> None:
    stamp = datetime.now().strftime("%Y-%m-%d %H:%M")
    entry = f"- **{stamp}** — {line}\n"
    try:
        if FEED.exists():
            text = FEED.read_text(encoding="utf-8")
            body = text[len(_HEADER):] if text.startswith(_HEADER) else text
            FEED.write_text(_HEADER + entry + body, encoding="utf-8")
        else:
            FEED.write_text(_HEADER + entry, encoding="utf-8")
    except OSError:
        pass


def _toast(title: str, message: str) -> None:
    """Windows toast via PowerShell/WinRT — no extra packages needed."""
    if not cfg.get("notify.toast", True):
        return
    ps = """
[Windows.UI.Notifications.ToastNotificationManager, Windows.UI.Notifications, ContentType = WindowsRuntime] | Out-Null
[Windows.Data.Xml.Dom.XmlDocument, Windows.Data.Xml.Dom.XmlDocument, ContentType = WindowsRuntime] | Out-Null
$xml = [Windows.UI.Notifications.ToastNotificationManager]::GetTemplateContent(
    [Windows.UI.Notifications.ToastTemplateType]::ToastText02)
$texts = $xml.GetElementsByTagName('text')
$texts.Item(0).AppendChild($xml.CreateTextNode(__TITLE__)) | Out-Null
$texts.Item(1).AppendChild($xml.CreateTextNode(__MESSAGE__)) | Out-Null
$toast = [Windows.UI.Notifications.ToastNotification]::new($xml)
[Windows.UI.Notifications.ToastNotificationManager]::CreateToastNotifier(
    'Podcast Shorts Factory').Show($toast)
""".replace("__TITLE__", _ps_quote(title)).replace("__MESSAGE__", _ps_quote(message))
    try:
        subprocess.run(
            ["powershell", "-NoProfile", "-NonInteractive", "-Command", ps],
            capture_output=True, timeout=15,
            creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
    except Exception:  # noqa: BLE001 - toasts are nice-to-have, never fatal
        pass


def _ps_quote(s: str) -> str:
    return "'" + (s or "").replace("'", "''") + "'"


def _phone(title: str, message: str, url: str | None) -> None:
    """Push to the user's phone via ntfy.sh (free, no account). The user installs
    the ntfy app, subscribes to a secret topic, and sets notify.ntfy_topic in
    config.yaml. Off until that's configured."""
    topic = (cfg.get("notify.ntfy_topic") or "").strip()
    if not topic:
        return
    try:
        import urllib.request
        req = urllib.request.Request(
            f"https://ntfy.sh/{topic}",
            data=message.encode("utf-8"),
            headers={"Title": title.encode("ascii", "ignore").decode(),
                     **({"Click": url} if url else {})},
            method="POST")
        urllib.request.urlopen(req, timeout=10).close()
    except Exception:  # noqa: BLE001 - phone push is nice-to-have, never fatal
        pass


def notify(title: str, message: str, url: str | None = None) -> None:
    """Tell the human something happened: activity feed + toast + phone."""
    line = f"**{title}** — {message}" + (f" → {url}" if url else "")
    _append_feed(line)
    _toast(title, message + (f"\n{url}" if url else ""))
    _phone(title, message, url)
