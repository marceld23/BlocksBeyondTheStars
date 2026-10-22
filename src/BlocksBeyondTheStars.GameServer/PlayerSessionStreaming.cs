// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection streaming bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionStreaming
{
    /// <summary>Chunks already streamed to this client, to avoid resending.</summary>
    public HashSet<ChunkCoord> SentChunks { get; } = new();

    /// <summary>#1507: the streaming pass found nothing left to send for this view — recorded with the centre
    /// chunk, the stream radius and the sent-set size it was true for, so the next ticks can skip the whole
    /// (2R+3)² column enumeration (up to ~1200 coords + hundreds of terrain lookups per player per tick) until
    /// the player moves, the view changes, the sent-set changes, or the periodic re-check comes due.</summary>
    public bool StreamSettled { get; set; }
    public ChunkCoord StreamSettledCenter { get; set; }
    public int StreamSettledRadius { get; set; }
    public int StreamSettledSentCount { get; set; }
    public int StreamSettledTicks { get; set; }

    /// <summary>#1818: the player's horizontal velocity (blocks/s) as the streamer sees it — sampled from successive
    /// positions, smoothed, zeroed on a teleport-sized jump. Drives the streaming look-ahead.</summary>
    public float StreamVelX { get; set; }
    public float StreamVelZ { get; set; }
    public float StreamSampleX { get; set; }
    public float StreamSampleZ { get; set; }
    public double StreamSampleAt { get; set; } = -1;

    /// <summary>#1821: far-terrain tiles this client asked for on its current world, with the version it has.</summary>
    public Dictionary<(int Tx, int Tz), int> FarTilesSent { get; } = new();
    public int FarTilesWorldId { get; set; }
    public double FarTileTokens { get; set; } = 64;
    public double FarTileTokensAt { get; set; }

    /// <summary>#1871: accepted tile requests still waiting for their build. Tiles are built under a per-tick budget
    /// (<c>ServeFarTiles</c>), never in the request handler — a burst of requests on a built-up world used to stall
    /// the tick for a minute. <see cref="FarTileQueued"/> mirrors the queue so a re-ask of a queued tile is a no-op.</summary>
    public Queue<(int Tx, int Tz)> FarTileQueue { get; } = new();
    public HashSet<(int Tx, int Tz)> FarTileQueued { get; } = new();

    /// <summary>#1820: the next streaming pass sends this session its world's FarTerrainWorldInfo (set on join and
    /// whenever a WorldReset goes out).</summary>
    public bool FarInfoDue { get; set; } = true;

    /// <summary>Uptime at which each chunk last triggered a full ghost re-stream for this session (#965), so a
    /// burst of ghosts in one chunk costs one re-stream (and one log line), not one per cell.</summary>
    public Dictionary<ChunkCoord, double> GhostChunkSeen { get; } = new();
}
