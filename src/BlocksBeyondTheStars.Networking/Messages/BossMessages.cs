// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Server → client (#3901): the state of one boss fight for the boss bar and the boss music — generic, so the bar does
/// not need to know which boss it shows. Sent to every pilot of the flight instance the boss is in: at once when the
/// fight starts (the boss warps in), when the phase changes, when its hull or shield changed (at most twice a second),
/// about once a second otherwise (a pilot who joins the flight late hears it within a second), and one last time with
/// <see cref="Active"/> false when the fight is over. A client that hears no status of an <see cref="Id"/> for a few
/// seconds may drop its bar (the pilot left the flight).
/// <para>The escort cruiser (#3900) is the first boss that uses it (<see cref="Source"/> <c>space</c>). The guardian
/// giant keeps its own <see cref="GuardianGiantStatus"/>; the bar reads both.</para>
/// </summary>
public sealed class BossStatus
{
    /// <summary>The boss's entity id — for a space boss the id of its <see cref="NetCombatEntity"/> in the
    /// <see cref="SpaceState"/> (kind <c>Cruiser</c>), so the client knows which ship is the boss.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The locale key of the name on the bar (<c>enemy.escort_cruiser.name</c>).</summary>
    public string NameKey { get; set; } = string.Empty;

    /// <summary>Hull left, 0..1.</summary>
    public float Health { get; set; }

    /// <summary>Shield left, 0..1 (0 = none, or gone — it does not come back).</summary>
    public float Shield { get; set; }

    /// <summary>The fight's phase: 1 = the first, 2 = enraged (<c>EscortCruiserRules.PhaseNormal</c> /
    /// <c>PhaseEnraged</c>). A change is the client's cue for the phase effect.</summary>
    public byte Phase { get; set; }

    /// <summary>Which kind of boss: <c>BossSources.Giant</c> ("giant") or <c>BossSources.Space</c> ("space").</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>True while the fight is on. False once, as the last status of this <see cref="Id"/>: the bar goes and the
    /// boss music ends.</summary>
    public bool Active { get; set; }

    /// <summary>With <see cref="Active"/> false: the boss was defeated (true) or it left — it jumped away after the
    /// pilots flew clear (false).</summary>
    public bool Defeated { get; set; }
}
