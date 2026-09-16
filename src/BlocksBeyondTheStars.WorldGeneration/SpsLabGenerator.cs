// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Builds an abandoned <b>SPS research station</b> (2026-09, Titas) deterministically from a seed: two or three rusted
/// modules — the lab with its data terminal, a store room, sometimes a generator shed — beside an old ship pad and a
/// helicopter pad with its "H". Holes in the walls and roofs, scrap on the floor. Reuses the
/// <see cref="SettlementStructure"/> container so the settlement placement/stamp pipeline applies unchanged.
/// Markers: <c>sps_cache</c> (salvage) and <c>data_terminal</c> (the lore terminal). No NPCs — only machines.
/// </summary>
public static class SpsLabGenerator
{
    /// <summary>The square footprint (modules, yard and both pads).</summary>
    public const int Size = 33;

    /// <summary>The structure height (floor row + four wall rows + the roof).</summary>
    public const int Height = 7;

    /// <summary>The structure tier the stamper and the server see.</summary>
    public const string Tier = "sps_lab";

    public static SettlementStructure Generate(long seed, string groundBlock, GameContent content)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        int w = Size, h = Height, l = Size;
        var blocks = new ushort[w * h * l];
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort ground = B(groundBlock);
        ushort stone = B("stone", ground);
        ushort wall = B("rusted_panel", stone);
        ushort floor = B("steel_floor", stone);
        ushort glass = B("glass", 0);
        ushort console = B("lab_panel", wall);
        ushort terminal = B("factory_terminal", console);
        ushort crate = B("crate", 0);
        ushort pad = B("concrete", stone);
        ushort lineWhite = B("light_white", pad);
        ushort lineRed = B("light_red", pad);
        ushort scrap = B("scrap_metal", wall);

        void Set(int x, int y, int z, ushort id)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = id;
            }
        }

        ushort Get(int x, int y, int z) => blocks[(x * h + y) * l + z];

        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < l; z++)
            {
                Set(x, 0, z, ground);
            }
        }

        // A rusted module: a steel floor, four wall rows, a roof; a two-wide doorway and some holes. Returns nothing —
        // the interior dressing is placed by the caller.
        void Module(int x0, int z0, int sx, int sz, char doorSide)
        {
            for (int x = x0; x < x0 + sx; x++)
            {
                for (int z = z0; z < z0 + sz; z++)
                {
                    bool edge = x == x0 || z == z0 || x == x0 + sx - 1 || z == z0 + sz - 1;
                    Set(x, 0, z, floor);
                    for (int y = 1; y <= 4; y++)
                    {
                        if (edge)
                        {
                            // Weathered: one wall cell in twelve rusted through, one in twenty lies as scrap.
                            double r = rng.NextDouble();
                            Set(x, y, z, r < 0.08 && y >= 2 ? (ushort)0 : r < 0.13 ? scrap : wall);
                        }
                    }

                    Set(x, 5, z, rng.NextDouble() < 0.1 && !edge ? (ushort)0 : wall); // roof, with a few holes
                }
            }

            int midX = x0 + sx / 2, midZ = z0 + sz / 2;
            for (int y = 1; y <= 3; y++)
            {
                switch (doorSide)
                {
                    case 'S':
                        Set(midX - 1, y, z0 + sz - 1, 0);
                        Set(midX, y, z0 + sz - 1, 0);
                        break;
                    case 'W':
                        Set(x0, y, midZ - 1, 0);
                        Set(x0, y, midZ, 0);
                        break;
                }
            }

            if (glass != 0)
            {
                // Windows in the long walls (away from the doorway).
                for (int x = x0 + 2; x < x0 + sx - 2; x += 3)
                {
                    Set(x, 2, z0, glass);
                }
            }
        }

        // The lab: consoles along the back wall, the data terminal in the middle, a crate of salvage by the door.
        Module(2, 2, 13, 9, 'S');
        for (int x = 4; x <= 12; x++)
        {
            Set(x, 1, 3, console);
        }

        Set(8, 1, 3, terminal);
        markers.Add(new SettlementMarker("data_terminal", new Vector3i(8, 2, 3)));
        if (crate != 0)
        {
            Set(4, 1, 8, crate);
        }

        markers.Add(new SettlementMarker("sps_cache", new Vector3i(4, 2, 8)));

        // The store room: crates and scrap.
        Module(18, 2, 9, 9, 'S');
        if (crate != 0)
        {
            Set(20, 1, 4, crate);
            Set(24, 1, 4, crate);
            Set(24, 2, 4, crate);
        }

        markers.Add(new SettlementMarker("sps_cache", new Vector3i(22, 1, 5)));
        Set(20, 1, 8, scrap);

        // Sometimes a generator shed beside it.
        int modules = 2;
        if (rng.NextDouble() < 0.5)
        {
            modules = 3;
            Module(20, 13, 9, 7, 'W');
            for (int z = 14; z <= 18; z++)
            {
                Set(27, 1, z, console);
            }

            markers.Add(new SettlementMarker("sps_cache", new Vector3i(24, 1, 16)));
        }

        // The old ship pad: a concrete apron with red corner lamps and a white centre line.
        for (int x = 2; x <= 14; x++)
        {
            for (int z = 17; z <= 30; z++)
            {
                Set(x, 0, z, pad);
            }
        }

        foreach (var (cx, cz) in new[] { (2, 17), (14, 17), (2, 30), (14, 30) })
        {
            Set(cx, 0, cz, lineRed);
        }

        for (int z = 19; z <= 28; z += 2)
        {
            Set(8, 0, z, lineWhite);
        }

        // The helicopter pad: a concrete square with its "H".
        for (int x = 21; x <= 29; x++)
        {
            for (int z = 22; z <= 30; z++)
            {
                Set(x, 0, z, pad);
            }
        }

        for (int z = 24; z <= 28; z++)
        {
            Set(23, 0, z, lineWhite);
            Set(27, 0, z, lineWhite);
        }

        for (int x = 24; x <= 26; x++)
        {
            Set(x, 0, 26, lineWhite);
        }

        // Scrap strewn over the yard.
        for (int i = 0; i < 6; i++)
        {
            int px = 1 + rng.Next(w - 2), pz = 12 + rng.Next(4);
            if (Get(px, 1, pz) == 0)
            {
                Set(px, 1, pz, scrap);
            }
        }

        return new SettlementStructure(w, h, l, Tier, ruined: true, inhabitant: string.Empty,
            blocks, markers, buildingCount: modules);
    }
}
