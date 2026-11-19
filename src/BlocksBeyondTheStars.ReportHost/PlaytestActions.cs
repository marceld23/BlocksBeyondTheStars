// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.ReportHost;

/// <summary>What <see cref="PlaytestActions.Confirm"/> did.</summary>
public sealed record PlaytestConfirmResult(IReadOnlyList<string> ReportIds, string ThreadReportId, long ReplyId, string Text);

/// <summary>
/// The operator's "confirm" of a playtest report: the tester's badge turns gold. It is ONE action on purpose —
/// status <c>done</c> on the whole report pair AND a short thank-you in the reply thread — because the game only
/// ever learns about a report through its thread: the client's poll returns threads with an unread developer entry
/// and nothing else, so a bare status change would never reach the tester.
/// </summary>
public static class PlaytestActions
{
    /// <summary>The thank-you when neither the operator nor the task supplies one.</summary>
    public const string DefaultThanksDe = "Danke fürs Testen!";

    /// <summary>See <see cref="DefaultThanksDe"/>.</summary>
    public const string DefaultThanksEn = "Thanks for testing!";

    /// <summary>
    /// Confirms the playtest report <paramref name="id"/> (either half of its pair may be named). The answer text is
    /// <paramref name="text"/> when the operator typed one, else the task's own thank-you in the tester's language,
    /// else the default. Confirming twice writes no second thank-you. Null when <paramref name="id"/> is unknown or
    /// its pair holds no playtest report.
    /// </summary>
    public static PlaytestConfirmResult? Confirm(ReportStore store, PlaytestStore playtests, string id, string? text,
        string? fixedInVersion, long nowUnix)
    {
        if (store.Get(id) is not { } addressed)
        {
            return null;
        }

        var playtest = ReportPairActions.PairOf(store, addressed).FirstOrDefault(r => r.Category == ReportIngest.PlaytestCategory);
        if (playtest == null)
        {
            return null;
        }

        var owner = ReportPairActions.ThreadOwnerOf(store, playtest);
        string answer = string.IsNullOrWhiteSpace(text) ? ThanksFor(playtests, playtest) : text.Trim();

        // Confirmed before (a done report that already holds a developer entry): keep it idempotent.
        bool thanked = playtest.Status == BugReportStatus.Done
                       && store.ListReplies(owner.Id).Any(r => r.Author == ReplyRecord.AuthorDev);
        long replyId = thanked ? 0 : store.AddDevReply(owner.Id, answer, isQuestion: false, nowUnix);
        if (replyId < 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(fixedInVersion))
        {
            store.SetFixedInVersion(owner.Id, fixedInVersion.Trim());
        }

        var ids = ReportPairActions.SetStatus(store, playtest.Id, BugReportStatus.Done) ?? new List<string>();
        return new PlaytestConfirmResult(ids, owner.Id, replyId, answer);
    }

    /// <summary>The thank-you for <paramref name="playtest"/>: its task's text in the tester's language, or the default.</summary>
    public static string ThanksFor(PlaytestStore playtests, BugReportRecord playtest)
    {
        var data = PlaytestReportData.Read(playtest);
        string locale = data?.Locale ?? "en";
        string fromTask = data != null && playtests.Get(data.TaskId) is { } record ? record.Task().Thanks.For(locale) : string.Empty;
        if (fromTask.Length > 0)
        {
            return fromTask;
        }

        return locale.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? DefaultThanksDe : DefaultThanksEn;
    }
}
