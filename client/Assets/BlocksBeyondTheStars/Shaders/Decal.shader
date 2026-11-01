// Voxel-face decals (#3058, "Details (High)"): scorch marks, dents and craters laid as small quads on the exposed cube
// faces around a hit (FxDecals builds the mesh, 5 mm proud of the faces). Two materials of this one shader:
//   * soot (_Mode 0, Blend DstColor Zero): multiplies what is already drawn — the lit, hazed block — toward the soot
//     tint, so the mark takes the surface's own lighting for free;
//   * embers (_Mode 1, Blend One One): an HDR orange glow from the atlas' ember channel that the bloom picks up while it
//     dies down (plasma, blaster, a wrecked speeder).
// The atlas (DecalPatterns, generated in code): R = soot, G = embers. The decal's quads reach past the mark (a whole cube
// face each), so the fragment masks everything outside its own [0,1]² square of decal space. The mark sinks into the
// haze with distance like the surface under it. Queue: after the opaque world (Geometry+400), before the sky dome
// (Geometry+499) and every transparent. WebGL 2 / WebGPU safe: no loops, one explicit-gradient sample.
Shader "BlocksBeyondTheStars/Decal"
{
    Properties
    {
        _MainTex ("Atlas (R soot, G embers)", 2D) = "black" {}
        _Tile ("Atlas tile: xy origin, z size, w inset", Vector) = (0, 0, 0.5, 0.002)
        _Tint ("Soot colour (linear)", Color) = (0.08, 0.07, 0.06, 1)
        _Strength ("Soot strength 0..1", Float) = 1
        _Ember ("Ember glow (HDR multiplier, 0 = off)", Float) = 0
        _EmberColor ("Ember colour (linear)", Color) = (1, 0.34, 0.07, 1)
        _Mode ("0 = soot, 1 = embers", Float) = 0
        [HideInInspector] _SrcBlend ("Source blend", Float) = 2
        [HideInInspector] _DstBlend ("Destination blend", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+400" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        ZTest LEqual
        Cull Back
        Offset -1, -1

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AtmosphereCommon.hlsl" // the shared haze (#2408): a far mark fades with the surface it lies on

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            // SRP Batcher (#573): per-material properties in UnityPerMaterial (the texture handle stays outside).
            CBUFFER_START(UnityPerMaterial)
                float4 _Tile;
                float4 _Tint;
                float _Strength;
                float _Ember;
                float4 _EmberColor;
                float _Mode;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 fade : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                o.uv = v.uv;
                // x = the depth fade baked per vertex (faces far off the mark's plane fade out), y = 1 − haze.
                o.fade = float2(v.color.a, 1.0 - BbtsHazeAmount(wp, 1.0));
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 inside2 = step(0.0, i.uv) * step(i.uv, 1.0);
                float inside = inside2.x * inside2.y;
                // Gradients of the unclamped coordinate: the clamp below would otherwise pick a wrong mip along the edge.
                float2 dx = ddx(i.uv) * _Tile.z;
                float2 dy = ddy(i.uv) * _Tile.z;
                float2 tileUv = _Tile.xy + _Tile.w + saturate(i.uv) * (_Tile.z - 2.0 * _Tile.w);
                half4 t = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, tileUv, dx, dy);
                float k = inside * saturate(i.fade.x) * saturate(i.fade.y);
                if (_Mode > 0.5)
                {
                    return half4(_EmberColor.rgb * (t.g * k * _Ember), 0.0);
                }

                float soot = saturate(t.r * k * _Strength);
                return half4(lerp(float3(1.0, 1.0, 1.0), _Tint.rgb, soot), 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
