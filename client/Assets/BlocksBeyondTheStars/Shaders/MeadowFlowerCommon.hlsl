// Blocks Beyond the Stars — the meadow flowers of the High preset (#3056), shared by the colour and the depth pass of
// MeadowFlower.shader so both draw exactly the same flower.
//
// GrassShells.cs draws one instance per flower (about one grass top in nine): a crossed stem quad pair and a tilted blossom
// disc, in object space one unit tall and scaled to 0.15 … 0.25 m by the instance matrix; the sprite is FlowerSprite's
// painted data texture (R shade, G petal, B stem, A cut-out). A flower grows out of the ground while the season's share
// of flowers in bloom (_FlowerDensity, MeadowField.FlowerDensity) passes its rank, and sinks back into it at the end of its
// range or under snow, so nothing ever pops. Per instance (MaterialPropertyBlock arrays):
//   _FlowerColor rgb = petal colour (the species' world colour, linear), a = bloom rank 0..1
//   _FlowerInfo  x = skylight of the air above the cell, yzw = placed block light there
// Include after the URP Core.hlsl and AtmosphereCommon.hlsl.
#ifndef BBTS_MEADOW_FLOWER_COMMON_INCLUDED
#define BBTS_MEADOW_FLOWER_COMMON_INCLUDED

#define BBTS_FLOWER_FADE_END 28.0  // metres: no flower beyond (GrassShells.FlowerRange)
#define BBTS_FLOWER_FADE_WIDTH 6.0 // metres over which they sink into the ground
#define BBTS_FLOWER_GROW 0.08      // the share of the season's density over which one flower grows to full size
#define BBTS_FLOWER_SWAY 0.6       // share of the plants' wind sway at the blossom

UNITY_INSTANCING_BUFFER_START(BbtsMeadowFlowerProps)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FlowerColor)
    UNITY_DEFINE_INSTANCED_PROP(float4, _FlowerInfo)
UNITY_INSTANCING_BUFFER_END(BbtsMeadowFlowerProps)

TEXTURE2D(_FlowerTex); SAMPLER(sampler_FlowerTex);

// The material's one property, in the same cbuffer layout in both passes (this file) as URP expects.
CBUFFER_START(UnityPerMaterial)
    float _FlowerDensity; // the season's share of flowers in bloom (0 … 1), set every frame by GrassShells
CBUFFER_END

struct FlowerAttributes
{
    float4 positionOS : POSITION; // one unit tall, rooted at 0
    float2 uv : TEXCOORD0;        // the sprite: blossom in the left half, stem in the right
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct FlowerVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 wp : TEXCOORD1;
    float4 color : TEXCOORD2; // _FlowerColor
    float4 info : TEXCOORD3;  // _FlowerInfo
};

FlowerVaryings BbtsFlowerVert(FlowerAttributes v)
{
    FlowerVaryings o = (FlowerVaryings)0;
    UNITY_SETUP_INSTANCE_ID(v);
    float4 color = UNITY_ACCESS_INSTANCED_PROP(BbtsMeadowFlowerProps, _FlowerColor);
    float4 info = UNITY_ACCESS_INSTANCED_PROP(BbtsMeadowFlowerProps, _FlowerInfo);

    float3 root = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
    float snow = _Sc_Surface.z > 0.5 ? saturate(_Sc_Surface.y) : 0.0;
    // The density is stretched a little so a full meadow (1) grows every flower to full size, rank 0.99 included.
    float grow = saturate((_FlowerDensity * (1.0 + BBTS_FLOWER_GROW) - color.a) / BBTS_FLOWER_GROW)
        * saturate((BBTS_FLOWER_FADE_END - distance(root, _WorldSpaceCameraPos)) / BBTS_FLOWER_FADE_WIDTH)
        * (1.0 - snow);
    if (grow < 0.02)
    {
        o.positionCS = float4(2.0, 2.0, 2.0, 1.0); // collapsed to one point outside the view: no fragments
        return o;
    }

    float3 wp = TransformObjectToWorld(v.positionOS.xyz * grow);
    wp += BbtsWindSway(wp, false, true, saturate(v.positionOS.y)) * (BBTS_FLOWER_SWAY * BbtsSwayShelter(info.x) * grow);
    o.positionCS = TransformWorldToHClip(wp);
    o.uv = v.uv;
    o.wp = wp;
    o.color = color;
    o.info = info;
    return o;
}

#endif
