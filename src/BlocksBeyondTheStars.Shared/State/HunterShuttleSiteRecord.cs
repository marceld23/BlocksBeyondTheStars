// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// #3097: where a hunter shuttle that got away landed on a body (<see cref="WorldMetadata.HunterShuttleSites"/>). The landing
/// is decided in space, while the body's world may not even be loaded: the record starts <see cref="Pending"/> with a planned
/// spot, and the first time a player stands on that body the server stamps the shuttle, its robot docks and its guard there
/// (<see cref="Active"/>). Mining every dock clears the site for good (<see cref="Cleared"/>). The blocks themselves live on as
/// the world's ordinary block edits; this record only remembers what the stamp cannot tell.
/// </summary>
public sealed class HunterShuttleSiteRecord
{
    /// <summary><see cref="State"/>: landed in space's telling, not stamped on the ground yet.</summary>
    public const string Pending = "pending";

    /// <summary><see cref="State"/>: stamped — the docks release robots while a player is near.</summary>
    public const string Active = "active";

    /// <summary><see cref="State"/>: every dock is gone; the site is quiet for good.</summary>
    public const string Cleared = "cleared";

    /// <summary><see cref="Pending"/>, <see cref="Active"/> or <see cref="Cleared"/>.</summary>
    public string State { get; set; } = Pending;

    /// <summary>Pending: the planned centre column. Active/cleared: the stamped hull's origin cell (its −X/−Z corner).</summary>
    public int X { get; set; }

    /// <summary>Active/cleared: the hull's floor row (the cell row the floor plates sit in). 0 while pending.</summary>
    public int Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public int Z { get; set; }

    /// <summary>The site's own seed — the spot search, the guard and the loot roll draw from it.</summary>
    public long Seed { get; set; }

    /// <summary>The robot docks (<c>guardian_spawner</c> cells) the stamp placed, in world cells.</summary>
    public List<HunterShuttleSiteCell> Spawners { get; set; } = new();

    /// <summary>True once the shuttle's hunter guard was defeated — it never comes back.</summary>
    public bool GuardDefeated { get; set; }

    /// <summary>When it landed (UTC unix seconds) — informational.</summary>
    public long LandedAt { get; set; }
}

/// <summary>#3097: one world cell of a <see cref="HunterShuttleSiteRecord"/>.</summary>
public sealed class HunterShuttleSiteCell
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Z { get; set; }
}
