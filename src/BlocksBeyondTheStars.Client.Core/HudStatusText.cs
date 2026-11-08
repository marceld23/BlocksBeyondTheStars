// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3411: the HUD status lines that used to be hardcoded English ("Boarded Blumenhall." in a German game, "Mode: Survival ·
/// PvP: Off", "Arriving at …") — their locale keys and the mapping of the server's rule words to the words the world
/// options already use.
/// </summary>
public static class HudStatusText
{
    /// <summary>"Boarded {0}." — the player stepped aboard a station.</summary>
    public const string BoardedKey = "ui.station.boarded";

    /// <summary>"Mode: {0} · PvP: {1}" — the world's rules after a join.</summary>
    public const string RulesLineKey = "ui.hud.rules_line";

    /// <summary>"Hyperjump → {0}…" — a jump lands on a new world.</summary>
    public const string HyperjumpToKey = "ui.travel.hyperjump_to";

    /// <summary>"Arriving at {0}…" — a landing on a new world.</summary>
    public const string ArrivingAtKey = "ui.travel.arriving_at";

    /// <summary>The PvP words in the server's order (<c>PvpMode</c>) — index n reads <c>ui.worldopt.pvp.n</c>.</summary>
    private static readonly string[] PvpWords = { "Off", "DuelsOnly", "GroupBased", "On" };

    /// <summary>The locale key of the server's game mode word (<c>GameMode</c>: Survival / Creative), or "" for an unknown
    /// word (the caller then shows the word itself).</summary>
    public static string ModeKey(string? gameMode) => gameMode switch
    {
        "Survival" => "ui.worldopt.pmode.1",
        "Creative" => "ui.worldopt.pmode.2",
        _ => string.Empty,
    };

    /// <summary>The locale key of the server's PvP word, or "" for an unknown word.</summary>
    public static string PvpKey(string? pvp)
    {
        int i = Array.IndexOf(PvpWords, pvp ?? string.Empty);
        return i < 0 ? string.Empty : "ui.worldopt.pvp." + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The rules line: <paramref name="get"/> resolves a key; an unknown mode or PvP word is shown as sent.</summary>
    public static string RulesLine(Func<string, string> get, string? gameMode, string? pvp)
    {
        string mode = ModeKey(gameMode) is { Length: > 0 } mk ? get(mk) : gameMode ?? string.Empty;
        string pv = PvpKey(pvp) is { Length: > 0 } pk ? get(pk) : pvp ?? string.Empty;
        return Format(get(RulesLineKey), mode, pv);
    }

    /// <summary>Fills {0} and {1} of a localized pattern.</summary>
    public static string Format(string pattern, string arg0, string arg1 = "")
        => (pattern ?? string.Empty).Replace("{0}", arg0 ?? string.Empty).Replace("{1}", arg1 ?? string.Empty);
}
