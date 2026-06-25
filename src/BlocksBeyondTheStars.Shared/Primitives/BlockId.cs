// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Primitives;

/// <summary>
/// Compact numeric handle for a block type. Assigned by the content registry from a
/// string key (palette). Stored densely inside chunks, so it wraps a <see cref="ushort"/>.
/// Id 0 is reserved for air/empty.
/// </summary>
public readonly struct BlockId : IEquatable<BlockId>
{
    public const ushort AirValue = 0;
    public static readonly BlockId Air = new(AirValue);

    public readonly ushort Value;

    public BlockId(ushort value) => Value = value;

    public bool IsAir => Value == AirValue;

    public bool Equals(BlockId other) => Value == other.Value;
    public override bool Equals(object? obj) => obj is BlockId other && Equals(other);
    public override int GetHashCode() => Value;
    public override string ToString() => $"Block#{Value}";

    public static bool operator ==(BlockId a, BlockId b) => a.Value == b.Value;
    public static bool operator !=(BlockId a, BlockId b) => a.Value != b.Value;
}
