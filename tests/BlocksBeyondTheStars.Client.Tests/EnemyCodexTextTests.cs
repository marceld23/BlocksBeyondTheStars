// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>#2971: the Codex's "Enemies" chapter — a met enemy shows its numbers, one not met yet is "???".</summary>
public sealed class EnemyCodexTextTests
{
    private static string L(string key) => key switch
    {
        "ui.wiki.enemies.count" => "{0} of {1} met.",
        "enemy.ufo.name" => "UFO",
        "enemy.ufo.desc" => "A strange craft.",
        "enemy.ufo.tip" => "Shields up.",
        "enemy.bandit_ship.name" => "Raider ship",
        _ => key,
    };

    [Fact]
    public void AMetEnemy_ShowsItsNumbers_AndTheOthersAreQuestionMarks()
    {
        var enemies = EnemyCatalog.Defaults();
        string text = EnemyCodexText.Build(enemies, new HashSet<string> { EnemyCatalog.Ufo, EnemyCatalog.BanditShip }, L, item => "<" + item + ">");

        Assert.Contains("2 of 9 met.", text, StringComparison.Ordinal);
        Assert.Contains("<b>UFO</b>", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.health: 40 (ui.wiki.enemies.elite: 95)", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.damage: 4/s (ui.wiki.enemies.elite: 8/s)", text, StringComparison.Ordinal);
        Assert.Contains("3× <data_fragment>", text, StringComparison.Ordinal);
        Assert.Contains("<i>A strange craft.</i>", text, StringComparison.Ordinal);
        Assert.Contains("Shields up.", text, StringComparison.Ordinal);
        Assert.Contains("ui.wiki.enemies.health: 55–120", text, StringComparison.Ordinal); // a raider's hull scales
        Assert.DoesNotContain("enemy.bandit_ship.desc", text, StringComparison.Ordinal); // an untranslated line is left out
        Assert.Equal(7, text.Split("<b>???</b>").Length - 1);
        Assert.True(text.IndexOf("ui.wiki.enemies.where_space", StringComparison.Ordinal) < text.IndexOf("ui.wiki.enemies.where_planet", StringComparison.Ordinal));
    }
}
