#!/usr/bin/env bash
# Downloads Unity reference assemblies and builds uGUI from Unity's public mirror so the
# game scripts can be compile-checked without a Unity install. Output goes to .deps/ (gitignored).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
DEPS="$HERE/.deps"; TMP="$(mktemp -d)"
mkdir -p "$DEPS/engine" "$DEPS/editor" "$DEPS/ugui"
curl -sSL -o "$TMP/m.nupkg" https://api.nuget.org/v3-flatcontainer/unityengine.modules/2021.3.33/unityengine.modules.2021.3.33.nupkg
unzip -q -o "$TMP/m.nupkg" -d "$TMP/m" && cp "$TMP"/m/lib/net45/*.dll "$DEPS/engine/"
curl -sSL -o "$TMP/sdk.nupkg" https://api.nuget.org/v3-flatcontainer/unity3d.sdk/2021.1.14.1/unity3d.sdk.2021.1.14.1.nupkg
unzip -q -o "$TMP/sdk.nupkg" -d "$TMP/sdk" && cp "$TMP/sdk/lib/UnityEditor.dll" "$DEPS/editor/"
git clone -q --depth 1 --branch 6000.0 https://github.com/Unity-Technologies/uGUI "$TMP/ugui"
SRC="$TMP/ugui/com.unity.ugui/Runtime/UGUI"
# Two engine APIs newer than the 2021.3 reference modules: patch them out for the harness only.
sed -i 's/rectTransform.sendChildDimensionsChange = \(true\|false\);//' "$SRC/UI/Core/Layout/LayoutGroup.cs"
cat > "$TMP/Shim.cs" <<'CS'
namespace UnityEngine { [System.Flags] public enum PenStatus { None = 0, Contact = 1, Barrel = 2, Inverted = 4, Eraser = 8 } }
CS
cat > "$TMP/UnityEngine.UI.csproj" <<CSPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework><AssemblyName>UnityEngine.UI</AssemblyName><LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems><GenerateAssemblyInfo>false</GenerateAssemblyInfo>
    <DefineConstants>UNITY_2021_3_OR_NEWER;PACKAGE_PHYSICS;PACKAGE_PHYSICS2D;PACKAGE_ANIMATION</DefineConstants>
    <NoWarn>\$(NoWarn);CS0618;CS0649;CS0414;CS0108;CS0114;CS0672;CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$SRC/**/*.cs" /><Compile Include="$TMP/Shim.cs" />
    <Reference Include="$DEPS/engine/*.dll" Private="false" />
  </ItemGroup>
</Project>
CSPROJ
dotnet build "$TMP/UnityEngine.UI.csproj" -c Release -o "$TMP/out" -nologo -v q
cp "$TMP/out/UnityEngine.UI.dll" "$DEPS/ugui/"
rm -rf "$TMP"
echo "Unity reference assemblies ready in $DEPS"
