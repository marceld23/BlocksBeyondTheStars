// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Globalization;
using BlocksBeyondTheStars.Client.FarTerrain;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// PerfProbe's outdoor phase (#4050), <c>-perfOutdoor &lt;seconds&gt;</c>: the player stands on open ground near the
    /// spawn and looks ahead. The idle phase cannot say what the look costs outdoors — the spawn is aboard the starter
    /// ship, so it measures a cabin — and the walk phase hovers 30 m up while fresh chunks stream in, so its GPU time
    /// swings from run to run. This phase is the still outdoor picture a <c>-perfFeature</c> sweep compares: the same
    /// spot for a seed, grass, plants and trees around, nothing streaming.
    /// <para>The spot is searched, not assumed: the first attempt stood the player 48 blocks ahead of the spawn wherever
    /// that was, and on the probe's seed that is the inside of a tree — a screen full of leaves and no view. So the phase
    /// looks around its aim for the nearest column of solid land whose air is free: nothing at the feet, and nothing
    /// overhead or beside the player within a few blocks.</para>
    /// </summary>
    public sealed partial class PerfProbe
    {
        private const float OutdoorAheadBlocks = 48f;   // the aim: beyond the landing pad's levelled ground
        private const float OutdoorSettleSeconds = 8f;  // fixed, so a profiler capture's phases can be laid out by time
        private const int OutdoorSearchBlocks = 40;     // how far around the aim a free spot is looked for
        private const int OutdoorSearchStep = 4;
        private const int OutdoorClearRadius = 5;       // free air around the player, in blocks …
        private const int OutdoorClearHeight = 14;      // … and above the ground
        private const int OutdoorBlockedFeet = 100000;  // a spot whose own cells are taken is the last resort

        /// <summary>The outdoor phase's length from <c>-perfOutdoor</c>, 0 without the flag.</summary>
        private static float OutdoorSeconds()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], "-perfOutdoor", StringComparison.OrdinalIgnoreCase)
                    && float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds))
                {
                    return Mathf.Clamp(seconds, 5f, 600f);
                }
            }

            return 0f;
        }

        /// <summary>Puts the player onto free ground near the aim (an authoritative teleport — the solo player is the
        /// world's admin) and waits for the view to stream and mesh.</summary>
        private static IEnumerator StandOutdoors(GameBootstrap boot, Vector3 spawn)
        {
            int aimX = Mathf.FloorToInt(spawn.x), aimZ = Mathf.FloorToInt(spawn.z + OutdoorAheadBlocks);
            var source = boot.FarView != null ? boot.FarView.MainThreadSource : null;
            (int x, int y, int z, int blocked) = source != null ? FreeSpot(boot.World, source, aimX, aimZ) : (aimX, Mathf.FloorToInt(spawn.y), aimZ, -1);
            Debug.Log($"[PerfProbe] Outdoor phase: standing at {x}, {y}, {z} ({(blocked < 0 ? "no far view to ask" : blocked + " cells in the way")}).");
            boot.Network.SendAdminCommand("teleport_to_location", x: x + 0.5f, y: y + 1f, z: z + 0.5f);
            yield return new WaitForSecondsRealtime(OutdoorSettleSeconds);
        }

        /// <summary>The nearest column of solid land around the aim with free air, and its ground's top; without one, the
        /// column with the fewest cells in the way.</summary>
        private static (int X, int Y, int Z, int Blocked) FreeSpot(ClientWorld world, FarTerrainSource source, int aimX, int aimZ)
        {
            var best = (X: aimX, Y: source.Sample(aimX, aimZ, detail: false).Top, Z: aimZ, Blocked: int.MaxValue);
            for (int ring = 0; ring <= OutdoorSearchBlocks; ring += OutdoorSearchStep)
            {
                for (int dx = -ring; dx <= ring; dx += OutdoorSearchStep)
                {
                    for (int dz = -ring; dz <= ring; dz += OutdoorSearchStep)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != ring)
                        {
                            continue; // inside the ring: looked at already
                        }

                        var sample = source.Sample(aimX + dx, aimZ + dz, detail: false);
                        if (sample.Surface != FarSurface.Ground)
                        {
                            continue; // a sea is no place to stand
                        }

                        int blocked = CellsInTheWay(world, aimX + dx, sample.Top, aimZ + dz);
                        if (blocked < best.Blocked)
                        {
                            best = (aimX + dx, sample.Top, aimZ + dz, blocked);
                        }
                    }
                }

                if (best.Blocked == 0)
                {
                    break; // the nearest free spot
                }
            }

            return best;
        }

        /// <summary>How much stands in the air of a column whose ground's top is <paramref name="top"/>: every cell that
        /// is not air — or not streamed — overhead and around, and the player's own two cells weighted so a spot inside a
        /// plant or a trunk loses against any other.</summary>
        private static int CellsInTheWay(ClientWorld world, int x, int top, int z)
        {
            int blocked = 0;
            for (int feet = 0; feet < 2; feet++)
            {
                if (!world.TryGetBlock(x, top + feet, z, out var cell) || !cell.IsAir)
                {
                    blocked += OutdoorBlockedFeet;
                }
            }

            for (int dx = -OutdoorClearRadius; dx <= OutdoorClearRadius; dx++)
            {
                for (int dz = -OutdoorClearRadius; dz <= OutdoorClearRadius; dz++)
                {
                    for (int dy = 2; dy < OutdoorClearHeight; dy++)
                    {
                        if (!world.TryGetBlock(x + dx, top + dy, z + dz, out var cell) || !cell.IsAir)
                        {
                            blocked++;
                        }
                    }
                }
            }

            return blocked;
        }
    }
}
