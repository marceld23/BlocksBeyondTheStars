// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3088 — THE interact pick (<see cref="InteractResolver"/>): one answer for what E does and what the HUD's prompt says.
/// Justus' reports (2026-11-01): his ship's mission board read "E: Workshop" (the 3 m station fallback), and in his base the
/// HUD promised "Talk to Vhaerk (E)" while E swung the door beside him. Aimed beats nearby; within each tier E's old order.
/// </summary>
public sealed class InteractResolverTests
{
    [Fact]
    public void NothingAround_IsNothing()
    {
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(new InteractFacts()));
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(null!));
    }

    [Fact]
    public void AnAimedMissionBoard_BeatsTheWorkshopThreeMetresOff()
    {
        var f = new InteractFacts { NearStation = "workshop" };
        Assert.Equal(InteractKind.NearStation, InteractResolver.Resolve(f));

        f.MissionBoard = true; // the board in the cabin, under the crosshair
        Assert.Equal(InteractKind.MissionBoard, InteractResolver.Resolve(f));
    }

    [Fact]
    public void ThePersonLookedAt_BeatsTheDoorBesideThem_AndTheDoorBeatsAMereNeighbour()
    {
        var f = new InteractFacts { NearDoor = true, TalkNear = true };
        Assert.Equal(InteractKind.NearDoor, InteractResolver.Resolve(f)); // the old order: a door before talking

        f.AimedNpc = true;
        Assert.Equal(InteractKind.AimedTalk, InteractResolver.Resolve(f));

        f.AimedNpcTrades = true;
        Assert.Equal(InteractKind.AimedTrade, InteractResolver.Resolve(f));
    }

    [Fact]
    public void ACrate_IsOpenedOnlyWhenAimedAt()
    {
        var f = new InteractFacts { TalkNear = true };
        Assert.Equal(InteractKind.Talk, InteractResolver.Resolve(f)); // a crate a few metres off is no E target

        f.Crate = true;
        Assert.Equal(InteractKind.Crate, InteractResolver.Resolve(f));
    }

    [Fact]
    public void TheAimedTier_KeepsTheOrderEAlwaysHad()
    {
        var f = new InteractFacts
        {
            OwnBeacon = true,
            OwnBase = true,
            BasePad = true,
            StationCore = true,
            Crate = true,
            CrystalDevice = true,
            BioLab = true,
            SpawnBed = true,
            Seat = true,
            MissionBoard = true,
            LookedStation = "cockpit",
            AimedDoor = true,
        };
        var order = new[]
        {
            InteractKind.OwnBeacon, InteractKind.OwnBase, InteractKind.BasePad, InteractKind.StationCore, InteractKind.Crate,
            InteractKind.CrystalDevice, InteractKind.BioLab, InteractKind.SpawnBed, InteractKind.Seat, InteractKind.MissionBoard,
            InteractKind.LookedStation, InteractKind.AimedDoor,
        };
        foreach (var expected in order)
        {
            Assert.Equal(expected, InteractResolver.Resolve(f));
            Assert.True(InteractResolver.IsAimed(expected));
            Clear(f, expected);
        }

        Assert.Equal(InteractKind.None, InteractResolver.Resolve(f));
    }

    [Fact]
    public void TheNearbyTier_KeepsTheOrderEAlwaysHad_AndEveryAimedThingBeatsIt()
    {
        var f = new InteractFacts
        {
            BeamPad = true,
            NearDoor = true,
            DataCube = true,
            Factory = true,
            NetFragment = true,
            NearStation = "medbay",
            Vendor = true,
            TalkNear = true,
        };
        var order = new[]
        {
            InteractKind.BeamPad, InteractKind.NearDoor, InteractKind.DataCube, InteractKind.Factory, InteractKind.NetFragment,
            InteractKind.NearStation, InteractKind.Trade, InteractKind.Talk,
        };
        foreach (var expected in order)
        {
            Assert.Equal(expected, InteractResolver.Resolve(f));
            Assert.False(InteractResolver.IsAimed(expected));

            f.Seat = true; // any aimed thing beats every nearby one
            Assert.Equal(InteractKind.Seat, InteractResolver.Resolve(f));
            f.Seat = false;
            Clear(f, expected);
        }
    }

    [Fact]
    public void TheWorldLabelledTargets_AndThePeople_AreNamed()
    {
        Assert.True(InteractResolver.HasWorldLabel(InteractKind.NearDoor));
        Assert.True(InteractResolver.HasWorldLabel(InteractKind.AimedDoor));
        Assert.True(InteractResolver.HasWorldLabel(InteractKind.DataCube));
        Assert.False(InteractResolver.HasWorldLabel(InteractKind.MissionBoard));
        Assert.True(InteractResolver.IsPerson(InteractKind.AimedTalk));
        Assert.True(InteractResolver.IsPerson(InteractKind.Trade));
        Assert.False(InteractResolver.IsPerson(InteractKind.NearStation));
    }

    [Fact]
    public void Clear_ForgetsEveryFact()
    {
        var f = new InteractFacts { AimedNpc = true, Crate = true, NearStation = "cargo", LookedStation = "cockpit", TalkNear = true };
        f.Clear();
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(f));
    }

    private static void Clear(InteractFacts f, InteractKind kind)
    {
        switch (kind)
        {
            case InteractKind.OwnBeacon: f.OwnBeacon = false; break;
            case InteractKind.OwnBase: f.OwnBase = false; break;
            case InteractKind.BasePad: f.BasePad = false; break;
            case InteractKind.StationCore: f.StationCore = false; break;
            case InteractKind.Crate: f.Crate = false; break;
            case InteractKind.CrystalDevice: f.CrystalDevice = false; break;
            case InteractKind.BioLab: f.BioLab = false; break;
            case InteractKind.SpawnBed: f.SpawnBed = false; break;
            case InteractKind.Seat: f.Seat = false; break;
            case InteractKind.MissionBoard: f.MissionBoard = false; break;
            case InteractKind.LookedStation: f.LookedStation = string.Empty; break;
            case InteractKind.AimedDoor: f.AimedDoor = false; break;
            case InteractKind.BeamPad: f.BeamPad = false; break;
            case InteractKind.NearDoor: f.NearDoor = false; break;
            case InteractKind.DataCube: f.DataCube = false; break;
            case InteractKind.Factory: f.Factory = false; break;
            case InteractKind.NetFragment: f.NetFragment = false; break;
            case InteractKind.NearStation: f.NearStation = string.Empty; break;
            case InteractKind.Trade: f.Vendor = false; break;
            case InteractKind.Talk: f.TalkNear = false; break;
        }
    }
}
