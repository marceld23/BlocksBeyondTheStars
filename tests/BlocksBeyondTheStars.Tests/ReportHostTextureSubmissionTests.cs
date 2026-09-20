// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using BlocksBeyondTheStars.ReportHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Texture submissions in the report inbox (#1966): a third category next to feedback and crash, files that
/// travel with the report, and a retention rule of their own — a submission that was not adopted is deleted
/// after a year, because that is what the player was told when they pressed the button.
/// </summary>
public sealed class ReportHostTextureSubmissionTests : IDisposable
{
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    internal static readonly byte[] Raw = Enumerable.Range(0, 64).Select(i => (byte)i).ToArray();

    private readonly string _root;
    private readonly List<ReportStore> _stores = new();

    public ReportHostTextureSubmissionTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_rht_" + Guid.NewGuid().ToString("N"));
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

    private (ReportStore Store, string Dir) NewStore()
    {
        string dir = System.IO.Path.Combine(_root, Guid.NewGuid().ToString("N"));
        var store = new ReportStore(new ReportHostConfig(), System.IO.Path.Combine(dir, "reports.db"));
        _stores.Add(store);
        return (store, dir);
    }

    private static Dictionary<string, object?> Attachment(string mime, byte[] bytes, string fileName = "whatever.bin")
        => new() { ["fileName"] = fileName, ["mimeType"] = mime, ["base64"] = Convert.ToBase64String(bytes) };

    /// <summary>A payload as the game's submit dialog sends it: an ordinary feedback report whose
    /// <c>reportJson</c> names the type and carries the consent, plus the two files at the root.</summary>
    internal static string SubmissionPayload(string kind = "", string reportType = ReportIngest.TextureSubmissionType, params Dictionary<string, object?>[] attachments)
    {
        if (attachments.Length == 0)
        {
            attachments = new[] { Attachment("image/png", Png, "texture.png"), Attachment("application/octet-stream", Raw, "texture.bytes") };
        }

        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["title"] = "Texture: campfire",
            ["description"] = "A campfire with blue flames.",
            ["email"] = "",
            ["gameVersion"] = "2026.9.12",
            ["playerId"] = "token-abc",
            ["playerName"] = "Screelit",
            ["platform"] = "WindowsPlayer",
            ["clientTimestamp"] = "2026-09-20T12:00:00Z",
            ["reportJson"] = new Dictionary<string, object?>
            {
                ["reportType"] = reportType,
                ["kind"] = kind,
                ["texture"] = new Dictionary<string, object?>
                {
                    ["key"] = "campfire",
                    ["nickname"] = "Screelit",
                    ["frames"] = 1,
                    ["fps"] = 0,
                    ["consent"] = new Dictionary<string, object?> { ["self"] = true, ["grant"] = true, ["age"] = true, ["textVersion"] = 1 },
                },
            },
            ["attachments"] = attachments,
        });
    }

    [Fact]
    public void ASubmission_GetsItsOwnCategory_AndItsFilesUnderServerChosenNames()
    {
        var parsed = ReportIngest.Parse(SubmissionPayload(), new ReportHostConfig(), out string error);

        Assert.NotNull(parsed);
        Assert.Equal(string.Empty, error);
        Assert.Equal(ReportIngest.TextureCategory, parsed!.Category);
        Assert.Equal(2, parsed.Attachments.Count);
        Assert.Equal(("texture0.png", "image/png"), (parsed.Attachments[0].FileName, parsed.Attachments[0].Mime));
        Assert.Equal(("texture1.bytes", "application/octet-stream"), (parsed.Attachments[1].FileName, parsed.Attachments[1].Mime));
        Assert.Equal(Raw, parsed.Attachments[1].Bytes);

        // The consent stays in the stored JSON, the base64 does not.
        using var doc = JsonDocument.Parse(parsed.ReportJson);
        Assert.False(doc.RootElement.TryGetProperty("attachments", out _));
        Assert.True(doc.RootElement.GetProperty("reportJson").GetProperty("texture").GetProperty("consent").GetProperty("grant").GetBoolean());
    }

    [Fact]
    public void ACrashKind_StillWins_AndOrdinaryFeedbackCarriesNoFiles()
    {
        var crash = ReportIngest.Parse(SubmissionPayload(kind: "tick-fault"), new ReportHostConfig(), out _);
        var feedback = ReportIngest.Parse(SubmissionPayload(reportType: "something-else"), new ReportHostConfig(), out _);

        Assert.Equal("crash", crash!.Category);
        Assert.Empty(crash.Attachments);
        Assert.Equal("feedback", feedback!.Category);
        Assert.Empty(feedback.Attachments);
    }

    [Fact]
    public void ABadFile_IsDropped_TheReportIsKept()
    {
        var config = new ReportHostConfig { MaxAttachmentBytes = 100, MaxAttachments = 2 };
        var broken = Attachment("image/png", Png);
        broken["base64"] = "@@ not base64 @@";
        string body = SubmissionPayload(attachments: new[]
        {
            Attachment("text/html", Png, "../../evil.html"),      // not a type the inbox stores
            Attachment("image/png", new byte[101]),               // over the size cap
            broken,
            Attachment("image/png", Png, "..\\..\\evil.png"),     // fine — and the name is not the client's
            Attachment("application/json", Raw),
            Attachment("image/png", Png),                         // over the count cap
        });

        var parsed = ReportIngest.Parse(body, config, out _);

        Assert.NotNull(parsed);
        Assert.Equal(new[] { "texture0.png", "texture1.json" }, parsed!.Attachments.Select(a => a.FileName).ToArray());
    }

    [Fact]
    public void TheStore_KeepsTheFiles_AndDeletingTheReportRemovesThem()
    {
        var (store, dir) = NewStore();
        string id = store.Add(ReportIngest.Parse(SubmissionPayload(), new ReportHostConfig(), out _)!, nowUnix: 1_000);

        var attachments = store.Attachments(id);

        Assert.Equal(2, attachments.Count);
        Assert.Equal(Raw.Length, attachments[1].Bytes);
        string path = store.AttachmentPath(attachments[1])!;
        Assert.StartsWith(System.IO.Path.Combine(dir, "attachments"), path);
        Assert.Equal(Raw, System.IO.File.ReadAllBytes(path));
        Assert.Equal(ReportIngest.TextureCategory, store.Get(id)!.Category);

        Assert.True(store.Delete(id));

        Assert.Empty(store.Attachments(id));
        Assert.False(System.IO.File.Exists(path));
        Assert.Empty(System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "attachments")));
    }

    [Fact]
    public void ASubmissionThatWasNotAdopted_IsDeletedAfterTheRetentionPeriod_AnAdoptedOneStays()
    {
        var (store, dir) = NewStore();
        const long day = 86400;
        var config = new ReportHostConfig();
        string oldOne = store.Add(ReportIngest.Parse(SubmissionPayload(), config, out _)!, nowUnix: 0);
        string adopted = store.Add(ReportIngest.Parse(SubmissionPayload(), config, out _)!, nowUnix: 0);
        string young = store.Add(ReportIngest.Parse(SubmissionPayload(), config, out _)!, nowUnix: 300 * day);
        string feedback = store.Add(ReportIngest.Parse(SubmissionPayload(reportType: ""), config, out _)!, nowUnix: 0);
        Assert.True(store.SetFixedInVersion(adopted, "2026.10.1"));

        Assert.Equal(365, config.TextureRetentionDays); // what the privacy page promises
        Assert.Equal(0, store.PruneTextureSubmissions(0, nowUnix: 9_999 * day)); // 0 = keep forever
        int removed = store.PruneTextureSubmissions(config.TextureRetentionDays, nowUnix: 366 * day);

        Assert.Equal(1, removed);
        Assert.Null(store.Get(oldOne));
        Assert.NotNull(store.Get(adopted));
        Assert.NotNull(store.Get(young));
        Assert.NotNull(store.Get(feedback)); // ordinary feedback follows the general retention, not this one
        Assert.Equal(4, System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "attachments")).Length); // two reports × two files
    }

    [Fact]
    public void TheGeneralRetention_TakesTheFilesAlong()
    {
        var (store, dir) = NewStore();
        store.Add(ReportIngest.Parse(SubmissionPayload(), new ReportHostConfig(), out _)!, nowUnix: 0);

        Assert.Equal(1, store.Prune(retentionDays: 30, nowUnix: 31 * 86400L));

        Assert.Empty(System.IO.Directory.GetFiles(System.IO.Path.Combine(dir, "attachments")));
    }

    [Fact]
    public void TheAdminPages_ListTheCategory_AndShowTheTextureWithItsDownloads()
    {
        var (store, _) = NewStore();
        string id = store.Add(ReportIngest.Parse(SubmissionPayload(), new ReportHostConfig(), out _)!, nowUnix: 1_000);

        string detail = ReportHostPages.Detail(store.Get(id)!, store.ListReplies(id), attachments: store.Attachments(id));
        string plain = ReportHostPages.Detail(store.Get(id)!, store.ListReplies(id));

        Assert.Contains("Submitted texture", detail);
        Assert.Contains($"/admin/report/{id}/attachment/0/view", detail);
        Assert.Contains($"href='/admin/report/{id}/attachment/1'", detail);
        Assert.Contains("texture1.bytes", detail);
        Assert.DoesNotContain("Submitted texture", plain);
    }
}

/// <summary>The download routes of a texture submission over real HTTP (#1966): the operator gets the files
/// by name, the page gets the picture inline, and nobody gets either without the admin login.</summary>
[Collection(RealTimeSensitiveCollection.Name)] // loopback requests are billed the parallel queue wait otherwise (#1362)
public sealed class ReportHostTextureSubmissionHttpTests : IClassFixture<ReportHostAdminHttpFixture>
{
    private static readonly string Basic = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(
        ReportHostAdminHttpFixture.AdminUser + ":" + ReportHostAdminHttpFixture.AdminPassword));

    private readonly ReportHostAdminHttpFixture _host;

    public ReportHostTextureSubmissionHttpTests(ReportHostAdminHttpFixture host)
    {
        _host = host;
    }

    private async Task<HttpResponseMessage> GetAsync(string path, bool authorized = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Forwarded-For", "10.0.3.1");
        if (authorized)
        {
            request.Headers.TryAddWithoutValidation("Authorization", Basic);
        }

        return await _host.Client.SendAsync(request);
    }

    [Fact]
    public async Task TheOperator_DownloadsTheFilesByName_AndSeesThePictureInlineAsync()
    {
        var parsed = ReportIngest.Parse(ReportHostTextureSubmissionTests.SubmissionPayload(), new ReportHostConfig(), out _);
        string id = _host.Store.Add(parsed!, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using var download = await GetAsync($"/admin/report/{id}/attachment/1");
        using var picture = await GetAsync($"/admin/report/{id}/attachment/0/view");
        using var notAPicture = await GetAsync($"/admin/report/{id}/attachment/1/view");
        using var missing = await GetAsync($"/admin/report/{id}/attachment/7");
        using var stranger = await GetAsync($"/admin/report/{id}/attachment/1", authorized: false);
        using var noReadKey = await GetAsync($"/api/reports/{id}/attachment/1", authorized: false);
        using var page = await GetAsync($"/admin/report/{id}");

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("texture1.bytes", download.Content.Headers.ContentDisposition?.FileNameStar ?? download.Content.Headers.ContentDisposition?.FileName);
        Assert.Equal(ReportHostTextureSubmissionTests.Raw, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.OK, picture.StatusCode);
        Assert.Equal("image/png", picture.Content.Headers.ContentType?.MediaType);
        Assert.Null(picture.Content.Headers.ContentDisposition); // inline, for the <img> on the page
        Assert.Equal(HttpStatusCode.NotFound, notAPicture.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, noReadKey.StatusCode); // the read API is off without a read key
        Assert.Contains("Submitted texture", await page.Content.ReadAsStringAsync());
    }
}
