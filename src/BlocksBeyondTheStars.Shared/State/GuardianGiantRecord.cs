// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Shared.State;

/// <summary>
/// #3676: one guardian giant lying on a body (<see cref="WorldMetadata.GuardianGiants"/>, keyed by <see cref="SiteId"/>). The
/// structure stamp writes it once, with the lying body; the boss logic keeps its state. The blocks themselves live on as the
/// world's ordinary block edits — the record only remembers what the stamp cannot tell: where the body lies, how it is turned,
/// whether it can wake, and what happened to it.
/// </summary>
public sealed class GuardianGiantRecord
{
    /// <summary><see cref="State"/>: lying, lights out — dead for good, or not woken yet when <see cref="CanWake"/>.</summary>
    public const string Dormant = "dormant";

    /// <summary><see cref="State"/>: it woke once, everyone left, it lay down again — lights on, healed, it rises again when a
    /// player comes within the wake range.</summary>
    public const string Lit = "lit";

    /// <summary><see cref="State"/>: standing — the lying body was removed and the boss walks. Kept while the world runs; a
    /// world loaded in this state stamps the body back (lit) at its resting place.</summary>
    public const string Awake = "awake";

    /// <summary><see cref="State"/>: defeated — it lies where it fell (<see cref="CorpseX"/> …), lights out, never wakes again.</summary>
    public const string Defeated = "defeated";

    /// <summary>The body (location id) it lies on.</summary>
    public string LocationId { get; set; } = string.Empty;

    /// <summary>Its index among the body's giants (the structure roll's order).</summary>
    public int Index { get; set; }

    /// <summary>The record key: <c>&lt;location&gt;#&lt;index&gt;</c>.</summary>
    public string SiteId => Key(LocationId, Index);

    /// <summary>The anchor of the lying body at its resting place: the world cell of the body's root in the lying pose
    /// (<c>GuardianGiantBody</c>); the lowest lying row is <c>GuardianGiantBody.LyingBottom</c> below it.</summary>
    public int X { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public int Y { get; set; }

    /// <summary>See <see cref="X"/>.</summary>
    public int Z { get; set; }

    /// <summary>Quarter turns about +Y of the lying body (0–3).</summary>
    public int Facing { get; set; }

    /// <summary>The body's seed: which plates rusted (<c>GuardianGiantBody.Standing</c>), the loot roll.</summary>
    public int Seed { get; set; }

    /// <summary>Whether this giant can wake (about one in four); the others are dead for good.</summary>
    public bool CanWake { get; set; }

    /// <summary><see cref="Dormant"/>, <see cref="Lit"/>, <see cref="Awake"/> or <see cref="Defeated"/>.</summary>
    public string State { get; set; } = Dormant;

    /// <summary>Defeated: the anchor of the body where it fell (equal to <see cref="X"/> … until then).</summary>
    public int CorpseX { get; set; }

    /// <summary>See <see cref="CorpseX"/>.</summary>
    public int CorpseY { get; set; }

    /// <summary>See <see cref="CorpseX"/>.</summary>
    public int CorpseZ { get; set; }

    /// <summary>See <see cref="CorpseX"/>.</summary>
    public int CorpseFacing { get; set; }

    /// <summary>Whether a player has come close enough to see it (its planet-map marker shows from then on).</summary>
    public bool Found { get; set; }

    /// <summary>When it was defeated (UTC unix seconds), 0 = not yet.</summary>
    public long DefeatedAt { get; set; }

    /// <summary>The players (ids) who were handed their loot crate for defeating it — each one once.</summary>
    public List<string> Rewarded { get; set; } = new();

    /// <summary>The record key of a giant.</summary>
    public static string Key(string locationId, int index) => locationId + "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
