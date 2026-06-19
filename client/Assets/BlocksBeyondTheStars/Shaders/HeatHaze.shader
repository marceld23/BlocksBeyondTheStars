// Heat-haze shimmer (HeatShimmer): a full-screen, camera-parented quad that re-displays the opaque scene with a
// faint animated UV warp — the air boiling over hot worlds. Distortion fades IN with distance (sampled from the
// depth texture) so only the far field shimmers, never the player's hands/feet. Amount is driven by the global
// _HeatAmp (HeatShimmer.cs sets it from the air temperature) and the quad is disabled when there is no heat, so
// this writes nothing on normal worlds. URP only (samples _CameraOpaqueTexture/_CameraDepthTexture, both of which
// the project's URP asset provides); the Built-in fallback is a no-op so the shader never breaks that path.
Shader "BlocksBeyondTheStars/HeatHaze"
{
    Properties { }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero // opaque replace: re-draw the (warped) scene colour

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _HeatAmp; // 0 = none .. 1 = full (global, set by HeatShimmer.cs)

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;

                // Distance fade from scene depth — near surfaces stay rock-steady, the far field boils.
                float raw = SampleSceneDepth(uv);
                float eye = LinearEyeDepth(raw, _ZBufferParams);
                float distFade = saturate((eye - 6.0) / 50.0);

                float amount = _HeatAmp * distFade;
                if (amount <= 0.0001)
                {
                    return half4(SampleSceneColor(uv), 1.0); // untouched
                }

                // Layered sines → a soft, rising, organic wobble (no texture needed).
                float t = _Time.y;
                float2 off;
                off.x = sin(uv.y * 38.0 + t * 3.0) * 0.6
                      + sin(uv.y * 71.0 - t * 2.1) * 0.3
                      + sin(uv.x * 53.0 + t * 1.7) * 0.2;
                off.y = sin(uv.x * 44.0 + t * 2.6) * 0.5
                      + sin(uv.y * 60.0 + t * 4.0) * 0.3;

                float2 uvd = uv + off * amount * 0.012;
                half3 col = SampleSceneColor(uvd);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (no-op fallback; the OnRenderImage PostFx path owns Built-in) ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Zero One // keep the framebuffer as-is

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; };

            v2f vert(appdata v) { v2f o; o.pos = UnityObjectToClipPos(v.vertex); return o; }
            fixed4 frag(v2f i) : SV_Target { return fixed4(0, 0, 0, 0); }
            ENDCG
        }
    }

    Fallback Off
}
