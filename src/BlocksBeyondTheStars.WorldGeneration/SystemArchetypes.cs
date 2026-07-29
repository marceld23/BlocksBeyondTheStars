// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>The character class a star system rolls when <see cref="WorldDescription.SystemVariance"/>
/// is on (#546). The archetype shapes the system's structural rolls in <see cref="UniverseGenerator"/>
/// (planet/moon/asteroid/station/wreck counts, sizes) AND its inhabitants at runtime (trader traffic,
/// pirate flag, camp odds) — so a system reads as one coherent place instead of eight unrelated dice.</summary>
public enum SystemArchetype
{
    /// <summary>Today's distribution — the baseline every pre-variance world uses for every system.</summary>
    Standard,
    /// <summary>One oversized planet with a large moon family (4–8) — the "gas giant" fantasy.</summary>
    LoneGiant,
    /// <summary>Many small planets, almost no moons.</summary>
    Swarm,
    /// <summary>Few planets, a dense field of landable asteroids; wrecks likelier.</summary>
    Belt,
    /// <summary>Civilised space: stations guaranteed, heavy trade, pirates almost absent.</summary>
    Hub,
    /// <summary>Near-empty space: one or two lonely worlds, no stations, no traders.</summary>
    Desolate,
    /// <summary>Lawless space: always pirate territory, more camps, no stations, wrecks everywhere.</summary>
    PirateHaven,
    /// <summary>Exactly two planets of similar size on close orbits.</summary>
    TwinWorlds,
}

/// <summary>Deterministically resolves a system's <see cref="SystemArchetype"/> from the world seed —
/// the SAME way for the generator and every runtime consumer (traders/bandits/camps), so no persistence
/// is needed (the trader-traffic pattern). Uses its own <see cref="Noise.Hash"/> salt (500-series),
/// never the generator's order-sensitive per-system rng stream.</summary>
public static class SystemArchetypes
{
    /// <summary>Hash01 salt for the archetype draw. 5xx is unused by UniverseGenerator's other draws
    /// (planets 1xx, moons 2xx, wreck/asteroids 3xx, asteroid families 4xx, planetary rings 6xx);
    /// 501/502 are reserved for the archetype's size-bias and twin-orbit draws.</summary>
    private const long ArchetypeSalt = 500;

    private static readonly (SystemArchetype Archetype, int Weight)[] Table =
    {
        (SystemArchetype.Standard, 30),
        (SystemArchetype.LoneGiant, 12),
        (SystemArchetype.Swarm, 12),
        (SystemArchetype.Belt, 10),
        (SystemArchetype.Hub, 10),
        (SystemArchetype.Desolate, 12),
        (SystemArchetype.PirateHaven, 8),
        (SystemArchetype.TwinWorlds, 6),
    };

    /// <summary>Archetype for a system id ("sys{i}"). Standard when variance is off, for reserved ids
    /// (the guardian finale), and for anything unparseable — so pre-variance saves and special systems
    /// behave exactly as before.</summary>
    public static SystemArchetype For(long seed, string? systemId, WorldDescription desc)
    {
        if (!desc.SystemVariance
            || string.IsNullOrEmpty(systemId)
            || !systemId.StartsWith("sys", System.StringComparison.Ordinal)
            || !int.TryParse(systemId.Substring(3), out int index)
            || index < 0)
        {
            return SystemArchetype.Standard;
        }

        return ForIndex(seed, index);
    }

    /// <summary>Archetype for a system INDEX (the generator's loop variable). The home system (index 0)
    /// never rolls Desolate or Pirate Haven, so a fresh start always has a friendly, non-empty sky.</summary>
    public static SystemArchetype ForIndex(long seed, int systemIndex)
    {
        bool home = systemIndex == 0;
        int total = 0;
        foreach (var (archetype, weight) in Table)
        {
            if (home && ExcludedForHome(archetype))
            {
                continue;
            }

            total += weight;
        }

        double h01 = (Noise.Hash(seed, systemIndex, ArchetypeSalt, 1) >> 11) * (1.0 / 9007199254740992.0);
        int roll = 1 + (int)(h01 * total);
        foreach (var (archetype, weight) in Table)
        {
            if (home && ExcludedForHome(archetype))
            {
                continue;
            }

            roll -= weight;
            if (roll <= 0)
            {
                return archetype;
            }
        }

        return SystemArchetype.Standard;
    }

    private static bool ExcludedForHome(SystemArchetype a)
        => a is SystemArchetype.Desolate or SystemArchetype.PirateHaven;
}
