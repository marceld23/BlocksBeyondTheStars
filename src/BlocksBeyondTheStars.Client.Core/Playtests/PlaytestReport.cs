// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Playtests
{
    /// <summary>Why <see cref="PlaytestReport.Build"/> built nothing.</summary>
    public enum PlaytestReportRefusal
    {
        /// <summary>A report was built.</summary>
        None,

        /// <summary>A step is still open — every step is closed first ("done" or "did not work").</summary>
        StepsOpen,

        /// <summary>A fixed question has no (valid) answer — all of them are mandatory.</summary>
        QuestionsMissing,
    }

    /// <summary>The outcome of <see cref="PlaytestReport.Build"/>.</summary>
    public sealed class PlaytestReportResult
    {
        /// <summary>The report to upload, or null when it was refused.</summary>
        public FeedbackReport? Report { get; set; }

        public PlaytestReportRefusal Refusal { get; set; }

        /// <summary>The ids of the questions still to answer — the dialog rings exactly these.</summary>
        public IReadOnlyList<string> MissingQuestions { get; set; } = Array.Empty<string>();
    }

    /// <summary>What the report dialog collected besides the progress.</summary>
    public sealed class PlaytestReportForm
    {
        /// <summary>Answers by question id: a star count <c>1</c>–<c>5</c>, or the id of the chosen option.</summary>
        public Dictionary<string, string> Answers { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The optional free text.</summary>
        public string FreeText { get; set; } = string.Empty;

        /// <summary>The optional nickname typed for this purpose — never the in-game name, never a real name.</summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>The player's language code (<c>de</c>, <c>en</c>, <c>fr</c> …) — picks the task texts quoted in the
        /// report and the language of the developers' thank-you.</summary>
        public string Locale { get; set; } = "en";

        /// <summary>Optional screenshot (JPG bytes); dropped when larger than <see cref="PlaytestReport.MaxScreenshotBytes"/>.</summary>
        public byte[]? ScreenshotJpg { get; set; }

        /// <summary>The tester's work (an editor's export bundle) — set ONLY when the tester ticked "send my work".</summary>
        public FeedbackAttachment? Work { get; set; }
    }

    /// <summary>
    /// Builds the closing report of a playtest task. It rides the ordinary feedback channel (same endpoint, spam key,
    /// spool, reply thread) with <c>reportJson.reportType = "playtest-report"</c>, which the inbox files under its own
    /// category. The data is deliberately small, because the tester is often a child: the answers, the steps, the game
    /// version — no e-mail, no install token (<c>playerId</c> stays empty; the one-way reply key is the anonymous id),
    /// and the name is the nickname typed for this purpose or empty.
    /// <para>The inbox requires a non-empty description, and an operator should be able to read a report without a
    /// special view, so the description is the whole report as plain text.</para>
    /// </summary>
    public static class PlaytestReport
    {
        /// <summary><c>reportJson.reportType</c> of a playtest report (the inbox's <c>ReportIngest.PlaytestReportType</c>).</summary>
        public const string ReportType = "playtest-report";

        /// <summary>The largest screenshot that travels with a report; a larger one is dropped, never the report.</summary>
        public const int MaxScreenshotBytes = 1_500_000;

        /// <summary>The inbox's description cap.</summary>
        public const int MaxDescriptionLength = 5000;

        private const int MaxNicknameLength = 40;

        /// <summary>The ids of the task's questions that <paramref name="answers"/> does not answer validly.</summary>
        public static IReadOnlyList<string> MissingQuestions(PlaytestTask task, IReadOnlyDictionary<string, string> answers)
        {
            var missing = new List<string>();
            foreach (var question in task.Questions)
            {
                if (!answers.TryGetValue(question.Id, out string? value) || !IsValidAnswer(question, value))
                {
                    missing.Add(question.Id);
                }
            }

            return missing;
        }

        /// <summary>Whether <paramref name="value"/> answers <paramref name="question"/>: one to five stars, or one of
        /// the question's options.</summary>
        public static bool IsValidAnswer(PlaytestQuestion question, string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (question.Type == PlaytestQuestionTypes.Stars)
            {
                return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int stars) && stars >= 1 && stars <= 5;
            }

            foreach (var option in question.Options)
            {
                if (string.Equals(option.Id, value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds the report for <paramref name="task"/> from this install's <paramref name="progress"/> and the
        /// dialog's <paramref name="form"/> — or names why not: a step still open, or a question unanswered.
        /// <paramref name="graphics"/> is how the game was drawn (the node an in-game bug report carries, #3838): the
        /// caller passes it for a report sent from inside a world and leaves it out in the menu and the editors —
        /// "too shaky" reads differently at the lowest preset, and nothing in a menu depends on it.
        /// </summary>
        public static PlaytestReportResult Build(
            PlaytestTask task, PlaytestProgress progress, PlaytestReportForm form,
            string gameVersion, string replyKey, string sessionId, string platform, DateTime utcNow,
            GraphicsReportInfo? graphics = null)
        {
            if (progress.OpenSteps(task) > 0)
            {
                return new PlaytestReportResult { Refusal = PlaytestReportRefusal.StepsOpen };
            }

            var missing = MissingQuestions(task, form.Answers);
            if (missing.Count > 0)
            {
                return new PlaytestReportResult { Refusal = PlaytestReportRefusal.QuestionsMissing, MissingQuestions = missing };
            }

            string freeText = (form.FreeText ?? string.Empty).Trim();
            var report = new FeedbackReport
            {
                Title = Cut("Test Pilot: " + task.Title.For("en"), 200),
                Description = Describe(task, progress, form, freeText),
                Email = string.Empty,                       // never asked for: answers come back in the game
                GameVersion = gameVersion ?? string.Empty,
                PlayerId = string.Empty,                    // the reply key is the anonymous id
                PlayerName = Cut((form.Nickname ?? string.Empty).Trim(), MaxNicknameLength),
                ReplyKey = replyKey ?? string.Empty,
                SessionId = sessionId ?? string.Empty,
                Platform = platform ?? string.Empty,
                ClientTimestamp = utcNow.ToString("o", CultureInfo.InvariantCulture),
            };
            report.ReportJson["reportType"] = ReportType;
            report.ReportJson["playtest"] = Node(task, progress, form, freeText, utcNow);
            graphics?.WriteTo(report.ReportJson);

            if (form.ScreenshotJpg is { Length: > 0 } shot && shot.Length <= MaxScreenshotBytes)
            {
                report.Screenshot = new FeedbackScreenshot { Base64 = Convert.ToBase64String(shot) };
            }

            if (form.Work != null)
            {
                report.Attachments = new List<FeedbackAttachment> { form.Work };
            }

            return new PlaytestReportResult { Report = report };
        }

        /// <summary>The machine-readable half (<c>reportJson.playtest</c>): the task and revision answered, every step's
        /// outcome, the answers by question id, how long the task took.</summary>
        private static Dictionary<string, object> Node(PlaytestTask task, PlaytestProgress progress, PlaytestReportForm form,
            string freeText, DateTime utcNow)
        {
            var entry = progress.Get(task.Id);
            long seconds = entry != null && entry.StartedUnix > 0
                ? Math.Max(0, new DateTimeOffset(utcNow).ToUnixTimeSeconds() - entry.StartedUnix)
                : 0;

            var steps = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (var step in task.Steps)
            {
                var state = progress.StepOf(task, step.Id);
                steps[step.Id] = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["state"] = state.State,
                    ["auto"] = state.Auto,
                    ["count"] = state.Count,
                    ["note"] = state.Note,
                };
            }

            return new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["taskId"] = task.Id,
                ["rev"] = task.Rev,
                ["area"] = task.Area,
                ["editor"] = task.Editor,
                ["locale"] = form.Locale ?? "en",
                ["seconds"] = seconds,
                ["steps"] = steps,
                ["answers"] = new Dictionary<string, string>(form.Answers, StringComparer.Ordinal),
                ["freeText"] = freeText,
                ["workAttached"] = form.Work != null,
            };
        }

        /// <summary>The whole report as plain text: the task, each step with its outcome, each question with its
        /// answer, the free text. Task texts are quoted in the player's language, the labels are English.</summary>
        private static string Describe(PlaytestTask task, PlaytestProgress progress, PlaytestReportForm form, string freeText)
        {
            string locale = form.Locale ?? "en";
            var sb = new StringBuilder();
            sb.Append("Playtest task: ").Append(task.Title.For(locale)).Append(" [").Append(task.Id).Append(", rev ")
                .Append(task.Rev.ToString(CultureInfo.InvariantCulture)).Append("]\n\nSteps:\n");
            foreach (var step in task.Steps)
            {
                var state = progress.StepOf(task, step.Id);
                string outcome = state.State == PlaytestStepStates.Failed ? "DID NOT WORK"
                    : state.Auto ? "done (recognised)"
                    : "done";
                sb.Append("- ").Append(outcome).Append(": ").Append(step.Text.For(locale));
                if (state.Note.Length > 0)
                {
                    sb.Append(" — note: ").Append(state.Note);
                }

                sb.Append('\n');
            }

            if (task.Questions.Count > 0)
            {
                sb.Append("\nAnswers:\n");
                foreach (var question in task.Questions)
                {
                    form.Answers.TryGetValue(question.Id, out string? value);
                    sb.Append("- ").Append(question.Text.For(locale)).Append(": ").Append(AnswerText(question, value, locale)).Append('\n');
                }
            }

            if (freeText.Length > 0)
            {
                sb.Append("\nIn their own words:\n").Append(freeText).Append('\n');
            }

            return Cut(sb.ToString().TrimEnd(), MaxDescriptionLength);
        }

        private static string AnswerText(PlaytestQuestion question, string? value, string locale)
        {
            if (question.Type == PlaytestQuestionTypes.Stars)
            {
                return (value ?? "?") + " of 5 stars";
            }

            foreach (var option in question.Options)
            {
                if (string.Equals(option.Id, value, StringComparison.Ordinal))
                {
                    return option.Text.For(locale) + " [" + option.Id + "]";
                }
            }

            return value ?? "?";
        }

        private static string Cut(string text, int max) => text.Length <= max ? text : text.Substring(0, max);
    }
}
