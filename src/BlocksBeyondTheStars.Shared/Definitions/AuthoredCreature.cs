// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// An AUTHORED creature species (school club wave 3, #1763), loaded from <c>data/creatures.json</c>: a species
/// somebody designed — Lena's "Leni", Damian's flowerling — rather than one the roster generator rolled. A planet
/// type names the ones it hosts in <see cref="PlanetType.AuthoredCreatures"/>; on a generation-5 world the roster
/// generator appends them AFTER its procedural slots (so no rolled species moves), coins the rest of the name, and
/// resolves the biome affinity. Everything the client renders comes from the same <see cref="CreatureSpecies"/>
/// fields a rolled species has; the traits that need new behaviour are the four flags at the end.
/// </summary>
public sealed class AuthoredCreature
{
    /// <summary>Stable key, the roster id is <c>"au_" + Key</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>The fixed first part of the coined name ("Leni" → "Leni Tarak"); the second word is coined per world.</summary>
    public string NamePrefix { get; set; } = string.Empty;

    public CreatureHabitat Habitat { get; set; } = CreatureHabitat.Land;
    public CreatureActivity Activity { get; set; } = CreatureActivity.Cathemeral;
    public CreatureTemperament Temperament { get; set; } = CreatureTemperament.Passive;
    public LocomotionStyle LocoStyle { get; set; } = LocomotionStyle.Strider;
    public CreatureBodyPlan BodyPlan { get; set; } = CreatureBodyPlan.Standard;

    public float Size { get; set; } = 1f;
    /// <summary>Null = the generator's formula for the size and temperament.</summary>
    public float? MaxHealth { get; set; }
    public float Speed { get; set; } = 2f;
    /// <summary>Null = the generator's formula: 0 for a peaceful species, the aggressive band otherwise.</summary>
    public float? AttackDamage { get; set; }

    public int Legs { get; set; } = 4;
    public bool HasWings { get; set; }
    public bool HasTail { get; set; }
    public int BodySegments { get; set; } = 1;
    public int ColorRgb { get; set; } = 0xFFFFFF;
    public int BellyRgb { get; set; } = 0xFFFFFF;
    public int Eyes { get; set; } = 2;
    public int Horns { get; set; }
    public bool HasCrest { get; set; }
    public bool Glows { get; set; }
    public int SocialGroupSize { get; set; } = 1;
    public float HoverAltitude { get; set; }

    /// <summary>Heads / wing pairs / fin pairs (#1780-#1782); the defaults are the classic single head, pair, pair.</summary>
    public int Heads { get; set; } = 1;
    public int WingPairs { get; set; } = 1;
    public int FinPairs { get; set; } = 1;

    public string DropItem { get; set; } = "creature_meat";
    public int DropCount { get; set; } = 1;
    public CreatureDropKind DropKind { get; set; } = CreatureDropKind.Food;

    /// <summary>The biome SURFACE blocks this species is native to ("snow", "ice"); empty = any biome. Resolved
    /// against the world's biome list by the roster generator.</summary>
    public List<string> BiomeSurfaces { get; set; } = new();

    /// <summary>When true the species spawns ONLY where the ground under its feet is one of
    /// <see cref="BiomeSurfaces"/> — a hard rule, unlike the procedural roster's affinity bias.</summary>
    public bool BiomeExclusive { get; set; }

    /// <summary>The client's hide tile ("fur", "shaggy", "petal", …) instead of the id-hashed pick; empty = hashed.</summary>
    public string Hide { get; set; } = string.Empty;

    /// <summary>Damian's rule (#1760): the species turns hostile toward a player it SEES breaking a block.</summary>
    public bool AngeredByMining { get; set; }

    /// <summary>Damian's rule (#1760): a calm individual near a player who has not mined for a while spills a gift.</summary>
    public bool GiftsWhenCalm { get; set; }
}
