// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Seating and stamping an underground structure (#3334) — see GameServerAncientRuins.cs. The stamp writes, in one repo
/// transaction and only once per world: every cell of the ruin's box (air included — the carve), the flooded basins under
/// it with the Ressaw crystal ore in their floor (#3338), a deepslate shell wherever the cut meets air, a liquid or loose
/// ground (so no water, lava or sand runs in and nothing opens to the sky), and the shaft with its ladder up to a vent tower
/// or the city hatch.
/// </summary>
public sealed partial class GameServer
{
    // ---------------------------------------------------------------- seats

    /// <summary>Under the G.D.S. city: the hatch goes into the city street crossing nearest the city centre (clear of the
    /// landing pad and the crash site), and the ruin is placed so that one of its own crossings lies right under it — the
    /// one that keeps the ruin nearest the centre. False without a stamped city (the open-land seat is used then).</summary>
    private bool TrySeatAncientRuinUnderCity(AncientRuinPlan plan)
    {
        var cityRec = FindPlacementRecord("settlement", 0);
        if (cityRec is not { Placed: true } || _worlds.Active.CityFootprint is null)
        {
            return false;
        }

        if (!CityLayoutSpec.TryParse(cityRec.KitLayout, out var city))
        {
            city = CityLayoutSpec.Default;
        }

        int stride = city.DistrictSize + city.Street;
        int centreX = cityRec.X + city.Footprint / 2, centreZ = cityRec.Z + city.Footprint / 2;
        var crossings = new List<(long D, int X, int Z)>();
        for (int i = 1; i < city.Grid; i++)
            for (int j = 1; j < city.Grid; j++)
            {
                int x = cityRec.X + i * stride + 1, z = cityRec.Z + j * stride + 1;
                if (!NearPadOrWreck(x, z))
                {
                    crossings.Add(((long)(x - centreX) * (x - centreX) + (long)(z - centreZ) * (z - centreZ), x, z));
                }
            }

        if (crossings.Count == 0)
        {
            return false;
        }

        crossings.Sort((a, b) => a.D != b.D ? a.D.CompareTo(b.D) : a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
        var (_, hx, hz) = crossings[0];
        int foot = plan.Width;
        long best = long.MaxValue;
        foreach (var (i, j) in AncientRuinGenerator.EntranceCrossings(plan.Spec))
        {
            var (sx, sz) = AncientRuinGenerator.ShaftAt(plan.Spec, i, j);
            long dx = hx - sx + foot / 2 - centreX, dz = hz - sz + foot / 2 - centreZ;
            if (dx * dx + dz * dz < best)
            {
                best = dx * dx + dz * dz;
                (plan.CrossI, plan.CrossJ, plan.OriginX, plan.OriginZ) = (i, j, hx - sx, hz - sz);
            }
        }

        plan.Hatch = true;
        plan.EntranceY = cityRec.GroundY;
        plan.GroundY = AncientRuinGroundY(plan, cityRec.GroundY);
        return true;
    }

    private bool NearPadOrWreck(int x, int z)
    {
        foreach (var pad in _landingPads)
        {
            if (System.Math.Abs(WorldConstants.WrapDeltaX(x - pad.CenterX, _world.Circumference)) <= LandingPadRadius + 4
                && System.Math.Abs(z - pad.CenterZ) <= LandingPadRadius + 4)
            {
                return true;
            }
        }

        var (wx, wz) = WreckAnchorFor(_landingPads);
        return System.Math.Abs(WorldConstants.WrapDeltaX(x - wx, _world.Circumference)) <= WreckReservedHalfExtent + 2
               && System.Math.Abs(z - wz) <= WreckReservedHalfExtent + 2;
    }

    /// <summary>In the open land: a deterministic ring of spots 160–400 blocks from pad 0 inside the latitude band, the first
    /// clear of every pad and structure whose entrance (a G.D.S. ruin's street crossings in hash order, an enclosed structure's
    /// walkway corner) stands on dry ground. False on a body too small to hide the structure under, or when nothing fits.</summary>
    private bool TrySeatAncientRuinInOpenLand(AncientRuinPlan plan)
    {
        int circ = _world.Circumference, w = plan.Width, l = plan.Length;
        if (!AncientRuinFitsOpenLand(circ, w, l))
        {
            return false;
        }

        int latBand = AncientRuinLatitudeBand(circ, l);
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0, pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        var rng = RngFor(plan.Seed, "spot");
        var reserved = AncientRuinReserved();
        var shafts = UndergroundShaftCandidates(plan);
        for (int attempt = 0; attempt < 48; attempt++)
        {
            double ang = rng.NextDouble() * System.Math.PI * 2.0;
            int dist = 160 + rng.Next(240);
            int cx = WorldConstants.WrapX(pad0X + (int)System.Math.Round(System.Math.Cos(ang) * dist), circ);
            int cz = System.Math.Clamp(pad0Z + (int)System.Math.Round(System.Math.Sin(ang) * dist), -latBand, latBand);
            if (OverlapsAnySettlement(cx, cz, System.Math.Max(w, l) / 2 + 4) || OverlapsFootprint(cx, cz, w / 2, l / 2, reserved, 8))
            {
                continue;
            }

            foreach (var (i, j, sx, sz) in shafts)
            {
                int ex = WorldConstants.WrapX(cx - w / 2 + sx, circ), ez = cz - l / 2 + sz;
                if (AncientRuinEntranceDry(ex, ez))
                {
                    (plan.CrossI, plan.CrossJ, plan.OriginX, plan.OriginZ, plan.Hatch) = (i, j, cx - w / 2, cz - l / 2, false);
                    plan.EntranceY = _generator.SurfaceHeight(_world.Planet, ex, ez);
                    plan.GroundY = AncientRuinGroundY(plan, plan.EntranceY);
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Whether a body of <paramref name="circumference"/> blocks is big enough to hide a <paramref name="w"/> ×
    /// <paramref name="l"/> structure in its open land: four footprints around, and a latitude band left over. The seat and the
    /// admin's <c>/ressaw</c> prediction (#3421) both ask it.</summary>
    private static bool AncientRuinFitsOpenLand(int circumference, int w, int l)
        => AncientRuinLatitudeBand(circumference, l) >= 16 && circumference >= 4 * System.Math.Max(w, l);

    /// <summary>How far north or south of the equator the centre of an <paramref name="l"/>-long structure may lie.</summary>
    private static int AncientRuinLatitudeBand(int circumference, int l)
        => WorldConstants.LatitudePeriodFor(circumference) / 2 - l / 2 - 16;

    /// <summary>Where the shaft may stand: a G.D.S. ruin's interior street crossings in hash order; an enclosed structure's one
    /// walkway corner, (1, 1).</summary>
    private static List<(int I, int J, int X, int Z)> UndergroundShaftCandidates(AncientRuinPlan plan)
    {
        var shafts = new List<(int I, int J, int X, int Z)>();
        if (!plan.Gds)
        {
            shafts.Add((0, 0, 1, 1));
            return shafts;
        }

        foreach (var (i, j) in AncientRuinGenerator.EntranceCrossings(plan.Spec))
        {
            var (x, z) = AncientRuinGenerator.ShaftAt(plan.Spec, i, j);
            shafts.Add((i, j, x, z));
        }

        shafts.Sort((a, b) => StableHashing.Unit($"gds_ruin:cross:{a.I}:{a.J}", plan.Seed)
            .CompareTo(StableHashing.Unit($"gds_ruin:cross:{b.I}:{b.J}", plan.Seed)));
        return shafts;
    }

    /// <summary>What a ruin in the open land keeps clear of besides the settlements: every pad, the wreck site, the fallen
    /// ruins, the monuments, the bandit camps, the factories and the SPS labs.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> AncientRuinReserved()
    {
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 24, LandingPadRadius + 24));
        }

        var (wreckX, wreckZ) = WreckAnchorFor(_landingPads);
        reserved.Add((wreckX, wreckZ, WreckReservedHalfExtent, WreckReservedHalfExtent));
        reserved.AddRange(_worlds.Active.RuinFootprints);
        void Box(Vector3i min, Vector3i max) => reserved.Add(((min.X + max.X) / 2, (min.Z + max.Z) / 2, (max.X - min.X) / 2 + 1, (max.Z - min.Z) / 2 + 1));
        _monuments.ForEach(m => Box(m.Min, m.Max));
        _banditCamps.ForEach(c => Box(c.Min, c.Max));
        _factories.ForEach(f => Box(f.Min, f.Max));
        _spsLabs.ForEach(l => Box(l.Min, l.Max));
        return reserved;
    }

    /// <summary>Dry ground for a vent tower: no water, lava or sea ice over the column.</summary>
    private bool AncientRuinEntranceDry(int x, int z)
    {
        var planet = _world.Planet;
        return !_generator.TryGetWaterSurface(planet, x, z, out _, out _) && !_generator.IsSurfaceLava(planet, x, z)
               && !(SeaIceIsWet && _generator.TryGetIceSurface(planet, x, z, out _));
    }

    /// <summary>The floor row of a ruin: its ceiling shell sits <see cref="AncientRuinCover"/> rows under the lowest surface over
    /// the footprint (sampled every four columns and along the far rims) — and under <paramref name="entranceY"/>.</summary>
    private int AncientRuinGroundY(AncientRuinPlan plan, int entranceY)
    {
        int lowest = entranceY;
        for (int x = 0; x < plan.Width + 3; x += 4)
            for (int z = 0; z < plan.Length + 3; z += 4)
            {
                int sx = System.Math.Min(x, plan.Width - 1), sz = System.Math.Min(z, plan.Length - 1);
                lowest = System.Math.Min(lowest, _generator.SurfaceHeight(_world.Planet, plan.OriginX + sx, plan.OriginZ + sz));
            }

        return lowest - AncientRuinCover - plan.Height;
    }

    // ---------------------------------------------------------------- the stamp

    /// <summary>Writes the whole ruin (inside the caller's transaction). Returns the cells written.</summary>
    private int WriteAncientRuin(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        int cells = WriteAncientRuinBox(plan, layout);
        cells += WriteAncientRuinBasins(plan, layout);
        cells += SealAncientRuinShell(plan, layout);
        cells += WriteAncientRuinShaft(plan, layout);
        return cells;
    }

    private Vector3i AncientRuinCell(AncientRuinPlan plan, int x, int y, int z) => new(plan.OriginX + x, plan.GroundY + y, plan.OriginZ + z);

    private BlockId AncientRuinBlock(string key, string fallback = "stone")
        => (_content.GetBlock(key) ?? _content.GetBlock(fallback))?.NumericId ?? BlockId.Air;

    /// <summary>The box: every cell from the floor row to the last row under the ceiling, air included (the carve), with
    /// its tint, glow and shape.</summary>
    private int WriteAncientRuinBox(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        for (int x = 0; x < layout.Width; x++)
            for (int y = 0; y < layout.Height; y++)
                for (int z = 0; z < layout.Length; z++)
                {
                    var (tint, glow) = layout.GetModifier(x, y, z);
                    _world.SetBlock(AncientRuinCell(plan, x, y, z), new BlockId(layout.Get(x, y, z)), tint, glow, layout.GetShape(x, y, z));
                }

        return layout.Width * layout.Height * layout.Length;
    }

    /// <summary>The flooded undercroft: still Ressaw from the floor row down <see cref="AncientRuinGenerator.BasinDepth"/> rows,
    /// the basin floor deepslate or Ressaw crystal ore, its sides sealed.</summary>
    private int WriteAncientRuinBasins(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        var ressaw = AncientRuinBlock("ressaw", "water");
        var ore = AncientRuinBlock("ressaw_crystal_ore");
        var shell = AncientRuinBlock("deepslate");
        var columns = new HashSet<(int X, int Z)>(layout.BasinColumns);
        var oreColumns = new HashSet<(int X, int Z)>();
        foreach (var c in layout.OreCells)
        {
            oreColumns.Add((c.X, c.Z));
        }

        int bottom = -AncientRuinGenerator.BasinDepth - 1, n = 0;
        foreach (var (x, z) in layout.BasinColumns)
        {
            for (int y = -1; y > bottom; y--)
            {
                _world.SetBlock(AncientRuinCell(plan, x, y, z), ressaw);
                n++;
            }

            _world.SetBlock(AncientRuinCell(plan, x, bottom, z), oreColumns.Contains((x, z)) ? ore : shell);
            n++;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                if (!columns.Contains((x + dx, z + dz)))
                {
                    for (int y = -1; y > bottom; y--)
                    {
                        n += SealAncientRuinCell(AncientRuinCell(plan, x + dx, y, z + dz), shell);
                    }
                }
            }
        }

        return n;
    }

    /// <summary>The stone shell: the ceiling row and the four walls around the box, and under any floor cell that does not
    /// hold — written only where the natural cell is air, a liquid, loose ground or not solid.</summary>
    private int SealAncientRuinShell(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        var shell = AncientRuinBlock("deepslate");
        var ressaw = AncientRuinBlock("ressaw", "water").Value;
        int w = layout.Width, h = layout.Height, l = layout.Length, n = 0;
        for (int x = 0; x < w; x++)
            for (int z = 0; z < l; z++)
            {
                if ((x, z) != layout.Shaft)
                {
                    n += SealAncientRuinCell(AncientRuinCell(plan, x, h, z), shell);
                }

                ushort floor = layout.Get(x, 0, z);
                if (floor != ressaw && _content.BlockById(new BlockId(floor)) is not { Solid: true, Liquid: false })
                {
                    n += SealAncientRuinCell(AncientRuinCell(plan, x, -1, z), shell);
                }
            }

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                n += SealAncientRuinCell(AncientRuinCell(plan, x, y, -1), shell) + SealAncientRuinCell(AncientRuinCell(plan, x, y, l), shell);
            }

            for (int z = 0; z < l; z++)
            {
                n += SealAncientRuinCell(AncientRuinCell(plan, -1, y, z), shell) + SealAncientRuinCell(AncientRuinCell(plan, w, y, z), shell);
            }
        }

        return n;
    }

    /// <summary>Puts <paramref name="shell"/> into a cell that would let something in: air, a liquid, loose (granular) ground
    /// or anything not solid. Returns 1 when it wrote.</summary>
    private int SealAncientRuinCell(Vector3i cell, BlockId shell)
    {
        if (_content.BlockById(_world.GetBlock(cell)) is { Solid: true, Liquid: false, Granular: false })
        {
            return 0;
        }

        _world.SetBlock(cell, shell);
        return 1;
    }

    /// <summary>The shaft from the ceiling to the surface: the tube's purple ring through the ceiling row, the ladder all the
    /// way up, the rock around it sealed; at the top the vent tower or the city hatch.</summary>
    private int WriteAncientRuinShaft(AncientRuinPlan plan, AncientRuinLayout layout)
    {
        var (sx, sz) = layout.Shaft;
        int wx = plan.OriginX + sx, wz = plan.OriginZ + sz;
        int ceiling = plan.GroundY + layout.Height;
        int top = plan.Hatch ? plan.EntranceY - 1 : plan.EntranceY + AncientRuinTowerHeight;
        var ladder = AncientRuinBlock("ladder");
        var wall = AncientRuinBlock("iron_wall");
        var shell = AncientRuinBlock("deepslate");
        int n = 0;
        for (int y = ceiling; y <= top; y++)
        {
            _world.SetBlock(new Vector3i(wx, y, wz), ladder);
            n++;
            if (y > (plan.Hatch ? plan.EntranceY - 1 : plan.EntranceY))
            {
                continue; // the tower's own walls stand around the ladder up there
            }

            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    var cell = new Vector3i(wx + dx, y, wz + dz);
                    if (y == ceiling)
                    {
                        _world.SetBlock(cell, plan.Gds ? wall : shell, plan.Gds ? CityGenerator.Purple : 0); // the tube's ring
                        n++;
                    }
                    else
                    {
                        n += SealAncientRuinCell(cell, shell);
                    }
                }
        }

        return n + (plan.Hatch ? WriteAncientRuinHatch(wx, plan.EntranceY, wz) : WriteAncientRuinVentTower(wx, plan.EntranceY, wz, plan.Seed, plan.Gds));
    }

    /// <summary>The sealed hatch in the city street: a glass pane over the ladder in a ring of red G.D.S. plates, flush with the
    /// street (the city's machines walk over it).</summary>
    private int WriteAncientRuinHatch(int x, int y, int z)
    {
        var glass = AncientRuinBlock("glass");
        var wall = AncientRuinBlock("iron_wall");
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                {
                    _world.SetBlock(new Vector3i(x, y, z), glass);
                }
                else
                {
                    _world.SetBlock(new Vector3i(x + dx, y, z + dz), wall, CityGenerator.Red);
                }
            }

        return 9;
    }

    /// <summary>The vent tower over the shaft: a 3 × 3 chimney — purple with a red band, a red roof and a red light on top for a
    /// G.D.S. ruin (<paramref name="gds"/>), plain iron with a warm light for an authored structure — with a doorway on the side
    /// with the lowest ground in front of it, seen from afar. The walls reach down to the ground on every side, so the tower
    /// never floats on a slope.</summary>
    private int WriteAncientRuinVentTower(int x, int ground, int z, long seed, bool gds)
    {
        var planet = _world.Planet;
        var wall = AncientRuinBlock("iron_wall");
        int n = 0, top = ground + AncientRuinTowerHeight;
        for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                int from = System.Math.Min(ground + 1, _generator.SurfaceHeight(planet, x + dx, z + dz) + 1);
                for (int y = from; y <= top; y++)
                {
                    _world.SetBlock(new Vector3i(x + dx, y, z + dz), wall, !gds ? 0 : y == ground + 3 ? CityGenerator.Red : CityGenerator.Purple);
                    n++;
                }

                _world.SetBlock(new Vector3i(x + dx, top + 1, z + dz), wall, gds ? CityGenerator.Red : 0);
                n++;
            }

        _world.SetBlock(new Vector3i(x, top + 1, z), AncientRuinBlock(gds ? "light_red" : "strip_light_warm", "iron_wall"));
        var (ddx, ddz) = VentDoorSide(x, z, seed);
        for (int y = ground + 1; y <= ground + 2; y++)
        {
            _world.SetBlock(new Vector3i(x + ddx, y, z + ddz), BlockId.Air);
            _world.SetBlock(new Vector3i(x + 2 * ddx, y, z + 2 * ddz), BlockId.Air); // the step in front stays open
        }

        return n + 5;
    }

    /// <summary>The doorway side of a vent tower: the one whose ground two cells out lies lowest (ties by hash).</summary>
    private (int Dx, int Dz) VentDoorSide(int x, int z, long seed)
    {
        (int, int)[] sides = { (1, 0), (-1, 0), (0, 1), (0, -1) };
        int start = StableHashing.Index("gds_ruin:door", seed, sides.Length);
        var best = sides[start];
        int lowest = int.MaxValue;
        for (int k = 0; k < sides.Length; k++)
        {
            var (dx, dz) = sides[(start + k) % sides.Length];
            int h = _generator.SurfaceHeight(_world.Planet, x + 2 * dx, z + 2 * dz);
            if (h < lowest)
            {
                lowest = h;
                best = (dx, dz);
            }
        }

        return best;
    }
}
