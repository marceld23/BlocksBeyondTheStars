// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3410 (Justus: "Was ist das?" at the wreck "Lavoo"): the ship scanner's card of a derelict ship wreck says WHAT it is.
/// Its title names the kind before the generated name ("Wreck · Lavoo"), and its description shows the kind's info line
/// ("Wreck hull — break it down with the mining laser …") above the salvage contents — the contents alone used to hide it.
/// </summary>
public static class WreckReadout
{
    /// <summary>The scan kind of a wreck readout.</summary>
    public const string Kind = "wreck";

    /// <summary>"Wreck · Lavoo" — the localized kind word and the wreck's name; the kind alone when it has no name.</summary>
    public static string Title(string kindWord, string? name)
        => string.IsNullOrEmpty(name) ? kindWord ?? string.Empty : (kindWord ?? string.Empty) + " · " + name;

    /// <summary>The info line above the contents line; either alone when the other is empty.</summary>
    public static string Info(string? infoLine, string? contentsLine)
    {
        if (string.IsNullOrEmpty(infoLine))
        {
            return contentsLine ?? string.Empty;
        }

        return string.IsNullOrEmpty(contentsLine) ? infoLine! : infoLine + "\n" + contentsLine;
    }
}
