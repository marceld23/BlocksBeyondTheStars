// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What a click in a build editor writes (#1975) — the server's own placement rules, run over the editor's
    /// grid so a template exports what the game would have stamped: a prop takes its default form when the brush
    /// says Automatic (a campfire is a slab, a rug a sheet, a ladder a plate against its wall), a bed is head +
    /// foot on two cells and is refused where the foot would not fit, both halves go together, and a door marker
    /// is only valid with a wall beside it and two free cells above. Unity-free so the headless suite pins it;
    /// the editors only draw and wire.
    /// <para>Grids are callbacks: <c>occupied</c> = any authored cell (block, marker, element), <c>solid</c> = a
    /// cell that blocks a door (a block, not a marker), <c>inBounds</c> = inside the build room.</para>
    /// </summary>
    public static class EditorPlacementRules
    {
        /// <summary>The form brush's "Automatic" setting: the block's own default form (a plain cube for most).</summary>
        public const int AutoForm = -1;

        /// <summary>Locale key of the refusal when a bed's foot cell is taken or outside the room.</summary>
        public const string BedNeedsRoom = "ui.ed.bed_needs_room";

        /// <summary>Locale key of the refusal when a door marker has no wall beside it or no free cells above.</summary>
        public const string DoorNeedsWall = "ui.ed.door_needs_wall";

        /// <summary>One cell a placement writes: where, and the packed form descriptor (0 = plain cube).</summary>
        public readonly struct CellWrite
        {
            public CellWrite(int x, int y, int z, int shape)
            {
                X = x;
                Y = y;
                Z = z;
                Shape = shape;
            }

            public int X { get; }

            public int Y { get; }

            public int Z { get; }

            public int Shape { get; }
        }

        /// <summary>The form index a block entry places with: the brush form when one is chosen, else the block's
        /// default (<see cref="PropShapes.DefaultPlaceShape"/> — 0 for an ordinary building block).</summary>
        public static int ResolveForm(string blockId, int brushShape)
            => brushShape >= 0 ? brushShape : PropShapes.DefaultPlaceShape(blockId);

        /// <summary>
        /// The packed form of a ladder placed at a cell: the plate hugging the wall it was clicked against
        /// (<paramref name="hitFace"/> = the face of the placed cell the click came through, -1 for none), else the
        /// first wall in the server's scan order, else the free-standing pole — the in-game rule
        /// (<see cref="PropShapes.DeriveLadderMount"/>, <see cref="PropShapes.LadderForm"/>).
        /// </summary>
        public static int LadderDescriptor(Func<int, int, int, bool> solid, int x, int y, int z, int hitFace)
        {
            bool HasWall(int upFace)
            {
                var d = ShapeCode.FaceDirection(upFace);
                return solid(x - d.X, y - d.Y, z - d.Z);
            }

            int mount = PropShapes.DeriveLadderMount(HasWall, hitFace);
            var (shape, upFace) = PropShapes.LadderForm(mount);
            return ShapeCode.Pack(shape, 0, upFace);
        }

        /// <summary>
        /// The cells a block entry writes at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) — the
        /// caller has already checked that cell is free and inside the room. One cell for most blocks (with the
        /// resolved form and the brush yaw), two for a bed (the head here, the foot where the yaw points), the
        /// ladder with its wall-derived form. False with a refusal key when the bed's foot cell is taken or outside.
        /// </summary>
        public static bool TryPlaceBlock(string blockId, int brushShape, int yaw, int x, int y, int z, int hitFace,
            Func<int, int, int, bool> occupied, Func<int, int, int, bool> inBounds, Func<int, int, int, bool> solid,
            out List<CellWrite> writes, out string? refusalKey)
        {
            writes = new List<CellWrite>(2);
            refusalKey = null;
            int form = ResolveForm(blockId, brushShape);

            if (blockId == "ladder" && brushShape < 0)
            {
                writes.Add(new CellWrite(x, y, z, LadderDescriptor(solid, x, y, z, hitFace)));
                return true;
            }

            int descriptor = form != 0 ? ShapeCode.Pack(form, yaw & 3, ShapeCode.UpPlusY) : 0;
            if (form == (int)BlockShape.BedHead && FurnitureShapes.TryBedPartnerOffset(descriptor, out int dx, out int dz))
            {
                int fx = x + dx, fz = z + dz;
                if (!inBounds(fx, y, fz) || occupied(fx, y, fz))
                {
                    refusalKey = BedNeedsRoom;
                    return false;
                }

                writes.Add(new CellWrite(x, y, z, descriptor));
                writes.Add(new CellWrite(fx, y, fz, FurnitureShapes.BedPartnerDescriptor(descriptor)));
                return true;
            }

            writes.Add(new CellWrite(x, y, z, descriptor));
            return true;
        }

        /// <summary>The other cells that go when the cell at (<paramref name="x"/>, <paramref name="y"/>,
        /// <paramref name="z"/>) with form <paramref name="descriptor"/> is removed: a bed half takes its partner
        /// along (the server mines a bed as a pair, #1846); anything else stands alone.</summary>
        public static IReadOnlyList<(int X, int Y, int Z)> PartnerCells(int descriptor, int x, int y, int z)
        {
            if (FurnitureShapes.TryBedPartnerOffset(descriptor, out int dx, out int dz))
            {
                return new[] { (x + dx, y, z + dz) };
            }

            return Array.Empty<(int, int, int)>();
        }

        /// <summary>
        /// Whether a door marker at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) would give
        /// the door the author means: a wall beside it (else the server hangs it along Z on open floor — never what
        /// was meant) and the two cells above free and inside the room (the door is 2.8 tall). Also hands back the
        /// door the server would hang, for the preview.
        /// </summary>
        public static bool DoorValid(Func<int, int, int, bool> solid, Func<int, int, int, bool> occupied, Func<int, int, int, bool> inBounds,
            int x, int y, int z, out DoorProbe.Fit fit, out string? refusalKey)
        {
            fit = DoorProbe.Measure(solid, x, y, z);
            refusalKey = null;
            if (!fit.HasJamb)
            {
                refusalKey = DoorNeedsWall;
                return false;
            }

            for (int up = 1; up <= 2; up++)
            {
                if (!inBounds(x, y + up, z) || occupied(x, y + up, z))
                {
                    refusalKey = DoorNeedsWall;
                    return false;
                }
            }

            return true;
        }

        /// <summary>The cells a door drawn for <paramref name="fit"/> at a marker (<paramref name="x"/>,
        /// <paramref name="y"/>, <paramref name="z"/>) depends on: a change in any of them re-fits the door. The
        /// probe reads ±1 across and up to the reach along the wall on both axes (the axis itself can flip), so the
        /// safe answer is the square of that reach around the marker at its floor level.</summary>
        public static bool AffectsDoor(int changedX, int changedY, int changedZ, int doorX, int doorY, int doorZ)
            => changedY == doorY
               && Math.Abs(changedX - doorX) <= DoorProbe.DefaultReach + 1
               && Math.Abs(changedZ - doorZ) <= DoorProbe.DefaultReach + 1;
    }
}
