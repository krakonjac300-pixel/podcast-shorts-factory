#!/usr/bin/env bash
# Times ProceduralSoundBank on Mono's JIT (a closer proxy for the Unity editor than .NET's RyuJIT).
# Needs the .NET SDK (for the Roslyn C# 9 compiler) and a Mono runtime (e.g. apt install mono-runtime).
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
bank="$here/../../Assets/SecondCursor/Scripts/Core/Audio/ProceduralSoundBank.cs"
csc="$(dirname "$(command -v dotnet)")/sdk/$(dotnet --version)/Roslyn/bincore/csc.dll"
[ -f "$csc" ] || csc="$(ls -d /usr/lib/dotnet/sdk/*/Roslyn/bincore/csc.dll | tail -1)"
lib=/usr/lib/mono/4.5
mkdir -p "$here/out"
dotnet "$csc" -nologo -langversion:9 -optimize+ -nostdlib \
  -r:"$lib/mscorlib.dll" -r:"$lib/System.dll" -r:"$lib/System.Core.dll" \
  -out:"$here/out/MonoBench.exe" "$bank" "$here/MonoBench.cs"
mono "$here/out/MonoBench.exe"
