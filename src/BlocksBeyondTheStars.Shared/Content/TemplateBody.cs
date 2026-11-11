// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3485: the COMPACT BODY of a structure template. A template lists one JSON object per cell — about 120 bytes each
/// on disk and an object in memory — which is fine for a house of 700 cells and hopeless for a landmark of 100 000.
/// The compact body stores the plain cells (a block or a cavity with its tint, glow, shape and keep flag) as a
/// <see cref="StructureTemplate.Palette"/> plus run-length <see cref="StructureTemplate.Rows"/>: one string per
/// (y, z) row, <c>"y,z:x.count.palette x.count.palette"</c>, rows ordered by y then z, runs by x. Cells that carry
/// settings — markers, ports, Crystal Net devices — stay in <see cref="StructureTemplate.Cells"/>.
/// <para>A template may hold both forms; <see cref="ForEachPlain"/> walks every plain cell of either, so readers never
/// have to expand a large body into cell objects.</para>
/// </summary>
public static class TemplateBody
{
    /// <summary>One plain cell as the readers see it.</summary>
    public readonly struct Plain
    {
        public Plain(int x, int y, int z, bool cavity, string id, int tint, int glow, int shape, bool keep)
        {
            X = x;
            Y = y;
            Z = z;
            Cavity = cavity;
            Id = id;
            Tint = tint;
            Glow = glow;
            Shape = shape;
            Keep = keep;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public bool Cavity { get; }

        public string Id { get; }

        public int Tint { get; }

        public int Glow { get; }

        public int Shape { get; }

        public bool Keep { get; }
    }

    /// <summary>Whether a cell is PLAIN — a block or a cavity without settings — and so may live in the compact body.</summary>
    public static bool IsPlain(TemplateCell c)
        => (c.Kind == "block" || c.Kind == LandmarkRules.CavityKind)
           && c.Port.Length == 0 && c.Mode == 0 && c.Config.Length == 0 && c.Label.Length == 0;

    /// <summary>Whether the template carries a compact body.</summary>
    public static bool IsCompact(StructureTemplate t) => t.Rows.Count > 0;

    /// <summary>Calls <paramref name="visit"/> for every plain cell — the compact rows first (in row order), then the
    /// plain cells of <see cref="StructureTemplate.Cells"/>. A malformed row or run is skipped.</summary>
    public static void ForEachPlain(StructureTemplate t, Action<Plain> visit)
    {
        var palette = t.Palette;
        foreach (string row in t.Rows)
        {
            if (!TryParseRowHead(row, out int y, out int z, out int at))
            {
                continue;
            }

            while (TryParseRun(row, ref at, out int x, out int count, out int p))
            {
                if (p < 0 || p >= palette.Count || count <= 0)
                {
                    continue;
                }

                var e = palette[p];
                bool cavity = e.Kind == LandmarkRules.CavityKind;
                for (int i = 0; i < count; i++)
                {
                    visit(new Plain(x + i, y, z, cavity, e.Id, e.Tint, e.Glow, e.Shape, e.Keep));
                }
            }
        }

        foreach (var c in t.Cells)
        {
            if (c.Kind == "block" || c.Kind == LandmarkRules.CavityKind)
            {
                visit(new Plain(c.X, c.Y, c.Z, c.Kind == LandmarkRules.CavityKind, c.Id, c.Tint, c.Glow, c.Shape, c.Keep));
            }
        }
    }

    /// <summary>How many cells the template holds, in either form (markers included).</summary>
    public static int CellCount(StructureTemplate t)
    {
        int n = t.Cells.Count;
        foreach (string row in t.Rows)
        {
            if (!TryParseRowHead(row, out _, out _, out int at))
            {
                continue;
            }

            while (TryParseRun(row, ref at, out _, out int count, out _))
            {
                n += Math.Max(0, count);
            }
        }

        return n;
    }

    /// <summary>Moves every plain cell of <see cref="StructureTemplate.Cells"/> into the compact body (replacing an
    /// existing one — the cells are the source). Cells with settings stay where they are, in their order.</summary>
    public static void Compact(StructureTemplate t)
    {
        var all = new List<Plain>();
        ForEachPlain(t, all.Add); // an existing compact body is folded in: Compact is idempotent
        var kept = new List<TemplateCell>();
        foreach (var c in t.Cells)
        {
            if (!IsPlain(c))
            {
                kept.Add(c);
            }
        }

        // A cell with settings on a block wins over a plain cell at the same place (it IS that block).
        var taken = new HashSet<(int, int, int)>();
        foreach (var c in kept)
        {
            if (c.Kind != "marker")
            {
                taken.Add((c.X, c.Y, c.Z));
            }
        }

        // Last write wins per cell, like the cell list itself.
        var byCell = new Dictionary<(int, int, int), Plain>();
        foreach (var p in all)
        {
            if (!taken.Contains((p.X, p.Y, p.Z)))
            {
                byCell[(p.X, p.Y, p.Z)] = p;
            }
        }

        var sorted = new List<Plain>(byCell.Values);
        sorted.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));

        var palette = new List<TemplatePaletteEntry>();
        var index = new Dictionary<(bool, string, int, int, int, bool), int>();
        var rows = new List<string>();
        var sb = new StringBuilder();
        int i = 0;
        while (i < sorted.Count)
        {
            int y = sorted[i].Y, z = sorted[i].Z;
            sb.Clear();
            sb.Append(y.ToString(CultureInfo.InvariantCulture)).Append(',').Append(z.ToString(CultureInfo.InvariantCulture)).Append(':');
            bool first = true;
            while (i < sorted.Count && sorted[i].Y == y && sorted[i].Z == z)
            {
                var start = sorted[i];
                var key = (start.Cavity, start.Id, start.Tint, start.Glow, start.Shape, start.Keep);
                if (!index.TryGetValue(key, out int p))
                {
                    p = palette.Count;
                    index[key] = p;
                    palette.Add(new TemplatePaletteEntry
                    {
                        Kind = start.Cavity ? LandmarkRules.CavityKind : "block",
                        Id = start.Id,
                        Tint = start.Tint,
                        Glow = start.Glow,
                        Shape = start.Shape,
                        Keep = start.Keep,
                    });
                }

                int count = 1;
                while (i + count < sorted.Count)
                {
                    var next = sorted[i + count];
                    if (next.Y != y || next.Z != z || next.X != start.X + count
                        || (next.Cavity, next.Id, next.Tint, next.Glow, next.Shape, next.Keep) != key)
                    {
                        break;
                    }

                    count++;
                }

                if (!first)
                {
                    sb.Append(' ');
                }

                first = false;
                sb.Append(start.X.ToString(CultureInfo.InvariantCulture)).Append('.')
                    .Append(count.ToString(CultureInfo.InvariantCulture)).Append('.')
                    .Append(p.ToString(CultureInfo.InvariantCulture));
                i += count;
            }

            rows.Add(sb.ToString());
        }

        t.Palette = palette;
        t.Rows = rows;
        t.Cells = kept;
    }

    /// <summary>The inverse of <see cref="Compact"/>: every plain cell becomes a <see cref="TemplateCell"/> again (the
    /// compact rows first, then the cells that were listed) and the compact body is cleared.</summary>
    public static void Expand(StructureTemplate t)
    {
        if (!IsCompact(t))
        {
            return;
        }

        var listed = t.Cells;
        var cells = new List<TemplateCell>();
        var rowsOnly = new StructureTemplate { Palette = t.Palette, Rows = t.Rows };
        ForEachPlain(rowsOnly, p => cells.Add(new TemplateCell
        {
            X = p.X,
            Y = p.Y,
            Z = p.Z,
            Kind = p.Cavity ? LandmarkRules.CavityKind : "block",
            Id = p.Id,
            Tint = p.Tint,
            Glow = p.Glow,
            Shape = p.Shape,
            Keep = p.Keep,
        }));
        cells.AddRange(listed);
        t.Cells = cells;
        t.Palette = new List<TemplatePaletteEntry>();
        t.Rows = new List<string>();
    }

    // --- row parsing: "y,z:x.count.palette x.count.palette" ---

    private static bool TryParseRowHead(string row, out int y, out int z, out int at)
    {
        y = z = 0;
        at = 0;
        if (string.IsNullOrEmpty(row))
        {
            return false;
        }

        if (!TryReadInt(row, ref at, out y) || at >= row.Length || row[at] != ',')
        {
            return false;
        }

        at++;
        if (!TryReadInt(row, ref at, out z) || at >= row.Length || row[at] != ':')
        {
            return false;
        }

        at++;
        return true;
    }

    /// <summary>Reads the next run; false at the end of the row. A malformed run ends the row (count 0 is skipped by
    /// the callers).</summary>
    private static bool TryParseRun(string row, ref int at, out int x, out int count, out int palette)
    {
        x = count = palette = 0;
        while (at < row.Length && row[at] == ' ')
        {
            at++;
        }

        if (at >= row.Length)
        {
            return false;
        }

        if (!TryReadInt(row, ref at, out x) || at >= row.Length || row[at] != '.')
        {
            at = row.Length;
            return false;
        }

        at++;
        if (!TryReadInt(row, ref at, out count) || at >= row.Length || row[at] != '.')
        {
            at = row.Length;
            return false;
        }

        at++;
        if (!TryReadInt(row, ref at, out palette))
        {
            at = row.Length;
            return false;
        }

        return true;
    }

    private static bool TryReadInt(string s, ref int at, out int value)
    {
        value = 0;
        int start = at;
        bool negative = at < s.Length && s[at] == '-';
        if (negative)
        {
            at++;
        }

        long v = 0;
        int digits = 0;
        while (at < s.Length && s[at] >= '0' && s[at] <= '9')
        {
            v = (v * 10) + (s[at] - '0');
            if (v > int.MaxValue)
            {
                at = start;
                return false;
            }

            at++;
            digits++;
        }

        if (digits == 0)
        {
            at = start;
            return false;
        }

        value = negative ? -(int)v : (int)v;
        return true;
    }
}
