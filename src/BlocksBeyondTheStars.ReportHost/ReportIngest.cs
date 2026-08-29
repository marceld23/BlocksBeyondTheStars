// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>A validated, size-capped report ready to be stored. The screenshot travels separately as
/// decoded bytes; <see cref="ReportJson"/> is the original payload re-serialized WITHOUT the screenshot
/// node, so the database never holds megabytes of base64.</summary>
public sealed class ParsedReport
{
    public string Title = string.Empty;
    public string Description = string.Empty;
    public string Email = string.Empty;
    public string GameVersion = string.Empty;
    public string BuildNumber = string.Empty;
    public string PlayerId = string.Empty;
    public string PlayerName = string.Empty;
    public string SessionId = string.Empty;
    public string Platform = string.Empty;
    public string ClientTimestamp = string.Empty;

    /// <summary>Coarse triage bucket derived at ingest: "crash" when the payload's reportJson carries a
    /// crash <c>kind</c>, otherwise "feedback" (the F1 dialog sends no kind).</summary>
    public string Category = "feedback";

    /// <summary>Origin from reportJson.source when present (e.g. <c>server</c> for automatic crash
    /// reports, <c>client</c> for client crashes); empty for plain player feedback.</summary>
    public string Source = string.Empty;

    /// <summary>reportJson.kind when present (e.g. <c>tick-fault</c>, <c>unhandled-exception</c>).</summary>
    public string Kind = string.Empty;

    /// <summary>The reporter's reply-thread credential (#1327): the client's <c>replyKey</c> when it sent a
    /// well-formed one, otherwise empty (the store then derives it from <see cref="PlayerId"/>).</summary>
    public string ReplyKey = string.Empty;

    /// <summary>The full original payload minus the screenshot, as compact JSON.</summary>
    public string ReportJson = "{}";

    public byte[]? ScreenshotBytes;

    /// <summary>File extension for the stored screenshot ("jpg" or "png"), derived from mimeType.</summary>
    public string ScreenshotExtension = "jpg";
}

/// <summary>
/// Parses and validates an incoming bug-report POST body. The wire contract is EXACTLY what the game
/// already sends to the Wix endpoint (<c>FeedbackReport</c> serialized camelCase, crash reports shaped
/// to the same fields), so existing clients and <c>CrashReportUploader</c> work unchanged. Deliberately
/// tolerant: unknown fields ride along inside <see cref="ParsedReport.ReportJson"/>, wrong-typed fields
/// are ignored, and an unusable screenshot drops the image but keeps the report — the only hard
/// requirement is a non-empty <c>description</c>.
/// </summary>
public static class ReportIngest
{
    /// <summary>Parses <paramref name="body"/>; returns null and an <paramref name="error"/> code
    /// (<c>invalid_json</c>, <c>empty_description</c>) when the payload is unusable.</summary>
    public static ParsedReport? Parse(string body, ReportHostConfig config, out string error)
    {
        error = string.Empty;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = "invalid_json";
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "invalid_json";
                return null;
            }

            var root = doc.RootElement;
            var report = new ParsedReport
            {
                Title = Str(root, "title", config.MaxTitleLength),
                Description = Str(root, "description", config.MaxDescriptionLength),
                Email = Str(root, "email", 200),
                GameVersion = Str(root, "gameVersion", 100),
                BuildNumber = Str(root, "buildNumber", 100),
                PlayerId = Str(root, "playerId", 100),
                PlayerName = Str(root, "playerName", 100),
                SessionId = Str(root, "sessionId", 100),
                Platform = Str(root, "platform", 100),
                ClientTimestamp = Str(root, "clientTimestamp", 64),
            };

            if (string.IsNullOrWhiteSpace(report.Description))
            {
                error = "empty_description";
                return null;
            }

            // Triage fields live inside reportJson (see CrashReportWriter): source="server"/"client" and a
            // crash kind. Plain F1 feedback has neither → category "feedback".
            if (root.TryGetProperty("reportJson", out var rj) && rj.ValueKind == JsonValueKind.Object)
            {
                report.Source = Str(rj, "source", 40);
                report.Kind = Str(rj, "kind", 60);
            }

            report.Category = report.Kind.Length > 0 ? "crash" : "feedback";

            // Reply-thread credential (#1327): only a syntactically valid key is kept — anything else is
            // treated as "not sent" so the store derives one from the player id instead.
            string replyKey = Str(root, "replyKey", BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.Length + 1);
            if (BlocksBeyondTheStars.Shared.Feedback.FeedbackReplyKey.IsWellFormed(replyKey))
            {
                report.ReplyKey = replyKey;
            }

            ExtractScreenshot(root, config, report);
            report.ReportJson = WithoutScreenshot(root);
            return report;
        }
    }

    /// <summary>Decodes the optional screenshot attachment; anything off (too large, bad base64, unknown
    /// mime) silently drops the image and keeps the report, mirroring the client's own drop-don't-reject
    /// behavior for oversized shots.</summary>
    private static void ExtractScreenshot(JsonElement root, ReportHostConfig config, ParsedReport report)
    {
        if (!root.TryGetProperty("screenshot", out var shot) || shot.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string base64 = Str(shot, "base64", int.MaxValue);
        if (base64.Length == 0 || base64.Length > config.MaxScreenshotBase64Length)
        {
            return;
        }

        try
        {
            report.ScreenshotBytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return;
        }

        report.ScreenshotExtension = Str(shot, "mimeType", 60).ToLowerInvariant() == "image/png" ? "png" : "jpg";
    }

    /// <summary>Re-serializes the payload without the screenshot node (compact, no indentation).</summary>
    private static string WithoutScreenshot(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject())
            {
                if (!property.NameEquals("screenshot"))
                {
                    property.WriteTo(writer);
                }
            }

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Str(JsonElement obj, string name, int maxLength)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.String)
        {
            return string.Empty;
        }

        string value = el.GetString() ?? string.Empty;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }
}
