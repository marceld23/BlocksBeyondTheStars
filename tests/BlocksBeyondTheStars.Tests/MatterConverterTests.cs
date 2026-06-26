// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The transmuter (matter forge): a gated processing station that compacts spare terrain into matter
/// dust and synthesises it back into ore. It reuses the crafting + station-gating system. These tests
/// pin the balance invariants that keep it from breaking the ore economy: a lossy matter_dust middle
/// step, energy-cell costs that stop a from-nothing bootstrap, and NO Tier-3 output.
/// </summary>
public sealed class MatterConverterTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public MatterConverterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_matter_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private IEnumerable<RecipeDefinition> TransmuterRecipes()
        => _content.Recipes.Values.Where(r => r.Station == CraftingStation.Transmuter);

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "matter"));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "matter",
            Seed = 5,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void TransmuterRecipes_ParseOntoTheNewStation()
    {
        // The JSON "transmuter" station string must map onto the CraftingStation.Transmuter enum value.
        Assert.NotEmpty(TransmuterRecipes());
        Assert.Contains(TransmuterRecipes(), r => r.Key == "compact_sand");
        Assert.Contains(TransmuterRecipes(), r => r.Key == "synth_iron_ore");
        Assert.Contains(TransmuterRecipes(), r => r.Key == "resynth_titanium_ore");
    }

    [Fact]
    public void Compaction_TurnsTrashIntoMatterDust()
    {
        var r = _content.Recipes["compact_sand"];
        Assert.Equal("sand", r.Inputs[0].Item);
        Assert.True(r.Inputs[0].Count >= 32, "trash→dust must stay a steep, lossy ratio");
        Assert.Equal("matter_dust", r.Outputs[0].Item);
        Assert.Equal(1, r.Outputs[0].Count);
    }

    /// <summary>I1: the converter must never mint Tier-3 endgame ores — those stay mining-exclusive.</summary>
    [Fact]
    public void Transmuter_NeverOutputs_Tier3Materials()
    {
        var tier3 = new HashSet<string>
        {
            "diamond_ore", "diamond", "uranium", "uranium_ore", "tungsten_ore", "tungsten_ingot",
            "platinum_ore", "platinum_ingot", "neodymium", "neodymium_ore",
        };
        var bad = TransmuterRecipes()
            .SelectMany(r => r.Outputs.Select(o => (r.Key, o.Item)))
            .Where(x => tier3.Contains(x.Item))
            .Select(x => $"{x.Key}->{x.Item}")
            .ToList();

        Assert.True(bad.Count == 0, "transmuter outputs a Tier-3 material (breaks the mining economy): " + string.Join(", ", bad));
    }

    /// <summary>I2: no from-nothing bootstrap — synthesis always burns an energy cell, resynthesis a power cell,
    /// both of which need already-mined ore to make. Compaction is the only free (infinite-trash) step.</summary>
    [Fact]
    public void Synthesis_AlwaysCostsEnergy()
    {
        foreach (var r in TransmuterRecipes().Where(r => r.Key.StartsWith("synth_")))
        {
            Assert.Contains(r.Inputs, i => i.Item == "energy_cell_1");
            Assert.Contains(r.Inputs, i => i.Item == "matter_dust");
        }

        foreach (var r in TransmuterRecipes().Where(r => r.Key.StartsWith("resynth_")))
        {
            Assert.Contains(r.Inputs, i => i.Item == "power_cell");
            Assert.Contains(r.Inputs, i => i.Item == "matter_dust");
        }
    }

    [Fact]
    public void Resynthesis_IsGatedBehindItsOwnBlueprint()
    {
        foreach (var r in TransmuterRecipes().Where(r => r.Key.StartsWith("resynth_")))
        {
            Assert.Equal("matter_resynth", r.RequiredBlueprint);
        }

        var bp = _content.Blueprints["matter_resynth"];
        Assert.Contains("matter_forge", bp.Prerequisites);
    }

    [Fact]
    public void MatterForge_IsPlaceable_AsAStationBlock()
    {
        var item = _content.GetItem("matter_forge");
        Assert.NotNull(item);
        Assert.Equal("matter_forge", item!.PlacesBlock);
        Assert.NotNull(_content.GetBlock("matter_forge"));
        Assert.Contains(_content.Recipes.Values, r => r.Outputs.Any(o => o.Item == "matter_forge"));
    }

    [Fact]
    public void Synthesis_MakesOre_WhenModuleInstalledAndBlueprintKnown()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Alchemist");
            p.Ships[p.ActiveShipId].Modules.Add("transmuter");
            p.State.UnlockedBlueprints.Add("matter_forge");
            p.State.AboardShip = true;
            p.State.Inventory.Add("matter_dust", 4, 99);
            p.State.Inventory.Add("energy_cell_1", 1, 50);

            server.Craft("Alchemist", "synth_iron_ore");

            Assert.Equal(0, p.State.Inventory.CountOf("matter_dust"));
            Assert.Equal(0, p.State.Inventory.CountOf("energy_cell_1"));
            Assert.Equal(2, p.State.Inventory.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void Synthesis_Fails_WithoutTheModule()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var p = server.AddLocalPlayer("Alchemist"); // starter ship has no transmuter
            p.State.UnlockedBlueprints.Add("matter_forge");
            p.State.AboardShip = true;
            p.State.Inventory.Add("matter_dust", 4, 99);
            p.State.Inventory.Add("energy_cell_1", 1, 50);

            server.Craft("Alchemist", "synth_iron_ore");

            Assert.Equal(4, p.State.Inventory.CountOf("matter_dust")); // nothing consumed
            Assert.Equal(0, p.State.Inventory.CountOf("iron_ore"));
        }
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
