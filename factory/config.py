"""Load config.yaml + .env into one place."""
from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path

import yaml
from dotenv import load_dotenv

# Make console output UTF-8 safe everywhere (rich uses →, ✓, ↳ glyphs that crash
# on legacy Windows cp1252 streams). Harmless on platforms that already use UTF-8.
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8")  # type: ignore[union-attr]
    except Exception:  # noqa: BLE001
        pass


def _ensure_ffmpeg_on_path() -> None:
    """Scheduled tasks and fresh shells may not have ffmpeg on PATH even when it's
    installed (winget updates PATH persistently, but a given process may predate
    that). If 'ffmpeg' isn't resolvable, locate the winget install and prepend it."""
    if shutil.which("ffmpeg"):
        return
    local = os.environ.get("LOCALAPPDATA", "")
    pkgs = Path(local) / "Microsoft" / "WinGet" / "Packages"
    if pkgs.exists():
        for exe in pkgs.glob("Gyan.FFmpeg*/**/ffmpeg.exe"):
            os.environ["PATH"] = str(exe.parent) + os.pathsep + os.environ.get("PATH", "")
            return


_ensure_ffmpeg_on_path()

ROOT = Path(__file__).resolve().parent.parent
WORK = ROOT / "workdir"          # downloads, audio, rendered clips
WORK.mkdir(exist_ok=True)

# Keep the Whisper/HF model cache on the same drive as the project (so nothing
# silently fills up C:). Set before faster-whisper / huggingface_hub import.
os.environ.setdefault("HF_HOME", str(ROOT / ".hfcache"))

# Route ALL scratch/temp (ffmpeg, whisper, downloads) to the project drive too.
# C: was hitting 90% full, which triggered Windows low-disk cleanup that wiped
# the factory. Keeping temp on D: stops the pipeline from adding to C: pressure.
_TMP = ROOT / ".tmp"
_TMP.mkdir(exist_ok=True)
for _v in ("TMPDIR", "TEMP", "TMP"):
    os.environ[_v] = str(_TMP)


class Config:
    def __init__(self, path: str | Path = ROOT / "config.yaml"):
        load_dotenv(ROOT / ".env")
        with open(path, "r", encoding="utf-8") as f:
            self._d = yaml.safe_load(f)

    # dotted access: cfg.get("finder.whisper_model")
    def get(self, dotted: str, default=None):
        node = self._d
        for part in dotted.split("."):
            if not isinstance(node, dict) or part not in node:
                return default
            node = node[part]
        return node

    @property
    def finder(self):    return self._d.get("finder", {})
    @property
    def editor(self):    return self._d.get("editor", {})
    @property
    def uploader(self):  return self._d.get("uploader", {})
    @property
    def manager(self):   return self._d.get("manager", {})

    def model_for(self, agent: str) -> str:
        """Per-agent Claude model. Falls back to models.default, then Sonnet."""
        models = self._d.get("models", {}) or {}
        return models.get(agent) or models.get("default") or "claude-sonnet-4-6"

    @staticmethod
    def env(name: str, default=None):
        return os.environ.get(name, default)


cfg = Config()
