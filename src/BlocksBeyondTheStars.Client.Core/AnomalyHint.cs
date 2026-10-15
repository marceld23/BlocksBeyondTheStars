// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// What the flight view says about a space anomaly (#2588). A player flew up to one, took the shimmering bubble with its
/// glitching cubes for a wormhole and pressed E at it — nothing happened, and nothing said why: it was labelled "???"
/// (the server's name for it, which is exactly what an unknown wormhole's destination reads), and the only explanation
/// was a one-time VEGA tip lost among eighteen unread messages. So an anomaly is now named by its KIND wherever it is
/// labelled (<see cref="NameKey"/>; never by the server's name, since servers before #2588 still send "???"), and flying
/// close to it or pressing E next to it says what it is and how to read it — on every approach, rate-limited, not once
/// per save — or, once this pilot has scanned it, that it has been read already. The anomaly itself never changes:
/// it stays scan-only (knowledge and a field record). Unity-free so range, scanned state and rate limit are covered by
/// plain .NET tests.
/// </summary>
public sealed class AnomalyHint
{
    /// <summary>The anomaly's name on the radar rim, the system chart, the lock frame and the scan card.</summary>
    public const string NameKey = "ui.scan.subject.anomaly";

    /// <summary>"An anomaly — not a wormhole. Pick the scanner and hold fire on it."</summary>
    public const string UnscannedKey = "ui.space.anomaly_hint";

    /// <summary>"You have already read this anomaly."</summary>
    public const string ScannedKey = "ui.space.anomaly_read";

    /// <summary>The scan readout kind the server sends for an anomaly (<c>ScanResult.Kind</c>).</summary>
    public const string ReadoutKind = "anomaly";

    /// <summary>This close (flight units) counts as an approach: twice the life pod's rescue range (14) and just past a
    /// wormhole's "fly through" reach (26) — clearly heading for it, and the line comes before the ship bumps into it.</summary>
    public const float ApproachRange = 28f;

    /// <summary>The ship has to fall back beyond this before the next approach speaks again — the slack keeps a pilot
    /// circling on the edge of <see cref="ApproachRange"/> from hearing the line over and over.</summary>
    public const float RearmRange = 40f;

    /// <summary>No two lines within this many seconds — an approach followed by an E press, or E pressed again and again;
    /// the toast of the first one is still up anyway.</summary>
    public const float CooldownSeconds = 4f;

    private readonly HashSet<string> _near = new HashSet<string>(StringComparer.Ordinal);
    private float _lastShownAt = float.NegativeInfinity;

    /// <summary>Whether a space entity of this kind is an anomaly.</summary>
    public static bool IsAnomaly(string? kind) => kind == SpaceTargeting.Anomaly;

    /// <summary>One cruise frame, once per anomaly of the instance: the line to show now (a locale key), or null. The
    /// first frame inside <see cref="ApproachRange"/> is the approach (one that falls into the cooldown stays silent);
    /// falling back beyond <see cref="RearmRange"/> arms the next one.</summary>
    public string? Approach(string id, float distance, bool scanned, float now)
    {
        if (distance > RearmRange)
        {
            _near.Remove(id);
            return null;
        }

        if (distance > ApproachRange || !_near.Add(id))
        {
            return null;
        }

        return Line(scanned, now);
    }

    /// <summary>E pressed in flight with nothing else for it to do: the line for the nearest anomaly when it is within
    /// <see cref="ApproachRange"/> (pass <see cref="float.PositiveInfinity"/> when there is none), or null.</summary>
    public string? Interact(float distance, bool scanned, float now)
        => distance <= ApproachRange ? Line(scanned, now) : null;

    /// <summary>The flight view closed: the next flight's first look at an anomaly is a fresh approach again. The
    /// cooldown stays — the clock keeps running across flights.</summary>
    public void Reset() => _near.Clear();

    private string? Line(bool scanned, float now)
    {
        if (now - _lastShownAt < CooldownSeconds)
        {
            return null;
        }

        _lastShownAt = now;
        return scanned ? ScannedKey : UnscannedKey;
    }
}

/// <summary>The name a pinned encounter shows at the radar's rim (#2241, #2588).</summary>
public static class SpaceEncounterLabel
{
    /// <summary>A life pod reads "SOS · {survivor}", an anomaly and a wormhole their localized kind — never the entity's
    /// name, which for an anomaly was "???" up to #2588 (older servers still send it) and for a wormhole is empty —
    /// and everything else its own name.</summary>
    public static string Rim(string kind, string name, Func<string, string> localize) => kind switch
    {
        SpaceTargeting.EscapePod => "SOS · " + name,
        SpaceTargeting.Anomaly => localize(AnomalyHint.NameKey),
        SpaceTargeting.Wormhole => localize("ui.scan.subject.wormhole"),
        _ => name,
    };
}
