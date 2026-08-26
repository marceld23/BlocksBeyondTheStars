// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The SPS relay network (#1125, Track F — F-1). A commissioned player station can be converted into a
/// <b>relay</b> by pouring the data-driven bill of materials into it (bulk metals + reactor fuel + circuit
/// boards — the late-game ore chain's consumer). Contributions are co-op: any player may deliver, in person,
/// at the station. Two completed relays whose systems are within the definition's link range form a
/// <b>jump lane</b>: travel between those two systems needs no jump generator. Lanes are never persisted —
/// they re-derive from the completed relays' systems on every start, so the data file can be rebalanced
/// without a migration. The galaxy-growth hook + star-map rendering + epilogue insights are F-2 (#1126).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Live jump lanes as unordered system-id pairs (A &lt; B ordinally, deduped). Recomputed on
    /// start (after stations + galaxy load) and whenever a relay completes.</summary>
    private readonly List<(string A, string B)> _relayLanes = new();

    /// <summary>Systems holding a completed relay (F-2 world effect): a relay draws traders — the ambient
    /// traffic level reads one step busier there. Derived alongside the lanes, never persisted.</summary>
    private readonly HashSet<string> _relaySystems = new();

    /// <summary>Speaks one of VEGA's relay-network insights (F-2) exactly once per save — the epilogue's
    /// promise made audible as each stage of the rebuild actually happens. The once-guard persists in
    /// <see cref="WorldMetadata.RelayInsights"/> (the caller saves metadata); kind 2 lands in the story log.</summary>
    private void SpeakRelayInsightOnce(string stage, string textKey)
    {
        if (_meta.RelayInsights.Contains(stage))
        {
            return;
        }

        _meta.RelayInsights.Add(stage);
        SpeakVegaLineToAll(textKey);
    }

    /// <summary>The relay meter for a station, creating the record on first touch (additive metadata).</summary>
    private RelayStationRecord RelayRecordFor(string stationId)
    {
        var rec = _meta.Relays.FirstOrDefault(r => r.StationId == stationId);
        if (rec is null)
        {
            rec = new RelayStationRecord { StationId = stationId };
            _meta.Relays.Add(rec);
        }

        return rec;
    }

    /// <summary>The star system a station's relay counts for — derived from its host body (the body whose
    /// space instance it floats in), NOT from <see cref="AddStationBodyToGalaxy"/>'s star-map entry, which
    /// files stations under the save's default system on multi-world servers.</summary>
    private string RelaySystemOf(string stationId)
    {
        if (_stationHostBody.TryGetValue(stationId, out var host) && !string.IsNullOrEmpty(host)
            && _galaxy?.FindBody(host)?.SystemId is { Length: > 0 } viaHost)
        {
            return viaHost;
        }

        return _galaxy?.FindBody(stationId)?.SystemId ?? string.Empty;
    }

    /// <summary>Whether the player is AT the station: aboard it, floating in its space instance, or at
    /// least in its star system. Contributions are delivered in person — never beamed across the galaxy.
    /// (System-level rather than body-level on the ground: a fresh ship's location can be a planet-TYPE
    /// key rather than a body id, so an exact host-body comparison would wrongly reject valid deliveries.)</summary>
    private bool IsAtRelayStation(PlayerSession session, string stationId)
    {
        string playerId = session.State.PlayerId;
        if (_boardedStation.TryGetValue(playerId, out var aboard) && aboard == stationId)
        {
            return true;
        }

        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
            && inst.Structures.ContainsKey(stationId))
        {
            return true;
        }

        string sys = RelaySystemOf(stationId);
        return !string.IsNullOrEmpty(sys) && _galaxy?.FindBody(session.CurrentLocationId)?.SystemId == sys;
    }

    /// <summary>A player pours items into a station's relay conversion (#1125). Clamps to what is missing
    /// and what the contributor holds; creative/instant-build fills the line for free (the station-deploy
    /// convention). Completing the last line makes the station a relay and recomputes the jump lanes.</summary>
    private void HandleContributeRelay(PlayerSession session, ContributeRelayIntent intent)
    {
        var def = _content.Relay;
        if (def is null)
        {
            Reject(session, "relay", "@srv.relay.disabled");
            return;
        }

        if (!_playerStationCells.TryGetValue(intent.StationId, out var station) || !station.Boardable)
        {
            Reject(session, "relay", "@srv.relay.no_station");
            return;
        }

        var rec = RelayRecordFor(intent.StationId);
        if (rec.Completed)
        {
            Reject(session, "relay", "@srv.relay.already_done");
            return;
        }

        if (!IsAtRelayStation(session, intent.StationId))
        {
            Reject(session, "relay", "@srv.relay.too_far");
            return;
        }

        var line = def.Costs.FirstOrDefault(c => c.Item == intent.Item);
        if (line is null)
        {
            Reject(session, "relay", "@srv.relay.wrong_item");
            return;
        }

        rec.Contributed.TryGetValue(line.Item, out int soFar);
        int missing = line.Count - soFar;
        if (missing <= 0)
        {
            Reject(session, "relay", "@srv.relay.line_full");
            return;
        }

        Serve(session); // _ship = this player's ship (the pool may draw from its cargo, like crafting)
        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        int give = free ? missing : Math.Min(missing, Math.Min(Math.Max(0, intent.Count), pool.Count(line.Item)));
        if (give <= 0)
        {
            Reject(session, "relay", "@srv.relay.nothing_held");
            return;
        }

        if (!free)
        {
            pool.Remove(new[] { new ItemAmount(line.Item, give) });
            SendInventory(session);
        }

        rec.Contributed[line.Item] = soFar + give;
        OnRelayContributed(session, line.Item, give); // #1213: drives the survey orders' Contribute objective
        bool completed = def.Costs.All(c => rec.Contributed.TryGetValue(c.Item, out int n) && n >= c.Count);
        if (completed)
        {
            rec.Completed = true;
        }

        _repo.SaveMetadata(_meta); // the relay meter is world-shared state — persist every accepted delivery

        if (completed)
        {
            OnAchievementRelayCommissioned(session);  // "Relay Engineer" (#1125)
            RecordStoryMilestone("relay:first");      // the save's first relay advances the arc
            foreach (var s in _sessions.Values.Where(x => x.Joined))
            {
                Send(s, new ServerMessage
                {
                    Text = Localize(s.Locale, "srv.relay.completed").Replace("{name}", station.Name),
                });
            }

            SpeakRelayInsightOnce("relay", "vega.relay.first");
            RecomputeRelayLanes(announce: true, source: session);
            _repo.SaveMetadata(_meta); // the insight/lane once-guards changed above — persist them too
            _log.Info($"Station '{station.Name}' ({station.Id}) is now an SPS relay.");
        }
        else
        {
            Send(session, new ServerMessage
            {
                Text = Localize(session.Locale, "srv.relay.contributed")
                    .Replace("{count}", give.ToString())
                    .Replace("{item}", Localize(session.Locale, "item." + line.Item + ".name")),
            });
        }

        BroadcastRelayNetwork();
    }

    /// <summary>Re-derives the jump lanes from the completed relays' systems: every unordered pair of
    /// distinct relay systems within the definition's link range is a lane. With <paramref name="announce"/>
    /// each NEWLY formed lane is broadcast (and the save's first lane advances the arc); a new lane also
    /// pushes the frontier (F-2): a lane INTO an edge system grows the galaxy there, credited to
    /// <paramref name="source"/> (the contributor who completed the relay).</summary>
    private void RecomputeRelayLanes(bool announce = false, PlayerSession? source = null)
    {
        var before = announce ? new HashSet<(string, string)>(_relayLanes) : null;
        _relayLanes.Clear();
        _relaySystems.Clear();

        var def = _content.Relay;
        if (def is null || _galaxy is null)
        {
            return;
        }

        var systems = _meta.Relays
            .Where(r => r.Completed)
            .Select(r => RelaySystemOf(r.StationId))
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .Select(id => _galaxy.Systems.FirstOrDefault(s => s.Id == id))
            .Where(s => s is not null)
            .ToList();

        for (int i = 0; i < systems.Count; i++)
        {
            for (int j = i + 1; j < systems.Count; j++)
            {
                float dx = systems[i]!.MapX - systems[j]!.MapX;
                float dy = systems[i]!.MapY - systems[j]!.MapY;
                if (dx * dx + dy * dy > def.LinkRange * def.LinkRange)
                {
                    continue;
                }

                var lane = string.CompareOrdinal(systems[i]!.Id, systems[j]!.Id) < 0
                    ? (systems[i]!.Id, systems[j]!.Id)
                    : (systems[j]!.Id, systems[i]!.Id);
                if (!_relayLanes.Contains(lane))
                {
                    _relayLanes.Add(lane);
                }
            }
        }

        foreach (var sys in systems)
        {
            _relaySystems.Add(sys!.Id); // relay systems read one traffic level busier (world effect)
        }

        if (before is null)
        {
            return;
        }

        foreach (var lane in _relayLanes.Where(l => !before.Contains(l)))
        {
            RecordStoryMilestone("relay:lane"); // the save's FIRST lane advances the arc (once-key dedupes)
            SpeakRelayInsightOnce("lane", "vega.relay.lane");
            if (source is not null)
            {
                OnAchievementLaneLinked(source); // "Network Weaver" — credited to the completing contributor
            }

            string nameA = _galaxy.Systems.FirstOrDefault(s => s.Id == lane.A)?.Name ?? lane.A;
            string nameB = _galaxy.Systems.FirstOrDefault(s => s.Id == lane.B)?.Name ?? lane.B;
            foreach (var s in _sessions.Values.Where(x => x.Joined))
            {
                Send(s, new ServerMessage
                {
                    Text = Localize(s.Locale, "srv.relay.lane").Replace("{a}", nameA).Replace("{b}", nameB),
                });
            }

            _log.Info($"Jump lane established: {lane.A} <-> {lane.B}.");

            // F-2: the network pushes the frontier — a lane INTO an edge system grows the galaxy there.
            // MaybeGrowGalaxy self-guards (GalaxyGrowth option, edge check, soft cap), and a lane forms
            // exactly once per pair, so this is a genuine "newly happened" trigger like the travel funnels.
            if (source is not null)
            {
                int beforeGrowth = _galaxy.Systems.Count;
                MaybeGrowGalaxy(source, lane.A);
                MaybeGrowGalaxy(source, lane.B);
                if (_galaxy.Systems.Count > beforeGrowth)
                {
                    SpeakRelayInsightOnce("growth", "vega.relay.growth");
                }
            }
        }
    }

    /// <summary>Whether a jump lane links these two systems — the jump-generator exemption used by both
    /// travel paths. Null/empty on either side (deep space, unknown origin) never matches.</summary>
    /// <summary>Whether any boardable station still has an unfinished relay — i.e. whether a Contribute
    /// objective (#1213) could be satisfied anywhere at all. No stations = nothing to contribute to.</summary>
    private bool AnyRelayOpen()
        => _playerStationCells.Values.Any(s => s.Boardable && !RelayRecordFor(s.Id).Completed);

    /// <summary>Whether the galaxy still holds a system the relay network has not reached — the promise the
    /// "survey an unlinked system" step makes (#1213).</summary>
    private bool AnyUnlinkedSystem()
        => _galaxy is not null && _galaxy.Systems.Any(s => !_relaySystems.Contains(s.Id));

    /// <summary>The location-id prefix of a boarded station's own void world (<c>station:&lt;id&gt;</c>) —
    /// the value <see cref="PlayerState.CurrentLocationId"/> carries while a player stands on a station.</summary>
    private const string StationLocationIdPrefix = "station:";

    /// <summary>The star system a location id belongs to (#1291). A station board's location id is
    /// <c>station:&lt;id&gt;</c>, which <c>Galaxy.FindBody</c> cannot resolve — it has to go through the same
    /// station→system mapping the relay meter uses; anything else is a plain celestial body.</summary>
    private string SystemOfLocation(string locationId)
    {
        if (string.IsNullOrEmpty(locationId))
        {
            return string.Empty;
        }

        return locationId.StartsWith(StationLocationIdPrefix, System.StringComparison.Ordinal)
            ? RelaySystemOf(locationId[StationLocationIdPrefix.Length..])
            : _galaxy?.FindBody(locationId)?.SystemId ?? string.Empty;
    }

    /// <summary>Whether arriving at <paramref name="bodyId"/> satisfies an "unlinked system" travel objective
    /// (#1213): the body's system carries no completed relay, and is not the system the job was taken in — so
    /// the order actually sends the player somewhere new rather than completing on the spot.
    /// <para>#1291: the "taken in" system comes from <see cref="MissionProgress.AcceptedSystemId"/>, which is
    /// resolved at accept time. Deriving it from the accepted BODY silently never fired, because the chain is
    /// only takeable at a station board, whose location id no body lookup resolves. Rows written before that
    /// field existed fall back to the old body-derived value.</para></summary>
    private bool ArrivedInUnlinkedSystem(string bodyId, MissionProgress pr)
    {
        string arrived = _galaxy?.FindBody(bodyId)?.SystemId ?? string.Empty;
        if (string.IsNullOrEmpty(arrived) || _relaySystems.Contains(arrived))
        {
            return false;
        }

        string from = !string.IsNullOrEmpty(pr.AcceptedSystemId)
            ? pr.AcceptedSystemId
            : string.IsNullOrEmpty(pr.AcceptedBodyId)
                ? string.Empty
                : _galaxy?.FindBody(pr.AcceptedBodyId)?.SystemId ?? string.Empty;
        return arrived != from;
    }

    /// <summary>Advances any active Contribute objectives for the item just poured into a relay (#1213).
    /// Per player, like Scan: the relay meter is shared world state, but the ORDER is the contributor's.</summary>
    private void OnRelayContributed(PlayerSession session, string item, int count)
    {
        foreach (var pr in session.State.Missions)
        {
            if (pr.Status != MissionStatus.Active || GetMissionDef(pr.MissionId) is not { } def)
            {
                continue;
            }

            for (int i = 0; i < def.Objectives.Count && i < pr.ObjectiveProgress.Count; i++)
            {
                var obj = def.Objectives[i];
                if (obj.Type == MissionObjectiveType.Contribute && obj.Target == item && pr.ObjectiveProgress[i] < obj.Required)
                {
                    pr.ObjectiveProgress[i] = Math.Min(obj.Required, pr.ObjectiveProgress[i] + count);
                }
            }
        }
    }

    private bool HasJumpLane(string? systemA, string? systemB)
    {
        if (string.IsNullOrEmpty(systemA) || string.IsNullOrEmpty(systemB))
        {
            return false;
        }

        return _relayLanes.Contains((systemA!, systemB!)) || _relayLanes.Contains((systemB!, systemA!));
    }

    /// <summary>Projects the whole relay network for the client (star-map meters + the Progress header).</summary>
    private RelayNetworkState BuildRelayNetworkState()
    {
        var def = _content.Relay;
        if (def is null)
        {
            return new RelayNetworkState { Enabled = false };
        }

        var relays = new List<NetRelayStation>();
        foreach (var s in _playerStationCells.Values.Where(s => s.Boardable))
        {
            var rec = _meta.Relays.FirstOrDefault(r => r.StationId == s.Id);
            relays.Add(new NetRelayStation
            {
                StationId = s.Id,
                Name = s.Name,
                SystemId = RelaySystemOf(s.Id),
                Items = def.Costs.Select(c => c.Item).ToArray(),
                Required = def.Costs.Select(c => c.Count).ToArray(),
                Contributed = def.Costs
                    .Select(c => rec is not null && rec.Contributed.TryGetValue(c.Item, out int n) ? Math.Min(n, c.Count) : 0)
                    .ToArray(),
                Completed = rec?.Completed ?? false,
            });
        }

        return new RelayNetworkState
        {
            Enabled = true,
            Relays = relays.ToArray(),
            LaneSystemA = _relayLanes.Select(l => l.A).ToArray(),
            LaneSystemB = _relayLanes.Select(l => l.B).ToArray(),
        };
    }

    private void SendRelayNetwork(PlayerSession session) => Send(session, BuildRelayNetworkState());

    private void BroadcastRelayNetwork()
    {
        var msg = BuildRelayNetworkState();
        foreach (var s in _sessions.Values.Where(x => x.Joined))
        {
            Send(s, msg);
        }
    }

    // ---------------- Test hooks ----------------

    public void ContributeRelayForTest(string playerId, string stationId, string item, int count)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            HandleContributeRelay(s, new ContributeRelayIntent { StationId = stationId, Item = item, Count = count });
        }
    }

    /// <summary>Test/inspection: the star system a location id belongs to — including a boarded station's
    /// <c>station:&lt;id&gt;</c> world (#1291).</summary>
    public string SystemOfLocationForTest(string locationId) => SystemOfLocation(locationId);

    /// <summary>Test/inspection: whether any boardable station still has an unfinished relay (#1213).</summary>
    public bool AnyRelayOpenForTest() => AnyRelayOpen();

    public bool RelayCompletedForTest(string stationId)
        => _meta.Relays.FirstOrDefault(r => r.StationId == stationId)?.Completed ?? false;

    /// <summary>Test hook: leaves the galaxy in the state a fully built-out relay network has — every
    /// boardable station's relay finished, which is exactly when the survey orders' Contribute step can no
    /// longer be finished by anyone (#1291).</summary>
    public void CompleteEveryRelayForTest()
    {
        foreach (var s in _playerStationCells.Values.Where(s => s.Boardable))
        {
            RelayRecordFor(s.Id).Completed = true;
        }
    }

    public bool HasJumpLaneForTest(string systemA, string systemB) => HasJumpLane(systemA, systemB);

    public int RelayLaneCountForTest => _relayLanes.Count;

    public RelayNetworkState RelayNetworkForTest() => BuildRelayNetworkState();
}
