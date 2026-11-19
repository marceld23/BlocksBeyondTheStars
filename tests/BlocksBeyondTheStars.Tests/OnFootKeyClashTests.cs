// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The keyboard reads two kinds of keys on foot: the rebindable actions with a stock key (<c>InputMap.DefaultKey</c>) and
/// the hard-wired controls that are no action at all — walking, jumping, crouching, mining and placing, the hotbar
/// numbers. Nothing compared the two lists, so the "look here!" ping was given C, one of the two crouch keys: every sneak
/// on C dropped a ping for the allies and showed "Ping!". No stock key of the on-foot group may be a hard-wired key. The
/// Unity client cannot be compiled here, so both lists are read from the source.
/// </summary>
public sealed class OnFootKeyClashTests
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The hard-wired keys that are no <c>KeyCode</c> literal in <c>DesktopInputSource</c>: the movement axes of the
    /// project's input manager (WASD and the arrow keys), the two mouse buttons it reads by number and the hotbar numbers
    /// it reads in a loop from <c>Alpha1</c>.</summary>
    private static readonly string[] FixedBeyondTheSource =
    {
        "W", "A", "S", "D", "UpArrow", "DownArrow", "LeftArrow", "RightArrow", "Mouse0", "Mouse1",
        "Alpha1", "Alpha2", "Alpha3", "Alpha4", "Alpha5", "Alpha6", "Alpha7", "Alpha8", "Alpha9",
    };

    private static string ScriptText(params string[] path)
    {
        var parts = new List<string> { TestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts" };
        parts.AddRange(path);
        string file = Path.Combine(parts.ToArray());
        Assert.True(File.Exists(file), $"{file} not found — did the client's input scripts move?");
        return File.ReadAllText(file);
    }

    /// <summary>The text of one declaration in <c>InputMap.cs</c>: from <paramref name="opening"/> to its closing <c>};</c>.</summary>
    private static string Block(string source, string opening)
    {
        int start = source.IndexOf(opening, StringComparison.Ordinal);
        Assert.True(start >= 0, $"InputMap.cs no longer declares '{opening}' — point this guard at the new declaration.");
        int end = source.IndexOf("};", start, StringComparison.Ordinal);
        Assert.True(end > start, $"'{opening}' has no closing '}};'");
        return source.Substring(start, end - start);
    }

    private static HashSet<string> HardWiredKeys()
    {
        var keys = new HashSet<string>(FixedBeyondTheSource, StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(ScriptText("Input", "DesktopInputSource.cs"), @"KeyCode\.(?<key>\w+)", RegexOptions.ExplicitCapture, RegexTimeout))
        {
            keys.Add(m.Groups["key"].Value);
        }

        keys.Remove("None"); // "no key" — the guard of the action polls, not a key
        return keys;
    }

    [Fact]
    public void NoStockKeyOfAnOnFootAction_IsAHardWiredKey()
    {
        string inputMap = ScriptText("InputMap.cs");
        var onFoot = Regex.Matches(Block(inputMap, "public static readonly InputAction[] Remappable ="),
                @"InputAction\.(?<action>\w+)", RegexOptions.ExplicitCapture, RegexTimeout)
            .Select(m => m.Groups["action"].Value).ToList();
        var stockKeys = Regex.Matches(Block(inputMap, "public static KeyCode DefaultKey(InputAction action) => action switch"),
                @"InputAction\.(?<action>\w+)\s*=>\s*KeyCode\.(?<key>\w+)", RegexOptions.ExplicitCapture, RegexTimeout)
            .ToDictionary(m => m.Groups["action"].Value, m => m.Groups["key"].Value, StringComparer.Ordinal);
        var hardWired = HardWiredKeys();

        // The scan must see what it guards — a blind one would pass for ever.
        Assert.True(onFoot.Count >= 20, $"only {onFoot.Count} on-foot actions parsed from InputMap.Remappable");
        Assert.Contains("PingMarker", onFoot);
        Assert.Contains("Space", hardWired);       // jump
        Assert.Contains("LeftControl", hardWired); // crouch
        Assert.Contains("C", hardWired);           // crouch too — the key the ping was on

        var clashes = onFoot
            .Where(action => stockKeys.TryGetValue(action, out var key) && hardWired.Contains(key))
            .Select(action => $"{action} → {stockKeys[action]}")
            .ToList();

        Assert.True(clashes.Count == 0,
            "These on-foot actions have a stock key the keyboard already reads for walking, jumping, crouching, mining, "
            + "placing or the hotbar — one press would do both: " + string.Join(", ", clashes)
            + ". Give the action another key in InputMap.DefaultKey.");
        Assert.Equal("Period", stockKeys["PingMarker"]);
    }
}
