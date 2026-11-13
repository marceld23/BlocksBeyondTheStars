// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>Reading <c>data/structure_types.json</c> (#3517): every entry merged over its kind's default, a broken one reported
/// and dropped — the load never fails.</summary>
public sealed partial class StructureTypeRules
{
    private static readonly JsonSerializerOptions EntryOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>One entry as the file has it: every field optional, unknown fields collected (and refused).</summary>
    private sealed class Entry
    {
        public bool? Enabled { get; set; }
        public int? MinGeneration { get; set; }
        public double? Chance { get; set; }
        public double? Density { get; set; }
        public List<double>? Thresholds { get; set; }
        public List<double>? ThresholdCaps { get; set; }
        public int? MinCount { get; set; }
        public int? Cap { get; set; }
        public string? SeatPolicy { get; set; }
        public int? KeepClear { get; set; }
        public Dictionary<string, int>? KeepClearFrom { get; set; }
        public List<string>? Conditions { get; set; }
        public string? Whitelist { get; set; }
        public SiteEntry? Site { get; set; }
        public GradeEntry? Grade { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }
    }

    /// <summary>A kind's <c>site</c> object as the file has it (#3563): every field optional.</summary>
    private sealed class SiteEntry
    {
        public List<int>? Water { get; set; }
        public List<int>? Coast { get; set; }
        public string? Prominence { get; set; }
        public string? Relief { get; set; }
        public List<double>? Altitude { get; set; }
        public List<string>? Ground { get; set; }
        public Dictionary<string, double>? Weights { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }
    }

    /// <summary>A kind's <c>grade</c> object as the file has it (#3563): every field optional.</summary>
    private sealed class GradeEntry
    {
        public int? Radius { get; set; }
        public int? MaxCut { get; set; }
        public int? MaxFill { get; set; }

        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Unknown { get; set; }
    }

    /// <summary>The unknown field names of an entry, its <c>site</c> and its <c>grade</c> object (comments excluded).</summary>
    private static List<string> UnknownFields(Entry entry)
    {
        static IEnumerable<string> Named(Dictionary<string, JsonElement>? unknown, string prefix)
            => unknown?.Keys.Where(k => !k.StartsWith("_", StringComparison.Ordinal)).Select(k => prefix + k) ?? Enumerable.Empty<string>();

        return Named(entry.Unknown, string.Empty).Concat(Named(entry.Site?.Unknown, "site.")).Concat(Named(entry.Grade?.Unknown, "grade."))
            .ToList();
    }

    /// <summary>The default site rule with every field the entry sets replaced.</summary>
    private static StructureSiteRule MergeSite(StructureSiteRule defaults, SiteEntry? e)
    {
        var s = defaults.Clone();
        if (e is null)
        {
            return s;
        }

        s.Water = e.Water ?? s.Water;
        s.Coast = e.Coast ?? s.Coast;
        s.Prominence = e.Prominence ?? s.Prominence;
        s.Relief = e.Relief ?? s.Relief;
        s.Altitude = e.Altitude ?? s.Altitude;
        s.Ground = e.Ground ?? s.Ground;
        s.Weights = e.Weights is { } w ? new Dictionary<string, double>(w, StringComparer.Ordinal) : s.Weights;
        return s;
    }

    /// <summary>The default grade rule with every field the entry sets replaced.</summary>
    private static StructureGradeRule MergeGrade(StructureGradeRule defaults, GradeEntry? e)
        => new()
        {
            Radius = e?.Radius ?? defaults.Radius,
            MaxCut = e?.MaxCut ?? defaults.MaxCut,
            MaxFill = e?.MaxFill ?? defaults.MaxFill,
        };

    /// <summary>The rules of a file — the defaults when it does not exist or cannot be read (reported).</summary>
    public static StructureTypeRules Load(string path, Action<string>? warn = null)
    {
        if (!File.Exists(path))
        {
            return Defaults;
        }

        try
        {
            return Parse(File.ReadAllText(path), warn);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            warn?.Invoke($"structure_types.json could not be read ({e.Message}) — every structure kind keeps its defaults.");
            return Defaults;
        }
    }

    /// <summary>The rules a JSON text describes, merged over the defaults (see the class summary).</summary>
    public static StructureTypeRules Parse(string json, Action<string>? warn = null)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException e)
        {
            warn?.Invoke($"structure_types.json is not valid JSON ({e.Message}) — every structure kind keeps its defaults.");
            return Defaults;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                warn?.Invoke("structure_types.json must hold one object (kind → rules) — every structure kind keeps its defaults.");
                return Defaults;
            }

            var types = new Dictionary<string, StructureTypeDefinition>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name.StartsWith("_", StringComparison.Ordinal))
                {
                    continue; // a comment
                }

                if (ReadEntry(property, warn) is { } type)
                {
                    types[type.Kind] = type;
                }
            }

            return types.Count == 0 ? Defaults : From(types);
        }
    }

    /// <summary>One kind's entry merged over its default, or null (reported) when the kind is unknown or the entry broken.</summary>
    private static StructureTypeDefinition? ReadEntry(JsonProperty property, Action<string>? warn)
    {
        string kind = property.Name;
        if (!Kinds.Contains(kind, StringComparer.Ordinal))
        {
            warn?.Invoke($"structure_types.json: unknown structure kind '{kind}' ({string.Join(", ", Kinds)}) — ignored.");
            return null;
        }

        Entry? entry;
        try
        {
            entry = property.Value.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Entry>(property.Value.GetRawText(), EntryOptions)
                : null;
        }
        catch (JsonException e)
        {
            warn?.Invoke($"structure_types.json: '{kind}' cannot be read ({e.Message}) — it keeps its defaults.");
            return null;
        }

        if (entry is null)
        {
            warn?.Invoke($"structure_types.json: '{kind}' is not an object — it keeps its defaults.");
            return null;
        }

        var unknown = UnknownFields(entry);
        if (unknown.Count > 0)
        {
            warn?.Invoke($"structure_types.json: '{kind}' has unknown field(s) {string.Join(", ", unknown)} — it keeps its defaults.");
            return null;
        }

        if (entry.MinGeneration is { } asked && asked < Defaults.Get(kind).MinGeneration)
        {
            warn?.Invoke($"structure_types.json: '{kind}' cannot start before generation {Defaults.Get(kind).MinGeneration} (asked {asked}) — older worlds would change.");
        }

        var merged = Merge(Defaults.Get(kind), entry);
        var problems = Problems(merged);
        if (problems.Count > 0)
        {
            warn?.Invoke($"structure_types.json: '{kind}' is dropped — {string.Join("; ", problems)}. It keeps its defaults.");
            return null;
        }

        return merged;
    }

    /// <summary>The default with every field the entry sets replaced.</summary>
    private static StructureTypeDefinition Merge(StructureTypeDefinition defaults, Entry e)
    {
        var t = defaults.Clone();
        t.Enabled = e.Enabled ?? t.Enabled;
        // Never below the code's own generation: a data file must not switch a kind on in worlds older than the kind (their
        // replays would change); it may only hold a kind back to a later generation.
        t.MinGeneration = Math.Max(defaults.MinGeneration, e.MinGeneration ?? t.MinGeneration);
        t.Chance = e.Chance ?? t.Chance;
        t.Density = e.Density ?? t.Density;
        t.Thresholds = e.Thresholds ?? t.Thresholds;
        t.ThresholdCaps = e.ThresholdCaps ?? t.ThresholdCaps;
        t.MinCount = e.MinCount ?? t.MinCount;
        t.Cap = e.Cap ?? t.Cap;
        t.SeatPolicy = e.SeatPolicy ?? t.SeatPolicy;
        t.KeepClear = e.KeepClear ?? t.KeepClear;
        t.KeepClearFrom = e.KeepClearFrom is { } from ? new Dictionary<string, int>(from, StringComparer.Ordinal) : t.KeepClearFrom;
        t.Conditions = e.Conditions ?? t.Conditions;
        t.Whitelist = e.Whitelist ?? t.Whitelist;
        t.Site = MergeSite(t.Site, e.Site);
        t.Grade = MergeGrade(t.Grade, e.Grade);
        return t;
    }
}
