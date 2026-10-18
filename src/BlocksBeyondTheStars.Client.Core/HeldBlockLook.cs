// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #2776: what a held block looks like when it is more than its plain cube — a dye, a glow colour, a form, a painted
    /// design (all four ride in the item key, see <see cref="ItemKey"/>) or the form the server stamps on a prop when it is
    /// placed (bed, campfire, rug, flower pot, ladder, staircase, stretcher, trapdoor — <see cref="PropShapes.DefaultPlaceShape"/>).
    /// The client meshes these cells with the world's own mesher, so the hand shows exactly the block a right-click places.
    /// Unity-free, so the headless suite pins the decisions; <c>HeldMiniatures.AttachBlock</c> turns it into a mesh.
    /// <para>The form is shown in a fixed display orientation, not the pending rotation (the placement ghost shows that):
    /// turned a quarter so it rises to the right — the side view the hotbar icon draws (<c>ShapeIconFactory</c>) — and
    /// upright. A ladder shows its rungs to the eye. A form over several blocks (#1961) and the bed (#1846) show all
    /// their cells.</para>
    /// <para>What this client cannot draw yet becomes what the world would draw: a form the server would not honour on
    /// this block (#1834) or whose voxels have not arrived is a cube, a design not in the paint atlas is no design.</para>
    /// </summary>
    public sealed class HeldBlockLook
    {
        /// <summary>The display yaw of a form: its local +Z (the way a ramp or a staircase rises) points to +X, the eye's
        /// right (<see cref="ShapeCode.YawDirection"/>).</summary>
        public const int DisplayYaw = 3;

        /// <summary>The ladder's display up-face (−Z, toward the eye): the plate hangs on the far wall with its rungs facing
        /// the player (<see cref="PropShapes.LadderForm"/>).</summary>
        public const int LadderUpFace = 5;

        private HeldBlockLook(int tint, int glow, int shape, IReadOnlyList<Cell> cells, string cacheKey)
        {
            Tint = tint;
            Glow = glow;
            Shape = shape;
            Cells = cells;
            CacheKey = cacheKey;
        }

        /// <summary>One block of the look: where it sits (all offsets ≥ 0) and the shape descriptor the world stores for it
        /// (<see cref="ShapeCode"/>: form, orientation, cell of a form over several blocks, paint design).</summary>
        public readonly struct Cell
        {
            public Cell(Vector3i offset, int descriptor)
            {
                Offset = offset;
                Descriptor = descriptor;
            }

            public Vector3i Offset { get; }

            public int Descriptor { get; }
        }

        /// <summary>The surface tint (0xRRGGBB, 0 = none) every cell carries.</summary>
        public int Tint { get; }

        /// <summary>The glow colour (0xRRGGBB, 0 = none) every cell carries.</summary>
        public int Glow { get; }

        /// <summary>The form index (0 = a cube — then only a dye, a glow or a design differ from the plain block).</summary>
        public int Shape { get; }

        /// <summary>The cells to mesh, never empty.</summary>
        public IReadOnlyList<Cell> Cells { get; }

        /// <summary>Everything the mesh depends on besides the block, as a key for the held-mesh cache: two items that look
        /// the same share one mesh.</summary>
        public string CacheKey { get; }

        /// <summary>True when the look is a form rather than a cube — the hand turns it a little to show its profile.</summary>
        public bool IsForm => Shape != 0;

        /// <summary>
        /// The look of <paramref name="itemKey"/> placing <paramref name="placed"/>, or null when it is just the plain cube
        /// (the hand keeps its cheaper cube then). <paramref name="designKnown"/> says whether a paint design id can be
        /// drawn; <paramref name="customVoxels"/> returns a player form's voxels, or null while they have not arrived.
        /// </summary>
        public static HeldBlockLook? For(string? itemKey, BlockDefinition? placed, Func<int, bool>? designKnown,
            Func<int, string?>? customVoxels)
        {
            if (placed == null || string.IsNullOrEmpty(placed.Key))
            {
                return null;
            }

            int tint = ItemKey.Tint(itemKey ?? string.Empty);
            int glow = ItemKey.Glow(itemKey ?? string.Empty);
            int design = ItemKey.Design(itemKey ?? string.Empty);
            if (design != 0 && (designKnown == null || !designKnown(design)))
            {
                design = 0; // not in the paint atlas (yet): the world would draw the plain block too
            }

            int shape = ItemKey.Shape(itemKey ?? string.Empty);
            string? voxels = null;
            if (shape != 0)
            {
                // #1834: the server honours a carried form only on a Shapeable block; a player form needs its voxels.
                bool drawable = placed.Shapeable
                    && (ShapeCode.IsValidShape(shape)
                        || (ShapeCode.IsCustomShape(shape) && customVoxels != null && (voxels = customVoxels(shape)) != null));
                if (!drawable)
                {
                    shape = 0;
                    voxels = null;
                }
            }

            bool stamped = false;
            if (shape == 0)
            {
                shape = PropShapes.DefaultPlaceShape(placed.Key);
                stamped = shape != 0;
            }

            if (tint == 0 && glow == 0 && shape == 0 && design == 0)
            {
                return null;
            }

            var cells = new List<Cell>();
            if (stamped && placed.Key == "ladder")
            {
                var (ladder, upFace) = PropShapes.LadderForm(LadderUpFace);
                cells.Add(new Cell(Vector3i.Zero, WithDesign(ShapeCode.Pack(ladder, 0, upFace), design)));
            }
            else if (shape == 0)
            {
                cells.Add(new Cell(Vector3i.Zero, WithDesign(0, design)));
            }
            else
            {
                int form = ShapeCode.Pack(shape, DisplayYaw, ShapeCode.UpPlusY);
                cells.Add(new Cell(Vector3i.Zero, WithDesign(form, design)));
                if (voxels != null && CustomShape.IsMulti(voxels))
                {
                    // #1961: every block of the form, where the server would put it for this yaw.
                    int count = CustomShape.CellCount(voxels);
                    for (int cell = 1; cell < count; cell++)
                    {
                        var (x, y, z) = CustomShape.CellOffset(voxels, cell, DisplayYaw);
                        cells.Add(new Cell(new Vector3i(x, y, z), WithDesign(ShapeCode.WithCell(form, cell), design)));
                    }
                }
                else if (shape == (int)BlockShape.BedHead
                         && FurnitureShapes.TryBedPartnerOffset(form, out int dx, out int dz))
                {
                    // #1846: a bed is its head and its foot, the foot where the head's yaw points — unpainted, as the server
                    // stamps the partner half.
                    cells.Add(new Cell(new Vector3i(dx, 0, dz), ShapeCode.Pack((int)BlockShape.BedFoot, DisplayYaw, ShapeCode.UpPlusY)));
                }
            }

            var normalised = Normalise(cells);
            var key = new System.Text.StringBuilder("b:");
            key.Append(placed.Key).Append('|').Append(tint.ToString("x6", CultureInfo.InvariantCulture))
                .Append('|').Append(glow.ToString("x6", CultureInfo.InvariantCulture));
            foreach (var c in normalised)
            {
                key.Append('|').Append(c.Offset.X).Append(',').Append(c.Offset.Y).Append(',').Append(c.Offset.Z)
                    .Append(':').Append(c.Descriptor.ToString("x", CultureInfo.InvariantCulture));
            }

            return new HeldBlockLook(tint, glow, shape, normalised, key.ToString());
        }

        private static int WithDesign(int descriptor, int design) => design == 0 ? descriptor : ShapeCode.WithDesign(descriptor, design);

        /// <summary>Shifts the cells so every offset is ≥ 0 — they are meshed inside one chunk.</summary>
        private static List<Cell> Normalise(List<Cell> cells)
        {
            int minX = 0, minY = 0, minZ = 0;
            foreach (var c in cells)
            {
                minX = Math.Min(minX, c.Offset.X);
                minY = Math.Min(minY, c.Offset.Y);
                minZ = Math.Min(minZ, c.Offset.Z);
            }

            if (minX == 0 && minY == 0 && minZ == 0)
            {
                return cells;
            }

            var shift = new Vector3i(-minX, -minY, -minZ);
            var moved = new List<Cell>(cells.Count);
            foreach (var c in cells)
            {
                moved.Add(new Cell(c.Offset + shift, c.Descriptor));
            }

            return moved;
        }
    }
}
