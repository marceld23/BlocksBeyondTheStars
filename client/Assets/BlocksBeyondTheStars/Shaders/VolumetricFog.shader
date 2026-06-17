// Full-screen volumetric-style fog + sun in-scatter, run by Unity's built-in FullScreenPassRendererFeature
// (auto-wired in BuildScript.EnsureRendererFeatures). NOT a true raymarch — a single-pass analytic fog that
// reconstructs each pixel's world position from the camera depth and blends a sky-tinted haze that thickens
// with distance and (optionally) toward the ground, brightening toward the sun so dawn/dusk glow scatters
// through the air. Sky pixels (no geometry) are left untouched. Driven by the same Sky.cs globals; gated by the
// _VolFog global (0 → passthrough) so the player toggle / preset can switch it off with no feature juggling.
//
// Tuning lives in the material properties (set each build in BuildScript). True shadowed light-shafts are a
// later refinement (needs marching the shadow map).
Shader "BlocksBeyondTheStars/VolumetricFog"
{
    Properties
    {
        _FogDensity ("Fog Density", Float) = 0.006
        _FogHeightFalloff ("Height Falloff", Float) = 0.0
        _FogHeight ("Height Reference", Float) = 0.0
        _SunScatter ("Sun Scatter", Range(0,1)) = 0.7
        _MaxFog ("Max Fog", Range(0,1)) = 0.75
        _FogTint ("Fog Tint", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "VolumetricFog"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _Sc_SunDir; // world-space direction TO the sun
            float4 _Sc_Light;  // sun colour × day brightness (a>0.5 = set)
            float4 _Sc_Sky;    // sky colour (the haze base tint)
            float _VolFog;        // global on/off (0 = passthrough), from the player setting/preset
            float _VolFogDensity; // global per-world+weather density (Sky.cs derives it from the view distance;
                                  // 0 in space/airless → no fog). Falls back to the material _FogDensity if unset.

            float _FogDensity;
            float _FogHeightFalloff;
            float _FogHeight;
            float _SunScatter;
            float _MaxFog;
            float4 _FogTint;

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                half3 sceneCol = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;

                // Off → straight passthrough (player toggle / Potato-Low).
                if (_VolFog < 0.5)
                {
                    return half4(sceneCol, 1.0);
                }

                float depth = SampleSceneDepth(uv);
                // Sky / no-geometry pixels: leave them alone (don't flatten the sky into fog colour).
                #if UNITY_REVERSED_Z
                    if (depth < 1e-6) return half4(sceneCol, 1.0);
                #else
                    if (depth > 0.999999) return half4(sceneCol, 1.0);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 camPos = _WorldSpaceCameraPos;
                float dist = distance(worldPos, camPos);

                // Per-world+weather density from Sky.cs (derived from the view distance); falls back to the
                // material default when the global isn't driven. 0 → no fog (space / airless / fog disabled).
                // Sky.cs always drives this per-world (and zeroes it indoors / in space). No material fallback —
                // a 0 here MUST mean "no fog", otherwise the indoor/space gating leaks residual fog + darkening.
                float density = _VolFogDensity;
                float fog = 1.0 - exp(-dist * max(0.0, density));
                if (_FogHeightFalloff > 0.0)
                {
                    fog *= saturate(exp(-(worldPos.y - _FogHeight) * _FogHeightFalloff));
                }
                fog = saturate(fog) * _MaxFog;

                float3 viewDir = normalize(worldPos - camPos);
                float3 sunDir = normalize(_Sc_SunDir.xyz);
                float sun = pow(saturate(dot(viewDir, sunDir)), 8.0);
                float3 sunCol = (_Sc_Light.a < 0.5) ? float3(1, 1, 1) : _Sc_Light.rgb;

                // Aerial-perspective haze colour = the sky tint LIFTED by the in-scattered sunlight, so by day the
                // haze is bright (it lightens the distance, like real atmosphere) and only at night/dusk is it dark.
                // Without the lift, the (linear) sky colour is dimmer than the sunlit terrain → the fog darkened
                // the daytime scene.
                float3 fogCol = _Sc_Sky.rgb * _FogTint.rgb + sunCol * 0.4;
                fogCol = lerp(fogCol, sunCol, sun * _SunScatter); // extra brightening straight toward the sun

                half3 outCol = lerp(sceneCol, fogCol, fog);
                return half4(outCol, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
