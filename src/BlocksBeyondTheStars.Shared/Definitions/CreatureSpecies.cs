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
/// The broad body architecture the client renders for a species (#637/#638). <see cref="Standard"/>
/// is the classic segment-row body every species had before body plans existed; the other plans give
/// a genuinely different silhouette built from the same parametric traits. Chosen deterministically
/// per species — drawn AFTER every legacy roll so existing worlds keep their species identity.
/// </summary>
public enum CreatureBodyPlan
{
    Standard, // segment-row body + head + limbs (the original, and still the most common)
    Medusa,   // jellyfish: translucent bell, long rim tentacles, drifts in air or water (#637)
    Titan,    // elephant/giraffe-scale land megafauna: pillar legs, neck/trunk, tusks (#638)
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

    /// <summary>The body architecture the client renders (#637/#638) — see <see cref="CreatureBodyPlan"/>.</summary>
    public CreatureBodyPlan BodyPlan { get; set; } = CreatureBodyPlan.Standard;

    /// <summary>Titan plan only (#638): stacked neck segments between body and head (0 = none, ≥2 reads
    /// giraffe). Ignored by the other plans.</summary>
    public int NeckLength { get; set; }

    /// <summary>Titan plan only (#638): a segmented trunk hanging from the head (elephant).</summary>
    public bool HasTrunk { get; set; }

    /// <summary>How high above the ground an <see cref="CreatureHabitat.Air"/> species hovers (#637) —
    /// per-species instead of one global constant, so the sky gets layers. 0 = the legacy default.</summary>
    public float HoverAltitude { get; set; }

    /// <summary>How many individuals this species lives in a group of (#639): 1 = solitary, 2–5 = the
    /// spawner places a herd/school/flock together and members drift toward their group as they roam.</summary>
    public int SocialGroupSize { get; set; } = 1;

    /// <summary>Bioluminescent — glows in the dark (ties into the lighting system).</summary>
    public bool Glows { get; set; }

    /// <summary>Seed for this species' generated voice (#907). Deliberately NOT derived from
    /// <see cref="Id"/>: ids are "sp0".."sp8" and repeat on every planet, so hashing the id gave the
    /// whole game nine voices per habitat. This carries the species' real per-world sub-seed instead.
    /// Derived without consuming the generator's RNG, so adding it left every existing world's traits
    /// untouched.</summary>
    public int VoiceSeed { get; set; }

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
