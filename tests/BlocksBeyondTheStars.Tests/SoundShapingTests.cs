// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3908 (server half) — the Resonance Blaster on the other side: the use key shapes a sound block on the near side of
/// the aimed face (a real block's or a sound block's) or in the open air, the mine key (<c>UseGadgetIntent.Alt</c>)
/// dissolves the sound block aimed at, real blocks are never touched, nothing a real-side player could notice goes out,
/// and shaping costs no suit energy at a 0.2 s cadence. The melody bar is the palette (tone → glow). On the
/// real side the blaster is unchanged. Also: the sound layer of a station world, kept across the world's unload.
/// </summary>
public sealed class SoundShapingTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_soundshape_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 12);

    /// <summary>The blaster in the hand, the suit's energy EMPTY (shaping must not need any), over there.</summary>
    private static PlayerSession Singer(NpcLifeWorld w)
    {
        w.Player.State.Inventory.SetSlot(0, new ItemStack(SoundersIds.ResonanceBlaster, 1));
        w.Player.State.SelectedHotbarSlot = 0;
        w.Player.State.SuitEnergy = 0f;
        w.Player.OtherSide = true;
        return w.Player;
    }

    /// <summary>The use key (shape) — or, with <paramref name="alt"/>, the mine key (dissolve) — as the intent carries it.</summary>
    private static void Pulse(NpcLifeWorld w, Vector3f at, bool alt = false)
        => w.Server.UseGadgetForTest(w.Player.State.PlayerId, SoundersIds.ResonanceBlaster, at, alt);

    /// <summary>The next pulse is due: the 0.2 s cadence has passed.</summary>
    private static void Beat(NpcLifeWorld w) => w.Server.AdvanceUptimeForTest(OtherSideRules.ShapeCooldownSeconds + 0.01);

    /// <summary>The middle of the TOP face of a cell — where a ray from the eyes above ends on it.</summary>
    private static Vector3f TopFace(Vector3i cell) => new(cell.X + 0.5f, cell.Y + 1f, cell.Z + 0.5f);

    [Fact]
    public void TheUseKey_AlwaysShapes_OnARealBlockAndOnASoundBlock_SoAWallGrowsBlockByBlock()
    {
        using var w = World("blaster_shape");
        var singer = Singer(w);
        var real = SoundLayerTests.Join(w, "Realsider", 4.5f, overThere: false);
        var ground = new Vector3i(NpcLifeWorld.Cx + 2, w.PadY, NpcLifeWorld.Cz);
        var first = new Vector3i(ground.X, ground.Y + 1, ground.Z);
        var onTop = new Vector3i(first.X, first.Y + 1, first.Z);
        var beside = new Vector3i(first.X - 1, first.Y, first.Z);
        w.Transport.Sent.Clear();

        Pulse(w, TopFace(ground));

        Assert.True(w.Server.IsSoundBlock(first), "the sound block stands on the near side of the face");
        Assert.Equal(OtherSideRules.ToneGlow[0], w.Server.SoundGlowAt(first)); // no tone played yet: tone 0
        Assert.Equal(w.Block("stone").Value, w.Server.World.GetBlock(ground).Value); // the real block is untouched
        Assert.True(w.Server.World.GetBlock(first).IsAir);
        Assert.Equal(0f, singer.State.SuitEnergy); // no energy needed, none taken

        // 0.2 s cadence: a pulse right away is swallowed, one a moment later lands — on the sound block's TOP face.
        Pulse(w, TopFace(first));
        Assert.False(w.Server.IsSoundBlock(onTop));
        Beat(w);
        Pulse(w, TopFace(first));
        Assert.True(w.Server.IsSoundBlock(onTop), "a pulse on a sound block's face builds on it");
        Assert.True(w.Server.IsSoundBlock(first), "and dissolves nothing");

        // ... and on its SIDE face (the one toward the singer): the wall grows sideways.
        Beat(w);
        Pulse(w, new Vector3f(first.X, first.Y + 0.5f, first.Z + 0.5f));
        Assert.True(w.Server.IsSoundBlock(beside));
        Assert.Equal(3, w.Server.SoundBlocksOf(singer.State.PlayerId));

        Assert.Empty(w.Transport.To(real)); // no pulse sound, no gadget effect, no block change on the real side
        Assert.DoesNotContain(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId.StartsWith("blaster_pulse", StringComparison.Ordinal));
        Assert.DoesNotContain(w.Transport.To<ActionRejected>(singer), r => r.Action == "gadget");
    }

    [Fact]
    public void TheMineKey_DissolvesTheSoundBlockAimedAt_AndDoesNothingAnywhereElse()
    {
        using var w = World("blaster_dissolve");
        var singer = Singer(w);
        string id = singer.State.PlayerId;
        var real = SoundLayerTests.Join(w, "Realsider", 4.5f, overThere: false);
        var ground = new Vector3i(NpcLifeWorld.Cx + 2, w.PadY, NpcLifeWorld.Cz);
        var first = new Vector3i(ground.X, ground.Y + 1, ground.Z);
        Pulse(w, TopFace(ground));
        Assert.True(w.Server.IsSoundBlock(first));
        Beat(w);
        w.Transport.Sent.Clear();

        // On a real block, and into the open air: nothing happens — no line, no cadence spent, nothing shaped.
        var elsewhere = new Vector3i(ground.X, ground.Y, ground.Z + 3);
        Pulse(w, TopFace(elsewhere), alt: true);
        Pulse(w, new Vector3f(singer.State.Position.X + 3f, singer.State.Position.Y + 1.5f, singer.State.Position.Z), alt: true);
        Assert.Empty(w.Transport.To<ActionRejected>(singer));
        Assert.Equal(0.0, w.Server.GadgetCooldownForTest(id, SoundersIds.ResonanceBlaster));
        Assert.Equal(1, w.Server.SoundBlocksOf(id));
        Assert.Equal(w.Block("stone").Value, w.Server.World.GetBlock(elsewhere).Value);

        Pulse(w, TopFace(first), alt: true);

        Assert.False(w.Server.IsSoundBlock(first), "the mine key on a sound block dissolves it");
        Assert.Equal(0, w.Server.SoundBlocksOf(id));
        Assert.Contains(w.Transport.To<SoundFx>(singer), f => f.SoundId == SvGameServer.SoundDissolveFx);
        Assert.Empty(w.Transport.To(real));

        // Somebody else's sound block: one line, and the blaster rests a second. (The first joiner of a world is its
        // admin, and an admin dissolves anything — so the singer is an ordinary player for this.)
        singer.State.Role = PlayerRole.Player;
        var theirs = new Vector3i(ground.X, ground.Y + 1, ground.Z + 2);
        w.Server.PutSoundCellForTest(theirs, OtherSideRules.ToneGlow[2], "Stranger");
        Beat(w);
        w.Transport.Sent.Clear();
        Pulse(w, TopFace(theirs), alt: true);
        var told = Assert.Single(w.Transport.To<ActionRejected>(singer));
        Assert.Equal(("gadget", "@" + SvGameServer.SoundNotYoursKey), (told.Action, told.Reason));
        Assert.True(w.Server.IsSoundBlock(theirs));
        Assert.True(w.Server.GadgetCooldownForTest(id, SoundersIds.ResonanceBlaster) > OtherSideRules.ShapeCooldownSeconds);
    }

    [Fact]
    public void APulseIntoTheOpen_SingsABlockIntoTheAir_WithinTheBlastersReach_AndARefusalIsOneLine()
    {
        using var w = World("blaster_air");
        var singer = Singer(w);
        var at = singer.State.Position;
        var air = new Vector3f(at.X + 3f, at.Y + 1.5f, at.Z); // straight ahead of the eyes, the middle of a cell of air
        var cell = air.ToBlock();

        Pulse(w, air);
        Assert.True(w.Server.IsSoundBlock(cell), "a bridge has to start somewhere");

        // A point far out in the open (a long ray, a modified client): pulled back to the blaster's reach, 8 blocks.
        float reach = Content.Value.GetItem(SoundersIds.ResonanceBlaster)!.Tool!.Range;
        var far = new Vector3f(at.X + 60f, at.Y + 1.5f + 15f, at.Z); // up and away, nothing in the way
        float len = MathF.Sqrt((60f * 60f) + (15f * 15f));
        var clamped = new Vector3f(at.X + (60f * reach / len), at.Y + 1.5f + (15f * reach / len), at.Z).ToBlock();
        Beat(w);
        Pulse(w, far);
        Assert.True(w.Server.IsSoundBlock(clamped), "sung where the blaster's reach ends, not 60 blocks away");
        Assert.Equal(2, w.Server.SoundBlocksOf(singer.State.PlayerId));

        // A real block's face whose near side is the singer's own head: refused, told once, and held for a second.
        var feet = singer.State.Position.ToBlock();
        var roof = new Vector3i(feet.X, feet.Y + 2, feet.Z);
        w.Set(roof.X, roof.Y, roof.Z, "stone");
        Beat(w);
        w.Transport.Sent.Clear();
        var underRoof = new Vector3f(roof.X + 0.5f, roof.Y, roof.Z + 0.5f); // the roof's bottom face, seen from below

        Pulse(w, underRoof);
        Beat(w);
        Pulse(w, underRoof); // still held by the refusal's cooldown

        var told = Assert.Single(w.Transport.To<ActionRejected>(singer));
        Assert.Equal(("gadget", "@" + SvGameServer.SoundBlockedKey), (told.Action, told.Reason));
        Assert.False(w.Server.IsSoundBlock(new Vector3i(feet.X, feet.Y + 1, feet.Z)));
    }

    [Fact]
    public void TheMelodyBar_IsThePalette_TheToneSetsTheGlow()
    {
        using var w = World("blaster_palette");
        var singer = Singer(w);
        var ground = new Vector3i(NpcLifeWorld.Cx + 2, w.PadY, NpcLifeWorld.Cz);
        var onIt = new Vector3i(ground.X, ground.Y + 1, ground.Z);
        Assert.Equal(0, w.Server.SoundToneOf(singer));

        // The real intent path: a tone of the melody bar, as the client sends it.
        w.Server.HandlePayloadForTest(singer.ConnectionId, NetCodec.Encode(new MelodyNoteIntent { Tone = 5 }));
        Assert.Equal(5, w.Server.SoundToneOf(singer));
        w.Transport.Sent.Clear();
        Pulse(w, TopFace(ground));

        Assert.Equal(OtherSideRules.ToneGlow[5], w.Server.SoundGlowAt(onIt));
        Assert.Equal(5, OtherSideRules.ToneOfGlow(w.Server.SoundGlowAt(onIt)));
        var cue = Assert.Single(w.Transport.To<SoundFx>(singer), f => f.SoundId == SvGameServer.SoundShapeFx);
        Assert.Equal(OtherSideRules.PitchOfTone(5), cue.Pitch, 4);

        // "Stop" (-1) and a tone outside the bar leave the palette alone.
        w.Server.RememberSoundToneForTest(singer, -1);
        w.Server.RememberSoundToneForTest(singer, 8);
        Assert.Equal(5, w.Server.SoundToneOf(singer));
    }

    [Fact]
    public void ThePalette_IsEightRisingBrightnessSteps_AndReadsItsToneBack()
    {
        Assert.Equal(8, OtherSideRules.ToneGlow.Length);
        int last = -1;
        for (int tone = 0; tone < OtherSideRules.ToneGlow.Length; tone++)
        {
            int glow = OtherSideRules.ToneGlow[tone];
            int level = (((glow >> 16) & 0xFF) * 299 + ((glow >> 8) & 0xFF) * 587 + (glow & 0xFF) * 114) / 1000;
            Assert.True(glow != 0, "0 means no glow on the wire");
            Assert.True(level >= last + 20, $"tone {tone} is clearly brighter than the one below ({level} vs {last})");
            Assert.Equal(tone, OtherSideRules.ToneOfGlow(glow));
            Assert.Equal(glow, OtherSideRules.GlowOfTone(tone));
            last = level;
        }

        Assert.Equal(0, OtherSideRules.ToneOfGlow(0x000000));      // not in the palette: the nearest in brightness
        Assert.Equal(7, OtherSideRules.ToneOfGlow(0xFFFFFF));
        Assert.Equal(OtherSideRules.ToneGlow[0], OtherSideRules.GlowOfTone(-3));
        Assert.Equal(OtherSideRules.ToneGlow[7], OtherSideRules.GlowOfTone(99));
        Assert.Equal(1f, OtherSideRules.PitchOfTone(0), 4);
        Assert.Equal(2f, OtherSideRules.PitchOfTone(7), 4);        // C' is the octave
        Assert.True(OtherSideRules.ShapeCooldownSeconds < SvGameServer.ResonanceBlasterCooldown);
    }

    [Fact]
    public void OnTheRealSide_TheBlasterIsWhatItWas_AndShapesNothing()
    {
        using var w = World("blaster_real_side");
        var player = Singer(w);
        player.OtherSide = false;
        player.State.SuitEnergy = 100f;
        var ground = new Vector3i(NpcLifeWorld.Cx + 2, w.PadY, NpcLifeWorld.Cz);
        float cost = Content.Value.GetItem(SoundersIds.ResonanceBlaster)!.Tool!.EnergyPerUse;

        Pulse(w, TopFace(ground));

        Assert.False(w.Server.IsSoundBlock(new Vector3i(ground.X, ground.Y + 1, ground.Z)));
        Assert.Equal(0, w.Server.SoundChunkCountForTest);
        Assert.Equal(100f - cost, player.State.SuitEnergy, 3); // the sonic push, at its price
        Assert.Contains(w.Transport.Messages().OfType<SoundFx>(), f => f.SoundId == "blaster_pulse_1");

        // Its 1.2 s cadence, not the other side's 0.2 s.
        Beat(w);
        Pulse(w, TopFace(ground));
        Assert.Equal(100f - cost, player.State.SuitEnergy, 3);

        // The alternative action means nothing on the real side: the pulse is the sonic push, at its price.
        w.Server.AdvanceUptimeForTest(SvGameServer.ResonanceBlasterCooldown);
        Pulse(w, TopFace(ground), alt: true);
        Assert.Equal(100f - (2 * cost), player.State.SuitEnergy, 3);
        Assert.Equal(0, w.Server.SoundChunkCountForTest);
    }

    [Fact]
    public void AStationWorld_HasItsSoundLayerToo_AndKeepsItAcrossTheWorldsUnload()
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "sound_station"));
        var config = new ServerConfig { WorldName = "sound_station", Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        config.Rules.FreeSpaceFlight = true;
        var transport = new RecordingTransport();
        var server = new SvGameServer(config, Content.Value, transport, repo);
        server.Start();
        try
        {
            var p = server.AddLocalPlayer("Justus");
            string pid = p.State.PlayerId;
            Board(server, pid);
            string station = p.CurrentLocationId;
            Assert.NotEqual(server.Metadata.ActiveLocationId, station);
            var cell = FreeCellNear(server, p);
            p.OtherSide = true;

            Assert.True(server.TryShapeSound(p, cell, 4, out string refusal), refusal);
            Assert.Equal(cell, Assert.Single(repo.ListSoundCells(station).Select(c => new Vector3i(c.X, c.Y, c.Z))));
            Assert.Empty(repo.ListSoundCells(server.Metadata.ActiveLocationId)); // the layer is per location
            server.StreamChunkForTest(p, WorldConstants.WorldToChunk(cell));
            Assert.Contains(Content.Value.GetBlock(SoundersIds.SoundBlock)!.NumericId.Value,
                transport.To<ChunkDataMessage>(p).Last().DecodeBlocks(WorldConstants.BlocksPerChunk)!);

            // Leaving drops the station world from memory; boarding again loads it — and its sound layer — afresh.
            p.OtherSide = false;
            server.LeaveStation(pid);
            Assert.False(server.InStation(pid));
            Board(server, pid);
            Assert.Equal(station, p.CurrentLocationId);

            Assert.True(server.IsSoundBlock(cell));
            Assert.Equal(OtherSideRules.ToneGlow[4], server.SoundGlowAt(cell));
            Assert.Equal(1, server.SoundBlocksOf(pid));
        }
        finally
        {
            server.Stop();
            repo.Dispose();
        }
    }

    /// <summary>Flies the player up to the start system's derelict station and boards it.</summary>
    private static void Board(SvGameServer server, string pid)
    {
        string id = DerelictPlacer.BodyId(server.Galaxy.FindBody(server.Metadata.ActiveLocationId)!.SystemId, 0);
        if (!server.InSpace(pid))
        {
            server.EnterSpace(pid);
        }

        var contact = server.SpaceEntitiesFor(pid).First(e => e.Id == id);
        server.ShipMove(pid, contact.Position.X, contact.Position.Y, contact.Position.Z - (server.DerelictLayoutForTest(id)!.Length / 2f + 8f));
        server.BoardStation(pid, id);
        Assert.True(server.InStation(pid), "the derelict should be boardable by anyone");
    }

    /// <summary>An air cell a few blocks from the player that is nobody's head or feet.</summary>
    private static Vector3i FreeCellNear(SvGameServer server, PlayerSession p)
    {
        var feet = p.State.Position.ToBlock();
        for (int d = 2; d < 8; d++)
        {
            foreach (var cell in new[] { new Vector3i(feet.X + d, feet.Y, feet.Z), new Vector3i(feet.X, feet.Y, feet.Z + d), new Vector3i(feet.X, feet.Y + d, feet.Z) })
            {
                if (server.World.GetBlock(cell).IsAir)
                {
                    return cell;
                }
            }
        }

        throw new InvalidOperationException("no free cell near the player aboard the station");
    }
}
