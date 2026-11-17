// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Client.Feel;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests.Feel;

/// <summary>
/// The interface rules of the feel pass: a piece of suit gear compared with the piece it would replace, and the
/// pad's button names chosen by the pad's own name.
/// </summary>
public sealed class UiFeelTests
{
    private static ItemDefinition Gear(string key, string slot, float armor = 0f, float warmth = 0f, float oxygen = 0f)
        => new() { Key = key, EquipSlot = slot, ArmorResistance = armor, ThermalInsulation = warmth, OxygenBonus = oxygen };

    [Fact]
    public void APiece_ReplacesAnotherPieceOfItsBodySlot_NeverAModule()
    {
        var helmet = Gear("helmet", "head");
        var visor = Gear("visor", "head");
        var boots = Gear("boots", "feet");
        var lamp = Gear("lamp", "module");
        var radio = Gear("radio", "module");

        Assert.True(GearCompare.Replaces(visor, helmet));
        Assert.False(GearCompare.Replaces(helmet, helmet)); // the same piece is no replacement
        Assert.False(GearCompare.Replaces(boots, helmet));  // another slot
        Assert.False(GearCompare.Replaces(radio, lamp));    // modules sit side by side
        Assert.False(GearCompare.Replaces(visor, null));
        Assert.False(GearCompare.Replaces(Gear("stone", string.Empty), helmet));
    }

    [Fact]
    public void TheComparison_ListsEveryEffectEitherPieceHas_WithTheDifference()
    {
        var worn = Gear("old", "chest", armor: 0.10f, warmth: 0.20f);
        var candidate = Gear("new", "chest", armor: 0.25f, oxygen: 30f);

        var lines = GearCompare.Lines(candidate, worn);

        Assert.Equal(new[] { "ui.equip.effect.armor", "ui.equip.effect.warmth", "ui.equip.effect.oxygen" }, lines.Select(l => l.EffectKey));
        Assert.Equal((25, 15), (lines[0].Value, lines[0].Delta));   // better armour
        Assert.Equal((0, -20), (lines[1].Value, lines[1].Delta));   // the warmth the old piece had is lost
        Assert.Equal((30, 30), (lines[2].Value, lines[2].Delta));   // oxygen is a plain amount
    }

    [Fact]
    public void TwoPiecesWithoutNumbers_CompareToNothing()
    {
        Assert.Empty(GearCompare.Lines(Gear("a", "back"), Gear("b", "back")));
    }

    [Theory]
    [InlineData("Controller (Xbox One For Windows)", PadGlyphSet.Xbox)]
    [InlineData("XInput Controller", PadGlyphSet.Xbox)]
    [InlineData("Wireless Controller", PadGlyphSet.PlayStation)]
    [InlineData("DualSense Wireless Controller", PadGlyphSet.PlayStation)]
    [InlineData("Sony Interactive Entertainment Wireless Controller", PadGlyphSet.PlayStation)]
    [InlineData("Nintendo Switch Pro Controller", PadGlyphSet.Nintendo)]
    [InlineData("Pro Controller", PadGlyphSet.Nintendo)]
    public void AKnownPad_GetsItsOwnButtonNames(string name, PadGlyphSet expected)
        => Assert.Equal(expected, PadGlyphTable.Detect(name));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Generic USB Joystick")]
    public void AnUnknownPad_KeepsThePlayersChoice(string? name)
        => Assert.Null(PadGlyphTable.Detect(name));

    // ---------------- EmoteCurves ----------------

    [Fact]
    public void AGesture_StartsAndEndsAtRest_AndHoldsInBetween()
    {
        Assert.Equal(0f, EmoteCurves.Envelope(0f));
        Assert.Equal(0f, EmoteCurves.Envelope(1f));
        Assert.Equal(0f, EmoteCurves.Envelope(-0.5f));
        Assert.Equal(0f, EmoteCurves.Envelope(1.5f));
        Assert.Equal(1f, EmoteCurves.Envelope(0.5f));

        // Eased in and out: rising through the attack, falling through the release, never above 1.
        float previous = 0f;
        for (float p = 0.01f; p < 0.2f; p += 0.01f)
        {
            float k = EmoteCurves.Envelope(p);
            Assert.InRange(k, previous, 1f);
            previous = k;
        }

        Assert.True(EmoteCurves.Envelope(0.9f) < EmoteCurves.Envelope(0.8f));
    }

    [Fact]
    public void EveryTimedEmote_HasItsLength_SitAndTheGreetingsHaveNone()
    {
        foreach (byte emote in new[] { PlayerEmotes.Point, PlayerEmotes.HeadShake, PlayerEmotes.Dance, PlayerEmotes.Clap, PlayerEmotes.Bow, PlayerEmotes.Hurt })
        {
            Assert.True(EmoteCurves.Seconds(emote) > 0.2f, $"emote {emote} has no length");
        }

        Assert.Equal(0f, EmoteCurves.Seconds(PlayerEmotes.Sit)); // not timed: it lasts until the figure moves
        Assert.Equal(0f, EmoteCurves.Seconds(PlayerEmotes.Wave)); // the greeting gestures keep their own curves
        Assert.True(PlayerEmotes.IsPlayable(PlayerEmotes.Bow));
        Assert.False(PlayerEmotes.IsPlayable(PlayerEmotes.Hurt));
        Assert.False(PlayerEmotes.IsPlayable(PlayerEmotes.None));
    }

    [Fact]
    public void ACrowd_NeverBlinksInStep()
    {
        var gaps = Enumerable.Range(1, 12).Select(seed => EmoteCurves.BlinkGap(seed * 7919, 0)).ToList();

        Assert.All(gaps, g => Assert.InRange(g, 3f, 6.5f));
        Assert.True(gaps.Distinct().Count() >= 10, "twelve figures should not share their blink timing");
        Assert.NotEqual(EmoteCurves.BlinkGap(42, 0), EmoteCurves.BlinkGap(42, 1)); // nor does one figure blink like a clock
    }

    [Fact]
    public void ATalkingMouth_OpensAndCloses_WithinItsRange()
    {
        var values = Enumerable.Range(0, 200).Select(i => EmoteCurves.MouthOpen(i * 0.02f)).ToList();

        Assert.All(values, v => Assert.InRange(v, 0f, 1f));
        Assert.True(values.Max() - values.Min() > 0.6f, "the mouth should really move");
    }
}
