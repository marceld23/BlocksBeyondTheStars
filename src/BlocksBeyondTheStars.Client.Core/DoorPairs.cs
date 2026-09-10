// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Double doors (#1729), inferred rather than declared. The server records every placed door as its own
    /// one-block doorway and knows nothing of pairs; a builder who set two hinged doors side by side in one
    /// wall got two leaves that both swung the same way, so a gateway wide enough for a freighter either never
    /// closed flush or one half opened into the other. The rule here decides, from positions alone, which door
    /// of an adjacent pair is the RIGHT-hand half — the one whose leaf must hang on the far jamb and turn the
    /// other way round. Lives here, Unity-free, so the rule is covered by plain .NET tests; <c>DoorView</c>
    /// only applies it.
    /// </summary>
    public static class DoorPairs
    {
        /// <summary>A door as the client holds it: its kind, whether it swings on a hinge (slide doors never
        /// pair), the doorway centre in world space, and the wall axis (true = the wall runs along X).</summary>
        public readonly struct Door
        {
            public readonly string Kind;
            public readonly bool Hinged;
            public readonly float X, Y, Z;
            public readonly bool AxisX;

            public Door(string kind, bool hinged, float x, float y, float z, bool axisX)
            {
                Kind = kind;
                Hinged = hinged;
                X = x;
                Y = y;
                Z = z;
                AxisX = axisX;
            }
        }

        /// <summary>Centre-to-centre distance of two adjacent one-block doorways along their wall, in blocks.</summary>
        private const float Neighbour = 1f;

        /// <summary>Slack on every coordinate comparison — positions are block centres, so anything short of
        /// half a block is noise.</summary>
        private const float Tolerance = 0.05f;

        /// <summary>
        /// True when <paramref name="door"/> is the right-hand half of a double door: a partner of the same
        /// kind — hinged, same wall axis, same floor, same wall line — sits exactly one block away on its
        /// local −X side, and none on its +X side. Three or more in a row pair off only at that end, so a
        /// middle leaf keeps the default swing; doors of different kinds, doors one behind the other across
        /// the wall, and doors on different floors never pair.
        /// <para>"Local X" is the door's own wall direction as the renderer frames it: world +X for an X wall,
        /// world −Z for a Z wall (the pivot is turned +90° about Y, which carries local +X onto world −Z).</para>
        /// </summary>
        public static bool MirrorsLeaf(Door door, IReadOnlyList<Door> all)
        {
            if (!door.Hinged)
            {
                return false;
            }

            bool onMinus = false, onPlus = false;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (!o.Hinged || o.AxisX != door.AxisX || !string.Equals(o.Kind, door.Kind, StringComparison.Ordinal))
                {
                    continue;
                }

                if (Math.Abs(o.Y - door.Y) > Tolerance)
                {
                    continue;
                }

                float along = door.AxisX ? o.X - door.X : -(o.Z - door.Z);
                float across = door.AxisX ? o.Z - door.Z : o.X - door.X;
                if (Math.Abs(across) > Tolerance)
                {
                    continue;
                }

                if (Math.Abs(along - Neighbour) <= Tolerance)
                {
                    onPlus = true;
                }
                else if (Math.Abs(along + Neighbour) <= Tolerance)
                {
                    onMinus = true;
                }
            }

            return onMinus && !onPlus;
        }
    }
}
