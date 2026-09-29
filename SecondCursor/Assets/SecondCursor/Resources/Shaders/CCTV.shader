// SecondCursor/CCTV - pipeline-agnostic fake lighting for the SecureView security-camera set.
//
// Works unchanged in the Built-in pipeline and in URP: one untagged CG pass (URP runs it as SRPDefaultUnlit),
// no keywords, no textures, no shadows and no Unity lights. SecurityCameraRig sets these globals for the camera
// that is about to render (only one CCTV camera renders at a time):
//   _SC_LightA, _SC_LightB    fluorescent tube as a line segment (world space)
//   _SC_LightIntensity         tube brightness (flicker already applied), _SC_LightRange = half-brightness distance
//   _SC_MonitorPos/_SC_MonitorDir/_SC_MonitorIntensity   CRT glow, only reaches what is in front of the screen
//   _SC_Ambient                ambient floor
//   _SC_FogColor (grey in .r), _SC_FogDensity   exponential distance fog
//   _SC_CameraPos              position of the rendering camera (own copy, so it is identical in every pipeline)
// Per material: _Color (grey albedo) and _Emission (added after lighting; the CRT screen and the ceiling tubes).
// All maths happens in display (gamma) space so the footage looks the same in Gamma and Linear projects.
Shader "SecondCursor/CCTV"
{
    Properties
    {
        [MainColor] _Color ("Albedo (grey)", Color) = (0.5, 0.5, 0.5, 1)
        _Emission ("Emission", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            ZWrite On
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4 _Color;
            float _Emission;

            // Globals (deliberately not in the Properties block, so materials never override them).
            float4 _SC_LightA;
            float4 _SC_LightB;
            float _SC_LightIntensity;
            float _SC_LightRange;
            float4 _SC_MonitorPos;
            float4 _SC_MonitorDir;
            float _SC_MonitorIntensity;
            float _SC_Ambient;
            float4 _SC_FogColor;
            float _SC_FogDensity;
            float4 _SC_CameraPos;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag (v2f i) : SV_Target
            {
                float3 n = normalize(i.worldNormal);
                float3 p = i.worldPos;

                // Unity linearises material colours in Linear projects: bring the albedo back to display space.
                float3 albedo = _Color.rgb;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                albedo = LinearToGammaSpace(albedo);
            #endif
                float grey = dot(albedo, float3(0.299, 0.587, 0.114));

                // Fluorescent tube: nearest point on the segment, soft wrap-around lambert, smooth distance falloff.
                float3 ab = _SC_LightB.xyz - _SC_LightA.xyz;
                float t = saturate(dot(p - _SC_LightA.xyz, ab) / max(dot(ab, ab), 0.0001));
                float3 toLight = _SC_LightA.xyz + ab * t - p;
                float dist2 = max(dot(toLight, toLight), 0.000001);
                float3 l = toLight * rsqrt(dist2);
                float range = max(_SC_LightRange, 0.001);
                float tube = _SC_LightIntensity * saturate(dot(n, l) * 0.8 + 0.2) / (1.0 + dist2 / (range * range));

                // CRT glow: a soft point light that only reaches surfaces in front of the screen.
                float3 toMonitor = _SC_MonitorPos.xyz - p;
                float mdist2 = max(dot(toMonitor, toMonitor), 0.000001);
                float3 ml = toMonitor * rsqrt(mdist2);
                float inFront = saturate(-dot(ml, _SC_MonitorDir.xyz));
                float glow = _SC_MonitorIntensity * inFront * saturate(dot(n, ml) * 0.8 + 0.2) / (1.0 + mdist2 * 2.5);

                // Ambient floor, slightly brighter on upward-facing surfaces.
                float ambient = _SC_Ambient * (0.75 + 0.25 * n.y);

                float c = grey * (ambient + tube + glow) + _Emission;

                // Exponential distance fog towards a dark grey.
                float fog = exp(-_SC_FogDensity * distance(p, _SC_CameraPos.xyz));
                c = saturate(lerp(_SC_FogColor.r, c, fog));

                float3 col = float3(c, c, c);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                col = GammaToLinearSpace(col);
            #endif
                return float4(col, 1.0);
            }
            ENDCG
        }
    }

    FallBack Off
}
