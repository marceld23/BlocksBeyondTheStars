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
using BlocksBeyondTheStars.Shared.Primitives;
using Microsoft.Data.Sqlite;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The talking robot (#3394-#3396, Justus' idea): E at the own robot companion makes it speak in VEGA's panel under its own
/// name; about one talk in three VEGA asks it something and it answers; now and then it speaks by itself (an advisor line,
/// muted with VEGA's hints). Its pools play without repeats until all were heard.
/// </summary>
public sealed class RobotChatterTests : IDisposable
{
    private const string Ada = "Ada";

    /// <summary>Beyond the robot's ambient range (it speaks by itself only close to its owner).</summary>
    private const float RobotAmbientFar = SvGameServer.RobotAmbientRange + 4f;

    private static readonly Vector3i Centre = new(300, 150, 300);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_robot_talk_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

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

    /// <summary>A flat stone floor, Ada on foot in the middle of it with her robot switched on beside her.</summary>
    private (SvGameServer Server, RecordingTransport Transport, PlayerSession Owner, CombatEntity Robot) Started(string name)
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
        owner.State.AboardShip = false;
        owner.State.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + 0.5f);
        owner.State.Inventory.Add(SvGameServer.HunterRobotItem, 1, 1);
        Assert.True(server.DeployRobotForTest(Ada));
        var robot = server.RobotEntityForTest(Ada)!;
        robot.Position = new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z + 0.5f);
        return (server, transport, owner, robot);
    }

    private static string RobotId(PlayerSession owner) => owner.State.TamedCreatures.Single().Id;

    private static void Talk(SvGameServer server, PlayerSession who, string companionId)
        => server.HandlePayloadForTest(who.ConnectionId, NetCodec.Encode(new TalkToCompanionIntent { CompanionId = companionId }));

    private static List<ShipAiLine> ChatterTo(RecordingTransport t, PlayerSession who)
        => t.Lines(who).Where(l => RobotChatter.IsChatterKey(l.LineKey)).ToList();

    private static int Counter(PlayerSession who, string counter)
        => who.State.AchievementCounters.TryGetValue(counter, out int v) ? v : 0;

    /// <summary>The first talk number from <paramref name="from"/> on that is (or is not) an exchange for Ada.</summary>
    private static int FirstTalk(bool exchange, int from = 0)
    {
        int t = from;
        while (RobotChatter.IsExchange(Ada, t) != exchange)
        {
            t++;
        }

        return t;
    }

    // ---------------- talking with E (#3394) ----------------

    [Fact]
    public void TalkingToTheOwnRobot_ItSpeaksAStoryLine_UnderItsOwnName()
    {
        var (server, transport, owner, _) = Started("talk_line");
        owner.State.AchievementCounters[RobotChatter.LinesHeardCounter] = FirstTalk(exchange: false);
        owner.State.TamedCreatures.Single().Name = "Blinky"; // renamed like any companion

        Talk(server, owner, RobotId(owner));

        var line = Assert.Single(ChatterTo(transport, owner));
        Assert.StartsWith("robot.talk.", line.LineKey, StringComparison.Ordinal);
        Assert.Equal(RobotChatter.SpeakerId, line.Speaker);
        Assert.Equal("Blinky", line.SpeakerName);
        Assert.Equal("Blinky", line.LineArg);
        Assert.Equal(2, line.Kind); // an answer the player asked for — never muted
        Assert.Equal(FirstTalk(exchange: false) + 1, Counter(owner, RobotChatter.LinesHeardCounter));
    }

    [Fact]
    public void AnEmptyCompanionId_NamesTheSendersRobot_WithItsDefaultName()
    {
        var (server, transport, owner, _) = Started("talk_default");

        Talk(server, owner, string.Empty);

        var spoken = ChatterTo(transport, owner);
        Assert.NotEmpty(spoken);
        Assert.Equal("Robo", spoken[^1].SpeakerName);
    }

    [Fact]
    public void ThePool_NeverRepeats_UntilEveryLineWasHeard()
    {
        var (server, transport, owner, _) = Started("talk_pool");
        string id = RobotId(owner);

        for (int i = 0; i < 80 && Counter(owner, RobotChatter.LinesHeardCounter) < RobotChatter.LineCount; i++)
        {
            Talk(server, owner, id);
            server.AdvanceUptimeForTest(SvGameServer.RobotTalkCooldownSeconds + 0.1);
        }

        var own = ChatterTo(transport, owner).Where(l => l.LineKey.StartsWith("robot.talk.", StringComparison.Ordinal)).Select(l => l.LineKey).ToList();
        Assert.Equal(RobotChatter.LineCount, own.Count);
        Assert.Equal(RobotChatter.LineCount, own.Distinct().Count()); // every line once before any repeats
        Assert.True(Counter(owner, RobotChatter.ExchangesHeardCounter) > 0, "about one talk in three should have been an exchange");
    }

    [Fact]
    public void AnExchange_SendsVegasQuestionFirst_ThenTheRobotsAnswer()
    {
        var (server, transport, owner, _) = Started("talk_exchange");
        owner.State.AchievementCounters[RobotChatter.LinesHeardCounter] = FirstTalk(exchange: true);

        Talk(server, owner, RobotId(owner));

        var spoken = ChatterTo(transport, owner);
        Assert.Equal(2, spoken.Count);
        var (question, answer) = (spoken[0], spoken[1]);
        Assert.Matches(@"^robot\.vega\.\d+\.q$", question.LineKey);
        Assert.Equal(string.Empty, question.Speaker); // VEGA asks
        Assert.Equal(string.Empty, question.SpeakerName);
        Assert.Equal("Robo", question.LineArg);       // … the robot by its name
        Assert.Equal(question.LineKey[..^1] + "a", answer.LineKey);
        Assert.Equal(RobotChatter.SpeakerId, answer.Speaker);
        Assert.Equal("Robo", answer.SpeakerName);
        Assert.Equal(1, Counter(owner, RobotChatter.ExchangesHeardCounter));
    }

    [Fact]
    public void ACooldown_KeepsAHeldKeyFromFloodingThePanel()
    {
        var (server, transport, owner, _) = Started("talk_cooldown");
        string id = RobotId(owner);

        Talk(server, owner, id);
        int first = ChatterTo(transport, owner).Count;
        Talk(server, owner, id);
        Assert.Equal(first, ChatterTo(transport, owner).Count);

        server.AdvanceUptimeForTest(SvGameServer.RobotTalkCooldownSeconds + 0.1);
        Talk(server, owner, id);
        Assert.True(ChatterTo(transport, owner).Count > first);
    }

    [Fact]
    public void AForeignRobot_NoRobot_OrOneTooFarAway_StaysSilent()
    {
        var (server, transport, owner, robot) = Started("talk_refused");
        var ben = server.AddLocalPlayer("Ben");
        ben.State.AboardShip = false;
        ben.State.Position = owner.State.Position;

        Talk(server, ben, RobotId(owner)); // Ada's robot answers only Ada
        Talk(server, ben, string.Empty);   // Ben has none
        Assert.Empty(ChatterTo(transport, ben));
        Assert.Empty(ChatterTo(transport, owner));

        robot.Position = new Vector3f(Centre.X + 10.5f, Centre.Y, Centre.Z + 0.5f); // across the yard
        Talk(server, owner, RobotId(owner));
        Assert.Empty(ChatterTo(transport, owner));
        Assert.Empty(transport.To<ActionRejected>(owner)); // ignored silently — no toast
    }

    // ---------------- by itself (#3396) ----------------

    [Fact]
    public void ByItself_NotBeforeTheFirstInterval_ThenOnce_AndTheNextGapIsRolledAgain()
    {
        var (server, transport, owner, _) = Started("ambient_due");
        server.TickRobotChatterForTest(Ada); // the first pass after the join arms it

        server.AdvanceUptimeForTest(RobotChatter.FirstAmbientMinSeconds - 1);
        server.TickRobotChatterForTest(Ada);
        Assert.Empty(ChatterTo(transport, owner)); // never in the first minutes

        server.AdvanceUptimeForTest(RobotChatter.FirstAmbientMaxSeconds);
        server.TickRobotChatterForTest(Ada);
        var spoken = ChatterTo(transport, owner);
        Assert.NotEmpty(spoken);
        Assert.All(spoken, l => Assert.Equal(1, l.Kind)); // an advisor line: muted with VEGA's hints
        Assert.Equal(RobotChatter.SpeakerId, spoken[^1].Speaker);

        int count = spoken.Count;
        server.AdvanceUptimeForTest(RobotChatter.AmbientMinSeconds - 1);
        server.TickRobotChatterForTest(Ada);
        Assert.Equal(count, ChatterTo(transport, owner).Count); // the next gap is 10-20 minutes

        server.AdvanceUptimeForTest(RobotChatter.AmbientMaxSeconds);
        server.TickRobotChatterForTest(Ada);
        Assert.True(ChatterTo(transport, owner).Count > count);
    }

    [Fact]
    public void ByItself_NeverWhileFarAwayAboardInAFightOrMuted_AndItWaitsForTheNextGoodMoment()
    {
        var (server, transport, owner, robot) = Started("ambient_gates");
        server.TickRobotChatterForTest(Ada);
        server.AdvanceUptimeForTest(RobotChatter.FirstAmbientMaxSeconds + 1);

        robot.Position = new Vector3f(Centre.X + 0.5f, Centre.Y, Centre.Z + RobotAmbientFar);
        server.TickRobotChatterForTest(Ada);
        robot.Position = new Vector3f(Centre.X + 2.5f, Centre.Y, Centre.Z + 0.5f);

        owner.State.AboardShip = true;
        server.TickRobotChatterForTest(Ada);
        owner.State.AboardShip = false;

        robot.RobotLastFightAt = double.MaxValue; // in a fight right now (later than any uptime)
        server.TickRobotChatterForTest(Ada);
        robot.RobotLastFightAt = double.MinValue;

        owner.Vega.VegaHintsMuted = true;
        server.TickRobotChatterForTest(Ada);
        owner.Vega.VegaHintsMuted = false;
        Assert.Empty(ChatterTo(transport, owner));

        server.TickRobotChatterForTest(Ada); // a good moment at last: the line was still due
        Assert.NotEmpty(ChatterTo(transport, owner));
    }

    // ---------------- the wire ----------------

    [Fact]
    public void TheIntentAndTheSpeakerName_RoundTripOnBothPaths()
    {
        var intent = new TalkToCompanionIntent { CompanionId = "tc-7" };
        var line = new ShipAiLine { LineKey = "robot.talk.3", Speaker = RobotChatter.SpeakerId, SpeakerName = "Robo" };
        foreach (var payload in new[] { NetCodec.Encode(intent), NetCodec.EncodeJson(intent) })
        {
            Assert.Equal("tc-7", Assert.IsType<TalkToCompanionIntent>(NetCodec.Decode(payload)).CompanionId);
        }

        foreach (var payload in new[] { NetCodec.Encode(line), NetCodec.EncodeJson(line) })
        {
            var back = Assert.IsType<ShipAiLine>(NetCodec.Decode(payload));
            Assert.Equal("Robo", back.SpeakerName);
            Assert.Equal(RobotChatter.SpeakerId, back.Speaker);
        }

        Assert.Equal(string.Empty, Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(new ShipAiLine()))).SpeakerName); // additive
        Assert.Equal(332, (int)NetCodec.RegisteredMessageTags[typeof(TalkToCompanionIntent)]);
    }
}
