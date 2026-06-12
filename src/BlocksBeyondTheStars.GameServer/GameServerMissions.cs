using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

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

    /// <summary>Called when a player lands on a body, to complete any matching Travel objective (item 31).</summary>
    private void OnPlayerTravelled(PlayerSession session, string bodyId, string bodyName)
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
                if (obj.Type == MissionObjectiveType.Travel
                    && (obj.Target == bodyId || string.Equals(obj.Target, bodyName, StringComparison.OrdinalIgnoreCase)))
                {
                    pr.ObjectiveProgress[i] = obj.Required; // arriving completes a travel objective
                }
            }
        }
    }

    /// <summary>Test hook: simulate a player arriving at a celestial body, driving any Travel objectives.</summary>
    public void SimulateTravelForTest(PlayerSession session, string bodyId, string bodyName)
        => OnPlayerTravelled(session, bodyId, bodyName);

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

        if (IsStationMission(missionId) && !NearSpaceStationMissionBoard(session.State))
        {
            MissionFail(session, missionId, "Visit the station mission board to take this mission.");
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

        // The board's quartermaster remembers that this player took a job from them (item 14).
        if (IsBoardMissionId(missionId))
        {
            RecordMissionAccepted(session.State, missionId, def.GiverName);
        }

        Send(session, new MissionResult { Success = true, MissionId = missionId });
        SendMissionList(session);
        ShipAiOnTradeOrMission(session); // VEGA onboarding: taking a board job counts like a first trade
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

        if (IsStationMission(missionId) && !NearSpaceStationMissionBoard(session.State))
        {
            MissionFail(session, missionId, "Return to the station mission board to turn this in.");
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
            RewardMissionPoster(def, session); // the poster gets a multiple of their stake back + a notice (item 31)
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
                type is not (MissionObjectiveType.Collect or MissionObjectiveType.Mine
                    or MissionObjectiveType.Deliver or MissionObjectiveType.Travel))
            {
                MissionFail(session, "", $"Unsupported objective type '{o.Type}'.");
                return;
            }

            bool valid = type switch
            {
                MissionObjectiveType.Mine => _content.GetBlock(o.Target) is not null,
                MissionObjectiveType.Travel => !string.IsNullOrWhiteSpace(o.Target), // a body id/name
                _ => _content.GetItem(o.Target) is not null,
            };
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

    /// <summary>Stake-with-multiplier payout (item 31): when someone ELSE completes a player's posted mission,
    /// the poster gets a multiple of their staked reward back (created — a return on a fulfilled contract) plus
    /// an in-game notice. Self-completion earns no bonus, so you can't mint items off your own mission.</summary>
    private const float MissionPosterReturn = 1.5f;

    private void RewardMissionPoster(MissionDefinition def, PlayerSession completer)
    {
        if (string.IsNullOrEmpty(def.CreatorId) || def.CreatorId == completer.State.PlayerId)
        {
            return;
        }

        var poster = FindSessionByPlayerId(def.CreatorId);
        if (poster is null)
        {
            return; // offline poster — no payout this time (online-only for now)
        }

        var posterShip = poster.Ships.TryGetValue(poster.ActiveShipId, out var ps) ? ps : _noShip;
        var posterPool = new MaterialPool(_content, poster.State, posterShip);
        var parts = new List<string>();
        foreach (var r in def.Rewards)
        {
            int back = (int)System.Math.Round(r.Count * MissionPosterReturn);
            if (back > 0)
            {
                posterPool.Add(r.Item, back);
                parts.Add($"{back}× {r.Item}");
            }
        }

        _repo.SavePlayer(poster.State);
        SendInventory(poster);
        Send(poster, new ServerMessage
        {
            Text = $"Mission '{def.Title}' completed by {completer.State.Name} — you got back {string.Join(", ", parts)}.",
        });
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

    // ---- Mission-giver boards: never run dry (item 13) ----
    // A giver offers an endless, deterministic sequence of procedural missions (slot 0,1,2,…). A player is
    // always shown the lowest BoardWindow slots they haven't taken yet, so the board never empties; the slots
    // are coined deterministically from (boardKey, slot) so they survive a reload without being persisted.

    private const int BoardWindow = 3;

    private static readonly (string Need, int Target, string Reward, int RewardN)[] GiverMissionTemplates =
    {
        ("iron_ore", 10, "iron_plate", 3),
        ("carbon", 8, "cable", 2),
        ("silicate", 8, "energy_cell_1", 1),
        ("copper_ore", 10, "cable", 3),
        ("crystal", 5, "titanium_plate", 2),
        ("titanium_ore", 6, "titanium_plate", 2),
        ("data_fragment", 3, "medpack", 1),
    };

    /// <summary>A mission-giver's coined name, deterministic per board so it matches the quartermaster NPC.</summary>
    private static string CoinGiverName(string boardKey)
        => BlocksBeyondTheStars.WorldGeneration.NameGenerator.Person(new System.Random(unchecked((int)WorldGenerator.StableHash("giver:" + boardKey))));

    /// <summary>Deterministically coins one board mission for a slot (stable across reloads, no persistence).</summary>
    private MissionDefinition BuildBoardMission(string id, string boardKey, int slot, string giverName)
    {
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash($"{boardKey}:mission:{slot}")));
        var tpl = GiverMissionTemplates[rng.Next(GiverMissionTemplates.Length)];
        // Fall back to the first template if a content item is missing.
        if (_content.GetItem(tpl.Need) is null || _content.GetItem(tpl.Reward) is null)
        {
            tpl = GiverMissionTemplates[0];
        }

        int required = System.Math.Max(3, tpl.Target + rng.Next(-2, 4));
        int rewardN = System.Math.Max(1, tpl.RewardN + rng.Next(0, 2));
        return new MissionDefinition
        {
            Id = id,
            Source = MissionSource.System,
            NameKey = "mission.settlement.gather.title",
            DescriptionKey = "mission.settlement.gather.desc",
            GiverName = giverName,
            Objectives = { new MissionObjective { Type = MissionObjectiveType.Deliver, Target = tpl.Need, Required = required } },
            Rewards = { new ItemAmount(tpl.Reward, rewardN) },
            Active = true,
        };
    }

    /// <summary>Tops a giver board up so the player always sees BoardWindow available missions: regenerates the
    /// defs for any board mission they currently hold (so it survives reload) and the next un-taken slots.
    /// Collects the ids this board currently offers into <paramref name="currentBoardIds"/>.</summary>
    private void EnsureBoardWindow(PlayerState player, string idPrefix, string boardKey, HashSet<string> idSet, string giverName, HashSet<string> currentBoardIds)
    {
        var taken = new HashSet<int>();
        foreach (var m in player.Missions)
        {
            if (!m.MissionId.StartsWith(idPrefix) || !int.TryParse(m.MissionId[idPrefix.Length..], out var s))
            {
                continue;
            }

            taken.Add(s);
            if (m.Status != MissionStatus.TurnedIn && !_missionDefs.ContainsKey(m.MissionId))
            {
                _missionDefs[m.MissionId] = BuildBoardMission(m.MissionId, boardKey, s, giverName); // turn-in-able after reload
            }

            idSet.Add(m.MissionId);
            currentBoardIds.Add(m.MissionId);
        }

        int offered = 0;
        for (int slot = 0; offered < BoardWindow && slot <= taken.Count + BoardWindow; slot++)
        {
            if (taken.Contains(slot))
            {
                continue;
            }

            string id = idPrefix + slot;
            if (!_missionDefs.ContainsKey(id))
            {
                _missionDefs[id] = BuildBoardMission(id, boardKey, slot, giverName);
            }

            idSet.Add(id);
            currentBoardIds.Add(id);
            offered++;
        }
    }

    /// <summary>Keeps the current settlement's mission board stocked for this player.</summary>
    private void EnsureSettlementWindow(PlayerState player, HashSet<string> currentBoardIds)
    {
        if (!_settlementStamped || _settlementRuined || string.IsNullOrEmpty(_settlementName))
        {
            return;
        }

        string prefix = $"settle_{(uint)WorldGenerator.StableHash(_settlementName) % 100000u}_";
        EnsureBoardWindow(player, prefix, _settlementName, _settlementMissionIds, CoinGiverName(_settlementName), currentBoardIds);
    }

    /// <summary>Keeps the boarded station's mission board stocked for this player.</summary>
    private void EnsureStationWindow(PlayerState player, HashSet<string> currentBoardIds)
    {
        if (!_boardedStation.TryGetValue(player.PlayerId, out var stationId))
        {
            return;
        }

        string prefix = $"station_{(uint)WorldGenerator.StableHash(stationId) % 100000u}_";
        EnsureBoardWindow(player, prefix, stationId, _stationMissionIds, CoinGiverName(stationId), currentBoardIds);
    }

    /// <summary>A board (giver) mission id — settlement or station — vs. a system/player mission.</summary>
    private static bool IsBoardMissionId(string id) => id.StartsWith("settle_") || id.StartsWith("station_");

    /// <summary>Seeds a giver board's first window (slots 0..BoardWindow-1) at stamp time, so the board offers
    /// missions even before any player has opened the list; the per-player window then slides as they take them.</summary>
    private void StockBoard(string idPrefix, string boardKey, HashSet<string> idSet, string giverName)
    {
        for (int slot = 0; slot < BoardWindow; slot++)
        {
            string id = idPrefix + slot;
            if (!_missionDefs.ContainsKey(id))
            {
                _missionDefs[id] = BuildBoardMission(id, boardKey, slot, giverName);
            }

            idSet.Add(id);
        }
    }

    private void SendMissionList(PlayerSession session)
    {
        var player = session.State;
        var pool = new MaterialPool(_content, player, _ship);

        // Refill the giver boards the player can reach so they never run dry (item 13); only the boards the
        // player is currently at offer their (board) missions — others (left behind) aren't shown.
        var currentBoardIds = new HashSet<string>();
        EnsureSettlementWindow(player, currentBoardIds);
        EnsureStationWindow(player, currentBoardIds);

        var available = new List<NetMission>();
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

            if (IsBoardMissionId(def.Id))
            {
                RequestBoardMissionText(session, def); // L3: flavour text generates off-thread, refreshes when ready
            }

            available.Add(BuildNetMission(def, null, pool, session.Locale));
        }

        var active = new List<NetMission>();
        foreach (var pr in player.Missions.Where(m => m.Status is MissionStatus.Active or MissionStatus.Completed))
        {
            var def = GetMissionDef(pr.MissionId);
            if (def is not null)
            {
                active.Add(BuildNetMission(def, pr, pool, session.Locale));
            }
        }

        Send(session, new MissionList { Available = available.ToArray(), Active = active.ToArray() });

        // Opening a settlement/station mission board greets the nearby quartermaster (item 15); a no-op aboard
        // ship or anywhere no quartermaster is in reach.
        GreetBoardQuartermaster(session);
    }

    private NetMission BuildNetMission(MissionDefinition def, MissionProgress? pr, MaterialPool pool, string locale)
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

        // L3: a generated flavour text (in the player's locale) overrides the static board keys.
        var llmText = IsBoardMissionId(def.Id) ? BoardMissionText(def.Id, locale) : null;

        return new NetMission
        {
            Id = def.Id,
            Source = def.Source.ToString(),
            // System/admin missions carry localization keys; player missions and L3 texts carry free text.
            Title = llmText?.Title ?? (isPlayer ? def.Title : def.NameKey),
            Description = llmText?.Description ?? (isPlayer ? def.Description : def.DescriptionKey),
            FreeText = isPlayer || llmText is not null,
            Status = pr?.Status.ToString() ?? "Available",
            Objectives = objectives,
            Rewards = def.Rewards.Select(r => new NetReward { Item = r.Item, Count = r.Count }).ToArray(),
            GiverName = def.GiverName,
        };
    }

    private void MissionFail(PlayerSession session, string missionId, string reason)
        => Send(session, new MissionResult { Success = false, MissionId = missionId, Reason = reason });

    /// <summary>The board (giver) mission ids currently available to a player — runs the giver window so it
    /// reflects the never-run-dry refill (test/inspection).</summary>
    public IReadOnlyList<string> AvailableBoardMissions(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return System.Array.Empty<string>();
        }

        var ids = new HashSet<string>();
        EnsureSettlementWindow(session.State, ids);
        EnsureStationWindow(session.State, ids);
        return ids.Where(id => session.State.Missions.All(m => m.MissionId != id)).ToList();
    }

    /// <summary>The mission-giver name coined for a mission (test/inspection); empty for non-board missions.</summary>
    public string MissionGiverName(string missionId) => GetMissionDef(missionId)?.GiverName ?? string.Empty;

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
