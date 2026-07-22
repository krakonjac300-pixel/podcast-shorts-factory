#!/usr/bin/env bash
# ── Podcast Shorts Factory — one-click installer (macOS / Linux) ─────────
# Run:  bash install.sh
set -e
cd "$(dirname "$0")"

echo
echo " ============================================"
echo "  PODCAST SHORTS FACTORY - Installer"
echo " ============================================"
echo

if ! command -v python3 >/dev/null; then
    echo "[!] python3 not found. Install Python 3.11+ first (https://www.python.org/downloads/)"
    exit 1
fi
echo "[ok] $(python3 --version)"

if ! command -v ffmpeg >/dev/null; then
    echo "[!] ffmpeg not found. Install it (mac: brew install ffmpeg / debian: sudo apt install ffmpeg)"
    echo "    You can finish this installer and add ffmpeg after."
else
    echo "[ok] ffmpeg found"
fi

if [ ! -d .venv ]; then
    echo "[..] Creating virtual environment..."
    python3 -m venv .venv
fi
echo "[..] Installing dependencies (2-5 minutes, one time only)..."
./.venv/bin/python -m pip install --disable-pip-version-check -q --upgrade pip
./.venv/bin/python -m pip install --disable-pip-version-check -q -r requirements.txt
echo "[ok] Dependencies installed."
echo
echo " Launching the setup wizard..."
echo
./.venv/bin/python run.py setup
echo
echo " Done! From now on, run commands with:  ./factory.sh auto \"https://youtube.com/watch?v=...\""
