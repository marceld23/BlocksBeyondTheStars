// Minimal additive, textured, vertex-coloured particle shader for code-driven Unity ParticleSystems (ambient
// dust motes now; the wider VFX migration later). Soft glow: reads the per-particle COLOR + a soft dot _MainTex
// and blends additively (SrcAlpha One), so motes/embers/sparks add light and never darken the scene. ZWrite Off,
// Cull Off, Transparent queue. Dual-pipeline (URP + Built-in RP); the default ParticleSystem vertex streams
// (position / colour / uv) feed it directly.
Shader "BlocksBeyondTheStars/Particle"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        // VFX overhaul (#2152): an HDR multiplier so sparks, flashes and glow cards can cross the bloom threshold
        // (vertex colours top out at 1). Default 1 keeps every existing user unchanged.
        _Intensity ("HDR intensity", Range(0, 8)) = 1
    }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One // additive (alpha folded in) — soft glow

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // #2403 soft particles
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): a far glow fades into the haze (#2393)

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Sc_ScreenFx;      // 1 when the depth texture exists (Medium+): soft particles (#2403)
            float _Sc_SoftParticles; // the player's switch (0/1)

            // SRP Batcher (#573): per-MATERIAL properties in UnityPerMaterial (texture handles stay outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Intensity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float haze : TEXCOORD1; float4 screenPos : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.haze = BbtsHazeAmount(wp, 1.0); // per vertex: a particle is tiny, the haze barely varies across it
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float fade = 1.0 - i.haze; // additive: light that the haze swallows simply gets dimmer
                // #2403: soft particles — a glow fades out within half a metre of the surface behind it (Medium+).
                if (_Sc_ScreenFx > 0.5 && _Sc_SoftParticles > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    fade *= saturate((sceneEye - i.screenPos.w) / 0.5);
                }

                return half4(i.color.rgb * t.rgb * _Intensity * fade, i.color.a * t.a);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (fallback) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float _Intensity;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                return half4(i.color.rgb * t.rgb * _Intensity, i.color.a * t.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
