// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// "Send my work along": what an editor hands a playtest report. A ship, a module or a figure travels as the same
/// document a submission of that kind carries; a canvas editor as a small document with its draft; and whatever is
/// empty, not valid or too large is simply not sent — the report never fails over it.
/// </summary>
public sealed class PlaytestWorkTests
{
    private static JsonElement Parse(FeedbackAttachment attachment)
    {
        byte[] bytes = Convert.FromBase64String(attachment.Base64);
        if (attachment.MimeType == "application/gzip")
        {
            using var packed = new MemoryStream(bytes);
            using var gzip = new GZipStream(packed, CompressionMode.Decompress);
            using var plain = new MemoryStream();
            gzip.CopyTo(plain);
            bytes = plain.ToArray();
        }

        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ACreation_TravelsAsTheDocumentASubmissionCarries()
    {
        var payload = new CreationPayload { CellCount = 2 }
            .Add("ship.json", "{ \"key\": \"my ship\" }")
            .Add("layout.json", "{ \"cells\": [1, 2] }");

        var work = PlaytestWork.OfCreation(CreationSubmission.KindShip, "My Ship", payload)!;

        Assert.Equal("work-ship.json", work.FileName);
        Assert.Equal("application/json", work.MimeType);
        var root = Parse(work);
        Assert.Equal("ship", root.GetProperty("kind").GetString());
        Assert.Equal("my_ship", root.GetProperty("key").GetString()); // the key as the pull tool's folder name
        Assert.Equal(2, root.GetProperty("files").GetProperty("layout.json").GetProperty("cells").GetArrayLength());
    }

    [Fact]
    public void ALargeCreation_IsCompressed_AndOneAboveTheCapIsNotSent()
    {
        string cells = "[" + string.Join(",", Enumerable.Repeat("{\"x\":1,\"y\":2,\"z\":3,\"id\":\"hull\"}", 20_000)) + "]";
        var large = new CreationPayload().Add("layout.json", "{ \"cells\": " + cells + " }");

        var work = PlaytestWork.OfCreation(CreationSubmission.KindStructure, "big", large)!;

        Assert.Equal("work-structure.json.gz", work.FileName);
        Assert.Equal("application/gzip", work.MimeType);
        Assert.Equal(20_000, Parse(work).GetProperty("files").GetProperty("layout.json").GetProperty("cells").GetArrayLength());

        // Random text does not compress: the file stays above the cap and is left out.
        var random = new Random(7);
        var noise = new StringBuilder(PlaytestWork.MaxBytes * 3);
        while (noise.Length < PlaytestWork.MaxBytes * 3)
        {
            noise.Append((char)('a' + random.Next(26)));
        }

        var tooLarge = new CreationPayload().Add("layout.json", "{ \"noise\": \"" + noise + "\" }");
        Assert.Null(PlaytestWork.OfCreation(CreationSubmission.KindStructure, "big", tooLarge));
    }

    [Fact]
    public void NothingValid_IsNothingToSend()
    {
        var payload = new CreationPayload().Add("ship.json", "{}");

        Assert.Null(PlaytestWork.OfCreation(CreationSubmission.KindShip, "key", null));
        Assert.Null(PlaytestWork.OfCreation(CreationSubmission.KindShip, "key", new CreationPayload())); // no files
        Assert.Null(PlaytestWork.OfCreation(CreationSubmission.KindShip, "key", new CreationPayload().Add("ship.json", "not json")));
        Assert.Null(PlaytestWork.OfCreation("texture", "key", payload)); // a texture goes as its draft, not as a creation
        Assert.Null(PlaytestWork.OfCreation("anything", "key", payload));
    }

    [Fact]
    public void ACanvasEditor_SendsItsDraftAsASmallDocument()
    {
        var work = PlaytestWork.OfFields("tool_looks", new[]
        {
            new KeyValuePair<string, string?>("name", "Bohrer"),
            new KeyValuePair<string, string?>("draft", "{\"kind\":\"tool_looks\",\"payload\":\"abc\"}"),
            new KeyValuePair<string, string?>("empty", string.Empty),
            new KeyValuePair<string, string?>("missing", null),
        })!;

        Assert.Equal("work-toollooks.json", work.FileName);
        Assert.Equal("application/json", work.MimeType);
        var root = Parse(work);
        Assert.Equal(PlaytestWork.FormatVersion, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal("tool_looks", root.GetProperty("editor").GetString());
        var fields = root.GetProperty("fields");
        Assert.Equal("Bohrer", fields.GetProperty("name").GetString());
        Assert.Contains("tool_looks", fields.GetProperty("draft").GetString());
        Assert.False(fields.TryGetProperty("empty", out _));
        Assert.False(fields.TryGetProperty("missing", out _));

        // Nothing to say is nothing to send.
        Assert.Null(PlaytestWork.OfFields("form", new[] { new KeyValuePair<string, string?>("draft", null) }));
        Assert.Null(PlaytestWork.OfFields(string.Empty, new[] { new KeyValuePair<string, string?>("draft", "x") }));
    }

    [Fact]
    public void APicture_TravelsAsPng_UnlessItIsEmptyOrTooLarge()
    {
        var work = PlaytestWork.OfPng("Texture", new byte[] { 1, 2, 3 })!;

        Assert.Equal("work-texture.png", work.FileName);
        Assert.Equal("image/png", work.MimeType);
        Assert.Equal(new byte[] { 1, 2, 3 }, Convert.FromBase64String(work.Base64));

        Assert.Null(PlaytestWork.OfPng("texture", null));
        Assert.Null(PlaytestWork.OfPng("texture", Array.Empty<byte>()));
        Assert.Null(PlaytestWork.OfPng("texture", new byte[PlaytestWork.MaxBytes + 1]));
    }
}
