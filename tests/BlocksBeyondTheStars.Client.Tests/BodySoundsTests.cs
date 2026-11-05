// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3240: the light body layer (maintainer decision: only light — never scary, never constant). An exhale after a long run,
/// a long jetpack burn or a hard landing, at most every 8 s; quicker soft breaths on low oxygen; a soft heartbeat that fades
/// in on low health; all of it off while dead, respawning, in a menu or a cutscene.
/// </summary>
public sealed class BodySoundsTests
{
    private const float Frame = 1f / 60f;

    private static BodyInput Calm(bool active = true, float health = 1f, float oxygen = 100f, bool headUnder = false,
        bool running = false, bool jetpacking = false, bool hardLanding = false)
        => new(Frame, active, health, oxygen, headUnder, running, jetpacking, hardLanding);

    /// <summary>Runs the layer for <paramref name="seconds"/> with one input; returns the cues with their times.</summary>
    private static List<(float At, BodyCue Cue)> Run(BodySounds body, float seconds, BodyInput input, float start = 0f)
    {
        var cues = new List<(float, BodyCue)>();
        for (float t = 0f; t < seconds; t += Frame)
        {
            var cue = body.Tick(input);
            if (!cue.IsNone)
            {
                cues.Add((start + t, cue));
            }
        }

        return cues;
    }

    [Fact]
    public void AHealthyCalmBody_MakesNoSound()
    {
        var body = new BodySounds(new Random(1));
        Assert.Empty(Run(body, 30f, Calm()));
    }

    [Fact]
    public void ALongRun_EndsWithOneExhale_AShortOneWithNone()
    {
        var body = new BodySounds(new Random(2));
        Assert.Empty(Run(body, BodySounds.RunSeconds - 1f, Calm(running: true)));
        Assert.Empty(Run(body, 3f, Calm())); // a short run: no breath

        Assert.Empty(Run(body, BodySounds.RunSeconds + 1f, Calm(running: true))); // never during the run
        var after = Run(body, 3f, Calm());
        var exhale = Assert.Single(after);
        Assert.StartsWith("breath_exhale", exhale.Cue.Id);
        Assert.InRange(exhale.At, BodySounds.ExhaleDelay - (3 * Frame), BodySounds.ExhaleDelay + (2 * Frame));
        Assert.Equal(BodySounds.ExhaleLevel, exhale.Cue.Volume, 4);
    }

    [Fact]
    public void ALongBurn_AndAHardLanding_EachExhale_ButNeverTwiceWithinTheGap()
    {
        var body = new BodySounds(new Random(3));
        Run(body, BodySounds.BurnSeconds + 0.5f, Calm(jetpacking: true));
        Assert.Single(Run(body, 2f, Calm())); // the burn's exhale

        body.Tick(Calm(hardLanding: true)); // a hard landing 2 s later: inside the 8 s gap
        Assert.Empty(Run(body, 3f, Calm()));

        Run(body, BodySounds.ExhaleGapSeconds, Calm()); // the gap runs out
        body.Tick(Calm(hardLanding: true));
        var landing = Assert.Single(Run(body, 3f, Calm()));
        Assert.InRange(landing.At, BodySounds.LandingExhaleDelay - (3 * Frame), BodySounds.LandingExhaleDelay + (2 * Frame));
    }

    [Fact]
    public void TheExhales_AlternateTheirTwoTakes()
    {
        var body = new BodySounds(new Random(4));
        var ids = new List<string?>();
        for (int i = 0; i < 4; i++)
        {
            body.Tick(Calm(hardLanding: true));
            foreach (var (_, cue) in Run(body, BodySounds.ExhaleGapSeconds + 1f, Calm()))
            {
                ids.Add(cue.Id);
            }
        }

        Assert.Equal(new string?[] { "breath_exhale", "breath_exhale_2", "breath_exhale", "breath_exhale_2" }, ids);
    }

    [Fact]
    public void NoBreath_UnderWater()
    {
        var body = new BodySounds(new Random(5));
        body.Tick(Calm(hardLanding: true, headUnder: true));
        Assert.Empty(Run(body, 3f, Calm(headUnder: true)));
        Assert.Empty(Run(body, 20f, Calm(oxygen: 10f, headUnder: true)));
    }

    [Fact]
    public void LowOxygen_BreathesQuickerEveryFiveToSevenSeconds()
    {
        var body = new BodySounds(new Random(6));
        Assert.Empty(Run(body, 10f, Calm(oxygen: BodySounds.LowOxygen + 1f)));

        var breaths = Run(body, 40f, Calm(oxygen: 12f));
        Assert.All(breaths, b => Assert.Equal("breath_quick", b.Cue.Id));
        Assert.InRange(breaths[0].At, BodySounds.QuickBreathFirst - (3 * Frame), BodySounds.QuickBreathFirst + (2 * Frame));
        for (int i = 1; i < breaths.Count; i++)
        {
            Assert.InRange(breaths[i].At - breaths[i - 1].At, BodySounds.QuickBreathMin - (2 * Frame), BodySounds.QuickBreathMax + (2 * Frame));
        }

        Assert.InRange(breaths.Count, 6, 8);
    }

    [Fact]
    public void LowHealth_ASoftSlowHeartbeat_FadesIn_AndOutAgain()
    {
        var body = new BodySounds(new Random(7));
        Assert.Empty(Run(body, 5f, Calm(health: BodySounds.LowHealth + 0.01f)));

        var beats = Run(body, 12f, Calm(health: 0.1f));
        Assert.All(beats, b => Assert.Equal("heartbeat_soft", b.Cue.Id));
        for (int i = 1; i < beats.Count; i++)
        {
            Assert.InRange(beats[i].At - beats[i - 1].At, BodySounds.HeartbeatMin - (2 * Frame), BodySounds.HeartbeatMax + (2 * Frame));
            Assert.True(beats[i].Cue.Volume >= beats[i - 1].Cue.Volume - 1e-4f, "the beat fades in, never jumps back");
        }

        Assert.True(beats[0].Cue.Volume < 0.1f * BodySounds.HeartbeatLevel, "the first beat is barely there");
        Assert.Equal(BodySounds.HeartbeatLevel, beats[^1].Cue.Volume, 3);

        var healed = Run(body, 5f, Calm(health: 0.5f));
        Assert.True(healed.Count <= 2, $"healed, the heart fades out within a beat or two ({healed.Count})");
        Assert.Equal(0f, body.HeartFade);
    }

    [Fact]
    public void DeadMenusAndCutscenes_StopEverythingAtOnce()
    {
        var body = new BodySounds(new Random(8));
        Run(body, 6f, Calm(health: 0.1f, oxygen: 5f));
        Run(body, BodySounds.RunSeconds + 1f, Calm(health: 0.1f, oxygen: 5f, running: true));

        Assert.Empty(Run(body, 20f, Calm(active: false, health: 0.1f, oxygen: 5f)));
        Assert.Equal(0f, body.HeartFade);

        // Back in play: no exhale left over from the run, the heart fades in afresh.
        var back = Run(body, 1f, Calm(health: 0.1f));
        Assert.All(back, b => Assert.Equal("heartbeat_soft", b.Cue.Id));
    }

    /// <summary>The Unity half, guarded as source text: the layer runs every frame from LateUpdate (so it also plays in the
    /// suit on a spacewalk), and is off while dead, respawning, in a menu or a cutscene.</summary>
    [Fact]
    public void ThePlayer_FeedsTheLayer_AndSilencesItOutOfPlay()
    {
        string player = ClientTestPaths.PlayerControllerSources();
        Assert.Contains("LateAudio();", player);
        Assert.Contains("BodyLayer.Tick(", player);
        foreach (string gate in new[] { "Game.Health > 0f", "!Game.AwaitingRespawnConfirm", "!Game.MenuOpen", "!GameBootstrap.CutsceneActive", "!Game.CinematicCameraActive" })
        {
            Assert.Contains(gate, Body(player, "private void TickBodyLayer()"));
        }
    }

    /// <summary>#3240: the third-person ears — WorldRig puts the scene's one AudioListener on a child of the camera and hands
    /// it to the player, who moves it to the head in third person and back onto the camera in the flight view and cinematics.</summary>
    [Fact]
    public void TheListener_IsOneChildOfTheCamera_AtTheHeadInThirdPerson()
    {
        string rig = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "WorldRig.cs"));
        Assert.Equal(1, rig.Split("AddComponent<AudioListener>()").Length - 1); // exactly one listener in the world rig
        Assert.Contains("ears.AddComponent<AudioListener>()", rig);
        Assert.Contains("ears.transform.SetParent(camGo.transform, false);", rig);
        Assert.Contains("pc.Ears = ears.transform;", rig);

        string ears = Body(ClientTestPaths.PlayerControllerSources(), "private void UpdateEars()");
        Assert.Contains("ThirdPerson", ears);
        Assert.Contains("!Game.SpaceViewActive", ears);
        Assert.Contains("!Game.CinematicCameraActive", ears);
        Assert.Contains("!GameBootstrap.CutsceneActive", ears);
        Assert.Contains("Ears.localPosition = Vector3.zero;", ears);
    }

    [Fact]
    public void EveryBodyClip_IsBundled()
    {
        string audio = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        foreach (string id in new[]
                 {
                     "breath_exhale", "breath_exhale_2", "breath_quick", "heartbeat_soft", "water_splash_in", "water_splash_out",
                     "swim_stroke", "swim_stroke_2", "underwater_bubbles", "surface_gasp", "suit_thruster", "suit_thruster_2",
                 })
        {
            Assert.True(File.Exists(Path.Combine(audio, id + ".mp3")), $"{id}.mp3 is not bundled");
        }
    }

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"'{signature}' not found");
        int end = src.IndexOf("/// <summary>", start, StringComparison.Ordinal);
        return end < 0 ? src.Substring(start) : src.Substring(start, end - start);
    }
}
