// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The hash-variety package for settlements (#2689, #2690): the greenhouse crop and the authored-module roll used the
/// raw <c>WorldGenerator.StableHash</c> with the plot (or the district's row) at the end of the key, so neighbouring
/// plots decided alike — 62 % single-crop settlements and never three crops, 83–92 % "all modules or none", a city
/// district column always the same. Fresh decisions now take the mixed <see cref="StableHashing"/> pick; whatever a
/// world already decided keeps the historical pick: a settlement whose record predates #2689 keeps its crops, a record
/// from before #1872 freezes the legacy module roll, and a pinned composition replays without rolling at all.
/// </summary>
public sealed class SettlementHashVarietyTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public SettlementHashVarietyTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_hashvariety_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The crop set in the generator's order: every cultivated key that exists as a block.</summary>
    private List<ushort> CropSet()
        => FloraCatalog.CultivatedKeys().Select(k => _content.GetBlock(k)?.NumericId.Value ?? (ushort)0).Where(id => id != 0).ToList();

    /// <summary>The historical crop pick, written out — the formula a greenhouse planted before #2689 grows.</summary>
    private static int HistoricalCrop(string tier, long seed, int plot, int n)
        => (int)((WorldGenerator.StableHash($"crop:{tier}:{seed}:{plot}") & 0x7fffffff) % n);

    /// <summary>The historical module roll, written out (#1827): the low 24 bits of the raw hash.</summary>
    private static double HistoricalRoll(string key) => (WorldGenerator.StableHash(key) & 0xFFFFFF) / (double)0x1000000;

    /// <summary>Every procedural greenhouse of a plain (non-kit) settlement: its plot index and the crops growing in it.
    /// The marker stands at the centre of the full 6-wide footprint, so the plot follows from the 8-block stride.</summary>
    private static List<(int Plot, HashSet<ushort> Crops)> Greenhouses(SettlementStructure s, HashSet<ushort> crops)
    {
        int rows = (s.Length - 1) / SettlementGenerator.Plot;
        var result = new List<(int, HashSet<ushort>)>();
        foreach (var m in s.Markers.Where(m => m.Type == "greenhouse"))
        {
            int cxp = (m.LocalPos.X - 4) / SettlementGenerator.Plot, czp = (m.LocalPos.Z - 4) / SettlementGenerator.Plot;
            int ox = cxp * SettlementGenerator.Plot + 1, oz = czp * SettlementGenerator.Plot + 1;
            var found = new HashSet<ushort>();
            for (int x = ox; x < ox + 6; x++)
                for (int z = oz; z < oz + 6; z++)
                    for (int y = 0; y < s.Height; y++)
                    {
                        if (crops.Contains(s.Get(x, y, z))) found.Add(s.Get(x, y, z));
                    }

            result.Add((cxp * rows + czp, found));
        }

        return result;
    }

    /// <summary>A hollow box module with a resident marker — enough for the roll tests, which only read the composition.</summary>
    private static StructureTemplate Box(string key, string role, string tier, int w, int h, int l)
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = tier, Kind = "settlement", Role = role, Width = w, Height = h, Length = l };
        for (int x = 0; x < w; x++)
            for (int z = 0; z < l; z++)
            {
                t.Cells.Add(new TemplateCell { X = x, Y = 0, Z = z, Kind = "block", Id = "gold_block" });
            }

        t.Cells.Add(new TemplateCell { X = w / 2, Y = 1, Z = l / 2, Kind = "marker", Id = "npc" });
        return t;
    }

    /// <summary>A town-style module for every plot role, so every plot of a town (which never leaves a plot open) takes a
    /// module exactly when its roll says so — the recorded composition IS the roll.</summary>
    private static StructureTemplate[] TownModules() => new[]
    {
        Box("t_market", StructureRoles.Market, "town", 6, 6, 6),
        Box("t_board", StructureRoles.Board, "town", 6, 6, 6),
        Box("t_house", StructureRoles.House, "town", 6, 6, 6),
        Box("t_greenhouse", StructureRoles.Greenhouse, "town", 6, 6, 6),
    };

    private static StructureTemplate[] CityModules() => new[]
    {
        Box("c_housing", StructureRoles.CityHousing, StructureRoles.MetropolisTier, 12, 6, 12),
        Box("c_market", StructureRoles.CityMarket, StructureRoles.MetropolisTier, 12, 6, 12),
        Box("c_hall", StructureRoles.CityHall, StructureRoles.MetropolisTier, 12, 6, 12),
        Box("c_garden", StructureRoles.CityGarden, StructureRoles.MetropolisTier, 12, 6, 12),
        Box("c_tower", StructureRoles.CityTower, StructureRoles.MetropolisTier, 12, 6, 12),
    };

    private static void AssertSameStructure(SettlementStructure a, SettlementStructure b)
    {
        Assert.Equal((a.Width, a.Height, a.Length), (b.Width, b.Height, b.Length));
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                    Assert.Equal(a.GetShape(x, y, z), b.GetShape(x, y, z));
                    Assert.Equal(a.GetModifier(x, y, z), b.GetModifier(x, y, z));
                }

        Assert.Equal(a.Markers.Select(m => $"{m.Type}@{m.LocalPos.X},{m.LocalPos.Y},{m.LocalPos.Z}"),
            b.Markers.Select(m => $"{m.Type}@{m.LocalPos.X},{m.LocalPos.Y},{m.LocalPos.Z}"));
    }

    // ---------------------------------------------------------------- #2689: the greenhouse crop

    [Fact]
    public void LegacyCrops_GrowTheHistoricalPick_AndTheMixedPickChangesNothingButTheCrops()
    {
        var cropSet = CropSet();
        var crops = cropSet.ToHashSet();
        Assert.True(cropSet.Count >= 3, "three cultivated crops are expected (berry, grain, mushroom)");

        int greenhouses = 0;
        foreach (var tier in new[] { "village", "town", "city" })
        {
            for (long seed = 1; seed <= 10; seed++)
            {
                var legacy = SettlementGenerator.Generate(tier, false, seed, "grass", _content, legacyCrops: true);
                var mixed = SettlementGenerator.Generate(tier, false, seed, "grass", _content);

                // Same stream: every cell is the same block, or a crop where the other has a crop.
                Assert.Equal((legacy.Width, legacy.Height, legacy.Length), (mixed.Width, mixed.Height, mixed.Length));
                for (int x = 0; x < legacy.Width; x++)
                    for (int y = 0; y < legacy.Height; y++)
                        for (int z = 0; z < legacy.Length; z++)
                        {
                            ushort a = legacy.Get(x, y, z), b = mixed.Get(x, y, z);
                            Assert.True(a == b || (crops.Contains(a) && crops.Contains(b)), $"{tier} seed {seed}: {x},{y},{z} differs beyond the crop");
                        }

                Assert.Equal(legacy.Markers.Select(m => (m.Type, m.LocalPos)), mixed.Markers.Select(m => (m.Type, m.LocalPos)));

                foreach (var (plot, grown) in Greenhouses(legacy, crops))
                {
                    Assert.Equal(cropSet[HistoricalCrop(tier, seed, plot, cropSet.Count)], Assert.Single(grown));
                    greenhouses++;
                }

                foreach (var (plot, grown) in Greenhouses(mixed, crops))
                {
                    Assert.Equal(cropSet[StableHashing.Index($"crop:{tier}:{plot}", seed, cropSet.Count)], Assert.Single(grown));
                }
            }
        }

        Assert.True(greenhouses >= 40, $"the sample should hold plenty of greenhouses, got {greenhouses}");
    }

    [Fact]
    public void MixedCrops_SpreadLikeChance_AndThreeDifferentCropsOccur()
    {
        var cropSet = CropSet();
        var crops = cropSet.ToHashSet();
        int n = cropSet.Count;

        int multi = 0, single = 0, three = 0, legacySingle = 0, legacyThree = 0;
        double expectedSingle = 0;
        foreach (var tier in new[] { "town", "city" })
        {
            for (long seed = 1; seed <= 200; seed++)
            {
                var houses = Greenhouses(SettlementGenerator.Generate(tier, false, seed * 7919, "grass", _content), crops);
                if (houses.Count < 2)
                {
                    continue;
                }

                multi++;
                expectedSingle += Math.Pow(1.0 / n, houses.Count - 1);
                var distinct = houses.SelectMany(h => h.Crops).Distinct().Count();
                if (distinct == 1) single++;
                if (distinct == 3) three++;

                // The same greenhouses under the historical pick (the plots do not move — see the test above).
                var legacyDistinct = houses.Select(h => HistoricalCrop(tier, seed * 7919, h.Plot, n)).Distinct().Count();
                if (legacyDistinct == 1) legacySingle++;
                if (legacyDistinct == 3) legacyThree++;
            }
        }

        Assert.True(multi >= 200, $"the sample should hold plenty of multi-greenhouse settlements, got {multi}");
        double share = (double)single / multi, expected = expectedSingle / multi;
        Assert.True(Math.Abs(share - expected) < 0.1, // ~4 σ on this sample size
            $"single-crop share {share:P0} should be near chance {expected:P0} (historical pick: {(double)legacySingle / multi:P0})");
        Assert.True(three > 0, $"three different crops should occur (historical pick: {legacyThree})");

        // The bug this fixes, measured on the same sample: the historical pick was far off chance.
        Assert.True((double)legacySingle / multi > expected + 0.2, $"historical single-crop share {(double)legacySingle / multi:P0} vs chance {expected:P0}");
    }

    // ---------------------------------------------------------------- #2690: the authored-module roll

    [Fact]
    public void MixedModuleRoll_DecidesEachPlotOnItsOwn_AllOrNoneNearChance()
    {
        const double chance = 0.4;
        var modules = TownModules();
        int settlements = 0, allOrNone = 0, legacyAllOrNone = 0, plots = 0, hits = 0, pairs = 0, agree = 0;
        double expected = 0;
        for (long seed = 1; seed <= 80; seed++)
        {
            var composition = new List<string>();
            SettlementGenerator.Generate("town", false, seed, "grass", _content, modules, chance, composition);
            int k = composition.Count;
            settlements++;
            expected += Math.Pow(chance, k) + Math.Pow(1 - chance, k);

            int taken = composition.Count(c => c.Length > 0);
            if (taken == 0 || taken == k) allOrNone++;
            plots += k;
            hits += taken;
            for (int i = 1; i < k; i++)
            {
                pairs++;
                if ((composition[i].Length > 0) == (composition[i - 1].Length > 0)) agree++;
            }

            int legacyTaken = Enumerable.Range(0, k).Count(p => HistoricalRoll($"module:town:{seed}:{p}") < chance);
            if (legacyTaken == 0 || legacyTaken == k) legacyAllOrNone++;
        }

        double share = (double)allOrNone / settlements;
        Assert.True(share <= expected / settlements + 0.06, $"all-or-none share {share:P0} vs chance {expected / settlements:P1}");
        Assert.InRange((double)hits / plots, chance - 0.06, chance + 0.06);
        double agreeExpected = chance * chance + (1 - chance) * (1 - chance);
        Assert.InRange((double)agree / pairs, agreeExpected - 0.08, agreeExpected + 0.08);

        // The bug, measured on the same plots: the historical roll put most towns at "all or none".
        Assert.True((double)legacyAllOrNone / settlements > 0.4, $"historical all-or-none share {(double)legacyAllOrNone / settlements:P0}");
    }

    [Fact]
    public void CityModuleRoll_ColumnsNoLongerDecideAlike()
    {
        const double chance = 0.5;
        var modules = CityModules();
        int columns = 0, uniform = 0, legacyUniform = 0;
        for (long seed = 1; seed <= 6; seed++)
        {
            var composition = new List<string>();
            CityGenerator.Generate(seed, _content, Array.Empty<CityGenerator.OpenZone>(), modules, chance, composition);
            Assert.Equal(CityGenerator.Modules * CityGenerator.Modules, composition.Count);

            for (int gx = 0; gx < CityGenerator.Modules; gx++)
            {
                var eligible = Enumerable.Range(0, CityGenerator.Modules).Where(gz => CityGenerator.RoleAt(gx, gz) != CityGenerator.Role.Plaza).ToList();
                var taken = eligible.Select(gz => composition[gx * CityGenerator.Modules + gz].Length > 0).Distinct().Count();
                var legacy = eligible.Select(gz => HistoricalRoll($"citymodule:{seed}:{gx}:{gz}") < chance).Distinct().Count();
                columns++;
                if (taken == 1) uniform++;
                if (legacy == 1) legacyUniform++;
            }
        }

        // Chance of a uniform 6- or 7-district column at 50 %: 2 · 0.5⁶ ≈ 3 %; the historical roll: every column.
        Assert.True((double)uniform / columns <= 0.15, $"{uniform}/{columns} columns decided alike");
        Assert.True((double)legacyUniform / columns >= 0.9, $"historical roll: {legacyUniform}/{columns} columns decided alike");
    }

    [Fact]
    public void PinnedComposition_ReplaysBitIdentically_WhateverTheRollFlag()
    {
        var modules = TownModules();
        for (long seed = 1; seed <= 6; seed++)
        {
            var recorded = new List<string>();
            var first = SettlementGenerator.Generate("town", false, seed, "grass", _content, modules, 0.5, recorded);
            Assert.Contains(recorded, k => k.Length > 0);

            // A replay reads the list and never rolls: the legacy flag (and the chance) change nothing.
            var replayList = new List<string>(recorded);
            var replay = SettlementGenerator.Generate("town", false, seed, "grass", _content, modules, 0.5, replayList, legacyModuleRoll: true);
            AssertSameStructure(first, replay);
            Assert.Equal(recorded, replayList);
        }

        var cityModules = CityModules();
        var cityRecorded = new List<string>();
        var city = CityGenerator.Generate(3, _content, Array.Empty<CityGenerator.OpenZone>(), cityModules, 0.5, cityRecorded);
        var cityList = new List<string>(cityRecorded);
        var cityReplay = CityGenerator.Generate(3, _content, Array.Empty<CityGenerator.OpenZone>(), cityModules, 0.5, cityList, legacyModuleRoll: true);
        AssertSameStructure(city, cityReplay);
        Assert.Equal(cityRecorded, cityList);
    }

    [Fact]
    public void PreCompositionRecord_FreezesTheHistoricalRoll()
    {
        const double chance = 0.4;
        var modules = TownModules();
        for (long seed = 1; seed <= 12; seed++)
        {
            var frozen = new List<string>();
            SettlementGenerator.Generate("town", false, seed, "grass", _content, modules, chance, frozen, legacyModuleRoll: true);
            for (int plot = 0; plot < frozen.Count; plot++)
            {
                Assert.Equal(HistoricalRoll($"module:town:{seed}:{plot}") < chance, frozen[plot].Length > 0);
            }
        }

        var cityFrozen = new List<string>();
        CityGenerator.Generate(5, _content, Array.Empty<CityGenerator.OpenZone>(), CityModules(), chance, cityFrozen, legacyModuleRoll: true);
        for (int gx = 0; gx < CityGenerator.Modules; gx++)
            for (int gz = 0; gz < CityGenerator.Modules; gz++)
            {
                bool eligible = CityGenerator.RoleAt(gx, gz) != CityGenerator.Role.Plaza;
                Assert.Equal(eligible && HistoricalRoll($"citymodule:5:{gx}:{gz}") < chance, cityFrozen[gx * CityGenerator.Modules + gz].Length > 0);
            }
    }

    // ---------------------------------------------------------------- the server: which record takes which pick

    [Fact]
    public void RecordGates_PickTheHistoricalHashOnlyWhereTheWorldAlreadyDecidedWithIt()
    {
        // Crops (#2689): a settlement composed right now takes the mixed pick; a legacy re-derive and a record from
        // before the field keep the historical one.
        Assert.False(SvGameServer.UsesLegacyCrops(null, virginAtLoad: true));
        Assert.True(SvGameServer.UsesLegacyCrops(null, virginAtLoad: false));
        Assert.True(SvGameServer.UsesLegacyCrops(new StructurePlacementRecord { Placed = true, Modules = 1 }, virginAtLoad: true));
        Assert.False(SvGameServer.UsesLegacyCrops(
            new StructurePlacementRecord { Placed = true, Modules = 1, HashRevision = StructurePlacementRecord.MixedHashRevision }, virginAtLoad: false));

        // Modules (#2690): only a pre-#1872 record — modules on, no composition — rolls with the historical hash.
        Assert.False(SvGameServer.UsesLegacyModuleRoll(null));
        Assert.True(SvGameServer.UsesLegacyModuleRoll(new StructurePlacementRecord { Placed = true, Modules = 1 }));
        Assert.False(SvGameServer.UsesLegacyModuleRoll(new StructurePlacementRecord { Placed = true, Modules = 1, Composition = new List<string> { "" } }));
        Assert.False(SvGameServer.UsesLegacyModuleRoll(new StructurePlacementRecord { Placed = true, Modules = 0 }));
        Assert.False(SvGameServer.UsesLegacyModuleRoll(new StructurePlacementRecord { Placed = false, Modules = 1 }));
    }

    private SvGameServer Started(string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "meadowlands",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
        };
        config.World.SettlementTemplateUse = Frequency.Off; // purely procedural settlements: every greenhouse is planted by CropFor
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Every block in every intact settlement's box, foundation row up.</summary>
    private static List<ushort> Snapshot(SvGameServer server)
    {
        var cells = new List<ushort>();
        foreach (var box in server.SettlementBoxesForTest.Where(b => !b.Ruined))
        {
            for (int x = box.Min.X; x <= box.Max.X; x++)
                for (int z = box.Min.Z; z <= box.Max.Z; z++)
                    for (int y = box.GroundY; y <= box.Max.Y; y++)
                    {
                        cells.Add(server.World.GetBlock(new Vector3i(x, y, z)).Value);
                    }
        }

        return cells;
    }

    /// <summary>Rewrites the save between two runs: drops the "structure is stamped" marks (#1990) so the next load
    /// stamps the settlements once more — the path a save from before #1990 takes — and lets the test edit the records.</summary>
    private void EditSave(string world, Action<StructurePlacementRecord> edit)
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        repo.Initialize();
        var meta = repo.LoadMetadata()!;
        meta.StampedFeatures.RemoveAll(f => f.Contains("|structblocks:", StringComparison.Ordinal));
        foreach (var r in meta.Placements.Where(r => r.Kind == "settlement"))
        {
            edit(r);
        }

        repo.SaveMetadata(meta);
        repo.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }

    [Fact]
    public void AStampedSettlement_KeepsItsCrops_EvenWhenItIsStampedAgain()
    {
        var crops = CropSet().ToHashSet();
        for (long seed = 1; seed <= 12; seed++)
        {
            string world = $"crops_{seed}";
            List<ushort> first;
            var server = Started(world, seed, out var repo);
            using (repo)
            {
                first = Snapshot(server);
                var recs = server.PlacementRecordsForTest.Where(r => r.Kind == "settlement" && r.Placed).ToList();
                server.Stop();
                if (!first.Any(crops.Contains))
                {
                    continue; // no inhabited settlement with a greenhouse on this world — next seed
                }

                // A settlement composed now records that its greenhouses were planted with the mixed pick.
                Assert.All(recs, r => Assert.Equal(StructurePlacementRecord.MixedHashRevision, r.HashRevision));
            }

            // Stamped once more (a save without the #1990 mark): the record's pick comes back, so do the very same crops.
            EditSave(world, _ => { });
            var again = Started(world, seed, out var repo2);
            using (repo2)
            {
                Assert.Equal(first, Snapshot(again));
                again.Stop();
            }

            // A record from before #2689 stamped once more grows the historical pick — the crops its blocks already
            // carry in a real save. Here the world was planted with the mixed pick, so the restamp shows the switch:
            // crops may swap for crops, nothing else moves.
            EditSave(world, r => r.HashRevision = 0);
            var old = Started(world, seed, out var repo3);
            bool swapped = false;
            using (repo3)
            {
                var restamped = Snapshot(old);
                Assert.Equal(first.Count, restamped.Count);
                for (int i = 0; i < first.Count; i++)
                {
                    if (first[i] == restamped[i]) continue;
                    Assert.True(crops.Contains(first[i]) && crops.Contains(restamped[i]), "only crops may differ between the two picks");
                    swapped = true;
                }

                old.Stop();
            }

            if (swapped)
            {
                return;
            }

            // Every greenhouse on this world happened to draw the same crop under both picks — try the next world.
        }

        Assert.Fail("no seed in 1..12 put a greenhouse whose crop differs between the two picks on meadowlands");
    }
}
