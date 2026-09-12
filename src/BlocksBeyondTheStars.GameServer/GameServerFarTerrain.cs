// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Far terrain (#1821). The client draws the horizon beyond its streamed chunks from the world seed, which knows
/// nothing of what was BUILT: the G.D.S. city, settlements, ruins, camps and every player build are persisted block
/// edits. On request the server summarises a 64×64-block tile of those edits — for each 4×4 cell the highest non-air
/// edit — and re-sends the tile when an edit changes it, so a tower going up on the horizon shows up there.
/// Bounded three ways: tiles are built lazily and cached per world, a client may only ask for tiles within the far-view
/// range of its own position, and a token bucket caps how many it gets per second.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The farthest tile centre (blocks, wrap-aware) a client may ask about: the largest far-view setting
    /// (1024) plus the streamed view (16 chunks) and a tile of slack.</summary>
    private const double FarTileMaxRangeBlocks = 1024 + 16 * 16 + FarTerrainTile.TileBlocks;

    private const double FarTileTokensPerSecond = 48;
    private const double FarTileTokenBurst = 160;
    private const int FarTilesSentCap = 4096;

    /// <summary>How often dirty tiles are rebuilt and re-sent to the clients holding them.</summary>
    private const double FarTileRefreshSeconds = 2.0;
    private double _sinceFarTileRefresh;

    private static (int Tx, int Tz) FarTileOf(int worldX, int worldZ)
        => (worldX >> 6, worldZ >> 6); // TileBlocks = 64; an arithmetic shift floors negatives

    /// <summary>BlockSet hook: an edit inside a cached tile marks it for a rebuild (tiles nobody asked for cost nothing).</summary>
    private void MarkFarTileDirty(LoadedWorld world, Vector3i cell)
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

    private void HandleFarTerrainTileRequest(PlayerSession session, FarTerrainTileRequest request)
    {
        var world = _worlds.Active;
        if (world is null || world.World.Planet.Void || session.CurrentLocationId != world.LocationId)
        {
            return; // a station interior has no far terrain; a request for a world the player left is stale
        }

        int worldId = WorldIdOf(session.CurrentLocationId);
        if (request.WorldId != 0 && request.WorldId != worldId)
        {
            return;
        }

        if (session.FarTilesWorldId != worldId)
        {
            session.FarTilesSent.Clear();
            session.FarTilesWorldId = worldId;
        }

        session.FarTileTokens = Math.Min(FarTileTokenBurst,
            session.FarTileTokens + Math.Max(0, _uptime - session.FarTileTokensAt) * FarTileTokensPerSecond);
        session.FarTileTokensAt = _uptime;

        var tiles = request.Tiles ?? Array.Empty<int>();
        int pairs = Math.Min(tiles.Length / 2, FarTerrainTile.MaxTilesPerRequest);
        int circumference = _world.Circumference;
        int latPeriod = WorldConstants.LatitudePeriodFor(circumference);
        var pos = session.State.Position;
        for (int i = 0; i < pairs; i++)
        {
            if (session.FarTileTokens < 1)
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

            session.FarTileTokens -= 1;
            var message = FarTileMessage(world, tx, tz, worldId);
            if (session.FarTilesSent.Count >= FarTilesSentCap)
            {
                session.FarTilesSent.Clear();
            }

            session.FarTilesSent[(tx, tz)] = message.Version;
            Send(session, message);
        }
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

    /// <summary>Test seam: runs a tile request the way the dispatcher would (active world = the player's).</summary>
    internal void FarTerrainTileRequestForTest(PlayerSession session, FarTerrainTileRequest request)
    {
        SetActiveWorld(session.CurrentLocationId);
        HandleFarTerrainTileRequest(session, request);
    }

    /// <summary>Whether this tick re-sends changed tiles (decided once per tick, like the chunk sweep).</summary>
    private bool FarTileRefreshDue(double dt)
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
    private void RefreshFarTiles()
    {
        var world = _worlds.Active;
        if (world is null || world.FarTiles.Count == 0)
        {
            return;
        }

        int worldId = WorldIdOf(world.LocationId);
        foreach (var session in JoinedInActiveWorld())
        {
            if (session.FarTilesWorldId != worldId || session.FarTilesSent.Count == 0)
            {
                continue;
            }

            List<(int, int)>? resend = null;
            foreach (var kv in session.FarTilesSent)
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
                session.FarTilesSent[(tx, tz)] = message.Version;
                Send(session, message);
            }
        }
    }
}
