// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2468 — the director's camera staging (<see cref="CutsceneStaging"/>: the requested camera, then the fallback chain
/// orbit → crane → sky → the player's view, on fake voxel worlds) and the Story tab's "Watch again" list and labels
/// (<see cref="CutsceneCatalog"/>).
/// </summary>
public sealed class CutsceneStagingTests
{
    private const int Ground = 10;

    /// <summary>Flat open ground: everything above <see cref="Ground"/> is known air.</summary>
    private static bool Open(int x, int y, int z) => y > Ground;

    /// <summary>Nothing streamed yet: every cell reads as blocked.</summary>
    private static bool Unloaded(int x, int y, int z) => false;

    private static StagedCamera Stage(string camera, CinematicStageScan.ClearSampler? world, bool sky = true, bool chart = false, float scale = 1f)
        => CutsceneStaging.Stage(camera, world, world, 0.5f, Ground + 1, 0.5f, scale, 0f, sky, chart);

    [Fact]
    public void OnOpenGround_TheOrbitCirclesFreely()
    {
        var shot = Stage(CutsceneCameras.Orbit, Open);
        Assert.Equal(StagedCameraKind.Orbit, shot.Kind);
        Assert.False(shot.FellBack);
        Assert.False(shot.ArcLimited);
        Assert.Equal(16f, shot.Radius);
        Assert.Equal(7f, shot.Height);
        Assert.Equal(22f, shot.StartRadius);
        Assert.True(shot.TakesCamera);
    }

    [Fact]
    public void ABiggerShip_ScalesTheWholeShot()
    {
        var shot = Stage(CutsceneCameras.Orbit, Open, scale: 2f);
        Assert.Equal(32f, shot.Radius);
        Assert.Equal(14f, shot.Height);
        Assert.Equal(4f, shot.LookHeight);
    }

    [Fact]
    public void AWallOnOneSide_LimitsTheOrbitToTheOpenArc()
    {
        // A cliff along +x: every cell with x > 4 is rock up to y = 60.
        static bool Cliff(int x, int y, int z) => y > Ground && !(x > 4 && y < 60);
        var shot = Stage(CutsceneCameras.Orbit, Cliff);
        Assert.Equal(StagedCameraKind.Orbit, shot.Kind);
        Assert.True(shot.ArcLimited);
        Assert.InRange(shot.ArcHalf * 2f, CutsceneStaging.MinArcSweep, 355f);
    }

    [Fact]
    public void InAPit_TheCraneTakesOver()
    {
        // A shaft of radius 9 around the anchor; rock all around it up to y = 40.
        static bool Pit(int x, int y, int z) => y > Ground && (x * x + z * z <= 81 || y > 40);
        var shot = Stage(CutsceneCameras.Orbit, Pit);
        Assert.Equal(StagedCameraKind.Crane, shot.Kind);
        Assert.True(shot.FellBack);
        Assert.True(shot.Height > shot.StartHeight); // it rises
    }

    [Fact]
    public void BeforeTheChunksStream_TheShotFallsBackToTheSky_OrThePlayersView()
    {
        var sky = Stage(CutsceneCameras.Orbit, Unloaded, sky: true);
        Assert.Equal(StagedCameraKind.Sky, sky.Kind);
        Assert.True(sky.FellBack);

        var view = Stage(CutsceneCameras.Static, Unloaded, sky: false);
        Assert.Equal(StagedCameraKind.PlayerView, view.Kind);
        Assert.True(view.FellBack);
        Assert.False(view.TakesCamera);

        var noWorld = Stage(CutsceneCameras.Crane, null, sky: false);
        Assert.Equal(StagedCameraKind.PlayerView, noWorld.Kind);
    }

    [Fact]
    public void AStaticShot_FramesTheAnchorFromAClearSpot_WithAPushIn()
    {
        var shot = Stage(CutsceneCameras.Static, Open);
        Assert.Equal(StagedCameraKind.Static, shot.Kind);
        Assert.True(shot.Radius < shot.StartRadius);
        Assert.Equal(150f, shot.StartAngle); // first try: behind the player's view (yaw 0 + 150°)
    }

    [Fact]
    public void TheChart_NeedsTheStarMap_TheSkyNeedsTheOpenSky()
    {
        Assert.Equal(StagedCameraKind.Chart, Stage(CutsceneCameras.Chart, null, sky: false, chart: true).Kind);
        Assert.Equal(StagedCameraKind.Sky, Stage(CutsceneCameras.Chart, null, sky: true, chart: false).Kind);
        Assert.Equal(StagedCameraKind.Sky, Stage(CutsceneCameras.Sky, null, sky: true).Kind);
        Assert.Equal(StagedCameraKind.PlayerView, Stage(CutsceneCameras.Sky, null, sky: false).Kind);
    }

    [Theory]
    [InlineData(CutsceneCameras.PlayerView, false)]
    [InlineData(CutsceneCameras.Overlay, true)]
    [InlineData("dolly_from_the_future", true)]
    public void PlayerViewAndOverlay_NeverTakeTheCamera(string camera, bool fellBack)
    {
        var shot = Stage(camera, Open);
        Assert.Equal(StagedCameraKind.PlayerView, shot.Kind);
        Assert.False(shot.TakesCamera);
        Assert.Equal(fellBack, shot.FellBack);
    }

    // ---------------- The "Watch again" list ----------------

    private static readonly Dictionary<string, string> Table = new(StringComparer.Ordinal)
    {
        ["ui.story.prologue"] = "Prologue",
        ["pack.name"] = "The Pack",
        ["ch.one"] = "Awakening",
        ["strip.title"] = "Part: {0}",
        ["beat.text"] = "A long beat text that keeps going well past the sixty characters a row may show in the list.",
    };

    private static CutsceneTextSource Text() => new(k => Table.TryGetValue(k, out var v) ? v : "[" + k + "]", Table.ContainsKey);

    private static StoryDefinition Pack()
    {
        var pack = new StoryDefinition { Id = "pack", NameKey = "pack.name" };
        pack.Chapters.Add(new StoryChapter { Id = "one", TitleKey = "ch.one" });
        pack.Chapters.Add(new StoryChapter { Id = "two", TitleKey = "ch.two" });
        pack.Cutscenes.Add(Def("prologue", CutsceneCaptionStyles.TitleCard, "pack.name", "ui.story.prologue"));
        pack.Cutscenes.Add(Def("strip", CutsceneCaptionStyles.TitleCard, "strip.title", template: true));
        pack.Cutscenes.Add(Def("beat", CutsceneCaptionStyles.Speaker, "beat.text"));
        var secret = Def("secret", CutsceneCaptionStyles.Subtitle, "beat.text");
        secret.Options.Replayable = false;
        pack.Cutscenes.Add(secret);
        var bound = Def("opener", CutsceneCaptionStyles.Subtitle, string.Empty);
        bound.Chapter = "one";
        pack.Cutscenes.Add(bound);
        return pack;
    }

    private static CutsceneDefinition Def(string id, string style, string key, string kicker = "", bool template = false)
    {
        var def = new CutsceneDefinition { Id = id, Template = template };
        var shot = new CutsceneShot { Duration = 4f };
        if (key.Length > 0)
        {
            shot.Captions.Add(new CutsceneCaption { Key = key, Style = style, KickerKey = kicker });
        }

        def.Shots.Add(shot);
        return def;
    }

    [Fact]
    public void TheList_HoldsOnlySeenReplayableScenesOfInstalledPacks_InThePacksOrder()
    {
        var seen = new[] { "pack/beat", "pack/strip@two", "pack/strip@one", "pack/secret", "pack/prologue", "pack/strip",
            "pack/nowhere", "gone/prologue", "pack/__recap", "pack/opener", "broken" };
        var rows = CutsceneCatalog.ReplayEntries(new[] { Pack() }, seen);
        Assert.Equal(new[] { "prologue", "strip@one", "strip@two", "beat", "opener" }, rows.Select(r => r.CutsceneId));
        Assert.All(rows, r => Assert.Equal("pack", r.Pack.Id));
    }

    [Fact]
    public void ARow_IsNamedLikeTheSceneShowsItself()
    {
        var pack = Pack();
        var text = Text();
        Assert.Equal("Prologue — The Pack", CutsceneCatalog.Label(pack, pack.FindCutscene("prologue")!, "prologue", text));
        Assert.Equal("Part: Awakening", CutsceneCatalog.Label(pack, pack.FindCutscene("strip@one")!, "strip@one", text));
        Assert.Equal("Awakening", CutsceneCatalog.Label(pack, pack.FindCutscene("opener")!, "opener", text));
        string beat = CutsceneCatalog.Label(pack, pack.FindCutscene("beat")!, "beat", text);
        Assert.True(beat.Length <= CutsceneCatalog.MaxLabelChars);
        Assert.EndsWith("…", beat);
        var bare = new CutsceneDefinition { Id = "bare" };
        Assert.Equal("bare", CutsceneCatalog.Label(pack, bare, "bare", text));
    }

    [Fact]
    public void SeenKeys_SplitIntoPackAndId()
    {
        Assert.True(CutsceneCatalog.TrySplitSeenKey("vega_protocol/ch1.open", out var pack, out var id));
        Assert.Equal(("vega_protocol", "ch1.open"), (pack, id));
        Assert.True(CutsceneCatalog.TrySplitSeenKey("tutorial/tut.strip@b3", out _, out id));
        Assert.Equal("tut.strip@b3", id);
        Assert.False(CutsceneCatalog.TrySplitSeenKey("noslash", out _, out _));
        Assert.False(CutsceneCatalog.TrySplitSeenKey("/x", out _, out _));
        Assert.False(CutsceneCatalog.TrySplitSeenKey("x/", out _, out _));
        Assert.Equal("a/b", CutsceneCatalog.SeenKey("a", "b"));
    }
}
