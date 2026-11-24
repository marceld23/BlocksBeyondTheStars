// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3633: the departure board's forecast (<see cref="RailEta"/>) against the REAL server's public train on a generated
/// intercity line: every forecast taken from a train list the client received — standing at the other station, on its way,
/// standing here — names the moment the train really halts at (or departs from) the stop, to within a second and a half.
/// </summary>
public sealed class RailEtaTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>A meadow world on generation 40 whose two towns share a 178-block line (probed 2026-11-14 with the client
    /// harness's settings); the search behind it only runs when a content change moved the towns.</summary>
    private const long Seed = 1;

    private const double Tolerance = 1.5;

    /// <summary>A harness on a meadow world that has a line: the known seed first, a short search behind it.</summary>
    private static ClientServerHarness WorldWithLine()
    {
        foreach (long seed in new[] { Seed }.Concat(Enumerable.Range(1, 12).Select(i => (long)i).Where(i => i != Seed)))
        {
            var h = new ClientServerHarness(Content, c =>
            {
                c.Seed = seed;
                c.StartPlanet = "meadowlands";
                c.IntercityRailChance = 1.0;
                c.World.TerrainGeneration = WorldDescription.GeographyGeneration;
            });
            if (h.Server.IntercityRailForTest() is not null)
            {
                return h;
            }

            h.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No intercity line on 'meadowlands' across thirteen seeds.");
    }

    [Fact]
    public void TheForecast_MeetsTheRealTrain_AtBothStations()
    {
        using var h = WorldWithLine();
        RailList? rails = null;
        WorldEnvironment? env = null;
        TrainList? trains = null;
        h.Client.RailsReceived += m => rails = m;
        h.Client.WorldEnvironmentReceived += m => env = m;
        h.Client.TrainsReceived += m => trains = m;
        h.Join();
        Assert.True(h.PumpUntil(() => rails is { Lines.Length: > 0 } && env != null && trains is { Trains.Length: > 0 }, 200),
            $"the line, the environment and the train arrive (lines {rails?.Lines.Length}, trains {trains?.Trains.Length})");

        var line = Assert.Single(rails!.Lines);
        var points = new List<Vector3f>();
        for (int i = 0; i + 2 < line.Points.Length; i += 3)
        {
            points.Add(new Vector3f(line.Points[i], line.Points[i + 1], line.Points[i + 2]));
        }

        var eta = new RailEta(RailSpline.Build(points, line.Closed, env!.Circumference), line.StopArcs);
        Assert.Equal(2, eta.StopCount);

        // Pump for two rounds; at every list, forecast both stops; check each forecast when the event it names happens.
        var pending = new List<(int Stop, RailEtaState State, double Due, double Made)>();
        var events = new List<string>();
        int checkedArrivals = 0, checkedDepartures = 0;
        bool wasHalted = Assert.Single(trains!.Trains).Halted;
        double now = 0;
        TrainList? seen = null;
        for (int step = 0; step < 4000 && (checkedArrivals < 3 || checkedDepartures < 2); step++)
        {
            h.Tick(0.1);
            now += 0.1;
            if (step % 100 == 0)
            {
                // A player stands on the platform: the client says where it is now and then, like the game does all the
                // time — a client silent for 90 s is dropped (the session's heartbeat).
                var me = h.Server.Sessions.Values.First().State;
                h.Client.SendMove(me.Position, me.Yaw, 0f);
            }

            if (ReferenceEquals(trains, seen))
            {
                continue;
            }

            seen = trains;
            var train = Assert.Single(trains!.Trains);
            int at = eta.StopNearest(RailRules.MiddleArc(train.Arc, RailRules.PoseFacing(train.Facing, train.Direction),
                RailRules.TrainSpan(train.Wagons.Length)));
            if (train.Halted && !wasHalted)
            {
                events.Add($"{now:0.0}s halt@{at} arc {train.Arc:0.0} left {train.HaltRemaining:0.0}");
                checkedArrivals += Settle(pending, at, RailEtaState.Coming, now);
            }
            else if (!train.Halted && wasHalted)
            {
                events.Add($"{now:0.0}s go@{at} arc {train.Arc:0.0}");
                checkedDepartures += Settle(pending, at, RailEtaState.Here, now);
            }

            wasHalted = train.Halted;
            for (int stop = 0; stop < eta.StopCount; stop++)
            {
                var reading = eta.Read(train, stop);
                Assert.NotEqual(RailEtaState.None, reading.State); // the public train always comes
                if (!pending.Any(p => p.Stop == stop && p.State == reading.State && now - p.Made < 4.0))
                {
                    pending.Add((stop, reading.State, now + reading.Seconds, now)); // a forecast every few seconds is plenty
                }
            }
        }

        string log = $"line {eta.Spline.TotalArc:0} blocks, stops {line.StopArcs[0]:0.0}/{line.StopArcs[1]:0.0}: " + string.Join("; ", events);
        Assert.True(checkedArrivals >= 3, $"three arrivals checked (saw {checkedArrivals}) — {log}");
        Assert.True(checkedDepartures >= 2, $"two departures checked (saw {checkedDepartures}) — {log}");
    }

    /// <summary>The forecasts an event settles: every one for this stop and kind made before it must name its moment.</summary>
    private static int Settle(List<(int Stop, RailEtaState State, double Due, double Made)> pending, int stop, RailEtaState state, double now)
    {
        var due = pending.Where(p => p.Stop == stop && p.State == state).ToList();
        foreach (var p in due)
        {
            Assert.True(Math.Abs(p.Due - now) <= Tolerance,
                $"stop {stop} {state}: forecast at {p.Made:0.0} s said {p.Due:0.0} s, it happened at {now:0.0} s");
        }

        pending.RemoveAll(p => p.Stop == stop && p.State == state);
        return due.Count > 0 ? 1 : 0;
    }

    [Fact]
    public void AManualTrain_HasNoForecast_AndAStandingOneCountsDownHere()
    {
        var spline = RailSpline.Build(new[] { new Vector3f(0, 10, 0), new Vector3f(60, 10, 0), new Vector3f(120, 10, 0) }, false, 4096);
        var eta = new RailEta(spline, new[] { 10f, spline.TotalArc - 10f });
        float span = RailRules.TrainSpan(2);
        var standing = new NetTrain
        {
            // Halted centred on the first stop, as the server halts it.
            Arc = RailRules.ArcForMiddleAt(RailRules.HaltMiddleArc(10f, span, spline.TotalArc, false), -1, span),
            Facing = -1,
            Direction = 1,
            Speed = 2,
            Autopilot = true,
            Halted = true,
            HaltRemaining = 12f,
            Wagons = RailRules.PublicTrainWagons,
            OwnerId = RailRules.PublicOwnerId,
        };
        int here = eta.StopNearest(RailRules.MiddleArc(standing.Arc, -1, span));
        Assert.Equal(0, here);

        var reading = eta.Read(standing, here);
        Assert.Equal(RailEtaState.Here, reading.State);
        Assert.Equal(12.0, reading.Seconds, 3);

        var other = eta.Read(standing, 1 - here);
        Assert.Equal(RailEtaState.Coming, other.State);
        Assert.True(other.Seconds > 12.0, "it first waits out its halt");

        standing.Autopilot = false;
        Assert.Equal(RailEtaState.None, eta.Read(standing, 1 - here).State);
    }

    /// <summary>#3985: on a line of three stops the board names the stops the next train calls at, in order — at the middle
    /// one those beyond it the way the train leaves, at the first one both others (it turns round there while it stands).</summary>
    [Fact]
    public void TheBoard_NamesTheNextStops_TheWayTheTrainLeaves()
    {
        var points = Enumerable.Range(0, 11).Select(i => new Vector3f(i * 30f, 10f, 0f)).ToList();
        var spline = RailSpline.Build(points, false, 4096);
        var eta = new RailEta(spline, new[] { 10f, spline.TotalArc * 0.5f, spline.TotalArc - 10f });
        float span = RailRules.TrainSpan(2);
        var train = new NetTrain
        {
            // Halted at the middle stop, heading on toward the far end.
            Arc = RailRules.ArcForMiddleAt(RailRules.HaltMiddleArc(spline.TotalArc * 0.5f, span, spline.TotalArc, false), -1, span),
            Facing = -1,
            Direction = 1,
            Speed = 2,
            Autopilot = true,
            Halted = true,
            HaltRemaining = 5f,
            Wagons = RailRules.PublicTrainWagons,
            OwnerId = RailRules.PublicOwnerId,
        };

        var here = eta.Read(train, 1);
        Assert.Equal(RailEtaState.Here, here.State);
        Assert.Equal(1, here.Direction);
        Assert.Equal(new[] { 2 }, eta.StopsAfter(1, here.Direction));

        var first = eta.Read(train, 0); // out to the far end, back past the middle, turned round at the first stop
        Assert.Equal(RailEtaState.Coming, first.State);
        Assert.Equal(1, first.Direction);
        Assert.Equal(new[] { 1, 2 }, eta.StopsAfter(0, first.Direction));
        Assert.Equal(new[] { 1, 0 }, eta.StopsAfter(2, -1));
        Assert.Empty(eta.StopsAfter(1, 0));
    }
}
