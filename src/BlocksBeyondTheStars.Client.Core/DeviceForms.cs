// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Device forms: one box of a device's form on the 8-grid of its cell — integer eighths 0..8
    /// per axis, <c>[X0, X1) × [Y0, Y1) × [Z0, Z1)</c> — in the form's OWN frame: y up, the device's FRONT is the −Z side
    /// (z = 0), and x grows to the right for someone standing in front of it. <see cref="Slot"/> (1-based) names the
    /// <see cref="DeviceFormSlot"/> that dresses the box's faces.
    /// </summary>
    public readonly struct DeviceFormBox
    {
        public DeviceFormBox(int x0, int y0, int z0, int x1, int y1, int z1, int slot)
        {
            X0 = x0;
            Y0 = y0;
            Z0 = z0;
            X1 = x1;
            Y1 = y1;
            Z1 = z1;
            Slot = slot;
        }

        public int X0 { get; }
        public int Y0 { get; }
        public int Z0 { get; }
        public int X1 { get; }
        public int Y1 { get; }
        public int Z1 { get; }
        public int Slot { get; }

        public override string ToString() => $"[{X0},{Y0},{Z0} .. {X1},{Y1},{Z1}] slot {Slot}";
    }

    /// <summary>How a tile is laid onto one face of a part.</summary>
    public enum DeviceTileMode : byte
    {
        /// <summary>The face shows the piece of the tile at its position — what a player's freeform shape does. Right for a
        /// material whose pattern sits on the micro-cube raster (1 micro cell = 8 px) or has no motif, and for a device's own
        /// picture on a face that lies in (or close to) the cube face the picture was drawn for.</summary>
        Slice = 0,

        /// <summary>The whole tile — or the region <see cref="DeviceFormTile.U0"/>..<see cref="DeviceFormTile.V1"/> of it — is
        /// laid onto the face, not cut: a picture that must be seen complete (a screen, a lamp lens, a drawer front).</summary>
        Fit = 1,
    }

    /// <summary>
    /// What one face of a part shows: <see cref="Key"/> null = the device's OWN cube face on that side (its top, bottom,
    /// side tile or front picture — exactly what the plain cube shows there), else a block key or a face-tile key.
    /// <see cref="DeviceTileMode.Fit"/> lays the region (tile fractions, v up: 0,0 = bottom-left) onto the face; the face's
    /// left/bottom corner — as seen from outside — gets (U0, V0).
    /// </summary>
    public readonly struct DeviceFormTile
    {
        private DeviceFormTile(string? key, DeviceTileMode mode, float u0, float v0, float u1, float v1)
        {
            Key = key;
            Mode = mode;
            U0 = u0;
            V0 = v0;
            U1 = u1;
            V1 = v1;
        }

        /// <summary>The device's own cube face, sliced by position.</summary>
        public static DeviceFormTile Own => default;

        public string? Key { get; }
        public DeviceTileMode Mode { get; }
        public float U0 { get; }
        public float V0 { get; }
        public float U1 { get; }
        public float V1 { get; }

        public bool IsOwn => Key == null;

        /// <summary>A named tile, sliced by position.</summary>
        public static DeviceFormTile Slice(string key) => new DeviceFormTile(key, DeviceTileMode.Slice, 0f, 0f, 1f, 1f);

        /// <summary>A named tile (null = the own cube face) laid whole onto each face.</summary>
        public static DeviceFormTile Fit(string? key) => new DeviceFormTile(key, DeviceTileMode.Fit, 0f, 0f, 1f, 1f);

        /// <summary>A region of a named tile (null = the own cube face) laid onto each face.</summary>
        public static DeviceFormTile Fit(string? key, float u0, float v0, float u1, float v1)
            => new DeviceFormTile(key, DeviceTileMode.Fit, u0, v0, u1, v1);

        public override string ToString()
            => (Key ?? "own") + (Mode == DeviceTileMode.Fit ? $" fit({U0:0.##},{V0:0.##}..{U1:0.##},{V1:0.##})" : " slice");
    }

    /// <summary>
    /// How a slot dresses the faces of its boxes, by the side a face looks to in the form's own frame (top, bottom, side,
    /// front = −Z). Emission: a face showing the OWN cube face keeps the block's glow; a face with a named tile glows only
    /// when <see cref="Glow"/> (at least <see cref="MinEmission"/>), else it is dark — a housing plate of a glowing machine
    /// does not bloom.
    /// </summary>
    public sealed class DeviceFormSlot
    {
        public DeviceFormSlot(DeviceFormTile top, DeviceFormTile bottom, DeviceFormTile side, DeviceFormTile front,
            bool glow = false, float minEmission = 0f)
        {
            Top = top;
            Bottom = bottom;
            Side = side;
            Front = front;
            Glow = glow;
            MinEmission = minEmission;
        }

        public DeviceFormTile Top { get; }
        public DeviceFormTile Bottom { get; }
        public DeviceFormTile Side { get; }
        public DeviceFormTile Front { get; }
        public bool Glow { get; }
        public float MinEmission { get; }

        /// <summary>Every face the own cube face, sliced by position.</summary>
        public static DeviceFormSlot Own() => All(DeviceFormTile.Own);

        /// <summary>Every face the same tile.</summary>
        public static DeviceFormSlot All(DeviceFormTile tile, bool glow = false, float minEmission = 0f)
            => new DeviceFormSlot(tile, tile, tile, tile, glow, minEmission);

        /// <summary>A housing part: the own cube face on top, a material on every other face.</summary>
        public static DeviceFormSlot OwnTop(DeviceFormTile rest) => new DeviceFormSlot(DeviceFormTile.Own, rest, rest, rest);

        /// <summary>The tile of the faces that look to <paramref name="side"/> in the form's own frame.</summary>
        public DeviceFormTile For(FaceSide side) => side switch
        {
            FaceSide.Top => Top,
            FaceSide.Bottom => Bottom,
            FaceSide.Front => Front,
            _ => Side,
        };

        /// <summary>The four tiles, in <see cref="FaceSide"/> order.</summary>
        public IEnumerable<DeviceFormTile> Tiles()
        {
            yield return Top;
            yield return Bottom;
            yield return Side;
            yield return Front;
        }
    }

    /// <summary>Device forms: the form of one device block — its boxes and the slots they name.</summary>
    public sealed class DeviceForm
    {
        public DeviceForm(string key, IReadOnlyList<DeviceFormBox> boxes, IReadOnlyList<DeviceFormSlot> slots)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            Boxes = boxes ?? throw new ArgumentNullException(nameof(boxes));
            Slots = slots ?? throw new ArgumentNullException(nameof(slots));
        }

        /// <summary>The block key this form draws.</summary>
        public string Key { get; }

        public IReadOnlyList<DeviceFormBox> Boxes { get; }

        /// <summary>Slot n of the boxes is <c>Slots[n - 1]</c>.</summary>
        public IReadOnlyList<DeviceFormSlot> Slots { get; }

        /// <summary>The slot a box names, or null when it names none of this form's slots.</summary>
        public DeviceFormSlot? SlotOf(int slot) => slot >= 1 && slot <= Slots.Count ? Slots[slot - 1] : null;

        /// <summary>The named tile keys of every slot (own faces excluded), distinct, in ordinal order.</summary>
        public IReadOnlyList<string> TileKeys()
        {
            var keys = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var slot in Slots)
            {
                foreach (var tile in slot.Tiles())
                {
                    if (!tile.IsOwn)
                    {
                        keys.Add(tile.Key!);
                    }
                }
            }

            return new List<string>(keys);
        }
    }

    /// <summary>
    /// Device forms: devices drawn as a FORM inside their cube instead of a plain cube with picture
    /// tiles. The outer size stays one block: every box lies inside the cell's 8-grid, the collider stays the full cube, and
    /// the server knows nothing of it. A form is a list of boxes on the 8-grid of the freeform shape editor, each carrying a
    /// slot that says which tile its faces wear (<see cref="DeviceFormSlot"/>). The table is <see cref="Table"/>
    /// (DeviceFormTable.cs); <see cref="FromVoxels"/> turns a form drawn in the game's shape editor into boxes, so a form a
    /// player draws can go into the table later. Unity-free so the headless suite checks every form.
    /// </summary>
    public static partial class DeviceForms
    {
        /// <summary>Micro cells per block side.</summary>
        public const int Grid = CustomShape.GridLarge;

        /// <summary>The freeform shapes' render/collider budget, kept here too.</summary>
        public const int MaxBoxes = CustomShape.MaxBoxes;

        private static Dictionary<string, DeviceForm>? _table;

        /// <summary>Every device form, by block key.</summary>
        public static IReadOnlyDictionary<string, DeviceForm> Table => _table ??= BuildTable();

        /// <summary>The form of a block key, if it has one.</summary>
        public static bool TryGet(string? key, out DeviceForm form)
        {
            form = null!;
            return key != null && Table.TryGetValue(key, out form!);
        }

        /// <summary>
        /// A form from a voxel string in the freeform format (<see cref="CustomShape"/>: 512 hex chars for the 8³ grid or 64 for
        /// the 4³ grid, x fastest, then z, then y). Here a cell's hex value 1..f is its SLOT (0 = empty); the cells of each slot
        /// are merged into boxes with the shape editor's own merge (<see cref="CustomShape.Merge"/>). The voxel frame is the
        /// form frame: the front is the −Z side.
        /// </summary>
        public static DeviceForm FromVoxels(string key, string voxels, params DeviceFormSlot[] slots)
        {
            int grid = CustomShape.GridOf(voxels);
            if (grid == 0)
            {
                throw new ArgumentException("A form needs 512 (8³) or 64 (4³) hex characters.", nameof(voxels));
            }

            int scale = Grid / grid;
            var boxes = new List<DeviceFormBox>();
            var mask = new char[voxels.Length];
            for (int slot = 1; slot <= 15; slot++)
            {
                char want = slot < 10 ? (char)('0' + slot) : (char)('a' + slot - 10);
                bool any = false;
                for (int i = 0; i < voxels.Length; i++)
                {
                    bool hit = char.ToLowerInvariant(voxels[i]) == want;
                    mask[i] = hit ? '1' : '0';
                    any |= hit;
                }

                if (!any)
                {
                    continue;
                }

                foreach (var b in CustomShape.Merge(new string(mask)))
                {
                    boxes.Add(new DeviceFormBox(b.X0 * scale, b.Y0 * scale, b.Z0 * scale, b.X1 * scale, b.Y1 * scale, b.Z1 * scale, slot));
                }
            }

            return new DeviceForm(key, boxes, slots ?? Array.Empty<DeviceFormSlot>());
        }

        /// <summary>Everything wrong with a form, as readable lines (empty = valid): boxes inside the cell with a positive size,
        /// no overlaps, the box budget, every slot defined, every named tile key non-empty, fit regions inside the tile, the
        /// form standing on the floor and every box held by the floor or another box (nothing floats).</summary>
        public static List<string> Problems(DeviceForm form)
        {
            var problems = new List<string>();
            var boxes = form.Boxes;
            if (boxes.Count == 0)
            {
                problems.Add($"{form.Key}: no boxes");
                return problems;
            }

            if (boxes.Count > MaxBoxes)
            {
                problems.Add($"{form.Key}: {boxes.Count} boxes, more than {MaxBoxes}");
            }

            for (int i = 0; i < boxes.Count; i++)
            {
                var b = boxes[i];
                if (b.X0 < 0 || b.Y0 < 0 || b.Z0 < 0 || b.X1 > Grid || b.Y1 > Grid || b.Z1 > Grid
                    || b.X1 <= b.X0 || b.Y1 <= b.Y0 || b.Z1 <= b.Z0)
                {
                    problems.Add($"{form.Key}: box {i} {b} leaves the cell or has no size");
                }

                if (form.SlotOf(b.Slot) == null)
                {
                    problems.Add($"{form.Key}: box {i} names slot {b.Slot}, which the form does not define");
                }

                for (int j = i + 1; j < boxes.Count; j++)
                {
                    if (Overlap(b, boxes[j]))
                    {
                        problems.Add($"{form.Key}: boxes {i} and {j} overlap");
                    }
                }
            }

            CheckSlots(form, problems);
            CheckStanding(form, problems);
            return problems;
        }

        private static void CheckSlots(DeviceForm form, List<string> problems)
        {
            for (int s = 0; s < form.Slots.Count; s++)
            {
                var slot = form.Slots[s];
                if (slot == null)
                {
                    problems.Add($"{form.Key}: slot {s + 1} is null");
                    continue;
                }

                foreach (var tile in slot.Tiles())
                {
                    if (tile.Key != null && tile.Key.Trim().Length == 0)
                    {
                        problems.Add($"{form.Key}: slot {s + 1} names an empty tile key");
                    }

                    if (tile.Mode == DeviceTileMode.Fit && !(InUnit(tile.U0) && InUnit(tile.V0) && InUnit(tile.U1) && InUnit(tile.V1)))
                    {
                        problems.Add($"{form.Key}: slot {s + 1} fits a region outside the tile");
                    }
                }

                if (slot.MinEmission < 0f || slot.MinEmission > 1f)
                {
                    problems.Add($"{form.Key}: slot {s + 1} has an emission outside 0..1");
                }
            }
        }

        private static bool InUnit(float v) => v >= 0f && v <= 1f && !float.IsNaN(v);

        /// <summary>The form stands on the floor and every box is held: it touches the floor or, face to face with a real
        /// area, a box that is held.</summary>
        private static void CheckStanding(DeviceForm form, List<string> problems)
        {
            var boxes = form.Boxes;
            var held = new bool[boxes.Count];
            var queue = new Queue<int>();
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Y0 == 0)
                {
                    held[i] = true;
                    queue.Enqueue(i);
                }
            }

            if (queue.Count == 0)
            {
                problems.Add($"{form.Key}: no box stands on the floor");
                return;
            }

            while (queue.Count > 0)
            {
                int i = queue.Dequeue();
                for (int j = 0; j < boxes.Count; j++)
                {
                    if (!held[j] && Touch(boxes[i], boxes[j]))
                    {
                        held[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }

            for (int i = 0; i < boxes.Count; i++)
            {
                if (!held[i])
                {
                    problems.Add($"{form.Key}: box {i} {boxes[i]} floats (touches neither the floor nor a held box)");
                }
            }
        }

        /// <summary>True when two boxes share volume.</summary>
        public static bool Overlap(DeviceFormBox a, DeviceFormBox b)
            => a.X0 < b.X1 && b.X0 < a.X1 && a.Y0 < b.Y1 && b.Y0 < a.Y1 && a.Z0 < b.Z1 && b.Z0 < a.Z1;

        /// <summary>True when two boxes meet face to face with a real (non-zero) contact area.</summary>
        public static bool Touch(DeviceFormBox a, DeviceFormBox b)
        {
            bool ox = a.X0 < b.X1 && b.X0 < a.X1, oy = a.Y0 < b.Y1 && b.Y0 < a.Y1, oz = a.Z0 < b.Z1 && b.Z0 < a.Z1;
            bool tx = a.X1 == b.X0 || b.X1 == a.X0, ty = a.Y1 == b.Y0 || b.Y1 == a.Y0, tz = a.Z1 == b.Z0 || b.Z1 == a.Z0;
            return (tx && oy && oz) || (ty && ox && oz) || (tz && ox && oy);
        }

        /// <summary>True when the form covers the whole top of the cell at full height — what a ship station's marker block
        /// needs, because the station's decor stands on that face (StationDecorView).</summary>
        public static bool KeepsFullTop(DeviceForm form)
        {
            var top = new bool[Grid * Grid];
            foreach (var b in form.Boxes)
            {
                if (b.Y1 != Grid)
                {
                    continue;
                }

                for (int x = b.X0; x < b.X1; x++)
                {
                    for (int z = b.Z0; z < b.Z1; z++)
                    {
                        top[(z * Grid) + x] = true;
                    }
                }
            }

            return Array.TrueForAll(top, t => t);
        }
    }
}
