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
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The stage of the other-side tests: a live jungle world with a stone pad high over the trees, two rune gates and one
/// lone rune stone on it, a recording transport, and players on foot who can be dressed for the crossing.
/// </summary>
internal sealed class OtherSideStage : IDisposable
{
    public const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_otherside_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content;
    private readonly ServerConfig _config;

    public OtherSideStage(GameContent content, string name, Action<ServerConfig>? tune = null)
    {
        _content = content;
        Repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        tune?.Invoke(_config);
        Server = new SvGameServer(_config, content, Transport, Repo);
        Server.Start();
        SoundersStage.Pacify(Server);
        PadY = SoundersStage.Pad(Server, content, Cx, Cz, 24);
        var rune = content.GetBlock("rune_stone")!.NumericId;
        foreach (var cell in GateA.Concat(GateB).Append(LoneStone))
        {
            Server.World.SetBlock(cell, rune);
        }
    }

    public SqliteWorldRepository Repo { get; }

    public RecordingTransport Transport { get; private set; } = new();

    public SvGameServer Server { get; private set; }

    public int PadY { get; }

    /// <summary>Three rune stones in a row, three blocks north of the pad's middle: a gate.</summary>
    public Vector3i[] GateA => new[] { new Vector3i(Cx - 2, PadY + 1, Cz + 3), new Vector3i(Cx, PadY + 1, Cz + 3), new Vector3i(Cx + 2, PadY + 1, Cz + 3) };

    /// <summary>A second gate, far from the first.</summary>
    public Vector3i[] GateB => new[] { new Vector3i(Cx - 16, PadY + 1, Cz - 14), new Vector3i(Cx - 14, PadY + 1, Cz - 14), new Vector3i(Cx - 12, PadY + 1, Cz - 14) };

    /// <summary>One rune stone on its own: it only sings.</summary>
    public Vector3i LoneStone => new(Cx + 16, PadY + 1, Cz - 12);

    /// <summary>Where a player stands to use <see cref="GateA"/>'s middle stone.</summary>
    public Vector3f AtGateA => new(Cx + 0.5f, PadY + 1, Cz + 0.5f);

    /// <summary>Where a player stands to use <see cref="GateB"/>'s middle stone.</summary>
    public Vector3f AtGateB => new(Cx - 13.5f, PadY + 1, Cz - 11.5f);

    /// <summary>Where a player stands to use the lone stone.</summary>
    public Vector3f AtLoneStone => new(Cx + 16.5f, PadY + 1, Cz - 9.5f);

    /// <summary>A player on foot at <paramref name="at"/> whose client can show the other side (a local player's does).</summary>
    public PlayerSession Player(string name, Vector3f at) => SoundersStage.OnFoot(Server, name, at);

    /// <summary>The obsidian chest worn, the crossing disc in its socket.</summary>
    public static void Dress(PlayerSession s)
    {
        s.State.Equipment.SetSlot((int)EquipSlot.Chest, new ItemStack(SoundersIds.ObsidianChest, 1));
        s.State.Equipment.SetSlot((int)EquipSlot.Disc, new ItemStack(SoundersIds.DiscCrossing, 1));
    }

    /// <summary>The use key on a rune stone, with the cooldowns of the stone and of the threshold over.</summary>
    public void Use(PlayerSession s, Vector3i stone)
    {
        Tick(3.0);
        Server.ResetRuneCooldownForTest(s);
        Server.UseBlockForTest(s, stone);
    }

    /// <summary>Dressed and across at gate A.</summary>
    public PlayerSession Crossed(string name)
    {
        var s = Player(name, AtGateA);
        Dress(s);
        Use(s, GateA[1]);
        Assert.True(s.OtherSide, name + " should be over there");
        return s;
    }

    public void Tick(double seconds)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 5.0); i++)
        {
            Server.TickForTest(0.2);
        }
    }

    /// <summary>An intent through the real receive path (the gates of <c>Dispatch</c> included).</summary>
    public void Send(PlayerSession s, object intent) => Server.HandlePayloadForTest(s.ConnectionId, NetCodec.Encode(intent));

    /// <summary>A second server on the same save — what a restart after a crash sees.</summary>
    public SvGameServer Restart()
    {
        Transport = new RecordingTransport();
        Server = new SvGameServer(_config, _content, Transport, Repo);
        Server.Start();
        return Server;
    }

    public void Dispose()
    {
        Repo.Dispose();
        SoundersStage.Delete(_root);
    }
}

/// <summary>
/// The other side as a player state (#3904, epic #3891): the rune gate and the crossing disc, coming back, what a player
/// may do over there (the allow-list, at the type level and through the real dispatch), no needs and no harm, the entry
/// gate, leaving the game over there, the movers that bring a player back first, and the admin verb.
/// </summary>
public sealed class OtherSideTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    // ---- crossing --------------------------------------------------------------------------------------------------

    [Fact]
    public void Crossing_NeedsTheDiscInTheSocket_AndAGate()
    {
        using var w = new OtherSideStage(Content, "cross");
        var a = w.Player("Kid", w.AtGateA);

        // No disc at all: the stone sings, as it always did, and nothing is said.
        w.Use(a, w.GateA[1]);
        Assert.False(a.OtherSide);
        Assert.NotEmpty(w.Transport.To<NoteSequence>(a));
        Assert.DoesNotContain(w.Transport.TextsTo(a), t => t.StartsWith("@srv.otherside", StringComparison.Ordinal));

        // The disc in the backpack: a gate says where it belongs.
        a.State.Inventory.SetSlot(12, new ItemStack(SoundersIds.DiscCrossing, 1));
        w.Use(a, w.GateA[1]);
        Assert.False(a.OtherSide);
        Assert.Contains("@srv.otherside.no_disc", w.Transport.TextsTo(a));

        // In the socket, but the chest is not worn: still no crossing.
        a.State.Inventory.SetSlot(12, null);
        a.State.Equipment.SetSlot((int)EquipSlot.Disc, new ItemStack(SoundersIds.DiscCrossing, 1));
        w.Use(a, w.GateA[1]);
        Assert.False(a.OtherSide);

        // The chest worn, the disc in its socket, a stone of a gate: across.
        OtherSideStage.Dress(a);
        w.Transport.Sent.Clear();
        var stood = a.State.Position;
        w.Use(a, w.GateA[1]);

        Assert.True(a.OtherSide);
        Assert.Contains(SvGameServer.OtherSideMarkerForTest, a.State.Milestones);
        Assert.Contains("snd:crossed", a.State.Milestones);
        Assert.Contains("@srv.otherside.crossed_first", w.Transport.TextsTo(a));
        Assert.Equal(stood, a.State.Position); // the same world, the same place
        var state = w.Transport.To<OtherSideState>(a).Last();
        Assert.True(state.Over && state.HasGate);
        Assert.Equal(OtherSideReasons.Crossed, state.Reason);
        Assert.Equal(OtherSideStage.Cx + 0.5f, state.GateX, 3); // the middle of the three stones
        Assert.Equal(w.PadY + 1f, state.GateY, 3);
        Assert.Equal(OtherSideStage.Cz + 3.5f, state.GateZ, 3);
        Assert.Equal(new Vector3f(state.GateX, state.GateY, state.GateZ), w.Server.EntryGateOfForTest(a));
        Assert.Contains("Kid", w.Server.PlayersOverThereForTest());
        Assert.Equal(a.State.CurrentLocationId, a.State.OtherSideGateLocation);
    }

    [Fact]
    public void ALoneStone_OnlySings_OnBothSides()
    {
        using var w = new OtherSideStage(Content, "lone");
        var a = w.Player("Kid", w.AtLoneStone);
        OtherSideStage.Dress(a);

        w.Use(a, w.LoneStone);
        Assert.False(a.OtherSide);
        Assert.NotEmpty(w.Transport.To<NoteSequence>(a));
        Assert.DoesNotContain(w.Transport.TextsTo(a), t => t.StartsWith("@srv.otherside", StringComparison.Ordinal));
        Assert.False(w.Server.TryCrossForTest(a, w.LoneStone, out string refusal));
        Assert.Equal("srv.otherside.no_gate", refusal);

        // Over there the lone stone sings as well — and brings nobody back.
        a.State.Position = w.AtGateA;
        w.Use(a, w.GateA[1]);
        Assert.True(a.OtherSide);
        a.State.Position = w.AtLoneStone;
        w.Transport.Sent.Clear();
        w.Use(a, w.LoneStone);
        Assert.True(a.OtherSide);
        Assert.NotEmpty(w.Transport.To<NoteSequence>(a));
        Assert.False(w.Server.TryReturnForTest(a, w.LoneStone, out refusal));
        Assert.Equal("srv.otherside.no_gate", refusal);
    }

    [Fact]
    public void ComingBack_WorksAtAnyGate_WithoutTheDisc()
    {
        using var w = new OtherSideStage(Content, "return");
        var a = w.Crossed("Kid");

        // The disc may be taken out over there: the way back does not need it.
        w.Send(a, new UnequipItemIntent { Slot = (int)EquipSlot.Disc });
        Assert.False(SvGameServer.HasActiveSocketDisc(a.State, SoundersIds.DiscCrossing));

        a.State.Position = w.AtGateB;
        w.Transport.Sent.Clear();
        w.Use(a, w.GateB[1]);

        Assert.False(a.OtherSide);
        Assert.Equal(w.AtGateB, a.State.Position); // where they stand, not where they went in
        Assert.DoesNotContain(SvGameServer.OtherSideMarkerForTest, a.State.Milestones);
        Assert.Contains("snd:crossed", a.State.Milestones); // the first crossing stays
        Assert.Equal(string.Empty, a.State.OtherSideGateLocation);
        Assert.Null(w.Server.EntryGateOfForTest(a));
        var state = w.Transport.To<OtherSideState>(a).Last();
        Assert.False(state.Over || state.HasGate);
        Assert.Equal(OtherSideReasons.Returned, state.Reason);
        Assert.Contains("@srv.otherside.returned", w.Transport.TextsTo(a));
    }

    [Fact]
    public void AHeldUseKey_DoesNotCrossAndComeStraightBack()
    {
        using var w = new OtherSideStage(Content, "held");
        var a = w.Crossed("Kid");

        w.Server.ResetRuneCooldownForTest(a);
        w.Server.UseBlockForTest(a, w.GateA[1]); // the same key press, a moment later
        Assert.True(a.OtherSide);
    }

    [Fact]
    public void AnOlderClient_CannotCross()
    {
        using var w = new OtherSideStage(Content, "old_client");
        var a = w.Player("Kid", w.AtGateA);
        OtherSideStage.Dress(a);
        a.ClientFeatures.Remove(ClientFeatures.OtherSide1);
        Assert.False(a.SupportsOtherSide);

        w.Use(a, w.GateA[1]);

        Assert.False(a.OtherSide);
        Assert.Contains("@srv.otherside.no_client", w.Transport.TextsTo(a));
        Assert.False(w.Server.TryCrossForTest(a, w.GateA[1], out string refusal));
        Assert.Equal("srv.otherside.no_client", refusal);
    }

    [Fact]
    public void NobodyCrosses_Seated_Aboard_OrWatching()
    {
        using var w = new OtherSideStage(Content, "refusals");
        var a = w.Player("Kid", w.AtGateA);
        OtherSideStage.Dress(a);

        a.State.Seated = true;
        Assert.False(w.Server.TryCrossForTest(a, w.GateA[1], out string refusal));
        Assert.Equal("srv.otherside.not_seated", refusal);
        a.State.Seated = false;

        a.State.AboardShip = true;
        Assert.False(w.Server.TryCrossForTest(a, w.GateA[1], out refusal));
        Assert.Equal("srv.otherside.not_here", refusal);
        a.State.AboardShip = false;

        a.Spectating = true;
        Assert.False(w.Server.TryCrossForTest(a, w.GateA[1], out refusal));
        Assert.Equal("srv.otherside.not_now", refusal);
        a.Spectating = false;

        Assert.True(w.Server.TryCrossForTest(a, w.GateA[1], out refusal));
        Assert.Equal(string.Empty, refusal);
    }

    // ---- what a player may do over there -----------------------------------------------------------------------------

    [Fact]
    public void TheAllowList_NamesMovingTalkingAndTheSoundersThings_AndRefusesTheRest()
    {
        object[] allowed =
        {
            new MoveIntent(), new FarTerrainTileRequest(), new SelectHotbarIntent(), new SetJetpackIntent { Active = true },
            new SetGlidingIntent { Active = true }, new SetSeatedIntent { Active = false },
            new ChatIntent(), new DirectChatIntent(), new VoiceFrame(), new PlayerEmoteIntent(),
            new PauseIntent(), new BumpReport(), new CallAdminIntent(), new PlayerReportIntent(), new AdminCommandIntent(),
            new SetAppearanceIntent(), new SetFaceIntent(), new SetBodyPaintIntent(), new SetToolLookIntent(),
            new RequestStarMap(), new CrewActionIntent { Kind = "list" }, new MarkerActionIntent { Kind = "list" },
            new StoryPackIntent(), new CutsceneSeenIntent(),
            new MoveItemIntent(), new EquipItemIntent(), new UnequipItemIntent(),
            new UseBlockIntent(), new TalkToNpcIntent(), new NpcDialogChoiceIntent(), new UseGadgetIntent(), new MelodyNoteIntent(),
        };
        foreach (object intent in allowed)
        {
            Assert.True(SvGameServer.OtherSideMayHandleForTest(intent), intent.GetType().Name + " is refused over there");
        }

        object[] refused =
        {
            new MineBlockIntent(), new PlaceBlockIntent(), new CraftIntent(), new AttackEntityIntent(), new FireWeaponIntent(),
            new LootContainerIntent(), new OpenContainerIntent(), new DoorInteractIntent(), new UseStationIntent(),
            new TradeRequestIntent(), new ConsumeItemIntent(), new DiscardItemIntent(), new ScanIntent(), new TravelIntent(),
            new TeleportToShipIntent(), new ReturnToShipIntent(), new EnterSpaceIntent(), new EnterTrainIntent(),
            new SetSeatedIntent { Active = true }, new CrewActionIntent { Kind = "invite" }, new MarkerActionIntent { Kind = "add" },
            new ToggleStealthIntent(), new CarryBlockIntent(), new ThrowFoodIntent(), new PaintBlockIntent(),
        };
        foreach (object intent in refused)
        {
            Assert.False(SvGameServer.OtherSideMayHandleForTest(intent), intent.GetType().Name + " is allowed over there");
        }

        // The default is "refused": an intent nobody listed — every future one — does not get through.
        Assert.False(SvGameServer.OtherSideMayHandleForTest(new object()));
        Assert.False(SvGameServer.OtherSideMayHandleForTest("a message of tomorrow"));

        // What a client sends by itself is dropped without the line.
        Assert.False(SvGameServer.OtherSideMayHandleForTest(new FallDamageIntent()));
        Assert.True(SvGameServer.OtherSideDropsQuietlyForTest(new FallDamageIntent()));
        Assert.False(SvGameServer.OtherSideDropsQuietlyForTest(new MineBlockIntent()));
    }

    [Fact]
    public void EveryHandledIntent_IsDecidedByTheList_NotByAccident()
    {
        // The list is an allow-list over the dispatch table: whatever the table handles and the list does not name is
        // refused. This pins how many intents get through, so a new one is a decision somebody made here.
        int through = SvGameServer.HandledIntentTypesForTest.Count(t =>
            t.GetConstructor(Type.EmptyTypes) is { } make && SvGameServer.OtherSideMayHandleForTest(make.Invoke(null)));
        Assert.Equal(53, through);
    }

    [Fact]
    public void ARefusedAction_IsDropped_AndAnsweredWithOneLine_NotWithFive()
    {
        using var w = new OtherSideStage(Content, "refused");
        var a = w.Crossed("Kid");
        var floor = new Vector3i(OtherSideStage.Cx + 1, w.PadY, OtherSideStage.Cz);
        var stone = w.Server.World.GetBlock(floor);
        w.Transport.Sent.Clear();

        for (int i = 0; i < 5; i++)
        {
            w.Send(a, new MineBlockIntent { X = floor.X, Y = floor.Y, Z = floor.Z });
        }

        w.Send(a, new FallDamageIntent { ImpactSpeed = 60f }); // the client's own report: dropped, and not a word
        Assert.Equal(stone, w.Server.World.GetBlock(floor));
        Assert.Equal(100f, a.State.Health);
        Assert.Single(w.Transport.TextsTo(a), t => t == "@srv.otherside.refused");

        // A few seconds later the line may be said again.
        w.Tick(5.0);
        w.Send(a, new PlaceBlockIntent { X = floor.X, Y = floor.Y + 1, Z = floor.Z });
        Assert.Equal(2, w.Transport.TextsTo(a).Count(t => t == "@srv.otherside.refused"));

        // Chat and moving get through, without a line.
        w.Transport.Sent.Clear();
        w.Send(a, new MoveIntent { X = a.State.Position.X + 1f, Y = a.State.Position.Y, Z = a.State.Position.Z });
        Assert.Equal(w.AtGateA.X + 1f, a.State.Position.X, 3);
        Assert.DoesNotContain("@srv.otherside.refused", w.Transport.TextsTo(a));
    }

    [Fact]
    public void TheUseKey_OnlyOnARuneStone_AGadget_OnlyTheResonanceBlasterInTheHand()
    {
        using var w = new OtherSideStage(Content, "conditions");
        var a = w.Crossed("Kid");
        var floor = new Vector3i(OtherSideStage.Cx + 1, w.PadY, OtherSideStage.Cz);

        Assert.True(w.Server.OtherSideAllowsForTest(a, new UseBlockIntent { X = w.GateA[0].X, Y = w.GateA[0].Y, Z = w.GateA[0].Z }));
        Assert.False(w.Server.OtherSideAllowsForTest(a, new UseBlockIntent { X = floor.X, Y = floor.Y, Z = floor.Z }));

        var blaster = new UseGadgetIntent { GadgetKey = SoundersIds.ResonanceBlaster };
        Assert.False(w.Server.OtherSideAllowsForTest(a, blaster)); // not in the hand
        a.State.Inventory.SetSlot(0, new ItemStack(SoundersIds.ResonanceBlaster, 1));
        a.State.SelectedHotbarSlot = 0;
        Assert.True(w.Server.OtherSideAllowsForTest(a, blaster));
        Assert.False(w.Server.OtherSideAllowsForTest(a, new UseGadgetIntent { GadgetKey = "field_medkit" }));

        // Leaving a talk is always possible; a reply needs a talk with somebody who lives over there.
        Assert.True(w.Server.OtherSideAllowsForTest(a, new NpcDialogChoiceIntent { ChoiceIndex = NpcDialogChoiceIntent.Leave }));
        Assert.False(w.Server.OtherSideAllowsForTest(a, new NpcDialogChoiceIntent { ChoiceIndex = 0 }));
    }

    // ---- no needs, no harm -------------------------------------------------------------------------------------------

    [Fact]
    public void OverThere_NothingDrains_AndNothingHurts()
    {
        using var w = new OtherSideStage(Content, "needs");
        var a = w.Crossed("Kid");
        var b = w.Player("Friend", new Vector3f(OtherSideStage.Cx + 8.5f, w.PadY + 1, OtherSideStage.Cz + 0.5f));
        a.State.Hunger = 37f;
        a.State.Oxygen = 41f;
        a.State.Health = 63f;
        a.State.Exposure = 0.4f;
        b.State.Oxygen = 41f;
        b.State.Health = 63f;

        w.Tick(8.0);

        Assert.Equal(37f, a.State.Hunger);
        Assert.Equal(41f, a.State.Oxygen);
        Assert.Equal(63f, a.State.Health);
        Assert.Equal(0.4f, a.State.Exposure);
        Assert.True(b.State.Oxygen > 41f || b.State.Health > 63f, "on our side the same bars move");
        Assert.False(w.Server.PlayerTakesDamageForTest(a.State));
        Assert.True(w.Server.PlayerTakesDamageForTest(b.State));

        // A jetpack still costs suit energy over there.
        a.State.Equipment.SetSlot((int)EquipSlot.Back, new ItemStack("jetpack", 1));
        a.State.SuitEnergy = 80f;
        w.Send(a, new SetJetpackIntent { Active = true });
        w.Tick(2.0);
        Assert.True(a.State.SuitEnergy < 80f, "the jetpack drains the suit as everywhere");
    }

    [Fact]
    public void Hostiles_LookThroughAPlayerOverThere()
    {
        using var w = new OtherSideStage(Content, "hostiles");
        var a = w.Crossed("Kid");
        var sp = w.Server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundrak)!;
        string id = w.Server.SpawnCreatureAtForTest(new Vector3f(OtherSideStage.Cx + 5.5f, w.PadY + 1, OtherSideStage.Cz - 6.5f), sp.Id);
        var rak = w.Server.Creatures.First(c => c.Id == id);

        for (int i = 0; i < 80; i++)
        {
            a.State.Position = new Vector3f(rak.Position.X + 4f, w.PadY + 1, rak.Position.Z);
            w.Server.TickForTest(0.1);
        }

        Assert.Equal(double.NegativeInfinity, rak.ScreamedAt); // it never noticed anybody
        Assert.False(w.Server.NetCreatureForTest(id).Hostile);
        Assert.Equal(100f, a.State.Health);

        // Back on our side, right beside it: now it screams its warning.
        w.Server.ForceReturnForTest(a);
        for (int i = 0; i < 20; i++)
        {
            a.State.Position = new Vector3f(rak.Position.X + 4f, w.PadY + 1, rak.Position.Z);
            w.Server.TickForTest(0.1);
        }

        Assert.True(rak.ScreamedAt > double.NegativeInfinity);
    }

    // ---- the entry gate, leaving, the movers -------------------------------------------------------------------------

    [Fact]
    public void AForcedReturn_PutsThePlayerBackAtTheEntryGate()
    {
        using var w = new OtherSideStage(Content, "forced");
        var a = w.Crossed("Kid");
        var entry = a.State.Position;
        a.State.Position = new Vector3f(OtherSideStage.Cx + 18.5f, w.PadY + 1, OtherSideStage.Cz + 14.5f);
        w.Transport.Sent.Clear();

        w.Server.ForceReturnForTest(a);

        Assert.False(a.OtherSide);
        Assert.Equal(entry, a.State.Position);
        var snap = w.Transport.To<RespawnNotice>(a).Single();
        Assert.Equal("@srv.otherside.forced", snap.Reason);
        Assert.Equal(entry, new Vector3f(snap.X, snap.Y, snap.Z));
        Assert.Equal(OtherSideReasons.Forced, w.Transport.To<OtherSideState>(a).Last().Reason);
        Assert.DoesNotContain(SvGameServer.OtherSideMarkerForTest, a.State.Milestones);

        w.Server.ForceReturnForTest(a); // nobody to bring back: nothing happens
        Assert.Single(w.Transport.To<RespawnNotice>(a));
    }

    [Fact]
    public void LeavingTheGameOverThere_SavesThePlayerAtTheEntryGate_OnOurSide()
    {
        using var w = new OtherSideStage(Content, "leave");
        var a = w.Crossed("Kid");
        var entry = a.State.Position;
        a.State.Position = new Vector3f(OtherSideStage.Cx + 18.5f, w.PadY + 1, OtherSideStage.Cz + 14.5f);

        w.Server.OtherSideOnLeaveForTest(a);

        Assert.False(a.OtherSide);
        Assert.Equal(entry, a.State.Position);
        Assert.DoesNotContain(SvGameServer.OtherSideMarkerForTest, a.State.Milestones);
        Assert.Equal(string.Empty, a.State.OtherSideGateLocation);

        // A record that was never over there is left alone.
        var fine = new PlayerState { PlayerId = "fine", Position = new Vector3f(1f, 80f, 1f), CurrentLocationId = "here" };
        Assert.False(SvGameServer.UndoOtherSideInSaveForTest(fine));
        Assert.Equal(new Vector3f(1f, 80f, 1f), fine.Position);
    }

    [Fact]
    public void ASaveWrittenOverThere_PutsThePlayerAtTheGate_AtTheNextJoin()
    {
        using var w = new OtherSideStage(Content, "crash");
        var a = w.Crossed("Kid");
        var entry = a.State.Position;
        string body = a.State.CurrentLocationId;
        a.State.Position = new Vector3f(OtherSideStage.Cx + 18.5f, w.PadY + 1, OtherSideStage.Cz + 14.5f);
        w.Server.SaveNow(); // the autosave: the record says "over there", far from the gate — and then the server is gone

        var saved = w.Repo.LoadPlayer("Kid")!;
        Assert.Contains(SvGameServer.OtherSideMarkerForTest, saved.Milestones);
        Assert.Equal(entry, saved.OtherSideEntry);

        var server = w.Restart();
        var again = server.AddLocalPlayer("Kid");

        Assert.False(again.OtherSide);
        Assert.Equal(body, again.State.CurrentLocationId);
        Assert.Equal(entry.X, again.State.Position.X, 3);
        Assert.Equal(entry.Z, again.State.Position.Z, 3);
        Assert.InRange(again.State.Position.Y, entry.Y - 0.5f, entry.Y + 2.5f);
        Assert.DoesNotContain(SvGameServer.OtherSideMarkerForTest, again.State.Milestones);
        var state = w.Transport.To<OtherSideState>(again).Single();
        Assert.False(state.Over || state.HasGate);
        Assert.Equal(OtherSideReasons.Rejoined, state.Reason);
        Assert.Contains("@srv.otherside.rejoined", w.Transport.TextsTo(again));
        Assert.True(server.PlayerTakesDamageForTest(again.State)); // an ordinary player again
    }

    [Fact]
    public void AJoin_AlwaysTellsTheClientWhereItStands()
    {
        using var w = new OtherSideStage(Content, "join");
        var a = w.Player("Kid", w.AtGateA);

        var state = w.Transport.To<OtherSideState>(a).Single();
        Assert.False(state.Over || state.HasGate);
        Assert.Equal(OtherSideReasons.Sync, state.Reason);
    }

    [Fact]
    public void AnAdminTeleport_ARespawn_AndASeat_BringThePlayerBackFirst()
    {
        using var w = new OtherSideStage(Content, "movers", c => c.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true });
        var a = w.Crossed("Kid");
        a.State.Role = PlayerRole.WorldAdmin;
        w.Transport.Sent.Clear();

        var target = new Vector3f(OtherSideStage.Cx + 10.5f, w.PadY + 1, OtherSideStage.Cz - 4.5f);
        w.Send(a, new AdminCommandIntent { Command = "teleport_to_location", X = target.X, Y = target.Y, Z = target.Z });

        Assert.False(a.OtherSide);
        Assert.Equal(target, a.State.Position); // where the teleport put them, not at the gate
        Assert.Equal(OtherSideReasons.Moved, w.Transport.To<OtherSideState>(a).Last().Reason);
        Assert.DoesNotContain("@srv.otherside.returned", w.Transport.TextsTo(a)); // silently: the teleport says what happened

        // A forced respawn.
        a.State.Position = w.AtGateA;
        w.Use(a, w.GateA[1]);
        Assert.True(a.OtherSide);
        w.Server.KillPlayerForTest(a, "@srv.death.critical");
        Assert.False(a.OtherSide);

        // Seated by anything: the watch brings them back within a second.
        a.State.AboardShip = false;
        a.State.Health = 100f;
        a.State.Position = w.AtGateA;
        w.Use(a, w.GateA[1]);
        Assert.True(a.OtherSide);
        a.State.Seated = true;
        w.Tick(1.4);
        Assert.False(a.OtherSide);
        Assert.Equal(w.AtGateA, a.State.Position);
    }

    [Fact]
    public void TheAdminVerb_CrossesAndComesBack_WithoutDiscAndGate()
    {
        using var w = new OtherSideStage(Content, "verb", c => c.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true });
        var a = w.Player("Owner", w.AtLoneStone); // no gate here, no disc
        a.State.Role = PlayerRole.WorldAdmin;

        w.Server.HandleForTest(a, new AdminCommandIntent { Command = "otherside" });
        Assert.True(a.OtherSide);
        Assert.Equal(OtherSideReasons.Admin, w.Transport.To<OtherSideState>(a).Last().Reason);
        Assert.Equal(w.AtLoneStone, w.Server.EntryGateOfForTest(a)); // the place they stand at is their way back

        w.Send(a, new AdminCommandIntent { Command = "otherside", StringArg = "off" }); // through the gate: an admin's own tool
        Assert.False(a.OtherSide);

        w.Server.HandleForTest(a, new AdminCommandIntent { Command = "otherside", StringArg = "on" });
        Assert.True(a.OtherSide);

        // A player without the role gets nowhere with it.
        var kid = w.Player("Kid", w.AtGateA);
        w.Server.HandleForTest(kid, new AdminCommandIntent { Command = "otherside" });
        Assert.False(kid.OtherSide);
    }

    [Fact]
    public void TheEvents_AreRaised_AndTheMessageTravels()
    {
        using var w = new OtherSideStage(Content, "events");
        var a = w.Player("Kid", w.AtGateA);
        OtherSideStage.Dress(a);
        var heard = new RecordingGameEventListener();
        w.Server.AddGameEventListener("otherside:*", heard);

        w.Use(a, w.GateA[1]);
        w.Use(a, w.GateA[1]);

        Assert.Equal(new[] { GameEventKeys.OtherSideCrossed, GameEventKeys.OtherSideReturned }, heard.Events.Select(e => e.Key));
        Assert.All(heard.Events, e => Assert.Equal(a.State.PlayerId, e.Player));

        var sent = new OtherSideState { Over = true, HasGate = true, GateX = 1.5f, GateY = 64f, GateZ = -3.5f, Reason = OtherSideReasons.Crossed, SoundBlocksUsed = 12, SoundBlockCap = 2000 };
        var back = Assert.IsType<OtherSideState>(NetCodec.Decode(NetCodec.Encode(sent)));
        Assert.True(back.Over && back.HasGate);
        Assert.Equal((1.5f, 64f, -3.5f), (back.GateX, back.GateY, back.GateZ));
        Assert.Equal((OtherSideReasons.Crossed, 12, 2000), (back.Reason, back.SoundBlocksUsed, back.SoundBlockCap));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryLine_ExistsInBothLanguages(string language)
    {
        var table = TestLocales.Load(language);
        string[] keys =
        {
            "srv.otherside.crossed_first", "srv.otherside.crossed", "srv.otherside.returned", "srv.otherside.forced",
            "srv.otherside.rejoined", "srv.otherside.refused", "srv.otherside.no_client", "srv.otherside.no_gate",
            "srv.otherside.no_disc", "srv.otherside.not_here", "srv.otherside.not_seated", "srv.otherside.not_now",
            "srv.otherside.admin_on", "srv.otherside.admin_off", "ui.admin.help_otherside",
            "dlg.snd_over_folk.prompt", "dlg.snd_over_folk.c_who", "dlg.snd_over_folk.r_who", "dlg.snd_over_folk.c_lights",
            "dlg.snd_over_folk.r_lights", "dlg.snd_over_folk.c_soft", "dlg.snd_over_folk.r_soft",
        };
        foreach (string key in keys)
        {
            Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{language}.json is missing '{key}'");
        }
    }
}
