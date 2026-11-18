// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3765 on the client: the new-world screen's fourth mode (Hard Survival) and what it launches a world with, the mode
/// in the world list, and the fallen player's banner. The death rule itself is the server suite's (<c>FallenTests</c>);
/// here the banner's rule (<see cref="FallenBanner"/>) and the Unity wiring as source text (the scripts cannot load
/// headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class HardSurvivalWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void TheBanner_WaitsForTheTeam_CountsDownAlone_ThenOffersTheShip()
    {
        // A team-mate still stands: no countdown, however long it takes.
        Assert.Equal(FallenRules.WaitForTeam, FallenBanner.SecondsLeft(FallenRules.WaitForTeam, 500f));
        Assert.Equal("ui.fallen.wait", FallenBanner.LineKey(FallenRules.WaitForTeam));
        Assert.False(FallenBanner.MayRestart(FallenRules.WaitForTeam));

        // Alone: the number the server sent runs down on the client, to zero and not below.
        Assert.Equal(48f, FallenBanner.SecondsLeft(60f, 12f), 3);
        Assert.Equal("ui.fallen.countdown", FallenBanner.LineKey(48f));
        Assert.False(FallenBanner.MayRestart(0.2f));
        Assert.Equal(0f, FallenBanner.SecondsLeft(60f, 75f));
        Assert.Equal("ui.fallen.restart", FallenBanner.LineKey(0f));
        Assert.True(FallenBanner.MayRestart(0f));

        Assert.Equal(60f, FallenBanner.SecondsLeft(60f, -3f)); // a clock that jumped back never adds time
    }

    [Fact]
    public void TheWorldList_NamesAHardSurvivalWorld()
    {
        Assert.Equal("ui.save.mode_hard", SavedWorld.ModeKeyOf(SavedWorld.ModeHard));
        Assert.Equal("ui.save.mode_hard", SavedWorld.ModeKeyOf("HARD"));
        Assert.Equal("ui.save.mode_sandbox", SavedWorld.ModeKeyOf(SavedWorld.ModeSandbox)); // the others unchanged
        Assert.Equal(string.Empty, SavedWorld.ModeKeyOf("something"));
    }

    [Fact]
    public void TheFourthCard_LaunchesTheWorldWithTheHardRules()
    {
        string create = Script("UiSaveSelect.Create.cs");
        Assert.Contains("private readonly Button[] _modeCards = new Button[4];", create);
        Assert.Contains("\"ui.save.mode_hard\"", create);
        Assert.Contains("_mode == 3 ? WorldCreationOptions.HardSurvival(_worldOptions) : _worldOptions", create);
        Assert.Contains("Launch(world, UnlockAll(), AllShips(), Kit(), _mode == 2, LaunchOptions(), Flight());", create);
        Assert.Contains("private bool Flight() => _mode == 1 || _mode == 2;", create); // no free flight in Hard Survival
        Assert.Contains("ModeCardW = 229f", create);                                      // four cards in the row of three: 4 × 229 + 3 × 10 = 946
        Assert.Equal(946f, (4 * 229f) + (3 * 10f));

        string options = Script("WorldCreationOptions.cs");
        Assert.Contains("o.PlanetEnemies = 4; // Extreme", options);
        Assert.Contains("o.Bandits = 4;       // Extreme", options);
        Assert.Contains("o.Hazards = 3;       // Hard", options);
        Assert.Contains("o.Oxygen = 1;        // Slow", options);
        Assert.Contains("o.Hunger = 1;        // Slow", options);
        Assert.Contains("if (ReviveOnDeath) Arg(\"death-penalty\", \"Revive\");", options);
        Assert.Contains("ReviveOnDeath = other.ReviveOnDeath;", options); // a copy keeps the rule
    }

    [Fact]
    public void AFallenPlayer_GetsTheBanner_NotTheDeathModal()
    {
        Assert.Contains("if (m.Fallen)", Script("RespawnPrompt.cs"));
        Assert.Contains("if (m.FallenAgain)", Script("DeathFx.cs")); // an update is no second death
        Assert.Contains("root.AddComponent<FallenView>();", Script("WorldRig.cs"));

        string view = Script("FallenView.cs");
        Assert.Contains("FallenBanner.SecondsLeft(_restartIn, Time.unscaledTime - _toldAt)", view);
        Assert.Contains("FallenBanner.MayRestart(left) && !Game.MenuOpen && !Game.ChatTyping && InputMap.Down(InputAction.Interact)", view);
        Assert.Contains("Game.Network?.SendRespawnChoice(false);", view);
        Assert.Contains("_sawSpectating && !Game.Spectating", view); // standing again ends it
        Assert.DoesNotContain("AwaitingRespawnConfirm", view);       // the watcher keeps flying: no gate, no cursor

        // The line is built by FallenBanner.Line — never by string.Format on the localized text (see the test below) —
        // and no stray RespawnNotice (a rescue teleport) may wipe the banner: only the state ends it.
        Assert.Contains("FallenBanner.Line(Tr(FallenBanner.LineKey(left)), left,", view);
        Assert.DoesNotContain("string.Format(", view);
        Assert.DoesNotContain("RespawnNoticeReceived", view);
    }

    [Fact]
    public void TheBannersLine_ExpandsItsControl_AndCountsDown_WithoutFormatting()
    {
        // The restart line is "{key:Interact}: start again at your ship". string.Format on it threw a FormatException
        // every frame once the countdown reached zero — before the press was read — so a lone player could never
        // restart. The real texts of both languages go through the line builder here.
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            static string Expand(string text) => text.Replace("{key:Interact}", "[E]");

            string restart = FallenBanner.Line(table[FallenBanner.LineKey(0f)], 0f, Expand);
            Assert.StartsWith("[E]", restart);
            Assert.DoesNotContain("{", restart);

            string countdown = FallenBanner.Line(table[FallenBanner.LineKey(12.3f)], 12.3f, Expand);
            Assert.Contains("13", countdown); // whole seconds, rounded up
            Assert.DoesNotContain("{0}", countdown);

            string wait = FallenBanner.Line(table[FallenBanner.LineKey(FallenRules.WaitForTeam)], FallenRules.WaitForTeam, Expand);
            Assert.Equal(table["ui.fallen.wait"], wait);
        }

        Assert.Equal(string.Empty, FallenBanner.Line(null, 5f, null));              // no text yet: nothing, not a crash
        Assert.Equal("in 5 s", FallenBanner.Line("in {0} s", 4.2f, null));          // no expander: the number still goes in
        string restartText = "{key:Interact}: go";
        Assert.Throws<FormatException>(() => string.Format(System.Globalization.CultureInfo.InvariantCulture, restartText, 0)); // why the builder exists
    }

    [Fact]
    public void TheTexts_ExistInEnglishAndGerman()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            foreach (string key in new[]
                     {
                         "ui.save.mode_hard", "ui.save.mode_hard_line", "ui.save.hard_hint",
                         "ui.fallen.title", "ui.fallen.wait", "ui.fallen.countdown", "ui.fallen.restart",
                         "srv.fallen.announce", "srv.fallen.revived", "srv.fallen.you_revived", "srv.fallen.restarted",
                     })
            {
                Assert.True(table.TryGetValue(key, out string? text) && !string.IsNullOrWhiteSpace(text), code + ": " + key);
            }

            Assert.Contains("{0}", table["ui.fallen.countdown"]);
            Assert.Contains("{key:Interact}", table["ui.fallen.restart"]);
            Assert.Contains("{player}", table["srv.fallen.announce"]);
            Assert.Contains("|hard", table["srv.worldmode.usage"]);
        }
    }
}
