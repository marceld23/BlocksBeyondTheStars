// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace BlocksBeyondTheStars.ReportHost;

/// <summary>The review list of a creation's card: every free text inside the submitted files (#3262).</summary>
public static partial class SubmissionCard
{
    /// <summary>One distinct text: where it sits (<c>layout.json › cells › label</c>), what it says, how often.</summary>
    public sealed record FreeText(string Field, string Text, int Count);

    private static string FreeTextSection(IReadOnlyList<AttachmentRecord> attachments, Func<AttachmentRecord, byte[]?>? read)
    {
        var payload = attachments.FirstOrDefault(a => a.Mime is "application/json" or "application/gzip");
        if (payload == null || read == null)
        {
            return string.Empty;
        }

        string? json = Unpack(read(payload), payload.Mime, out string problem);
        if (json == null)
        {
            return $"<p><b>⚠ The file could not be opened</b> ({E(problem)}) — download it and look inside before adopting.</p>";
        }

        List<FreeText> texts;
        try
        {
            texts = FreeTexts(json);
        }
        catch (JsonException e)
        {
            return $"<p><b>⚠ The file is no valid JSON</b> ({E(e.Message)}).</p>";
        }

        var sb = new StringBuilder("<h2>Free text inside — read before adopting</h2>");
        if (texts.Count == 0)
        {
            return sb.Append("<p class='hint'>No free text in the files.</p>").ToString();
        }

        sb.Append("<table><tr><th>Field</th><th>Text</th><th>×</th></tr>");
        foreach (var t in texts)
        {
            sb.Append($"<tr><td><code>{E(t.Field)}</code></td><td>{E(t.Text)}</td><td>{t.Count}</td></tr>");
        }

        sb.Append("</table>");
        if (texts.Count >= MaxListedTexts)
        {
            sb.Append($"<p class='hint'>Only the first {MaxListedTexts} distinct texts are listed — download the file for the rest.</p>");
        }

        return sb.ToString();
    }

    /// <summary>The payload as text: gzip is unpacked up to <see cref="MaxUnpackedBytes"/>.</summary>
    public static string? Unpack(byte[]? bytes, string mime, out string problem)
    {
        problem = string.Empty;
        if (bytes == null || bytes.Length == 0)
        {
            problem = "missing on disk";
            return null;
        }

        if (mime != "application/gzip")
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            using var gzip = new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            int n;
            while ((n = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + n > MaxUnpackedBytes)
                {
                    problem = $"unpacks to more than {MaxUnpackedBytes / (1024 * 1024)} MB";
                    return null;
                }

                output.Write(buffer, 0, n);
            }

            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (InvalidDataException e)
        {
            problem = "broken gzip: " + e.Message;
            return null;
        }
    }

    /// <summary>Every distinct free text of a payload document, in the order first met. The walk starts at the
    /// document's <c>files</c> (the export files by name); a field that only names things (<see cref="StructuralFields"/>)
    /// is listed only when its value is not a plain token.</summary>
    public static List<FreeText> FreeTexts(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Object)
        {
            root = files;
        }

        var order = new List<(string Field, string Text)>();
        var counts = new Dictionary<(string Field, string Text), int>();
        Walk(root, string.Empty, string.Empty, order, counts);
        return order.Select(k => new FreeText(k.Field, k.Text, counts[k])).ToList();
    }

    private static void Walk(JsonElement element, string path, string field, List<(string, string)> order, Dictionary<(string, string), int> counts)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, path.Length == 0 ? property.Name : path + " › " + property.Name, property.Name, order, counts);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, path, field, order, counts);
                }

                break;
            case JsonValueKind.String:
                Collect(path, field, element.GetString() ?? string.Empty, order, counts);
                break;
        }
    }

    private static void Collect(string path, string field, string text, List<(string, string)> order, Dictionary<(string, string), int> counts)
    {
        if (text.Trim().Length == 0 || (StructuralFields.Contains(field) && IsPlainToken(text)))
        {
            return;
        }

        var entry = (path, text);
        if (counts.TryGetValue(entry, out int count))
        {
            counts[entry] = count + 1;
        }
        else if (order.Count < MaxListedTexts)
        {
            order.Add(entry);
            counts[entry] = 1;
        }
    }
}
