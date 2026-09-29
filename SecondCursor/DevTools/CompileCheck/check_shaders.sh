#!/bin/bash
# Syntax/type check for the game's CG shaders without Unity: each CGPROGRAM block is compiled with
# glslang's HLSL front end (apt install glslang-tools), in the Linear and the Gamma colour-space variant.
# A small stub stands in for the UnityCG.cginc helpers the shaders use, so this validates the shaders'
# own code, not Unity's include files.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SHADERS="$HERE/../../Assets/SecondCursor/Resources/Shaders"
if ! command -v glslangValidator >/dev/null 2>&1; then
  echo "glslangValidator not found (apt install glslang-tools): shader check skipped"
  exit 0
fi
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

cat > "$WORK/stub.hlsl" <<'STUB'
float4x4 unity_ObjectToWorld;
float4x4 unity_WorldToObject;
float4x4 unity_MatrixVP;
float3 _WorldSpaceCameraPos;
float4 _Time;
inline float4 UnityObjectToClipPos(in float3 pos) { return mul(unity_MatrixVP, mul(unity_ObjectToWorld, float4(pos, 1.0))); }
inline float4 UnityObjectToClipPos(float4 pos) { return UnityObjectToClipPos(pos.xyz); }
inline float3 UnityObjectToWorldNormal(in float3 norm) { return normalize(mul(norm, (float3x3)unity_WorldToObject)); }
inline half3 GammaToLinearSpace(half3 sRGB) { return sRGB * (sRGB * (sRGB * 0.305306011h + 0.682171111h) + 0.012522878h); }
inline half3 LinearToGammaSpace(half3 linRGB) { linRGB = max(linRGB, half3(0.h, 0.h, 0.h)); return max(1.055h * pow(linRGB, 0.416666667h) - 0.055h, 0.h); }
STUB

status=0
for shader in "$SHADERS"/*.shader; do
  name="$(basename "$shader")"
  awk '/CGPROGRAM/{f=1;next}/ENDCG/{f=0}f' "$shader" | grep -v '#pragma' | grep -v '#include' > "$WORK/body.hlsl"
  vert="$(grep -m1 -o '#pragma vertex [A-Za-z_0-9]*' "$shader" | awk '{print $3}')"
  frag="$(grep -m1 -o '#pragma fragment [A-Za-z_0-9]*' "$shader" | awk '{print $3}')"
  cat "$WORK/stub.hlsl" "$WORK/body.hlsl" > "$WORK/full.hlsl"
  for variant in "" "-DUNITY_COLORSPACE_GAMMA"; do
    if ! glslangValidator -D -V $variant -S vert -e "$vert" -o "$WORK/v.spv" "$WORK/full.hlsl" >"$WORK/log" 2>&1 ||
       ! glslangValidator -D -V $variant -S frag -e "$frag" -o "$WORK/f.spv" "$WORK/full.hlsl" >>"$WORK/log" 2>&1; then
      echo "FAIL $name ${variant:-(linear)}"
      grep -E "ERROR|error" "$WORK/log" || cat "$WORK/log"
      status=1
    fi
  done
  [ $status -eq 0 ] && echo "ok   $name"
done
exit $status
