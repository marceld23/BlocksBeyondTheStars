// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// VEGA notices the player: praise for an achievement (#2597 — several unlocks within 2 s become one line, the join's
/// retro-awards stay silent, nothing during the basic training or with the hints muted, 90 s between praise lines, the
/// story category skipped) and "welcome back" (#2595 — boarding after at least 10 minutes outside, at most every half
/// hour, never after a death; a login after 18 hours or more away).
/// </summary>
public sealed class VegaChatterTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private static readonly DateTime T0 = new(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);

    private static NpcLifeWorld World(string name, Action<ServerConfig>? configure = null, bool trainingDone = true)
    {
        var w = new NpcLifeWorld(Content, name, configure: configure);
        if (trainingDone)
        {
            w.Server.SkipTutorialForTest(w.Player); // the basic training is through — VEGA may chat
        }

        w.Server.SetWallClockForTest(T0);
        return w;
    }

    private static List<ShipAiLine> Lines(NpcLifeWorld w, string prefix)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId && s.Msg is ShipAiLine l
                && l.LineKey.StartsWith(prefix, StringComparison.Ordinal))
            .Select(s => (ShipAiLine)s.Msg).ToList();

    /// <summary>Lets the praise window close and runs VEGA's chatter pass.</summary>
    private static void Settle(NpcLifeWorld w, double seconds = 2.5)
    {
        w.Server.AdvanceUptimeForTest(seconds);
        w.Server.TickVegaChatterForTest(w.Player);
    }

    // ---------------- #2597: praise ----------------

    [Fact]
    public void OneUnlock_IsPraisedByCategory_WithTheAchievementsName()
    {
        using var w = World("vega_praise_one");
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.BuildAny);
        Assert.Contains("first_build", w.Player.State.Achievements);
        Settle(w, 1);
        Assert.Empty(Lines(w, "vega.praise.")); // the coalescing window is still open

        Settle(w);
        var line = Assert.Single(Lines(w, "vega.praise."));
        Assert.Equal("vega.praise.building", line.LineKey);
        Assert.Equal(Content.CreateLocalizer(Shared.Localization.GameLocale.English).Get("achv.first_build.name"), line.LineArg);
        Assert.Equal(5, line.Kind); // the repeated-tip channel: muted with the hints, dropped while VEGA is busy
    }

    [Fact]
    public void UnlocksWithinTwoSeconds_BecomeOneLine_AndPraiseWaitsNinetySeconds()
    {
        using var w = World("vega_praise_many");
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.BuildAny);
        w.Server.AdvanceUptimeForTest(1);
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.CraftAny);
        Settle(w);

        var line = Assert.Single(Lines(w, "vega.praise."));
        Assert.Equal("vega.praise.many", line.LineKey);
        Assert.Equal("2", line.LineArg);

        // Another unlock a minute later: inside the 90 s gap — the banner shows it, VEGA stays quiet.
        w.Server.AdvanceUptimeForTest(60);
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.Craft("door_wood"));
        Assert.Contains("own_door", w.Player.State.Achievements);
        Settle(w);
        Assert.Single(Lines(w, "vega.praise."));
    }

    [Fact]
    public void TheJoinsRetroAwards_AreSilent()
    {
        using var w = World("vega_praise_settle");
        w.Player.State.AchievementCounters[AchievementCounters.BuildAny] = 3;
        w.Server.SettleAchievementsForTest(w.Player);
        Assert.Contains("first_build", w.Player.State.Achievements);
        Settle(w);
        Assert.Empty(Lines(w, "vega.praise."));
    }

    [Fact]
    public void DuringTheBasicTraining_OrWithTheHintsMuted_VegaDoesNotPraise()
    {
        using (var training = World("vega_praise_training", trainingDone: false))
        {
            Assert.False(training.Server.TutorialGateForTest(training.Player, StoryGateNames.Banter) ?? true,
                "the arena's player should still be in the basic training");
            training.Server.AdvanceAchievementForTest(training.Player, AchievementCounters.BuildAny);
            Settle(training);
            Assert.Empty(Lines(training, "vega.praise."));
        }

        using var muted = World("vega_praise_muted");
        muted.Player.VegaHintsMuted = true;
        muted.Server.AdvanceAchievementForTest(muted.Player, AchievementCounters.BuildAny);
        Settle(muted);
        Assert.Empty(Lines(muted, "vega.praise."));
    }

    [Fact]
    public void TheStoryCategory_IsLeftToTheStory()
    {
        using var w = World("vega_praise_story");
        w.Player.State.AchievementCounters[AchievementCounters.MissionCompleted] = 4;
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.MissionCompleted);
        Assert.Contains("helping_hand", w.Player.State.Achievements);
        Settle(w);
        Assert.Empty(Lines(w, "vega.praise."));
    }

    // ---------------- #2595: welcome back ----------------

    [Fact]
    public void BoardingAfterTenMinutesOutside_WelcomesBack_AtMostEveryHalfHour()
    {
        using var w = World("vega_aboard");
        var p = w.Player;
        p.State.AboardShip = false;
        Settle(w, 0); // seeds: outside since now

        w.Server.AdvanceUptimeForTest(300);
        p.State.AboardShip = true;
        Settle(w, 0);
        Assert.Empty(Lines(w, "vega.welcome.")); // five minutes outside is no homecoming

        p.State.AboardShip = false;
        Settle(w, 0);
        w.Server.AdvanceUptimeForTest(601);
        p.State.AboardShip = true;
        Settle(w, 0);
        var line = Assert.Single(Lines(w, "vega.welcome.aboard."));
        Assert.Equal(5, line.Kind);

        // Out and back again after another ten minutes: within the half hour — quiet.
        p.State.AboardShip = false;
        Settle(w, 0);
        w.Server.AdvanceUptimeForTest(700);
        p.State.AboardShip = true;
        Settle(w, 0);
        Assert.Single(Lines(w, "vega.welcome.aboard."));
    }

    [Fact]
    public void WakingUpAboardAfterADeath_IsNoHomecoming()
    {
        using var w = World("vega_aboard_respawn");
        var p = w.Player;
        p.State.AboardShip = false;
        Settle(w, 0);
        w.Server.AdvanceUptimeForTest(700);

        w.Server.KillPlayerForTest(p, "test");
        p.State.AboardShip = true; // the respawn puts the clone back aboard
        Settle(w, 0);
        Assert.Empty(Lines(w, "vega.welcome."));
    }

    [Fact]
    public void LoggingInAfterTwoDays_VegaWelcomesBack_Once()
    {
        using var w = World("vega_login");
        w.Player.PreviousSeenUtc = T0.AddDays(-2).ToString("o", CultureInfo.InvariantCulture);
        Settle(w, 0); // the join's first pass arms it
        Settle(w, 5);
        Assert.Empty(Lines(w, "vega.welcome.login.")); // a moment after the join, not on top of it

        Settle(w, 12);
        var line = Assert.Single(Lines(w, "vega.welcome.login."));
        Assert.Equal(1, line.Kind);
        Assert.Equal(NpcLifeWorld.Owner, line.LineArg.Split('\u001f').First(a => a.Length > 0));

        Settle(w, 60);
        Assert.Single(Lines(w, "vega.welcome.login."));
    }

    [Fact]
    public void LoggingInAfterAnHour_OrForTheFirstTime_IsNoWelcome()
    {
        using var w = World("vega_login_short");
        w.Player.PreviousSeenUtc = T0.AddHours(-1).ToString("o", CultureInfo.InvariantCulture);
        Settle(w, 0);
        Settle(w, 30);
        Assert.Empty(Lines(w, "vega.welcome.login."));

        using var fresh = World("vega_login_first");
        Settle(fresh, 0);
        Settle(fresh, 30);
        Assert.Empty(Lines(fresh, "vega.welcome.login."));
    }

    // ---------------- AI (hosted worlds) ----------------

    private sealed class LinesProvider : IAiMissionProvider
    {
        public List<NpcLinesRequest> Requests { get; } = new();

        public MissionPlan? Generate(string context) => null;

        public string? GenerateNpcLine(NpcLineRequest request) => null;

        public MissionTextResult? GenerateMissionText(MissionTextRequest request) => null;

        public string[]? GenerateNpcLines(NpcLinesRequest request)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }

            return request.Purpose == NpcLinePurposes.VegaPraise ? new[] { "{0}? Not bad at all, {player}." } : null;
        }
    }

    [Fact]
    public void WithAi_ThePraiseUsesTheCachedLine_NextTime_NeverLate()
    {
        var provider = new LinesProvider();
        using var w = World("vega_praise_ai", c => c.AiLevel = AiLevel.TextOnly);
        w.Server.SetAiProviderForTest(provider);
        w.Server.RunAiLinesSynchronouslyForTest();
        w.Server.SetAiVariantRollForTest(1.0);

        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.BuildAny); // first_build (building)
        Settle(w);
        var first = Assert.Single(Lines(w, "vega.praise."));
        Assert.Equal(string.Empty, first.Text); // the authored line went out at once; the AI line is for next time
        Assert.Contains(provider.Requests, r => r.Purpose == NpcLinePurposes.VegaPraise && r.Authored.Count > 0);

        // The next building achievement, past the 90 s gap: the cached wording with ITS name and the player's.
        w.Server.AdvanceUptimeForTest(100);
        w.Player.State.AchievementCounters[AchievementCounters.BuildAny] = 99;
        w.Server.AdvanceAchievementForTest(w.Player, AchievementCounters.BuildAny); // builder_100
        Assert.Contains("builder_100", w.Player.State.Achievements);
        Settle(w);
        var second = Lines(w, "vega.praise.")[1];
        string name = Content.CreateLocalizer(Shared.Localization.GameLocale.English).Get("achv.builder_100.name");
        Assert.Equal($"{name}? Not bad at all, {NpcLifeWorld.Owner}.", second.Text);
        Assert.Equal(5, second.Kind);
    }
}
