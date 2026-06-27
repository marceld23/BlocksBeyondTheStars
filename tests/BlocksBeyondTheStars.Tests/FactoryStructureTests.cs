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
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Factories: rare industrial halls that house animated machines and a production terminal. Each offers only
/// a seeded ROSTER (a subset of the factory recipes — never all). Factories are protected until claimed, and
/// crafting at a terminal is gated to that factory's roster.
/// </summary>
public sealed class FactoryStructureTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public FactoryStructureTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_fac_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    [Fact]
    public void Generator_IsDeterministic_HasTerminalAndMachines()
    {
        var a = FactoryGenerator.Generate(123, 3, _content);
        var b = FactoryGenerator.Generate(123, 3, _content);

        Assert.Equal(a.Width, b.Width);
        Assert.Equal(a.Length, b.Length);
        for (int x = 0; x < a.Width; x++)
            for (int y = 0; y < a.Height; y++)
                for (int z = 0; z < a.Length; z++)
                {
                    Assert.Equal(a.Get(x, y, z), b.Get(x, y, z));
                }

        Assert.Contains(a.Markers, m => m.Type == "factory_terminal");
        Assert.Equal(3, a.Markers.Count(m => m.Type.StartsWith("machine:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Generator_MachineCount_TracksRosterSize()
    {
        int one = FactoryGenerator.Generate(5, 1, _content).Markers.Count(m => m.Type.StartsWith("machine:", StringComparison.Ordinal));
        int four = FactoryGenerator.Generate(5, 4, _content).Markers.Count(m => m.Type.StartsWith("machine:", StringComparison.Ordinal));
        Assert.Equal(1, one);
        Assert.Equal(4, four);
    }

    private SvGameServer Start(long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "f" + seed));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = "f" + seed,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceRuins = false,
            PlaceChests = false,
            PlaceWrecks = false,
            PlaceVaults = false,
            PlaceFactories = true,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    /// <summary>Finds the first seed that stamps a factory and returns the server + that factory's details.</summary>
    private SvGameServer FindFactory(out SqliteWorldRepository repo,
        out (int Id, Vector3f Terminal, Vector3i Min, Vector3i Max, IReadOnlyList<string> Roster, int MachineCount, bool Claimable, string OwnerId) fac)
    {
        for (long seed = 1; seed <= 80; seed++)
        {
            var server = Start(seed, out repo);
            if (server.FactoryCount > 0)
            {
                fac = server.FactoriesForTest[0];
                return server;
            }

            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("No factory stamped across 80 seeds.");
    }

    [Fact]
    public void Factory_HasASeededRoster_NotEveryRecipe()
    {
        int allFactoryRecipes = _content.Recipes.Values.Count(r => r.Station == CraftingStation.Factory);
        var server = FindFactory(out var repo, out var fac);
        using (repo)
        {
            Assert.True(fac.Roster.Count >= 1, "A factory must offer at least one recipe.");
            Assert.True(fac.Roster.Count < allFactoryRecipes, "A factory must NOT offer every factory recipe.");
            Assert.True(fac.MachineCount >= 1, "A factory must contain at least one machine.");
            Assert.All(fac.Roster, k => Assert.Equal(CraftingStation.Factory, _content.GetRecipe(k)!.Station));
        }
    }

    [Fact]
    public void Factory_Terminal_CraftsRoster_ButRejectsOffRoster()
    {
        var server = FindFactory(out var repo, out var fac);
        using (repo)
        {
            var p = server.AddLocalPlayer("Operator");
            p.State.AboardShip = false;
            p.State.Position = fac.Terminal;

            // A roster recipe: stock its inputs generously, then craft — its output must appear.
            string rosterKey = fac.Roster[0];
            var onRoster = _content.GetRecipe(rosterKey)!;
            foreach (var inp in onRoster.Inputs)
            {
                p.State.Inventory.Add(inp.Item, inp.Count * 4, 999);
            }

            string outItem = onRoster.Outputs[0].Item;
            int before = p.State.Inventory.CountOf(outItem);
            server.Craft("Operator", rosterKey, 1);
            Assert.True(p.State.Inventory.CountOf(outItem) > before, "A roster recipe should craft at the terminal.");

            // An off-roster factory recipe: even with its inputs, the factory must refuse it.
            string? offRoster = _content.Recipes.Values
                .First(r => r.Station == CraftingStation.Factory && !fac.Roster.Contains(r.Key)).Key;
            var off = _content.GetRecipe(offRoster!)!;
            foreach (var inp in off.Inputs)
            {
                p.State.Inventory.Add(inp.Item, inp.Count * 4, 999);
            }

            string offOut = off.Outputs[0].Item;
            int offBefore = p.State.Inventory.CountOf(offOut);
            server.Craft("Operator", offRoster!, 1);
            Assert.Equal(offBefore, p.State.Inventory.CountOf(offOut)); // not on this factory's roster ⇒ refused
        }
    }

    [Fact]
    public void Factory_IsProtected_FromMining()
    {
        var server = FindFactory(out var repo, out var fac);
        using (repo)
        {
            var p = server.AddLocalPlayer("Raider");
            p.State.AboardShip = false;
            p.State.Position = fac.Terminal;

            var cell = new Vector3i((int)System.Math.Floor(fac.Terminal.X), (int)System.Math.Floor(fac.Terminal.Y), (int)System.Math.Floor(fac.Terminal.Z));
            Assert.True(server.IsFactoryBlock(cell)); // the terminal cell is inside the factory footprint
            ushort before = server.World.GetBlock(cell).Value;

            server.MineBlock("Raider", cell.X, cell.Y, cell.Z);

            Assert.Equal(before, server.World.GetBlock(cell).Value); // protected — the block survives
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
