// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2477 — the cutscene director's new cameras, on fake voxel worlds: the <c>dolly</c> (a straight, eased push-in whose
/// whole path is clear), the <c>pan</c> (turning in place where the view stays open), the <c>flyover</c> (a high arc that
/// rises in the middle), each falling back to the still framing of the same place and down the usual chain; the synthetic
/// stages <c>space_vista</c> and <c>diorama</c> (no terrain needed, skipped on the low presets — the sky, or a card for a
/// scene made only of them); the moves' easing and "Camera motion" off; and the stages' layout rules (the core within the
/// block budget, a calm pulse, the memory chart drifting to its rim without naming a star).
/// </summary>
public sealed class CutsceneCameraModesTests
{
    private const int Ground = 10;

    /// <summary>Flat open ground: everything above <see cref="Ground"/> is known air.</summary>
    private static bool Open(int x, int y, int z) => y > Ground;

    /// <summary>Nothing streamed yet: every cell reads as blocked.</summary>
    private static bool Unloaded(int x, int y, int z) => false;

    private static StagedCamera Stage(string camera, CinematicStageScan.ClearSampler? world, bool sky = true, float yaw = 0f,
        bool synthetic = true)
        => CutsceneStaging.Stage(camera, world, world, 0.5f, Ground + 1, 0.5f, 1f, yaw, sky, chartAvailable: false,
            syntheticAllowed: synthetic);

    private static float Dist(float ax, float ay, float az, float bx, float by, float bz)
        => (float)Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));

    // ---------------- Dolly ----------------

    [Fact]
    public void OnOpenGround_TheDolly_PushesInAlongAStraightLine_ThatIsClearAllTheWay()
    {
        var shot = Stage(CutsceneCameras.Dolly, Open);
        Assert.Equal(StagedCameraKind.Dolly, shot.Kind);
        Assert.False(shot.FellBack);
        Assert.True(shot.TakesCamera && shot.Moves && !shot.Synthetic);

        float lookY = shot.CenterY + shot.LookHeight;
        float from = Dist(shot.StartX, shot.StartY, shot.StartZ, shot.CenterX, lookY, shot.CenterZ);
        float to = Dist(shot.EndX, shot.EndY, shot.EndZ, shot.CenterX, lookY, shot.CenterZ);
        Assert.True(to < from - 3f, $"a push-in: {from} → {to}");
        Assert.Equal(150f, shot.StartAngle); // first try: behind the player's view (yaw 0 + 150°)
        Assert.Equal(shot.StartAngle + CutsceneStaging.DollySlide, shot.EndAngle);

        for (int i = 0; i <= 10; i++)
        {
            CutsceneMoves.Position(shot, i / 10f, out float x, out float y, out float z);
            Assert.True(CinematicStageScan.CameraClear(Open, shot.CenterX, lookY, shot.CenterZ, x, y, z), $"blocked at {i / 10f}");
        }
    }

    [Fact]
    public void TheDolly_EasesFromItsStartToItsEnd_AndSettlesThere()
    {
        var shot = Stage(CutsceneCameras.Dolly, Open);
        CutsceneMoves.Position(shot, CutsceneMoves.Progress(shot, 0f, 10f, motion: true), out float x0, out float y0, out float z0);
        Assert.Equal((shot.StartX, shot.StartY, shot.StartZ), (x0, y0, z0));

        CutsceneMoves.Position(shot, CutsceneMoves.Progress(shot, 10f, 10f, motion: true), out float x1, out float y1, out float z1);
        Assert.Equal(shot.EndX, x1, 3);
        Assert.Equal(shot.EndY, y1, 3);
        Assert.Equal(shot.EndZ, z1, 3);

        // Eased: slow at both ends, fastest in the middle; it holds at the end while a caption still waits for the reader.
        float early = CutsceneMoves.Eased(1f, 10f), middle = CutsceneMoves.Eased(5.5f, 10f) - CutsceneMoves.Eased(4.5f, 10f);
        Assert.True(early < 0.1f * 0.5f, $"a gentle start ({early})");
        Assert.True(middle > early);
        Assert.Equal(0.5f, CutsceneMoves.Eased(5f, 10f), 3);
        Assert.Equal(1f, CutsceneMoves.Eased(25f, 10f));

        // "Camera motion" off: the close end as a still framing.
        Assert.Equal(1f, CutsceneMoves.Progress(shot, 3f, 10f, motion: false));
    }

    [Fact]
    public void AWallAcrossTheDollysPath_MakesItTryAnotherSide_OrFallBackToTheStillFraming()
    {
        // A wall of rock along x = 3..4 (every height): the push-ins that cross it are refused.
        static bool Wall(int x, int y, int z) => y > Ground && !(x == 3 || x == 4);
        var shot = Stage(CutsceneCameras.Dolly, Wall);
        if (shot.Kind == StagedCameraKind.Dolly)
        {
            for (int i = 0; i <= CutsceneStaging.PathSamples; i++)
            {
                CutsceneMoves.Position(shot, (float)i / CutsceneStaging.PathSamples, out float x, out _, out _);
                Assert.True(x < 3f, $"the dolly stays on the anchor's side of the wall ({x})");
            }
        }

        // In a narrow shaft no dolly fits: the still framing of the same place takes over (or the chain below it).
        static bool Shaft(int x, int y, int z) => y > Ground && (x * x + z * z <= 4 || y > 60);
        var shaft = Stage(CutsceneCameras.Dolly, Shaft);
        Assert.NotEqual(StagedCameraKind.Dolly, shaft.Kind);
        Assert.True(shaft.FellBack);
        Assert.Equal(new[] { StagedCameraKind.Dolly, StagedCameraKind.Static, StagedCameraKind.Orbit, StagedCameraKind.Crane, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneStaging.Chain(CutsceneCameras.Dolly));
    }

    // ---------------- Pan ----------------

    [Fact]
    public void OnOpenGround_ThePan_TurnsInPlace_AboveThePlayersHead_AcrossTheirView()
    {
        var shot = Stage(CutsceneCameras.Pan, Open, yaw: 30f);
        Assert.Equal(StagedCameraKind.Pan, shot.Kind);
        Assert.False(shot.FellBack);
        Assert.Equal((0.5f, Ground + 1 + CutsceneStaging.PanPivotHeight, 0.5f), (shot.StartX, shot.StartY, shot.StartZ));
        Assert.Equal(30f, (shot.StartAngle + shot.EndAngle) * 0.5f, 3); // centred on the player's view
        Assert.Equal(70f, shot.EndAngle - shot.StartAngle, 3); // the wide sweep
        Assert.Equal(CutsceneStaging.PanPitch, shot.Pitch);

        // It never moves: every progress is the pivot; the yaw eases out and back (the reader may still be reading).
        CutsceneMoves.Position(shot, 0.7f, out float x, out float y, out float z);
        Assert.Equal((shot.StartX, shot.StartY, shot.StartZ), (x, y, z));
        Assert.Equal(shot.StartAngle, CutsceneMoves.Yaw(shot, CutsceneMoves.Progress(shot, 0f, 8f, motion: true)), 3);
        Assert.Equal(shot.EndAngle, CutsceneMoves.Yaw(shot, CutsceneMoves.Progress(shot, 8f, 8f, motion: true)), 3);
        Assert.Equal(shot.StartAngle, CutsceneMoves.Yaw(shot, CutsceneMoves.Progress(shot, 16f, 8f, motion: true)), 3);
        Assert.Equal(30f, CutsceneMoves.Yaw(shot, CutsceneMoves.Progress(shot, 1f, 8f, motion: false)), 3); // still: the middle
    }

    [Fact]
    public void FacingAWall_ThePanTurnsToWhereTheViewIsOpen()
    {
        // A cliff right in front (+z from z = 3 on): looking ahead is no view, the sides are.
        static bool Cliff(int x, int y, int z) => y > Ground && z < 3;
        var shot = Stage(CutsceneCameras.Pan, Cliff, yaw: 0f);
        Assert.Equal(StagedCameraKind.Pan, shot.Kind);
        float centre = (shot.StartAngle + shot.EndAngle) * 0.5f;
        Assert.NotEqual(0f, centre, 3);
        float half = (shot.EndAngle - shot.StartAngle) * 0.5f;
        for (int i = 0; i <= 6; i++)
        {
            double rad = (centre - half + 2f * half * i / 6f) * Math.PI / 180.0;
            float ex = shot.StartX + (float)Math.Sin(rad) * CutsceneStaging.PanReach;
            float ez = shot.StartZ + (float)Math.Cos(rad) * CutsceneStaging.PanReach;
            Assert.True(CinematicStageScan.PathClear(Cliff, shot.StartX, shot.StartY, shot.StartZ, ex, shot.StartY, ez));
        }
    }

    [Fact]
    public void InATightCave_ThePanFallsBack()
    {
        // A 3×3 pocket around the player, a ceiling at +2: no pivot above the head, no vantage point, no sky.
        static bool Pocket(int x, int y, int z) => y > Ground && y <= Ground + 2 && x >= -1 && x <= 1 && z >= -1 && z <= 1;
        var shot = Stage(CutsceneCameras.Pan, Pocket, sky: false);
        Assert.Equal(StagedCameraKind.PlayerView, shot.Kind);
        Assert.True(shot.FellBack);
        Assert.Equal(new[] { StagedCameraKind.Pan, StagedCameraKind.Static, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneStaging.Chain(CutsceneCameras.Pan));
    }

    // ---------------- Flyover ----------------

    [Fact]
    public void OnOpenGround_TheFlyover_SweepsAHighArc_ThatRisesInTheMiddle()
    {
        var shot = Stage(CutsceneCameras.Flyover, Open);
        Assert.Equal(StagedCameraKind.Flyover, shot.Kind);
        Assert.False(shot.FellBack);
        Assert.Equal(CutsceneStaging.FlyoverSweep, shot.EndAngle - shot.StartAngle, 3);

        CutsceneMoves.Position(shot, 0f, out _, out float y0, out _);
        CutsceneMoves.Position(shot, 0.5f, out float xm, out float ym, out float zm);
        CutsceneMoves.Position(shot, 1f, out _, out float y1, out _);
        Assert.Equal(y0, y1, 3);
        Assert.Equal(y0 + CutsceneStaging.FlyoverRise, ym, 3); // highest in the middle
        Assert.True(y0 - shot.CenterY >= 16f, "higher than the orbit and the crane's start");
        Assert.Equal(CutsceneStaging.FlyoverRadius, Dist(xm, 0f, zm, shot.CenterX, 0f, shot.CenterZ), 2); // on its circle
        Assert.Equal(0.5f, CutsceneMoves.Progress(shot, 0f, 10f, motion: false)); // still: the middle of the arc
    }

    [Fact]
    public void UnderAnOverhang_TheFlyoverFallsBackToTheCrane_OrFurther()
    {
        // A roof 14 blocks up over everything farther than 7 blocks from the anchor: no high arc fits.
        static bool Roof(int x, int y, int z) => y > Ground && (x * x + z * z <= 49 || y < Ground + 14);
        var shot = Stage(CutsceneCameras.Flyover, Roof);
        Assert.NotEqual(StagedCameraKind.Flyover, shot.Kind);
        Assert.True(shot.FellBack);
        Assert.Equal(new[] { StagedCameraKind.Flyover, StagedCameraKind.Crane, StagedCameraKind.Orbit, StagedCameraKind.Sky, StagedCameraKind.PlayerView },
            CutsceneStaging.Chain(CutsceneCameras.Flyover));
    }

    [Theory]
    [InlineData(CutsceneCameras.Dolly)]
    [InlineData(CutsceneCameras.Pan)]
    [InlineData(CutsceneCameras.Flyover)]
    public void BeforeTheChunksStream_OrWithoutAWorld_TheMovesFallBackToTheSky_OrThePlayersView(string camera)
    {
        Assert.Equal(StagedCameraKind.Sky, Stage(camera, Unloaded, sky: true).Kind);
        Assert.Equal(StagedCameraKind.PlayerView, Stage(camera, Unloaded, sky: false).Kind);
        Assert.Equal(StagedCameraKind.PlayerView, Stage(camera, null, sky: false).Kind); // the space view: no world
    }

    // ---------------- Synthetic stages ----------------

    [Theory]
    [InlineData(CutsceneCameras.SpaceVista, StagedCameraKind.SpaceVista)]
    [InlineData(CutsceneCameras.Diorama, StagedCameraKind.Diorama)]
    public void ASyntheticStage_NeedsNoWorld_ButAPresetThatBuildsIt(string camera, StagedCameraKind kind)
    {
        // On the ground, in space (no world) or before the chunks stream: the stage plays all the same.
        foreach (var world in new CinematicStageScan.ClearSampler?[] { Open, Unloaded, null })
        {
            var shot = Stage(camera, world, sky: false);
            Assert.Equal(kind, shot.Kind);
            Assert.False(shot.FellBack);
            Assert.True(shot.TakesCamera && shot.Synthetic && !shot.Moves);
        }

        // The low presets (or a stage that could not be built): the sky under the open sky, else the player's view.
        Assert.Equal(StagedCameraKind.Sky, Stage(camera, Open, sky: true, synthetic: false).Kind);
        var view = Stage(camera, null, sky: false, synthetic: false);
        Assert.Equal(StagedCameraKind.PlayerView, view.Kind);
        Assert.True(view.FellBack);
    }

    [Theory]
    [InlineData(0, false)] // Potato
    [InlineData(1, false)] // Low
    [InlineData(2, true)]  // Medium
    [InlineData(3, true)]  // High
    public void OnlyMediumAndUp_BuildTheSyntheticStages(int preset, bool allowed)
        => Assert.Equal(allowed, CutsceneRules.SyntheticStagesAllowed(preset));

    private static CutscenePlan PlanOf(params (string Camera, string Anchor)[] shots)
    {
        var def = new CutsceneDefinition { Id = "probe", Tier = CutsceneTiers.Scene };
        foreach (var (camera, anchor) in shots)
        {
            def.Shots.Add(new CutsceneShot
            {
                Camera = new CutsceneCamera { Type = camera, Anchor = anchor },
                Duration = 6,
                Captions = { new CutsceneCaption { Key = "line", Style = CutsceneCaptionStyles.Speaker } },
            });
        }

        return CutscenePlan.Build(def, "pack", "probe", string.Empty, new CutsceneTextSource(k => k, _ => true), waitForReader: false);
    }

    [Fact]
    public void ASceneMadeOnlyOfSyntheticStages_PlaysAsACard_OnTheLowPresets()
    {
        var vistaOnly = PlanOf((CutsceneCameras.PlayerView, ""), (CutsceneCameras.SpaceVista, CutsceneAnchors.Ship));
        Assert.False(CutsceneRules.NeedsCardFallback(vistaOnly, _ => true, syntheticAllowed: true));
        Assert.True(CutsceneRules.NeedsCardFallback(vistaOnly, _ => true, syntheticAllowed: false));

        // Another camera shot keeps the scene staged — the synthetic one then falls back to the sky on its own.
        var mixed = PlanOf((CutsceneCameras.Orbit, CutsceneAnchors.Ship), (CutsceneCameras.Diorama, CutsceneAnchors.Core));
        Assert.False(CutsceneRules.NeedsCardFallback(mixed, _ => true, syntheticAllowed: false));

        // A scene without a camera shot is no staged scene at all.
        Assert.False(CutsceneRules.NeedsCardFallback(PlanOf((CutsceneCameras.PlayerView, "")), _ => true, syntheticAllowed: false));
    }

    [Theory]
    [InlineData(CutsceneCameras.SpaceVista, "", SyntheticSet.Vista)]
    [InlineData(CutsceneCameras.SpaceVista, CutsceneAnchors.Player, SyntheticSet.Vista)]
    [InlineData(CutsceneCameras.SpaceVista, CutsceneAnchors.Ship, SyntheticSet.VistaShip)]
    [InlineData(CutsceneCameras.SpaceVista, CutsceneAnchors.Planet, SyntheticSet.VistaPlanet)]
    [InlineData(CutsceneCameras.Diorama, CutsceneAnchors.Core, SyntheticSet.Core)]
    [InlineData(CutsceneCameras.Diorama, CutsceneAnchors.StarMap, SyntheticSet.MemoryChart)]
    public void TheAnchor_PicksWhatTheStageShows(string camera, string anchor, SyntheticSet expected)
        => Assert.Equal(expected, SyntheticStages.SetOf(camera, anchor));

    [Fact]
    public void ADioramaWithoutItsSubject_OrAWorldCamera_HasNoStage()
    {
        Assert.Null(SyntheticStages.SetOf(CutsceneCameras.Diorama, CutsceneAnchors.Ship));
        Assert.Null(SyntheticStages.SetOf(CutsceneCameras.Orbit, CutsceneAnchors.Core));
        Assert.Null(SyntheticStages.SetOf(null, null));
    }

    [Fact]
    public void TheCoreDiorama_StaysWithinTheBlockBudget_WithTheColumnInsideAShellOpenToTheCamera()
    {
        var cells = SyntheticStages.CoreCells();
        Assert.InRange(cells.Count, 50, SyntheticStages.DioramaBlockBudget);
        var column = cells.Where(c => c.Part == CoreDioramaPart.Column).ToList();
        Assert.Equal(SyntheticStages.ColumnHeight, column.Count);
        Assert.All(column, c => Assert.Equal((0, 0), (c.X, c.Z)));
        Assert.Equal(column.Max(c => c.Y), cells.Max(c => c.Y)); // the column stands out over the shell
        Assert.Contains(cells, c => c.Part == CoreDioramaPart.Shell && c.Z > 2); // a back wall …
        Assert.DoesNotContain(cells, c => c.Part == CoreDioramaPart.Shell && c.Z <= -2); // … and the front open
        Assert.Equal(cells.Count, cells.Select(c => (c.X, c.Y, c.Z)).Distinct().Count());

        // The camera rises along the column, looking at it through the opening, outside the shell.
        SyntheticStages.CoreCamera(0f, out _, out float y0, out float z0, out _, out float ly0, out _);
        SyntheticStages.CoreCamera(1f, out _, out float y1, out float z1, out _, out float ly1, out _);
        Assert.True(y1 > y0 + 3f && ly1 > ly0, "rising along the column");
        Assert.True(z0 < -4f && z1 < -4f, "in front of the open shell");
    }

    [Fact]
    public void TheCoresGlow_BreathesSlowly_FarBelowThreeHertz()
    {
        Assert.True(SyntheticStages.CorePulseHz <= 1f); // synthesis §6.6: red pulses ≤ 3 Hz — this one is a calm breath
        Assert.Equal(SyntheticStages.CorePulse(0f), SyntheticStages.CorePulse(1f / SyntheticStages.CorePulseHz), 3);
        for (float t = 0f; t < 5f; t += 0.1f)
        {
            Assert.InRange(SyntheticStages.CorePulse(t), 0f, 1f);
        }
    }

    [Fact]
    public void TheMemoryChart_FitsTheStarsOnItsDisc_AndDriftsFromThePlayersSystemToTheRim()
    {
        var systems = new List<(string Id, float MapX, float MapY, int Color)>
        {
            ("home", 500f, 500f, 0), ("near", 560f, 470f, 0xFFAA66), ("mid", 300f, 650f, 0), ("far", 1400f, 900f, 0x88AAFF),
        };
        var chart = SyntheticStages.LayoutMemoryChart(systems, "home");
        Assert.Equal(4, chart.Stars.Count);
        Assert.All(chart.Stars, s => Assert.True(Math.Sqrt(s.X * s.X + s.Z * s.Z) <= SyntheticStages.ChartRadius + 0.01, "on the disc"));
        Assert.Single(chart.Stars, s => s.Current);
        var home = chart.Stars.Single(s => s.Current);
        Assert.Equal((home.X, home.Z), (chart.StartX, chart.StartZ));
        var far = chart.Stars[3];
        Assert.Equal((far.X, far.Z), (chart.RimX, chart.RimZ)); // the star farthest from home is the rim
        Assert.Equal(0x88AAFF, far.Color);

        // The faint red glow lies beyond the rim — outside every star, never on one (no marker, no name).
        float pulse = (float)Math.Sqrt(chart.PulseX * chart.PulseX + chart.PulseZ * chart.PulseZ);
        float rim = (float)Math.Sqrt(chart.RimX * chart.RimX + chart.RimZ * chart.RimZ);
        Assert.Equal(rim + SyntheticStages.PulseBeyondRim, pulse, 2);

        // The camera's look point drifts from home to the rim; the camera follows behind and above it.
        SyntheticStages.MemoryChartCamera(chart, 0f, out _, out float py, out _, out float lx0, out _, out float lz0);
        SyntheticStages.MemoryChartCamera(chart, 1f, out float px1, out _, out float pz1, out float lx1, out _, out float lz1);
        Assert.Equal((chart.StartX, chart.StartZ), (lx0, lz0));
        Assert.Equal(chart.RimX, lx1, 3);
        Assert.Equal(chart.RimZ, lz1, 3);
        Assert.Equal(SyntheticStages.ChartCameraUp, py);
        Assert.Equal(SyntheticStages.ChartCameraBack, Dist(px1, 0f, pz1, chart.RimX, 0f, chart.RimZ), 2);
        Assert.True(Dist(px1, 0f, pz1, chart.StartX, 0f, chart.StartZ) < Dist(chart.RimX, 0f, chart.RimZ, chart.StartX, 0f, chart.StartZ),
            "the camera trails the drift (it is on the home side of the rim)");
    }

    [Fact]
    public void TheMemoryChart_HoldsUpWithoutAStarMap_OrWithASingleStar()
    {
        var empty = SyntheticStages.LayoutMemoryChart(null, null);
        Assert.Empty(empty.Stars);
        Assert.True(empty.PulseZ > empty.RimZ);

        var single = SyntheticStages.LayoutMemoryChart(new List<(string, float, float, int)> { ("home", 10f, 10f, 0) }, "elsewhere");
        Assert.Single(single.Stars);
        Assert.True(single.Stars[0].Current); // an unknown current system: the first star
        Assert.True(single.RimZ > single.StartZ); // a single star drifts straight out
        SyntheticStages.MemoryChartCamera(single, 0.5f, out _, out _, out _, out _, out _, out float lz);
        Assert.InRange(lz, single.StartZ, single.RimZ);
    }

    [Fact]
    public void TheVista_PushesIntoThePicture_TurningAFewDegrees()
    {
        foreach (var set in new[] { SyntheticSet.Vista, SyntheticSet.VistaShip, SyntheticSet.VistaPlanet })
        {
            SyntheticStages.VistaCamera(set, 0f, out _, out _, out float z0, out float yaw0, out _);
            SyntheticStages.VistaCamera(set, 1f, out _, out _, out float z1, out float yaw1, out _);
            Assert.True(z1 > z0);
            Assert.InRange(yaw1 - yaw0, 5f, 20f);
            SyntheticStages.VistaPlanet(set, out _, out _, out float pz, out float size);
            Assert.True(pz > size * 0.5f + z1, "the camera never flies into the planet");
        }

        SyntheticStages.VistaShip(0f, out float sx0, out _, out _);
        SyntheticStages.VistaShip(1f, out float sx1, out _, out float sz1);
        Assert.True(sx1 > sx0); // across the frame
        SyntheticStages.VistaPlanet(SyntheticSet.VistaShip, out _, out _, out float planetZ, out float planetSize);
        Assert.True(sz1 < planetZ - planetSize * 0.5f, "in front of the planet");
    }
}
