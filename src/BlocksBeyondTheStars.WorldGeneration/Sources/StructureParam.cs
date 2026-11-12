// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace BlocksBeyondTheStars.WorldGeneration.Sources;

/// <summary>Where a structure of a source stands (#3512): what the editor proposes as its target mode.</summary>
public enum StructureSourceKind
{
    /// <summary>On a planet's surface (settlements, camps, monuments, ruins …).</summary>
    Surface,

    /// <summary>Under a planet's surface (the buried G.D.S. ruins and the authored underground kits).</summary>
    Underground,

    /// <summary>In space (stations, derelicts).</summary>
    Space,

    /// <summary>A ship hull.</summary>
    Ship,
}

/// <summary>The kind of value a <see cref="StructureParam"/> holds.</summary>
public enum StructureParamType
{
    /// <summary>A whole number between <see cref="StructureParam.Min"/> and <see cref="StructureParam.Max"/>.</summary>
    Int,

    /// <summary>One of <see cref="StructureParam.Options"/>.</summary>
    Choice,

    /// <summary><c>true</c> / <c>false</c>.</summary>
    Bool,

    /// <summary>Free text (never a <c>;</c>, <c>|</c> or line break — the settings-line separators).</summary>
    Text,
}

/// <summary>
/// One parameter a structure source declares (#3512): its key in the settings line (<c>key=value;…</c>), its type, the
/// options of a choice, the default and the range of a number. Values travel as invariant strings, so the editor, a
/// template's settings line and the server read them the same way. The editor shows <see cref="LabelKey"/> as the
/// field's label; one label per parameter key, shared by every source that declares it.
/// </summary>
public sealed class StructureParam
{
    private static readonly IReadOnlyList<string> NoOptions = Array.Empty<string>();

    private StructureParam(string key, StructureParamType type, string defaultValue, IReadOnlyList<string> options, int min, int max)
    {
        Key = key;
        Type = type;
        Default = defaultValue;
        Options = options;
        Min = min;
        Max = max;
    }

    /// <summary>The key in the settings line (lower case).</summary>
    public string Key { get; }

    public StructureParamType Type { get; }

    /// <summary>The values a <see cref="StructureParamType.Choice"/> allows, in the order the editor lists them; empty for
    /// every other type. An empty string among them is a real option (e.g. "no kit — procedural").</summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>The value a build uses when the request does not name the parameter.</summary>
    public string Default { get; }

    /// <summary>The smallest value of an <see cref="StructureParamType.Int"/> parameter.</summary>
    public int Min { get; }

    /// <summary>The largest value of an <see cref="StructureParamType.Int"/> parameter.</summary>
    public int Max { get; }

    /// <summary>The locale key of the field's label: <c>ui.source.param.&lt;key&gt;</c>.</summary>
    public string LabelKey => StructureSources.ParamLabelKeyPrefix + Key;

    /// <summary>A number between <paramref name="min"/> and <paramref name="max"/>.</summary>
    public static StructureParam Int(string key, int defaultValue, int min, int max)
        => new(key, StructureParamType.Int, defaultValue.ToString(CultureInfo.InvariantCulture), NoOptions, min, max);

    /// <summary>One of <paramref name="options"/>; the default is <paramref name="defaultValue"/> when it is an option,
    /// else the first option (an empty list defaults to "").</summary>
    public static StructureParam Choice(string key, IReadOnlyList<string> options, string? defaultValue = null)
    {
        string d = defaultValue is not null && Contains(options, defaultValue) ? defaultValue : options.Count > 0 ? options[0] : string.Empty;
        return new(key, StructureParamType.Choice, d, options, 0, 0);
    }

    public static StructureParam Bool(string key, bool defaultValue)
        => new(key, StructureParamType.Bool, defaultValue ? "true" : "false", NoOptions, 0, 0);

    public static StructureParam Text(string key, string defaultValue)
        => new(key, StructureParamType.Text, defaultValue, NoOptions, 0, 0);

    /// <summary>Reads <paramref name="value"/> as this parameter's value: true with the normalized form (a choice as
    /// listed, a number in invariant digits, a flag as <c>true</c>/<c>false</c>), false with the default when the value
    /// does not fit (unknown option, unreadable or out-of-range number, not a flag). Null takes the default (and is fine).</summary>
    public bool TryNormalize(string? value, out string normalized)
    {
        normalized = Default;
        if (value is null)
        {
            return true;
        }

        switch (Type)
        {
            case StructureParamType.Int:
                if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= Min && n <= Max)
                {
                    normalized = n.ToString(CultureInfo.InvariantCulture);
                    return true;
                }

                return false;
            case StructureParamType.Choice:
                if (Contains(Options, value))
                {
                    normalized = value;
                    return true;
                }

                return false;
            case StructureParamType.Bool:
                return TryReadBool(value, ref normalized);
            default:
                normalized = value;
                return true;
        }
    }

    private static bool TryReadBool(string value, ref string normalized)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "true" or "1" or "yes":
                normalized = "true";
                return true;
            case "false" or "0" or "no":
                normalized = "false";
                return true;
            default:
                return false;
        }
    }

    private static bool Contains(IReadOnlyList<string> options, string value)
    {
        foreach (string o in options)
        {
            if (string.Equals(o, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
