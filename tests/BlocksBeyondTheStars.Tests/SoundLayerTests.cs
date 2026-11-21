// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3907 — the sound layer: the sound blocks of the other side live beside the world's block grid. A chunk has two
/// variants (the real one and "real + sound blocks"); a chunk without sound cells is one cache entry for both sides; a
/// change of a sound cell reaches only the players over there; a change of side re-sends only the chunks that carry
/// sound cells; the cap, the builder's ownership and the base rule hold; a block a player builds dissolves a sound block,
/// what weather and nature write only hides it; the layer survives a restart. The Resonance Blaster's part is in <see cref="SoundShapingTests"/>.
/// </summary>
public sealed class SoundLayerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private sealed class Recorder : IGameEventListener
    {
        public readonly List<(string Player, string Key, string Arg)> Events = new();

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => Events.Add((actor.State.PlayerId, key, arg));
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 12);

    /// <summary>A second player on the pad, <paramref name="dx"/> blocks east of its middle.</summary>
    internal static PlayerSession Join(NpcLifeWorld w, string name, float dx, bool overThere)
    {
        var s = w.Server.AddLocalPlayer(name);
        s.State.AboardShip = false;
        s.State.Position = new Vector3f(NpcLifeWorld.Cx + dx, w.Feet, NpcLifeWorld.Cz + 6.5f);
        s.OtherSide = overThere;
        return s;
    }

    /// <summary>A free cell on the pad (air on the real side), <paramref name="dx"/>/<paramref name="dz"/> from its middle.</summary>
    internal static Vector3i OnPad(NpcLifeWorld w, int dx, int dz, int up = 0) => new(NpcLifeWorld.Cx + dx, w.Feet + up, NpcLifeWorld.Cz + dz);

    private static ushort SoundId(NpcLifeWorld w) => w.Block(SoundersIds.SoundBlock).Value;

    private static ChunkDataMessage LastChunk(NpcLifeWorld w, PlayerSession to, ChunkCoord coord)
        => w.Transport.To<ChunkDataMessage>(to).Last(m => m.Cx == coord.X && m.Cy == coord.Y && m.Cz == coord.Z);

    private static ushort BlockIn(ChunkDataMessage m, Vector3i cell)
    {
        var local = WorldConstants.WorldToLocal(cell);
        return m.DecodeBlocks(WorldConstants.BlocksPerChunk)![WorldConstants.LocalIndex(local.X, local.Y, local.Z)];
    }

    private static int GlowIn(ChunkDataMessage m, Vector3i cell)
    {
        var local = WorldConstants.WorldToLocal(cell);
        int at = Array.IndexOf(m.ModIndex, WorldConstants.LocalIndex(local.X, local.Y, local.Z));
        return at < 0 ? 0 : m.ModGlow[at];
    }

    [Fact]
    public void ASoundBlock_IsNotInTheRealWorld_AndOnlyThePlayersOverThereAreTold()
    {
        using var w = World("sound_shape");
        var events = new Recorder();
        w.Server.AddGameEventListener(GameEventKeys.SoundShaped, events);
        var singer = w.Player;
        singer.OtherSide = true;
        var listener = Join(w, "Listener", 2.5f, overThere: true);
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var cell = OnPad(w, 3, 3);
        w.Transport.Sent.Clear();

        Assert.True(w.Server.TryShapeSound(singer, cell, 3, out string refusal), refusal);

        Assert.True(w.Server.IsSoundBlock(cell));
        Assert.Equal(OtherSideRules.ToneGlow[3], w.Server.SoundGlowAt(cell));
        Assert.True(w.Server.World.GetBlock(cell).IsAir, "the real world's grid never holds a sound block");
        foreach (var over in new[] { singer, listener })
        {
            var change = Assert.Single(w.Transport.To<BlockChanged>(over));
            Assert.Equal((cell.X, cell.Y, cell.Z, SoundId(w), OtherSideRules.ToneGlow[3]), (change.X, change.Y, change.Z, change.Block, change.Glow));
            var cue = Assert.Single(w.Transport.To<SoundFx>(over));
            Assert.Equal(SvGameServer.SoundShapeFx, cue.SoundId);
            Assert.Equal(OtherSideRules.PitchOfTone(3), cue.Pitch, 4);
        }

        Assert.Empty(w.Transport.To(real));
        Assert.DoesNotContain(w.Transport.Sent, x => x.Conn == int.MinValue);
        Assert.Equal((singer.State.PlayerId, GameEventKeys.SoundShaped, "3"), Assert.Single(events.Events));
        Assert.Equal(1, w.Server.SoundBlocksOf(singer.State.PlayerId));
        Assert.Equal(new[] { cell }, w.Server.SoundCellsNear(new Vector3f(cell.X, cell.Y, cell.Z), 4));
        Assert.Empty(w.Server.SoundCellsNear(new Vector3f(cell.X + 30, cell.Y, cell.Z), 4));
    }

    [Fact]
    public void AChunkWithoutSoundCells_IsServedFromTheRealCache_ForBothSides()
    {
        using var w = World("sound_cache_shared");
        var over = w.Player;
        over.OtherSide = true;
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var sung = OnPad(w, 3, 3);
        Assert.True(w.Server.TryShapeSound(over, sung, 0, out _)); // the layer is not empty — another chunk carries a cell
        var plain = new ChunkCoord(WorldConstants.WorldToChunk(sung).X, WorldConstants.WorldToChunk(sung).Y + 2, WorldConstants.WorldToChunk(sung).Z);
        int encodes = w.Server.ChunkPayloadEncodes, hits = w.Server.ChunkPayloadCacheHits;
        int soundEncodes = w.Server.SoundChunkEncodes, soundHits = w.Server.SoundChunkCacheHits;

        w.Server.StreamChunkForTest(real, plain);
        w.Server.StreamChunkForTest(over, plain);

        Assert.Equal(encodes + 1, w.Server.ChunkPayloadEncodes); // encoded once …
        Assert.Equal(hits + 1, w.Server.ChunkPayloadCacheHits);  // … and the player over there got the same cache entry
        Assert.Equal(soundEncodes, w.Server.SoundChunkEncodes);  // no second variant was built
        Assert.Equal(soundHits, w.Server.SoundChunkCacheHits);
        Assert.Equal(1, w.Server.SoundChunkCountForTest);
    }

    [Fact]
    public void OverThere_TheChunkCarriesTheSoundBlocks_OnTheRealSide_ItIsTheRealChunk()
    {
        using var w = World("sound_cache_variant");
        var over = w.Player;
        over.OtherSide = true;
        var second = Join(w, "Second", 2.5f, overThere: true);
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var cell = OnPad(w, 3, 3);
        var coord = WorldConstants.WorldToChunk(cell);
        Assert.True(w.Server.TryShapeSound(over, cell, 6, out _));
        int soundEncodes = w.Server.SoundChunkEncodes, soundHits = w.Server.SoundChunkCacheHits;

        w.Server.StreamChunkForTest(over, coord);
        w.Server.StreamChunkForTest(real, coord);
        w.Server.StreamChunkForTest(second, coord);

        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, over, coord), cell));
        Assert.Equal(OtherSideRules.ToneGlow[6], GlowIn(LastChunk(w, over, coord), cell));
        Assert.Equal(0, BlockIn(LastChunk(w, real, coord), cell));
        Assert.Equal(0, GlowIn(LastChunk(w, real, coord), cell));
        Assert.DoesNotContain(SoundId(w), LastChunk(w, real, coord).DecodeBlocks(WorldConstants.BlocksPerChunk)!);
        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, second, coord), cell));
        Assert.Equal(soundEncodes + 1, w.Server.SoundChunkEncodes); // composed once …
        Assert.Equal(soundHits + 1, w.Server.SoundChunkCacheHits);  // … the second player over there got the cached bytes

        // A further sound cell in the chunk, and a real edit in it, each make the cached variant stale.
        var beside = OnPad(w, 4, 3);
        Assert.True(w.Server.TryShapeSound(second, beside, 1, out _));
        w.Server.StreamChunkForTest(over, coord);
        Assert.Equal(soundEncodes + 2, w.Server.SoundChunkEncodes);
        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, over, coord), beside));

        var built = OnPad(w, 5, 3);
        w.Set(built.X, built.Y, built.Z, "stone");
        w.Server.StreamChunkForTest(over, coord);
        Assert.Equal(soundEncodes + 3, w.Server.SoundChunkEncodes);
        Assert.Equal(w.Block("stone").Value, BlockIn(LastChunk(w, over, coord), built));
        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, over, coord), cell));
    }

    [Fact]
    public void AChangeOfSide_ResendsOnlyTheChunksThatCarrySoundCells()
    {
        using var w = World("sound_side_change");
        var builder = Join(w, "Builder", 2.5f, overThere: true);
        var walker = w.Player; // on the real side for now
        var cell = OnPad(w, 3, 3);
        var sung = WorldConstants.WorldToChunk(cell);
        var plain = new[] { new ChunkCoord(sung.X, sung.Y + 2, sung.Z), new ChunkCoord(sung.X, sung.Y + 3, sung.Z) };
        Assert.True(w.Server.TryShapeSound(builder, cell, 2, out _));
        w.Server.StreamChunkForTest(walker, sung);
        w.Server.StreamChunkForTest(walker, plain[0]);
        w.Server.StreamChunkForTest(walker, plain[1]);
        Assert.Equal(0, BlockIn(LastChunk(w, walker, sung), cell));

        w.Transport.Sent.Clear();
        walker.OtherSide = true;
        w.Server.SoundLayerSideChangedForTest(walker);

        var crossed = Assert.Single(w.Transport.To<ChunkDataMessage>(walker));
        Assert.Equal((sung.X, sung.Y, sung.Z), (crossed.Cx, crossed.Cy, crossed.Cz));
        Assert.Equal(SoundId(w), BlockIn(crossed, cell));

        w.Transport.Sent.Clear();
        walker.OtherSide = false;
        w.Server.SoundLayerSideChangedForTest(walker);

        var back = Assert.Single(w.Transport.To<ChunkDataMessage>(walker));
        Assert.Equal((sung.X, sung.Y, sung.Z), (back.Cx, back.Cy, back.Cz));
        Assert.Equal(0, BlockIn(back, cell)); // the real chunk again: the sound block is gone from the client
    }

    [Fact]
    public void TheCap_IsPerPlayerAndWorld_AndOneLineSaysSo()
    {
        using var w = World("sound_cap");
        var singer = w.Player;
        string id = singer.State.PlayerId;
        var other = Join(w, "Other", 2.5f, overThere: true);
        // The layer is filled while the singer is on the real side: nothing is sent, the cells are the singer's all the same.
        int n = 0;
        for (int y = 20; n < OtherSideRules.SoundBlockCap; y++)
            for (int x = -10; x < 10 && n < OtherSideRules.SoundBlockCap; x++)
                for (int z = -10; z < 10 && n < OtherSideRules.SoundBlockCap; z++, n++)
                {
                    w.Server.PutSoundCellForTest(OnPad(w, x, z, y), OtherSideRules.ToneGlow[n % 8], id);
                }

        singer.OtherSide = true;
        Assert.Equal((OtherSideRules.SoundBlockCap, OtherSideRules.SoundBlockCap), w.Server.SoundBlockCountForTest(singer));
        Assert.Equal((0, OtherSideRules.SoundBlockCap), w.Server.SoundBlockCountForTest(other));

        Assert.False(w.Server.TryShapeSound(singer, OnPad(w, 3, 3), 0, out string refusal));
        Assert.Equal(SvGameServer.SoundCapKey, refusal);
        Assert.True(w.Server.TryShapeSound(other, OnPad(w, 3, 3), 0, out _), "the cap is each player's own");

        Assert.True(w.Server.TryDissolveSound(singer, OnPad(w, -10, -10, 20), out _));
        Assert.True(w.Server.TryShapeSound(singer, OnPad(w, 4, 3), 0, out refusal), refusal);
        Assert.Equal(OtherSideRules.SoundBlockCap, w.Server.SoundBlocksOf(id));
    }

    [Fact]
    public void OnlyTheBuilder_TheirAllies_AndAdmins_Dissolve()
    {
        using var w = World("sound_owner");
        var builder = w.Player;
        builder.OtherSide = true;
        var stranger = Join(w, "Stranger", 2.5f, overThere: true);
        var admin = Join(w, "Warden", 4.5f, overThere: true);
        admin.State.Role = PlayerRole.Admin;
        var a = OnPad(w, 3, 3);
        var b = OnPad(w, 4, 3);
        Assert.True(w.Server.TryShapeSound(builder, a, 0, out _));
        Assert.True(w.Server.TryShapeSound(builder, b, 0, out _));

        Assert.False(w.Server.TryDissolveSound(stranger, a, out string refusal));
        Assert.Equal(SvGameServer.SoundNotYoursKey, refusal);
        Assert.True(w.Server.IsSoundBlock(a));

        w.Server.RequestAllianceForTest(builder.State.PlayerId, stranger.State.PlayerId);
        w.Server.RespondAllianceForTest(stranger.State.PlayerId, builder.State.PlayerId, accept: true);
        w.Transport.Sent.Clear();
        Assert.True(w.Server.TryDissolveSound(stranger, a, out refusal), refusal);
        Assert.False(w.Server.IsSoundBlock(a));
        var gone = Assert.Single(w.Transport.To<BlockChanged>(builder));
        Assert.Equal((a.X, a.Y, a.Z, (ushort)0), (gone.X, gone.Y, gone.Z, gone.Block));
        Assert.Contains(w.Transport.To<SoundFx>(builder), f => f.SoundId == SvGameServer.SoundDissolveFx);

        Assert.True(w.Server.TryDissolveSound(admin, b, out refusal), refusal);
        Assert.Equal(0, w.Server.SoundBlocksOf(builder.State.PlayerId));
        Assert.False(w.Server.TryDissolveSound(admin, b, out refusal), "nothing stands there any more");
        Assert.Equal(SvGameServer.SoundBlockedKey, refusal);
    }

    [Fact]
    public void NoSoundBlock_InTheBaseOfAPlayerWhoIsNoAlly()
    {
        using var w = World("sound_base");
        w.FoundBase();
        var owner = w.Player;
        owner.OtherSide = true;
        var stranger = Join(w, "Stranger", 2.5f, overThere: true);
        var inside = OnPad(w, 3, 3);
        var outside = OnPad(w, WorldConstants.BaseZoneRadius + 2, 0);

        Assert.False(w.Server.TryShapeSound(stranger, inside, 0, out string refusal));
        Assert.Equal(SvGameServer.SoundBlockedKey, refusal);
        Assert.True(w.Server.TryShapeSound(stranger, outside, 0, out refusal), refusal);
        Assert.True(w.Server.TryShapeSound(owner, inside, 0, out refusal), refusal);

        w.Server.RequestAllianceForTest(owner.State.PlayerId, stranger.State.PlayerId);
        w.Server.RespondAllianceForTest(stranger.State.PlayerId, owner.State.PlayerId, accept: true);
        Assert.True(w.Server.TryShapeSound(stranger, OnPad(w, 4, 3), 0, out refusal), refusal);
    }

    [Fact]
    public void SoundStandsOnlyInRealAir_InsideTheBuildBand_NotInABody_AndOnlyOverThere()
    {
        using var w = World("sound_rules");
        var singer = w.Player;
        singer.OtherSide = true;
        var other = Join(w, "Other", 2.5f, overThere: true);
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var free = OnPad(w, 3, 3);
        var feet = singer.State.Position.ToBlock();
        var otherFeet = other.State.Position.ToBlock();

        string Refused(PlayerSession who, Vector3i cell)
        {
            Assert.False(w.Server.TryShapeSound(who, cell, 0, out string key));
            Assert.False(w.Server.IsSoundBlock(cell));
            return key;
        }

        Assert.Equal(SvGameServer.SoundBlockedKey, Refused(singer, new Vector3i(free.X, w.PadY, free.Z))); // a real block
        Assert.Equal(SvGameServer.SoundBlockedKey, Refused(singer, new Vector3i(free.X, 5000, free.Z)));   // above the band
        Assert.Equal(SvGameServer.SoundBlockedKey, Refused(singer, new Vector3i(feet.X, feet.Y + 1, feet.Z))); // the own head
        Assert.Equal(SvGameServer.SoundBlockedKey, Refused(singer, otherFeet));                             // somebody's feet
        Assert.Equal(SvGameServer.SoundBlockedKey, Refused(real, free));                                    // not over there

        Assert.True(w.Server.TryShapeSound(singer, feet, 0, out string refusal), refusal); // under oneself: lifts the builder
        Assert.True(w.Server.TryShapeSound(singer, free, 0, out refusal), refusal);
        Assert.False(w.Server.TryShapeSound(other, free, 5, out refusal)); // a cell holds one sound block
        Assert.Equal(SvGameServer.SoundBlockedKey, refusal);
        Assert.Equal(OtherSideRules.ToneGlow[0], w.Server.SoundGlowAt(free));
    }

    /// <summary>What weather and nature write goes through <c>ServerWorld.SetBlock</c> with no owner — settling snow,
    /// flowing water, falling sand. The test writes the same way.</summary>
    [Theory]
    [InlineData("snow")]
    [InlineData("water")]
    [InlineData("sand")]
    public void ARealBlockNobodyBuilt_OnlyHidesTheSoundBlock_AndItShowsAgainWhenTheCellIsAir(string block)
    {
        using var w = World("sound_hidden_" + block);
        var singer = w.Player;
        singer.OtherSide = true;
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var cell = OnPad(w, 3, 3);
        var coord = WorldConstants.WorldToChunk(cell);
        Assert.True(w.Server.TryShapeSound(singer, cell, 6, out _));
        w.Transport.Sent.Clear();

        w.Server.World.SetBlock(cell, w.Block(block));

        Assert.True(w.Server.IsSoundBlock(cell), "hidden, not dissolved");
        Assert.Equal(1, w.Server.SoundBlocksOf(singer.State.PlayerId)); // a hidden block still counts toward the cap
        Assert.Equal(new[] { cell }, w.Server.SoundCellsNear(new Vector3f(cell.X, cell.Y, cell.Z), 3));
        Assert.Equal(0, w.Server.SoundReappearQueuedForTest);
        Assert.Empty(w.Transport.To<SoundFx>(singer)); // no dissolve cue
        w.Server.StreamChunkForTest(singer, coord);
        Assert.Equal(w.Block(block).Value, BlockIn(LastChunk(w, singer, coord), cell)); // the real block shows over there too
        Assert.DoesNotContain(SoundId(w), LastChunk(w, singer, coord).DecodeBlocks(WorldConstants.BlocksPerChunk)!);
        Assert.False(w.Server.TryShapeSound(singer, cell, 0, out _), "the cell is taken — by the hidden block and by the real one");

        // The snow melts, the water runs off: the real change goes out first, the sound block right after it (the flush).
        w.Transport.Sent.Clear();
        w.Server.World.SetBlock(cell, BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
        Assert.Equal(1, w.Server.SoundReappearQueuedForTest);
        Assert.Empty(w.Transport.To<BlockChanged>(singer));
        w.Server.FlushSoundLayerForTest(singer);

        var back = Assert.Single(w.Transport.To<BlockChanged>(singer));
        Assert.Equal((cell.X, cell.Y, cell.Z, SoundId(w), OtherSideRules.ToneGlow[6]), (back.X, back.Y, back.Z, back.Block, back.Glow));
        Assert.Empty(w.Transport.To(real));
        Assert.Equal(0, w.Server.SoundReappearQueuedForTest);
        w.Server.StreamChunkForTest(singer, coord);
        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, singer, coord), cell));
        Assert.Equal(OtherSideRules.ToneGlow[6], GlowIn(LastChunk(w, singer, coord), cell));
    }

    [Fact]
    public void TheRealSideWins_ABlockAPlayerBuilds_DissolvesTheSoundBlockForGood()
    {
        using var w = World("sound_real_wins");
        var singer = w.Player;
        singer.OtherSide = true;
        var real = Join(w, "Realsider", 4.5f, overThere: false);
        var built = OnPad(w, 3, 3);
        var mined = OnPad(w, 4, 3);
        Assert.True(w.Server.TryShapeSound(singer, built, 0, out _));
        Assert.True(w.Server.TryShapeSound(singer, mined, 0, out _));
        w.Transport.Sent.Clear();

        w.Set(built.X, built.Y, built.Z, "stone"); // a block with a player's name on it

        Assert.False(w.Server.IsSoundBlock(built));
        Assert.Equal(1, w.Server.SoundBlocksOf(singer.State.PlayerId));
        var told = Assert.Single(w.Transport.To<BlockChanged>(singer));
        Assert.Equal((built.X, built.Y, built.Z, w.Block("stone").Value), (told.X, told.Y, told.Z, told.Block));
        Assert.Contains(w.Transport.To<SoundFx>(singer), f => f.SoundId == SvGameServer.SoundDissolveFx);
        Assert.Empty(w.Transport.To(real));

        // Snow hid the other one; a player digging the snow away writes air — that dissolves nothing, the block shows again.
        w.Server.World.SetBlock(mined, w.Block("snow"));
        w.Server.World.SetBlock(mined, BlocksBeyondTheStars.Shared.Primitives.BlockId.Air, owner: NpcLifeWorld.Owner);
        w.Server.RemoveBlockForTest(built.X, built.Y, built.Z); // and the built block goes again
        w.Server.FlushSoundLayerForTest(singer);
        Assert.True(w.Server.IsSoundBlock(mined));
        Assert.False(w.Server.IsSoundBlock(built), "dissolved for good");

        w.Restart();
        Assert.True(w.Server.IsSoundBlock(mined));
        Assert.False(w.Server.IsSoundBlock(built));
        Assert.Equal(1, w.Server.SoundBlocksOf(w.Player.State.PlayerId));
    }

    [Fact]
    public void WithNothingQueued_TheTickDoesNoSoundWork_AndAirOverASoundBlock_IsToldAgainOnce()
    {
        using var w = World("sound_flush");
        var cell = OnPad(w, 3, 3);
        w.Server.PutSoundCellForTest(cell, OtherSideRules.ToneGlow[1], NpcLifeWorld.Owner);
        for (int i = 0; i < 20; i++)
        {
            w.Server.TickForTest(0.05);
        }

        Assert.Equal(0, w.Server.SoundReappearFlushes);

        // A tool that "clears" a cell that was air all along: the server keeps the sound block and tells it again.
        w.Server.World.SetBlock(cell, BlocksBeyondTheStars.Shared.Primitives.BlockId.Air);
        Assert.Equal(1, w.Server.SoundReappearQueuedForTest);
        w.Server.TickForTest(0.05);
        Assert.Equal(1, w.Server.SoundReappearFlushes);
        Assert.Equal(0, w.Server.SoundReappearQueuedForTest);
        Assert.True(w.Server.IsSoundBlock(cell));

        w.Server.TickForTest(0.05);
        Assert.Equal(1, w.Server.SoundReappearFlushes);
    }

    [Fact]
    public void TheLayer_SurvivesARestart()
    {
        using var w = World("sound_restart");
        var singer = w.Player;
        singer.OtherSide = true;
        var other = Join(w, "Other", 2.5f, overThere: true);
        string otherId = other.State.PlayerId;
        var a = OnPad(w, 3, 3);
        var b = OnPad(w, 4, 3);
        var gone = OnPad(w, 5, 3);
        Assert.True(w.Server.TryShapeSound(singer, a, 2, out _));
        Assert.True(w.Server.TryShapeSound(other, b, 7, out _));
        Assert.True(w.Server.TryShapeSound(singer, gone, 4, out _));
        Assert.True(w.Server.TryDissolveSound(singer, gone, out _));

        w.Restart();

        Assert.True(w.Server.IsSoundBlock(a));
        Assert.True(w.Server.IsSoundBlock(b));
        Assert.False(w.Server.IsSoundBlock(gone));
        Assert.Equal(OtherSideRules.ToneGlow[2], w.Server.SoundGlowAt(a));
        Assert.Equal(OtherSideRules.ToneGlow[7], w.Server.SoundGlowAt(b));
        Assert.Equal(1, w.Server.SoundBlocksOf(w.Player.State.PlayerId));
        Assert.Equal(1, w.Server.SoundBlocksOf(otherId));
        Assert.True(w.Server.World.GetBlock(a).IsAir);

        w.Player.OtherSide = true;
        var coord = WorldConstants.WorldToChunk(a);
        w.Server.StreamChunkForTest(w.Player, coord);
        Assert.Equal(SoundId(w), BlockIn(LastChunk(w, w.Player, coord), a));
        Assert.Equal(OtherSideRules.ToneGlow[7], GlowIn(LastChunk(w, w.Player, coord), b));
    }
}
