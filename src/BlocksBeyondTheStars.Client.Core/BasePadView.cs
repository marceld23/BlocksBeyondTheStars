// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3069: reads the base landing pads out of a body's pad list for the views. A body's list carries its communal pads
    /// (addressed by <see cref="NetLandingPad.Index"/>) and the player-built pads of its bases (<see cref="NetLandingPad.Base"/>,
    /// addressed by their cell). Up to four pads of one base stand within a few dozen blocks — one pixel on the land map — so
    /// the chooser shows one marker per base, at its core, and lists that base's pads when it is picked.
    /// </summary>
    public static class BasePadView
    {
        /// <summary>One base's pads in a list: where its core is, its name, whose it is, and the pads in a stable order.</summary>
        public sealed class Group
        {
            public int BaseX { get; set; }
            public int BaseY { get; set; }
            public int BaseZ { get; set; }
            public string BaseName { get; set; } = string.Empty;
            public string OwnerName { get; set; } = string.Empty;

            /// <summary><see cref="NetBase.RelationOwn"/>, <see cref="NetBase.RelationAlly"/> or <see cref="NetBase.RelationOther"/>.</summary>
            public byte Relation { get; set; }

            public List<NetLandingPad> Pads { get; } = new List<NetLandingPad>();

            /// <summary>Pads the receiver may land on right now: not locked, and free or their own.</summary>
            public int Landable
            {
                get
                {
                    int n = 0;
                    foreach (var p in Pads)
                    {
                        if (CanLand(p))
                        {
                            n++;
                        }
                    }

                    return n;
                }
            }
        }

        /// <summary>Whether the receiver may pick this pad in the chooser: not locked, and not held by someone else.</summary>
        public static bool CanLand(NetLandingPad pad) => !pad.Locked && (!pad.Occupied || pad.Mine);

        /// <summary>The body's communal pads (everything the chooser and the world map drew before #3069).</summary>
        public static List<NetLandingPad> Communal(NetLandingPad[]? pads)
        {
            var list = new List<NetLandingPad>();
            if (pads is null)
            {
                return list;
            }

            foreach (var p in pads)
            {
                if (!p.Base)
                {
                    list.Add(p);
                }
            }

            return list;
        }

        /// <summary>The base pads grouped by their base (core cell), bases and pads in a fixed order — the receiver's own bases
        /// first, then allies', then the rest; inside a base by pad name, then by cell.</summary>
        public static List<Group> GroupByBase(NetLandingPad[]? pads)
        {
            var groups = new List<Group>();
            if (pads is null)
            {
                return groups;
            }

            var byCore = new Dictionary<(int, int, int), Group>();
            foreach (var p in pads)
            {
                if (!p.Base)
                {
                    continue;
                }

                var key = (p.BaseX, p.BaseY, p.BaseZ);
                if (!byCore.TryGetValue(key, out var g))
                {
                    g = new Group
                    {
                        BaseX = p.BaseX,
                        BaseY = p.BaseY,
                        BaseZ = p.BaseZ,
                        BaseName = p.BaseName ?? string.Empty,
                        OwnerName = p.OwnerName ?? string.Empty,
                        Relation = p.Relation,
                    };
                    byCore[key] = g;
                    groups.Add(g);
                }

                g.Pads.Add(p);
            }

            foreach (var g in groups)
            {
                g.Pads.Sort(ComparePads);
            }

            groups.Sort((a, b) => a.Relation != b.Relation
                ? a.Relation.CompareTo(b.Relation)
                : string.CompareOrdinal(a.BaseName, b.BaseName) != 0
                    ? string.CompareOrdinal(a.BaseName, b.BaseName)
                    : CompareCells(a.BaseX, a.BaseY, a.BaseZ, b.BaseX, b.BaseY, b.BaseZ));
            return groups;
        }

        /// <summary>The base pad at a cell in a list, or null.</summary>
        public static NetLandingPad? Find(NetLandingPad[]? pads, int x, int y, int z)
        {
            if (pads is null)
            {
                return null;
            }

            foreach (var p in pads)
            {
                if (p.Base && p.CellX == x && p.CellY == y && p.CellZ == z)
                {
                    return p;
                }
            }

            return null;
        }

        private static int ComparePads(NetLandingPad a, NetLandingPad b)
        {
            int byName = string.CompareOrdinal(a.Name ?? string.Empty, b.Name ?? string.Empty);
            return byName != 0 ? byName : CompareCells(a.CellX, a.CellY, a.CellZ, b.CellX, b.CellY, b.CellZ);
        }

        private static int CompareCells(int ax, int ay, int az, int bx, int by, int bz)
            => ax != bx ? ax.CompareTo(bx) : ay != by ? ay.CompareTo(by) : az.CompareTo(bz);
    }
}
