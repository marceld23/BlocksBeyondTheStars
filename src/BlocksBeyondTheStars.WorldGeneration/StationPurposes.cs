// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The lively places: what a space station is for. The purpose picks the station's kit (<c>StructureKit.Purposes</c>),
/// its first vendor's goods, the look of its crew and the head line a player reads when boarding; "" is a station
/// without a purpose — every station composed before terrain generation 46.
/// </summary>
public static class StationPurposes
{
    public const string None = "";
    public const string Trade = "trade";
    public const string Mining = "mining";
    public const string Research = "research";

    public static readonly string[] All = { Trade, Mining, Research };

    /// <summary>The colours a station's band can have (0xRRGGBB): clear, friendly tones that read on iron walls.</summary>
    public static readonly int[] Colours =
    {
        0x3D7EBF, 0x2FA48E, 0xD9AE33, 0xC24B5A, 0x8A63BF, 0xD97B29, 0x5C9950, 0x5FB6E0,
    };

    /// <summary>The trade theme of a purpose's first vendor and of its crew's clothes (the themes vendors already have).</summary>
    public static string ThemeOf(string purpose) => purpose switch
    {
        Mining => "miners",
        Research => "researchers",
        _ => "traders",
    };

    /// <summary>The market theme of a mining station's own exchange: the miners' goods at the station's rates
    /// (<c>data/recipes.json</c>, <c>marketTheme</c>).</summary>
    public const string OreExchange = "ore_exchange";

    /// <summary>The market theme of a research station's own exchange: data fragments at the station's rates.</summary>
    public const string DataExchange = "data_exchange";

    /// <summary>What a purpose's FIRST vendor deals in — the station's own exchange on a mining or a research station (the
    /// trade's goods at better rates), the plain trade everywhere else. Its clothes stay the trade's
    /// (<see cref="LookOf"/>).</summary>
    public static string ServiceThemeOf(string purpose) => purpose switch
    {
        Mining => OreExchange,
        Research => DataExchange,
        _ => ThemeOf(purpose),
    };

    /// <summary>The trade a vendor theme dresses as: a station exchange looks like the trade it belongs to (a miner, a
    /// researcher); every other theme is its own look.</summary>
    public static string LookOf(string theme) => theme switch
    {
        OreExchange => "miners",
        DataExchange => "researchers",
        _ => theme,
    };

    /// <summary>Whether a text is one of the three purposes.</summary>
    public static bool IsKnown(string? purpose) => purpose is Trade or Mining or Research;

    /// <summary>The rooms a research station puts upstairs (#3871: its floor plan has decks): its own rooms, by the module
    /// key's ending (<c>st_&lt;tier&gt;_lab</c> …).</summary>
    public static readonly string[] UpstairsRooms = { "lab", "observatory", "archive" };

    /// <summary>#3871: whether a module of a <paramref name="purpose"/> station climbs while it is composed — on a research
    /// station a ladder junction takes an upward port first (so the decks stack) and its own rooms the upper deck first.
    /// Nothing of any other purpose, or of a station without one, climbs: those compose exactly as before.</summary>
    public static bool Climbs(string purpose, string module)
    {
        if (purpose != Research)
        {
            return false;
        }

        if (module.EndsWith("_ladder_junction", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (string room in UpstairsRooms)
        {
            if (module.EndsWith("_" + room, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The purpose of a station, by its seed: a station in the start system (<paramref name="home"/>) is a trade station
    /// — the first one a new player boards sells what they expect —, one in a system with an asteroid belt
    /// (<paramref name="belt"/>) leans to mining, every other is one of the three by weight.
    /// </summary>
    public static string For(long seed, bool home, bool belt)
    {
        if (home)
        {
            return Trade;
        }

        int roll = BlocksBeyondTheStars.Shared.World.StableHashing.Index("station:purpose", seed, 100);
        if (belt)
        {
            return roll < 55 ? Mining : roll < 80 ? Trade : Research;
        }

        return roll < 40 ? Trade : roll < 65 ? Mining : Research;
    }

    /// <summary>The band colour of a station, by its seed.</summary>
    public static int ColourFor(long seed)
        => Colours[BlocksBeyondTheStars.Shared.World.StableHashing.Index("station:colour", seed, Colours.Length)];
}
