// Thermal-vision grade (ThermalVision): a full-screen, camera-parented quad that re-displays the opaque scene
// as a cold infrared image — deep navy shadows, teal midtones, pale highlights — with a faint sensor scanline and
// a slow sweep bar. The world going COLD is what makes the warm contact blobs (ThermalMarker) read at a glance;
// this pass deliberately never tints anything warm. Amount comes from the global _ThermalAmt (ThermalVision.cs
// eases it in/out), and the quad is disabled at zero so nothing is drawn in normal play. URP only (samples
// _CameraOpaqueTexture/_CameraDepthTexture, both provided by the project's URP asset); the Built-in fallback is a
// no-op so this can never break that path.
Shader "BlocksBeyondTheStars/Thermal"
{
    Properties { }

    // ---------------- URP ----------------
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-100" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero // opaque replace: re-draw the (graded) scene colour

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float _ThermalAmt; // 0 = off .. 1 = full (global, set by ThermalVision.cs)

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
                half3 src = SampleSceneColor(uv);

                float amt = saturate(_ThermalAmt);
                if (amt <= 0.0001)
                {
                    return half4(src, 1.0); // untouched
                }

                // Perceptual luminance drives the false-colour ramp: shadows go near-black navy, midtones teal,
                // highlights a pale cyan. Nothing in the ramp is warm — warmth is reserved for the contacts.
                float lum = dot(src, half3(0.299, 0.587, 0.114));
                float3 cold = lerp(float3(0.015, 0.035, 0.085), float3(0.10, 0.42, 0.50), pow(saturate(lum), 0.75));
                cold = lerp(cold, float3(0.55, 0.82, 0.92), saturate((lum - 0.70) / 0.30));

                // Distance falls off into the dark so the far field recedes instead of competing with contacts.
                float raw = SampleSceneDepth(uv);
                float eye = LinearEyeDepth(raw, _ZBufferParams);
                cold *= lerp(1.0, 0.55, saturate((eye - 15.0) / 120.0));

                // Sensor character: fine scanlines plus one slow sweep bar travelling up the frame.
                float scan = 1.0 - 0.07 * step(0.5, frac(uv.y * 320.0));
                float sweep = saturate(1.0 - abs(frac(_Time.y * 0.18) - uv.y) * 26.0);
                cold = cold * scan + sweep * float3(0.05, 0.16, 0.18);

                return half4(lerp(src, cold, amt), 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------- Built-in RP (no-op fallback; the project always runs URP) ----------------
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
