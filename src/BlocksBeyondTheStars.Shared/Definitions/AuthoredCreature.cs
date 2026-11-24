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

    /// <summary>#2084: the name is <see cref="NamePrefix"/> alone on every world — no coined second word ("Mini-Michi-Paul").</summary>
    public bool FixedName { get; set; }

    /// <summary>#2084: the terrain generation a world needs before this species joins its roster (0 = the authored wave's own
    /// generation 5). A species that relies on a later wave — the bananas of generation 14, the biped body of 16 — waits for it,
    /// so no older world meets a creature it cannot tame or draw.</summary>
    public int MinGeneration { get; set; }

    public CreatureHabitat Habitat { get; set; } = CreatureHabitat.Land;
    public CreatureActivity Activity { get; set; } = CreatureActivity.Cathemeral;
    public CreatureTemperament Temperament { get; set; } = CreatureTemperament.Passive;
    public LocomotionStyle LocoStyle { get; set; } = LocomotionStyle.Strider;
    public CreatureBodyPlan BodyPlan { get; set; } = CreatureBodyPlan.Standard;

    /// <summary>The head's silhouette (#2009): the classic box, or one of the arachnid plan's pyramids.</summary>
    public CreatureHeadShape HeadShape { get; set; } = CreatureHeadShape.Box;

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
    /// <summary>The iris colour (packed 0xRRGGBB, #2069); 0 = the classic pale eye.</summary>
    public int EyeRgb { get; set; }
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

    /// <summary>The species begs for the food a player holds (#2018) — see <see cref="CreatureSpecies.BegsForFood"/>. The rule
    /// (<see cref="HerdRules.BegsForFood"/>) honours it on passive Land species only.</summary>
    public bool BegsForFood { get; set; }

    /// <summary>#2081: the arms of a biped (2); ignored by the other plans.</summary>
    public int Arms { get; set; }

    /// <summary>#2081: the head's size relative to the classic head (1 = classic, a biped's big head 1.3–2.0).</summary>
    public float HeadRatio { get; set; } = 1f;

    /// <summary>#2082: the item this species loves ("fruit_banana"); empty = the generator's roll for a begging species.</summary>
    public string FavouriteFood { get; set; } = string.Empty;

    /// <summary>#2082: favourite meals from one player that tame it; 0 = <see cref="HerdRules.FeedsToTame"/>.</summary>
    public int FeedsToTame { get; set; }

    // --- Hounds (#3325/#3326, generation 34) — the traits the Schuppenhund class rolls, so a worksheet hound is a data entry. ---
    public CreatureEarStyle EarStyle { get; set; }
    public float SnoutLength { get; set; }
    public bool HasClaws { get; set; }
    public bool HasTeeth { get; set; }

    /// <summary>#3326: the damage of one bite; with <see cref="BiteInterval"/> above 0 the species bites in chunks.</summary>
    public float BiteDamage { get; set; }
    public float BiteInterval { get; set; }

    // --- Giants (#1998, generation 9) — the traits the procedural colossus / sandworm roll, so the school club's
    // concrete giants (#2003) become data entries. Zero/empty on every other species. ---
    public float GiantHeight { get; set; }
    public string BackFeature { get; set; } = string.Empty;
    public float LegRatio { get; set; } = 1f;
    public int NeckLength { get; set; }
    public int Mandibles { get; set; }
    public float WormLength { get; set; }
    public float WormGirth { get; set; }
    public float Hearing { get; set; }

    /// <summary>Sandworm: it swallows the animals a breach hits (#2076) — see <see cref="CreatureSpecies.SwallowsCreatures"/>.</summary>
    public bool SwallowsCreatures { get; set; }

    /// <summary>#3365: tamed in a duel, not by the ritual (the Soundi) — see <see cref="CreatureSpecies.DuelTame"/>.</summary>
    public bool DuelTame { get; set; }

    /// <summary>#3365: the pack defends an attacked member — see <see cref="CreatureSpecies.PackDefender"/>.</summary>
    public bool PackDefender { get; set; }

    /// <summary>#3972: the coats this species comes in (<c>coats</c>) — each animal wears one, picked by its own seed. Empty
    /// (every record so far) = the designed <see cref="ColorRgb"/>/<see cref="BellyRgb"/>/<see cref="Hide"/> on every animal.</summary>
    public List<CoatVariant> Coats { get; set; } = new();

    // --- Fixed species of a home world (#3973, the animals of Earth). Empty on every older record, which behaves as before. ---

    /// <summary>#3973: the species' localized name (<c>creature.&lt;key&gt;.name</c>, <see cref="CreatureNames"/>) instead of a
    /// coined or fixed proper name — "Hund" in German, "Dog" in English. Empty = <see cref="NamePrefix"/> and the coined word.</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>#3973: the planet types this species lives on (keys of <c>data/planets.json</c>), beside the types that list it in
    /// their <see cref="PlanetType.AuthoredCreatures"/>: the roster of such a type appends it after the type's own list
    /// (<c>GameContent.AuthoredCreaturesFor</c>). An unknown type is a load warning (the type may come with another pack).</summary>
    public List<string> Homes { get; set; } = new();

    /// <summary>#3973: the climate zones (flora theme names, <see cref="Definitions.FloraThemes"/>) this species is native to,
    /// beside <see cref="BiomeSurfaces"/> — the biome affinity and the exclusive rule accept either (<see cref="CreatureBiomes"/>).</summary>
    public List<string> FloraThemes { get; set; } = new();

    /// <summary>#3973: the species' own recorded calls by mood (<see cref="SpeciesVoices"/>, JSON
    /// <c>{"calm": [...], "alarm": [...], "friendly": [...]}</c>) instead of the body plan's voice; null = the body plan's.</summary>
    public SpeciesVoices? Voices { get; set; }

    /// <summary>#3973: the titan body carries a trunk (the elephant) — see <see cref="CreatureSpecies.HasTrunk"/>.</summary>
    public bool HasTrunk { get; set; }

    /// <summary>#3973: a shy animal that bites back when hit — see <see cref="CreatureSpecies.DefendsItself"/>.</summary>
    public bool DefendsItself { get; set; }

    // --- Beaks (#3974): see CreatureSpecies.BeakStyle. None on every older record. ---
    public CreatureBeakStyle BeakStyle { get; set; }
    public float BeakLength { get; set; }
    public int BeakRgb { get; set; }
    public bool HasComb { get; set; }

    /// <summary>#3973: the English text of <see cref="NameKey"/> — the species' language-neutral fallback name (logs, saves, an
    /// older client). Filled at content load from the English locale; empty without a name key.</summary>
    public string EnglishName => _englishName;

    private string _englishName = string.Empty;

    /// <summary>Sets <see cref="EnglishName"/> (the content loader, from the English locale).</summary>
    internal void SetEnglishName(string name) => _englishName = name ?? string.Empty;
}
