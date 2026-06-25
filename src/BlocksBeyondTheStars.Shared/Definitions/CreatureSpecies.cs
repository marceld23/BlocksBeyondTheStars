// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>Where a creature lives — governs spawning, movement and survival.</summary>
public enum CreatureHabitat
{
    Land,
    Water,
    Lava,
    Air,
    Cave,       // subterranean: lives in caves underground (often eyeless + bioluminescent)
    Amphibian,  // shorelines: at home both in shallow water and on the land beside it
}

/// <summary>When a creature is active; the rest of the cycle it sleeps/rests.</summary>
public enum CreatureActivity
{
    Diurnal,     // active by day
    Nocturnal,   // active by night
    Crepuscular, // active at dawn/dusk
    Cathemeral,  // active any time
}

/// <summary>
/// How a creature behaves toward the player. Only <see cref="Aggressive"/> and
/// <see cref="PackHunter"/> roam and attack on sight — the rest are not hostile, so a world is
/// never all-hostile.
/// </summary>
public enum CreatureTemperament
{
    Passive,      // ignores the player (grazes/wanders)
    Skittish,     // flees the player
    Territorial,  // only retaliates if attacked (no roaming damage)
    Aggressive,   // hunts nearby players
    PackHunter,   // hunts in groups
}

/// <summary>
/// What a defeated/harvested creature yields, mirroring the flora property tags. Rarely a
/// creature is a building-material substitute; more often it is edible (food) or poisonous.
/// </summary>
public enum CreatureDropKind
{
    Material, // a building-resource substitute (so creatures can replace some mining) — rare
    Food,     // edible: consuming it restores health
    Poison,   // toxic: consuming it harms the player
}

/// <summary>
/// A procedurally generated creature species (technical requirements / `anf_space_flight.md`
/// §12). Every world deterministically derives its own roster of species from the world seed +
/// planet, so different planets host different, surprising lifeforms. The descriptor is sent to
/// clients so the parametric blocky <c>CreatureBuilder</c> renders the same creature everywhere.
/// Server-authoritative: only the server generates species, spawns them and resolves combat.
/// </summary>
public sealed class CreatureSpecies
{
    /// <summary>Stable per-world id, e.g. "sp0".</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Bilingual display-name key (generated species use a generic fallback name).</summary>
    public string NameKey { get; set; } = "creature.generic.name";

    /// <summary>A coined, pronounceable species name (e.g. "Vexilth Krool"), generated per species and shown
    /// to the player on scan. Language-neutral (invented), so it needs no localization.</summary>
    public string Name { get; set; } = string.Empty;

    public CreatureHabitat Habitat { get; set; }
    public CreatureActivity Activity { get; set; }
    public CreatureTemperament Temperament { get; set; }

    /// <summary>The species' randomly-chosen movement signature (gait/cadence), biased by its body + habitat +
    /// temperament. Drives a per-species <see cref="LocomotionProfile"/> so a world's fauna move in
    /// recognisably different ways (grazers pause to feed, darters skitter, gliders swoop, slitherers weave).</summary>
    public LocomotionStyle LocoStyle { get; set; } = LocomotionStyle.Strider;

    // --- Stats ---
    public float Size { get; set; } = 1f;
    public float MaxHealth { get; set; } = 20f;
    public float Speed { get; set; } = 2f;

    /// <summary>Damage dealt per second to a nearby player — only while hostile AND active.</summary>
    public float AttackDamage { get; set; }

    // --- Appearance (parametric blocky body for the client renderer) ---
    public int Legs { get; set; } = 4;
    public bool HasWings { get; set; }
    public bool HasTail { get; set; }
    public int BodySegments { get; set; } = 1;
    public int ColorRgb { get; set; } = 0xFFFFFF;

    /// <summary>Number of eyes on the head — optional (0 = eyeless) and often, but not always, two; some
    /// species have three or more. Random per species for visual variety.</summary>
    public int Eyes { get; set; } = 2;

    /// <summary>Number of horns/spikes on the head/back (0 = none) — silhouette variety.</summary>
    public int Horns { get; set; }

    /// <summary>A row of dorsal-crest spines/frill along the back — extra silhouette variety (Task 6).</summary>
    public bool HasCrest { get; set; }

    /// <summary>Dangling tentacles under the body (0 = none) — mostly water/cave fauna (item-21 morphology).</summary>
    public int Tentacles { get; set; }

    /// <summary>Eyes sit on stalks atop the head instead of in the face (snail-like) — item-21 morphology.</summary>
    public bool EyeStalks { get; set; }

    /// <summary>A translucent buoyancy gas-sac above the body (floating grazers) — item-21 morphology.</summary>
    public bool HasGasSac { get; set; }

    /// <summary>Secondary/belly accent colour (packed RGB) for a two-tone body, for more visible variety.</summary>
    public int BellyRgb { get; set; } = 0xFFFFFF;

    /// <summary>Bioluminescent — glows in the dark (ties into the lighting system).</summary>
    public bool Glows { get; set; }

    /// <summary>The biome (index into the planet's biome list) this species is native to, so a multi-biome
    /// world shows different fauna in different regions. -1 = at home in any biome (single-biome worlds).</summary>
    public int BiomeAffinity { get; set; } = -1;

    // --- Harvest (drop + its property kind) ---
    public string DropItem { get; set; } = string.Empty;
    public int DropCount { get; set; } = 1;
    public CreatureDropKind DropKind { get; set; } = CreatureDropKind.Food;

    /// <summary>Only Aggressive/PackHunter creatures roam and deal proximity damage.</summary>
    public bool Hostile => Temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
}
