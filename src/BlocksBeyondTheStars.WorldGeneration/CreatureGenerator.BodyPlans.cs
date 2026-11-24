// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The body plans a rolled species may take on top of its classic traits — worm, biped, arachnid, ray, air fish,
/// medusa, titan, the big herds and the generation-6 kinds. Each plan is rolled after every older draw and only
/// overrides the traits its body demands.
/// </summary>
public static partial class CreatureGenerator
{
    /// <summary>#2009: the hides an arachnid rolls — shaggy is the tarantula, the rest read as armour.</summary>
    private static readonly string[] ArachnidHides = { "chitin", "plated", "spined", "banded", "shaggy", "mottled" };

    /// <summary>#2009: the share of standard-plan Land species that become arachnids on a generation-10 world — rare
    /// (Marcel: rarer than the titan's 18 %): with 5–9 species per world, roughly every third or fourth world has one.</summary>
    private const double ArachnidChance = 1.0 / 12.0;

    private static readonly string[] SmallWormHides = { "skin", "banded", "scales", "warty", "mottled" };

    /// <summary>The worm (#2109, Marcel's finding): a legless slitherer, knee- to hip-high — a head and a chain of
    /// <see cref="WormRules.MinSegments"/>–<see cref="WormRules.MaxSegments"/> links that runs a travelling wave. Colours, eye
    /// colour, temperament and drop stay as rolled; the plan overrides what the body demands (no legs, no wings, no arms, the
    /// slither style, a slow pace) and keeps the worm in a small group.</summary>
    private static void ApplyWormPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Worm;
        sp.Size = WormRules.MinSize + (float)rng.NextDouble() * (WormRules.MaxSize - WormRules.MinSize);
        sp.Legs = 0;
        sp.Arms = 0;
        sp.BodySegments = WormRules.MinSegments + rng.Next(WormRules.MaxSegments - WormRules.MinSegments + 1);
        sp.LocoStyle = LocomotionStyle.Slitherer;
        sp.Speed = WormRules.MinSpeed + (float)rng.NextDouble() * (WormRules.MaxSpeed - WormRules.MinSpeed);
        sp.MaxHealth = 10f + sp.Size * 8f + (sp.Hostile ? 10f : 0f);
        sp.Eyes = Weighted(rng, 0, 25, 1, 15, 2, 50, 4, 10);            // many worms are eyeless
        sp.Horns = Weighted(rng, 0, 65, 1, 15, 2, 20);                    // the builder draws them as antennae
        sp.EyeStalks = rng.NextDouble() < 0.2;
        sp.Hide = SmallWormHides[rng.Next(SmallWormHides.Length)];
        sp.HasTail = false;                                               // the body IS the chain
        sp.HasWings = false;
        sp.WingPairs = 1;
        sp.FinPairs = 1;
        sp.HasGasSac = false;
        sp.Tentacles = 0;
        sp.Heads = 1;
        sp.NeckLength = 0;
        sp.HasTrunk = false;
        sp.HeadShape = CreatureHeadShape.Box;
        sp.HoverAltitude = 0f;
        sp.HasFins = CreatureMotion.FinsFor(sp);
        sp.BegsForFood = false;
        sp.SocialGroupSize = 1 + rng.Next(3);
    }

    private static readonly string[] BipedHides = { "skin", "skin", "fur", "spots", "stripes", "mottled", "feathers", "scales" };

    /// <summary>The biped (#2081, the school club idea of Paul and Ben): an upright two-legger with two arms and a big head,
    /// knee-high to child-high. Always peaceful (decision 2026-09-27: passive or skittish, no bite); never alone — a passive
    /// one begs with <see cref="BipedRules.BeggingChance"/> and then lives in a begging herd, every other one in a group of
    /// <see cref="BipedRules.GroupMin"/>–<see cref="BipedRules.GroupMax"/>. Colours, eyes' colour and drop stay as rolled; the
    /// plan overrides what the body demands and re-rolls what depends on it (the gait, the group).</summary>
    private static void ApplyBipedPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Biped;
        sp.Size = BipedRules.MinSize + (float)rng.NextDouble() * (BipedRules.MaxSize - BipedRules.MinSize);
        sp.HeadRatio = BipedRules.MinHeadRatio + (float)rng.NextDouble() * (BipedRules.MaxHeadRatio - BipedRules.MinHeadRatio);
        sp.Legs = 2;
        sp.Arms = 2;
        sp.BodySegments = 1;
        sp.Temperament = rng.NextDouble() < 0.7 ? CreatureTemperament.Passive : CreatureTemperament.Skittish;
        sp.AttackDamage = 0f;
        sp.MaxHealth = 10f + sp.Size * 8f;
        sp.Speed = 2.0f + (float)rng.NextDouble() * 1.5f;          // a walking pace — the player (6) outruns them
        sp.Eyes = Weighted(rng, 2, 70, 1, 10, 3, 15, 4, 5);          // mostly two big eyes
        sp.Horns = Weighted(rng, 0, 60, 1, 15, 2, 25);                // the builder draws them as antennae
        sp.HasTail = rng.NextDouble() < 0.3;
        sp.Hide = BipedHides[rng.Next(BipedHides.Length)];
        sp.EyeStalks = false;
        sp.HasCrest = false;
        sp.HasWings = false;
        sp.WingPairs = 1;
        sp.FinPairs = 1;
        sp.HasGasSac = false;
        sp.Tentacles = 0;
        sp.Heads = 1;                                                 // generation 6 may have rolled a second head
        sp.NeckLength = 0;
        sp.HasTrunk = false;
        sp.HeadShape = CreatureHeadShape.Box;
        sp.HoverAltitude = 0f;
        sp.HasFins = CreatureMotion.FinsFor(sp);
        sp.LocoStyle = PickLocoStyle(rng, sp);                        // two legs: hopper, grazer, darter or strider
        sp.BegsForFood = sp.Temperament == CreatureTemperament.Passive && rng.NextDouble() < BipedRules.BeggingChance;
        sp.SocialGroupSize = sp.BegsForFood
            ? HerdRules.BeggarHerdMin + rng.Next(HerdRules.BeggarHerdMax - HerdRules.BeggarHerdMin + 1)
            : BipedRules.GroupMin + rng.Next(BipedRules.GroupMax - BipedRules.GroupMin + 1);
    }

    /// <summary>A rolled biped for a world whose roster has none (#2081, the <c>/biped</c> test command): this world's own Land
    /// roll with the plan forced and — so the test command shows the whole loop — begging, from a seed salted so it never
    /// collides with a roster slot. Its favourite food is picked like a roster beggar's.</summary>
    public static CreatureSpecies GenerateBiped(PlanetType planet, long rosterSeed, int terrainGeneration)
    {
        long s = unchecked(rosterSeed ^ 0xB1BED5L);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        var sp = MakeSpecies(91, rng, allowWater: false, allowLava: false, allowCave: false, biomeCount: planet.Biomes.Count,
            forcedHabitat: CreatureHabitat.Land, speciesSeed: s,
            terrainGeneration: BlocksBeyondTheStars.Shared.World.WorldDescription.BipedGeneration);
        if (sp.BodyPlan != CreatureBodyPlan.Biped)
        {
            ApplyBipedPlan(rng, sp);
        }

        sp.Temperament = CreatureTemperament.Passive;
        sp.BegsForFood = true;
        sp.SocialGroupSize = System.Math.Max(sp.SocialGroupSize, HerdRules.BeggarHerdMin);
        sp.FavouriteFood = FavouriteFrom(FruitRules.CleanFruitItems(planet, rosterSeed, terrainGeneration), sp.VoiceSeed);
        return sp;
    }

    /// <summary>The big herds (#2018, generation 12): peaceful standard-plan Land species only — a passive one begs with
    /// <see cref="HerdRules.BeggingChance"/> and then always lives in a herd of 8–12; the other passive ones and the skittish
    /// ones roll a herd of 6–9 with <see cref="HerdRules.BigHerdChance"/>. Everything else (titans keep 2–4, arachnids their
    /// solitude, every hunter and territorial species) consumes no draw at all, so its RNG stream is untouched. Skittish species
    /// never beg (decision 2026-09-26): the flee reflex and a beggar's approach contradict each other.</summary>
    private static void ApplyBigHerds(System.Random rng, CreatureSpecies sp)
    {
        if (sp.Habitat != CreatureHabitat.Land || sp.BodyPlan != CreatureBodyPlan.Standard)
        {
            return;
        }

        if (sp.Temperament == CreatureTemperament.Passive && rng.NextDouble() < HerdRules.BeggingChance)
        {
            sp.BegsForFood = true;
            sp.SocialGroupSize = HerdRules.BeggarHerdMin + rng.Next(HerdRules.BeggarHerdMax - HerdRules.BeggarHerdMin + 1);
            return;
        }

        if (sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish
            && rng.NextDouble() < HerdRules.BigHerdChance)
        {
            sp.SocialGroupSize = HerdRules.BigHerdMin + rng.Next(HerdRules.BigHerdMax - HerdRules.BigHerdMin + 1);
        }
    }

    /// <summary>The arachnid (#2009): a speeder-sized eight-legger — Size 3–3.6 on eight splayed legs, a cephalothorax
    /// and an abdomen, a head that is a box or one of four pyramids (apex up, and not always the same one), spider eye
    /// clusters, fangs on most. Temperament, activity and colours stay exactly as rolled (the normal roll, so a world's
    /// arachnid may be a grazer or a pack hunter); the gait is re-rolled for the new body; a hunter or a territorial one
    /// lies in wait (<see cref="ArachnidRules.Lurks"/>). Solitary unless it hunts as a pack.</summary>
    private static void ApplyArachnidPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Arachnid;
        sp.Size = ArachnidRules.MinSize + (float)rng.NextDouble() * (ArachnidRules.MaxSize - ArachnidRules.MinSize);
        sp.Legs = 8;
        sp.BodySegments = 2;                                    // cephalothorax + abdomen
        sp.HeadShape = (CreatureHeadShape)Weighted(rng,
            (int)CreatureHeadShape.Box, 50,
            (int)CreatureHeadShape.Pyramid, 18,
            (int)CreatureHeadShape.Spire, 12,
            (int)CreatureHeadShape.Frustum, 10,
            (int)CreatureHeadShape.Ziggurat, 10);
        sp.Eyes = Weighted(rng, 2, 20, 4, 30, 6, 25, 8, 25);   // spider eye clusters
        sp.EyeStalks = false;
        sp.Horns = rng.NextDouble() < 0.65 ? 2 : 0;             // the builder draws them as fangs
        sp.Hide = ArachnidHides[rng.Next(ArachnidHides.Length)];
        sp.HasWings = false;
        sp.WingPairs = 1;
        sp.FinPairs = 1;
        sp.HasGasSac = false;
        sp.HasTail = false;
        sp.Tentacles = 0;
        sp.Heads = 1;                                           // generation 6 may have rolled a hydra before this plan
        sp.NeckLength = 0;
        sp.HasTrunk = false;
        sp.HoverAltitude = 0f;
        sp.MaxHealth = (10f + sp.Size * 8f + (sp.Hostile ? 10f : 0f)) * 2.5f; // ≈ 85–110: a real fight, below a titan
        sp.AttackDamage = 3f + (float)rng.NextDouble() * 5f;    // used when hostile or provoked
        sp.Speed = 2.0f + (float)rng.NextDouble() * 2.0f;       // quick for its size, still below a running player
        sp.DropCount = 2 + rng.Next(3);                         // 2..4
        sp.LocoStyle = PickLocoStyle(rng, sp);                  // prowler / darter / grazer / strider by temperament
        sp.SocialGroupSize = sp.Temperament == CreatureTemperament.PackHunter ? 2 + rng.Next(2) : 1;
    }

    /// <summary>A rolled arachnid for a world whose roster has none (#2009, the <c>/arachnid</c> test command): this
    /// world's own Land roll with the plan forced, from a seed salted so it never collides with a roster slot.</summary>
    public static CreatureSpecies GenerateArachnid(PlanetType planet, long rosterSeed)
    {
        long s = unchecked(rosterSeed ^ 0x5A2A_C41DL);
        var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
        var sp = MakeSpecies(90, rng, allowWater: false, allowLava: false, allowCave: false, biomeCount: planet.Biomes.Count,
            forcedHabitat: CreatureHabitat.Land, speciesSeed: s,
            terrainGeneration: BlocksBeyondTheStars.Shared.World.WorldDescription.ArachnidGeneration);
        if (sp.BodyPlan != CreatureBodyPlan.Arachnid)
        {
            ApplyArachnidPlan(rng, sp);
        }

        return sp;
    }

    /// <summary>The generation-6 kinds (#1778-#1782), in a fixed roll order: first the body (ray / air fish),
    /// then the counts that depend on it. Standard-plan Air and Water species may become rays (20 %), a standard
    /// Air species that stayed an air animal may become an air fish (25 %); heads, wing pairs and fin pairs are
    /// rolled for whichever body came out.</summary>
    private static void ApplyNewKinds(System.Random rng, CreatureSpecies sp)
    {
        bool airOrWater = sp.Habitat is CreatureHabitat.Air or CreatureHabitat.Water;
        if (airOrWater && sp.BodyPlan == CreatureBodyPlan.Standard && rng.NextDouble() < 0.20)
        {
            ApplyRayPlan(rng, sp);
        }
        else if (sp.Habitat == CreatureHabitat.Air && sp.BodyPlan == CreatureBodyPlan.Standard && rng.NextDouble() < 0.25)
        {
            ApplyAirFish(rng, sp);
        }

        // Heads (#1780): a few standard ground bodies carry two or three; titans more often (the hydra).
        // Never on a medusa (a bell has no head), a ray (its eyes sit on the disc) or the flowerling.
        bool ground = sp.Habitat is CreatureHabitat.Land or CreatureHabitat.Cave or CreatureHabitat.Lava or CreatureHabitat.Amphibian;
        if (sp.BodyPlan == CreatureBodyPlan.Standard && ground)
        {
            sp.Heads = Weighted(rng, 1, 92, 2, 6, 3, 2);
        }
        else if (sp.BodyPlan == CreatureBodyPlan.Titan)
        {
            sp.Heads = Weighted(rng, 1, 80, 2, 15, 3, 5);
        }

        // Wing pairs (#1781): a fifth of the winged air species carry two pairs, a few three (the dragonfly);
        // a winged ground bird sometimes two. A ray's wings ARE its body — one pair, always.
        if (sp.HasWings && sp.BodyPlan != CreatureBodyPlan.Ray)
        {
            sp.WingPairs = sp.Habitat == CreatureHabitat.Air
                ? Weighted(rng, 1, 72, 2, 20, 3, 8)
                : Weighted(rng, 1, 90, 2, 10);
        }

        // Fins are derived (FinsFor), so re-read them for the body that came out; then the pair count (#1782)
        // for the legless finned bodies — the fish and the air fish.
        sp.HasFins = CreatureMotion.FinsFor(sp);
        if (sp.HasFins && sp.Legs <= 0)
        {
            sp.FinPairs = Weighted(rng, 1, 60, 2, 30, 3, 10);
        }
    }

    /// <summary>The ray (#1778): a flat disc on one pair of wing panels that beat as a travelling wave, a whip
    /// tail, eyes on top — under water a bottom-hugging glider, in the air a sky glider that never lands (see
    /// <see cref="CreatureMotion.IsSkyGlider"/>). Never a hunter: passive, skittish or territorial at most.</summary>
    private static void ApplyRayPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Ray;
        sp.Legs = 0;
        sp.HasWings = true;
        sp.HasTail = true;
        sp.HasGasSac = false;
        sp.HasCrest = false;
        sp.EyeStalks = false;
        sp.Tentacles = 0;
        sp.Horns = rng.NextDouble() < 0.3 ? 2 : 0;                  // cephalic lobes on some
        sp.BodySegments = 1 + rng.Next(2);                          // 1..2 — a disc, or a slightly longer one
        sp.Eyes = Weighted(rng, 2, 80, 0, 10, 4, 10);
        sp.Size = 1.2f + (float)rng.NextDouble() * 1.8f;            // 1.2..3
        sp.Temperament = (CreatureTemperament)Weighted(rng,
            (int)CreatureTemperament.Passive, 55,
            (int)CreatureTemperament.Skittish, 30,
            (int)CreatureTemperament.Territorial, 15);
        sp.MaxHealth = 10f + sp.Size * 8f;
        sp.AttackDamage = sp.Temperament == CreatureTemperament.Territorial ? 2f + (float)rng.NextDouble() * 3f : 0f;
        sp.Speed = 1.2f + (float)rng.NextDouble() * 1.4f;           // 1.2..2.6 — an unhurried glide
        sp.LocoStyle = sp.Habitat == CreatureHabitat.Air
            ? LocomotionStyle.Glider                                // the sky ray swoops
            : rng.NextDouble() < 0.6 ? LocomotionStyle.Drifter : LocomotionStyle.Schooler;
        sp.SocialGroupSize = 1 + rng.Next(3);                       // 1..3
        sp.Heads = 1;
        sp.WingPairs = 1;
        sp.FinPairs = 1;
    }

    /// <summary>The air fish (#1779): a fish body that lives in the air like a bird — legless, wingless, finned,
    /// tailed, gliding on its swoop wave and never landing (a sky glider, see <see cref="CreatureMotion.IsSkyGlider"/>).
    /// Everything else (colour, size, temperament, eyes, glow) stays as rolled.</summary>
    private static void ApplyAirFish(System.Random rng, CreatureSpecies sp)
    {
        sp.Legs = 0;
        sp.HasWings = false;
        sp.HasTail = true;
        sp.HasGasSac = false;
        sp.EyeStalks = false;
        sp.Tentacles = rng.NextDouble() < 0.15 ? 2 : 0;             // the odd barbel pair
        sp.LocoStyle = LocomotionStyle.Glider;
        sp.Heads = 1;
        sp.WingPairs = 1;
    }

    /// <summary>Rolls whether this species gets a non-standard body plan (#637/#638) and, if so,
    /// overrides the traits that plan demands. Drawn after all legacy rolls (see the call site).</summary>
    private static void ApplyBodyPlan(System.Random rng, CreatureSpecies sp)
    {
        if ((sp.Habitat == CreatureHabitat.Air || sp.Habitat == CreatureHabitat.Water)
            && rng.NextDouble() < 0.25)
        {
            ApplyMedusaPlan(rng, sp);
        }
        else if (sp.Habitat == CreatureHabitat.Land && rng.NextDouble() < 0.18)
        {
            ApplyTitanPlan(rng, sp);
        }
    }

    /// <summary>The floating jellyfish (#637): translucent bell, 6–10 rim tentacles, drifting, glowing
    /// more often than not — and never a threat (a hunting jellyfish would be miserable).</summary>
    private static void ApplyMedusaPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Medusa;
        sp.Legs = 0;
        sp.HasWings = false;
        sp.HasTail = false;
        sp.HasCrest = false;
        sp.EyeStalks = false;
        sp.Horns = 0;
        sp.BodySegments = 1;
        sp.HasGasSac = true;
        sp.Tentacles = 6 + rng.Next(5);                       // 6..10 long rim tentacles
        sp.Eyes = Weighted(rng, 0, 60, 1, 15, 2, 25);          // usually eyeless
        sp.Glows = rng.NextDouble() < 0.7;                     // drifting lanterns at night
        sp.Temperament = rng.NextDouble() < 0.7 ? CreatureTemperament.Passive : CreatureTemperament.Skittish;
        sp.AttackDamage = 0f;                                  // never hostile — a mood piece, not a threat
        sp.Speed = 0.8f + (float)rng.NextDouble() * 0.8f;      // slow, lazy drift
        sp.LocoStyle = LocomotionStyle.Drifter;
        sp.HoverAltitude = 3f + (float)rng.NextDouble() * 9f;  // 3..12 (air variant; water uses the column)
    }

    /// <summary>The land titan (#638): elephant/giraffe-scale megafauna — pillar legs, an optional long
    /// neck or trunk, tusks. Huntable: tanky with a matching drop pile, dangerous when provoked, but
    /// never a roaming pack-hunter.</summary>
    private static void ApplyTitanPlan(System.Random rng, CreatureSpecies sp)
    {
        sp.BodyPlan = CreatureBodyPlan.Titan;
        sp.Size = 3.5f + (float)rng.NextDouble() * 2.5f;       // 3.5..6 — far past the old 2.2 cap
        sp.Legs = 4;
        sp.HasWings = false;
        sp.HasTail = true;
        sp.HasGasSac = false;
        sp.EyeStalks = false;
        sp.Tentacles = 0;
        sp.BodySegments = 2 + rng.Next(2);                     // 2..3 — a long, heavy torso
        sp.NeckLength = rng.Next(4);                            // 0..3 (≥2 reads giraffe)
        sp.HasTrunk = sp.NeckLength <= 1 && rng.NextDouble() < 0.6; // short-necked titans often carry a trunk
        sp.Temperament = (CreatureTemperament)Weighted(rng,     // never PackHunter — dangerous when provoked,
            (int)CreatureTemperament.Passive, 60,               // not a roaming player-hunter
            (int)CreatureTemperament.Territorial, 35,
            (int)CreatureTemperament.Aggressive, 5);
        sp.MaxHealth = (10f + sp.Size * 8f) * 3.5f;             // a real fight (~150–200 HP)
        sp.AttackDamage = 4f + (float)rng.NextDouble() * 6f;    // a provoked titan genuinely hurts
        sp.Speed = 1.0f + (float)rng.NextDouble() * 1.0f;       // ponderous
        sp.DropCount = 3 + rng.Next(4);                         // 3..6 — a real reward for a real fight
        sp.LocoStyle = sp.Temperament == CreatureTemperament.Passive
            ? LocomotionStyle.Grazer
            : LocomotionStyle.Strider;                          // no hopping/darting giants
    }
}
