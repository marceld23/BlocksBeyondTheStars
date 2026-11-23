// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3970: Sol, our solar system, as the first fixed star system (<c>data/systems/sol.json</c>, #3969). It joins a growing
/// galaxy with its sixth growth — exactly once, never in a galaxy that does not grow — keeps its map spot over a restart,
/// carries its 21 bodies with their real parents, sizes and gravity (on the server's profile and on the star map, names in
/// the player's language, the gas giants drawn bigger than they walk), and the admin's <c>/sol</c> brings it now.
/// </summary>
public sealed class SolSystemTests : IDisposable
{
    private const string Sol = "sol";

    /// <summary>The bodies of Sol: id → (parent, circumference, gravity, fixed drawn diameter).</summary>
    private static readonly Dictionary<string, (string Parent, int Circumference, float Gravity, float Visible)> Bodies = new()
    {
        ["mercury"] = ("", 6128, 0.5f, 0f), ["venus"] = ("", 15200, 0.9f, 0f), ["earth"] = ("", 16000, 1.0f, 0f),
        ["moon"] = ("earth", 4368, 0.35f, 0f), ["mars"] = ("", 8512, 0.5f, 0f), ["phobos"] = ("mars", 800, 0.35f, 0f),
        ["deimos"] = ("mars", 800, 0.35f, 0f), ["ceres"] = ("", 1200, 0.35f, 0f), ["vesta"] = ("", 896, 0.35f, 0f),
        ["jupiter"] = ("", 16000, 1.6f, 242f), ["io"] = ("jupiter", 5776, 0.4f, 0f), ["europa"] = ("jupiter", 4896, 0.35f, 0f),
        ["ganymede"] = ("jupiter", 6592, 0.4f, 0f), ["callisto"] = ("jupiter", 6048, 0.4f, 0f),
        ["saturn"] = ("", 14400, 1.1f, 202f), ["titan"] = ("saturn", 6464, 0.35f, 0f), ["uranus"] = ("", 12000, 0.9f, 129f),
        ["neptune"] = ("", 12000, 1.15f, 129f), ["triton"] = ("neptune", 3392, 0.35f, 0f), ["pluto"] = ("", 2976, 0.35f, 0f),
        ["charon"] = ("pluto", 1520, 0.35f, 0f),
    };

    /// <summary>The bodies that orbit the star, in their real order outward.</summary>
    private static readonly string[] SunOrder =
        { "mercury", "venus", "earth", "mars", "ceres", "vesta", "jupiter", "saturn", "uranus", "neptune", "pluto" };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_sol_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Boot(string world, RecordingTransport transport, bool growth, bool cheats = false)
    {
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        config.World.GalaxyGrowth = growth;
        if (cheats)
        {
            config.Rules = new GameRules { AdminCheats = true, AllowCheatsInSurvival = true };
        }

        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>Grows the galaxy <paramref name="times"/> times, each by a jump to the current edge.</summary>
    private static void Grow(SvGameServer server, PlayerSession who, int times)
    {
        for (int i = 0; i < times; i++)
        {
            string edge = server.Galaxy.Systems.First(s => server.IsEdgeSystemForTest(s.Id)).Id;
            Assert.True(server.TryGrowGalaxyForTest(who, edge), $"growth {i + 1} did not happen");
        }
    }

    [Fact]
    public void Sol_JoinsWithTheSixthGrowth_Once_AndComesBackAfterARestartAtItsSpot()
    {
        (float X, float Y) spot;
        {
            var server = Boot("grow", new RecordingTransport(), growth: true);
            var ada = server.AddLocalPlayer("Ada");
            while (server.Metadata.GalaxyGrownSystems < 5) // the join itself may have grown it once (a spawn at the rim)
            {
                Grow(server, ada, 1);
            }

            Assert.Equal(5, server.Metadata.GalaxyGrownSystems);
            Assert.False(server.GalaxyHasSystemForTest(Sol));

            Grow(server, ada, 1);
            Assert.Equal(1, server.GalaxySystemCountForTest(Sol));
            var sol = server.Galaxy.Systems.Single(s => s.Id == Sol);
            Assert.Equal(StarSystemOrigin.Fixed, sol.Origin);
            Assert.All(server.Galaxy.Systems.Where(s => s.Id != Sol), s => Assert.True(s.IsRolled)); // nothing else changed kind
            spot = (sol.MapX, sol.MapY);
            Assert.Contains(server.Metadata.StampedFeatures, f => f.StartsWith(SvGameServer.FixedSystemFeaturePrefix + Sol + "|", StringComparison.Ordinal));

            Grow(server, ada, 1); // a further growth never appends it twice, and Sol itself is never a growth edge
            Assert.Equal(1, server.GalaxySystemCountForTest(Sol));
            Assert.False(server.IsEdgeSystemForTest(Sol));
            _repos[^1].Flush();
            _repos[^1].Dispose();
            _repos.RemoveAt(_repos.Count - 1);
        }

        var again = Boot("grow", new RecordingTransport(), growth: true);
        var back = again.Galaxy.Systems.Single(s => s.Id == Sol);
        Assert.Equal(spot, (back.MapX, back.MapY));
        Assert.Equal(21, back.Bodies.Count);
    }

    [Fact]
    public void WithoutGalaxyGrowth_SolNeverJoins()
    {
        var server = Boot("fixed", new RecordingTransport(), growth: false);
        server.Metadata.GalaxyGrownSystems = 10; // even a count that says so: the world was not created to grow
        server.EnsureFixedSystemsForTest();
        Assert.False(server.GalaxyHasSystemForTest(Sol));
    }

    [Fact]
    public void Sol_Has21BodiesWithTheirParentsSizesAndGravity_OnTheServerAndOnTheStarMap()
    {
        var transport = new RecordingTransport();
        var server = Boot("bodies", transport, growth: false, cheats: true);
        var admin = server.AddLocalPlayer("Ada", "de");
        admin.State.Role = PlayerRole.WorldAdmin;
        server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system" });

        var sol = server.Galaxy.Systems.Single(s => s.Id == Sol);
        Assert.Equal(21, sol.Bodies.Count);
        foreach (var (id, want) in Bodies)
        {
            var body = sol.Bodies.Single(b => b.Id == "sol-" + id);
            Assert.Equal(want.Parent.Length == 0 ? string.Empty : "sol-" + want.Parent, body.ParentId);
            Assert.Equal("sol_" + id, body.PlanetType);
            var profile = server.BodyProfileForTest(body);
            Assert.Equal(want.Circumference, profile.Circumference);
            Assert.Equal(0, want.Circumference % WorldConstants.ChunkSize);
            Assert.Equal(want.Gravity, profile.GravityFactor);
            Assert.Equal(want.Gravity, server.WorldGravityForTest(body.Id, WorldConstants.SizeClassFor(body.Kind, body.PlanetType!)));
        }

        var radii = SunOrder.Select(id => sol.Bodies.Single(b => b.Id == "sol-" + id))
            .Select(b => MathF.Sqrt((b.SystemX * b.SystemX) + (b.SystemZ * b.SystemZ))).ToList();
        Assert.Equal(radii.OrderBy(r => r), radii); // the real order outward

        var map = transport.To<StarMapData>(admin).Last();
        var netSol = map.Systems.Single(s => s.Id == Sol);
        Assert.Equal("Sol", netSol.Name);
        Assert.Equal(0xFFF4E0, netSol.StarColor);
        var earth = netSol.Bodies.Single(b => b.Id == "sol-earth");
        Assert.Equal("Erde", earth.Name); // in the player's language
        Assert.Equal("Ganymed", netSol.Bodies.Single(b => b.Id == "sol-ganymede").Name);
        foreach (var (id, want) in Bodies)
        {
            var net = netSol.Bodies.Single(b => b.Id == "sol-" + id);
            Assert.Equal(want.Circumference, net.Circumference);
            Assert.Equal(want.Visible, net.VisibleDiameter);
        }

        float earthDrawn = SystemBodyLayout.VisibleDiameter(earth.VisibleDiameter, earth.Circumference);
        Assert.InRange(242f / earthDrawn, 2.8f, 3.2f); // Jupiter about three Earths across
        Assert.NotEqual(0, sol.Bodies.Single(b => b.Id == "sol-saturn").RingSeed);
        Assert.All(netSol.Bodies.Where(b => b.VisibleDiameter > 0f),
            b => Assert.True(SystemBodyLayout.HomeDiameter(b.VisibleDiameter) <= SystemBodyLayout.MaxHomeDiameter));
    }

    [Fact]
    public void TheSolVerb_RevealsSolOnce_AndRefusesAnUnknownSystem()
    {
        var transport = new RecordingTransport();
        var server = Boot("verb", transport, growth: false, cheats: true);
        var admin = server.AddLocalPlayer("Ada");
        admin.State.Role = PlayerRole.WorldAdmin;
        admin.State.Position = new Vector3f(300f, 120f, 0f);

        List<string> Lines(Action act)
        {
            int before = transport.Sent.Count;
            act();
            return transport.Sent.Skip(before).Where(s => s.Conn == admin.ConnectionId).Select(s => s.Msg switch
            {
                ServerMessage m => m.Text,
                ActionRejected r => r.Reason,
                _ => string.Empty,
            }).Where(t => t.Length > 0).ToList();
        }

        Assert.Contains("@srv.admin.system_revealed:Sol",
            Lines(() => server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" })));
        Assert.Equal(1, server.GalaxySystemCountForTest(Sol));
        Assert.Contains("@srv.admin.system_already:Sol",
            Lines(() => server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "sol" })));
        Assert.Equal(1, server.GalaxySystemCountForTest(Sol));
        Assert.Contains("@srv.admin.system_unknown:nowhere",
            Lines(() => server.HandleForTest(admin, new AdminCommandIntent { Command = "reveal_system", StringArg = "nowhere" })));
    }
}
