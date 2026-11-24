// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministically derives a planet's roster of <see cref="CreatureSpecies"/> from the world
/// seed + planet (technical requirements / `anf_space_flight.md` §12). Same seed + planet → the
/// same species every run, so the roster never needs storing. How many species a world has comes
/// from its <see cref="PlanetType.CreatureAbundance"/> (none / few / many); habitats are biased by
/// the planet (lava worlds get lava-dwellers, wet worlds get aquatic life). The roster skews
/// **non-hostile** so a world is never all-hostile.
/// </summary>
public static partial class CreatureGenerator
{
    /// <summary>This world's roster. <paramref name="terrainGeneration"/> and <paramref name="authored"/> are the
    /// school club wave (#1763): on a generation-5 world the type's authored species are appended AFTER the
    /// procedural slots, so no rolled species ever moves; older worlds and callers of the two-argument form get
    /// the procedural roster exactly as before. <paramref name="strays"/> (#3976, generation 49) are the records that may live
    /// wild away from their homes (<c>GameContent.StrayAnimalRecords</c>): the ones this world hosts join last
    /// (<see cref="StrayAnimals"/>).</summary>
    public static IReadOnlyList<CreatureSpecies> GenerateRoster(PlanetType planet, long worldSeed,
        int terrainGeneration = 0, IReadOnlyList<AuthoredCreature>? authored = null, IReadOnlyList<AuthoredCreature>? strays = null)
    {
        int count = planet.IsAirless ? 0 : AbundanceCount(planet.CreatureAbundance);
        var list = new List<CreatureSpecies>(count);
        var roll = new RosterRoll(planet, worldSeed, terrainGeneration);

        if (count > 0)
        {
            RollProceduralRoster(list, count, roll);
        }
        else if (!planet.IsAirless && roll.AllowCave && WorldTraits.For(planet, worldSeed, terrainGeneration).CaveFauna)
        {
            AddCaveFauna(list, roll);
        }

        // #3448: a type with a fauna palette (Soaniri's light blues and mints) dresses its ROLLED species in it — after every
        // rolled pass, before the authored records join (they keep their designed colours), by each species' own seed (no RNG
        // draw), so a type without a palette rolls bit-identically.
        BlocksBeyondTheStars.Shared.World.FaunaTints.Dress(list, planet);

        AppendAuthored(list, roll, authored);

        // Generation 34 (#3325, Jafar's Schuppenhund): a dry world may host ONE hound — appended after every rolled and authored
        // slot from its own salted sub-seed, so no other species moves; older generations never roll it.
        if (!planet.IsAirless && terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.ScaleHoundGeneration
            && HostsScaleHound(planet, worldSeed))
        {
            list.Add(GenerateScaleHound(planet, worldSeed));
        }

        // Generation 49 (#3976): the Earth animals that live wild elsewhere (the dog, the cat) — each by the world's own salted
        // roll, appended after every other slot, so no species moves; older generations never roll them.
        StrayAnimals.AppendTo(list, planet, worldSeed, terrainGeneration, strays);

        // Generation 16 (#2082): every begging species loves one food — a post-pass, so no rolled trait moves.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BipedGeneration)
        {
            AssignFavouriteFoods(list, planet, worldSeed, terrainGeneration);
        }

        return list;
    }

    /// <summary>The golden-ratio stride that spreads the roster slots' sub-seeds.</summary>
    private const long RosterGolden = unchecked((long)0x9E3779B97F4A7C15UL);

    /// <summary>What every rolled slot of one roster shares: the planet, its roster seed, which habitats its world allows and
    /// the world's terrain generation.</summary>
    private sealed class RosterRoll
    {
        public RosterRoll(PlanetType planet, long worldSeed, int terrainGeneration)
        {
            Planet = planet;
            PlanetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key);
            AllowWater = HasWaterLife(planet);
            AllowLava = HasLavaLife(planet);
            AllowCave = planet.CaveThreshold > 0.0; // worlds with caves host subterranean fauna
            BiomeCount = System.Math.Max(1, planet.Biomes.Count);
            TerrainGeneration = terrainGeneration;
        }

        public PlanetType Planet { get; }

        public long PlanetSeed { get; }

        public bool AllowWater { get; }

        public bool AllowLava { get; }

        public bool AllowCave { get; }

        public int BiomeCount { get; }

        public int TerrainGeneration { get; }
    }

    /// <summary>The <paramref name="count"/> procedural slots and the passes over them, in their fixed order: diversity,
    /// the extreme types' rules, the gas giant's fliers, contamination.</summary>
    private static void RollProceduralRoster(List<CreatureSpecies> list, int count, RosterRoll roll)
    {
        var planet = roll.Planet;
        int terrainGeneration = roll.TerrainGeneration;
        for (int i = 0; i < count; i++)
        {
            long s = unchecked(roll.PlanetSeed ^ ((long)i * RosterGolden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(MakeSpecies(i, rng, roll.AllowWater, roll.AllowLava, roll.AllowCave, roll.BiomeCount, forcedHabitat: null, speciesSeed: s, terrainGeneration));
        }

        // Diversity guarantee (#640): every living world should field at least one ground species and
        // one flier (and an aquatic one where the world has water life). Only the slots ADDED by the
        // roster bump (index ≥ legacy count) may be re-drawn — each species draws its own sub-seed, so
        // the legacy indices keep their exact pre-bump rolls and existing worlds keep their known fauna.
        EnsureHabitatDiversity(list, LegacyAbundanceCount(planet.CreatureAbundance), roll.PlanetSeed,
            roll.AllowWater, roll.AllowLava, roll.AllowCave, roll.BiomeCount, terrainGeneration);

        // Generation 8 (2026-09): the extreme types' roster rules — Titas caps its water life, Valuma is peaceful.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.ExtremePlanetsGeneration)
        {
            CapAquaticSpecies(list, planet.MaxAquaticSpecies, roll.PlanetSeed, roll.AllowWater, roll.AllowLava, roll.AllowCave, roll.BiomeCount, terrainGeneration);
            if (planet.PeacefulFauna)
            {
                MakePeaceful(list);
            }
        }

        // Generation 18 (#2112): a gas giant has no ground but its islands — every rolled species flies. The type is
        // itself gated on generation 18, so no older world's roster can take this branch.
        if (planet.IsGasWorld && terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.GasGiantGeneration)
        {
            MakeEveryoneFly(list, roll);
        }

        // Generation 15 (#2069, Toxica-Maxima): a contaminated roster hunts on sight, never sleeps, wears the type's eyes
        // and drops toxic meat. A post-pass like MakePeaceful, so no rolled trait moves; only a generation-15 world reads it.
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.ToxicaMaximaGeneration && planet.ContaminatedFauna)
        {
            MakeContaminated(list, planet.EyeColor);
        }
    }

    /// <summary>Generation 18 (#2112): re-draws every rolled species that does not fly as a flier, from a sky-salted seed.</summary>
    private static void MakeEveryoneFly(List<CreatureSpecies> list, RosterRoll roll)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Habitat != CreatureHabitat.Air)
            {
                long s = unchecked(roll.PlanetSeed ^ ((long)i * RosterGolden) ^ WorldGenerator.StableHash("sky:" + i));
                var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
                list[i] = MakeSpecies(i, rng, allowWater: false, allowLava: false, allowCave: false, roll.BiomeCount, CreatureHabitat.Air, speciesSeed: s, roll.TerrainGeneration);
            }
        }
    }

    /// <summary>Generation 13 (#2029): a world with no procedural fauna (the toxic worlds) may still keep one or two CAVE
    /// species — life that fled underground. Own sub-seed, so no other roster ever moves.</summary>
    private static void AddCaveFauna(List<CreatureSpecies> list, RosterRoll roll)
    {
        long caveSeed = unchecked(roll.PlanetSeed ^ WorldGenerator.StableHash("cave-fauna"));
        int caveCount = 1 + (int)((ulong)caveSeed % 2UL);
        for (int i = 0; i < caveCount; i++)
        {
            long s = unchecked(caveSeed ^ ((long)i * RosterGolden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(MakeSpecies(i, rng, allowWater: false, allowLava: false, allowCave: true, roll.BiomeCount,
                forcedHabitat: CreatureHabitat.Cave, speciesSeed: s, roll.TerrainGeneration));
        }
    }

    /// <summary>Airless bodies (asteroids / airless moons+planets) stay lifeless whatever the data says; otherwise the
    /// authored species join at the tail on a generation-5 world. Their sub-seed is salted with the key, not
    /// the slot, so adding a second authored species to a type never renames the first.</summary>
    private static void AppendAuthored(List<CreatureSpecies> list, RosterRoll roll, IReadOnlyList<AuthoredCreature>? authored)
    {
        var planet = roll.Planet;
        if (planet.IsAirless || authored is not { Count: > 0 }
            || roll.TerrainGeneration < BlocksBeyondTheStars.Shared.World.WorldDescription.AuthoredContentGeneration)
        {
            return;
        }

        foreach (var record in authored)
        {
            // #2084: a record that relies on a later wave (Mini-Michi-Paul: the bananas of 14, the biped body of 16) waits
            // for it. Skipping it leaves every older world's roster exactly as it was — the record did not exist for it.
            if (record.MinGeneration > roll.TerrainGeneration)
            {
                continue;
            }

            if (IsGiantPlan(record.BodyPlan))
            {
                continue; // #2075: an authored giant lives in the giant slots (GenerateAuthoredGiant), never in the roster
            }

            long s = unchecked(roll.PlanetSeed ^ ((long)list.Count * RosterGolden) ^ WorldGenerator.StableHash("authored:" + record.Key));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(AuthoredSpeciesMapping.ToSpecies(record, planet, rng, s));
        }
    }

    /// <summary>The favourite foods (#2082, generation 16): every begging species that has none (an authored one may name its
    /// own) loves one of the clean fruit its world's trees bear (<see cref="FruitRules.CleanFruitItems"/>), or the berries when
    /// the world bears none. Folded from the species' voice seed — no RNG draw, so the roster's rolls stay as they are.</summary>
    private static void AssignFavouriteFoods(List<CreatureSpecies> list, PlanetType planet, long rosterSeed, int terrainGeneration)
    {
        IReadOnlyList<string>? foods = null;
        foreach (var sp in list)
        {
            if (!HerdRules.BegsForFood(sp) || !string.IsNullOrEmpty(sp.FavouriteFood))
            {
                continue;
            }

            foods ??= FruitRules.CleanFruitItems(planet, rosterSeed, terrainGeneration);
            sp.FavouriteFood = FavouriteFrom(foods, sp.VoiceSeed);
        }
    }

    /// <summary>One food of <paramref name="foods"/> picked by <paramref name="seed"/>, or the fallback berries.</summary>
    internal static string FavouriteFrom(IReadOnlyList<string> foods, int seed)
    {
        if (foods.Count == 0)
        {
            return HerdRules.FallbackFavouriteFood;
        }

        uint h = unchecked((uint)seed * 2654435761u);
        h ^= h >> 15;
        return foods[(int)(h % (uint)foods.Count)];
    }

    private static int AbundanceCount(string? abundance) => (abundance ?? "few").ToLowerInvariant() switch
    {
        "none" => 0,
        "authored" => 0, // #1763: only the type's authored species
        "many" => 9, // was 6 before the roster bump (#640)
        _ => 5,      // "few" / unknown — was 3 before the roster bump (#640)
    };

    /// <summary>The roster sizes before the #640 bump. Indices below these keep their exact legacy
    /// rolls (existing worlds keep their known species); only indices at/above are diversity-adjustable.</summary>
    private static int LegacyAbundanceCount(string? abundance) => (abundance ?? "few").ToLowerInvariant() switch
    {
        "none" => 0,
        "authored" => 0,
        "many" => 6,
        _ => 3,
    };

    /// <summary>Re-draws appended roster slots (index ≥ <paramref name="legacyCount"/>) into missing
    /// habitat niches, deterministically (#640). Priority: ground (Land) → flier (Air) → aquatic
    /// (Water/Amphibian, only on water worlds). Each fix regenerates one slot from a niche-salted seed
    /// with a forced habitat, preferring slots whose habitat is over-represented in the roster.</summary>
    private static void EnsureHabitatDiversity(List<CreatureSpecies> list, int legacyCount, long planetSeed,
        bool allowWater, bool allowLava, bool allowCave, int biomeCount, int terrainGeneration)
    {
        if (list.Count <= legacyCount)
        {
            return; // no adjustable slots
        }

        var needed = new List<CreatureHabitat>();
        if (!list.Exists(s => s.Habitat == CreatureHabitat.Land)) needed.Add(CreatureHabitat.Land);
        if (!list.Exists(s => s.Habitat == CreatureHabitat.Air)) needed.Add(CreatureHabitat.Air);
        if (allowWater && !list.Exists(s => s.Habitat is CreatureHabitat.Water or CreatureHabitat.Amphibian))
        {
            needed.Add(CreatureHabitat.Water);
        }

        var used = new HashSet<int>(); // adjustable slots already consumed by an earlier niche fix
        foreach (var niche in needed)
        {
            // Prefer the highest adjustable slot whose habitat appears more than once in the roster
            // (losing it can't empty another niche); fall back to any free adjustable slot.
            int pick = -1;
            for (int i = list.Count - 1; i >= legacyCount; i--)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                var habitat = list[i].Habitat;
                if (list.FindAll(s => s.Habitat == habitat).Count > 1)
                {
                    pick = i;
                    break;
                }

                if (pick < 0)
                {
                    pick = i; // remember the highest free slot as the fallback
                }
            }

            if (pick < 0)
            {
                return; // all adjustable slots used up — the lowest-priority niches stay unmet
            }

            used.Add(pick);
            const long golden = unchecked((long)0x9E3779B97F4A7C15UL);
            long s = unchecked(planetSeed ^ ((long)pick * golden) ^ WorldGenerator.StableHash("niche:" + niche));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list[pick] = MakeSpecies(pick, rng, allowWater, allowLava, allowCave, biomeCount, niche, speciesSeed: s, terrainGeneration);
        }
    }

    /// <summary>Generation 8 (Titas): keeps the first <paramref name="cap"/> water/amphibian species and re-draws every later
    /// one as a land species from a salted seed (-1 = no cap).</summary>
    private static void CapAquaticSpecies(List<CreatureSpecies> list, int cap, long planetSeed,
        bool allowWater, bool allowLava, bool allowCave, int biomeCount, int terrainGeneration)
    {
        if (cap < 0)
        {
            return;
        }

        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);
        int kept = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Habitat is not (CreatureHabitat.Water or CreatureHabitat.Amphibian))
            {
                continue;
            }

            if (kept < cap)
            {
                kept++;
                continue;
            }

            long s = unchecked(planetSeed ^ ((long)i * golden) ^ WorldGenerator.StableHash("cap:land"));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list[i] = MakeSpecies(i, rng, allowWater, allowLava, allowCave, biomeCount, CreatureHabitat.Land, speciesSeed: s, terrainGeneration);
        }
    }

    /// <summary>Generation 15 (#2069, Toxica-Maxima): every rolled species is contaminated — it hunts on sight (every fourth in
    /// a pack), bites even where its plan rolled no bite, is awake around the clock (a sleeping hunter is no hunter), wears the
    /// type's eye colour and drops toxic meat where it dropped food. Medusas, rays and titans included: their body-plan
    /// overrides ran before this pass.</summary>
    private static void MakeContaminated(List<CreatureSpecies> list, int eyeRgb)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var sp = list[i];
            sp.Temperament = i % 4 == 3 ? CreatureTemperament.PackHunter : CreatureTemperament.Aggressive;
            if (sp.AttackDamage <= 0f)
            {
                sp.AttackDamage = 2f + i % 6; // the rolled hunters' band (2..7), deterministic per slot
            }

            sp.Activity = CreatureActivity.Cathemeral;
            if (eyeRgb != 0)
            {
                sp.EyeRgb = eyeRgb;
            }

            if (sp.DropKind == CreatureDropKind.Food || sp.DropItem == "creature_meat")
            {
                sp.DropItem = "toxic_meat";
                sp.DropKind = CreatureDropKind.Poison;
            }
        }
    }

    /// <summary>Generation 8 (Valuma): every rolled species is passive or skittish and bites for nothing.</summary>
    private static void MakePeaceful(List<CreatureSpecies> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var sp = list[i];
            if (sp.Temperament is CreatureTemperament.Passive or CreatureTemperament.Skittish)
            {
                sp.AttackDamage = 0f;
                continue;
            }

            sp.Temperament = i % 2 == 0 ? CreatureTemperament.Passive : CreatureTemperament.Skittish;
            sp.AttackDamage = 0f;
        }
    }

    private static bool HasWaterLife(PlanetType planet)
    {
        // Any world that actually pools water — sea, ponds or rivers — hosts aquatic fauna. The threshold
        // matches WorldGenerator's pond gate (WaterAbundance > 0.15), so wherever the world grows water bodies
        // it also grows the life for them. The type/surface checks stay as a fallback for worlds that read as
        // watery (swamp/jungle/ice, mud/grass surfaces) even without an explicit abundance.
        string atmosphere = (planet.Atmosphere ?? string.Empty).ToLowerInvariant();
        double waterAbundance = planet.WaterAbundance ?? (atmosphere == "none" ? 0.0 : 0.55);
        if (waterAbundance > 0.15)
        {
            return true;
        }

        string key = planet.Key.ToLowerInvariant();
        string surface = (planet.SurfaceBlock ?? string.Empty).ToLowerInvariant();
        return key is "swamp" or "jungle" or "varied" or "ice"
               || surface is "mud" or "grass" or "ice";
    }

    private static bool HasLavaLife(PlanetType planet)
    {
        string key = planet.Key.ToLowerInvariant();
        return key == "lava" || (planet.SurfaceBlock ?? string.Empty).ToLowerInvariant() == "basalt";
    }
}
