// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3899 — the sky-lights' four scenes of the VEGA pack (<c>tower.message</c>, <c>tank.opens</c>, <c>cruiser.arrives</c>,
/// <c>echo.speaks</c>) through the client's own planner and playback, in English and German: every key resolves, the two
/// scenes at a tower take the camera at the place the server sends, the two cards leave the camera and the controls to
/// the player, ECHO speaks in her own box, one page a line, and each scene plays to its end as the reader continues.
/// </summary>
public sealed class SkyLightScenesClientTests
{
    private const string V = StoryRegistry.DefaultStoryId;

    private static readonly string[] Scenes =
    {
        StoryCutsceneIds.TowerMessage, StoryCutsceneIds.TankOpens, StoryCutsceneIds.CruiserArrives, StoryCutsceneIds.EchoSpeaks,
    };

    private static string PackDir => Path.Combine(ClientTestPaths.DataDir(), "stories", V);

    private static Dictionary<string, string> ReadTable(string path)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path),
               new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })
           ?? new Dictionary<string, string>();

    private static Dictionary<string, string> Table(string code)
    {
        var table = ReadTable(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json"));
        foreach (var kv in ReadTable(Path.Combine(PackDir, "locales", code + ".json")))
        {
            table[kv.Key] = kv.Value;
        }

        return table;
    }

    private static CutscenePlan Plan(string id, string code = "en", bool waitForReader = true)
    {
        var file = ContentLoader.ParseCutscenes(File.ReadAllText(Path.Combine(PackDir, "cutscenes.json")));
        var def = Assert.Single(file, c => c.Id == id);
        var table = Table(code);
        var text = new CutsceneTextSource(k => table.TryGetValue(k, out var v) ? v : "[" + k + "]", table.ContainsKey);
        return CutscenePlan.Build(def, V, id, null, text, waitForReader);
    }

    /// <summary>Plays a plan the way a reader does: time passes, and whenever the scene waits the reader continues.</summary>
    private static CutscenePlayback PlayThrough(CutscenePlan plan)
    {
        var play = new CutscenePlayback(plan);
        for (int i = 0; i < 4000 && !play.Done; i++)
        {
            play.Tick(0.25f);
            if (play.Waiting)
            {
                play.Continue();
            }
        }

        Assert.True(play.Done, plan.CutsceneId + " did not end");
        return play;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EveryScene_PlansFromTheDataPack_WithEveryKeyResolved_AndPlaysToItsEnd(string code)
    {
        foreach (string id in Scenes)
        {
            foreach (bool wait in new[] { true, false })
            {
                var plan = Plan(id, code, wait);
                Assert.NotEmpty(plan.Shots);
                Assert.True(plan.Skippable, id);
                Assert.All(plan.Shots.SelectMany(s => s.Captions), c =>
                {
                    Assert.False(c.Text.StartsWith('['), $"{id}: {c.Key} is missing in {code}");
                    Assert.False(c.Kicker.StartsWith('['), $"{id}: a kicker of {c.Key} is missing in {code}");
                    Assert.NotEmpty(c.Text);
                });
                var play = PlayThrough(plan);
                Assert.True(play.Elapsed < 180f, $"{id} took {play.Elapsed:0} s in {code}");
            }
        }
    }

    [Fact]
    public void TheTowerAndTheTank_AreShortStagedMoments_AtThePlaceTheServerSends()
    {
        foreach (string id in new[] { StoryCutsceneIds.TowerMessage, StoryCutsceneIds.TankOpens })
        {
            var plan = Plan(id);
            Assert.Equal(CutsceneTiers.Scene, plan.Tier);
            Assert.True(plan.TakesCamera && plan.HoldWorld && plan.LockInput, id); // a staged scene: a singleplayer world holds
            Assert.All(plan.Shots, s => Assert.Equal(CutsceneAnchors.Custom, s.Anchor));
            Assert.InRange(plan.TimedLength, 8f, 20f);
            var title = plan.Shots[0].Captions[0];
            Assert.Equal(CutsceneCaptionStyles.TitleCard, title.Style);
            Assert.False(title.PageGated); // a title runs on its own
            Assert.NotEmpty(title.Kicker);
        }

        var tower = Plan(StoryCutsceneIds.TowerMessage);
        Assert.Equal(CutsceneCameras.Orbit, tower.Shots[0].Camera);
        var console = tower.Shots[0].Captions.Where(c => c.Style == CutsceneCaptionStyles.Subtitle).ToList();
        Assert.Equal(3, console.Count); // the console's three status lines, one after the other
        Assert.All(console, c => Assert.Single(c.Pages));
        Assert.True(console[0].End <= console[1].At && console[1].End <= console[2].At, "the console's lines never overlap");
        Assert.Empty(tower.ReplaceLineKeys);

        var tank = Plan(StoryCutsceneIds.TankOpens);
        Assert.Equal(CutsceneCameras.Static, tank.Shots[0].Camera);
        var vega = Assert.Single(tank.Shots[0].Captions, c => c.Style == CutsceneCaptionStyles.Speaker);
        Assert.Equal(("story.vega.tank.opens", string.Empty), (vega.Key, vega.Speaker)); // VEGA's own box
        Assert.Equal(new[] { "story.vega.tank.opens" }, tank.ReplaceLineKeys);
    }

    [Fact]
    public void TheCruisersArrival_IsACardOnTheLiveFlight_AndAMissedOneLeavesItsTitleAsABanner()
    {
        var plan = Plan(StoryCutsceneIds.CruiserArrives);
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.False(plan.TakesCamera || plan.HoldWorld || plan.LockInput || plan.HideHud); // the pilot keeps flying
        Assert.Equal(new[] { CutsceneCameras.PlayerView }, plan.Shots.Select(s => s.Camera));
        Assert.Equal(new[] { "story.vega.cruiser.arrives" }, plan.ReplaceLineKeys);
        var line = Assert.Single(plan.Shots[0].Captions, c => c.Style == CutsceneCaptionStyles.Speaker);
        Assert.Equal("That hull… that is one of ours. That is the escort wing.", line.Text);
        Assert.Equal("Dieser Rumpf … das ist einer von unseren. Das ist die Geleitstaffel.",
            Plan(StoryCutsceneIds.CruiserArrives, "de").Shots[0].Captions.Single(c => c.Style == CutsceneCaptionStyles.Speaker).Text);

        // "Now or never": with shots already flying the card is dropped — its title still shows, as a banner.
        var banner = plan.MissedBanner();
        Assert.NotNull(banner);
        var title = Assert.Single(banner!.Shots.SelectMany(s => s.Captions));
        Assert.Equal(("Jump signature", "The Escort Cruiser"), (title.Kicker, title.Text));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void EchosGoodbye_IsACard_WithHerLinesInHerOwnBox_OneBoxALine(string code)
    {
        var plan = Plan(StoryCutsceneIds.EchoSpeaks, code);
        Assert.Equal(CutsceneTiers.Card, plan.Tier);
        Assert.False(plan.TakesCamera || plan.HoldWorld || plan.LockInput || plan.HideHud);
        Assert.All(plan.Shots, s => Assert.Equal(CutsceneCameras.PlayerView, s.Camera));

        string[] order =
        {
            "story.vega.echo.1", "story.vega.echo.2", "story.vega.echo.vega.1", "story.vega.echo.3", "story.vega.echo.4",
            "story.vega.echo.5", "story.vega.echo.vega.2",
        };
        var spoken = plan.Shots.SelectMany(s => s.Captions).Where(c => c.Style == CutsceneCaptionStyles.Speaker).ToList();
        Assert.Equal(order, spoken.Select(c => c.Key));
        Assert.Equal(order, plan.ReplaceLineKeys);
        Assert.All(spoken, c => Assert.Equal(c.Key.Contains(".vega.echo.vega.", StringComparison.Ordinal) ? string.Empty : "echo", c.Speaker));
        // Every line fits one box in English; German runs longer and may turn one page (the speaker box pages at 220).
        Assert.All(spoken, c => Assert.InRange(c.Pages.Count, 1, code == "en" ? 1 : 2));
        Assert.All(spoken, c => Assert.True(c.PageGated)); // "Story text: wait for me": the reader turns each one
        Assert.All(plan.Shots, s => Assert.Single(s.Captions)); // one box per shot — two speakers never share the screen

        var play = PlayThrough(plan);
        Assert.All(order, key => Assert.Contains(key, play.ShownKeys));
    }
}
