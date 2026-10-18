// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2739: a space station's quartermaster (its quest giver) and its extra vendors' themes came from the station id alone —
/// and station ids repeat in every world (<c>sys0-st</c>, <c>pstation:&lt;owner&gt;:&lt;n&gt;</c>), so every world met the same
/// quartermaster with the same vendors at "the same" station. From terrain generation 27 both are seeded by the world; a
/// world of generation 26 or older keeps both exactly (the pinned values below were taken from the code before the change).
/// The station twin of <see cref="SettlementTradeAndGiverTests"/>. Side bug fixed with it: a chain step's default giver at a
/// station was coined from the station's place key — a second name for the quartermaster.
/// </summary>
public sealed class StationTradeAndGiverTests : IDisposable
{
    private const int Gen = WorldDescription.WorldSeededStationsGeneration;
    private const int OldGen = Gen - 1;

    private static readonly string[] Trades = { "miners", "traders", "researchers", "settlers" };

    /// <summary>Station ids as the universe and the players mint them: the rolled stations of forty systems (first and
    /// second), the synthesised local fallback, and player-built stations.</summary>
    private static readonly string[] StationIds =
        Enumerable.Range(0, 40).SelectMany(i => new[] { $"sys{i}-st", $"sys{i}-st1", $"sys{i}-st-local" })
            .Concat(Enumerable.Range(1, 30).Select(n => $"pstation:Pilot:{n}"))
            .ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_stationgiver_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A repo file may still be locked on Windows — the temp dir is disposable either way.
        }
    }

    /// <summary>A coined giver name exactly as <c>CoinGiverName(stationId)</c> computed it before #2739.</summary>
    private static string PreChangeGiver(string key)
        => NameGenerator.Person(new System.Random(unchecked((int)WorldGenerator.StableHash("giver:" + key))));

    // ---------------- Pure ----------------

    [Fact]
    public void OldGeneration_QuartermasterAndVendors_ComeFromTheIdAlone_BitForBit()
    {
        // Pinned from the code before #2739: no station vendor switches its goods, nobody a player knows is renamed.
        Assert.Equal("Spetror Klymdraitrook", SvGameServer.StationGiverNameFor("sys0-st", 4242, OldGen));
        Assert.Equal("traders", SvGameServer.StationVendorThemeFor("sys0-st", 1, 4242, OldGen));
        Assert.Equal("miners", SvGameServer.StationVendorThemeFor("sys0-st", 2, 4242, OldGen));
        Assert.Equal("Klouzdrych Trevroox", SvGameServer.StationGiverNameFor("sys3-st1", -7, OldGen));
        Assert.Equal("settlers", SvGameServer.StationVendorThemeFor("sys3-st1", 1, -7, OldGen));
        Assert.Equal("Reervoo Zuathileer", SvGameServer.StationGiverNameFor("pstation:Pilot:1", 1, OldGen));
        Assert.Equal("Tuabreir Grighymux", SvGameServer.StationGiverNameFor("sys0-st-local", 42, OldGen));

        // Every generation below 27, every world: the id alone decides, exactly as before.
        foreach (int generation in new[] { 0, 21, 25, OldGen })
            foreach (long seed in new[] { 1L, 42L, -7L })
                foreach (string id in StationIds)
                {
                    Assert.Equal(PreChangeGiver(id), SvGameServer.StationGiverNameFor(id, seed, generation));
                    for (int vendor = 0; vendor < 4; vendor++)
                    {
                        Assert.Equal(SvGameServer.VendorThemeForTest(id, vendor, "traders"),
                            SvGameServer.StationVendorThemeFor(id, vendor, seed, generation));
                    }
                }
    }

    [Fact]
    public void NewGeneration_TheSameStationInTwoWorlds_HasItsOwnQuartermaster()
    {
        int n = StationIds.Length;
        int giverDiffers = 0, classicDiffers = 0;
        foreach (string id in StationIds)
        {
            giverDiffers += SvGameServer.StationGiverNameFor(id, 42, Gen) != SvGameServer.StationGiverNameFor(id, 43, Gen) ? 1 : 0;
            classicDiffers += SvGameServer.StationGiverNameFor(id, 42, OldGen) != SvGameServer.StationGiverNameFor(id, 43, OldGen) ? 1 : 0;
        }

        Assert.Equal(0, classicDiffers); // the bug: the id alone decided
        Assert.True(giverDiffers >= n * 95 / 100, $"only {giverDiffers} of {n} stations have their own quartermaster in a second world");
    }

    [Fact]
    public void NewGeneration_TheFirstVendorStaysTraders_AndExtraVendorsAreEvenlySpread()
    {
        var extra = Trades.ToDictionary(t => t, _ => 0);
        int rolls = 0;
        foreach (long seed in new[] { 4242L, -7L, 1L })
            foreach (string id in StationIds)
            {
                Assert.Equal("traders", SvGameServer.StationVendorThemeFor(id, 0, seed, Gen)); // the station's identity
                for (int vendor = 1; vendor <= 2; vendor++)
                {
                    extra[SvGameServer.StationVendorThemeFor(id, vendor, seed, Gen)]++;
                    rolls++;
                }
            }

        foreach (string t in Trades)
        {
            Assert.InRange(extra[t] / (double)rolls, 0.18, 0.32);
        }
    }

    [Fact]
    public void NewGeneration_QuartermasterAndVendors_AreBitFrozen()
    {
        // Known answers: a generation-27 save's station people (their names are in players' memories) must never move.
        Assert.Equal("Kezask Kuxgramjeim", SvGameServer.StationGiverNameFor("sys0-st", 4242, Gen));
        Assert.Equal("settlers", SvGameServer.StationVendorThemeFor("sys0-st", 1, 4242, Gen));
        Assert.Equal("researchers", SvGameServer.StationVendorThemeFor("sys0-st", 2, 4242, Gen));
        Assert.Equal("Skosk Zrumvhath", SvGameServer.StationGiverNameFor("sys3-st1", -7, Gen));
        Assert.Equal("traders", SvGameServer.StationVendorThemeFor("sys3-st1", 1, -7, Gen));
        Assert.Equal("Spith Spuakeetraeph", SvGameServer.StationGiverNameFor("pstation:Pilot:1", 1, Gen));
        Assert.Equal("settlers", SvGameServer.StationVendorThemeFor("pstation:Pilot:1", 3, 1, Gen));
        Assert.Equal("Quash Ghionglomphyph", SvGameServer.StationGiverNameFor("sys0-st-local", 42, Gen));
    }

    // ---------------- One station, one name ----------------

    private SvGameServer Started(int generation, out SqliteWorldRepository repo)
    {
        string name = "stationgiver_g" + generation;
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription { SpaceStations = Frequency.Frequent, TerrainGeneration = generation },
        };
        config.Rules.FreeSpaceFlight = true;

        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static CombatEntity BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == CombatEntityKind.SpaceStation);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, station.Id);
        return station;
    }

    [Theory]
    [InlineData(OldGen)]
    [InlineData(Gen)]
    public void OneStation_ItsQuartermaster_BoardJobs_VendorsAndChainGiver_Agree(int generation)
    {
        var server = Started(generation, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            var station = BoardFirstStation(server, "Pilot");
            Assert.True(server.InStation("Pilot"));
            string place = server.CurrentPlaceKeyForTest("Pilot");
            Assert.StartsWith("station_", place, StringComparison.Ordinal); // the save key stays as it was

            string giver = SvGameServer.StationGiverNameFor(station.Id, 42, generation);
            if (generation < Gen)
            {
                Assert.Equal(PreChangeGiver(station.Id), giver);
            }

            // The quartermaster NPC carries the giver's name, and the board's jobs are handed out in it.
            var crew = server.NpcCrewForTest();
            var quartermasters = crew.Where(n => n.Role == "quartermaster").ToList();
            Assert.NotEmpty(quartermasters);
            Assert.All(quartermasters, q => Assert.Equal(giver, q.Name));
            var jobs = server.VisibleMissionIdsForTest("Pilot").Where(id => id.StartsWith(place + "_", StringComparison.Ordinal)).ToList();
            Assert.NotEmpty(jobs);
            Assert.All(jobs, id => Assert.Equal(giver, server.MissionDefForTest(id)!.GiverName));

            // The vendors at the trade posts: the first sells the station's "traders" goods, every further one its own
            // roll of this generation (a trading profession and a visiting trader keep their own themes).
            int posts = server.SpaceStationMarkers.Count(m => m.Type == "vendor");
            var vendors = crew.Where(n => n.Role == "vendor" && NpcProfessions.ByJob(n.Job) is null).Take(posts).ToList();
            Assert.NotEmpty(vendors);
            for (int i = 0; i < vendors.Count; i++)
            {
                Assert.Equal(SvGameServer.StationVendorThemeFor(station.Id, i, 42, generation), vendors[i].Theme);
            }

            // A chain giver nobody named yet: the place key's coinage below 27 (as before — a second name for the
            // quartermaster, kept so nobody a player knows is renamed), the quartermaster from it.
            string chainGiver = server.ChainGiverFallbackNameForTest(place);
            Assert.Equal(generation < Gen ? PreChangeGiver(place) : giver, chainGiver);
        }
    }
}
