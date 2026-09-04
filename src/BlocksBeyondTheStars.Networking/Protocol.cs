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
    /// of the new symbols WITHOUT telling anyone; refusing the join says so plainly instead.
    /// v4 (#1533/#1535): MessagePack bodies above NetCodec.CompressionMinLengthBytes are LZ4 block arrays — a
    /// v3 reader hands the LZ4 ext block to the plain formatter and silently drops every entity list and
    /// chunk; and InventoryUpdate.BlueprintsUnchanged lets the server omit the unlocked-blueprint list, which
    /// a v3 client would read as "no blueprints" and grey out its whole tech tree.
    /// v5 (#1534): chunks and block changes travel as DeliveryMode.ReliableOrderedBulk — a second LiteNetLib
    /// channel (ChannelsCount = 2 on both peers; a v4 peer drops channel-1 packets) — so a lost chunk fragment
    /// no longer stalls presence, creatures and chat behind its resend. ChunkDataMessage, BlockChanged,
    /// WorldReset and JoinAccepted carry a WorldId, which lets the client order the two channels: world-stream
    /// messages of a world it has not been told about yet wait, those of the world it just left are dropped.</summary>
    public const int Version = 5;

    public const int DefaultGameplayPort = 31415;
    public const int DefaultAdminPort = 31416;

    /// <summary>Longest player name the server keeps on join (anything longer is truncated, control characters
    /// stripped). Client name fields cap their input at this so the name a player typed — and stored in the
    /// settings — is the identity the server actually uses (#1368).</summary>
    public const int MaxPlayerNameLength = 24;
}

/// <summary>Network delivery guarantees, mapped onto transport channels.</summary>
public enum DeliveryMode
{
    /// <summary>Guaranteed, in-order delivery — for actions and world deltas.</summary>
    ReliableOrdered,

    /// <summary>Best-effort, may drop/reorder — for frequent position updates.</summary>
    Unreliable,

    /// <summary>Guaranteed, in-order delivery on its OWN queue (#1534): the world stream (chunks, block changes)
    /// stays ordered against itself but a lost chunk fragment no longer holds back everything else. Transports
    /// without channels treat it exactly as <see cref="ReliableOrdered"/>.</summary>
    ReliableOrderedBulk,

    /// <summary>Best-effort, never duplicated, arrives in order (an older packet behind a newer one is dropped)
    /// — for the presence beat (#1534): a lost fragment of a chunk on the reliable stream no longer holds every
    /// avatar pose behind its resend round trip. Transports without the distinction treat it as reliable.</summary>
    Sequenced,
}
