// Soft distance (#3462): on a world with air, the far third of the distance haze gets a very slight real blur — the depth
// planes separate (near crisp, the mountain range "far away") and the seam where the full-detail chunks give way to the
// coarse far terrain softens. Barely noticeable on purpose: the blend tops out at 0.4–0.5 (DistanceSoftLook in
// Client.Core holds the rules). A render-graph pass on the gameplay camera and the capture clones (DistanceSoftView +
// DistanceSoftPass), Medium+ on a desktop client only; it compiles for every target because it is always-included.
//   Pass 0  downsample — the camera colour to half resolution, depth-aware over the 2×2 pixels (only those at the nearest
//                        surface's distance count, so sky never bleeds into a ridge); alpha = the scene's
//                        distance from the camera in metres (the haze's own measure), DS_SKY_DEPTH for the open sky.
//   Pass 1/2 blur      — a depth-aware 7-tap gaussian, horizontal then vertical: taps across a depth edge drop out, and
//                        the open sky never bleeds into a ridgeline (nor a ridgeline into the sky).
//   Pass 3  composite  — a depth-aware upsample of the blurred image, blended over the camera colour with
//                        weight = _Sc_DistSoft.y × smoothstep(band start _Sc_DistSoft.z, haze end _Sc_Fog.y, distance).
//                        It never touches the open sky (far-plane pixels: weight 0 — the sun, the stars, the aurora, the
//                        rings and the clouds against the sky stay sharp) nor the moons and planets in the surface sky
//                        (SkyBodyPhase draws them opaque at 60–460 m and marks them with stencil bit 64 — as do the particles and the
//                        world-space labels, which sit in front of the far hills their depth reads; the stencil test
//                        below skips those pixels). Where the upsample finds no neighbour at a similar distance (a thin
//                        silhouette against the sky) it leaves the pixel sharp.
// WebGL 2 / WebGPU rules: only explicit-LOD texture reads (the pass never runs in the browser, but it must compile there).
//
// Globals (DistanceSoftPass; the AtmosphereCommon ones come from Sky.Mist.cs):
//   _Sc_DistSoftTexel xy = 1 / half-res size, zw = half-res size
//   _Sc_DistSoft      y = the largest blend, z = the band start (metres) — see AtmosphereCommon.hlsl
//   _Sc_Fog           y = the haze end (metres): the blend is full there
Shader "BlocksBeyondTheStars/DistanceSoft"
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
        #include "AtmosphereCommon.hlsl" // _Sc_Fog, _Sc_DistSoft

        // The open sky's "distance" in the half-res alpha (a half float holds up to 65504).
        #define DS_SKY_DEPTH 60000.0

        float4 _Sc_DistSoftTexel;

        // True where the depth texture holds the far plane: nothing was drawn there but the sky.
        bool DsIsSky(float raw)
        {
            return Linear01Depth(raw, _ZBufferParams) > 0.9995;
        }

        // The scene's distance from the camera (metres) at `uv` for the raw depth `raw` — the haze's own measure, so the
        // band starts where Sky put it.
        float DsDistance(float2 uv, float raw)
        {
        #if UNITY_REVERSED_Z
            float deviceDepth = raw;
        #else
            float deviceDepth = raw * 2.0 - 1.0;
        #endif
            float3 wp = ComputeWorldSpacePosition(uv, deviceDepth, UNITY_MATRIX_I_VP);
            return distance(wp, _WorldSpaceCameraPos);
        }

        // How much a neighbour counts by how close its distance lies to ours (relative, so far pixels tolerate more); 0 for
        // the open sky.
        float DsDepthWeight(float d0, float d)
        {
            return d >= DS_SKY_DEPTH * 0.5 ? 0.0 : saturate(1.0 - abs(d - d0) / (0.15 * d0 + 1.0));
        }

        void DsBlurTap(float2 uv, float d0, float weight, inout float3 sum, inout float wsum)
        {
            float4 s = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            float w = weight * DsDepthWeight(d0, s.a);
            sum += s.rgb * w;
            wsum += w;
        }

        // A depth-aware 7-tap gaussian along `stepUv` (one half-res texel). Keeps the centre's distance in alpha for the
        // next pass; the open sky passes through untouched.
        float4 DsBlur(float2 uv, float2 stepUv)
        {
            float4 c = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            if (c.a >= DS_SKY_DEPTH * 0.5)
            {
                return c;
            }

            float3 sum = c.rgb * 0.266;
            float wsum = 0.266;
            DsBlurTap(uv + stepUv, c.a, 0.213, sum, wsum);
            DsBlurTap(uv - stepUv, c.a, 0.213, sum, wsum);
            DsBlurTap(uv + stepUv * 2.0, c.a, 0.109, sum, wsum);
            DsBlurTap(uv - stepUv * 2.0, c.a, 0.109, sum, wsum);
            DsBlurTap(uv + stepUv * 3.0, c.a, 0.036, sum, wsum);
            DsBlurTap(uv - stepUv * 3.0, c.a, 0.036, sum, wsum);
            return float4(sum / wsum, c.a);
        }
        ENDHLSL

        Pass
        {
            Name "DistanceSoftDownsample"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDownsample

            // The 2×2 full-res pixels this half-res texel covers, depth-aware: the texel's distance is the nearest surface among
            // them, and only the pixels at about that distance give their colour — so the open sky never bleeds into a
            // ridgeline's texel (nor a ridge into the sky's). An all-sky texel keeps the plain average.
            float4 FragDownsample(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float2 h = _CameraDepthTexture_TexelSize.xy * 0.5;
                float2 offs[4] = { float2(-h.x, -h.y), float2(h.x, -h.y), float2(-h.x, h.y), float2(h.x, h.y) };
                float3 col[4];
                float dist[4];
                float nearest = DS_SKY_DEPTH;
                [unroll]
                for (int k = 0; k < 4; k++)
                {
                    float2 tuv = uv + offs[k];
                    col[k] = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, tuv, 0).rgb;
                    float raw = SAMPLE_TEXTURE2D_X_LOD(_CameraDepthTexture, sampler_PointClamp, tuv, 0).r;
                    dist[k] = DsIsSky(raw) ? DS_SKY_DEPTH : min(DsDistance(tuv, raw), DS_SKY_DEPTH * 0.4);
                    nearest = min(nearest, dist[k]);
                }

                bool allSky = nearest >= DS_SKY_DEPTH * 0.5;
                float3 sum = float3(0.0, 0.0, 0.0);
                float wsum = 0.0;
                [unroll]
                for (int j = 0; j < 4; j++)
                {
                    float w = allSky ? 1.0 : DsDepthWeight(nearest, dist[j]);
                    sum += col[j] * w;
                    wsum += w;
                }

                return float4(sum / max(wsum, 1e-4), nearest);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceSoftBlurH"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurH

            float4 FragBlurH(Varyings input) : SV_Target
            {
                return DsBlur(input.texcoord, float2(_Sc_DistSoftTexel.x, 0.0));
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceSoftBlurV"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlurV

            float4 FragBlurV(Varyings input) : SV_Target
            {
                return DsBlur(input.texcoord, float2(0.0, _Sc_DistSoftTexel.y));
            }
            ENDHLSL
        }

        Pass
        {
            Name "DistanceSoftComposite"
            // Blends the blurred far field over the camera colour by the band weight; alpha 0 leaves a pixel as it was.
            Blend SrcAlpha OneMinusSrcAlpha
            ColorMask RGB
            // The moons and planets of the surface sky (SkyBodyPhase writes bit 64) always stay sharp.
            Stencil
            {
                Ref 64
                ReadMask 64
                Comp NotEqual
            }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite

            half4 FragComposite(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float raw = LoadSceneDepth(uint2(input.positionCS.xy));
                if (DsIsSky(raw))
                {
                    return half4(0.0, 0.0, 0.0, 0.0); // the open sky: untouched
                }

                float d0 = DsDistance(uv, raw);
                float bandEnd = max(_Sc_Fog.y, _Sc_DistSoft.z + 1.0);
                float weight = _Sc_DistSoft.y * smoothstep(_Sc_DistSoft.z, bandEnd, d0);
                if (weight < 0.002)
                {
                    return half4(0.0, 0.0, 0.0, 0.0);
                }

                // Depth-aware bilinear upsample of the blurred half-res image (as the volumetric light's composite does).
                float2 hp = uv * _Sc_DistSoftTexel.zw - 0.5;
                float2 f = frac(hp);
                float2 base = (floor(hp) + 0.5) * _Sc_DistSoftTexel.xy;
                float4 s00 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base, 0);
                float4 s10 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + float2(_Sc_DistSoftTexel.x, 0.0), 0);
                float4 s01 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + float2(0.0, _Sc_DistSoftTexel.y), 0);
                float4 s11 = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, base + _Sc_DistSoftTexel.xy, 0);
                float4 w = float4((1.0 - f.x) * (1.0 - f.y), f.x * (1.0 - f.y), (1.0 - f.x) * f.y, f.x * f.y)
                         * float4(DsDepthWeight(d0, s00.a), DsDepthWeight(d0, s10.a), DsDepthWeight(d0, s01.a), DsDepthWeight(d0, s11.a));
                float wsum = w.x + w.y + w.z + w.w;
                if (wsum < 0.05)
                {
                    return half4(0.0, 0.0, 0.0, 0.0); // no neighbour at this distance (a thin silhouette): keep it sharp
                }

                float3 blurred = (s00.rgb * w.x + s10.rgb * w.y + s01.rgb * w.z + s11.rgb * w.w) / wsum;
                return half4(max(blurred, 0.0), weight);
            }
            ENDHLSL
        }
    }

    // No Built-in RP SubShader on purpose: the effect is a URP render-graph pass (DistanceSoftPass) and does not exist under
    // the Built-in pipeline, so DistanceSoftView switches itself off there (Shader.isSupported is false).
    Fallback Off
}
