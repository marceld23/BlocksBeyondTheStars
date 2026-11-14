// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// #3609: a giant wreck as it drifts in space (<see cref="GiantShipGenerator.BuildSpace"/>) — the ruined cells in TEMPLATE
/// coordinates (a wreck in space is never turned) with their modifiers, the markers that survived (chests, sounds, entrances,
/// doorways — <see cref="SettlementMarker.Data"/> is the marker's settings line) and the boarding arrival. The derelict
/// technique turns it into a station structure. Index of every array and dictionary: <c>(x * Height + y) * Length + z</c>.
/// </summary>
public sealed class GiantSpaceLayout
{
    private readonly Dictionary<int, (int Tint, int Glow)> _mods;
    private readonly Dictionary<int, int> _shapes;

    internal GiantSpaceLayout(string key, VoxelGrid grid, IReadOnlyList<SettlementMarker> markers, Vector3i entrance, int intactCells)
    {
        Key = key;
        Width = grid.W;
        Height = grid.H;
        Length = grid.L;
        Blocks = grid.Blocks;
        _mods = grid.Mods;
        _shapes = grid.Shapes;
        Markers = markers;
        Entrance = entrance;
        IntactCells = intactCells;
        foreach (ushort b in grid.Blocks)
        {
            SolidCells += b != 0 ? 1 : 0;
        }
    }

    /// <summary>The giant ship's template key.</summary>
    public string Key { get; }

    public int Width { get; }

    public int Height { get; }

    public int Length { get; }

    /// <summary>The block of every cell (0 = air).</summary>
    public ushort[] Blocks { get; }

    /// <summary>The tint and glow of the cells that carry one (0xRRGGBB each).</summary>
    public IReadOnlyDictionary<int, (int Tint, int Glow)> Mods => _mods;

    /// <summary>The packed shape and orientation of the cells that carry one.</summary>
    public IReadOnlyDictionary<int, int> Shapes => _shapes;

    /// <summary>The markers that survived, in template coordinates.</summary>
    public IReadOnlyList<SettlementMarker> Markers { get; }

    /// <summary>The boarding arrival: the first entrance that survived — a free cell with a free cell above and a floor under
    /// it (<see cref="GiantShipGenerator"/> moves an entrance that lost its floor to the nearest such cell inside the hull).</summary>
    public Vector3i Entrance { get; }

    /// <summary>How many cells held a block before the ruin pass.</summary>
    public int IntactCells { get; }

    /// <summary>How many cells hold a block now.</summary>
    public int SolidCells { get; }

    public int Index(int x, int y, int z) => (((x * Height) + y) * Length) + z;

    public bool InBounds(int x, int y, int z) => x >= 0 && y >= 0 && z >= 0 && x < Width && y < Height && z < Length;

    public ushort Get(int x, int y, int z) => InBounds(x, y, z) ? Blocks[Index(x, y, z)] : (ushort)0;

    public (int Tint, int Glow) GetModifier(int x, int y, int z)
        => InBounds(x, y, z) && _mods.TryGetValue(Index(x, y, z), out var m) ? m : (0, 0);

    public int GetShape(int x, int y, int z) => InBounds(x, y, z) && _shapes.TryGetValue(Index(x, y, z), out int s) ? s : 0;
}
