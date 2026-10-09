// Alpha-blended, textured, vertex-coloured particle shader for code-driven ParticleSystems that need opaque-ish
// bits rather than additive glow: mining debris/dust, weather rain/snow/ash, smoke. Sibling of
// BlocksBeyondTheStars/Particle (which is additive). SrcAlpha/OneMinusSrcAlpha, ZWrite Off, Cull Off, Transparent
// queue. Dual-pipeline (URP + Built-in RP); the default ParticleSystem vertex streams feed it directly.
Shader "BlocksBeyondTheStars/ParticleAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl" // #2403 soft particles
            #include "AtmosphereCommon.hlsl" // atmosphere package (#2408): far dust and smoke sink into the haze (#2393)

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            float _Sc_ScreenFx;   // 1 when the depth texture exists (Medium+): soft particles (#2403)
            float _Sc_SoftParticles; // the player's switch (0/1)

            // SRP Batcher (#573): per-MATERIAL properties in UnityPerMaterial (texture handles stay outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float4 haze : TEXCOORD1; float4 screenPos : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                o.haze = float4(BbtsHazeColor(wp), BbtsHazeAmount(wp, 1.0)); // per vertex: tiny particle, flat haze
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float3 col = lerp(i.color.rgb * t.rgb, i.haze.rgb, i.haze.a); // alpha-blended: tints toward the haze like a surface
                float alpha = i.color.a * t.a;
                // #2403: soft particles — fade out within half a metre of the surface behind, so dust, mist and smoke never
                // cut a hard line into the ground. Needs the depth texture (Medium+); a no-op on Potato/Low.
                if (_Sc_ScreenFx > 0.5 && _Sc_SoftParticles > 0.5)
                {
                    float2 suv = i.screenPos.xy / i.screenPos.w;
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float fragEye = i.screenPos.w;
                    alpha *= saturate((sceneEye - fragEye) / 0.5);
                }

                if (_Sc_AtmoDebug > 3.5 && _Sc_AtmoDebug < 4.5)
                {
                    return half4(1, 0, 1, 1); // capture diagnostics: every alpha particle solid magenta
                }

                return half4(col, alpha);
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
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;

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
                return fixed4(i.color.rgb * t.rgb, i.color.a * t.a);
            }
            ENDCG
        }
    }

    Fallback Off
}
