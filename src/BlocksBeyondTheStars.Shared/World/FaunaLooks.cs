// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.World;

/// <summary>
/// #2868: the per-world part of a creature's hide look. The client dresses an animal by a planet THEME (read from the
/// planet type's data) and a random MOTIF per world, derived from <see cref="WorldLookSeed"/>. The server sends both
/// values with every creature (<c>NetCreature.LookPlanet</c> / <c>LookSeed</c>) — the world the look belongs to, which
/// for a companion, a clone or a cross is its home world, not the world it is shown on.
/// </summary>
public static class FaunaLooks
{
    /// <summary>The seed the client derives a world's random fauna motif from (#2868): a pure function of the world seed
    /// and the location key the flora colours use ("system · body"), never 0 — 0 on the wire and in a species means "no
    /// look stamped". Built like <see cref="FloraLooks.IndexFor"/> (FNV-1a through the SplitMix64 finaliser). Bit-frozen
    /// once shipped: companion snapshots and the species register carry it, so a change re-dresses every pet. The seed
    /// is written with the invariant culture, so a server in any locale gives a world the same motif.</summary>
    public static int WorldLookSeed(long worldSeed, string locationKey)
    {
        string key = worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + (locationKey ?? string.Empty) + "|fauna-look";
        ulong h = StableHashing.Mix64(FloraTints.Hash(key));
        int seed = unchecked((int)(h ^ (h >> 32)));
        return seed == 0 ? 1 : seed;
    }
}
