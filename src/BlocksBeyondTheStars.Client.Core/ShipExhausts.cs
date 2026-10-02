// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where a ship's engine plumes go (#2162). A ship's engines are its nozzle blocks (<c>engine_nozzle</c> on the
    /// authored layouts and the starter box, <c>ship_engine</c> on player-built hulls). Neighbouring nozzle cells form ONE
    /// engine — the Hammerhead's 2×2 blocks are one big engine each, not four small ones. Only the cells of an engine
    /// that have open space behind them (−Z, the stern direction of every hull) breathe exhaust; an engine buried in the
    /// hull or pointing elsewhere gets no plume. Each engine yields one exhaust: the centre of its open rear faces, with a
    /// size that grows with the number of those faces. Unity-free so the flight view, other players' ships and the
    /// landing view share one answer, and it is unit-tested headless.
    /// </summary>
    public static class ShipExhausts
    {
        /// <summary>One engine's exhaust point in structure cell units: a cell (x, y, z) spans x..x+1 etc., so
        /// <see cref="X"/>/<see cref="Y"/> are the centre of the engine's open rear faces and <see cref="Z"/> is the
        /// rear face itself. <see cref="Faces"/> = how many rear faces breathe (1 for a single nozzle, 4 for a 2×2 block).</summary>
        public readonly struct Exhaust
        {
            public Exhaust(float x, float y, float z, int faces)
            {
                X = x;
                Y = y;
                Z = z;
                Faces = faces;
            }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }

            public int Faces { get; }

            /// <summary>Plume scale: 1 for a single nozzle, √faces for bigger engines (a 2×2 block = 2).</summary>
            public float Size => (float)Math.Sqrt(Math.Max(1, Faces));
        }

        /// <summary>The block keys that count as engine nozzles.</summary>
        public static bool IsEngineBlock(string? blockKey) => blockKey == "engine_nozzle" || blockKey == "ship_engine";

        /// <summary>Finds the exhausts of a hull. <paramref name="engineCells"/> are the cells holding an engine block;
        /// <paramref name="occupied"/> answers whether a cell holds any block at all (to test the open space behind a
        /// nozzle). At most <paramref name="max"/> exhausts, the biggest first, ties ordered left to right then bottom to
        /// top so the result is stable.</summary>
        public static IReadOnlyList<Exhaust> Find(IEnumerable<(int X, int Y, int Z)> engineCells, Func<int, int, int, bool> occupied, int max = 6)
        {
            if (engineCells is null)
            {
                throw new ArgumentNullException(nameof(engineCells));
            }

            if (occupied is null)
            {
                throw new ArgumentNullException(nameof(occupied));
            }

            var engines = new HashSet<(int X, int Y, int Z)>(engineCells);
            var seen = new HashSet<(int X, int Y, int Z)>();
            var result = new List<Exhaust>();
            var stack = new Stack<(int X, int Y, int Z)>();
            foreach (var start in engines)
            {
                if (!seen.Add(start))
                {
                    continue;
                }

                // Flood-fill one engine (6-neighbourhood).
                stack.Push(start);
                double sumX = 0, sumY = 0;
                int faces = 0;
                int rearZ = int.MaxValue;
                var cluster = new List<(int X, int Y, int Z)>();
                while (stack.Count > 0)
                {
                    var c = stack.Pop();
                    cluster.Add(c);
                    foreach (var n in Neighbours(c))
                    {
                        if (engines.Contains(n) && seen.Add(n))
                        {
                            stack.Push(n);
                        }
                    }
                }

                foreach (var c in cluster)
                {
                    if (occupied(c.X, c.Y, c.Z - 1))
                    {
                        continue; // something sits behind this cell — no exhaust out of it
                    }

                    sumX += c.X + 0.5;
                    sumY += c.Y + 0.5;
                    faces++;
                    rearZ = Math.Min(rearZ, c.Z);
                }

                if (faces > 0)
                {
                    result.Add(new Exhaust((float)(sumX / faces), (float)(sumY / faces), rearZ, faces));
                }
            }

            result.Sort((a, b) =>
            {
                int bySize = b.Faces.CompareTo(a.Faces);
                if (bySize != 0)
                {
                    return bySize;
                }

                int byX = a.X.CompareTo(b.X);
                return byX != 0 ? byX : a.Y.CompareTo(b.Y);
            });
            if (result.Count > max)
            {
                result.RemoveRange(max, result.Count - max);
            }

            return result;
        }

        private static IEnumerable<(int X, int Y, int Z)> Neighbours((int X, int Y, int Z) c)
        {
            yield return (c.X + 1, c.Y, c.Z);
            yield return (c.X - 1, c.Y, c.Z);
            yield return (c.X, c.Y + 1, c.Z);
            yield return (c.X, c.Y - 1, c.Z);
            yield return (c.X, c.Y, c.Z + 1);
            yield return (c.X, c.Y, c.Z - 1);
        }
    }
}
