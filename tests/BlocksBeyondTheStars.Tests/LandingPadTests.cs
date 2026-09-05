// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
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

    private SvGameServer NewOceanServer(string tag, int seed)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, tag));
        var st = new LoopbackServerTransport(new LoopbackLink());
        var config = new ServerConfig { WorldName = tag, Seed = seed, StartPlanet = "ocean", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();
        server.AddLocalPlayer("Pilot"); // loads the ocean body + builds its pads
        return server;
    }

    [Fact]
    public void OceanWorld_RaisesAnIsletUnderDeepPads_AndOnlyShallowOnesStayOnTheSeabed()
    {
        // #1453/#1454/#1619/#1620: an ocean-class world floods 78–97 % of its columns. A pad whose
        // footprint is still all water after the 2-D nudge carries an islet (plateau three blocks above the
        // sea, air above it) unless the water there is shallow (≤ 8 blocks) — only then does the ship park
        // in a seabed shaft, flagged Wet with its depth for the chooser. Deep shafts are gone.
        // The 2-D nudge (#1618) finds real land for most pads, so the test walks the wettest probe seeds
        // (9 = 97 % water) until a world still needs an islet.
        var server = NewOceanServer("islet9", 9);
        int seaLevel = server.SeaLevelForTest();
        int islets = 0, wet = 0, dry = 0;
        for (int i = 0; i < server.LandingPadCenters.Count; i++)
        {
            var pad = server.LandingPadInfoForTest(i);
            if (pad.Islet)
            {
                islets++;
                Assert.False(pad.Wet, "an islet pad is dry by definition");
                Assert.Equal(0, pad.Depth);
                Assert.Equal(seaLevel + 3, pad.Y);
                // The mound exists in the generated world: a solid block at the levelled height, air above.
                Assert.False(server.World.GetBlock(new Vector3i(pad.X, pad.Y, pad.Z)).IsAir);
                Assert.True(server.World.GetBlock(new Vector3i(pad.X, pad.Y + 1, pad.Z)).IsAir);
                Assert.True(server.World.GetBlock(new Vector3i(pad.X, pad.Y + 2, pad.Z)).IsAir);
                // The plateau reaches beyond the reserved pad (#1620): level ground 9 blocks out (the rim
                // wobbles ±3 around radius 12, so 9 is always plateau).
                Assert.False(server.World.GetBlock(new Vector3i(pad.X + 9, pad.Y, pad.Z)).IsAir);
                Assert.True(server.World.GetBlock(new Vector3i(pad.X + 9, pad.Y + 2, pad.Z)).IsAir);
            }
            else if (pad.Wet)
            {
                wet++;
                Assert.True(pad.Y < seaLevel, "a wet pad sits on the seabed");
                Assert.InRange(seaLevel - pad.Y, 1, 8); // shallow only (#1619)
                Assert.InRange(pad.Depth, 1, 8);
            }
            else
            {
                dry++;
                Assert.Equal(0, pad.Depth);
            }
        }

        Assert.True(islets + wet + dry == server.LandingPadCenters.Count);
        Assert.True(islets > 0, "an ocean world is expected to raise at least one islet (seed 7 has deep all-water pads)");
    }

    [Fact]
    public void PadNudge_FindsLandNorthOrSouth_NotOnlyAlongTheLatitude()
    {
        // #1618: on ocean seed 1 the planned column of pad 4 (x 3920, z −685) is all water along its whole
        // latitude band (the old X-only march gave up and rolled an islet), but dry ground lies a few blocks
        // north/south. The ring search must find it.
        var server = NewOceanServer("nudge", 1);
        var (x, z, dry) = server.NudgePadForTest(3920, -685, 300);
        Assert.True(dry, $"the 2-D nudge should end on dry ground (got {x},{z})");
        Assert.True(z != -685 || x != 3920, "the pad moved off its all-water column");
    }

    [Fact]
    public void PlayerPadPreference_DryBeforeIsletBeforeSeabed_TiesByIndex()
    {
        // #1621: pads 0 = seabed, 1 = islet, 2 = dry, 3 = dry.
        var pads = new List<(bool Wet, bool Islet)> { (true, false), (false, true), (false, false), (false, false) };
        Assert.Equal(2, SvGameServer.PreferredPadIndexForTest(pads, Array.Empty<int>()));      // first dry pad
        Assert.Equal(3, SvGameServer.PreferredPadIndexForTest(pads, new[] { 2 }));             // next dry pad
        Assert.Equal(1, SvGameServer.PreferredPadIndexForTest(pads, new[] { 2, 3 }));          // islet before seabed
        Assert.Equal(0, SvGameServer.PreferredPadIndexForTest(pads, new[] { 1, 2, 3 }));       // seabed last
        Assert.Equal(-1, SvGameServer.PreferredPadIndexForTest(pads, new[] { 0, 1, 2, 3 }));   // full
    }

    [Fact]
    public void NewPlayer_SpawnsOnAPadNoWorseThanTheBestFreeOne()
    {
        // #1621: a new player's first pad is never worse (seabed < islet < dry) than the best free pad.
        var server = NewOceanServer("spawn", 5);
        int bestRank = int.MaxValue, spawnRank = int.MaxValue;
        var me = server.AddLocalPlayer("Newbie");
        var pos = me.State.Position;
        for (int i = 0; i < server.LandingPadCenters.Count; i++)
        {
            var pad = server.LandingPadInfoForTest(i);
            int rank = pad.Wet ? 2 : pad.Islet ? 1 : 0;
            bestRank = Math.Min(bestRank, rank);
            if (Math.Abs(pos.X - (pad.X + 0.5f)) < 0.01f && Math.Abs(pos.Z - (pad.Z + 0.5f)) < 0.01f)
            {
                spawnRank = Math.Min(spawnRank, rank);
            }
        }

        Assert.NotEqual(int.MaxValue, spawnRank); // the spawn IS a pad centre
        Assert.Equal(bestRank, spawnRank);
    }

    [Fact]
    public void Body_HasPadsInItsSizeClassRange()
    {
        var server = NewServer("range");
        server.AddLocalPlayer("Pilot"); // loads the home body + builds its pads

        // Counts are DOUBLED (×2): a body always has at least two pads and never more than the largest class
        // allows (planet: up to 8 base → 16 doubled).
        Assert.InRange(server.LandingPadCount, 2, 16);

        // Pads spread across BOTH longitude and latitude — they no longer all sit on one equator line.
        var centers = server.LandingPadCenters;
        Assert.True(centers.Select(p => p.Z).Distinct().Count() >= 2, "pads spread across latitudes, not a single horizontal line");
        Assert.True(centers.Select(p => p.X).Distinct().Count() >= 2, "pads spread across longitudes");
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
        // world — same count AND same positions (both derive from the same ComputeLandingPads source of truth),
        // so a pad on the chooser map is exactly where the ship touches down.
        Assert.Equal(worldPads, server.ApproachMapPadCountForTest());
        Assert.Equal(worldPads, server.LandingPadCenters.Count);
        Assert.Equal(server.LandingPadCenters, server.ApproachMapPadsForTest());
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
