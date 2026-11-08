// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Minigames;
using BlocksBeyondTheStars.Client.Minigames.Games;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3382 — the arcade games drew some localized words with the 5×7 bitmap font, which knows only A–Z, digits and a few
/// signs: in Russian, Ukrainian, Japanese, Korean and Chinese those words came out blank. Now every localized word goes on
/// the canvas' label layer, which the Unity host draws with the real UI font. These tests pin the layer itself and guard
/// every registered game: run with a localizer whose every text starts with a Cyrillic letter, no game may hand such a
/// text to <see cref="Canvas2D.DrawText"/>, and the words each game shows must arrive as labels.
/// </summary>
public sealed class MinigameLabelTests
{
    private const string Marker = "Ж-";

    private static string Fake(string key) => Marker + key;

    /// <summary>The locale keys each game shows on its canvas — they must reach the label layer (compared upper-case,
    /// the games upper-case most of them like the old pixel text).</summary>
    private static readonly Dictionary<string, string[]> ExpectedLabelKeys = new()
    {
        ["asteroid_breaker"] = new[] { "minigame.asteroid_breaker.serve_hint" },
        ["cargo_sorter"] = new[]
        {
            "minigame.cargo_sorter.bay_ore", "minigame.cargo_sorter.bay_tech", "minigame.cargo_sorter.bay_bio", "minigame.cargo_sorter.bay_energy",
        },
        ["glyph_decoder"] = new[] { "minigame.glyph_decoder.decoding_in", "minigame.glyph_decoder.question", "minigame.glyph_decoder.meaning_" },
        ["reactor_balance"] = new[]
        {
            "minigame.reactor_balance.label_temperature", "minigame.reactor_balance.label_power",
            "minigame.reactor_balance.label_coolant", "minigame.reactor_balance.label_stability",
        },
        ["signal_tuner"] = new[]
        {
            "minigame.signal_tuner.label_frequency", "minigame.signal_tuner.label_amplitude", "minigame.signal_tuner.label_filter",
            "minigame.signal_tuner.label_quality", "minigame.signal_tuner.label_decode",
        },
        ["note_key"] = new[]
        {
            "minigame.note_key.word_again", "minigame.note_key.letter_again", "minigame.note_key.known",
            "minigame.note_key.round", "minigame.note_key.listen",
        },
    };

    private static readonly MinigameAction[] PlayActions =
    {
        MinigameAction.Left, MinigameAction.Right, MinigameAction.Up, MinigameAction.Down,
        MinigameAction.Confirm, MinigameAction.Primary, MinigameAction.Secondary, MinigameAction.Cancel,
    };

    public static IEnumerable<object[]> GameKeys() => MinigameRegistry.Keys.Select(k => new object[] { k });

    // ---------------- the guard over every game ----------------

    [Theory]
    [MemberData(nameof(GameKeys))]
    public void NoGame_DrawsALocalizedWord_WithTheBitmapFont_AndItsWordsArriveAsLabels(string key)
    {
        var host = new MinigameHost(MinigameRegistry.Create(key)!, localize: Fake, seed: 5);
        host.StartGame();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Inspect(host, key, -1, seen);

        // 12 s of play at 20 Hz: every few steps a press of a play action and a tap somewhere on a 7×5 grid of the canvas,
        // so the games reach their later screens too (the glyph quiz, a picked bay, a sung letter).
        for (int i = 0; i < 240 && host.State == MinigameState.Playing; i++)
        {
            host.Tick(0.05f);
            Inspect(host, key, i, seen);
            Poke(host, i);
        }

        if (ExpectedLabelKeys.TryGetValue(key, out var expected))
        {
            foreach (string word in expected)
            {
                string upper = (Marker + word).ToUpperInvariant();
                Assert.True(seen.Any(t => t.ToUpperInvariant().Contains(upper, StringComparison.Ordinal)),
                    $"{key}: '{word}' never reached the label layer (labels seen: {string.Join(" | ", seen.Take(12))})");
            }
        }
    }

    /// <summary>Fails on a localized text drawn with the bitmap font, and collects the labels shown so far.</summary>
    private static void Inspect(MinigameHost host, string key, int step, HashSet<string> seen)
    {
        var canvas = host.Api.Surface;
        if (canvas == null)
        {
            return;
        }

        Assert.True(canvas.UndrawableText == null,
            $"{key} (step {step}) drew \"{canvas.UndrawableText}\" with the 5×7 bitmap font — a localized word belongs on Canvas2D.Label (#3382)");
        foreach (var label in canvas.Labels)
        {
            seen.Add(label.Text);
        }
    }

    private static void Poke(MinigameHost host, int step)
    {
        if (step % 4 == 1)
        {
            var action = PlayActions[(step / 4) % PlayActions.Length];
            host.Press(action);
            host.Release(action);
        }

        var canvas = host.Api.Surface;
        if (step % 6 == 3 && canvas != null)
        {
            int k = step / 6;
            float x = ((k % 7) + 0.5f) * canvas.Width / 7f;
            float y = (((k / 7) % 5) + 0.5f) * canvas.Height / 5f;
            host.Pointer(PointerPhase.Down, x, y);
            host.Pointer(PointerPhase.Up, x, y);
        }
    }

    // ---------------- the two games of the issue ----------------

    [Fact]
    public void GlyphDecoder_LabelsTheCountdownTheMeaningsAndTheQuestion_TheGlyphsStayPixels()
    {
        var host = new MinigameHost(new GlyphDecoderGame(), localize: Fake, seed: 2);
        host.StartGame();
        var canvas = host.Api.Surface!;

        // Study: the countdown, then the four glyph → meaning pairs (the meanings at x 210, one row each 60 px).
        Assert.Equal(5, canvas.Labels.Count);
        var countdown = canvas.Labels[0];
        Assert.Equal("Ж-MINIGAME.GLYPH_DECODER.DECODING_IN5", countdown.Text);
        Assert.Equal((20, 20, 2, LabelAlign.Left), (countdown.X, countdown.Y, countdown.Scale, countdown.Align));
        for (int k = 0; k < 4; k++)
        {
            var meaning = canvas.Labels[1 + k];
            Assert.StartsWith("Ж-MINIGAME.GLYPH_DECODER.MEANING_", meaning.Text, StringComparison.Ordinal);
            Assert.Equal((210, 80 + (k * 60) + 7), (meaning.X, meaning.Y));
        }

        Assert.Contains(PixelColors(canvas), c => c == (70, 214, 255)); // the glyphs themselves are still bitmap letters
        TickSeconds(host, 2.1f);
        Assert.Equal("Ж-MINIGAME.GLYPH_DECODER.DECODING_IN3", canvas.Labels[0].Text);
        Assert.Equal(5, canvas.Labels.Count); // a redraw replaces the labels, it never piles them up

        // Quiz: the question and four option captions, centred in their buttons and kept inside them.
        TickSeconds(host, 3.2f);
        Assert.Equal(5, canvas.Labels.Count);
        Assert.Equal("Ж-minigame.glyph_decoder.question", canvas.Labels[0].Text);
        const int optW = 250, optH = 46, optStartX = (560 - (2 * optW + 16)) / 2;
        for (int i = 0; i < 4; i++)
        {
            var option = canvas.Labels[1 + i];
            Assert.Equal(LabelAlign.Center, option.Align);
            Assert.Equal(optStartX + ((i % 2) * (optW + 16)) + (optW / 2), option.X);
            Assert.Equal(230 + ((i / 2) * (optH + 14)) + (optH / 2) - 7, option.Y);
            Assert.True(option.MaxWidth < optW, "an option caption stays inside its button");
        }

        Assert.Null(canvas.UndrawableText);
    }

    [Fact]
    public void NoteKey_LabelsTheButtonsTheCountersTheMessage_AndTheLocalesNoteNames()
    {
        static string Localize(string key) => key == "ui.note.names" ? "До Ре Ми Фа Соль Ля Си До'" : Marker + key;
        var host = new MinigameHost(new NoteKeyGame(new[] { "OLU" }, "O"), localize: Localize, seed: 7);
        host.StartGame();
        TickSeconds(host, 0.5f); // the first sung tone draws the table
        var canvas = host.Api.Surface!;
        var labels = canvas.Labels;

        // The two buttons (x 16, 176 wide, at y 150 and 202): their captions centred, kept inside the box.
        var word = Assert.Single(labels, l => l.Text == "Ж-MINIGAME.NOTE_KEY.WORD_AGAIN");
        Assert.Equal((16 + 88, 150 + 13, 2, LabelAlign.Center), (word.X, word.Y, word.Scale, word.Align));
        Assert.True(word.MaxWidth < 176);
        var letter = Assert.Single(labels, l => l.Text == "Ж-MINIGAME.NOTE_KEY.LETTER_AGAIN");
        Assert.Equal((16 + 88, 202 + 13), (letter.X, letter.Y));

        var known = Assert.Single(labels, l => l.Text.StartsWith("Ж-MINIGAME.NOTE_KEY.KNOWN", StringComparison.Ordinal));
        Assert.Equal("Ж-MINIGAME.NOTE_KEY.KNOWN 1/26", known.Text);
        Assert.Equal((16, 262, 1), (known.X, known.Y, known.Scale));
        Assert.Contains(labels, l => l.Text == "Ж-MINIGAME.NOTE_KEY.ROUND 1/1" && l.X == 16 && l.Y == 12);
        Assert.Contains(labels, l => l.Text == "Ж-MINIGAME.NOTE_KEY.LISTEN" && l.Align == LabelAlign.Center && l.X == NoteKeyRun.W / 2);

        // The headers carry the locale's note names: four columns, seven rows — each centred on its header.
        for (int col = 0; col < 4; col++)
        {
            var (x, y, w, _) = NoteKeyLayout.ColumnHeader(col);
            Assert.Contains(labels, l => l.X == x + (w / 2) && l.Y == y + 5 && l.MaxWidth <= w);
        }

        string[] names = { "До", "Ре", "Ми", "Фа", "Соль", "Ля", "Си" };
        for (int row = 0; row < names.Length; row++)
        {
            var (x, y, w, _) = NoteKeyLayout.RowHeader(row);
            Assert.Contains(labels, l => l.Text == names[row] && l.X == x + (w / 2) && l.Y == y + 11);
        }

        int count = labels.Count;
        host.Press(MinigameAction.Right); // a redraw (the cursor moves) rebuilds the same labels
        host.Release(MinigameAction.Right);
        Assert.Equal(count, canvas.Labels.Count);
        Assert.Null(canvas.UndrawableText); // the word's letters and the table's '?' stay bitmap glyphs, all drawable
    }

    // ---------------- the label layer itself ----------------

    [Fact]
    public void Label_IsListed_ButNeverRasterised()
    {
        var c = new Canvas2D(40, 20);
        c.Label(2, 3, "Ключ", Rgba.White, 2);
        Assert.All(c.Rgba, b => Assert.Equal(0, b));
        var label = Assert.Single(c.Labels);
        Assert.Equal(("Ключ", 2, 3, 2, LabelAlign.Left), (label.Text, label.X, label.Y, label.Scale, label.Align));
        Assert.Equal(14, label.CapHeight);
        Assert.Null(c.UndrawableText);
    }

    [Fact]
    public void Clear_DropsTheLabels_AndEveryChangeBumpsTheVersion()
    {
        var c = new Canvas2D(40, 20);
        int v0 = c.LabelVersion;
        c.Label(0, 0, string.Empty, Rgba.White); // nothing to show — nothing changes
        Assert.Equal(v0, c.LabelVersion);
        c.LabelCentered(20, 0, "A", Rgba.White);
        Assert.True(c.LabelVersion > v0);
        int v1 = c.LabelVersion;
        c.ClearLabels();
        Assert.Empty(c.Labels);
        Assert.True(c.LabelVersion > v1);
        int v2 = c.LabelVersion;
        c.ClearLabels(); // already empty
        Assert.Equal(v2, c.LabelVersion);
        c.Label(0, 0, "B", Rgba.White);
        c.Clear(Rgba.Black);
        Assert.Empty(c.Labels);
    }

    [Theory]
    [InlineData(LabelAlign.Left, 100, 0, 460)]   // to the right edge
    [InlineData(LabelAlign.Center, 100, 0, 200)] // twice the way to the nearer edge
    [InlineData(LabelAlign.Center, 500, 0, 120)]
    [InlineData(LabelAlign.Right, 100, 0, 100)]  // to the left edge
    [InlineData(LabelAlign.Left, 100, 80, 80)]   // an explicit width wins inside the room ...
    [InlineData(LabelAlign.Left, 500, 300, 60)]  // ... but never runs off the canvas
    public void MaxWidth_DefaultsToTheRoomToTheCanvasEdge(LabelAlign align, int x, int maxWidth, int expected)
    {
        var c = new Canvas2D(560, 100);
        c.Label(x, 10, "X", Rgba.White, 1, align, maxWidth);
        Assert.Equal(expected, Assert.Single(c.Labels).MaxWidth);
    }

    [Fact]
    public void Box_HangsOffTheAnchor_AndCentresOnTheCapitals()
    {
        var left = new CanvasLabel(10, 20, "A", Rgba.White, 2, LabelAlign.Left, 100);
        Assert.Equal((10f, 13f, 100f, 28f), left.Box()); // caps 14 px tall, from y 20 → box 28 tall around y 27
        Assert.Equal((50f, 13f, 100f, 28f), new CanvasLabel(100, 20, "A", Rgba.White, 2, LabelAlign.Center, 100).Box());
        Assert.Equal((0f, 13f, 100f, 28f), new CanvasLabel(100, 20, "A", Rgba.White, 2, LabelAlign.Right, 100).Box());
        Assert.Equal(14 * CanvasLabel.FontPerCap * 2f, left.FontSize(2f), 3);
        Assert.Equal(left, new CanvasLabel(10, 20, "A", Rgba.White, 2, LabelAlign.Left, 100));
        Assert.NotEqual(left, new CanvasLabel(10, 20, "A", Rgba.Black, 2, LabelAlign.Left, 100));
    }

    [Theory]
    [InlineData("SCORE 12/30", false)]
    [InlineData("abc", false)] // lowercase folds to the capitals
    [InlineData("Ж", true)]
    [InlineData("KÜHLUNG", true)]
    [InlineData("C'", true)]
    public void DrawText_RemembersATextTheFontCannotSpell(string text, bool undrawable)
    {
        var c = new Canvas2D(80, 10);
        c.DrawText(0, 0, text, Rgba.White);
        Assert.Equal(undrawable ? text : null, c.UndrawableText);
    }

    private static void TickSeconds(MinigameHost host, float seconds)
    {
        int n = (int)(seconds * 60);
        for (int i = 0; i < n && host.State == MinigameState.Playing; i++)
        {
            host.Tick(1f / 60f);
        }
    }

    private static IEnumerable<(int R, int G, int B)> PixelColors(Canvas2D c)
    {
        for (int i = 0; i < c.Rgba.Length; i += 4)
        {
            yield return (c.Rgba[i], c.Rgba[i + 1], c.Rgba[i + 2]);
        }
    }
}
