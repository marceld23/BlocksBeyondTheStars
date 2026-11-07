// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The crates of a buried G.D.S. ruin (#3333–#3343; Justus: "In some crates you also find armour pieces of Ressaw diving
/// suits" … "entries from old crates about a civilisation called GDS"). The ruin stamper emits one structure marker
/// <c>gds_cache:&lt;n&gt;</c> per crate (n = 0, 1, 2, … in a fixed order, at least four per ruin); the ordinary structure-loot
/// path (<see cref="SpawnStructureLoot"/>) turns each into a one-time <c>salvage</c> container — once ever, through
/// <see cref="WorldMetadata.GeneratedLoot"/> — and asks <see cref="BuildGdsCacheLoot"/> what it holds:
/// <list type="bullet">
/// <item>crate n &lt; 4 always holds Ressaw diving-suit piece n (0 helmet, 1 suit, 2 leggings, 3 boots), so every ruin holds
/// the whole set (#3337);</item>
/// <item>1–3 Ressaw crystals;</item>
/// <item>one G.D.S. log, sometimes two (#3341, <see cref="GdsLogs"/>: one story per world body, a different writer per
/// log) — taken out, it lands in the Discoveries ledger like a derelict's log;</item>
/// <item>now and then a data fragment, and a pick of old salvage (cable, copper wire, a circuit board, iron plate).</item>
/// </list>
/// The roll is seed-pure from the world seed, the body, the crate's cell and its number — it never draws from the stamp's
/// shared rng, so adding the crates moves no other roll. Cost: a handful of list adds once per crate ever; no tick work.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The marker kind of a G.D.S. ruin crate: the marker id is <c>gds_cache:&lt;n&gt;</c>.</summary>
    internal const string GdsCacheMarker = "gds_cache";

    /// <summary>The Ressaw diving suit, piece by piece in crate order (#3337): crate n &lt; 4 holds piece n.</summary>
    internal static readonly string[] GdsCacheSuitPieces = { "ressaw_helmet", "ressaw_suit", "ressaw_leggings", "ressaw_boots" };

    /// <summary>The old salvage a G.D.S. crate may hold besides its crystals and logs.</summary>
    private static readonly string[] GdsCacheSalvage = { "cable", "copper_wire", "circuit_board", "iron_plate" };

    /// <summary>The crate number of a G.D.S. ruin marker (<c>gds_cache:&lt;n&gt;</c>), false for every other marker.</summary>
    internal static bool TryParseGdsCache(string markerType, out int cache)
    {
        cache = -1;
        const string prefix = GdsCacheMarker + ":";
        return markerType.StartsWith(prefix, System.StringComparison.Ordinal)
            && int.TryParse(markerType.Substring(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out cache)
            && cache is >= 0 and < 10_000;
    }

    /// <summary>What G.D.S. crate <paramref name="cache"/> at <paramref name="cell"/> holds — the same world, body, cell and
    /// number always give the same crate.</summary>
    private List<ItemStack> BuildGdsCacheLoot(int cache, Vector3i cell)
    {
        string crateId = cell.X.ToString(CultureInfo.InvariantCulture) + ":" + cell.Y.ToString(CultureInfo.InvariantCulture)
                         + ":" + cell.Z.ToString(CultureInfo.InvariantCulture);
        var rng = new System.Random(StableHashing.Seed32(
            "gds-cache:" + _world.LocationId + ":" + crateId + ":" + cache.ToString(CultureInfo.InvariantCulture), _meta.Seed));
        var items = new List<ItemStack>();

        void Add(string item, int count) // every pick below is a different item, so nothing needs merging
        {
            if (count > 0 && _content.GetItem(item) is not null)
            {
                items.Add(new ItemStack(item, count));
            }
        }

        if (cache < GdsCacheSuitPieces.Length)
        {
            Add(GdsCacheSuitPieces[cache], 1);
        }

        Add("ressaw_crystal", 1 + rng.Next(3));

        // The logs: one, sometimes two — crate n carries log indices 2n and 2n+1, so the writers of a ruin get different
        // jobs (the index wraps at 16 in the seed). One story per world body: every crate of its buried city tells it.
        int logs = rng.NextDouble() < 0.4 ? 2 : 1;
        if (!_content.GdsLogs.IsEmpty)
        {
            for (int k = 0; k < logs; k++)
            {
                Add(GdsLogs.ItemFor(GdsLogs.SeedFor(_meta.Seed, _world.LocationId, crateId, cache * 2 + k)), 1);
            }
        }

        if (rng.NextDouble() < 0.5)
        {
            Add("data_fragment", 1 + rng.Next(2));
        }

        Add(GdsCacheSalvage[rng.Next(GdsCacheSalvage.Length)], 1 + rng.Next(3));
        return items;
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam: what G.D.S. crate <paramref name="cache"/> at <paramref name="cell"/> holds (or would hold).</summary>
    public IReadOnlyList<ItemStack> GdsCacheLootForTest(int cache, Vector3i cell) => BuildGdsCacheLoot(cache, cell);

    /// <summary>Test seam: runs the structure-loot spawn of a G.D.S. crate marker on the active world (once ever, like a
    /// stamp does) and returns the container it left in the marker's cell, or null.</summary>
    public StoredContainer? SpawnGdsCacheForTest(string structureKind, string markerType, Vector3f pos)
    {
        SpawnStructureLoot(structureKind, markerType, pos, new System.Random(0));
        var cell = pos.ToBlock();
        return _containers.Find(c => c.Position.Equals(cell) && c.Id.StartsWith("loot_", System.StringComparison.Ordinal));
    }
}
