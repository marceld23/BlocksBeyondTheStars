// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Numerics;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The boxes a door is made of (#1975), in the door's LOCAL frame: the wall runs along local X, the door is
    /// <see cref="Thickness"/> deep along local Z, and the origin is the centre of the doorway gap on the floor.
    /// A door in a Z wall is this frame turned 90° about Y — exactly what <c>DoorView</c> does with its pivot.
    /// <para>One list for everything that draws a door: <c>DoorView</c> builds its cubes from it, the in-game
    /// placement ghost and the build editors mesh it translucently or in flat colours. The numbers are the ones
    /// <c>DoorView</c> has always used; keeping them here means a previewed door and a hung door cannot drift
    /// apart, the way <c>BlockShapeGeometry</c> already keeps a previewed block honest.</para>
    /// <para>Unity-free (System.Numerics) so the headless suite pins the part counts and sizes.</para>
    /// </summary>
    public static class DoorGeometry
    {
        /// <summary>Door height: covers the 3-tall doorway, standing on the floor.</summary>
        public const float Height = 2.8f;

        /// <summary>Depth of a leaf or panel across the wall.</summary>
        public const float Thickness = 0.18f;

        /// <summary>A hinge leaf spans this much of the gap, so it clears the jambs while it swings.</summary>
        public const float LeafWidthFactor = 0.96f;

        /// <summary>Each of the two slide panels spans this much of its half of the gap.</summary>
        public const float PanelWidthFactor = 0.98f;

        /// <summary>The vertical light seam down the middle of a leaf or panel, relative to that panel.</summary>
        public const float SeamWidthFactor = 0.12f, SeamHeightFactor = 0.9f, SeamDepthFactor = 1.05f;

        /// <summary>A jamb post: width along the wall, extra height over the door, depth relative to the thickness.</summary>
        public const float PostWidth = 0.14f, PostExtraHeight = 0.1f, PostDepthFactor = 2.2f;

        /// <summary>The energy door's translucent field filling the opening.</summary>
        public const float FieldWidthFactor = 0.98f, FieldThickness = 0.05f;

        /// <summary>How far a hinge leaf swings when open (degrees about the jamb).</summary>
        public const float HingeSwingDegrees = 96f;

        /// <summary>How far each slide panel retracts into its jamb when open, as a share of half the gap.</summary>
        public const float SlideTravelFactor = 0.92f;

        /// <summary>Which piece of the door a box is — the renderer picks the material by it, the editors the colour.</summary>
        public enum Part
        {
            /// <summary>The single swinging leaf of a hinge or wooden door.</summary>
            Leaf,

            /// <summary>The slide panel on the local −X side.</summary>
            PanelMinus,

            /// <summary>The slide panel on the local +X side.</summary>
            PanelPlus,

            /// <summary>The light seam down the middle of a leaf or panel (trim material). Follows its panel.</summary>
            Seam,

            /// <summary>The jamb post on the local −X side (trim material).</summary>
            PostMinus,

            /// <summary>The jamb post on the local +X side (trim material).</summary>
            PostPlus,

            /// <summary>The energy door's field (translucent, drawn only while opening/open).</summary>
            Field,
        }

        /// <summary>One axis-aligned box of a closed door: its centre and full size in the local frame.</summary>
        public readonly struct Box
        {
            public Box(Part part, Vector3 centre, Vector3 size, Part follows)
            {
                Part = part;
                Centre = centre;
                Size = size;
                Follows = follows;
            }

            public Part Part { get; }

            public Vector3 Centre { get; }

            public Vector3 Size { get; }

            /// <summary>The part this box moves with when the door animates — a seam follows its panel or leaf;
            /// every other box follows itself.</summary>
            public Part Follows { get; }

            /// <summary>True for the pieces drawn in the trim material (seams, posts) rather than the panel material.</summary>
            public bool IsTrim => Part == Part.Seam || Part == Part.PostMinus || Part == Part.PostPlus;
        }

        /// <summary>A door is never narrower than one block, whatever the record says.</summary>
        public static float ClampWidth(float width) => Math.Max(1f, width);

        /// <summary>The local X of the jamb a hinge leaf hangs on: the −X jamb by default, the +X jamb for the
        /// mirrored right-hand half of a double door (#1729).</summary>
        public static float HingePivotX(float width, bool mirrored)
        {
            float w = ClampWidth(width);
            return mirrored ? w * 0.5f : -w * 0.5f;
        }

        /// <summary>The leaf's swing about its jamb for an open amount 0..1: the mirrored leaf turns the other way
        /// round so both halves of a double door open to the same side of the wall.</summary>
        public static float HingeSwingDegreesFor(float open01, bool mirrored)
            => (mirrored ? 1f : -1f) * open01 * HingeSwingDegrees;

        /// <summary>The centre X of a slide panel for an open amount 0..1: closed at a quarter of the gap from the
        /// middle, retracting outward into its jamb as the door opens.</summary>
        public static float SlidePanelX(float width, bool plusSide, float open01)
        {
            float w = ClampWidth(width);
            float slide = (w * 0.5f) * open01 * SlideTravelFactor;
            return plusSide ? w * 0.25f + slide : -w * 0.25f - slide;
        }

        /// <summary>
        /// The boxes of a CLOSED door of <paramref name="kind"/> over a gap <paramref name="width"/> blocks wide. A
        /// hinge/wood door is one leaf (hung on the jamb <see cref="HingePivotX"/> names — the far one when
        /// <paramref name="mirrored"/>), a slide/energy door two panels; both get a seam per panel and a jamb post on
        /// every side not shared with a partner leaf (<paramref name="partners"/>, #1852); the energy door adds its
        /// field. Order: panels/leaf first, then their seams, then the posts, then the field.
        /// </summary>
        public static List<Box> Closed(string kind, float width, bool mirrored = false, DoorPairs.Sides partners = DoorPairs.Sides.None)
        {
            float w = ClampWidth(width);
            var boxes = new List<Box>(7);
            var seamScale = new Vector3(SeamWidthFactor, SeamHeightFactor, SeamDepthFactor);

            if (DoorBlocks.IsHandOperated(kind))
            {
                // One leaf across the whole gap; it swings about the jamb, but closed it stands centred.
                var size = new Vector3(w * LeafWidthFactor, Height, Thickness);
                var centre = new Vector3(0f, Height * 0.5f, 0f);
                boxes.Add(new Box(Part.Leaf, centre, size, Part.Leaf));
                boxes.Add(new Box(Part.Seam, centre, size * seamScale, Part.Leaf));
            }
            else
            {
                var size = new Vector3(w * 0.5f * PanelWidthFactor, Height, Thickness);
                var minus = new Vector3(SlidePanelX(w, plusSide: false, 0f), Height * 0.5f, 0f);
                var plus = new Vector3(SlidePanelX(w, plusSide: true, 0f), Height * 0.5f, 0f);
                boxes.Add(new Box(Part.PanelMinus, minus, size, Part.PanelMinus));
                boxes.Add(new Box(Part.PanelPlus, plus, size, Part.PanelPlus));
                boxes.Add(new Box(Part.Seam, minus, size * seamScale, Part.PanelMinus));
                boxes.Add(new Box(Part.Seam, plus, size * seamScale, Part.PanelPlus));
            }

            // Frame trim: a post on each jamb so the opening reads as a real doorway — except on a jamb shared
            // with a partner leaf, where the double door's two posts would meet in a black bar (#1852).
            var postSize = new Vector3(PostWidth, Height + PostExtraHeight, Thickness * PostDepthFactor);
            if (WantsJambPost(partners, plusSide: false))
            {
                boxes.Add(new Box(Part.PostMinus, new Vector3(-w * 0.5f, Height * 0.5f, 0f), postSize, Part.PostMinus));
            }

            if (WantsJambPost(partners, plusSide: true))
            {
                boxes.Add(new Box(Part.PostPlus, new Vector3(w * 0.5f, Height * 0.5f, 0f), postSize, Part.PostPlus));
            }

            if (kind == DoorBlocks.Energy)
            {
                boxes.Add(new Box(Part.Field, new Vector3(0f, Height * 0.5f, 0f), new Vector3(w * FieldWidthFactor, Height, FieldThickness), Part.Field));
            }

            return boxes;
        }

        /// <summary>Whether the jamb post on one local side of a door is drawn (#1852): a jamb shared with a
        /// partner leaf gets none, since each door used to put its own post there and the two coincident posts
        /// read as a black bar splitting the double door.</summary>
        public static bool WantsJambPost(DoorPairs.Sides partners, bool plusSide)
            => (partners & (plusSide ? DoorPairs.Sides.Plus : DoorPairs.Sides.Minus)) == DoorPairs.Sides.None;

        /// <summary>The half extents of the closed door's bounding box in the local frame (posts included) —
        /// the aim slab <c>DoorView</c> tests a ray against and the footprint the editors colour.</summary>
        public static Vector3 HalfExtents(float width)
            => new Vector3(ClampWidth(width) * 0.5f + PostWidth * 0.5f, (Height + PostExtraHeight) * 0.5f, Thickness * PostDepthFactor * 0.5f);
    }
}
