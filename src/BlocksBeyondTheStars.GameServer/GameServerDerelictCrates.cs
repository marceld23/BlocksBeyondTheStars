// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The old supply crates of a derelict station (#2633). Screelit: "old crates with notes from people and records of
/// disasters that are always random — and rotten food and medkits." On the first boarding of a generated derelict
/// (anyone's), every crate spot of its layout (<see cref="DerelictCrateSpotsWorld"/>) gets a <c>crate</c> block — written
/// into the build like a player edit, so the hull and the persisted row keep it — and a one-time salvage container in
/// that cell: medpacks, now and then a field medkit, emergency rations, spoiled food, scrap and one or two
/// <b>log entries</b> (<see cref="DerelictLogs"/>). Each crate fills exactly once ever (the
/// <see cref="WorldMetadata.GeneratedLoot"/> ledger, keyed by station and crate), its contents are rolled seed-pure from
/// the world seed, the station and the crate, and the container persists like every structure loot until it is emptied.
/// <para>A log the player takes out of any container lands in their Discoveries ledger (<c>log:&lt;seed&gt;</c>, named
/// after the writer, sited at the derelict) — the Codex lists it and re-reads the text from the seed, so it stays
/// readable after the item is dropped or traded away.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <see cref="WorldMetadata.GeneratedLoot"/> key prefix of a derelict crate: <c>derelict-crate:&lt;station&gt;:&lt;n&gt;</c>.</summary>
    private const string DerelictCrateLootPrefix = "derelict-crate:";

    /// <summary>Container id prefix of a derelict crate — deliberately NOT <c>loot_derelict_…</c>, which
    /// <see cref="LoreSiteOfContainer"/> reads as the story's wreck-manifest lore site.</summary>
    internal const string DerelictCrateContainerPrefix = "loot_oldcrate_";

    /// <summary>The block a derelict crate stands as.</summary>
    private const string DerelictCrateBlockKey = "crate";

    /// <summary>Fills the crate spots of a generated derelict whose interior the ACTIVE world is — once per crate, ever.
    /// Called from <see cref="OnDerelictBoarded"/> right after the stamp. A spot someone built into first stays without a
    /// crate (it is marked done all the same, so it never pops up later).</summary>
    private void SpawnDerelictCrates(BoardableStation station)
    {
        if (!IsGeneratedDerelictId(station.Id) || _world.LocationId != StationLocationIdPrefix + station.Id)
        {
            return;
        }

        var spots = DerelictCrateSpotsWorld(station.Id);
        if (spots.Count == 0)
        {
            return;
        }

        var crateBlock = _content.GetBlock(DerelictCrateBlockKey);
        string? changedStation = null;
        _repo.RunInTransaction(() =>
        {
            for (int i = 0; i < spots.Count; i++)
            {
                string ledger = DerelictCrateLootPrefix + station.Id + ":" + i;
                if (_meta.GeneratedLoot.Contains(ledger))
                {
                    continue; // filled once already (it may since have been looted away)
                }

                _meta.GeneratedLoot.Add(ledger);
                var cell = spots[i];
                if (!_world.GetBlock(cell).IsAir)
                {
                    continue;
                }

                if (crateBlock is not null)
                {
                    _world.SetBlock(cell, crateBlock.NumericId);
                    changedStation = MirrorStationCell(cell, crateBlock.NumericId) ?? changedStation; // the build keeps it
                    var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
                    BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = crateBlock.NumericId.Value });
                }

                var items = DerelictCrateLoot(station.Id, i);
                string id = DerelictCrateContainerPrefix + station.Id.Replace(':', '_') + "_" + i;
                if (items.Count == 0 || _containers.Exists(c => c.Id == id))
                {
                    continue;
                }

                AddContainer(new StoredContainer
                {
                    Id = id,
                    Planet = _world.LocationId,
                    Kind = "salvage",
                    Position = cell,
                    Items = items,
                });
            }
        });

        if (changedStation is not null)
        {
            PublishStationCells(changedStation); // the row (and every pilot outside) keeps the crates
        }
    }

    /// <summary>The contents of crate <paramref name="crate"/> of a derelict — seed-pure (world seed, station, crate).</summary>
    private List<ItemStack> DerelictCrateLoot(string stationId, int crate)
    {
        long h = WorldGenerator.StableHash(DerelictCrateLootPrefix + stationId + ":" + crate) ^ _meta.Seed;
        return BuildDerelictCrateLoot(stationId, crate, new System.Random(unchecked((int)(h ^ (h >> 32)))));
    }

    /// <summary>What crate <paramref name="crate"/> of a derelict holds: medical supplies (the first crate always has a
    /// medpack), now and then a field medkit, rations and spoiled food, a little scrap — and one or two log entries.
    /// The same rng in, the same crate out.</summary>
    private List<ItemStack> BuildDerelictCrateLoot(string stationId, int crate, System.Random rng)
    {
        var items = new List<ItemStack>();

        void Add(string item, int count)
        {
            if (count <= 0 || _content.GetItem(item) is null)
            {
                return;
            }

            int at = items.FindIndex(s => s.Item == item);
            if (at >= 0)
            {
                items[at] = new ItemStack(item, items[at].Count + count);
            }
            else
            {
                items.Add(new ItemStack(item, count));
            }
        }

        if (crate == 0 || rng.NextDouble() < 0.7)
        {
            Add("medpack", rng.NextDouble() < 0.25 ? 2 : 1);
        }

        if (rng.NextDouble() < 0.1)
        {
            Add("field_medkit", 1);
        }

        if (rng.NextDouble() < 0.5)
        {
            Add("emergency_ration", 1 + rng.Next(2));
        }

        if (rng.NextDouble() < 0.45)
        {
            Add("spoiled_food", 1 + rng.Next(3));
        }

        var scrap = new[] { "scrap_metal", "cable", "iron_plate" };
        int picks = 1 + rng.Next(2);
        for (int k = 0; k < picks; k++)
        {
            Add(scrap[rng.Next(scrap.Length)], 1 + rng.Next(3));
        }

        // The logs: one, sometimes two — crate n carries log indices 2n and 2n+1 of its station, so every log of a
        // derelict is a different writer with a different job (DerelictLogs).
        int logs = rng.NextDouble() < 0.35 ? 2 : 1;
        if (!_content.DerelictLogs.IsEmpty && _content.GetItem(DerelictLogs.ItemKeyBase) is not null)
        {
            for (int k = 0; k < logs && crate * 2 + k < DerelictLogs.MaxPerStation; k++)
            {
                Add(DerelictLogs.ItemFor(DerelictLogs.SeedFor(_meta.Seed, stationId, crate * 2 + k)), 1);
            }
        }

        return items;
    }

    /// <summary>The logs a player just took out of a container (<see cref="LootContainer"/>): each new one goes into their
    /// Discoveries ledger (<c>log:&lt;seed&gt;</c>, named after the writer, sited where they found it — the Codex lists and
    /// re-reads it) with a toast that says where to read it. A log already in the ledger (dropped and picked up again, or
    /// a copy a friend left in a crate) only moves.</summary>
    private void RecordFoundDerelictLogs(PlayerSession session, List<string> taken)
    {
        var p = session.State;
        foreach (var item in taken)
        {
            uint seed = DerelictLogs.SeedOfItem(item);
            string key = DerelictLogs.LedgerKey(seed);
            if (seed == 0 || !p.Scanned.Add(key))
            {
                continue;
            }

            string writer = DerelictLogs.Compose(seed, _content.DerelictLogs)?.Writer ?? string.Empty;
            p.ScannedNames[key] = writer;
            var site = SiteFor(session);
            if (site is not null)
            {
                p.ScannedWhere[key] = site;
            }

            Send(session, DiscoveryDelta(key, writer, site));
            Send(session, new ServerMessage { Text = string.Format(Localize(session.Locale, "srv.derelict.log_found"), writer) });
        }
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam: the derelict crates of the active world (containers, by crate index).</summary>
    public IReadOnlyList<StoredContainer> DerelictCratesForTest(string stationId)
    {
        string prefix = DerelictCrateContainerPrefix + stationId.Replace(':', '_') + "_";
        return _containers.FindAll(c => c.Id.StartsWith(prefix, System.StringComparison.Ordinal));
    }

    /// <summary>Test seam: what crate <paramref name="crate"/> of a derelict holds (or would hold) in this world.</summary>
    public IReadOnlyList<ItemStack> DerelictCrateLootForTest(string stationId, int crate) => DerelictCrateLoot(stationId, crate);
}
