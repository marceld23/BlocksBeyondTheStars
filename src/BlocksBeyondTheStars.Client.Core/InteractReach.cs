// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client's "what does the key reach?" picks over server entities (#2613): the person E talks to, the crate G and H
    /// work on, the crate under the crosshair, the base zone the player stands in. The server sends every position
    /// canonical (X in [0, circumference)), while the player's scene position runs unbounded once it walks past the seam —
    /// so every pick measures the short way round both seams, the same mapping <c>GameBootstrap.ScenePos</c> draws the
    /// entities at. The home landing pad lies at X = 0 on every body, and a base dug beside the ship straddles the seam:
    /// before this, crates and settlers on the far side of X = 0 from where the player loaded never answered E.
    /// Both the key handlers and the HUD prompts call these, so a prompt never offers what the key would not do.
    /// </summary>
    public static class InteractReach
    {
        /// <summary>How far E reaches to talk to a person (#1127); the "Talk to … (E)" prompt uses the same reach.</summary>
        public const float TalkReach = 4.5f;

        /// <summary>How far G (take all) and H (store) reach to a container, measured to its cell centre — the server's
        /// <c>LootReach</c>; the crate line on the HUD uses the same reach.</summary>
        public const float ContainerReach = 6f;

        /// <summary>Squared distance from a scene position (unbounded) to a canonical world position, the short way round
        /// the longitude and latitude seams of a world of <paramref name="circumference"/> (Y is linear). A circumference
        /// of 0 or less measures straight (no wrapping world).</summary>
        public static double SeamDistanceSquared(double sceneX, double sceneY, double sceneZ,
            double worldX, double worldY, double worldZ, int circumference)
        {
            double dx = circumference > 0 ? WorldConstants.WrapDeltaX(worldX - sceneX, circumference) : worldX - sceneX;
            double dz = circumference > 0 ? WorldConstants.WrapDeltaZ(worldZ - sceneZ, circumference) : worldZ - sceneZ;
            double dy = worldY - sceneY;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>The person E talks to from (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) — the
        /// nearest NPC within <see cref="TalkReach"/>, or null. THE one picker for the E key and the HUD's talk prompt.</summary>
        public static NetNpc? NearestTalkableNpc(IReadOnlyList<NetNpc>? npcs, float x, float y, float z, int circumference)
        {
            NetNpc? best = null;
            double bestSq = TalkReach * (double)TalkReach;
            if (npcs == null)
            {
                return null;
            }

            for (int i = 0; i < npcs.Count; i++)
            {
                var n = npcs[i];
                if (n == null)
                {
                    continue;
                }

                double sq = SeamDistanceSquared(x, y, z, n.X, n.Y, n.Z, circumference);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = n;
                }
            }

            return best;
        }

        /// <summary>The container G and H work on from (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) —
        /// the nearest within <see cref="ContainerReach"/> of its cell centre, or null; only storage crates when
        /// <paramref name="crateOnly"/>. The HUD's crate line names the same one.</summary>
        public static NetContainer? NearestContainer(IReadOnlyList<NetContainer>? containers, float x, float y, float z,
            int circumference, bool crateOnly)
        {
            NetContainer? best = null;
            double bestSq = ContainerReach * (double)ContainerReach;
            if (containers == null)
            {
                return null;
            }

            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c == null || (crateOnly && c.Kind != "crate"))
                {
                    continue;
                }

                double sq = SeamDistanceSquared(x, y, z, c.X + 0.5, c.Y + 0.5, c.Z + 0.5, circumference);
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = c;
                }
            }

            return best;
        }

        /// <summary>The storage crate registered at the aimed cell (<paramref name="cellX"/>, <paramref name="cellY"/>,
        /// <paramref name="cellZ"/>, read off the scene), or null — E on a crate opens its screen (#2436). Matched with
        /// <see cref="WorldConstants.SameCell"/>, so a crate across the seam is still the crate in the crosshair.</summary>
        public static NetContainer? CrateAtCell(IReadOnlyList<NetContainer>? containers, int cellX, int cellY, int cellZ, int circumference)
        {
            if (containers == null)
            {
                return null;
            }

            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                if (c != null && c.Kind == "crate"
                    && WorldConstants.SameCell(Cell(c.X), Cell(c.Y), Cell(c.Z), cellX, cellY, cellZ, circumference))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>Whether the player's cell (<paramref name="px"/>, <paramref name="py"/>, <paramref name="pz"/>, read
        /// off the scene) lies in the zone of the base whose core carries the world position (<paramref name="coreX"/>,
        /// <paramref name="coreY"/>, <paramref name="coreZ"/>) — the <see cref="WorldConstants.BaseZoneRadius"/> cube
        /// around the core cell, the short way round both seams like the server's air check. HUD feedback only.</summary>
        public static bool InBaseZone(int px, int py, int pz, float coreX, float coreY, float coreZ, int circumference)
        {
            int r = WorldConstants.BaseZoneRadius;
            int dx = Cell(coreX) - px, dz = Cell(coreZ) - pz;
            if (circumference > 0)
            {
                dx = WorldConstants.WrapDeltaX(dx, circumference);
                dz = WorldConstants.WrapDeltaZ(dz, circumference);
            }

            return Math.Abs(dx) <= r && Math.Abs(py - Cell(coreY)) <= r && Math.Abs(dz) <= r;
        }

        /// <summary>The block cell a world coordinate falls in (floor, so negative coordinates land in the right cell).</summary>
        public static int Cell(float v) => (int)Math.Floor(v);
    }
}
