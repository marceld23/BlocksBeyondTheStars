// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// One rolled species (<see cref="MakeSpecies"/>) and the weighted trait picks it draws from — habitat, temperament,
/// colours, body parts, gait, group size and drop. The draws run in a fixed order; a newer trait is always rolled after
/// every older one, so a world keeps its fauna.
/// </summary>
public static partial class CreatureGenerator
{
    /// <summary>Generation 15 (#2069): the eye colour — 60 % keep the classic pale eye, the rest draw one of five irises; a
    /// hunter leans red, a glower violet. The FINAL draw of a species, after the herds, and only on a generation-15 world, so
    /// a species of any older world keeps every trait it had.</summary>
    private static void ApplyEyeColour(System.Random rng, CreatureSpecies sp)
    {
        if (rng.NextDouble() < 0.6)
        {
            return;
        }

        int pick = sp.Hostile && rng.NextDouble() < 0.5 ? 4
            : sp.Glows && rng.NextDouble() < 0.5 ? 3
            : rng.Next(0, 3);
        sp.EyeRgb = EyePalette[pick];
    }

    /// <summary>The rolled irises (#2069): amber, blue, black, violet, red.</summary>
    private static readonly int[] EyePalette = { 0xE0A020, 0x3A7BD5, 0x101010, 0x8A2BE2, 0xD03030 };

    /// <summary>One rolled species of roster slot <paramref name="index"/>, every trait drawn from <paramref name="rng"/> in its fixed order.</summary>
    private static CreatureSpecies MakeSpecies(int index, System.Random rng, bool allowWater, bool allowLava, bool allowCave, int biomeCount, CreatureHabitat? forcedHabitat, long speciesSeed, int terrainGeneration)
    {
        var habitat = forcedHabitat ?? PickHabitat(rng, allowWater, allowLava, allowCave);
        var species = RollClassicSpecies(index, rng, habitat, biomeCount);
        RollMorphology(rng, species);

        // Movement signature (item: natural locomotion): a randomly-chosen gait biased by the body + habitat +
        // temperament we just generated, so fauna move in recognisably different ways. Drawn LAST so the species'
        // appearance rolls (hence existing worlds' rosters) are unchanged by adding it.
        species.LocoStyle = PickLocoStyle(rng, species, terrainGeneration);

        // Body plan + social rolls (#637/#638/#639) — appended AFTER every legacy roll (same discipline
        // as LocoStyle above) so pre-existing worlds keep their species' identity; a plan then only
        // OVERRIDES the traits it needs rather than biasing the draws that precede it.
        ApplyBodyPlan(rng, species);
        species.SocialGroupSize = PickSocialGroupSize(rng, species);

        // Voice seed (#907): folded from the species' own sub-seed rather than drawn from `rng`, so
        // adding it changed no existing world's traits by a single bit. It must NOT come from
        // species.Id — ids are "sp0".."sp8" and repeat on every planet, which is exactly why the old
        // client-side id hash gave the whole game nine voices per habitat.
        species.VoiceSeed = unchecked((int)(speciesSeed ^ (speciesSeed >> 32)) ^ 0x5EED_1CE);

        // Fins: folded from the voice seed above, NOT drawn from `rng` — same discipline as the seed itself,
        // so adding them changed no existing world's traits by a single bit. Must come after the body plan
        // (a medusa never has fins) and after the voice seed it reads.
        species.HasFins = CreatureMotion.FinsFor(species);

        if (species.Habitat == CreatureHabitat.Air && species.HoverAltitude <= 0f)
        {
            // Per-species hover altitude (#637) so the sky gets layers instead of one uniform band.
            species.HoverAltitude = 3f + (float)rng.NextDouble() * 9f; // 3..12
        }

        ApplyGenerationRolls(rng, species, terrainGeneration);
        return species;
    }

    /// <summary>The classic first draws of a species, in their fixed order: temperament, activity, size, drop, biome
    /// affinity, the two body tones, then the name, speed and bite. <see cref="RollMorphology"/> continues the stream.</summary>
    private static CreatureSpecies RollClassicSpecies(int index, System.Random rng, CreatureHabitat habitat, int biomeCount)
    {
        var temperament = (CreatureTemperament)Weighted(rng, // B18: fewer hostiles — more peaceful fauna
            (int)CreatureTemperament.Passive, 42,
            (int)CreatureTemperament.Skittish, 30,
            (int)CreatureTemperament.Territorial, 16,
            (int)CreatureTemperament.Aggressive, 9,
            (int)CreatureTemperament.PackHunter, 3);
        var activity = (CreatureActivity)Weighted(rng,
            (int)CreatureActivity.Diurnal, 40,
            (int)CreatureActivity.Nocturnal, 30,
            (int)CreatureActivity.Crepuscular, 20,
            (int)CreatureActivity.Cathemeral, 10);

        bool hostile = temperament is CreatureTemperament.Aggressive or CreatureTemperament.PackHunter;
        float size = 0.6f + (float)rng.NextDouble() * 1.6f;

        var (dropKind, dropItem, dropCount) = PickDrop(rng);

        // Per-biome palette (item-21 rest): species native to a biome share a recognisable colour family —
        // their hue is pulled toward that biome's anchor hue, so region A's fauna reads green-ish while
        // region B's reads violet-ish on the same world. Biome-agnostic species keep their free colour.
        int biomeAffinity = biomeCount <= 1 ? -1 : rng.Next(biomeCount);
        int colorRgb = PickColor(rng, habitat);
        int bellyRgb = PickColor(rng, habitat);
        if (biomeAffinity >= 0)
        {
            colorRgb = ShiftTowardBiomeHue(colorRgb, biomeAffinity);
            bellyRgb = ShiftTowardBiomeHue(bellyRgb, biomeAffinity);
        }

        return new CreatureSpecies
        {
            Id = "sp" + index,
            NameKey = "creature.generic.name",
            Name = NameGenerator.Creature(rng),
            Habitat = habitat,
            Activity = activity,
            Temperament = temperament,

            Size = size,
            MaxHealth = 10f + size * 8f + (hostile ? 10f : 0f),
            Speed = 1.5f + (float)rng.NextDouble() * 2.5f,
            AttackDamage = hostile ? 2f + (float)rng.NextDouble() * 5f : 0f,

            ColorRgb = colorRgb,
            BellyRgb = bellyRgb, // a second tone → two-tone bodies
            BiomeAffinity = biomeAffinity, // native to one biome on multi-biome worlds

            DropItem = dropItem,
            DropCount = dropCount,
            DropKind = dropKind,
        };
    }

    /// <summary>The classic body parts, drawn right after <see cref="RollClassicSpecies"/> in their fixed order: legs,
    /// wings, tail, segments, eyes, horns, crest, tentacles, eyestalks, gas sac, glow.</summary>
    private static void RollMorphology(System.Random rng, CreatureSpecies species)
    {
        var habitat = species.Habitat;
        bool cave = habitat == CreatureHabitat.Cave;
        species.Legs = PickLegs(rng, habitat);
        species.HasWings = habitat == CreatureHabitat.Air
                           || (habitat != CreatureHabitat.Cave && habitat != CreatureHabitat.Amphibian && rng.NextDouble() < 0.1);
        species.HasTail = rng.NextDouble() < (habitat == CreatureHabitat.Amphibian ? 0.85 : 0.5);
        species.BodySegments = 1 + rng.Next(4);                            // 1..4 — some long, segmented bodies
        // Cave dwellers are mostly eyeless (they navigate in the dark); surface fauna keep the normal mix.
        species.Eyes = cave ? Weighted(rng, 0, 55, 1, 15, 2, 25, 4, 5)
                            : Weighted(rng, 0, 12, 1, 6, 2, 44, 3, 16, 4, 14, 6, 6, 8, 2);
        species.Horns = Weighted(rng, 0, 50, 1, 18, 2, 15, 3, 12, 4, 5);   // none / one / two / three / four
        species.HasCrest = rng.NextDouble() < 0.32;                        // a dorsal frill on roughly a third
        // item-21 morphology rest: tentacles for the wet/dark fauna, snail-like eyestalks, and a
        // translucent buoyancy gas-sac on the odd floating grazer — beyond just legs/eyes.
        species.Tentacles = PickTentacles(rng, habitat);
        species.EyeStalks = rng.NextDouble() < habitat switch
        {
            CreatureHabitat.Amphibian => 0.45,
            CreatureHabitat.Cave => 0.35,
            CreatureHabitat.Water => 0.25,
            _ => 0.10,
        };
        species.HasGasSac = habitat == CreatureHabitat.Air
            ? rng.NextDouble() < 0.35
            : habitat == CreatureHabitat.Land && rng.NextDouble() < 0.06;
        // Lava + cave dwellers are bioluminescent (the only light down in the dark); others sometimes glow.
        species.Glows = habitat == CreatureHabitat.Lava || cave || rng.NextDouble() < (species.Activity == CreatureActivity.Nocturnal ? 0.3 : 0.12);
    }

    /// <summary>The generation-gated rolls, each after every older one and only on a world of its generation, so a species
    /// of any older world keeps every trait it had.</summary>
    private static void ApplyGenerationRolls(System.Random rng, CreatureSpecies species, int terrainGeneration)
    {
        // Generation 6 (#1778-#1782): rays, air fish, extra heads / wing pairs / fin pairs. Rolled AFTER every
        // roll above and applied only on a generation-6 world, so a species of any older world keeps every
        // trait it had — the same discipline as the body plans (appended) and the authored overlay (gated).
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.NewKindsGeneration)
        {
            ApplyNewKinds(rng, species);
        }

        // Generation 10 (#2009): the arachnid — rolled LAST, after every older roll, and only on a generation-10 world,
        // so a species of any older world keeps every trait it had (the roll is the final draw, nothing reads the RNG
        // after it). One draw per standard-plan Land species; the plan then overrides what the body demands and
        // re-rolls what depends on it (the gait, the group size).
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.ArachnidGeneration
            && species.Habitat == CreatureHabitat.Land && species.BodyPlan == CreatureBodyPlan.Standard
            && rng.NextDouble() < ArachnidChance)
        {
            ApplyArachnidPlan(rng, species);
        }

        // Generation 12 (#2018): big herds and the begging trait — rolled after the arachnid draw (so a generation-10/11
        // species keeps every trait it had; nothing reads the RNG after this) and only on a generation-12 world.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BigHerdsGeneration)
        {
            ApplyBigHerds(rng, species);
        }

        // Generation 15 (#2069): the eye colour — after the herds (so a generation-12..14 species keeps every trait it had;
        // nothing reads the RNG after this) and only on a generation-15 world.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.ToxicaMaximaGeneration)
        {
            ApplyEyeColour(rng, species);
        }

        // Generation 16 (#2081): the biped — rolled LAST, after the eye colour (so a generation-15 species keeps every trait it
        // had; nothing reads the RNG after this) and only on a generation-16 world. One draw per standard-plan Land species.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BipedGeneration
            && species.Habitat == CreatureHabitat.Land && species.BodyPlan == CreatureBodyPlan.Standard
            && rng.NextDouble() < BipedRules.BipedChance)
        {
            ApplyBipedPlan(rng, species);
        }

        // Generation 18 (#2109): the worm — rolled LAST, after the biped draw (so a generation-16/17 species keeps every trait
        // it had; nothing reads the RNG after this) and only on a generation-18 world. One draw per standard-plan Land species
        // that the biped draw left standard.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.WormGeneration
            && species.Habitat == CreatureHabitat.Land && species.BodyPlan == CreatureBodyPlan.Standard
            && rng.NextDouble() < WormRules.WormChance)
        {
            ApplyWormPlan(rng, species);
        }
    }

    /// <summary>How many individuals a species lives in a group of (#639): titan herds, fish schools,
    /// flocks of fliers, small groups of peaceful grazers — predators and everything else stay solitary
    /// (pack-hunters excepted: their pack IS a group).</summary>
    private static int PickSocialGroupSize(System.Random rng, CreatureSpecies sp)
    {
        if (sp.BodyPlan == CreatureBodyPlan.Titan)
        {
            return 2 + rng.Next(3); // 2..4 — megafauna reads best in a small herd
        }

        if (sp.Temperament == CreatureTemperament.PackHunter)
        {
            return 2 + rng.Next(2); // 2..3 — the pack hunts together from the start
        }

        if (sp.Habitat == CreatureHabitat.Water && sp.LocoStyle == LocomotionStyle.Schooler)
        {
            return 3 + rng.Next(3); // 3..5 — an actual school
        }

        if (sp.Habitat == CreatureHabitat.Air && rng.NextDouble() < 0.4)
        {
            return 2 + rng.Next(3); // 2..4 — a loose flock
        }

        if (sp.Habitat == CreatureHabitat.Land
            && sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish
            && rng.NextDouble() < 0.25)
        {
            return 2 + rng.Next(2); // 2..3 — grazers sometimes come in pairs/trios
        }

        return 1;
    }

    /// <summary>Picks a creature's <see cref="LocomotionStyle"/> from its already-generated traits: limbless +
    /// segmented bodies slither, two-leggers hop, gas-sacs drift, fliers glide, water fauna school, and the
    /// temperament colours the ground gait (predators prowl, skittish ones dart, grazers feed in stop-and-go).</summary>
    private static LocomotionStyle PickLocoStyle(System.Random rng, CreatureSpecies sp, int terrainGeneration = 0)
    {
        var w = new List<(int Value, int Weight)>();
        void Add(LocomotionStyle st, int weight) { if (weight > 0) w.Add(((int)st, weight)); }

        if (sp.Habitat == CreatureHabitat.Air)
        {
            Add(LocomotionStyle.Glider, sp.HasWings ? 50 : 20);
            Add(LocomotionStyle.Drifter, sp.HasGasSac ? 45 : 20);
            Add(LocomotionStyle.Strider, 10);
        }
        else if (sp.Habitat == CreatureHabitat.Water)
        {
            Add(LocomotionStyle.Schooler, 50);
            Add(LocomotionStyle.Slitherer, sp.Legs == 0 ? 30 : 12);
            Add(LocomotionStyle.Drifter, 12);
        }
        else // land / cave / lava / amphibian — ground movers
        {
            if (sp.Legs == 0) Add(LocomotionStyle.Slitherer, 45);
            if (sp.Legs == 2) Add(LocomotionStyle.Hopper, 35);
            // Generation 18 (#2109): a long body with legs no longer "slithers" — whoever slithers is a legless worm. The
            // draw count is the same, only the weights differ, so the gate changes nothing below generation 18.
            if (sp.BodySegments >= 3 && sp.Legs > 0 && terrainGeneration < BlocksBeyondTheStars.Shared.World.WorldDescription.WormGeneration) Add(LocomotionStyle.Slitherer, 25);
            if (sp.HasGasSac) Add(LocomotionStyle.Drifter, 30);

            switch (sp.Temperament)
            {
                case CreatureTemperament.Aggressive:
                case CreatureTemperament.PackHunter:
                    Add(LocomotionStyle.Prowler, 45); Add(LocomotionStyle.Strider, 20); break;
                case CreatureTemperament.Skittish:
                    Add(LocomotionStyle.Darter, 45); Add(LocomotionStyle.Strider, 15); break;
                case CreatureTemperament.Passive:
                    Add(LocomotionStyle.Grazer, 45); Add(LocomotionStyle.Strider, 20); break;
                default: // Territorial
                    Add(LocomotionStyle.Strider, 35); Add(LocomotionStyle.Grazer, 20); break;
            }

            Add(LocomotionStyle.Strider, 10); // always a small chance of the plain strider
        }

        return (LocomotionStyle)WeightedList(rng, w);
    }

    private static CreatureHabitat PickHabitat(System.Random rng, bool allowWater, bool allowLava, bool allowCave)
    {
        // Land/air always possible; water/lava/cave/amphibian only on suitable worlds.
        var weights = new List<(int Value, int Weight)>
        {
            ((int)CreatureHabitat.Land, 50),
            ((int)CreatureHabitat.Air, 20),
        };
        if (allowWater) weights.Add(((int)CreatureHabitat.Water, 18));
        if (allowWater) weights.Add(((int)CreatureHabitat.Amphibian, 12)); // shoreline dwellers need water too
        if (allowLava) weights.Add(((int)CreatureHabitat.Lava, 10));
        if (allowCave) weights.Add(((int)CreatureHabitat.Cave, 16));        // subterranean fauna fill the caves
        return (CreatureHabitat)WeightedList(rng, weights);
    }

    private static (CreatureDropKind, string, int) PickDrop(System.Random rng)
    {
        int kind = Weighted(rng,
            (int)CreatureDropKind.Food, 50,
            (int)CreatureDropKind.Poison, 30,
            (int)CreatureDropKind.Material, 20); // material substitute is the rare one
        return (CreatureDropKind)kind switch
        {
            CreatureDropKind.Food => (CreatureDropKind.Food, "creature_meat", 1 + rng.Next(2)),
            CreatureDropKind.Poison => (CreatureDropKind.Poison, "toxic_gland", 1),
            _ => (CreatureDropKind.Material, MaterialSubstitute(rng), 1 + rng.Next(2)),
        };
    }

    /// <summary>A real building resource a creature can yield instead of mining (rare).</summary>
    private static string MaterialSubstitute(System.Random rng)
    {
        string[] mats = { "carbon", "silicate", "copper_ore", "iron_ore" };
        return mats[rng.Next(mats.Length)];
    }

    /// <summary>Tentacle count by habitat (item-21 morphology): water dwellers often trail 4–6, cave +
    /// amphibian fauna sometimes 2–4, the occasional land/air oddity 2–3, most species none.</summary>
    private static int PickTentacles(System.Random rng, CreatureHabitat habitat) => habitat switch
    {
        CreatureHabitat.Water => rng.NextDouble() < 0.55 ? 4 + rng.Next(3) : 0,
        CreatureHabitat.Cave => rng.NextDouble() < 0.35 ? 2 + rng.Next(3) : 0,
        CreatureHabitat.Amphibian => rng.NextDouble() < 0.30 ? 2 + rng.Next(3) : 0,
        _ => rng.NextDouble() < 0.08 ? 2 + rng.Next(2) : 0,
    };

    /// <summary>Pulls a colour's hue toward its biome's anchor hue (item-21 per-biome palettes): each biome
    /// index owns a fixed anchor on the hue wheel (golden-ratio spaced so neighbours differ clearly), and a
    /// native species blends ~45% toward it — a shared family tint per region, with individuality kept.</summary>
    private static int ShiftTowardBiomeHue(int rgb, int biome)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, b = (rgb & 0xFF) / 255f;
        float max = System.Math.Max(r, System.Math.Max(g, b)), min = System.Math.Min(r, System.Math.Min(g, b));
        float v = max, d = max - min;
        float s = max <= 0f ? 0f : d / max;
        float h = 0f;
        if (d > 0f)
        {
            if (max == r) h = ((g - b) / d % 6f + 6f) % 6f / 6f;
            else if (max == g) h = ((b - r) / d + 2f) / 6f;
            else h = ((r - g) / d + 4f) / 6f;
        }

        float anchor = (0.07f + biome * 0.382f) % 1f; // golden-ratio spacing across the wheel
        float delta = anchor - h;
        if (delta > 0.5f) delta -= 1f;
        if (delta < -0.5f) delta += 1f;
        h = (h + delta * 0.45f + 1f) % 1f;
        s = System.Math.Max(s, 0.35f); // keep the family tint visible even on washed-out bases
        return HsvToRgb(h, s, v);
    }

    private static int PickLegs(System.Random rng, CreatureHabitat habitat) => habitat switch
    {
        CreatureHabitat.Water => 0,
        CreatureHabitat.Air => 2,
        CreatureHabitat.Amphibian => new[] { 0, 2, 4 }[rng.Next(3)], // finned swimmers to short-legged crawlers
        CreatureHabitat.Cave => new[] { 0, 4, 6, 8 }[rng.Next(4)],   // crawlers / many-legged cave things
        _ => new[] { 2, 4, 6 }[rng.Next(3)],
    };

    private static int PickColor(System.Random rng, CreatureHabitat habitat)
    {
        // ~Half of all species are vivid exotics — a fully random hue at high saturation, so pinks, violets,
        // yellows, teals and oranges are common (alien fauna, not just earthy naturals). The rest are a
        // habitat-tinted base hue with a wide jitter, so a world still spans subtle naturals to bold exotics.
        if (rng.NextDouble() < 0.5)
        {
            float h = (float)rng.NextDouble();                       // any hue on the wheel
            float s = 0.6f + (float)rng.NextDouble() * 0.4f;          // strongly saturated
            float v = 0.7f + (float)rng.NextDouble() * 0.3f;          // bright
            return HsvToRgb(h, s, v);
        }

        int r, g, b;
        int j() => rng.Next(-60, 61);
        switch (habitat)
        {
            case CreatureHabitat.Water: r = 60; g = 130; b = 200; break;
            case CreatureHabitat.Amphibian: r = 80; g = 150; b = 150; break; // teal, between water + land
            case CreatureHabitat.Lava: r = 210; g = 90; b = 40; break;
            case CreatureHabitat.Cave: r = 190; g = 195; b = 210; break;     // pale, washed-out cave-dweller
            case CreatureHabitat.Air: r = 200; g = 200; b = 170; break;
            default: r = 110; g = 150; b = 80; break; // land
        }

        return (Clamp8(r + j()) << 16) | (Clamp8(g + j()) << 8) | Clamp8(b + j());
    }

    /// <summary>HSV→RGB (h,s,v in 0..1) packed as 0xRRGGBB — used for vivid, evenly-spread exotic hues.</summary>
    private static int HsvToRgb(float h, float s, float v)
    {
        float r = v, g = v, b = v;
        if (s > 0f)
        {
            float hf = (h - (float)System.Math.Floor(h)) * 6f;
            int i = (int)hf;
            float f = hf - i;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (i % 6)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }

        return (Clamp8((int)(r * 255f + 0.5f)) << 16) | (Clamp8((int)(g * 255f + 0.5f)) << 8) | Clamp8((int)(b * 255f + 0.5f));
    }

    private static int Clamp8(int v) => v < 0 ? 0 : (v > 255 ? 255 : v);

    // --- Weighted picks (deterministic from the RNG) ---

    private static int Weighted(System.Random rng, params int[] valueWeightPairs)
    {
        var list = new List<(int, int)>();
        for (int i = 0; i + 1 < valueWeightPairs.Length; i += 2)
        {
            list.Add((valueWeightPairs[i], valueWeightPairs[i + 1]));
        }

        return WeightedList(rng, list);
    }

    private static int WeightedList(System.Random rng, List<(int Value, int Weight)> weights)
    {
        int total = 0;
        foreach (var (_, w) in weights) total += w;
        int roll = rng.Next(total);
        foreach (var (value, w) in weights)
        {
            roll -= w;
            if (roll < 0) return value;
        }

        return weights[weights.Count - 1].Value;
    }
}
