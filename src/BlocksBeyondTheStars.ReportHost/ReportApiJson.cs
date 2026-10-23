// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The JSON shapes the read API, the client's poll and the admin export return — property order is the wire order,
/// do not reorder. (Not to be confused with <see cref="BugReportRecord.ReportJson"/>, the stored raw payload that
/// <see cref="Item"/> embeds.)
/// </summary>
public static class ReportApiJson
{
    // One reply-thread entry as JSON (shared by the read API and the client's poll).
    public static object Reply(ReplyRecord reply) => new
    {
        id = reply.Id,
        author = reply.Author,
        text = reply.Text,
        isQuestion = reply.IsQuestion,
        createdUnix = reply.CreatedUnix,
        seen = reply.SeenUnix > 0,
    };

    // The read API's item shape — matches the planned puller contract (camelCase, ISO createdAt, parsed
    // reportJson, relative screenshotUrl or null). The single-report endpoint adds the reply thread;
    // the reply key itself is never exposed (it is the player's credential) — neither the column nor the copy the
    // client sent inside its payload, which reportJson would otherwise carry verbatim (#2921).
    public static object Item(ReportStore store, BugReportRecord r, IReadOnlyList<ReplyRecord>? replies = null)
    {
        JsonElement reportJson;
        try
        {
            using var doc = JsonDocument.Parse(WithoutReplyKey(r.ReportJson));
            reportJson = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var doc = JsonDocument.Parse("{}");
            reportJson = doc.RootElement.Clone();
        }

        return new
        {
            id = r.Id,
            title = r.Title,
            description = r.Description,
            email = r.Email,
            gameVersion = r.GameVersion,
            buildNumber = r.BuildNumber,
            playerId = r.PlayerId,
            playerName = r.PlayerName,
            sessionId = r.SessionId,
            platform = r.Platform,
            clientTimestamp = r.ClientTimestamp,
            category = r.Category,
            source = r.Source,
            kind = r.Kind,
            status = r.Status,
            createdAt = DateTimeOffset.FromUnixTimeSeconds(r.CreatedUnix).UtcDateTime.ToString("o"),
            screenshotUrl = r.ScreenshotFile.Length > 0 ? $"/api/reports/{r.Id}/screenshot" : null,
            attachments = store.Attachments(r.Id).Select(a => new
            {
                index = a.Index,
                fileName = a.FileName,
                mimeType = a.Mime,
                bytes = a.Bytes,
                url = $"/api/reports/{r.Id}/attachment/{a.Index}",
            }).ToArray(),
            reportJson,
            fixedInVersion = r.FixedInVersion,
            replies = replies?.Select(x => Reply(x)).ToArray(),
        };
    }

    // One row of the admin export's "Download JSON" file (#2921): every stored column under its record name, in the
    // record's order, EXCEPT the reply key — the reporter's credential for the reply thread, a column the read API
    // leaves out too. The copy of the key a client sends inside its payload goes as well, so the file holds none.
    public static object ExportItem(BugReportRecord r) => new
    {
        r.Id,
        r.Title,
        r.Description,
        r.Email,
        r.GameVersion,
        r.BuildNumber,
        r.PlayerId,
        r.PlayerName,
        r.SessionId,
        r.Platform,
        r.ClientTimestamp,
        r.Category,
        r.Source,
        r.Kind,
        r.Status,
        r.ScreenshotFile,
        ReportJson = WithoutReplyKey(r.ReportJson),
        r.CreatedUnix,
        r.FixedInVersion,
    };

    // The stored payload (BugReportRecord.ReportJson) minus its top-level "replyKey" node — where ingest reads the
    // client's key from (ReportIngest.Parse). Anything else, an unreadable payload included, passes through as is.
    public static string WithoutReplyKey(string reportJson)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(reportJson);
        }
        catch (JsonException)
        {
            return reportJson;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("replyKey", out _))
            {
                return reportJson;
            }

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var property in root.EnumerateObject())
                {
                    if (!property.NameEquals("replyKey"))
                    {
                        property.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }

    // One thread as the client's poll returns it: the report's title/status/fixed-in line, the whole
    // conversation, and the ids the client must acknowledge once shown.
    public static object Thread(ReportThread thread) => new
    {
        reportId = thread.Report.Id,
        title = thread.Report.Title,
        status = thread.Report.Status,
        fixedInVersion = thread.Report.FixedInVersion,
        createdUnix = thread.Report.CreatedUnix,
        replies = thread.Replies.Select(x => Reply(x)).ToArray(),
        unseenIds = thread.Replies.Where(x => x.Author == ReplyRecord.AuthorDev && x.SeenUnix == 0).Select(x => x.Id).ToArray(),
    };
}
