// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// Composing a narrator line for display (#2363, moved to Shared by #2466): <c>{key:Action}</c> tokens are expanded
/// first, then the <c>{0}</c>, <c>{1}</c>… slots are filled from a packed argument. ONE implementation for both sides:
/// the client passes its device-aware glyph resolver (<c>HudUi.KeyTokenGlyph</c> through <c>VegaText</c>), the server
/// composes the <c>Text</c> of a line for a client that does not understand the line's key (no <c>packs1</c>, synthesis
/// §5.7) with device-neutral bracket words from <c>ui.vega.act.&lt;Action&gt;</c> ("[Use]") — it cannot know the
/// player's device or bindings.
/// </summary>
public static class LineText
{
    /// <summary>Separator between several <c>{n}</c> arguments packed into one argument string (<c>ShipAiLine.LineArg</c>).</summary>
    public const char ArgSeparator = '\u001f';

    /// <summary>What a <c>{0}</c> slot reads as when a line is shown without its argument (the tips log, #2364).</summary>
    public const string MissingArg = "…";

    /// <summary>The opening of a key token (<c>{key:Interact}</c>).</summary>
    public const string KeyTokenOpen = "{key:";

    /// <summary>Locale-key prefix of the device-neutral word for an action (<c>ui.vega.act.Interact</c> = "[Use]").</summary>
    public const string NeutralActionKeyPrefix = "ui.vega.act.";

    /// <summary>Splits a packed line argument into the <c>{0}</c>, <c>{1}</c>… values.</summary>
    public static string[] SplitArgs(string? lineArg)
        => string.IsNullOrEmpty(lineArg) ? Array.Empty<string>() : lineArg!.Split(ArgSeparator);

    /// <summary>
    /// Replaces <c>{key:Action}</c> tokens with what <paramref name="glyph"/> returns for the action name (a key, a pad
    /// button, a touch label or a neutral word). A token the resolver does not know reads as its action name, so a stale
    /// locale never shows raw braces. Lines without tokens are returned untouched. A token the text puts in parentheses —
    /// "Launch ({key:Interact})" — keeps one pair when the glyph wears its own (a pad's "(X)"): "Launch (X)", never
    /// "Launch ((X))" (#2502).
    /// </summary>
    public static string ExpandKeyTokens(string text, Func<string, string> glyph)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(KeyTokenOpen, StringComparison.Ordinal) < 0)
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length)
        {
            int start = text.IndexOf(KeyTokenOpen, i, StringComparison.Ordinal);
            if (start < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            int end = text.IndexOf('}', start);
            if (end < 0)
            {
                sb.Append(text, i, text.Length - i);
                break;
            }

            sb.Append(text, i, start - i);
            string action = text.Substring(start + KeyTokenOpen.Length, end - start - KeyTokenOpen.Length);
            string resolved = glyph(action);
            string shown = string.IsNullOrEmpty(resolved) ? action : resolved;
            if (IsParenthesised(shown) && start > i && text[start - 1] == '(' && end + 1 < text.Length && text[end + 1] == ')')
            {
                sb.Length--; // the text's "(" just copied — the glyph brings its own pair
                sb.Append(shown);
                i = end + 2;
                continue;
            }

            sb.Append(shown);
            i = end + 1;
        }

        return sb.ToString();
    }

    /// <summary>Whether a control glyph already wears parentheses — the pad's face buttons read "(A)", "(X)", "(Cross)".</summary>
    public static bool IsParenthesised(string? glyph)
        => glyph is { Length: >= 2 } && glyph[0] == '(' && glyph[glyph.Length - 1] == ')';

    /// <summary>A control glyph in parentheses for a prompt — "Board (E)" — without doubling the pair a pad glyph already
    /// has: "Board (X)", never "Board ((X))" (#2502).</summary>
    public static string InParentheses(string glyph) => IsParenthesised(glyph) ? glyph : "(" + glyph + ")";

    /// <summary>A prompt a locale template formatted around a glyph in its own parentheses ("Talk to {0} ({1})"): the doubled
    /// pair around a pad glyph that wears one collapses to one — "Talk to Ana (X)" (#2502). Other text is untouched.</summary>
    public static string CollapseGlyphParentheses(string text, string glyph)
        => IsParenthesised(glyph) && !string.IsNullOrEmpty(text) ? text.Replace("(" + glyph + ")", glyph) : text;

    /// <summary>The action names of every <c>{key:Action}</c> token in <paramref name="text"/>, in order.</summary>
    public static IEnumerable<string> KeyTokenActions(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            yield break;
        }

        int i = 0;
        while (true)
        {
            int start = text!.IndexOf(KeyTokenOpen, i, StringComparison.Ordinal);
            if (start < 0)
            {
                yield break;
            }

            int end = text.IndexOf('}', start);
            if (end < 0)
            {
                yield break;
            }

            yield return text.Substring(start + KeyTokenOpen.Length, end - start - KeyTokenOpen.Length);
            i = end + 1;
        }
    }

    /// <summary>
    /// Composes a line for display: the <c>{key:Action}</c> tokens are expanded FIRST, then the slots are filled from the
    /// packed <paramref name="lineArg"/>. The order is the fix of #2363 — <c>string.Format</c> reads <c>{key:…}</c> as a
    /// malformed placeholder and throws. A format that still fails (a damaged translation) never throws: the
    /// token-expanded line is returned as it is. Without an argument the slots read as <see cref="MissingArg"/>.
    /// </summary>
    public static string Compose(string? text, string? lineArg, Func<string, string> glyph)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        text = ExpandKeyTokens(text!, glyph);
        if (!HasSlot(text))
        {
            return text; // no slot — the common case
        }

        if (string.IsNullOrEmpty(lineArg))
        {
            return NeutralisePlaceholders(text);
        }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, text, SplitArgs(lineArg));
        }
        catch (FormatException)
        {
            return text;
        }
    }

    /// <summary>The device-neutral word for an action: the localized <c>ui.vega.act.&lt;Action&gt;</c> ("[Use]") when
    /// <paramref name="tryLocalize"/> has it (returns null or empty otherwise), else <c>[Action]</c> — never a brace.</summary>
    public static string NeutralActionWord(string action, Func<string, string?> tryLocalize)
    {
        string? word = tryLocalize(NeutralActionKeyPrefix + action);
        return string.IsNullOrEmpty(word) ? "[" + action + "]" : word!;
    }

    /// <summary>Composes a line with device-neutral words for its key tokens — what the server sends as <c>Text</c> to a
    /// client that cannot localize the line's key itself.</summary>
    public static string ComposeNeutral(string? text, string? lineArg, Func<string, string?> tryLocalize)
        => Compose(text, lineArg, action => NeutralActionWord(action, tryLocalize));

    /// <summary>Whether <paramref name="text"/> has a <c>{n}</c> slot — any number, not only <c>{0}</c> (#2473: the
    /// tutorial's part card fills "Part {1} of {2}" from its argument's second and third values).</summary>
    public static bool HasSlot(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        for (int i = text!.IndexOf('{'); i >= 0 && i < text.Length - 2; i = text.IndexOf('{', i + 1))
        {
            int j = i + 1;
            while (j < text.Length && char.IsDigit(text[j]))
            {
                j++;
            }

            if (j > i + 1 && j < text.Length && text[j] == '}')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Replaces the <c>{0}</c>…<c>{9}</c> slots with <see cref="MissingArg"/>.</summary>
    private static string NeutralisePlaceholders(string text)
    {
        for (int i = 0; i < 10; i++)
        {
            text = text.Replace("{" + i.ToString(CultureInfo.InvariantCulture) + "}", MissingArg);
        }

        return text;
    }
}
