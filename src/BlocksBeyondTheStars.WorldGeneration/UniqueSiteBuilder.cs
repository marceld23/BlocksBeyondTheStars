// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The geometry of the galaxy's one-of-a-kind surface sites (#1129; #3522 moved it out of <c>GameServerUniqueSites</c>):
/// the Singing Shrine and the Sealed Observatory, each around its centre column with its floor on the ground row
/// (<c>groundY</c>, the measured surface — the plinth and the floor disc replace the top terrain cells). Neither reads the
/// ground: both stand as built on whatever is there. Their loot spots end the emission (the server spawns that loot on every
/// load from its own generator).
/// </summary>
public static class UniqueSiteBuilder
{
    /// <summary>The Singing Shrine: a brick plinth, a ring of rune pillars, a humming green beacon — and glowing strip-light
    /// "voices" between the stones. Nothing without ancient brick and rune stone in the content.</summary>
    public static void BuildShrine(IStructureCellSink sink, GameContent content, int cx, int groundY, int cz)
    {
        var brick = content.GetBlock("ancient_brick")?.NumericId ?? BlockId.Air;
        var rune = content.GetBlock("rune_stone")?.NumericId ?? BlockId.Air;
        var beam = content.GetBlock("beam_block")?.NumericId ?? BlockId.Air;
        var light = content.GetBlock("strip_light_cyan")?.NumericId ?? BlockId.Air;
        if (brick.IsAir || rune.IsAir)
        {
            return;
        }

        for (int dx = -6; dx <= 6; dx++)
            for (int dz = -6; dz <= 6; dz++)
            {
                if (dx * dx + dz * dz <= 36)
                {
                    sink.Put(cx + dx, groundY, cz + dz, brick);
                }
            }

        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4.0;
            int px = cx + (int)Math.Round(Math.Cos(a) * 5.0);
            int pz = cz + (int)Math.Round(Math.Sin(a) * 5.0);
            for (int dy = 1; dy <= 3; dy++)
            {
                sink.Put(px, groundY + dy, pz, rune);
            }

            if (!light.IsAir && i % 2 == 0)
            {
                sink.Put(px, groundY + 4, pz, light); // the "voices": four softly glowing crowns
            }
        }

        sink.Put(cx, groundY + 1, cz, rune);
        sink.Put(cx, groundY + 2, cz, rune);
        if (!beam.IsAir)
        {
            sink.Put(cx, groundY + 3, cz, beam); // the shrine's green song, visible from afar
        }

        sink.Mark("relic_cache", cx + 2, groundY + 1, cz);
    }

    /// <summary>The Sealed Observatory: a granite drum under a glass dome, one narrow entrance in the south, and the old
    /// Service's survey terminal still blinking inside. Nothing without granite and glass in the content.</summary>
    public static void BuildObservatory(IStructureCellSink sink, GameContent content, int cx, int groundY, int cz)
    {
        var stone = content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        var glass = content.GetBlock("glass")?.NumericId ?? BlockId.Air;
        if (stone.IsAir || glass.IsAir)
        {
            return;
        }

        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
            {
                int r2 = dx * dx + dz * dz;
                if (r2 > 16)
                {
                    continue;
                }

                sink.Put(cx + dx, groundY, cz + dz, stone); // floor disc
                if (r2 >= 12) // the drum wall, with a 2-tall south doorway
                {
                    bool doorway = dx == 0 && dz > 0;
                    for (int dy = 1; dy <= 2 && !doorway; dy++)
                    {
                        sink.Put(cx + dx, groundY + dy, cz + dz, stone);
                    }

                    sink.Put(cx + dx, groundY + 3, cz + dz, stone); // lintel ring closes over the door
                }
            }

        // The dome: shrinking glass rings up to the cap.
        for (int dy = 4; dy <= 6; dy++)
        {
            int r = 6 - dy + 1; // 3, 2, 1
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz >= (r - 1) * (r - 1) && dx * dx + dz * dz <= r * r)
                    {
                        sink.Put(cx + dx, groundY + dy, cz + dz, glass);
                    }
                }
        }

        sink.Put(cx, groundY + 7, cz, glass); // the cap
        sink.Mark("data_terminal", cx, groundY + 1, cz);
    }
}
