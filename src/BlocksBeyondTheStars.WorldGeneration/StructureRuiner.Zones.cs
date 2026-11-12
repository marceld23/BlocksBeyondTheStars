// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>One ruin zone in grid coordinates (inclusive corners, already turned with the structure): a box that decays with a
/// profile and a strength of its own (<see cref="Shared.Definitions.StructureTemplate.RuinZones"/>).</summary>
internal sealed class RuinZoneBox
{
    public RuinZoneBox(int x0, int y0, int z0, int x1, int y1, int z1, string profile, double strength)
    {
        (X0, X1) = (System.Math.Min(x0, x1), System.Math.Max(x0, x1));
        (Y0, Y1) = (System.Math.Min(y0, y1), System.Math.Max(y0, y1));
        (Z0, Z1) = (System.Math.Min(z0, z1), System.Math.Max(z0, z1));
        Profile = profile;
        Strength = strength;
    }

    public int X0 { get; }

    public int Y0 { get; }

    public int Z0 { get; }

    public int X1 { get; }

    public int Y1 { get; }

    public int Z1 { get; }

    public string Profile { get; }

    public double Strength { get; }
}

/// <summary>
/// The school club's landmarks (terrain generation 39): the RUIN ZONES — one structure, several ways of falling apart (the
/// lattice tower on the masonry pyramid). The pass with zones (revision 2 adds the lattice breaks to a lattice zone's decay):
/// <list type="number">
/// <item>Every zone, in order, runs the decay steps (crumble, erosion, collapse spots, openings, weathering, scorch, cave-ins)
/// with its own profile and strength on a window of the grid that is exactly its box — the heights, the patches and the spots
/// are the zone's own, as if it were the whole structure — with every cell of an earlier zone kept. Its seed is the instance
/// seed mixed with the zone's index, so no two zones share a pattern.</item>
/// <item>The cells of no zone run the decay steps over the whole grid with the template's profile and strength, every zone's
/// cell kept.</item>
/// <item>One shared settle, rubble and overgrowth over the whole grid, with only the author's keep cells (what fell in a zone
/// counts for the rubble of its column).</item>
/// </list>
/// A template without zones never comes here, so its output is byte-identical to the pass as it shipped.
/// </summary>
internal sealed partial class StructureRuiner
{
    /// <summary>Spreads the zones' seeds: zone k decays with seed + (k + 1) × this odd constant.</summary>
    private const long ZoneSeedStep = unchecked((long)0x9E3779B97F4A7C15UL);

    /// <summary>
    /// Runs revision <paramref name="revision"/> with ruin zones (see the class summary) over <paramref name="grid"/> in place.
    /// Without zones this is exactly
    /// <see cref="Run(VoxelGrid, GameContent, long, string, double, int, IReadOnlyList{Vector3i}, string, int, RuinOptions?)"/>.
    /// </summary>
    public static void RunZoned(VoxelGrid grid, GameContent content, long seed, string profile, double strength, int groundRow,
        IReadOnlyList<Vector3i> markerCells, string surfaceBlock, int revision, RuinOptions? options, IReadOnlyList<RuinZoneBox> zones)
    {
        if (zones.Count == 0)
        {
            Run(grid, content, seed, profile, strength, groundRow, markerCells, surfaceBlock, revision, options);
            return;
        }

        options ??= new RuinOptions();
        int[] owner = ZoneOwners(grid, zones);
        var authorKeep = (BitArray)grid.Keep.Clone();
        var whole = new StructureRuiner(grid, content, seed, profile, strength, groundRow, markerCells, surfaceBlock, options, revision);
        for (int k = 0; k < zones.Count; k++)
        {
            DecayZone(grid, owner, k, zones[k], whole, content, unchecked(seed + ((k + 1) * ZoneSeedStep)), groundRow, markerCells,
                surfaceBlock, options);
        }

        bool rest = false;
        for (int i = 0; i < owner.Length; i++)
        {
            if (owner[i] >= 0)
            {
                grid.Keep[i] = true; // the zones are done: the rest pass leaves them as they are
            }
            else
            {
                rest = true;
            }
        }

        if (rest)
        {
            whole.Decay();
        }

        RestoreKeep(grid, authorKeep);
        whole.Finish();
    }

    /// <summary>The zone index of every cell (the first zone whose box holds it), -1 for a cell of no zone.</summary>
    private static int[] ZoneOwners(VoxelGrid grid, IReadOnlyList<RuinZoneBox> zones)
    {
        var owner = new int[grid.Blocks.Length];
        for (int i = 0; i < owner.Length; i++)
        {
            owner[i] = -1;
        }

        for (int k = zones.Count - 1; k >= 0; k--) // backwards, so the first zone overwrites a later one where they overlap
        {
            var z = zones[k];
            for (int x = System.Math.Max(0, z.X0); x <= System.Math.Min(grid.W - 1, z.X1); x++)
            {
                for (int y = System.Math.Max(0, z.Y0); y <= System.Math.Min(grid.H - 1, z.Y1); y++)
                {
                    for (int zz = System.Math.Max(0, z.Z0); zz <= System.Math.Min(grid.L - 1, z.Z1); zz++)
                    {
                        owner[grid.Index(x, y, zz)] = k;
                    }
                }
            }
        }

        return owner;
    }

    private static void RestoreKeep(VoxelGrid grid, BitArray authorKeep)
    {
        for (int i = 0; i < authorKeep.Length; i++)
        {
            grid.Keep[i] = authorKeep[i];
        }
    }

    /// <summary>Zone <paramref name="k"/>'s decay on a window of the grid that is exactly its box: copied out (cells of other
    /// zones kept), decayed, copied back; what fell is added to the whole pass's columns for the shared rubble.</summary>
    private static void DecayZone(VoxelGrid grid, int[] owner, int k, RuinZoneBox zone, StructureRuiner whole, GameContent content,
        long seed, int groundRow, IReadOnlyList<Vector3i> markerCells, string surfaceBlock, RuinOptions options)
    {
        int x0 = System.Math.Max(0, zone.X0), y0 = System.Math.Max(0, zone.Y0), z0 = System.Math.Max(0, zone.Z0);
        int x1 = System.Math.Min(grid.W - 1, zone.X1), y1 = System.Math.Min(grid.H - 1, zone.Y1), z1 = System.Math.Min(grid.L - 1, zone.Z1);
        if (x1 < x0 || y1 < y0 || z1 < z0)
        {
            return; // the zone lies outside the (built) structure
        }

        var sub = new VoxelGrid(x1 - x0 + 1, y1 - y0 + 1, z1 - z0 + 1);
        CopyWindow(grid, sub, owner, k, x0, y0, z0, toWindow: true);
        var markers = new List<Vector3i>();
        foreach (var m in markerCells)
        {
            // a marker just below the window still keeps the cell above it free
            if (m.X >= x0 && m.X <= x1 && m.Z >= z0 && m.Z <= z1 && m.Y >= y0 - 1 && m.Y <= y1)
            {
                markers.Add(new Vector3i(m.X - x0, m.Y - y0, m.Z - z0));
            }
        }

        var r = new StructureRuiner(sub, content, seed, zone.Profile, zone.Strength, groundRow - y0, markers, surfaceBlock,
            ZoneOptions(options, x0, y0, z0), whole._revision);
        r.Decay();
        CopyWindow(grid, sub, owner, k, x0, y0, z0, toWindow: false);
        for (int x = 0; x < sub.W; x++)
        {
            for (int z = 0; z < sub.L; z++)
            {
                int c = r.Column(x, z), wc = whole.Column(x + x0, z + z0);
                whole._fallen[wc] += r._fallen[c];
                if (r._fallenBlock[c] != 0)
                {
                    whole._fallenBlock[wc] = r._fallenBlock[c];
                }
            }
        }
    }

    /// <summary>Copies the zone's window between the grid and its own grid — blocks, tint, glow, shape and cavity. Out: the
    /// author's keep cells and every cell of another zone are kept. Back: only the zone's own cells are written.</summary>
    private static void CopyWindow(VoxelGrid grid, VoxelGrid sub, int[] owner, int k, int x0, int y0, int z0, bool toWindow)
    {
        for (int x = 0; x < sub.W; x++)
        {
            for (int y = 0; y < sub.H; y++)
            {
                for (int z = 0; z < sub.L; z++)
                {
                    int gi = grid.Index(x + x0, y + y0, z + z0), si = sub.Index(x, y, z);
                    var (from, to, fi, ti) = toWindow ? (grid, sub, gi, si) : (sub, grid, si, gi);
                    if (!toWindow && owner[gi] != k)
                    {
                        continue;
                    }

                    var (tint, glow) = from.Mods.TryGetValue(fi, out var mod) ? mod : (0, 0);
                    int shape = from.Shapes.TryGetValue(fi, out int s) ? s : 0;
                    int tx = toWindow ? x : x + x0, ty = toWindow ? y : y + y0, tz = toWindow ? z : z + z0;
                    to.Set(tx, ty, tz, from.Blocks[fi], tint, glow, shape);
                    to.Cavity[ti] = from.Cavity[fi];
                    if (toWindow)
                    {
                        sub.Keep[si] = grid.Keep[gi] || owner[gi] != k;
                    }
                }
            }
        }
    }

    /// <summary>The caller's options as a zone's window sees them: the strength map and the protected rows moved by the
    /// window's corner; everything else as given.</summary>
    private static RuinOptions ZoneOptions(RuinOptions o, int x0, int y0, int z0)
    {
        var strengthAt = o.StrengthAt;
        return new RuinOptions
        {
            Overgrowth = o.Overgrowth,
            OvergrowthBlock = o.OvergrowthBlock,
            RubblePalette = o.RubblePalette,
            ErodeShaped = o.ErodeShaped,
            StrengthAt = strengthAt is null ? null : (x, z) => strengthAt(x + x0, z + z0),
            ProtectRowsUpTo = o.ProtectRowsUpTo - y0 >= 0 ? o.ProtectRowsUpTo - y0 : -1,
            LightOffBlock = o.LightOffBlock,
            Substitutions = o.Substitutions,
            Scorch = o.Scorch,
        };
    }
}
