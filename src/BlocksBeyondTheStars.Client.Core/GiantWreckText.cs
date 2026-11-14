// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3611/#3615: how the client names a giant ship wreck — the one home of the locale keys and ledger shapes the flight
/// chart, the travel screen, the scanner card and the Codex share, so the four never drift apart. A wreck in space is a
/// derelict of its own family (<see cref="GiantShipRules.IdPrefix"/>): every generic derelict rule (no air, nobody home,
/// a fixed navigation point) already holds for it; only its words differ. A wreck on a planet is a landmark of its own
/// kind (<see cref="GiantShipRules.MapType"/>). Unity-free.
/// </summary>
public static class GiantWreckText
{
    /// <summary>The kind word of a giant wreck: the planet map's legend, the flight chart's marker and the travel screen.</summary>
    public const string KindKey = "ui.map.kind_giant_wreck";

    /// <summary>The travel screen's detail paragraph of a giant wreck in space.</summary>
    public const string DetailKey = "ui.map.giantwreck_detail";

    /// <summary>The kind word of an ordinary derelict station.</summary>
    public const string DerelictKindKey = "ui.map.kind_derelict";

    /// <summary>The travel screen's detail paragraph of an ordinary derelict station.</summary>
    public const string DerelictDetailKey = "ui.map.derelict_detail";

    /// <summary>The space readout's <c>SubjectKey</c> (its kind is <c>station</c>) and the label key it reads as.</summary>
    public const string SpaceSubject = "giant_wreck";

    /// <summary>The scanner card's title word of a giant wreck in space ("Giant ship wreck · Wanderer Seven").</summary>
    public const string SpaceSubjectKey = "ui.scan.subject." + SpaceSubject;

    /// <summary>The space readout's info line — also the Codex text of a wreck whose ship class this session never saw.</summary>
    public const string SpaceInfoKey = "ui.scan.giant_wreck_space";

    /// <summary>The planet readout's <c>Kind</c> (the scan of any block of a wreck on a planet).</summary>
    public const string PlanetReadoutKind = GiantShipRules.MapType;

    /// <summary>The Codex's discoveries group (heading <c>ui.wiki.discoveries.giantwreck</c>) — both ledgers list there.</summary>
    public const string CodexGroup = "giantwreck";

    /// <summary>The planet ledger: <c>giantwreck:&lt;body&gt;:&lt;ship key&gt;</c> — once per body and ship.</summary>
    public const string PlanetLedgerPrefix = "giantwreck:";

    /// <summary>The space ledger: <c>space:giantwreck:&lt;wreck id&gt;#&lt;ship key&gt;</c> — once per wreck, named after its
    /// coined name; the ship class after the <c>#</c> (an entry recorded without it has none).</summary>
    public const string SpaceLedgerPrefix = "space:giantwreck:";

    /// <summary>The trait of a space readout that names the ship class: <c>giantship.&lt;key&gt;.name</c>.</summary>
    private const string ClassTraitPrefix = "giantship.";

    private const string ClassTraitSuffix = ".name";

    /// <summary>Whether a space entity / star-map body id is a giant wreck.</summary>
    public static bool IsGiantWreck(string? id) => GiantShipRules.IsGiantWreckId(id);

    /// <summary>The kind word key of a derelict body or contact: the giant wreck's own, else the derelict station's.</summary>
    public static string DerelictKind(string? id) => IsGiantWreck(id) ? KindKey : DerelictKindKey;

    /// <summary>The travel screen's detail key of a derelict body: the giant wreck's own, else the derelict station's.</summary>
    public static string DerelictDetail(string? id) => IsGiantWreck(id) ? DetailKey : DerelictDetailKey;

    /// <summary>Whether a scan readout is a giant wreck's in space (kind <c>station</c>, subject <see cref="SpaceSubject"/>).</summary>
    public static bool IsSpaceReadout(string? kind, string? subjectKey) => kind == "station" && subjectKey == SpaceSubject;

    /// <summary>The Codex group of a ledger key: <see cref="CodexGroup"/> for both giant wreck ledgers, else null (the
    /// caller's own "kind before the first colon" rule applies — a space key would read "space" there).</summary>
    public static string? CodexKind(string? ledgerKey)
        => ledgerKey != null && (ledgerKey.StartsWith(PlanetLedgerPrefix, StringComparison.Ordinal)
                                 || ledgerKey.StartsWith(SpaceLedgerPrefix, StringComparison.Ordinal))
            ? CodexGroup
            : null;

    /// <summary>Whether a ledger key is a giant wreck in space (named after its coined name).</summary>
    public static bool IsSpaceLedger(string? ledgerKey) => ledgerKey != null && ledgerKey.StartsWith(SpaceLedgerPrefix, StringComparison.Ordinal);

    /// <summary>The ship key of a planet ledger key (the part after its last colon); null for any other key.</summary>
    public static string? PlanetShipKey(string? ledgerKey)
    {
        if (ledgerKey == null || !ledgerKey.StartsWith(PlanetLedgerPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        string key = ledgerKey.Substring(ledgerKey.LastIndexOf(':') + 1);
        return key.Length == 0 ? null : key;
    }

    /// <summary>The ship class a space readout names in its traits (<c>giantship.&lt;key&gt;.name</c>), or null.</summary>
    public static string? ShipKeyFromTraits(IReadOnlyList<string>? traitKeys)
    {
        if (traitKeys == null)
        {
            return null;
        }

        foreach (string t in traitKeys)
        {
            if (t != null && t.Length > ClassTraitPrefix.Length + ClassTraitSuffix.Length
                && t.StartsWith(ClassTraitPrefix, StringComparison.Ordinal) && t.EndsWith(ClassTraitSuffix, StringComparison.Ordinal))
            {
                return t.Substring(ClassTraitPrefix.Length, t.Length - ClassTraitPrefix.Length - ClassTraitSuffix.Length);
            }
        }

        return null;
    }

    /// <summary>
    /// The Codex line of a giant wreck discovery: its shown name and the dim text under it. A planet wreck is named after its
    /// ship class (<see cref="GiantShipRules.NameKey"/> in this client's language, else the name the server recorded) with the
    /// class's Codex text. A wreck in space is named after its coined name; the text under it is "class — class text" when
    /// <paramref name="classOfName"/> knows the class (this session scanned it), else the space readout's sentence.
    /// <paramref name="text"/> resolves a key to its text, or null when the locale has none.
    /// </summary>
    public static (string Name, string Desc) CodexLine(string ledgerKey, string? recordedName, Func<string, string?> text,
        IReadOnlyDictionary<string, string>? classOfName = null)
    {
        string name = recordedName ?? string.Empty;
        if (PlanetShipKey(ledgerKey) is { } ship)
        {
            string shown = text(GiantShipRules.NameKey(ship)) ?? (name.Length > 0 ? name : ship);
            return (shown, text(GiantShipRules.DescriptionKey(ship)) ?? string.Empty);
        }

        int hash = ledgerKey.IndexOf('#');
        string? recordedClass = hash > 0 && hash + 1 < ledgerKey.Length ? ledgerKey.Substring(hash + 1) : null;
        if (name.Length == 0)
        {
            int end = hash > 0 ? hash : ledgerKey.Length;
            int start = Math.Min(end, SpaceLedgerPrefix.Length);
            name = ledgerKey.Substring(start, end - start); // a pre-name entry: the raw id
        }

        string? cls = recordedClass;
        if (cls == null && classOfName != null && classOfName.TryGetValue(name, out var seen))
        {
            cls = seen;
        }

        if (cls != null && text(GiantShipRules.NameKey(cls)) is { } className)
        {
            string? desc = text(GiantShipRules.DescriptionKey(cls));
            return (name, string.IsNullOrEmpty(desc) ? className : className + " — " + desc);
        }

        return (name, text(SpaceInfoKey) ?? string.Empty);
    }
}
