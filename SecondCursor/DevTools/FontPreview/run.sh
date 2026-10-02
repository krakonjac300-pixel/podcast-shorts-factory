#!/usr/bin/env bash
# Validate SecondCursor.Core.Art.PixelFontData and render PNG previews into ./out
#   1. compile PixelFontData.cs alone against netstandard2.1 / C# 9 (Unity Core constraints)
#   2. run the C# validator (writes out/font.json + out/samples_cs.pgm reference render)
#   3. render PNG previews with Python 3 + Pillow (cross-checked against the C# render)
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p out
dotnet build NetStandardCheck/NetStandardCheck.csproj -c Release -nologo -v q
dotnet run --project FontPreview.csproj -c Release
python3 render_preview.py
