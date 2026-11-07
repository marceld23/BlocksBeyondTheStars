// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The fragments of a set of readable old log entries: the derelict stations' supply-crate logs (#2633, from
/// <c>data/derelict_logs.json</c>) and the G.D.S. logs in the crates of a buried G.D.S. ruin (#3341, from
/// <c>data/gds_logs.json</c>) — the writers' jobs, what happened, how everyone got out and the writers' last words. Every
/// entry is the tail of a locale key (<see cref="JobKey"/>, <see cref="IncidentKey"/>, <see cref="EscapeKey"/>,
/// <see cref="ClosingKey"/>) in the set's key family (<see cref="TextPrefix"/>); the composer
/// (<c>WorldGeneration.DerelictLogs</c>) picks them from a log's seed. A data folder without the file has no logs of that
/// set: the crates then hold their other loot only.
/// </summary>
public sealed class DerelictLogDefinition
{
    /// <summary>The key family of the derelict stations' logs (#2633).</summary>
    public const string DefaultTextPrefix = "derelict.log";

    /// <summary>The locale key family every fragment key starts with — <c>derelict.log</c> or <c>gds.log</c> (#3341). The
    /// content loader assigns it per set (the file decides the set, the set decides the keys), so it is never read from
    /// the file.</summary>
    [JsonIgnore]
    public string TextPrefix { get; set; } = DefaultTextPrefix;

    /// <summary>The day (or cycle) a log is dated (the same for every log of one story), inclusive range.</summary>
    public int DayMin { get; set; } = 14;

    public int DayMax { get; set; } = 380;

    /// <summary>Who wrote the log: <c>&lt;prefix&gt;.job.&lt;job&gt;</c> — one first-person sentence about their work.</summary>
    public List<string> Jobs { get; set; } = new();

    /// <summary>What happened: <c>&lt;prefix&gt;.incident.&lt;key&gt;.&lt;1..variants&gt;</c>.</summary>
    public List<DerelictLogIncident> Incidents { get; set; } = new();

    /// <summary>How everyone got out: <c>&lt;prefix&gt;.escape.&lt;key&gt;</c>.</summary>
    public List<string> Escapes { get; set; } = new();

    /// <summary>The writer's last words: <c>&lt;prefix&gt;.close.&lt;key&gt;</c>.</summary>
    public List<string> Closings { get; set; } = new();

    /// <summary>True when any part is missing — no log can be written from it.</summary>
    public bool IsEmpty => Jobs.Count == 0 || Incidents.Count == 0 || Escapes.Count == 0 || Closings.Count == 0;

    /// <summary>The header line: <c>{0}</c> the day, <c>{1}</c> the writer's (coined, language-neutral) name.</summary>
    [JsonIgnore]
    public string HeaderKey => TextPrefix + ".header";

    public string JobKey(string job) => TextPrefix + ".job." + job;

    public string IncidentKey(string incident, int variant) => TextPrefix + ".incident." + incident + "." + variant;

    public string EscapeKey(string escape) => TextPrefix + ".escape." + escape;

    public string ClosingKey(string closing) => TextPrefix + ".close." + closing;

    /// <summary>Every locale key the logs can use (for the locale tests).</summary>
    public IEnumerable<string> AllTextKeys()
    {
        yield return HeaderKey;
        foreach (var job in Jobs)
        {
            yield return JobKey(job);
        }

        foreach (var incident in Incidents)
        {
            for (int v = 1; v <= System.Math.Max(1, incident.Variants); v++)
            {
                yield return IncidentKey(incident.Key, v);
            }
        }

        foreach (var escape in Escapes)
        {
            yield return EscapeKey(escape);
        }

        foreach (var closing in Closings)
        {
            yield return ClosingKey(closing);
        }
    }
}

/// <summary>One thing that can happen in a log set's story (#2633, #3341) and how many ways a writer tells it.</summary>
public sealed class DerelictLogIncident
{
    public string Key { get; set; } = string.Empty;

    /// <summary>How many tellings (<c>&lt;prefix&gt;.incident.&lt;key&gt;.1</c> …) exist; at least 1.</summary>
    public int Variants { get; set; } = 1;
}
