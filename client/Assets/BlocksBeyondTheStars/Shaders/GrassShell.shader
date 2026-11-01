// The grass carpet of the High preset (#3056): stacked shells over every open grass, alien-grass and mycelium top near the
// camera, GPU-instanced per chunk by GrassShells.cs (strand cut, layer lift and wind in GrassShellCommon.hlsl). A strand
// takes its colour from the texel of the block's OWN atlas tile it grows out of — sampled at an explicit LOD from the shared
// block atlas, so it matches the ground under it on every world and with every texture pack — darker at the roots, lighter
// at the tips. The light is the block shader's (MeadowLight.hlsl): sun shadow map, cloud shade, skylight ambient, haze; rain
// darkens and snow whitens it like the ground (BbtsWeatherSurface). The carpet never casts shadows; a DepthOnly pass keeps it
// in the depth prepass with the same strands. URP only (the Built-in pipeline draws no carpet). Desktop High only — but every
// feature is WebGL 2-safe (no compute, no geometry stage, no derivatives in loops).
Shader "BlocksBeyondTheStars/GrassShell"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
    }

    SubShader
    {
        // AlphaTest: drawn after the solid terrain, so a carpet behind a hill fails the depth test before its strands are cut.
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "GrassShellForward"
            Tags { "LightMode" = "UniversalForward" }
            // #3078: the strand edge as MSAA coverage (BbtsShellCover keeps the hard cut where there is no MSAA).
            AlphaToMask On
            HLSLPROGRAM
            #pragma vertex BbtsShellVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            // Receive the sun's real-time shadow map (the block shader's keywords, nothing new).
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AtmosphereCommon.hlsl" // the shared haze, cloud shade, wind and weather traces
            #include "GrassShellCommon.hlsl"
            #include "MeadowLight.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            half4 frag(ShellVaryings i) : SV_Target
            {
                float coverage;
                float4 s = BbtsShellCover(i, coverage);
                float2 st = BbtsShellTileSt(s.zw, i.info.y);
                float3 albedo = SAMPLE_TEXTURE2D_LOD(_MainTex, sampler_MainTex, i.tile.xy + i.tile.zw * st, 0).rgb;

                // Darker at the roots (the carpet shades itself), lighter at each tip — easing back to the ground's own
                // look where the carpet lies flat at the end of its range, so its edge never shows.
                float shade = lerp(0.52, 1.0, i.info.w) + 0.22 * s.y * s.y;
                albedo *= lerp(1.0, shade, i.fade);

                float gloss = 0.0;
                BbtsWeatherSurface(albedo, gloss, i.wp, float3(0.0, 1.0, 0.0), i.info.x); // wet darkens, snow whitens (#2398)
                return half4(BbtsMeadowShade(albedo, i.wp, i.info.x, i.bl, s.y), coverage);
            }
            ENDHLSL
        }

        // The depth prepass (#3063's rule for everything on the ground): the same layers, the same strands.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex BbtsShellVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl"
            #include "GrassShellCommon.hlsl"

            half4 depthFrag(ShellVaryings i) : SV_Target
            {
                BbtsShellClip(i);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
