// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3642: the client's side of the lost civilizations, Unity-free — the Codex's own discovery kind <c>civilization</c> (ledger
/// <c>civ:&lt;key&gt;</c>, its title and text from the civilization file the client loads too), the scanner card's title of a
/// site, the "built by" line of a block a civilization built and the toast of a new discovery (<see cref="CivilizationText"/>).
/// A test-made civilization, so nothing depends on the shipped ones.
/// </summary>
public sealed class CivilizationClientTests
{
    private const string Key = "test_people";

    private static readonly Dictionary<string, string> Texts = new()
    {
        ["civ.test_people.name"] = "The Test People",
        ["civ.test_people.codex.title"] = "The Test People of Old",
        ["civ.test_people.codex.text"] = "They built with stone and left long ago.",
        ["ui.map.kind_civilization"] = "Ancient ruin",
        ["ui.scan.civilization_block"] = "Built by: {0}",
        ["ui.wiki.civilization.new"] = "Discovered: {0} — see the Codex, Lost civilizations",
    };

    private static string? Text(string key) => Texts.TryGetValue(key, out var t) ? t : null;

    /// <summary>The shipped content with only the test civilization, and the stone tagged as its block.</summary>
    private static GameContent ContentWithPeople()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var civ = new CivilizationDefinition
        {
            Key = Key,
            NameKey = "civ.test_people.name",
            Sites = new List<CivilizationSitePool> { new() { Pool = CivilizationRules.PoolGiant } },
            Codex = new CivilizationCodex { TitleKey = "civ.test_people.codex.title", TextKey = "civ.test_people.codex.text" },
        };
        var warnings = new List<string>();
        content.SetCivilizations(new[] { civ }, Array.Empty<StructureTemplate>(), warnings.Add);
        Assert.NotNull(content.GetCivilization(Key));
        content.GetBlock("stone")!.Civilization = Key;
        return content;
    }

    [Fact]
    public void TheCodex_ListsACivilization_UnderItsOwnKind_WithTheTitleAndTextOfItsFile()
    {
        var content = ContentWithPeople();
        const string Ledger = "civ:" + Key;
        Assert.Equal(Ledger, CivilizationRules.DiscoveryKey(Key)); // the server's ledger shape
        Assert.Equal("civilization", CivilizationText.CodexGroup);
        Assert.Equal(CivilizationText.CodexGroup, CivilizationText.CodexKind(Ledger));
        Assert.Equal(Key, CivilizationText.LedgerCivilization(Ledger));

        // Not a civilization: the caller's "kind before the first colon" rule applies.
        Assert.Null(CivilizationText.CodexKind("civ:"));
        Assert.Null(CivilizationText.CodexKind("creature:sp1"));
        Assert.Null(CivilizationText.CodexKind("landmark:sys0-p1:tower"));
        Assert.Null(CivilizationText.CodexKind("giantwreck:sys0-p1:liner"));
        Assert.Null(CivilizationText.CodexKind(null));

        // The Codex title and text in this client's language — not the name the server recorded in another one.
        Assert.Equal(("The Test People of Old", "They built with stone and left long ago."),
            CivilizationText.CodexLine(Ledger, "Das Testvolk", content, Text));

        // Without a Codex title the people's name; a civilization this client does not know keeps the recorded name, then the key.
        content.GetCivilization(Key)!.Codex.TitleKey = string.Empty;
        Assert.Equal("The Test People", CivilizationText.CodexLine(Ledger, "Das Testvolk", content, Text).Name);
        Assert.Equal(("Old Folk", string.Empty), CivilizationText.CodexLine("civ:old_folk", "Old Folk", content, Text));
        Assert.Equal(("old_folk", string.Empty), CivilizationText.CodexLine("civ:old_folk", string.Empty, content, Text));
        Assert.Equal(("old_folk", string.Empty), CivilizationText.CodexLine("civ:old_folk", null, null, Text));
    }

    [Fact]
    public void TheScannerCard_NamesThePeopleAndTheSite_AndABlockNamesItsBuilders()
    {
        var content = ContentWithPeople();
        Assert.True(CivilizationText.IsReadout("civilization"));
        Assert.False(CivilizationText.IsReadout("landmark"));
        Assert.Equal(Key, CivilizationText.SubjectCivilization("civ_" + Key));
        Assert.Null(CivilizationText.SubjectCivilization("civ_"));
        Assert.Null(CivilizationText.SubjectCivilization("landmark_tower"));

        Assert.Equal("The Test People · Sunken City", CivilizationText.ReadoutTitle("civ_" + Key, "Sunken City", content, Text));
        Assert.Equal("Ancient ruin · Sunken City", CivilizationText.ReadoutTitle("civ_old_folk", "Sunken City", content, Text));
        Assert.Equal("The Test People", CivilizationText.ReadoutTitle("civ_" + Key, string.Empty, content, Text));

        Assert.Equal("Built by: The Test People", CivilizationText.BlockLine(content, "stone", Text));
        Assert.Null(CivilizationText.BlockLine(content, "dirt", Text));
        Assert.Null(CivilizationText.BlockLine(content, null, Text));
        Assert.Null(CivilizationText.BlockLine(content, "stone", key => key == "ui.scan.civilization_block" ? null : Text(key)));
    }

    [Fact]
    public void ANewDiscovery_SaysSo_OnlyForACivilization()
    {
        var content = ContentWithPeople();
        Assert.Equal("Discovered: The Test People of Old — see the Codex, Lost civilizations",
            CivilizationText.NewDiscoveryLine("civ:" + Key, "The Test People", content, Text));
        Assert.Null(CivilizationText.NewDiscoveryLine("block:stone", "stone", content, Text));
        Assert.Null(CivilizationText.NewDiscoveryLine("civ:" + Key, "The Test People", content, _ => null));
    }

    [Fact]
    public void TheWordsOfTheCivilizations_ExistInEnglishAndGerman()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")))!;
            foreach (string key in new[]
                     {
                         CivilizationText.KindKey, CivilizationText.BlockLineKey, CivilizationText.NewDiscoveryKey,
                         "ui.wiki.discoveries." + CivilizationText.CodexGroup, "ui.scan.civilization",
                     })
            {
                Assert.True(table.TryGetValue(key, out var text) && text.Length > 0, $"{code}.json lacks '{key}'");
            }

            Assert.Contains("{0}", table[CivilizationText.BlockLineKey]);
            Assert.Contains("{0}", table[CivilizationText.NewDiscoveryKey]);
        }
    }
}
