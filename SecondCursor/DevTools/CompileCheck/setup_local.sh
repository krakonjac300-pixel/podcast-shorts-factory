#!/usr/bin/env bash
# Fills .deps/ from a local Unity install instead of downloading (setup.sh): the engine and editor module
# assemblies of the installed Editor, and uGUI as the project compiled it. Nothing is written outside .deps/.
# Usage: ./setup_local.sh "<Unity>/Editor/Data" "<project>/Library"
#   e.g. ./setup_local.sh "/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Data" "/d/Downloads/Podaci/Project 1/Library"
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DATA="${1:?Unity Editor Data folder}"
LIB="${2:?the Library folder of the Unity project (for UnityEngine.UI.dll)}"
MANAGED="$DATA/Managed/UnityEngine"
DEPS="$HERE/.deps"
[ -f "$MANAGED/UnityEngine.dll" ] || { echo "No UnityEngine.dll in $MANAGED"; exit 2; }
[ -f "$LIB/ScriptAssemblies/UnityEngine.UI.dll" ] || { echo "No UnityEngine.UI.dll in $LIB/ScriptAssemblies (open the project once)"; exit 2; }
rm -rf "$DEPS"
mkdir -p "$DEPS/engine" "$DEPS/editor" "$DEPS/ugui"
cp "$MANAGED/UnityEngine.dll" "$DEPS/engine/"
for f in "$MANAGED"/UnityEngine.*Module.dll; do cp "$f" "$DEPS/engine/"; done
cp "$MANAGED/UnityEditor.dll" "$DEPS/editor/"
for f in "$MANAGED"/UnityEditor.*Module.dll; do cp "$f" "$DEPS/editor/"; done
cp "$LIB/ScriptAssemblies/UnityEngine.UI.dll" "$DEPS/ugui/"
# Version defines of this Unity (6000.N gives UNITY_6000_1_OR_NEWER up to UNITY_6000_N_OR_NEWER), like Unity's own compile.
VERSION="$(basename "$(dirname "$(dirname "$DATA")")")"
MINOR="$(echo "$VERSION" | cut -d. -f2)"
EXTRA=""
case "$VERSION" in
  6000.*) for i in $(seq 1 "$MINOR"); do EXTRA="$EXTRA;UNITY_6000_${i}_OR_NEWER"; done ;;
esac
{
  echo '<Project>'
  echo '  <PropertyGroup>'
  echo "    <UnityDefines>\$(UnityDefines)$EXTRA</UnityDefines>"
  echo '  </PropertyGroup>'
  echo '</Project>'
} > "$DEPS/local.props"
echo "Unity $VERSION reference assemblies copied into $DEPS ($(ls "$DEPS/engine" | wc -l) engine, $(ls "$DEPS/editor" | wc -l) editor)"
