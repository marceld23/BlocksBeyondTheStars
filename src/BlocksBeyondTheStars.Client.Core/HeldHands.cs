// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2777: the first-person suit hand that holds every item. Before, only the empty slot (the bare fist, #1033) and the
    /// fight gloves (#2278) showed a hand; everything else floated in the corner. Now the forearm and hand of the bare fist
    /// come along with every item and take a grip that fits it (<see cref="HeldGrips"/>):
    /// <list type="bullet">
    /// <item><b>Fist</b> — closed round the model's handle (<see cref="HandlePoint"/>): a pistol grip hanging below the
    /// body, otherwise the model's origin, which every held model already puts in the palm (the avatar holds it there).</item>
    /// <item><b>Palm</b> — the item rests on the open hand, the thumb against its inner side (blocks, vehicles, ore lumps).</item>
    /// <item><b>Pinch</b> — the fist under the item's lower edge (an extruded icon, held like a card).</item>
    /// <item><b>Stem</b> — the fist round the foot of a plant.</item>
    /// </list>
    /// The item stays exactly where it always sat; the hand is built under it. Everything is in the held model's frame
    /// (metres before the viewmodel's item scale, +Z forward, a RIGHT hand: its thumb on −X). Unity-free so the headless
    /// suite pins it; <c>HeldItem.AttachHand</c> turns the boxes into the painted hand cubes.
    /// </summary>
    public static class HeldHands
    {
        /// <summary>What a box of the hand is: the painted lower arm, the darker cuff, or the painted hand.</summary>
        public enum Piece
        {
            Forearm,
            Cuff,
            Hand,
        }

        /// <summary>One box of the hand (centre + size, held-model frame).</summary>
        public readonly struct Box
        {
            public Box(Piece piece, Vector3 position, Vector3 size)
            {
                Piece = piece;
                Position = position;
                Size = size;
            }

            public Piece Piece { get; }

            public Vector3 Position { get; }

            public Vector3 Size { get; }
        }

        // The bare fist of #1033, relative to the fist's centre (it sits at z 0.07 in the empty-slot hand).
        private static readonly Vector3 FistSize = new Vector3(0.13f, 0.12f, 0.14f);
        private static readonly Vector3 ThumbOffset = new Vector3(-0.075f, 0.01f, -0.02f);
        private static readonly Vector3 ThumbSize = new Vector3(0.05f, 0.06f, 0.08f);
        private static readonly Vector3 CuffOffset = new Vector3(0f, -0.02f, -0.09f);
        private static readonly Vector3 CuffSize = new Vector3(0.12f, 0.12f, 0.07f);

        /// <summary>The bare fist's forearm (#1033): its front end 8 cm behind the fist's centre.</summary>
        public const float BareForearm = 0.30f;

        /// <summary>The forearm under a held item: longer, so its far end stays out of view at the peak of a swing (the bare
        /// fist's punch barely tilts and keeps its #1428 arm).</summary>
        public const float ItemForearm = 0.6f;

        /// <summary>Half the fist's height: a fist centred this far below a point has its top there.</summary>
        public const float FistHalfHeight = 0.06f;

        /// <summary>How deep a card's lower edge sits in the pinching fist.</summary>
        public const float PinchDepth = 0.015f;

        /// <summary>How deep a plant's foot sits in the fist round its stem.</summary>
        public const float StemDepth = 0.08f;

        /// <summary>The palm's width; it reaches from the item's inner edge toward the middle.</summary>
        public const float PalmWidth = 0.14f;

        /// <summary>
        /// The grip the first-person hand takes for an item of <paramref name="kind"/> (a <c>HeldItem.Kind</c> name), or
        /// null when the item brings its own hands — the bare hand, the gloves — or shows nothing. An item's own
        /// <see cref="ItemDefinition.HeldGrip"/> wins when it names a one-handed grip.
        /// </summary>
        public static string? GripFor(ItemDefinition? item, string kind)
        {
            if (kind is "None" or "Hand" or HeldItemShapes.GlovesKind)
            {
                return null;
            }

            string? own = item?.HeldGrip;
            if (own is HeldGrips.Fist or HeldGrips.Palm or HeldGrips.Pinch or HeldGrips.Stem)
            {
                return own;
            }

            return kind switch
            {
                "Block" or "Vehicle" or HeldItemShapes.LumpKind => HeldGrips.Palm, // #2838: an ore lump rests on the open hand
                "Plant" => HeldGrips.Stem,
                "Icon" => HeldGrips.Pinch,
                _ => HeldGrips.Fist,
            };
        }

        /// <summary>
        /// Where the fist closes on a model built from <paramref name="parts"/>: on a pistol grip — a part hanging below the
        /// body (its centre under −0.06) that is taller than it is long, the lowest one — just under the body, so the fist
        /// meets the body and the grip shows below it; otherwise at the model's origin, where every held model puts the palm.
        /// </summary>
        public static Vector3 HandlePoint(IReadOnlyList<HeldItemShapes.Part>? parts)
        {
            HeldItemShapes.Part? grip = null;
            if (parts != null)
            {
                foreach (var p in parts)
                {
                    if (p.Position.Y < -0.06f && p.Size.Y >= p.Size.Z && (grip == null || p.Position.Y < grip.Value.Position.Y))
                    {
                        grip = p;
                    }
                }
            }

            if (grip == null)
            {
                return Vector3.Zero;
            }

            var g = grip.Value;
            float top = g.Position.Y + (g.Size.Y * 0.5f);
            return new Vector3(g.Position.X, top - FistHalfHeight - 0.005f, g.Position.Z);
        }

        /// <summary>
        /// The hand for <paramref name="grip"/> under an item spanning <paramref name="min"/>..<paramref name="max"/> (its
        /// bounds in the held-model frame); <paramref name="parts"/> are the model's cubes for the fist's handle. Empty for a
        /// grip it does not know.
        /// </summary>
        public static List<Box> For(string? grip, IReadOnlyList<HeldItemShapes.Part>? parts, Vector3 min, Vector3 max)
        {
            var bottom = new Vector3((min.X + max.X) * 0.5f, min.Y, (min.Z + max.Z) * 0.5f);
            var hand = grip switch
            {
                HeldGrips.Fist => Fist(HandlePoint(parts), ItemForearm),
                HeldGrips.Palm => Palm(bottom, Math.Max(0f, (max.X - min.X) * 0.5f)),
                HeldGrips.Pinch => Fist(bottom - new Vector3(0f, FistHalfHeight - PinchDepth, 0f), ItemForearm),
                HeldGrips.Stem => Fist(bottom + new Vector3(0f, StemDepth - FistHalfHeight, 0f), ItemForearm),
                _ => new List<Box>(),
            };
            return ClearOf(hand, parts);
        }

        /// <summary>How close a hand face may come to a face of the item that faces the same way before the two count as
        /// one plane (#2798).</summary>
        public const float SharedPlaneTolerance = 0.001f;

        /// <summary>How far a hand face in the plane of an item face is pulled back into the hand (#2798).</summary>
        public const float SharedPlaneInset = 0.002f;

        /// <summary>
        /// #2798: pulls every face of <paramref name="hand"/> that lies in the plane of a face of one of the item's
        /// <paramref name="parts"/> facing the same way (closer than <see cref="SharedPlaneTolerance"/>, the two faces
        /// overlapping) back into the hand by <see cref="SharedPlaneInset"/>. Two opaque faces in one plane z-fight: the hand
        /// scanner's 12 cm body and the 12 cm fist round its origin shared their top and bottom, and the hand flickered
        /// through the scanner with every bob of the view. The item stays where it always sat; the hand ends a hair under
        /// its surface. A face that only touches the item from outside (the palm under a block, the thumb at its side) faces
        /// the other way and stays — one of the two is culled.
        /// </summary>
        public static List<Box> ClearOf(List<Box> hand, IReadOnlyList<HeldItemShapes.Part>? parts)
        {
            if (parts == null || parts.Count == 0)
            {
                return hand;
            }

            for (int i = 0; i < hand.Count; i++)
            {
                var box = hand[i];
                var min = box.Position - (box.Size * 0.5f);
                var max = box.Position + (box.Size * 0.5f);
                bool moved = false;

                // A face pulled back may land in the plane of another part: look again until nothing moves (a few rounds at most).
                for (int round = 0; round < 4; round++)
                {
                    bool again = false;
                    foreach (var p in parts)
                    {
                        var pMin = p.Position - (p.Size * 0.5f);
                        var pMax = p.Position + (p.Size * 0.5f);
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if (!FacesOverlap(min, max, pMin, pMax, axis))
                            {
                                continue;
                            }

                            if (Math.Abs(Axis(min, axis) - Axis(pMin, axis)) <= SharedPlaneTolerance)
                            {
                                min = WithAxis(min, axis, Axis(pMin, axis) + SharedPlaneInset);
                                again = true;
                            }

                            if (Math.Abs(Axis(max, axis) - Axis(pMax, axis)) <= SharedPlaneTolerance)
                            {
                                max = WithAxis(max, axis, Axis(pMax, axis) - SharedPlaneInset);
                                again = true;
                            }
                        }
                    }

                    moved |= again;
                    if (!again)
                    {
                        break;
                    }
                }

                if (moved)
                {
                    hand[i] = new Box(box.Piece, (min + max) * 0.5f, max - min);
                }
            }

            return hand;
        }

        // True when the faces of the two boxes across <axis> overlap on the other two axes by more than the tolerance — a
        // shared edge or corner is no shared face.
        private static bool FacesOverlap(Vector3 aMin, Vector3 aMax, Vector3 bMin, Vector3 bMax, int axis)
        {
            for (int other = 0; other < 3; other++)
            {
                if (other != axis
                    && Math.Min(Axis(aMax, other), Axis(bMax, other)) - Math.Max(Axis(aMin, other), Axis(bMin, other)) <= SharedPlaneTolerance)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Axis(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

        private static Vector3 WithAxis(Vector3 v, int axis, float value)
            => axis == 0 ? new Vector3(value, v.Y, v.Z) : axis == 1 ? new Vector3(v.X, value, v.Z) : new Vector3(v.X, v.Y, value);

        /// <summary>How far behind the hand, along the forearm, the swings of a held item turn: round the hand alone the
        /// forearm's far end would swing up into view (the #1428 problem); round the elbow the item would fall out of view.</summary>
        public const float SwingPivotDistance = 0.15f;

        /// <summary>The point the first-person swings of the item held by <paramref name="hand"/> turn round: from the hand
        /// (its biggest box — the fist or the palm) toward the elbow end of the forearm, <see cref="SwingPivotDistance"/>
        /// along. The model's origin when the hand has no forearm.</summary>
        public static Vector3 SwingPivot(IReadOnlyList<Box> hand)
        {
            Box? palm = null, forearm = null;
            foreach (var b in hand)
            {
                if (b.Piece == Piece.Forearm)
                {
                    forearm = b;
                }
                else if (b.Piece == Piece.Hand && (palm == null || Volume(b.Size) > Volume(palm.Value.Size)))
                {
                    palm = b;
                }
            }

            if (palm == null || forearm == null)
            {
                return Vector3.Zero;
            }

            var f = forearm.Value;
            var elbow = new Vector3(f.Position.X, f.Position.Y, f.Position.Z - (f.Size.Z * 0.5f));
            var along = elbow - palm.Value.Position;
            float length = along.Length();
            return length < 1e-4f ? palm.Value.Position : palm.Value.Position + (along * (Math.Min(SwingPivotDistance, length) / length));
        }

        private static float Volume(Vector3 s) => s.X * s.Y * s.Z;

        /// <summary>The closed fist centred on <paramref name="centre"/>, its forearm (<paramref name="forearm"/> long) running
        /// back and down out of view — the bare hand of #1033, moved.</summary>
        public static List<Box> Fist(Vector3 centre, float forearm = BareForearm) => new List<Box>
        {
            new Box(Piece.Forearm, centre + new Vector3(0.02f, -0.06f, -0.08f - (forearm * 0.5f)), new Vector3(0.11f, 0.11f, forearm)),
            new Box(Piece.Cuff, centre + CuffOffset, CuffSize),
            new Box(Piece.Hand, centre, FistSize),
            new Box(Piece.Hand, centre + ThumbOffset, ThumbSize),
        };

        /// <summary>
        /// The open hand under an item whose lower face is centred on <paramref name="bottom"/> and that is
        /// <paramref name="halfWidth"/> wide either side: the palm's top touches the item, it reaches in from the item's inner
        /// (−X) edge — a right hand carries from the inside — and the thumb rises against that edge.
        /// </summary>
        public static List<Box> Palm(Vector3 bottom, float halfWidth)
        {
            float palmX = Math.Min(0f, (PalmWidth * 0.5f) - halfWidth);
            var palm = bottom + new Vector3(palmX, -0.025f, -0.01f);
            return new List<Box>
            {
                new Box(Piece.Forearm, bottom + new Vector3(palmX + 0.02f, -0.075f, -0.115f - (ItemForearm * 0.5f)),
                    new Vector3(0.11f, 0.11f, ItemForearm)),
                new Box(Piece.Cuff, bottom + new Vector3(palmX, -0.04f, -0.115f), new Vector3(0.12f, 0.11f, 0.07f)),
                new Box(Piece.Hand, palm, new Vector3(PalmWidth, 0.05f, 0.15f)),
                new Box(Piece.Hand, bottom + new Vector3(-halfWidth - 0.02f, 0.025f, -0.02f), new Vector3(0.04f, 0.07f, 0.07f)),
            };
        }
    }
}
