// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3906/#3908 — the Unity side of the other side is thin, but PR CI never compiles it. This guard reads the client
/// scripts as text (the UI wiring guards' pattern) and keeps the joints in place that connect them to the tested
/// Client.Core models: the server's state and gate list feed <see cref="OtherSideView"/>, the grade takes its level, the
/// player rig asks it before it starts what the server refuses, the Resonance Blaster's keys go through
/// <see cref="SoundShaper"/> and <see cref="SoundAim"/>, the melody bar is the palette, sound blocks are no lights — and
/// the two transition sounds are played behind <c>Has</c>, so a missing file is silence.
/// </summary>
public sealed class OtherSideWiringTests
{
    private static string Script(string name)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", name));

    [Fact]
    public void TheServersWord_FeedsTheOneModel()
    {
        string boot = Script("GameBootstrap.OtherSide.cs");
        Assert.Contains("public OtherSideView OtherSide { get; } = new OtherSideView();", boot);
        Assert.Contains("Network.OtherSideStateReceived += OnOtherSideState;", boot);
        Assert.Contains("Network.OtherSideGatesReceived += m => OtherSide.Gates.Apply(m);", boot);
        Assert.Contains("Network.JoinAccepted += _ => ForgetOtherSide();", boot);
        Assert.Contains("SoundShaper.Hold();", boot); // a refused pulse rests the trigger, like the server
        Assert.Contains("World?.SetGlowLookBlocks(", boot); // sound blocks glow, they are no light sources
        Assert.Contains("RegisterSoundBlockLook();", Script("GameBootstrap.Environment.cs"));
        Assert.Contains("WireOtherSide();", Script("GameBootstrap.Knowledge.cs"));
        Assert.Contains("Network.ShowsOtherSide = true;", Script("GameBootstrap.cs")); // the capability word otherside1
        Assert.Contains("SoundPalette.Played(tone);", Script("GameBootstrap.Melody.cs")); // the tone played last is the palette
    }

    [Fact]
    public void TheLook_IsOneLevelOfTheGrade_AndTheStepThroughAGateIsStaged()
    {
        string fx = Script("OtherSideFx.cs");
        Assert.Contains("view.Tick(Time.deltaTime);", fx);
        Assert.Contains("UrpScenePost.Instance?.SetOtherSide(look);", fx);
        Assert.Contains("UrpScenePost.Instance?.PulseOtherSide(1f);", fx);
        Assert.Contains("audio != null && audio.Has(id)", fx); // a missing sound file is silence
        Assert.Contains("OtherSideLook.PillarStrength(look, gate.Entry, now, UiKit.ReducedMotion)", fx);
        Assert.Contains("Game.Wears(SoundersIds.ObsidianChest) && Game.Wears(SoundersIds.DiscCrossing)", fx); // the door on our side
        Assert.DoesNotContain("AddComponent<Light>", fx); // lines only, no real lights
        Assert.DoesNotContain("new Material(", fx);       // the existing beam material, no shader of its own

        string post = Script("UrpScenePost.cs");
        Assert.Contains("public void SetOtherSide(float level01)", post);
        Assert.Contains("OtherSideLook.Saturation(", post);
        Assert.Contains("OtherSideLook.Contrast(", post);
        Assert.Contains("* (1f - _otherSide)", post); // the filter, the warmth and the mood LUT fade out
        Assert.Contains("_lut.contribution.Override(MoodLutContribution());", post);
        Assert.Contains("public void SetFlashback(float level01)", post); // the flashback's grade stays as it was
    }

    [Fact]
    public void TheRig_DoesNotStartWhatTheServerRefuses_AndTheBlasterShapes()
    {
        string interact = Script("PlayerController.Interact.cs");
        Assert.Contains("if (OtherSideOwnsHands())", interact);
        Assert.Contains("NearContainer => Game != null && !OverThere", interact);
        Assert.Contains("if (Game == null || OverThere)", interact);

        string rig = Script("PlayerController.OtherSide.cs");
        Assert.Contains("private bool OverThere => Game != null && Game.OtherSide.Over;", rig);
        Assert.Contains("Game.SoundShaper.Tick(Time.deltaTime, aimed, InputMap.SecondaryHeld(), InputMap.PrimaryHeld(), onSound)", rig);
        Assert.Contains("action == SoundShapeAction.Dissolve);", rig); // the mine key's pulse carries Alt
        Assert.Contains("SoundAim.Resolve(_soundSolid,", rig);
        Assert.Contains("Game.OtherSide.FilterInteraction(_facts, rune, aimedLivesThere, nearLivesThere);", rig);
        Assert.Contains("Game.SoundSteps.Step(Time.deltaTime, grounded, x, y, z, sound, glow, out int tone)", rig);

        Assert.Contains("FilterInteractionOverThere();", Script("PlayerController.InteractTarget.cs"));
        Assert.Contains("QuietAimStateOverThere();", Script("PlayerController.Stations.cs"));
        Assert.Contains("return OtherSideOutlineCell(out cell);", Script("PlayerController.Aim.cs")); // the outline shows the cell to shape
        Assert.Contains("Game != null && !OverThere ? Game.ItemInSlot(Game.SelectedHotbarSlot) : null;", Script("PlayerController.Placement.cs")); // no ghost
        Assert.Contains("&& !OverThere;", Script("PlayerController.BeamWeapon.cs"));
        Assert.Contains("if (!CanLiftBlock || OverThere)", Script("PlayerController.Carry.cs"));
        Assert.Contains("OtherSideSteps(grounded && !inWater);", Script("PlayerController.Audio.cs"));
        Assert.Contains("if (!OverThere) // #3906", Script("PlayerController.cs")); // no attack swing
    }

    [Fact]
    public void TheHud_MarksTheOtherSide_ThePalette_AndKeepsTheNeedsQuiet()
    {
        string hud = Script("HudUi.cs");
        Assert.Contains("BuildOtherSideChip(root);", hud);
        Assert.Contains("UpdateOtherSideChip(show);", hud);
        Assert.Contains("RefreshCompassGate(radius);", Script("HudUi.Compass.cs"));

        string part = Script("HudUi.OtherSide.cs");
        Assert.Contains("loc.Get(\"ui.otherside.chip\")", part);
        Assert.Contains("CompassLabel(\"ui.otherside.gate_marker\", \"Your rune gate\")", part);
        Assert.Contains("loc.Get(\"ui.otherside.bar_hint\")", part);
        Assert.Contains("loc.Get(\"ui.otherside.count\")", part);
        Assert.Contains("Game.OtherSide.Over && bar.FreePlay ? Game.SoundPalette.Tone : -1", part);

        string bar = Script("HudUi.MelodyBar.cs");
        Assert.Contains("int next = SoundPaletteTone(bar);", bar);
        Assert.Contains("next >= 0 ? PaletteToneColor(i, on) : ToneColor(i, on || i == glow ? 1f : 0.62f)", bar); // the bar's own colours elsewhere

        Assert.Contains("Game.OtherSide.ShowsNeedWarnings", Script("HudUi.Vitals.cs"));
        Assert.Contains("Game.OtherSide.ShowsNeedWarnings", Script("HudUi.Damage.cs"));
    }

    [Fact]
    public void TheTextsExist_InEnglishAndGerman_WithTheRealKeyTokens()
    {
        foreach (string code in new[] { "en", "de" })
        {
            string file = Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json");
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(file, System.Text.Encoding.UTF8));
            foreach (string key in new[] { "ui.otherside.chip", "ui.otherside.gate_marker", "ui.otherside.bar_hint", "ui.otherside.count" })
            {
                Assert.True(table.ContainsKey(key), $"{code}: {key} is missing");
            }

            Assert.Contains("{key:Place}", table["ui.otherside.bar_hint"]); // the use key shapes
            Assert.Contains("{key:Mine}", table["ui.otherside.bar_hint"]);  // the mine key dissolves
            Assert.Contains("{0}", table["ui.otherside.count"]);
        }
    }
}
