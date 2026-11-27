// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>One cell of a ship or a station / town module as the editor exports it: what <see cref="ShippedShipMatch"/>
    /// and <see cref="ShippedTemplateMatch"/> compare. An empty kind reads as <c>block</c>, like everywhere else a layout is
    /// read. A template cell's device settings (port, mode, config, label, keep — #4067) are one <see cref="Detail"/> string;
    /// a ship cell has none.</summary>
    public readonly struct CreationCell : IEquatable<CreationCell>
    {
        /// <summary>Separates the parts of <see cref="Detail"/> (a control character no setting or label contains).</summary>
        private const char DetailSeparator = '\u001f';

        public CreationCell(int x, int y, int z, string? kind, string? id, int tint, int glow, int shape, string? detail = null)
        {
            X = x;
            Y = y;
            Z = z;
            Kind = string.IsNullOrEmpty(kind) ? "block" : kind!;
            Id = id ?? string.Empty;
            Tint = tint;
            Glow = glow;
            Shape = shape;
            Detail = detail ?? string.Empty;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public string Kind { get; }

        public string Id { get; }

        public int Tint { get; }

        public int Glow { get; }

        public int Shape { get; }

        /// <summary>A template cell's device settings in one string (<see cref="TemplateDetail"/>); empty for a ship cell.</summary>
        public string Detail { get; }

        public static bool operator ==(CreationCell left, CreationCell right) => left.Equals(right);

        public static bool operator !=(CreationCell left, CreationCell right) => !left.Equals(right);

        /// <summary>The <see cref="Detail"/> of a template cell: its port, device mode, config, label and keep flag.</summary>
        public static string TemplateDetail(string? port, int mode, string? config, string? label, bool keep)
            => string.Join(DetailSeparator.ToString(), port ?? string.Empty, mode.ToString(CultureInfo.InvariantCulture),
                config ?? string.Empty, label ?? string.Empty, keep ? "1" : "0");

        /// <summary>The same cell moved by (−<paramref name="dx"/>, −<paramref name="dy"/>, −<paramref name="dz"/>).</summary>
        public CreationCell Shifted(int dx, int dy, int dz) => new(X - dx, Y - dy, Z - dz, Kind, Id, Tint, Glow, Shape, Detail);

        public bool Equals(CreationCell other)
            => X == other.X && Y == other.Y && Z == other.Z && Tint == other.Tint && Glow == other.Glow && Shape == other.Shape
               && string.Equals(Kind, other.Kind, StringComparison.Ordinal) && string.Equals(Id, other.Id, StringComparison.Ordinal)
               && string.Equals(Detail, other.Detail, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CreationCell other && Equals(other);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(X);
            hash.Add(Y);
            hash.Add(Z);
            hash.Add(Kind ?? string.Empty, StringComparer.Ordinal);
            hash.Add(Id ?? string.Empty, StringComparer.Ordinal);
            hash.Add(Tint);
            hash.Add(Glow);
            hash.Add(Shape);
            hash.Add(Detail ?? string.Empty, StringComparer.Ordinal);
            return hash.ToHashCode();
        }

        /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same build: every cell matches, and a shift of
        /// the whole build is ignored.</summary>
        public static bool SameBuild(IReadOnlyCollection<CreationCell> a, IReadOnlyCollection<CreationCell> b)
            => a is not null && b is not null && a.Count == b.Count && Normalised(a).SetEquals(Normalised(b));

        /// <summary>The cells moved so the smallest x, y and z are 0: a build shifted as a whole compares equal.</summary>
        internal static HashSet<CreationCell> Normalised(IEnumerable<CreationCell> cells)
        {
            int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue;
            foreach (var c in cells)
            {
                minX = Math.Min(minX, c.X);
                minY = Math.Min(minY, c.Y);
                minZ = Math.Min(minZ, c.Z);
            }

            var set = new HashSet<CreationCell>();
            foreach (var c in cells)
            {
                set.Add(c.Shifted(minX, minY, minZ));
            }

            return set;
        }
    }

    /// <summary>
    /// #4064, #4065: is a Ship Designer build still one of the shipped ships, block for block? The first ship submission in
    /// the inbox was the shipped Hammerhead with only its stats turned up — nothing a reviewer can adopt, and its
    /// <c>basedOn</c> was empty because the editor had forgotten where the ship came from. Two uses:
    /// <list type="bullet">
    /// <item><b>Submit</b> refuses such a build with a friendly line (<c>ui.submit.unchanged_ship</c>), like an empty one.</item>
    /// <item><b>basedOn</b> falls back to the matching ship, so a ship saved before the editor kept its base still names it.</item>
    /// </list>
    /// Two builds are the same when every cell matches in position, kind, id, tint, glow and shape; a shift of the whole build
    /// is ignored. Stats, texts and the cabin frame are not part of the build — a single painted or added block is a change.
    /// </summary>
    public static class ShippedShipMatch
    {
        /// <summary>The key of the shipped ship (<see cref="GameContent.Ships"/>, in key order) whose layout holds exactly the
        /// cells of <paramref name="design"/>; empty when there is none (or no content, or nothing built).</summary>
        public static string Find(IReadOnlyCollection<CreationCell> design, GameContent? content)
        {
            if (design is null || design.Count == 0 || content is null)
            {
                return string.Empty;
            }

            var keys = new List<string>(content.Ships.Keys);
            keys.Sort(StringComparer.Ordinal);
            HashSet<CreationCell>? mine = null;
            foreach (var key in keys)
            {
                var layout = content.GetShipLayout(content.Ships[key].Layout);
                if (layout is null || layout.Cells.Count != design.Count)
                {
                    continue; // a different number of cells is a different build (and the cheap test comes first)
                }

                mine ??= CreationCell.Normalised(design);
                if (mine.SetEquals(CreationCell.Normalised(Of(layout))))
                {
                    return key;
                }
            }

            return string.Empty;
        }

        /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same build (see the class summary).</summary>
        public static bool SameBuild(IReadOnlyCollection<CreationCell> a, IReadOnlyCollection<CreationCell> b) => CreationCell.SameBuild(a, b);

        /// <summary>The cells of a layout file in the shape the editor exports them.</summary>
        public static List<CreationCell> Of(ShipLayout layout)
        {
            var cells = new List<CreationCell>(layout?.Cells?.Count ?? 0);
            foreach (var c in layout?.Cells ?? new List<ShipLayoutCell>())
            {
                cells.Add(new CreationCell(c.X, c.Y, c.Z, c.Kind, c.Id, c.Tint, c.Glow, c.Shape));
            }

            return cells;
        }
    }

    /// <summary>
    /// #4067: the Station / Village editor's twin of <see cref="ShippedShipMatch"/> — is a build still one of the shipped
    /// station or town templates, cell for cell? Submit refuses it kindly (<c>ui.submit.unchanged_structure</c>), and
    /// <c>basedOn</c> falls back to it. The cells are compared as Save exports them: position, kind, id, tint, glow, shape and
    /// the device settings (port, mode, config, label, keep); a shift of the whole build is ignored. A renamed template or
    /// another tier is not a change; one changed block, sign text or device setting is.
    /// </summary>
    public static class ShippedTemplateMatch
    {
        /// <summary>The key of the first template of <paramref name="shipped"/> (in key order; generator entries without cells are
        /// skipped) that holds exactly the cells of <paramref name="design"/>; empty when there is none.</summary>
        public static string Find(IReadOnlyCollection<CreationCell> design, IEnumerable<StructureTemplate>? shipped)
        {
            if (design is null || design.Count == 0 || shipped is null)
            {
                return string.Empty;
            }

            var sorted = new List<StructureTemplate>(shipped);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            HashSet<CreationCell>? mine = null;
            foreach (var t in sorted)
            {
                if (t?.Cells is null || t.Cells.Count != design.Count)
                {
                    continue; // a different number of cells is a different build (and the cheap test comes first)
                }

                mine ??= CreationCell.Normalised(design);
                if (mine.SetEquals(CreationCell.Normalised(Of(t))))
                {
                    return t.Key;
                }
            }

            return string.Empty;
        }

        /// <summary>The cells of a template in the shape the editor exports them.</summary>
        public static List<CreationCell> Of(StructureTemplate template)
        {
            var cells = new List<CreationCell>(template?.Cells?.Count ?? 0);
            foreach (var c in template?.Cells ?? new List<TemplateCell>())
            {
                cells.Add(Cell(c.X, c.Y, c.Z, c.Kind, c.Id, c.Tint, c.Glow, c.Shape, c.Port, c.Mode, c.Config, c.Label, c.Keep));
            }

            return cells;
        }

        /// <summary>One template cell with its device settings — the shape both the template and the editor's export map to.</summary>
        public static CreationCell Cell(int x, int y, int z, string? kind, string? id, int tint, int glow, int shape, string? port, int mode,
            string? config, string? label, bool keep)
            => new(x, y, z, kind, id, tint, glow, shape, CreationCell.TemplateDetail(port, mode, config, label, keep));
    }
}
