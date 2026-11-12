// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
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
/// A station pinned to a complete template replays that template whatever the template coin says (from terrain generation
/// 38; an older world keeps its old replay exactly, #3510). A fresh station whose
/// tier has complete templates but no kits takes a template even when the coin says no (<c>PickTemplateOrKit</c>) and pins
/// it; the replay used to apply the pinned template only when the coin said yes, so on the next session such a station
/// stamped the procedural interior over its template blocks.
/// </summary>
public sealed class StationTemplatePinTests : IDisposable
{
    private static readonly string[] StationTiers = { "small", "medium", "large", "huge", "colossal" };

    /// <summary>The template use of the test worlds: above Off (so a fresh station draws at all), the coin mostly says no.</summary>
    private const Frequency TemplateUse = Frequency.Rare;

    /// <summary>The station every start system has (<c>UniverseGenerator</c>: the start system's first station id).</summary>
    private const string StartStationId = "sys0-st";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_stationpin_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }

    [Theory]
    [InlineData(WorldDescription.LandmarkGeneration)]
    [InlineData(WorldDescription.StructurePipelineGeneration)]
    public void AStationPinnedToATemplateWhileTheCoinSaidNo_ReplaysTheTemplate_FromGeneration38(int generation)
    {
        string stationId, pinnedKey, markers, blocks;
        var content = TemplateOnlyContent();
        var server = Started(content, generation, out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");
            server.EnterSpace("Pilot");
            stationId = FirstStationTheCoinRefuses(server);
            Board(server, stationId);

            // The coin said no, yet the tier has templates and no kits: the template is taken, pinned and stamped.
            pinnedKey = server.Metadata.StationTemplates[stationId];
            Assert.StartsWith("test_pin_station_", pinnedKey, StringComparison.Ordinal);
            bool pipeline = generation >= WorldDescription.StructurePipelineGeneration;
            var expected = StationGenerator.FromTemplate(content.StationTemplateByKey(pinnedKey)!, content,
                pipeline ? ModuleMaterials.ForStation(content) : null, markerSettings: pipeline);
            var origin = server.StationStampOriginForTest(stationId);
            Assert.Equal(MarkersOf(expected, origin), MarkersOf(server));
            AssertStamped(server, expected, origin);

            markers = MarkersOf(server);
            blocks = BlocksOf(server);
            server.LeaveStation("Pilot"); // undock, so the restarted server boards the same station again from space
            server.Stop();
        }

        // The next session: the coin still says no. From generation 38 the pinned template is what was stamped, so it is
        // replayed; an older world keeps its old replay exactly — the procedural interior, stamped over the template's blocks.
        var content2 = TemplateOnlyContent();
        var server2 = Started(content2, generation, out var repo2);
        using (repo2)
        {
            server2.AddLocalPlayer("Pilot");
            server2.EnterSpace("Pilot");
            Board(server2, stationId);
            Assert.Equal(pinnedKey, server2.Metadata.StationTemplates[stationId]);
            if (generation >= WorldDescription.StructurePipelineGeneration)
            {
                Assert.Equal(markers, MarkersOf(server2));
                Assert.Equal(blocks, BlocksOf(server2));
            }
            else
            {
                string tier = pinnedKey.Substring("test_pin_station_".Length);
                long sSeed = server2.Metadata.Seed ^ WorldGenerator.StableHash("station:" + stationId);
                var procedural = StationGenerator.Generate(tier, sSeed, content2);
                Assert.Equal(MarkersOf(procedural, server2.StationStampOriginForTest(stationId)), MarkersOf(server2));
                Assert.NotEqual(markers, MarkersOf(server2));
            }

            server2.Stop();
        }
    }

    /// <summary>The shipped content with one complete, drawable template per station tier and no station kit (and no
    /// station generator entry), so a fresh station's pick always lands on a template.</summary>
    private static GameContent TemplateOnlyContent()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var waystation = content.StationTemplates.First(s => s.Key == "pocket_waystation");
        var stations = StationTiers.Select(tier =>
        {
            var t = TemplatePipelineTests.Clone(waystation);
            t.Key = "test_pin_station_" + tier;
            t.Tier = tier;
            t.PinOnly = false;
            return t;
        }).ToList();
        content.SetStructureTemplates(stations, content.SettlementTemplates);
        content.SetStructureKits(content.StructureKits.Where(k => k.KindOrDefault != StructureKit.KindStation).ToList());
        return content;
    }

    private SvGameServer Started(GameContent content, int generation, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "stationpin" + generation));
        var config = new ServerConfig
        {
            WorldName = "stationpin" + generation,
            Seed = SeedWhoseStartStationCoinSaysNo(),
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            World = new WorldDescription
            {
                SpaceStations = Frequency.Frequent,
                StationTemplateUse = TemplateUse,
                TerrainGeneration = generation,
            },
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>The template coin of a station — the legacy roll the server draws first from the station's own stream: a
    /// value at or above the option's probability says no.</summary>
    private static double Coin(long worldSeed, string stationId)
    {
        long sSeed = worldSeed ^ WorldGenerator.StableHash("station:" + stationId);
        return new Random(unchecked((int)(sSeed ^ (sSeed >> 32)))).NextDouble();
    }

    /// <summary>The first world seed whose start system's station (every start system has one, <c>sys0-st</c>) gets a coin
    /// that says no.</summary>
    private static long SeedWhoseStartStationCoinSaysNo()
        => Enumerable.Range(1, 1000).Select(s => (long)s).First(s => Coin(s, StartStationId) >= TemplateUse.Probability());

    /// <summary>The first generated station in the pilot's sight whose template coin says no.</summary>
    private static string FirstStationTheCoinRefuses(SvGameServer server)
    {
        double useP = server.Metadata.Description.StationTemplateUse.Probability();
        var all = server.SpaceEntitiesFor("Pilot")
            .Where(e => e.Kind == CombatEntityKind.SpaceStation && !DerelictPlacer.IsDerelictId(e.Id))
            .ToList();
        var refused = all.FirstOrDefault(e => Coin(server.Metadata.Seed, e.Id) >= useP);
        Assert.True(refused is not null,
            "no station whose coin says no: " + string.Join(", ", all.Select(e => $"{e.Id}={Coin(server.Metadata.Seed, e.Id):F3}")));
        return refused!.Id;
    }

    private static void Board(SvGameServer server, string stationId)
    {
        var station = server.SpaceEntitiesFor("Pilot").First(e => e.Id == stationId);
        server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
        server.BoardStation("Pilot", stationId);
        Assert.True(server.InStation("Pilot"));
    }

    /// <summary>Every non-air cell of <paramref name="expected"/> stands in the station world at its stamp origin — except the
    /// 3×3 hull pad the stamp lays under the spawn point.</summary>
    private static void AssertStamped(SvGameServer server, StationStructure expected, Vector3i origin)
    {
        // The stamp's own choice: the spawn marker, else the first post (the waystation has no spawn marker).
        var spawnMarker = server.SpaceStationMarkers.FirstOrDefault(m => m.Type == "spawn");
        if (spawnMarker.Type is null)
        {
            spawnMarker = server.SpaceStationMarkers.First(m => m.Type is "vendor" or "heal_tank" or "quarters");
        }

        var spawn = spawnMarker.Pos.ToBlock();
        int cells = 0;
        var wrong = new System.Collections.Generic.List<string>();
        for (int x = 0; x < expected.Width; x++)
            for (int y = 0; y < expected.Height; y++)
                for (int z = 0; z < expected.Length; z++)
                {
                    var cell = new Vector3i(origin.X + x, origin.Y + y, origin.Z + z);
                    ushort b = expected.Get(x, y, z);
                    if (b == 0 || (cell.Y == spawn.Y - 1 && Math.Abs(cell.X - spawn.X) <= 1 && Math.Abs(cell.Z - spawn.Z) <= 1))
                    {
                        continue;
                    }

                    cells++;
                    ushort got = server.World.GetBlock(cell).Value;
                    if (got != b)
                    {
                        wrong.Add($"{x},{y},{z}: {b}->{got}");
                    }
                }

        Assert.True(cells > 0, "the template has blocks");
        Assert.True(wrong.Count == 0, $"{wrong.Count}/{cells} template cells differ: " + string.Join("; ", wrong.Take(20)));
    }

    private static string MarkersOf(SvGameServer server)
        => string.Join(";", server.SpaceStationMarkers.Select(m => $"{m.Type}@{m.Pos.X:F1},{m.Pos.Y:F1},{m.Pos.Z:F1}").OrderBy(s => s, StringComparer.Ordinal));

    private static string MarkersOf(StationStructure expected, Vector3i origin)
        => string.Join(";", expected.Markers
            .Select(m => $"{m.Type}@{origin.X + m.LocalPos.X + 0.5f:F1},{origin.Y + m.LocalPos.Y + 0.5f:F1},{origin.Z + m.LocalPos.Z + 0.5f:F1}")
            .OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>Every block and shape of the station world's build box — the template plus anything a different interior
    /// stamped at the same origin would add.</summary>
    private static string BlocksOf(SvGameServer server)
    {
        var sb = new System.Text.StringBuilder();
        for (int x = 0; x < 80; x++)
            for (int y = 56; y < 90; y++)
                for (int z = 0; z < 80; z++)
                {
                    var p = new Vector3i(x, y, z);
                    var b = server.World.GetBlock(p);
                    if (!b.IsAir)
                    {
                        sb.Append(x).Append(',').Append(y).Append(',').Append(z).Append(':').Append(b.Value).Append(':').Append(server.World.GetShape(p)).Append(';');
                    }
                }

        return sb.ToString();
    }
}
