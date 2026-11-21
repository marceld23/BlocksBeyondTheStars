// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// #3893: one SPS guide tower standing (as a ruin) on a body — an entry of <see cref="WorldMetadata.SpsTowers"/>, written once
/// by the tower stamp next to the tower's placement record (kind <c>sps_tower</c>). The blocks live in the tower's structure
/// overlay; the record remembers what the later systems must find again without re-deriving the ruin: where the relay console
/// stands, which cells hold the emergency lamps, whether a guard post stands beside the foot and where its capsule tank is —
/// and the state those systems keep (<see cref="Lit"/>, <see cref="TankOpened"/>, <see cref="GuardSettlement"/>). Every cell
/// is a canonical world cell (X wrapped into the body's circumference).
/// </summary>
public sealed class SpsTowerRecord
{
    /// <summary>The body (location id) the tower stands on.</summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>Its placement record's index on that body (0 = the rolled one; an admin's hand stamp takes the next).</summary>
    public int Index { get; set; }

    /// <summary>The record key: <c>&lt;location&gt;#&lt;index&gt;</c>.</summary>
    public string SiteId => LocationId + "#" + Index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The key of the tower template (<c>data/sps_towers/&lt;key&gt;.json</c>).</summary>
    public string Template { get; set; } = string.Empty;

    /// <summary>The world cell of the template's low corner: X and Z of the footprint's corner, Y the world row of the
    /// template's ground row (the seat).</summary>
    public int X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public int Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public int Z { get; set; }

    /// <summary>The footprint's size along X and Z as it stands (after the quarter turn).</summary>
    public int Width { get; set; }

    /// <summary>See <see cref="Width"/>.</summary>
    public int Length { get; set; }

    /// <summary>The rows it stands above its seat.</summary>
    public int Height { get; set; }

    /// <summary>The quarter turns the template was turned by (0–3).</summary>
    public int Turns { get; set; }

    /// <summary>The cell of the relay console (block <c>sps_console</c>), or null when the tower has none (a broken template).</summary>
    public SpsTowerCell? Console { get; set; }

    /// <summary>The cells of the tower's own emergency lamps (the relay room's and the mast's): <c>light_red</c> as the tower
    /// is found, <c>light_blue</c> once it is <see cref="Lit"/>.</summary>
    public List<SpsTowerCell> TowerLamps { get; set; } = new();

    /// <summary>Whether a guard post (a ruined wooden hut with a capsule tank) stands beside the tower's foot.</summary>
    public bool HasGuardPost { get; set; }

    /// <summary>The LOWER cell of the guard post's capsule tank (two <c>capsule_tank</c> cells stacked: this one and the one
    /// above it); null without a guard post.</summary>
    public SpsTowerCell? Tank { get; set; }

    /// <summary>The cells of the two lamps that belong to the capsule tank; empty without a guard post.</summary>
    public List<SpsTowerCell> TankLamps { get; set; } = new();

    /// <summary>Written by the tower's life (#3895): the tower's lights were brought back (red turned blue).</summary>
    public bool Lit { get; set; }

    /// <summary>Written by the sleeper (#3897): the capsule tank was opened.</summary>
    public bool TankOpened { get; set; }

    /// <summary>Written by the sleeper (#3897): the settlement (its name or id, as that system keys it) the guard went to;
    /// empty = not decided.</summary>
    public string GuardSettlement { get; set; } = string.Empty;

    /// <summary>Written by the sleeper (#3897): the name of this post's guard, given when his tank is opened and kept for
    /// good — every guard post of a save holds a different guard. Empty = the tank was not opened yet. Additive: a save
    /// without the field reads empty, and an opened tank's guard is named the next time he is placed.</summary>
    public string GuardName { get; set; } = string.Empty;
}

/// <summary>A world cell of an <see cref="SpsTowerRecord"/> (a plain, JSON-friendly triple; <see cref="ToVector"/> for the
/// geometry type).</summary>
public sealed class SpsTowerCell
{
    public SpsTowerCell()
    {
    }

    public SpsTowerCell(Vector3i cell)
    {
        (X, Y, Z) = (cell.X, cell.Y, cell.Z);
    }

    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }

    public Vector3i ToVector() => new(X, Y, Z);

    /// <summary>Whether this is <paramref name="cell"/>.</summary>
    public bool Is(Vector3i cell) => X == cell.X && Y == cell.Y && Z == cell.Z;
}
