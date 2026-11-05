// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3189: an open crate screen is refreshed by changes on its own world only. A container change on another world looked
/// the crate up in THAT world's store, found nothing, sent the player an empty crate and stopped their updates.
/// </summary>
public sealed class CrateRefreshAcrossWorldsTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrateRefreshAcrossWorldsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crateworlds_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "crateworlds"));
        var config = new ServerConfig
        {
            WorldName = "crateworlds",
            Seed = 4242,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
            PlaceWrecks = false,
            PlaceLodges = false, // a hut's store is a crate too — keep the test's crates the only ones
        };
        config.Rules.FreeSpaceFlight = true;
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private string OtherBody(SvGameServer server, string home)
    {
        string system = server.Galaxy.FindBody(home)!.SystemId;
        return server.Galaxy.AllBodies().First(b =>
            b.Kind is CelestialKind.Planet or CelestialKind.Moon or CelestialKind.AsteroidField
            && b.SystemId == system
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.Id != home).Id;
    }

    private static StoredContainer Crate(string id, string world, Vector3i cell, string item, int count) => new()
    {
        Id = id,
        Planet = world,
        Kind = "crate",
        Position = cell,
        Items = new List<ItemStack> { new(item, count) },
    };

    [Fact]
    public void AContainerChangeOnAnotherWorld_LeavesAnOpenCrateAlone()
    {
        var transport = new RecordingTransport();
        var server = Started(transport, out var repo);
        using (repo)
        {
            var keeper = server.AddLocalPlayer("Keeper");
            server.AddLocalPlayer("Traveller");
            string home = keeper.CurrentLocationId;
            var feet = keeper.State.Position.ToBlock();
            server.AddContainerForTest(Crate("keeper_crate", home, feet, "iron_ore", 10));
            server.OpenContainerForTest(keeper, "keeper_crate");
            Assert.Equal("keeper_crate", keeper.OpenContainerId);

            server.SetInstantTravelForTest(true);
            string away = OtherBody(server, home);
            Assert.True(server.QuickTravelForTest("Traveller", away));
            Assert.Equal(away, server.ActiveLocationId);
            transport.Sent.Clear();

            server.AddContainerForTest(Crate("away_crate", away, new Vector3i(0, 200, 0), "stone", 3)); // a change over THERE

            Assert.Empty(transport.To<ContainerContents>(keeper)); // no empty crate pushed onto the keeper's open screen
            Assert.Equal("keeper_crate", keeper.OpenContainerId); // and the screen keeps getting updates

            // Back on the keeper's own world, a change still refreshes the open screen with the real contents.
            Assert.True(server.ActivateWorldForTest(home));
            server.AddContainerForTest(Crate("second_crate", home, new Vector3i(feet.X + 3, feet.Y, feet.Z), "stone", 1));
            var refreshed = transport.To<ContainerContents>(keeper).Last(c => c.ContainerId == "keeper_crate");
            Assert.Contains(refreshed.Items, i => i.Item == "iron_ore" && i.Count == 10);
        }
    }
}
