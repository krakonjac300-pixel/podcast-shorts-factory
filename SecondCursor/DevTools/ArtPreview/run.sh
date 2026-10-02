#!/usr/bin/env bash
# Build + validate the NEXUS OS pixel art and render preview sheets.
#   ./run.sh            -> out/contact_sheet.png, out/desktop_preview.png
#   ./run.sh --review   -> also out/review/*.png (8x zooms, 1x strip)
set -euo pipefail
cd "$(dirname "$0")"

# 1) C# 9 / .NET Standard 2.1 compile check of the runtime file (no Unity).
dotnet build NetStandardCheck/NetStandardCheck.csproj -nologo -v q -clp:ErrorsOnly

# 2) Load every sprite through the public API, run the checks, dump JSON.
dotnet run --project ArtPreview.csproj -v q -- out

# 3) Render the previews (Python 3 + Pillow).
python3 render_preview.py --json out/sprites.json --out out "$@"
