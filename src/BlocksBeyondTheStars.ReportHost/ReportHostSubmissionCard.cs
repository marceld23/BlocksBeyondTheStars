// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>
/// The admin detail page's card for a player's submission: a texture (#1966), a ship, station/town module or
/// avatar skin (#3262), or a HUD frame (#3319). For the creations it is a review sheet — the players are often children, and what they type
/// ends up in the game for everyone once adopted, so every free-text field inside the submitted files is listed (ship
/// names and descriptions, device labels and configs, …), next to the preview, the facts and a warning when the
/// design was built with a different block set than this inbox's build. Everything player-made is HTML-encoded.
/// </summary>
public static partial class SubmissionCard
{
    /// <summary>The most bytes a gzipped payload may unpack to before the card stops reading (a gzip bomb stays one).</summary>
    public const int MaxUnpackedBytes = 32 * 1024 * 1024;

    /// <summary>The most distinct texts listed; a design with more says so.</summary>
    public const int MaxListedTexts = 300;

    private static readonly Lazy<string> EmbeddedFingerprint = new(ComputeEmbeddedFingerprint);

    /// <summary>The block fingerprint of the content this inbox was built with (the embedded <c>data/blocks.json</c>),
    /// or empty when the resource is missing.</summary>
    public static string CurrentBlockFingerprint => EmbeddedFingerprint.Value;

    /// <summary>Fields that name things rather than say things: their value is listed only when it is not a plain
    /// token (a key, a palette string, a colour) — so a sentence smuggled into a block id still shows up.</summary>
    private static readonly HashSet<string> StructuralFields = new(StringComparer.Ordinal)
    {
        "id", "kind", "key", "layout", "tier", "pack", "role", "kit", "function", "style", "port", "requiredBlueprint",
        "item", "module", "start", "planetTypes", "startModules", "roleMap", "structureKind",
        "facePixels", "torsoPixels", "armPixels", "legPixels", "helmetPixels", "skin", "torso", "arms", "legs",
        "corner", "top", "side", "bottom", "twinkleColors", // a HUD frame's four tiles and its light colours (#3319)
    };

    /// <summary>A key, a palette string or a colour: ASCII letters, digits and <c>_ # . : -</c> only.</summary>
    private static bool IsPlainToken(string text)
    {
        foreach (char c in text)
        {
            if (!(char.IsAsciiLetterOrDigit(c) || c is '_' or '#' or '.' or ':' or '-'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The card, or empty when the report is no submission. <paramref name="read"/> loads an attachment's
    /// bytes (null = not on disk); without it the creation card lists the files but cannot open them.</summary>
    public static string Render(BugReportRecord r, IReadOnlyList<AttachmentRecord> attachments, Func<AttachmentRecord, byte[]?>? read = null)
    {
        if (r.Category == ReportIngest.TextureCategory || (!ReportIngest.IsSubmission(r.Category) && attachments.Count > 0))
        {
            return attachments.Count > 0 ? Texture(r, attachments) : string.Empty;
        }

        return ReportIngest.IsSubmission(r.Category) ? Creation(r, attachments, read) : string.Empty;
    }

    /// <summary>A texture submission (#1966): the picture blown up with hard pixels, and every file as a download.</summary>
    private static string Texture(BugReportRecord r, IReadOnlyList<AttachmentRecord> attachments)
    {
        var sb = new StringBuilder("<div class='card'><h2>Submitted texture</h2>");
        foreach (var a in attachments.Where(a => a.Mime == "image/png"))
        {
            sb.Append($"<p><img src='/admin/report/{r.Id}/attachment/{a.Index}/view' alt='texture' style='image-rendering:pixelated;height:256px;border:1px solid #456'></p>");
        }

        sb.Append("<p>").Append(Downloads(r, attachments)).Append("</p>");
        sb.Append("<p class='hint'>Adopt it with <code>tools/pull_submissions.py --kind texture</code> + <code>tools/merge_texture.py</code>; set “fixed in version” so the player hears from which release on it ships — a submission without it is deleted after the retention period.</p></div>");
        return sb.ToString();
    }

    private static string Creation(BugReportRecord r, IReadOnlyList<AttachmentRecord> attachments, Func<AttachmentRecord, byte[]?>? read)
    {
        var block = KindBlock(r.ReportJson, r.Category);
        var sb = new StringBuilder($"<div class='card'><h2>Submitted {E(KindLabel(r.Category))}</h2>");
        if (r.ScreenshotFile.Length > 0)
        {
            sb.Append($"<p><a href='/admin/report/{r.Id}/screenshot'><img src='/admin/report/{r.Id}/screenshot' alt='preview' style='max-height:360px;border:1px solid #456'></a></p>");
        }

        sb.Append("<table class='meta'>");
        foreach (string field in new[] { "name", "nickname", "note", "key", "structureKind", "kit", "cellCount", "gameVersion", "formatVersion", "basedOn" })
        {
            if (Text(block, field) is { Length: > 0 } value)
            {
                Row(sb, field, value);
            }
        }

        Row(sb, "consent", Consent(block));
        sb.Append("</table>");
        sb.Append(FingerprintNote(Text(block, "blockFingerprint") ?? string.Empty));
        sb.Append("<p>").Append(attachments.Count > 0 ? Downloads(r, attachments) : "<span class='hint'>No file arrived — nothing to adopt.</span>").Append("</p>");
        sb.Append(FreeTextSection(attachments, read));
        sb.Append($"<p class='hint'>Adopt it with <code>tools/pull_submissions.py --kind {E(r.Category)} --fetch</code> (checks the consent and every block key) and the kind's merge tool; never copy a name into the locale files unread. Set “fixed in version” so the player hears from which release on it ships — a submission without it is deleted after the retention period.</p></div>");
        return sb.ToString();
    }

    private static string KindLabel(string category) => category switch
    {
        ReportIngest.ShipCategory => "ship",
        ReportIngest.StructureCategory => "station / town module",
        ReportIngest.AvatarCategory => "avatar skin",
        ReportIngest.HudCategory => "HUD frame",
        _ => category,
    };

    /// <summary>The kind's block of the stored payload (<c>reportJson.reportJson.&lt;category&gt;</c>), or an empty object.</summary>
    private static JsonElement KindBlock(string reportJson, string category)
    {
        try
        {
            using var doc = JsonDocument.Parse(reportJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("reportJson", out var inner) && inner.ValueKind == JsonValueKind.Object
                && inner.TryGetProperty(category, out var block) && block.ValueKind == JsonValueKind.Object)
            {
                return block.Clone();
            }
        }
        catch (JsonException)
        {
            // an unreadable payload shows an empty fact table
        }

        using var empty = JsonDocument.Parse("{}");
        return empty.RootElement.Clone();
    }

    private static string? Text(JsonElement block, string field)
    {
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty(field, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => null,
        };
    }

    private static string Consent(JsonElement block)
    {
        if (block.ValueKind != JsonValueKind.Object || !block.TryGetProperty("consent", out var c) || c.ValueKind != JsonValueKind.Object)
        {
            return "⚠ missing";
        }

        bool all = new[] { "self", "grant", "age" }.All(n => c.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True);
        string version = c.TryGetProperty("textVersion", out var tv) ? tv.GetRawText() : "?";
        return (all ? "all three given" : "⚠ INCOMPLETE — do not adopt") + $" (text version {version})";
    }

    /// <summary>The fingerprint row, with a warning when the block set differs from this inbox's build.</summary>
    private static string FingerprintNote(string submitted)
    {
        string current = CurrentBlockFingerprint;
        if (submitted.Length == 0)
        {
            return "<p class='hint'>No block fingerprint sent.</p>";
        }

        if (current.Length > 0 && !string.Equals(submitted, current, StringComparison.Ordinal))
        {
            return $"<p><b>⚠ Built with a different block set</b> (<code>{E(submitted)}</code>, this inbox's build: <code>{E(current)}</code>) — "
                   + "blocks may have been renamed or removed since; <code>tools/pull_submissions.py</code> lists every unknown block key.</p>";
        }

        return $"<p class='hint'>Block fingerprint <code>{E(submitted)}</code>{(current.Length > 0 ? " — same block set as this inbox's build." : string.Empty)}</p>";
    }

    private static string Downloads(BugReportRecord r, IReadOnlyList<AttachmentRecord> attachments)
        => string.Join(" · ", attachments.Select(a =>
            $"<a href='/admin/report/{r.Id}/attachment/{a.Index}'>{E(a.FileName)}</a> <span class='sub'>({a.Bytes} B)</span>"));

    private static void Row(StringBuilder sb, string label, string value)
        => sb.Append($"<tr><th>{E(label)}</th><td>{E(value)}</td></tr>");

    private static string E(string s) => WebUtility.HtmlEncode(s);

    private static string ComputeEmbeddedFingerprint()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BlocksBeyondTheStars.ReportHost.blocks.json");
            if (stream == null)
            {
                return string.Empty;
            }

            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var keys = doc.RootElement.EnumerateArray()
                .Where(b => b.ValueKind == JsonValueKind.Object && b.TryGetProperty("key", out var k) && k.ValueKind == JsonValueKind.String)
                .Select(b => b.GetProperty("key").GetString() ?? string.Empty)
                .Where(k => k.Length > 0);
            return GameContent.FingerprintOfBlockKeys(keys);
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }
}
