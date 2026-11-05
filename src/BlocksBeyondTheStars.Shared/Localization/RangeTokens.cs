// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Localization;

/// <summary>
/// The range placeholders of the player-facing texts (#3206). A text that names a range the code defines writes a token —
/// "Place it within <c>{range_base_pad}</c> blocks of your base core" — instead of the number, and the locale loader
/// (<see cref="Content.ContentLoader.ParseLocaleTable"/>) puts the constant in when it parses a table. Every path that
/// shows a locale text (the item and block descriptions, the Codex, VEGA's lines and her tips log, dialogs, labels, the
/// server's own <c>Localize</c>) therefore reads the number the code uses, and changing a range is a one-line constant
/// change: no locale file repeats the number, so none of the fourteen can drift behind it.
/// <para>The tokens are ordinary <c>{name}</c> placeholders to the translation tooling and the locale tests, so every
/// translation keeps them like any other placeholder. A server message that reports the server's own rule passes the
/// number as its argument instead (<c>@srv.basepad.no_base:64</c> fills <c>{name}</c>): the server it came from is the
/// authority, whatever build the client runs.</para>
/// <para>Only constants both sides compile (Shared) can be tokens; a range that lives in the server alone reaches a
/// text as a message argument.</para>
/// </summary>
public static class RangeTokens
{
    /// <summary>What every token starts with — a text without it is returned untouched.</summary>
    public const string TokenOpen = "{range_";

    /// <summary>Token → the number it stands for, in the invariant culture.</summary>
    private static readonly KeyValuePair<string, string>[] Table =
    {
        Token("base_pad", BasePadRules.ReachXZ),
        Token("base_discovery", BaseRules.DiscoveryRadius),
        Token("base_no_spawn", WorldConstants.BaseSpawnExclusionRadius),
        Token("base_zone", WorldConstants.BaseZoneRadius),
        Token("sentry", WorldConstants.SentryRange),
        Token("rail_autolink", RailRules.AutoLinkRange),
        Token("sensor_near", CrystalNetRules.ProximityRadii[0]),
        Token("sensor_mid", CrystalNetRules.ProximityRadii[1]),
        Token("sensor_far", CrystalNetRules.ProximityRadii[2]),
    };

    /// <summary>Every token with the number it is filled with (the tests and the docs list them from here).</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> All => Table;

    /// <summary><paramref name="text"/> with every range token replaced by its number; a text without a token (and null)
    /// comes back as it is.</summary>
    public static string Fill(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(TokenOpen, StringComparison.Ordinal) < 0)
        {
            return text;
        }

        foreach (var token in Table)
        {
            text = text.Replace(token.Key, token.Value);
        }

        return text;
    }

    /// <summary>Fills the range tokens of every value of a parsed locale table in place.</summary>
    public static void FillTable(IDictionary<string, string> table)
    {
        List<string>? keys = null;
        foreach (var entry in table)
        {
            if (entry.Value is { } value && value.IndexOf(TokenOpen, StringComparison.Ordinal) >= 0)
            {
                (keys ??= new List<string>()).Add(entry.Key);
            }
        }

        if (keys is null)
        {
            return;
        }

        foreach (string key in keys)
        {
            table[key] = Fill(table[key]);
        }
    }

    private static KeyValuePair<string, string> Token(string name, float value)
        => new(TokenOpen + name + "}", value.ToString("0.##", CultureInfo.InvariantCulture));
}
