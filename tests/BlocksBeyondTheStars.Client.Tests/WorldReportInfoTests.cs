// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #4080: reports name the world they came from — its name beside its seed. Once a new world's seed is random by default
/// (#4078), the name alone no longer identifies the generated world and the seed alone does not say which save it is.
/// </summary>
public sealed class WorldReportInfoTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>The real server names the world in its join; and the server's fallback for a world created without a
    /// seed — the hash of its name — is exactly what the new-world form's seed field makes of that name typed as a word
    /// (#4078), so an old name-derived world can be made again.</summary>
    [Fact]
    public void TheServer_NamesTheWorldInTheJoin_AndItsNameSeedIsTheFieldsWordSeed()
    {
        using var h = new ClientServerHarness(Content, c => c.Seed = 0L);
        h.Join();

        Assert.NotNull(h.JoinAccepted);
        Assert.Equal("client_it", h.JoinAccepted!.WorldName);
        Assert.Equal(WorldSeedInput.Resolve("client_it", new System.Random(1)), h.JoinAccepted.WorldSeed);
    }

    [Fact]
    public void WriteTo_AddsTheNameAndTheSeed()
    {
        var json = new Dictionary<string, object>();
        new WorldReportInfo("Lyxettes_Welt", -4711L).WriteTo(json);

        Assert.Equal("Lyxettes_Welt", json["worldName"]);
        Assert.Equal(-4711L, json["worldSeed"]);
    }

    [Fact]
    public void WriteTo_AnOlderServerWithoutAName_StillSendsTheSeed()
    {
        var json = new Dictionary<string, object>();
        new WorldReportInfo(null, 42L).WriteTo(json);

        Assert.False(json.ContainsKey(WorldReportInfo.NameKey));
        Assert.Equal(42L, json[WorldReportInfo.SeedKey]);
    }

    [Fact]
    public void TheKeys_AreTheOnesTheF1ReportAndTheInboxUse()
    {
        // "worldSeed" is the key the F1 report always sent; the report inbox's World line reads both keys.
        Assert.Equal("worldSeed", WorldReportInfo.SeedKey);
        Assert.Equal("worldName", WorldReportInfo.NameKey);
    }

    [Fact]
    public void TheClient_SetsTheSnapshotOnJoin_ClearsItOnLeave_AndBothReportsCarryIt()
    {
        string scripts = Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        string boot = File.ReadAllText(Path.Combine(scripts, "GameBootstrap.cs"));
        Assert.Contains("WorldReportInfo.Current = new WorldReportInfo(WorldName, m.WorldSeed);", boot, System.StringComparison.Ordinal);
        Assert.Contains("WorldReportInfo.Current = null;", boot, System.StringComparison.Ordinal);

        Assert.Contains("WorldReportInfo.Current?.WriteTo(reportJson);", File.ReadAllText(Path.Combine(scripts, "CrashReporter.cs")),
            System.StringComparison.Ordinal);

        // The F1 report sends both from a world and drops both in the menu (#2523: no world, nothing to mislead).
        string feedback = File.ReadAllText(Path.Combine(scripts, "FeedbackUi.cs"));
        Assert.Contains("[WorldReportInfo.NameKey] = Game != null ? Game.WorldName : string.Empty,", feedback, System.StringComparison.Ordinal);
        Assert.Contains("WorldReportInfo.SeedKey, WorldReportInfo.NameKey, \"health\"", feedback, System.StringComparison.Ordinal);
    }
}
