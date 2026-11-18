// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3767 (Justus, 2026-11-17: "I want to choose in the skin editor what the sounds the figure makes with its mouth are
/// like"): the figure's voice — a pitch and an age slider, 0…100, the middle the sound the game always had. These pin the
/// shared rule: the wire packing (0 = not set = the middle; anything foreign is clamped), the pitch factor (1 at the
/// middle, within a range a clip survives), the age's timbre bands, which clips are voice at all, and that both new wire
/// fields are additive.
/// </summary>
public sealed class PlayerVoiceTests
{
    [Fact]
    public void TheVoice_PacksIntoOneInt_NotSetIsTheMiddle_AndForeignValuesAreClamped()
    {
        Assert.Equal((PlayerVoice.Middle, PlayerVoice.Middle), PlayerVoice.Unpack(0)); // an older client or save
        for (int pitch = 0; pitch <= 100; pitch += 10)
        {
            for (int age = 0; age <= 100; age += 10)
            {
                int packed = PlayerVoice.Pack(pitch, age);
                Assert.NotEqual(0, packed); // a set voice is never mistaken for "not set" — not even (0, 0)
                Assert.Equal((pitch, age), PlayerVoice.Unpack(packed));
                Assert.Equal(packed, PlayerVoice.Sanitize(packed));
            }
        }

        Assert.Equal(0, PlayerVoice.Sanitize(0));
        Assert.Equal(PlayerVoice.Pack(100, 100), PlayerVoice.Sanitize(int.MaxValue));
        Assert.Equal(PlayerVoice.Pack(100, 0), PlayerVoice.Pack(999, -5));
        Assert.Equal(PlayerVoice.Unpack(PlayerVoice.Pack(40, 60)), PlayerVoice.Unpack(PlayerVoice.Pack(40, 60) | (0x55 << 16))); // high bits ignored
        Assert.Equal(new[] { 0, 50, 50, 100, 100 }, new[] { -3, 46, 54, 96, 250 }.Select(PlayerVoice.Snap));
    }

    [Fact]
    public void ThePitchFactor_IsOneAtTheMiddle_RisesWithThePitch_AndStaysPlayable()
    {
        Assert.Equal(1f, PlayerVoice.Factor(PlayerVoice.Middle, PlayerVoice.Middle), 5);
        Assert.Equal(1f, PlayerVoice.Factor(0), 5); // not set
        Assert.True(PlayerVoice.Factor(PlayerVoice.High, 50) > 1.1f && PlayerVoice.Factor(PlayerVoice.Low, 50) < 0.9f);
        Assert.True(PlayerVoice.Factor(50, 0) > 1f && PlayerVoice.Factor(50, 100) < 1f, "young a touch higher, old a touch lower");
        float last = 0f;
        for (int pitch = 0; pitch <= 100; pitch += 10)
        {
            float f = PlayerVoice.Factor(pitch, 50);
            Assert.True(f > last, "a higher slider is a higher voice");
            last = f;
        }

        // The extremes of both sliders together stay inside what the audio layer plays (0.5…2) with room to spare.
        Assert.InRange(PlayerVoice.Factor(0, 100), 0.6f, 1f);
        Assert.InRange(PlayerVoice.Factor(100, 0), 1f, 1.6f);
        Assert.Equal((double)PlayerVoice.Factor(100, 50), Math.Pow(2.0, PlayerVoice.PitchSemitones / 12.0), 4);
    }

    [Fact]
    public void TheAge_PicksATimbre_YoungThin_TheMiddleDry_OldDull()
    {
        Assert.Equal((VoiceOp.Thin, 2), PlayerVoice.Timbre(0));
        Assert.Equal((VoiceOp.Thin, 1), PlayerVoice.Timbre(30));
        Assert.Equal((VoiceOp.None, 0), PlayerVoice.Timbre(40));
        Assert.Equal((VoiceOp.None, 0), PlayerVoice.Timbre(PlayerVoice.Middle));
        Assert.Equal((VoiceOp.None, 0), PlayerVoice.Timbre(60));
        Assert.Equal((VoiceOp.Dull, 1), PlayerVoice.Timbre(70));
        Assert.Equal((VoiceOp.Dull, 2), PlayerVoice.Timbre(100));
        Assert.All(new[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }, age => Assert.InRange(PlayerVoice.Timbre(age).Amount, 0, 2));
    }

    [Theory]
    [InlineData("breath_exhale", true)]
    [InlineData("breath_exhale_2", true)]
    [InlineData("breath_quick", true)]
    [InlineData("surface_gasp", true)]
    [InlineData("climb_strain", true)]
    [InlineData("hurt_player", true)]
    [InlineData("hurt_player_2", true)]
    [InlineData("player_death", true)]
    [InlineData("land_heavy", false)]   // boots first, a grunt second: it keeps its pitch
    [InlineData("jump", false)]         // the suit's servo
    [InlineData("heartbeat_soft", false)]
    [InlineData("step_stone_1", false)]
    [InlineData("eat", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyTheFiguresMouthAndBreath_AreVoice(string? clip, bool voice) => Assert.Equal(voice, PlayerVoice.IsVoiceClip(clip));

    [Fact]
    public void BothWireFields_AreAdditive_AndRoundTrip()
    {
        static T Trip<T>(T message)
            where T : class
            => Assert.IsType<T>(NetCodec.Decode(NetCodec.Encode(message)));

        Assert.Equal(PlayerVoice.Pack(70, 30), Trip(new SetAppearanceIntent { Skin = 1, Legs = 4, Voice = PlayerVoice.Pack(70, 30) }).Voice);
        Assert.Equal(0, Trip(new SetAppearanceIntent { Skin = 1 }).Voice);

        var emote = Trip(new PlayerEmote { PlayerId = "p", Emote = PlayerEmotes.Hurt, Voice = PlayerVoice.Pack(20, 80) });
        Assert.Equal((PlayerEmotes.Hurt, PlayerVoice.Pack(20, 80)), (emote.Emote, emote.Voice));
        Assert.Equal(0, Trip(new PlayerEmote { PlayerId = "p", Emote = PlayerEmotes.Wave }).Voice);
    }
}
