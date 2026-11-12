// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The passes that turn a composed city into a buried ruin (#3334) — see <see cref="AncientRuinGenerator"/>. Every random
/// choice is a stateless hash of the seed, a lane and the cell (<see cref="U"/>), so the passes never share a stream and
/// the same seed always gives the same ruin. Works on its own copy of the voxels; the composed city is not changed.
/// </summary>
internal sealed partial class AncientRuinBuilder
{
    // Hash lanes — one per decision, so no pass moves another's rolls.
    private const ulong LaneCrumble = 0xA5A5_0001_0000_0001UL;
    private const ulong LaneCrumbleHeight = 0xA5A5_0002_0000_0002UL;
    private const ulong LaneGlass = 0xA5A5_0003_0000_0003UL;
    private const ulong LaneLight = 0xA5A5_0004_0000_0004UL;
    private const ulong LaneSpots = 0xA5A5_0005_0000_0005UL;
    private const ulong LaneSpotX = 0xA5A5_0006_0000_0006UL;
    private const ulong LaneSpotZ = 0xA5A5_0007_0000_0007UL;
    private const ulong LaneRadius = 0xA5A5_0008_0000_0008UL;
    private const ulong LaneFall = 0xA5A5_0009_0000_0009UL;
    private const ulong LaneRubble = 0xA5A5_000A_0000_000AUL;
    private const ulong LaneGlow = 0xA5A5_000B_0000_000BUL;
    private const ulong LaneDoor = 0xA5A5_000C_0000_000CUL;
    private const ulong LaneOre = 0xA5A5_000D_0000_000DUL;
    private const ulong LaneProp = 0xA5A5_000E_0000_000EUL;
    private const ulong LanePropKind = 0xA5A5_000F_0000_000FUL;
    private const ulong LanePropFace = 0xA5A5_0010_0000_0010UL;

    private const double CrumbleShare = 0.22;   // share of 2 × 2 column patches whose walls broke off
    private const double GlassBroken = 0.45;    // share of windows smashed
    private const double LightsAlive = 0.3;     // share of lights still glowing
    private const double GlowShare = 0.012;     // share of exposed eye-level wall cells that are Ressaw blocks
    private const int Sinkholes = 2;            // collapse spots whose floor gave way into the flooded undercroft
    private const int Purple = CityGenerator.Purple;
    private const int Red = CityGenerator.Red;

    private readonly int _w, _h, _l;
    private readonly ushort[] _blocks;
    private readonly Dictionary<int, (int Tint, int Glow)> _mods = new();
    private readonly Dictionary<int, int> _shapes = new();
    private readonly ulong _seedMix;
    private readonly GameContent _content;
    private readonly Dictionary<ushort, bool> _solid = new();
    private readonly ushort _wall, _glass, _ressaw, _ressawBlock, _ladder, _lightOff, _lightRed, _floor, _deepslate,
        _scrap, _panel, _helmet, _head, _skull, _crate, _stone, _warmLight;

    /// <summary>A canvas holding <paramref name="city"/> at (<paramref name="margin"/>, 0, <paramref name="margin"/>), one
    /// <paramref name="margin"/> wider on every side and at least <paramref name="height"/> rows tall.</summary>
    public AncientRuinBuilder(long seed, GameContent content, SettlementStructure city, int height, (int X, int Z) shaft, int margin = 0)
    {
        _content = content;
        _seedMix = StableHashing.Mix64(unchecked((ulong)seed) ^ 0x6D5A_2E11_C0FF_EE01UL);
        _w = city.Width + 2 * margin;
        _h = System.Math.Max(height, city.Height);
        _l = city.Length + 2 * margin;
        Shaft = shaft;
        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        _wall = B("iron_wall", B("stone"));
        _glass = B("glass");
        _ressaw = B("ressaw");
        _ressawBlock = B("ressaw_block", _wall);
        _ladder = B("ladder");
        _lightOff = B("strip_light_warm_off", _wall);
        _lightRed = B("light_red", B("strip_light_warm"));
        _floor = B("steel_floor", _wall);
        _deepslate = B("deepslate", B("stone"));
        _scrap = B("scrap_metal", _deepslate);
        _panel = B("rusted_panel", _scrap);
        _helmet = B("gds_diving_helmet");
        _head = B("gds_guardian_head");
        _skull = B("gds_skull");
        _crate = B("crate", _scrap);
        _stone = B("stone", _deepslate);
        _warmLight = B("strip_light_warm", _lightRed);
        _blocks = new ushort[_w * _h * _l];
        CopyCity(city, margin);
    }

    /// <summary>The ladder column of the entrance tube.</summary>
    public (int X, int Z) Shaft { get; }

    private void CopyCity(SettlementStructure city, int margin)
    {
        for (int x = 0; x < city.Width; x++)
            for (int y = 0; y < city.Height; y++)
                for (int z = 0; z < city.Length; z++)
                {
                    ushort b = city.Get(x, y, z);
                    if (b != 0)
                    {
                        var (tint, glow) = city.GetModifier(x, y, z);
                        Set(x + margin, y, z + margin, b, tint, city.GetShape(x, y, z), glow);
                    }
                }
    }

    /// <summary>A stone floor wherever the floor row is empty — an enclosed structure's walkway and any cell its authored
    /// floor leaves open (the surface stamper lays its foundation the same way).</summary>
    public void FillFloor()
    {
        for (int x = 0; x < _w; x++)
            for (int z = 0; z < _l; z++)
            {
                if (Get(x, 0, z) == 0)
                {
                    Set(x, 0, z, _deepslate);
                }
            }
    }

    // ---------------------------------------------------------------- the canvas

    private int Index(int x, int y, int z) => (x * _h + y) * _l + z;

    private bool In(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < _w && y < _h && z < _l;

    private ushort Get(int x, int y, int z) => In(x, y, z) ? _blocks[Index(x, y, z)] : (ushort)0;

    private int ShapeAt(int x, int y, int z) => In(x, y, z) && _shapes.TryGetValue(Index(x, y, z), out int s) ? s : 0;

    private void Set(int x, int y, int z, ushort b, int tint = 0, int shape = 0, int glow = 0)
    {
        if (!In(x, y, z))
        {
            return;
        }

        int i = Index(x, y, z);
        _blocks[i] = b;
        if (b != 0 && (tint != 0 || glow != 0)) _mods[i] = (tint, glow); else _mods.Remove(i);
        if (b != 0 && shape != 0) _shapes[i] = shape; else _shapes.Remove(i);
    }

    /// <summary>Solid and not a liquid — what you can stand on and what holds a wall.</summary>
    private bool Solid(ushort id)
    {
        if (id == 0)
        {
            return false;
        }

        if (!_solid.TryGetValue(id, out bool solid))
        {
            var def = _content.BlockById(new BlockId(id));
            solid = def is { Solid: true, Liquid: false };
            _solid[id] = solid;
        }

        return solid;
    }

    private bool IsLight(ushort id) => id != 0 && id != _ressawBlock && _content.BlockById(new BlockId(id)) is { Category: "light" };

    /// <summary>A stateless hash of the seed, a lane and a cell, as a uniform double in [0, 1).</summary>
    private double U(ulong lane, int x, int y, int z)
    {
        ulong h = _seedMix ^ lane ^ unchecked((ulong)(uint)x * 0x9E37_79B9_7F4A_7C15UL);
        h = StableHashing.Mix64(h ^ unchecked((ulong)(uint)y * 0xC2B2_AE3D_27D4_EB4FUL));
        return StableHashing.Unit(StableHashing.Mix64(h ^ unchecked((ulong)(uint)z * 0x1656_67B1_9E37_79F9UL)));
    }

    private int Chebyshev(int x, int z) => System.Math.Max(System.Math.Abs(x - Shaft.X), System.Math.Abs(z - Shaft.Z));

    /// <summary>The entrance landing: no decay, collapse, rubble or prop reaches it.</summary>
    private bool Protected(int x, int z) => Chebyshev(x, z) <= AncientRuinGenerator.EntranceClearance;

    private bool TouchesRessaw(int x, int y, int z)
        => Get(x - 1, y, z) == _ressaw || Get(x + 1, y, z) == _ressaw || Get(x, y, z - 1) == _ressaw || Get(x, y, z + 1) == _ressaw
           || Get(x, y + 1, z) == _ressaw;

    /// <summary>Whether decay may take this cell: not Ressaw, not what holds Ressaw (a vat's glass, a pool's rim), not a
    /// ladder or a shaped piece (stairs, tables, beds keep the ruin walkable and readable).</summary>
    private bool Breakable(int x, int y, int z, ushort b)
        => b != _ressaw && b != _ressawBlock && b != _ladder && ShapeAt(x, y, z) == 0 && !TouchesRessaw(x, y, z);

    // ---------------------------------------------------------------- broken walls

    /// <summary>Wall tops crumble in 2 × 2 column patches (everything above a cut height of 2–6 goes), windows are smashed,
    /// most lights are dead.</summary>
    public void Decay()
    {
        for (int x = 0; x < _w; x++)
            for (int z = 0; z < _l; z++)
            {
                if (Protected(x, z))
                {
                    continue;
                }

                bool crumble = U(LaneCrumble, x >> 1, 0, z >> 1) < CrumbleShare;
                int cut = 2 + (int)(U(LaneCrumbleHeight, x >> 1, 0, z >> 1) * 5);
                for (int y = 1; y < _h; y++)
                {
                    ushort b = Get(x, y, z);
                    if (b == 0 || b == _ressaw || b == _ressawBlock || TouchesRessaw(x, y, z))
                    {
                        continue;
                    }

                    if (crumble && y >= cut)
                    {
                        Set(x, y, z, 0);
                    }
                    else if (b == _glass && Breakable(x, y, z, b) && U(LaneGlass, x, y, z) < GlassBroken)
                    {
                        Set(x, y, z, 0);
                    }
                    else if (IsLight(b) && U(LaneLight, x, y, z) >= LightsAlive)
                    {
                        Set(x, y, z, _lightOff);
                    }
                }
            }
    }

    // ---------------------------------------------------------------- collapses

    /// <summary>Three to five collapse spots away from the entrance: the first two are sinkholes (the floor gave way into
    /// the flooded undercroft), the others are fallen roofs with rubble on the floor. <paramref name="sinkholesOnly"/>
    /// (#3516, after the shared ruin pass, which brought the roofs down already): the two sinkholes alone.</summary>
    public void Collapse(bool sinkholesOnly = false)
    {
        int spots = sinkholesOnly ? Sinkholes : 3 + (int)(U(LaneSpots, 0, 0, 0) * 3);
        int placed = 0;
        for (int attempt = 0; attempt < 48 && placed < spots; attempt++)
        {
            int r = 2 + (int)(U(LaneRadius, attempt, 0, 0) * 3);
            int span = System.Math.Max(1, _w - 2 * r - 6);
            int cx = r + 3 + (int)(U(LaneSpotX, attempt, 0, 0) * span);
            int cz = r + 3 + (int)(U(LaneSpotZ, attempt, 0, 0) * System.Math.Max(1, _l - 2 * r - 6));
            if (Chebyshev(cx, cz) <= AncientRuinGenerator.EntranceClearance + r + 1)
            {
                continue;
            }

            if (placed < Sinkholes)
            {
                if (HoldsRessawAbove(cx, cz, r + 1))
                {
                    continue; // a vat stands here: a sinkhole would cut it open
                }

                Sinkhole(cx, cz, r);
            }
            else
            {
                RoofFall(cx, cz, r);
            }

            placed++;
        }
    }

    private bool HoldsRessawAbove(int cx, int cz, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
                for (int y = 1; y < _h; y++)
                {
                    if (Get(x, y, z) == _ressaw)
                    {
                        return true;
                    }
                }

        return false;
    }

    private void Sinkhole(int cx, int cz, int r)
    {
        int inner = (r - 1) * (r - 1);
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
            {
                int d2 = (x - cx) * (x - cx) + (z - cz) * (z - cz);
                if (d2 <= inner)
                {
                    for (int y = 1; y < _h; y++)
                    {
                        Set(x, y, z, 0); // whatever stood here went down with the floor
                    }

                    Set(x, 0, z, _ressaw);
                }
                else if (d2 <= r * r)
                {
                    Rubble(x, z, 0.5);
                }
            }
    }

    private void RoofFall(int cx, int cz, int r)
    {
        for (int x = cx - r; x <= cx + r; x++)
            for (int z = cz - r; z <= cz + r; z++)
            {
                if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r * r)
                {
                    continue;
                }

                for (int y = 2; y < _h; y++)
                {
                    ushort b = Get(x, y, z);
                    if (b != 0 && Breakable(x, y, z, b) && U(LaneFall, x, y, z) < 0.8)
                    {
                        Set(x, y, z, 0);
                    }
                }

                Rubble(x, z, 0.45);
            }
    }

    /// <summary>A rubble block on the floor at (x, 1, z) — scrap, a rusted panel or a fallen rock — when the cell is free.</summary>
    private void Rubble(int x, int z, double chance)
    {
        if (!In(x, 1, z) || Protected(x, z) || Get(x, 1, z) != 0 || !Solid(Get(x, 0, z)) || TouchesRessaw(x, 1, z))
        {
            return;
        }

        double u = U(LaneRubble, x, 1, z);
        if (u < chance)
        {
            Set(x, 1, z, u < chance / 3 ? _scrap : u < chance * 2 / 3 ? _panel : _deepslate);
        }
    }

    // ---------------------------------------------------------------- glowing walls

    /// <summary>Sets glowing Ressaw blocks into exposed eye-level walls (rows 1–3) — a share of them, at least
    /// <see cref="AncientRuinGenerator.MinRessawBlocks"/> at eye level in the ruin.</summary>
    public void SetRessawIntoWalls()
    {
        int count = 0;
        var candidates = new List<(double U, int X, int Y, int Z)>();
        for (int x = 0; x < _w; x++)
            for (int y = 1; y <= 3 && y < _h; y++)
                for (int z = 0; z < _l; z++)
                {
                    ushort b = Get(x, y, z);
                    if (b == _ressawBlock)
                    {
                        count++;
                    }
                    else if (b == _wall && ShapeAt(x, y, z) == 0 && !Protected(x, z) && Exposed(x, y, z))
                    {
                        candidates.Add((U(LaneGlow, x, y, z), x, y, z));
                    }
                }

        candidates.Sort((a, b) => a.U.CompareTo(b.U));
        foreach (var c in candidates)
        {
            if (c.U >= GlowShare && count >= AncientRuinGenerator.MinRessawBlocks)
            {
                break;
            }

            Set(c.X, c.Y, c.Z, _ressawBlock); // the tint goes: the block glows white
            count++;
        }
    }

    private bool Exposed(int x, int y, int z)
        => (In(x - 1, y, z) && Get(x - 1, y, z) == 0) || (In(x + 1, y, z) && Get(x + 1, y, z) == 0)
           || (In(x, y, z - 1) && Get(x, y, z - 1) == 0) || (In(x, y, z + 1) && Get(x, y, z + 1) == 0);
}
