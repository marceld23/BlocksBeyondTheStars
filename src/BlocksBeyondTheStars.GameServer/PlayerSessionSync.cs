// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>Per-connection client-sync bookkeeping of a <see cref="PlayerSession"/>
/// (RAM only, never persisted).</summary>
public sealed class PlayerSessionSync
{
    /// <summary>Countdown to the next station-set re-evaluation (StationsInReach publish-on-change).</summary>
    public double StationScanIn { get; set; }

    /// <summary>Compact key of the last StationsInReach sent, so the tick only pushes real changes.</summary>
    public string? LastStationsInReach { get; set; }

    // --- Periodic vitals sync (HUD bars froze between event-driven sends before) ---
    public double VitalsSyncTimer { get; set; }
    public float LastSentHealth = 100f;
    public float LastSentOxygen = 100f;
    public float LastSentEnergy = 100f;
    public float LastSentHunger = 100f;

    /// <summary>#1530: a combat/vitals path changed the player's state this tick — GameServer.FlushEntityLists
    /// sends ONE PlayerStateUpdate at the end of the world tick (each biting attacker used to send its own).</summary>
    public bool PlayerStateDirty { get; set; }

    // --- The last blow on this player (#2564) — sent with every PlayerStateUpdate (LastHit*) ---

    /// <summary>Entity id of the creature, machine, bandit or giant that last hurt this player (empty = none yet).</summary>
    public string LastHitEntityId { get; set; } = string.Empty;

    /// <summary>Where that blow came from: the attacker's position, or a giant's stomp/strike spot.</summary>
    public BlocksBeyondTheStars.Shared.Geometry.Vector3f LastHitFrom { get; set; }

    /// <summary>Counts the blows (0 = none yet this session) — the client treats every change as a fresh hit.</summary>
    public int LastHitSeq { get; set; }

    // #1530: presence on change + keep-alive. Per viewer: the hash of the presence they last received and the
    // beat it was sent on; a subject is (re)sent to a viewer when the hash changed, the viewer never had it,
    // or PresenceKeepAliveBeats elapsed — so a stationary player costs one message per keep-alive, not ten a second.
    public Dictionary<int, (ulong Hash, int Beat)> PresenceSentTo { get; } = new();
    public int PresenceBeat { get; set; }

    // #1535: signature of the unlocked-blueprint set as last sent — SendInventory omits the list while it holds.
    public long SentBlueprintsSignature { get; set; }
    public bool SentBlueprintsOnce { get; set; }

    // #2201: the same for the sample case, and the species this client already has in its research book.
    public long SentSamplesSignature { get; set; }
    public bool SentSamplesOnce { get; set; }
    public HashSet<uint> BioBookSeeds { get; } = new();
}
