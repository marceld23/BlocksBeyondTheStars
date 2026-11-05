// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3202: the surface cloud deck placed every cloud at <c>absolute world time × wind</c>, so a weather flip or a wind
/// ramp re-scaled the whole accumulated offset and the deck jumped (worse the longer the session ran), and the look
/// switched in one frame. <see cref="CloudDeck"/> accumulates each cloud's azimuth per world step and eases the look.
/// </summary>
public sealed class CloudDeckTests
{
    private const int Clouds = 20;
    private const float Frame = 1f / 60f;
    private const float TwoPi = (float)(Math.PI * 2.0);

    private static CloudDeck NewDeck(out float maxDrift)
    {
        var rng = new Random(20260602);
        var angles = new float[Clouds];
        var drift = new float[Clouds];
        maxDrift = 0f;
        for (int i = 0; i < Clouds; i++)
        {
            angles[i] = (float)(rng.NextDouble() * TwoPi);
            float dist = 150f + ((float)rng.NextDouble() * 180f);
            drift[i] = 200f / dist; // the deck's own rule: near clouds drift faster
            maxDrift = Math.Max(maxDrift, drift[i]);
        }

        return new CloudDeck(angles, drift);
    }

    private static float AngularStep(float from, float to)
    {
        float d = Math.Abs(to - from) % TwoPi;
        return Math.Min(d, TwoPi - d);
    }

    [Fact]
    public void AWindChangeAfterAnHour_OnlyChangesTheSpeed_NeverJumpsTheDeck()
    {
        var deck = NewDeck(out float maxDrift);
        for (int f = 0; f < 3600 * 60; f++)
        {
            deck.Drift(Frame, 1f);
        }

        var before = Enumerable.Range(0, Clouds).Select(deck.Angle).ToArray();
        const float StormScale = 3f;
        deck.Drift(Frame, StormScale);

        float bound = (Frame * CloudDeck.DriftRate * StormScale * maxDrift) + 1e-4f;
        for (int i = 0; i < Clouds; i++)
        {
            Assert.True(AngularStep(before[i], deck.Angle(i)) <= bound,
                $"cloud {i} moved {AngularStep(before[i], deck.Angle(i))} rad in one frame (bound {bound})");
            Assert.InRange(deck.Angle(i), 0f, TwoPi);
        }

        // The old rule (angle = start + worldTime × 0.012 × scale × drift) moved the same cloud by turns in that frame.
        float oldJump = 3600f * CloudDeck.DriftRate * (StormScale - 1f) * maxDrift;
        Assert.True(oldJump > TwoPi * 10f, "the regression this pins: the absolute-time rule was a reshuffle");
    }

    [Fact]
    public void AHeldWorld_ParksTheDeck()
    {
        var deck = NewDeck(out _);
        var before = Enumerable.Range(0, Clouds).Select(deck.Angle).ToArray();
        deck.Drift(0f, 4f);
        deck.Drift(-1f, 4f);
        Assert.Equal(before, Enumerable.Range(0, Clouds).Select(deck.Angle).ToArray());
    }

    [Fact]
    public void AStormToClearFlip_Eases_WithBoundedFrames_AndLands()
    {
        var deck = NewDeck(out _);
        const float Density = 0.35f;
        deck.EaseToward(CloudDeck.Target("storm", Density), Frame); // the first look snaps
        Assert.Equal(CloudDeck.Target("storm", Density).Cover, deck.Look.Cover);

        var clear = CloudDeck.Target("clear", Density);
        float maxCoverStep = Math.Max(Math.Abs(deck.Look.Cover - clear.Cover) * Frame / CloudDeck.EaseSeconds, CloudDeck.MinEaseRate * Frame) + 1e-6f;
        int visible = VisibleClouds(deck.Look.Cover);
        float seconds = 0f;
        while (!Same(deck.Look, clear))
        {
            var prev = deck.Look;
            deck.EaseToward(clear, Frame);
            seconds += Frame;
            Assert.True(seconds <= 60f, "the ease must land on the target within a minute");

            Assert.True(Math.Abs(deck.Look.Cover - prev.Cover) <= maxCoverStep, $"cover stepped {Math.Abs(deck.Look.Cover - prev.Cover)} in one frame");
            Assert.True(Math.Abs(deck.Look.WindScale - prev.WindScale) <= 3f * Frame / CloudDeck.EaseSeconds + 1e-5f);
            Assert.True(Math.Abs(deck.Look.Darken - prev.Darken) <= 0.1f * Frame + 1e-5f);
            Assert.True(Math.Abs(deck.Look.CirrusFade - prev.CirrusFade) <= 0.1f * Frame + 1e-5f);

            // Single clouds fade instead of popping: no cloud's share changes by more than a sliver per frame, and the
            // number of clouds on screen moves by one at most.
            for (int i = 0; i < Clouds; i++)
            {
                float step = Math.Abs(CloudDeck.Presence(i, Clouds, deck.Look.Cover) - CloudDeck.Presence(i, Clouds, prev.Cover));
                Assert.True(step <= Clouds * maxCoverStep + 1e-5f, $"cloud {i} faded {step} in one frame");
            }

            int now = VisibleClouds(deck.Look.Cover);
            Assert.True(Math.Abs(now - visible) <= 1, $"{Math.Abs(now - visible)} clouds popped in one frame");
            visible = now;
        }

        Assert.True(seconds >= 5f, $"a storm → clear flip must not finish in {seconds:0.0} s");
    }

    [Fact]
    public void Snap_JumpsStraightToTheTarget()
    {
        var deck = NewDeck(out _);
        deck.Snap(CloudDeck.Target("clear", 0.3f));
        var storm = CloudDeck.Target("storm", 0.3f);
        deck.EaseToward(storm, Frame);
        Assert.False(Same(deck.Look, storm));

        deck.Snap(storm);
        Assert.True(Same(deck.Look, storm));
    }

    [Fact]
    public void Target_UsesTheSkysCover_AndTheWeatherTable()
    {
        var target = CloudDeck.Target("rain", 0.9f);
        Assert.Equal(WeatherLook.Cover("rain", 0.9f), target.Cover);
        Assert.Equal(WeatherLook.Sky("rain", 0.9f).WindScale, target.WindScale);
        Assert.Equal(0f, CloudDeck.Presence(0, Clouds, 0f));
        Assert.Equal(1f, CloudDeck.Presence(Clouds - 1, Clouds, 1f));
        Assert.Equal(0.5, CloudDeck.Presence(3, Clouds, 3.5f / Clouds), 3);
    }

    private static int VisibleClouds(float cover)
        => Enumerable.Range(0, Clouds).Count(i => CloudDeck.Presence(i, Clouds, cover) > 0.001f);

    private static bool Same(CloudStyle a, CloudStyle b)
        => a.Cover == b.Cover && a.Darken == b.Darken && a.WindScale == b.WindScale && a.StormTall == b.StormTall && a.CirrusFade == b.CirrusFade;
}
