// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One floor of a lift as the floor picker lists it (#3171).</summary>
    public readonly struct LiftFloor
    {
        public LiftFloor(int level, int number, string name, bool hasStop, bool here, bool heading)
        {
            Level = level;
            Number = number;
            Name = name;
            HasStop = hasStop;
            Here = here;
            Heading = heading;
        }

        /// <summary>The platform height the lift rests at on this floor (what the "go" action sends).</summary>
        public int Level { get; }

        /// <summary>1 for the bottom floor, counting up — "Floor 2" when the floor has no name.</summary>
        public int Number { get; }

        /// <summary>The name of the floor's lift stop (#3172), empty when it has none.</summary>
        public string Name { get; }

        /// <summary>A lift stop stands on this floor — only a stop carries a name.</summary>
        public bool HasStop { get; }

        /// <summary>The platform rests here.</summary>
        public bool Here { get; }

        /// <summary>The platform is on its way here.</summary>
        public bool Heading { get; }
    }

    /// <summary>
    /// #3171 — the client's lift facts, Unity-free: which lift the player stands on (E there opens the floor picker), which
    /// lift a stop belongs to and whether its platform waits there (E at that stop opens the picker instead of calling),
    /// what the platform hides from the voxel-only aim ray, and the picker's floors. The lift list arrives canonical
    /// (X and Z in [0, circumference)) while the player's scene position runs unbounded past the seams, so every test measures
    /// the short way round (<see cref="InteractReach"/>'s rule). The geometry mirrors the server's (<c>GameServerLifts</c>).
    /// </summary>
    public static class LiftRiding
    {
        /// <summary>The ring a lift stop stands in: the platform's half width plus one (two cells from the motor's column).</summary>
        public const int StopRing = CrystalNetRules.LiftPlatformRadius + CrystalNetRules.LiftStopReach;

        /// <summary>The lift whose platform the feet at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) stand
        /// on — the box the client carries a rider in (<c>LiftView</c>), a little more generous in height so a rider still
        /// counts while the platform eases between two lists. Null when the player stands on none.</summary>
        public static NetLift? StandingOn(IReadOnlyList<NetLift>? lifts, double x, double y, double z, int circumference)
        {
            if (lifts == null)
            {
                return null;
            }

            double r = CrystalNetRules.LiftPlatformRadius + 0.5;
            for (int i = 0; i < lifts.Count; i++)
            {
                var l = lifts[i];
                if (l == null)
                {
                    continue;
                }

                double lo = Math.Min(l.PlatformY, l.Moving ? l.TargetY : l.PlatformY) + 1.0, hi = Math.Max(l.PlatformY, l.Moving ? l.TargetY : l.PlatformY) + 1.0;
                if (Math.Abs(DeltaX(l.X + 0.5 - x, circumference)) <= r && Math.Abs(DeltaZ(l.Z + 0.5 - z, circumference)) <= r
                    && y >= lo - 0.5 && y <= hi + 0.75)
                {
                    return l;
                }
            }

            return null;
        }

        /// <summary>Whether the platform hides a cell from a player standing on it: a cell of the 3×3 column under the
        /// platform's top — the motor at the bottom floor above all. The aim ray walks voxels only and went straight through the
        /// platform onto the motor ("E only works from below").</summary>
        public static bool HidesCell(NetLift? lift, int x, int y, int z, int circumference)
            => lift != null
               && Math.Abs(CellDeltaX(x - lift.X, circumference)) <= CrystalNetRules.LiftPlatformRadius
               && Math.Abs(CellDeltaZ(z - lift.Z, circumference)) <= CrystalNetRules.LiftPlatformRadius
               && y <= Math.Floor(lift.PlatformY);

        /// <summary>The lift a stop at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) belongs to — the
        /// server's rule: in the ring two cells from the motor's column, on the bottom floor or above it, within the lift's
        /// height. Null when it belongs to none.</summary>
        public static NetLift? LiftOfStop(IReadOnlyList<NetLift>? lifts, int x, int y, int z, int circumference)
        {
            if (lifts == null)
            {
                return null;
            }

            for (int i = 0; i < lifts.Count; i++)
            {
                var l = lifts[i];
                if (l == null)
                {
                    continue;
                }

                int dx = Math.Abs(CellDeltaX(x - l.X, circumference)), dz = Math.Abs(CellDeltaZ(z - l.Z, circumference));
                if (Math.Max(dx, dz) == StopRing && y - 1 >= l.Y + 1 && y - l.Y <= CrystalNetRules.LiftMaxHeight)
                {
                    return l;
                }
            }

            return null;
        }

        /// <summary>Whether a lift's platform rests at a floor (a platform height).</summary>
        public static bool WaitsAt(NetLift? lift, int level)
            => lift != null && !lift.Moving && Math.Abs(lift.PlatformY - level) < 0.01f;

        /// <summary>The lift whose platform waits at the stop at this cell — E there opens the floor picker — or null (E calls
        /// the platform).</summary>
        public static NetLift? WaitingAtStop(IReadOnlyList<NetLift>? lifts, int x, int y, int z, int circumference)
            => LiftOfStop(lifts, x, y, z, circumference) is { } lift && WaitsAt(lift, y - 1) ? lift : null;

        /// <summary>The floor picker's rows, bottom first: each floor's height, number, name and stop, and where the platform
        /// is (or is going). Empty when the server sent no floors (an older server).</summary>
        public static List<LiftFloor> Floors(NetLift? lift)
        {
            var floors = new List<LiftFloor>();
            var levels = lift?.Levels;
            if (lift == null || levels == null)
            {
                return floors;
            }

            for (int i = 0; i < levels.Length; i++)
            {
                string name = lift.LevelNames != null && i < lift.LevelNames.Length ? lift.LevelNames[i] ?? string.Empty : string.Empty;
                bool stop = lift.LevelHasStop != null && i < lift.LevelHasStop.Length && lift.LevelHasStop[i];
                bool heading = lift.Moving && Math.Abs(lift.TargetY - levels[i]) < 0.01f;
                floors.Add(new LiftFloor(levels[i], i + 1, name, stop, WaitsAt(lift, levels[i]), heading));
            }

            return floors;
        }

        /// <summary>A signature of what the picker shows of a lift (floors, names, where the platform is) — the open panel
        /// rebuilds only when it changes, not on every list of a moving platform.</summary>
        public static string Signature(NetLift? lift)
        {
            var parts = new List<string>();
            foreach (var f in Floors(lift))
            {
                parts.Add(f.Level + "|" + f.Name + "|" + (f.HasStop ? 1 : 0) + (f.Here ? 1 : 0) + (f.Heading ? 1 : 0));
            }

            return string.Join(";", parts);
        }

        private static double DeltaX(double dx, int circumference) => circumference > 0 ? WorldConstants.WrapDeltaX(dx, circumference) : dx;

        private static double DeltaZ(double dz, int circumference) => circumference > 0 ? WorldConstants.WrapDeltaZ(dz, circumference) : dz;

        private static int CellDeltaX(int dx, int circumference) => circumference > 0 ? WorldConstants.WrapDeltaX(dx, circumference) : dx;

        private static int CellDeltaZ(int dz, int circumference) => circumference > 0 ? WorldConstants.WrapDeltaZ(dz, circumference) : dz;
    }
}
