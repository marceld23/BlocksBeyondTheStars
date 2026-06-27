// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Builds a procedurally varied <b>factory</b> — an industrial hall of metal walls + glass windows housing
/// one or more multi-block <b>machines</b>, with a <b>production terminal</b> by the door. Reuses the generic
/// <see cref="SettlementStructure"/> voxel+marker container so the existing stamping pipeline places it. The
/// machines themselves are static <c>machine_block</c> housings in the voxel grid; each carries a
/// <c>machine:&lt;archetype&gt;</c> marker so the client can overlay an animated machine entity on top (the
/// moving pistons/rotors/conveyors). The terminal carries a <c>factory_terminal</c> marker; a <c>loot</c>
/// marker seeds a scavenge cache. Randomised per instance (size, machine count, depth, archetypes).
/// </summary>
public static class FactoryGenerator
{
    private const int BayStride = 5; // X spacing between machine bays
    private const int Height = 8;    // hall height incl. roof

    /// <summary>The machine archetypes the client knows how to animate (piston press, rotary mill, conveyor).</summary>
    public static readonly string[] Archetypes = { "press", "rotor", "conveyor" };

    public static SettlementStructure Generate(long seed, int machineCount, GameContent content)
    {
        machineCount = System.Math.Clamp(machineCount, 1, 6);
        var rng = new System.Random(unchecked((int)(seed ^ (seed >> 32)) ^ 0x4AC7));

        ushort B(string key, ushort fallback = 0) => content.GetBlock(key)?.NumericId.Value ?? fallback;
        ushort wall = B("metal_panel", B("iron_wall", B("stone")));
        ushort floor = B("steel_floor", B("concrete", wall));
        ushort glass = B("glass", wall);
        ushort roof = B("metal_panel", wall);
        ushort machine = B("machine_block", wall);
        ushort pipe = B("factory_pipe", machine);
        ushort terminal = B("factory_terminal", wall);

        int w = machineCount * BayStride + 3;
        int l = 9 + rng.Next(0, 2) * 2; // 9 or 11 deep
        int h = Height;
        var blocks = new ushort[w * h * l];
        void Set(int x, int y, int z, ushort b)
        {
            if (x >= 0 && y >= 0 && z >= 0 && x < w && y < h && z < l)
            {
                blocks[(x * h + y) * l + z] = b;
            }
        }

        var markers = new List<SettlementMarker>();
        int wallTop = h - 2; // interior ceiling height; roof sits at wallTop+1

        // Floor + flat roof slab.
        for (int x = 0; x < w; x++)
            for (int z = 0; z < l; z++)
            {
                Set(x, 0, z, floor);
                Set(x, wallTop + 1, z, roof);
            }

        // Front/back walls (Z faces) with a window band.
        for (int x = 0; x < w; x++)
            for (int y = 1; y <= wallTop; y++)
            {
                bool win = (y == 2 || y == 3) && x % 2 == 0;
                Set(x, y, 0, win ? glass : wall);
                Set(x, y, l - 1, win ? glass : wall);
            }

        // Side walls (X faces) with a window band.
        for (int z = 0; z < l; z++)
            for (int y = 1; y <= wallTop; y++)
            {
                bool win = (y == 2 || y == 3) && z % 2 == 0;
                Set(0, y, z, win ? glass : wall);
                Set(w - 1, y, z, win ? glass : wall);
            }

        // A 2-wide, 3-tall entrance on the front (-Z) wall.
        int dcx = w / 2;
        for (int dx = -1; dx <= 0; dx++)
            for (int y = 1; y <= 3; y++)
            {
                Set(dcx + dx, y, 0, 0);
            }

        // The production terminal just inside the door (front row, clear of the machine bays at mid-depth).
        var terminalCell = new Vector3i(dcx + 1, 1, 1);
        Set(terminalCell.X, terminalCell.Y, terminalCell.Z, terminal);
        markers.Add(new SettlementMarker("factory_terminal", terminalCell));

        // Machine bays down the centre of the hall.
        int bz = l / 2 - 1; // bays occupy depth bz..bz+2
        for (int i = 0; i < machineCount; i++)
        {
            int bx = 2 + i * BayStride;
            int mh = System.Math.Min(wallTop, 4 + rng.Next(0, 2)); // 4..5 tall machine mass
            for (int x = 0; x < 3; x++)
                for (int z = 0; z < 3; z++)
                    for (int y = 1; y <= mh; y++)
                    {
                        Set(bx + x, y, bz + z, machine);
                    }

            // Pipe stack on top of the housing, and a machine anchor marker the client animates from.
            Set(bx + 1, mh + 1, bz + 1, pipe);
            string archetype = Archetypes[(i + (int)(((seed >> 3) % Archetypes.Length + Archetypes.Length)) % Archetypes.Length) % Archetypes.Length];
            markers.Add(new SettlementMarker("machine:" + archetype, new Vector3i(bx + 1, mh + 1, bz + 1)));
        }

        // A scavenge cache in a back corner.
        markers.Add(new SettlementMarker("loot", new Vector3i(1, 1, l - 2)));

        return new SettlementStructure(w, h, l, "factory", ruined: false, inhabitant: string.Empty, blocks, markers, machineCount);
    }
}
