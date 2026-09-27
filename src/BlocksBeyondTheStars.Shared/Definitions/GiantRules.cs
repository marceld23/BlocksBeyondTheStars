// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>What shook the ground (#2001): every source a sandworm can hear.</summary>
public enum VibrationSource : byte
{
    Step,         // a player walking (not sneaking) across the sand
    Mining,       // a block broken by hand or tool
    AreaDrill,    // a drill's area break
    Blaster,      // the terrain blaster's blast
    HardLanding,  // a fall that hurt
    Speeder,      // a speeder driving over the sand
    SpeederCrash, // a speeder hitting something
    Thumper,      // the thumper's pulse (#2002) — built to be heard
    Horn,         // a Crystal-Net chime or horn sounding on the sand (#2077)
    Siren,        // a Crystal-Net alarm siren wailing on the sand (#2077) — louder, and it keeps pulsing while it is on
}

/// <summary>
/// The rules of the giants (#1998–#2002), kept Unity-free and pure so the server, the client and the tests agree:
/// which worlds host a colossus or sandworms, and what a sandworm hears. Decisions (Marcel, 2026-09-24): giants are
/// 60 blocks tall, defeatable but very tough; a colossus lives only on very flat worlds with very low gravity and is
/// very rare; sandworms live only on sand-sea worlds.
/// </summary>
public static class GiantRules
{
    // ---------------- World gates ----------------

    /// <summary>A world this light or lighter may host a colossus. The per-world gravity band is 0.80–1.60 on planets,
    /// 0.55–0.85 on moons and 0.35–0.55 on asteroids — so in practice: the lower half of the moons.</summary>
    public const float ColossusMaxGravity = 0.70f;

    /// <summary>The steepest a "very flat" type may be (its relief amplitude, blocks).</summary>
    public const int VeryFlatAmplitude = 10;

    /// <summary>One qualifying world in this many rolls a colossus.</summary>
    public const int ColossusRarity = 3;

    /// <summary>The terrain styles a very flat type may lay out — plains, rolling downs and dune seas.</summary>
    private static readonly string[] FlatStyles = { "flats", "downs", "dunes" };

    /// <summary>A type whose whole relief is plains, downs or dunes and whose amplitude is small. A type without any
    /// style (the archetype blend: hills and mountains) is never very flat.</summary>
    public static bool IsVeryFlat(PlanetType planet)
    {
        if (planet is null || planet.Void || planet.Amplitude > VeryFlatAmplitude)
        {
            return false;
        }

        int styles = 0;
        foreach (string s in planet.TerrainStyles)
        {
            if (!IsFlatStyle(s))
            {
                return false;
            }

            styles++;
        }

        if (!string.IsNullOrEmpty(planet.TerrainStyle))
        {
            if (!IsFlatStyle(planet.TerrainStyle))
            {
                return false;
            }

            styles++;
        }

        return styles > 0;
    }

    private static bool IsFlatStyle(string style)
    {
        foreach (string f in FlatStyles)
        {
            if (string.Equals(f, style, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Fauna a giant may join: not a lifeless type and not one reserved for its authored species.</summary>
    public static bool FaunaAllowsGiants(PlanetType planet)
        => planet is not null && !planet.Void
           && !string.Equals(planet.CreatureAbundance, "none", System.StringComparison.OrdinalIgnoreCase)
           && !string.Equals(planet.CreatureAbundance, "authored", System.StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this world hosts a colossus: generation 9+, very flat, very light, living — and the seeded
    /// one-in-<see cref="ColossusRarity"/> roll of this very world.</summary>
    public static bool HostsColossus(PlanetType planet, float gravityFactor, int terrainGeneration, long seed, string locationId)
        => terrainGeneration >= WorldDescription.GiantsGeneration
           && IsVeryFlat(planet)
           && gravityFactor <= ColossusMaxGravity
           && FaunaAllowsGiants(planet)
           && RarityRoll(seed, locationId);

    /// <summary>The seeded rarity roll alone (tests; the hosting rule combines it with the gates).</summary>
    public static bool RarityRoll(long seed, string locationId)
    {
        uint h = unchecked((uint)(StableHash("colossus:" + (locationId ?? string.Empty)) ^ (int)seed ^ (int)(seed >> 32)));
        h ^= h >> 16;
        h = unchecked(h * 0x7FEB352Du);
        h ^= h >> 15;
        return h % (uint)ColossusRarity == 0;
    }

    /// <summary>Whether this world hosts sandworms: generation 9+ on a sand-sea type (and nowhere else).</summary>
    public static bool HostsSandworms(PlanetType planet, int terrainGeneration)
        => planet is not null && !planet.Void && terrainGeneration >= WorldDescription.GiantsGeneration
           && planet.SandSeaShare > 0.0;

    /// <summary>How many sandworms a sand-sea world carries: one, two on a big world.</summary>
    public static int SandwormCount(int circumference) => circumference >= 6000 ? 2 : 1;

    /// <summary>How many sandworms THIS type's world carries (#2075): the type's own <see cref="PlanetType.SandwormCount"/>
    /// on a generation-17 world, else the classic rule by circumference.</summary>
    public static int SandwormCount(PlanetType planet, int circumference, int terrainGeneration)
        => planet is { SandwormCount: > 0 } && terrainGeneration >= WorldDescription.ArenaNigraGeneration
            ? planet.SandwormCount
            : SandwormCount(circumference);

    /// <summary>The block a sand sea is made of (#2074): the surface block of the biome flagged <see cref="PlanetType.Biome.SandSea"/>,
    /// or <c>sand</c> where no biome says. A vibration carries only through THIS block, and the worm's dust wears its colour.</summary>
    public static string SeaSandBlock(PlanetType? planet)
    {
        if (planet is not null)
        {
            foreach (var biome in planet.Biomes)
            {
                if (biome.SandSea && !string.IsNullOrEmpty(biome.SurfaceBlock))
                {
                    return biome.SurfaceBlock;
                }
            }
        }

        return "sand";
    }

    /// <summary>Whether a ROLLED sandworm of this world hunts the animals (#2076): generation 17 and later. An authored worm
    /// says so itself (<see cref="AuthoredCreature.SwallowsCreatures"/>); older sand-sea worlds keep their worms' manners.</summary>
    public static bool RolledWormsHunt(int terrainGeneration) => terrainGeneration >= WorldDescription.ArenaNigraGeneration;

    /// <summary>The meat a swallowed animal leaves on the sand (#2076): one or two pieces, by the animal's own hash.</summary>
    public static int SwallowMeat(string creatureId) => 1 + (int)((uint)StableHash("swallow:" + (creatureId ?? string.Empty)) % 2u);

    // ---------------- Vibration (#2001) ----------------

    /// <summary>A player on foot shakes the sand only above this speed: crouching (2.4) sneaks, walking (6) is heard.</summary>
    public const float SneakSpeed = 3.2f;

    /// <summary>A walking player sends one step pulse per this many blocks covered.</summary>
    public const float StepSpacing = 2.5f;

    /// <summary>How far through the sand a pulse of this kind carries (blocks) to a worm of normal hearing.</summary>
    public static float Reach(VibrationSource source) => source switch
    {
        VibrationSource.Step => 45f,
        VibrationSource.Mining => 70f,
        VibrationSource.AreaDrill => 90f,
        VibrationSource.Blaster => 140f,
        VibrationSource.HardLanding => 70f,
        VibrationSource.Speeder => 80f,
        VibrationSource.SpeederCrash => 120f,
        VibrationSource.Horn => 160f,   // #2077: a chime or a horn on the sand
        VibrationSource.Siren => 220f,  // #2077: the alarm siren — between the speeder crash and the thumper
        _ => 260f, // Thumper
    };

    /// <summary>How much one heard pulse adds to a worm's attention.</summary>
    public static float Weight(VibrationSource source) => source switch
    {
        VibrationSource.Step => 0.55f,
        VibrationSource.Mining => 0.7f,
        VibrationSource.AreaDrill => 1.2f,
        VibrationSource.Blaster => 3f,
        VibrationSource.HardLanding => 1.2f,
        VibrationSource.Speeder => 0.9f,
        VibrationSource.SpeederCrash => 2.5f,
        VibrationSource.Horn => 1.2f,
        VibrationSource.Siren => 1.5f,
        _ => 1.6f, // Thumper
    };

    /// <summary>Attention a worm needs before it comes for the source.</summary>
    public const float AttentionThreshold = 2.5f;

    /// <summary>Attention fades by this much per second of quiet.</summary>
    public const float AttentionDecayPerSecond = 0.35f;

    /// <summary>Whether a worm of <paramref name="hearing"/> (1 = normal, rolled 0.8–1.3) hears this pulse at that distance.</summary>
    public static bool Hears(VibrationSource source, float hearing, float distance)
        => distance <= Reach(source) * System.Math.Max(0.1f, hearing);

    /// <summary>Whether a player on foot moving at <paramref name="speed"/> blocks/s shakes the sand (sneaking does not).</summary>
    public static bool WalkShakes(float speed) => speed > SneakSpeed;

    /// <summary>A wild creature shakes the sand above this speed (#2076): a grazing herd ambles at 2–3 blocks/s, well under a
    /// player's sneak, and must still be heard — only a creature standing still is quiet.</summary>
    public const float CreatureStepSpeed = 0.8f;

    /// <summary>Whether a creature moving at <paramref name="speed"/> blocks/s shakes the sand (#2076).</summary>
    public static bool CreatureShakes(float speed) => speed > CreatureStepSpeed;

    /// <summary>A worm's attention after <paramref name="dt"/> seconds of quiet.</summary>
    public static float DecayAttention(float attention, float dt)
        => System.Math.Max(0f, attention - AttentionDecayPerSecond * System.Math.Max(0f, dt));

    // ---------------- Shared helpers ----------------

    /// <summary>FNV-1a over the string — stable across runtimes (string.GetHashCode is randomized per process).</summary>
    public static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in s ?? string.Empty)
            {
                h ^= c;
                h *= 16777619u;
            }

            return (int)h;
        }
    }
}
