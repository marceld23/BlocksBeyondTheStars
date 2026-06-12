using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Item 38 — fixed, pre-planned landing pads. Each body has a deterministic, seeded-random set of pads (varying
/// within its size-class range), reserved against building, with live occupancy: when every pad is held the body
/// is full and landing is refused. Replaces the old dynamic per-player landing zones.
/// </summary>
public sealed class LandingPadTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public LandingPadTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_pad_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer NewServer(string tag, int seed = 1, bool placeShip = false)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = seed, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = placeShip };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        return server;
    }

    [Fact]
    public void Pvp_Preset_EnablesSpaceCombat()
    {
        var rules = ServerPresets.Get("pvp")!;
        Assert.Equal(SpaceCombatMode.Both, rules.SpaceCombat);
        Assert.Equal(ShipWeaponMode.PvpAllowed, rules.ShipWeapons);

        var peaceful = ServerPresets.Get("peaceful-creative")!;
        Assert.Equal(SpaceCombatMode.Off, peaceful.SpaceCombat);
        Assert.Equal(AlienActivity.Off, peaceful.PlanetEnemies);
    }

    [Fact]
    public void Body_HasPadsInItsSizeClassRange()
    {
        var server = NewServer("range");
        server.AddLocalPlayer("Pilot"); // loads the home body + builds its pads

        // Counts are DOUBLED (×2): a body always has at least two pads and never more than the largest class
        // allows (planet: up to 8 base → 16 doubled).
        Assert.InRange(server.LandingPadCount, 2, 16);
        Assert.All(server.LandingPadCenters, p => Assert.Equal(0, p.Z)); // pads sit on the equator band
    }

    [Fact]
    public void PadCount_IsDoubled_AndMapMatchesTheWorld()
    {
        var server = NewServer("doubled");
        server.AddLocalPlayer("Pilot"); // loads the home body + builds its pads

        int worldPads = server.LandingPadCount;

        // Doubling produces an even count, and at least two pads (the smallest base count is 1 → 2).
        Assert.True(worldPads >= 2, "a doubled body has at least two pads");
        Assert.True(worldPads % 2 == 0, "a doubled pad count is always even");

        // Consistency: the approach landing map / pad chooser advertises EXACTLY the pads that exist in the
        // world — both derive from the same PadCountFor source of truth.
        Assert.Equal(worldPads, server.ApproachMapPadCountForTest());
        Assert.Equal(worldPads, server.LandingPadCenters.Count);
    }

    [Fact]
    public void Pads_AreDeterministic_FromTheSeed()
    {
        var a = NewServer("det_a", seed: 7);
        a.AddLocalPlayer("A");
        var b = NewServer("det_b", seed: 7);
        b.AddLocalPlayer("B");

        // Same seed → same galaxy → same home body → identical pad set (count + positions).
        Assert.Equal(a.LandingPadCenters, b.LandingPadCenters);
    }

    [Fact]
    public void APadCell_IsReservedAgainstBuilding_OffPadIsFree()
    {
        var server = NewServer("reserve");
        server.AddLocalPlayer("Builder");
        var pad0 = server.LandingPadCenters[0];

        Assert.True(server.IsOnLandingPadForTest(pad0.X, pad0.Z));                  // the pad cell is reserved…
        Assert.False(server.IsOnLandingPadForTest(pad0.X + 1000, pad0.Z + 1000));   // …a cell well off it is free
    }

    [Fact]
    public void WhenEveryPadIsTaken_TheBodyIsFull_AndLandingIsRefused()
    {
        var server = NewServer("full");
        server.AddLocalPlayer("Host"); // loads the body + its pads
        int total = server.LandingPadCount;

        // Fill every pad with a player standing on the body (live occupancy → that pad is taken).
        var holders = new List<BlocksBeyondTheStars.GameServer.PlayerSession>();
        for (int i = 0; i < total; i++)
        {
            var s = server.AddLocalPlayer("P" + i);
            s.AssignedPadIndex = i;
            holders.Add(s);
        }

        Assert.Equal(0, server.FreePadCountForTest()); // the body is full

        var latecomer = server.AddLocalPlayer("Late");
        var (chosen, reason) = server.TryClaimPadForTest(latecomer, -1); // auto-pick the first free pad
        Assert.True(chosen < 0);                       // none free → refused
        Assert.False(string.IsNullOrEmpty(reason));    // with a "full" reason for the player

        // A pad frees the moment its holder leaves the body (live occupancy): drop one holder off-body.
        holders[0].CurrentLocationId = "elsewhere";
        Assert.Equal(1, server.FreePadCountForTest());
        var (chosen2, _) = server.TryClaimPadForTest(latecomer, -1);
        Assert.Equal(0, chosen2); // the vacated pad 0 is now claimable
    }

    [Fact]
    public void ClaimingATakenPad_IsRefused_ButAFreeOneSucceeds()
    {
        var server = NewServer("claim");
        server.AddLocalPlayer("Host");
        Assert.True(server.LandingPadCount >= 2);

        var other = server.AddLocalPlayer("Other");
        other.AssignedPadIndex = 1; // Other is holding pad 1

        var me = server.AddLocalPlayer("Me");
        var (taken, reason) = server.TryClaimPadForTest(me, 1); // ask for the taken pad
        Assert.True(taken < 0);
        Assert.False(string.IsNullOrEmpty(reason));

        var (ok, _) = server.TryClaimPadForTest(me, 0); // a free pad is fine
        Assert.Equal(0, ok);
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (System.IO.Directory.Exists(_root)) System.IO.Directory.Delete(_root, recursive: true);
        }
        catch { }
    }
}
