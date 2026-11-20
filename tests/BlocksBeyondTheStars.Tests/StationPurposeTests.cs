// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — what a space station is for: a kit station composed for the first time in a world of terrain
/// generation 46 gets a purpose and a colour, pinned and replayed; its first vendor sells the purpose's goods; the band
/// is dyed into its walls and floor edge; a kit made for a purpose is drawn only for that purpose; a world of an older
/// generation composes the station it always did.
/// </summary>
public sealed class StationPurposeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_stationpurpose_" + Guid.NewGuid().ToString("N"));

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

    private static List<StructureTemplate> Modules() => new()
    {
        StationKitComposerTests.Room("kit_hub", "hub", markers: new[] { ("spawn", 3, 1, 3), ("vendor", 2, 1, 4), ("mission_board", 4, 1, 4) }),
        StationKitComposerTests.Room("kit_cabin", "cabins", faces: new[] { "z-" }),
        StationKitComposerTests.Room("kit_canteen", "canteen", faces: new[] { "x-" }),
    };

    private static StructureKit Kit(string key, string tier, params string[] purposes)
    {
        var kit = new StructureKit
        {
            Key = key,
            Name = key,
            Kind = "station",
            Tier = tier,
            Start = "kit_hub",
            Entries =
            {
                new KitEntry { Module = "kit_hub", Required = true, Max = 1 },
                new KitEntry { Module = "kit_cabin", Required = true, Min = 2, Max = 2 },
                new KitEntry { Module = "kit_canteen", Required = true, Max = 1 },
            },
        };
        kit.Purposes.AddRange(purposes);
        return kit;
    }

    private static readonly string[] Tiers = { "small", "medium", "large", "huge", "colossal" };

    /// <summary>Content whose only station structures are one kit per tier (and, with <paramref name="purposeKits"/>, one
    /// more per tier and purpose).</summary>
    private static GameContent Content(bool purposeKits)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStructureTemplates(Modules(), content.SettlementTemplates);
        var kits = new List<StructureKit>();
        foreach (string tier in Tiers)
        {
            kits.Add(Kit("plain_" + tier, tier));
            if (purposeKits)
            {
                kits.AddRange(StationPurposes.All.Select(p => Kit(p + "_" + tier, tier, p)));
            }
        }

        var warnings = new List<string>();
        content.SetStructureKits(kits, warnings.Add);
        Assert.Empty(warnings);
        return content;
    }

    private SvGameServer Started(GameContent content, string world, int generation, out SqliteWorldRepository repo, long seed = 42,
        IServerTransport? transport = null)
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
            World = new WorldDescription { SpaceStations = Frequency.Frequent, StationTemplateUse = Frequency.Rare, TerrainGeneration = generation },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, transport ?? new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private static string BoardFirstStation(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, station.Id);
        return station.Id;
    }

    /// <summary>The dyes of every block of the station world's build box, by block key.</summary>
    private static Dictionary<string, HashSet<int>> Dyes(SvGameServer server, GameContent content)
    {
        var dyes = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (int x = 0; x < 80; x++)
        {
            for (int y = 56; y < 90; y++)
            {
                for (int z = 0; z < 80; z++)
                {
                    var p = new Vector3i(x, y, z);
                    var b = server.World.GetBlock(p);
                    if (b.IsAir)
                    {
                        continue;
                    }

                    string key = content.BlockById(b)?.Key ?? "?";
                    if (!dyes.TryGetValue(key, out var set))
                    {
                        dyes[key] = set = new HashSet<int>();
                    }

                    set.Add(server.World.GetModifier(p).Tint);
                }
            }
        }

        return dyes;
    }

    [Fact]
    public void AStationOfANewWorld_GetsAPurposeAndAColour_PinnedAndTheSameOnReload()
    {
        var content = Content(purposeKits: false);
        string stationId, purpose;
        int colour;
        var server = Started(content, "purpose", WorldDescription.LivelyPlacesGeneration, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            stationId = BoardFirstStation(server, "Pilot");
            (purpose, colour) = server.StationPurposeForTest(stationId);
            Assert.Equal(StationPurposes.Trade, purpose); // the start system's station trades
            Assert.Contains(colour, StationPurposes.Colours);
            var rec = Assert.Contains(stationId, repo.LoadMetadata()!.StationKits);
            Assert.Equal((purpose, colour), (rec.Purpose, rec.Colour));

            // The band: iron walls and the floor's edge carry the colour, and nothing else does.
            var dyes = Dyes(server, content);
            Assert.Contains(colour, dyes["iron_wall"]);
            Assert.Contains(0, dyes["iron_wall"]); // the wall between the bands stays plain
            Assert.Contains(colour, dyes["steel_floor"]);
            Assert.Contains(0, dyes["steel_floor"]); // only the edge of the floor
            Assert.All(dyes.Where(d => d.Key is not ("iron_wall" or "steel_floor")), d => Assert.DoesNotContain(colour, d.Value));

            // The place the client is told about: the station, its purpose, its colour.
            var place = Assert.Single(server.PlaceListForTest().Places);
            Assert.Equal((PlaceKinds.Station, purpose, colour), (place.Kind, place.Purpose, place.Color));
            Assert.False(string.IsNullOrWhiteSpace(place.Name));

            // The first vendor sells the purpose's goods and the crew wears its colours' theme.
            Assert.Contains(server.NpcSnapshots, n => n.Role == "vendor");
            Assert.Equal(StationPurposes.ThemeOf(purpose), server.NpcCrewForTest().First(n => n.Role == "vendor").Theme);
            server.LeaveStation("Pilot"); // undock, so the restarted server boards the same station again from space
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Started(content, "purpose", WorldDescription.LivelyPlacesGeneration, out var repo2);
        using (repo2)
        {
            again.AddLocalPlayer("Pilot");
            Assert.Equal(stationId, BoardFirstStation(again, "Pilot"));
            Assert.Equal((purpose, colour), again.StationPurposeForTest(stationId));
            Assert.Contains(colour, Dyes(again, content)["iron_wall"]);
            again.Stop();
        }
    }

    [Fact]
    public void ThePurpose_RidesOnTheHullDesign_AndOnTheStarMap_OnceTheStationIsComposed()
    {
        var content = Content(purposeKits: false);
        var transport = new RecordingTransport();
        var server = Started(content, "purposewire", WorldDescription.LivelyPlacesGeneration, out var repo, transport: transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string stationId = BoardFirstStation(server, "Pilot");
            var (purpose, _) = server.StationPurposeForTest(stationId);
            Assert.Equal(StationPurposes.Trade, purpose);

            IEnumerable<T> Sent<T>() => transport.Sent.Where(s => s.Conn == pilot.ConnectionId).Select(s => s.Msg).OfType<T>();

            // The hull the pilot flew up to: its design names the purpose (the dock prompt adds it to the name) — and no
            // other hull's design does.
            var hull = Sent<SpaceShipDesign>().Last(d => d.Id == stationId);
            Assert.Equal(purpose, hull.Purpose);
            Assert.All(Sent<SpaceShipDesign>().Where(d => d.Kind != "station"), d => Assert.Equal(string.Empty, d.Purpose));

            // The star map: the station's body carries it too, and nothing that is no station does.
            server.SendStarMapForTest("Pilot");
            var bodies = Sent<StarMapData>().Last().Systems.SelectMany(s => s.Bodies).ToList();
            var body = Assert.Single(bodies, b => b.Id == stationId);
            Assert.Equal(purpose, body.Purpose);
            Assert.All(bodies.Where(b => b.Kind != nameof(CelestialKind.SpaceStation)), b => Assert.Equal(string.Empty, b.Purpose));

            // Over the wire and back: the additive fields survive the codec.
            var back = Assert.IsType<SpaceShipDesign>(NetCodec.Decode(NetCodec.Encode(hull)));
            Assert.Equal(purpose, back.Purpose);
            server.Stop();
        }
    }

    [Fact]
    public void TheMissionBoard_ShowsMoreOfThePurposesKindOfJob_AndNoJobMoves()
    {
        var content = Content(purposeKits: false);
        var server = Started(content, "purposeboard", WorldDescription.LivelyPlacesGeneration, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string stationId = BoardFirstStation(server, "Pilot");

            (List<string> Deliveries, List<string> Surveys) Board()
            {
                var ids = server.VisibleMissionIdsForTest("Pilot").Where(id => id.StartsWith("station_", StringComparison.Ordinal)).ToList();
                // The new streams: "…_m<slot>" a delivery, "…_ms<slot>" a survey.
                bool Survey(string id) => id[(id.LastIndexOf('_') + 1)..].StartsWith("ms", StringComparison.Ordinal);
                bool Delivery(string id) => id[(id.LastIndexOf('_') + 1)..] is { Length: > 1 } tail && tail[0] == 'm' && char.IsDigit(tail[1]);
                return (ids.Where(Delivery).OrderBy(i => i, StringComparer.Ordinal).ToList(), ids.Where(Survey).OrderBy(i => i, StringComparer.Ordinal).ToList());
            }

            var trade = Board(); // the start system's station trades: the board it always had
            Assert.Equal(3, trade.Deliveries.Count);
            Assert.Single(trade.Surveys);

            server.SetStationPurposeForTest(stationId, StationPurposes.Mining);
            var mining = Board();
            Assert.Equal(5, mining.Deliveries.Count);
            Assert.Single(mining.Surveys);
            Assert.Subset(mining.Deliveries.ToHashSet(), trade.Deliveries.ToHashSet()); // the same jobs, two more beside them

            server.SetStationPurposeForTest(stationId, StationPurposes.Research);
            var research = Board();
            Assert.Equal(3, research.Deliveries.Count);
            Assert.Equal(3, research.Surveys.Count);
            Assert.Equal(trade.Deliveries, research.Deliveries);
            Assert.Subset(research.Surveys.ToHashSet(), trade.Surveys.ToHashSet());
            server.Stop();
        }
    }

    /// <summary>The station's own exchange: a mining station's first trader deals in the miners' goods at the station's
    /// rates (and no longer in the plain miners' rows), a research station's in data fragments at better rates.</summary>
    [Theory]
    [InlineData(StationPurposes.Mining, StationPurposes.OreExchange, "station_ore_iron", "silicate", 4, "iron_ore", 8, "market_buy_iron")]
    [InlineData(StationPurposes.Research, StationPurposes.DataExchange, "station_data_circuit", "data_fragment", 1, "circuit_board", 3, "market_data_to_circuit")]
    public void TheFirstTraderOfAMiningOrAResearchStation_RunsTheStationsOwnExchange(string purpose, string theme, string deal,
        string give, int giveCount, string get, int getCount, string plainDeal)
    {
        var content = Content(purposeKits: false);
        string world = "exchange_" + purpose;
        string stationId;
        var server = Started(content, world, WorldDescription.LivelyPlacesGeneration, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            stationId = BoardFirstStation(server, "Pilot");
            Assert.Equal("traders", server.NpcCrewForTest().First(n => n.Role == "vendor").Theme); // the start system trades: the plain trade
            server.SetStationPurposeForTest(stationId, purpose); // what a station in another system would be
            server.LeaveStation("Pilot");
            server.Stop();
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var transport = new RecordingTransport();
        var again = Started(content, world, WorldDescription.LivelyPlacesGeneration, out var repo2, transport: transport);
        using (repo2)
        {
            var pilot = again.AddLocalPlayer("Pilot");
            Assert.Equal(stationId, BoardFirstStation(again, "Pilot"));
            Assert.Equal(theme, again.NpcCrewForTest().First(n => n.Role == "vendor").Theme);

            // At its counter: the station's deal goes through at the station's rate, the plain row of the trade does not.
            var trader = again.NpcSnapshots.First(n => n.Role == "vendor");
            pilot.State.Position = new Vector3f(trader.Pos.X + 1f, trader.Pos.Y, trader.Pos.Z);
            pilot.State.Inventory.Add(give, giveCount * 2, 99);
            int before = pilot.State.Inventory.CountOf(get);

            string Trade(string recipe)
            {
                int mark = transport.Sent.Count;
                again.Craft("Pilot", recipe);
                var res = transport.Sent.Skip(mark).Select(x => x.Msg).OfType<CraftResult>().LastOrDefault();
                return res is null ? "(no result)" : res.Success ? "ok" : res.Reason ?? string.Empty;
            }

            Assert.Equal("ok", Trade(deal));
            Assert.Equal(before + getCount, pilot.State.Inventory.CountOf(get));
            Assert.Equal(giveCount, pilot.State.Inventory.CountOf(give));
            Assert.Equal("@srv.craft.wrong_vendor", Trade(plainDeal));
            Assert.Equal(before + getCount, pilot.State.Inventory.CountOf(get));
            again.Stop();
        }
    }

    [Fact]
    public void TheStationExchanges_AreNeverWorseThanThePlainTrade_AndNeverTravelWithTheMerchant()
    {
        var recipes = ContentLoader.LoadFromDirectory(TestPaths.DataDir()).Recipes.Values.ToList();
        var ore = recipes.Where(r => r.MarketTheme == StationPurposes.OreExchange).ToList();
        var data = recipes.Where(r => r.MarketTheme == StationPurposes.DataExchange).ToList();
        Assert.Equal(5, ore.Count);
        Assert.Equal(5, data.Count);
        Assert.All(ore.Concat(data), r => Assert.True(r.MarketExclusive && MarketStock.IsTrade(r), r.Key));

        // What one unit of the goods costs: every plain row of the trade that sells the same thing for the same payment
        // costs at least as much as the station's row — and at least one row of each exchange is really better.
        static double Price(RecipeDefinition r) => r.Inputs.Sum(i => i.Count) / (double)r.Outputs.Sum(o => o.Count);
        static string Shape(RecipeDefinition r)
            => string.Join("+", r.Inputs.Select(i => i.Item).OrderBy(i => i, StringComparer.Ordinal)) + ">"
               + string.Join("+", r.Outputs.Select(o => o.Item).OrderBy(o => o, StringComparer.Ordinal));
        foreach (var (station, plainTheme) in new[] { (ore, "miners"), (data, "researchers") })
        {
            int better = 0;
            foreach (var plain in recipes.Where(r => r.MarketTheme == plainTheme))
            {
                var twin = station.SingleOrDefault(s => Shape(s) == Shape(plain));
                Assert.True(twin != null, $"the station exchange lacks the {plainTheme}' deal '{plain.Key}'");
                Assert.True(Price(twin!) <= Price(plain) + 1e-9, $"'{twin!.Key}' is worse than '{plain.Key}'");
                better += Price(twin) < Price(plain) - 1e-9 ? 1 : 0;
            }

            Assert.True(better >= 2, $"the exchange of the {plainTheme} is hardly better than the plain trade");
        }

        // The travelling merchant's mixed stock never carries a station's deal, on no day.
        for (long day = 0; day < 400; day++)
        {
            Assert.DoesNotContain(MarketStock.MixedOffers(recipes, day), r => r.MarketExclusive);
        }

        // A station exchange dresses as its trade; everything else is its own look.
        Assert.Equal("miners", StationPurposes.LookOf(StationPurposes.OreExchange));
        Assert.Equal("researchers", StationPurposes.LookOf(StationPurposes.DataExchange));
        Assert.Equal("traders", StationPurposes.LookOf("traders"));
        Assert.Equal("traders", StationPurposes.ServiceThemeOf(StationPurposes.Trade));
        Assert.Equal(StationPurposes.OreExchange, StationPurposes.ServiceThemeOf(StationPurposes.Mining));
        Assert.Equal(StationPurposes.DataExchange, StationPurposes.ServiceThemeOf(StationPurposes.Research));
    }

    [Fact]
    public void AStationOfAnOlderWorld_HasNoPurposeAndNoColour()
    {
        var content = Content(purposeKits: true);
        var server = Started(content, "nopurpose", WorldDescription.LivelyPlacesGeneration - 1, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string stationId = BoardFirstStation(server, "Pilot");
            Assert.Equal((string.Empty, 0), server.StationPurposeForTest(stationId));
            Assert.StartsWith("plain_", server.StationKitForTest(stationId)); // never a kit made for a purpose
            Assert.All(Dyes(server, content).Values, set => Assert.Equal(new[] { 0 }, set));
            var place = Assert.Single(server.PlaceListForTest().Places);
            Assert.Equal((PlaceKinds.Station, string.Empty, 0), (place.Kind, place.Purpose, place.Color));
            server.Stop();
        }
    }

    [Fact]
    public void AKitMadeForAPurpose_IsDrawnForThatPurposeOnly()
    {
        var content = Content(purposeKits: true);
        var server = Started(content, "purposekits", WorldDescription.LivelyPlacesGeneration, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            string stationId = BoardFirstStation(server, "Pilot");
            var (purpose, _) = server.StationPurposeForTest(stationId);
            Assert.StartsWith(purpose + "_", server.StationKitForTest(stationId)); // its own kit, not the plain one, not another's
            server.Stop();
        }
    }

    [Fact]
    public void AboardAStation_ItsRoomsSound_AndItAnnouncesWithAGong_AtMostOnceAMinute()
    {
        var content = Content(purposeKits: false);
        var transport = new RecordingTransport();
        // An older world on purpose: the sounds and the announcements are behaviour, not world generation.
        var server = Started(content, "stationlife", WorldDescription.LivelyPlacesGeneration - 1, out var repo, transport: transport);
        using (repo)
        {
            var pilot = server.AddLocalPlayer("Pilot");
            string stationId = BoardFirstStation(server, "Pilot");

            // The canteen murmurs while the crew is awake; it rides in the landmark list like every emitter.
            var canteen = Assert.Single(server.PlaceEmittersForTest, e => e.Sound == SvGameServer.StationCanteenSound);
            Assert.Equal(SoundEmitterTimes.Waking, canteen.When);
            Assert.Contains(server.LandmarkListForTest(pilot).Emitters, e => e.Sound == SvGameServer.StationCanteenSound && e.When == SoundEmitterTimes.Waking);

            IEnumerable<object> Sent() => transport.Sent.Where(s => s.Conn == pilot.ConnectionId).Select(s => s.Msg);
            int before = transport.Sent.Count;
            Assert.True(server.AnnounceAboardStationForTest(stationId, "night"));
            var news = transport.Sent.Skip(before).Where(s => s.Conn == pilot.ConnectionId).Select(s => s.Msg).ToList();
            Assert.Contains(news.OfType<SoundFx>(), f => f.SoundId == SvGameServer.StationGongSound);
            Assert.Contains(news.OfType<ServerMessage>(), m => m.Text == "@srv.station.announce.night");
            Assert.False(server.AnnounceAboardStationForTest(stationId, "trader_docked", "Mira")); // the station just spoke
            Assert.Equal(1, Sent().OfType<ServerMessage>().Count(m => m.Text.StartsWith("@srv.station.announce.", StringComparison.Ordinal)));

            // Nobody aboard: nothing is said (and the minute is not used up).
            server.LeaveStation("Pilot");
            Assert.False(server.AnnounceAboardStationForTest(stationId, "day"));
            server.Stop();
        }

        // The lines exist in both mandatory languages, with the name where one is given.
        foreach (var locale in new[] { BlocksBeyondTheStars.Shared.Localization.GameLocale.English, BlocksBeyondTheStars.Shared.Localization.GameLocale.German })
        {
            var loc = content.CreateLocalizer(locale);
            foreach (string key in new[] { "night", "day", "trader_docked", "trader_left" })
            {
                Assert.NotEqual("srv.station.announce." + key, loc.Get("srv.station.announce." + key));
            }

            Assert.Contains("{name}", loc.Get("srv.station.announce.trader_docked"));
        }
    }

    [Fact]
    public void ThePurpose_FollowsTheSystem_AndIsStablePerSeed()
    {
        // The start system always trades.
        for (long seed = 1; seed <= 50; seed++)
        {
            Assert.Equal(StationPurposes.Trade, StationPurposes.For(seed, home: true, belt: true));
        }

        int Count(bool belt, string purpose) => Enumerable.Range(1, 2000).Count(s => StationPurposes.For(s, home: false, belt) == purpose);
        Assert.True(Count(true, StationPurposes.Mining) > Count(false, StationPurposes.Mining) + 300, "a belt does not make mining stations likelier");
        foreach (string purpose in StationPurposes.All)
        {
            Assert.InRange(Count(false, purpose), 300, 1100); // all three occur elsewhere
        }

        Assert.Equal(StationPurposes.For(77, false, false), StationPurposes.For(77, false, false));
        Assert.Equal(StationPurposes.ColourFor(77), StationPurposes.ColourFor(77));
        Assert.True(Enumerable.Range(1, 200).Select(s => StationPurposes.ColourFor(s)).Distinct().Count() >= 6);
        Assert.Equal("traders", StationPurposes.ThemeOf(StationPurposes.None));
        Assert.Equal("miners", StationPurposes.ThemeOf(StationPurposes.Mining));
        Assert.Equal("researchers", StationPurposes.ThemeOf(StationPurposes.Research));
    }

    [Fact]
    public void TheBand_IsPartOfTheBake_AndAReplayPaintsItAgain()
    {
        var content = Content(purposeKits: false);
        var kit = content.KitByKey("plain_small")!;
        StructureTemplate? ByKey(string key) => content.TemplateByKey(StructureKit.KindStation, key);
        var plain = StationKitComposer.Compose(kit, ByKey, 7, content, out _, out var failure)!;
        Assert.Null(failure);
        var dyed = StationKitComposer.Compose(kit, ByKey, 7, content, out var composition, out failure, StationPurposes.Research, 0x2FA48E)!;
        Assert.Null(failure);
        Assert.Equal((StationPurposes.Research, 0x2FA48E), (composition.Purpose, composition.Colour));

        int dyedCells = 0;
        for (int x = 0; x < dyed.Width; x++)
        {
            for (int y = 0; y < dyed.Height; y++)
            {
                for (int z = 0; z < dyed.Length; z++)
                {
                    Assert.Equal(plain.Get(x, y, z), dyed.Get(x, y, z)); // the same blocks: only dyes differ
                    Assert.Equal(0, plain.GetModifier(x, y, z).Tint);
                    dyedCells += dyed.GetModifier(x, y, z).Tint == 0x2FA48E ? 1 : 0;
                }
            }
        }

        Assert.True(dyedCells > 40, $"only {dyedCells} cells carry the band");
        var replay = StationKitComposer.Replay(composition, ByKey, content, kit.Tier, out failure)!;
        Assert.Null(failure);
        for (int x = 0; x < dyed.Width; x++)
        {
            for (int y = 0; y < dyed.Height; y++)
            {
                for (int z = 0; z < dyed.Length; z++)
                {
                    Assert.Equal(dyed.GetModifier(x, y, z), replay.GetModifier(x, y, z));
                }
            }
        }
    }
}
