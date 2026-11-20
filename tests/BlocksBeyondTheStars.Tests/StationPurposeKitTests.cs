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
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — the shipped station kits made for a purpose (<c>tools/gen_station_purpose_modules.py</c>): one per
/// tier and purpose, each with the purpose's own rooms; a trade station has a stall for each of the four trades and the
/// cabins to staff them; a room's sound marker keeps its settings through the bake; the classic kits have no purpose and
/// are what a station without one draws.
/// </summary>
public sealed class StationPurposeKitTests
{
    private static readonly Lazy<GameContent> Shipped = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static GameContent Content => Shipped.Value;

    private static readonly string[] Tiers = { "small", "medium", "large", "huge", "colossal" };

    /// <summary>The rooms a purpose's kit always has, and from which tier on (by index into <see cref="Tiers"/>).</summary>
    private static readonly Dictionary<string, (string Room, int FromTier)[]> OwnRooms = new(StringComparer.Ordinal)
    {
        [StationPurposes.Trade] = new[] { ("bazaar", 0), ("customs", 1) },
        [StationPurposes.Mining] = new[] { ("ore_bay", 0), ("smelter", 1), ("assay", 2) },
        [StationPurposes.Research] = new[] { ("lab", 0), ("observatory", 1), ("archive", 2) },
    };

    private static StructureTemplate? ByKey(string key) => Content.TemplateByKey(StructureKit.KindStation, key);

    private static List<StructureKit> StationKits() => Content.StructureKits.Where(k => k.Kind == StructureKit.KindStation).ToList();

    [Fact]
    public void EveryTierHasOneKitPerPurpose_AndTheClassicKitsHaveNone()
    {
        var kits = StationKits();
        foreach (string tier in Tiers)
        {
            var ofTier = kits.Where(k => k.Tier == tier).ToList();
            Assert.Single(ofTier, k => k.Purposes.Count == 0); // the classic kit: every older world, every station without a purpose
            foreach (string purpose in StationPurposes.All)
            {
                var kit = Assert.Single(ofTier, k => k.Purposes.Contains(purpose));
                Assert.Equal(new[] { purpose }, kit.Purposes);
                Assert.Equal($"station_{tier}_{purpose}_1", kit.Key);
                Assert.Equal($"st_{tier}_hub", kit.Start);

                // Everything the classic kit of the tier has is in it too (the profession rooms, the old halls).
                var classic = ofTier.Single(k => k.Purposes.Count == 0);
                Assert.Subset(kit.Entries.Select(e => e.Module).ToHashSet(StringComparer.Ordinal), classic.Entries.Select(e => e.Module).ToHashSet(StringComparer.Ordinal));
            }
        }

        Assert.Equal(Tiers.Length * (1 + StationPurposes.All.Length), kits.Count);
    }

    [Fact]
    public void APurposeKit_ComposesWithItsOwnRooms_OnEverySeed()
    {
        foreach (var kit in StationKits().Where(k => k.Purposes.Count == 1))
        {
            string purpose = kit.Purposes[0];
            int tier = Array.IndexOf(Tiers, kit.Tier);
            for (long seed = 1; seed <= 5; seed++)
            {
                var station = StationKitComposer.Compose(kit, ByKey, seed, Content, out var composition, out var failure, purpose, StationPurposes.ColourFor(seed));
                Assert.True(station != null, $"kit '{kit.Key}' seed {seed}: {failure}");
                var modules = composition.Modules.Select(m => m.Key).ToList();
                foreach (var (room, fromTier) in OwnRooms[purpose])
                {
                    if (tier >= fromTier)
                    {
                        Assert.Contains($"st_{kit.Tier}_{room}", modules);
                    }
                }

                // No room of another purpose ever.
                foreach (var other in OwnRooms.Where(o => o.Key != purpose).SelectMany(o => o.Value))
                {
                    Assert.DoesNotContain($"st_{kit.Tier}_{other.Room}", modules);
                }

                Assert.Contains(station!.Markers, m => m.Type == "spawn");
                Assert.Contains(station.Markers, m => m.Type == "hangar");
            }
        }
    }

    [Fact]
    public void ATradeStation_HasAStallForEachOfTheFourTrades_AndTheCabinsToStaffThem()
    {
        foreach (var kit in StationKits().Where(k => k.Purposes.Contains(StationPurposes.Trade)))
        {
            for (long seed = 1; seed <= 3; seed++)
            {
                var station = StationKitComposer.Compose(kit, ByKey, seed, Content, out _, out var failure, StationPurposes.Trade, StationPurposes.ColourFor(seed));
                Assert.True(station != null, $"kit '{kit.Key}' seed {seed}: {failure}");
                int stalls = station!.Markers.Count(m => m.Type == "vendor");
                int cabins = station.Markers.Count(m => m.Type == "cabin");
                Assert.True(stalls >= 4, $"kit '{kit.Key}' seed {seed}: only {stalls} stalls");
                // Every stall and the mission board get somebody: a station with a purpose staffs those before every
                // other post (GameServerStationCrew), so they need a cabin each.
                int posts = stalls + station.Markers.Count(m => m.Type == "mission_board");
                Assert.True(cabins >= posts, $"kit '{kit.Key}' seed {seed}: {cabins} cabins for {posts} stalls and boards");
            }
        }
    }

    [Fact]
    public void AboardTheStartSystemsTradeStation_AllFourTradesHaveATrader()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_tradekit_" + Guid.NewGuid().ToString("N"));
        try
        {
            int tradeKitStations = 0;
            for (long seed = 1; seed <= 6 && tradeKitStations < 2; seed++)
            {
                using var repo = new SqliteWorldRepository(new SaveGamePaths(root, "tradekit_" + seed));
                var config = new ServerConfig
                {
                    WorldName = "tradekit_" + seed,
                    Seed = seed,
                    AutoSaveIntervalMinutes = 9999,
                    PlaceStarterShip = false,
                    PlaceSettlements = false,
                    PlaceWrecks = false,
                    World = new WorldDescription { SpaceStations = Frequency.Frequent, StationTemplateUse = Frequency.Rare, TerrainGeneration = WorldDescription.CurrentTerrainGeneration },
                };
                config.Rules.FreeSpaceFlight = true;
                var server = new SvGameServer(config, ContentLoader.LoadFromDirectory(TestPaths.DataDir()), new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();
                server.AddLocalPlayer("Pilot");
                server.EnterSpace("Pilot");
                var station = server.SpaceEntitiesFor("Pilot").First(e => e.Kind == BlocksBeyondTheStars.GameServer.CombatEntityKind.SpaceStation);
                server.ShipMove("Pilot", station.Position.X, station.Position.Y, station.Position.Z - 8f);
                server.BoardStation("Pilot", station.Id);

                string kit = server.StationKitForTest(station.Id);
                if (kit.Length > 0)
                {
                    // A kit station of the start system: it trades, built from the trade kit of its tier.
                    Assert.Equal(StationPurposes.Trade, server.StationPurposeForTest(station.Id).Purpose);
                    Assert.EndsWith("_trade_1", kit, StringComparison.Ordinal);
                    var trades = server.NpcCrewForTest().Where(n => n.Role == "vendor").Select(n => n.Theme).ToHashSet(StringComparer.Ordinal);
                    Assert.Superset(new HashSet<string>(StringComparer.Ordinal) { "traders", "miners", "researchers", "settlers" }, trades);
                    tradeKitStations++;
                }

                // (No kit: this seed drew one of the hand-made station templates — it has no purpose.)
                server.Stop();
            }

            Assert.True(tradeKitStations > 0, "no seed composed the start system's station from a kit");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    [Fact]
    public void ARoomsSoundMarker_KeepsItsSettingsThroughTheBake()
    {
        var kit = Content.KitByKey("station_medium_research_1")!;
        var station = StationKitComposer.Compose(kit, ByKey, 3, Content, out _, out var failure, StationPurposes.Research, 0)!;
        Assert.Null(failure);
        var sounds = station.Markers.Where(m => m.Type == LandmarkRules.SoundMarker).ToList();
        Assert.NotEmpty(sounds); // the laboratory's and the observatory's
        Assert.All(sounds, m => Assert.Contains("sound=amb_station_lab", m.Data, StringComparison.Ordinal));

        // A marker without settings is what it always was.
        Assert.All(station.Markers.Where(m => m.Type is "cabin" or "vendor" or "spawn"), m => Assert.Equal(string.Empty, m.Data));
    }

    [Fact]
    public void ThePurposeRooms_AreStoredCompact_AndKeepTheirWalkwaysFree()
    {
        foreach (string tier in Tiers)
        {
            foreach (var (room, _) in OwnRooms.Values.SelectMany(v => v))
            {
                var t = ByKey($"st_{tier}_{room}");
                Assert.True(t != null, $"st_{tier}_{room} is missing");
                // Written compact, expanded by the loader: the deck, the hull and the doors are all there as cells.
                Assert.Contains(t!.Cells, c => c.Kind == "block" && c.Id == "steel_floor" && c.Y == 0);
                Assert.Contains(t.Cells, c => c.Kind == "block" && c.Id == "iron_wall" && c.Y == t.Height - 1);
                Assert.True(t.Cells.Count(c => c.Port == "door") >= 12, $"st_{tier}_{room}: a dead end (fewer than two door ports)");

                // The cross of walkways between the doors: nothing stands in it up to head height.
                int cx = t.Width / 2, cz = t.Length / 2;
                var blocked = t.Cells.Where(c => c.Kind == "block" && c.Y is 1 or 2
                    && c.X >= 1 && c.X <= t.Width - 2 && c.Z >= 1 && c.Z <= t.Length - 2
                    && ((c.X == cx - 1 || c.X == cx) && (c.Z == cz - 1 || c.Z == cz))).ToList();
                Assert.True(blocked.Count == 0, $"st_{tier}_{room}: {blocked.Count} block(s) in the middle of the walkways");
            }
        }
    }

    /// <summary>One room works, the others are decoration. A block a crafting station hangs on (a lit forge is a refinery
    /// for whoever stands beside it, an algae tank an algae station …) hands out a service, and a service is a decision:
    /// the maintainer decided the smelter's two furnaces are real forges (2026-11-20) and nothing else is. A Crystal Net
    /// device or conduit would put a circuit into every such station's world — none anywhere, so the furnaces are on no
    /// circuit (the maintainer's rule: nothing of a place is wired into the net, and no function rides in unasked).</summary>
    [Fact]
    public void OnlyTheSmelter_HoldsABlockThatWorksWhereItStands()
    {
        var working = new HashSet<string>(StringComparer.Ordinal)
        {
            "workbench", "forge", "detoxifier", "matter_forge", "algae_tank", "campfire", "factory_terminal", "decontaminator",
            "bio_lab", "heal_tank", "clone_tank", "vending_machine", "base_core", "station_core", "ship_core",
        };
        foreach (string tier in Tiers)
        {
            foreach (var (room, _) in OwnRooms.Values.SelectMany(v => v))
            {
                var t = ByKey($"st_{tier}_{room}")!;
                bool smelter = room == "smelter";
                foreach (var c in t.Cells.Where(c => c.Kind == "block"))
                {
                    // The smelter's furnaces are real forges (a mining station's service, decided 2026-11-20) — and nothing else.
                    bool itsFurnace = smelter && c.Id == "forge";
                    Assert.False(working.Contains(c.Id) && !itsFurnace, $"st_{tier}_{room}: the working block '{c.Id}' at ({c.X},{c.Y},{c.Z})");
                    Assert.True(TemplateDevices.DataFor(c) is null, $"st_{tier}_{room}: the Crystal Net cell '{c.Id}' at ({c.X},{c.Y},{c.Z})");
                    Assert.False(c.Id.Contains("conduit", StringComparison.Ordinal), $"st_{tier}_{room}: a conduit at ({c.X},{c.Y},{c.Z})");
                }

                Assert.Equal(smelter ? 2 : 0, t.Cells.Count(c => c.Kind == "block" && c.Id == "forge"));

                // The only markers: where a visitor or the crew stands, and the room's own sound.
                Assert.All(t.Cells.Where(c => c.Kind == "marker"), c => Assert.Contains(c.Id, new[] { "room", "vendor", LandmarkRules.SoundMarker }));
            }
        }
    }
}
