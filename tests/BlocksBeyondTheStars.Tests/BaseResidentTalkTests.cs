// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3089 / #3090 — the people at the player's base (Justus 2026-11-01: "in my base I can't interact with the people who work
/// there!"): the quartermaster's "Show me the jobs" opens the Missions tab, every base job and every base focus has lines of
/// its own, an out-of-reach talk says so instead of nothing, and a staffed base board offers the stand-alone board jobs
/// (supply_cables) like a settlement's board. An idle base board says why it has no jobs when the Missions tab opens there.
/// </summary>
public sealed class BaseResidentTalkTests
{
    private const string Owner = NpcLifeWorld.Owner;
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static NpcDialogState? LastPanel(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcDialogState>().LastOrDefault();

    [Fact]
    public void TheQuartermaster_ShowsTheJobs_TheClientOpensTheMissionsTab()
    {
        using var w = new NpcLifeWorld(_content, "talk_qm_jobs");
        var at = w.Player.State.Position;
        int qm = w.Server.AddNpcForTest(new Vector3f(at.X + 2f, w.Feet, at.Z), role: "quartermaster");

        w.Server.TalkToNpcForTest(Owner, qm);
        Assert.Equal(("quartermaster_hello", 0), w.Server.ActiveDialogForTest(Owner));
        Assert.Equal(3, LastPanel(w)!.Choices.Length); // the panel shows three replies at most

        w.Server.ChooseDialogForTest(Owner, 0); // "Show me the jobs!"
        var line = LastPanel(w)!;
        Assert.True(line.End);
        Assert.Equal(NpcDialogState.ActionMissions, line.Action);
    }

    [Fact]
    public void ATalkOutOfReach_OrToSomebodyGone_SaysSo()
    {
        using var w = new NpcLifeWorld(_content, "talk_reach");
        var at = w.Player.State.Position;
        int far = w.Server.AddNpcForTest(new Vector3f(at.X + 7f, w.Feet, at.Z), role: "guardian");
        w.Server.TalkToNpcForTest(Owner, far);
        Assert.Contains("@srv.npc.talk_far", w.MessagesToPlayer());

        w.Server.TalkToNpcForTest(Owner, 987654);
        Assert.Contains("@srv.npc.talk_gone", w.MessagesToPlayer());

        // The client offers a talk up to InteractionReach.Talk; the server takes it with a step of slack.
        int edge = w.Server.AddNpcForTest(new Vector3f(at.X + InteractionReach.Talk - 0.05f, w.Feet, at.Z), role: "guardian");
        int refusals = w.MessagesToPlayer().Count(m => m == "@srv.npc.talk_far");
        w.Server.TalkToNpcForTest(Owner, edge);
        Assert.Equal(refusals, w.MessagesToPlayer().Count(m => m == "@srv.npc.talk_far"));
        Assert.Contains(w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcGreeting>(),
            g => g.NpcId == edge);
    }

    [Fact]
    public void AResidentWithoutAPost_TalksAboutTheBaseFocus_AndTheLumberjackAboutTheWood()
    {
        using var w = new NpcLifeWorld(_content, "talk_focus");
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        var settler = Assert.Single(w.Server.BaseResidentsForTest(baseId));
        w.Player.State.Milestones.Add("dialog:settler_crystal_gift:done"); // a once-only present would come first

        Assert.Equal("resident_home", w.Server.PickedDialogKeyForTest(Owner, settler.NpcId));
        foreach (string focus in new[] { BaseFocus.Miner, BaseFocus.Market, BaseFocus.Radio })
        {
            w.SetBaseFocus(focus);
            Assert.Equal("resident_focus_" + focus, w.Server.PickedDialogKeyForTest(Owner, settler.NpcId));
        }

        w.SetBaseFocus(BaseFocus.Lumberjack);
        w.Server.ScanBaseLifeForTest();
        var woodcutter = Assert.Single(w.Server.BaseResidentsForTest(baseId));
        Assert.Equal("lumberjack", woodcutter.Job);
        Assert.Equal("resident_lumberjack", w.Server.PickedDialogKeyForTest(Owner, woodcutter.NpcId));
    }

    [Fact]
    public void EveryBaseJob_AndEveryFocus_HasLinesForTheOwnResidents()
    {
        static bool ForResidents(DialogDefinition d)
            => d.MinStage.Length == 0 && d.Character.Length == 0 && !d.OncePerPlayer && d.Nodes.Count > 0
               && d.Nodes[0].Choices.Count > 0 && (d.Home == DialogDefinition.HomeAny || d.Home == DialogDefinition.HomeOwnBase);

        // The two classic posts talk by their role (a settlement's, a station's and a base's alike).
        foreach (string role in new[] { "vendor", "quartermaster" })
        {
            Assert.True(_content.Dialogs.Any(d => ForResidents(d) && d.Job.Length == 0 && d.Role == role), $"no lines for the role '{role}'");
        }

        var jobs = new[] { "guard", "gardener", "craftsman", "lumberjack", "focus_miner", "focus_market", "focus_radio" }
            .Concat(NpcProfessions.All.Select(p => p.Job));
        foreach (string job in jobs)
        {
            Assert.True(_content.Dialogs.Any(d => ForResidents(d) && d.Job == job), $"no lines for the base job '{job}'");
        }
    }

    [Fact]
    public void AStaffedBaseBoard_OffersTheStandAloneBoardJob_OnlyAtTheBoard()
    {
        using var w = new NpcLifeWorld(_content, "board_standalone");
        int baseId = w.FoundBase();
        var board = new Vector3i(NpcLifeWorld.Cx - 4, w.Feet, NpcLifeWorld.Cz + 4);
        w.Set(board.X, board.Y, board.Z, "mission_board");
        w.Server.ScanBaseLifeForTest();
        Assert.Contains(w.Server.BaseResidentsForTest(baseId), r => r.Job == "quartermaster");

        w.Player.State.Position = new Vector3f(board.X + 12.5f, w.Feet, board.Z + 0.5f);
        Assert.DoesNotContain("supply_cables", w.Server.OfferedMissionIdsForTest(Owner));
        w.Server.AcceptMission(Owner, "supply_cables");
        Assert.DoesNotContain(w.Player.State.Missions, m => m.MissionId == "supply_cables");

        w.Player.State.Position = new Vector3f(board.X + 1.5f, w.Feet, board.Z + 0.5f);
        Assert.Contains("supply_cables", w.Server.OfferedMissionIdsForTest(Owner)); // #3090: a base board is a board
        w.Server.AcceptMission(Owner, "supply_cables");
        Assert.Contains(w.Player.State.Missions, m => m.MissionId == "supply_cables");
    }

    [Fact]
    public void TheMissionsTab_AtABoardNobodyStaffs_SaysWhy()
    {
        using var w = new NpcLifeWorld(_content, "board_idle");
        w.Player.State.Inventory.Add("base_core", 1, 16);
        w.Server.PlaceBlock(Owner, NpcLifeWorld.Cx, w.Feet, NpcLifeWorld.Cz, "base_core"); // a bare claim: nobody lives here
        var board = new Vector3i(NpcLifeWorld.Cx + 3, w.Feet, NpcLifeWorld.Cz + 3);
        w.Set(board.X, board.Y, board.Z, "mission_board");
        w.Server.ScanBaseLifeForTest();

        w.Player.State.Position = new Vector3f(board.X + 1.5f, w.Feet, board.Z + 0.5f);
        w.Server.HandlePayloadForTest(w.Player.ConnectionId, NetCodec.Encode(new RequestMissions()));
        Assert.Contains("@srv.base.post_no_resident", w.MessagesToPlayer());
        Assert.Contains(w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg), m => m is MissionList);
    }
}
