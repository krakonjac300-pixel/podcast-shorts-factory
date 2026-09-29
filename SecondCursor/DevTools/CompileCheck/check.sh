#!/usr/bin/env bash
# Compile-checks every SecondCursor assembly configuration against real Unity reference assemblies.
# Usage: ./check.sh    (run ./setup.sh once first)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
[ -f "$HERE/.deps/ugui/UnityEngine.UI.dll" ] || { echo "Run setup.sh (download) or setup_local.sh (local Unity) first"; exit 2; }
status=0
for p in Core Runtime RuntimeEditor RuntimeInputSystem RuntimeDemo RuntimeSteam RuntimeSteamDemo Editor; do
  out=$(dotnet build "$HERE/$p.csproj" -nologo -v q -clp:NoSummary 2>&1)
  errs=$(echo "$out" | grep -E " error " | sed -E 's/ \[[^]]*\]$//' | sort -u)
  warns=$(echo "$out" | grep -E " warning " | grep -v "MSB3277\|NU1" | sed -E 's/ \[[^]]*\]$//' | sort -u)
  if [ -n "$errs" ]; then echo "=== $p: FAILED"; echo "$errs"; status=1; else echo "=== $p: OK"; fi
  [ -n "$warns" ] && echo "$warns" | sed 's/^/  warn: /'
done
exit $status
