// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The Unity-free rules behind the cinematics fixes (#2461): the shared cutscene state every overlay reads
/// (<see cref="CutsceneGate"/>), the size rule and retry schedule of the staged prologue (<see cref="PrologueOrbit"/>,
/// <see cref="StagingRetry"/>) and the hull-aware camera spot of the orbit scan (<see cref="CinematicStageScan"/>).
/// </summary>
public sealed class CinematicsFixesTests
{
    // ---- CutsceneGate ----------------------------------------------------------------------------------------------

    [Fact]
    public void CutsceneGate_IsActive_WhileAnyReasonIsOpen()
    {
        var gate = new CutsceneGate();
        Assert.False(gate.Active);

        Assert.True(gate.Begin(CutsceneGate.Prologue));
        Assert.True(gate.Active);
        Assert.True(gate.IsActive(CutsceneGate.Prologue));

        Assert.True(gate.End(CutsceneGate.Prologue));
        Assert.False(gate.Active);
        Assert.Equal(0, gate.Count);
    }

    [Fact]
    public void CutsceneGate_NestedOwners_DoNotClearEachOther()
    {
        // An ending that starts while the prologue still runs: the prologue ending first must not hand the screen
        // back to the HUD while the ending still plays — and the other way round.
        var gate = new CutsceneGate();
        gate.Begin(CutsceneGate.Prologue);
        gate.Begin(CutsceneGate.Resolution);

        gate.End(CutsceneGate.Prologue);
        Assert.True(gate.Active);

        gate.End(CutsceneGate.Resolution);
        Assert.False(gate.Active);
    }

    [Fact]
    public void CutsceneGate_BeginAndEnd_AreIdempotentPerReason()
    {
        // The prologue chrome calls Set every frame; the ending calls End from finish, skip and teardown alike.
        var gate = new CutsceneGate();
        Assert.True(gate.Begin(CutsceneGate.Resolution));
        Assert.False(gate.Begin(CutsceneGate.Resolution));
        Assert.Equal(1, gate.Count);

        Assert.True(gate.End(CutsceneGate.Resolution));
        Assert.False(gate.End(CutsceneGate.Resolution));
        Assert.False(gate.End(CutsceneGate.Intro)); // never opened
        Assert.False(gate.End(null!));
        Assert.False(gate.Active);

        gate.Set(CutsceneGate.Prologue, true);
        gate.Set(CutsceneGate.Prologue, true);
        Assert.Equal(1, gate.Count);
        gate.Set(CutsceneGate.Prologue, false);
        gate.Set(CutsceneGate.Prologue, false);
        Assert.False(gate.Active);
    }

    [Fact]
    public void CutsceneGate_ActiveOtherThan_LetsThePresenterOfOneSceneYieldToTheOthers()
    {
        // VEGA's panel presents the prologue's text, so the prologue alone must not silence it — the ending must.
        var gate = new CutsceneGate();
        Assert.False(gate.ActiveOtherThan(CutsceneGate.Prologue));

        gate.Begin(CutsceneGate.Prologue);
        Assert.False(gate.ActiveOtherThan(CutsceneGate.Prologue));

        gate.Begin(CutsceneGate.Resolution);
        Assert.True(gate.ActiveOtherThan(CutsceneGate.Prologue));

        gate.End(CutsceneGate.Prologue);
        Assert.True(gate.ActiveOtherThan(CutsceneGate.Prologue));
    }

    [Fact]
    public void CutsceneGate_RejectsAnUnnamedReason_AndClearDropsEverything()
    {
        var gate = new CutsceneGate();
        Assert.Throws<ArgumentException>(() => gate.Begin(string.Empty));

        gate.Begin(CutsceneGate.Prologue);
        gate.Begin(CutsceneGate.Resolution);
        gate.Clear();
        Assert.False(gate.Active);
    }

    // ---- PrologueOrbit ---------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]   // size not known yet — the tuned shot
    [InlineData(9, 13)]  // starter-sized: the half diagonal fits inside the tuned orbit with clearance to spare
    [InlineData(11, 11)]
    public void PrologueOrbit_KeepsTheTunedShot_ForStarterSizedShips(int width, int length)
    {
        Assert.Equal(1f, PrologueOrbit.Scale(width, length));
    }

    [Fact]
    public void PrologueOrbit_ScalesBigShips_SoTheSettleOrbitClearsTheHull()
    {
        // A 48×48 self-built ship: the settle orbit must keep HullClearance blocks between camera and hull corner.
        float scale = PrologueOrbit.Scale(48, 48);
        float halfDiagonal = 0.5f * MathF.Sqrt(48f * 48f + 48f * 48f);
        Assert.True(scale > 2f);
        Assert.True(PrologueOrbit.TunedSettleRadius * scale >= halfDiagonal + PrologueOrbit.HullClearance - 0.001f);

        // Bigger ship, bigger shot — monotonic.
        Assert.True(PrologueOrbit.Scale(20, 30) < PrologueOrbit.Scale(30, 40));
    }

    // ---- StagingRetry ----------------------------------------------------------------------------------------------

    [Fact]
    public void StagingRetry_RetriesOnItsCadence_UntilItGivesUp()
    {
        var retry = new StagingRetry();
        retry.Reset();
        Assert.True(retry.Due(10f)); // the first attempt is due at once

        retry.Record(10f, success: false);
        Assert.False(retry.Due(10.1f));
        Assert.True(retry.Due(10f + StagingRetry.RetryInterval));

        // Keep failing on every due moment until the window closes.
        float now = 10f;
        int attempts = 1;
        while (now < 10f + StagingRetry.GiveUpAfter + 1f)
        {
            now += 0.05f;
            if (retry.Due(now))
            {
                retry.Record(now, success: false);
                attempts++;
            }
        }

        Assert.True(retry.GaveUp(now));
        Assert.False(retry.Due(now));
        Assert.InRange(attempts, 6, 10); // one try every half second for about four seconds — not one per frame
    }

    [Fact]
    public void StagingRetry_StopsAfterASuccess_AndResetStartsAnewRun()
    {
        var retry = new StagingRetry();
        retry.Record(0f, success: false);
        retry.Record(0.6f, success: true);
        Assert.True(retry.Succeeded);
        Assert.False(retry.Due(1.2f));
        Assert.False(retry.GaveUp(100f)); // a success never reads as giving up

        retry.Reset();
        Assert.False(retry.Succeeded);
        Assert.True(retry.Due(200f));
    }

    // ---- CinematicStageScan: the hull-aware camera spot ---------------------------------------------------------------

    /// <summary>Flat ground: everything at y &lt; 0 is solid, the rest is loaded air (the hull is no part of it).</summary>
    private static bool FlatGround(int x, int y, int z) => y >= 0;

    /// <summary>A 30×30 landed ship around the origin, 8 high: solid for the camera spot only.</summary>
    private static bool GroundAndHull(int x, int y, int z) => FlatGround(x, y, z) && !(x >= -15 && x < 15 && z >= -15 && z < 15 && y < 8);

    [Fact]
    public void ScanRing_WithAHullSpotSampler_RejectsSpotsInsideTheShip_ButSeesThroughItsHull()
    {
        // Radius 16 at height 7 sits inside the ship's 30×30 footprint along the diagonals' inner part — the spot
        // sampler must block those samples, and the terrain-only path must not block the rest (the look target is
        // inside the ship, so every line of sight crosses the hull).
        var terrainOnly = CinematicStageScan.ScanRing(FlatGround, 0f, 0f, 0f, 2f, 10f, 4f, 36);
        var hullAware = CinematicStageScan.ScanRing(FlatGround, GroundAndHull, 0f, 0f, 0f, 2f, 10f, 4f, 36);
        Assert.All(terrainOnly, Assert.True);  // the old scan happily staged the camera inside the hull
        Assert.All(hullAware, Assert.False);   // radius 10 is inside the 30-wide hull all the way round

        // Far enough out, the hull-aware ring is clear again: the hull never blocks the line of sight.
        var outside = CinematicStageScan.ScanRing(FlatGround, GroundAndHull, 0f, 0f, 0f, 2f, 24f, 4f, 36);
        Assert.All(outside, Assert.True);
    }

    [Fact]
    public void CameraClear_SingleSamplerOverload_StaysTheSameCheck()
    {
        Assert.Equal(
            CinematicStageScan.CameraClear(FlatGround, 0f, 2f, 0f, 16f, 9f, 16f),
            CinematicStageScan.CameraClear(FlatGround, FlatGround, 0f, 2f, 0f, 16f, 9f, 16f));
    }
}
