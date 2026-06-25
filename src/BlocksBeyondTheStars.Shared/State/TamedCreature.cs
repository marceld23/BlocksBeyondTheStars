// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// A creature the player has tamed (design: <c>docs/developer/CREATURE_TAMING.md</c>). Wild fauna is
/// transient and per-world (regenerated from the seed on every visit), so a companion cannot be a saved
/// wild entity — it is its own piece of <b>per-player</b> state. It is bound to the body it was tamed on
/// and re-appears as a follower whenever the owner returns to that world. It carries a full
/// <see cref="CreatureSpecies"/> snapshot so it renders + behaves independently of any world's procedural
/// roster. Persisted in the player JSON blob.
/// </summary>
public sealed class TamedCreature
{
    /// <summary>Our own stable id (not the wild entity id, which is transient and re-rolled each visit).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Celestial-body id this companion lives on — it is present only while the owner is on it.</summary>
    public string HomeBodyId { get; set; } = string.Empty;

    /// <summary>Player-given name shown on its nameplate + in the Companions menu.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The home world's species id this is an instance of (e.g. "sp3").</summary>
    public string SpeciesId { get; set; } = string.Empty;

    /// <summary>Full species descriptor snapshot — kept so the companion renders + moves even when no world
    /// roster is loaded, and so it survives the (deterministic) roster being rebuilt.</summary>
    public CreatureSpecies Species { get; set; } = new();

    /// <summary>This individual's cosmetic size factor within its species (matches the wild animal it was).</summary>
    public float SizeScale { get; set; } = 1f;

    /// <summary>Affection/bond 0..100 — starts where the taming trust ended; room to grow (perks/cosmetics).</summary>
    public int Bond { get; set; }

    /// <summary>Server-stamped tame time (unix ms); 0 if unknown.</summary>
    public long TamedAtUtc { get; set; }
}
