// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>The developer editors' key rule (#3694): only a–z, 0–9 and _, a space named as such, blanks around ignored.</summary>
public sealed class ContentKeyRulesTests
{
    [Theory]
    [InlineData("my_item")]
    [InlineData("iron_plate")]
    [InlineData("ai_core_mk2")]
    [InlineData("  my_item  ")]
    public void Check_AcceptsShippedStyleKeys(string key) => Assert.Equal(ContentKeyProblem.None, ContentKeyRules.Check(key));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Check_EmptyKey_IsEmpty(string? key) => Assert.Equal(ContentKeyProblem.Empty, ContentKeyRules.Check(key));

    [Theory]
    [InlineData("mein material")]
    [InlineData("my\titem")]
    [InlineData("My Item")] // the space is named first: it is the slip a child makes
    public void Check_SpaceInside_IsSpace(string key) => Assert.Equal(ContentKeyProblem.Space, ContentKeyRules.Check(key));

    [Theory]
    [InlineData("My_Item")]
    [InlineData("my-item")]
    [InlineData("eisenerz_größer")]
    [InlineData("item.name")]
    public void Check_OtherCharacters_AreBad(string key) => Assert.Equal(ContentKeyProblem.BadCharacter, ContentKeyRules.Check(key));

    [Fact]
    public void Clean_TrimsBlanks_AndNeverReturnsNull()
    {
        Assert.Equal("my_item", ContentKeyRules.Clean(" my_item \t"));
        Assert.Equal(string.Empty, ContentKeyRules.Clean(null));
    }

    [Fact]
    public void EveryShippedItemAndBlockKey_PassesTheRule()
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        foreach (string file in new[] { "items.json", "blocks.json" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), file)), options);
            int checkedKeys = 0;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
                {
                    checkedKeys++;
                    Assert.True(ContentKeyRules.Check(key.GetString()) == ContentKeyProblem.None,
                        $"{file}: '{key.GetString()}' breaks the editors' key rule");
                }
            }

            Assert.True(checkedKeys > 50, $"{file}: read only {checkedKeys} keys");
        }
    }
}
