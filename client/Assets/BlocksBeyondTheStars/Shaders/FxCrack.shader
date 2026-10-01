// Mining crack overlay (VFX overhaul #2155) on a cube a hair larger than the block being mined. Procedural — no
// texture: a Voronoi edge network on each face whose cracks appear cell by cell as _Progress (the server's
// MiningProgress fraction) rises, spreading from the face centre outward like Minecraft's ten destroy stages but
// continuous. Dark cracks are alpha-blended over the block; with _Heat > 0 (the mining beam) the cracks glow
// white-hot in _HotColor (HDR, so bloom catches them) and the whole face warms toward red.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the alpha-blended ParticleAlpha shader.
Shader "BlocksBeyondTheStars/FxCrack"
{
    Properties
    {
        _Progress ("Progress 0..1", Range(0, 1)) = 0
        _Heat ("Heat 0..1", Range(0, 1)) = 0
        _HotColor ("Hot colour", Color) = (1, 0.55, 0.15, 1)
        _Seed ("Seed", Float) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back
        Offset -1, -1

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Progress;
                float _Heat;
                float4 _HotColor;
                float _Seed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float face : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.face = dot(v.normal, float3(1.0, 2.0, 4.0)); // a different crack pattern per face
                return o;
            }

            float2 Hash22(float2 p)
            {
                return float2(BbtsHash21(p), BbtsHash21(p + 17.31));
            }

            // x = distance to the nearest Voronoi edge, y = hash of the cell
            float2 VoronoiEdge(float2 p)
            {
                float2 n = floor(p);
                float2 f = frac(p);
                float2 mg = float2(0, 0);
                float2 mr = float2(0, 0);
                float md = 8.0;
                for (int j = -1; j <= 1; j++)
                {
                    for (int k = -1; k <= 1; k++)
                    {
                        float2 g = float2(k, j);
                        float2 r = g + Hash22(n + g) - f;
                        float d = dot(r, r);
                        if (d < md)
                        {
                            md = d;
                            mr = r;
                            mg = g;
                        }
                    }
                }

                md = 8.0;
                for (int jj = -2; jj <= 2; jj++)
                {
                    for (int kk = -2; kk <= 2; kk++)
                    {
                        float2 g = mg + float2(kk, jj);
                        float2 r = g + Hash22(n + g) - f;
                        float2 diff = r - mr;
                        if (dot(diff, diff) > 1e-5)
                        {
                            md = min(md, dot(0.5 * (mr + r), normalize(diff)));
                        }
                    }
                }

                return float2(md, BbtsHash21(n + mg));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = i.uv * 3.2 + float2(i.face * 1.37 + _Seed, i.face * 2.11);
                float2 v = VoronoiEdge(p);
                float fromCentre = length(i.uv - 0.5) * 1.4;
                // A cell border shows once progress passes its (random + distance) threshold: cracks start in the
                // middle of the face and run out to the edges as the block weakens.
                float threshold = v.y * 0.55 + fromCentre * 0.55;
                float shown = smoothstep(threshold, threshold + 0.08, _Progress * 1.25);
                float width = lerp(0.015, 0.05, _Progress);
                float crack = (1.0 - smoothstep(width * 0.5, width, v.x)) * shown;

                float3 dark = float3(0.03, 0.03, 0.04);
                float3 hot = _HotColor.rgb * (2.5 + 2.0 * _Heat);
                float3 col = lerp(dark, hot, _Heat);
                float alpha = crack * lerp(0.82, 1.0, _Heat);
                // The mining beam warms the whole face.
                float warm = _Heat * _Progress * 0.35;
                col = lerp(_HotColor.rgb * 1.4, col, saturate(alpha / max(1e-3, alpha + warm)));
                alpha = saturate(alpha + warm);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/ParticleAlpha"
}
