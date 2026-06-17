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
        bool allowCave = planet.CaveThreshold > 0.0; // worlds with caves host subterranean fauna
        int biomeCount = System.Math.Max(1, planet.Biomes.Count);

        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);
        for (int i = 0; i < count; i++)
        {
            long s = unchecked(planetSeed ^ ((long)i * golden));
            var rng = new System.Random(unchecked((int)(s ^ (s >> 32))));
            list.Add(MakeSpecies(i, rng, allowWater, allowLava, allowCave, biomeCount));
        }

        return list;
    }

    private static int AbundanceCount(string? abundance) => (abundance ?? "few").ToLowerInvariant() switch
    {
        "none" => 0,
        "many" => 6,
        _ => 3, // "few" / unknown
    };

    private static CreatureSpecies MakeSpecies(int index, System.Random rng, bool allowWater, bool allowLava, bool allowCave, int biomeCount)
    {
        var habitat = PickHabitat(rng, allowWater, allowLava, allowCave);
        bool cave = habitat == CreatureHabitat.Cave;
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

        var species = new CreatureSpecies
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
            HasWings = habitat == CreatureHabitat.Air
                       || (habitat != CreatureHabitat.Cave && habitat != CreatureHabitat.Amphibian && rng.NextDouble() < 0.1),
            HasTail = rng.NextDouble() < (habitat == CreatureHabitat.Amphibian ? 0.85 : 0.5),
            BodySegments = 1 + rng.Next(4),                            // 1..4 — some long, segmented bodies
            ColorRgb = colorRgb,
            BellyRgb = bellyRgb, // a second tone → two-tone bodies
            // Cave dwellers are mostly eyeless (they navigate in the dark); surface fauna keep the normal mix.
            Eyes = cave ? Weighted(rng, 0, 55, 1, 15, 2, 25, 4, 5)
                        : Weighted(rng, 0, 12, 1, 6, 2, 44, 3, 16, 4, 14, 6, 6, 8, 2),
            Horns = Weighted(rng, 0, 50, 1, 18, 2, 15, 3, 12, 4, 5),   // none / one / two / three / four
            HasCrest = rng.NextDouble() < 0.32,                        // a dorsal frill on roughly a third
            // item-21 morphology rest: tentacles for the wet/dark fauna, snail-like eyestalks, and a
            // translucent buoyancy gas-sac on the odd floating grazer — beyond just legs/eyes.
            Tentacles = PickTentacles(rng, habitat),
            EyeStalks = rng.NextDouble() < habitat switch
            {
                CreatureHabitat.Amphibian => 0.45,
                CreatureHabitat.Cave => 0.35,
                CreatureHabitat.Water => 0.25,
                _ => 0.10,
            },
            HasGasSac = habitat == CreatureHabitat.Air
                ? rng.NextDouble() < 0.35
                : habitat == CreatureHabitat.Land && rng.NextDouble() < 0.06,
            // Lava + cave dwellers are bioluminescent (the only light down in the dark); others sometimes glow.
            Glows = habitat == CreatureHabitat.Lava || cave || rng.NextDouble() < (activity == CreatureActivity.Nocturnal ? 0.3 : 0.12),
            BiomeAffinity = biomeAffinity, // native to one biome on multi-biome worlds

            DropItem = dropItem,
            DropCount = dropCount,
            DropKind = dropKind,
        };

        // Movement signature (item: natural locomotion): a randomly-chosen gait biased by the body + habitat +
        // temperament we just generated, so fauna move in recognisably different ways. Drawn LAST so the species'
        // appearance rolls (hence existing worlds' rosters) are unchanged by adding it.
        species.LocoStyle = PickLocoStyle(rng, species);
        return species;
    }

    /// <summary>Picks a creature's <see cref="LocomotionStyle"/> from its already-generated traits: limbless +
    /// segmented bodies slither, two-leggers hop, gas-sacs drift, fliers glide, water fauna school, and the
    /// temperament colours the ground gait (predators prowl, skittish ones dart, grazers feed in stop-and-go).</summary>
    private static LocomotionStyle PickLocoStyle(System.Random rng, CreatureSpecies sp)
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
            if (sp.BodySegments >= 3) Add(LocomotionStyle.Slitherer, 25);
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
