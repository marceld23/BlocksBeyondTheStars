// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The admin pages of the playtest catalogue — the task list, the editing form with its preview and evaluation — and
/// the review card a playtest report gets on the report detail page. Server-rendered like every page of the inbox
/// (inline CSS from <see cref="ReportHostPages.Shell"/>, plain form POSTs, no script). A task is authored text, a
/// report is player-sent text: both are HTML-encoded on the way out.
/// </summary>
public static class ReportHostPlaytestPages
{
    /// <summary>What the "new task" form starts with — a complete, valid task to edit.</summary>
    public const string NewTaskTemplate = """
        {
          "id": "my-task",
          "rev": 1,
          "enabled": false,
          "sort": 100,
          "area": "ingame",
          "minVersion": "",
          "title": { "de": "Titel", "en": "Title" },
          "intro": { "de": "Worum es geht.", "en": "What it is about." },
          "minutes": 10,
          "steps": [
            { "id": "first", "text": { "de": "Mach das.", "en": "Do this." } }
          ],
          "questions": [
            { "id": "fun", "type": "stars", "text": { "de": "Wie viel Spaß hat es gemacht?", "en": "How much fun was it?" } }
          ],
          "thanks": { "de": "Danke fürs Testen!", "en": "Thanks for testing!" }
        }
        """;

    private static string E(string s) => ReportHostPages.E(s);

    // ---------------------------------------------------------------- the task list

    /// <summary>The list of every stored task with its numbers, the kill switch and the way to a new task.</summary>
    public static string TaskList(IReadOnlyList<PlaytestTaskRecord> records, IReadOnlyDictionary<string, PlaytestStats> stats,
        bool catalogEnabled, AdminCsrf csrf)
    {
        var sb = new StringBuilder();
        sb.Append("<p><a href='/admin'>&larr; back to the reports</a></p>");
        sb.Append($"<h1>Playtest tasks <span class='sub'>{records.Count} stored · catalogue {(catalogEnabled ? "ON" : "OFF")}</span></h1>");
        sb.Append("<p class='hint'>The tasks of the game's Test Pilot page. Every game client fetches this list, so a change " +
                  "here reaches players without a release. A task that is switched off stays stored but is not sent. " +
                  $"Players see at most {PlaytestLimits.MaxTasks} tasks, in the order of their <code>sort</code> number.</p>");

        sb.Append("<div class='actions'>");
        sb.Append("<a href='/admin/playtests/new'><button type='button'>new task</button></a>");
        sb.Append($"<form method='post' action='/admin/playtests/switch'>{csrf.HiddenField()}" +
                  $"<input type='hidden' name='enabled' value='{(catalogEnabled ? "0" : "1")}'>" +
                  $"<button{(catalogEnabled ? " class='danger'" : string.Empty)}>{(catalogEnabled ? "switch the whole catalogue OFF" : "switch the catalogue ON")}</button></form>");
        sb.Append("<a href='/admin?category=playtest'><button type='button'>all playtest reports</button></a>");
        sb.Append("</div>");

        if (records.Count == 0)
        {
            sb.Append("<p class='hint'>No task yet.</p>");
            return ReportHostPages.Shell("Playtest tasks — Blocks Beyond the Stars", sb.ToString());
        }

        sb.Append("<table><tr><th>Task</th><th>Where</th><th>Rev</th><th>Versions</th><th>Reports</th><th>Confirmed</th><th>★</th><th>State</th></tr>");
        foreach (var record in records)
        {
            AppendTaskRow(sb, record, stats.GetValueOrDefault(record.Id), csrf);
        }

        sb.Append("</table>");
        return ReportHostPages.Shell("Playtest tasks — Blocks Beyond the Stars", sb.ToString());
    }

    private static void AppendTaskRow(StringBuilder sb, PlaytestTaskRecord record, PlaytestStats? stats, AdminCsrf csrf)
    {
        var task = record.Task();
        string where = task.Area == PlaytestAreas.Editor ? "editor: " + task.Editor : "in game";
        string versions = task.MinVersion.Length == 0 && task.MaxVersion.Length == 0
            ? "any"
            : (task.MinVersion.Length > 0 ? task.MinVersion : "…") + " – " + (task.MaxVersion.Length > 0 ? task.MaxVersion : "…");
        string stars = stats != null && stats.AverageStars > 0 ? stats.AverageStars.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

        sb.Append($"<tr{(task.Enabled ? string.Empty : " class='off'")}>");
        sb.Append($"<td><a href='/admin/playtests/{E(record.Id)}'>{E(task.Title.For("en"))}</a><div class='sub'>{E(record.Id)} · sort {task.Sort}</div></td>");
        sb.Append($"<td>{E(where)}</td><td>{record.Rev}</td><td>{E(versions)}</td>");
        sb.Append($"<td>{stats?.Reports ?? 0}</td><td>{stats?.Confirmed ?? 0}</td><td>{stars}</td>");
        sb.Append($"<td><form method='post' action='/admin/playtests/{E(record.Id)}/toggle'>{csrf.HiddenField()}" +
                  $"<button>{(task.Enabled ? "on — switch off" : "off — switch on")}</button></form></td></tr>");
    }

    // ---------------------------------------------------------------- one task

    /// <summary>
    /// The editing page of one task: the findings of the last save attempt, the task's JSON in a form, a preview of
    /// its texts in both languages and the evaluation of its reports.
    /// </summary>
    /// <param name="storedId">The id of the stored task being edited; empty for a new one.</param>
    /// <param name="json">What the form shows — the stored task, or the text of a refused save attempt.</param>
    /// <param name="task">The task <paramref name="json"/> reads as, for the preview; null when it is not JSON.</param>
    public static string TaskEdit(string storedId, string json, PlaytestTask? task, IReadOnlyList<PlaytestProblem> problems,
        PlaytestStats? stats, AdminCsrf csrf, bool saved)
    {
        var sb = new StringBuilder();
        sb.Append("<p><a href='/admin/playtests'>&larr; back to the tasks</a></p>");
        sb.Append($"<h1>{(storedId.Length > 0 ? "Task " + E(storedId) : "New task")}</h1>");
        if (saved)
        {
            sb.Append("<p class='st-done'>Saved. Game clients fetch the change within a few hours, or when the player opens the Test Pilot page.</p>");
        }

        AppendProblems(sb, problems);

        sb.Append($"<div class='card'><h2>Task (JSON)</h2><form method='post' action='/admin/playtests/save' class='taskform'>{csrf.HiddenField()}");
        sb.Append($"<input type='hidden' name='stored' value='{E(storedId)}'>");
        sb.Append($"<textarea name='json' spellcheck='false'>{E(json)}</textarea>");
        sb.Append("<p class='hint'>German and English texts are both required. The revision counts up by itself when the " +
                  "content changes. A step without <code>check</code> is ticked off by the player; a step with one is " +
                  "recognised by the game (<code>{ \"event\": \"space:enter\", \"arg\": \"launch\", \"required\": 1 }</code>). " +
                  "Never ask for personal data — the audience includes children.</p>");
        sb.Append("<button>save</button></form></div>");

        if (task != null)
        {
            AppendPreview(sb, task);
        }

        if (stats != null && task != null)
        {
            AppendEvaluation(sb, task, stats);
        }

        if (storedId.Length > 0)
        {
            sb.Append($"<div class='card actions'><a href='/admin?category=playtest'><button type='button'>its reports</button></a>" +
                      $"<form method='post' action='/admin/playtests/{E(storedId)}/delete' onsubmit=\"return confirm('Delete this task? Reports already sent stay.')\">" +
                      $"{csrf.HiddenField()}<button class='danger'>delete task</button></form></div>");
        }

        return ReportHostPages.Shell($"Task {storedId} — Blocks Beyond the Stars", sb.ToString());
    }

    private static void AppendProblems(StringBuilder sb, IReadOnlyList<PlaytestProblem> problems)
    {
        if (problems.Count == 0)
        {
            return;
        }

        bool refused = PlaytestCatalogValidator.HasErrors(problems);
        sb.Append($"<div class='card'><h2>{(refused ? "Not saved — fix these first" : "Saved, with warnings")}</h2><ul>");
        foreach (var problem in problems)
        {
            string kind = problem.IsError ? "error" : "warning";
            sb.Append($"<li class='problem-{kind}'><b>{kind}</b> <code>{E(problem.Where)}</code> — {E(problem.Message)}</li>");
        }

        sb.Append("</ul></div>");
    }

    private static void AppendPreview(StringBuilder sb, PlaytestTask task)
    {
        sb.Append("<div class='card'><h2>Preview — what players read</h2><table><tr><th></th><th>Deutsch</th><th>English</th></tr>");
        Row(sb, "Title", task.Title);
        Row(sb, "Intro", task.Intro);
        for (int i = 0; i < task.Steps.Count; i++)
        {
            var step = task.Steps[i];
            string how = step.Check is { } check
                ? $"recognised: {check.Event}{(check.Arg.Length > 0 ? " (" + check.Arg + ")" : string.Empty)} × {check.Required}"
                : "ticked off by the player";
            Row(sb, $"Step {i + 1}<div class='sub'>{E(how)}</div>", step.Text);
        }

        foreach (var question in task.Questions)
        {
            Row(sb, $"Question<div class='sub'>{E(question.Type)}</div>", question.Text);
            foreach (var option in question.Options)
            {
                Row(sb, "&nbsp;&nbsp;option", option.Text);
            }
        }

        if (!task.Thanks.IsEmpty)
        {
            Row(sb, "Thank-you", task.Thanks);
        }

        sb.Append("</table></div>");
    }

    private static void Row(StringBuilder sb, string labelHtml, PlaytestText text)
        => sb.Append($"<tr><th>{labelHtml}</th><td>{E(text.De)}</td><td>{E(text.En)}</td></tr>");

    private static void AppendEvaluation(StringBuilder sb, PlaytestTask task, PlaytestStats stats)
    {
        sb.Append($"<div class='card'><h2>Evaluation</h2><p>{stats.Reports} report(s), {stats.Confirmed} confirmed.</p><table>");
        foreach (var question in task.Questions)
        {
            sb.Append($"<tr><th>{E(question.Text.For("en"))}</th><td>");
            if (stats.Stars.TryGetValue(question.Id, out var stars) && stars.Count > 0)
            {
                double average = (double)stars.Sum / stars.Count;
                sb.Append($"★ {average.ToString("0.0", CultureInfo.InvariantCulture)} of 5 ({stars.Count} answers)");
            }
            else if (stats.Choices.TryGetValue(question.Id, out var chosen))
            {
                sb.Append(string.Join(" · ", question.Options.Select(o => $"{E(o.Text.For("en"))}: {chosen.GetValueOrDefault(o.Id)}")));
            }
            else
            {
                sb.Append("<span class='sub'>no answers yet</span>");
            }

            sb.Append("</td></tr>");
        }

        foreach (var step in task.Steps)
        {
            int failed = stats.FailedSteps.GetValueOrDefault(step.Id);
            if (failed > 0)
            {
                sb.Append($"<tr><th>did not work</th><td>{E(step.Text.For("en"))} — <b>{failed}×</b></td></tr>");
            }
        }

        sb.Append("</table></div>");
    }

    // ---------------------------------------------------------------- the report card

    /// <summary>
    /// The review card of a playtest report for the report detail page: the task it answers, each step's outcome with
    /// the tester's note, the answers in words, and the confirm form. Empty when neither <paramref name="shown"/> nor
    /// its paired row is a playtest report.
    /// </summary>
    /// <param name="shown">The row the detail page shows (the form posts to its route).</param>
    /// <param name="playtest">The playtest half of the pair, or null.</param>
    /// <param name="task">The stored task the report names, or null when it was deleted since.</param>
    public static string ReportCard(BugReportRecord shown, BugReportRecord? playtest, PlaytestTask? task,
        IReadOnlyList<AttachmentRecord> work, AdminCsrf csrf)
    {
        if (playtest == null || PlaytestReportData.Read(playtest) is not { } data)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append("<div class='card'><h2>Playtest report</h2><table class='meta'>");
        string title = task != null ? task.Title.For("en") : "(task no longer stored)";
        sb.Append($"<tr><th>Task</th><td><a href='/admin/playtests/{E(data.TaskId)}'>{E(title)}</a> <span class='sub'>{E(data.TaskId)} · answered rev {data.Rev}" +
                  $"{(task != null && task.Rev != data.Rev ? $" (the task is at rev {task.Rev} now)" : string.Empty)}</span></td></tr>");
        sb.Append($"<tr><th>Time taken</th><td>{data.Seconds / 60} min {data.Seconds % 60} s · language {E(data.Locale)}</td></tr>");
        AppendOutcomes(sb, data, task);
        AppendAnswers(sb, data, task);
        if (data.FreeText.Length > 0)
        {
            sb.Append($"<tr><th>In their words</th><td><pre>{E(data.FreeText)}</pre></td></tr>");
        }

        foreach (var file in work)
        {
            sb.Append($"<tr><th>Attached work</th><td><a href='/admin/report/{E(playtest.Id)}/attachment/{file.Index}'>{E(file.FileName)}</a> " +
                      $"<span class='sub'>{file.Bytes} bytes — for finding the fault only, never adopted into the game</span></td></tr>");
        }

        sb.Append("</table>");
        AppendConfirmForm(sb, shown, playtest, csrf);
        return sb.Append("</div>").ToString();
    }

    private static void AppendOutcomes(StringBuilder sb, PlaytestReportData data, PlaytestTask? task)
    {
        foreach (var step in data.Steps)
        {
            string text = task?.Steps.FirstOrDefault(s => s.Id == step.Id)?.Text.For("en") ?? step.Id;
            string outcome = step.Failed ? "<b class='problem-error'>did not work</b>"
                : step.Auto ? "done — recognised by the game"
                : "done";
            string note = step.Note.Length > 0 ? $"<div class='sub'>note: {E(step.Note)}</div>" : string.Empty;
            sb.Append($"<tr><th>Step</th><td>{E(text)} — {outcome}{note}</td></tr>");
        }
    }

    private static void AppendAnswers(StringBuilder sb, PlaytestReportData data, PlaytestTask? task)
    {
        foreach (var answer in data.Answers)
        {
            var question = task?.Questions.FirstOrDefault(q => q.Id == answer.Key);
            string asked = question?.Text.For("en") ?? answer.Key;
            int stars = data.Stars(answer.Key);
            string value = stars > 0 && question?.Type != PlaytestQuestionTypes.Choice
                ? new string('★', stars) + new string('☆', 5 - stars)
                : E(question?.Options.FirstOrDefault(o => o.Id == answer.Value)?.Text.For("en") ?? answer.Value);
            sb.Append($"<tr><th>Answer</th><td>{E(asked)} — <b>{value}</b></td></tr>");
        }
    }

    private static void AppendConfirmForm(StringBuilder sb, BugReportRecord shown, BugReportRecord playtest, AdminCsrf csrf)
    {
        if (playtest.Status == BugReportStatus.Done)
        {
            sb.Append("<p class='st-done'>Confirmed — the tester's badge is gold.</p>");
            return;
        }

        sb.Append($"<form method='post' action='/admin/report/{E(shown.Id)}/confirm' class='replyform'>{csrf.HiddenField()}");
        sb.Append("<label>thank-you <input type='text' name='text' maxlength='400' size='60' placeholder='empty = the task&#39;s own thank-you, in the tester&#39;s language'></label>");
        sb.Append("<label>fixed in version <input type='text' name='fixed_in_version' placeholder='e.g. 2026.11.23' size='14'></label>");
        sb.Append("<button>confirm — the badge turns gold and the thank-you goes to the player</button></form>");
    }
}
