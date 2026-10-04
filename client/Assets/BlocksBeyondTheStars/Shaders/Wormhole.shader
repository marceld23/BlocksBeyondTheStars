// The wormhole (#2242, WormholeView): one shader, two materials.
//   _Halo = 0 — the TEAR: a jagged vertical crack mesh (u across, 0/1 = its edges; v along). A white-blue rim that
//               flickers along its edge, crackle lines running down the spine, and inside another sky: a deep tint of
//               the twin system's nebula with drifting clouds and a few twinkling stars.
//   _Halo = 1 — the GLOW around it on a camera-facing quad: a soft halo, and on Medium and above (_Sc_ScreenFx, the
//               opaque copy exists) the stars, nebula and planets behind it are pulled toward the tear — space bends.
// Kid-friendly: sparkling and mysterious, never a horror maw. No texture, no depth; the Built-in fallback draws the
// same tear and the glow without the lensing (and Low/Potato/browser read the same: bright rim + dark core + halo).
// Every material property sits in the one UnityPerMaterial cbuffer (SRP Batcher); the global stays outside it.
Shader "BlocksBeyondTheStars/Wormhole"
{
    Properties
    {
        _Color ("Rim colour", Color) = (0.78, 0.88, 1, 1)
        _Inner ("Inside colour (the twin system's tint)", Color) = (0.42, 0.16, 0.75, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 2.6
        _Seed ("Seed", Float) = 0
        _Halo ("Halo pass (0 = the tear, 1 = the glow)", Float) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Inner;
                half _Intensity;
                float _Seed;
                half _Halo;
            CBUFFER_END

            float _Sc_ScreenFx; // 1 when the opaque copy exists (Medium and above) — set by ClientSettings

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float4 centerPos : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.centerPos = ComputeScreenPos(TransformObjectToHClip(float3(0.0, 0.0, 0.0)));
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                if (_Halo > 0.5)
                {
                    float2 c = i.uv * 2.0 - 1.0;
                    float r = length(c);
                    float glow = saturate(1.0 - r);
                    glow *= glow;
                    half3 col = _Color.rgb * glow * 0.55 * _Intensity + _Inner.rgb * glow * glow * 0.6;
                    half alpha = glow * 0.6;
                    if (_Sc_ScreenFx > 0.5)
                    {
                        // Space bends: sample the scene behind, pulled toward the tear's centre.
                        float2 uv = i.screenPos.xy / i.screenPos.w;
                        float2 cuv = i.centerPos.xy / i.centerPos.w;
                        float pull = saturate(1.0 - r) * 0.32;
                        half3 bent = SampleSceneColor(uv - (uv - cuv) * pull);
                        return half4(bent + col, saturate(1.0 - r * r));
                    }

                    return half4(col, alpha);
                }

                float edge = min(i.uv.x, 1.0 - i.uv.x) * 2.0; // 0 at the edges, 1 on the spine
                float n = Noise(float2(i.uv.y * 18.0 + _Seed, t * 1.7));
                float rim = exp(-edge * (5.0 + 3.0 * n)) * (0.8 + 0.4 * sin(t * 9.0 + i.uv.y * 40.0 + _Seed));

                float2 sp = float2(i.uv.x * 6.0, i.uv.y * 20.0) + _Seed;
                float neb = Noise(sp * 0.7 + float2(t * 0.05, -t * 0.08)) * 0.7 + Noise(sp * 2.1) * 0.3;
                float2 cell = floor(sp * 3.0);
                float star = step(0.985, Hash21(cell)) * (0.6 + 0.4 * sin(t * 3.0 + Hash21(cell + 7.0) * 6.28));
                half3 inside = lerp(half3(0.02, 0.0, 0.05), _Inner.rgb, neb * 0.8) + star.xxx;

                float crack = smoothstep(0.96, 1.0, Noise(float2(i.uv.x * 40.0 + t * 2.0, i.uv.y * 8.0 + _Seed))) * (1.0 - edge * 0.5);
                half3 col = inside + _Color.rgb * (rim + crack * 0.8) * _Intensity;
                return half4(col, saturate(0.92 + rim));
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (the same tear and glow, no lensing) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            fixed4 _Inner;
            half _Intensity;
            float _Seed;
            half _Halo;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                if (_Halo > 0.5)
                {
                    float r = length(i.uv * 2.0 - 1.0);
                    float glow = saturate(1.0 - r);
                    glow *= glow;
                    return fixed4(_Color.rgb * glow * 0.55 * _Intensity + _Inner.rgb * glow * glow * 0.6, glow * 0.6);
                }

                float edge = min(i.uv.x, 1.0 - i.uv.x) * 2.0;
                float n = Noise(float2(i.uv.y * 18.0 + _Seed, t * 1.7));
                float rim = exp(-edge * (5.0 + 3.0 * n)) * (0.8 + 0.4 * sin(t * 9.0 + i.uv.y * 40.0 + _Seed));
                float2 sp = float2(i.uv.x * 6.0, i.uv.y * 20.0) + _Seed;
                float neb = Noise(sp * 0.7 + float2(t * 0.05, -t * 0.08)) * 0.7 + Noise(sp * 2.1) * 0.3;
                float star = step(0.985, Hash21(floor(sp * 3.0)));
                fixed3 inside = lerp(fixed3(0.02, 0.0, 0.05), _Inner.rgb, neb * 0.8) + star;
                return fixed4(inside + _Color.rgb * rim * _Intensity, saturate(0.92 + rim));
            }
            ENDCG
        }
    }

    Fallback Off
}
