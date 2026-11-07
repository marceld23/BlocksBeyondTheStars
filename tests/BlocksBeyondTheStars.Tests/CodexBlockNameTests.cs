// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3093: the Codex calls a block by the name the game gives it. The German Crystal Net articles called the watcher
/// "Wächter" — the name of the hostile robots — and the thumper, the hydroponic tray, the waterfall spout and a dozen
/// more devices by names no block in the game carries, so a child reading the Codex looked for blocks that do not exist.
/// Guarded for the two mandatory languages; a block renamed in <c>data/locales</c> needs its Codex mentions renamed in
/// the same change. (The community languages inflect their names, so a plain substring check cannot hold them.)
/// </summary>
public sealed class CodexBlockNameTests
{
    /// <summary>Every Crystal Net block the four Crystal Net articles describe.</summary>
    private static readonly string[] CrystalNetBlocks =
    {
        "crystal_conduit", "crystal_switch", "crystal_button", "step_plate", "proximity_sensor", "daylight_sensor",
        "storage_sensor", "watcher", "environment_sensor", "logic_block", "timer_block", "dice_block", "device_eye",
        "signal_display", "alarm_siren", "chime", "horn", "melody_block", "announcer", "signal_sender", "signal_receiver",
        "force_field", "energy_fence", "energy_gate", "campfire", "forge", "heal_tank", "flower_pot", "hydro_tray",
        "beam_block", "radio_beacon", "sentry_post", "thumper", "water_spout", "matter_sender", "matter_receiver",
        "drill_laser", "fabricator", "caller", "clone_tank", "phase_block", "trapdoor", "bridge_motor", "piston",
        "lift_motor", "lift_stop", "lift_gate", "ship_sensor", // #3298: the lift gate was named nowhere in the Codex
    };

    private static Dictionary<string, string> Table(params string[] path)
        => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(new[] { TestPaths.DataDir() }.Concat(path).ToArray())))
           ?? new Dictionary<string, string>();

    private static string CrystalArticles(string code)
        => string.Join(" ", Table("wiki", "locales", code + ".json")
            .Where(kv => kv.Key.StartsWith("wiki.crystal-", StringComparison.Ordinal))
            .Select(kv => kv.Value));

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheCrystalNetArticles_NameEveryDevice_ByItsBlockName(string code)
    {
        var names = Table("locales", code + ".json");
        string text = CrystalArticles(code);
        var missing = CrystalNetBlocks
            .Select(key => names.TryGetValue("block." + key + ".name", out var name) ? name : "block." + key + ".name (no name)")
            .Where(name => !text.Contains(name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(missing.Count == 0,
            $"The {code} Crystal Net articles never name these blocks by their block name: {string.Join(", ", missing)}");
    }

    /// <summary>The names the German articles used before #3093 — none of them is a block of the game ("Wächter" is the
    /// hostile robots' name).</summary>
    [Fact]
    public void TheGermanCrystalNetArticles_UseNoOldDeviceName()
    {
        string text = CrystalArticles("de");
        string[] old =
        {
            "Wächter", "Stampfer", "Hydrobeet", "Wasserspeier", "Zeitblock", "Geräteauge", "Glockenspiel", "Signalsender",
            "Signalempfänger", "Beamblock", "Materiesender", "Materieempfänger", "Klontank", "Auto-Bohrer",
        };

        Assert.Empty(old.Where(name => text.Contains(name, StringComparison.Ordinal)));
    }
}
