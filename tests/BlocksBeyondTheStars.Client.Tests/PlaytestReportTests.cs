// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Shared.Playtests;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The closing report of a playtest task: refused while a step is open or a mandatory question unanswered, built
/// with the report type the inbox files under its own category, readable as plain text, and small on purpose — no
/// e-mail, no install token, the nickname instead of the in-game name, the tester's work only when it was ticked.
/// </summary>
public sealed class PlaytestReportTests
{
    private static readonly DateTime Now = new DateTime(2026, 11, 18, 16, 0, 0, DateTimeKind.Utc);

    private static PlaytestTask Task()
    {
        const string json = """
            { "id": "heavy-shot", "rev": 2,
              "title": { "de": "Der schwere Schuss", "en": "The heavy shot" },
              "steps": [
                { "id": "launch", "text": { "de": "Starte ins All.", "en": "Launch into space." }, "check": { "event": "space:enter" } },
                { "id": "feel", "text": { "de": "Fühlt er sich schwer an?", "en": "Does it feel heavy?" } } ],
              "questions": [
                { "id": "fun", "type": "stars", "text": { "de": "Wie viel Spaß?", "en": "How much fun?" } },
                { "id": "hard", "type": "choice", "text": { "de": "Wie schwer?", "en": "How hard?" },
                  "options": [ { "id": "easy", "text": { "de": "Leicht", "en": "Easy" } },
                               { "id": "tough", "text": { "de": "Schwer", "en": "Hard" } } ] } ] }
            """;
        Assert.True(PlaytestCatalogJson.TryReadTask(json, out var task));
        return task;
    }

    private static PlaytestProgress Closed(PlaytestTask task)
    {
        var progress = new PlaytestProgress(string.Empty);
        long start = new DateTimeOffset(Now).ToUnixTimeSeconds() - 412;
        progress.CountEvent(task, "space:enter", "launch", 1, start);
        progress.SetStep(task, "feel", PlaytestStepStates.Failed, "Kein Rückstoß zu spüren.", start);
        return progress;
    }

    private static PlaytestReportForm Form() => new PlaytestReportForm
    {
        Answers = new Dictionary<string, string> { ["fun"] = "4", ["hard"] = "tough" },
        FreeText = "  Der Ring ist schwer zu sehen.  ",
        Nickname = "Layex",
        Locale = "de",
    };

    private static PlaytestReportResult Build(PlaytestTask task, PlaytestProgress progress, PlaytestReportForm form)
        => PlaytestReport.Build(task, progress, form, "2026.11.22", "reply-key", "session-1", "WindowsPlayer", Now);

    [Fact]
    public void AnOpenStep_RefusesTheReport()
    {
        var task = Task();
        var progress = new PlaytestProgress(string.Empty);
        progress.CountEvent(task, "space:enter", "launch", 1, 100);

        var result = Build(task, progress, Form());

        Assert.Null(result.Report);
        Assert.Equal(PlaytestReportRefusal.StepsOpen, result.Refusal);
    }

    [Theory]
    [InlineData(null, "tough", "fun")]
    [InlineData("0", "tough", "fun")]
    [InlineData("6", "tough", "fun")]
    [InlineData("four", "tough", "fun")]
    [InlineData("4", null, "hard")]
    [InlineData("4", "medium", "hard")]
    public void AMissingOrInvalidAnswer_RefusesTheReport_AndNamesTheQuestion(string? fun, string? hard, string missing)
    {
        var task = Task();
        var form = Form();
        form.Answers.Clear();
        if (fun != null)
        {
            form.Answers["fun"] = fun;
        }

        if (hard != null)
        {
            form.Answers["hard"] = hard;
        }

        var result = Build(task, Closed(task), form);

        Assert.Null(result.Report);
        Assert.Equal(PlaytestReportRefusal.QuestionsMissing, result.Refusal);
        Assert.Equal(new[] { missing }, result.MissingQuestions);
    }

    [Fact]
    public void ACompleteReport_CarriesTheTypeTheStepsAndTheAnswers()
    {
        var task = Task();

        var report = Build(task, Closed(task), Form()).Report!;

        Assert.Equal("Test Pilot: The heavy shot", report.Title);
        Assert.Equal(PlaytestReport.ReportType, report.ReportJson["reportType"]);
        var node = (Dictionary<string, object>)report.ReportJson["playtest"];
        Assert.Equal("heavy-shot", node["taskId"]);
        Assert.Equal(2, node["rev"]);
        Assert.Equal("de", node["locale"]);
        Assert.Equal(412L, node["seconds"]);
        Assert.Equal("Der Ring ist schwer zu sehen.", node["freeText"]);
        Assert.Equal(false, node["workAttached"]);
        var steps = (Dictionary<string, object>)node["steps"];
        var launch = (Dictionary<string, object>)steps["launch"];
        Assert.Equal(PlaytestStepStates.Done, launch["state"]);
        Assert.Equal(true, launch["auto"]);
        var feel = (Dictionary<string, object>)steps["feel"];
        Assert.Equal(PlaytestStepStates.Failed, feel["state"]);
        Assert.Equal("Kein Rückstoß zu spüren.", feel["note"]);
        Assert.Equal("tough", ((Dictionary<string, string>)node["answers"])["hard"]);
    }

    [Fact]
    public void TheDescription_IsTheWholeReportAsPlainText()
    {
        var task = Task();

        string text = Build(task, Closed(task), Form()).Report!.Description;

        Assert.Contains("Der schwere Schuss [heavy-shot, rev 2]", text);
        Assert.Contains("- done (recognised): Starte ins All.", text);
        Assert.Contains("- DID NOT WORK: Fühlt er sich schwer an? — note: Kein Rückstoß zu spüren.", text);
        Assert.Contains("- Wie viel Spaß?: 4 of 5 stars", text);
        Assert.Contains("- Wie schwer?: Schwer [tough]", text);
        Assert.Contains("Der Ring ist schwer zu sehen.", text);
        Assert.True(text.Length <= PlaytestReport.MaxDescriptionLength);
    }

    [Fact]
    public void TheReport_CarriesNoEmailNoInstallToken_AndTheNicknameAsItsName()
    {
        var task = Task();

        var report = Build(task, Closed(task), Form()).Report!;

        Assert.Equal(string.Empty, report.Email);
        Assert.Equal(string.Empty, report.PlayerId);
        Assert.Equal("Layex", report.PlayerName);
        Assert.Equal("reply-key", report.ReplyKey);
        Assert.Equal("2026.11.22", report.GameVersion);
        Assert.Null(report.Screenshot);
        Assert.Null(report.Attachments);
    }

    [Fact]
    public void ScreenshotAndWork_TravelOnlyWhenGiven_AndAnOversizedShotIsDropped()
    {
        var task = Task();
        var form = Form();
        form.ScreenshotJpg = new byte[] { 1, 2, 3 };
        form.Work = new FeedbackAttachment { FileName = "ship.json", MimeType = "application/json", Base64 = "e30=" };

        var report = Build(task, Closed(task), form).Report!;

        Assert.Equal(Convert.ToBase64String(new byte[] { 1, 2, 3 }), report.Screenshot!.Base64);
        Assert.Same(form.Work, Assert.Single(report.Attachments!));
        Assert.Equal(true, ((Dictionary<string, object>)report.ReportJson["playtest"])["workAttached"]);

        form.ScreenshotJpg = new byte[PlaytestReport.MaxScreenshotBytes + 1];
        var tooBig = Build(task, Closed(task), form).Report!;
        Assert.Null(tooBig.Screenshot); // the picture is dropped, never the report
    }

    [Fact]
    public void TheGraphicsState_RidesAlong_OnlyWhenTheCallerHandsItOver()
    {
        // #3838: the hub hands it over for a report sent from inside a world — the node an in-game bug report carries.
        var task = Task();
        var graphics = new GraphicsReportInfo { Api = "Direct3D11", Preset = "Low", PresetAuto = true, RenderScale = 0.7f, ScreenWidth = 1280, ScreenHeight = 720 };

        var fromAWorld = PlaytestReport.Build(task, Closed(task), Form(), "2026.11.24", "reply-key", "session-1", "WindowsPlayer", Now, graphics).Report!;
        var node = (Dictionary<string, object>)fromAWorld.ReportJson["graphics"];
        Assert.Equal("Low", node["preset"]);
        Assert.Equal(true, node["presetAuto"]);
        Assert.Equal("1280x720", node["screen"]);
        Assert.True(fromAWorld.ReportJson.ContainsKey("playtest")); // beside the report, not instead of it

        var fromTheMenu = Build(task, Closed(task), Form()).Report!;
        Assert.False(fromTheMenu.ReportJson.ContainsKey("graphics"));
    }
}
