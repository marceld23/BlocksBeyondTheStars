// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3450: Justus' story lines over the loading bar. Five lines share the bar in equal bands, each line stays up at least
/// <see cref="LoadingLore.MinLineSeconds"/> and the story moves one line at a time toward the bar's band, "You awaken!" is
/// always the last one shown (in the hand-off frame at the latest, at once), and the story never steps back while the bar
/// stalls, creeps or starts over.
/// </summary>
public sealed class LoadingLoreTests
{
    private const float Frame = 1f / 60f;

    [Theory]
    [InlineData(0f, 1)]
    [InlineData(0.1f, 1)]
    [InlineData(0.19f, 1)]
    [InlineData(0.2f, 2)]
    [InlineData(0.39f, 2)]
    [InlineData(0.4f, 3)]
    [InlineData(0.6f, 4)] // the hold band (a booting server or browser world) starts here …
    [InlineData(0.79f, 4)]
    [InlineData(0.8f, 5)]
    [InlineData(0.85f, 5)] // … and never reaches 100 % before the world is ready
    [InlineData(0.9999f, 5)]
    [InlineData(1f, 6)]
    public void BandLine_StepsThroughFiveEqualBands_AndAwakensAtTheFullBar(float progress, int line)
        => Assert.Equal(line, LoadingLore.BandLine(progress));

    [Theory]
    [InlineData(-0.5f, 1)]
    [InlineData(float.NaN, 1)]
    [InlineData(float.NegativeInfinity, 1)]
    [InlineData(1.5f, 6)]
    [InlineData(float.PositiveInfinity, 6)]
    public void BandLine_ClampsProgressOutsideTheBar(float progress, int line)
        => Assert.Equal(line, LoadingLore.BandLine(progress));

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(0.9f)]
    [InlineData(1f)]
    public void TheStory_StartsAtTheFirstLine_WhereverTheBarIs(float progress)
        => Assert.Equal(LoadingLore.First, LoadingLore.LineFor(progress, false, LoadingLore.None, 0f));

    [Fact]
    public void ALine_StaysUpItsMinimumTime_ThenTheNextOneFollows_OneAtATime()
    {
        const float justUnder = LoadingLore.MinLineSeconds - 0.01f;
        Assert.Equal(1, LoadingLore.LineFor(0.85f, false, 1, justUnder)); // the bar is far ahead — the line still waits
        Assert.Equal(2, LoadingLore.LineFor(0.85f, false, 1, LoadingLore.MinLineSeconds)); // one line on, not four
        Assert.Equal(3, LoadingLore.LineFor(0.85f, false, 2, 60f)); // however long it waited
        Assert.Equal(2, LoadingLore.LineFor(0.3f, false, 2, 60f)); // and only as far as the bar's band
        Assert.Equal(6, LoadingLore.LineFor(1f, false, 5, LoadingLore.MinLineSeconds)); // the full bar awakens, in its turn
        Assert.Equal(5, LoadingLore.LineFor(1f, false, 5, justUnder));
        Assert.Equal(3, LoadingLore.LineFor(0.85f, false, 3, float.NaN)); // a broken clock never skips ahead
        Assert.Equal(3, LoadingLore.LineFor(0.85f, false, 3, -1f));
    }

    [Theory]
    [InlineData(0f, LoadingLore.None, 0f)]
    [InlineData(0.3f, 1, 0f)]
    [InlineData(0.85f, 2, 0.1f)]
    [InlineData(float.NaN, 5, 0f)]
    public void TheHandOffFrame_AlwaysReadsYouAwaken_AtOnce(float progress, int shown, float seconds)
        => Assert.Equal(LoadingLore.Last, LoadingLore.LineFor(progress, true, shown, seconds));

    [Fact]
    public void TheStory_NeverStepsBack()
    {
        Assert.Equal(4, LoadingLore.LineFor(0.1f, false, 4, 60f)); // a give-up restarts the bar at 0 — not the story
        Assert.Equal(6, LoadingLore.LineFor(0.5f, false, 6, 60f));
        Assert.Equal(6, LoadingLore.LineFor(0.5f, false, 99, 0f)); // a stray value still names a real line

        // A stalling, creeping, jumping bar, a frame every 0.4 s: the shown line only ever rises, by one at most.
        float[] bar = { 0f, 0.05f, 0.3f, 0.25f, 0.6f, 0.6f, 0.62f, 0.4f, 0.81f, 0.8f, 0.84f, 0.2f, 0.85f, 0.85f, 0.85f, 0.85f, 0.85f, 0.85f };
        int shown = LoadingLore.None;
        float seconds = 0f;
        foreach (float p in bar)
        {
            seconds += 0.4f;
            int next = LoadingLore.LineFor(p, false, shown, seconds);
            Assert.True(next >= shown, $"stepped back from {shown} to {next} at {p}");
            Assert.True(next <= shown + 1, $"skipped from {shown} to {next} at {p}");
            Assert.InRange(next, LoadingLore.First, LoadingLore.Last - 1);
            if (next != shown)
            {
                shown = next;
                seconds = 0f;
            }
        }

        Assert.Equal(5, shown);
        Assert.Equal(LoadingLore.Last, LoadingLore.LineFor(0.2f, true, shown, 0f));
    }

    [Fact]
    public void ABarThatRacesToTheHoldBand_StillShowsEveryLineItsMinimumTime()
    {
        // The desktop and browser screens ramp to the hold band (60 %) in about a second while the world boots, then creep:
        // the old pacing flashed the first lines past in half a second each. Now each line stays up its full time.
        var (shownAt, _) = Run(t => Math.Min(0.6f, t / 1.2f) + (0.25f * (1f - MathF.Exp(-t / 12f))), handOffAt: 30f);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, shownAt.Keys.OrderBy(k => k));
        for (int line = LoadingLore.First + 1; line <= LoadingLore.Last - 1; line++)
        {
            Assert.True(shownAt[line] - shownAt[line - 1] >= LoadingLore.MinLineSeconds - Frame,
                $"line {line - 1} was up only {shownAt[line] - shownAt[line - 1]:0.00} s");
        }

        Assert.Equal(30f, shownAt[LoadingLore.Last], 1); // and "You awaken!" comes with the hand-off
    }

    [Fact]
    public void AFastLoad_StillEndsOnYouAwaken()
    {
        // A 0.6-second remote join: the first line, then the hand-off frame jumps straight to the last.
        var (shownAt, last) = Run(t => Math.Min(1f, t / 0.6f), handOffAt: 0.6f);
        Assert.Equal(new[] { 1, 6 }, shownAt.Keys.OrderBy(k => k));
        Assert.Equal(LoadingLore.Last, last);
    }

    [Theory]
    [InlineData(1, "ui.loading.lore.1")]
    [InlineData(6, "ui.loading.lore.6")]
    [InlineData(LoadingLore.None, "ui.loading.lore.1")]
    [InlineData(-3, "ui.loading.lore.1")]
    [InlineData(7, "ui.loading.lore.6")]
    public void KeyFor_NamesTheSixLines(int line, string key) => Assert.Equal(key, LoadingLore.KeyFor(line));

    [Fact]
    public void EveryLine_IsInEnglishAndGerman()
    {
        var keys = LoadingLore.AllKeys;
        Assert.Equal(6, keys.Count);
        Assert.Equal("ui.loading.lore.1", keys[0]);
        Assert.Equal("ui.loading.lore.6", keys[5]);

        foreach (string locale in new[] { "en", "de" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", locale + ".json")));
            foreach (string key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var text) && text.GetString()!.Length > 0, $"{locale}.json misses {key}");
            }
        }
    }

    /// <summary>Plays the loading screen at 60 frames a second the way <c>UiLoading</c> does — the time on the current line
    /// counted, reset when the line changes — until the hand-off frame at <paramref name="handOffAt"/>. Returns when each
    /// line first came up, and the line of the hand-off frame.</summary>
    private static (Dictionary<int, float> ShownAt, int Last) Run(Func<float, float> bar, float handOffAt)
    {
        var shownAt = new Dictionary<int, float>();
        int shown = LoadingLore.None;
        float seconds = 0f;
        for (int frame = 0; ; frame++)
        {
            float t = frame * Frame;
            bool handingOff = t >= handOffAt - (Frame * 0.5f);
            seconds += Frame;
            int line = LoadingLore.LineFor(bar(t), handingOff, shown, seconds);
            if (line != shown)
            {
                shown = line;
                seconds = 0f;
                shownAt.TryAdd(line, t);
            }

            if (handingOff)
            {
                return (shownAt, shown);
            }
        }
    }
}
