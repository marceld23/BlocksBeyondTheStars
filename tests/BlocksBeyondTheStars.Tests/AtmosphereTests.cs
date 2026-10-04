// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Per-planet atmosphere &amp; breathability: on a breathable world the suit doesn't drain oxygen on
/// the surface, while toxic/airless worlds drain it as before. Aboard the ship always regenerates.
/// </summary>
public sealed class AtmosphereTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public AtmosphereTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_atmo_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "atmo_" + planet));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "atmo_" + planet,
            Seed = 11,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 }, // #1645: gameplay test on the classic relief — the player sits at a fixed (0, 64, 0), which generation-1 terrain may flood or bury
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    private static SvGameServer SurfacePlayer(SvGameServer server, out BlocksBeyondTheStars.Shared.State.PlayerState state)
    {
        var session = server.AddLocalPlayer("Diver");
        session.State.AboardShip = false;

        // Stand in the open air just above the surface at the origin column — the first air cell over the top
        // solid/water cell — so the breathing test isn't accidentally placed underwater when an upland pond
        // (B7) happens to sit at the origin on this seed.
        int y = 66;
        for (int yy = 160; yy > 8; yy--)
        {
            if (!server.World.GetBlock(new Vector3i(0, yy, 0)).IsAir
                && server.World.GetBlock(new Vector3i(0, yy + 1, 0)).IsAir)
            {
                y = yy + 2;
                break;
            }
        }

        session.State.Position = new Vector3f(0, y, 0);
        session.State.Oxygen = 50f;
        state = session.State;
        return server;
    }

    [Fact]
    public void BreathablePlanet_DoesNotDrainOxygenOnSurface()
    {
        var server = Started("jungle", out var repo); // breathable
        using (repo)
        {
            Assert.True(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen > 50f, "Breathable air should regenerate, not drain, oxygen.");
        }
    }

    [Fact]
    public void Eva_DrainsOxygen_EvenOverABreathableWorld()
    {
        var server = Started("jungle", out var repo); // breathable air at the surface
        using (repo)
        {
            Assert.True(server.AtmosphereBreathable);

            var session = server.AddLocalPlayer("Spacewalker");
            var p = session.State;
            p.AboardShip = true; // EVA keeps the ship bond; InEva is what forces the drain
            p.InEva = true;      // floating outside the ship in space
            p.Position = new Vector3f(0, 64, 0);
            p.Oxygen = 50f;

            server.Tick(2.0);
            Assert.True(p.Oxygen < 50f, "An EVA spacewalk must drain oxygen even over a breathable world.");
        }
    }

    [Fact]
    public void ToxicPlanet_DrainsOxygenOnSurface()
    {
        var server = Started("rocky", out var repo); // toxic
        using (repo)
        {
            Assert.False(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen < 50f, "A toxic atmosphere should drain oxygen outside the ship.");
        }
    }

    [Fact]
    public void AirlessPlanet_DrainsOxygenOnSurface()
    {
        var server = Started("crystal", out var repo); // none (airless)
        using (repo)
        {
            Assert.False(server.AtmosphereBreathable);
            SurfacePlayer(server, out var p);

            server.Tick(2.0);
            Assert.True(p.Oxygen < 50f, "An airless world should drain oxygen outside the ship.");
        }
    }

    [Fact]
    public void Environment_ReportsBreathable()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            Assert.True(server.AtmosphereBreathable);
        }
    }

    private (BlocksBeyondTheStars.Shared.State.PlayerState withExtractor, BlocksBeyondTheStars.Shared.State.PlayerState without) TwoSurfaceMiners(SvGameServer server)
    {
        var a = server.AddLocalPlayer("WithExtractor").State;
        var b = server.AddLocalPlayer("Plain").State;
        foreach (var s in new[] { a, b })
        {
            s.AboardShip = false;
            s.Position = new Vector3f(0, 64, 0);
            s.Oxygen = 50f;
        }

        TestGear.Wear(a, "oxygen_extractor");
        return (a, b);
    }

    [Fact]
    public void OxygenExtractor_SlowsDrain_OnToxicPlanet()
    {
        var server = Started("rocky", out var repo); // toxic, extractability 0.6
        using (repo)
        {
            var (withExtractor, without) = TwoSurfaceMiners(server);
            server.Tick(2.0);

            Assert.True(withExtractor.Oxygen > without.Oxygen, "The extractor should slow oxygen loss in a toxic atmosphere.");
            Assert.True(without.Oxygen < 50f); // the plain suit still drains
        }
    }

    [Fact]
    public void OxygenExtractor_GivesNoBenefit_OnAirlessPlanet()
    {
        var server = Started("crystal", out var repo); // airless (none), extractability 0
        using (repo)
        {
            var (withExtractor, without) = TwoSurfaceMiners(server);
            server.Tick(2.0);

            Assert.Equal(without.Oxygen, withExtractor.Oxygen); // nothing to extract from vacuum
        }
    }

    // ---------------- Build above the atmosphere → on-foot in space (item 10) ----------------

    [Fact]
    public void AtmosphereHeight_IsPerBody_HigherForThickAtmospheres()
    {
        var jungle = Started("jungle", out var rj); // breathable → high line
        var asteroid = Started("crystal", out var ra); // airless → lower line
        using (rj)
        using (ra)
        {
            Assert.True(jungle.AtmosphereHeight > 0);
            Assert.True(asteroid.AtmosphereHeight > 0);
            Assert.True(jungle.AtmosphereHeight > asteroid.AtmosphereHeight,
                "A thick/breathable world's atmosphere should reach higher than an airless body's.");
        }
    }

    [Fact]
    public void ClimbingAboveTheAtmosphere_EntersSpaceOnFoot_AndDrainsOxygen_EvenWhenBreathable()
    {
        var server = Started("jungle", out var repo); // breathable: surface air normally regenerates
        using (repo)
        {
            SurfacePlayer(server, out var p);
            p.Oxygen = 50f;

            server.Tick(1.0);
            Assert.False(p.AboveAtmosphere);
            Assert.True(p.Oxygen >= 50f, "On the breathable surface the suit doesn't drain.");

            // Teleport to the top of a tower above the atmosphere line.
            p.Position = new Vector3f(0, (float)server.AtmosphereHeight + 10f, 0);
            p.Oxygen = 50f;
            server.Tick(2.0);

            Assert.True(p.AboveAtmosphere, "Above the atmosphere line the player is in space on foot.");
            Assert.True(p.Oxygen < 50f, "In space the suit air drains even above a breathable world.");
        }
    }

    [Fact]
    public void DescendingBackBelowTheAtmosphere_ClearsTheSpaceState()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            SurfacePlayer(server, out var p);
            p.Position = new Vector3f(0, (float)server.AtmosphereHeight + 10f, 0);
            server.Tick(0.5);
            Assert.True(p.AboveAtmosphere);

            p.Position = new Vector3f(0, 64, 0); // climbed back down
            server.Tick(0.5);
            Assert.False(p.AboveAtmosphere, "Back below the line the player is on the ground again.");
        }
    }

    [Fact]
    public void AtmosphereLine_HasHysteresis_DoesNotFlickerJustBelow()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            SurfacePlayer(server, out var p);
            float line = (float)server.AtmosphereHeight;

            p.Position = new Vector3f(0, line + 10f, 0);
            server.Tick(0.5);
            Assert.True(p.AboveAtmosphere);

            // Dip just 2 blocks below the line (inside the hysteresis band) — stays in space.
            p.Position = new Vector3f(0, line - 2f, 0);
            server.Tick(0.5);
            Assert.True(p.AboveAtmosphere, "A small dip below the line shouldn't drop the space state.");

            // Drop clearly below the band — now it ends.
            p.Position = new Vector3f(0, line - 10f, 0);
            server.Tick(0.5);
            Assert.False(p.AboveAtmosphere);
        }
    }

    [Fact]
    public void AboardShip_NeverCountsAsAboveAtmosphere()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot").State;
            p.AboardShip = true; // life support — not an on-foot climber
            p.Position = new Vector3f(0, (float)server.AtmosphereHeight + 50f, 0);

            server.Tick(1.0);
            Assert.False(p.AboveAtmosphere, "Aboard the ship you're never 'on foot above the atmosphere'.");
        }
    }

    // ---------------- #2276: a summit (or tower) above the line is readable and survivable ----------------

    /// <summary>The top solid cell of the origin column — a player standing on it has its feet at Y + 1.</summary>
    private static int GroundY(SvGameServer server)
    {
        for (int y = 200; y > 8; y--)
        {
            if (!server.World.GetBlock(new Vector3i(0, y, 0)).IsAir)
            {
                return y;
            }
        }

        return 64;
    }

    /// <summary>Floats the player above the line, then sinks them back below it in open air — a re-entry.</summary>
    private static void FloatUpAndReEnter(SvGameServer server, BlocksBeyondTheStars.Shared.State.PlayerState p)
    {
        float line = (float)server.AtmosphereHeight;
        p.Position = new Vector3f(0, line + 10f, 0);
        server.Tick(0.2);
        Assert.True(p.AboveAtmosphere);

        p.Position = new Vector3f(0, line - 10f, 0); // below the hysteresis band, far above the ground
        server.Tick(0.2);
        Assert.False(p.AboveAtmosphere);
    }

    [Fact]
    public void ReEntry_NoFallDamageUntilTheFirstLanding_ThenFallsCountAgain()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            server.SetGravityFactorForTest(1f); // safe landing speed 14
            var session = server.AddLocalPlayer("Climber");
            var p = session.State;
            p.AboardShip = false;
            FloatUpAndReEnter(server, p);
            Assert.True(session.ReentryFallGrace, "sinking back below the line arms the fall grace");

            // Still falling through open air: the grace holds over ticks.
            server.Tick(0.5);
            Assert.True(session.ReentryFallGrace);

            p.Health = 100f;
            server.FallDamageForTest("Climber", 20f); // a hard landing — the first one after the re-entry
            Assert.Equal(100f, p.Health);
            Assert.False(session.ReentryFallGrace, "the first landing ends the grace");

            server.FallDamageForTest("Climber", 20f); // the next fall is a real fall again
            Assert.True(p.Health < 100f, "after the first landing a hard landing hurts as usual");
        }
    }

    [Fact]
    public void ReEntry_GraceEndsAfterStandingOnTheGround_WithoutAFallReport()
    {
        var server = Started("jungle", out var repo);
        using (repo)
        {
            server.SetGravityFactorForTest(1f);
            var session = server.AddLocalPlayer("Climber");
            var p = session.State;
            p.AboardShip = false;
            FloatUpAndReEnter(server, p);
            Assert.True(session.ReentryFallGrace);

            // A soft landing (the client reports no fall): the feet rest on the ground for a moment.
            p.Position = new Vector3f(0.5f, GroundY(server) + 1, 0.5f);
            for (int i = 0; i < 6; i++)
            {
                server.Tick(0.25);
            }

            Assert.False(session.ReentryFallGrace, "standing on the ground ends the grace");
            p.Health = 100f;
            server.FallDamageForTest("Climber", 20f);
            Assert.True(p.Health < 100f, "a later fall counts again");
        }
    }

    [Fact]
    public void FallDamageThreshold_ScalesWithTheSquareRootOfGravity_LikeTheClient()
    {
        var server = Started("rocky", out var repo);
        using (repo)
        {
            var session = server.AddLocalPlayer("Faller");
            var p = session.State;
            p.AboardShip = false;
            p.Position = new Vector3f(0.5f, GroundY(server) + 1, 0.5f);

            // Heavy world (2 g): the client only reports from 14·√2 ≈ 19.8 — 19 is still a safe landing.
            server.SetGravityFactorForTest(2f);
            p.Health = 100f;
            server.FallDamageForTest("Faller", 19f);
            Assert.Equal(100f, p.Health);
            server.FallDamageForTest("Faller", 22f);
            Assert.True(p.Health < 100f, "past 14·√g a landing hurts on a heavy world");

            // Light world (0.5 g): the safe speed drops to 14·√0.5 ≈ 9.9 — 12 already hurts (the flat 14 let it pass).
            server.SetGravityFactorForTest(0.5f);
            p.Health = 100f;
            server.FallDamageForTest("Faller", 12f);
            Assert.True(p.Health < 100f, "a light world's threshold sits lower too");
        }
    }

    [Fact]
    public void FirstFloatAboveTheLine_VegaExplainsTheWayDown_OncePerPlayer()
    {
        var link = new LoopbackLink();
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "atmo_vega"));
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };

        var config = new ServerConfig
        {
            WorldName = "atmo_vega",
            Seed = 11,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            World = { TerrainGeneration = 0 },
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { ContentFingerprint = TestJoin.Fingerprint, PlayerName = "Climber" }),
            DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var p = server.Sessions.Values.First(s => s.State.Name == "Climber").State;
        p.AboardShip = false;
        float line = (float)server.AtmosphereHeight;
        int Hints() => lines.Count(l => l.LineKey == "vega.hint.zero_g");

        p.Position = new Vector3f(0, line + 10f, 0);
        server.Tick(0.2);
        client.Poll();
        Assert.True(p.AboveAtmosphere);
        Assert.Equal(1, Hints());
        Assert.Contains("vega:hint:zero_g", server.MilestonesForTest("Climber"));

        // Down below the line and up again: the hint was learned — it does not repeat.
        p.Position = new Vector3f(0, line - 10f, 0);
        server.Tick(0.2);
        p.Position = new Vector3f(0, line + 10f, 0);
        server.Tick(0.2);
        client.Poll();
        Assert.True(p.AboveAtmosphere);
        Assert.Equal(1, Hints());
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
