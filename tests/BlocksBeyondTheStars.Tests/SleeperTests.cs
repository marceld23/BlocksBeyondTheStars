// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3897 — the guard post and the sleeper: the capsule tank sealed without a chip and opened once with one (the lamps, both
/// tank cells, the record, the event for everyone near), the guard who steps out (look, name, never a vendor), his dialog with
/// its gift once per player — also for a player who comes later, also with a full backpack —, VEGA's words by what the
/// player's story knows, his move to the nearest inhabited settlement a minute after the first conversation, and that he is
/// there again after the world was loaded anew. A tower without a guard post has no tank to use.
/// </summary>
public sealed class SleeperTests : IDisposable
{
    private const string Chip = SvGameServer.SpsTowerChipItem;
    private const string Gift = "guardian_plasma_rusted";
    private const string DialogDone = "dialog:" + SvGameServer.SleeperDialogKey + ":done";

    private readonly SpsTowerLifeWorlds _worlds = new("bbts_sleeper_");

    public void Dispose() => _worlds.Dispose();

    private static ushort Id(string block) => SpsTowerLifeWorlds.Content.GetBlock(block)!.NumericId.Value;

    private static Vector3i Upper(SpsTowerCell tank) => new(tank.X, tank.Y + 1, tank.Z);

    /// <summary>Stands the player a step beside the guard.</summary>
    private static void StandBy(SvGameServer server, PlayerSession p, SpsTowerRecord tower)
    {
        var guard = server.SleeperForTest(tower)!.Value;
        p.State.Position = new Vector3f(guard.Pos.X + 1f, guard.Pos.Y, guard.Pos.Z);
    }

    /// <summary>Talks the guard's own dialog through (five nodes, the first reply each) — its last reply hands over the gift.</summary>
    private static void TalkThrough(SvGameServer server, PlayerSession p, SpsTowerRecord tower)
    {
        StandBy(server, p, tower);
        string id = p.State.PlayerId;
        server.TalkToNpcForTest(id, server.SleeperForTest(tower)!.Value.NpcId);
        Assert.Equal((SvGameServer.SleeperDialogKey, 0), server.ActiveDialogForTest(id));
        for (int node = 0; node < 5; node++)
        {
            server.ChooseDialogForTest(id, 0);
        }

        Assert.Null(server.ActiveDialogForTest(id));
    }

    [Fact]
    public void TheCapsuleTank_IsSealedWithoutAChip_OpensOnceWithOne_AndEveryoneNearGetsTheEvent()
    {
        var (server, t) = _worlds.Start("sleeper_tank");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump post");
        Assert.True(tower.HasGuardPost);
        var tank = tower.Tank!;
        var near = SpsTowerLifeWorlds.Join(server, 2, "Near");
        var far = SpsTowerLifeWorlds.Join(server, 3, "Far");
        near.State.Position = new Vector3f(tank.X + 12.5f, tank.Y, tank.Z + 0.5f);
        far.State.Position = new Vector3f(tank.X + 120.5f, tank.Y, tank.Z + 0.5f);
        var heard = new SpsTowerLifeWorlds.Listener();
        server.AddGameEventListener(GameEventKeys.CapsuleOpened, heard);

        // The block has a use, and without a chip the real path only says what opens it.
        Assert.True(SpsTowerLifeWorlds.Content.GetBlock(SpsTowerRules.TankBlock)!.Usable);
        Assert.True(server.BlockHasUse(SpsTowerRules.TankBlock));
        Assert.False(server.BlockHasUse(SvGameServer.SleeperTankOpenBlock));
        Assert.Equal(Id(SpsTowerRules.TankBlock), server.World.GetBlock(tank.ToVector()).Value);
        SpsTowerLifeWorlds.StandAt(p, tank.ToVector());
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = tank.X, Y = tank.Y, Z = tank.Z }));
        Assert.Contains("@srv.capsule.sealed", t.TextsTo(p));
        Assert.False(tower.TankOpened);
        Assert.Null(server.SleeperForTest(tower));
        Assert.Empty(heard.Events);

        // With a chip (aimed at the upper cell): one chip, the two lamps, both cells, the record, the cue.
        p.State.Inventory.Add(Chip, 2, 16);
        t.Sent.Clear();
        Assert.True(server.UseCapsuleTankForTest(p, tower, upper: true));
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.True(tower.TankOpened);
        Assert.Equal(Id(SvGameServer.SleeperTankOpenBlock), server.World.GetBlock(tank.ToVector()).Value);
        Assert.Equal(Id(SvGameServer.SleeperTankOpenBlock), server.World.GetBlock(Upper(tank)).Value);
        Assert.Equal(2, tower.TankLamps.Count);
        Assert.All(tower.TankLamps, l => Assert.Equal(Id(SpsTowerRules.LitLampBlock), server.World.GetBlock(l.ToVector()).Value));
        Assert.All(tower.TowerLamps, l => Assert.Equal(Id(SpsTowerRules.LampBlock), server.World.GetBlock(l.ToVector()).Value)); // the tower's own stay red
        Assert.False(tower.Lit);
        Assert.Single(t.To<SoundFx>(p), s => s.SoundId == SvGameServer.SleeperTankSound);
        Assert.True(server.EvaluateStoryConditionForTest("world:sps_guard_awake", far));

        // Everybody within 24 blocks gets the event (the tower's id), the line and the scene or VEGA's words — nobody further off.
        Assert.Equal(new[] { "Pilot", "Near" }, heard.Actors);
        Assert.All(heard.Events, e => Assert.Equal((GameEventKeys.CapsuleOpened, tower.SiteId), e));
        foreach (var witness in new[] { p, near })
        {
            Assert.Contains("@srv.capsule.opened", t.TextsTo(witness));
            // #3899: the pack defines the scene — committed to the witness's ledger (it may still wait behind the cooldown).
            bool scene = witness.State.Milestones.Contains(StoryPackKeys.CutsceneSent(StoryRegistry.DefaultStoryId, SvGameServer.SleeperTankCutscene));
            Assert.True(scene ^ t.LineKeys(witness).Contains(SvGameServer.SleeperTankLine));
        }

        Assert.DoesNotContain("@srv.capsule.opened", t.TextsTo(far));
        Assert.DoesNotContain(SvGameServer.SleeperTankLine, t.LineKeys(far));

        // The guard steps out: one figure by the tank, his look, his name, a settler by role — never a vendor.
        var guard = server.SleeperForTest(tower)!.Value;
        Assert.True(guard.AtPost);
        Assert.Equal(new[] { guard.NpcId }, server.SleeperNpcIdsForTest());
        Assert.True(Math.Abs(guard.Pos.X - (tank.X + 0.5f)) <= 4f && Math.Abs(guard.Pos.Z - (tank.Z + 0.5f)) <= 4f);
        var net = server.NetNpcForTest(guard.NpcId);
        Assert.Equal((SvGameServer.SleeperLook, "Iven", SvGameServer.SleeperRoleKey, "settler"), (net.Look, net.Name, net.NameKey, net.Role));
        Assert.Equal("Iven", tower.GuardName); // the first guard a save wakes carries the name of chr.sps_guard.name — in the record
        Assert.Contains(server.NpcRosterForTest(), n => n.Id == guard.NpcId && n.CharacterId == SvGameServer.SleeperCharacterId);

        // Once per tank: the opened tank is another block, a second chip is never taken, no second guard.
        Assert.True(server.UseCapsuleTankForTest(p, tower));
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = tank.X, Y = tank.Y, Z = tank.Z }));
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.Equal(2, heard.Events.Count);
        Assert.Single(server.SleeperNpcIdsForTest());

        // Nobody lives on this world: a minute after the talk he still keeps his post — and he is there after a reload.
        TalkThrough(server, p, tower);
        server.TickSpsTowerLifeForTest();
        server.AdvanceUptimeForTest(SvGameServer.SleeperLeaveSeconds + 1);
        server.TickSpsTowerLifeForTest();
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperForTest(tower)!.Value.AtPost);
        Assert.Equal(string.Empty, tower.GuardSettlement);
        server.ForgetSleepersForTest();
        Assert.Null(server.SleeperForTest(tower));
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperForTest(tower)!.Value.AtPost);
        Assert.Single(server.SleeperNpcIdsForTest());
        Assert.Equal("Iven", server.NetNpcForTest(server.SleeperForTest(tower)!.Value.NpcId).Name);
    }

    [Fact]
    public void EveryGuardPost_HoldsADifferentGuard_NamedInTheOrderTheTanksAreOpened_AndTheGiftStaysOncePerPlayer()
    {
        var (server, _) = _worlds.Start("sleeper_names");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var built = new[] { "sps_tower_stump post", "sps_tower_broken post", "sps_tower_leaning post" }.Select(arg =>
        {
            p.State.Position = new Vector3f(p.State.Position.X + 240, p.State.Position.Y, p.State.Position.Z);
            return SpsTowerLifeWorlds.Stamp(server, p, arg);
        }).ToList();
        Assert.All(built, tower => Assert.Equal(string.Empty, tower.GuardName)); // nobody is named before his tank opens
        string Plate(SpsTowerRecord tower) => server.NetNpcForTest(server.SleeperForTest(tower)!.Value.NpcId).Name;

        // The pool: language-neutral names, none the first guard's, none of the stories' or of the Sounders at the gates.
        var pool = SvGameServer.SleeperNames;
        Assert.Equal(pool.Length, pool.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.DoesNotContain("Iven", pool);
        Assert.Empty(pool.Intersect(SvGameServer.OtherSideFolkNames.Concat(new[] { "Elora", "Kamo", "Feya", "Tini", "Roll" }), StringComparer.OrdinalIgnoreCase));

        // The names follow the order the tanks are opened in (not the order the towers were built in).
        p.State.Inventory.Add(Chip, 3, 16);
        var opened = new[] { built[1], built[0], built[2] };
        foreach (var tower in opened)
        {
            Assert.True(server.UseCapsuleTankForTest(p, tower));
        }

        Assert.Equal(0, p.State.Inventory.CountOf(Chip));
        Assert.Equal(new[] { "Iven", pool[0], pool[1] }, opened.Select(tower => tower.GuardName));
        Assert.Equal(new[] { "Iven", pool[0], pool[1] }, opened.Select(Plate)); // the nameplate shows it
        Assert.Equal(3, server.SleeperNpcIdsForTest().Count);

        // He keeps his name: the record holds it, so a world loaded anew puts the same three guards back.
        server.ForgetSleepersForTest();
        server.TickSpsTowerLifeForTest();
        Assert.Equal(new[] { "Iven", pool[0], pool[1] }, opened.Select(Plate));
        Assert.Equal(3, server.SleeperNpcIdsForTest().Count);

        // Once per player, whichever guard they talk to first: the full talk and the gift there, the everyday talk at the others.
        TalkThrough(server, p, opened[1]);
        Assert.Equal(1, p.State.Inventory.CountOf(Gift));
        foreach (var other in new[] { opened[0], opened[2] })
        {
            StandBy(server, p, other);
            server.TalkToNpcForTest(p.State.PlayerId, server.SleeperForTest(other)!.Value.NpcId);
            Assert.Equal(("sps_guard_again", 0), server.ActiveDialogForTest(p.State.PlayerId));
            server.ChooseDialogForTest(p.State.PlayerId, 0);
        }

        Assert.Equal(1, p.State.Inventory.CountOf(Gift));

        // Another player has their own first guard — any of them.
        var q = SpsTowerLifeWorlds.Join(server, 2, "Second");
        TalkThrough(server, q, opened[2]);
        Assert.Equal(1, q.State.Inventory.CountOf(Gift));
    }

    [Fact]
    public void ATowerWithoutAGuardPost_HasNoTankToUse()
    {
        var (server, t) = _worlds.Start("sleeper_nopost");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_stump nopost");
        Assert.False(tower.HasGuardPost);
        Assert.Null(tower.Tank);
        p.State.Inventory.Add(Chip, 1, 16);
        Assert.False(server.UseCapsuleTankForTest(p, tower));

        // Even a tank block that stands somewhere else does nothing: it belongs to no guard post.
        var console = tower.Console!.ToVector();
        var cell = new Vector3i(console.X, console.Y + 30, console.Z);
        server.World.SetBlock(cell, new Shared.Primitives.BlockId(Id(SpsTowerRules.TankBlock)));
        p.State.Position = new Vector3f(cell.X + 1.5f, cell.Y, cell.Z + 0.5f);
        t.Sent.Clear();
        server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(new UseBlockIntent { X = cell.X, Y = cell.Y, Z = cell.Z }));
        Assert.Equal(1, p.State.Inventory.CountOf(Chip));
        Assert.Empty(t.TextsTo(p));
        Assert.Empty(server.SleeperNpcIdsForTest());
        Assert.False(server.EvaluateStoryConditionForTest("world:sps_guard_post_known", p));
    }

    [Fact]
    public void TheSleepersDialog_GivesTheRustedPlasmaOncePerPlayer_NeverLosesIt_AndVegaSaysWhatSheKnows()
    {
        var (server, t) = _worlds.Start("sleeper_dialog");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.Stamp(server, p, "sps_tower_broken post");
        p.State.Inventory.Add(Chip, 1, 16);
        Assert.True(server.UseCapsuleTankForTest(p, tower));
        int npc = server.SleeperForTest(tower)!.Value.NpcId;

        // The pack's dialog: five short nodes of two replies each, both last replies give the gift.
        var dialog = Assert.Single(SpsTowerLifeWorlds.Content.Stories[SpsTowerLifeTests.V].Dialogs, d => d.Key == SvGameServer.SleeperDialogKey);
        Assert.True(dialog.OncePerPlayer);
        Assert.Equal(SvGameServer.SleeperCharacterId, dialog.Character);
        Assert.All(dialog.Nodes, n => Assert.InRange(n.Choices.Count, 2, 3));
        Assert.All(dialog.Nodes[^1].Choices, c => Assert.Equal("gift:" + Gift + ":1", c.Consequence));
        Assert.DoesNotContain(dialog.Nodes.SelectMany(n => n.Choices), c => c.Consequence.StartsWith("gift:", StringComparison.Ordinal) && c.Next >= 0);

        // Before the talk VEGA says nothing about him; leaving half-way gives nothing and the dialog stays open to them.
        server.TickSleeperAftermathForTest(p);
        Assert.DoesNotContain(SvGameServer.SleeperUnsureLine, t.LineKeys(p));
        StandBy(server, p, tower);
        server.TalkToNpcForTest(p.State.PlayerId, npc);
        server.ChooseDialogForTest(p.State.PlayerId, 1);
        server.LeaveDialogForTest(p.State.PlayerId, npc);
        Assert.Equal(0, p.State.Inventory.CountOf(Gift));
        Assert.DoesNotContain(DialogDone, p.State.Milestones);

        TalkThrough(server, p, tower);
        Assert.Equal(1, p.State.Inventory.CountOf(Gift));
        Assert.Contains(DialogDone, p.State.Milestones);

        // Afterwards his everyday talk — never the gift again.
        server.TalkToNpcForTest(p.State.PlayerId, npc);
        Assert.Equal(("sps_guard_again", 0), server.ActiveDialogForTest(p.State.PlayerId));
        server.ChooseDialogForTest(p.State.PlayerId, 1);
        Assert.Equal(1, p.State.Inventory.CountOf(Gift));

        // VEGA's truth by what the player's story knows: unsure before "What You Are", then — once — "He is like you".
        server.TickSleeperAftermathForTest(p);
        server.TickSleeperAftermathForTest(p);
        Assert.Equal(1, t.LineKeys(p).Count(k => k == SvGameServer.SleeperUnsureLine));
        Assert.DoesNotContain("story.vega.insight.sps_guard", t.LineKeys(p));
        SpsTowerLifeWorlds.AdvanceTo(server, 9);
        p.State.Position = new Vector3f(p.State.Position.X + 60, p.State.Position.Y, p.State.Position.Z);
        server.TickSleeperAftermathForTest(p);
        Assert.DoesNotContain("story.vega.insight.sps_guard", t.LineKeys(p)); // not while they are away from him
        StandBy(server, p, tower);
        server.TickSleeperAftermathForTest(p);
        server.TickSleeperAftermathForTest(p);
        Assert.Equal(1, t.LineKeys(p).Count(k => k == "story.vega.insight.sps_guard"));
        Assert.Equal(1, t.LineKeys(p).Count(k => k == SvGameServer.SleeperUnsureLine));

        // A second player gets their own conversation and gift — with a full backpack it lies at their feet, never lost.
        var q = SpsTowerLifeWorlds.Join(server, 2, "Second");
        var inv = q.State.Inventory;
        int max = SpsTowerLifeWorlds.Content.MaxStackOf("stone");
        for (int i = 0; i < inv.SlotCount; i++)
        {
            if (inv.Slots[i] is not { IsEmpty: false })
            {
                inv.SetSlot(i, new ItemStack("stone", max));
            }
        }

        int packets = server.DropPackets.Count;
        TalkThrough(server, q, tower);
        Assert.Contains(DialogDone, q.State.Milestones);
        Assert.Equal(0, q.State.Inventory.CountOf(Gift));
        Assert.Equal(packets + 1, server.DropPackets.Count);
        Assert.Contains(server.DropPackets[^1].Items, i => i.Item == Gift && i.Count == 1);
        Assert.Equal(1, p.State.Inventory.CountOf(Gift)); // the first player's is their own

        // The latecomer already knows what they are: no "unsure" line for them, the insight at once.
        server.TickSleeperAftermathForTest(q);
        Assert.DoesNotContain(SvGameServer.SleeperUnsureLine, t.LineKeys(q));
        Assert.Equal(1, t.LineKeys(q).Count(k => k == "story.vega.insight.sps_guard"));

        // Every line of his exists in English and German, and none says what he is.
        var en = SpsTowerLifeWorlds.PackTable("en");
        var de = SpsTowerLifeWorlds.PackTable("de");
        foreach (var d in SpsTowerLifeWorlds.Content.Stories[SpsTowerLifeTests.V].Dialogs.Where(d => d.Character == SvGameServer.SleeperCharacterId))
        {
            foreach (string key in d.Nodes.SelectMany(n => n.Choices.SelectMany(c => new[] { c.TextKey, c.ResponseKey }).Append(n.PromptKey)))
            {
                Assert.True(en.ContainsKey(key) && de.ContainsKey(key), key);
                Assert.DoesNotContain("clone", en[key], StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Klon", de[key], StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("imprint", en[key], StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("Iven", en[key], StringComparison.Ordinal); // every guard speaks these lines: no name in them
                Assert.DoesNotContain("Iven", de[key], StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheSleeper_MovesToTheNearestInhabitedSettlement_AMinuteAfterTheFirstTalk_AndIsThereAfterAReload()
    {
        var (server, t) = _worlds.Start("sleeper_home", settlements: true);
        Assert.True(server.InhabitedSettlementCount > 0, "the test world needs an inhabited settlement");
        var p = SpsTowerLifeWorlds.Join(server, 1, "Pilot");
        var tower = SpsTowerLifeWorlds.StampAwayFromTheVillage(server, p, "sps_tower_stump post");
        p.State.Inventory.Add(Chip, 1, 16);
        Assert.True(server.UseCapsuleTankForTest(p, tower));
        var post = server.SleeperForTest(tower)!.Value;
        Assert.True(post.AtPost);

        // Nobody talked with him yet: he waits at his post, however long.
        server.AdvanceUptimeForTest(600);
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperForTest(tower)!.Value.AtPost);
        Assert.Equal(0, server.SleeperLeaveAtForTest(tower));

        // The first conversation: while it runs he stays; a minute after it ended he is taken to the settlement.
        StandBy(server, p, tower);
        server.TalkToNpcForTest(p.State.PlayerId, post.NpcId);
        server.TickSpsTowerLifeForTest();
        server.AdvanceUptimeForTest(SvGameServer.SleeperLeaveSeconds * 3);
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperForTest(tower)!.Value.AtPost); // still talking
        for (int node = 0; node < 5; node++)
        {
            server.ChooseDialogForTest(p.State.PlayerId, 0);
        }

        Assert.Equal(1, p.State.Inventory.CountOf(Gift));
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperLeaveAtForTest(tower) > 0);
        server.AdvanceUptimeForTest(SvGameServer.SleeperLeaveSeconds - 5);
        server.TickSpsTowerLifeForTest();
        Assert.True(server.SleeperForTest(tower)!.Value.AtPost);
        t.Sent.Clear();
        server.AdvanceUptimeForTest(6);
        server.TickSpsTowerLifeForTest();

        var home = server.SleeperForTest(tower)!.Value;
        Assert.False(home.AtPost);
        Assert.NotEqual(post.NpcId, home.NpcId); // taken away and put down again — not walked across the world
        Assert.Single(server.SleeperNpcIdsForTest());
        Assert.NotEqual(string.Empty, tower.GuardSettlement);
        Assert.Contains(t.TextsTo(p), x => x.Contains("Iven", StringComparison.Ordinal) && x.Contains(tower.GuardSettlement, StringComparison.Ordinal));
        int circ = server.World.Circumference;
        Assert.Contains(server.SettlementsForTest, s => !s.Ruined
            && WorldConstants.WrapDeltaX((int)home.Pos.X - s.MinX, circ) >= -4 && WorldConstants.WrapDeltaX((int)home.Pos.X - s.MaxX, circ) <= 4
            && home.Pos.Z >= s.MinZ - 4 && home.Pos.Z <= s.MaxZ + 4);

        // A player who comes later meets him there: their own conversation, their own gift.
        var q = SpsTowerLifeWorlds.Join(server, 2, "Later");
        TalkThrough(server, q, tower);
        Assert.Equal(1, q.State.Inventory.CountOf(Gift));
        server.AdvanceUptimeForTest(SvGameServer.SleeperLeaveSeconds * 2);
        server.TickSpsTowerLifeForTest();
        Assert.Equal(home.NpcId, server.SleeperForTest(tower)!.Value.NpcId); // he stays where he lives

        // The world's NPCs are rebuilt on a load: he is placed again from the record.
        server.ForgetSleepersForTest();
        server.TickSpsTowerLifeForTest();
        var again = server.SleeperForTest(tower)!.Value;
        Assert.False(again.AtPost);
        Assert.True(Math.Abs(again.Pos.Z - home.Pos.Z) < 8f);
        Assert.Single(server.SleeperNpcIdsForTest());

        // And after a restart of the server: the record holds his settlement, the first tick puts him there.
        string settlement = tower.GuardSettlement, site = tower.SiteId;
        server.Stop();
        _worlds.CloseLast();
        var (restarted, _) = _worlds.Start("sleeper_home", settlements: true);
        var kept = restarted.Metadata.SpsTowers.Single(x => x.SiteId == site);
        Assert.True(kept.TankOpened);
        Assert.Equal(settlement, kept.GuardSettlement);
        Assert.Equal(Id(SvGameServer.SleeperTankOpenBlock), restarted.World.GetBlock(kept.Tank!.ToVector()).Value);
        var r = SpsTowerLifeWorlds.Join(restarted, 1, "Pilot");
        Assert.Equal(1, r.State.Inventory.CountOf(Gift));
        restarted.TickSpsTowerLifeForTest();
        var back = restarted.SleeperForTest(kept)!.Value;
        Assert.False(back.AtPost);
        Assert.Single(restarted.SleeperNpcIdsForTest());
        Assert.True(Math.Abs(back.Pos.Z - home.Pos.Z) < 8f);

        // A tank opened but never talked at: at the next load he is in the settlement too (the record decides).
        kept.GuardSettlement = string.Empty;
        restarted.ForgetSleepersForTest();
        restarted.TickSpsTowerLifeForTest();
        Assert.False(restarted.SleeperForTest(kept)!.Value.AtPost);
        Assert.Equal(settlement, kept.GuardSettlement);
    }
}
