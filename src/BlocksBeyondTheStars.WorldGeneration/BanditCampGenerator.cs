// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Builds a <b>bandit camp</b> deterministically from a seed: a scruffy outpost far smaller than a
/// village — 3–4 log huts around a fire pit, a palisade ring with gate gaps, crates and a loot stash.
/// Reuses the <see cref="SettlementStructure"/> container so the settlement placement/stamp pipeline
/// applies unchanged. Markers: <c>bandit</c> (guard spawns) and <c>loot</c> (the stash — the raid
/// reward). No vendor, no mission board, no doors — bandits don't run shops.
/// </summary>
public static class BanditCampGenerator
{
    private const int Size = 23; // square footprint (palisade ring included)
    private const int Height = 7;
    private const int Hut = 6;   // hut footprint (Hut×Hut, hollow)

    public static SettlementStructure Generate(long seed, string biomeSurfaceBlock, GameContent content)
    {
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32))));
        int w = Size, h = Height, l = Size;
        var blocks = new ushort[w * h * l];
        var markers = new List<SettlementMarker>();

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort ground = B(biomeSurfaceBlock);
        ushort log = B("wood_log", ground);
        ushort stone = B("stone", ground);
        ushort scorch = B("carbon", stone);
        ushort crate = B("crate", 0);
        ushort ember = B("data_cache", 0); // the glowing block the settlement generator uses for lamps

        void Set(int x, int y, int z, ushort id)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = id;
            }
        }

        // Trampled ground: a floor pass keeps the camp's footprint tidy on rough terrain (y0 is the
        // foundation row the stamper sinks to ground level).
        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < l; z++)
            {
                Set(x, 0, z, ground);
            }
        }

        // Palisade ring (2 high) with a gate gap in the middle of each side — raiders can walk in,
        // and so can the player.
        int gate = w / 2;
        for (int x = 0; x < w; x++)
        {
            for (int y = 1; y <= 2; y++)
            {
                if (System.Math.Abs(x - gate) > 1)
                {
                    Set(x, y, 0, log);
                    Set(x, y, l - 1, log);
                    Set(0, y, x, log);
                    Set(w - 1, y, x, log);
                }
            }
        }

        // Fire pit at the centre: a stone ring around a scorched, ember-lit core.
        int cx = w / 2, cz = l / 2;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                bool rim = System.Math.Abs(dx) == 1 || System.Math.Abs(dz) == 1;
                Set(cx + dx, 0, cz + dz, rim ? stone : scorch);
            }
        }

        if (ember != 0)
        {
            Set(cx, 1, cz, ember); // the campfire glow
        }

        markers.Add(new SettlementMarker("bandit", new Vector3i(cx + 2, 1, cz))); // one guard by the fire

        // 3–4 log huts in the corners, doorways facing the fire. Hollow shells with a flat roof.
        var corners = new (int X, int Z)[] { (2, 2), (w - 2 - Hut, 2), (2, l - 2 - Hut), (w - 2 - Hut, l - 2 - Hut) };
        int huts = 3 + (rng.NextDouble() < 0.5 ? 1 : 0);
        int lootPlaced = 0;
        for (int i = 0; i < huts && i < corners.Length; i++)
        {
            var (ox, oz) = corners[i];
            for (int x = 0; x < Hut; x++)
            {
                for (int z = 0; z < Hut; z++)
                {
                    bool wall = x == 0 || z == 0 || x == Hut - 1 || z == Hut - 1;
                    for (int y = 1; y <= 3; y++)
                    {
                        if (wall)
                        {
                            Set(ox + x, y, oz + z, log);
                        }
                    }

                    Set(ox + x, 4, oz + z, log); // flat roof
                }
            }

            // Doorway toward the camp centre: 2 wide, 3 tall (kid-and-avatar-proof clearance).
            bool west = ox < cx;   // hut sits west of the fire → door on its east wall, and vice versa
            bool north = oz < cz;
            int doorX = west ? ox + Hut - 1 : ox;
            int doorZ0 = oz + Hut / 2 - 1;
            for (int y = 1; y <= 3; y++)
            {
                Set(doorX, y, doorZ0, 0);
                Set(doorX, y, doorZ0 + 1, 0);
            }

            _ = north; // symmetry note: east/west doors are enough for all four corners

            int ix = ox + Hut / 2, iz = oz + Hut / 2;
            if (lootPlaced < 2)
            {
                // The stash: a crate to see + a loot marker the server turns into a lootable container.
                if (crate != 0)
                {
                    Set(ix, 1, iz, crate);
                }

                markers.Add(new SettlementMarker("loot", new Vector3i(ix, 2, iz)));
                lootPlaced++;
            }
            else
            {
                markers.Add(new SettlementMarker("bandit", new Vector3i(ix, 1, iz))); // a guard bunks here
            }
        }

        // A couple of scattered crates so the yard reads as a raider dump, not a plaza.
        if (crate != 0)
        {
            for (int i = 0; i < 3; i++)
            {
                int px = 3 + rng.Next(w - 6);
                int pz = 3 + rng.Next(l - 6);
                if (blocks[(px * h + 1) * l + pz] == 0 && (px != cx || pz != cz))
                {
                    Set(px, 1, pz, crate);
                }
            }
        }

        // One patrol guard near the gate.
        markers.Add(new SettlementMarker("bandit", new Vector3i(gate, 1, 3)));

        return new SettlementStructure(w, h, l, "camp", ruined: false, inhabitant: "bandit",
            blocks, markers, buildingCount: huts);
    }
}
