// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// What the friendly hunter robot says in VEGA's panel (#3394-#3396, Justus' "talking robot" idea): its own lines
/// (<c>robot.talk.&lt;n&gt;</c>) and short exchanges in which VEGA asks and the robot answers (<c>robot.vega.&lt;n&gt;.q</c> /
/// <c>.a</c>). The texts live in the locale tables; this class owns the pool sizes, the keys and the deterministic picks, so
/// the server and the tests agree on them:
/// <list type="bullet">
/// <item>each pool plays in a per-player shuffled order without repeats until every entry was heard, then reshuffles (the
/// next round never opens with the line the last one closed on) — the progress is one counter per pool;</item>
/// <item>about one talk in three is an exchange instead of a single line, rolled from the player id and the talk count;</item>
/// <item>the robot speaks by itself every 10-20 minutes, the first time 5-10 minutes after a join.</item>
/// </list>
/// Adding a line is two locale entries (en + de) and one higher count here.
/// </summary>
public static class RobotChatter
{
    /// <summary>The robot's own lines: <c>robot.talk.1</c> .. <c>robot.talk.&lt;LineCount&gt;</c>.</summary>
    public const int LineCount = 14;

    /// <summary>The exchanges: <c>robot.vega.1.q</c>/<c>.a</c> .. <c>robot.vega.&lt;ExchangeCount&gt;.q</c>/<c>.a</c>.</summary>
    public const int ExchangeCount = 6;

    /// <summary>The <c>ShipAiLine.Speaker</c> of the robot's lines (empty = VEGA).</summary>
    public const string SpeakerId = "robot";

    /// <summary>The robot's colour in VEGA's panel — its light-blue visor (<c>RobotSpecies().EyeRgb</c>).</summary>
    public const int SpeakerRgb = 0x7FE8FF;

    /// <summary>The prefix every chatter key starts with — the client keeps these lines out of the Journal and VEGA's log.</summary>
    public const string KeyPrefix = "robot.";

    /// <summary>The <c>PlayerState.AchievementCounters</c> entry counting the robot's own lines heard (persisted).</summary>
    public const string LinesHeardCounter = "robot_talk_lines";

    /// <summary>The <c>PlayerState.AchievementCounters</c> entry counting the exchanges heard (persisted).</summary>
    public const string ExchangesHeardCounter = "robot_talk_exchanges";

    /// <summary>One talk in this many is an exchange (on average).</summary>
    public const int ExchangeOneIn = 3;

    /// <summary>The first ambient line comes this many seconds after a join at the earliest …</summary>
    public const double FirstAmbientMinSeconds = 300.0;

    /// <summary>… and at the latest this many.</summary>
    public const double FirstAmbientMaxSeconds = 600.0;

    /// <summary>The gap between two ambient lines: at least this many seconds …</summary>
    public const double AmbientMinSeconds = 600.0;

    /// <summary>… and at most this many.</summary>
    public const double AmbientMaxSeconds = 1200.0;

    /// <summary>The locale key of the robot's own line <paramref name="n"/> (1-based).</summary>
    public static string LineKey(int n) => KeyPrefix + "talk." + n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>The locale key of VEGA's question in exchange <paramref name="n"/> (1-based).</summary>
    public static string QuestionKey(int n) => KeyPrefix + "vega." + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".q";

    /// <summary>The locale key of the robot's answer in exchange <paramref name="n"/> (1-based).</summary>
    public static string AnswerKey(int n) => KeyPrefix + "vega." + n.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".a";

    /// <summary>Whether <paramref name="lineKey"/> is one of the robot's chatter lines (its own, or VEGA's question to it).</summary>
    public static bool IsChatterKey(string? lineKey)
        => lineKey is not null && lineKey.StartsWith(KeyPrefix, StringComparison.Ordinal);

    /// <summary>Every chatter key, in pool order — what the locale tables must hold.</summary>
    public static IEnumerable<string> AllKeys()
    {
        for (int n = 1; n <= LineCount; n++)
        {
            yield return LineKey(n);
        }

        for (int n = 1; n <= ExchangeCount; n++)
        {
            yield return QuestionKey(n);
            yield return AnswerKey(n);
        }
    }

    /// <summary>Whether the talk number <paramref name="talk"/> (0-based, lines and exchanges together) of this player is an
    /// exchange — about one in <see cref="ExchangeOneIn"/>, fixed per player and talk so tests can drive it.</summary>
    public static bool IsExchange(string playerId, int talk)
        => StableHashing.Index("robot:exchange:" + (playerId ?? string.Empty) + ":" + talk.ToString(System.Globalization.CultureInfo.InvariantCulture), ExchangeOneIn) == 0;

    /// <summary>The next talk of a player who has heard <paramref name="linesHeard"/> own lines and
    /// <paramref name="exchangesHeard"/> exchanges: whether it is an exchange, and which entry (1-based) of that pool.</summary>
    public static (bool Exchange, int Number) Next(string playerId, int linesHeard, int exchangesHeard)
    {
        int lines = Math.Max(0, linesHeard), exchanges = Math.Max(0, exchangesHeard);
        bool exchange = IsExchange(playerId, lines + exchanges);
        return exchange
            ? (true, ShuffledEntry(playerId, "exchange", exchanges, ExchangeCount) + 1)
            : (false, ShuffledEntry(playerId, "line", lines, LineCount) + 1);
    }

    /// <summary>The entry (0-based) a pool of <paramref name="count"/> plays as its <paramref name="heard"/>-th: rounds of a
    /// per-player shuffle, each a permutation (no repeat until all were heard). A round never opens with the entry the round
    /// before closed on, so the same line is never heard twice in a row.</summary>
    public static int ShuffledEntry(string playerId, string pool, int heard, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        int round = Math.Max(0, heard) / count, slot = Math.Max(0, heard) % count;
        int[] order = Round(playerId, pool, round, count);
        if (round > 0 && order[0] == Round(playerId, pool, round - 1, count)[count - 1])
        {
            (order[0], order[1]) = (order[1], order[0]);
        }

        return order[slot];
    }

    /// <summary>One round's shuffle: the entries sorted by a stable hash of the player, the pool, the round and the entry.</summary>
    private static int[] Round(string playerId, string pool, int round, int count)
    {
        string salt = "robot:" + pool + ":" + (playerId ?? string.Empty) + ":" + round.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":";
        return Enumerable.Range(0, count)
            .OrderBy(i => StableHashing.Mixed(salt + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            .ThenBy(i => i)
            .ToArray();
    }

    /// <summary>Seconds from a join to the robot's first ambient line (<see cref="FirstAmbientMinSeconds"/> ..
    /// <see cref="FirstAmbientMaxSeconds"/>), rolled per player.</summary>
    public static double FirstAmbientDelay(string playerId)
        => Roll("robot:ambient:first:" + (playerId ?? string.Empty), FirstAmbientMinSeconds, FirstAmbientMaxSeconds);

    /// <summary>Seconds from ambient line <paramref name="spoken"/> to the next (<see cref="AmbientMinSeconds"/> ..
    /// <see cref="AmbientMaxSeconds"/>), rolled per player and line.</summary>
    public static double AmbientInterval(string playerId, int spoken)
        => Roll("robot:ambient:" + (playerId ?? string.Empty) + ":" + spoken.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AmbientMinSeconds, AmbientMaxSeconds);

    private static double Roll(string key, double min, double max) => min + (StableHashing.Unit(key) * (max - min));
}
