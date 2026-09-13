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

        /// <summary>Which local sides of a door carry a partner leaf (#1852). A jamb shared with a partner gets
        /// no post — the renderer used to draw one post per door there, so a double door showed a dark bar
        /// between its leaves.</summary>
        [Flags]
        public enum Sides
        {
            None = 0,
            /// <summary>A partner one block away on the door's local −X side.</summary>
            Minus = 1,
            /// <summary>A partner one block away on the door's local +X side.</summary>
            Plus = 2,
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
        public static bool MirrorsLeaf(Door door, IReadOnlyList<Door> all) => MirrorsLeaf(PartnerSides(door, all));

        /// <summary>The swing rule on an already computed neighbourhood: only the leaf whose sole partner sits
        /// on its local −X side hangs on the far jamb (see <see cref="MirrorsLeaf(Door, IReadOnlyList{Door})"/>).</summary>
        public static bool MirrorsLeaf(Sides partners) => partners == Sides.Minus;

        /// <summary>
        /// The sides of <paramref name="door"/> on which a partner leaf sits exactly one block away along the
        /// wall — the same neighbour search <see cref="MirrorsLeaf(Door, IReadOnlyList{Door})"/> rests on, so
        /// the swing and the shared-jamb post (#1852) can never disagree. A slide door has no partners. Both
        /// flags are set for the middle leaf of three in a row.
        /// </summary>
        public static Sides PartnerSides(Door door, IReadOnlyList<Door> all)
        {
            if (!door.Hinged)
            {
                return Sides.None;
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

            return (onMinus ? Sides.Minus : Sides.None) | (onPlus ? Sides.Plus : Sides.None);
        }
    }
}
