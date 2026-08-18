// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Persisted exploration (#1113): a coarse per-player, per-body explored-cell bitmap fills up as terrain
/// streams (the planet map's fog then stays lifted across sessions), and the FIRST landing on a body writes
/// a "place" entry into the Discoveries ledger and pays a small knowledge grant — exploration itself became
/// a knowledge faucet. Existing saves backfill their already-landed bodies into the ledger on join, without
/// the knowledge windfall.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Knowledge granted for the first landing on a body (#1113).</summary>
    internal const int KnowledgeFirstLanding = 5;

    /// <summary>Discoveries-ledger prefix for first landings ("place:&lt;bodyId&gt;") — the Codex lists these
    /// under a "Places" group (same "kind:key" convention as every other ledger entry).</summary>
    internal const string PlaceLedgerPrefix = "place:";

    /// <summary>Marks the explored-map cell of a chunk column just streamed to this player. Only real
    /// galaxy bodies map — ship interiors and station shells have no planet map.</summary>
    private void MarkExploredCell(PlayerSession session, ChunkCoord canonical)
    {
        string bodyId = _world.LocationId;
        if (_galaxy?.FindBody(bodyId) == null)
        {
            return;
        }

        int index = ExploredMap.CellIndex(canonical.X, canonical.Z, _world.Circumference);
        if (index < 0)
        {
            return;
        }

        var (cols, rows) = ExploredMap.GridFor(_world.Circumference);
        int bytes = ExploredMap.ByteSize(cols, rows);
        if (bytes > ExploredMap.MaxBytesPerBody)
        {
            return; // defensive — no legal circumference produces this
        }

        var cells = session.State.ExploredCells;
        if (!cells.TryGetValue(bodyId, out var map) || map.Length != bytes)
        {
            map = new byte[bytes]; // absent, or wrong-length (corrupt) data restarts clean
            cells[bodyId] = map;
        }

        ExploredMap.SetBit(map, index);
    }

    /// <summary>Sends the receiver's persisted explored cells for the body they just arrived on. The grid
    /// derives from the ACTIVE world's circumference, so arrivals elsewhere (never the common case) skip.</summary>
    private void SendExploredMap(PlayerSession session, string bodyId)
    {
        if (bodyId != _world.LocationId)
        {
            return;
        }

        var (cols, rows) = ExploredMap.GridFor(_world.Circumference);
        session.State.ExploredCells.TryGetValue(bodyId, out var map);
        Send(session, new ExploredMapData
        {
            BodyId = bodyId,
            Cols = cols,
            Rows = rows,
            Cells = map ?? System.Array.Empty<byte>(),
        });
    }

    /// <summary>First landing on a body: a "place" Discoveries entry + the knowledge grant, pushed as the
    /// usual ledger delta so the Codex updates live. The VERY FIRST body — the spawn world — records the
    /// entry but pays nothing: knowledge is for venturing out, and a join-time grant would make VEGA read
    /// every fresh save as a veteran (<c>VegaIsVeteran</c> keys on KnowledgePoints &gt; 0) and skip the
    /// whole onboarding.</summary>
    private void RecordPlaceDiscovery(PlayerSession session, CelestialBody body)
    {
        if (!TryAddPlaceEntry(session.State, body))
        {
            return;
        }

        bool spawnWorld = session.State.LandedBodies.Count <= 1; // the caller just added this body
        if (!spawnWorld)
        {
            session.State.KnowledgePoints += KnowledgeFirstLanding;
        }

        Send(session, new DiscoveryLog
        {
            Entries = new[] { PlaceLedgerPrefix + body.Id },
            Names = new[] { body.Name },
            Full = false,
        });
        if (!spawnWorld)
        {
            SendInventory(session); // the knowledge total just changed
        }
    }

    /// <summary>Adds the ledger entry alone — no knowledge, no messages. Shared by the live path and the
    /// join backfill.</summary>
    private static bool TryAddPlaceEntry(PlayerState p, CelestialBody body)
    {
        string key = PlaceLedgerPrefix + body.Id;
        if (!p.Scanned.Add(key))
        {
            return false;
        }

        p.ScannedNames[key] = body.Name;
        return true;
    }

    /// <summary>Join backfill (#1113): saves from before this feature already carry landed bodies — mirror
    /// them into the Places group once, silently, BEFORE the full DiscoveryLog snapshot goes out.</summary>
    private void BackfillPlaceDiscoveries(PlayerSession session)
    {
        foreach (var id in session.State.LandedBodies)
        {
            var body = _galaxy?.FindBody(id);
            if (body != null)
            {
                TryAddPlaceEntry(session.State, body);
            }
        }
    }
}
