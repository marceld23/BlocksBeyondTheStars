// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

/// <summary>
/// Client → server (#3898): the fire control is HELD with a held-beam weapon in the hand (the Guardian laser). The
/// client sends one of these per beam tick while the control stays down — the pattern of the drill's hold loop, which
/// sends a mine hit every 0.28 s — and one last one with <see cref="Active"/> false when it is released. (Not to be
/// confused with the teleporter pads' <see cref="BeamTeleportIntent"/>.)
/// <para>The server stays the truth: it resolves at most one tick per the weapon's <c>beamTick</c> whatever arrives,
/// validates the held item, the reach, the line of fire and the aim exactly as it validates a shot
/// (<see cref="AttackEntityIntent"/>), drains suit energy per tick and stops the beam at the suit's reserve. A beam it
/// has not heard of for <c>BeamWeaponRules.TimeoutSeconds</c> is over. An older server drops the message (the weapon
/// then only ever fires its tap shot).</para>
/// </summary>
public sealed class BeamWeaponIntent
{
    /// <summary>True = the beam is on (one tick); false = the control was released.</summary>
    public bool Active { get; set; }

    /// <summary>The entity under the beam as the client sees it — a machine, a creature, a robber or
    /// <c>player:&lt;id&gt;</c> — or empty when the beam meets nothing alive. A claim only: the server validates it.</summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>The camera ray of the beam (the aim the server validates, and the line the others see).</summary>
    public float DirX { get; set; }
    public float DirY { get; set; }
    public float DirZ { get; set; }

    /// <summary>True when the beam ends on terrain: <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> is the struck cell
    /// (what <see cref="ShootBlockIntent"/> carries for a shot), which an igniting weapon may set alight.</summary>
    public bool HasCell { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}
