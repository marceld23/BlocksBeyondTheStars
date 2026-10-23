// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking.Messages;

public sealed class MoveIntent
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }

    /// <summary>#2113: the moving frame. Aboard a train the client reports its pose INSIDE the wagon — this is the frame
    /// (<c>RailRules.FrameId</c>: train and wagon) and X/Y/Z are then the wagon-local offset; the server derives the world
    /// position from the wagon pose it drives. Empty = a world pose, as always. Additive: an older client never sends it.</summary>
    public string FrameId { get; set; } = string.Empty;

    /// <summary>#2193: the player hangs on a wall or a ladder — other clients pose the avatar climbing. Pure pose state
    /// (on-foot movement is the client's); additive, an older client never sends it.</summary>
    public bool Climbing { get; set; }

    /// <summary>#2435: the player crouches — other clients pose the avatar squatting. Pure pose state like
    /// <see cref="Climbing"/>; additive, an older client never sends it.</summary>
    public bool Crouching { get; set; }

    /// <summary>#2723: the world the client stood in when it sent this pose — the WorldId of its last JoinAccepted /
    /// WorldReset. The server drops a pose from a world it has already moved the player out of (the reset was still on
    /// its way); 0 = unknown (an older client), which is always taken.</summary>
    public int WorldId { get; set; }
}

/// <summary>The client (which owns on-foot movement) reports a hard landing; the server applies fall damage
/// scaled by how far over a safe impact speed it was.</summary>
public sealed class FallDamageIntent
{
    public float ImpactSpeed { get; set; }
}

/// <summary>Client uses the suit teleporter to recall to its ship (server validates device/cooldown/energy).</summary>
public sealed class TeleportToShipIntent { }

/// <summary>Client → server (#2286): the pause menu's "Back to my ship" — a stuck player on foot asks to be put back
/// aboard their own ship. No device, no energy: the server checks the world rule, that the player is on foot, that the
/// own ship is landed on this body, not in a fight, not falling, and the three-minute cooldown
/// (<c>ReturnToShipRules</c>), then snaps the player aboard with a <see cref="RespawnNotice"/>. Refusals arrive as
/// <c>@srv.return_ship.*</c> toasts. No protocol bump — an older server drops the unknown tag.</summary>
public sealed class ReturnToShipIntent { }

/// <summary>Client uses the suit teleporter to beam to an <b>allied</b> player on the same body (#1056). The
/// server validates device/cooldown/energy, the alliance, same-body + not-in-space, and that the target is not
/// aboard their own ship; the arrival lands beside the target. Rejects arrive as <c>@srv.tp.*</c> toasts.</summary>
public sealed class TeleportToPlayerIntent
{
    /// <summary>The ally to beam to (player id == display name).</summary>
    public string TargetPlayerId { get; set; } = string.Empty;
}

/// <summary>Client toggles the stealth field (requires a stealth suit + energy).</summary>
public sealed class ToggleStealthIntent { }

/// <summary>Client → server: the player is (or is no longer) firing the jetpack. The server drains suit
/// energy while active and forces it off when the energy runs out (the client applies the thrust locally).</summary>
public sealed class SetJetpackIntent
{
    public bool Active { get; set; }
}

/// <summary>Client → server (#2296): the player opened (or closed) the glider while falling. The glide itself is the
/// client's (on-foot movement) and costs no energy; the server only checks that a glider is worn and mirrors the flag
/// into the presence so other players see the wing.</summary>
public sealed class SetGlidingIntent
{
    public bool Active { get; set; }
}

/// <summary>Client → server (#2319): the player let go of the energy rope (a crouch, a hop, a snapped rope, a hotbar
/// change). The shot itself rides the gadget intent; this only clears the anchor the presence shows to others.</summary>
public sealed class ReleaseRopeIntent { }

/// <summary>Client → server: the suit lamp was switched on or off (#1077). Purely informational — the lamp
/// itself is a client-side shader light; the server only uses this for VEGA's "it's dark and your lamp is
/// off" context tip. Sent on every toggle, when the client forces the lamp off, and once after spawning.</summary>
public sealed class SetLampIntent
{
    public bool On { get; set; }
}

/// <summary>Client → server: the player sat down on (or stood up from) a chair-shaped cell (#806).
/// Pure pose state — no buffs; it rides the presence broadcast so other players see a seated avatar.
/// The client owns the camera/movement lock; the server mirrors the flag and, since #2122, checks the seat:
/// it must be a seat within reach that no seated NPC and no other seated player occupies — otherwise an
/// <see cref="ActionRejected"/> with action <c>"seat"</c> comes back and the client stands up again.</summary>
public sealed class SetSeatedIntent
{
    public bool Active { get; set; }

    /// <summary>#2122: <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> carry the seat cell the player sits down on.
    /// Additive (contractless): false from an older client — the server then looks for the seat around the
    /// player's position.</summary>
    public bool HasCell { get; set; }

    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
}

public sealed class PlayerStateUpdate
{
    public string PlayerId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }
    public float Pitch { get; set; }
    public float Health { get; set; }
    public float Oxygen { get; set; }
    public float SuitEnergy { get; set; }
    public float Hunger { get; set; }

    /// <summary>Whether the player is currently inside their ship (enables cargo crafting, oxygen regen).</summary>
    public bool AboardShip { get; set; }

    /// <summary>True while the player is on an EVA spacewalk (floating in space on foot) — drives the
    /// suit float HUD and tells the client to skip the ship take-off animation when boarding again.</summary>
    public bool InEva { get; set; }

    /// <summary>True while the on-foot player has built/climbed above the atmosphere into space (item 10)
    /// — the client floats in zero-g and shows a space sky.</summary>
    public bool AboveAtmosphere { get; set; }

    /// <summary>True while the suit's climate control is fighting extreme heat/cold/vacuum (#666) —
    /// tells the HUD WHY suit energy (or, once empty, health) is falling.</summary>
    public bool SuitClimateActive { get; set; }

    /// <summary>The exposure meter 0..1 (2026-09, Titas) — the HUD shows the cold/heat protection left.</summary>
    public float Exposure { get; set; }

    /// <summary>True while the exposure meter is running (on foot outside on a timed-exposure type).</summary>
    public bool ExposureActive { get; set; }

    /// <summary>True when the heat of a hot zone, not the cold, fills the meter.</summary>
    public bool ExposureHot { get; set; }

    /// <summary>Valuma's mood (2026-09): the player has stayed long enough that the music darkens.</summary>
    public bool Uneasy { get; set; }

    /// <summary>Which life support keeps this player breathing (#794): 0 none (own tank / the world's own
    /// air), 1 ship cabin, 2 station, 3 base (zone cube or sealed room). Lets the HUD name the source —
    /// the client cannot mirror the sealed-room fill locally. New field on an existing contractless
    /// MessagePack message: an older client simply ignores it, an older server leaves it 0.</summary>
    public byte LifeSupportSource { get; set; }

    /// <summary>Name of the space station the player is currently boarded on (empty when not on one).</summary>
    public string StationName { get; set; } = string.Empty;

    /// <summary>AI-core tier of the player's active ship (1 = bare VEGA, 2 = Mk2, 3 = Mk3) — gates the
    /// client-side autopilot assist and the companion panel's ability hints.</summary>
    public int AiCoreTier { get; set; } = 1;

    /// <summary>Id of the hover speeder the player is currently piloting (empty = on foot). Drives the client's
    /// switch into vehicle-drive mode + the speeder HUD.</summary>
    public string InSpeeder { get; set; } = string.Empty;

    /// <summary>#2113: the train frame the player rides in (train:wagon, empty = on foot) and the seat (−1 = standing).
    /// The client parents itself to that wagon and reports its pose inside it.</summary>
    public string InTrain { get; set; } = string.Empty;
    public int TrainSeat { get; set; } = -1;

    /// <summary>Fleet-admin observer mode is active for this player (issue #487): the client switches to free
    /// flight with no collision, hides the viewmodel/hotbar and shows the SPECTATOR badge. Server-authoritative
    /// — the client never decides this for itself, and every other player's server never sends this player's
    /// presence at all while it is set.</summary>
    public bool Spectating { get; set; }

    /// <summary>This world lets the player fly freely (Creative/Sandbox worlds, or the <c>/fly</c> cheat).
    /// The client offers the double-tap-jump toggle only while this is set. New field on an existing
    /// contractless MessagePack message: an older client ignores it, an older server leaves it false.</summary>
    public bool CanFly { get; set; }

    /// <summary>Zero-g construction mode (#1842) is on for this player on their boarded player station: the
    /// float is chosen, not a drift over the edge — the HUD words its hints and badge accordingly. New field on
    /// an existing contractless MessagePack message: an older client ignores it, an older server leaves it false.</summary>
    public bool StationZeroG { get; set; }

    /// <summary>The status effects this player is under (#2202) — sent whole each time, the client counts the seconds
    /// down between updates. New field on an existing contractless MessagePack message: an older client ignores it.</summary>
    public NetEffect[] Effects { get; set; } = System.Array.Empty<NetEffect>();

    /// <summary>What is left of the shield cushion (#2202): extra health on top of the 100, drawn beside the health bar.</summary>
    public float Shield { get; set; }

    /// <summary>The player's own stealth cloak is up (#2291) — the HUD gear strip lights it and the client plays the cloak's
    /// sound on this answer rather than on the key. New field on an existing contractless MessagePack message: an older
    /// client ignores it, an older server leaves it false.</summary>
    public bool Stealthed { get; set; }

    /// <summary>Seconds until "Back to my ship" (#2286) may be used again, 0 when it is ready — the pause menu greys its
    /// button and counts the rest down between updates. New field on an existing contractless MessagePack message: an
    /// older client ignores it, an older server leaves it 0.</summary>
    public float ReturnToShipCooldownSeconds { get; set; }

    /// <summary>Seconds a fight still blocks "Back to my ship" (#2559), 0 when none does — the grace after the last blow.
    /// The pause menu greys its row with a countdown that runs in REAL time: the grace keeps running while the menu holds
    /// the world (#2558). A negative value (<c>ReturnToShipRules.RobberWaitingFightSeconds</c>) means a robber waits for
    /// the player's answer: blocked, no countdown. New field on an existing contractless MessagePack message: an older
    /// client ignores it, an older server leaves it 0.</summary>
    public float ReturnToShipFightSeconds { get; set; }

    /// <summary>#2564: where the last blow on this player came from — the attacker's position when it hit (a stomp's or a
    /// strike's spot for a giant). The HUD's damage marker points there (and above/below), instead of guessing the
    /// nearest hostile. Only meaningful once <see cref="LastHitSeq"/> is not 0. New fields on an existing contractless
    /// MessagePack message: an older client ignores them, an older server leaves them 0/empty.</summary>
    public float LastHitFromX { get; set; }
    public float LastHitFromY { get; set; }
    public float LastHitFromZ { get; set; }

    /// <summary>#2564: the entity id of that attacker (creature, machine, bandit or giant) — the client blinks it red and
    /// keeps the marker on it while it moves.</summary>
    public string LastHitEntityId { get; set; } = string.Empty;

    /// <summary>#2564: changes with every blow the server records on this player (0 = none yet this session), so the
    /// client can tell a fresh hit from a repeated report of an old one.</summary>
    public int LastHitSeq { get; set; }
}

/// <summary>Authoritative state of another player, for rendering them (avatar + nameplate).</summary>
public sealed class PlayerPresence
{
    public string PlayerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Yaw { get; set; }

    // Avatar colours, packed 0xRRGGBB.
    public int Skin { get; set; }
    public int Torso { get; set; }
    public int Arms { get; set; }
    public int Legs { get; set; }

    /// <summary>Stealth field active — other clients fade/hide the avatar + nameplate.</summary>
    public bool Stealthed { get; set; }

    /// <summary>Jetpack firing — other clients show the thrust flame under the avatar.</summary>
    public bool Jetpacking { get; set; }

    /// <summary>Gliding (#2296) — other clients open the glider's wing over the avatar.</summary>
    public bool Gliding { get; set; }

    /// <summary>Sitting on a chair-shaped cell (#806) — other clients pose the avatar seated.</summary>
    public bool Seated { get; set; }

    /// <summary>Hanging on a wall or a ladder (#2193) — other clients pose the avatar climbing, facing the wall.</summary>
    public bool Climbing { get; set; }

    /// <summary>Crouching (#2435) — other clients pose the avatar squatting. Additive; an older client ignores it.</summary>
    public bool Crouching { get; set; }

    /// <summary>The look pitch in degrees (#2434, up is negative like the camera's): other clients nod the avatar's head
    /// so you can see where someone looks. Additive; an older client ignores it, an older server leaves it 0.</summary>
    public float Pitch { get; set; }

    /// <summary>The energy rope is out (#2319): other clients draw it from the avatar's hand to the anchor below.</summary>
    public bool Roped { get; set; }

    /// <summary>The rope's anchor (world position on a block face) while <see cref="Roped"/>.</summary>
    public float RopeX { get; set; }
    public float RopeY { get; set; }
    public float RopeZ { get; set; }

    /// <summary>Equipped-gear bitmask shown on the avatar — the bits of <c>Shared.State.GearLook</c>: 1=helmet, 2=chest,
    /// 4=legs, 8=jetpack, 16=lamp, 32=boots, 64=tank, 128=climbing gloves, 256=climbing claws, and the tier-2 looks on top
    /// (#2294–#2297): 512=titanium helmet, 1024=titanium chest, 2048=titanium legs, 4096=spring boots, 8192=glider,
    /// 16384=suit battery.</summary>
    public int Gear { get; set; }

    /// <summary>Item key currently held (selected hotbar slot), shown in the avatar's hand; empty if none.</summary>
    public string Held { get; set; } = string.Empty;

    /// <summary>#2113: aboard a train — the frame (train:wagon) and the rider's wagon-local offset, so every client places
    /// the avatar relative to the wagon as IT draws it and nobody slides through a wall. X/Y/Z stay the world pose the
    /// server derived (an older client keeps using them). Empty frame = on foot.</summary>
    public string FrameId { get; set; } = string.Empty;
    public float LocalX { get; set; }
    public float LocalY { get; set; }
    public float LocalZ { get; set; }
}

/// <summary>A player left; the client removes their avatar.</summary>
public sealed class PlayerLeft
{
    public string PlayerId { get; set; } = string.Empty;
}
