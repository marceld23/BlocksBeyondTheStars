// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// Headless tests for the uGUI text splitter behind the Codex chapters (#1097). One uGUI Text stops
/// rendering (VertexHelper's 65 000-vertex ArgumentException) at ~16 250 characters, which made the Guide
/// and Items chapters render EMPTY once the articles / descriptions grew — so every chunk must stay under
/// the budget, the split must be lossless, and it must never cut a rich-text tag in half.
/// </summary>
public sealed class UiTextChunksTests
{
    [Fact]
    public void Empty_YieldsNoChunks()
    {
        Assert.Empty(UiTextChunks.Split(null));
        Assert.Empty(UiTextChunks.Split(string.Empty));
    }

    [Fact]
    public void ShortText_IsOneChunk_Unchanged()
    {
        var chunks = UiTextChunks.Split("<b>Title</b>\n\nbody\n", 100);
        Assert.Single(chunks);
        Assert.Equal("<b>Title</b>\n\nbody\n", chunks[0]);
    }

    [Fact]
    public void PrefersParagraphBreak_KeepsSeparatorOnPrecedingChunk()
    {
        // 6 + 2 + 6 + 2 + 6 = 22 chars; budget 15 → the cut lands on the paragraph break, not mid-word.
        var chunks = UiTextChunks.Split("aaaaaa\n\nbbbbbb\n\ncccccc", 15);
        Assert.Equal(new[] { "aaaaaa\n\n", "bbbbbb\n\ncccccc" }, chunks);
    }

    [Fact]
    public void FallsBackToLineBreak_WhenNoParagraphFits()
    {
        var chunks = UiTextChunks.Split("aaaaaa\nbbbbbb\ncccccc", 15);
        Assert.Equal(new[] { "aaaaaa\nbbbbbb\n", "cccccc" }, chunks);
    }

    [Fact]
    public void HardCut_ForUnbrokenRun_NeverExceedsBudget()
    {
        string run = new string('x', 35);
        var chunks = UiTextChunks.Split(run, 10);
        Assert.Equal(4, chunks.Count);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 10));
        Assert.Equal(run, string.Concat(chunks));
    }

    [Fact]
    public void HardCut_DoesNotLandInsideATag()
    {
        // Budget 12 would cut "xxxxxxxx<col|or=#fff>y" inside the tag; the chunk ends before '<' instead.
        var chunks = UiTextChunks.Split("xxxxxxxx<color=#fff>y</color>", 12);
        Assert.Equal("xxxxxxxx", chunks[0]);
        Assert.All(chunks, c => Assert.DoesNotMatch("^[^<]*>", c)); // no chunk starts with the tail of a tag
        Assert.Equal("xxxxxxxx<color=#fff>y</color>", string.Concat(chunks));
    }

    [Fact]
    public void HardCut_DoesNotSplitSurrogatePairs()
    {
        string s = "abcd" + "\U0001F680" + "efgh"; // 🚀 is two UTF-16 code units at index 4..5
        var chunks = UiTextChunks.Split(s, 5);
        Assert.Equal("abcd", chunks[0]);
        Assert.Equal(s, string.Concat(chunks));
        Assert.All(chunks, c => Assert.False(char.IsHighSurrogate(c[c.Length - 1]), "chunk ends on a lone high surrogate"));
    }

    [Fact]
    public void Split_IsLossless()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < 400; i++)
        {
            sb.Append("<b>Head ").Append(i).Append("</b>\n• line one\n• line two\n\n");
        }

        string text = sb.ToString();
        var chunks = UiTextChunks.Split(text, 1000);
        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, 1000));
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void ZeroBudget_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => UiTextChunks.Split("abc", 0));
    }

    /// <summary>The regression itself: the whole Guide chapter, rendered the way WikiUI renders it (every
    /// article's title + WikiMarkup body concatenated), is OVER the uGUI limit for both mandatory languages —
    /// and split with the default budget every chunk is safely under it, without losing a character.</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void RealGuideChapter_ExceedsOneText_ButEveryChunkFits(string lang)
    {
        string path = Path.Combine(ClientTestPaths.DataDir(), "wiki", "articles.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        var sb = new StringBuilder();
        foreach (var article in doc.RootElement.EnumerateArray())
        {
            sb.Append("<b><size=24>").Append(Loc(article.GetProperty("title"), lang)).Append("</size></b>\n\n");
            sb.Append(WikiMarkup.ToUnityRichText(Loc(article.GetProperty("body"), lang))).Append("\n\n\n");
        }

        string guide = sb.ToString();
        int visible = Regex.Replace(guide, "<[^>]+>", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(1)).Length;
        var chunks = UiTextChunks.Split(guide);

        // At the time of the fix both languages were over the limit (EN ~17 k, DE ~19 k visible characters);
        // should the articles ever shrink below one Text's budget again, one chunk is the right answer.
        Assert.True(
            chunks.Count >= (visible > UiTextChunks.DefaultMaxChars ? 2 : 1),
            $"the {lang} Guide ({visible} visible chars) must be split into more than one Text");
        Assert.All(chunks, c => Assert.InRange(c.Length, 1, UiTextChunks.DefaultMaxChars));
        Assert.Equal(guide, string.Concat(chunks));

        // Every chunk starts at an article/paragraph boundary — never mid-sentence — for the real content.
        for (int i = 1; i < chunks.Count; i++)
        {
            Assert.EndsWith("\n", chunks[i - 1]);
        }
    }

    private static string Loc(JsonElement loc, string lang)
        => loc.TryGetProperty(lang, out var v) && v.GetString() is { Length: > 0 } s
            ? s
            : loc.GetProperty("en").GetString() ?? string.Empty;
}
