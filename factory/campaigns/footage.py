"""Fetch a campaign's approved source footage from wherever it's hosted.

Campaigns rarely point at YouTube: they hand clippers a Google Drive folder,
a Dropbox link, a Frame.io share or raw .mp4 URLs. This module turns any of
those into local video files the Finder can transcribe.

  Google Drive file   direct download (no extra packages)
  Google Drive folder needs `pip install gdown`
  Dropbox file/folder dl=1 download (folders arrive as a .zip and are unpacked)
  direct .mp4/.mov    streamed download
  anything else       yt-dlp (YouTube, Vimeo, Twitch/Kick VODs, X, ...)

Hosts none of these can reach (Frame.io, WeTransfer, Mega, private shares):
download the files yourself into campaign_footage/<campaign-slug>/ and the
Campaigner picks them up from there like any other footage.
"""
from __future__ import annotations

import re
import shutil
import urllib.parse
import urllib.request
import zipfile
from pathlib import Path

from ..config import ROOT, WORK

DROP_DIR = ROOT / "campaign_footage"
VIDEO_EXT = {".mp4", ".mov", ".m4v", ".mkv", ".webm", ".avi"}
_UA = {"User-Agent": "Mozilla/5.0 (podcast-shorts-factory)"}


def slug(campaign_id: str) -> str:
    return re.sub(r"[^A-Za-z0-9_.-]+", "_", campaign_id).strip("_")[:80]


def _dest(campaign_id: str) -> Path:
    d = WORK / "campaigns" / slug(campaign_id)
    d.mkdir(parents=True, exist_ok=True)
    return d


def _filename_from(resp, fallback: str) -> str:
    cd = resp.headers.get("Content-Disposition", "") or ""
    m = re.search(r"filename\*=UTF-8''([^;]+)", cd) or re.search(r'filename="?([^";]+)"?', cd)
    name = urllib.parse.unquote(m.group(1)) if m else fallback
    return re.sub(r'[\\/:*?"<>|]+', "_", name).strip() or fallback


def _stream(url: str, dest_dir: Path, fallback_name: str) -> Path:
    req = urllib.request.Request(url, headers=_UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        ctype = (r.headers.get("Content-Type") or "").lower()
        if "text/html" in ctype:
            raise RuntimeError("got a web page, not a file — the link may be "
                               "private or need a login")
        path = dest_dir / _filename_from(r, fallback_name)
        if path.exists() and path.stat().st_size > 0:
            return path                       # already fetched on an earlier run
        tmp = path.with_suffix(path.suffix + ".part")
        with open(tmp, "wb") as f:
            shutil.copyfileobj(r, f, length=1 << 20)
        tmp.replace(path)
    return path


def _videos_in(paths) -> list[Path]:
    return sorted(p for p in paths if p.suffix.lower() in VIDEO_EXT and p.is_file())


def _unzip(path: Path) -> list[Path]:
    out = path.with_suffix("")
    with zipfile.ZipFile(path) as z:
        z.extractall(out)
    return _videos_in(out.rglob("*"))


def _drive_id(url: str) -> str | None:
    m = re.search(r"/(?:file/d|d)/([A-Za-z0-9_-]{10,})", url) or \
        re.search(r"[?&]id=([A-Za-z0-9_-]{10,})", url)
    return m.group(1) if m else None


def _google_drive(url: str, dest: Path) -> list[Path]:
    if "/folders/" in url:
        try:
            import gdown  # type: ignore
        except ImportError:
            raise RuntimeError("Google Drive FOLDER links need `pip install gdown` "
                               "(or download the files into campaign_footage/)") from None
        got = gdown.download_folder(url, output=str(dest), quiet=True,
                                    remaining_ok=True) or []
        return _videos_in(Path(p) for p in got)
    fid = _drive_id(url)
    if not fid:
        raise RuntimeError("couldn't find a Drive file id in the link")
    direct = (f"https://drive.usercontent.google.com/download?id={fid}"
              "&export=download&confirm=t")
    p = _stream(direct, dest, f"{fid}.mp4")
    return _unzip(p) if p.suffix.lower() == ".zip" else [p]


def _dropbox(url: str, dest: Path) -> list[Path]:
    parts = urllib.parse.urlsplit(url)
    q = dict(urllib.parse.parse_qsl(parts.query))
    q["dl"] = "1"
    direct = urllib.parse.urlunsplit(parts._replace(query=urllib.parse.urlencode(q)))
    p = _stream(direct, dest, "dropbox_download")
    if p.suffix.lower() == ".zip" or zipfile.is_zipfile(p):
        return _unzip(p)
    return [p]


def _direct(url: str, dest: Path) -> list[Path]:
    name = Path(urllib.parse.urlsplit(url).path).name or "footage.mp4"
    return [_stream(url, dest, name)]


def _ytdlp(url: str) -> list[Path]:
    from ..utils import media
    path, _title, _ch = media.download(url)
    return [path]


def fetch(url: str, campaign_id: str) -> list[Path]:
    """Download one footage link. Returns the local video files it contained."""
    dest = _dest(campaign_id)
    host = urllib.parse.urlsplit(url).netloc.lower()
    path = urllib.parse.urlsplit(url).path.lower()
    if "drive.google.com" in host or "docs.google.com" in host \
            or "drive.usercontent.google.com" in host:
        return _google_drive(url, dest)
    if "dropbox.com" in host:
        return _dropbox(url, dest)
    if Path(path).suffix in VIDEO_EXT:
        return _direct(url, dest)
    if any(h in host for h in ("frame.io", "f.io", "wetransfer", "we.tl", "mega.nz")):
        raise RuntimeError(f"{host} links can't be auto-downloaded — save the files "
                           f"into campaign_footage/{slug(campaign_id)}/")
    return _ytdlp(url)


def dropped_files(campaign_id: str) -> list[Path]:
    """Videos the human saved by hand into campaign_footage/<slug>/."""
    d = DROP_DIR / slug(campaign_id)
    return _videos_in(d.rglob("*")) if d.exists() else []
