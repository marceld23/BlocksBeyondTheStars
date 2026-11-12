// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Station templates and station generator entries in a running world (#3518, #3519): tokens, marker settings,
/// the client's emitter list, an entry drawn and recorded.</summary>
public sealed partial class TemplatePipelineWorldTests
{
    private static readonly string[] StationTiers = { "small", "medium", "large", "huge", "colossal" };

    [Theory]
    [InlineData(Gen38)]
    [InlineData(Gen37)]
    public void AStationTemplate_SendsItsSoundToTheClient_AndItsTokensBecomeBlocks_FromGeneration38(int generation)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        Vector3i wall = default, floor = default;
        var stations = StationTiers.Select(tier =>
        {
            var t = TemplatePipelineTests.StationWithTokens(
                TemplatePipelineTests.Clone(content.StationTemplates.First(s => s.Key == "pocket_waystation")), out wall, out floor);
            t.Key = "test_station_" + tier;
            t.Tier = tier;
            t.PinOnly = false;
            return t;
        }).ToList();

        BoardFirstStation(content, stations, "station" + generation, generation, (server, pilot, stationId) =>
        {
            Assert.StartsWith("test_station_", server.Metadata.StationTemplates[stationId], StringComparison.Ordinal);
            var origin = server.StationStampOriginForTest(stationId);
            ushort At(Vector3i local) => server.World.GetBlock(new Vector3i(origin.X + local.X, origin.Y + local.Y, origin.Z + local.Z)).Value;
            var emitters = server.LandmarkListForTest(pilot).Emitters;
            if (generation >= Gen38)
            {
                Assert.Equal(content.GetBlock("iron_wall")!.NumericId.Value, At(wall));
                Assert.Equal(content.GetBlock("steel_floor")!.NumericId.Value, At(floor));
                var emitter = Assert.Single(emitters);
                Assert.Equal("amb_sounder", emitter.Sound);
                Assert.Equal(20f, emitter.Radius);
            }
            else
            {
                Assert.Equal(0, At(wall)); // a token stayed air before generation 38
                Assert.Empty(emitters);
            }
        });
    }

    [Fact]
    public void AStationGeneratorEntry_IsDrawnAndRecorded_AndBuiltByItsSource()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var entries = StationTiers.Select(tier => new StructureTemplate
        {
            Key = "test_station_entry_" + tier,
            Tier = tier,
            Source = "station",
            SourceParams = "tier=small",
        }).ToList();

        BoardFirstStation(content, entries, "stationentry", Gen38, (server, _, stationId) =>
        {
            Assert.StartsWith("source:test_station_entry_", server.Metadata.StationTemplates[stationId], StringComparison.Ordinal);
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "vendor");
            Assert.Contains(server.SpaceStationMarkers, m => m.Type == "mission_board");
        });
    }

    /// <summary>Boots a world whose station pool is <paramref name="stations"/> (no station kits), docks the first station and
    /// hands the server, the pilot and the station id to <paramref name="look"/>.</summary>
    private void BoardFirstStation(GameContent content, List<StructureTemplate> stations, string name, int generation,
        Action<SvGameServer, PlayerSession, string> look)
    {
        content.SetStructureTemplates(stations, content.SettlementTemplates);
        content.SetStructureKits(content.StructureKits.Where(k => k.KindOrDefault != StructureKit.KindStation).ToList());

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, name));
        var config = new ServerConfig
        {
            WorldName = name,
            Seed = 42,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription
            {
                SpaceStations = Frequency.Frequent,
                StationTemplateUse = Frequency.Frequent,
                TerrainGeneration = generation,
            },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        try
        {
            var pilot = server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == CombatEntityKind.SpaceStation);
            server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
            server.BoardStation("Pilot", station.Id);
            Assert.True(server.InStation("Pilot"));
            look(server, pilot, station.Id);
        }
        finally
        {
            server.Stop();
        }
    }
}
