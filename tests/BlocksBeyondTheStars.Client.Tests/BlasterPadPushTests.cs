// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3917 — the Resonance Blaster's push on a gamepad on our side, while the open melody bar holds the shoulder buttons:
/// a short tap of the left shoulder alone or a pull of the left trigger is one push; a longer hold and a chord with the
/// d-pad never push; a press made before the reading started never fires. And the rig reads it every frame and feeds
/// it into the place click, where the gadget path sends the push.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class BlasterPadPushTests
{
    private const float Frame = 1f / 60f;

    /// <summary>Holds the left shoulder for <paramref name="seconds"/>, then lets go; returns how many pushes came out.</summary>
    private static int Press(BlasterPadPush pad, float seconds, bool dpadFrom = false, float dpadAfter = -1f)
    {
        int pushes = 0;
        for (float t = 0f; t < seconds; t += Frame)
        {
            bool dpad = dpadFrom || (dpadAfter >= 0f && t >= dpadAfter);
            pad.Update(Frame, leftShoulder: true, leftTrigger: false, dpadPushed: dpad);
            pushes += pad.Pushed ? 1 : 0;
        }

        pad.Update(Frame, leftShoulder: false, leftTrigger: false, dpadPushed: false);
        pushes += pad.Pushed ? 1 : 0;
        pad.Update(Frame, false, false, false);
        return pushes + (pad.Pushed ? 1 : 0);
    }

    [Fact]
    public void AShortTapOfTheLeftShoulder_IsOnePush_AsItIsLetGo()
    {
        var pad = new BlasterPadPush();
        pad.Update(Frame, leftShoulder: true, leftTrigger: false, dpadPushed: false);
        Assert.False(pad.Pushed); // nothing while it is down — it might still become a chord
        pad.Update(Frame, leftShoulder: false, leftTrigger: false, dpadPushed: false);
        Assert.True(pad.Pushed);
        pad.Update(Frame, false, false, false);
        Assert.False(pad.Pushed); // one push, one frame

        Assert.Equal(1, Press(new BlasterPadPush(), 0.1f));
    }

    [Fact]
    public void ALongerHold_NeverPushes_WithOrWithoutTheDpad()
    {
        Assert.Equal(0, Press(new BlasterPadPush(), BlasterPadPush.TapSeconds + 0.1f));
        Assert.Equal(0, Press(new BlasterPadPush(), 2f));

        // Held first, then the d-pad: the bar's upper tones — this is the case that must never push at a rune stone.
        Assert.Equal(0, Press(new BlasterPadPush(), 1f, dpadAfter: 0.4f));
    }

    [Fact]
    public void AChordWithTheDpad_NeverPushes_EvenAShortOne()
    {
        Assert.Equal(0, Press(new BlasterPadPush(), 0.1f, dpadFrom: true));        // pressed onto a pushed d-pad
        Assert.Equal(0, Press(new BlasterPadPush(), 0.15f, dpadAfter: 0.05f));     // the d-pad came a moment later
    }

    [Fact]
    public void TheLeftTrigger_PushesOnceAtThePull()
    {
        var pad = new BlasterPadPush();
        pad.Update(Frame, leftShoulder: false, leftTrigger: true, dpadPushed: false);
        Assert.True(pad.Pushed);
        int more = 0;
        for (int i = 0; i < 60; i++)
        {
            pad.Update(Frame, false, true, true); // held, even with the d-pad: a trigger is never the bar's modifier
            more += pad.Pushed ? 1 : 0;
        }

        Assert.Equal(0, more);
        pad.Update(Frame, false, false, false);
        Assert.False(pad.Pushed);
        pad.Update(Frame, false, true, false);
        Assert.True(pad.Pushed); // the next pull is the next push
    }

    [Fact]
    public void APressMadeBeforeTheReadingStarted_NeverFires()
    {
        var pad = new BlasterPadPush();
        pad.Reset(); // e.g. the blaster was just taken in hand while LB and LT were already down
        pad.Update(Frame, leftShoulder: true, leftTrigger: true, dpadPushed: false);
        Assert.False(pad.Pushed);
        pad.Update(Frame, leftShoulder: false, leftTrigger: true, dpadPushed: false);
        Assert.False(pad.Pushed); // the stale shoulder was let go: no tap
        pad.Update(Frame, false, false, false);
        Assert.False(pad.Pushed);

        Assert.Equal(1, Press(pad, 0.1f)); // a fresh tap works again
        pad.Update(Frame, false, true, false);
        Assert.True(pad.Pushed);
    }

    [Fact]
    public void TheRig_ReadsThePushEveryFrame_AndFeedsThePlaceClick()
    {
        string interact = System.IO.File.ReadAllText(System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets",
            "BlocksBeyondTheStars", "Scripts", "PlayerController.Interact.cs"));

        // Read before HandleInteract's first return, so the tap's release frame and the trigger's edge are never missed.
        int method = interact.IndexOf("private void HandleInteract()", System.StringComparison.Ordinal);
        int read = interact.IndexOf("bool padPush = BlasterPadPushThisFrame();", method, System.StringComparison.Ordinal);
        int firstReturn = interact.IndexOf("return;", method, System.StringComparison.Ordinal);
        Assert.True(method > 0 && read > method && read < firstReturn, "the pad push must be read before HandleInteract can return");
        Assert.Contains("bool place = InputMap.SecondaryDown() || padPush;", interact);

        // Only on our side, only with the bar holding the shoulders and the blaster in hand; a skipped frame starts over.
        Assert.Contains("if (!continuous || OverThere || !InputMap.MelodyBarCapture || !HoldsResonanceBlaster())", interact);
        Assert.Contains("_blasterPadPush.Update(Time.deltaTime, InputMap.PadHeld(PadButton.Lb), InputMap.PadTriggerPlaceHeld(), dpad);", interact);
    }
}
