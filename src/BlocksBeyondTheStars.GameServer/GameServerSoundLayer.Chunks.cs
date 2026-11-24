// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The second chunk variant (#3907): "real + sound blocks", what a player on the other side is sent for a chunk that
/// carries sound cells. On-foot movement belongs to the client and the server trusts the reported position, so over there
/// a sound block is an ORDINARY block inside the chunk message — meshing, collision and light need nothing new.
/// <para>The variant is composed from the real chunk (its blocks with <c>sound_block</c> written into the sound cells
/// that are air on the real side, and the glow per cell) and cached beside the real one, keyed by the chunk's version and
/// the sound layer's per-chunk counter. A chunk WITHOUT sound cells has no second variant at all: both sides are served
/// from the one real cache entry. A real-side player never reaches any of this (<see cref="StreamChunkNow"/> asks
/// <see cref="PlayerSession.OtherSide"/> first).</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Diagnostics (#3907): composed chunk variants built vs served from their cache — a chunk without sound
    /// cells moves neither (it is the real chunk's cache entry, <see cref="ChunkPayloadCacheHits"/>).</summary>
    public int SoundChunkEncodes { get; private set; }
    public int SoundChunkCacheHits { get; private set; }

    /// <summary>The compose buffer: one chunk's cells, reused (tick thread only).</summary>
    private readonly ushort[] _soundComposeBlocks = new ushort[WorldConstants.BlocksPerChunk];

    /// <summary>The chunks a side change sends again (reused).</summary>
    private readonly List<ChunkCoord> _soundResend = new();

    /// <summary>The sound cells of a chunk of the active world, or null for a chunk that carries none — the chunk is then
    /// the same for both sides.</summary>
    private SoundChunk? SoundChunkAt(ChunkCoord coord)
    {
        var world = _worlds.Active;
        var layer = SoundLayerOf(world);
        return layer.Count != 0 && layer.Chunks.TryGetValue(WorldConstants.CanonicalChunk(coord, world.World.Circumference), out var chunk)
            ? chunk
            : null;
    }

    /// <summary>The composed chunk as a wire message: the real chunk's message with the sound blocks in it. A sound cell
    /// a real block stands in right now (snow, water, a stamp) is HIDDEN: the real block is sent, the cell stays in the
    /// layer and shows again when the real cell is air.</summary>
    private ChunkDataMessage BuildSoundChunkMessage(ChunkData chunk, ChunkCoord coord, SoundChunk sound)
    {
        var blocks = _soundComposeBlocks;
        chunk.RawBlocks.CopyTo(blocks);
        ushort soundId = SoundBlockId;
        int shown = 0;
        foreach (int index in sound.Cells.Keys)
        {
            if (blocks[index] == 0)
            {
                blocks[index] = soundId;
                shown++;
            }
        }

        var msg = new ChunkDataMessage { Cx = coord.X, Cy = coord.Y, Cz = coord.Z, WorldId = ActiveWorldId };
        var rle = ChunkBlocksRle.Encode((System.ReadOnlySpan<ushort>)blocks);
        if (rle.Length < WorldConstants.BlocksPerChunk)
        {
            msg.BlocksRle = rle;
        }
        else
        {
            msg.Blocks = (ushort[])blocks.Clone();
        }

        PackChunkModifiers(chunk, msg);
        AppendSoundGlow(chunk, sound, msg, shown);
        msg.EditRuns = chunk.EditedRuns();
        msg.DugRuns = chunk.DugRuns();
        return msg;
    }

    /// <summary>Adds the glow colour of every shown sound cell to the message's sparse modifier arrays. A cell that is air
    /// on the real side carries no modifier of its own, so no index appears twice.</summary>
    private static void AppendSoundGlow(ChunkData chunk, SoundChunk sound, ChunkDataMessage msg, int shown)
    {
        if (shown == 0)
        {
            return;
        }

        int at = msg.ModIndex.Length;
        var index = new int[at + shown];
        var tint = new int[at + shown];
        var glow = new int[at + shown];
        System.Array.Copy(msg.ModIndex, index, at);
        System.Array.Copy(msg.ModTint, tint, at);
        System.Array.Copy(msg.ModGlow, glow, at);
        var real = chunk.RawBlocks;
        foreach (var kv in sound.Cells)
        {
            if (real[kv.Key] == 0)
            {
                index[at] = kv.Key;
                glow[at] = kv.Value.Glow;
                at++;
            }
        }

        msg.ModIndex = index;
        msg.ModTint = tint;
        msg.ModGlow = glow;
    }

    /// <summary>The object-path twin of <see cref="SoundChunkPayloadFor"/> (the in-process loopback): the composed message,
    /// cached per (chunk version, sound version).</summary>
    private ChunkDataMessage SoundChunkMessageFor(ChunkData chunk, ChunkCoord coord, SoundChunk sound)
    {
        var cache = _worlds.Active.SoundLayer.Messages;
        if (cache.TryGetValue(coord, out var hit) && hit.Version == chunk.Version && hit.SoundVersion == sound.SoundVersion)
        {
            SoundChunkCacheHits++;
            return hit.Message;
        }

        var msg = BuildSoundChunkMessage(chunk, coord, sound);
        SoundChunkEncodes++;
        if (cache.Count >= ChunkPayloadCacheCap)
        {
            cache.Clear();
        }

        cache[coord] = (chunk.Version, sound.SoundVersion, msg);
        return msg;
    }

    /// <summary>The encoded composed chunk, cached beside the real chunk's payload per (chunk version, sound version, codec
    /// format) with the same cap — every player over there who streams the chunk gets the same bytes.</summary>
    private byte[] SoundChunkPayloadFor(ChunkData chunk, ChunkCoord coord, SoundChunk sound)
    {
        var cache = _worlds.Active.SoundLayer.Payloads;
        bool json = NetCodec.UseJsonEncoding;
        if (cache.TryGetValue(coord, out var hit) && hit.Version == chunk.Version && hit.SoundVersion == sound.SoundVersion && hit.Json == json)
        {
            SoundChunkCacheHits++;
            return hit.Payload;
        }

        var payload = NetCodec.Encode(BuildSoundChunkMessage(chunk, coord, sound));
        SoundChunkEncodes++;
        if (cache.Count >= ChunkPayloadCacheCap)
        {
            cache.Clear();
        }

        cache[coord] = (chunk.Version, sound.SoundVersion, json, payload);
        return payload;
    }

    /// <summary>Sends a world-local message to the players of the active world who are on the other side, and to nobody
    /// else — a sound cell's <see cref="BlockChanged"/>, its cue. Encoded once.</summary>
    private void BroadcastToOtherSide(object message) => SendToOtherSideOf(_worlds.Active.LocationId, message);

    /// <summary>As <see cref="BroadcastToOtherSide"/> for a named resident world (the real-side-wins hook runs for whichever
    /// world was written, not only the one under the cursor). Nothing is encoded when nobody is over there.</summary>
    private void SendToOtherSideOf(string locationId, object message)
    {
        var mode = DeliveryMode.ReliableOrdered;
        if (message is BlockChanged change)
        {
            change.WorldId = WorldIdOf(locationId);
            mode = DeliveryMode.ReliableOrderedBulk; // the world-stream channel, like every block change
        }

        byte[]? payload = null;
        foreach (var s in _sessions.Values)
        {
            if (!s.Joined || !s.OtherSide || s.CurrentLocationId != locationId)
            {
                continue;
            }

            if (_objectTransport != null)
            {
                _objectTransport.SendMessage(s.ConnectionId, message, mode);
                continue;
            }

            payload ??= NetCodec.Encode(message);
            SendEncoded(s.ConnectionId, payload, mode);
        }
    }

    /// <summary>A player crossed or came back (called by the other-side core right after the flag flipped): the chunks of
    /// their sent set that carry sound cells go out again in the variant of their new side — and only those; every other
    /// chunk is the same on both sides.</summary>
    partial void SoundLayerSideChanged(PlayerSession s)
    {
        var sent = s.Streaming.SentChunks;
        if (!s.Joined || sent.Count == 0 || _worlds.Find(s.CurrentLocationId) is not { } world)
        {
            return;
        }

        var layer = SoundLayerOf(world);
        if (layer.Count == 0)
        {
            return;
        }

        var resend = _soundResend;
        resend.Clear();
        if (layer.Chunks.Count <= sent.Count)
        {
            foreach (var coord in layer.Chunks.Keys)
            {
                if (sent.Contains(coord))
                {
                    resend.Add(coord);
                }
            }
        }
        else
        {
            foreach (var coord in sent)
            {
                if (layer.Chunks.ContainsKey(coord))
                {
                    resend.Add(coord);
                }
            }
        }

        bool underCursor = ReferenceEquals(world, _worlds.Active);
        foreach (var coord in resend)
        {
            if (underCursor)
            {
                StreamChunkNow(s, coord);
            }
            else
            {
                sent.Remove(coord); // not this world's turn: the streaming pass sends it on the world's next tick
            }
        }

        resend.Clear();
    }

    /// <summary>What the other-side core shows of the cap: how many sound blocks the player has standing in their world,
    /// and how many they may have.</summary>
    partial void FillSoundBlockCount(PlayerSession s, ref int used, ref int cap)
    {
        cap = OtherSideRules.SoundBlockCap;
        used = _worlds.Find(s.CurrentLocationId) is { } world ? SoundBlocksOf(world, s.State.PlayerId) : 0;
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: sends one chunk of the player's world to the player right now, as the streaming pass would.</summary>
    internal void StreamChunkForTest(PlayerSession s, ChunkCoord coord)
    {
        if (SetActiveWorld(s.CurrentLocationId))
        {
            StreamChunkNow(s, WorldConstants.CanonicalChunk(coord, _world.Circumference));
        }
    }

    /// <summary>Test seam: the side-change hook as the other-side core calls it after flipping the flag.</summary>
    internal void SoundLayerSideChangedForTest(PlayerSession s) => SoundLayerSideChanged(s);

    /// <summary>Test seam: the count and the cap as the other-side core's state message reads them.</summary>
    internal (int Used, int Cap) SoundBlockCountForTest(PlayerSession s)
    {
        int used = 0, cap = 0;
        FillSoundBlockCount(s, ref used, ref cap);
        return (used, cap);
    }

    /// <summary>Test seam (the cap's measurement): one chunk of the active world composed and encoded past the cache —
    /// the composed variant when it carries sound cells, the real chunk otherwise.</summary>
    internal byte[] EncodeChunkUncachedForTest(ChunkCoord coord)
    {
        coord = WorldConstants.CanonicalChunk(coord, _world.Circumference);
        var chunk = _world.GetOrLoadChunk(coord);
        var message = SoundChunkAt(coord) is { } sound ? BuildSoundChunkMessage(chunk, coord, sound) : BuildChunkMessage(chunk, coord);
        return NetCodec.Encode(message);
    }

    /// <summary>Test seam: the per-world flush of the tick, for the player's world.</summary>
    internal void FlushSoundLayerForTest(PlayerSession s)
    {
        if (SetActiveWorld(s.CurrentLocationId))
        {
            FlushSoundLayer();
        }
    }

    /// <summary>Test seam: how many sound cells of the active world wait for the flush.</summary>
    internal int SoundReappearQueuedForTest => _worlds.Active.SoundLayer.Reappear.Count;

    /// <summary>Test seam: stands a sound block in a cell of the active world with no rule asked (fills a layer quickly).</summary>
    internal void PutSoundCellForTest(Vector3i cell, int glow, string owner) => PutSoundCell(_worlds.Active, cell, glow, owner);

    /// <summary>Test seam: how many chunks of the active world carry sound cells.</summary>
    internal int SoundChunkCountForTest => SoundLayerOf(_worlds.Active).Chunks.Count;

    /// <summary>Test seam: a tone of the melody bar, as the note intent's handler remembers it.</summary>
    internal void RememberSoundToneForTest(PlayerSession s, int tone) => RememberSoundTone(s, tone);
}
