// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="FarTerrainSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): the wire id of a
/// world, the sessions on the active world, the world's generator settings and — for the test seam only — the world
/// cursor. Implemented explicitly by <c>GameServer</c> in <c>GameServerFarTerrain.cs</c>, each member delegating to the
/// server's own member of the same name.
/// </summary>
internal interface IFarTerrainHost : IServerHost
{
    int WorldIdOf(string? locationId);

    IEnumerable<PlayerSession> JoinedInActiveWorld();

    /// <summary>The save's metadata (the server's <c>_meta</c>) — replaced when the server starts, so read it at use
    /// time.</summary>
    WorldMetadata Metadata { get; }

    bool SetActiveWorld(string locationId);
}

/// <summary>
/// Far terrain (#1821). The client draws the horizon beyond its streamed chunks from the world seed, which knows
/// nothing of what was BUILT: the G.D.S. city, settlements, ruins, camps and every player build are persisted block
/// edits. On request the server summarises a 64×64-block tile of those edits — for each 4×4 cell the highest non-air
/// edit — and re-sends the tile when an edit changes it, so a tower going up on the horizon shows up there.
/// Bounded three ways: tiles are built lazily and cached per world, a client may only ask for tiles within the far-view
/// range of its own position, and a token bucket caps how many it gets per second.
/// </summary>
internal sealed class FarTerrainSystem
{
    /// <summary>The farthest tile centre (blocks, wrap-aware) a client may ask about: the largest far-view setting
    /// (1024) plus the streamed view (16 chunks) and a tile of slack.</summary>
    private const double FarTileMaxRangeBlocks = 1024 + 16 * 16 + FarTerrainTile.TileBlocks;

    private const double FarTileTokensPerSecond = 48;
    private const double FarTileTokenBurst = 160;
    private const int FarTilesSentCap = 4096;

    /// <summary>#1871: wall-clock milliseconds one tick spends BUILDING tiles from the repository. A built-up world
    /// (a city, a big base) made every tile a repository query worth tens of milliseconds and the client asks for
    /// up to 48 a second right after joining; built in the handler, that froze the tick for a minute — doors and
    /// NPCs reacted seconds late while the client-side walk felt fine. Now requests queue per session and this
    /// budget paces the builds (at least one per tick, so a queue never starves). Cached tiles cost nothing and
    /// are still answered in the handler.</summary>
    private const double FarTileBuildBudgetMs = 4.0;

    /// <summary>Queued tiles per session beyond which further requests are dropped (the client re-asks later).</summary>
    private const int FarTileQueueCap = 512;

    /// <summary>Test seam (#1871): overrides <see cref="FarTileBuildBudgetMs"/>; 0 = exactly one build per tick,
    /// negative = unlimited (drain the queue in one tick).</summary>
    internal double? FarTileBudgetMsForTest { get; set; }

    /// <summary>How often dirty tiles are rebuilt and re-sent to the clients holding them.</summary>
    private const double FarTileRefreshSeconds = 2.0;
    private double _sinceFarTileRefresh;

    private readonly IFarTerrainHost _host;
    private readonly IWorldRepository _repo;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal FarTerrainSystem(IFarTerrainHost host, IWorldRepository repo)
    {
        _host = host;
        _repo = repo;
    }

    private static (int Tx, int Tz) FarTileOf(int worldX, int worldZ)
        => (worldX >> 6, worldZ >> 6); // TileBlocks = 64; an arithmetic shift floors negatives

    /// <summary>BlockSet hook: an edit inside a cached tile marks it for a rebuild (tiles nobody asked for cost nothing).</summary>
    internal void MarkFarTileDirty(LoadedWorld world, Vector3i cell)
    {
        if (world.FarTiles.Count == 0)
        {
            return;
        }

        if (world.FarTiles.TryGetValue(FarTileOf(cell.X, cell.Z), out var state))
        {
            state.Dirty = true;
        }
    }

    internal void HandleFarTerrainTileRequest(PlayerSession session, FarTerrainTileRequest request)
    {
        var world = _host.Worlds.Active;
        if (world is null || world.World.Planet.Void || session.CurrentLocationId != world.LocationId)
        {
            return; // a station interior has no far terrain; a request for a world the player left is stale
        }

        int worldId = _host.WorldIdOf(session.CurrentLocationId);
        if (request.WorldId != 0 && request.WorldId != worldId)
        {
            return;
        }

        if (session.Streaming.FarTilesWorldId != worldId)
        {
            session.Streaming.FarTilesSent.Clear();
            session.Streaming.FarTileQueue.Clear();
            session.Streaming.FarTileQueued.Clear();
            session.Streaming.FarTilesWorldId = worldId;
        }

        session.Streaming.FarTileTokens = Math.Min(FarTileTokenBurst,
            session.Streaming.FarTileTokens + Math.Max(0, _host.Uptime - session.Streaming.FarTileTokensAt) * FarTileTokensPerSecond);
        session.Streaming.FarTileTokensAt = _host.Uptime;

        var tiles = request.Tiles ?? Array.Empty<int>();
        int pairs = Math.Min(tiles.Length / 2, FarTerrainTile.MaxTilesPerRequest);
        int circumference = _host.World.Circumference;
        int latPeriod = WorldConstants.LatitudePeriodFor(circumference);
        var pos = session.State.Position;
        for (int i = 0; i < pairs; i++)
        {
            if (session.Streaming.FarTileTokens < 1)
            {
                break; // flood gate — the client re-asks for what it still lacks
            }

            // Canonical tile of the requested tile's origin (the tile grid restarts at each seam; the last column /
            // row of tiles before a seam is partial because circumferences are not multiples of 64).
            int tx = (int)WorldConstants.WrapX((double)tiles[2 * i] * FarTerrainTile.TileBlocks, circumference) >> 6;
            int tz = (int)Math.Floor(WorldConstants.WrapZ((double)tiles[2 * i + 1] * FarTerrainTile.TileBlocks, circumference)) >> 6;
            double centreX = tx * FarTerrainTile.TileBlocks + FarTerrainTile.TileBlocks * 0.5;
            double centreZ = tz * FarTerrainTile.TileBlocks + FarTerrainTile.TileBlocks * 0.5;
            if (!WithinFarRange(pos.X, pos.Z, centreX, centreZ, circumference, latPeriod))
            {
                continue; // out of any far view: refuse rather than scan the save for someone else's base
            }

            session.Streaming.FarTileTokens -= 1;
            var key = (tx, tz);
            if (world.FarTiles.TryGetValue(key, out var cached) && !cached.Dirty && cached.Message is not null)
            {
                SendFarTile(session, world, tx, tz, worldId); // already built: free, answered at once (the old path)
                continue;
            }

            // #1871: a build costs a repository query — queued for ServeFarTiles, never done in the handler.
            if (session.Streaming.FarTileQueued.Add(key))
            {
                if (session.Streaming.FarTileQueue.Count >= FarTileQueueCap)
                {
                    session.Streaming.FarTileQueued.Remove(key);
                    break; // the client re-asks what it still lacks
                }

                session.Streaming.FarTileQueue.Enqueue(key);
            }
        }
    }

    /// <summary>Builds (or fetches) one tile and sends it, recording the version the session holds.</summary>
    private void SendFarTile(PlayerSession session, LoadedWorld world, int tx, int tz, int worldId)
    {
        var message = FarTileMessage(world, tx, tz, worldId);
        if (session.Streaming.FarTilesSent.Count >= FarTilesSentCap)
        {
            session.Streaming.FarTilesSent.Clear();
        }

        session.Streaming.FarTilesSent[(tx, tz)] = message.Version;
        _host.Send(session, message);
    }

    /// <summary>
    /// #1871: builds the queued tiles of every session on the active world under <see cref="FarTileBuildBudgetMs"/>
    /// of wall-clock time per tick — round-robin across sessions, at least one tile per tick when anything waits.
    /// Guard-registered right after the chunk stream.
    /// </summary>
    internal void ServeFarTiles()
    {
        var world = _host.Worlds.Active;
        if (world is null)
        {
            return;
        }

        int worldId = _host.WorldIdOf(world.LocationId);
        double budgetMs = FarTileBudgetMsForTest ?? FarTileBuildBudgetMs;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        int built = 0;
        bool any;
        do
        {
            any = false;
            foreach (var session in _host.JoinedInActiveWorld())
            {
                if (session.Streaming.FarTileQueue.Count == 0 || session.Streaming.FarTilesWorldId != worldId)
                {
                    session.Streaming.FarTileQueue.Clear(); // a stale queue from a world the player left
                    session.Streaming.FarTileQueued.Clear();
                    continue;
                }

                var (tx, tz) = session.Streaming.FarTileQueue.Dequeue();
                session.Streaming.FarTileQueued.Remove((tx, tz));
                SendFarTile(session, world, tx, tz, worldId);
                built++;
                any = true;

                double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (budgetMs >= 0 && (elapsedMs >= budgetMs || budgetMs == 0))
                {
                    return; // the budget is spent (at least one tile went out)
                }
            }
        }
        while (any);
    }

    private static bool WithinFarRange(double px, double pz, double cx, double cz, int circumference, int latPeriod)
    {
        double dx = Math.Abs(px - cx) % circumference;
        dx = Math.Min(dx, circumference - dx);
        double dz = latPeriod > 0 ? Math.Abs(pz - cz) % latPeriod : Math.Abs(pz - cz);
        if (latPeriod > 0)
        {
            dz = Math.Min(dz, latPeriod - dz);
        }

        return dx * dx + dz * dz <= FarTileMaxRangeBlocks * FarTileMaxRangeBlocks;
    }

    /// <summary>The tile's current message — rebuilt from the repository when it is new or an edit dirtied it.</summary>
    private FarTerrainTile FarTileMessage(LoadedWorld world, int tx, int tz, int worldId)
    {
        if (!world.FarTiles.TryGetValue((tx, tz), out var state))
        {
            state = new FarTerrainTileState();
            world.FarTiles[(tx, tz)] = state;
        }

        if (state.Dirty || state.Message is null)
        {
            state.Version++;
            state.Message = BuildFarTile(world, tx, tz, state.Version);
            state.Dirty = false;
        }

        state.Message.WorldId = worldId;
        return state.Message;
    }

    /// <summary>Summarises one tile's persisted edits: per 4×4 cell the highest non-air edit (height, block, tint).</summary>
    internal FarTerrainTile BuildFarTile(LoadedWorld world, int tx, int tz, int version)
    {
        int minX = tx * FarTerrainTile.TileBlocks;
        int minZ = tz * FarTerrainTile.TileBlocks;
        var tops = _repo.LoadEditColumnTops(world.LocationId, minX, minZ,
            minX + FarTerrainTile.TileBlocks - 1, minZ + FarTerrainTile.TileBlocks - 1);

        var msg = new FarTerrainTile { TileX = tx, TileZ = tz, Version = version };
        if (tops.Count == 0)
        {
            return msg;
        }

        const int Cells = FarTerrainTile.CellsPerSide * FarTerrainTile.CellsPerSide;
        var topY = new int[Cells];
        var block = new ushort[Cells];
        var tint = new int[Cells];
        var has = new bool[Cells];
        foreach (var top in tops)
        {
            int cx = (top.X - minX) / FarTerrainTile.CellBlocks;
            int cz = (top.Z - minZ) / FarTerrainTile.CellBlocks;
            if ((uint)cx >= FarTerrainTile.CellsPerSide || (uint)cz >= FarTerrainTile.CellsPerSide)
            {
                continue;
            }

            int index = cz * FarTerrainTile.CellsPerSide + cx;
            if (!has[index] || top.Y > topY[index])
            {
                has[index] = true;
                topY[index] = top.Y;
                block[index] = top.Block;
                tint[index] = top.Tint;
            }
        }

        int count = 0;
        foreach (bool h in has)
        {
            count += h ? 1 : 0;
        }

        msg.Cells = new byte[count];
        msg.TopY = new short[count];
        msg.Blocks = new ushort[count];
        msg.Tints = new int[count];
        int n = 0;
        for (int index = 0; index < Cells; index++)
        {
            if (!has[index])
            {
                continue;
            }

            msg.Cells[n] = (byte)index;
            msg.TopY[n] = (short)Math.Clamp(topY[index], short.MinValue, short.MaxValue);
            msg.Blocks[n] = block[index];
            msg.Tints[n] = tint[index];
            n++;
        }

        return msg;
    }

    /// <summary>#1820: the active world's generator settings for the client's far terrain.</summary>
    internal FarTerrainWorldInfo BuildFarTerrainWorldInfo()
    {
        var world = _host.World;
        var pads = world.LandingPadFlats;
        var packed = new int[pads.Count * FarTerrainWorldInfo.PadStride];
        for (int i = 0; i < pads.Count; i++)
        {
            var pad = pads[i];
            int o = i * FarTerrainWorldInfo.PadStride;
            packed[o] = pad.CenterX;
            packed[o + 1] = pad.CenterZ;
            packed[o + 2] = pad.SurfaceY;
            packed[o + 3] = pad.Radius;
            packed[o + 4] = pad.Islet ? (pad.Deck ? 3 : pad.Molten ? 2 : 1) : 0; // 2 = a basalt lava islet (generation 8), 3 = a metal deck (#2112)
            packed[o + 5] = pad.PlateauRadius;
            packed[o + 6] = pad.IsletRadius;
            packed[o + 7] = pad.ClassicShape ? 1 : 0;
        }

        return new FarTerrainWorldInfo
        {
            WorldId = _host.WorldIdOf(world.LocationId),
            LocationId = world.LocationId,
            PlanetType = world.PlanetKey,
            Circumference = world.Circumference,
            Cratered = world.Cratered,
            ContinentsEnabled = _host.Metadata.Description.TerrainContinents,
            LavaCoreVolcanoes = _host.Metadata.Description.LavaCoreVolcanoes,
            TerrainGeneration = _host.Metadata.Description.TerrainGeneration,
            Void = world.Planet.Void,
            Pads = packed,
        };
    }

    internal void SendFarTerrainWorldInfo(PlayerSession session) => _host.Send(session, BuildFarTerrainWorldInfo());

    /// <summary>Test seam: runs a tile request the way the dispatcher would (active world = the player's) and,
    /// unless <paramref name="queueOnly"/>, serves everything it queued in one go (#1871) — the pre-queue contract
    /// for tests that look at the answer straight away.</summary>
    internal void FarTerrainTileRequestForTest(PlayerSession session, FarTerrainTileRequest request, bool queueOnly = false)
    {
        _host.SetActiveWorld(session.CurrentLocationId);
        HandleFarTerrainTileRequest(session, request);
        if (!queueOnly)
        {
            double? saved = FarTileBudgetMsForTest;
            FarTileBudgetMsForTest = -1;
            ServeFarTiles();
            FarTileBudgetMsForTest = saved;
        }
    }

    /// <summary>Whether this tick re-sends changed tiles (decided once per tick, like the chunk sweep).</summary>
    internal bool FarTileRefreshDue(double dt)
    {
        _sinceFarTileRefresh += dt;
        if (_sinceFarTileRefresh < FarTileRefreshSeconds)
        {
            return false;
        }

        _sinceFarTileRefresh = 0;
        return true;
    }

    /// <summary>Per active world: rebuilds the dirty tiles some client on it holds and re-sends them.</summary>
    internal void RefreshFarTiles()
    {
        var world = _host.Worlds.Active;
        if (world is null || world.FarTiles.Count == 0)
        {
            return;
        }

        int worldId = _host.WorldIdOf(world.LocationId);
        foreach (var session in _host.JoinedInActiveWorld())
        {
            if (session.Streaming.FarTilesWorldId != worldId || session.Streaming.FarTilesSent.Count == 0)
            {
                continue;
            }

            List<(int, int)>? resend = null;
            foreach (var kv in session.Streaming.FarTilesSent)
            {
                if (world.FarTiles.TryGetValue(kv.Key, out var state) && (state.Dirty || state.Version != kv.Value))
                {
                    (resend ??= new List<(int, int)>()).Add(kv.Key);
                }
            }

            if (resend is null)
            {
                continue;
            }

            foreach (var (tx, tz) in resend)
            {
                var message = FarTileMessage(world, tx, tz, worldId);
                session.Streaming.FarTilesSent[(tx, tz)] = message.Version;
                _host.Send(session, message);
            }
        }
    }
}
