// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC identity made visible (#1118): the relationship the memory system has always tracked now reaches the
/// player — a stage on the nameplate (the greeting-tone thresholds, localized), and a "People you know"
/// roster in the Character tab, straight from the persisted <see cref="PlayerState.NpcMemory"/>. Plus the
/// NPC radio-call preference intent (#1119) — stored server-side because the server initiates the calls.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Locale key for a relationship score's stage (#1118) — the SAME thresholds the greeting tone
    /// has always used (<see cref="RelationshipTier"/>), now player-visible.</summary>
    private static string NpcStageKey(int value) => "npc.stage." + RelationshipTier(value);

    /// <summary>Where the player currently is, as a display name (settlement / boarded station / empty) —
    /// captured into the NPC memory so the roster can say where an acquaintance lives (#1118).</summary>
    private string NpcPlaceFor(PlayerState player)
    {
        foreach (var s in _settlements)
        {
            if (!s.Ruined && PlayerInSettlement(player, s))
            {
                return s.Name;
            }
        }

        return _boardedStation.TryGetValue(player.PlayerId, out var stationId)
            ? _galaxy?.FindBody(stationId)?.Name ?? string.Empty
            : string.Empty;
    }

    /// <summary>The stable memory key for a live NPC as seen by this player — mirrors the greeting
    /// pipeline's derivation (the NPC's settlement; else the receiver's boarded station).</summary>
    private string NpcKeyForNpc(PlayerSession session, ServerNpc npc)
        => !string.IsNullOrEmpty(npc.CharacterId)
            ? "char:" + npc.CharacterId // an authored character (#1128) remembers the player GLOBALLY
            : !string.IsNullOrEmpty(npc.Settlement)
                ? NpcKey(SettlementLocationKey(npc.Settlement), npc.Role)
                : _boardedStation.TryGetValue(session.State.PlayerId, out var stationId)
                    ? NpcKey(StationLocationKey(stationId), npc.Role)
                    : NpcKey(SettlementLocationKey(string.Empty), npc.Role);

    /// <summary>Per-receiver stages for the live NPCs (#1118). Only non-strangers are listed — absence
    /// means stranger, so the common case (a fresh settlement) is an empty message.</summary>
    private void SendNpcStandings(PlayerSession session)
    {
        var ids = new List<int>();
        var stages = new List<string>();
        foreach (var npc in _npcs)
        {
            if (session.State.NpcMemory.TryGetValue(NpcKeyForNpc(session, npc), out var rel)
                && RelationshipTier(rel.Value) != "stranger")
            {
                ids.Add(npc.Id);
                stages.Add(NpcStageKey(rel.Value));
            }
        }

        Send(session, new NpcStandingList { NpcIds = ids.ToArray(), StageKeys = stages.ToArray() });
    }

    /// <summary>The "People you know" roster (#1118): everyone with a standing, friendliest first.</summary>
    private void HandleRequestKnownNpcs(PlayerSession session)
    {
        var people = session.State.NpcMemory.Values
            .Where(r => r.Value != 0 && !string.IsNullOrEmpty(r.Name))
            .OrderByDescending(r => r.Value)
            .Take(64)
            .Select(r => new NetKnownNpc
            {
                Name = r.Name,
                RoleKey = "npc.role." + (string.IsNullOrEmpty(r.Role) ? "vendor" : r.Role),
                StageKey = NpcStageKey(r.Value),
                Place = r.Place,
                Standing = r.Value,
            })
            .ToArray();
        Send(session, new KnownNpcList { People = people });
    }

    /// <summary>Stores the player's NPC radio-call preference (#1119) in the save.</summary>
    private void HandleSetNpcCalls(PlayerSession session, SetNpcCallsIntent intent)
    {
        session.State.NpcCallsMode = System.Enum.IsDefined(typeof(NpcCallsMode), intent.Mode)
            ? (NpcCallsMode)intent.Mode
            : NpcCallsMode.All;
        _repo.SavePlayer(session.State);
    }
}
