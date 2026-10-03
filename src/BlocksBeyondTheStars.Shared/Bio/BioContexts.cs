// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Bio;

/// <summary>
/// Reads the <see cref="BioContext"/> of a species or a deposit from what the game already knows about it — planet →
/// biome → species (#2200). Nothing here is rolled: the context is the plain truth of where and how a thing lives, and
/// the seed then draws <i>within</i> it. That is what lets a player search with reason.
/// </summary>
public static class BioContexts
{
    /// <summary>A world this hot (°C) or hotter is a hot world, this cold or colder a cold one.</summary>
    public const double HotWorld = 45.0;
    public const double ColdWorld = -10.0;

    /// <summary>The tags a planet type gives everything that lives or lies on it.</summary>
    public static BioTag PlanetTags(PlanetType planet)
    {
        var tags = BioTag.None;
        if (planet.BaseTemperature >= HotWorld || planet.ExposureMinutesHot > 0)
        {
            tags |= BioTag.Hot;
        }

        if (planet.BaseTemperature <= ColdWorld || planet.ExposureMinutesCold > 0)
        {
            tags |= BioTag.Cold;
        }

        if (planet.CorrosiveAirChance > 0 || planet.AirDamagePerSecond > 0 || planet.WaterDamagePerSecond > 0 || planet.ContaminatedFauna)
        {
            tags |= BioTag.ToxicWorld;
        }

        if (PlanetPoints(planet) >= 2)
        {
            tags |= BioTag.Frontier;
        }

        return tags;
    }

    /// <summary>Scarcity points of a planet type: the rarer the galaxy rolls it, the more.</summary>
    public static int PlanetPoints(PlanetType planet)
        => planet.OncePerGalaxy || planet.SpawnWeight <= 1 ? 3 : planet.SpawnWeight <= 4 ? 2 : planet.SpawnWeight <= 9 ? 1 : 0;

    /// <summary>The context of an animal species on a world.</summary>
    public static BioContext ForCreature(CreatureSpecies sp, PlanetType planet)
    {
        var tags = PlanetTags(planet);
        int points = PlanetPoints(planet);

        switch (sp.Habitat)
        {
            case CreatureHabitat.Lava: tags |= BioTag.Lava | BioTag.Hot; points += 3; break;
            case CreatureHabitat.Cave: tags |= BioTag.Cave; points += 2; break;
            case CreatureHabitat.Air: tags |= BioTag.Air; points += 1; break;
            case CreatureHabitat.Amphibian: tags |= BioTag.Water; points += 1; break;
            case CreatureHabitat.Water: tags |= BioTag.Water; break;
        }

        switch (sp.Temperament)
        {
            case CreatureTemperament.Skittish: tags |= BioTag.Skittish; points += 1; break;
            case CreatureTemperament.Territorial: points += 1; break;
            case CreatureTemperament.Aggressive: points += 2; break;
            case CreatureTemperament.PackHunter: tags |= BioTag.PackHunter; points += 3; break;
        }

        switch (sp.BodyPlan)
        {
            case CreatureBodyPlan.Titan: tags |= BioTag.Titan | BioTag.Big; points += 3; break;
            case CreatureBodyPlan.Arachnid: tags |= BioTag.Arachnid | BioTag.ManyLegs; points += 1; break;
            case CreatureBodyPlan.Medusa:
            case CreatureBodyPlan.Ray:
            case CreatureBodyPlan.Worm:
            case CreatureBodyPlan.Biped: points += 1; break;
        }

        if (sp.IsGiant)
        {
            tags |= BioTag.Titan | BioTag.Big;
            points = Math.Max(points, 9); // one per world: always legendary
        }

        // The biome it is native to: a hot zone or snow underfoot outweighs the planet's average.
        if (sp.BiomeAffinity >= 0 && sp.BiomeAffinity < planet.Biomes.Count)
        {
            tags |= BiomeTags(planet.Biomes[sp.BiomeAffinity]);
        }

        foreach (string surface in sp.BiomeSurfaces)
        {
            tags |= SurfaceTags(surface);
        }

        if (sp.Activity == CreatureActivity.Nocturnal) { tags |= BioTag.Nocturnal; }
        if (sp.AttackDamage > 0f) { tags |= BioTag.Fierce; }
        if (sp.Speed >= 3.2f) { tags |= BioTag.Fast; }
        if (sp.Size <= 0.9f) { tags |= BioTag.Small; }
        if (sp.Size >= 1.7f) { tags |= BioTag.Big; }
        if (sp.HasWings) { tags |= BioTag.Winged; }
        if (sp.HasGasSac) { tags |= BioTag.GasSac; }
        if (sp.Legs >= 6) { tags |= BioTag.ManyLegs; }
        if (sp.Tentacles > 0) { tags |= BioTag.Tentacles; }
        if (sp.Horns >= 2) { tags |= BioTag.Horned; }
        if (sp.Eyes >= 4 || sp.EyeStalks) { tags |= BioTag.ManyEyes; }
        if (sp.LocoStyle == LocomotionStyle.Darter) { tags |= BioTag.Darter; }
        if (sp.Temperament == CreatureTemperament.Passive && sp.SocialGroupSize >= 2) { tags |= BioTag.Grazer; }
        if (sp.Glows) { tags |= BioTag.Glow; points += 1; }
        if (sp.Heads > 1) { points += 1; }

        tags |= sp.DropKind switch
        {
            CreatureDropKind.Poison => BioTag.PoisonYield,
            CreatureDropKind.Material => BioTag.MaterialYield,
            _ => BioTag.FoodYield,
        };
        if (sp.DropItem == "crystal")
        {
            tags |= BioTag.CrystalYield;
        }

        bool poison = sp.DropKind == CreatureDropKind.Poison;
        return new BioContext
        {
            Kind = BioKind.Animal,
            Tags = (ulong)tags,
            RarityPoints = points,
            Toxicity = planet.ContaminatedFauna ? 3 : poison ? 2 : 0,
            Carrier = sp.DropKind == CreatureDropKind.Food ? BioCarrier.Tissue : BioCarrier.Secretion,
        };
    }

    /// <summary>
    /// The context of a plant species on a world. <paramref name="onTheme"/> says whether the species fits the world's
    /// flora theme — an off-theme plant is the occasional find. <paramref name="yields"/> are the item keys its block drops.
    /// </summary>
    public static BioContext ForFlora(FloraCatalog.Species archetype, bool toxic, bool onTheme, PlanetType planet, IEnumerable<string> yields)
    {
        var tags = PlanetTags(planet);
        int points = PlanetPoints(planet);

        var ft = archetype.Tags;
        if ((ft & FloraTag.Lush) != 0) { tags |= BioTag.Lush; }
        if ((ft & FloraTag.Dry) != 0) { tags |= BioTag.Dry; }
        if ((ft & FloraTag.Cold) != 0) { tags |= BioTag.Cold; }
        if ((ft & FloraTag.Fungal) != 0) { tags |= BioTag.Fungal; }
        if ((ft & FloraTag.Alien) != 0) { tags |= BioTag.Alien; }
        if ((ft & FloraTag.Rocky) != 0) { tags |= BioTag.Rocky; }
        if ((ft & FloraTag.Wetland) != 0) { tags |= BioTag.Wetland; }
        if ((ft & FloraTag.Tropical) != 0) { tags |= BioTag.Tropical; }
        if ((ft & FloraTag.Glow) != 0) { tags |= BioTag.Glow; }
        if ((ft & FloraTag.Floral) != 0) { tags |= BioTag.Floral; }

        if (archetype.Aquatic) { tags |= BioTag.Water; points += 1; }
        if (archetype.Hanging) { tags |= BioTag.Hanging; points += 1; }
        if (archetype.Habitat == FloraHabitat.Cave) { tags |= BioTag.Cave; points += 2; }
        if (archetype.Habitat == FloraHabitat.Both) { points += 1; }
        if (archetype.Rainbow) { tags |= BioTag.Rainbow; points += 3; }
        if (archetype.Light > 0f) { tags |= BioTag.Glow; points += 1; }
        if (archetype.Fruit) { tags |= BioTag.Fruit; points += 1; }
        if (!onTheme) { points += 2; }
        if (toxic) { points += 1; }

        bool food = false;
        foreach (string item in yields)
        {
            if (item == "crystal") { tags |= BioTag.CrystalYield; }
            if (item == "toxic_gland") { tags |= BioTag.PoisonYield; }
            if (item == "berries" || item == "grain" || item.StartsWith("fruit_", StringComparison.Ordinal)) { food = true; }
        }

        if (food)
        {
            tags |= BioTag.FoodYield;
        }

        return new BioContext
        {
            Kind = BioKind.Plant,
            Tags = (ulong)tags,
            RarityPoints = points,
            Toxicity = toxic ? ((tags & BioTag.ToxicWorld) != 0 ? 3 : 2) : 0,
            Carrier = food ? BioCarrier.Juice : BioCarrier.Fibre,
        };
    }

    /// <summary>The context of a world's tree species (trunk and leaves are one species).</summary>
    public static BioContext ForTree(bool toxic, PlanetType planet)
    {
        var tags = PlanetTags(planet) | BioTag.Lush;
        return new BioContext
        {
            Kind = BioKind.Plant,
            Tags = (ulong)tags,
            RarityPoints = PlanetPoints(planet) + (toxic ? 1 : 0),
            Toxicity = toxic ? ((tags & BioTag.ToxicWorld) != 0 ? 3 : 2) : 0,
            Carrier = BioCarrier.Fibre,
        };
    }

    /// <summary>The context of a deposit: a material on a world. <paramref name="vein"/> is the planet type's vein rule
    /// for the mined block when it has one.</summary>
    public static BioContext ForDeposit(PlanetType planet, OreVein? vein, bool tainted)
    {
        var tags = PlanetTags(planet);
        int points = 0;
        if (vein is not null)
        {
            points += vein.Rarity <= 0.012 ? 3 : vein.Rarity <= 0.02 ? 2 : vein.Rarity <= 0.04 ? 1 : 0;
            points += vein.RareTier ? 1 : 0;
            points += vein.MinDepth >= 40 ? 1 : 0;
        }

        if (tainted)
        {
            points += 1;
            tags |= BioTag.ToxicWorld;
        }

        return new BioContext { Kind = BioKind.Mineral, Tags = (ulong)tags, RarityPoints = points, Carrier = BioCarrier.Mineral };
    }

    private static BioTag BiomeTags(Biome biome)
    {
        var tags = SurfaceTags(biome.SurfaceBlock);
        if (biome.HotZone || biome.Temperature >= HotWorld)
        {
            tags |= BioTag.Hot;
        }

        if (biome.Temperature <= ColdWorld)
        {
            tags |= BioTag.Cold;
        }

        return tags;
    }

    private static BioTag SurfaceTags(string surfaceBlock) => surfaceBlock switch
    {
        "snow" or "ice" => BioTag.Cold,
        "basalt" or "ash" or "obsidian" => BioTag.Hot,
        _ => BioTag.None,
    };
}
