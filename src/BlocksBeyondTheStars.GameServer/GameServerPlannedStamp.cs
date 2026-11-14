// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The ground plan is a mask, not a box (#3588, #3591): a planned settlement is stamped column by column — only the columns
/// it claims are cleared, floored at their own height and given a plinth down to the ground; the corners of its box keep
/// their terrain and their trees. Its ground is reserved lot by lot and road stretch by road stretch, each at its own floor,
/// so the generator levels terraces instead of one box.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The grade radius round a reserved lot and round a reserved road stretch.</summary>
    private const int LotGradeRadius = 3;
    private const int RoadGradeRadius = 2;

    /// <summary>The deepest plinth under a planned column (the classic stamp's cap).</summary>
    private const int PlannedSkirtDepth = 48;

    private void StampPlannedBlocks(PlacedSettlement p, string surface, bool crownRing)
    {
        var s = p.Structure;
        var (top, sub) = FoundationGround(p.Origin, s.Width, s.Length, surface);
        var floorId = _content.GetBlock(top)?.NumericId ?? BlockId.Air;
        var skirtId = _content.GetBlock(sub)?.NumericId ?? floorId;
        for (int x = 0; x < s.Width; x++)
        {
            for (int z = 0; z < s.Length; z++)
            {
                int f = s.FloorAt(x, z);
                if (f >= 0)
                {
                    StampPlannedColumn(p, x, z, f, floorId, skirtId);
                }
            }
        }

        CarvePlannedVegetation(p, crownRing);
    }

    /// <summary>One claimed column: the terrain above its floor cleared, the floor laid, a plinth down to the natural ground,
    /// then the structure's own cells.</summary>
    private void StampPlannedColumn(PlacedSettlement p, int x, int z, int f, BlockId floorId, BlockId skirtId)
    {
        var s = p.Structure;
        int wx = p.Origin.X + x, wz = p.Origin.Z + z, floorY = p.GroundY + f;
        int natural = _generator.SurfaceHeight(_world.Planet, wx, wz);
        int clearTop = System.Math.Min(System.Math.Max(floorY + 3, natural + 1), floorY + 96);
        for (int y = floorY + 1; y <= clearTop; y++)
        {
            _world.SetBlock(new Vector3i(wx, y, wz), BlockId.Air);
        }

        if (!floorId.IsAir)
        {
            _world.SetBlock(new Vector3i(wx, floorY, wz), floorId);
            for (int y = floorY - 1; y >= System.Math.Max(natural + 1, floorY - PlannedSkirtDepth); y--)
            {
                _world.SetBlock(new Vector3i(wx, y, wz), skirtId);
            }
        }

        for (int y = 0; y < s.Height; y++)
        {
            ushort b = s.Get(x, y, z);
            if (b != 0)
            {
                var (tint, glow) = s.GetModifier(x, y, z);
                _world.SetBlock(new Vector3i(wx, p.GroundY + y, wz), new BlockId(b), tint, glow, s.GetShape(x, y, z));
            }
        }
    }

    /// <summary>The vegetation carve of a planned settlement (#1659 by column): tree blocks out of every claimed column up to
    /// tree height and out of the crown margin round the claimed columns — left alone where a player has built.</summary>
    private void CarvePlannedVegetation(PlacedSettlement p, bool crownRing)
    {
        var s = p.Structure;
        int margin = crownRing ? SettlementCrownMargin : 0;
        bool someonesBuild = crownRing && FootprintHasPlayerEdits(p.Origin.X - margin, p.Origin.Z - margin, p.GroundY,
            s.Width + (2 * margin), SettlementVegetationRise, s.Length + (2 * margin));
        var vegetation = SettlementVegetationIds;
        for (int x = -margin; x < s.Width + margin; x++)
        {
            for (int z = -margin; z < s.Length + margin; z++)
            {
                int f = s.FloorAt(x, z);
                if (f < 0 && (someonesBuild || !NearClaimed(s, x, z, margin)))
                {
                    continue;
                }

                int baseY = p.GroundY + System.Math.Max(f, 0);
                for (int y = baseY - (f < 0 ? margin : 0); y <= baseY + SettlementVegetationRise; y++)
                {
                    var cell = new Vector3i(p.Origin.X + x, y, p.Origin.Z + z);
                    if (WithinBuildHeight(y) && vegetation.Contains(_world.GetBlock(cell).Value))
                    {
                        _world.SetBlock(cell, BlockId.Air);
                    }
                }
            }
        }
    }

    private static bool NearClaimed(SettlementStructure s, int x, int z, int margin)
    {
        for (int dx = -margin; dx <= margin; dx++)
        {
            for (int dz = -margin; dz <= margin; dz++)
            {
                if (s.FloorAt(x + dx, z + dz) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Reserves a fresh planned settlement's ground (#3591): one site per lot (with its yard, at the lot's floor) and one per
    /// road stretch (same-height runs of road, footpath, plaza and green merged into rectangles) — each with a small grade, the
    /// cut and fill limits of the kind's grade. Same gates as <see cref="ReserveSiteGround"/>.
    /// </summary>
    private void ReservePlanGround(string kind, SettlementPlan plan, Vector3i origin, int groundY, string seat)
    {
        if (!_siteGroundOpen || !GeographyWorld || !_worlds.Active.VirginAtLoad || seat is not ("flat" or "slope" or "shelf"))
        {
            return;
        }

        var grade = StructureType(kind).Grade;
        if (!grade.Reserves)
        {
            return;
        }

        var sites = new List<SiteFlat>();
        foreach (var lot in plan.Lots)
        {
            sites.Add(new SiteFlat(origin.X + lot.X - 1, origin.Z + lot.Z - 1, lot.W + 2, lot.L + 2, groundY + lot.Y, LotGradeRadius,
                grade.MaxCut, grade.MaxFill));
        }

        foreach (var (x, z, w, l, y) in RoadStretches(plan))
        {
            sites.Add(new SiteFlat(origin.X + x, origin.Z + z, w, l, groundY + y, RoadGradeRadius, grade.MaxCut, grade.MaxFill));
        }

        int r = LotGradeRadius + 1;
        AddSiteFlats(sites, origin.X - r, origin.Z - r, origin.X + plan.Width + r, origin.Z + plan.Length + r);
    }

    /// <summary>The plan's open ground (roads, footpaths, the plaza, greens) as rectangles of one height each, merged greedily
    /// row by row.</summary>
    internal static List<(int X, int Z, int W, int L, int Y)> RoadStretches(SettlementPlan plan)
    {
        var rects = new List<(int X, int Z, int W, int L, int Y)>();
        var done = new bool[plan.Width * plan.Length];
        bool Open(int x, int z) => plan.InBox(x, z) && !done[(x * plan.Length) + z]
            && (SettlementPlan.IsWalkable(plan.KindAt(x, z)) || plan.KindAt(x, z) == SettlementPlan.Green);
        for (int z = 0; z < plan.Length; z++)
        {
            for (int x = 0; x < plan.Width; x++)
            {
                if (!Open(x, z))
                {
                    continue;
                }

                int y = plan.HeightAt(x, z), w = 1, l = 1;
                while (Open(x + w, z) && plan.HeightAt(x + w, z) == y) w++;
                while (RowOpen(x, z + l, w, y)) l++;
                for (int dx = 0; dx < w; dx++)
                    for (int dz = 0; dz < l; dz++)
                        done[((x + dx) * plan.Length) + z + dz] = true;
                rects.Add((x, z, w, l, y));
            }
        }

        return rects;

        bool RowOpen(int x0, int z0, int w0, int y0)
        {
            for (int dx = 0; dx < w0; dx++)
            {
                if (!Open(x0 + dx, z0) || plan.HeightAt(x0 + dx, z0) != y0)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
