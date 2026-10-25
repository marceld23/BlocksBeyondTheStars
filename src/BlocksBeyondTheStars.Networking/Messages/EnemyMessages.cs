// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client: the enemies this player has met (#2971, <see cref="Shared.State.PlayerState.EnemiesMet"/>), which fill
/// the Codex's "Enemies" chapter — the rest of an entry (name, health, damage, loot) the client reads from its own copy of
/// <c>data/enemies.json</c>. Sent in full on join and as a one-key delta on each first meeting.
/// </summary>
public sealed class EnemyLog
{
    /// <summary>Enemy keys (<see cref="Shared.Definitions.EnemyCatalog"/>): the full list on the join snapshot, the delta on
    /// an update.</summary>
    public string[] Keys { get; set; } = System.Array.Empty<string>();

    /// <summary>False = add <see cref="Keys"/> to what the client already has; true = replace.</summary>
    public bool Full { get; set; }
}
