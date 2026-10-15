// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The fragments of the old log entries in a derelict station's supply crates (#2633), from <c>data/derelict_logs.json</c>:
/// the writers' jobs, what happened to the station, how everyone got out and the writers' last words. Every entry is the
/// tail of a locale key (<see cref="JobKey"/>, <see cref="IncidentKey"/>, <see cref="EscapeKey"/>, <see cref="ClosingKey"/>);
/// the composer (<c>WorldGeneration.DerelictLogs</c>) picks them from a log's seed. A data folder without the file has no
/// logs: the crates then hold supplies only.
/// </summary>
public sealed class DerelictLogDefinition
{
    /// <summary>The station day a log is dated (the same for every log of one derelict), inclusive range.</summary>
    public int DayMin { get; set; } = 14;

    public int DayMax { get; set; } = 380;

    /// <summary>Who wrote the log: <c>derelict.log.job.&lt;job&gt;</c> — one first-person sentence about their work.</summary>
    public List<string> Jobs { get; set; } = new();

    /// <summary>What happened to the station: <c>derelict.log.incident.&lt;key&gt;.&lt;1..variants&gt;</c>.</summary>
    public List<DerelictLogIncident> Incidents { get; set; } = new();

    /// <summary>How everyone got out: <c>derelict.log.escape.&lt;key&gt;</c>.</summary>
    public List<string> Escapes { get; set; } = new();

    /// <summary>The writer's last words: <c>derelict.log.close.&lt;key&gt;</c>.</summary>
    public List<string> Closings { get; set; } = new();

    /// <summary>True when any part is missing — no log can be written from it.</summary>
    public bool IsEmpty => Jobs.Count == 0 || Incidents.Count == 0 || Escapes.Count == 0 || Closings.Count == 0;

    /// <summary>The header line: <c>{0}</c> the station day, <c>{1}</c> the writer's (coined, language-neutral) name.</summary>
    public const string HeaderKey = "derelict.log.header";

    public static string JobKey(string job) => "derelict.log.job." + job;

    public static string IncidentKey(string incident, int variant) => "derelict.log.incident." + incident + "." + variant;

    public static string EscapeKey(string escape) => "derelict.log.escape." + escape;

    public static string ClosingKey(string closing) => "derelict.log.close." + closing;

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

/// <summary>One thing that can happen to a derelict station (#2633) and how many ways a writer tells it.</summary>
public sealed class DerelictLogIncident
{
    public string Key { get; set; } = string.Empty;

    /// <summary>How many tellings (<c>derelict.log.incident.&lt;key&gt;.1</c> …) exist; at least 1.</summary>
    public int Variants { get; set; } = 1;
}
