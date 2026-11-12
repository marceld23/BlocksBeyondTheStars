// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The footprint registry of one resident world (#3514): every stamper registers the ground box of what it stamped — when it
/// stamps it and when a later load replays it — and the landing pads and the planned crash site are entries too. Terrain
/// generation 38+ asks this one registry wherever a structure keeps clear of the others; older worlds keep the checks they
/// were stamped with (the registry is filled there as well, but nothing reads it).
/// <para>A box is a centre and half extents (the shape every keep-clear list used): it covers |dx| &lt; Hw, |dz| &lt; Hl around
/// the centre, X wrapped around the world, Z not. Two boxes keep clear when they are at least the margin apart on one axis —
/// the larger of the entry's own margin and the one the asker keeps from that kind
/// (<see cref="Shared.Definitions.StructureTypeDefinition.MarginFrom"/>).</para>
/// <para>Because the stampers run in the same order on every load and register on every load, the registry holds the same
/// boxes at every query on a replay as on the first stamp — a check that draws from a random stream (the chests) draws the
/// same numbers again.</para>
/// </summary>
internal sealed class StructureFootprints
{
    /// <summary>One registered box: its kind, centre, half extents, the margin it keeps, and the structure it belongs to (one
    /// structure may register several boxes — the intercity line's stations and spans, an outpost and its boat).</summary>
    internal readonly record struct Footprint(string Kind, int Cx, int Cz, int Hw, int Hl, int Margin, string Owner)
    {
        public int MinX => Cx - Hw;
        public int MaxX => Cx + Hw;
        public int MinZ => Cz - Hl;
        public int MaxZ => Cz + Hl;
    }

    private readonly List<Footprint> _entries = new();

    /// <summary>The world's circumference (the X wrap); 0 = no wrap.</summary>
    public int Circumference { get; set; }

    /// <summary>Every registered box, in registration order.</summary>
    public IReadOnlyList<Footprint> Entries => _entries;

    public void Clear() => _entries.Clear();

    /// <summary>Registers a box given by its corners (inclusive cells): centre (min + max) / 2, half extents (max − min) / 2 + 1.</summary>
    public void Add(string kind, int minX, int minZ, int maxX, int maxZ, int margin, string? owner = null)
        => Add(kind, ((minX + maxX) / 2, (minZ + maxZ) / 2, ((maxX - minX) / 2) + 1, ((maxZ - minZ) / 2) + 1), margin, owner);

    /// <summary>Registers a box given as a centre and half extents.</summary>
    public void Add(string kind, (int Cx, int Cz, int Hw, int Hl) rect, int margin, string? owner = null)
        => _entries.Add(new Footprint(kind, rect.Cx, rect.Cz, rect.Hw, rect.Hl, margin, owner ?? kind + "#" + _entries.Count));

    /// <summary>Drops every box of a kind (the crash-site reservation once the wreck stands). Returns how many went.</summary>
    public int RemoveKind(string kind) => _entries.RemoveAll(e => e.Kind == kind);

    /// <summary>Whether a square of <paramref name="halfExtent"/> around a point comes closer than <paramref name="margin"/> (or
    /// an entry's own, larger margin) to any box — <paramref name="exceptKind"/>'s boxes aside.</summary>
    public bool Overlaps(int x, int z, int halfExtent, int margin, string? exceptKind = null)
        => Overlaps(x, z, halfExtent, halfExtent, (_, own) => Math.Max(margin, own), exceptKind);

    /// <summary>Whether a box comes closer to any registered box than <paramref name="marginFor"/> says for that box (its kind
    /// and own margin in, the margin to keep out) — <paramref name="exceptKind"/>'s boxes aside.</summary>
    public bool Overlaps(int cx, int cz, int hw, int hl, Func<string, int, int> marginFor, string? exceptKind = null)
    {
        foreach (var e in _entries)
        {
            if (exceptKind is not null && e.Kind == exceptKind)
            {
                continue;
            }

            int margin = marginFor(e.Kind, e.Margin);
            if (Math.Abs(DeltaX(cx - e.Cx)) < hw + e.Hw + margin && Math.Abs(cz - e.Cz) < hl + e.Hl + margin)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The boxes as the shared placement searches take them (centre and half extents, checked with
    /// <paramref name="searchMargin"/>): each box grown by what its margin (<paramref name="marginFor"/>) exceeds the search's,
    /// so the search keeps exactly that margin from it — and, where the search relaxes its margin, relaxes from there.
    /// </summary>
    public List<(int Cx, int Cz, int Hw, int Hl)> Reserved(Func<string, int, int> marginFor, int searchMargin, string? exceptKind = null)
    {
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>(_entries.Count);
        foreach (var e in _entries)
        {
            if (exceptKind is not null && e.Kind == exceptKind)
            {
                continue;
            }

            int grow = Math.Max(0, marginFor(e.Kind, e.Margin) - searchMargin);
            reserved.Add((e.Cx, e.Cz, e.Hw + grow, e.Hl + grow));
        }

        return reserved;
    }

    /// <summary>The pairs of boxes of different structures that overlap (no margin) — what the registry is there to prevent.</summary>
    public List<(Footprint A, Footprint B)> Collisions()
    {
        var hits = new List<(Footprint, Footprint)>();
        for (int i = 0; i < _entries.Count; i++)
        {
            for (int j = i + 1; j < _entries.Count; j++)
            {
                var a = _entries[i];
                var b = _entries[j];
                if (a.Owner != b.Owner && Math.Abs(DeltaX(a.Cx - b.Cx)) < a.Hw + b.Hw && Math.Abs(a.Cz - b.Cz) < a.Hl + b.Hl)
                {
                    hits.Add((a, b));
                }
            }
        }

        return hits;
    }

    private int DeltaX(int dx) => Circumference > 0 ? WorldConstants.WrapDeltaX(dx, Circumference) : dx;
}
