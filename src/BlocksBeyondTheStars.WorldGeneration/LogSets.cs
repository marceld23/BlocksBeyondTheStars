// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// One family of readable, seed-written log items (#2633, #3341): its item base key (the item key carries the seed,
/// <c>&lt;base&gt;#x&lt;seed&gt;</c>), its Discoveries-ledger prefix (<c>&lt;prefix&gt;&lt;8 hex digits&gt;</c>), the
/// fragments its text is written from (<see cref="DerelictLogs.Compose"/>) and the locale keys of its pick-up toast and
/// its Codex list. The server (looting → Discoveries ledger), the inventory reader, the Codex and the icon lookup all ask
/// <see cref="LogSets"/> instead of naming a set, so a log of any set reads, records and lists the same way.
/// </summary>
public sealed class LogSet
{
    private readonly Func<GameContent, DerelictLogDefinition> _fragments;

    internal LogSet(string itemKeyBase, string ledgerPrefix, string foundToastKey, string codexHeadingKey,
        Func<GameContent, DerelictLogDefinition> fragments)
    {
        ItemKeyBase = itemKeyBase;
        LedgerPrefix = ledgerPrefix;
        FoundToastKey = foundToastKey;
        CodexHeadingKey = codexHeadingKey;
        _fragments = fragments;
    }

    /// <summary>The base key of the log item (data/items.json).</summary>
    public string ItemKeyBase { get; }

    /// <summary>The Discoveries-ledger prefix of a found log of this set (<c>log:</c>, <c>gdslog:</c>).</summary>
    public string LedgerPrefix { get; }

    /// <summary>The toast when a player takes a new log of this set out of a crate: <c>{0}</c> the writer.</summary>
    public string FoundToastKey { get; }

    /// <summary>The heading of this set's list in the Codex's Discoveries chapter.</summary>
    public string CodexHeadingKey { get; }

    /// <summary>The fragments this set's logs are written from, or null without content.</summary>
    public DerelictLogDefinition? Fragments(GameContent? content) => content is null ? null : _fragments(content);

    /// <summary>The item key of this set's log with this seed.</summary>
    public string ItemFor(uint seed) => ItemKey.WithSeed(ItemKeyBase, seed);

    /// <summary>True for a log item of this set (any seed).</summary>
    public bool IsLogItem(string? itemKey) => ItemKey.Base(itemKey ?? string.Empty) == ItemKeyBase;

    /// <summary>The seed a log item of this set carries, or 0 when the key is none (or carries none).</summary>
    public uint SeedOfItem(string? itemKey) => IsLogItem(itemKey) ? ItemKey.Seed(itemKey) : 0u;

    /// <summary>The Discoveries-ledger key of a found log of this set.</summary>
    public string LedgerKey(uint seed) => LedgerPrefix + seed.ToString("x8", CultureInfo.InvariantCulture);

    /// <summary>The seed in a Discoveries-ledger key of this set, or 0 when it is none.</summary>
    public uint SeedOfLedgerKey(string? key)
    {
        if (key is null || !key.StartsWith(LedgerPrefix, StringComparison.Ordinal))
        {
            return 0u;
        }

        return uint.TryParse(key.Substring(LedgerPrefix.Length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint seed) ? seed : 0u;
    }

    /// <summary>Writes this set's log of <paramref name="seed"/> — null for seed 0 or without its fragments.</summary>
    public DerelictLog? Compose(uint seed, GameContent? content) => DerelictLogs.Compose(seed, Fragments(content));

    /// <summary>The logs of this set among Discoveries-ledger keys (the Codex re-reads them from their seeds), in the order
    /// given; every other key is skipped.</summary>
    public IEnumerable<(string LedgerKey, DerelictLog Log)> FoundIn(IEnumerable<string> ledgerKeys, GameContent? content)
    {
        foreach (var key in ledgerKeys)
        {
            if (Compose(SeedOfLedgerKey(key), content) is { } log)
            {
                yield return (key, log);
            }
        }
    }
}

/// <summary>
/// Every set of readable log items (#2633, #3341): the derelict stations' supply-crate logs and the G.D.S. logs of a buried
/// ruin. Code that handles "a log" — taking one out of a crate, reading it in the inventory, listing it in the Codex,
/// filing its icon — resolves the set here, so a new set is one entry in <see cref="All"/>.
/// </summary>
public static class LogSets
{
    /// <summary>The old logs in a derelict station's supply crates (#2633): <c>derelict_log#x…</c>, ledger <c>log:…</c>.</summary>
    public static readonly LogSet Derelict = new(DerelictLogs.ItemKeyBase, DerelictLogs.LedgerPrefix,
        "srv.derelict.log_found", "ui.wiki.discoveries.log", c => c.DerelictLogs);

    /// <summary>The G.D.S. logs in the crates of a buried G.D.S. ruin (#3341): <c>gds_log#x…</c>, ledger <c>gdslog:…</c>.</summary>
    public static readonly LogSet Gds = new(GdsLogs.ItemKeyBase, GdsLogs.LedgerPrefix,
        "srv.gds.log_found", "ui.wiki.discoveries.gds_log", c => c.GdsLogs);

    /// <summary>Every log set, in the order the Codex lists them.</summary>
    public static IReadOnlyList<LogSet> All { get; } = new[] { Derelict, Gds };

    /// <summary>The set a log item belongs to, or null when the key is no log.</summary>
    public static LogSet? OfItem(string? itemKey)
    {
        foreach (var set in All)
        {
            if (set.IsLogItem(itemKey))
            {
                return set;
            }
        }

        return null;
    }

    /// <summary>True for a log item of any set.</summary>
    public static bool IsLogItem(string? itemKey) => OfItem(itemKey) is not null;
}
