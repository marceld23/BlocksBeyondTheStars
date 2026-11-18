// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3793: with "Crouch: press once instead of holding" the crouch key switches sneaking — but only where sneaking can
/// start. In the air, in water and on a wall the same key means "down", and a press there used to flip the switch all
/// the same: the figure landed crouched (or stood up) without having been asked. The rule (<see cref="CrouchSwitch"/>)
/// and its wiring in the Unity scripts (as source text — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class CrouchSwitchTests
{
    private static string Script(params string[] path)
        => File.ReadAllText(Path.Combine(new[] { ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts" }.Concat(path).ToArray()));

    [Fact]
    public void OnTheGround_APressSwitchesSneakingOnAndOff()
    {
        bool latched = CrouchSwitch.Next(latched: false, pressed: true, jumped: false, canSneak: true);
        Assert.True(latched);
        latched = CrouchSwitch.Next(latched, pressed: false, jumped: false, canSneak: true);
        Assert.True(latched); // it stays on without the key
        latched = CrouchSwitch.Next(latched, pressed: true, jumped: false, canSneak: true);
        Assert.False(latched);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OffTheGround_APressIsDown_AndLeavesTheSwitchAsItIs(bool latched)
    {
        // In the air (creative flight, the jetpack, a fall), in water, on a ladder or a wall.
        Assert.Equal(latched, CrouchSwitch.Next(latched, pressed: true, jumped: false, canSneak: false));
        Assert.Equal(latched, CrouchSwitch.Next(latched, pressed: false, jumped: false, canSneak: false));
    }

    [Fact]
    public void AJump_AlwaysStandsTheBodyUp()
    {
        Assert.False(CrouchSwitch.Next(latched: true, pressed: false, jumped: true, canSneak: true));
        Assert.False(CrouchSwitch.Next(latched: true, pressed: false, jumped: true, canSneak: false));
        Assert.False(CrouchSwitch.Next(latched: true, pressed: true, jumped: true, canSneak: true)); // both in one frame: up
    }

    [Fact]
    public void SneakingOffALedge_AndPressingDownInTheAir_StillSneaksAfterTheLanding()
    {
        bool latched = CrouchSwitch.Next(false, pressed: true, jumped: false, canSneak: true);  // sneaking
        latched = CrouchSwitch.Next(latched, pressed: false, jumped: false, canSneak: false);   // walked off the edge
        latched = CrouchSwitch.Next(latched, pressed: true, jumped: false, canSneak: false);    // "down" in the air
        latched = CrouchSwitch.Next(latched, pressed: false, jumped: false, canSneak: true);    // landed
        Assert.True(latched);
    }

    [Fact]
    public void TheScripts_AskTheRule_AndSayWhereAPressCounts()
    {
        string map = Script("InputMap.cs");
        Assert.Contains("_crouchLatched = CrouchSwitch.Next(_crouchLatched, pressed, JumpDown(), CrouchSwitchArmed);", map);
        Assert.DoesNotContain("_crouchLatched = !_crouchLatched;", map); // no flip outside the rule
        Assert.Contains("public static bool CrouchSwitchArmed = true;", map);

        string move = Script("PlayerController.Movement.cs");
        Assert.Contains("InputMap.CrouchSwitchArmed = grounded && !inWater && !onLadder;", move);
        Assert.True(
            move.IndexOf("InputMap.CrouchSwitchArmed = grounded", StringComparison.Ordinal)
            < move.IndexOf("bool want = InputMap.CrouchHeld()", StringComparison.Ordinal),
            "armed before the crouch is read — the same frame's press must see it");

        // "Down" reads the key as held, never through the switch (v2026.11.22) — still so.
        Assert.Contains("public static bool DescendHeld() => _desktop.DescendHeld() || _pad.DescendHeld() || _touch.DescendHeld();", map);
    }

    [Fact]
    public void TheSettingsText_SaysWhereTheSwitchWorks_InEnglishAndGerman()
    {
        var en = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", "en.json")));
        var de = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", "de.json")));
        Assert.Contains("on the ground", en["ui.settings.crouch_toggle_info"]);
        Assert.Contains("means down", en["ui.settings.crouch_toggle_info"]);
        Assert.Contains("auf dem Boden", de["ui.settings.crouch_toggle_info"]);
    }
}
