// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

// VFX overhaul (#2152/#2154/#2158). All three messages are cosmetic: none of them changes the world, so a peer that
// does not know them drops the unknown tag and loses nothing but an effect — no protocol bump.

/// <summary>
/// Client → server (#2158): "I just did this" — a shot, a swing, a drill hit, a scan, a gadget use or a block placement,
/// drawn locally and now to be shown to the others. Purely cosmetic: the authoritative action travels in its own intent
/// (<c>AttackEntityIntent</c>, <c>MineBlockIntent</c> …). The server validates cheaply (kind, key length, finite numbers,
/// a per-player rate, <see cref="FromX"/> near the sender, <see cref="ToX"/> near the start) and relays it as
/// <see cref="ActionFx"/> to the other players nearby; anything else is dropped silently. Positions are world
/// coordinates of the sender's current world (on a surface the longitude/latitude seam is honoured; in space the flight
/// instance's frame).
/// </summary>
public sealed class FxIntent
{
    /// <summary>One of <c>Shared.Definitions.FxActionKinds</c> (1 shot … 6 place).</summary>
    public byte Kind { get; set; }

    /// <summary>The item (or, in space, the ship module) that made the effect; "" = bare hands. At most 48 characters.</summary>
    public string ItemKey { get; set; } = string.Empty;

    /// <summary>Where the effect starts (the muzzle, the hand, the drill tip).</summary>
    public float FromX { get; set; }
    public float FromY { get; set; }
    public float FromZ { get; set; }

    /// <summary>Where it ends (the impact point, the drilled cell, the aim point).</summary>
    public float ToX { get; set; }
    public float ToY { get; set; }
    public float ToZ { get; set; }

    /// <summary>True when the action hit something (an entity or a block) — impact sparks instead of a fizzle.</summary>
    public bool Hit { get; set; }
}

/// <summary>
/// Server → client (#2158): another player's tool action to draw with the same style the local one uses (the style
/// comes from the item's <c>fx</c> data, see <c>FxStyleResolver</c>). A relayed <see cref="FxIntent"/> never reaches its
/// own sender. With <see cref="Outcome"/> = true it is the server's confirmation of a successful gadget use and goes to
/// everyone nearby, the user included — the user's client plays the heal / freeze / blast / scan on this message.
/// <para>Coordinates: on a surface <see cref="FromX"/>/<see cref="FromZ"/> are canonical world coordinates (like a
/// presence position) and <see cref="ToX"/>/<see cref="ToZ"/> = From + the short way round the seam, so the pair stays
/// one straight segment; the receiver unwraps From into its own frame and keeps the offset. In space both are in the
/// flight instance's frame.</para>
/// </summary>
public sealed class ActionFx
{
    /// <summary>Who did it (the authoritative sender id, never the client's word).</summary>
    public string PlayerId { get; set; } = string.Empty;

    /// <summary>One of <c>Shared.Definitions.FxActionKinds</c>.</summary>
    public byte Kind { get; set; }

    /// <summary>The item (or ship module) key; "" = bare hands.</summary>
    public string ItemKey { get; set; } = string.Empty;

    public float FromX { get; set; }
    public float FromY { get; set; }
    public float FromZ { get; set; }
    public float ToX { get; set; }
    public float ToY { get; set; }
    public float ToZ { get; set; }

    /// <summary>The action hit something.</summary>
    public bool Hit { get; set; }

    /// <summary>True = a server-confirmed gadget outcome (the effect really happened); false = a relayed cosmetic
    /// action of another player.</summary>
    public bool Outcome { get; set; }
}

/// <summary>
/// Server → world (#2154): a creature was DEFEATED here — killed by a player, a sentry or fire — so the client breaks
/// it apart instead of letting it vanish. Sent before the creature is gone from the next <c>CreatureList</c>; a
/// creature that merely despawns (far away, crowded out, boxed in, tamed, released) gets no such message and simply
/// disappears. Machines and bandits keep using <c>PlanetEnemyDefeated</c>.
/// </summary>
public sealed class CreatureDefeated
{
    /// <summary>The creature's id, as in <c>NetCreature.Id</c>.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Where it fell (its feet), in world coordinates.</summary>
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
}
