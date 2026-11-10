// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using BlocksBeyondTheStars.Client;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3450/#3460: Justus' story lines on the black world-entry veil. The first line comes up with the veil, the next one
/// every <see cref="LoadingLore.MinLineSeconds"/> while the world loads (the clock alone stops at the fifth), and "You
/// awaken!" the moment the world is ready — no artificial waiting, and the story never steps back.
/// </summary>
public sealed class LoadingLoreTests
{
    private const float Frame = 1f / 60f;

    [Fact]
    public void TheStory_StartsAtTheFirstLine()
        => Assert.Equal(LoadingLore.First, LoadingLore.LineFor(false, LoadingLore.None, 0f));

    [Fact]
    public void ALine_StaysUpItsMinimumTime_ThenTheNextOneFollows_OneAtATime()
    {
        const float justUnder = LoadingLore.MinLineSeconds - 0.01f;
        Assert.Equal(1, LoadingLore.LineFor(false, 1, justUnder));
        Assert.Equal(2, LoadingLore.LineFor(false, 1, LoadingLore.MinLineSeconds));
        Assert.Equal(3, LoadingLore.LineFor(false, 2, 60f)); // one line on, however long it waited
        Assert.Equal(3, LoadingLore.LineFor(false, 3, float.NaN)); // a broken clock never skips ahead
        Assert.Equal(3, LoadingLore.LineFor(false, 3, -1f));
    }

    [Fact]
    public void TheClockAlone_StopsAtTheFifthLine()
    {
        Assert.Equal(LoadingLore.LastWhileLoading, LoadingLore.LineFor(false, 4, LoadingLore.MinLineSeconds));
        Assert.Equal(LoadingLore.LastWhileLoading, LoadingLore.LineFor(false, LoadingLore.LastWhileLoading, 60f));
    }

    [Theory]
    [InlineData(LoadingLore.None, 0f)]
    [InlineData(1, 0f)]
    [InlineData(3, 0.1f)]
    [InlineData(5, 60f)]
    [InlineData(2, float.NaN)]
    public void AReadyWorld_ReadsYouAwaken_AtOnce(int shown, float seconds)
        => Assert.Equal(LoadingLore.Last, LoadingLore.LineFor(true, shown, seconds));

    [Fact]
    public void TheStory_NeverStepsBack()
    {
        Assert.Equal(LoadingLore.Last, LoadingLore.LineFor(false, LoadingLore.Last, 0f)); // "You awaken!" stays up
        Assert.Equal(LoadingLore.Last, LoadingLore.LineFor(false, 99, 0f)); // a stray value still names a real line
        Assert.Equal(4, LoadingLore.LineFor(false, 4, 0f));
    }

    [Theory]
    [InlineData(0.8f, new[] { 1, 6 })] // a fast world: the first line, then "You awaken!"
    [InlineData(4f, new[] { 1, 2, 3, 6 })] // lines at 0, 1.5 and 3 s, awake at 4 s
    [InlineData(9f, new[] { 1, 2, 3, 4, 5, 6 })] // the whole story, the fifth line held until the world is ready
    [InlineData(25f, new[] { 1, 2, 3, 4, 5, 6 })] // the veil's safety drop counts as ready
    public void TheVeil_ShowsAsMuchStoryAsTheLoadTakes_AndEndsOnYouAwaken(float readyAt, int[] lines)
    {
        var (shownAt, last) = Run(readyAt);
        Assert.Equal(lines, shownAt.Keys.OrderBy(k => k));
        Assert.Equal(LoadingLore.Last, last);
        Assert.Equal(readyAt, shownAt[LoadingLore.Last], 1); // "You awaken!" comes the moment the world is ready, not later
        for (int line = LoadingLore.First + 1; line <= LoadingLore.LastWhileLoading; line++)
        {
            if (shownAt.TryGetValue(line, out float at))
            {
                Assert.True(at - shownAt[line - 1] >= LoadingLore.MinLineSeconds - Frame,
                    $"line {line - 1} was up only {at - shownAt[line - 1]:0.00} s");
            }
        }
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

    /// <summary>Plays the veil at 60 frames a second the way <c>WorldLoadingOverlay</c> does — the time on the current line
    /// counted, reset when the line changes — until the world is ready at <paramref name="readyAt"/>. Returns when each line
    /// first came up, and the line of the ready frame.</summary>
    private static (Dictionary<int, float> ShownAt, int Last) Run(float readyAt)
    {
        var shownAt = new Dictionary<int, float>();
        int shown = LoadingLore.None;
        float seconds = 0f;
        for (int frame = 0; ; frame++)
        {
            float t = frame * Frame;
            bool ready = t >= readyAt - (Frame * 0.5f);
            int line = LoadingLore.LineFor(ready, shown, seconds);
            if (line != shown)
            {
                shown = line;
                seconds = 0f;
                shownAt.TryAdd(line, t);
            }

            if (ready)
            {
                return (shownAt, shown);
            }

            seconds += Frame;
        }
    }
}
