#!/usr/bin/env bash
# Shortcut so you never have to activate the venv:
#   ./factory.sh setup | ./factory.sh auto "<url>" | ./factory.sh daily ...
cd "$(dirname "$0")"
if [ ! -x ./.venv/bin/python ]; then
    echo "[!] Not installed yet - run: bash install.sh"
    exit 1
fi
exec ./.venv/bin/python run.py "$@"
