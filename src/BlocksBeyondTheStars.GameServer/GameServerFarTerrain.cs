// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Far terrain (#1821/#1871) on the server: the facade of <see cref="FarTerrainSystem"/> (ADR 0019), which owns the
/// per-world tile cache, the per-session token bucket and build queue, the per-tick build budget and the dirty-tile
/// refresh. This partial keeps the members the world's block hook, the tick, the dispatch table, the join path and the
/// tests call — same names, same signatures, each one line delegating — and the explicit
/// <see cref="IFarTerrainHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IFarTerrainHost
{
    /// <summary>Test seam (#1871): overrides the per-tick tile build budget of <see cref="FarTerrainSystem"/>; 0 = exactly
    /// one build per tick, negative = unlimited (drain the queue in one tick).</summary>
    internal double? FarTileBudgetMsForTest
    {
        get => _farTerrainSystem.FarTileBudgetMsForTest;
        set => _farTerrainSystem.FarTileBudgetMsForTest = value;
    }

    /// <summary>BlockSet hook: an edit inside a cached tile marks it for a rebuild (tiles nobody asked for cost nothing).</summary>
    private void MarkFarTileDirty(LoadedWorld world, Vector3i cell) => _farTerrainSystem.MarkFarTileDirty(world, cell);

    private void HandleFarTerrainTileRequest(PlayerSession session, FarTerrainTileRequest request)
        => _farTerrainSystem.HandleFarTerrainTileRequest(session, request);

    /// <summary>#1871: builds the queued tiles of every session on the active world under the per-tick budget.
    /// Guard-registered right after the chunk stream.</summary>
    private void ServeFarTiles() => _farTerrainSystem.ServeFarTiles();

    /// <summary>#1820: the active world's generator settings for the client's far terrain.</summary>
    internal FarTerrainWorldInfo BuildFarTerrainWorldInfo() => _farTerrainSystem.BuildFarTerrainWorldInfo();

    private void SendFarTerrainWorldInfo(PlayerSession session) => _farTerrainSystem.SendFarTerrainWorldInfo(session);

    /// <summary>Test seam: runs a tile request the way the dispatcher would (active world = the player's) and,
    /// unless <paramref name="queueOnly"/>, serves everything it queued in one go (#1871) — the pre-queue contract
    /// for tests that look at the answer straight away.</summary>
    internal void FarTerrainTileRequestForTest(PlayerSession session, FarTerrainTileRequest request, bool queueOnly = false)
        => _farTerrainSystem.FarTerrainTileRequestForTest(session, request, queueOnly);

    /// <summary>Whether this tick re-sends changed tiles (decided once per tick, like the chunk sweep).</summary>
    private bool FarTileRefreshDue(double dt) => _farTerrainSystem.FarTileRefreshDue(dt);

    /// <summary>Per active world: rebuilds the dirty tiles some client on it holds and re-sends them.</summary>
    private void RefreshFarTiles() => _farTerrainSystem.RefreshFarTiles();

    // IFarTerrainHost: each member delegates to the server's own member of the same name (never to the interface
    // member, which would recurse).
    int IFarTerrainHost.WorldIdOf(string? locationId) => WorldIdOf(locationId);

    IEnumerable<PlayerSession> IFarTerrainHost.JoinedInActiveWorld() => JoinedInActiveWorld();

    WorldMetadata IFarTerrainHost.Metadata => _meta;

    bool IFarTerrainHost.SetActiveWorld(string locationId) => SetActiveWorld(locationId);
}
