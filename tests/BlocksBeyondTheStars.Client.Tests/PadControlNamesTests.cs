// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.Json;
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Localization;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2507: the gamepad's flight and spacewalk lines (<c>ui.space.controls_pad</c>, <c>ui.space.eva_controls_pad</c>) were
/// fixed Xbox text — "RB fire · (X) land/dock" also for a PlayStation or Nintendo glyph set and after a rebind. They are
/// <c>{key:Action}</c> templates now, named by <see cref="PadControlNames"/> from the player's bindings, glyph set and
/// trigger switch with the one glyph table (<see cref="PadGlyphTable"/>, which <c>InputMap.PadGlyph</c> reads too).
/// </summary>
public sealed class PadControlNamesTests
{
    private const string FlightKey = "ui.space.controls_pad";
    private const string EvaKey = "ui.space.eva_controls_pad";

    /// <summary>The flight line shares the bottom row of the 1536-unit flight HUD with the appended chart and autopilot
    /// hints (about 25 characters); the 18 px line (<c>SpaceView</c>'s hint, horizontal overflow) holds about 190
    /// characters across it. So a pad line stays within this many characters in its longest form — the PlayStation
    /// names with the triggers switched on.</summary>
    private const int HudSlotChars = 160;

    private static readonly string[] Languages = { "en", "de", "es", "fr", "it", "ja", "ko", "nl", "pl", "pt", "ru", "tr", "uk", "zh" };

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static Dictionary<string, string> Table(string code)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")))
           ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static readonly Lazy<Dictionary<string, int>> Stock = new(StockPadButtons);

    /// <summary>The stock pad buttons of the rebindable actions (<c>GamepadInputSource.DefaultButtonFor</c>), read from the
    /// Unity source — the enum and the table live in the Unity assembly, which this project cannot reference.</summary>
    private static Dictionary<string, int> StockPadButtons()
    {
        string src = File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", "Input", "GamepadInputSource.cs"));
        var buttons = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(src, @"const KeyCode (?<name>Btn\w+) = KeyCode\.JoystickButton(?<n>\d+);", RegexOptions.ExplicitCapture, RegexTimeout))
        {
            buttons[m.Groups["name"].Value] = int.Parse(m.Groups["n"].Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        int start = src.IndexOf("public static KeyCode DefaultButtonFor(", StringComparison.Ordinal);
        Assert.True(start >= 0, "GamepadInputSource.cs no longer declares DefaultButtonFor");
        int end = src.IndexOf("};", start, StringComparison.Ordinal);
        var stock = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(src.Substring(start, end - start), @"InputAction\.(?<action>\w+) => (?<button>Btn\w+),", RegexOptions.ExplicitCapture, RegexTimeout))
        {
            stock[m.Groups["action"].Value] = buttons[m.Groups["button"].Value];
        }

        return stock;
    }

    /// <summary>The stock bindings, with <paramref name="rebinds"/> on top (a negative number = unbound).</summary>
    private static Func<string, int> Bindings(params (string Action, int Button)[] rebinds)
    {
        var map = new Dictionary<string, int>(Stock.Value, StringComparer.Ordinal);
        foreach (var (action, button) in rebinds)
        {
            map[action] = button;
        }

        return action => map.TryGetValue(action, out int b) ? b : -1;
    }

    private static string Line(string code, string key, PadGlyphSet set, bool triggers = false, Func<string, int>? bindings = null)
    {
        var table = Table(code);
        var en = Table("en");
        string Localize(string k) => table.TryGetValue(k, out var v) ? v : en.TryGetValue(k, out var e) ? e : k;
        var buttonOf = bindings ?? Bindings();
        return PadControlNames.ComposeLine(Localize(key), action => PadControlNames.Glyph(action, buttonOf, set, triggers, Localize));
    }

    [Fact]
    public void TheStockButtons_AreReadFromTheUnitySource()
    {
        Assert.Equal(2, Stock.Value["Interact"]);          // X
        Assert.Equal(3, Stock.Value["ToggleThirdPerson"]); // Y
        Assert.Equal(8, Stock.Value["ContextActions"]);    // left-stick click
        Assert.Equal(4, Stock.Value["FlightTargetNext"]);  // LB
    }

    [Fact]
    public void TheGlyphTable_NamesTheSameButtonByPosition_InEveryLayout()
    {
        Assert.Equal("(A)", PadGlyphTable.Button(0, PadGlyphSet.Xbox));
        Assert.Equal("(Cross)", PadGlyphTable.Button(0, PadGlyphSet.PlayStation));
        Assert.Equal("(B)", PadGlyphTable.Button(0, PadGlyphSet.Nintendo)); // Nintendo's bottom button is B
        Assert.Equal("(Triangle)", PadGlyphTable.Button(3, PadGlyphSet.PlayStation));
        Assert.Equal("R1", PadGlyphTable.Button(PadGlyphTable.RightBumper, PadGlyphSet.PlayStation));
        Assert.Equal("L", PadGlyphTable.Button(PadGlyphTable.LeftBumper, PadGlyphSet.Nintendo));
        Assert.Equal("LS", PadGlyphTable.Button(8, PadGlyphSet.Xbox));
        Assert.Equal("L3", PadGlyphTable.Button(8, PadGlyphSet.Nintendo));
        Assert.Equal("Menu", PadGlyphTable.Button(7, PadGlyphSet.Xbox));
        Assert.Equal("B12", PadGlyphTable.Button(12, PadGlyphSet.PlayStation));
        Assert.Null(PadGlyphTable.Button(-1, PadGlyphSet.Xbox));
        Assert.Null(PadGlyphTable.Button(PadGlyphTable.LastButton + 1, PadGlyphSet.Xbox));

        Assert.Equal("RT", PadGlyphTable.Trigger(right: true, PadGlyphSet.Xbox));
        Assert.Equal("L2", PadGlyphTable.Trigger(right: false, PadGlyphSet.PlayStation));
        Assert.Equal("ZR", PadGlyphTable.Trigger(right: true, PadGlyphSet.Nintendo));
    }

    [Fact]
    public void Xbox_ReadsTheStockButtons()
    {
        Assert.Equal(
            "Left stick fly · Right stick steer · RB fire · LB target · D-pad ◄ ► system · (X) land/dock · (Y) view · LS actions",
            Line("en", FlightKey, PadGlyphSet.Xbox));
        Assert.Equal(
            "EVA — Left stick float · Right stick look · (A)/(B) up·down · RB mine · LB place · (X) board · LS actions",
            Line("en", EvaKey, PadGlyphSet.Xbox));
        Assert.Equal(
            "EVA — Linker Stick schweben · Rechter Stick umsehen · (A)/(B) hoch·runter · RB abbauen · LB setzen · (X) einsteigen · LS Aktionen",
            Line("de", EvaKey, PadGlyphSet.Xbox));
    }

    [Fact]
    public void PlayStation_NamesItsOwnButtons()
    {
        Assert.Equal(
            "Left stick fly · Right stick steer · R1 fire · L1 target · D-pad ◄ ► system · (Square) land/dock · (Triangle) view · L3 actions",
            Line("en", FlightKey, PadGlyphSet.PlayStation));
        Assert.Equal(
            "EVA — Left stick float · Right stick look · (Cross)/(Circle) up·down · R1 mine · L1 place · (Square) board · L3 actions",
            Line("en", EvaKey, PadGlyphSet.PlayStation));
    }

    [Fact]
    public void Nintendo_NamesItsOwnButtons_ByPosition()
    {
        // The bottom face button is B on a Nintendo pad and the left one Y — the same physical buttons as Xbox A and X.
        Assert.Equal(
            "Left stick fly · Right stick steer · R fire · L target · D-pad ◄ ► system · (Y) land/dock · (X) view · L3 actions",
            Line("en", FlightKey, PadGlyphSet.Nintendo));
        Assert.Equal(
            "EVA — Left stick float · Right stick look · (B)/(A) up·down · R mine · L place · (Y) board · L3 actions",
            Line("en", EvaKey, PadGlyphSet.Nintendo));
    }

    [Fact]
    public void TheTriggers_JoinFireMineAndPlace_OnlyWhileSwitchedOn()
    {
        Assert.Contains("RB/RT fire", Line("en", FlightKey, PadGlyphSet.Xbox, triggers: true));
        Assert.Contains("RB/RT mine · LB/LT place", Line("en", EvaKey, PadGlyphSet.Xbox, triggers: true));
        Assert.Contains("R1/R2 mine · L1/L2 place", Line("en", EvaKey, PadGlyphSet.PlayStation, triggers: true));
        Assert.Contains("R/ZR fire", Line("en", FlightKey, PadGlyphSet.Nintendo, triggers: true));

        // The target lock stays on the bumper alone: the trigger switch adds mine/place, nothing else.
        Assert.Contains("LB target", Line("en", FlightKey, PadGlyphSet.Xbox, triggers: true));
        Assert.DoesNotContain("RT", Line("en", FlightKey, PadGlyphSet.Xbox, triggers: false));
    }

    [Fact]
    public void AReboundButton_IsNamed()
    {
        // The player swapped land/dock and view: X views, Y lands.
        var swapped = Bindings(("Interact", 3), ("ToggleThirdPerson", 2));
        string flight = Line("en", FlightKey, PadGlyphSet.Xbox, bindings: swapped);
        Assert.Contains("(Y) land/dock", flight);
        Assert.Contains("(X) view", flight);
        Assert.Contains("(Y) board", Line("en", EvaKey, PadGlyphSet.Xbox, bindings: swapped));

        // The same rebind in the PlayStation set, in German.
        Assert.Contains("(Triangle) landen/andocken", Line("de", FlightKey, PadGlyphSet.PlayStation, bindings: swapped));

        // The actions list moved to the right-stick click.
        Assert.EndsWith("· RS actions", Line("en", EvaKey, PadGlyphSet.Xbox, bindings: Bindings(("ContextActions", 9))), StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnboundAction_DropsItsSegment_NeverAPlaceholder()
    {
        string flight = Line("en", FlightKey, PadGlyphSet.Xbox, bindings: Bindings(("FlightTargetNext", -1)));
        Assert.Equal("Left stick fly · Right stick steer · RB fire · D-pad ◄ ► system · (X) land/dock · (Y) view · LS actions", flight);

        string eva = Line("en", EvaKey, PadGlyphSet.Xbox, bindings: Bindings(("ContextActions", -1), ("Interact", -1)));
        Assert.Equal("EVA — Left stick float · Right stick look · (A)/(B) up·down · RB mine · LB place", eva);

        foreach (string line in new[] { flight, eva })
        {
            Assert.DoesNotContain("?", line, StringComparison.Ordinal);
            Assert.DoesNotContain("{", line, StringComparison.Ordinal);
            Assert.DoesNotContain(" ·  · ", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Glyph_IsNull_ForAnUnboundOrUnknownAction()
    {
        static string Localize(string key) => key;
        Assert.Null(PadControlNames.Glyph("FlightAutopilot", _ => -1, PadGlyphSet.Xbox, triggers: false, Localize));
        Assert.Null(PadControlNames.Glyph("Dance", _ => -1, PadGlyphSet.Xbox, triggers: false, Localize));
        Assert.Equal("(X)", PadControlNames.Glyph("Interact", _ => 2, PadGlyphSet.Xbox, triggers: false, Localize));

        // The pseudo-actions are fixed controls — no binding is asked.
        Assert.Equal("ui.key.pad_left_stick", PadControlNames.Glyph("Move", _ => throw new InvalidOperationException(), PadGlyphSet.Xbox, false, Localize));
        Assert.Equal("(Cross)", PadControlNames.Glyph("Jump", _ => -1, PadGlyphSet.PlayStation, triggers: false, Localize));
        Assert.Equal("R1/R2", PadControlNames.Glyph("Scan", _ => -1, PadGlyphSet.PlayStation, triggers: true, Localize));
        Assert.Equal("ui.key.pad_dpad " + VegaText.TouchHotbarLabel, PadControlNames.Glyph("Hotbar", _ => -1, PadGlyphSet.Xbox, false, Localize));
    }

    [Fact]
    public void ComposeLine_SplitsOnlyOnTheSpacedDot_AndKeepsTokenFreeSegments()
    {
        static string? Glyph(string action) => action switch
        {
            "Jump" => "(A)",
            "Crouch" => "(B)",
            "Interact" => "(X)",
            _ => null,
        };

        Assert.Equal("(A)/(B) up·down · steady · (X) board", PadControlNames.ComposeLine("{key:Jump}/{key:Crouch} up·down · steady · {key:Interact} board", Glyph));
        Assert.Equal("(X) board", PadControlNames.ComposeLine("{key:FlightAutopilot} autopilot · {key:Interact} board", Glyph));
        Assert.Equal("(X) board", PadControlNames.ComposeLine("{key:Interact} board · {key:Nonsense} dance", Glyph));
        Assert.Equal("Board ((X) twice)", PadControlNames.ComposeLine("Board (({key:Interact}) twice)", Glyph)); // one pair per glyph
        Assert.Equal(string.Empty, PadControlNames.ComposeLine(null, Glyph));
        Assert.Equal(string.Empty, PadControlNames.ComposeLine("{key:Dance} dance", Glyph));
    }

    [Fact]
    public void EveryLanguage_NamesTheSameControlsAsEnglish_AndEachHasAPadControl()
    {
        var en = Table("en");
        var bad = new List<string>();
        foreach (string code in Languages)
        {
            var table = Table(code);
            foreach (string key in new[] { FlightKey, EvaKey })
            {
                if (!table.TryGetValue(key, out var text))
                {
                    continue; // a community table may lag — English fills the key
                }

                var expected = LineText.KeyTokenActions(en[key]).OrderBy(a => a, StringComparer.Ordinal).ToList();
                var actual = LineText.KeyTokenActions(text).OrderBy(a => a, StringComparer.Ordinal).ToList();
                if (!expected.SequenceEqual(actual))
                {
                    bad.Add($"{code}:{key} names [{string.Join(", ", actual)}], English [{string.Join(", ", expected)}]");
                }

                int segments = text.Split(new[] { PadControlNames.Separator }, StringSplitOptions.None).Length;
                string line = Line(code, key, PadGlyphSet.Xbox);
                if (line.Split(new[] { PadControlNames.Separator }, StringSplitOptions.None).Length != segments || line.Contains('{', StringComparison.Ordinal))
                {
                    bad.Add($"{code}:{key} loses a control with the stock buttons: \"{line}\"");
                }
            }
        }

        Assert.True(bad.Count == 0, "Pad controls lines that drift from English:\n" + string.Join("\n", bad));
    }

    [Fact]
    public void EveryLanguage_FitsTheHudSlot_InItsLongestForm()
    {
        var over = new List<string>();
        foreach (string code in Languages)
        {
            foreach (string key in new[] { FlightKey, EvaKey })
            {
                string line = Line(code, key, PadGlyphSet.PlayStation, triggers: true);
                if (line.Length > HudSlotChars)
                {
                    over.Add($"{code}:{key} ({line.Length}): {line}");
                }
            }
        }

        Assert.True(over.Count == 0, $"Pad controls lines longer than the flight HUD's bottom row ({HudSlotChars} characters) — shorten the words:\n"
                                     + string.Join("\n", over));
    }
}
