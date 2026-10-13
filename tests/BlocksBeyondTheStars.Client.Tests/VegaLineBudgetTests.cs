// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// VEGA's texts are written for her speech panel and for every input device (#2463, B26/B28). Guards over the English
/// and German tables — the twelve community languages may lag a pass behind and simply page:
/// <list type="bullet">
/// <item>a VEGA line fits one panel page (it used to run two or three pages, every page one more key press);</item>
/// <item>a <c>{key:Action}</c> token names a real <c>InputAction</c> (an unknown name renders as the bare word);</item>
/// <item>the tutorial lines, the advisor tips, the converted HUD prompts and the item/block/blueprint/module descriptions
/// (#2503) never name a key literally — "press Tab" is wrong on a pad and on a tablet, the token is right on all three.</item>
/// </list>
/// </summary>
public sealed class VegaLineBudgetTests
{
    /// <summary>One page of VEGA's speech panel: the 612×116 HUD-unit text box at font 22 holds about four lines,
    /// ≈ 220–240 characters (<c>VegaPanel.SpeechTextW/H</c>). English lines stay within it.</summary>
    public const int EnglishBudget = 220;

    /// <summary>German runs 12–20 % longer than English; its budget is the English one plus 20 %.</summary>
    public const int GermanBudget = 264;

    /// <summary>What a <c>{key:Action}</c> token typically shows ("Tab", "(A)", "USE") — counted at this width.</summary>
    private const int TokenWidth = 3;

    /// <summary>What a <c>{0}</c> slot typically shows (a world, item or wreck name) — counted at this width.</summary>
    private const int SlotWidth = 10;

    /// <summary>Lines that are not read in the panel: the epilogue is shown on the ending card (ResolutionCinematic
    /// withdraws it from VEGA's queue, #2461).</summary>
    private static readonly HashSet<string> NotPanelText = new(StringComparer.Ordinal) { "story.vega.beat13" };

    /// <summary>Prompts that expand <c>{key:Action}</c> tokens since #2463: the HUD (HudUi, FactoryView) and the ship
    /// stations' server messages (GameBootstrap.ResolveServerToken). #2502 (stage 1) adds the server messages whose path
    /// already expanded, the HUD/world/button texts (each wrapped in <c>HudUi.ExpandKeyTokens</c> where it is drawn), the
    /// flight and EVA lines, and the two texts rewritten without a key (the streamer's greeting, which the server composes
    /// itself, and the chat field's title).</summary>
    private static readonly HashSet<string> ConvertedPrompts = new(StringComparer.Ordinal)
    {
        "ui.hud.use", "ui.beam.use_hint", "ui.factory.claim_prompt",
        "srv.station.cargo", "srv.station.cockpit", "srv.station.console", "srv.station.workshop",
        // #2502 — server messages (@srv tokens, GameBootstrap.ResolveServerToken)
        "srv.base.founded", "srv.rail.placed", "srv.ship.commissioned", "srv.ship.core_placed", "srv.station.zero_g",
        // #2502 — HUD, world labels and buttons
        "ui.wreck.repair_hint", "ui.base.rename_hint", "ui.hud.loot", "ui.hud.feed", "ui.hud.stash_keys", "ui.hud.stash_filter_key",
        "ui.scan.use_hint", "ui.hud.hint", "ui.hud.hint_fly", "ui.station.leave_hint", "ui.dock.undock_hint",
        "ui.interact.trade_dock", "ui.datacube.prompt", "ui.netfragment.prompt", "ui.chat.hidden", "ui.container_filter.hint",
        "ui.crate.stash_all", "ui.crate.take_all", "ui.arcade.empty_body", "ui.map.wreck_detail", "ui.map.debris_detail",
        "ui.spacemap.autopilot_ready", "ui.launch.yes", "ui.vendor.trade_choice", "ui.bio.workshop_choice_craft",
        "ui.save.creative_flight", "ui.photos.empty", "ui.wiki.discoveries.empty",
        // #2502 — the flight and EVA lines (SpaceView)
        "ui.space.controls", "ui.space.eva_controls", "ui.vega.autopilot.hint", "ui.spacemap.key_hint",
        // #2507 — the pad's flight and EVA lines (HudUi.PadLine): no "(X)", "RB", "LS" or "Left stick" written in
        "ui.space.controls_pad", "ui.space.eva_controls_pad",
        // #2502 — rewritten without a key
        "npc.streamer.ask", "ui.chat.hint",
    };

    /// <summary>#2502: texts the SERVER localizes itself (<c>Localize(s.Locale, …)</c>) and sends as plain text — a
    /// <c>{key:Action}</c> token there would reach every client as raw braces, so they never carry one.</summary>
    private static readonly string[] ServerComposedTexts = { "npc.streamer.ask" };

    private const string StationBlockPrompts = "ui.station.block.";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex KeyToken = new(
        @"\{key:(?<action>[A-Za-z0-9_]+)\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static readonly Regex Slot = new(@"\{[0-9]\}", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>A key named literally: a lone key letter ("E at the cockpit", "M opens the map" — not part of a word, a
    /// token or a number), or the Tab key ("(Tab)", "press Tab", "drücke Tab", "Tab menu", "Tab-Menü"). German also says
    /// "im Tab „Karte“" for a menu tab — that noun is not a key and passes.</summary>
    private static readonly Regex LiteralKey = new(
        @"(?<![\p{L}\p{N}_{:])(?:E|M|R|Q)(?![\p{L}\p{N}_}])|\(Tab\)|(?i:press|drück\w*)\s+Tab\b|\bTab(?:-Menü|\s+menu)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>#2502: the wider net over the same texts — any key letter in a prompt shape ("G: loot", "Launch (E)",
    /// "[P] Autopilot"), the mouse buttons by name ("right mouse button", "Rechtsklick", LMB/RMB, LMT/RMT), WASD, the space
    /// bar ("press Space", "Space×2", "Leertaste") and Ctrl/Strg. <c>vega.hint.rope_gun</c> ("Hold the right mouse button
    /// (pad LB, touch PLACE)") slipped through the narrow pattern; "Space begins right above the clouds" is not a key.</summary>
    private static readonly Regex LiteralKeyWide = new(
        @"^[A-Z]:\s|\([A-Z]\)|\[[A-Z]\]|(?i:\b(?:right|left)[ -](?:mouse|click))|\b(?:LMB|RMB|LMT|RMT)\b|\bWASD\b"
        + @"|(?i:(?:press|hold|tap)\s+)Space\b|\bSpace(?:×|/|\s+(?:jump|up)\b)|\bLeertaste\b|(?i:\b(?:rechts|links)klick)"
        + @"|(?i:\b(?:rechte|linke)n? Maustaste)|\bStrg\b|\bCtrl\b",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>#2507: a pad control written in — the shoulder buttons, triggers and stick clicks of the three glyph sets
    /// ("RB fire", "L1", "ZR", "LS actions") and the sticks and the d-pad by name ("Left stick fly", "Steuerkreuz"). The
    /// pad's flight and EVA lines read like that before they became templates; a token names the player's own button.</summary>
    private static readonly Regex LiteralPadControl = new(
        @"\b(?:RB|LB|RT|LT|RS|LS|R1|L1|R2|L2|R3|L3|ZR|ZL)\b|(?i:\b(?:left|right) stick\b|\b(?:linker|rechter) Stick\b|\bD-pad\b|\bSteuerkreuz\b)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>The description keys (#2503): item, block, blueprint and ship-module texts, drawn by the crafting/inventory/
    /// tech/ship detail panes (<c>CraftingTechShipUI.Desc</c>) and the Codex (<c>WikiUI.Desc</c>), both of which expand
    /// <c>{key:Action}</c> tokens.</summary>
    private static readonly Regex DescriptionKey = new(
        @"^(?:item|block|blueprint|module)\.[a-z0-9_.]+\.desc$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    /// <summary>#2503: what the two patterns above miss in a description — any lone key letter ("G to take, H to store",
    /// "(press B)", "Taste B"; "A" and the roman "I" of "Oxygen Tank I" are words, "N-ten" is no key), a key named by its
    /// job ("the stow key", "die Wärmebild-Taste") and the action's name used as a key ("press Interact", "Benutzen
    /// drücken"). "Hold the fire button" stays legal: the ship's fire control has no token yet.</summary>
    private static readonly Regex LiteralKeyInDescription = new(
        @"(?<![\p{L}\p{N}_{:\-])[B-HJ-Z](?![\p{L}\p{N}_}\-])"
        + @"|(?i:\b(?:stow|thermal|einpack|wärmebild)[- ]?(?:key|taste)\b)"
        + @"|(?i:\bpress(?:es|ing)?\s+interact\b)|(?i:\bbenutzen\s+drücken\b|\bdrücke\s+benutzen\b)",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);

    private static Dictionary<string, string> Table(params string[] relative)
    {
        string path = Path.Combine(new[] { ClientTestPaths.DataDir() }.Concat(relative).ToArray());
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
               ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    private static Dictionary<string, string> Core(string code) => Table("locales", code + ".json");

    private static Dictionary<string, string> Pack(string code) => Table("stories", "vega_protocol", "locales", code + ".json");

    /// <summary>#2469: the basic training's texts (the tutorial pack, VEGA presents them).</summary>
    private static Dictionary<string, string> TutorialPack(string code) => Table("stories", "tutorial", "locales", code + ".json");

    /// <summary>Every line VEGA speaks in the panel: the core <c>vega.*</c> keys, the VEGA pack's <c>story.vega.*</c> lines and
    /// the tutorial pack's <c>story.tutorial.*</c> lines (#2469; the pack validator holds them to the tighter basic-training
    /// budget on top).</summary>
    private static IEnumerable<KeyValuePair<string, string>> VegaLines(string code)
        => Core(code).Where(e => e.Key.StartsWith("vega.", StringComparison.Ordinal))
            .Concat(Pack(code).Where(e => e.Key.StartsWith("story.vega.", StringComparison.Ordinal)))
            .Concat(TutorialPack(code).Where(e => e.Key.StartsWith("story.tutorial.", StringComparison.Ordinal)))
            .Where(e => !NotPanelText.Contains(e.Key));

    /// <summary>The characters a line shows: a token counted as <see cref="TokenWidth"/>, a slot as <see cref="SlotWidth"/>.</summary>
    internal static int ShownLength(string text)
    {
        string shown = KeyToken.Replace(text, new string('K', TokenWidth));
        shown = Slot.Replace(shown, new string('N', SlotWidth));
        return shown.Length;
    }

    [Fact]
    public void ShownLength_CountsATokenAsAGlyph_AndASlotAsAName()
    {
        Assert.Equal(5, ShownLength("Hello"));
        Assert.Equal("Press ".Length + TokenWidth, ShownLength("Press {key:Interact}"));
        Assert.Equal(SlotWidth + " lies here".Length, ShownLength("{0} lies here"));
    }

    [Theory]
    [InlineData("en", EnglishBudget)]
    [InlineData("de", GermanBudget)]
    public void EveryVegaLine_FitsOnePanelPage(string code, int budget)
    {
        var over = VegaLines(code)
            .Select(e => (e.Key, Length: ShownLength(e.Value)))
            .Where(e => e.Length > budget)
            .OrderByDescending(e => e.Length)
            .Select(e => $"{e.Key} ({e.Length})")
            .ToList();

        Assert.True(
            over.Count == 0,
            $"{code}: {over.Count} VEGA line(s) are longer than one speech-panel page ({budget} shown characters) — "
            + "shorten them (one sentence and one action is the goal for kids): " + string.Join(", ", over));
    }

    /// <summary>The <c>InputAction</c> names, read from the Unity source — the enum lives in the Unity assembly, which this
    /// project cannot reference.</summary>
    private static HashSet<string> InputActionNames()
    {
        string src = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "InputMap.cs"));
        int start = src.IndexOf("public enum InputAction", StringComparison.Ordinal);
        Assert.True(start >= 0, "InputMap.cs no longer declares 'public enum InputAction'");
        int open = src.IndexOf('{', start);
        int close = src.IndexOf('}', open);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string line in src.Substring(open + 1, close - open - 1).Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*(?<name>[A-Z][A-Za-z0-9]*)\s*,", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, RegexTimeout);
            if (m.Success)
            {
                names.Add(m.Groups["name"].Value);
            }
        }

        Assert.Contains("Interact", names);
        Assert.Contains("UiMenu", names);
        return names;
    }

    [Fact]
    public void EveryKeyToken_InEnglishAndGerman_NamesARealInputAction()
    {
        // #2469: or one of the pseudo-actions (move, look, jump, crouch, mine, place, scan, the quick bar) that
        // HudUi.KeyTokenGlyph resolves per device.
        var actions = InputActionNames();
        var bad = new List<string>();
        foreach (string code in new[] { "en", "de" })
        {
            foreach (var table in new[] { Core(code), Pack(code), TutorialPack(code) })
            {
                foreach (var (key, text) in table)
                {
                    foreach (Match m in KeyToken.Matches(text))
                    {
                        string action = m.Groups["action"].Value;
                        if (!actions.Contains(action) && !VegaText.IsPseudoAction(action))
                        {
                            bad.Add($"{code}:{key} {{key:{action}}}");
                        }
                    }
                }
            }
        }

        Assert.True(bad.Count == 0, "Tokens that name no InputAction render as the bare word: " + string.Join(", ", bad));
    }

    [Fact]
    public void ThePseudoActions_AreNoInputAction()
    {
        // A pseudo-action that became a real InputAction would be resolved twice — once is the rule.
        var actions = InputActionNames();
        Assert.All(VegaText.PseudoActions, pseudo => Assert.DoesNotContain(pseudo, actions));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TutorialLines_TipsAndConvertedPrompts_NameNoKeyLiterally(string code)
    {
        var bad = Core(code)
            .Where(e => e.Key.StartsWith("vega.", StringComparison.Ordinal)
                        || e.Key.StartsWith(StationBlockPrompts, StringComparison.Ordinal)
                        || ConvertedPrompts.Contains(e.Key))
            .Concat(TutorialPack(code)) // #2469: every line of the basic training
            .Concat(Pack(code).Where(e => e.Key.StartsWith("story.vega.", StringComparison.Ordinal))) // #2502: VEGA's story lines too
            .Where(e => LiteralKey.IsMatch(e.Value) || LiteralKeyWide.IsMatch(e.Value))
            .Select(e => $"{e.Key}: \"{e.Value}\"")
            .ToList();

        Assert.True(
            bad.Count == 0,
            $"{code}: texts that name a key literally — use a {{key:Action}} token (the game shows the player's own key, "
            + "pad button or touch button): " + string.Join(" | ", bad));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void ConvertedPrompts_NameNoPadControlLiterally(string code)
    {
        // #2507: "RB fire · LB target · (X) land/dock" was right only for an Xbox pad with the stock buttons.
        var bad = Core(code)
            .Where(e => ConvertedPrompts.Contains(e.Key) && LiteralPadControl.IsMatch(e.Value))
            .Select(e => $"{e.Key}: \"{e.Value}\"")
            .ToList();

        Assert.True(
            bad.Count == 0,
            $"{code}: texts that name a pad control literally — use a {{key:Action}} token ({{key:Mine}}, {{key:Move}}, "
            + "{key:Hotbar}, {key:ContextActions} …; the game names the player's own button in their glyph set): " + string.Join(" | ", bad));
    }

    [Fact]
    public void LiteralPadControlPattern_CatchesButtonNames_ButNotWordsOrTokens()
    {
        foreach (string literal in new[]
                 {
                     "RB fire · LB target", "LS actions", "R1/R2 mine", "ZL place", "Left stick fly", "Linker Stick fliegen",
                     "D-pad system", "Steuerkreuz System",
                 })
        {
            Assert.Matches(LiteralPadControl, literal);
        }

        foreach (string fine in new[]
                 {
                     "{key:Mine} fire · {key:FlightTargetNext} target", "{key:ContextActions} actions", "Stick to the plan",
                     "Rbx", "LSD", "Rtm", "a stick of dynamite", "Holzstick",
                 })
        {
            Assert.DoesNotMatch(LiteralPadControl, fine);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void ItemBlockBlueprintAndModuleDescriptions_NameNoKeyLiterally(string code)
    {
        // #2503: "Press E on it", "Right-click to magnify", "G to take, H to store" — the descriptions now say
        // {key:Interact}, {key:Place}, {key:LootContainer} and read as the player's own key, pad button or touch button.
        var descriptions = Core(code).Where(e => DescriptionKey.IsMatch(e.Key)).ToList();
        Assert.True(descriptions.Count > 100, $"{code}: only {descriptions.Count} description keys found — did the key scheme change?");

        var bad = descriptions
            .Where(e => LiteralKey.IsMatch(e.Value) || LiteralKeyWide.IsMatch(e.Value) || LiteralKeyInDescription.IsMatch(e.Value))
            .Select(e => $"{e.Key}: \"{e.Value}\"")
            .ToList();

        Assert.True(
            bad.Count == 0,
            $"{code}: descriptions that name a key literally — use a {{key:Action}} token (\"press {{key:Interact}}\", "
            + "\"Use {key:Place} to …\"; CraftingTechShipUI.Desc and WikiUI.Desc expand it): " + string.Join(" | ", bad));
    }

    [Fact]
    public void DescriptionKeyPattern_CatchesTheOldTexts_ButNotNumeralsOrTokens()
    {
        foreach (string literal in new[]
                 {
                     "Place it to stash items at a base: G to take, H to store.", "Deploy it on a spacewalk (press B)",
                     "Beim Weltraumspaziergang einsetzen (Taste B)", "Press the stow key to pack it back",
                     "drücke beim Durchsehen die Wärmebild-Taste", "mit der Einpack-Taste daneben", "press Interact and its network",
                     "ansehen und Benutzen drücken",
                 })
        {
            Assert.Matches(LiteralKeyInDescription, literal);
        }

        // The old texts the narrow and the wide pattern already caught.
        Assert.Matches(LiteralKey, "Press E on it to make it your home spawn");
        Assert.Matches(LiteralKeyWide, "Right-click to magnify");
        Assert.Matches(LiteralKeyWide, "Linksklick schießt; halte die rechte Maustaste");
        Assert.Matches(LiteralKeyWide, "just steer with WASD");

        foreach (string fine in new[]
                 {
                     "upgraded from Oxygen Tank I", "+32 cargo slots on top of Expansion I", "löst bei jedem N-ten Impuls aus",
                     "fires every Nth pulse", "A simple cot of logs", "press {key:Interact} on it", "Use {key:Place} to magnify",
                     "{key:LootContainer} to take, {key:DepositToCrate} to store", "hold the fire button on a target",
                     "A swinging door that opens when you interact with it.", "its network is ON or OFF", "sein Netz ist AN oder AUS",
                 })
        {
            Assert.DoesNotMatch(LiteralKeyInDescription, fine);
            Assert.DoesNotMatch(LiteralKey, fine);
            Assert.DoesNotMatch(LiteralKeyWide, fine);
        }
    }

    [Fact]
    public void LiteralKeyPattern_CatchesKeyNames_ButNotWordsOrTokens()
    {
        Assert.Matches(LiteralKey, "open the menu (Tab), Crafting tab");
        Assert.Matches(LiteralKey, "To leave: E at the cockpit.");
        Assert.Matches(LiteralKey, "M opens the map.");
        Assert.Matches(LiteralKey, "Tab-Menü, Herstellen");
        Assert.Matches(LiteralKey, "Tab menu, Research.");
        Assert.Matches(LiteralKey, "drücke Tab: Inventar");
        Assert.DoesNotMatch(LiteralKey, "press {key:UiMenu} at the {key:Interact}");
        Assert.DoesNotMatch(LiteralKey, "Table, Mk3, Escape, Mars, Riding, Quiet, slot 1, {0}");
        Assert.DoesNotMatch(LiteralKey, "Diese Karte findest du später im Tab „Karte“ wieder.");
    }

    [Fact]
    public void WideLiteralKeyPattern_CatchesPromptShapesAndMouseWords_ButNotWordsOrTokens()
    {
        foreach (string literal in new[]
                 {
                     "G: loot", "Launch (E)", "[P] Autopilot", "Hold the right mouse button", "left-click it", "LMB: scan",
                     "LMT abbauen", "WASD move", "press Space", "Space×2 fly", "Space/Ctrl up·down", "Leertaste×2 fliegen",
                     "mit Rechtsklick ein Foto", "Halte die rechte Maustaste", "Strg/C ducken", "Ctrl/C crouch",
                 })
        {
            Assert.Matches(LiteralKeyWide, literal);
        }

        foreach (string fine in new[]
                 {
                     "{key:LootContainer}: loot", "Launch ({key:Interact})", "{key:FlightAutopilot} autopilot",
                     "Space begins right above the clouds", "a space station", "Spacewalk", "EVA spacewalk",
                     "VEGA: hello", "Mk2 (Tier 2)", "click the button", "Gold: 3",
                 })
        {
            Assert.DoesNotMatch(LiteralKeyWide, fine);
        }
    }

    [Fact]
    public void ServerComposedTexts_NameNoKeyToken_InAnyLanguage()
    {
        // #2502: the streamer's greeting used to say "Talk to me (E)!" — a key in a text the server sends as plain text.
        foreach (string code in new[] { "en", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt", "ru", "tr", "uk", "zh" })
        {
            var table = Core(code);
            foreach (string key in ServerComposedTexts)
            {
                if (table.TryGetValue(key, out var text))
                {
                    Assert.DoesNotMatch(KeyToken, text);
                    Assert.DoesNotMatch(LiteralKeyWide, text);
                }
            }
        }
    }

    [Fact]
    public void TouchControlLabel_NamesTheLayoutsOwnButtons()
    {
        var en = Core("en");
        string Localize(string key) => en.TryGetValue(key, out var v) ? v : key;

        Assert.Equal("USE", VegaText.TouchControlLabel("Interact", Localize));
        Assert.Equal("MAP", VegaText.TouchControlLabel("PlanetMap", Localize));
        Assert.Equal("MAP", VegaText.TouchControlLabel("FlightMap", Localize));
        Assert.Equal("FEED", VegaText.TouchControlLabel("FeedCreature", Localize));
        Assert.Equal(VegaText.TouchMenuLabel, VegaText.TouchControlLabel("UiMenu", Localize));

        // No button on the tablet layout: the caller names the on-screen action menu instead.
        Assert.Null(VegaText.TouchControlLabel("ToggleLamp", Localize));
        Assert.Null(VegaText.TouchControlLabel("Nonsense", Localize));
    }

    [Fact]
    public void TouchControlLabel_OnlyNamesRealActions_WhoseLabelsExistInEnglishAndGerman()
    {
        var actions = InputActionNames();
        var en = Core("en");
        var de = Core("de");
        foreach (string action in actions.Concat(VegaText.PseudoActions)) // #2469: the pseudo-actions too
        {
            var asked = new List<string>();
            string? label = VegaText.TouchControlLabel(action, key =>
            {
                asked.Add(key);
                return key;
            });

            foreach (string key in asked)
            {
                Assert.True(en.ContainsKey(key) && de.ContainsKey(key), $"{action} → '{key}' is missing in en or de");
            }

            if (label != null && asked.Count == 0)
            {
                // the symbol labels without a locale key: the menu button and the quick-bar arrows
                Assert.Contains(label, new[] { VegaText.TouchMenuLabel, VegaText.TouchHotbarLabel });
            }
        }
    }
}
