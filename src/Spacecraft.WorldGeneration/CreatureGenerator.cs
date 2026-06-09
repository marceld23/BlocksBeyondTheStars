using System.Collections.Generic;
using Spacecraft.Shared.Definitions;

namespace Spacecraft.WorldGeneration;

/// <summary>
/// Deterministically derives a planet's roster of <see cref="CreatureSpecies"/> from the world
/// seed + planet (technical requirements / `anf_space_flight.md` §12). Same seed + planet → the
/// same species every run, so the roster never needs storing. How many species a world has comes
/// from its <see cref="PlanetType.CreatureAbundance"/> (none / few / many); habitats are biased by
/// the planet (lava worlds get lava-dwellers, wet worlds get aquatic life). The roster skews
/// **non-hostile** so a world is never all-hostile.
/// </summary>
public static class CreatureGenerator
{
    public static IReadOnlyList<CreatureSpecies> GenerateRoster(PlanetType planet, long worldSeed)
    {
        int count = planet.IsAirless ? 0 : AbundanceCount(planet.CreatureAbundance);
        var list = new List<CreatureSpecies>(count);
        if (count == 0)
        {
            return list; // airless bodies (asteroids / airless moons+planets) + "none" worlds are lifeless
        }

        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key);
        bool allowWater = HasWaterLife(planet);
        bool allowLava = HasLavaLife(planet);
        int biomeCount = System.Math.Max(1, planet.Biomes.Count);

        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);
        for (int i = 0; i < count; i++)
        {
            long s = unchecked(planetSeed ^ ((long)i * golden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(MakeSpecies(i, rng, allowWater, allowLava, biomeCount));
        }

        return list;
    }

    private static int AbundanceCount(string? abundance) => (abundance ?? "few").ToLowerInvariant() switch
    {
        "none" => 0,
        "many" => 6,
        _ => 3, // "few" / unknown
    };

    private static CreatureSpecies MakeSpecies(int index, System.Random rng, bool allowWater, bool allowLava, int biomeCount)
    {
        var habitat = PickHabitat(rng, allowWater, allowLava);
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

            Legs = PickLegs(rng, habitat),
            HasWings = habitat == CreatureHabitat.Air || rng.NextDouble() < 0.1,
            HasTail = rng.NextDouble() < 0.5,
            BodySegments = 1 + rng.Next(4),                            // 1..4 — some long, segmented bodies
            ColorRgb = PickColor(rng, habitat),
            BellyRgb = PickColor(rng, habitat), // a second tone → two-tone bodies
            Eyes = Weighted(rng, 0, 12, 1, 6, 2, 44, 3, 16, 4, 14, 6, 6, 8, 2), // eyeless / one / two / three / four / six / eight
            Horns = Weighted(rng, 0, 50, 1, 18, 2, 15, 3, 12, 4, 5),   // none / one / two / three / four
            HasCrest = rng.NextDouble() < 0.32,                        // a dorsal frill on roughly a third
            Glows = habitat == CreatureHabitat.Lava || rng.NextDouble() < (activity == CreatureActivity.Nocturnal ? 0.3 : 0.12),
            BiomeAffinity = biomeCount <= 1 ? -1 : rng.Next(biomeCount), // native to one biome on multi-biome worlds

            DropItem = dropItem,
            DropCount = dropCount,
            DropKind = dropKind,
        };
    }

    private static CreatureHabitat PickHabitat(System.Random rng, bool allowWater, bool allowLava)
    {
        // Land/air always possible; water/lava only on suitable worlds.
        var weights = new List<(int Value, int Weight)>
        {
            ((int)CreatureHabitat.Land, 50),
            ((int)CreatureHabitat.Air, 20),
        };
        if (allowWater) weights.Add(((int)CreatureHabitat.Water, 20));
        if (allowLava) weights.Add(((int)CreatureHabitat.Lava, 10));
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

    private static int PickLegs(System.Random rng, CreatureHabitat habitat) => habitat switch
    {
        CreatureHabitat.Water => 0,
        CreatureHabitat.Air => 2,
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
            case CreatureHabitat.Lava: r = 210; g = 90; b = 40; break;
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

    private static bool HasWaterLife(PlanetType planet)
    {
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
