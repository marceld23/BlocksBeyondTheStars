// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
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

    /// <summary>Only camps within this range of a settlement trigger its quartermaster's call (#1158 —
    /// "a camp NEAR their settlement", not every camp on the world).</summary>
    private const double NpcCallCampRange = 400.0;

    /// <summary>Total relationship bonus for clearing a camp the player was CALLED about (#1158): the
    /// normal mission weight (3) plus a little extra gratitude on top.</summary>
    private const int NpcCallBountyExtra = 2;

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
        string text = Localize(session.Locale, textKey);
        if (!string.IsNullOrEmpty(arg))
        {
            text = text.Replace("{0}", arg);
        }

        return TryNpcRadioCallText(session, npcKey, npcName, place, bodyId, cooldownKey, text, isMission, requireKnown);
    }

    /// <summary>Same gates, but with an already-resolved text — for calls whose line is computed elsewhere
    /// (story threads, #1158).</summary>
    private bool TryNpcRadioCallText(PlayerSession session, string npcKey, string npcName, string place, string bodyId,
        string cooldownKey, string text, bool isMission, bool requireKnown = true)
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

        Send(session, new ChatMessage { Sender = $"📻 {npcName} ({place})", Text = text, IsNpcCall = true });
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

            // Each scan may place at most one call; a placed call arms the global cadence, which silences
            // the remaining scans of this pass automatically.
            ScanCampCalls(session);
            ScanRaiderCalls(session);
            ScanFoodCalls(session);
            ScanThreadCalls(session);
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

            var settlementCenter = new Vector3f(
                (s.Min.X + s.Max.X) * 0.5f, (s.Min.Y + s.Max.Y) * 0.5f, (s.Min.Z + s.Max.Z) * 0.5f);
            string npcKey = NpcKey(SettlementLocationKey(s.Name), "quartermaster");
            foreach (var camp in _banditCamps)
            {
                // Only a camp NEAR this settlement worries its quartermaster (#1158) — a camp on the far
                // side of the world is someone else's problem.
                if (camp.Cleared || WrapDistSq(camp.Center, settlementCenter) > NpcCallCampRange * NpcCallCampRange)
                {
                    continue;
                }

                if (TryNpcRadioCall(session, npcKey, CoinGiverName(s.Name), s.Name, bodyId,
                        $"camp:{s.Name}:{camp.Key}", "npc.call.camp", string.Empty, isMission: true))
                {
                    // Remember who called: clearing the camp now pays a friendship bonus (#1158).
                    session.CalledCamps[camp.Key] = (npcKey, CoinGiverName(s.Name), s.Name);
                    return;
                }
            }
        }
    }

    /// <summary>Friendship bonus (#1158): the player cleared a camp a quartermaster had CALLED them about —
    /// gratitude on top of the normal mission weight, plus a thank-you over the radio.</summary>
    private void NpcRadioOnCampCleared(BanditCampInstance camp)
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined || !session.CalledCamps.Remove(camp.Key, out var call))
            {
                continue;
            }

            var p = session.State;
            RecordNpcInteraction(p, call.NpcKey, call.Name, "quartermaster", NpcInteractionKind.MissionAccepted, call.Place);
            if (p.NpcMemory.TryGetValue(call.NpcKey, out var rel))
            {
                rel.Value = System.Math.Clamp(rel.Value + NpcCallBountyExtra, -100, 100);
            }

            _repo.SavePlayer(p);
            SendNpcStandings(session);

            // A direct reply to the player's own deed — deliberately outside the cadence/cooldown gates
            // (they already received the call, so they hold a radio).
            Send(session, new ChatMessage
            {
                Sender = $"📻 {call.Name} ({call.Place})",
                Text = Localize(session.Locale, "npc.call.camp_thanks"),
                IsNpcCall = true,
            });
        }
    }

    /// <summary>Raider warning (#1158): hostile raider ships active in the player's star system — an
    /// acquainted quartermaster on the player's world passes the sighting along.</summary>
    private void ScanRaiderCalls(PlayerSession session)
    {
        string? mySystem = _galaxy?.FindBody(session.CurrentLocationId)?.SystemId;
        if (mySystem is null)
        {
            return;
        }

        foreach (var (instanceId, instance) in _spaceInstances)
        {
            if (!instance.Entities.Any(e => e.Kind == CombatEntityKind.BanditShip && e.Hostile))
            {
                continue;
            }

            string raidedBodyId = instanceId.StartsWith("space:", System.StringComparison.Ordinal)
                ? instanceId.Substring("space:".Length)
                : instanceId;
            var raidedBody = _galaxy?.FindBody(raidedBodyId);

            // A fresh ship's instance can carry the planet TYPE instead of a body id (the relay fallback's
            // sibling): when the id doesn't resolve, the player's OWN instance is by definition in their
            // system — a raider right next to them absolutely warrants the warning.
            bool inMySystem = raidedBody?.SystemId == mySystem
                || (raidedBody is null && instance.Players.Contains(session.State.PlayerId));
            if (!inMySystem)
            {
                continue;
            }

            string raidedName = raidedBody?.Name ?? _galaxy?.FindBody(session.CurrentLocationId)?.Name ?? string.Empty;

            foreach (var s in _settlements)
            {
                if (s.Ruined || string.IsNullOrEmpty(s.Name) || !s.Markers.Any(m => m.Type == "mission_board"))
                {
                    continue;
                }

                string npcKey = NpcKey(SettlementLocationKey(s.Name), "quartermaster");
                if (TryNpcRadioCall(session, npcKey, CoinGiverName(s.Name), s.Name, _world.LocationId,
                        "raider:" + mySystem, "npc.call.raider", raidedName, isMission: false))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Food-shortage call (#1158): a known quartermaster whose board carries a food delivery
    /// mentions the settlement is running short.</summary>
    private void ScanFoodCalls(PlayerSession session)
    {
        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name) || !s.Markers.Any(m => m.Type == "mission_board"))
            {
                continue;
            }

            string npcKey = NpcKey(SettlementLocationKey(s.Name), "quartermaster");
            foreach (string missionId in s.MissionIds)
            {
                if (!_missionDefs.TryGetValue(missionId, out var mission) || !mission.Active)
                {
                    continue;
                }

                bool foodDelivery = mission.Objectives.Any(o =>
                    o.Type == MissionObjectiveType.Deliver && (_content.GetItem(o.Target)?.ConsumeHunger ?? 0f) > 0f);
                if (foodDelivery
                    && TryNpcRadioCall(session, npcKey, CoinGiverName(s.Name), s.Name, _world.LocationId,
                        "food:" + missionId, "npc.call.food", string.Empty, isMission: true))
                {
                    return;
                }
            }
        }
    }

    /// <summary>Story rumours over the radio (#1158, the radio half of the G3 threads): a known NPC with an
    /// untold thread calls it in. Gates run BEFORE the once-milestone is burnt (peek → call → commit), so a
    /// blocked call keeps the thread for the next greeting or scan.</summary>
    private void ScanThreadCalls(PlayerSession session)
    {
        if (!StoryActive || _story is null || _story.NpcThreads.Count == 0)
        {
            return;
        }

        foreach (var s in _settlements)
        {
            if (s.Ruined || string.IsNullOrEmpty(s.Name))
            {
                continue;
            }

            foreach (string role in new[] { "quartermaster", "vendor", "settler" })
            {
                string npcKey = NpcKey(SettlementLocationKey(s.Name), role);
                if (!session.State.NpcMemory.TryGetValue(npcKey, out var rel)
                    || PeekNpcThread(session, role, npcKey) is not { } thread)
                {
                    continue;
                }

                string name = string.IsNullOrEmpty(rel.Name) ? CoinGiverName(s.Name) : rel.Name;
                if (TryNpcRadioCallText(session, npcKey, name, s.Name, _world.LocationId,
                        "thread:" + thread.Key, Localize(session.Locale, thread.TextKey), isMission: false))
                {
                    CommitNpcThread(session, thread);
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

    /// <summary>Test seam: run one full radio trigger scan for a player right now (skips the scan timer).</summary>
    public void ScanNpcRadioForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            ScanCampCalls(session);
            ScanRaiderCalls(session);
            ScanFoodCalls(session);
            ScanThreadCalls(session);
        }
    }

    /// <summary>Test seam: a settlement's stable NPC-memory location key (for seeding standings).</summary>
    public string SettlementLocationKeyForTest(string settlementName) => SettlementLocationKey(settlementName);

    /// <summary>Test seam: the first inhabited settlement carrying a mission board (the radio scans' anchor).</summary>
    public string BoardSettlementNameForTest()
        => _settlements.FirstOrDefault(s => !s.Ruined && !string.IsNullOrEmpty(s.Name)
            && s.Markers.Any(m => m.Type == "mission_board"))?.Name ?? string.Empty;

    /// <summary>Test seam: registers a mission def on a settlement's board (so the food-shortage call has
    /// something to point at, #1158).</summary>
    public void AddSettlementBoardMissionForTest(string settlementName, Shared.Missions.MissionDefinition def)
    {
        _missionDefs[def.Id] = def;
        _settlements.First(s => s.Name == settlementName).MissionIds.Add(def.Id);
    }

    /// <summary>Test seam: adds a hostile raider ship to the player's space instance (#1158 raider call).</summary>
    public void SpawnRaiderShipForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance))
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = "raider_test",
                Kind = CombatEntityKind.BanditShip,
                Name = "Raider",
                Hostile = true,
                Hull = 10f,
                HullMax = 10f,
            });
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
