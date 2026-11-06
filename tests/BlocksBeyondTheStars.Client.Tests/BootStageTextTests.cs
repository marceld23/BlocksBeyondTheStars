// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Client;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3260: the loading screen names the server's boot stage that is really running. The server announces each pass
/// before it runs it (<c>[boot] &gt; 7/12 landing_pads</c>); the client parses that and shows a grouped, kid-friendly
/// message — and must know every pass the server can announce, or the player reads "Preparing the world…" for it.
/// </summary>
public sealed class BootStageTextTests
{
    [Theory]
    [InlineData("[boot] > 7/12 landing_pads", 7, 12, "landing_pads")]
    [InlineData("2026-11-06 09:20:01 [INFO] [boot] > 1/12 persistence", 1, 12, "persistence")] // the server's own log prefix
    [InlineData("[server] 2026-11-06 09:20:01 [INFO] [boot] > 12/12 ready", 12, 12, "ready")] // as the launcher relays it
    [InlineData("[boot]   > settlements", 0, 0, "settlements")] // a step inside a pass carries no counter
    [InlineData("[SP-Server] [boot]   > npcs", 0, 0, "npcs")]
    public void TryParseStart_ReadsPassesAndSteps(string line, int stage, int stages, string id)
    {
        Assert.True(BootStageText.TryParseStart(line, out int s, out int n, out string parsed));
        Assert.Equal(stage, s);
        Assert.Equal(stages, n);
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("[boot] 7/12 landing pads (4120 ms)")] // the finish line drives the bar, not the message
    [InlineData("[boot]   · settlements (4095 ms)")]
    [InlineData("[boot] 12/12 ready (16897 ms total)")]
    [InlineData("[boot] > 7/x landing_pads")]
    [InlineData("[boot] > 7/12")]
    [InlineData("[boot] >")]
    [InlineData("[boot] > -1/12 galaxy")]
    [InlineData("[boot] > 1/0 galaxy")]
    [InlineData("Server 'x' started on port 31550")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseStart_RejectsEverythingElse(string? line)
        => Assert.False(BootStageText.TryParseStart(line, out _, out _, out _));

    [Theory]
    [InlineData("landing_pads", "ui.loading.stage.landing_pads")]
    [InlineData("weather", "ui.loading.stage.nature")]
    [InlineData("flora", "ui.loading.stage.nature")]
    [InlineData("intercity_rail", "ui.loading.stage.settlements")]
    [InlineData("doors", "ui.loading.stage.residents")]
    [InlineData("sps_labs", "ui.loading.stage.ruins")]
    [InlineData("unique_sites", "ui.loading.stage.treasure")]
    [InlineData("fire", "ui.loading.stage.world")]
    [InlineData("ready", "ui.loading.stage.ready")]
    [InlineData("a_pass_from_a_newer_server", "ui.loading.stage.world")] // unknown → "Preparing the world…"
    [InlineData(null, "ui.loading.stage.world")]
    public void KeyFor_GroupsTheStages(string? id, string key)
        => Assert.Equal(key, BootStageText.KeyFor(id));

    [Fact]
    public void StatusKey_FollowsTheWorldStart()
    {
        const string flavour = "ui.loading.status";

        Assert.Equal(flavour, BootStageText.StatusKey(false, true, false, null, flavour)); // an older server: no start lines
        Assert.Equal(flavour, BootStageText.StatusKey(false, false, false, null, flavour)); // a remote join
        Assert.Equal("ui.loading.stage.creatures", BootStageText.StatusKey(false, true, false, "creatures", flavour));
        Assert.Equal("ui.loading.stage.ready", BootStageText.StatusKey(false, false, true, "unique_sites", flavour));
        Assert.Equal(BootStageText.ClientPrepKey, BootStageText.StatusKey(true, false, true, "ready", flavour));
        Assert.Equal(BootStageText.ClientPrepKey, BootStageText.StatusKey(true, false, false, null, flavour)); // remote join hand-off
    }

    /// <summary>Every pass and step the server announces (GameServer*.cs <c>BootStage("…")</c> / <c>BootDetail("…")</c>, with
    /// the server's id rule: trimmed, lower-cased, spaces as <c>_</c>) has its own message — a new pass must be mapped.</summary>
    [Fact]
    public void EveryServerBootStage_HasItsOwnMessage()
    {
        string dir = Path.Combine(ClientTestPaths.RepoRoot(), "src", "BlocksBeyondTheStars.GameServer");
        var call = new Regex("Boot(Stage|Detail)\\(\"(?<name>[^\"]+)\"", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1));
        var ids = Directory.GetFiles(dir, "GameServer*.cs")
            .SelectMany(f => call.Matches(File.ReadAllText(f)).Select(m => m.Groups["name"].Value))
            .Select(name => name.Trim().ToLowerInvariant().Replace(' ', '_'))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.Contains("landing_pads", ids); // the scan found the passes at all
        Assert.Contains("npcs", ids);
        var unmapped = ids.Where(id => !BootStageText.IsKnown(id)).ToList();
        Assert.True(unmapped.Count == 0, "boot stages without a loading message (add them to BootStageText): " + string.Join(", ", unmapped));
        Assert.True(BootStageText.IsKnown(BootStageText.ReadyId));
    }

    [Fact]
    public void EveryMessage_IsInEnglishAndGerman()
    {
        var keys = BootStageText.AllKeys.Append("ui.save.loading_worlds").ToList();
        Assert.Equal(12, keys.Count); // ten stage groups + the client's own step + the world list's line

        foreach (string locale in new[] { "en", "de" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", locale + ".json")));
            foreach (string key in keys)
            {
                Assert.True(doc.RootElement.TryGetProperty(key, out var text) && text.GetString()!.Length > 0, $"{locale}.json misses {key}");
            }
        }
    }
}
