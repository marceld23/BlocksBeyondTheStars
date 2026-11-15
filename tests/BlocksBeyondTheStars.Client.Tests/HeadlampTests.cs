// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Unity-free rules of the suit headlamp package (#3229–#3234): which other players' lamps the shaders get, the eye
/// adaptation's lamp share, the volumetric beam's conditions, the dust that only the beam shows, and the occupancy volume
/// behind the High lamp shadows (window, wrap addressing, slab refills, reading the world).
/// </summary>
public sealed class HeadlampTests
{
    private const int Cs = WorldConstants.ChunkSize;

    [Fact]
    public void RemoteLamps_AreTheNearestLitOnesInReach_AtMostFour()
    {
        var off = new Vector3f(float.NaN, 0f, 0f);
        var lamps = new List<Vector3f>
        {
            new Vector3f(30f, 0f, 0f), new Vector3f(5f, 0f, 0f), off, new Vector3f(60f, 0f, 0f),
            new Vector3f(10f, 0f, 0f), new Vector3f(2f, 0f, 0f), new Vector3f(20f, 0f, 0f),
        };
        var pick = new List<int>();

        HeadlampRules.SelectRemote(lamps, Vector3f.Zero, HeadlampRules.RemoteReach, HeadlampRules.MaxRemoteLamps, pick);

        Assert.Equal(new[] { 5, 1, 4, 6 }, pick); // 2 m, 5 m, 10 m, 20 m — the off lamp, the far one and the fifth drop out
    }

    [Fact]
    public void RemoteLamps_NoneInReach_LeaveTheListEmpty()
    {
        var pick = new List<int> { 9 };
        HeadlampRules.SelectRemote(new List<Vector3f> { new Vector3f(0f, 100f, 0f) }, Vector3f.Zero, HeadlampRules.RemoteReach, 4, pick);
        Assert.Empty(pick);
    }

    [Fact]
    public void ALitLamp_IsLightTheEyesAdaptTo()
    {
        Assert.Equal(0.03f, HeadlampRules.AdaptedBrightness(0.03f, lampLit: false));
        Assert.Equal(HeadlampRules.AdaptationBrightness, HeadlampRules.AdaptedBrightness(0.03f, lampLit: true));
        Assert.Equal(0.9f, HeadlampRules.AdaptedBrightness(0.9f, lampLit: true)); // daylight wins over the lamp
    }

    [Fact]
    public void TheLampBeam_RunsAtNight_WhereThereIsAirToCarryIt()
    {
        var cave = VolumetricLightPolicy.LampTargetFor(enabled: true, anyLamp: true, spaceView: false, menuOpen: false, air: true,
            hazeOn: true, indoors: false, sheltered: true, underwater: 0f);
        Assert.Equal(1f, cave.Strength);
        Assert.Equal(VolumetricDust.Shelter, cave.Dust);

        // The sun's own target is off at night — the lamp's is not.
        Assert.Equal(0f, VolumetricLightPolicy.TargetFor(true, false, false, true, true, false, true, sunUp: -0.5f, underwater: 0f, weather: 0f).Strength);
    }

    [Theory]
    [InlineData(false, true, false, false, true, false)] // no lamp lit
    [InlineData(true, false, false, false, true, false)] // the switch is off
    [InlineData(true, true, true, false, true, false)]   // the flight view
    [InlineData(true, true, false, true, true, false)]   // a menu
    [InlineData(true, true, false, false, false, false)] // no air (space, an airless world)
    [InlineData(false, true, false, false, true, true)]  // under water: still no lamp lit
    [InlineData(true, false, false, false, true, true)]  // under water: the switch is off
    [InlineData(true, true, true, false, true, true)]    // under water: the flight view
    [InlineData(true, true, false, true, true, true)]    // under water: a menu
    public void TheLampBeam_IsOff(bool anyLamp, bool enabled, bool spaceView, bool menuOpen, bool air, bool underwater)
    {
        var t = VolumetricLightPolicy.LampTargetFor(enabled, anyLamp, spaceView, menuOpen, air, hazeOn: true, indoors: false,
            sheltered: true, underwater: underwater ? 1f : 0f);
        Assert.Equal(0f, t.Strength);
    }

    [Theory]
    [InlineData(true, true)]   // a breathable sea
    [InlineData(false, false)] // no air counted, the haze switched off: the water carries the beam all the same
    public void UnderWater_TheWaterCarriesTheLampBeam(bool air, bool hazeOn)
    {
        var t = VolumetricLightPolicy.LampTargetFor(enabled: true, anyLamp: true, spaceView: false, menuOpen: false, air: air,
            hazeOn: hazeOn, indoors: false, sheltered: false, underwater: 1f);
        Assert.Equal(1f, t.Strength);
        Assert.Equal(VolumetricDust.Water, t.Dust); // #3697

        // The sun's beams stay off under water.
        Assert.Equal(0f, VolumetricLightPolicy.TargetFor(true, false, false, true, true, false, false, sunUp: 1f, underwater: 1f, weather: 0f).Strength);
    }

    [Fact]
    public void BeamDust_ShowsWhereNoOtherMotesDrift_WhileTheLampIsLit()
    {
        Assert.Equal(AmbientMoteMode.Beam, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: false, reducedEffects: false, headlamp: true)); // a cave
        Assert.Equal(AmbientMoteMode.Beam, AmbientMotes.ModeFor(airless: false, aboard: true, atHome: false, openAir: true, reducedEffects: false, headlamp: true));  // a guest aboard
        Assert.Equal(AmbientMoteMode.Outdoor, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: true, reducedEffects: false, headlamp: true)); // the outdoor motes catch the beam themselves
        Assert.Equal(AmbientMoteMode.Lamplight, AmbientMotes.ModeFor(airless: false, aboard: true, atHome: true, openAir: false, reducedEffects: false, headlamp: true));
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: true, aboard: false, atHome: false, openAir: false, reducedEffects: false, headlamp: true));  // no air, no dust
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: false, reducedEffects: true, headlamp: true));  // Reduced effects
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: false, reducedEffects: false, headlamp: false));
    }

    [Theory]
    [InlineData(true, true)]   // the open sea, the lamp lit: the lamp lights the particles — its cone on every preset
    [InlineData(true, false)]  // the open sea by day: the particles drift on their own
    [InlineData(false, true)]  // a flooded cave
    public void UnderWater_TheParticlesInTheWaterDrift_NeverThePlanetsOutdoorMotes(bool openAir, bool headlamp)
    {
        // #3698
        Assert.Equal(AmbientMoteMode.Water, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: openAir,
            reducedEffects: false, headlamp: headlamp, underwater: true));
        Assert.Equal(AmbientMoteMode.None, AmbientMotes.ModeFor(airless: false, aboard: false, atHome: false, openAir: openAir,
            reducedEffects: true, headlamp: headlamp, underwater: true));
        Assert.True(AmbientMotes.WaterMotes.Rise < 0f, "marine snow sinks");
        Assert.True(AmbientMotes.WaterMotes.R < 0.3f && AmbientMotes.WaterMotes.G < 0.3f, "dim on its own: the lamp lights it");
    }

    [Theory]
    [InlineData(0, -28)]
    [InlineData(7, -28)]
    [InlineData(8, -20)]
    [InlineData(-1, -36)]
    [InlineData(-8, -36)]
    [InlineData(-9, -44)]
    public void TheWindow_SnapsInSteps_AndKeepsTheCentreWellInside(int centre, int origin)
    {
        Assert.Equal(origin, LampOccupancy.OriginFor(centre));
        int low = centre - origin, high = origin + LampOccupancy.Size - 1 - centre;
        Assert.True(low >= 28 && high >= 28, $"centre {centre}: {low} cells below, {high} above");
    }

    [Fact]
    public void Cells_AreStoredByWorldCoordinateModSize()
    {
        Assert.Equal(LampOccupancy.Index(3, 4, 5), LampOccupancy.Index(3 + 64, 4 - 64, 5 + 128));
        Assert.Equal(61, LampOccupancy.Index(-3, 0, 0));
        Assert.Equal(1 + (2 * 64) + (3 * 64 * 64), LampOccupancy.Index(1, 2, 3));
    }

    [Fact]
    public void Recenter_FillsTheWholeWindowFirst_ThenOnlyTheSlabThatCameIn()
    {
        var o = new LampOccupancy();
        var dirty = new List<CellBox>();

        Assert.True(o.Recenter(0, 0, 0, dirty));
        var all = Assert.Single(dirty);
        Assert.Equal(64L * 64 * 64, all.Volume);

        dirty.Clear();
        Assert.False(o.Recenter(5, 3, 7, dirty)); // still inside the same snap step
        Assert.Empty(dirty);

        Assert.True(o.Recenter(8, 0, 0, dirty)); // one step east
        var slab = Assert.Single(dirty);
        Assert.Equal(36, slab.MinX); // the old window ended at -28 + 64 = 36
        Assert.Equal(44, slab.MaxX);
        Assert.Equal(8L * 64 * 64, slab.Volume);

        dirty.Clear();
        Assert.True(o.Recenter(8, 0, -200, dirty)); // a jump further than the window: everything again
        Assert.Equal(64L * 64 * 64, Assert.Single(dirty).Volume);
    }

    [Fact]
    public void Fill_ReadsTheWorld_OpenWhereNothingStreamed()
    {
        var world = new ClientWorld();
        var blocks = new ushort[Cs * Cs * Cs];
        blocks[WorldConstants.LocalIndex(2, 3, 4)] = 7; // one block in the chunk at the origin
        world.StoreChunk(new ChunkCoord(0, 0, 0), blocks);
        var o = new LampOccupancy();
        var dirty = new List<CellBox>();
        o.Recenter(0, 0, 0, dirty);

        bool changed = o.Fill(dirty[0], world, (id, shape) => id == 7 ? (byte)255 : (byte)0);

        Assert.True(changed);
        Assert.Equal(255, o.Cells[LampOccupancy.Index(2, 3, 4)]);
        Assert.Equal(0, o.Cells[LampOccupancy.Index(2, 3, 5)]);
        Assert.Equal(0, o.Cells[LampOccupancy.Index(-5, -5, -5)]); // a chunk that never streamed reads open
        Assert.False(o.Fill(dirty[0], world, (id, shape) => id == 7 ? (byte)255 : (byte)0)); // nothing new: no upload

        blocks[WorldConstants.LocalIndex(2, 3, 4)] = 0; // mined (a fresh copy of the chunk)
        world.StoreChunk(new ChunkCoord(0, 0, 0), blocks);
        Assert.True(o.Fill(o.Brick(BrickHolding(o, 2, 3, 4)), world, (id, shape) => id == 7 ? (byte)255 : (byte)0));
        Assert.Equal(0, o.Cells[LampOccupancy.Index(2, 3, 4)]);
    }

    /// <summary>The refresh brick that contains world cell (x, y, z) of the current window.</summary>
    private static int BrickHolding(LampOccupancy o, int x, int y, int z)
    {
        for (int b = 0; b < LampOccupancy.BrickCount; b++)
        {
            var box = o.Brick(b);
            if (x >= box.MinX && x < box.MaxX && y >= box.MinY && y < box.MaxY && z >= box.MinZ && z < box.MaxZ)
            {
                return b;
            }
        }

        return -1;
    }
}
