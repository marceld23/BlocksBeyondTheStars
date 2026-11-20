// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Bio;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;
using SvSession = BlocksBeyondTheStars.GameServer.PlayerSession;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3870: a research station takes samples. Aboard one, a sample handed to the station archive pays data fragments by the
/// species' rarity — once per species and player; one sample leaves the case. Anywhere else, or for a species already
/// given, nothing moves and the player is told why.
/// </summary>
public sealed class StationArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_archive_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a locked temp file is no test failure
        }
    }

    private SvGameServer Started(string world, RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription
            {
                SpaceStations = Frequency.Frequent,
                StationTemplateUse = Frequency.Rare,
                TerrainGeneration = WorldDescription.LivelyPlacesGeneration,
            },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private static string Board(SvGameServer server, string playerId)
    {
        server.EnterSpace(playerId);
        var station = server.SpaceEntitiesFor(playerId).First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
        server.ShipMove(playerId, station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation(playerId, station.Id);
        return station.Id;
    }

    private static List<string> Rejections(RecordingTransport t, SvSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(r => r.Reason).ToList();

    private static List<string> Messages(RecordingTransport t, SvSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ServerMessage>().Select(m => m.Text).ToList();

    [Fact]
    public void AboardAResearchStation_TheArchivePaysByRarity_OncePerSpecies()
    {
        var t = new RecordingTransport();
        var server = Started("archive", t, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string station = Board(server, "Pilot");
            server.SetStationPurposeForTest(station, StationPurposes.Research);
            uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 3);
            string key = ItemKey.WithSeed(BioItems.Sample, fern);
            int rarity = server.BioProfileForTest(fern)!.Rarity;
            int before = p.State.Inventory.CountOf(SvGameServer.ArchiveReward);

            server.GiveSampleToStationForTest(p, key);

            Assert.Equal(before + SvGameServer.ArchiveFragments(rarity), p.State.Inventory.CountOf(SvGameServer.ArchiveReward));
            Assert.Equal(2, p.State.SampleCase.CountOf(key)); // one sample leaves the case, the rest stays
            Assert.Contains("@srv.station.archive_thanks:" + SvGameServer.ArchiveFragments(rarity), Messages(t, p));
            Assert.Equal(1, server.GivenToStationsForTest(p.State.PlayerId));

            // The same species again: declined, nothing moves.
            server.GiveSampleToStationForTest(p, key);
            Assert.Equal(before + SvGameServer.ArchiveFragments(rarity), p.State.Inventory.CountOf(SvGameServer.ArchiveReward));
            Assert.Equal(2, p.State.SampleCase.CountOf(key));
            Assert.Contains("@srv.station.archive_known", Rejections(t, p));
            server.Stop();
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 3)]
    [InlineData(4, 5)]
    [InlineData(9, 5)] // a tier from later is capped
    public void TheReward_IsOnePlusTheRarity(int rarity, int fragments)
        => Assert.Equal(fragments, SvGameServer.ArchiveFragments(rarity));

    [Fact]
    public void OnATradeStation_OrForASampleNotInTheCase_NothingMoves()
    {
        var t = new RecordingTransport();
        var server = Started("archive_trade", t, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string station = Board(server, "Pilot");
            server.SetStationPurposeForTest(station, StationPurposes.Trade);
            uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
            string key = ItemKey.WithSeed(BioItems.Sample, fern);

            server.GiveSampleToStationForTest(p, key);
            Assert.Equal(1, p.State.SampleCase.CountOf(key));
            Assert.Equal(0, server.GivenToStationsForTest(p.State.PlayerId));
            Assert.Contains("@srv.station.archive_not_here", Rejections(t, p));

            // Aboard a research station, but the sample is not in the case: quietly nothing.
            server.SetStationPurposeForTest(station, StationPurposes.Research);
            server.GiveSampleToStationForTest(p, ItemKey.WithSeed(BioItems.Sample, fern + 1));
            server.GiveSampleToStationForTest(p, "data_fragment");
            Assert.Equal(0, server.GivenToStationsForTest(p.State.PlayerId));
            server.Stop();
        }
    }

    [Fact]
    public void TheHandOver_IsServedWhileTheMenuHoldsASingleplayerWorld()
    {
        var t = new RecordingTransport();
        var server = Started("archive_paused", t, out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Pilot");
            string station = Board(server, "Pilot");
            server.SetStationPurposeForTest(station, StationPurposes.Research);
            uint fern = server.GiveFloraSampleForTest(p, "flora_fern", 1);
            server.PauseForTest(p, true);
            Assert.True(server.IsPaused);

            server.HandlePayloadForTest(p.ConnectionId, NetCodec.Encode(
                new GiveSampleToStationIntent { Item = ItemKey.WithSeed(BioItems.Sample, fern) }));

            Assert.Equal(1, server.GivenToStationsForTest(p.State.PlayerId));
            server.Stop();
        }
    }
}
