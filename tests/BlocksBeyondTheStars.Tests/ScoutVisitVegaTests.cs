// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2602 side fix — one VEGA message per bandit-scout visit. VEGA's own explanation (<c>vega.sys.base_scouts</c>, once per
/// session) and the guard's report she passes on without a radio (<c>vega.news.guard</c>, #2596) used to BOTH arrive for
/// the same two scouts. Now a guard on duty reports the visit and VEGA keeps quiet; without one VEGA explains — and a
/// guard waking up during that visit reports by radio only, never through her a second time.
/// </summary>
public sealed class ScoutVisitVegaTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static readonly string[] ScoutLines = { "vega.sys.base_scouts", "vega.news.guard" };

    private static NpcLifeWorld World(string name)
        => new(Content.Value, name, padHalf: 48, configure: c =>
        {
            c.Rules.BaseVisitors = true;
            c.Rules.Bandits = AlienActivity.Normal;
            c.Rules.PlanetEnemies = AlienActivity.Rare;
            c.Rules.FriendlyVisitors = false; // only the scouts come by in these tests
        });

    private static List<string> ScoutVega(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId && s.Msg is ShipAiLine l && ScoutLines.Contains(l.LineKey))
            .Select(s => ((ShipAiLine)s.Msg).LineKey).ToList();

    /// <summary>A walled base whose resident is its guard (the night shift).</summary>
    private static int GuardedBase(NpcLifeWorld w)
    {
        int baseId = w.FoundBase();
        w.WallRing(9);
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, w.Feet, NpcLifeWorld.Cz + 1.5f);
        w.Server.ScanBaseLifeForTest();
        Assert.Equal("guard", w.Server.BaseResidentsForTest(baseId).Single().Job);
        return baseId;
    }

    /// <summary>Ticks the night until the guard has sent the scouts of <paramref name="baseId"/> away.</summary>
    private static void UntilTheGuardSendsThemAway(NpcLifeWorld w, int baseId)
    {
        bool sentAway = false;
        w.TickAt(0.9, 55, every: () => sentAway |= w.Server.Bandits.Any(b => b.ScoutBaseId == baseId && b.BanditPhase == BanditPhase.Leaving),
            until: () => sentAway);
        Assert.True(sentAway, "the guard never noticed the scouts");
    }

    [Fact]
    public void AGuardOnDuty_ReportsTheScouts_AndVegaDoesNotExplainThemAsWell()
    {
        using var w = World("scouts_vega_guard");
        int baseId = GuardedBase(w);
        w.TickAt(0.9, 20); // night: the guard is up and on its round

        Assert.True(w.Server.SpawnScoutsForTest(baseId));
        UntilTheGuardSendsThemAway(w, baseId);

        Assert.Equal(new[] { "vega.news.guard" }, ScoutVega(w));
        Assert.True(w.Server.ScoutVisitToldByVegaForTest(baseId));
    }

    [Fact]
    public void WithoutAGuardOnDuty_VegaExplains_AndTheGuardWakingUpDoesNotRepeatItThroughHer()
    {
        using var w = World("scouts_vega_asleep");
        int baseId = GuardedBase(w);
        w.TickAt(0.4, 10); // by day the guard of the night shift is asleep

        Assert.True(w.Server.SpawnScoutsForTest(baseId));
        Assert.Equal(new[] { "vega.sys.base_scouts" }, ScoutVega(w));
        Assert.True(w.Server.ScoutVisitToldByVegaForTest(baseId));

        UntilTheGuardSendsThemAway(w, baseId); // night falls: the guard wakes, sees them and warns — not via VEGA again
        Assert.Equal(new[] { "vega.sys.base_scouts" }, ScoutVega(w));
    }

    [Fact]
    public void ABaseWithoutAGuard_StillHearsVegasExplanation()
    {
        using var w = World("scouts_vega_noguard");
        int baseId = w.FoundBase(); // no walls, no sentry: the resident has no guard's job
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx - 1.5f, w.Feet, NpcLifeWorld.Cz + 1.5f);
        w.Server.ScanBaseLifeForTest();
        Assert.DoesNotContain(w.Server.BaseResidentsForTest(baseId), r => r.Job == "guard");

        Assert.True(w.Server.SpawnScoutsForTest(baseId));
        Assert.Equal(new[] { "vega.sys.base_scouts" }, ScoutVega(w));
    }
}
