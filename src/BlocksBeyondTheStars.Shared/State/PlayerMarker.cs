// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// A named map marker a player placed on a world (#1217): a labelled, icon-and-colour-coded spot on the planet
/// map and compass — at most 8 per player per world. Persisted in the owner's player blob (additive, like
/// <see cref="DeployedSpeeder"/>); a <see cref="Shared"/> marker is also shown to players the owner is allied
/// or crewed with while they stand on the same body. Pings (the transient "look here" pulse) are deliberately
/// NOT this type — they live only in server memory for 30 seconds and are never saved.
/// </summary>
public sealed class PlayerMarker
{
    /// <summary>Stable id (server-issued), used to update / delete the marker.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The celestial-body id the marker sits on (markers are per world).</summary>
    public string LocationId { get; set; } = string.Empty;

    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }

    /// <summary>Player-typed label, ≤ 24 chars, sanitized + content-screened like a beacon label.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Icon index 0..7: flag, home, ore, danger, water, star, heart, question — a fixed safe set.</summary>
    public int Icon { get; set; }

    /// <summary>Colour index into the shared marker palette (0..5) — a fixed named set, not a free picker.</summary>
    public int Color { get; set; }

    /// <summary>True = visible to allies + crew on the same body; false = only the owner sees it.</summary>
    public bool Shared { get; set; }

    /// <summary>Unix ms the marker was created (oldest-first ordering in the map list).</summary>
    public long CreatedUtc { get; set; }
}
