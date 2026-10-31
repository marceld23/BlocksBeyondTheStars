// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2971/#3031: the Codex's "Enemies" chapter — every enemy shows its numbers from the start, a met one carries
/// "✓ met", and the head counts the met ones.</summary>
public sealed class EnemyCodexTextTests
{
    private static string L(string key) => key switch
    {
        "ui.wiki.enemies.count" => "{0} of {1} met.",
        "ui.wiki.enemies.met" => "met",
        "enemy.ufo.name" => "UFO",
        "enemy.ufo.desc" => "A strange craft.",
        "enemy.ufo.tip" => "Shields up.",
        "enemy.bandit_ship.name" => "Raider ship",
        "enemy.space_drone.name" => "Drone",
        _ => key,
    };

    [Fact]
    public void EveryEnemy_ShowsItsNumbers_AndOnlyTheMetOnesAreMarked()
    {
        var enemies = EnemyCatalog.Defaults();
        string text = EnemyCodexText.Build(enemies, new HashSet<string> { EnemyCatalog.Ufo, EnemyCatalog.BanditShip }, L, item => "<" + item + ">");

        Assert.Contains("2 of 9 met.", text, StringComparison.Ordinal);
        Assert.Contains("<b>UFO</b>  <color=#7fe0a8>✓ met</color>", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.health: 40 (ui.wiki.enemies.elite: 95)", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.damage: 4/s (ui.wiki.enemies.elite: 8/s)", text, StringComparison.Ordinal);
        Assert.Contains("3× <data_fragment>", text, StringComparison.Ordinal);
        Assert.Contains("<i>A strange craft.</i>", text, StringComparison.Ordinal);
        Assert.Contains("Shields up.", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.health: 55–120", text, StringComparison.Ordinal); // a raider's hull scales
        Assert.DoesNotContain("enemy.bandit_ship.desc", text, StringComparison.Ordinal); // an untranslated line is left out
        Assert.DoesNotContain("???", text, StringComparison.Ordinal); // #3031: nothing hides behind a first meeting
        Assert.Contains("<b>Drone</b>\n", text, StringComparison.Ordinal); // an enemy not met yet: its name, no mark
        Assert.Equal(2, text.Split("✓").Length - 1);
        Assert.Equal(9, text.Split("ui.wiki.enemies.health:").Length - 1); // all nine with their numbers
        Assert.True(text.IndexOf("ui.wiki.enemies.where_space", StringComparison.Ordinal) < text.IndexOf("ui.wiki.enemies.where_planet", StringComparison.Ordinal));
    }

    [Fact]
    public void ThePieces_MakeTheWholeChapter()
    {
        var enemies = EnemyCatalog.Defaults();
        var met = new HashSet<string> { EnemyCatalog.Ufo };
        var sb = new System.Text.StringBuilder(EnemyCodexText.Head(enemies, met, L));
        int count = 0;
        foreach (var (where, list) in EnemyCodexText.Groups(enemies))
        {
            sb.Append(EnemyCodexText.GroupHeading(where, L));
            foreach (var e in list)
            {
                sb.Append(EnemyCodexText.Entry(e, met.Contains(e.Key), L, item => item));
                count++;
            }
        }

        Assert.Equal(enemies.Count, count); // every enemy sits in one group
        Assert.Equal(EnemyCodexText.Build(enemies, met, L, item => item), sb.ToString());
    }
}
