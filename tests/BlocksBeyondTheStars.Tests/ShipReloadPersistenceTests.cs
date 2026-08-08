// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #848 — "my ship is gone after I leave the world and load it again". Two save-game gaps caused it: the
/// landing pad a player holds was session-only (so a reload re-parked the ship on the first free pad, i.e. on
/// the far side of the planet from where the player was restored), and only the ACTIVE ship was persisted (so
/// crafted ships and claimed wrecks were deleted by the next load). Both are covered here across a real
/// server restart on the same save.
/// </summary>
public sealed class ShipReloadPersistenceTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipReloadPersistenceTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_reload_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    /// <summary>Opens (or re-opens) the save under <paramref name="tag"/> and starts a server on it — calling
    /// this twice with the same tag is a "quit the game and load the world again".</summary>
    private SvGameServer Start(string tag, out SqliteWorldRepository repo, bool placeShip = false)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var transport = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig
        {
            WorldName = tag,
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = placeShip,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void CraftedShip_AndActiveChoice_SurviveAReload()
    {
        string craftedId;
        var first = Start("fleet", out var repo1);
        using (repo1)
        {
            var pilot = first.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true; // skip the material cost; this test is about persistence
            pilot.State.UnlockedBlueprints.Add("ship_hauler");

            var (ok, id) = first.CraftShip("Pilot", "hauler");
            Assert.True(ok);
            craftedId = id;
            Assert.True(first.SwitchShip(craftedId));
            first.Stop(); // saves like a normal shutdown
        }

        var reloaded = Start("fleet", out var repo2);
        using (repo2)
        {
            reloaded.AddLocalPlayer("Pilot");

            Assert.Equal(2, reloaded.OwnedShips.Count);       // the starter AND the ship that was paid for
            Assert.Equal(craftedId, reloaded.ActiveShipId);   // still flying the one they switched to
            Assert.Equal("hauler", reloaded.Ship.ShipType);
            Assert.Contains(reloaded.OwnedShips.Values, s => s.ShipType == "starter");
        }
    }

    [Fact]
    public void ShipCargo_SurvivesAReload_PerShip()
    {
        var first = Start("cargo", out var repo1);
        using (repo1)
        {
            var pilot = first.AddLocalPlayer("Pilot");
            pilot.State.InstantBuild = true;
            pilot.State.UnlockedBlueprints.Add("ship_hauler");

            var (ok, id) = first.CraftShip("Pilot", "hauler");
            Assert.True(ok);
            pilot.Ships[id].Cargo.Add("iron_ore", 7, 99); // load the NON-active ship's hold
            first.Stop();
        }

        var reloaded = Start("cargo", out var repo2);
        using (repo2)
        {
            reloaded.AddLocalPlayer("Pilot");
            var hauler = reloaded.OwnedShips.Values.Single(s => s.ShipType == "hauler");
            Assert.Equal(7, hauler.Cargo.CountOf("iron_ore"));
        }
    }

    [Fact]
    public void LegacySave_WithoutAFleetIndex_KeepsItsShip()
    {
        // A save written before per-ship persistence: one player row, one ship row under the legacy key.
        using (var seed = new SqliteWorldRepository(new SaveGamePaths(_root, "legacy")))
        {
            seed.Initialize();
            seed.SavePlayer(new PlayerState { PlayerId = "Pilot", Name = "Pilot" });
            var ship = new ShipState { ShipType = "hauler", Hull = 42f };
            ship.Cargo.Add("titanium_plate", 3, 99);
            seed.SaveShip("ship_Pilot", ship);
        }

        var server = Start("legacy", out var repo);
        using (repo)
        {
            server.AddLocalPlayer("Pilot");

            Assert.Single(server.OwnedShips);
            Assert.Equal("hauler", server.Ship.ShipType);       // migrated, not replaced by a fresh starter
            Assert.Equal(3, server.Ship.Cargo.CountOf("titanium_plate"));
        }
    }

    [Fact]
    public void LandingPad_SurvivesAReload_SoTheShipIsWhereItWasLeft()
    {
        var first = Start("pad", out var repo1, placeShip: true);
        int pad;
        using (repo1)
        {
            var pilot = first.AddLocalPlayer("Pilot");
            Assert.True(first.LandingPadCenters.Count > 2, "the home body should offer several pads");
            pad = 2;
            pilot.AssignedPadIndex = pad; // as landing on pad 2 would
            first.Stop();
        }

        var reloaded = Start("pad", out var repo2, placeShip: true);
        using (repo2)
        {
            var pilot = reloaded.AddLocalPlayer("Pilot");

            Assert.Equal(pad, pilot.AssignedPadIndex);
            var centre = reloaded.LandingPadCenters.Single(p => p.Index == pad);
            var anchor = reloaded.ShipAnchorOf("Pilot");
            Assert.Equal(centre.X, anchor.X); // the ship is parked on THAT pad, not back on pad 0
            Assert.Equal(centre.Z, anchor.Z);
        }
    }

    [Fact]
    public void RestoredPad_IsReleased_WhenAnotherPlayerAlreadyHoldsIt()
    {
        var first = Start("shared", out var repo1, placeShip: true);
        using (repo1)
        {
            var a = first.AddLocalPlayer("Ann");
            var b = first.AddLocalPlayer("Ben");
            a.AssignedPadIndex = 2;
            b.AssignedPadIndex = 2; // a save that somehow recorded the same pad for both
            first.Stop();
        }

        var reloaded = Start("shared", out var repo2, placeShip: true);
        using (repo2)
        {
            var a = reloaded.AddLocalPlayer("Ann");
            var b = reloaded.AddLocalPlayer("Ben");

            Assert.Equal(2, a.AssignedPadIndex);          // first in keeps the pad
            Assert.NotEqual(a.AssignedPadIndex, b.AssignedPadIndex); // the other is moved to a free one
            Assert.NotEqual(reloaded.ShipAnchorOf("Ann"), reloaded.ShipAnchorOf("Ben"));
        }
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root))
            {
                System.IO.Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }
}
