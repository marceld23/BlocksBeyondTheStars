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
        Assert.Contains("Network.OtherSideGatesReceived += m => OtherSide.ApplyGates(m);", boot); // a return's empty list lets the lights fade
        Assert.Contains("Network.JoinAccepted += _ => ForgetOtherSide();", boot);
        Assert.Contains("SoundShaper.Hold();", boot); // a refused pulse rests the trigger, like the server
        Assert.Contains("World?.SetGlowLookBlocks(", boot); // sound blocks glow, they are no light sources
        Assert.Contains("RegisterSoundBlockLook();", Script("GameBootstrap.Environment.cs"));
        Assert.Contains("WireOtherSide();", Script("GameBootstrap.Knowledge.cs"));
        Assert.Contains("Network.ShowsOtherSide = true;", Script("GameBootstrap.cs")); // the capability word otherside1
        string melody = Script("GameBootstrap.Melody.cs");
        Assert.Contains("SoundPalette.Played(tone, Time.unscaledTimeAsDouble);", melody); // the tone played last is the palette …
        Assert.Contains("SoundPalette.Played(-1, Time.unscaledTimeAsDouble);", melody);   // … within the server's note budget, which the leave takes from too
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
        Assert.Contains("ReadSoundHands(out bool shape, out bool dissolve);", rig);
        Assert.Contains("Game.SoundShaper.Tick(Time.deltaTime, aimed, shape, dissolve, onSound)", rig);
        Assert.Contains("shape = InputMap.SecondaryHeld();", rig);  // the place key shapes (mouse, touch, a pad whose bar is closed)
        Assert.Contains("dissolve = InputMap.PrimaryHeld();", rig); // the mine key dissolves

        // A gamepad under the open melody bar: InputMap drops the pad's place and mine there (the shoulders are the bar's
        // modifier), so the rig reads the pad itself — a shoulder alone is the hand, with the d-pad the modifier, the
        // triggers (where switched on) always the hand. Without this nothing could be shaped with a pad.
        Assert.Contains("if (!InputMap.MelodyBarCapture)", rig);
        Assert.Contains("_padHands.Update(Time.deltaTime, InputMap.PadHeld(PadButton.Lb), InputMap.PadHeld(PadButton.Rb),", rig);
        Assert.Contains("InputMap.PadTriggerPlaceHeld(), InputMap.PadTriggerMineHeld(), dpad);", rig);
        Assert.Contains("shape |= _padHands.Shape;", rig);
        Assert.Contains("dissolve |= _padHands.Dissolve;", rig);
        string map = Script("InputMap.cs");
        Assert.Contains("public static bool PadTriggerMineHeld() => _pad.TriggerMineHeld();", map);
        Assert.Contains("public static bool PadTriggerPlaceHeld() => _pad.TriggerPlaceHeld();", map);
        Assert.Contains("(!MelodyBarCapture && _pad.SecondaryHeld())", map); // everywhere else the bar keeps the pad's hands quiet, as before
        string pad = Script(Path.Combine("Input", "GamepadInputSource.cs"));
        Assert.Contains("public bool TriggerMineHeld() => TriggerHeld(TriggerMine);", pad);   // RT, only while switched on
        Assert.Contains("public bool TriggerPlaceHeld() => TriggerHeld(TriggerPlace);", pad); // LT
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
        Assert.Contains("|| !stamp.Equals(_otherSideHintStamp))", part); // a rebind, the glyph set or the triggers switch renames the keys

        // The bottom controls line: over there the full line (mine, place, attack, loot …) gives way to the other side's.
        Assert.Contains("_hint.text = ControlsLine(loc, hintMode, shortHint);", hud);
        string line = Script("HudUi.HintLine.cs");
        Assert.Contains("if (overThere && !shortHint && HoldsSoundBlaster())", line);
        Assert.Contains("return OtherSideBarHint(loc, device);", line);
        Assert.Contains("if (shortHint || overThere)", line);

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
            foreach (string key in new[] { "ui.otherside.chip", "ui.otherside.gate_marker", "ui.otherside.bar_hint", "ui.otherside.count", "ui.otherside.bare_hint" })
            {
                Assert.True(table.ContainsKey(key), $"{code}: {key} is missing");
            }

            Assert.Contains("{key:Place}", table["ui.otherside.bar_hint"]); // the use key shapes
            Assert.Contains("{key:Mine}", table["ui.otherside.bar_hint"]);  // the mine key dissolves
            Assert.Contains("{key:Mine}", table["ui.otherside.bare_hint"]); // #3914: bare hands — the mine key, held
            Assert.Contains("{0}", table["ui.otherside.count"]);
        }
    }

    /// <summary>#3914: without the blaster over there the mine key works a sound block that touches the player — sent as
    /// mine hits at a hand's pace, only for a block in the player's own body box (the shared rule the server applies), the
    /// block outlined so the server's cracks show on it, and the controls line naming the key meanwhile.</summary>
    [Fact]
    public void BareHands_WorkASoundBlockThatTouchesThePlayer_WithTheServersRule()
    {
        string rig = Script("PlayerController.OtherSide.cs");
        Assert.Contains("if (!HoldsResonanceBlaster())\n            {\n                BareHandsOnSound();", rig);
        Assert.Contains("OtherSideRules.InBodyBox(feet, new Vector3i(aim.HitX, aim.HitY, aim.HitZ), OtherSideRules.BodyMargin, Game.Circumference)", rig);
        Assert.Contains("Game.Network.SendMine(cell.x, cell.y, cell.z);", rig);
        Assert.Contains("return BareHandSoundTarget(out cell);", rig); // the outline: where the cracks show
        Assert.Contains("Game.BareHandSoundFrame = Time.frameCount;", rig);

        string hint = Script("HudUi.HintLine.cs");
        Assert.Contains("if (overThere && Game.BareHandSoundAimed)", hint);
        Assert.Contains("loc.Get(\"ui.otherside.bare_hint\")", Script("HudUi.OtherSide.cs"));
    }

    /// <summary>#4024: over there our side's beings look through the player — the animals' gaze and a hostile's bite cue,
    /// the people's head turn and a Sounder's gaze all ask <see cref="OtherSideView.MayReactToPlayer"/>, so only the Qualis
    /// of the other side and the Echoes still turn to them.</summary>
    [Fact]
    public void OverThere_OurBeings_DoNotTurnToThePlayer_NorBite()
    {
        string creatures = Script("CreatureView.cs");
        Assert.Contains("toPlayer.sqrMagnitude < 144f && Game.OtherSide.MayCreatureReact(c.SpeciesId)", creatures);
        Assert.Contains("&& attackNow && Game.OtherSide.MayCreatureReact(c.SpeciesId))", creatures);

        string npcs = Script("NpcView.cs");
        Assert.Contains("if (Game != null && n.Pose != 2 && Game.OtherSide.MayReactToPlayer(n.LivesOverThere))", npcs);
        string sounders = Script("NpcView.Sounders.cs");
        Assert.Contains("n.LivesOverThere = OtherSideView.NpcLivesOverThere(nd.Look);", sounders);
        Assert.Contains("&& Game.OtherSide.MayReactToPlayer(n.LivesOverThere)", sounders);
    }
}
