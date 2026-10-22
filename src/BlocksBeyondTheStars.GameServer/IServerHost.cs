// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The server members most extracted subsystems need (ADR 0019). Every per-subsystem host interface (for example
/// <see cref="ICrewHost"/>) derives from it and adds only what that one subsystem asks of the server, as the compiler
/// listed it during the extraction. <c>GameServer</c> implements it once, explicitly, in <c>GameServer.Systems.cs</c>;
/// each member delegates to the server's own member of the same name. A member joins this interface only when at
/// least three subsystems need it.
/// </summary>
/// <remarks>
/// Read every member at use time: <see cref="World"/> and <see cref="Worlds"/> follow the world cursor and exist only
/// once <c>GameServer.Start</c> has run, so a subsystem never calls its host from its constructor or caches what it got.
/// </remarks>
internal interface IServerHost
{
    /// <summary>Sends one message to one session (the server's <c>Send</c>).</summary>
    void Send(PlayerSession session, object message);

    /// <summary>Refuses an intent with an <c>ActionRejected</c> (the server's <c>Reject</c>).</summary>
    void Reject(PlayerSession session, string action, string reason);

    /// <summary>Reports a gameplay event to the listening story and course steps (the server's <c>RaiseGameEvent</c>;
    /// same defaults).</summary>
    void RaiseGameEvent(PlayerSession? actor, string key, string arg = "", int amount = 1);

    /// <summary>The JOINED session of a player id, or null (the server's <c>FindSessionByPlayerId</c>).</summary>
    PlayerSession? FindSessionByPlayerId(string playerId);

    /// <summary>The active voxel world under the world cursor (the server's <c>_world</c>).</summary>
    ServerWorld World { get; }

    /// <summary>The server's tick clock in seconds (its <c>_uptime</c>).</summary>
    double Uptime { get; }

    /// <summary>The resident worlds (the server's <c>_worlds</c>).</summary>
    WorldManager Worlds { get; }

    /// <summary>Sends a player their state update (the server's <c>SendPlayerState</c>).</summary>
    void SendPlayerState(PlayerSession session);

    /// <summary>Sends a player their inventory (the server's <c>SendInventory</c>).</summary>
    void SendInventory(PlayerSession session);
}
