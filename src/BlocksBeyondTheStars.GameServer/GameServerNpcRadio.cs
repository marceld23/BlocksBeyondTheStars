// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC radio calls (#1119): the world reaches out. An NPC the player KNOWS (relationship ≥ known) calls
/// them over the radio — "📻 Name (Place)" in chat — about a bandit camp bounty, a refilled mission board,
/// or a trader setting down near their base (#1120). Gated by the player's radio tier (comm = same world,
/// system = same system, galaxy = anywhere), throttled to ≤1 call / 10 min with per-call cooldowns (the
/// VEGA context-tip pattern), and governed by the persisted <see cref="PlayerState.NpcCallsMode"/>
/// preference. Calls are informational or helpful, never threats — family presets stay untouched (their
/// worlds simply have no camps to call about). "Queued while offline" is the repo's recompute-on-join
/// pattern: the first scan after the join quiet period announces whatever is still true.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Global cadence: at most one NPC call per player per 10 minutes.</summary>
    private const double NpcCallGap = 600.0;

    /// <summary>Quiet period after a join before the first call — a rejoin never opens with a ringing radio.</summary>
    private const double NpcCallJoinQuiet = 90.0;

    /// <summary>How often each player's call triggers are re-scanned.</summary>
    private const double NpcCallScanInterval = 30.0;

    /// <summary>The same call (same camp, same board) repeats at most once per hour.</summary>
    private const double NpcCallRepeatCooldown = 3600.0;

    /// <summary>Arms the join quiet period (called from the join flow).</summary>
    private void ArmNpcRadioOnJoin(PlayerSession session)
    {
        session.NpcCallReadyAt = System.Math.Max(session.NpcCallReadyAt, _uptime + NpcCallJoinQuiet);
        session.NpcCallScanAt = _uptime + NpcCallJoinQuiet;
    }

    /// <summary>Whether the player's radio tier reaches a location: galaxy = anywhere, system = the same
    /// star system, comm = the same body. No radio, no call — the world can't reach a silent suit.</summary>
    private bool NpcRadioReaches(PlayerSession session, string bodyId)
    {
        var inv = session.State.Inventory;
        if (inv.Has("galaxy_radio", 1))
        {
            return true;
        }

        if (inv.Has("system_radio", 1))
        {
            string? mySystem = _galaxy?.FindBody(session.CurrentLocationId)?.SystemId;
            return mySystem is not null && mySystem == _galaxy?.FindBody(bodyId)?.SystemId;
        }

        return inv.Has("comm_radio", 1) && session.CurrentLocationId == bodyId;
    }

    /// <summary>One NPC radio call, if every gate opens: preference, global cadence, per-call cooldown,
    /// radio reach, and the relationship (only someone the player KNOWS calls — strangers stay quiet;
    /// <paramref name="requireKnown"/> false is for hails that aren't personal, e.g. a landed trader
    /// advertising to the base owner, #1120). Returns true when the call went out.</summary>
    private bool TryNpcRadioCall(PlayerSession session, string npcKey, string npcName, string place, string bodyId,
        string cooldownKey, string textKey, string arg, bool isMission, bool requireKnown = true)
    {
        var p = session.State;
        if (p.NpcCallsMode == NpcCallsMode.Off || (p.NpcCallsMode == NpcCallsMode.MissionsOnly && !isMission))
        {
            return false;
        }

        if (_uptime < session.NpcCallReadyAt
            || (session.NpcCallCooldownUntil.TryGetValue(cooldownKey, out double until) && _uptime < until)
            || !NpcRadioReaches(session, bodyId))
        {
            return false;
        }

        if (requireKnown
            && (!p.NpcMemory.TryGetValue(npcKey, out var rel) || RelationshipTier(rel.Value) is not ("known" or "trusted")))
        {
            return false;
        }

        string text = Localize(session.Locale, textKey);
        if (!string.IsNullOrEmpty(arg))
        {
            text = text.Replace("{0}", arg);
        }

        Send(session, new ChatMessage { Sender = $"📻 {npcName} ({place})", Text = text });
        session.NpcCallReadyAt = _uptime + NpcCallGap;
        session.NpcCallCooldownUntil[cooldownKey] = _uptime + NpcCallRepeatCooldown;
        return true;
    }

    /// <summary>Periodic trigger scan (Guard-registered): active-world events an acquaintance would call
    /// about. One call at most per scan per player (the global cadence enforces the rest).</summary>
    private void TickNpcRadio()
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined || _uptime < session.NpcCallScanAt)
            {
                continue;
            }

            session.NpcCallScanAt = _uptime + NpcCallScanInterval;
            ScanCampCalls(session);
        }
    }

    /// <summary>An uncleared bandit camp on the active world: its settlement's quartermaster (if the player
    /// knows them) calls about the bounty on their board. Family/peaceful worlds have no camps — no call.</summary>
    private void ScanCampCalls(PlayerSession session)
    {
        if (!BanditsActive)
        {
            return;
        }

        string bodyId = _world.LocationId;
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name) || !s.Markers.Any(m => m.Type == "mission_board"))
            {
                continue;
            }

            string npcKey = NpcKey(SettlementLocationKey(s.Name), "quartermaster");
            foreach (var camp in _banditCamps)
            {
                if (!camp.Cleared
                    && TryNpcRadioCall(session, npcKey, CoinGiverName(s.Name), s.Name, bodyId,
                        $"camp:{s.Name}:{camp.Key}", "npc.call.camp", string.Empty, isMission: true))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Board-refill hint (#1119): after a board turn-in the window slides — the quartermaster
    /// mentions there is something new. Hint-category (muted by "missions only").</summary>
    private void NpcRadioOnBoardTurnIn(PlayerSession session, string missionId, string giverName)
    {
        string place = NpcPlaceFor(session.State);
        TryNpcRadioCall(session, NpcKey(LocationKeyOfMission(missionId), "quartermaster"), giverName,
            string.IsNullOrEmpty(place) ? giverName : place, _world.LocationId,
            "board:" + LocationKeyOfMission(missionId), "npc.call.board", string.Empty, isMission: false);
    }

    /// <summary>Test seam: run one radio trigger scan for a player right now (skips the scan timer).</summary>
    public void ScanNpcRadioForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            ScanCampCalls(session);
        }
    }

    /// <summary>Test seam: clears the join quiet period + global cadence for a player.</summary>
    public void SkipNpcCallCooldownsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            session.NpcCallReadyAt = 0;
            session.NpcCallScanAt = 0;
        }
    }
}
