// Fog banks in the air (#3273): the soft mist cards MistCards.cs writes into one dynamic mesh (world-space vertices, far to
// near). Each card is a camera-facing quad; its shape is drawn here, not baked: a soft ellipse whose rim the tileable noise
// (MistNoiseTexture: R coarse, G fine) bites into, so every card has its own ragged outline and slowly evolving billows
// (the C# side drifts each card's two noise offsets — wrapped at one tile, seamless because the noise repeats and the fine
// octave is sampled at an integer multiple). Per pixel the card fades out within a few metres of the camera
// (MistCardRules.PixelNearFade), never "in your face"; from Medium it fades softly into the ground behind it (soft
// particles, _Sc_ScreenFx × _Sc_SoftParticles like ParticleAlpha); it sinks into the shared haze like every world shader.
// The colour is the height fog's own lit mist colour from the shared include (BbtsHazeMix: _Sc_MistColor, or the haze
// colour on a world without one), so a card is exactly the mist the surfaces behind it sink into.
// Streams: uv0 = (corner u, corner v, noise tiles across the card, -), uv1 = (octave 1 offset xy, octave 2 offset xy),
// colour = (a tint multiplier, white by default; the card's alpha). Unlit, alpha-blended, no depth write, both sides. URP
// only (the shipping pipeline); always-included (built in code via Shader.Find).
Shader "BlocksBeyondTheStars/MistCard"
{
    Properties
    {
        _MainTex ("Noise (R coarse, G fine; tileable)", 2D) = "gray" {}
        _Params ("x = near fade start (m), y = near fade length (m), z = soft-particle depth (m), w = rim bite", Vector) = (2, 8, 2, 0.9)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // soft particles
            #include "AtmosphereCommon.hlsl" // the shared haze (#2408): far banks sink into the distance like the land

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Sc_ScreenFx;      // 1 when the depth texture exists (Medium+)
            float _Sc_SoftParticles; // the player's switch (0/1)

            // SRP Batcher (#573): per-material properties in UnityPerMaterial (the texture handle stays outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Params;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 uv : TEXCOORD0;
                float4 offsets : TEXCOORD1;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;
                float4 offsets : TEXCOORD1;
                float4 color : COLOR;
                float4 haze : TEXCOORD2;
                float4 screenPos : TEXCOORD3;
                float3 wp : TEXCOORD4;
                float3 mist : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                o.offsets = v.offsets;
                o.color = v.color;
                o.haze = BbtsHaze(wp, 1.0, 1.0); // per vertex: a soft card, a flat haze (amount + colour in one evaluation)
                // The card is made of the height fog's own mist: its lit colour (#3271, _Sc_MistColor), warmed toward the sun
                // like the haze — or the haze colour on a world without a mist colour of its own. Same include, same colour.
                o.mist = BbtsHazeMix(wp, float3(0.0, 1.0, 0.0));
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.wp = wp;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // The noise first (implicit gradients, outside every branch).
                float2 nuv = i.uv.xy * i.uv.z;
                float n1 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, nuv + i.offsets.xy).r;
                float n2 = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, nuv * 2.0 + i.offsets.zw).g;
                float n = n1 * 0.65 + n2 * 0.35;

                // A soft ellipse (1 at the centre, 0 at the rim); the noise lifts the billows and bites into the rim.
                float2 p = i.uv.xy * 2.0 - 1.0;
                float body = saturate(1.0 - dot(p, p));
                float dens = saturate(body * (0.6 + 0.9 * n) - _Params.w * (1.0 - n) * 0.35);
                dens = dens * dens * (3.0 - 2.0 * dens);
                float alpha = dens * i.color.a;

                // Never in your face: nothing within _Params.x metres, fully there _Params.y metres further out.
                float camDist = distance(i.wp, _WorldSpaceCameraPos);
                alpha *= saturate((camDist - _Params.x) / max(_Params.y, 0.01));

                // Soft particles (#2403): melt into the ground behind instead of cutting a hard line (Medium+).
                if (_Sc_ScreenFx > 0.5 && _Sc_SoftParticles > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    alpha *= saturate((sceneEye - i.screenPos.w) / max(_Params.z, 0.01));
                }

                if (_Sc_AtmoDebug > 3.5 && _Sc_AtmoDebug < 4.5)
                {
                    return half4(1, 0, 1, 1); // capture diagnostics: every alpha particle (and card) solid magenta
                }

                float3 col = i.mist * i.color.rgb * (0.9 + 0.2 * n); // the thicker billows a touch brighter
                col = lerp(col, i.haze.rgb, i.haze.a);
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }

    Fallback Off
}
