// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The voxel shot plan of the own ship's landing and lift-off (#3258): on open ground the classic low rear-quarter shot
/// with its glides, a better spot when one side is walled, the crane over a courtyard's roofs for the part it can see and
/// the cockpit for the rest, the cockpit all the way in a walled pad or a roofed hall — and unknown (unstreamed) cells
/// never read as open space.
/// </summary>
public sealed class TransitionShotPlanTests
{
    // A starter-sized hull standing on flat ground at y = 0: cells x -3..3, y 0..4, z -5..5 (the box is one block wider
    // than the last cell on each axis, as a render box is).
    private const float MinX = -3f, MaxX = 4f, MinY = 0f, MaxY = 5f, MinZ = -5f, MaxZ = 6f;

    private static bool HullCell(int x, int y, int z) => x >= -3 && x <= 3 && y >= 0 && y <= 4 && z >= -5 && z <= 5;

    /// <summary>Flat ground: everything below y = 0 is solid, the rest is loaded air.</summary>
    private static bool Open(int x, int y, int z) => y >= 0;

    private static TransitionShotInput Input(CinematicStageScan.ClearSampler terrain)
    {
        float ex = (MaxX - MinX) / 2f, ey = (MaxY - MinY) / 2f, ez = (MaxZ - MinZ) / 2f;
        float radius = (float)Math.Sqrt(ex * ex + ey * ey + ez * ez);
        return new TransitionShotInput(terrain, (x, y, z) => terrain(x, y, z) && !HullCell(x, y, z))
        {
            CenterX = MinX + ex,
            CenterY = MinY + ey,
            CenterZ = MinZ + ez,
            ExtentX = ex,
            ExtentY = ey,
            ExtentZ = ez,
            EyeX = 0.5f,
            EyeY = 1.6f,
            EyeZ = 3.5f,
            Distance = Math.Max(10f, radius * 1.6f), // the director's touchdown distance
            GroundHeight = 2.2f,
            Drop = TransitionFlightPath.TouchdownDrop(MaxY - MinY),
        };
    }

    [Fact]
    public void OpenField_Touchdown_IsTheClassicShotOutsideAllTheWay()
    {
        var plan = TransitionShotPlan.ForTouchdown(Input(Open));

        Assert.True(plan.HasSpot);
        Assert.Equal(0f, plan.OutsideFrom);
        Assert.Equal(plan.Duration, plan.OutsideTo);
        Assert.True(plan.Glide); // the push-in and the glide home into the eyes, as always
        Assert.Equal((0, 0, 0), (plan.Direction, plan.DistanceStep, plan.HeightStep)); // low, far, rear-right
        Assert.True(plan.SpotZ < MinZ && plan.SpotX > MaxX, "the rear-right quarter, the engines in frame");
        Assert.Equal(MinY + 2.2f, plan.SpotY, 3);
        Assert.Equal(1, plan.CandidatesChecked); // nothing can beat a full shot with its glide — the search stops there
    }

    [Fact]
    public void OpenField_Liftoff_GlidesOutOfTheEyesAndStaysOutside()
    {
        var plan = TransitionShotPlan.ForLiftoff(Input(Open));

        Assert.True(plan.Glide);
        Assert.Equal(0f, plan.OutsideFrom);
        Assert.Equal(TransitionFlightPath.CloudsAt, plan.OutsideTo);
        Assert.Equal((0, 0, 0), (plan.Direction, plan.DistanceStep, plan.HeightStep));
    }

    [Fact]
    public void PadWalledInOnAllSides_PlaysInTheCockpitFromTheStart()
    {
        // A building ring from 9 blocks out, 60 high: every candidate (the nearest ring stands ~12.7 out) is inside it.
        bool Walled(int x, int y, int z) => y >= 0 && (Math.Max(Math.Abs(x), Math.Abs(z)) < 9 || y >= 60);

        var touchdown = TransitionShotPlan.ForTouchdown(Input(Walled));
        var liftoff = TransitionShotPlan.ForLiftoff(Input(Walled));

        Assert.False(touchdown.HasSpot);
        Assert.False(touchdown.HasOutside);
        Assert.False(touchdown.Glide);
        Assert.Equal(TransitionShotPlan.CandidateCount, touchdown.CandidatesChecked);
        Assert.False(liftoff.HasOutside);
        Assert.Contains("cockpit 0–4.1 s", touchdown.Describe(cabinGlide: true), StringComparison.Ordinal);
        Assert.Contains("cabin glide to the eyes", touchdown.Describe(cabinGlide: true), StringComparison.Ordinal);
        Assert.Contains("blink to the helm", liftoff.Describe(cabinGlide: false), StringComparison.Ordinal);
    }

    [Fact]
    public void HallWithARoof_NeverPutsTheCameraOutside()
    {
        // A closed hangar: walls 30 out, a roof at y 20..21 over everything, the pad included. The spots inside are free,
        // but the stand-in starts above the roof and the lift-off climbs through it — no spot sees the stage's ends.
        bool Hall(int x, int y, int z) => y >= 0 && y != 20 && y != 21 && Math.Max(Math.Abs(x), Math.Abs(z)) < 30;

        var touchdown = TransitionShotPlan.ForTouchdown(Input(Hall));
        var liftoff = TransitionShotPlan.ForLiftoff(Input(Hall));

        Assert.False(touchdown.HasOutside);
        Assert.False(liftoff.HasOutside);
    }

    [Fact]
    public void HighWallOnOneSide_PicksASpotOnTheOpenSide()
    {
        // A cliff behind the stern (z < -8), 60 high: the rear quarters and the rear are inside it.
        bool Cliff(int x, int y, int z) => y >= 0 && (z >= -8 || y >= 60);

        var plan = TransitionShotPlan.ForTouchdown(Input(Cliff));

        Assert.True(plan.HasSpot);
        Assert.True(plan.SpotZ > -8f, $"spot z {plan.SpotZ} must stand clear of the cliff");
        Assert.Equal(0, plan.HeightStep); // still the low shot ...
        Assert.Equal(2, plan.Direction);  // ... from the front-right quarter, the first open one
        Assert.Equal(plan.Duration, plan.OutsideTo);
        Assert.True(plan.Glide);
    }

    [Fact]
    public void Courtyard_CraneSeesTheTopOfTheDescentThenTheCockpit()
    {
        // Buildings 10 high all round a yard one block wider than the hull: the low and roofline spots
        // stand inside the buildings; a crane spot over their roofs sees the stand-in high up, loses it behind the eaves
        // in the last metres — so the touchdown cuts into the cockpit for those.
        var plan = TransitionShotPlan.ForTouchdown(Input(Courtyard));

        Assert.True(plan.HasSpot);
        Assert.Equal(2, plan.HeightStep);
        Assert.Equal(0f, plan.OutsideFrom);
        Assert.InRange(plan.OutsideTo, TransitionShotPlan.MinOutsideSeconds, plan.Duration - 0.5f);
        Assert.True(plan.OutsideTo <= plan.VisibleTo);
        Assert.False(plan.Glide);
        string line = plan.Describe(cabinGlide: false);
        Assert.Contains("cockpit", line, StringComparison.Ordinal);
        Assert.Contains("blink to the eyes", line, StringComparison.Ordinal);
        Assert.Contains("crane", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Courtyard_LiftoffStartsInTheCockpitAndCutsOutOnABeat()
    {
        var plan = TransitionShotPlan.ForLiftoff(Input(Courtyard));

        Assert.True(plan.HasOutside);
        Assert.Equal(2, plan.HeightStep);
        Assert.False(plan.Glide); // the way from the eyes to the crane runs through the buildings
        Assert.True(plan.OutsideFrom > 0f);
        Assert.True(plan.OutsideFrom >= plan.VisibleFrom);
        Assert.Equal(plan.Duration, plan.OutsideTo);
    }

    [Fact]
    public void UnknownCells_ReadAsBlocked()
    {
        // Nothing streamed at all → nothing to stage.
        static bool Unknown(int x, int y, int z) => false;
        Assert.False(TransitionShotPlan.ForTouchdown(Input(Unknown)).HasOutside);
        Assert.False(TransitionShotPlan.ForLiftoff(Input(Unknown)).HasOutside);

        // Everything behind the stern not streamed yet: the rear spots there must not count as open sky.
        bool RearUnknown(int x, int y, int z) => y >= 0 && z >= -6;
        var plan = TransitionShotPlan.ForTouchdown(Input(RearUnknown));
        Assert.True(plan.HasSpot);
        Assert.True(plan.SpotZ > -5f, $"spot z {plan.SpotZ} must stand in the streamed half");
    }

    [Fact]
    public void ABlockOnTheGlideHome_PrefersAnotherLowSpotWithAClearWay()
    {
        // The whole descent is visible from the classic spot, but a low block (y 0..1, under every line of sight) stands on
        // its glide home, where the glide has come down to the eyes' height.
        var input = Input(Open);
        var open = TransitionShotPlan.ForTouchdown(input);
        float pushX = open.SpotX + (input.CenterX - open.SpotX) * 0.25f, pushZ = open.SpotZ + (input.CenterZ - open.SpotZ) * 0.25f;
        int px = (int)Math.Floor(pushX + (input.EyeX - pushX) * 0.45f), pz = (int)Math.Floor(pushZ + (input.EyeZ - pushZ) * 0.45f);
        bool Block(int x, int y, int z) => y >= 0 && !(y <= 1 && Math.Abs(x - px) <= 1 && Math.Abs(z - pz) <= 1);

        var plan = TransitionShotPlan.ForTouchdown(Input(Block));

        Assert.Equal(plan.Duration, plan.OutsideTo);
        Assert.True(plan.Glide, "another spot with a clear way home wins over a blink");
        Assert.Equal(0, plan.HeightStep);
        Assert.NotEqual((open.Direction, open.DistanceStep), (plan.Direction, plan.DistanceStep));
    }

    [Fact]
    public void SomethingLowRoundTheEyes_KeepsTheClassicShotAndBlinksHome()
    {
        // A two-block ring round the eyes' cell (0, 1, 3): under every line of sight, but on every low glide into the eyes
        // (those come in at eye height), while the roofline and crane glides pass over it. A low shot that blinks home
        // beats a higher one that could glide.
        bool Ring(int x, int y, int z) => y >= 0 && !(y <= 1 && Math.Max(Math.Abs(x), Math.Abs(z - 3)) == 2);

        var plan = TransitionShotPlan.ForTouchdown(Input(Ring));

        Assert.Equal((0, 0, 0), (plan.Direction, plan.DistanceStep, plan.HeightStep));
        Assert.Equal(plan.Duration, plan.OutsideTo);
        Assert.False(plan.GlideClear);
        Assert.False(plan.Glide);
        Assert.Contains("outside 0–4.1 s, blink to the eyes", plan.Describe(cabinGlide: true), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16, 0f, 4.1f)]   // all visible → outside to the end
    [InlineData(0, 0f, 0f)]      // nothing visible → cockpit from the start
    [InlineData(4, 0f, 0f)]      // the last visible moment (0.82 s) is under a second → cockpit from the start
    [InlineData(5, 0f, 1.0933f)] // 1.09 s visible → cut there
    public void TouchdownWindow_FollowsTheVisiblePrefix(int visible, float from, float to)
    {
        var (f, t) = TransitionShotPlan.TouchdownWindow(visible, 4.1f, dustBeat: 0.5f);
        Assert.Equal(from, f, 3);
        Assert.Equal(to, t, 3);
    }

    [Fact]
    public void TouchdownWindow_SnapsBackToANearDustBeat()
    {
        float last = TransitionShotPlan.TimeAt(8, 4.1f); // 2.19 s
        Assert.Equal(1.8f, TransitionShotPlan.TouchdownWindow(9, 4.1f, dustBeat: 1.8f).To, 3);
        Assert.Equal(last, TransitionShotPlan.TouchdownWindow(9, 4.1f, dustBeat: 1.2f).To, 3); // too far back — no snap
        Assert.Equal(last, TransitionShotPlan.TouchdownWindow(9, 4.1f, dustBeat: 2.5f).To, 3); // after the cut — no snap
    }

    [Fact]
    public void LiftoffWindow_CutsOutOnTheFirstBeatThatSeesTheRest()
    {
        const float d = TransitionFlightPath.CloudsAt;
        Assert.Equal((0f, d), TransitionShotPlan.LiftoffWindow(0, d, glideClear: true));
        Assert.Equal((TransitionFlightPath.LiftAt, d), TransitionShotPlan.LiftoffWindow(0, d, glideClear: false));
        Assert.Equal((TransitionFlightPath.ClimbAt, d), TransitionShotPlan.LiftoffWindow(7, d, glideClear: true)); // 1.91 s
        Assert.Equal((TransitionFlightPath.ClimbAt, d), TransitionShotPlan.LiftoffWindow(10, d, glideClear: false)); // 2.73 s
        Assert.Equal((TransitionFlightPath.BoomAt, d), TransitionShotPlan.LiftoffWindow(11, d, glideClear: false)); // 3.01 s
        float late = TransitionShotPlan.TimeAt(13, d); // 3.55 s, past the last beat
        Assert.Equal(late, TransitionShotPlan.LiftoffWindow(13, d, glideClear: false).From, 3);
        Assert.Equal((0f, 0f), TransitionShotPlan.LiftoffWindow(15, d, glideClear: false)); // only the very end — stay inside
        Assert.Equal((0f, 0f), TransitionShotPlan.LiftoffWindow(16, d, glideClear: true));  // never visible
    }

    /// <summary>Buildings 10 high everywhere but a yard of x -4..4, z -6..6 — one block round the hull.</summary>
    private static bool Courtyard(int x, int y, int z)
        => y >= 0 && (y >= 10 || (x >= -4 && x <= 4 && z >= -6 && z <= 6));
}
