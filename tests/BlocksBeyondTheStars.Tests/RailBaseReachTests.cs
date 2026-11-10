// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3457 — Lyxette's spaceport (~80×80) has its station "top left", beside the village the base was built around: 48 blocks
/// from the foundation stone did not reach it, and the village took the stop first. Now a stop belongs to a base whose core
/// is within 96 blocks or one of whose chained power relays is within 24; a stop beside a village counts for the village
/// and the base; no trip pairs a stop with itself; the relay walk loads no terrain; and the owner waiting at the station
/// counts as home for a train guest.
/// </summary>
public sealed class RailBaseReachTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const int Cx = NpcLifeWorld.Cx, Cz = NpcLifeWorld.Cz;

    /// <summary>The relays of the chain run along this row: the first one sits inside the core's zone cube.</summary>
    private const int RelayZ = Cz + 4;

    private static string Base(RailTravelArena a) => a.Server.BaseSnapshots.Single(b => b.Id == a.W.BaseId).Name;

    /// <summary>A chain of power relays east of the core, every eight blocks up to <paramref name="lastX"/>.</summary>
    private static void RelayChain(RailTravelArena a, int lastX)
    {
        for (int x = Cx + 8; x <= lastX; x += 8)
        {
            a.W.Set(x, a.W.Feet, RelayZ, "power_relay");
        }
    }

    [Fact]
    public void AStop96BlocksFromTheCore_IsTheBases_AndOneBesideAVillage_CountsForBoth()
    {
        using var a = new RailTravelArena(Content.Value, "rail_reach_96", foundBase: true);
        string name = Base(a);
        Assert.Equal(new[] { (name, "base") }, a.Server.RailPlacesForTest(new Vector3i(Cx + 90, a.W.Feet, Cz)));
        Assert.Empty(a.Server.RailPlacesForTest(new Vector3i(Cx + 100, a.W.Feet, Cz)));
        Assert.Empty(a.Server.RailPlacesForTest(new Vector3i(Cx + 60, a.W.Feet + 30, Cz))); // far above: not the base's

        // The village beside the station keeps it — and the base gets it too.
        a.Town("Ysel Glen", RailTravelArena.StopWestX);
        Assert.Equal(new[] { ("Ysel Glen", "settlement"), (name, "base") }, a.Server.RailPlacesForTest(a.StopWest));
        Assert.Equal(("Ysel Glen", "settlement"), a.Server.RailPlaceForTest(a.StopWest));
    }

    [Fact]
    public void ThePowerRelayChain_WidensTheReach_AndARemovedRelayShrinksItAgain()
    {
        using var a = new RailTravelArena(Content.Value, "rail_reach_relays", foundBase: true);
        string name = Base(a);
        var far = new Vector3i(Cx + 156, a.W.Feet, RelayZ);
        Assert.Empty(a.Server.RailPlacesForTest(far));

        RelayChain(a, Cx + 136);
        Assert.Equal(new[] { (name, "base") }, a.Server.RailPlacesForTest(far)); // 20 from the last relay
        Assert.Empty(a.Server.RailPlacesForTest(new Vector3i(Cx + 166, a.W.Feet, RelayZ))); // 30 from it

        // The chain is cached: asking again walks nothing; a relay taken out of the middle cuts the chain there.
        var (relays, walks) = a.Server.RailRelaysForTest(a.W.BaseId);
        Assert.Equal(17, relays.Count);
        Assert.Equal(walks, a.Server.RailRelaysForTest(a.W.BaseId).Walks);
        a.Server.World.SetBlock(new Vector3i(Cx + 40, a.W.Feet, RelayZ), BlockId.Air);
        Assert.Empty(a.Server.RailPlacesForTest(far));
        Assert.Equal(4, a.Server.RailRelaysForTest(a.W.BaseId).Relays.Count);
        Assert.True(a.Server.RailRelaysForTest(a.W.BaseId).Walks > walks);
    }

    [Fact]
    public void AskingForAStopsPlaces_LoadsNoTerrain()
    {
        using var a = new RailTravelArena(Content.Value, "rail_reach_noload", foundBase: true);
        RelayChain(a, Cx + 40);
        a.Server.RailPlacesForTest(a.StopEast); // the chain walked once, near the core (resident)
        long loads = a.Server.World.ChunkLoads;
        Assert.Empty(a.Server.RailPlacesForTest(new Vector3i(Cx + 700, a.W.Feet, Cz + 500)));
        Assert.Empty(a.Server.RailPlacesForTest(new Vector3i(Cx - 900, a.W.Feet, Cz - 300)));
        Assert.Equal(loads, a.Server.World.ChunkLoads);
    }

    [Fact]
    public void NoTripPairsAStopWithItself_AndTheBaseRidesFromAVillagesStop()
    {
        using var a = new RailTravelArena(Content.Value, "rail_reach_pairs", foundBase: true);
        a.Server.PauseRailTripPlannerForTest();
        a.Town("Westdorf", RailTravelArena.StopWestX);
        a.Town("Ostdorf", RailTravelArena.StopEastX);
        string name = Base(a);

        var pairs = a.Server.RailTripPairsForTest();
        Assert.DoesNotContain(pairs, p => p.SameStop);
        Assert.Contains(pairs, p => p.Home == "Westdorf" && p.Away == "Ostdorf");
        Assert.Contains(pairs, p => p.Home == "Ostdorf" && p.Away == "Westdorf");
        Assert.Contains(pairs, p => p.Home == name && p.HomeKind == "base" && p.Away == "Ostdorf");
        Assert.Contains(pairs, p => p.Home == name && p.HomeKind == "base" && p.Away == "Westdorf");
        Assert.DoesNotContain(pairs, p => p.Away == name); // a trip's other end is always a town
    }

    [Fact]
    public void TheOwnerCountsAsHome_NearTheCore_NearARelay_OrWaitingAtTheBasesStation()
    {
        using var a = new RailTravelArena(Content.Value, "rail_reach_owner", foundBase: true);
        var p = a.W.Player.State;
        p.Position = new Vector3f(Cx + 110.5f, a.W.Feet, Cz + 0.5f);
        Assert.True(a.Server.OwnerNearBaseForTrainGuestForTest(a.W.BaseId)); // within 96 + 24 of the core
        p.Position = new Vector3f(Cx + 150.5f, a.W.Feet, RelayZ + 0.5f);
        Assert.False(a.Server.OwnerNearBaseForTrainGuestForTest(a.W.BaseId));

        RelayChain(a, Cx + 176);
        Assert.True(a.Server.OwnerNearBaseForTrainGuestForTest(a.W.BaseId)); // 14 from a relay

        // A far town's station 22 blocks from the last relay: the town's and the base's. Waiting on its platform, 25 blocks
        // past the last relay and 200 from the core, the owner is home all the same.
        a.FarTown("Sabik Outpost", Cx + 196);
        var stop = new Vector3i(Cx + 196, a.W.Feet, RailTravelArena.LineZ + 3);
        Assert.Equal(new[] { ("Sabik Outpost", "settlement"), (Base(a), "base") }, a.Server.RailPlacesForTest(stop));
        p.Position = new Vector3f(Cx + 201.5f, a.W.Feet, RailTravelArena.LineZ + 3.5f);
        Assert.True(a.Server.OwnerNearBaseForTrainGuestForTest(a.W.BaseId));
        p.Position = new Vector3f(Cx + 230.5f, a.W.Feet, RailTravelArena.LineZ + 3.5f);
        Assert.False(a.Server.OwnerNearBaseForTrainGuestForTest(a.W.BaseId));
    }
}
