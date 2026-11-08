// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The robot conversation (#3405-#3408, Justus' report "Ich meinte mit": "…and that you can communicate with it!"): E at the
/// own robot opens the dialogue panel with "How are you?", "Tell me something!", "Ask me something!" and "Follow me!" /
/// "Wait here!"; the robot asks questions, remembers the answers and recalls them; it holds still while the two talk.
/// </summary>
public sealed class RobotDialogTests : IDisposable
{
    private const string Ada = "Ada";

    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_robot_dialog_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();
    private readonly Localizer _en;

    public RobotDialogTests() => _en = _content.CreateLocalizer(GameLocale.English);

    public void Dispose()
    {
        foreach (var repo in _repos)
        {
            repo.Dispose();
        }

        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
        catch (UnauthorizedAccessException)
        {
            // best-effort temp cleanup
        }
    }

    /// <summary>A flat stone floor, Ada on foot in the middle of it with her robot switched on beside her; her client has
    /// the robot dialogue (<c>robottalk1</c>) unless <paramref name="dialogClient"/> is false.</summary>
    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner, CombatEntity Robot) Started(string name, bool dialogClient = true)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        _repos.Add(repo);
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            ViewDistanceChunks = 1,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();

        var stone = _content.GetBlock("stone")!.NumericId;
        for (int x = -6; x <= 6; x++)
        {
            for (int z = -6; z <= 6; z++)
            {
                server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y - 1, Centre.Z + z), stone);
                for (int y = 0; y < 3; y++)
                {
                    server.World.SetBlock(new Vector3i(Centre.X + x, Centre.Y + y, Centre.Z + z), BlockId.Air);
                }
            }
        }

        var owner = server.AddLocalPlayer(Ada);
        if (dialogClient)
        {
            owner.ClientFeatures.Add(ClientFeatures.RobotTalk1);
        }

        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1);
        Assert.True(server.DeployRobotForTest(Ada));
        var robot = server.RobotEntityForTest(Ada)!;
        robot.Position = new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z + 0.5f);
        return (server, transport, owner, robot);
    }

    private static void Talk(SvGameServer server, PlayerSession who)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new TalkToCompanionIntent { CompanionId = who.State.TamedCreatures.Single().Id }));

    private static void Choose(SvGameServer server, PlayerSession who, int index)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new NpcDialogChoiceIntent { ChoiceIndex = index, NpcId = NpcDialogState.RobotNpcId }));

    private static List<NpcDialogState> Panels(RecordingTransport t, PlayerSession who)
        => t.Sent.Where(x => x.Conn == who.ConnectionId).Select(x => x.Msg).OfType<NpcDialogState>().ToList();

    /// <summary>Opens a talk, picks <paramref name="index"/> and returns the robot's closing line.</summary>
    private static NpcDialogState Converse(SvGameServer server, RecordingTransport t, PlayerSession who, int index)
    {
        t.Sent.Clear();
        server.AdvanceUptimeForTest(1.0); // past the E cooldown
        Talk(server, who);
        Choose(server, who, index);
        return Panels(t, who).Last();
    }

    [Fact]
    public void E_OpensTheConversation_WithTheFourReplies_AndHoldsTheRobotStill()
    {
        var (server, t, owner, robot) = Started("rd_open");

        Talk(server, owner);

        var panel = Assert.Single(Panels(t, owner));
        Assert.Equal(NpcDialogState.RobotNpcId, panel.NpcId);
        Assert.Equal(owner.State.TamedCreatures.Single().Species.Name, panel.Name);
        Assert.False(panel.End);
        Assert.Contains(Ada, panel.Text, StringComparison.Ordinal); // the greeting names the player
        Assert.Equal(new[]
        {
            _en.Get(RobotDialogs.HowLabelKey), _en.Get(RobotDialogs.TellLabelKey), _en.Get(RobotDialogs.AskLabelKey), _en.Get(RobotDialogs.WaitLabelKey),
        }, panel.Choices);
        Assert.True(server.RobotConversationOpenForTest(Ada));
        Assert.Empty(t.Sent.Where(x => x.Conn == owner.ConnectionId).Select(x => x.Msg).OfType<ShipAiLine>()); // no VEGA-panel line

        var before = robot.Position;
        server.MoveCompanionsForTest(1.0);
        Assert.Equal(before, robot.Position); // it listens
    }

    [Fact]
    public void AnOlderClient_StillHearsTheSingleLineInVegasPanel()
    {
        var (server, t, owner, _) = Started("rd_old", dialogClient: false);

        Talk(server, owner);

        Assert.Empty(Panels(t, owner));
        var line = Assert.Single(t.Sent.Where(x => x.Conn == owner.ConnectionId).Select(x => x.Msg).OfType<ShipAiLine>());
        Assert.Equal(RobotChatter.SpeakerId, line.Speaker);
    }

    [Fact]
    public void OutOfReach_NoConversation()
    {
        var (server, t, owner, robot) = Started("rd_far");
        robot.Position = new Vector3f(Centre.X + 30.5f, Centre.Y, Centre.Z + 0.5f);

        Talk(server, owner);

        Assert.Empty(Panels(t, owner));
        Assert.False(server.RobotConversationOpenForTest(Ada));
    }

    [Fact]
    public void HowAreYou_AnswersToTheMoment()
    {
        var (server, t, owner, robot) = Started("rd_mood");
        var tc = owner.State.TamedCreatures.Single();

        tc.GearWeapon = "plasma_blaster";
        var weapon = Enumerable.Range(0, 8).Select(_ => Converse(server, t, owner, RobotDialogs.HowAreYou)).ToList();
        string blaster = _en.Get(_content.GetItem("plasma_blaster")!.NameKey);
        Assert.All(weapon, p => Assert.True(p.End));
        Assert.Contains(weapon, p => p.Text.Contains(blaster, StringComparison.Ordinal)); // the weapon line names it

        robot.RobotLastFightAt = server.UptimeForTest - 5.0; // a fight a moment ago wins over everything else
        var fight = Converse(server, t, owner, RobotDialogs.HowAreYou);
        Assert.Equal(_en.Get(RobotDialogs.MoodKey(RobotDialogs.MoodFight)), fight.Text);
    }

    [Fact]
    public void AskMe_AsksAQuestion_RemembersTheAnswer_AndNeverRepeatsBeforeAllWereAsked()
    {
        var (server, t, owner, _) = Started("rd_ask");
        var asked = new List<int>();
        for (int i = 0; i < RobotDialogs.QuestionCount; i++)
        {
            t.Sent.Clear();
            server.AdvanceUptimeForTest(1.0);
            Talk(server, owner);
            Choose(server, owner, RobotDialogs.AskMe);
            var question = Panels(t, owner).Last();
            Assert.False(question.End);
            Assert.Equal(RobotDialogs.AnswerCount, question.Choices.Length);
            int q = Enumerable.Range(1, RobotDialogs.QuestionCount).Single(n => _en.Get(RobotDialogs.QuestionKey(n)) == question.Text);
            Assert.DoesNotContain(q, asked);
            asked.Add(q);

            int answer = i % RobotDialogs.AnswerCount; // 0-based pick
            Choose(server, owner, answer);
            var reaction = Panels(t, owner).Last();
            Assert.True(reaction.End);
            Assert.Equal(_en.Get(RobotDialogs.ReactionKey(q, answer + 1)), reaction.Text);
            Assert.Contains(RobotDialogs.AnswerMilestone(q, answer + 1), owner.State.Milestones);
            Assert.False(server.RobotConversationOpenForTest(Ada));
        }

        // All six answered: "Ask me something!" now recalls one of the answers.
        var recall = Converse(server, t, owner, RobotDialogs.AskMe);
        Assert.True(recall.End);
        Assert.StartsWith(_en.Get(RobotDialogs.AskDoneKey), recall.Text, StringComparison.Ordinal);
        const int First = 1; // recalls go through the answered questions in turn, the lowest number first
        Assert.Contains(_en.Get(RobotDialogs.RecallKey(First, RobotDialogs.StoredAnswer(owner.State.Milestones, First))), recall.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void TellMe_PlaysItsStoryLines_NotItsQuestions_AndNowAndThenAMemory()
    {
        var (server, t, owner, _) = Started("rd_tell");
        var storyLines = RobotDialogs.TellLines.Select(n => _en.Get(RobotChatter.LineKey(n))).ToHashSet(StringComparer.Ordinal);
        var questionLines = RobotDialogs.ChatterQuestionLines.Select(n => _en.Get(RobotChatter.LineKey(n))).ToHashSet(StringComparer.Ordinal);

        var told = Enumerable.Range(0, 3).Select(_ => Converse(server, t, owner, RobotDialogs.TellMe).Text).ToList();
        Assert.All(told, line => Assert.Contains(line, storyLines)); // nothing answered yet: every third is a story line too
        Assert.All(told, line => Assert.DoesNotContain(line, questionLines));
        Assert.Equal(told.Count, told.Distinct().Count());

        owner.State.Milestones.Add(RobotDialogs.AnswerMilestone(1, 2));
        var more = Enumerable.Range(0, 3).Select(_ => Converse(server, t, owner, RobotDialogs.TellMe).Text).ToList();
        Assert.Contains(_en.Get(RobotDialogs.RecallKey(1, 2)), more); // the sixth tell recalls the answer
    }

    [Fact]
    public void WaitHere_KeepsTheRobotOnItsSpot_UntilFollowMe()
    {
        var (server, t, owner, robot) = Started("rd_wait");

        var waiting = Converse(server, t, owner, RobotDialogs.FollowOrWait);
        Assert.Equal(_en.Get(RobotDialogs.WaitLineKey), waiting.Text);
        Assert.True(robot.WaitHere);

        owner.State.Position = new Vector3f(Centre.X - 4.5f, Centre.Y, Centre.Z + 0.5f);
        var spot = robot.Position;
        server.MoveCompanionsForTest(1.0);
        Assert.Equal(spot, robot.Position);

        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        t.Sent.Clear();
        server.AdvanceUptimeForTest(1.0);
        Talk(server, owner);
        Assert.Equal(_en.Get(RobotDialogs.FollowLabelKey), Panels(t, owner).Last().Choices[RobotDialogs.FollowOrWait]);
        Choose(server, owner, RobotDialogs.FollowOrWait);
        Assert.Equal(_en.Get(RobotDialogs.FollowLineKey), Panels(t, owner).Last().Text);
        Assert.False(robot.WaitHere);
    }

    [Fact]
    public void WalkingAway_EndsTheTalk_AndClosesThePanel_TheLeaveEndsItToo()
    {
        var (server, t, owner, robot) = Started("rd_away");
        Talk(server, owner);
        Assert.True(server.RobotConversationOpenForTest(Ada));

        owner.State.Position = new Vector3f(Centre.X + 20.5f, Centre.Y, Centre.Z + 0.5f);
        t.Sent.Clear();
        server.TickRobotConversationForTest(Ada);

        Assert.False(server.RobotConversationOpenForTest(Ada));
        var close = Assert.Single(Panels(t, owner));
        Assert.Equal(NpcDialogState.ActionClose, close.Action);
        Assert.Equal(NpcDialogState.RobotNpcId, close.NpcId);
        Assert.Equal(0, robot.TalkHoldUntil);

        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        server.AdvanceUptimeForTest(1.0);
        Talk(server, owner);
        Assert.True(server.RobotConversationOpenForTest(Ada));
        Choose(server, owner, NpcDialogChoiceIntent.Leave);
        Assert.False(server.RobotConversationOpenForTest(Ada));
    }

    [Theory]
    [InlineData("en", 160)]
    [InlineData("de", 200)]
    public void EveryDialogueKey_IsTranslated_Short_AndKeepsItsSlots(string code, int max)
    {
        string path = Path.Combine(TestPaths.DataDir(), "locales", code + ".json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var table = doc.RootElement.EnumerateObject().ToDictionary(e => e.Name, e => e.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
        foreach (string key in RobotDialogs.AllKeys())
        {
            Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), $"{code}.json lacks {key}");
            Assert.True(text!.Length <= max, $"{code}: {key} has {text.Length} characters (max {max})");
            Assert.DoesNotContain("{1}", text, StringComparison.Ordinal);
        }

        for (int n = 1; n <= RobotDialogs.GreetingCount; n++)
        {
            Assert.Contains("{0}", table[RobotDialogs.GreetingKey(n)], StringComparison.Ordinal); // the player's name
        }

        Assert.Contains("{0}", table[RobotDialogs.MoodKey(RobotDialogs.MoodWeapon)], StringComparison.Ordinal); // the weapon's name
    }

    [Fact]
    public void TheTables_HoldNoDialogueKeyTheCodeDoesNotUse()
    {
        string path = Path.Combine(TestPaths.DataDir(), "locales", "en.json");
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var english = doc.RootElement.EnumerateObject().Select(e => e.Name)
            .Where(k => k.StartsWith("robotdialog.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(RobotDialogs.AllKeys().ToHashSet(StringComparer.Ordinal), english); // a new line raises its count
    }

    [Fact]
    public void Moods_UrgentFirst_ThenEveryFittingOne_PlusTheHappyDefault()
    {
        Assert.Equal(new[] { RobotDialogs.MoodFight }, RobotDialogs.MoodCandidates(new RobotMoodFacts { JustFought = true, Night = true }));
        Assert.Equal(new[] { RobotDialogs.MoodHurt }, RobotDialogs.MoodCandidates(new RobotMoodFacts { OwnerHurt = true, HasWeapon = true }));
        Assert.Equal(new[] { RobotDialogs.MoodHappy }, RobotDialogs.MoodCandidates(new RobotMoodFacts()));
        Assert.Equal(
            new[] { RobotDialogs.MoodJetpack, RobotDialogs.MoodStorm, RobotDialogs.MoodGas, RobotDialogs.MoodHappy },
            RobotDialogs.MoodCandidates(new RobotMoodFacts { HasJetpack = true, Storm = true, WorldMood = RobotDialogs.WorldMoodOf("gas_giant", false) }));
        Assert.Equal(RobotDialogs.MoodAirless, RobotDialogs.WorldMoodOf("asteroid_crystal", asteroid: true));
        Assert.Equal(string.Empty, RobotDialogs.WorldMoodOf("rocky", asteroid: false));
    }
}
