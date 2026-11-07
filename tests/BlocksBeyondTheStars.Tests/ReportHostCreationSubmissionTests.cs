// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using BlocksBeyondTheStars.ReportHost;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ships, station/town modules and avatar skins submitted from the editors (#3262): their own categories next to the
/// texture, the one JSON document kept as a file (gzip allowed, a bigger cap for ships and structures), the same
/// twelve-month deletion of submissions that were not adopted, and an admin card that lists every free text inside
/// the files for a human review — the players are often children.
/// </summary>
public sealed class ReportHostCreationSubmissionTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 9 };

    private readonly string _root;
    private readonly List<ReportStore> _stores = new();

    public ReportHostCreationSubmissionTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rhc_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (var store in _stores)
        {
            store.Dispose();
        }

        try
        {
            System.IO.Directory.Delete(_root, recursive: true);
        }
        catch (System.IO.IOException)
        {
            // Windows can hold SQLite WAL handles briefly; temp cleanup is best-effort.
        }
    }

    private ReportStore NewStore()
    {
        var store = new ReportStore(new ReportHostConfig(), System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"), "reports.db"));
        _stores.Add(store);
        return store;
    }

    private const string ShipDocument =
        "{\"formatVersion\":1,\"kind\":\"ship\",\"key\":\"star_hopper\",\"files\":{" +
        "\"ship.json\":{\"key\":\"star_hopper\",\"name\":\"Star Hopper\",\"description\":\"Fast <b>and</b> small\",\"startModules\":[\"reactor\"]}," +
        "\"layout.json\":{\"width\":5,\"cells\":[{\"x\":0,\"id\":\"steel_wall\",\"label\":\"\"},{\"x\":1,\"id\":\"steel_wall\"}," +
        "{\"x\":2,\"id\":\"call me at 0123\",\"label\":\"Bridge\"},{\"x\":3,\"id\":\"console\",\"label\":\"Bridge\"}]}}}";

    private static byte[] Gzip(string text)
    {
        using var output = new System.IO.MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    /// <summary>A body as the game's CreationSubmission builds it: the facts in <c>reportJson.&lt;kind&gt;</c>, the
    /// document as the one attachment, the preview in the screenshot field.</summary>
    internal static string Payload(string kind, byte[] document, string mime = "application/json", string fingerprint = "00ff00ff00ff00ff", bool consent = true)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Ship: Star Hopper (star_hopper)",
            ["description"] = "It has two engines.",
            ["gameVersion"] = "2026.11.7",
            ["playerName"] = "Layex",
            ["replyKey"] = string.Empty,
            ["reportJson"] = new Dictionary<string, object?>
            {
                ["reportType"] = kind + "-submission",
                [kind] = new Dictionary<string, object?>
                {
                    ["key"] = "star_hopper",
                    ["name"] = "Star Hopper <script>",
                    ["nickname"] = "Layex",
                    ["note"] = "It has two engines.",
                    ["gameVersion"] = "2026.11.7",
                    ["blockFingerprint"] = fingerprint,
                    ["formatVersion"] = 1,
                    ["cellCount"] = 4,
                    ["basedOn"] = "hammerhead",
                    ["consent"] = new Dictionary<string, object?> { ["self"] = true, ["grant"] = consent, ["age"] = true, ["textVersion"] = 1 },
                },
            },
            ["screenshot"] = new Dictionary<string, object?> { ["fileName"] = "preview.png", ["mimeType"] = "image/png", ["base64"] = Convert.ToBase64String(Png) },
            ["attachments"] = new[]
            {
                new Dictionary<string, object?> { ["fileName"] = "x", ["mimeType"] = mime, ["base64"] = Convert.ToBase64String(document) },
            },
        });

    [Theory]
    [InlineData("ship", ReportIngest.ShipCategory)]
    [InlineData("structure", ReportIngest.StructureCategory)]
    [InlineData("avatar", ReportIngest.AvatarCategory)]
    [InlineData("hud", ReportIngest.HudCategory)]
    public void EveryCreationKind_GetsItsOwnCategory_AndItsFileUnderAServerChosenName(string kind, string category)
    {
        var parsed = ReportIngest.Parse(Payload(kind, Gzip(ShipDocument), "application/gzip"), new ReportHostConfig(), out string error);

        Assert.Equal(string.Empty, error);
        Assert.Equal(category, parsed!.Category);
        Assert.Equal(string.Empty, parsed.Kind);
        var file = Assert.Single(parsed.Attachments);
        Assert.Equal((category + "0.gz", "application/gzip"), (file.FileName, file.Mime));
        Assert.Equal("png", parsed.ScreenshotExtension);
        Assert.True(ReportIngest.IsSubmission(category));
    }

    [Fact]
    public void AnUnknownSubmissionType_IsFeedback_AndCarriesNoFiles()
    {
        var parsed = ReportIngest.Parse(Payload("spaceship", Encoding.UTF8.GetBytes(ShipDocument)), new ReportHostConfig(), out _);

        Assert.Equal("feedback", parsed!.Category);
        Assert.Empty(parsed.Attachments);
        Assert.Equal(string.Empty, ReportIngest.SubmissionCategoryOf("feedback-submission"));
    }

    [Fact]
    public void ShipsAndStructures_MayBringABiggerFile_ThanTexturesAndSkins()
    {
        var config = new ReportHostConfig();
        byte[] big = new byte[config.MaxAttachmentBytes + 1000];

        Assert.Single(ReportIngest.Parse(Payload("ship", big), config, out _)!.Attachments);
        Assert.Single(ReportIngest.Parse(Payload("structure", big), config, out _)!.Attachments);
        Assert.Empty(ReportIngest.Parse(Payload("avatar", big), config, out _)!.Attachments);
        Assert.Empty(ReportIngest.Parse(Payload("hud", big), config, out _)!.Attachments);
        Assert.Empty(ReportIngest.Parse(Payload("ship", new byte[config.MaxCreationAttachmentBytes + 1]), config, out _)!.Attachments);
        Assert.Equal(2_000_000, config.MaxCreationAttachmentBytes);
    }

    [Fact]
    public void EverySubmissionCategory_IsDeletedAfterTheRetentionPeriod_UnlessAdopted()
    {
        var store = NewStore();
        const long day = 86400;
        var config = new ReportHostConfig();
        var old = new[] { "ship", "structure", "avatar", "hud" }
            .Select(kind => store.Add(ReportIngest.Parse(Payload(kind, Encoding.UTF8.GetBytes(ShipDocument)), config, out _)!, nowUnix: 0))
            .ToList();
        string adopted = store.Add(ReportIngest.Parse(Payload("ship", Encoding.UTF8.GetBytes(ShipDocument)), config, out _)!, nowUnix: 0);
        Assert.True(store.SetFixedInVersion(adopted, "2026.12.1"));

        int removed = store.PruneSubmissions(config.SubmissionRetentionDays, nowUnix: 366 * day);

        Assert.Equal(4, removed);
        Assert.All(old, id => Assert.Null(store.Get(id)));
        Assert.NotNull(store.Get(adopted));
    }

    [Fact]
    public void TheAdminCard_ListsEveryFreeText_EncodesIt_AndShowsThePreview()
    {
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(Payload("ship", Gzip(ShipDocument), "application/gzip"), new ReportHostConfig(), out _)!, nowUnix: 1_000);
        var record = store.Get(id)!;

        string page = ReportHostPages.Detail(record, store.ListReplies(id), attachments: store.Attachments(id),
            readAttachment: a => System.IO.File.ReadAllBytes(store.AttachmentPath(a)!));

        Assert.Contains("Submitted ship", page);
        Assert.Contains($"/admin/report/{id}/screenshot", page);
        Assert.Contains($"href='/admin/report/{id}/attachment/0'", page);
        Assert.Contains("ship0.gz", page);
        Assert.Contains("Star Hopper &lt;script&gt;", page);      // the name, encoded
        Assert.DoesNotContain("<script>", page);
        Assert.Contains("Fast &lt;b&gt;and&lt;/b&gt; small", page); // the description inside the file
        Assert.Contains("call me at 0123", page);                  // a sentence in a block id still shows
        Assert.Contains("all three given", page);
        Assert.Contains("Built with a different block set", page);

        var texts = SubmissionCard.FreeTexts(ShipDocument);
        Assert.Equal(2, texts.Single(t => t.Text == "Bridge").Count);
        Assert.DoesNotContain(texts, t => t.Text is "steel_wall" or "reactor" or "star_hopper"); // keys are no free text
    }

    [Fact]
    public void TheAdminCard_WarnsOnlyWhenTheBlockSetDiffers_AndFlagsAMissingConsent()
    {
        var store = NewStore();
        string current = SubmissionCard.CurrentBlockFingerprint;
        string same = store.Add(ReportIngest.Parse(Payload("structure", Encoding.UTF8.GetBytes(ShipDocument), fingerprint: current, consent: false), new ReportHostConfig(), out _)!, nowUnix: 1);

        string page = ReportHostPages.Detail(store.Get(same)!, attachments: store.Attachments(same));

        Assert.DoesNotContain("different block set", page);
        Assert.Contains("same block set", page);
        Assert.Contains("INCOMPLETE", page);
        Assert.Contains("Submitted station / town module", page);
    }

    [Fact]
    public void TheInboxFingerprint_IsTheGamesOwn_AndAGzipBombStaysClosed()
    {
        Assert.Equal(ContentLoader.LoadFromDirectory(TestPaths.DataDir()).BlockFingerprint, SubmissionCard.CurrentBlockFingerprint);

        byte[] bomb = Gzip(new string(' ', SubmissionCard.MaxUnpackedBytes + 10));
        Assert.Null(SubmissionCard.Unpack(bomb, "application/gzip", out string problem));
        Assert.Contains("more than", problem);
    }

    [Fact]
    public void TheListFilter_OffersEveryCategory()
    {
        string list = ReportHostPages.List(Array.Empty<BugReportRecord>(), new Dictionary<string, int>(), null, "avatar");

        foreach (string category in new[] { "feedback", "crash", "texture", "ship", "structure", "avatar", "hud" })
        {
            Assert.Contains($"<option value='{category}'", list);
        }

        Assert.Contains("<option value='avatar' selected>", list);
    }

    /// <summary>#3319: a HUD frame's card names the kind, and its four tiles and light colours are no free text — only the
    /// name the player typed is.</summary>
    [Fact]
    public void AHudFrameCard_ListsTheName_NotTheTiles()
    {
        string tile = new string('a', 1024);
        string hud = "{\"formatVersion\":1,\"kind\":\"hud\",\"key\":\"snow\",\"files\":{\"hud.json\":{\"formatVersion\":1," +
            "\"key\":\"snow\",\"name\":\"Snowy <i>frame</i>\",\"size\":1,\"corner\":\"" + tile + "\",\"top\":\"\",\"side\":\"" + tile +
            "\",\"bottom\":\"0\",\"twinkle\":[25],\"twinkleColors\":[\"#fff4d6\"]}}}";
        var store = NewStore();
        string id = store.Add(ReportIngest.Parse(Payload("hud", Encoding.UTF8.GetBytes(hud)), new ReportHostConfig(), out _)!, nowUnix: 1_000);

        string page = ReportHostPages.Detail(store.Get(id)!, store.ListReplies(id), attachments: store.Attachments(id),
            readAttachment: a => System.IO.File.ReadAllBytes(store.AttachmentPath(a)!));

        Assert.Contains("Submitted HUD frame", page);
        Assert.Contains("--kind hud", page);
        var texts = SubmissionCard.FreeTexts(hud);
        Assert.Equal("Snowy <i>frame</i>", Assert.Single(texts).Text);
        Assert.Contains("Snowy &lt;i&gt;frame&lt;/i&gt;", page);
    }
}
