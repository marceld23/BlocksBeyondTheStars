// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.ReportHost;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #4080: a report's detail page names the world it came from — "name · seed n" in the Meta table — so the operator need
/// not scroll the raw JSON. The client sends <c>worldName</c>/<c>worldSeed</c>, a server crash report <c>world</c>/<c>seed</c>.
/// </summary>
public sealed class ReportHostWorldLineTests
{
    [Theory]
    [InlineData("{\"reportJson\":{\"worldName\":\"Lyxettes_Welt\",\"worldSeed\":-4711}}", "Lyxettes_Welt · seed -4711")]
    [InlineData("{\"reportJson\":{\"world\":\"server_world\",\"seed\":42,\"source\":\"server\"}}", "server_world · seed 42")]
    [InlineData("{\"reportJson\":{\"world\":\"old_server\",\"seed\":null}}", "old_server")] // a server crash without a seed
    [InlineData("{\"reportJson\":{\"worldSeed\":99}}", "seed 99")] // an F1 report from a client before #4080
    [InlineData("{\"reportJson\":{\"worldName\":\"\",\"worldSeed\":0}}", "")] // no world (the F1 report's placeholders)
    [InlineData("{\"reportJson\":{\"source\":\"menu\"}}", "")] // a menu report names no world
    [InlineData("{\"snapshot\":{}}", "")]
    [InlineData("{ not json", "")]
    public void WorldLine_ReadsTheClientAndTheServerKeys(string payload, string line)
        => Assert.Equal(line, ReportHostPages.WorldLine(payload));

    [Fact]
    public void Detail_ShowsTheWorldRow_OnlyWhenTheReportNamesAWorld()
    {
        string withWorld = ReportHostPages.Detail(Row("{\"reportJson\":{\"worldName\":\"Lyxettes_Welt\",\"worldSeed\":-4711}}"));
        string cell = System.Net.WebUtility.HtmlEncode("Lyxettes_Welt · seed -4711"); // the page escapes every value
        Assert.Contains("<tr><th>World</th><td>" + cell + "</td></tr>", withWorld, System.StringComparison.Ordinal);

        string withoutWorld = ReportHostPages.Detail(Row("{\"reportJson\":{\"source\":\"menu\"}}"));
        Assert.DoesNotContain("<th>World</th>", withoutWorld, System.StringComparison.Ordinal);
    }

    private static BugReportRecord Row(string payload)
        => new(
            Id: "0123456789abcdef0123456789abcdef",
            Title: "the sky is green",
            Description: "it really is",
            Email: "",
            GameVersion: "2026.11.33",
            BuildNumber: "",
            PlayerId: "token",
            PlayerName: "Pilot",
            SessionId: "",
            Platform: "WindowsPlayer",
            ClientTimestamp: "",
            Category: "feedback",
            Source: "",
            Kind: "",
            Status: "new",
            ScreenshotFile: "",
            ReportJson: payload,
            CreatedUnix: 1_790_000_000,
            ReplyKey: "",
            FixedInVersion: "");
}
