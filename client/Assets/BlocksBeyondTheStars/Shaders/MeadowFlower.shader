// The meadow flowers of the High preset (#3056): small procedural flowers on about one grass top in nine near the camera,
// GPU-instanced per chunk by GrassShells.cs (growth, range and wind in MeadowFlowerCommon.hlsl). The sprite is a painted
// data texture (FlowerSprite): the petals take the flower's colour from the world's floral palette, the centre stays warm
// yellow, stem and leaves take the world's grass-tuft green turned by the season like the plants (FloraAccent.hlsl). Lit
// like the carpet (MeadowLight.hlsl: sun shadow map, cloud shade, skylight, haze; the sun glows through the petals). Never
// casts shadows; a DepthOnly pass keeps it in the depth prepass. URP only.
Shader "BlocksBeyondTheStars/MeadowFlower"
{
    Properties
    {
        _FlowerTex ("Flower sprite (data)", 2D) = "black" {}
        _FlowerDensity ("Season's share in bloom", Range(0, 1)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        Pass
        {
            Name "MeadowFlowerForward"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex BbtsFlowerVert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "AtmosphereCommon.hlsl"
            #include "FloraAccent.hlsl" // BbtsSeasonTint: the stem turns with the plants (#3042)
            #include "MeadowFlowerCommon.hlsl"
            #include "MeadowLight.hlsl"

            float4 _Sc_GrassTint; // the world's grass-tuft colour (a > 0.5 = set); GameBootstrap.RebuildFloraTints

            half4 frag(FlowerVaryings i) : SV_Target
            {
                float4 t = SAMPLE_TEXTURE2D(_FlowerTex, sampler_FlowerTex, i.uv);
                clip(t.a - 0.5);

                float3 stem = BbtsSeasonTint(_Sc_GrassTint.a > 0.5 ? _Sc_GrassTint.rgb * 0.5 : float3(0.10, 0.26, 0.06));
                float3 centre = float3(0.60, 0.40, 0.05);
                float3 albedo = (t.b > 0.5 ? stem : lerp(centre, i.color.rgb * 0.8, t.g)) * t.r;

                float gloss = 0.0;
                BbtsWeatherSurface(albedo, gloss, i.wp, float3(0.0, 1.0, 0.0), i.info.x);
                return half4(BbtsMeadowShade(albedo, i.wp, i.info.x, i.info.yzw, t.b > 0.5 ? 0.3 : 1.0), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex BbtsFlowerVert
            #pragma fragment depthFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl"
            #include "MeadowFlowerCommon.hlsl"

            half4 depthFrag(FlowerVaryings i) : SV_Target
            {
                clip(SAMPLE_TEXTURE2D(_FlowerTex, sampler_FlowerTex, i.uv).a - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
