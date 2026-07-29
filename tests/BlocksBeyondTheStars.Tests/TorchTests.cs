// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Torches: a cheap early light source crafted by hand, placed against a wall — and an OPEN FLAME, so it only
/// works where there is air. On an airless body (atmosphere "none") placing one is refused with a reason rather
/// than accepted as a dud that gives no light.
/// </summary>
public sealed class TorchTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public TorchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_torch_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(out SqliteWorldRepository repo, string planetType)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "torch_" + planetType));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "torch_" + planetType,
            Seed = 9,
            StartPlanet = planetType,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>A planet type with air, and one without — read from the content rather than hard-coded, so the
    /// test keeps meaning if the planet table is re-authored.</summary>
    private string PlanetWithAir()
        => _content.Planets.Values.First(p => !p.IsAirless && !p.SpaceSky).Key;

    private string AirlessPlanet()
        => _content.Planets.Values.First(p => p.IsAirless).Key;

    [Fact]
    public void TorchIsCraftedByHand_FromWoodAndFibre()
    {
        var recipe = _content.GetRecipe("torch");
        Assert.NotNull(recipe);

        // Hand station: reachable from the very first minutes, no workshop needed.
        Assert.Equal(Shared.Definitions.CraftingStation.Hand, recipe!.Station);
        Assert.True(string.IsNullOrEmpty(recipe.RequiredBlueprint)); // nothing to research first
        Assert.Contains(recipe.Inputs, i => i.Item == "wood_log");
        Assert.Contains(recipe.Inputs, i => i.Item == "plant_fiber");
        Assert.Equal("torch", recipe.Outputs.Single().Item);
        Assert.True(recipe.Outputs.Single().Count > 1); // a batch, so lighting a base isn't a grind
    }

    [Fact]
    public void TorchEmitsLight_AndIsPickedBackUp()
    {
        var block = _content.GetBlock("torch");
        Assert.NotNull(block);
        Assert.True(block!.Emission > 0.5f);                             // it is a light source
        Assert.Equal("torch", block.Drops.Single().Item);                // mining returns the torch itself
    }

    [Fact]
    public void OnAWorldWithAir_ATorchCanBePlaced()
    {
        var server = Started(out var repo, PlanetWithAir());
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(0, 200, 0);
            p.State.Inventory.Add("torch", 4, _content.MaxStackOf("torch"));

            server.Craft("Justus", "torch", 0); // no-op; placement is the subject here
            server.PlaceBlock("Justus", 1, 200, 0, "torch");

            Assert.False(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir); // it is there, lighting the room
            Assert.Equal(3, p.State.Inventory.CountOf("torch"));                 // one was spent
        }
    }

    [Fact]
    public void OnAnAirlessWorld_ATorchIsRefused_AndNotConsumed()
    {
        var server = Started(out var repo, AirlessPlanet());
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(0, 200, 0);
            p.State.Inventory.Add("torch", 4, _content.MaxStackOf("torch"));

            server.PlaceBlock("Justus", 1, 200, 0, "torch");

            // Nothing placed, and the torch stays in the pack — a flame has nothing to burn out here.
            Assert.True(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);
            Assert.Equal(4, p.State.Inventory.CountOf("torch"));
        }
    }

    /// <summary>An ordinary block must still be placeable on an airless world — the gate is the torch's alone.</summary>
    [Fact]
    public void TheAirGateAppliesOnlyToTheTorch()
    {
        var server = Started(out var repo, AirlessPlanet());
        using (repo)
        {
            var p = server.AddLocalPlayer("Justus");
            p.State.Position = new Vector3f(0, 200, 0);
            p.State.Inventory.Add("stone", 4, _content.MaxStackOf("stone"));

            server.PlaceBlock("Justus", 1, 200, 0, "stone");

            Assert.False(server.World.GetBlock(new Vector3i(1, 200, 0)).IsAir);
        }
    }

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
            // A locked save file must never fail the test run.
        }
    }
}
