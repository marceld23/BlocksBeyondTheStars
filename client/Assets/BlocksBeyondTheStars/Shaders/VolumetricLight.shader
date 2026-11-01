// Volumetric sun light (#3060, the "Details (High)" epic #3066): sunbeams in the air — through a forest canopy, out of a
// cave mouth, through a ship window or an open base door. A hand-written fragment raymarch at half resolution: no
// compute, nothing WebGL 2 lacks (ADR 0015 §3) — it still compiles for every target because it is always-included
// (GraphicsSettings m_AlwaysIncludedShaders), but only the High preset of a desktop client draws it
// (VolumetricSunLight + VolumetricLightPass, a render-graph pass on the gameplay camera and the capture clones).
//   Pass 0  march     — per half-res pixel, 24 FIXED steps from the camera to min(scene, the camera's shadow distance),
//                        spaced quadratically (half the steps in the first quarter of the way, where rooms, caves and
//                        canopies are) and jittered by interleaved-gradient noise. Per step: the sun's shadow map × the
//                        cloud shade × the density of the air — the haze's OWN terms (AtmosphereCommon: the distance
//                        veil's max haze spread over its far distance, plus BbtsMistDensity's height fog), plus a dust
//                        term near the camera (aboard, at home, under a roof or a canopy), because the haze is off
//                        indoors. Henyey-Greenstein phase toward the sun. Out: rgb = the in-scattered light, a = the
//                        scene's linear eye depth (for the depth-aware blur and upsample).
//   Pass 1/2 blur     — a depth-aware 7-tap gaussian, horizontal then vertical, at half resolution.
//   Pass 3  composite — a depth-aware bilinear upsample, ADDED onto the camera colour (Blend One One). It only ever adds
//                        light, so it can never darken the picture (the retired full-screen fog pass darkened the frame
//                        through its colour copy); extinction stays with the existing haze and only shapes the beams.
// Only the sun casts beams: torches, lanterns, lava and the headlamp are the baked flood-fill block light (ADR 0003), not
// Unity lights with a shadow map, so they get none (a possible later extension).
// WebGL 2 / WebGPU rules: fixed loop counts; inside the loop only explicit-LOD texture reads (SAMPLE_TEXTURE2D_LOD,
// SampleCmpLevelZero) — no implicit derivatives in non-uniform control flow.
//
// Globals (VolumetricSunLight / VolumetricLightPass; the AtmosphereCommon ones come from Sky.cs):
//   _Sc_VolMarch  x = march distance (m: the camera's shadow distance), y = haze density (optical depth across the haze's
//                 far distance at full max haze), z = height-fog share, w = extinction share (how much the density also
//                 dims the light further along the same ray)
//   _Sc_VolDust   x = dust density per metre near the camera, y = metres the dust reaches (it fades over the last 40 %),
//                 z = Henyey-Greenstein g, w = the HG share of the phase (the rest scatters evenly)
//   _Sc_VolLight  x = intensity (every fade folded in), y = share of the haze's hue in the beam colour,
//                 z = cap on the added light per pixel (keeps the bloom sane)
//   _Sc_VolTexel  xy = 1 / half-res size, zw = half-res size
//   _Sc_Light     the sun's light (Sky.cs: star colour × day brightness × weather dim, linear; a > 0.5 = set)
Shader "BlocksBeyondTheStars/VolumetricLight"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        HLSLINCLUDE
        // URP Core first: it defines the texture macros (TEXTURE2D_X etc.) that Blit.hlsl expects.
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        float4 _Sc_VolMarch;
        float4 _Sc_VolDust;
        float4 _Sc_VolLight;
        float4 _Sc_VolTexel;

        // How much a neighbour counts by how close its depth lies to ours (relative, so far pixels tolerate more). Never
        // quite 0, so a lone pixel falls back to the plain filter instead of dividing by nothing.
        float VolDepthWeight(float d0, float d)
        {
            return max(1e-3, 1.0 - abs(d - d0) / (0.08 * d0 + 0.15));
        }

        void VolBlurTap(float2 uv, float d0, float weight, inout float3 sum, inout float wsum)
        {
            float4 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            float w = weight * VolDepthWeight(d0, s.a);
            sum += s.rgb * w;
            wsum += w;
        }

        // A depth-aware 7-tap gaussian along `stepUv` (one half-res texel): taps across a depth edge drop out, so a beam
        // behind a tree trunk does not smear over the trunk. Keeps the centre's depth in alpha for the next pass.
        float4 VolBlur(float2 uv, float2 stepUv)
        {
            float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            float3 sum = c.rgb * 0.266;
            float wsum = 0.266;
            VolBlurTap(uv + stepUv, c.a, 0.213, sum, wsum);
            VolBlurTap(uv - stepUv, c.a, 0.213, sum, wsum);
            VolBlurTap(uv + stepUv * 2.0, c.a, 0.109, sum, wsum);
            VolBlurTap(uv - stepUv * 2.0, c.a, 0.109, sum, wsum);
            VolBlurTap(uv + stepUv * 3.0, c.a, 0.036, sum, wsum);
            VolBlurTap(uv - stepUv * 3.0, c.a, 0.036, sum, wsum);
            return float4(sum / wsum, c.a);
        }
        ENDHLSL

        Pass
        {
            Name "VolumetricMarch"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragMarch
            // The sun's shadow map as the block shaders receive it. No soft-shadow keyword: one hardware-filtered tap per
            // step is plenty under the blur (24 tent filters per pixel would not be).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "AtmosphereCommon.hlsl"

            #define VOL_STEPS 24

            float4 _Sc_Light;

            // Interleaved gradient noise (Jimenez 2014): a stable per-pixel offset of the steps, so 24 steps read as a
            // fine grain the blur removes instead of as bands.
            float VolJitter(float2 pixel)
            {
                return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
            }

            // How much sun reaches world point p: the RAW shadow map (full contrast — the receivers' shadow strength is a
            // surface look; a beam in the air ends where its occluder is), the drifting cloud shade, and the shadow
            // distance's own fade, so the march never shows the shadow map's edge. Without a shadow map there is no beam.
            float VolSun(float3 p)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                float4 sc = TransformWorldToShadowCoord(p);
                float att = SAMPLE_TEXTURE2D_SHADOW(_MainLightShadowmapTexture, sampler_LinearClampCompare, sc.xyz);
                bool beyond = sc.z <= 0.0 || sc.z >= 1.0; // URP's BEYOND_SHADOW_FAR: outside the light's volume = lit
                float lit = beyond ? 1.0 : att;
                return lit * (1.0 - GetMainLightShadowFade(p)) * BbtsCloudShade(p);
            #else
                return 0.0;
            #endif
            }

            // The density of the air at p, `t` metres from the camera (per metre). The haze's own terms, scaled like the
            // haze by its max haze (_Sc_Fog.z: 0 indoors and with the haze switched off): the distance veil spread over its
            // far distance (so fog weather, a sandstorm and thick air all thicken the beams) and the height fog lying in
            // the low ground (the camera's exposure: nothing mists up underground). Plus the dust near the camera, fading
            // out toward its reach so it never veils the view out of a door.
            float VolDensity(float3 p, float t)
            {
                float haze = 0.0;
                if (_Sc_Fog.w > 0.5)
                {
                    float veil = _Sc_VolMarch.y / max(_Sc_Fog.y, 32.0);
                    float mist = BbtsMistDensity(p.y) * 0.035 * _Sc_FogHeight.z * _Sc_FogHeight.w * _Sc_VolMarch.z;
                    haze = (veil + mist) * _Sc_Fog.z;
                }

                float reach = max(_Sc_VolDust.y, 1.0);
                float dust = _Sc_VolDust.x * saturate((reach - t) / (reach * 0.4));
                return haze + dust;
            }

            // Henyey-Greenstein toward the sun, scaled by 4π (isotropic scattering = 1), mixed with an even share so a
            // beam seen side-on still shows.
            float VolPhase(float cosTheta)
            {
                float g = _Sc_VolDust.z;
                float denom = max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4);
                float hg = (1.0 - g * g) / (denom * sqrt(denom));
                return lerp(1.0, hg, _Sc_VolDust.w);
            }

            // The beams' colour: the sun's light, tinted a little toward the haze's hue in this direction (the sky warmed
            // toward the sun), so they follow each planet's air, the weather and the dawn. No haze (the cabin in space):
            // the plain sun.
            float3 VolBeamColor(float3 dir)
            {
                float3 sun = _Sc_Light.a > 0.5 ? _Sc_Light.rgb : float3(1.0, 1.0, 1.0);
                float3 haze = BbtsHazeColor(_WorldSpaceCameraPos + dir);
                float3 hue = haze / max(max(haze.r, max(haze.g, haze.b)), 1e-3);
                float share = _Sc_VolLight.y * step(0.5, _Sc_Fog.w);
                return sun * lerp(float3(1.0, 1.0, 1.0), hue, share);
            }

            float4 FragMarch(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, uv, 0).r;
                float eye = LinearEyeDepth(raw, _ZBufferParams);
            #if UNITY_REVERSED_Z
                float deviceDepth = raw;
            #else
                float deviceDepth = raw * 2.0 - 1.0;
            #endif
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP) - cam;
                float sceneDist = max(length(ray), 1e-3);
                float3 dir = ray / sceneDist;
                // Without haze (aboard, where it is off indoors) only the dust carries light: spend every step inside its
                // reach instead of out to the shadow distance.
                bool hazeAir = _Sc_Fog.w > 0.5 && _Sc_Fog.z > 0.001;
                float marchDist = min(sceneDist, hazeAir ? _Sc_VolMarch.x : min(_Sc_VolMarch.x, max(_Sc_VolDust.y, 1.0)));
                float jitter = VolJitter(input.positionCS.xy);

                float scatter = 0.0;
                float trans = 1.0;
                UNITY_LOOP
                for (int i = 0; i < VOL_STEPS; i++)
                {
                    float u0 = (float)i / VOL_STEPS;
                    float u1 = (float)(i + 1) / VOL_STEPS;
                    float t0 = marchDist * u0 * u0;
                    float t1 = marchDist * u1 * u1;
                    float ds = t1 - t0;
                    float t = lerp(t0, t1, jitter);
                    float3 p = cam + dir * t;
                    float sigma = VolDensity(p, t);
                    scatter += VolSun(p) * sigma * ds * trans;
                    trans *= exp(-sigma * ds * _Sc_VolMarch.w);
                }

                float3 sunDir = _Sc_FogSunDir.xyz * rsqrt(max(dot(_Sc_FogSunDir.xyz, _Sc_FogSunDir.xyz), 1e-6));
                float phase = VolPhase(dot(dir, sunDir));
                float3 light = VolBeamColor(dir) * (scatter * phase * _Sc_VolLight.x);
                return float4(clamp(light, 0.0, _Sc_VolLight.z), eye);
            }
            ENDHLSL
        }

        Pass
        {
            Name "VolumetricBlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH

            float4 FragBlurH(Varyings input) : SV_Target
            {
                return VolBlur(input.texcoord, float2(_Sc_VolTexel.x, 0.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "VolumetricBlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV

            float4 FragBlurV(Varyings input) : SV_Target
            {
                return VolBlur(input.texcoord, float2(0.0, _Sc_VolTexel.y));
            }
            ENDHLSL
        }

        Pass
        {
            Name "VolumetricComposite"
            // Additive only: the camera colour is never replaced, so nothing can come out darker than without the beams.
            Blend One One
            ColorMask RGB
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            // Depth-aware bilinear upsample of the half-res light: of the four half-res texels around this pixel, the
            // ones whose depth differs from the full-res depth count less, so the beams keep the silhouettes' edges.
            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float d0 = LinearEyeDepth(LoadSceneDepth(uint2(input.positionCS.xy)), _ZBufferParams);
                float2 hp = uv * _Sc_VolTexel.zw - 0.5;
                float2 f = frac(hp);
                float2 base = (floor(hp) + 0.5) * _Sc_VolTexel.xy;
                float4 s00 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base, 0);
                float4 s10 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + float2(_Sc_VolTexel.x, 0.0), 0);
                float4 s01 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + float2(0.0, _Sc_VolTexel.y), 0);
                float4 s11 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + _Sc_VolTexel.xy, 0);
                float4 w = float4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y)
                         * float4(VolDepthWeight(d0, s00.a), VolDepthWeight(d0, s10.a), VolDepthWeight(d0, s01.a), VolDepthWeight(d0, s11.a));
                float3 light = (s00.rgb * w.x + s10.rgb * w.y + s01.rgb * w.z + s11.rgb * w.w) / max(w.x + w.y + w.z + w.w, 1e-5);
                return half4(max(light, 0.0), 0.0);
            }
            ENDHLSL
        }
    }

    // No Built-in RP SubShader on purpose: the effect is a URP render-graph pass (VolumetricLightPass) and does not exist
    // under the Built-in pipeline, so VolumetricSunLight switches itself off there (Shader.isSupported is false).
    Fallback Off
}
