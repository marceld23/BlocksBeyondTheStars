// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>
/// The registry of every structure source (#3512), by key, in one stable order — the order the editor lists them in.
/// Parameters travel as a settings line (<c>key=value;key=value</c>, <see cref="MarkerSettings"/>), the same form a
/// marker's settings and a source template's parameters use.
/// </summary>
public static class StructureSources
{
    /// <summary>Prefix of a source's display-name key: <c>ui.source.&lt;key&gt;</c>.</summary>
    public const string LabelKeyPrefix = "ui.source.";

    /// <summary>Prefix of a parameter's label key: <c>ui.source.param.&lt;key&gt;</c>.</summary>
    public const string ParamLabelKeyPrefix = "ui.source.param.";

    private static readonly IStructureSource[] Registered = CreateAll();

    /// <summary>Every source, in the stable editor order.</summary>
    public static IReadOnlyList<IStructureSource> All => Registered;

    /// <summary>The source with this key, or null.</summary>
    public static IStructureSource? Get(string? key)
    {
        foreach (var s in Registered)
        {
            if (string.Equals(s.Key, key, StringComparison.Ordinal))
            {
                return s;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a settings line against <paramref name="source"/>'s parameters: <paramref name="values"/> holds every declared
    /// parameter (named ones normalized, the rest at their defaults). False — with every problem in
    /// <paramref name="error"/> and the readable values still filled in — when the line names a parameter the source does
    /// not declare or a value that does not fit it.
    /// </summary>
    public static bool TryParseParams(IStructureSource source, GameContent content, string? settingsLine,
        out IReadOnlyDictionary<string, string> values, out string error)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var settings = MarkerSettings.Parse(settingsLine);
        var declared = source.GetParams(content);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var p in declared)
        {
            string? given = settings.Has(p.Key) ? settings.Get(p.Key) : null;
            if (!p.TryNormalize(given, out string v))
            {
                problems.Add($"'{given}' is no valid value of '{p.Key}'");
            }

            result[p.Key] = v;
        }

        foreach (string key in KeysOf(settingsLine))
        {
            if (!result.ContainsKey(key))
            {
                problems.Add($"'{source.Key}' has no parameter '{key}'");
            }
        }

        values = result;
        error = string.Join("; ", problems);
        return problems.Count == 0;
    }

    /// <summary>The settings line of <paramref name="values"/> in <paramref name="source"/>'s parameter order — only the
    /// parameters that differ from their default, so a line stays short and a later default change reaches it.</summary>
    public static string FormatParams(IStructureSource source, GameContent content, IReadOnlyDictionary<string, string>? values)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var settings = new MarkerSettings();
        foreach (var p in source.GetParams(content))
        {
            if (values != null && values.TryGetValue(p.Key, out string? v) && p.TryNormalize(v, out string n)
                && !string.Equals(n, p.Default, StringComparison.Ordinal))
            {
                settings.Set(p.Key, n);
            }
        }

        return settings.ToString();
    }

    /// <summary>Every declared parameter of <paramref name="source"/> with its value from <paramref name="given"/>
    /// (normalized) or its default — what a build reads.</summary>
    internal static Dictionary<string, string> Resolve(IStructureSource source, GameContent content, IReadOnlyDictionary<string, string>? given)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in source.GetParams(content))
        {
            string? raw = given != null && given.TryGetValue(p.Key, out string? v) ? v : null;
            _ = p.TryNormalize(raw, out string value); // an invalid value builds with the default
            result[p.Key] = value;
        }

        return result;
    }

    /// <summary>
    /// THE registration list — the one place a source is added. The order is the editor's order and is pinned by
    /// <c>StructureSourceTests</c>; append new sources (the direct writers turned builders — vaults, unique sites, Soaniri
    /// buildings, guardian core, hunter camp — and ship sources) at the end of their group.
    /// </summary>
    private static IStructureSource[] CreateAll() => new IStructureSource[]
    {
        // Surface
        new SettlementSource(),
        new FallenRuinSource(),
        new CitySource(),
        new MonumentSource(),
        new BanditCampSource(),
        new LodgeSource(),
        new PioneerOutpostSource(),
        new SpsLabSource(),
        new FactorySource(),
        new RailStationSource(),
        new RailRuinSource(),
        new WreckSource(),
        new TextTabletSource(),
        new LandmarkSource(),

        // Underground
        new AncientRuinSource(),

        // Space
        new StationSource(),
        new StationKitSource(),
        new DerelictSource(),

        // The direct writers turned builders (#3522)
        new VaultSource(),
        new SingingShrineSource(),
        new SealedObservatorySource(),
        new SoaniriSiteSource(SoaniriBuilder.MirunKey),
        new SoaniriSiteSource(SoaniriBuilder.OrnasaKey),
        new SoaniriSiteSource(SoaniriBuilder.ChoirTowerKey),
        new GuardianCoreSource(),
        new HunterCampSource(),
    };

    private static IEnumerable<string> KeysOf(string? settingsLine)
    {
        var keys = new List<string>();
        if (string.IsNullOrWhiteSpace(settingsLine))
        {
            return keys;
        }

        foreach (string part in settingsLine!.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq > 0)
            {
                keys.Add(part.Substring(0, eq).Trim().ToLowerInvariant());
            }
        }

        return keys;
    }
}
