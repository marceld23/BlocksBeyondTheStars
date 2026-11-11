// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// #3488: the settings line of a structure marker — <c>key=value;key=value</c> — the way an author's choices travel
/// from the editor through the template (<see cref="TemplateCell.Config"/> of a marker cell) and the marker's data
/// into the server: a chest's content, a vendor's theme, a sound's key and radius, a den's species. Keys are
/// lower-case words; a value never holds <c>;</c>, <c>|</c> or a line break (the marker data separators).
/// </summary>
public sealed class MarkerSettings
{
    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>The keys in the order they were set (the editor writes them back in this order).</summary>
    private readonly List<string> _order = new();

    /// <summary>Parses a settings line; a malformed part is skipped, an empty or null line gives no settings.</summary>
    public static MarkerSettings Parse(string? line)
    {
        var s = new MarkerSettings();
        if (string.IsNullOrWhiteSpace(line))
        {
            return s;
        }

        foreach (string part in line!.Split(';'))
        {
            int eq = part.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            s.Set(part.Substring(0, eq), part.Substring(eq + 1));
        }

        return s;
    }

    public bool IsEmpty => _order.Count == 0;

    public bool Has(string key) => _values.ContainsKey(key);

    /// <summary>The value of <paramref name="key"/>, or <paramref name="fallback"/>.</summary>
    public string Get(string key, string fallback = "")
        => _values.TryGetValue(key, out var v) ? v : fallback;

    public int GetInt(string key, int fallback)
        => _values.TryGetValue(key, out var v) && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : fallback;

    public double GetDouble(string key, double fallback)
        => _values.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : fallback;

    public bool GetBool(string key, bool fallback = false)
        => _values.TryGetValue(key, out var v) ? v is "1" or "true" or "yes" : fallback;

    /// <summary>Sets a value; an empty value removes the key. Separators are stripped from both.</summary>
    public MarkerSettings Set(string key, string? value)
    {
        key = Clean(key).ToLowerInvariant();
        string v = Clean(value);
        if (key.Length == 0)
        {
            return this;
        }

        if (v.Length == 0)
        {
            if (_values.Remove(key))
            {
                _order.Remove(key);
            }

            return this;
        }

        if (!_values.ContainsKey(key))
        {
            _order.Add(key);
        }

        _values[key] = v;
        return this;
    }

    public MarkerSettings Set(string key, double value)
        => Set(key, value.ToString("0.###", CultureInfo.InvariantCulture));

    public MarkerSettings Set(string key, int value)
        => Set(key, value.ToString(CultureInfo.InvariantCulture));

    /// <summary>The settings as one line, in the order the keys were set.</summary>
    public override string ToString()
    {
        var sb = new StringBuilder();
        foreach (string key in _order)
        {
            if (sb.Length > 0)
            {
                sb.Append(';');
            }

            sb.Append(key).Append('=').Append(_values[key]);
        }

        return sb.ToString();
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(s!.Length);
        foreach (char c in s)
        {
            if (c != ';' && c != '|' && c != '\n' && c != '\r')
            {
                sb.Append(c);
            }
        }

        return sb.ToString().Trim();
    }

    // --- the item list of a chest: "iron_plate:2-4,gold_ingot:1" ---

    /// <summary>One fixed chest entry: an item and how many (a range draws by the chest's own hash).</summary>
    public readonly struct ItemEntry
    {
        public ItemEntry(string item, int min, int max)
        {
            Item = item;
            Min = min;
            Max = max;
        }

        public string Item { get; }

        public int Min { get; }

        public int Max { get; }
    }

    /// <summary>Parses <c>item:count</c> or <c>item:min-max</c>, comma-separated; a part without a readable count is 1.</summary>
    public static List<ItemEntry> ParseItems(string? list)
    {
        var result = new List<ItemEntry>();
        if (string.IsNullOrWhiteSpace(list))
        {
            return result;
        }

        foreach (string raw in list!.Split(','))
        {
            string part = raw.Trim();
            if (part.Length == 0)
            {
                continue;
            }

            int colon = part.IndexOf(':');
            string item = colon < 0 ? part : part.Substring(0, colon).Trim();
            int min = 1, max = 1;
            if (colon >= 0)
            {
                string count = part.Substring(colon + 1).Trim();
                int dash = count.IndexOf('-');
                if (dash > 0
                    && int.TryParse(count.Substring(0, dash), NumberStyles.Integer, CultureInfo.InvariantCulture, out int a)
                    && int.TryParse(count.Substring(dash + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int b))
                {
                    min = a;
                    max = b;
                }
                else if (int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                {
                    min = max = n;
                }
            }

            if (item.Length == 0)
            {
                continue;
            }

            min = Math.Max(1, min);
            result.Add(new ItemEntry(item, min, Math.Max(min, max)));
        }

        return result;
    }

    /// <summary>The inverse of <see cref="ParseItems"/>.</summary>
    public static string FormatItems(IEnumerable<ItemEntry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            if (sb.Length > 0)
            {
                sb.Append(',');
            }

            sb.Append(e.Item).Append(':').Append(e.Min.ToString(CultureInfo.InvariantCulture));
            if (e.Max > e.Min)
            {
                sb.Append('-').Append(e.Max.ToString(CultureInfo.InvariantCulture));
            }
        }

        return sb.ToString();
    }
}
