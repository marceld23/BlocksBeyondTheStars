// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3642: how the client names a LOST CIVILIZATION — the one home of the locale keys and ledger shapes the planet map, the
/// scanner card and the Codex share. The words come from the civilization file (<see cref="CivilizationDefinition"/>), which
/// the client loads with the same content as the server: the people's name, the Codex title and text. Unity-free.
/// </summary>
public static class CivilizationText
{
    /// <summary>The map/POI type of a civilization site (the server's <c>GameServer.CivilizationMapType</c>) — also the kind of
    /// its scan readout and of its silhouette in the landmark list.</summary>
    public const string MapType = "civilization";

    /// <summary>The kind word of a civilization site: the planet map's legend and the scanner card of a people this client does
    /// not know.</summary>
    public const string KindKey = "ui.map.kind_civilization";

    /// <summary>The Codex's discoveries group (heading <c>ui.wiki.discoveries.civilization</c>).</summary>
    public const string CodexGroup = "civilization";

    /// <summary>The discovery ledger of a civilization: <c>civ:&lt;key&gt;</c> (<see cref="CivilizationRules.DiscoveryKey"/>) —
    /// once per player.</summary>
    public const string LedgerPrefix = CivilizationRules.RecordKindPrefix;

    /// <summary>The scan readout's subject of a civilization site: <c>civ_&lt;key&gt;</c>.</summary>
    public const string SubjectPrefix = "civ_";

    /// <summary>The scan line of a block a civilization built, <c>{0}</c> = the people's name.</summary>
    public const string BlockLineKey = "ui.scan.civilization_block";

    /// <summary>The toast of a newly discovered civilization, <c>{0}</c> = its Codex name.</summary>
    public const string NewDiscoveryKey = "ui.wiki.civilization.new";

    /// <summary>The Codex group of a ledger key: <see cref="CodexGroup"/> for a civilization's discovery, else null (the caller's
    /// own "kind before the first colon" rule applies — the key would read "civ" there).</summary>
    public static string? CodexKind(string? ledgerKey) => LedgerCivilization(ledgerKey) != null ? CodexGroup : null;

    /// <summary>The civilization key of a discovery ledger key, or null for any other key.</summary>
    public static string? LedgerCivilization(string? ledgerKey)
        => ledgerKey != null && ledgerKey.Length > LedgerPrefix.Length && ledgerKey.StartsWith(LedgerPrefix, StringComparison.Ordinal)
            ? ledgerKey.Substring(LedgerPrefix.Length)
            : null;

    /// <summary>The civilization key of a scan readout's subject (<c>civ_&lt;key&gt;</c>), or null.</summary>
    public static string? SubjectCivilization(string? subjectKey)
        => subjectKey != null && subjectKey.Length > SubjectPrefix.Length && subjectKey.StartsWith(SubjectPrefix, StringComparison.Ordinal)
            ? subjectKey.Substring(SubjectPrefix.Length)
            : null;

    /// <summary>Whether a scan readout is a civilization site's.</summary>
    public static bool IsReadout(string? kind) => kind == MapType;

    /// <summary>
    /// The Codex line of a civilization's discovery: its shown name — the civilization file's Codex title in this client's
    /// language, else the people's name, else the name the server recorded, else the key — and the Codex text under it (empty
    /// for a civilization this client does not know). <paramref name="text"/> resolves a key to its text, or null when the
    /// locale has none.
    /// </summary>
    public static (string Name, string Desc) CodexLine(string ledgerKey, string? recordedName, GameContent? content, Func<string, string?> text)
    {
        string key = LedgerCivilization(ledgerKey) ?? ledgerKey;
        var civ = content?.GetCivilization(key);
        string? title = civ == null ? null : Known(civ.Codex.TitleKey, text) ?? Known(civ.NameKey, text);
        string name = title ?? (string.IsNullOrEmpty(recordedName) ? key : recordedName!);
        string desc = civ == null ? string.Empty : Known(civ.Codex.TextKey, text) ?? string.Empty;
        return (name, desc);
    }

    /// <summary>The toast of a civilization a delta of the discovery ledger brings — the Codex has a new page ("Discovered: the
    /// people — see the Codex …"); null for any other ledger key, or when the locale lacks the words.</summary>
    public static string? NewDiscoveryLine(string ledgerKey, string? recordedName, GameContent? content, Func<string, string?> text)
    {
        if (CodexKind(ledgerKey) == null || text(NewDiscoveryKey) is not { } line)
        {
            return null;
        }

        return line.Replace("{0}", CodexLine(ledgerKey, recordedName, content, text).Name);
    }

    /// <summary>The scanner card's title of a civilization site: "the people · the site" (<c>Das verschwundene Volk · Versunkene
    /// Stadt</c>) — the kind word in place of a people this client does not know.</summary>
    public static string ReadoutTitle(string? subjectKey, string? siteName, GameContent? content, Func<string, string?> text)
    {
        var civ = content?.GetCivilization(SubjectCivilization(subjectKey));
        string people = (civ == null ? null : Known(civ.NameKey, text)) ?? text(KindKey) ?? KindKey;
        return WreckReadout.Title(people, siteName);
    }

    /// <summary>The scan line of a block a civilization built (<see cref="BlockDefinition.Civilization"/>): "Built by: the
    /// people" — null for any other block, or when the locale lacks the words.</summary>
    public static string? BlockLine(GameContent? content, string? blockKey, Func<string, string?> text)
    {
        var civ = content?.CivilizationOfBlock(blockKey);
        if (civ == null || text(BlockLineKey) is not { } line || Known(civ.NameKey, text) is not { } people)
        {
            return null;
        }

        return line.Replace("{0}", people);
    }

    private static string? Known(string? key, Func<string, string?> text) => string.IsNullOrEmpty(key) ? null : text(key!);
}
