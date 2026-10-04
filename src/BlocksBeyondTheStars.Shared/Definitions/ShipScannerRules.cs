// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>A ship's effective scanner (#2237/#2240): its tier, its object range (flight units), its scan time (the
/// hold ring, seconds) and the module that provides it (the look of the scan effect, the hotbar icon).</summary>
public readonly struct ShipScannerSpec
{
    public ShipScannerSpec(int tier, float range, float scanTime, string moduleKey)
    {
        Tier = tier;
        Range = range;
        ScanTime = scanTime;
        ModuleKey = moduleKey;
    }

    public int Tier { get; }
    public float Range { get; }
    public float ScanTime { get; }
    public string ModuleKey { get; }
}

/// <summary>
/// One rule for server and client (#2240): the fitted module with the highest <c>scanner_strength</c> stat is the
/// ship's scanner — the mandatory cockpit (tier 1), the Deep scanner (tier 2), the Quantum scanner (tier 3). Its
/// <c>scanner_range</c> and <c>scan_time</c> stats give the range and the hold time.
/// </summary>
public static class ShipScannerRules
{
    /// <summary>What a ship with no scanner module would have — the cockpit's values. Every ship has a cockpit; this
    /// only guards odd test ships and an old content set.</summary>
    public static readonly ShipScannerSpec Cockpit = new(1, 150f, 1.2f, "cockpit");

    /// <summary>The effective scanner of a ship with these modules.</summary>
    public static ShipScannerSpec For(System.Collections.Generic.IEnumerable<string> modules, System.Func<string, ShipModuleDefinition?> lookup)
    {
        ShipScannerSpec? best = null;
        foreach (string key in modules)
        {
            if (lookup(key) is not { } module
                || !module.Stats.TryGetValue("scanner_strength", out double strength)
                || best is { } current && (int)strength <= current.Tier)
            {
                continue;
            }

            best = new ShipScannerSpec(
                (int)strength,
                module.Stats.TryGetValue("scanner_range", out double range) ? (float)range : Cockpit.Range,
                module.Stats.TryGetValue("scan_time", out double time) ? (float)time : Cockpit.ScanTime,
                module.Key);
        }

        return best ?? Cockpit;
    }
}
