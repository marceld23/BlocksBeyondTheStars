// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// The old log entries in a derelict station's supply crates (#2633): short notes the crew left behind — who wrote it
/// (a coined name, <see cref="NameGenerator.Person"/>), their job on the station, what happened (a meteor storm, the
/// reactor, a leaking oxygen tank, …) and how everyone got out. A log is a pure function of a 32-bit seed, so the item
/// that carries it is just <c>derelict_log#x&lt;seed&gt;</c> (<see cref="ItemKey.WithSeed"/>): the server and every client
/// write the same text from it, in the reader's own language, with no text stored or sent.
/// <para>The seed is laid out so one derelict tells ONE story: bits 16–31 are the station's (what happened, how they got
/// out, the day — the same for all its logs), bits 4–15 the writer's (name, telling, last words) and bits 0–3 the log's
/// index on the station, which also hands every writer of a station a different job.</para>
/// <para>Determinism: the picks use <see cref="Random"/> with a fixed seed (the legacy seeded algorithm, identical on
/// .NET and Unity's Mono), like the coined species names.</para>
/// </summary>
public static class DerelictLogs
{
    /// <summary>The base key of the readable log item (data/items.json).</summary>
    public const string ItemKeyBase = "derelict_log";

    /// <summary>Discoveries-ledger prefix of a found log: <c>log:&lt;8 hex digits&gt;</c> (the Codex lists and re-reads them).</summary>
    public const string LedgerPrefix = "log:";

    /// <summary>How many logs one derelict can carry — the index rides in four bits of the seed.</summary>
    public const int MaxPerStation = 16;

    /// <summary>The seed of log number <paramref name="index"/> of a derelict — stable for a world seed and station id.
    /// Never 0 (an item key reads 0 as "no seed").</summary>
    public static uint SeedFor(long worldSeed, string stationId, int index)
    {
        if (index < 0 || index >= MaxPerStation)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, "a derelict holds at most " + MaxPerStation + " logs");
        }

        uint story = Fold(WorldGenerator.StableHash("derelict-log-story:" + stationId) ^ worldSeed) & 0xFFFF;
        uint writer = Fold(WorldGenerator.StableHash("derelict-log:" + stationId + ":" + index.ToString(CultureInfo.InvariantCulture))
                           ^ (worldSeed * 31)) & 0xFFF;
        uint seed = (story << 16) | (writer << 4) | (uint)index;
        return seed == 0 ? 1u << 4 : seed;
    }

    /// <summary>The item key of the log with this seed.</summary>
    public static string ItemFor(uint seed) => ItemKey.WithSeed(ItemKeyBase, seed);

    /// <summary>True for the base key of the log item.</summary>
    public static bool IsLogItem(string? itemKey) => ItemKey.Base(itemKey ?? string.Empty) == ItemKeyBase;

    /// <summary>The seed a log item carries, or 0 when the key is no log (or carries none).</summary>
    public static uint SeedOfItem(string? itemKey) => IsLogItem(itemKey) ? ItemKey.Seed(itemKey) : 0u;

    /// <summary>The Discoveries-ledger key of a found log.</summary>
    public static string LedgerKey(uint seed) => LedgerPrefix + seed.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>The seed in a Discoveries-ledger key (<c>log:&lt;hex&gt;</c>), or 0 when it is none.</summary>
    public static uint SeedOfLedgerKey(string? key)
    {
        if (key is null || !key.StartsWith(LedgerPrefix, StringComparison.Ordinal))
        {
            return 0u;
        }

        return uint.TryParse(key.Substring(LedgerPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint seed) ? seed : 0u;
    }

    /// <summary>Writes the log of <paramref name="seed"/> from the fragments — null when the seed is 0 or the fragments
    /// are missing (a data folder without <c>derelict_logs.json</c>).</summary>
    public static DerelictLog? Compose(uint seed, DerelictLogDefinition? fragments)
    {
        if (seed == 0 || fragments is null || fragments.IsEmpty)
        {
            return null;
        }

        int index = (int)(seed & 0xF);
        var story = new Random(unchecked((int)((seed >> 16) * 0x9E3779B1u)));
        int day = story.Next(fragments.DayMin, Math.Max(fragments.DayMin, fragments.DayMax) + 1);
        var incident = fragments.Incidents[story.Next(fragments.Incidents.Count)];
        string escape = fragments.Escapes[story.Next(fragments.Escapes.Count)];
        int jobStart = story.Next(fragments.Jobs.Count);

        var writer = new Random(unchecked((int)(seed * 0x85EBCA6Bu)));
        string name = NameGenerator.Person(writer);
        int variant = 1 + writer.Next(Math.Max(1, incident.Variants));
        string closing = fragments.Closings[writer.Next(fragments.Closings.Count)];

        return new DerelictLog
        {
            Seed = seed,
            Writer = name,
            Day = day,
            JobKey = DerelictLogDefinition.JobKey(fragments.Jobs[(jobStart + index) % fragments.Jobs.Count]),
            IncidentKey = DerelictLogDefinition.IncidentKey(incident.Key, variant),
            EscapeKey = DerelictLogDefinition.EscapeKey(escape),
            ClosingKey = DerelictLogDefinition.ClosingKey(closing),
        };
    }

    private static uint Fold(long value)
    {
        unchecked
        {
            ulong v = (ulong)value;
            v ^= v >> 33;
            v *= 0xFF51AFD7ED558CCDUL;
            v ^= v >> 33;
            return (uint)v ^ (uint)(v >> 32);
        }
    }
}

/// <summary>One composed derelict log (#2633): the writer and the locale keys of its parts. <see cref="Render"/> writes it
/// in a language.</summary>
public sealed class DerelictLog
{
    public uint Seed { get; set; }

    /// <summary>The writer's coined name — language-neutral, shown as-is.</summary>
    public string Writer { get; set; } = string.Empty;

    /// <summary>The station day the entry is dated.</summary>
    public int Day { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string IncidentKey { get; set; } = string.Empty;

    public string EscapeKey { get; set; } = string.Empty;

    public string ClosingKey { get; set; } = string.Empty;

    /// <summary>Every locale key this log reads.</summary>
    public IReadOnlyList<string> TextKeys => new[] { DerelictLogDefinition.HeaderKey, JobKey, IncidentKey, EscapeKey, ClosingKey };

    /// <summary>The log as the reader shows it: the dated header, then who they are and what happened, then how everyone
    /// got out and their last words. <paramref name="text"/> resolves a locale key (the localizer's <c>Get</c>).</summary>
    public string Render(Func<string, string> text)
    {
        string header = text(DerelictLogDefinition.HeaderKey);
        try
        {
            header = string.Format(CultureInfo.InvariantCulture, header, Day, Writer);
        }
        catch (FormatException)
        {
            header = Writer; // a broken translation must not break the reader
        }

        return header + "\n\n" + text(JobKey) + " " + text(IncidentKey) + "\n\n" + text(EscapeKey) + " " + text(ClosingKey);
    }
}
