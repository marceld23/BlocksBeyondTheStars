// Layered energy beam for LineRenderers (VFX overhaul #2152): a narrow white-hot core, a coloured falloff glow
// across the beam, two procedural noises flowing along it at different speeds (no texture), optional travelling
// pulse rings (the asteroid breaker / mining beam), all additive (Blend One One) so the beam only adds light and
// crosses the bloom threshold through _Intensity. The LineRenderer runs in LineTextureMode.Tile, so uv.x is the
// distance along the beam in world units and the noise never stretches with the beam length; uv.y runs across.
// The vertex colour (the LineRenderer's colour gradient) carries the tint and the alpha that fades the beam's ends
// and its life — so one cached material serves every beam of a style. Meshes with white vertex colours (the melee
// slash ribbons) take their colour from _Tint instead.
// DUAL-PIPELINE: URP SubShader; the Built-in RP falls back to the additive Particle shader.
Shader "BlocksBeyondTheStars/FxBeam"
{
    Properties
    {
        _Color2 ("Core colour", Color) = (1, 1, 1, 1)
        _Tint ("Glow tint (x vertex colour)", Color) = (1, 1, 1, 1)
        _Intensity ("HDR intensity", Range(0, 8)) = 2.5
        _CoreWidth ("Core width (0..1 of the beam)", Range(0.02, 1)) = 0.3
        _NoiseScale ("Noise scale (per block)", Range(0.05, 8)) = 1.2
        _NoiseSpeed ("Noise speed", Range(0, 40)) = 9
        _Pulse ("Pulse rings speed (0 = off)", Range(0, 20)) = 0
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FxCommon.hlsl"

            // SRP Batcher (#573): every material property in UnityPerMaterial.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color2;
                float4 _Tint;
                float _Intensity;
                float _CoreWidth;
                float _NoiseScale;
                float _NoiseSpeed;
                float _Pulse;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float across = abs(i.uv.y * 2.0 - 1.0); // 0 on the axis, 1 at the edge
                float core = 1.0 - smoothstep(_CoreWidth * 0.45, _CoreWidth, across);
                float glow = pow(saturate(1.0 - across), 2.4);

                float t = _Time.y * _NoiseSpeed;
                float n1 = BbtsNoise(float2(i.uv.x * _NoiseScale - t, across * 3.0));
                float n2 = BbtsNoise(float2(i.uv.x * _NoiseScale * 2.3 + t * 1.7, across * 5.0 + 7.0));
                float flow = 0.45 + 1.1 * n1 * n2;

                float rings = 0.0;
                if (_Pulse > 0.0)
                {
                    rings = smoothstep(0.78, 1.0, frac(i.uv.x * 0.5 - _Time.y * _Pulse)) * glow;
                }

                float3 col = i.color.rgb * _Tint.rgb * (glow * flow + rings * 1.6) + _Color2.rgb * core * 1.5;
                return half4(col * (_Intensity * i.color.a), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback "BlocksBeyondTheStars/Particle"
}
