// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>A marker of a <see cref="BuiltStructure"/>: what stands at a cell (a door, a vendor, a crate …), its settings
/// or device data and, for a door the generator cut, the wall it was cut into.</summary>
public readonly struct BuiltMarker : IEquatable<BuiltMarker>
{
    public BuiltMarker(string type, int x, int y, int z, string data = "", DoorWall doorAxis = DoorWall.Unknown)
    {
        Type = type ?? string.Empty;
        X = x;
        Y = y;
        Z = z;
        Data = data ?? string.Empty;
        DoorAxis = doorAxis;
    }

    public string Type { get; }

    public int X { get; }

    public int Y { get; }

    public int Z { get; }

    /// <summary>The marker's data (a settings line, a device, an index) — empty for most.</summary>
    public string Data { get; }

    public DoorWall DoorAxis { get; }

    public static bool operator ==(BuiltMarker a, BuiltMarker b) => a.Equals(b);

    public static bool operator !=(BuiltMarker a, BuiltMarker b) => !a.Equals(b);

    public bool Equals(BuiltMarker other)
        => string.Equals(Type, other.Type, StringComparison.Ordinal) && X == other.X && Y == other.Y && Z == other.Z
           && string.Equals(Data, other.Data, StringComparison.Ordinal) && DoorAxis == other.DoorAxis;

    public override bool Equals(object? obj) => obj is BuiltMarker m && Equals(m);

    public override int GetHashCode()
        => HashCode.Combine(StringComparer.Ordinal.GetHashCode(Type ?? string.Empty), X, Y, Z,
            StringComparer.Ordinal.GetHashCode(Data ?? string.Empty), DoorAxis);

    public override string ToString() => $"{Type}@({X},{Y},{Z}){(string.IsNullOrEmpty(Data) ? string.Empty : " " + Data)}";
}

/// <summary>
/// A structure a source built (#3512), in one shape for every generator: the voxel box (block ids, index
/// <c>(x · Height + y) · Length + z</c> like <see cref="SettlementStructure"/>), the sparse per-cell dye/glow and
/// shape+orientation, the cells the structure carves to air in the ground, its markers and the row that sits on the
/// ground. Rows below <see cref="GroundRow"/> lie in the ground (a cellar, a flooded basin, a landmark's underground
/// part); a source whose generator has nothing below its floor row keeps it at 0.
/// </summary>
public sealed class BuiltStructure
{
    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    public BuiltStructure(string sourceKey, int width, int height, int length, int groundRow = 0)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "A structure is at least one cell wide.");
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height), height, "A structure is at least one cell high.");
        }

        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "A structure is at least one cell long.");
        }

        SourceKey = sourceKey ?? string.Empty;
        Width = width;
        Height = height;
        Length = length;
        GroundRow = Math.Max(0, Math.Min(groundRow, height - 1));
        Blocks = new ushort[width * height * length];
    }

    /// <summary>The key of the source that built it.</summary>
    public string SourceKey { get; }

    public int Width { get; }

    public int Height { get; }

    public int Length { get; }

    /// <summary>The row that sits on the ground (underground: the floor row); the rows below lie in the ground.</summary>
    public int GroundRow { get; }

    /// <summary>Block ids by cell (0 = air), index <see cref="Index"/>.</summary>
    public ushort[] Blocks { get; }

    /// <summary>Per-cell dye and glow (0xRRGGBB each), only for cells that have either.</summary>
    public Dictionary<int, (int Tint, int Glow)> Mods { get; } = new();

    /// <summary>Per-cell packed shape + orientation, only for cells that are not plain cubes.</summary>
    public Dictionary<int, int> Shapes { get; } = new();

    /// <summary>Cells the generator explicitly carves to air in the ground (a landmark's tunnels, an outpost's cellar).</summary>
    public HashSet<int> Cavity { get; } = new();

    /// <summary>The markers, in the generator's order.</summary>
    public List<BuiltMarker> Markers { get; } = new();

    public int Index(int x, int y, int z) => (((x * Height) + y) * Length) + z;

    public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;

    public ushort Get(int x, int y, int z) => Blocks[Index(x, y, z)];

    public (int Tint, int Glow) GetModifier(int x, int y, int z) => Mods.TryGetValue(Index(x, y, z), out var m) ? m : (0, 0);

    public int GetShape(int x, int y, int z) => Shapes.TryGetValue(Index(x, y, z), out int s) ? s : 0;

    public bool IsCavity(int x, int y, int z) => Cavity.Contains(Index(x, y, z));

    /// <summary>Sets a cell's block, dye/glow and shape (zero modifiers and a plain shape clear the cell's entries).</summary>
    public void Set(int x, int y, int z, ushort block, int tint = 0, int glow = 0, int shape = 0)
    {
        int i = Index(x, y, z);
        Blocks[i] = block;
        if (tint != 0 || glow != 0)
        {
            Mods[i] = (tint, glow);
        }
        else
        {
            Mods.Remove(i);
        }

        if (shape != 0)
        {
            Shapes[i] = shape;
        }
        else
        {
            Shapes.Remove(i);
        }
    }

    /// <summary>How many cells hold a block.</summary>
    public int CountBlocks()
    {
        int n = 0;
        foreach (ushort b in Blocks)
        {
            n += b != 0 ? 1 : 0;
        }

        return n;
    }

    /// <summary>A stable 64-bit FNV-1a hash over the size, the ground row, every block, the modifiers, shapes and cavity
    /// cells in cell order, and the markers in their order — equal structures hash equal on every platform.</summary>
    public ulong ContentHash()
    {
        ulong h = FnvOffset;
        h = Mix(h, Width);
        h = Mix(h, Height);
        h = Mix(h, Length);
        h = Mix(h, GroundRow);
        foreach (ushort b in Blocks)
        {
            h = MixByte(MixByte(h, (byte)b), (byte)(b >> 8));
        }

        foreach (int i in Sorted(Mods.Keys))
        {
            h = Mix(Mix(Mix(h, i), Mods[i].Tint), Mods[i].Glow);
        }

        foreach (int i in Sorted(Shapes.Keys))
        {
            h = Mix(Mix(h, i), Shapes[i]);
        }

        foreach (int i in Sorted(Cavity))
        {
            h = Mix(h, i);
        }

        h = Mix(h, Markers.Count);
        foreach (var m in Markers)
        {
            h = Mix(Mix(Mix(Mix(MixString(h, m.Type), m.X), m.Y), m.Z), (int)m.DoorAxis);
            h = MixString(h, m.Data);
        }

        return h;
    }

    private static List<int> Sorted(IEnumerable<int> keys)
    {
        var list = new List<int>(keys);
        list.Sort();
        return list;
    }

    private static ulong MixByte(ulong h, byte b) => unchecked((h ^ b) * FnvPrime);

    private static ulong Mix(ulong h, int v)
    {
        unchecked
        {
            h = MixByte(h, (byte)v);
            h = MixByte(h, (byte)(v >> 8));
            h = MixByte(h, (byte)(v >> 16));
            return MixByte(h, (byte)(v >> 24));
        }
    }

    private static ulong MixString(ulong h, string s)
    {
        h = Mix(h, s.Length);
        foreach (char c in s)
        {
            h = MixByte(MixByte(h, (byte)c), (byte)(c >> 8));
        }

        return h;
    }
}
