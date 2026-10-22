// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Crews (#1216) on the server: the facade of <see cref="CrewSystem"/> (ADR 0019), which owns the crew graph, the
/// pending invites and every crew verb. This partial keeps the members sibling partials, the dispatch table and the
/// tests call — same names, same signatures, each one line delegating — and the explicit <see cref="ICrewHost"/>
/// implementation.
/// </summary>
public sealed partial class GameServer : ICrewHost
{
    /// <summary>True while the two players are members of the same crew — the crew half of <c>AreAllied</c>.</summary>
    public bool SameCrew(string a, string b) => _crewSystem.SameCrew(a, b);

    private CrewSystem.ServerCrew? CrewOf(string playerId) => _crewSystem.CrewOf(playerId);

    /// <summary>Crew states (id/name/owner/members) for tests + inspection.</summary>
    public IReadOnlyList<(string Id, string Name, string OwnerId, string[] Members)> CrewSnapshots => _crewSystem.CrewSnapshots;

    private void LoadAllCrews() => _crewSystem.LoadAllCrews();

    private void HandleCrewAction(PlayerSession session, CrewActionIntent intent) => _crewSystem.HandleCrewAction(session, intent);

    private void SendCrewList(PlayerSession session) => _crewSystem.SendCrewList(session);

    private void ClearCrewPending(string playerId) => _crewSystem.ClearCrewPending(playerId);

    /// <summary>Test/util: run a crew verb as a player. Returns the player's crew id afterwards ("" = none).</summary>
    public string CrewActionForTest(string playerId, string kind, string name = "", string target = "")
        => _crewSystem.CrewActionForTest(playerId, kind, name, target);

    // ICrewHost: each member delegates to the server's own member of the same name.
    string? ICrewHost.ScreenPlayerName(PlayerSession session, string clean, string surface) => ScreenPlayerName(session, clean, surface);

    string ICrewHost.NameOf(string playerId) => NameOf(playerId);

    void ICrewHost.RefreshAllianceRoster(string playerId) => RefreshAllianceRoster(playerId);

    void ICrewHost.RefreshMarkersFor(IEnumerable<string> playerIds) => RefreshMarkersFor(playerIds);
}
