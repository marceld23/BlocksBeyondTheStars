using Spacecraft.Networking.Messages;
using Spacecraft.Persistence;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Missions;
using Spacecraft.Shared.State;

namespace Spacecraft.GameServer;

/// <summary>
/// Mission system (technical requirements / `anf_mission_editor.md`, no AI in this MVP):
/// system + player/admin-created missions, server-validated objectives (mine/collect/
/// deliver), a reward depot for player-created missions, accept/track/turn-in flow.
/// </summary>
public sealed partial class GameServer
{
    private const string DepotPlanet = "depot";
    private readonly Dictionary<string, MissionDefinition> _missionDefs = new();

    private void BuildMissions()
    {
        _missionDefs.Clear();
        foreach (var m in _content.Missions.Values)
        {
            _missionDefs[m.Id] = m; // system missions
        }

        foreach (var m in _repo.ListMissions())
        {
            _missionDefs[m.Id] = m; // player/admin-created, persisted
        }
    }

    private MissionDefinition? GetMissionDef(string id) => _missionDefs.TryGetValue(id, out var m) ? m : null;

    /// <summary>Called when a player mines a block, to advance any matching Mine objectives.</summary>
    private void OnBlockMined(PlayerSession session, string blockKey)
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
                if (obj.Type == MissionObjectiveType.Mine && obj.Target == blockKey && pr.ObjectiveProgress[i] < obj.Required)
                {
                    pr.ObjectiveProgress[i]++;
                }
            }
        }
    }

    private void HandleAcceptMission(PlayerSession session, string missionId)
    {
        var def = GetMissionDef(missionId);
        if (def is null || !def.Active)
        {
            MissionFail(session, missionId, "Unknown or inactive mission.");
            return;
        }

        if (IsSettlementMission(missionId) && !NearSettlementMissionBoard(session.State))
        {
            MissionFail(session, missionId, "Visit the settlement's mission board to take this mission.");
            return;
        }

        if (session.State.Missions.Any(m => m.MissionId == missionId))
        {
            MissionFail(session, missionId, "Mission already accepted.");
            return;
        }

        session.State.Missions.Add(new MissionProgress
        {
            MissionId = missionId,
            Status = MissionStatus.Active,
            ObjectiveProgress = Enumerable.Repeat(0, def.Objectives.Count).ToList(),
        });

        Send(session, new MissionResult { Success = true, MissionId = missionId });
        SendMissionList(session);
    }

    private void HandleTurnInMission(PlayerSession session, string missionId)
    {
        var pr = session.State.Missions.FirstOrDefault(m => m.MissionId == missionId && m.Status != MissionStatus.TurnedIn);
        var def = GetMissionDef(missionId);
        if (pr is null || def is null)
        {
            MissionFail(session, missionId, "Mission is not active.");
            return;
        }

        if (IsSettlementMission(missionId) && !NearSettlementMissionBoard(session.State))
        {
            MissionFail(session, missionId, "Return to the settlement's mission board to turn this in.");
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);

        for (int i = 0; i < def.Objectives.Count; i++)
        {
            var obj = def.Objectives[i];
            int have = obj.Type switch
            {
                MissionObjectiveType.Collect or MissionObjectiveType.Deliver => pool.Count(obj.Target),
                _ => i < pr.ObjectiveProgress.Count ? pr.ObjectiveProgress[i] : 0,
            };

            if (have < obj.Required)
            {
                MissionFail(session, missionId, "Objectives are not complete yet.");
                return;
            }
        }

        // Consume Deliver items.
        foreach (var obj in def.Objectives.Where(o => o.Type == MissionObjectiveType.Deliver))
        {
            pool.Remove(new[] { new ItemAmount(obj.Target, obj.Required) });
        }

        // Pay the reward.
        if (def.Source == MissionSource.Player)
        {
            PayoutDepot(missionId, pool);
        }
        else
        {
            foreach (var reward in def.Rewards)
            {
                pool.Add(reward.Item, reward.Count);
            }
        }

        // Finalize.
        if (def.Source == MissionSource.Player)
        {
            session.State.Missions.Remove(pr);
            _missionDefs.Remove(missionId);
            _repo.DeleteMission(missionId);
        }
        else if (def.Repeatable)
        {
            session.State.Missions.Remove(pr); // can be accepted again
        }
        else
        {
            pr.Status = MissionStatus.TurnedIn;
        }

        _repo.SavePlayer(session.State);
        Send(session, new MissionResult { Success = true, MissionId = missionId });
        SendInventory(session);
        SendMissionList(session);
        _log.Info($"Player '{session.State.Name}' turned in mission '{missionId}'.");
    }

    private void HandleCreateMission(PlayerSession session, CreateMissionIntent intent)
    {
        var objectives = new List<MissionObjective>();
        foreach (var o in intent.Objectives)
        {
            if (!Enum.TryParse<MissionObjectiveType>(o.Type, ignoreCase: true, out var type) ||
                type is not (MissionObjectiveType.Collect or MissionObjectiveType.Mine or MissionObjectiveType.Deliver))
            {
                MissionFail(session, "", $"Unsupported objective type '{o.Type}'.");
                return;
            }

            bool valid = type == MissionObjectiveType.Mine
                ? _content.GetBlock(o.Target) is not null
                : _content.GetItem(o.Target) is not null;
            if (!valid || o.Required < 1)
            {
                MissionFail(session, "", $"Invalid objective target '{o.Target}'.");
                return;
            }

            objectives.Add(new MissionObjective { Type = type, Target = o.Target, Required = o.Required });
        }

        if (objectives.Count == 0)
        {
            MissionFail(session, "", "A mission needs at least one objective.");
            return;
        }

        var rewards = new List<ItemAmount>();
        foreach (var r in intent.Rewards)
        {
            if (_content.GetItem(r.Item) is null || r.Count < 1)
            {
                MissionFail(session, "", $"Invalid reward item '{r.Item}'.");
                return;
            }

            rewards.Add(new ItemAmount(r.Item, r.Count));
        }

        // The creator must own and deposit the reward items.
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!pool.Has(rewards))
        {
            MissionFail(session, "", "You do not have the reward items to deposit.");
            return;
        }

        var id = "pm_" + Guid.NewGuid().ToString("N");
        var def = new MissionDefinition
        {
            Id = id,
            Source = MissionSource.Player,
            CreatorId = session.State.PlayerId,
            Title = intent.Title,
            Description = intent.Description,
            Objectives = objectives,
            Rewards = rewards,
            Active = true,
            Repeatable = false,
        };

        pool.Remove(rewards);
        _repo.SaveContainer(new StoredContainer
        {
            Id = "depot_" + id,
            Planet = DepotPlanet,
            Kind = "reward_depot",
            Position = Vector3i.Zero,
            Items = rewards.Select(r => new ItemStack(r.Item, r.Count)).ToList(),
        });
        _repo.SaveMission(def);
        _missionDefs[id] = def;

        Send(session, new MissionResult { Success = true, MissionId = id });
        SendInventory(session);
        SendMissionList(session);
        _log.Info($"Player '{session.State.Name}' created mission '{id}'.");
    }

    private void PayoutDepot(string missionId, MaterialPool pool)
    {
        var depot = _repo.ListContainers(DepotPlanet).FirstOrDefault(c => c.Id == "depot_" + missionId);
        if (depot is null)
        {
            return;
        }

        foreach (var item in depot.Items)
        {
            pool.Add(item.Item, item.Count);
        }

        _repo.DeleteContainer(depot.Id);
    }

    private void SendMissionList(PlayerSession session)
    {
        var player = session.State;
        var pool = new MaterialPool(_content, player, _ship);

        var available = new List<NetMission>();
        foreach (var def in _missionDefs.Values)
        {
            if (!def.Active || player.Missions.Any(m => m.MissionId == def.Id))
            {
                continue; // already accepted / turned in (non-repeatable) is hidden
            }

            available.Add(BuildNetMission(def, null, pool));
        }

        var active = new List<NetMission>();
        foreach (var pr in player.Missions.Where(m => m.Status is MissionStatus.Active or MissionStatus.Completed))
        {
            var def = GetMissionDef(pr.MissionId);
            if (def is not null)
            {
                active.Add(BuildNetMission(def, pr, pool));
            }
        }

        Send(session, new MissionList { Available = available.ToArray(), Active = active.ToArray() });
    }

    private static NetMission BuildNetMission(MissionDefinition def, MissionProgress? pr, MaterialPool pool)
    {
        var objectives = new NetMissionObjective[def.Objectives.Count];
        for (int i = 0; i < def.Objectives.Count; i++)
        {
            var obj = def.Objectives[i];
            int progress = obj.Type switch
            {
                MissionObjectiveType.Collect or MissionObjectiveType.Deliver => System.Math.Min(obj.Required, pool.Count(obj.Target)),
                _ => pr is not null && i < pr.ObjectiveProgress.Count ? pr.ObjectiveProgress[i] : 0,
            };

            objectives[i] = new NetMissionObjective
            {
                Type = obj.Type.ToString(),
                Target = obj.Target,
                Required = obj.Required,
                Progress = progress,
            };
        }

        bool isPlayer = def.Source == MissionSource.Player;
        return new NetMission
        {
            Id = def.Id,
            Source = def.Source.ToString(),
            // System/admin missions carry localization keys; player missions carry free text.
            Title = isPlayer ? def.Title : def.NameKey,
            Description = isPlayer ? def.Description : def.DescriptionKey,
            Status = pr?.Status.ToString() ?? "Available",
            Objectives = objectives,
            Rewards = def.Rewards.Select(r => new NetReward { Item = r.Item, Count = r.Count }).ToArray(),
        };
    }

    private void MissionFail(PlayerSession session, string missionId, string reason)
        => Send(session, new MissionResult { Success = false, MissionId = missionId, Reason = reason });

    /// <summary>Accepts a mission for a player (used by local play / tests).</summary>
    public void AcceptMission(string playerId, string missionId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleAcceptMission(session, missionId);
        }
    }

    /// <summary>Turns in a mission for a player (used by local play / tests).</summary>
    public void TurnInMission(string playerId, string missionId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            HandleTurnInMission(session, missionId);
        }
    }
}
