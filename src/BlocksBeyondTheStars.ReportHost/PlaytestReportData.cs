// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>One step's outcome as a playtest report carries it.</summary>
public sealed record PlaytestStepOutcome(string Id, string State, bool Auto, int Count, string Note)
{
    /// <summary>The tester said the step did not work.</summary>
    public bool Failed => State == "failed";
}

/// <summary>
/// The machine-readable half of a playtest report (<c>reportJson.playtest</c>, written by the client's
/// <c>PlaytestReport</c>): which task and revision it answers, each step's outcome, the answers by question id. Read
/// tolerantly — it is player-sent data like every other part of a report, so a missing or wrong-typed field reads as
/// its default and nothing here is trusted beyond being shown (HTML-encoded) to the operator.
/// </summary>
public sealed class PlaytestReportData
{
    public string TaskId { get; private set; } = string.Empty;

    public int Rev { get; private set; }

    /// <summary>The tester's language code — picks the language of the thank-you.</summary>
    public string Locale { get; private set; } = "en";

    public long Seconds { get; private set; }

    public string FreeText { get; private set; } = string.Empty;

    public bool WorkAttached { get; private set; }

    public List<PlaytestStepOutcome> Steps { get; } = new();

    /// <summary>Answers by question id: a star count or an option id.</summary>
    public Dictionary<string, string> Answers { get; } = new(StringComparer.Ordinal);

    /// <summary>Reads the playtest node of <paramref name="record"/>; null when the report carries none (or names no
    /// well-formed task id).</summary>
    public static PlaytestReportData? Read(BugReportRecord record)
    {
        try
        {
            using var doc = JsonDocument.Parse(record.ReportJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("reportJson", out var rj) || rj.ValueKind != JsonValueKind.Object
                || !rj.TryGetProperty("playtest", out var node) || node.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var data = new PlaytestReportData
            {
                TaskId = Str(node, "taskId", PlaytestLimits.MaxIdLength),
                Rev = node.TryGetProperty("rev", out var rev) && rev.TryGetInt32(out int r) ? r : 0,
                Locale = Str(node, "locale", 16),
                Seconds = node.TryGetProperty("seconds", out var s) && s.TryGetInt64(out long sec) ? Math.Max(0, sec) : 0,
                FreeText = Str(node, "freeText", 5000),
                WorkAttached = node.TryGetProperty("workAttached", out var w) && w.ValueKind == JsonValueKind.True,
            };
            if (!PlaytestCatalogValidator.IsWellFormedId(data.TaskId))
            {
                return null;
            }

            ReadSteps(node, data);
            ReadAnswers(node, data);
            return data;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The stars a stars question got in this report (1–5), or 0 when it was not answered that way.</summary>
    public int Stars(string questionId)
        => Answers.TryGetValue(questionId, out string? value)
           && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int stars) && stars >= 1 && stars <= 5
            ? stars
            : 0;

    private static void ReadSteps(JsonElement node, PlaytestReportData data)
    {
        if (!node.TryGetProperty("steps", out var steps) || steps.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var p in steps.EnumerateObject())
        {
            if (data.Steps.Count >= PlaytestLimits.MaxSteps || p.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            data.Steps.Add(new PlaytestStepOutcome(
                Cut(p.Name, PlaytestLimits.MaxIdLength),
                Str(p.Value, "state", 16),
                p.Value.TryGetProperty("auto", out var auto) && auto.ValueKind == JsonValueKind.True,
                p.Value.TryGetProperty("count", out var count) && count.TryGetInt32(out int c) ? c : 0,
                Str(p.Value, "note", PlaytestLimits.MaxTextLength)));
        }
    }

    private static void ReadAnswers(JsonElement node, PlaytestReportData data)
    {
        if (!node.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var p in answers.EnumerateObject())
        {
            if (data.Answers.Count < PlaytestLimits.MaxQuestions && p.Value.ValueKind == JsonValueKind.String)
            {
                data.Answers[Cut(p.Name, PlaytestLimits.MaxIdLength)] = Cut(p.Value.GetString() ?? string.Empty, PlaytestLimits.MaxIdLength);
            }
        }
    }

    private static string Str(JsonElement o, string name, int max)
        => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Cut(v.GetString() ?? string.Empty, max) : string.Empty;

    private static string Cut(string text, int max) => text.Length <= max ? text : text[..max];
}

/// <summary>
/// What the reports of one task add up to — the admin page's evaluation: how many came in, how many are confirmed,
/// the average of each stars question, the spread of each choice question, and how often each step did not work.
/// </summary>
public sealed class PlaytestStats
{
    public int Reports { get; private set; }

    public int Confirmed { get; private set; }

    /// <summary>Per question id: the sum and the number of star answers.</summary>
    public Dictionary<string, (int Sum, int Count)> Stars { get; } = new(StringComparer.Ordinal);

    /// <summary>Per question id: how often each option id was chosen.</summary>
    public Dictionary<string, Dictionary<string, int>> Choices { get; } = new(StringComparer.Ordinal);

    /// <summary>Per step id: how often it was reported as "did not work".</summary>
    public Dictionary<string, int> FailedSteps { get; } = new(StringComparer.Ordinal);

    /// <summary>The average over every stars answer of the task, or 0 without one.</summary>
    public double AverageStars
    {
        get
        {
            int sum = Stars.Values.Sum(v => v.Sum);
            int count = Stars.Values.Sum(v => v.Count);
            return count == 0 ? 0 : (double)sum / count;
        }
    }

    /// <summary>Adds the playtest reports of <paramref name="records"/> up, per task id.</summary>
    public static Dictionary<string, PlaytestStats> ByTask(IEnumerable<BugReportRecord> records)
    {
        var byTask = new Dictionary<string, PlaytestStats>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            if (PlaytestReportData.Read(record) is not { } data)
            {
                continue;
            }

            if (!byTask.TryGetValue(data.TaskId, out var stats))
            {
                byTask[data.TaskId] = stats = new PlaytestStats();
            }

            stats.Add(record, data);
        }

        return byTask;
    }

    private void Add(BugReportRecord record, PlaytestReportData data)
    {
        Reports++;
        if (record.Status == BugReportStatus.Done)
        {
            Confirmed++;
        }

        foreach (var step in data.Steps.Where(s => s.Failed))
        {
            FailedSteps[step.Id] = FailedSteps.GetValueOrDefault(step.Id) + 1;
        }

        foreach (var answer in data.Answers)
        {
            int stars = data.Stars(answer.Key);
            if (stars > 0)
            {
                var (sum, count) = Stars.GetValueOrDefault(answer.Key);
                Stars[answer.Key] = (sum + stars, count + 1);
                continue;
            }

            if (!Choices.TryGetValue(answer.Key, out var options))
            {
                Choices[answer.Key] = options = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            options[answer.Value] = options.GetValueOrDefault(answer.Value) + 1;
        }
    }
}
