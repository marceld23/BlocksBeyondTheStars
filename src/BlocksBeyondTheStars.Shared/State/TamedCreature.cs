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

    /// <summary>Affection/bond 0..100 — starts where the taming trust ended and GROWS by feeding (#1225).
    /// Three tiers hang off it: 50 a wider fetch reach, 70 the bandit ward (#1210), 90 the odd scouting
    /// hint. It decays a point a day when the animal is not fed, but never below
    /// <see cref="BondFloor"/> — a holiday must not undo a friendship.</summary>
    public int Bond { get; set; }

    /// <summary>The lowest bond decay can reach: where a freshly tamed animal starts, so time apart can
    /// cost the perks but never the companion itself.</summary>
    public const int BondFloor = 40;

    /// <summary>Server-stamped last feeding (unix ms); 0 = never fed since taming (then
    /// <see cref="TamedAtUtc"/> is the clock decay counts from).</summary>
    public long LastFedUtc { get; set; }

    /// <summary>Server-stamped tame time (unix ms); 0 if unknown.</summary>
    public long TamedAtUtc { get; set; }

    /// <summary>#3035: the companion wears a name tag — its name floats above it as a hologram. Set when a name tag was used on
    /// it; it stays for good.</summary>
    public bool NameTag { get; set; }

    /// <summary>The <see cref="Kind"/> of the friendly hunter robot (#3295), built from the finale's robot core.</summary>
    public const string RobotKind = "robot";

    /// <summary>The <see cref="Kind"/> of Rudolpho, the alien reindeer (#3322), called with the star bell.</summary>
    public const string ReindeerKind = "reindeer";

    /// <summary>#3295: what kind of companion this is — empty for a tamed animal (every record saved before this shipped),
    /// <see cref="RobotKind"/> for the hunter robot: its bond is fixed, it does not eat, it cannot be scanned or sampled, it
    /// guards its owner, and releasing it powers it down back into its item. <see cref="ReindeerKind"/> (#3322) for Rudolpho:
    /// the same fixed bond, no food, no scan or sample — but he never fights, and "send home" turns him back into the star
    /// bell.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>#3295: the companion follows its owner to every world instead of living on <see cref="HomeBodyId"/> (the
    /// robot, Rudolpho). False for every animal.</summary>
    public bool Roams { get; set; }

    /// <summary>#3295: the robot was knocked out in a fight — it is gone from the world and its parts wait for their
    /// owner-locked crate (placed as soon as the owner stands on open ground; never lost). The record goes once the crate
    /// is out.</summary>
    public bool KnockedOut { get; set; }

    /// <summary>The <see cref="Kind"/> of the story Soundi (#3361), the Sounders pack's reward after the sing duel: a
    /// travelling companion like Rudolpho, named by the player.</summary>
    public const string SoundiKind = "soundi";

    /// <summary>#3344: the player chose to take this companion along. True by default, so every record saved before the
    /// choice existed behaves exactly as before. A planet pet that does not follow stays home (<see cref="HasHomeSpot"/>);
    /// a travelling companion (robot, Rudolpho, the Soundi) that does not follow waits on board and appears nowhere.</summary>
    public bool Following { get; set; } = true;

    /// <summary>#3344: whether <see cref="HomeX"/>/<see cref="HomeY"/>/<see cref="HomeZ"/> hold the spot a planet pet lives at
    /// while it stays home — where it was tamed, or where "set home here" put it. False on records from before (the first
    /// time such a pet stays home, the spot it is at becomes its home).</summary>
    public bool HasHomeSpot { get; set; }

    /// <summary>#3344: the home spot on <see cref="HomeBodyId"/> (world coordinates, X).</summary>
    public float HomeX { get; set; }

    /// <summary>#3344: the home spot on <see cref="HomeBodyId"/> (world coordinates, Y).</summary>
    public float HomeY { get; set; }

    /// <summary>#3344: the home spot on <see cref="HomeBodyId"/> (world coordinates, Z).</summary>
    public float HomeZ { get; set; }

    /// <summary>#3398: the weapon the robot carries — the full item key it was given (empty = none). It fights with it, and
    /// it comes back with "take back", the power-down and the knock-out crate. Only the robot takes gear (and never armour).</summary>
    public string GearWeapon { get; set; } = string.Empty;

    /// <summary>#3399: the jetpack the robot wears — the full item key it was given (empty = none): it flies up to its owner.
    /// Returned like <see cref="GearWeapon"/>.</summary>
    public string GearJetpack { get; set; } = string.Empty;
}
