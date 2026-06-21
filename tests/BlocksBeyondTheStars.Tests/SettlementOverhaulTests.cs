using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The multi-settlement overhaul: a world can hold several settlements whose count scales with the world's
/// hospitability and size (with high variance); they never overlap landing pads or each other; harsh worlds
/// skew to ruins; floating-island worlds can host sky settlements. Plus the weather/atmosphere gating (no
/// clouds/weather without an atmosphere).
/// </summary>
public sealed class SettlementOverhaulTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SettlementOverhaulTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_settle_overhaul_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(string planet, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, planet + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = planet + seed,
            Seed = seed,
            StartPlanet = planet,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void LushWorld_CanHaveSeveralSettlements()
    {
        int maxFound = 0;
        for (long seed = 1; seed <= 120 && maxFound < 2; seed++)
        {
            var server = Started("jungle", seed, out var repo);
            using (repo) maxFound = Math.Max(maxFound, server.SettlementCount);
        }

        Assert.True(maxFound >= 2, $"A lush, large world should sometimes have multiple settlements (saw at most {maxFound}).");
    }

    [Fact]
    public void Settlements_NeverSitOnALandingPad()
    {
        for (long seed = 1; seed <= 30; seed++)
        {
            var server = Started("jungle", seed, out var repo);
            using (repo)
            {
                if (!server.HasSettlement)
                {
                    continue;
                }

                int circ = server.World.Circumference;
                foreach (var s in server.SettlementsForTest)
                {
                    foreach (var pad in server.LandingPadCenters)
                    {
                        int dx = WorldConstants.WrapDeltaX(pad.X - s.MinX, circ);
                        bool insideX = dx >= 0 && dx <= s.MaxX - s.MinX;
                        bool insideZ = pad.Z >= s.MinZ && pad.Z <= s.MaxZ;
                        Assert.False(insideX && insideZ,
                            $"seed {seed}: a landing pad ({pad.X},{pad.Z}) fell inside a settlement footprint.");
                    }
                }
            }
        }
    }

    [Fact]
    public void Settlements_DoNotOverlapEachOther()
    {
        bool checkedAMultiWorld = false;
        for (long seed = 1; seed <= 120 && !checkedAMultiWorld; seed++)
        {
            var server = Started("jungle", seed, out var repo);
            using (repo)
            {
                var list = server.SettlementsForTest;
                if (list.Count < 2)
                {
                    continue;
                }

                checkedAMultiWorld = true;
                int circ = server.World.Circumference;
                for (int a = 0; a < list.Count; a++)
                    for (int b = a + 1; b < list.Count; b++)
                    {
                        Assert.False(RectsOverlap(list[a], list[b], circ),
                            $"seed {seed}: two settlement footprints overlap.");
                    }
            }
        }

        Assert.True(checkedAMultiWorld, "Expected to find at least one multi-settlement world to check overlap.");
    }

    [Fact]
    public void AirlessWorld_HasNoCloudsOrWeather()
    {
        // crystal has atmosphere "none" but is NOT a space-sky body — clouds/weather must still be gated off.
        var server = Started("crystal", 1, out var repo);
        using (repo)
        {
            Assert.Equal(0f, server.CloudDensityForTest);
            Assert.Equal("clear", server.WeatherModeForTest);
            Assert.Equal(0.0, server.AtmosphereDensity);
            Assert.False(server.HasSettlement); // airless ⇒ uninhabited
        }
    }

    [Fact]
    public void LavaWorld_NowHasAtmosphereWeather()
    {
        // lava is now toxic (not airless) so it keeps clouds + dynamic (ash) weather.
        var server = Started("lava", 1, out var repo);
        using (repo)
        {
            Assert.True(server.CloudDensityForTest > 0f);
            Assert.Equal("dynamic", server.WeatherModeForTest);
            Assert.True(server.AtmosphereDensity > 0.0);
        }
    }

    [Fact]
    public void HarshWorlds_SkewToRuins_MoreThanLushWorlds()
    {
        var (iceInhab, iceRuin) = CountKinds("ice", 60);
        var (jungInhab, jungRuin) = CountKinds("jungle", 60);

        double iceRuinFrac = iceRuin / (double)Math.Max(1, iceInhab + iceRuin);
        double jungRuinFrac = jungRuin / (double)Math.Max(1, jungInhab + jungRuin);

        Assert.True(iceRuin + iceInhab > 0 && jungRuin + jungInhab > 0, "Both planets should produce settlements.");
        Assert.True(iceRuinFrac > jungRuinFrac,
            $"A harsh world should have a higher ruin fraction (ice {iceRuinFrac:F2} vs jungle {jungRuinFrac:F2}).");
    }

    [Fact]
    public void FloatingIslandWorld_CanPlaceSkySettlements()
    {
        bool sawIslandSettlement = false;
        for (long seed = 1; seed <= 160 && !sawIslandSettlement; seed++)
        {
            var server = Started("skylands", seed, out var repo);
            using (repo) sawIslandSettlement = server.SettlementsForTest.Any(s => s.OnIsland);
        }

        Assert.True(sawIslandSettlement, "A floating-island world should sometimes place a settlement on a sky island.");
    }

    private (int Inhabited, int Ruined) CountKinds(string planet, int seeds)
    {
        int inhab = 0, ruin = 0;
        for (long seed = 1; seed <= seeds; seed++)
        {
            var server = Started(planet, seed, out var repo);
            using (repo)
            {
                inhab += server.InhabitedSettlementCount;
                ruin += server.SettlementCount - server.InhabitedSettlementCount;
            }
        }

        return (inhab, ruin);
    }

    private static bool RectsOverlap(
        (int MinX, int MinZ, int MaxX, int MaxZ, bool Ruined, bool OnIsland) a,
        (int MinX, int MinZ, int MaxX, int MaxZ, bool Ruined, bool OnIsland) b,
        int circ)
    {
        if (a.MaxZ < b.MinZ || b.MaxZ < a.MinZ)
        {
            return false;
        }

        int aCx = (a.MinX + a.MaxX) / 2, bCx = (b.MinX + b.MaxX) / 2;
        int aHw = (a.MaxX - a.MinX) / 2, bHw = (b.MaxX - b.MinX) / 2;
        int dx = Math.Abs(WorldConstants.WrapDeltaX(aCx - bCx, circ));
        return dx <= aHw + bHw;
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
