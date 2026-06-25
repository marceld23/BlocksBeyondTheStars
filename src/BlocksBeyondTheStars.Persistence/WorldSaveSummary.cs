// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Headline stats mirrored into the <c>world.meta.json</c> sidecar beside each save's <c>world.db</c>.
/// The authoritative copy lives in the DB's <see cref="BlocksBeyondTheStars.Shared.State.WorldMetadata"/>;
/// this lightweight file exists only so the Unity client's world-picker can show name/playtime/last-played
/// without a SQLite dependency (the picker runs in the menu, before any server process is launched).
/// Field names are PascalCase so Unity's <c>JsonUtility</c> can read them into a matching struct verbatim.
/// </summary>
public sealed class WorldSaveSummary
{
    public string WorldName { get; set; } = string.Empty;
    public long PlaytimeSeconds { get; set; }

    /// <summary>ISO-8601 UTC timestamp of the last save (round-trip "o" format).</summary>
    public string LastPlayedUtc { get; set; } = string.Empty;
}
