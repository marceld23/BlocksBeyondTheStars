// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>One cell of a ship as the editor exports it (layout coordinates): what <see cref="ShippedShipMatch"/> compares.
    /// An empty kind reads as <c>block</c>, like everywhere else a layout is read.</summary>
    public readonly struct CreationCell : IEquatable<CreationCell>
    {
        public CreationCell(int x, int y, int z, string? kind, string? id, int tint, int glow, int shape)
        {
            X = x;
            Y = y;
            Z = z;
            Kind = string.IsNullOrEmpty(kind) ? "block" : kind!;
            Id = id ?? string.Empty;
            Tint = tint;
            Glow = glow;
            Shape = shape;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public string Kind { get; }

        public string Id { get; }

        public int Tint { get; }

        public int Glow { get; }

        public int Shape { get; }

        public static bool operator ==(CreationCell left, CreationCell right) => left.Equals(right);

        public static bool operator !=(CreationCell left, CreationCell right) => !left.Equals(right);

        /// <summary>The same cell moved by (−<paramref name="dx"/>, −<paramref name="dy"/>, −<paramref name="dz"/>).</summary>
        public CreationCell Shifted(int dx, int dy, int dz) => new(X - dx, Y - dy, Z - dz, Kind, Id, Tint, Glow, Shape);

        public bool Equals(CreationCell other)
            => X == other.X && Y == other.Y && Z == other.Z && Tint == other.Tint && Glow == other.Glow && Shape == other.Shape
               && string.Equals(Kind, other.Kind, StringComparison.Ordinal) && string.Equals(Id, other.Id, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CreationCell other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(X, Y, Z, StringComparer.Ordinal.GetHashCode(Kind ?? string.Empty),
                StringComparer.Ordinal.GetHashCode(Id ?? string.Empty), Tint, Glow, Shape);
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

                mine ??= Normalised(design);
                if (mine.SetEquals(Normalised(Of(layout))))
                {
                    return key;
                }
            }

            return string.Empty;
        }

        /// <summary>True when <paramref name="a"/> and <paramref name="b"/> are the same build (see the class summary).</summary>
        public static bool SameBuild(IReadOnlyCollection<CreationCell> a, IReadOnlyCollection<CreationCell> b)
            => a is not null && b is not null && a.Count == b.Count && Normalised(a).SetEquals(Normalised(b));

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

        /// <summary>The cells moved so the smallest x, y and z are 0: a build shifted as a whole compares equal.</summary>
        private static HashSet<CreationCell> Normalised(IEnumerable<CreationCell> cells)
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
}
