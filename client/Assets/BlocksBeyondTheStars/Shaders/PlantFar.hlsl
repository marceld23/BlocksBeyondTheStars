// Blocks Beyond the Stars — plant far pictures (PlantBodies.FarPictures.cs).
//
// Beyond the plant bodies' range a plant is its crossed picture. Its far picture is baked from the body into a small far
// atlas (16 x 8 cells of 64 px); the atlas' LAST cell holds a slot map: texel (slot % 32, slot / 32) of that cell carries,
// in its alpha, 1 + the far cell of the block-atlas slot (0 = no far picture: the tile stays). A staged crossed picture
// (cutout code 3 or 4 with the bodies' mark, BbtsStagedPlant) whose slot has an entry samples the far atlas instead of
// the block atlas, at the same place inside the cell and with the same texel density. Off while
// _Sc_LookPlantFarInfo.z is 0 — the pictures are not baked yet.
// Include after AtmosphereCommon.hlsl and after the pass declared _MainTex's sampler (sampler_MainTex, point + clamp
// like the far atlas; platforms without separate samplers use the far atlas' own, which is the same).
#ifndef BBTS_PLANT_FAR_INCLUDED
#define BBTS_PLANT_FAR_INCLUDED

TEXTURE2D(_Sc_LookPlantFar);
float4 _Sc_LookPlantFarInfo; // x = cells per row, y = rows, z = 1 when on, w = the mip with one texel per cell

// True when this fragment's crossed picture has a baked far picture; farUv = where to sample it, farCell = its cell.
bool BbtsPlantFarUv(float2 uv, float leafCode, out float2 farUv, out float2 farCell)
{
    farUv = uv;
    farCell = float2(0.0, 0.0);
    if (_Sc_LookPlantFarInfo.z < 0.5 || leafCode < 2.5 || leafCode > 4.5 || !BbtsStagedPlant(leafCode))
    {
        return false;
    }

    float2 grid = max(_Sc_LookPlantFarInfo.xy, float2(1.0, 1.0));
    float2 slot = floor(uv * 32.0);
    float2 mapUv = ((grid - 1.0) * 64.0 + slot + 0.5) / (grid * 64.0);
    float index = round(SAMPLE_TEXTURE2D_LOD(_Sc_LookPlantFar, sampler_MainTex, mapUv, 0).a * 255.0) - 1.0;
    if (index < 0.0)
    {
        return false;
    }

    farCell = float2(fmod(index, grid.x), floor(index / grid.x));
    farUv = (farCell + saturate(uv * 32.0 - slot)) / grid;
    return true;
}

// The UV gradient in the far atlas for a gradient in the block atlas: one far cell is 1/grid of the atlas where a tile
// is 1/32, both 64 px wide, so the texel density stays.
float2 BbtsPlantFarGrad(float2 d)
{
    return d * (32.0 / max(_Sc_LookPlantFarInfo.xy, float2(1.0, 1.0)));
}

#endif
