// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Bounty missions (#730/#731): quest givers react to the bandits around them. A settlement board on a
/// world with an uncleared bandit camp offers "drive the bandits out of their camp" (accepting reveals
/// the camp on the planet map); a station board in pirate space offers "drive off the raider ship"
/// (holding it guarantees the next flight's ambush roll, so the quest is always completable). Bounty
/// offers are coined deterministically like the gather board missions, but a def is persisted the
/// moment a player accepts it — progress is event-driven (kills happen far from the board), so the
/// definition must survive a restart even when no one is standing at the board to re-coin it.
/// </summary>
public sealed partial class GameServer
{
    private const string CampBountyInfix = "_bounty_";  // settle_{hash}_bounty_{campKey}
    private const string ShipBountySuffix = "_bounty";  // station_{hash}_bounty

    /// <summary>Defeat-objective target keys (the "what counts" discriminator at the kill sites).</summary>
    private const string DefeatTargetCamp = "bandit_camp";
    private const string DefeatTargetShip = "bandit_ship";
    private const string DefeatTargetBandit = "bandit";

    /// <summary>The camp key inside a settlement camp-bounty mission id, if it is one.</summary>
    private static bool TryCampBountyKey(string missionId, out string campKey)
    {
        int i = missionId.IndexOf(CampBountyInfix, System.StringComparison.Ordinal);
        if (missionId.StartsWith("settle_", System.StringComparison.Ordinal) && i > 0)
        {
            campKey = missionId[(i + CampBountyInfix.Length)..];
            return campKey.Length > 0;
        }

        campKey = string.Empty;
        return false;
    }

    /// <summary>A station raider-bounty mission id.</summary>
    private static bool IsShipBountyMissionId(string missionId)
        => missionId.StartsWith("station_", System.StringComparison.Ordinal)
           && missionId.EndsWith(ShipBountySuffix, System.StringComparison.Ordinal);

    private static bool IsBountyMissionId(string missionId)
        => IsShipBountyMissionId(missionId) || TryCampBountyKey(missionId, out _);

    /// <summary>Coins the camp bounty a settlement board offers for one uncleared camp. Deterministic per
    /// (board, camp) like every board mission; rewards clearly beat the gather jobs — camps bite back.</summary>
    private MissionDefinition BuildCampBountyMission(string id, string boardKey, string campKey, string giverName)
    {
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"{boardKey}:bounty:{campKey}")));
        return new MissionDefinition
        {
            Id = id,
            Source = MissionSource.System,
            NameKey = "mission.bounty.camp.title",
            DescriptionKey = "mission.bounty.camp.desc",
            GiverName = giverName,
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Defeat, Target = DefeatTargetCamp, Required = 1 } },
            Rewards =
            {
                new ItemAmount("titanium_plate", 3 + rng.Next(0, 3)),
                new ItemAmount("gold_ingot", 1 + rng.Next(0, 2)),
            },
            Active = true,
        };
    }

    /// <summary>Coins the raider bounty a station board offers in pirate space. Repeatable — raiders keep
    /// coming as long as the system stays pirate country.</summary>
    private MissionDefinition BuildShipBountyMission(string id, string boardKey, string giverName)
    {
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"{boardKey}:bounty:ship")));
        return new MissionDefinition
        {
            Id = id,
            Source = MissionSource.System,
            NameKey = "mission.bounty.ship.title",
            DescriptionKey = "mission.bounty.ship.desc",
            GiverName = giverName,
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Defeat, Target = DefeatTargetShip, Required = 1 } },
            Rewards =
            {
                new ItemAmount("data_fragment", 2 + rng.Next(0, 2)),
                new ItemAmount("titanium_plate", 3 + rng.Next(0, 3)),
            },
            Active = true,
            Repeatable = true,
        };
    }

    /// <summary>Adds one camp bounty per uncleared camp on this world to a settlement board's offers.
    /// Cleared camps stop being offered on their own (and a held bounty completes via the clear).</summary>
    private void EnsureCampBounties(string idPrefix, SettlementInstance s, HashSet<string> currentBoardIds)
    {
        if (!BanditsActive)
        {
            return;
        }

        foreach (var camp in _banditCamps)
        {
            if (camp.Cleared)
            {
                continue;
            }

            string id = idPrefix + "bounty_" + camp.Key;
            if (!_missionDefs.ContainsKey(id))
            {
                _missionDefs[id] = BuildCampBountyMission(id, s.Name, camp.Key, CoinGiverName(s.Name));
            }

            s.MissionIds.Add(id);
            currentBoardIds.Add(id);
        }
    }

    /// <summary>Whether a station's board offers the raider bounty: raiders must be able to spawn (rules,
    /// story) AND the station's system must roll as pirate space AND the world's Danger option must not
    /// have disabled ambushes — otherwise the quest could never be completed.</summary>
    private bool ShipBountyOffered(string stationId)
        => BanditShipsAllowed
           && _meta.Description.Danger.DangerFactor() > 0
           && BanditSystem(_galaxy.FindBody(stationId)?.SystemId ?? string.Empty);

    /// <summary>Adds the raider bounty to a station board's offers when the system qualifies.</summary>
    private void EnsureStationBounty(string idPrefix, string stationId, HashSet<string> currentBoardIds)
    {
        if (!ShipBountyOffered(stationId))
        {
            return;
        }

        string id = idPrefix + "bounty";
        if (!_missionDefs.ContainsKey(id))
        {
            _missionDefs[id] = BuildShipBountyMission(id, stationId, CoinGiverName(stationId));
        }

        _stationMissionIds.Add(id);
        currentBoardIds.Add(id);
    }

    /// <summary>Accept-time bounty bookkeeping: persist the def (progress is event-driven far from the
    /// board, so it must survive restarts without the board's re-coining path) and reveal a camp bounty's
    /// camp on the planet map — without a marker, finding it would be pure luck.</summary>
    private void OnBountyAccepted(PlayerSession session, MissionDefinition def)
    {
        if (!IsBountyMissionId(def.Id))
        {
            return;
        }

        _repo.SaveMission(def);
        if (TryCampBountyKey(def.Id, out var campKey))
        {
            string revealKey = _world.LocationId + "|banditcamp:" + campKey;
            if (!_meta.RevealedPois.Contains(revealKey))
            {
                RevealPoi(revealKey);
            }

            // Already cleared by someone else between stocking and accepting? Complete on the spot.
            SyncCampBountyProgress(session);
        }
    }

    /// <summary>Advances any active Defeat objectives matching the target key (mirrors OnBlockMined).</summary>
    private void OnMissionDefeat(PlayerSession session, string targetKey)
    {
        foreach (var pr in session.State.Missions)
        {
            if (pr.Status != MissionStatus.Active)
            {
                continue;
            }

            var def = GetMissionDef(pr.MissionId);
            if (def is null)
            {
                continue;
            }

            for (int i = 0; i < def.Objectives.Count && i < pr.ObjectiveProgress.Count; i++)
            {
                var obj = def.Objectives[i];
                if (obj.Type == MissionObjectiveType.Defeat && obj.Target == targetKey && pr.ObjectiveProgress[i] < obj.Required)
                {
                    pr.ObjectiveProgress[i]++;
                }
            }
        }
    }

    /// <summary>A camp was cleared: every online player holding its bounty gets the objective completed —
    /// co-op friendly, matching the world-global cleared flag (whoever lands the last blow, the crew wins).</summary>
    private void OnCampBountyCleared(BanditCampInstance camp)
    {
        string suffix = CampBountyInfix + camp.Key;
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined)
            {
                continue;
            }

            bool changed = false;
            foreach (var pr in session.State.Missions)
            {
                if (pr.Status != MissionStatus.Active
                    || !pr.MissionId.StartsWith("settle_", System.StringComparison.Ordinal)
                    || !pr.MissionId.EndsWith(suffix, System.StringComparison.Ordinal))
                {
                    continue;
                }

                changed |= CompleteDefeatObjectives(pr, DefeatTargetCamp);
            }

            if (changed)
            {
                _repo.SavePlayer(session.State);
                SendMissionList(session); // an open mission tab updates live
            }
        }
    }

    /// <summary>Completes camp-bounty objectives whose camp is already cleared on the active world — covers
    /// players who were offline (or elsewhere) when the last guard fell. Cheap; runs per mission-list send
    /// and before a turn-in check.</summary>
    private void SyncCampBountyProgress(PlayerSession session)
    {
        foreach (var pr in session.State.Missions)
        {
            if (pr.Status != MissionStatus.Active || !TryCampBountyKey(pr.MissionId, out var campKey))
            {
                continue;
            }

            foreach (var camp in _banditCamps)
            {
                if (camp.Key == campKey && camp.Cleared)
                {
                    CompleteDefeatObjectives(pr, DefeatTargetCamp);
                    break;
                }
            }
        }
    }

    /// <summary>Sets every matching Defeat objective of one mission to its required count.</summary>
    private bool CompleteDefeatObjectives(MissionProgress pr, string targetKey)
    {
        var def = GetMissionDef(pr.MissionId);
        if (def is null)
        {
            return false;
        }

        bool changed = false;
        for (int i = 0; i < def.Objectives.Count && i < pr.ObjectiveProgress.Count; i++)
        {
            var obj = def.Objectives[i];
            if (obj.Type == MissionObjectiveType.Defeat && obj.Target == targetKey && pr.ObjectiveProgress[i] < obj.Required)
            {
                pr.ObjectiveProgress[i] = obj.Required;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>Whether any joined pilot in this instance holds an active raider bounty — their accepted
    /// job guarantees the ambush roll (an encounter the player asked for must actually happen).</summary>
    private bool AnyPilotHoldsShipBounty(SpaceInstance instance)
    {
        foreach (var playerId in instance.Players)
        {
            if (FindSessionByPlayerId(playerId) is not { } s || !s.Joined)
            {
                continue;
            }

            foreach (var pr in s.State.Missions)
            {
                if (pr.Status == MissionStatus.Active && IsShipBountyMissionId(pr.MissionId))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ---------------- Test hooks ----------------

    /// <summary>Test/util: whether a station's board would offer the raider bounty.</summary>
    public bool ShipBountyOfferedForTest(string stationId) => ShipBountyOffered(stationId);

    /// <summary>Test/util: puts an active raider bounty in the player's log exactly as accepting it at a
    /// station board would (def registered + progress active), skipping the boarding trip. Returns the id.</summary>
    public string GrantShipBountyForTest(string playerId, string stationKey = "test_station")
    {
        string id = $"station_{(uint)WorldGenerator.StableHash(stationKey) % 100000u}_bounty";
        if (!_missionDefs.ContainsKey(id))
        {
            _missionDefs[id] = BuildShipBountyMission(id, stationKey, CoinGiverName(stationKey));
        }

        if (FindSessionByPlayerId(playerId) is { } session
            && session.State.Missions.All(m => m.MissionId != id))
        {
            session.State.Missions.Add(new MissionProgress
            {
                MissionId = id,
                Status = MissionStatus.Active,
                ObjectiveProgress = Enumerable.Repeat(0, _missionDefs[id].Objectives.Count).ToList(),
            });
        }

        return id;
    }

    /// <summary>Test/util: the star-system id of the player's current space instance (empty = not in space).</summary>
    public string SpaceSystemIdForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var instance)
            ? SystemIdOfInstance(instance)
            : string.Empty;
}
