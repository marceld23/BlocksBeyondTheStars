// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — a round through the station: one of a kit station's crew members without a post walks from post
/// to post by day (a stop beside each, never on the post itself) and keeps the evening and the night like everybody
/// else; nobody is added to the crew for it.
/// </summary>
public sealed class StationRoundsTests : IDisposable
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_rounds_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best-effort temp cleanup
        }
    }

    private SvGameServer Started(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent, StationTemplateUse = Frequency.Rare, TerrainGeneration = WorldDescription.CurrentTerrainGeneration },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static void TickAt(SvGameServer server, double dayFraction, double seconds, Func<bool>? until = null, Action? every = null)
    {
        for (int i = 0; i < (int)Math.Ceiling(seconds / 0.25); i++)
        {
            server.SetLocalDayFractionForTest(dayFraction, 0f);
            server.TickForTest(0.25);
            every?.Invoke();
            if (until != null && until())
            {
                return;
            }
        }
    }

    private static double Dist(Vector3f a, Vector3f b)
    {
        double dx = a.X - b.X, dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    [Fact]
    public void OneOfTheCrewWithoutAPost_WalksFromPostToPostByDay_AndKeepsItsEvening()
    {
        bool walked = false;
        for (long seed = 1; seed <= 8 && !walked; seed++)
        {
            var server = Started("rounds_" + seed, seed, out var repo);
            using (repo)
            {
                server.AddLocalPlayer("Pilot");
                server.EnterSpace("Pilot");
                var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
                server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
                server.BoardStation("Pilot", station.Id);
                Assert.True(server.InStation("Pilot"));
                int crew = server.NpcCrewForTest().Count;

                var walkers = server.ServiceRoundWalkersForTest;
                Assert.True(walkers.Count <= 1, "more than one crew member walks the round");
                var stops = server.StationRoundStopsForTest();
                if (walkers.Count == 0 || stops.Count < 3)
                {
                    server.Stop();
                    continue; // every cabin of this station has a post (or it has too few posts): the next seed
                }

                // No stop lies on another, and the round is a loop of different places.
                for (int i = 0; i < stops.Count; i++)
                {
                    for (int j = i + 1; j < stops.Count; j++)
                    {
                        Assert.True(Dist(stops[i], stops[j]) >= 1.0 || Math.Abs(stops[i].Y - stops[j].Y) >= 1f, $"stops {i} and {j} share a spot");
                    }
                }

                int id = walkers[0];
                var visited = new HashSet<int>();
                bool onRounds = false;
                TickAt(server, 0.40, 420, until: () => visited.Count >= 3, every: () =>
                {
                    var round = server.ServiceRoundForTest(id);
                    onRounds |= round.Activity == SvGameServer.RoundsActivityKey;
                    if (onRounds && !round.Walking && Dist(server.NpcRoutineForTest(id).Pos, stops[round.Stop]) < 2.5)
                    {
                        visited.Add(round.Stop);
                    }
                });
                Assert.True(onRounds, "the walker never set out");
                Assert.True(visited.Count >= 3, $"only {visited.Count} of {stops.Count} stops were reached");
                Assert.Equal(crew, server.NpcCrewForTest().Count); // nobody was added for it

                // The evening is the routine's: the round is off its nameplate and no new stop is started.
                TickAt(server, 0.90, 20);
                var evening = server.ServiceRoundForTest(id);
                Assert.NotEqual(SvGameServer.RoundsActivityKey, evening.Activity);
                int stopAtDusk = evening.Stop;
                TickAt(server, 0.90, 60);
                Assert.Equal(stopAtDusk, server.ServiceRoundForTest(id).Stop);
                walked = true;
                server.Stop();
            }
        }

        Assert.True(walked, "no station in the searched seeds had a crew member without a post and three posts to walk");
    }

    [Fact]
    public void TheRoundsLine_IsWritten_InBothMandatoryLanguages()
    {
        foreach (var locale in new[] { BlocksBeyondTheStars.Shared.Localization.GameLocale.English, BlocksBeyondTheStars.Shared.Localization.GameLocale.German })
        {
            Assert.NotEqual(SvGameServer.RoundsActivityKey, Content.Value.CreateLocalizer(locale).Get(SvGameServer.RoundsActivityKey));
        }
    }
}
