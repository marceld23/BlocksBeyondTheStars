// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The G.D.S. logs (#3341): the notes the people of a buried G.D.S. city left in its crates (the ruin's <c>gds_cache</c>
/// markers) — who wrote it (a coined name), their work in the glowing city, how the city flooded with Ressaw and how
/// everyone got out, sealed it and moved up into the desert city. The same seed-written log as a derelict's
/// (<see cref="DerelictLogs.Compose"/>, the fragments of <c>data/gds_logs.json</c> under the <c>gds.log.*</c> keys): the
/// item is <c>gds_log#x&lt;seed&gt;</c>, a found log is <c>gdslog:&lt;seed&gt;</c> in the Discoveries ledger
/// (<see cref="LogSets.Gds"/>).
/// <para>Seed layout as the derelict's: bits 16–31 the city's story (one per world body — which hall cracked first, how
/// they got out, the cycle), bits 4–15 the writer (from the crate and the log's index there) and bits 0–3 the index,
/// which hands the writers different jobs.</para>
/// </summary>
public static class GdsLogs
{
    /// <summary>The base key of the readable G.D.S. log item (data/items.json).</summary>
    public const string ItemKeyBase = "gds_log";

    /// <summary>Discoveries-ledger prefix of a found G.D.S. log: <c>gdslog:&lt;8 hex digits&gt;</c>.</summary>
    public const string LedgerPrefix = "gdslog:";

    /// <summary>The seed of a G.D.S. log: the story is the city's (<paramref name="cityId"/>, the same for every log of
    /// one buried city), the writer is <paramref name="writerId"/>'s (a crate) at <paramref name="index"/> (only its low
    /// four bits ride in the seed, so they wrap at 16). Never 0.</summary>
    public static uint SeedFor(long worldSeed, string cityId, string writerId, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "a log index is never negative");
        }

        uint story = DerelictLogs.Fold(WorldGenerator.StableHash("gds-log-story:" + cityId) ^ worldSeed);
        uint writer = DerelictLogs.Fold(WorldGenerator.StableHash("gds-log:" + writerId + ":" + index.ToString(CultureInfo.InvariantCulture))
                                        ^ (worldSeed * 31));
        return DerelictLogs.Pack(story, writer, index & 0xF);
    }

    /// <summary>The item key of the G.D.S. log with this seed.</summary>
    public static string ItemFor(uint seed) => LogSets.Gds.ItemFor(seed);
}
