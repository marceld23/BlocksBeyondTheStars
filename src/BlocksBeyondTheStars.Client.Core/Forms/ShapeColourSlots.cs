// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One merged box of a player form together with the colour slot all its micro cells share.</summary>
    public readonly struct SlotBox
    {
        public SlotBox(CustomShape.Box box, int slot)
        {
            Box = box;
            Slot = slot;
        }

        public CustomShape.Box Box { get; }

        /// <summary>The colour slot (<see cref="ShapeColourSlots.Main"/> .. <see cref="ShapeColourSlots.Glow"/>).</summary>
        public int Slot { get; }
    }

    /// <summary>
    /// Colour slots per micro cell of a player form. The form format keeps one hex char per micro cell and
    /// already accepts 2..f as "filled" (<see cref="CustomShape"/>), so a cell can say WHICH colour of its block it shows
    /// without any change to the format, the server or a save: 1 = main (the block's tile as today), 2 = second (a lighter
    /// step), 3 = accent, 4 = dark (a darker step), 5 = glow. 6..f read as main. A slot is not a colour — the colours come
    /// from the block the form is made of (and its dye), so one form works on wood, stone, metal and every dye.
    /// <para>
    /// Drawing only: the server, the collider and the box budget the server checks stay with the value-blind
    /// <see cref="CustomShape.Merge"/>. The client draws the visible faces from <see cref="Merge"/>, which merges only cells
    /// of the same slot; a form whose per-slot merge needs more than <see cref="CustomShape.MaxBoxes"/> boxes is drawn in one
    /// colour instead (<see cref="FitsBudget"/>).
    /// </para>
    /// </summary>
    public static class ShapeColourSlots
    {
        public const int Empty = 0;
        public const int Main = 1;
        public const int Second = 2;
        public const int Accent = 3;
        public const int Dark = 4;
        public const int Glow = 5;

        /// <summary>The number of slots a player can paint (1..5).</summary>
        public const int Count = 5;

        /// <summary>The slot of one bitmap char: '0' empty, '1'..'5' the slots, any other hex digit main.</summary>
        public static int SlotOf(char c)
        {
            if (c == '0')
            {
                return Empty;
            }

            return c >= '2' && c <= '5' ? c - '0' : Main;
        }

        /// <summary>The slot a stored cell value (0..15) stands for.</summary>
        public static int SlotOfValue(int value) => value <= 0 ? Empty : value >= Second && value <= Glow ? value : Main;

        /// <summary>The bitmap char for a cell value 0..15 (lowercase hex, like the wire format).</summary>
        public static char CharOf(int value) => "0123456789abcdef"[Math.Max(0, Math.Min(15, value))];

        /// <summary>The cell value (0..15) of a bitmap char; 0 for anything that is not lowercase hex.</summary>
        public static int ValueOf(char c) => c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : 0;

        /// <summary>True when any cell of the bitmap names a slot other than main — a form an older editor wrote (only 0
        /// and 1) is never slotted, so it is drawn exactly as before.</summary>
        public static bool HasSlots(string? voxels)
        {
            if (voxels == null)
            {
                return false;
            }

            foreach (char c in voxels)
            {
                if (c >= '2' && c <= '5')
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Greedy-merges the filled micro cells of ONE cell bitmap into boxes of a single slot each — the same pass as
        /// <see cref="CustomShape.Merge"/> (grow along +X, then +Z, then +Y from each unclaimed cell in y, z, x order), with
        /// "free" meaning "filled, unclaimed and of the start cell's slot". So a form of one slot merges into exactly the
        /// boxes the value-blind merge makes. Deterministic and integer-only. Empty for a bitmap of neither grid length.
        /// </summary>
        public static List<SlotBox> Merge(string voxels)
        {
            var boxes = new List<SlotBox>();
            int grid = CustomShape.GridOf(voxels);
            if (grid == 0)
            {
                return boxes;
            }

            var slots = new int[grid * grid * grid];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = SlotOf(voxels[i]);
            }

            var claimed = new bool[slots.Length];
            for (int y = 0; y < grid; y++)
            {
                for (int z = 0; z < grid; z++)
                {
                    for (int x = 0; x < grid; x++)
                    {
                        int slot = slots[CustomShape.IndexOf(x, y, z, grid)];
                        if (slot == Empty || claimed[CustomShape.IndexOf(x, y, z, grid)])
                        {
                            continue;
                        }

                        var box = Grow(slots, claimed, grid, slot, x, y, z);
                        Claim(claimed, box);
                        boxes.Add(new SlotBox(box, slot));
                    }
                }
            }

            return boxes;
        }

        /// <summary>True when the per-slot merge of the bitmap stays within <see cref="CustomShape.MaxBoxes"/>.</summary>
        public static bool FitsBudget(string voxels) => Merge(voxels).Count <= CustomShape.MaxBoxes;

        /// <summary>The value-blind boxes as slot boxes of the main slot (the one-colour drawing).</summary>
        public static List<SlotBox> AsMain(IReadOnlyList<CustomShape.Box> boxes)
        {
            var result = new List<SlotBox>(boxes.Count);
            foreach (var box in boxes)
            {
                result.Add(new SlotBox(box, Main));
            }

            return result;
        }

        /// <summary>The boxes a client draws for one cell bitmap: per slot when <paramref name="slotsOn"/>, the bitmap
        /// names slots and the per-slot merge fits the budget; otherwise the value-blind boxes in the main slot.</summary>
        public static List<SlotBox> VisibleBoxes(string voxels, bool slotsOn)
        {
            if (slotsOn && HasSlots(voxels))
            {
                var bySlot = Merge(voxels);
                if (bySlot.Count <= CustomShape.MaxBoxes)
                {
                    return bySlot;
                }
            }

            return AsMain(CustomShape.Merge(voxels));
        }

        private static CustomShape.Box Grow(int[] slots, bool[] claimed, int grid, int slot, int x, int y, int z)
        {
            bool Free(int fx, int fy, int fz)
            {
                int i = CustomShape.IndexOf(fx, fy, fz, grid);
                return slots[i] == slot && !claimed[i];
            }

            int x1 = x + 1;
            while (x1 < grid && Free(x1, y, z))
            {
                x1++;
            }

            int z1 = z + 1;
            while (z1 < grid && RowFree(Free, x, x1, y, z1))
            {
                z1++;
            }

            int y1 = y + 1;
            while (y1 < grid && SlabFree(Free, x, x1, y1, z, z1))
            {
                y1++;
            }

            return new CustomShape.Box(x, y, z, x1, y1, z1, grid);
        }

        private static bool RowFree(Func<int, int, int, bool> free, int x0, int x1, int y, int z)
        {
            for (int x = x0; x < x1; x++)
            {
                if (!free(x, y, z))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SlabFree(Func<int, int, int, bool> free, int x0, int x1, int y, int z0, int z1)
        {
            for (int z = z0; z < z1; z++)
            {
                if (!RowFree(free, x0, x1, y, z))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Claim(bool[] claimed, CustomShape.Box b)
        {
            for (int y = b.Y0; y < b.Y1; y++)
            {
                for (int z = b.Z0; z < b.Z1; z++)
                {
                    for (int x = b.X0; x < b.X1; x++)
                    {
                        claimed[CustomShape.IndexOf(x, y, z, b.Grid)] = true;
                    }
                }
            }
        }
    }
}
