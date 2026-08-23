// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Mission chains (#1212): multi-step missions with prerequisites, a giver role, a relationship gate and a
/// surface (board / radio / dialogue). Three sources share one rule set:
/// <list type="bullet">
/// <item><b>Authored</b> chains in <c>data/missions.json</c> — the 4-step "settlement needs" chain on every
/// settlement board and the 3-step vendor friendship chain handed out in conversation.</item>
/// <item><b>Procedural "big orders"</b> — after <see cref="BigOrderEveryTurnIns"/> turn-ins at one board the
/// quartermaster coins a 2-step order (ids <c>…c&lt;n&gt;</c>, rewards × <see cref="BigOrderRewardFactor"/>)
/// for players they know.</item>
/// <item><b>Dialogue grants</b> — the <c>mission:&lt;id&gt;</c> consequence (#1127 graph walker) puts a step
/// straight into the log; a dialogue offering a mission is only picked while that mission can be taken.</item>
/// </list>
/// The visibility rule (<see cref="ChainStepAvailable"/>) runs for the Available list AND is enforced on
/// accept, so a client cannot skip ahead. Progress stays per player (<see cref="MissionProgress"/> carries
/// <c>ChainId</c> + where the step was taken); a chain stays at the place it was started — step 2 of a
/// settlement's chain is offered at THAT settlement's board only. Nothing here runs per tick.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Every this many turn-ins at one board the quartermaster coins a new 2-step big order.</summary>
    private const int BigOrderEveryTurnIns = 3;

    /// <summary>Big orders pay this multiple of the ordinary template reward — they ask for a lot more.</summary>
    private const float BigOrderRewardFactor = 2.5f;

    private const string BigOrderSlotInfix = "c";  // settle_{hash}_c{n} — two consecutive n per order

    private static bool IsChainMission(MissionDefinition def) => !string.IsNullOrEmpty(def.ChainId);

    /// <summary>The stable location key of the place the player is standing in — the settlement's board
    /// key (<c>settle_&lt;hash&gt;</c>) or the boarded station's (<c>station_&lt;hash&gt;</c>); empty in the wild.</summary>
    private string CurrentPlaceKey(PlayerState p)
    {
        foreach (var s in _settlements)
        {
            if (!s.Ruined && !string.IsNullOrEmpty(s.Name) && PlayerInSettlement(p, s))
            {
                return SettlementLocationKey(s.Name);
            }
        }

        return _boardedStation.TryGetValue(p.PlayerId, out var stationId) ? StationLocationKey(stationId) : string.Empty;
    }

    private bool AtAnyMissionBoard(PlayerState p) => NearSettlementMissionBoard(p) || NearSpaceStationMissionBoard(p);

    private static bool PlaceIsStation(string placeKey) => placeKey.StartsWith("station_", StringComparison.Ordinal);

    /// <summary>Whether a chain step may be offered at this kind of place (<c>OfferAt</c>: settlement by default).</summary>
    private static bool OfferAtMatches(MissionDefinition def, string placeKey) => def.OfferAt switch
    {
        MissionChains.OfferAtStation => PlaceIsStation(placeKey),
        MissionChains.OfferAtAny => placeKey.Length > 0,
        _ => placeKey.StartsWith("settle_", StringComparison.Ordinal),
    };

    /// <summary>The NPC-memory key of a chain step's giver at a place: an authored character remembers the
    /// player globally (<c>char:&lt;id&gt;</c>), everyone else is "that role at that place".</summary>
    private static string ChainGiverKey(MissionDefinition def, string placeKey)
    {
        string role = MissionChains.GiverRoleOf(def);
        return role.StartsWith(MissionChains.CharacterRolePrefix, StringComparison.Ordinal)
            ? "char:" + role[MissionChains.CharacterRolePrefix.Length..]
            : NpcKey(placeKey, role);
    }

    private bool ChainStageSatisfied(PlayerState p, MissionDefinition def, string giverKey)
    {
        if (MissionChains.StageRank(def.MinStage) == 0)
        {
            return true;
        }

        int value = p.NpcMemory.TryGetValue(giverKey, out var rel) ? rel.Value : 0;
        return MissionChains.StageRank(RelationshipTier(value)) >= MissionChains.StageRank(def.MinStage);
    }

    /// <summary>All prerequisites turned in. <paramref name="boundPlace"/> is where the chain was started (the
    /// latest prerequisite's AcceptedFrom) — empty when the chain is not bound to a place yet.</summary>
    private static bool ChainPrerequisitesMet(PlayerState p, MissionDefinition def, out string boundPlace)
    {
        boundPlace = string.Empty;
        foreach (var id in def.Prerequisites)
        {
            var done = p.Missions.FirstOrDefault(m => m.MissionId == id && m.Status == MissionStatus.TurnedIn);
            if (done is null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(done.AcceptedFrom))
            {
                boundPlace = done.AcceptedFrom;
            }
        }

        return true;
    }

    /// <summary>Whether the world has reached the story point a definition asks for (#1213). Empty gate =
    /// always satisfied, which is every mission that existed before this field did.</summary>
    private bool StoryGateSatisfied(MissionDefinition def) => def.RequiresStory switch
    {
        MissionChains.StoryGuardianDefeated => _storyState.GuardianDefeated,
        _ => true,
    };

    /// <summary>World feasibility of a chain step — a step that could never finish here is never offered
    /// (the same promise the scan templates keep, #1205). "Clear the camp" needs an uncleared camp, the
    /// hostile/rune scans need their subjects.</summary>
    private bool ChainStepFeasible(MissionDefinition def)
    {
        foreach (var o in def.Objectives)
        {
            switch (o.Type)
            {
                case MissionObjectiveType.Defeat when o.Target == DefeatTargetMachine:
                    // Remnant drones only exist post-win AND only where hostiles are switched on at all —
                    // on a family preset or in creative there is nothing to defeat, so never offer it (#1206).
                    if (!RemnantEra || !PlanetEnemiesActive)
                    {
                        return false;
                    }

                    break;
                case MissionObjectiveType.Contribute:
                    if (_content.Relay is null || !_content.Relay.Costs.Any(c => c.Item == o.Target) || !AnyRelayOpen())
                    {
                        return false; // nothing left to build, or the item is not on the relay's bill of materials
                    }

                    break;
                case MissionObjectiveType.Travel when o.Target == MissionChains.TravelUnlinkedSystem:
                    if (!AnyUnlinkedSystem())
                    {
                        return false; // the network already reaches everything we could send them to
                    }

                    break;
                case MissionObjectiveType.Defeat when o.Target == DefeatTargetCamp:
                    if (!BanditsActive || !_banditCamps.Any(c => !c.Cleared))
                    {
                        return false;
                    }

                    break;
                case MissionObjectiveType.Defeat when o.Target == DefeatTargetScout:
                    if (!BaseVisitorsActive)
                    {
                        return false; // scouts only come when the world option is on (#1224)
                    }

                    break;
                case MissionObjectiveType.Scan when o.Target == "creature:hostile":
                    if (!_speciesRoster.Any(sp => sp.Hostile))
                    {
                        return false;
                    }

                    break;
                case MissionObjectiveType.Scan when o.Target == "monument:any":
                    if (_monuments.Count == 0)
                    {
                        return false;
                    }

                    break;
            }
        }

        return true;
    }

    /// <summary>Alternatives (same ChainId + Step): the feasible one with the lowest id wins.</summary>
    private bool IsPreferredAlternative(MissionDefinition def)
    {
        if (!ChainStepFeasible(def))
        {
            return false;
        }

        foreach (var other in _missionDefs.Values)
        {
            if (!ReferenceEquals(other, def) && other.Active && other.ChainId == def.ChainId && other.Step == def.Step
                && string.CompareOrdinal(other.Id, def.Id) < 0 && ChainStepFeasible(other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>THE chain rule (#1212): whether <paramref name="def"/> can be offered to / taken by this player
    /// right now. Checks: not held, no sibling alternative held or done, prerequisites turned in, the feasible
    /// alternative, the surface (board steps need a matching board — and the chain's own place once it is
    /// bound; dialogue steps only through a dialogue; radio steps anywhere) and the giver's relationship stage.
    /// <paramref name="reason"/> is the client-facing failure key when it returns false.</summary>
    private bool ChainStepAvailable(PlayerState p, MissionDefinition def, string placeKey, bool atBoard,
        bool viaDialog, string? dialogGiverKey, out string reason)
    {
        reason = "@srv.mission.chain_locked";
        if (!StoryGateSatisfied(def))
        {
            return false; // e.g. the SPS survey orders before the Guardian is down (#1213)
        }

        if (p.Missions.Any(m => m.MissionId == def.Id))
        {
            reason = "@srv.mission.accepted";
            return false;
        }

        foreach (var m in p.Missions)
        {
            if (m.MissionId != def.Id && GetMissionDef(m.MissionId) is { } sibling
                && sibling.ChainId == def.ChainId && sibling.Step == def.Step)
            {
                return false; // the other alternative of this step is held / done
            }
        }

        if (!ChainPrerequisitesMet(p, def, out var bound) || !IsPreferredAlternative(def))
        {
            return false;
        }

        string surface = MissionChains.SurfaceOf(def);
        string giverKey;
        switch (surface)
        {
            case MissionChains.SurfaceDialog:
                if (!viaDialog || dialogGiverKey is null)
                {
                    reason = "@srv.mission.chain_giver";
                    return false;
                }

                // A place-bound chain continues with the SAME giver (an authored character is the same person anywhere).
                if (bound.Length > 0 && !dialogGiverKey.StartsWith("char:", StringComparison.Ordinal)
                    && !dialogGiverKey.StartsWith(bound + ":", StringComparison.Ordinal))
                {
                    reason = "@srv.mission.chain_giver";
                    return false;
                }

                giverKey = dialogGiverKey;
                break;

            case MissionChains.SurfaceRadio:
                giverKey = ChainGiverKey(def, bound.Length > 0 ? bound : placeKey);
                break;

            default: // board
                if (!atBoard || !OfferAtMatches(def, placeKey) || (bound.Length > 0 && bound != placeKey))
                {
                    reason = "@srv.mission.chain_board";
                    return false;
                }

                giverKey = ChainGiverKey(def, placeKey);
                break;
        }

        if (!ChainStageSatisfied(p, def, giverKey))
        {
            reason = "@srv.mission.chain_stage";
            return false;
        }

        return true;
    }

    /// <summary>The missions offered to a player right now — the plain Available list plus the chain rule,
    /// with the board scoping (<paramref name="currentBoardIds"/>) already applied by the caller's window run.</summary>
    private List<MissionDefinition> AvailableMissionsFor(PlayerState player, HashSet<string> currentBoardIds)
    {
        string place = CurrentPlaceKey(player);
        bool atBoard = AtAnyMissionBoard(player);
        var list = new List<MissionDefinition>();
        foreach (var def in _missionDefs.Values)
        {
            if (!def.Active || player.Missions.Any(m => m.MissionId == def.Id))
            {
                continue; // already accepted / turned in (non-repeatable) is hidden
            }

            if (IsBoardMissionId(def.Id) && !currentBoardIds.Contains(def.Id))
            {
                continue; // a board mission belonging to a board the player isn't standing at
            }

            if (IsChainMission(def) && !ChainStepAvailable(player, def, place, atBoard, viaDialog: false, null, out _))
            {
                continue; // #1212: hidden until its prerequisites / place / stage line up
            }

            list.Add(def);
        }

        return list;
    }

    /// <summary>Progress entry for a freshly accepted mission — chain steps remember their chain and where
    /// they were taken (the place binds the chain, the body drives the <c>other_body</c> travel target).</summary>
    private MissionProgress NewMissionProgress(PlayerState p, MissionDefinition def, string acceptedFrom)
        => new()
        {
            MissionId = def.Id,
            Status = MissionStatus.Active,
            ObjectiveProgress = Enumerable.Repeat(0, def.Objectives.Count).ToList(),
            ChainId = def.ChainId,
            AcceptedFrom = IsChainMission(def) ? acceptedFrom : string.Empty,
            AcceptedBodyId = IsChainMission(def) ? p.CurrentLocationId : string.Empty,
        };

    /// <summary>Accept-time bookkeeping for a chain step: the giver remembers the player (friendship grows
    /// along the chain), and a procedural step's definition is persisted so progress far from the board —
    /// and across a restart — still has a definition to count against (the bounty pattern, #730).</summary>
    private void OnChainStepAccepted(PlayerSession session, MissionDefinition def, string giverKey, string giverName)
    {
        if (!IsBoardMissionId(def.Id))
        {
            // Board ids already went through RecordMissionAccepted; authored chain ids record here.
            RecordNpcInteraction(session.State, giverKey, giverName, MissionChains.GiverRoleOf(def),
                NpcInteractionKind.MissionAccepted, NpcPlaceFor(session.State));
            SendNpcStandings(session);
        }
        else
        {
            _repo.SaveMission(def);
        }
    }

    /// <summary>Turn-in gate for a chain step: board steps report back at the board of the place the chain
    /// is bound to; dialogue steps to the giver (their role, in reach, at that place); radio steps anywhere.</summary>
    private bool ChainTurnInAllowed(PlayerState p, MissionDefinition def, MissionProgress pr, out string reason)
    {
        reason = string.Empty;
        string place = CurrentPlaceKey(p);
        bool placeOk = string.IsNullOrEmpty(pr.AcceptedFrom) || pr.AcceptedFrom == place;
        switch (MissionChains.SurfaceOf(def))
        {
            case MissionChains.SurfaceDialog:
                string role = MissionChains.GiverRoleOf(def);
                bool giverNear = role.StartsWith(MissionChains.CharacterRolePrefix, StringComparison.Ordinal)
                    ? _npcs.Any(n => n.CharacterId == role[MissionChains.CharacterRolePrefix.Length..] && WrapDistSq(p.Position, n.Pos) <= NpcGreetRange * NpcGreetRange)
                    : NearestNpc(p, role) is { } npc && WrapDistSq(p.Position, npc.Pos) <= NpcGreetRange * NpcGreetRange;
                if (!placeOk || !giverNear)
                {
                    reason = "@srv.mission.chain_return_giver";
                    return false;
                }

                return true;

            case MissionChains.SurfaceRadio:
                return true;

            default:
                if (!placeOk || !AtAnyMissionBoard(p))
                {
                    reason = "@srv.mission.chain_return_board";
                    return false;
                }

                return true;
        }
    }

    /// <summary>After a chain step is turned in: when a next step exists the giver calls a little later
    /// ("I've got something more for you") — through the dialogue-promised-call queue, so it obeys the
    /// radio gates, the player's NpcCallsMode and the 90 s delay, and gives up if the player stays away.</summary>
    private void OnChainStepTurnedIn(PlayerSession session, MissionDefinition def, MissionProgress pr)
    {
        bool hasNext = !string.IsNullOrEmpty(def.NextMissionId)
            || _missionDefs.Values.Any(d => d.Active && d.ChainId == def.ChainId && d.Prerequisites.Contains(def.Id));
        if (!hasNext)
        {
            RestartChainIfRepeatable(session, def);
            return;
        }

        string place = string.IsNullOrEmpty(pr.AcceptedFrom) ? CurrentPlaceKey(session.State) : pr.AcceptedFrom;
        string giverKey = ChainGiverKey(def, place);
        string giverName = !string.IsNullOrEmpty(def.GiverName) ? def.GiverName
            : session.State.NpcMemory.TryGetValue(giverKey, out var rel) && !string.IsNullOrEmpty(rel.Name) ? rel.Name
            : CoinGiverName(place);
        string placeName = NpcPlaceFor(session.State);
        _dialogRadioPending.Add((session.State.PlayerId, giverKey, giverName,
            string.IsNullOrEmpty(placeName) ? giverName : placeName, session.CurrentLocationId,
            ChainRadioLineKey, _uptime + DialogRadioDelaySeconds, _uptime + DialogRadioGiveUpSeconds));
    }

    /// <summary>A repeatable chain (<c>repeatable: true</c> on its LAST step) starts over once that step is
    /// turned in: every progress row of the chain is dropped, so step 1 is offered again (#1213). The
    /// endgame survey orders use this — a one-shot chain would leave the station boards quiet again, which
    /// is the very thing the Remnant Protocol (#1206) set out to fix.
    /// <para>Note this canNOT go through the ordinary <c>def.Repeatable</c> path in HandleTurnInMission:
    /// that removes only THIS row, and a chain's rows are what the next run's prerequisite check reads.</para></summary>
    private void RestartChainIfRepeatable(PlayerSession session, MissionDefinition def)
    {
        if (!def.Repeatable || !IsChainMission(def))
        {
            return;
        }

        session.State.Missions.RemoveAll(m => GetMissionDef(m.MissionId) is { } d && d.ChainId == def.ChainId);
        _repo.SavePlayer(session.State);
    }

    /// <summary>The radio nudge line of a chain's next step — a MISSION call (NpcCallsMode "missions only" still gets it).</summary>
    private const string ChainRadioLineKey = "npc.call.chain_next";

    // ---------------- Dialogue grants ----------------

    /// <summary>Whether a dialogue that hands out <paramref name="missionId"/> should be offered right now:
    /// the mission exists, is not held / done, and (for a chain step) passes the chain rule through THIS
    /// NPC. Keeps a favour dialogue from hijacking the vendor's smalltalk once the job is taken.</summary>
    private bool DialogMissionGrantable(PlayerState p, string npcKey, string missionId)
    {
        if (GetMissionDef(missionId) is not { Active: true } def || p.Missions.Any(m => m.MissionId == missionId))
        {
            return false;
        }

        return !IsChainMission(def)
            || ChainStepAvailable(p, def, CurrentPlaceKey(p), atBoard: false, viaDialog: true, npcKey, out _);
    }

    /// <summary>The mission ids a dialogue's choices hand out (<c>mission:&lt;id&gt;</c> consequences).</summary>
    private static IEnumerable<string> DialogOfferedMissions(DialogDefinition dialog)
    {
        foreach (var node in dialog.Nodes)
        {
            foreach (var choice in node.Choices)
            {
                var parts = choice.Consequence.Split(':');
                if (parts.Length >= 2 && parts[0] == MissionChains.DialogConsequence && parts[1].Length > 0)
                {
                    yield return parts[1];
                }
            }
        }
    }

    /// <summary>Dialogue pick gate (#1212): a dialogue whose choices hand out missions is only offered while at
    /// least one of them can be taken from this NPC — and not again this session after the player declined it
    /// (so "not today" lets the ordinary smalltalk through until the next visit).</summary>
    private bool DialogOffersTakeableMission(PlayerSession session, string npcKey, DialogDefinition dialog, out bool offersAny)
    {
        offersAny = false;
        bool any = false;
        foreach (var id in DialogOfferedMissions(dialog))
        {
            offersAny = true;
            any |= DialogMissionGrantable(session.State, npcKey, id);
        }

        return any && !session.DeclinedMissionDialogs.Contains(dialog.Key);
    }

    /// <summary>The <c>mission:&lt;id&gt;</c> consequence: puts the step straight into the player's log as if
    /// accepted — the conversation IS the acceptance. Silently ignored when the chain rule says no (a replayed
    /// dialogue must not mint duplicates).</summary>
    private void GrantMissionFromDialog(PlayerSession session, ServerNpc npc, string npcKey, string missionId)
    {
        var p = session.State;
        if (GetMissionDef(missionId) is not { Active: true } def)
        {
            return;
        }

        string place = CurrentPlaceKey(p);
        if (IsChainMission(def))
        {
            if (!ChainStepAvailable(p, def, place, atBoard: false, viaDialog: true, npcKey, out _))
            {
                return;
            }
        }
        else if (p.Missions.Any(m => m.MissionId == missionId))
        {
            return;
        }

        // The chain binds to the giver's place; an authored character binds to wherever you met them.
        string acceptedFrom = npcKey.StartsWith("char:", StringComparison.Ordinal) ? place : npcKey[..Math.Max(0, npcKey.IndexOf(':'))];
        p.Missions.Add(NewMissionProgress(p, def, acceptedFrom));
        OnChainStepAccepted(session, def, npcKey, npc.Name);
        Send(session, new MissionResult { Success = true, MissionId = missionId });
        SendMissionList(session);
        ShipAiOnTradeOrMission(session);
        _log.Info($"Player '{p.Name}' took mission '{missionId}' from {npc.Role} '{npc.Name}' in conversation.");
    }

    // ---------------- Procedural big orders ----------------

    /// <summary>Keeps a board's big orders stocked for this player (#1212): every <see cref="BigOrderEveryTurnIns"/>
    /// turn-ins at this board earn one 2-step order (ids <c>…c{2k}</c>/<c>…c{2k+1}</c>); only the current order
    /// is on offer, the next one appears once it is finished. The chain rule gates the steps (known standing,
    /// step 2 after step 1).</summary>
    private void EnsureBigOrders(PlayerState player, string idPrefix, string boardKey, HashSet<string> idSet, string giverName, HashSet<string> currentBoardIds, bool station)
    {
        string orderPrefix = idPrefix + BigOrderSlotInfix;
        int turnIns = 0;
        foreach (var m in player.Missions)
        {
            if (m.Status == MissionStatus.TurnedIn && m.MissionId.StartsWith(idPrefix, StringComparison.Ordinal)
                && !m.MissionId.StartsWith(orderPrefix, StringComparison.Ordinal))
            {
                turnIns++;
            }
        }

        int orders = turnIns / BigOrderEveryTurnIns;
        for (int k = 0; k < orders; k++)
        {
            string idA = orderPrefix + (2 * k);
            string idB = orderPrefix + (2 * k + 1);
            if (!_missionDefs.ContainsKey(idA))
            {
                _missionDefs[idA] = BuildBigOrderMission(idA, idB, boardKey, k, step: 1, giverName, station);
            }

            if (!_missionDefs.ContainsKey(idB))
            {
                _missionDefs[idB] = BuildBigOrderMission(idA, idB, boardKey, k, step: 2, giverName, station);
            }

            idSet.Add(idA);
            idSet.Add(idB);
            currentBoardIds.Add(idA);
            currentBoardIds.Add(idB);

            if (!player.Missions.Any(m => m.MissionId == idB && m.Status == MissionStatus.TurnedIn))
            {
                break; // one big order at a time
            }
        }
    }

    /// <summary>Deterministically coins one step of a board's k-th big order (rng stream "bigorder", stable per
    /// (board, k) like every board mission): step 1 is a doubled delivery, step 2 a large build (settlement) or
    /// a wide survey (station); both pay × <see cref="BigOrderRewardFactor"/> of the template reward.</summary>
    private MissionDefinition BuildBigOrderMission(string idA, string idB, string boardKey, int order, int step, string giverName, bool station)
    {
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"{boardKey}:bigorder:{order}")));
        var tpl = GiverMissionTemplates[rng.Next(GiverMissionTemplates.Length)];
        if (_content.GetItem(tpl.Need) is null || _content.GetItem(tpl.Reward) is null)
        {
            tpl = GiverMissionTemplates[0];
        }

        int second = rng.Next(2); // drawn before the branch so both steps roll the same stream
        string chainId = $"{boardKey}:bigorder:{order}";
        string scope = station ? "station" : "settlement";
        var def = new MissionDefinition
        {
            Id = step == 1 ? idA : idB,
            Source = MissionSource.System,
            GiverName = giverName,
            ChainId = chainId,
            Step = step,
            GiverRole = "quartermaster",
            MinStage = MissionChains.StageKnown,
            Surface = MissionChains.SurfaceBoard,
            OfferAt = station ? MissionChains.OfferAtStation : MissionChains.OfferAtSettlement,
            Active = true,
        };

        if (step == 1)
        {
            def.NameKey = $"mission.{scope}.bigorder_1.title";
            def.DescriptionKey = $"mission.{scope}.bigorder_1.desc";
            def.NextMissionId = idB;
            def.Objectives.Add(new MissionObjective { Type = MissionObjectiveType.Deliver, Target = tpl.Need, Required = tpl.Target * 2 + rng.Next(0, 4) });
            def.Rewards.Add(new ItemAmount(tpl.Reward, Math.Max(1, (int)Math.Round(tpl.RewardN * BigOrderRewardFactor))));
        }
        else
        {
            def.NameKey = $"mission.{scope}.bigorder_2.title";
            def.DescriptionKey = $"mission.{scope}.bigorder_2.desc";
            def.Prerequisites.Add(idA);
            if (station)
            {
                def.Objectives.Add(new MissionObjective { Type = MissionObjectiveType.Scan, Target = "asteroid", Required = 4 });
                def.KnowledgeReward = 4;
            }
            else if (second == 0)
            {
                def.Objectives.Add(new MissionObjective { Type = MissionObjectiveType.Build, Target = "any", Required = 25 });
            }
            else
            {
                def.Objectives.Add(new MissionObjective { Type = MissionObjectiveType.Scan, Target = "creature:any", Required = 4 });
                def.KnowledgeReward = 4;
            }

            def.Rewards.Add(new ItemAmount(tpl.Reward, Math.Max(1, (int)Math.Round(tpl.RewardN * BigOrderRewardFactor))));
            def.Rewards.Add(new ItemAmount("data_fragment", 1));
        }

        return def;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/inspection: the mission ids the player would see as Available right now (board window
    /// refill + chain rule) — the same list <see cref="SendMissionList"/> sends.</summary>
    public IReadOnlyList<string> VisibleMissionIdsForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return Array.Empty<string>();
        }

        var ids = new HashSet<string>();
        EnsureSettlementWindow(session.State, ids);
        EnsureStationWindow(session.State, ids);
        return AvailableMissionsFor(session.State, ids).Select(d => d.Id).ToList();
    }

    /// <summary>Test/inspection: the chain rule's verdict + reason for one mission as the player stands now.</summary>
    public (bool Ok, string Reason) ChainStepAvailableForTest(string playerId, string missionId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || GetMissionDef(missionId) is not { } def)
        {
            return (false, "@srv.mission.unknown");
        }

        bool ok = ChainStepAvailable(session.State, def, CurrentPlaceKey(session.State), AtAnyMissionBoard(session.State), viaDialog: false, null, out var reason);
        return (ok, ok ? string.Empty : reason);
    }

    /// <summary>Test/inspection: a registered mission definition (authored, board-coined or persisted), or null.</summary>
    public MissionDefinition? MissionDefForTest(string missionId) => GetMissionDef(missionId);

    /// <summary>Test hook: run the repeatable-chain restart for a turned-in last step (#1213).</summary>
    public void RestartChainIfRepeatableForTest(PlayerSession session, MissionDefinition def)
        => RestartChainIfRepeatable(session, def);

    /// <summary>Test seam: the place key (settle_/station_) the player currently stands in, or empty.</summary>
    public string CurrentPlaceKeyForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? CurrentPlaceKey(s.State) : string.Empty;
}
