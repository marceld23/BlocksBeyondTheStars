// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The data of "the village lives" (epic #2589), from <c>data/village_life.json</c>: the presents a villager friend
/// hands over (#2598), the little conversations villagers have with each other and the village festival. Optional
/// content — a data folder without the file (or without a section) simply has none of that part; every section is
/// optional on its own.
/// </summary>
public sealed class VillageLifeDefinition
{
    /// <summary>Conversation topics villagers talk about among themselves (C1), in authored order.</summary>
    public List<VillageChatterTopic> Chatter { get; set; } = new();

    /// <summary>The pool a villager friend picks a present from (#2598, C3) — one row per present, small and
    /// kid-friendly (berries, a fruit, a lantern …). Empty → friends give nothing.</summary>
    public List<ItemAmount> FriendGifts { get; set; } = new();

    /// <summary>The village festival (C6): the quartermaster's present and the cheer lines.</summary>
    public VillageFestivalDefinition Festival { get; set; } = new();
}

/// <summary>One conversation topic two villagers may pick (C1, #2600): when it fits, what it needs, how likely it is and
/// the lines the two speakers alternate (locale keys).</summary>
public sealed class VillageChatterTopic
{
    /// <summary>The most lines one conversation has (and the fewest: <see cref="MinLines"/>).</summary>
    public const int MaxLines = 4;

    /// <summary>The fewest lines one conversation has.</summary>
    public const int MinLines = 2;

    /// <summary>The longest a chatter line may be in English and German (a speech bubble, not a paragraph).</summary>
    public const int MaxLineChars = 56;

    public string Key { get; set; } = string.Empty;

    /// <summary>"day", "evening" or "any" (never at night — villagers sleep).</summary>
    public string When { get; set; } = "any";

    /// <summary>What must be true in the world for the topic — one of <see cref="VillageChatterNeeds"/> (e.g.
    /// "weather_wet", "trader", "deed:camp_cleared"); "" = always.</summary>
    public string Needs { get; set; } = string.Empty;

    /// <summary>How likely the topic is among the fitting ones (at least 1; a deed topic counts five times while its hero
    /// listens).</summary>
    public int Weight { get; set; } = 1;

    /// <summary>A few English words about the topic for the AI re-wording ("rain on the gardens"); "" = the key.</summary>
    public string Hint { get; set; } = string.Empty;

    /// <summary>The lines, speaker A first, then alternating (locale keys, <see cref="MinLines"/>–<see cref="MaxLines"/>).
    /// Placeholders: <c>{a}</c>/<c>{b}</c> (the speakers), <c>{place}</c> (the village), plus what the need provides
    /// (<see cref="VillageChatterNeeds.TokensFor"/>).</summary>
    public List<string> Lines { get; set; } = new();
}

/// <summary>
/// #2600: the vocabulary of <see cref="VillageChatterTopic.Needs"/> — what must be true in the world for a topic — and
/// the placeholders each need provides to the lines. Shared by the content validation, the server and the tests.
/// </summary>
public static class VillageChatterNeeds
{
    /// <summary>Something wet is falling at the village (rain, drizzle).</summary>
    public const string WeatherWet = "weather_wet";

    /// <summary>A thunderstorm at the village.</summary>
    public const string WeatherStorm = "weather_storm";

    /// <summary>It snows at the village (or a blizzard).</summary>
    public const string WeatherSnow = "weather_snow";

    /// <summary>A clear sky at the village (sunny by day, stars in the evening).</summary>
    public const string WeatherClear = "weather_clear";

    /// <summary>Fog at the village.</summary>
    public const string WeatherFog = "weather_fog";

    /// <summary>A trader ship is parked within 400 m (<c>{trader}</c> = its trader's name).</summary>
    public const string Trader = "trader";

    /// <summary>An uncleared bandit camp lies within 400 m (bandits active on the world).</summary>
    public const string CampNear = "camp_near";

    /// <summary>A listening player is an acquaintance or friend of the first speaker (<c>{player}</c> = them).</summary>
    public const string PlayerKnown = "player_known";

    /// <summary>The village's festival is planned and waits for its hero (<c>{player}</c> = the hero; lit up by the
    /// festival package).</summary>
    public const string FestivalPending = "festival_pending";

    /// <summary>Prefix of a deed need: <c>deed:&lt;kind&gt;</c> (a <c>DeedKinds</c> kind recorded on this world in the last
    /// three hours of playtime; <c>{player}</c> = its hero).</summary>
    public const string DeedPrefix = "deed:";

    /// <summary>Every need that is not a deed.</summary>
    public static readonly IReadOnlyList<string> Plain = new[]
    {
        WeatherWet, WeatherStorm, WeatherSnow, WeatherClear, WeatherFog, Trader, CampNear, PlayerKnown, FestivalPending,
    };

    /// <summary>Placeholders every line may use: the two speakers and the village.</summary>
    public static readonly IReadOnlyList<string> CommonTokens = new[] { "{a}", "{b}", "{place}" };

    /// <summary>Whether <paramref name="needs"/> is "" or a known need (a deed need must name a known deed kind).</summary>
    public static bool IsKnown(string? needs)
    {
        if (string.IsNullOrEmpty(needs))
        {
            return true;
        }

        if (needs!.StartsWith(DeedPrefix, System.StringComparison.Ordinal))
        {
            string kind = needs.Substring(DeedPrefix.Length);
            foreach (var k in DeedKinds.All)
            {
                if (k == kind)
                {
                    return true;
                }
            }

            return false;
        }

        foreach (var n in Plain)
        {
            if (n == needs)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The placeholders a topic with this need may use in its lines: <see cref="CommonTokens"/>, plus
    /// <c>{player}</c> for a deed, a known player or a pending festival, and <c>{trader}</c> for a landed trader.</summary>
    public static IReadOnlyList<string> TokensFor(string? needs)
    {
        var tokens = new List<string>(CommonTokens);
        if (!string.IsNullOrEmpty(needs)
            && (needs!.StartsWith(DeedPrefix, System.StringComparison.Ordinal) || needs == PlayerKnown || needs == FestivalPending))
        {
            tokens.Add("{player}");
        }

        if (needs == Trader)
        {
            tokens.Add("{trader}");
        }

        return tokens;
    }
}

/// <summary>The village festival's data (C6).</summary>
public sealed class VillageFestivalDefinition
{
    /// <summary>What the quartermaster hands the hero when the festival starts.</summary>
    public List<ItemAmount> Gift { get; set; } = new();

    /// <summary>The cheer lines villagers call out during the festival (locale keys).</summary>
    public List<string> Cheers { get; set; } = new();
}
