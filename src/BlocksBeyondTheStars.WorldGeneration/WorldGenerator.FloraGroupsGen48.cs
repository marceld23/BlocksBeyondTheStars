// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Terrain generation 48 — the flora groups (#3931, the scatter rules), partial of
/// <see cref="WorldGenerator"/>. The small surface plants (one flora block in the air cell above the ground) stand in groups
/// with open ground between them instead of an even sprinkle:
/// <list type="bullet">
/// <item>A jittered grid of group centres, one per cell of about <see cref="FloraGroupCell"/> blocks (the cell width is the
/// wrap period divided by a whole number of cells, so the grid closes seamlessly at both seams). A cell holds a group with a
/// chance between <see cref="FloraGroupMinChance"/> and <see cref="FloraGroupMaxChance"/> that follows a mid-scale clearing
/// mask (wavelength <see cref="FloraGroupClearingScale"/>): few groups in a clearing, many elsewhere.</item>
/// <item>A group is a disc of radius <see cref="FloraGroupMinRadius"/>..+<see cref="FloraGroupRadiusSpan"/> with a ragged
/// edge (± <see cref="FloraGroupRagged"/> per column), wrapped in a RING out to <see cref="FloraGroupRingScale"/> × that
/// radius. Every column is in one of three zones: inside a group (inside wins over a neighbour's ring), in the ring of the
/// nearest group, or on the open ground.</item>
/// <item>Inside, the density is the ungrouped density × <see cref="FloraGroupInsideFactor"/>; on the ring and the open ground ×
/// <see cref="FloraGroupOutsideFactor"/> — a stray plant now and then. The two factors average one over a large area
/// (<see cref="FloraGroupCoverage"/> is the measured share of columns inside a group). On lush ground, what the 0.95 cap cuts
/// off inside a group goes into the ring (spread by <see cref="FloraGroupCoverage"/> / <see cref="FloraGroupRingCoverage"/>),
/// so the clump grows instead of the meadow between the clumps filling up; only what the cap then cuts off on the ring goes
/// to the open ground. The plants per area stay what they were.</item>
/// <item><see cref="FloraGroupSpeciesShare"/> of a group's columns take the group's species, picked from the column's own host
/// pool with the same theme weights — a ring column too, where its ring carries cut-off growth; the other columns pick as
/// before.</item>
/// </list>
/// Stateless and deterministic: hashes of the seed, the wrapped column and the wrapped cell, plus the torus noise. Nothing here
/// runs below <see cref="WorldDescription.GroupedFloraGeneration"/>, so every older world keeps its plants bit for bit.
/// </summary>
public sealed partial class WorldGenerator
{
    /// <summary>The nominal edge of a group cell in blocks (one group centre per cell at most).</summary>
    internal const int FloraGroupCell = 8;

    /// <summary>The wavelength of the clearing mask that decides how many cells hold a group.</summary>
    internal const double FloraGroupClearingScale = 36.0;

    /// <summary>The group chance of a cell in the deepest clearing and in the thickest stand.</summary>
    internal const double FloraGroupMinChance = 0.08, FloraGroupMaxChance = 0.92;

    /// <summary>The clearing-mask values mapped to the minimum and the maximum group chance (linear between, clamped).</summary>
    private const double FloraGroupClearingLow = 0.36, FloraGroupClearingHigh = 0.64;

    /// <summary>A group's radius: <see cref="FloraGroupMinRadius"/> plus up to <see cref="FloraGroupRadiusSpan"/> blocks.</summary>
    internal const double FloraGroupMinRadius = 1.8, FloraGroupRadiusSpan = 2.0;

    /// <summary>The ragged edge: each column moves the radius by up to this many blocks either way.</summary>
    internal const double FloraGroupRagged = 0.6;

    /// <summary>The ring around a group reaches out to this multiple of the group's (ragged) radius.</summary>
    internal const double FloraGroupRingScale = 1.5;

    /// <summary>The farthest any group's ring reaches from its centre: (the widest radius plus the ragged edge) × the ring scale
    /// = 4.4 × 1.5 = 6.6 blocks.</summary>
    private const double FloraGroupReach = (FloraGroupMinRadius + FloraGroupRadiusSpan + FloraGroupRagged) * FloraGroupRingScale;

    /// <summary>A centre sits in the inner part of its cell: from this fraction of the cell, over the span fraction.</summary>
    private const double FloraGroupJitterMin = 0.2, FloraGroupJitterSpan = 0.6;

    /// <summary>The measured share of surface columns that lie inside a group (FloraGroupsTests checks it over whole worlds),
    /// which balances the inside and outside factors so the group factor averages one.</summary>
    internal const double FloraGroupCoverage = 0.199;

    /// <summary>The measured share of surface columns that lie in a group's ring and inside no group (FloraGroupsTests checks
    /// it over whole worlds) — the ground the cut-off growth of the groups is spread over.</summary>
    internal const double FloraGroupRingCoverage = 0.202;

    /// <summary>The density factor on the ring and the open ground: a stray plant now and then.</summary>
    internal const double FloraGroupOutsideFactor = 0.15;

    /// <summary>The density factor inside a group, chosen so coverage × inside + (1 − coverage) × outside = 1.</summary>
    internal const double FloraGroupInsideFactor =
        (1.0 - (1.0 - FloraGroupCoverage) * FloraGroupOutsideFactor) / FloraGroupCoverage;

    /// <summary>The share of a group's columns that grow the group's species (the rest pick as on an ungrouped world).</summary>
    internal const double FloraGroupSpeciesShare = 0.7;

    /// <summary>The three zones of a column: the open ground, a group's ring (and no group), inside a group.</summary>
    internal const int FloraZoneOpen = 0, FloraZoneRing = 1, FloraZoneInside = 2;

    private const long FloraGroupJitterXSalt = 0x6E0A01, FloraGroupJitterZSalt = 0x6E0A02, FloraGroupRadiusSalt = 0x6E0A03;
    private const long FloraGroupPresenceSalt = 0x6E0A04, FloraGroupClearingSalt = 0x6E0A05, FloraGroupEdgeSalt = 0x6E0A06;
    private const long FloraGroupSpeciesSalt = 0x6E0A07, FloraGroupSpeciesRollSalt = 0x6E0A08;

    /// <summary>True when this world's surface plants stand in groups (from generation 48 on).</summary>
    private bool FloraGroupsActive => _terrainGeneration >= WorldDescription.GroupedFloraGeneration;

    /// <summary>The surface plant species of a column and, in <paramref name="density"/>, its planting chance (capped, before the
    /// caller's beach and cold factors) — for the ground, an island top and a further band top alike. Below generation 48 this is
    /// exactly <see cref="FloraForSurface"/> and <see cref="LocalFloraDensity"/>.</summary>
    private BlockId GroupedSurfaceFlora(PlanetType planet, BiomeResolved biome, double baseDensity, long seed, int worldX,
        int worldZ, BlockId? surfaceOverride, out double density)
    {
        if (!FloraGroupsActive)
        {
            density = LocalFloraDensity(planet, biome, baseDensity, seed, worldX, worldZ);
            return FloraForSurface(planet, biome, seed, worldX, worldZ, surfaceOverride);
        }

        int zone = FloraZoneAt(seed, worldX, worldZ, out int groupX, out int groupZ);
        double raw = RawFloraDensity(planet, biome, baseDensity, seed, worldX, worldZ);
        density = GroupedFloraDensity(raw, zone);
        // A ring column belongs to its group only where the ring carries the group's cut-off growth.
        bool member = zone == FloraZoneInside || (zone == FloraZoneRing && raw * FloraGroupInsideFactor > FloraDensityCap);
        if (member && Noise.Value01(seed + FloraGroupSpeciesRollSalt, WorldConstants.WrapX(worldX, _circumference), 19,
                Wz(worldZ)) < FloraGroupSpeciesShare)
        {
            ResolveFlora(planet);
            var host = surfaceOverride ?? biome.Surface; // the column's own host: never a plant on ground it cannot grow on
            if (_floraBySurface.TryGetValue(host.Value, out var pool) && pool.Length > 0)
            {
                return PickSurfaceSpecies(pool, biome.Theme, Noise.Value01(seed + FloraGroupSpeciesSalt, groupX, 23, groupZ));
            }
        }

        return FloraForSurface(planet, biome, seed, worldX, worldZ, surfaceOverride);
    }

    /// <summary>The grouped planting chance of a column in <paramref name="zone"/> from the ungrouped, uncapped density
    /// <paramref name="raw"/>, each zone capped at 0.95: inside = raw × inside factor; the ring = raw × outside factor plus what
    /// the cap cut off inside (E, spread by coverage / ring coverage); the open ground = raw × outside factor plus what the cap
    /// then cut off on the ring (E2, spread by ring coverage / open share). Over a large area the mean is the ungrouped capped
    /// density. The excess is reckoned from the column's own density — the richness mask changes slowly across a group.</summary>
    internal static double GroupedFloraDensity(double raw, int zone)
    {
        double inside = raw * FloraGroupInsideFactor;
        if (zone == FloraZoneInside)
        {
            return inside > FloraDensityCap ? FloraDensityCap : inside;
        }

        double outside = raw * FloraGroupOutsideFactor;
        double excess = inside > FloraDensityCap ? inside - FloraDensityCap : 0.0;
        double ring = outside + excess * (FloraGroupCoverage / FloraGroupRingCoverage);
        if (zone == FloraZoneRing)
        {
            return ring > FloraDensityCap ? FloraDensityCap : ring;
        }

        double excess2 = ring > FloraDensityCap ? ring - FloraDensityCap : 0.0;
        double open = outside + excess2 * (FloraGroupRingCoverage / (1.0 - FloraGroupCoverage - FloraGroupRingCoverage));
        return open > FloraDensityCap ? FloraDensityCap : open;
    }

    /// <summary>The column's zone and, inside or on a ring, that group's cell: inside the nearest group by its own radius
    /// (q = d² / r² ≤ 1) wins; else the ring of the nearest group (1 &lt; q ≤ ring scale²). Looks at the column's cell and its
    /// eight neighbours only: a ring reaches at most <see cref="FloraGroupReach"/> = 6.6 blocks from its centre, which sits at
    /// least 1.6 blocks inside its cell, so a centre two cells away is at least 1.2 cell widths ≥ 9.6 blocks off and can never
    /// reach the column. Allocation-free.</summary>
    private int FloraZoneAt(long seed, int worldX, int worldZ, out int groupX, out int groupZ)
    {
        groupX = groupZ = 0;
        int lat = LatPeriod;
        int cellsX = System.Math.Max(1, _circumference / FloraGroupCell);
        int cellsZ = System.Math.Max(1, lat / FloraGroupCell);
        double cellW = (double)_circumference / cellsX, cellH = (double)lat / cellsZ;
        int wx = WorldConstants.WrapX(worldX, _circumference);
        int wzc = Wz(worldZ);
        int wz = wzc + lat / 2; // [0, lat): the canonical latitude shifted to start at the seam
        int cx = (int)((long)wx * cellsX / _circumference);
        int cz = (int)((long)wz * cellsZ / lat);
        double edge = (Noise.Value01(seed + FloraGroupEdgeSalt, wx, 17, wzc) - 0.5) * 2.0 * FloraGroupRagged;
        double bestInside = double.MaxValue, bestRing = double.MaxValue;
        for (int di = -1; di <= 1; di++)
        {
            for (int dj = -1; dj <= 1; dj++)
            {
                int gi = ((cx + di) % cellsX + cellsX) % cellsX;
                int gj = ((cz + dj) % cellsZ + cellsZ) % cellsZ;
                double q = FloraGroupQ(seed, gi, gj, wx, wz, edge, cellW, cellH);
                bool improvesInside = q <= 1.0 && q < bestInside;
                bool improvesRing = q > 1.0 && q <= FloraGroupRingScale * FloraGroupRingScale && q < bestRing
                    && bestInside == double.MaxValue;
                if ((!improvesInside && !improvesRing) || !FloraGroupCellHolds(seed, gi, gj, cellW, cellH, lat))
                {
                    continue; // the clearing mask runs only for a cell that would change the answer
                }

                if (improvesInside)
                {
                    bestInside = q;
                }
                else
                {
                    bestRing = q;
                }

                groupX = gi;
                groupZ = gj;
            }
        }

        return bestInside != double.MaxValue ? FloraZoneInside : bestRing != double.MaxValue ? FloraZoneRing : FloraZoneOpen;
    }

    /// <summary>The column's relative squared distance q = d² / r² to the centre of group cell (gi, gj), with the column's
    /// ragged edge applied to the radius; <see cref="double.MaxValue"/> beyond the widest ring (no radius hash then).</summary>
    private double FloraGroupQ(long seed, int gi, int gj, int wx, int wz, double edge, double cellW, double cellH)
    {
        double gx = (gi + FloraGroupJitterMin + FloraGroupJitterSpan * Noise.Value01(seed + FloraGroupJitterXSalt, gi, 0, gj)) * cellW;
        double gz = (gj + FloraGroupJitterMin + FloraGroupJitterSpan * Noise.Value01(seed + FloraGroupJitterZSalt, gi, 0, gj)) * cellH;
        double dx = WorldConstants.WrapDeltaX(gx - wx, _circumference);
        double dz = WorldConstants.WrapDeltaZ(gz - wz, _circumference);
        double d2 = dx * dx + dz * dz;
        if (d2 > FloraGroupReach * FloraGroupReach)
        {
            return double.MaxValue;
        }

        double r = FloraGroupMinRadius + FloraGroupRadiusSpan * Noise.Value01(seed + FloraGroupRadiusSalt, gi, 0, gj) + edge;
        return d2 / (r * r);
    }

    /// <summary>Whether a group cell holds a group: its chance follows the clearing mask at the cell's corner.</summary>
    private bool FloraGroupCellHolds(long seed, int gi, int gj, double cellW, double cellH, int lat)
    {
        double m = FbmT(seed + FloraGroupClearingSalt, gi * cellW, gj * cellH - lat / 2, FloraGroupClearingScale, octaves: 2);
        double t = (m - FloraGroupClearingLow) / (FloraGroupClearingHigh - FloraGroupClearingLow);
        t = t < 0 ? 0 : t > 1 ? 1 : t;
        double chance = FloraGroupMinChance + (FloraGroupMaxChance - FloraGroupMinChance) * t;
        return Noise.Value01(seed + FloraGroupPresenceSalt, gi, 0, gj) < chance;
    }

    /// <summary>Test seam: the group cell the column lies inside, or null outside every group — a ring counts as outside (on any
    /// generation — the rule itself, not its gate).</summary>
    internal (int X, int Z)? FloraGroupAtForTest(long seed, int worldX, int worldZ)
        => FloraZoneAt(seed, worldX, worldZ, out int gx, out int gz) == FloraZoneInside ? (gx, gz) : null;

    /// <summary>Test seam: the column's zone (<see cref="FloraZoneOpen"/>, <see cref="FloraZoneRing"/> or
    /// <see cref="FloraZoneInside"/>).</summary>
    internal int FloraZoneForTest(long seed, int worldX, int worldZ) => FloraZoneAt(seed, worldX, worldZ, out _, out _);

    /// <summary>Test seam: the uncapped group factor of a column where no cap bites (<see cref="FloraGroupInsideFactor"/> inside a
    /// group, <see cref="FloraGroupOutsideFactor"/> on a ring and the open ground).</summary>
    internal double FloraGroupFactorForTest(long seed, int worldX, int worldZ)
        => FloraZoneForTest(seed, worldX, worldZ) == FloraZoneInside ? FloraGroupInsideFactor : FloraGroupOutsideFactor;
}
