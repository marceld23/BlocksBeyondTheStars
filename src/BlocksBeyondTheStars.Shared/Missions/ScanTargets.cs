// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Shared.Missions;

/// <summary>
/// The target grammar of a <see cref="MissionObjectiveType.Scan"/> objective (#1205) — shared by the content
/// validator, the server's player-mission whitelist and the progress hook, so the three can never disagree:
/// <list type="bullet">
/// <item><c>any</c> — every scan counts.</item>
/// <item><c>creature:any</c> · <c>creature:hostile</c> · <c>creature:&lt;speciesId&gt;</c> — handheld creature scans.</item>
/// <item><c>block:&lt;blockKey&gt;</c> — a block, tree or flora scan of that block key (never a monument rune).</item>
/// <item><c>flora:any</c> · <c>tree:any</c> · <c>monument:any</c> · <c>microfauna:any</c> — by readout kind.</item>
/// <item><c>asteroid</c> · <c>anomaly</c> — the ship scanner in space.</item>
/// </list>
/// The hook decides with the scan readout's <em>kind</em> (creature / block / tree / flora / monument /
/// microfauna / asteroid / anomaly), its subject key and the hostile flag — the same values every scan already
/// carries, so no scan path needs to know about missions.
/// </summary>
public static class ScanTargets
{
    public const string Any = "any";

    /// <summary>Whether a target string is well-formed (and, for <c>block:&lt;key&gt;</c>, whether the block exists —
    /// <paramref name="blockExists"/> answers that; pass <c>null</c> to accept any key).</summary>
    public static bool IsValid(string? target, Func<string, bool>? blockExists)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string t = target.Trim();
        switch (t)
        {
            case Any:
            case "creature:any":
            case "creature:hostile":
            case "flora:any":
            case "tree:any":
            case "monument:any":
            case "microfauna:any":
            case "asteroid":
            case "anomaly":
                return true;
        }

        if (t.StartsWith("creature:", StringComparison.Ordinal))
        {
            return t.Length > "creature:".Length; // a species id — per world, so only its shape is checkable
        }

        if (t.StartsWith("block:", StringComparison.Ordinal))
        {
            string key = t.Substring("block:".Length);
            return key.Length > 0 && (blockExists is null || blockExists(key));
        }

        return false;
    }

    /// <summary>Whether one scan (readout kind + subject key + hostile flag) satisfies a target.</summary>
    public static bool Matches(string? target, string kind, string subjectKey, bool hostile)
    {
        string t = (target ?? string.Empty).Trim();
        switch (t)
        {
            case Any:
                return true;
            case "creature:any":
                return kind == "creature";
            case "creature:hostile":
                return kind == "creature" && hostile;
            case "flora:any":
                return kind == "flora";
            case "tree:any":
                return kind == "tree";
            case "monument:any":
                return kind == "monument";
            case "microfauna:any":
                return kind == "microfauna";
            case "asteroid":
                return kind == "asteroid";
            case "anomaly":
                return kind == "anomaly";
        }

        if (t.StartsWith("creature:", StringComparison.Ordinal))
        {
            return kind == "creature" && string.Equals(subjectKey, t.Substring("creature:".Length), StringComparison.Ordinal);
        }

        if (t.StartsWith("block:", StringComparison.Ordinal))
        {
            // A block, a tree or a flora scan all keep the block key as the subject; a monument rune re-keys
            // its subject to the monument, so it can never satisfy a block target by accident.
            return kind is "block" or "tree" or "flora"
                   && string.Equals(subjectKey, t.Substring("block:".Length), StringComparison.Ordinal);
        }

        return false;
    }
}
