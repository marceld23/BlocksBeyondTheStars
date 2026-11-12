// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A stable 64-bit fingerprint for the structure goldens (#3511): FNV-1a over 64-bit words, each word first run through
/// the SplitMix64 finaliser (<see cref="StableHashing.Mix64"/>) so every bit of it reaches the whole hash. Blocks are
/// hashed by their KEY, never their numeric id — the ids follow the key order, so adding any block would otherwise move
/// every golden although no structure changed (the rule of <c>WorldGenerationGoldenTests</c>). Never feed it
/// <c>GetHashCode</c> or a float.
/// </summary>
internal sealed class StructureGoldenHash
{
    private const ulong Prime = 1099511628211UL;

    private readonly GameContent _content;
    private ulong _h = 14695981039346656037UL;

    public StructureGoldenHash(GameContent content) => _content = content;

    public ulong Value => _h;

    public StructureGoldenHash Add(ulong v)
    {
        unchecked
        {
            _h = (_h ^ StableHashing.Mix64(v)) * Prime;
        }

        return this;
    }

    public StructureGoldenHash Add(long v) => Add(unchecked((ulong)v));

    public StructureGoldenHash Add(int v) => Add(unchecked((ulong)(long)v));

    public StructureGoldenHash Add(bool v) => Add(v ? 1UL : 2UL);

    /// <summary>A string, terminated so "ab"+"c" and "a"+"bc" differ.</summary>
    public StructureGoldenHash Add(string? s)
    {
        Add(StableHashing.Fnv64(s ?? "\u0000null"));
        return Add(s?.Length ?? -1);
    }

    public StructureGoldenHash Add(Vector3i p) => Add(p.X).Add(p.Y).Add(p.Z);

    /// <summary>A block by its key (air and unknown ids hash as their number, tagged so they never collide with a key).</summary>
    public StructureGoldenHash Block(ushort id)
    {
        if (id == 0)
        {
            return Add(0UL);
        }

        string? key = _content.BlockById(new BlockId(id))?.Key;
        return key is null ? Add(0xB10C_0000_0000_0000UL | id) : Add(key);
    }

    /// <summary>One cell: block (by key), tint, glow, shape.</summary>
    public StructureGoldenHash Cell(ushort block, int tint, int glow, int shape)
        => Block(block).Add(tint).Add(glow).Add(shape);

    /// <summary>A box of cells in x, y, z order: the size, then every cell.</summary>
    public StructureGoldenHash Grid(int w, int h, int l, Func<int, int, int, ushort> get,
        Func<int, int, int, (int Tint, int Glow)>? mod = null, Func<int, int, int, int>? shape = null)
    {
        Add(w).Add(h).Add(l);
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                for (int z = 0; z < l; z++)
                {
                    var (tint, glow) = mod?.Invoke(x, y, z) ?? (0, 0);
                    Cell(get(x, y, z), tint, glow, shape?.Invoke(x, y, z) ?? 0);
                }
            }
        }

        return this;
    }

    public string Hex => _h.ToString("x16", CultureInfo.InvariantCulture);
}

/// <summary>Fingerprints of every generator output the structure goldens pin (#3511), and the pinned-table check.</summary>
internal static class StructureGoldens
{
    public static StructureGoldenHash Settlement(StructureGoldenHash h, SettlementStructure s)
    {
        h.Add(s.Tier).Add(s.Ruined).Add(s.Inhabitant).Add(s.BuildingCount);
        h.Grid(s.Width, s.Height, s.Length, s.Get, s.GetModifier, s.GetShape);
        h.Add(s.Markers.Count);
        foreach (var m in s.Markers)
        {
            h.Add(m.Type).Add(m.LocalPos).Add((int)m.DoorAxis).Add(m.Data);
        }

        return h;
    }

    public static ulong Settlement(GameContent content, SettlementStructure s) => Settlement(new StructureGoldenHash(content), s).Value;

    public static ulong Station(GameContent content, StationStructure s)
    {
        var h = new StructureGoldenHash(content);
        h.Add(s.SizeTier).Add(s.RoomW).Add(s.RoomH).Add(s.RoomL).Add(s.ModuleShift);
        h.Grid(s.Width, s.Height, s.Length, s.Get, s.GetModifier, s.GetShape);
        h.Add(s.Markers.Count);
        foreach (var m in s.Markers)
        {
            h.Add(m.Type).Add(m.LocalPos).Add(m.Data);
        }

        h.Add(s.Modules.Count);
        foreach (var m in s.Modules)
        {
            h.Add(m.Type).Add(m.Grid).Add(m.Origin);
        }

        return h.Value;
    }

    public static ulong Wreck(GameContent content, WreckStructure s)
    {
        var h = new StructureGoldenHash(content);
        h.Add(s.ShipType).Add(s.Origin);
        h.Grid(s.Width, s.Height, s.Length, s.Get);
        h.Grid(s.Width, s.Height, s.Length, s.IntactAt);
        h.Add(s.Markers.Count);
        foreach (var m in s.Markers)
        {
            h.Add(m.Type).Add(m.LocalPos);
        }

        return h.Value;
    }

    public static ulong Derelict(GameContent content, DerelictLayout l)
    {
        var h = new StructureGoldenHash(content);
        h.Add(l.Source).Add(l.SizeTier).Add(l.Arrival).Add(l.ArrivalSealed).Add(l.SpawnerSpot.HasValue);
        h.Add(l.SpawnerSpot ?? default);
        h.Add(l.RoomCount).Add(l.SealedRoomCount).Add(l.BreachedRoomCount);
        h.Add(l.CrateSpots.Count);
        foreach (var c in l.CrateSpots)
        {
            h.Add(c);
        }

        h.Add(l.TraderPosts.Count);
        foreach (var p in l.TraderPosts)
        {
            h.Add(p);
        }

        h.Add(l.Doors.Count);
        foreach (var d in l.Doors)
        {
            h.Add(d.Cell).Add(d.AxisX);
        }

        return h.Grid(l.Width, l.Height, l.Length, l.Get, l.GetModifier, l.GetShape).Value;
    }

    public static ulong AncientRuin(GameContent content, AncientRuinLayout r)
    {
        var h = new StructureGoldenHash(content);
        h.Add(r.IsGdsRuin).Add(r.KitKey).Add(r.SourceOffset).Add(r.Shaft.X).Add(r.Shaft.Z).Add(r.DoorFace).Add(r.RessawBlocks);
        // The layout field by field: an enclosed structure carries the default struct (no role map at all).
        var spec = r.Layout;
        h.Add(spec.Grid).Add(spec.DistrictSize).Add(spec.Street).Add(spec.Height).Add(spec.RoleMap?.Count ?? -1);
        foreach (string role in spec.RoleMap ?? Array.Empty<string>())
        {
            h.Add(role);
        }

        h.Add(r.Composition.Count);
        foreach (string c in r.Composition)
        {
            h.Add(c);
        }

        h.Add(r.BasinColumns.Count);
        foreach (var (x, z) in r.BasinColumns)
        {
            h.Add(x).Add(z);
        }

        h.Add(r.OreCells.Count);
        foreach (var c in r.OreCells)
        {
            h.Add(c);
        }

        h.Add(r.Caches.Count);
        foreach (var c in r.Caches)
        {
            h.Add(c);
        }

        h.Add(r.Props.Count);
        foreach (var (cell, block) in r.Props)
        {
            h.Add(cell).Block(block);
        }

        if (r.Source is { } source)
        {
            Settlement(h, source);
        }

        return h.Grid(r.Width, r.Height, r.Length, r.Get, r.GetModifier, r.GetShape).Value;
    }

    public static ulong Landmark(GameContent content, LandmarkLayout l)
    {
        var h = new StructureGoldenHash(content);
        h.Add(l.GroundRow).Add(l.Ruined).Add(l.Turns).Add(l.CavityCount);
        var s = l.Structure;
        h.Grid(s.Width, s.Height, s.Length, (x, y, z) => (ushort)(l.IsCavity(x, y, z) ? 1 : 0));
        return Settlement(h, s).Value;
    }

    public static ulong Tablet(GameContent content, TextTablet t)
    {
        var h = new StructureGoldenHash(content);
        h.Add(t.Lines.Count);
        foreach (string line in t.Lines)
        {
            h.Add(line);
        }

        h.Grid(t.Width, t.Height, t.Depth, t.Get);
        return h.Grid(t.Width, t.Height, t.Depth, (x, y, z) => (ushort)((t.IsLetter(x, y, z) ? 1 : 0) | (t.IsLamp(x, y, z) ? 2 : 0))).Value;
    }

    public static ulong Outpost(GameContent content, PioneerOutpostGenerator.Outpost o)
    {
        var h = new StructureGoldenHash(content);
        h.Add(o.WallKind).Block(o.WallBlock).Add(o.HiddenShaft).Add(o.Chests);
        h.Add(o.Below.Count);
        foreach (var (local, block, shape) in o.Below)
        {
            h.Add(local).Block(block).Add(shape);
        }

        h.Add(o.BelowMarkers.Count);
        foreach (var m in o.BelowMarkers)
        {
            h.Add(m.Type).Add(m.LocalPos).Add((int)m.DoorAxis).Add(m.Data);
        }

        return Settlement(h, o.Structure).Value;
    }

    /// <summary>Cells written as a list (a boat, a camp, a direct writer): position, block, tint, glow, shape — sorted.</summary>
    public static ulong Cells(GameContent content, IEnumerable<(Vector3i Cell, ushort Block, int Tint, int Glow, int Shape)> cells)
    {
        var h = new StructureGoldenHash(content);
        var sorted = cells.OrderBy(c => c.Cell.X).ThenBy(c => c.Cell.Y).ThenBy(c => c.Cell.Z).ToList();
        h.Add(sorted.Count);
        foreach (var c in sorted)
        {
            h.Add(c.Cell).Cell(c.Block, c.Tint, c.Glow, c.Shape);
        }

        return h.Value;
    }

    /// <summary>A sorted list of strings (keys, record lines).</summary>
    public static ulong Strings(GameContent content, IEnumerable<string> lines)
    {
        var h = new StructureGoldenHash(content);
        var sorted = lines.OrderBy(s => s, StringComparer.Ordinal).ToList();
        h.Add(sorted.Count);
        foreach (string s in sorted)
        {
            h.Add(s);
        }

        return h.Value;
    }

    /// <summary>
    /// Compares the computed fingerprints with the pinned ones. Every case is checked; a failure lists each mismatch and
    /// ends with the whole table as computed on this machine, ready to paste — the "print mode" for a deliberate re-pin
    /// (which, for terrain generation ≤ 37, must never be needed: these goldens guard the structure refactor of #3510).
    /// <paramref name="linux"/> holds Linux-only values for a case whose output passes through <c>Math.Sin/Cos</c> and
    /// differs on that libm (none so far); a case absent there uses the shared value.
    /// </summary>
    public static void AssertPinned(string table, IReadOnlyDictionary<string, string> pinned, IReadOnlyList<(string Case, ulong Hash)> computed,
        IReadOnlyDictionary<string, string>? linux = null, string scope = "", string note = "")
    {
        var failures = new List<string>();
        var report = new StringBuilder();
        foreach (var (name, hash) in computed)
        {
            string hex = hash.ToString("x16", CultureInfo.InvariantCulture);
            report.Append("        [\"").Append(name).Append("\"] = \"").Append(hex).AppendLine("\",");
            string? expected = OperatingSystem.IsLinux() && linux is not null && linux.TryGetValue(name, out var l) ? l
                : pinned.TryGetValue(name, out var p) ? p : null;
            if (expected is null)
            {
                failures.Add($"{name}: not pinned (got {hex})");
            }
            else if (!string.Equals(expected, hex, StringComparison.Ordinal))
            {
                failures.Add($"{name}: expected {expected}, got {hex}");
            }
        }

        // Only the pins of this table's scope (a theory case checks its own prefix).
        foreach (string stale in pinned.Keys.Where(k => k.StartsWith(scope, StringComparison.Ordinal) && computed.All(c => c.Case != k)))
        {
            failures.Add($"{stale}: pinned but no longer computed");
        }

        Xunit.Assert.True(failures.Count == 0,
            $"{table}: {failures.Count} structure golden(s) moved (#3511):\n" + string.Join("\n", failures)
            + "\n\nA structure refactor must keep these byte-identical for terrain generation <= 37. Computed table:\n" + report
            + (note.Length > 0 ? "\n" + note : string.Empty));
    }
}
