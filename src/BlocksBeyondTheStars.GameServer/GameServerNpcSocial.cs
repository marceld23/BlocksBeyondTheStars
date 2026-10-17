// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
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

        if (BaseAround(player) is { } home)
        {
            return home.Name; // #1865: the people at a base live there
        }

        return _boardedStation.TryGetValue(player.PlayerId, out var stationId)
            ? _galaxy?.FindBody(stationId)?.Name ?? string.Empty
            : string.Empty;
    }

    /// <summary>The stable memory key for a live NPC as seen by this player — the ONE derivation every reader uses
    /// (greetings, dialogues, standings, hints, radio, #2598): an authored character's global key, a base resident's
    /// slot key, a villager's own <see cref="ServerNpc.PersonKey"/>, else the shared role key of the NPC's settlement
    /// or the receiver's boarded station. A travelling merchant or a lost traveller at the base (#2602) is
    /// <c>guest:&lt;name&gt;</c> — a key that is never written to the save.</summary>
    private string NpcKeyForNpc(PlayerSession session, ServerNpc npc)
        => !string.IsNullOrEmpty(npc.CharacterId)
            ? "char:" + npc.CharacterId // an authored character (#1128) remembers the player GLOBALLY
            : !string.IsNullOrEmpty(npc.GuestKey)
                ? npc.GuestKey // #2602: a passing stranger, not a person to remember
            : npc.BaseId > 0
                ? BaseResidentKeyOf(npc) // a base resident: the key pinned in its base's plan, rename- and id-proof (#1262, #1865, #2716)
            : !string.IsNullOrEmpty(npc.PersonKey)
                ? npc.PersonKey // #2598: a villager is a PERSON — friendship per bed/post, not per role
            : !string.IsNullOrEmpty(npc.Settlement)
                ? NpcKey(SettlementLocationKey(npc.Settlement), npc.Role)
                : _boardedStation.TryGetValue(session.State.PlayerId, out var stationId)
                    ? NpcKey(StationLocationKey(stationId), npc.Role)
                    : NpcKey(SettlementLocationKey(string.Empty), npc.Role);

    /// <summary>Per-receiver stages for the live NPCs (#1118). Only non-strangers are listed — absence
    /// means stranger, so the common case (a fresh settlement) is an empty message. A villager without an entry of
    /// their own shows the floor of the old shared standing (#2598).</summary>
    private void SendNpcStandings(PlayerSession session)
    {
        var ids = new List<int>();
        var stages = new List<string>();
        foreach (var npc in _npcs)
        {
            int value = RelationshipValue(session.State, NpcKeyForNpc(session, npc));
            if (RelationshipTier(value) != "stranger")
            {
                ids.Add(npc.Id);
                stages.Add(NpcStageKey(value));
            }
        }

        // #2601: the villagers with a personal wish for this player ("!") and the ones whose wish is ready ("?").
        var wishOffers = new List<int>();
        var wishReady = new List<int>();
        WishMarksFor(session, wishOffers, wishReady);

        Send(session, new NpcStandingList
        {
            NpcIds = ids.ToArray(),
            StageKeys = stages.ToArray(),
            WishNpcIds = wishOffers.ToArray(),
            WishReadyNpcIds = wishReady.ToArray(),
        });
    }

    /// <summary>The "People you know" roster (#1118): everyone with a standing, friendliest first. A villager shows
    /// their job when it has a name of its own (#2598: "Innkeeper", "Doctor"), else the role.</summary>
    private void HandleRequestKnownNpcs(PlayerSession session)
    {
        Send(session, new KnownNpcList { People = KnownNpcEntries(session.State) });
        RaiseGameEvent(session, Shared.Story.GameEventKeys.NpcPeople); // #2482: the neighbours course
    }

    /// <summary>Stores the player's NPC radio-call preference (#1119) in the save, and takes the session's VEGA hints
    /// mute (#2462, B31) that rides on the same intent.</summary>
    private void HandleSetNpcCalls(PlayerSession session, SetNpcCallsIntent intent)
    {
        session.VegaHintsMuted = intent.VegaHintsMuted;
        session.State.NpcCallsMode = System.Enum.IsDefined(typeof(NpcCallsMode), intent.Mode)
            ? (NpcCallsMode)intent.Mode
            : NpcCallsMode.All;
        _repo.SavePlayer(session.State);
    }
}
