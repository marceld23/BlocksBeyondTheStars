// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3615: which 16³ chunks of a voxel structure in the flight view a change touches — so a mined block of a giant wreck
/// re-meshes one chunk (or the few around it), never the whole hull. Structure cells are plain design coordinates (no world
/// wrap), the chunks those of the planet grid (<see cref="WorldConstants.ChunkSize"/>).
/// </summary>
public static class StructureChunkMath
{
    /// <summary>The chunk that holds <paramref name="cell"/>.</summary>
    public static ChunkCoord ChunkOf(Vector3i cell)
        => new(FloorDiv(cell.X), FloorDiv(cell.Y), FloorDiv(cell.Z));

    /// <summary>The chunks a changed cell's faces reach: its own, plus the neighbour across each chunk face the cell lies on
    /// (the neighbour's face toward it appears or disappears). Added to <paramref name="into"/>.</summary>
    public static void FaceChunks(Vector3i cell, ICollection<ChunkCoord> into)
    {
        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        var own = ChunkOf(cell);
        into.Add(own);
        int cs = WorldConstants.ChunkSize;
        int lx = cell.X - (own.X * cs), ly = cell.Y - (own.Y * cs), lz = cell.Z - (own.Z * cs);
        if (lx == 0) into.Add(new ChunkCoord(own.X - 1, own.Y, own.Z));
        if (lx == cs - 1) into.Add(new ChunkCoord(own.X + 1, own.Y, own.Z));
        if (ly == 0) into.Add(new ChunkCoord(own.X, own.Y - 1, own.Z));
        if (ly == cs - 1) into.Add(new ChunkCoord(own.X, own.Y + 1, own.Z));
        if (lz == 0) into.Add(new ChunkCoord(own.X, own.Y, own.Z - 1));
        if (lz == cs - 1) into.Add(new ChunkCoord(own.X, own.Y, own.Z + 1));
    }

    /// <summary>Every chunk within <paramref name="radius"/> blocks of <paramref name="cell"/> on each axis — the chunks a lamp
    /// placed, mined or uncovered there can light differently (the mesher's light flood reaches that far). Added to
    /// <paramref name="into"/>.</summary>
    public static void ReachChunks(Vector3i cell, int radius, ICollection<ChunkCoord> into)
    {
        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        radius = Math.Max(0, radius);
        for (int cx = FloorDiv(cell.X - radius); cx <= FloorDiv(cell.X + radius); cx++)
        {
            for (int cy = FloorDiv(cell.Y - radius); cy <= FloorDiv(cell.Y + radius); cy++)
            {
                for (int cz = FloorDiv(cell.Z - radius); cz <= FloorDiv(cell.Z + radius); cz++)
                {
                    into.Add(new ChunkCoord(cx, cy, cz));
                }
            }
        }
    }

    /// <summary>The reach (blocks, per axis) of the mesher's block light around a lamp: its flood radius plus the one-cell
    /// sampling margin of the faces that look into a lit cell.</summary>
    public const int LampReach = BlocksBeyondTheStars.Shared.Definitions.BlockLight.DefaultRadius + 1;

    /// <summary>The chunks a changed cell's own mesh data reaches: its chunk and all 26 around it that the cell touches — a
    /// face across a chunk border, and the corner shading (ambient occlusion) of faces that read the cell along an edge or a
    /// corner. Added to <paramref name="into"/>.</summary>
    public static void NeighbourChunks(Vector3i cell, ICollection<ChunkCoord> into) => ReachChunks(cell, 1, into);

    /// <summary>
    /// The chunks whose block light a change of <paramref name="cell"/> can alter, added to <paramref name="into"/>: the
    /// reach of every lamp near it (a wall opened or closed inside a lamp's flood changes the light anywhere that lamp
    /// reaches — up to twice <see cref="LampReach"/> from the cell) and, when the cell itself gained, lost or changed a
    /// light (<paramref name="lampChanged"/>), its own reach. <paramref name="lights"/> are the structure's lamps AFTER the
    /// change; an empty or null list adds nothing but the changed lamp's reach.
    /// </summary>
    public static void LightChunks(Vector3i cell, bool lampChanged, IReadOnlyList<(Vector3i Pos, int Rgb)>? lights,
        ICollection<ChunkCoord> into)
    {
        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        if (lampChanged)
        {
            ReachChunks(cell, LampReach, into);
        }

        if (lights is null)
        {
            return;
        }

        foreach (var (pos, _) in lights)
        {
            if (Math.Abs(pos.X - cell.X) <= LampReach && Math.Abs(pos.Y - cell.Y) <= LampReach && Math.Abs(pos.Z - cell.Z) <= LampReach)
            {
                ReachChunks(pos, LampReach, into);
            }
        }
    }

    /// <summary>
    /// The chunks whose skylight a change of <paramref name="cell"/> between light-stopping and open can alter
    /// (<see cref="SkylightField"/>): a build reads the columns <see cref="SkylightField.Margin"/> blocks beside its chunk,
    /// from <see cref="SkylightField.ScanAbove"/> blocks above its top down to <see cref="SkylightField.ScanBelow"/> below
    /// its bottom — so a roof block opened above a hall re-lights the chunks down there. Added to <paramref name="into"/>.
    /// </summary>
    public static void SkyChunks(Vector3i cell, ICollection<ChunkCoord> into)
    {
        if (into is null)
        {
            throw new ArgumentNullException(nameof(into));
        }

        // A chunk reads the column from its top + ScanAbove down to its bottom − ScanBelow: the lowest chunk whose scan still
        // reaches up to the cell starts at or above cell − ChunkSize − ScanAbove (rounded up to a chunk), the highest at or
        // below cell + ScanBelow.
        int cs = WorldConstants.ChunkSize, side = SkylightField.Margin;
        int lowY = -FloorDiv(-(cell.Y - SkylightField.ScanAbove - cs)), highY = FloorDiv(cell.Y + SkylightField.ScanBelow);
        for (int cx = FloorDiv(cell.X - side); cx <= FloorDiv(cell.X + side); cx++)
        {
            for (int cy = lowY; cy <= highY; cy++)
            {
                for (int cz = FloorDiv(cell.Z - side); cz <= FloorDiv(cell.Z + side); cz++)
                {
                    into.Add(new ChunkCoord(cx, cy, cz));
                }
            }
        }
    }

    /// <summary>A structure's lamps after one cell changed: the entry at <paramref name="cell"/> replaced by
    /// <paramref name="rgb"/> (0 = no lamp there any more). Keeps the list the next builds light with in step with an edit,
    /// instead of gathering every lamp of a giant hull again.</summary>
    public static void UpdateLight(List<(Vector3i Pos, int Rgb)> lights, Vector3i cell, int rgb)
    {
        if (lights is null)
        {
            throw new ArgumentNullException(nameof(lights));
        }

        lights.RemoveAll(l => l.Pos.Equals(cell));
        if (rgb != 0)
        {
            lights.Add((cell, rgb));
        }
    }

    /// <summary>The chunks that hold at least one of <paramref name="cells"/>.</summary>
    public static HashSet<ChunkCoord> Occupied(IEnumerable<Vector3i> cells)
    {
        var set = new HashSet<ChunkCoord>();
        if (cells is null)
        {
            return set;
        }

        foreach (var c in cells)
        {
            set.Add(ChunkOf(c));
        }

        return set;
    }

    /// <summary>The chunks of <paramref name="chunks"/> ordered nearest first to <paramref name="eye"/> (design coordinates) —
    /// a big hull builds where the player looks at it first.</summary>
    public static List<ChunkCoord> NearestFirst(IEnumerable<ChunkCoord> chunks, float eyeX, float eyeY, float eyeZ)
    {
        var list = new List<ChunkCoord>(chunks ?? Array.Empty<ChunkCoord>());
        float half = WorldConstants.ChunkSize * 0.5f;
        float Dist(ChunkCoord c)
        {
            float dx = (c.X * WorldConstants.ChunkSize) + half - eyeX;
            float dy = (c.Y * WorldConstants.ChunkSize) + half - eyeY;
            float dz = (c.Z * WorldConstants.ChunkSize) + half - eyeZ;
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        list.Sort((a, b) =>
        {
            int byDistance = Dist(a).CompareTo(Dist(b));
            if (byDistance != 0)
            {
                return byDistance;
            }

            // A stable tie-break, so two equally near chunks always build in the same order.
            int x = a.X.CompareTo(b.X);
            return x != 0 ? x : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
        });
        return list;
    }

    private static int FloorDiv(int a)
    {
        int cs = WorldConstants.ChunkSize;
        return (a >= 0 ? a : a - (cs - 1)) / cs;
    }
}
