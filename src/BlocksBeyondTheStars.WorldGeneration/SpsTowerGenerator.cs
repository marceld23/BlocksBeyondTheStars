// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3893: the SPS GUIDE TOWERS (<see cref="SpsTowerRules"/>, docs/developer/SPS_TOWERS.md) — the ruin of a Scout and Pioneer
/// Service guide tower, derived from its authored template (<c>data/sps_towers/*.json</c>) with the instance's seed. Pure and
/// deterministic, like <see cref="LandmarkGenerator"/>, whose build it is with three steps of its own:
/// <list type="number">
/// <item><b>The guard post.</b> A template carries its guard post (a ruined wooden hut with the capsule tank) inside its box,
/// marked by <see cref="SpsTowerRules.GuardPostMarker"/>. A tower without one (<paramref name="guardPost"/> false — about
/// six in ten, <see cref="RollsGuardPost"/>) loses every cell and marker of that box before anything else happens.</item>
/// <item><b>The ruin pass</b> — the landmark's, at the revision the record pins, with the template's profile (lattice), its
/// ruin zones and a scrap rubble palette; plants push through only on a body with air. The markers a ruin keeps
/// (<see cref="RuinMarkerRules.Kept"/> — the tower's console, lamp and tank markers are among them) come to rest on what is
/// left.</item>
/// <item><b>The dress</b> (after the pass, which switches every light off and would eat a machine): the relay console
/// (<see cref="SpsTowerRules.ConsoleBlock"/>) in its marker's cell, an emergency lamp (<see cref="SpsTowerRules.LampBlock"/>,
/// burning) in every lamp marker's cell, the capsule tank (two <see cref="SpsTowerRules.TankBlock"/> cells stacked) on its
/// marker. The markers stay in the layout: <see cref="PartsOf"/> reads them back for the tower's record.</item>
/// </list>
/// </summary>
public static class SpsTowerGenerator
{
    /// <summary>The ruin revision a fresh tower is built with (pinned with the record).</summary>
    public const int RuinRevision = LandmarkGenerator.RuinRevision;

    /// <summary>The rubble of a fallen tower: scrap, rusted panels, a little iron.</summary>
    private static readonly RuinWeightedBlock[] Rubble =
    {
        new("scrap_metal", 4), new("scrap_pile", 3), new("rusted_panel", 2), new("iron_block", 1),
    };

    /// <summary>Whether the tower built from <paramref name="seed"/> has a guard post beside its foot: the template carries
    /// one and a hash of the seed falls below <see cref="SpsTowerRules.GuardPostChance"/>.</summary>
    public static bool RollsGuardPost(StructureTemplate t, long seed)
        => SpsTowerRules.GuardPostBox(t) is not null
           && StableHashing.Unit("spstower:guardpost:" + t.Key, seed) < SpsTowerRules.GuardPostChance;

    /// <summary>A 16-hex-digit hash of everything that shapes the ruin (size, ground row, strength, zones, the body, every
    /// listed cell) — a record pins it and notices a changed template.</summary>
    public static string Fingerprint(StructureTemplate t) => GiantShipGenerator.Fingerprint(t);

    /// <summary>
    /// The ruin of <paramref name="t"/> as it stands on a planet: turned by <paramref name="turns"/> quarter turns, with or
    /// without its guard post, ruined at <paramref name="revision"/> from <paramref name="seed"/>, then dressed (console,
    /// lamps, tank). <paramref name="surfaceBlock"/> is the biome surface the overgrowth follows;
    /// <paramref name="atmosphere"/> says whether the body has air (plants only then).
    /// </summary>
    public static LandmarkLayout Build(StructureTemplate t, long seed, int turns, GameContent content, string surfaceBlock,
        bool atmosphere, bool guardPost, int revision = RuinRevision)
    {
        turns = ((turns % 4) + 4) % 4;
        bool swap = (turns & 1) == 1;
        int w = System.Math.Max(1, swap ? t.Length : t.Width);
        int h = System.Math.Max(1, t.Height);
        int l = System.Math.Max(1, swap ? t.Width : t.Length);
        var grid = new VoxelGrid(w, h, l);
        var materials = ModuleMaterials.ForSettlement("village", surfaceBlock, alien: false, content);
        LandmarkGenerator.FillBlocks(t, turns, grid, content, materials);
        var markers = LandmarkGenerator.ReadMarkers(t, turns, grid, content);
        StripGuardPost(t, turns, grid, markers, keep: guardPost);

        int groundRow = System.Math.Clamp(t.GroundRow, 0, h - 1);
        var options = new RuinOptions { Overgrowth = atmosphere, RubblePalette = Rubble };
        long ruinSeed = unchecked((long)StableHashing.Mixed("ruin:spstower:" + t.Key, seed));
        return LandmarkGenerator.Finish(t, grid, markers, groundRow, ruined: true, ruinSeed, content, surfaceBlock, revision, turns,
            options, new TowerDress(content));
    }

    /// <summary>Drops the guard post's marker — and, unless the post is kept, every cell and marker of its box.</summary>
    private static void StripGuardPost(StructureTemplate t, int turns, VoxelGrid grid, List<SettlementMarker> markers, bool keep)
    {
        var box = SpsTowerRules.GuardPostBox(t);
        markers.RemoveAll(m => m.Type == SpsTowerRules.GuardPostMarker);
        if (keep || box is not { } b)
        {
            return;
        }

        var (ax, az) = LandmarkGenerator.Turn(b.X0, b.Z0, t.Width, t.Length, turns);
        var (bx, bz) = LandmarkGenerator.Turn(b.X1, b.Z1, t.Width, t.Length, turns);
        int x0 = System.Math.Min(ax, bx), x1 = System.Math.Max(ax, bx), z0 = System.Math.Min(az, bz), z1 = System.Math.Max(az, bz);
        for (int x = x0; x <= x1; x++)
        {
            for (int y = b.Y0; y <= b.Y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    if (grid.In(x, y, z))
                    {
                        int i = grid.Index(x, y, z);
                        grid.Clear(x, y, z);
                        grid.Keep[i] = false;
                        grid.Cavity[i] = false;
                    }
                }
            }
        }

        markers.RemoveAll(m => m.LocalPos.X >= x0 && m.LocalPos.X <= x1 && m.LocalPos.Y >= b.Y0 && m.LocalPos.Y <= b.Y1
                               && m.LocalPos.Z >= z0 && m.LocalPos.Z <= z1);
    }

    /// <summary>What the stamp must remember of a built tower, in structure-local cells (<see cref="PartsOf"/>).</summary>
    public sealed class Parts
    {
        /// <summary>The relay console's cell, or null when the layout holds none.</summary>
        public Vector3i? Console { get; set; }

        /// <summary>The cells of the tower's own lamps (relay room and mast), in template order.</summary>
        public List<Vector3i> TowerLamps { get; } = new();

        /// <summary>The lower cell of the capsule tank, or null without a guard post.</summary>
        public Vector3i? Tank { get; set; }

        /// <summary>The cells of the tank's lamps.</summary>
        public List<Vector3i> TankLamps { get; } = new();

        /// <summary>Whether the guard post stands (its tank is there).</summary>
        public bool HasGuardPost => Tank is not null;
    }

    /// <summary>Reads the tower's parts back from a built layout's markers (structure-local cells, where they came to rest).</summary>
    public static Parts PartsOf(LandmarkLayout layout)
    {
        var parts = new Parts();
        foreach (var m in layout.Structure.Markers)
        {
            switch (m.Type)
            {
                case SpsTowerRules.ConsoleMarker:
                    parts.Console ??= m.LocalPos;
                    break;
                case SpsTowerRules.TankMarker:
                    parts.Tank ??= m.LocalPos;
                    break;
                case SpsTowerRules.LampMarker:
                    (SpsTowerRules.IsTankLamp(m.Data) ? parts.TankLamps : parts.TowerLamps).Add(m.LocalPos);
                    break;
            }
        }

        return parts;
    }

    /// <summary>The dress step: the blocks of the tower's own markers, placed on what the ruin pass left.</summary>
    private sealed class TowerDress : LayoutDress
    {
        private readonly ushort _console, _lamp, _tank;

        public TowerDress(GameContent content)
        {
            _console = content.GetBlock(SpsTowerRules.ConsoleBlock)?.NumericId.Value ?? 0;
            _lamp = content.GetBlock(SpsTowerRules.LampBlock)?.NumericId.Value ?? 0;
            _tank = content.GetBlock(SpsTowerRules.TankBlock)?.NumericId.Value ?? 0;
        }

        internal override void Apply(VoxelGrid grid, List<SettlementMarker> markers)
        {
            foreach (var m in markers)
            {
                var p = m.LocalPos;
                switch (m.Type)
                {
                    case SpsTowerRules.ConsoleMarker:
                        Place(grid, p.X, p.Y, p.Z, _console);
                        break;
                    case SpsTowerRules.LampMarker:
                        Place(grid, p.X, p.Y, p.Z, _lamp);
                        break;
                    case SpsTowerRules.TankMarker:
                        Place(grid, p.X, p.Y, p.Z, _tank);
                        Place(grid, p.X, p.Y + 1, p.Z, _tank);
                        break;
                }
            }
        }

        private static void Place(VoxelGrid grid, int x, int y, int z, ushort block)
        {
            if (block != 0 && grid.In(x, y, z))
            {
                grid.Set(x, y, z, block);
                grid.Cavity[grid.Index(x, y, z)] = false;
            }
        }
    }
}
