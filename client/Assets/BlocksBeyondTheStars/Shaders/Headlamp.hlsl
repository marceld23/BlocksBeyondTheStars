// Blocks Beyond the Stars — the suit headlamp (#3229–#3234): an LED spotlight per player, shared by every lit shader.
//
// ADR 0003: the voxel shaders own their lighting, so the headlamp is no Unity Light. The player's own lamp and up to
// BBTS_REMOTE_LAMPS lamps of other players (#3234) arrive as per-frame globals (Headlamp.cs) and every lit surface adds
// them through BbtsHeadlamps():
//   - an LED beam: a bright hotspot, a faint reflector ring just inside its edge and a soft, dim spill (#3229);
//   - windowed inverse-square falloff with a small source radius — near rock is bright, rock at 15 m is dim (#3229);
//   - Lambert plus the same stable GGX glint the sun and the placed block light use (#3229);
//   - on the High preset, occlusion by the blocks around the player: a march through the 64³ occupancy volume
//     (LampVolume.cs, #3233) — no shadow map and no extra geometry pass.
// Plain HLSL that also compiles in the Built-in RP CG passes; the occupancy volume needs the SRP texture macros and is
// only declared where they exist (the URP passes) — elsewhere the lamp is simply unshadowed. A shader that cannot afford
// the march (the many-layer grass shells) defines BBTS_LAMP_NO_SHADOW before including this file.
//
// Globals:
//   _Sc_LampPos    xyz world position of the own lamp, w = range (m)
//   _Sc_LampDir    xyz beam axis (unit), w = cosine of the spill's outer edge
//   _Sc_LampColor  rgb LED colour × intensity (linear, always set — the remote lamps use it too), a > 0.5 = own lamp lit
//   _Sc_LampShape  x = cosine of the hotspot edge, y = cosine of the hotspot core, z = spill level, w = source radius² (m²)
//   _Sc_RLampPos[] xyz world position of another player's lamp, w = range
//   _Sc_RLampDir[] xyz beam axis of that lamp
//   _Sc_RLampCount live remote slots (0..BBTS_REMOTE_LAMPS)
//   _Sc_LampVolume 64³ occupancy (r: 1 = an opaque cube, 0 = open), addressed by world cell with wrap
//   _Sc_LampVoxel  xyz = the volume's lowest world cell, w > 0.5 = lamp shadows on
#ifndef BBTS_HEADLAMP_INCLUDED
#define BBTS_HEADLAMP_INCLUDED

#define BBTS_REMOTE_LAMPS 4
#define BBTS_LAMP_SHADOW_STEPS 32
#define BBTS_LAMP_VOLUME_SIZE 64.0

float4 _Sc_LampPos;
float4 _Sc_LampDir;
float4 _Sc_LampColor;
float4 _Sc_LampShape;
float4 _Sc_RLampPos[BBTS_REMOTE_LAMPS];
float4 _Sc_RLampDir[BBTS_REMOTE_LAMPS];
float _Sc_RLampCount;
float4 _Sc_LampVoxel;

// The beam's brightness along a direction whose cosine to the beam axis is `c` (1 on the axis): the hotspot, the reflector
// ring and the spill, 0 beyond the spill's edge.
float BbtsLampProfile(float c)
{
    float edge = _Sc_LampShape.x;
    float core = _Sc_LampShape.y;
    float spill = smoothstep(_Sc_LampDir.w, edge, c);
    float hot = smoothstep(edge, core, c);
    float ringAt = lerp(edge, core, 0.3);
    float ring = saturate(1.0 - abs(c - ringAt) / max((core - edge) * 0.35, 1e-5));
    return _Sc_LampShape.z * spill + (1.0 - _Sc_LampShape.z) * hot + 0.10 * ring * ring;
}

// Windowed inverse-square falloff: 1 / (d² + r²) with the source radius r, eased to 0 at the range (d2 = distance²).
float BbtsLampFalloff(float d2, float range)
{
    float x = d2 / max(range * range, 1e-4);
    float win = saturate(1.0 - x * x);
    return win * win / (d2 + _Sc_LampShape.w);
}

// The light a lamp at `lampPos` (axis `axis`, reach `range`) sends to point `p`, before the colour: profile × falloff.
// 0 outside the cone or the range.
float BbtsLampRadiance(float3 lampPos, float3 axis, float range, float3 p)
{
    float3 fromLamp = p - lampPos;
    float d2 = dot(fromLamp, fromLamp);
    if (d2 >= range * range)
    {
        return 0.0;
    }

    float c = dot(fromLamp * rsqrt(max(d2, 1e-8)), axis);
    return c > _Sc_LampDir.w ? BbtsLampProfile(c) * BbtsLampFalloff(d2, range) : 0.0;
}

#if defined(TEXTURE3D) && !defined(BBTS_LAMP_NO_SHADOW)
TEXTURE3D(_Sc_LampVolume);
SAMPLER(sampler_Sc_LampVolume);

// How much the block at world point `p` blocks the lamp (0 open … 1 an opaque cube); open outside the volume.
float BbtsLampOcclusionAt(float3 p)
{
    float3 cell = floor(p);
    float3 rel = cell - _Sc_LampVoxel.xyz;
    if (any(rel < 0.0) || any(rel >= BBTS_LAMP_VOLUME_SIZE))
    {
        return 0.0;
    }

    return SAMPLE_TEXTURE3D_LOD(_Sc_LampVolume, sampler_Sc_LampVolume, (cell + 0.5) / BBTS_LAMP_VOLUME_SIZE, 0).r;
}

// How much of a lamp at `lampPos` reaches `from` (1 = unblocked): up to `maxSteps` jittered samples of the occupancy
// volume along the way, about one per 0.7 m. Samples in the start cell are skipped: a surface inside its own cell (a
// cactus, a slab top, a stair tread) must not shadow itself; for a cube face that cell is the open one in front of it.
// Fixed loop bound with an early exit (WebGL 2 / WebGPU rules).
float BbtsLampShadowN(float3 from, float3 lampPos, float maxSteps)
{
    if (_Sc_LampVoxel.w < 0.5)
    {
        return 1.0;
    }

    float3 seg = lampPos - from;
    float n = clamp(ceil(length(seg) / 0.7), 1.0, maxSteps);
    float jitter = frac(52.9829189 * frac(dot(floor(from * 8.0).xz + floor(from.y * 8.0), float2(0.06711056, 0.00583715))));
    float3 startCell = floor(from);
    float vis = 1.0;
    [loop]
    for (int i = 0; i < BBTS_LAMP_SHADOW_STEPS; i++)
    {
        if (i >= n || vis < 0.03)
        {
            break;
        }

        float3 sp = from + seg * ((i + jitter) / n);
        if (any(floor(sp) != startCell))
        {
            vis *= 1.0 - BbtsLampOcclusionAt(sp);
        }
    }

    return vis;
}
#else
float BbtsLampShadowN(float3 from, float3 lampPos, float maxSteps)
{
    return 1.0;
}
#endif

// One lamp's light on a surface, added to `diffuse` (× albedo by the caller) and `spec` (× F0): Lambert and the stable
// GGX glint of the block shader, the beam, the falloff and — on High — the blocks in the way.
void BbtsLampTerm(float3 lampPos, float3 axis, float range, float3 wp, float3 N, float3 V, float rough,
    inout float3 diffuse, inout float3 spec)
{
    float radiance = BbtsLampRadiance(lampPos, axis, range, wp);
    if (radiance <= 0.0)
    {
        return;
    }

    float3 L = normalize(lampPos - wp);
    float ndl = saturate(dot(N, L));
    if (ndl <= 0.0)
    {
        return;
    }

    radiance *= BbtsLampShadowN(wp + N * 0.06, lampPos, BBTS_LAMP_SHADOW_STEPS);
    float3 H = normalize(L + V);
    float nh = saturate(dot(N, H));
    float lh = saturate(dot(L, H));
    float r2 = rough * rough;
    float dterm = nh * nh * (r2 - 1.0) + 1.00001;
    float specTerm = r2 / ((dterm * dterm) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
    diffuse += radiance * ndl;
    spec += radiance * ndl * min(specTerm, 24.0);
}

// Every lit headlamp (the own one and the other players') on a surface: albedo × diffuse + F0 × glint, in the LED colour.
// `rough` is the perceptual roughness (1 - gloss). Costs one uniform compare per lamp kind when no lamp is lit.
float3 BbtsHeadlamps(float3 wp, float3 N, float3 V, float3 albedo, float3 F0, float rough)
{
    float3 diffuse = float3(0.0, 0.0, 0.0);
    float3 spec = float3(0.0, 0.0, 0.0);
    rough = clamp(rough, 0.045, 1.0);
    if (_Sc_LampColor.a > 0.5)
    {
        BbtsLampTerm(_Sc_LampPos.xyz, _Sc_LampDir.xyz, _Sc_LampPos.w, wp, N, V, rough, diffuse, spec);
    }

    int count = (int)_Sc_RLampCount;
    [loop]
    for (int k = 0; k < BBTS_REMOTE_LAMPS; k++)
    {
        if (k >= count)
        {
            break;
        }

        BbtsLampTerm(_Sc_RLampPos[k].xyz, _Sc_RLampDir[k].xyz, _Sc_RLampPos[k].w, wp, N, V, rough, diffuse, spec);
    }

    return _Sc_LampColor.rgb * (albedo * diffuse + F0 * spec);
}

// The headlamps' light at a point in the air (a dust mote, a particle — no surface normal), × colour, unshadowed.
float3 BbtsHeadlampsAt(float3 p)
{
    float radiance = 0.0;
    if (_Sc_LampColor.a > 0.5)
    {
        radiance += BbtsLampRadiance(_Sc_LampPos.xyz, _Sc_LampDir.xyz, _Sc_LampPos.w, p);
    }

    int count = (int)_Sc_RLampCount;
    [loop]
    for (int k = 0; k < BBTS_REMOTE_LAMPS; k++)
    {
        if (k >= count)
        {
            break;
        }

        radiance += BbtsLampRadiance(_Sc_RLampPos[k].xyz, _Sc_RLampDir[k].xyz, _Sc_RLampPos[k].w, p);
    }

    return _Sc_LampColor.rgb * radiance;
}

// The lamps mirrored in a smooth surface (water): a tight highlight around the reflected view ray `R`, × colour.
float3 BbtsHeadlampMirror(float3 wp, float3 R)
{
    float3 acc = float3(0.0, 0.0, 0.0);
    if (_Sc_LampColor.a > 0.5)
    {
        float3 L = normalize(_Sc_LampPos.xyz - wp);
        acc += BbtsLampRadiance(_Sc_LampPos.xyz, _Sc_LampDir.xyz, _Sc_LampPos.w, wp) * pow(saturate(dot(R, L)), 300.0);
    }

    int count = (int)_Sc_RLampCount;
    [loop]
    for (int k = 0; k < BBTS_REMOTE_LAMPS; k++)
    {
        if (k >= count)
        {
            break;
        }

        float3 Lk = normalize(_Sc_RLampPos[k].xyz - wp);
        acc += BbtsLampRadiance(_Sc_RLampPos[k].xyz, _Sc_RLampDir[k].xyz, _Sc_RLampPos[k].w, wp) * pow(saturate(dot(R, Lk)), 300.0);
    }

    return _Sc_LampColor.rgb * min(acc, float3(6.0, 6.0, 6.0)) * 2.0;
}

#endif
