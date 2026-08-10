// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Networking;

/// <summary>Shared protocol constants for client/server compatibility.</summary>
public static class Protocol
{
    /// <summary>Bumped whenever the wire format or message set changes incompatibly.
    /// v2: ChunkDataMessage carries the run-length-encoded BlocksRle payload (older clients
    /// cannot decode it, so the join-time version check keeps them off newer servers).
    /// v3: the pixel-payload alphabet widened from hex to base32 (#899 — a 32-colour palette). An older
    /// server charset-checks against hex and would drop a face/body painting/block design containing any
    /// of the new symbols WITHOUT telling anyone; refusing the join says so plainly instead.</summary>
    public const int Version = 3;

    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;
}

/// <summary>Network delivery guarantees, mapped onto transport channels.</summary>
public enum DeliveryMode
{
    /// <summary>Guaranteed, in-order delivery — for actions and world deltas.</summary>
    ReliableOrdered,

    /// <summary>Best-effort, may drop/reorder — for frequent position updates.</summary>
    Unreliable,
}
