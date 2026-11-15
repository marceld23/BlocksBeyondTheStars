// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3646: the structure editor's design holds ONE cell per place, a template may hold two there — a marker (a chest, a sound,
    /// a den, a prop) standing in a cavity or an air pocket: a chest in a carved tunnel, a chest in a diving bell. Loading gave the
    /// place to the carve and skipped the marker, so a save lost it (the old mine, the carrier, the giant dwelling). Now the marker
    /// keeps its place in the design, the carve under it is kept aside (<see cref="Under"/>) and written back on save
    /// (<see cref="WriteBack"/>) — under every marker that still stands there.
    /// </summary>
    public static class MarkerCarves
    {
        /// <summary>The places where a listed marker of <paramref name="t"/> stands in one of its plain cavities or air pockets,
        /// with that carve as the template holds it (its kind, keep flag and modifiers).</summary>
        public static Dictionary<(int X, int Y, int Z), TemplateBody.Plain> Under(StructureTemplate t)
        {
            if (t is null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            var markers = new HashSet<(int, int, int)>();
            foreach (var c in t.Cells)
            {
                if (c != null && c.Kind == "marker")
                {
                    markers.Add((c.X, c.Y, c.Z));
                }
            }

            var carves = new Dictionary<(int X, int Y, int Z), TemplateBody.Plain>();
            if (markers.Count == 0)
            {
                return carves;
            }

            TemplateBody.ForEachPlain(t, p =>
            {
                if (p.Cavity && markers.Contains((p.X, p.Y, p.Z)))
                {
                    carves[(p.X, p.Y, p.Z)] = p; // an air pocket is a cavity too
                }
            });
            return carves;
        }

        /// <summary>Adds the kept carve under every marker of <paramref name="t"/> (cells listed, before the compact body is
        /// packed) that still stands on one of the <paramref name="carves"/> places. A place whose marker was removed or replaced
        /// is written as the design holds it now.</summary>
        public static void WriteBack(StructureTemplate t, IReadOnlyDictionary<(int X, int Y, int Z), TemplateBody.Plain>? carves)
        {
            if (t is null)
            {
                throw new ArgumentNullException(nameof(t));
            }

            if (carves is null || carves.Count == 0)
            {
                return;
            }

            var added = new List<TemplateCell>();
            var done = new HashSet<(int, int, int)>();
            foreach (var c in t.Cells)
            {
                if (c != null && c.Kind == "marker" && carves.TryGetValue((c.X, c.Y, c.Z), out var p) && done.Add((c.X, c.Y, c.Z)))
                {
                    added.Add(new TemplateCell
                    {
                        X = c.X,
                        Y = c.Y,
                        Z = c.Z,
                        Kind = CivilizationEditorRules.DesignKind(p),
                        Tint = p.Tint,
                        Glow = p.Glow,
                        Shape = p.Shape,
                        Keep = p.Keep,
                    });
                }
            }

            t.Cells.AddRange(added);
        }
    }
}
