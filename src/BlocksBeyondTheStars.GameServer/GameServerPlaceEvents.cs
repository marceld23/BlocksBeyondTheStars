// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, what happens at a place (docs/developer/LIVELY_PLACES.md) — two recurring events of a settlement,
/// both only while a player is on foot near it, both without a condition and without anything to miss:
/// <list type="bullet">
/// <item><b>Market day.</b> Every third in-game day (<see cref="MarketDay"/>, each settlement on its own of the three) a
/// village, a town or a city with a vendor holds a market while the sun is up: one to three travelling merchants walk in
/// from where a road leaves the place, stand at the stalls (the plaza, where the place has none) with goods of their own,
/// the villagers' errands lead to the plaza more often, and the place says so (<see cref="PlaceStates.Market"/> — the
/// client shows "market day" in its greeting and plays the market's sound). In the evening the merchants leave.</item>
/// <item><b>A calendar festival gathers the village.</b> While one of the game's calendar festivals is on and a player
/// near the place has it switched on, the villagers gather at the plaza once in the early evening: the ring of the
/// village festival, its tune, dancing and clapping — no speech, no present, no friendship. Two minutes.</item>
/// </list>
/// Runtime only: after a restart a market day simply sets itself up again.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A market every this many in-game days per settlement.</summary>
    private const int MarketDayEvery = 3;

    /// <summary>The local day fractions between which a market is held.</summary>
    private const double MarketOpensAt = 0.27;
    private const double MarketClosesAt = 0.60;

    /// <summary>A player on foot this close to the plaza makes the place hold its market or gather for a festival.</summary>
    private const float PlaceEventRange = 96f;

    /// <summary>The job of a travelling merchant at a village market (never a guest job: it greets like any vendor).</summary>
    internal const string MarketTraderJob = "market_trader";

    /// <summary>Seconds a merchant has to walk out before it is simply gone.</summary>
    private const double MarketLeaveSeconds = 90.0;

    /// <summary>The local day fractions between which a calendar festival gathers a village.</summary>
    private const double GatheringFrom = 0.60;
    private const double GatheringUntil = 0.70;

    /// <summary>How long a gathering lasts (seconds).</summary>
    private const double GatheringSeconds = 120.0;

    /// <summary>The fewest villagers a gathering needs.</summary>
    private const int GatheringMinPeople = 3;

    private readonly Dictionary<string, double> _placeEventTickAt = new(StringComparer.Ordinal);

    /// <summary>The settlements whose market is set up right now ("&lt;location&gt;|&lt;place key&gt;" → its merchants' NPC ids).</summary>
    private readonly Dictionary<string, List<int>> _markets = new(StringComparer.Ordinal);

    /// <summary>Merchants on their way out: NPC id → the uptime they are removed at the latest.</summary>
    private readonly Dictionary<int, double> _marketLeavers = new();

    /// <summary>"&lt;location&gt;|&lt;place key&gt;" → the in-game day the place last gathered for a calendar festival.</summary>
    private readonly Dictionary<string, long> _gatheredDay = new(StringComparer.Ordinal);

    /// <summary>The 1 Hz pass per world (Guard-registered after the village festivals).</summary>
    private void TickPlaceEvents(double dt)
    {
        if (_world is null)
        {
            return;
        }

        string world = _world.LocationId;
        if (_placeEventTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _placeEventTickAt[world] = _uptime + 1.0;
        TickStationAnnouncements(); // aboard a station: the turn of its day (GameServerStationLife.cs)
        if (_settlements.Count == 0)
        {
            return;
        }

        StepMarketLeavers();
        foreach (var s in _settlements)
        {
            if (s.Ruined)
            {
                continue;
            }

            string key = world + "|" + SettlementPlaceKey(s);
            var plaza = SettlementSquareCentre(s);
            bool near = AnyPlayerOnFootWithin(plaza, PlaceEventRange);
            StepMarket(s, key, plaza, near);
            StepGathering(s, key, plaza, near);
        }
    }

    private bool AnyPlayerOnFootWithin(Vector3f at, float range)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            if (!session.Spectating && OnFootHere(session) && WrapDistSq(session.State.Position, at) <= (double)range * range)
            {
                return true;
            }
        }

        return false;
    }

    // =====================================================================================================
    // Market day
    // =====================================================================================================

    /// <summary>Whether today is a market day of this settlement: a place with a vendor, bigger than a hamlet, on its own
    /// day of every <see cref="MarketDayEvery"/>.</summary>
    private bool IsMarketDayOf(SettlementInstance s)
    {
        if (s.Tier == "hamlet" || !s.Markers.Any(m => m.Type == "vendor"))
        {
            return false;
        }

        long own = (uint)WorldGenerator.StableHash("market:" + SettlementPlaceKey(s)) % MarketDayEvery;
        return MarketDay % MarketDayEvery == own;
    }

    /// <summary>Whether a market is set up at this settlement right now.</summary>
    private bool MarketOpenAt(SettlementInstance s) => _markets.ContainsKey(_world.LocationId + "|" + SettlementPlaceKey(s));

    private void StepMarket(SettlementInstance s, string key, Vector3f plaza, bool playerNear)
    {
        double t = LocalDayFraction(plaza);
        bool wanted = playerNear && IsMarketDayOf(s) && t >= MarketOpensAt && t < MarketClosesAt;
        bool open = _markets.ContainsKey(key);
        if (wanted && !open)
        {
            OpenMarket(s, key, plaza);
        }
        else if (!wanted && open && (!playerNear || t >= MarketClosesAt || t < MarketOpensAt || !IsMarketDayOf(s)))
        {
            CloseMarket(s, key);
        }
    }

    private static int MarketTradersFor(string tier) => tier switch
    {
        "town" => 2,
        "city" or CityGenerator.Tier => 3,
        _ => 1,
    };

    private void OpenMarket(SettlementInstance s, string key, Vector3f plaza)
    {
        var anchors = PlaceAnchorsOf(s);
        var stalls = anchors.Where(a => a.Kind == PlaceAnchorKind.Stall).Select(a => a.Spot).ToList();
        if (stalls.Count == 0)
        {
            stalls = anchors.Where(a => a.Kind == PlaceAnchorKind.Plaza).Select(a => a.Spot).ToList();
        }

        var ids = new List<int>();
        _markets[key] = ids;
        if (stalls.Count == 0)
        {
            SetSettlementEvent(s, PlaceStates.Market); // a place not loaded yet: the day is a market day, the merchants come later
            return;
        }

        var exits = anchors.Where(a => a.Kind == PlaceAnchorKind.Exit).Select(a => a.Spot).ToList();
        string shopTheme = SettlementTrade(s);
        int count = Math.Min(MarketTradersFor(s.Tier), stalls.Count);
        for (int i = 0; i < count; i++)
        {
            var rng = new Random(unchecked((int)WorldGenerator.StableHash($"market:{key}:{MarketDay}:{i}")));
            var from = exits.Count > 0 ? exits[rng.Next(exits.Count)] : stalls[i];
            string theme = VisitingTraderTheme($"market:{key}:{MarketDay}:{i}", shopTheme);
            var npc = MakeNpc("vendor", theme, robotic: false, from, rng);
            npc.Settlement = s.Name;
            npc.Job = MarketTraderJob;
            npc.GuestKey = "guest:" + npc.Name; // a visitor's memory key: never saved, never a villager's
            npc.ActivityKey = "npc.activity.trading";
            npc.Leash = 0.8f;
            _npcs.Add(npc);
            ids.Add(npc.Id);
            GoTo(npc, stalls[i], NpcArrival.None, 0.8f);
        }

        SetSettlementEvent(s, PlaceStates.Market);
        BroadcastNpcs();
        _log.Info($"Market day at '{s.Name}': {ids.Count} travelling merchant(s).");
    }

    private void CloseMarket(SettlementInstance s, string key)
    {
        if (!_markets.Remove(key, out var ids))
        {
            return;
        }

        var exits = PlaceAnchorsOf(s).Where(a => a.Kind == PlaceAnchorKind.Exit).Select(a => a.Spot).ToList();
        foreach (int id in ids)
        {
            if (_npcs.FirstOrDefault(n => n.Id == id) is not { } npc)
            {
                continue;
            }

            if (exits.Count == 0)
            {
                _npcs.Remove(npc);
                continue;
            }

            npc.ActivityKey = string.Empty;
            SetNpcGoal(npc, exits.OrderBy(e => WrapDistSq(e, npc.Pos)).First(), NpcArrival.None, 0f);
            _marketLeavers[id] = _uptime + MarketLeaveSeconds;
        }

        if (string.Equals(SettlementEventOf(s), PlaceStates.Market, StringComparison.Ordinal))
        {
            SetSettlementEvent(s, PlaceStates.None);
        }

        BroadcastNpcs();
    }

    /// <summary>A merchant that reached the road out (or took too long) is gone.</summary>
    private void StepMarketLeavers()
    {
        if (_marketLeavers.Count == 0)
        {
            return;
        }

        foreach (var (id, until) in _marketLeavers.ToList())
        {
            var npc = _npcs.FirstOrDefault(n => n.Id == id);
            if (npc is null)
            {
                _marketLeavers.Remove(id); // another world's, or gone with a reload
                continue;
            }

            if (npc.Goal is null || _uptime >= until)
            {
                _npcs.Remove(npc);
                _marketLeavers.Remove(id);
                BroadcastNpcs();
            }
        }
    }

    // =====================================================================================================
    // A calendar festival gathers the village
    // =====================================================================================================

    private static string GatheringKey(string placeKey) => "gathering|" + placeKey;

    private void StepGathering(SettlementInstance s, string key, Vector3f plaza, bool playerNear)
    {
        string runKey = GatheringKey(key);
        bool running = _festivalRuns.ContainsKey(runKey);
        if (running)
        {
            return; // the festival pass steps and ends it
        }

        if (string.Equals(SettlementEventOf(s), PlaceStates.Festival, StringComparison.Ordinal))
        {
            SetSettlementEvent(s, MarketOpenAt(s) ? PlaceStates.Market : PlaceStates.None); // it just ended
        }

        if (!playerNear || ActiveFestivals.Count == 0 || (_gatheredDay.TryGetValue(key, out long day) && day == MarketDay)
            || _festivalRuns.ContainsKey(FestivalKey(_world.LocationId, s.Name)))
        {
            return;
        }

        double t = LocalDayFraction(plaza);
        if (t < GatheringFrom || t >= GatheringUntil || !AnyPlayerCelebratesNear(plaza))
        {
            return;
        }

        var anchor = FestivalAnchorFor(s);
        var attendees = FestivalAttendees(s.Name, anchor);
        if (attendees.Count < GatheringMinPeople)
        {
            return;
        }

        _gatheredDay[key] = MarketDay;
        var run = new FestivalRun(runKey, _world.LocationId, s.Name, heroId: string.Empty, heroName: string.Empty, anchor, _uptime + GatheringSeconds)
        {
            NextCheerAt = _uptime + FestivalFirstCheerDelay,
            NextJingleAt = _uptime + FestivalJingleGap,
        };
        SeatFestivalRing(run, attendees);
        _festivalRuns[runKey] = run;
        foreach (var npc in run.Spots.Keys)
        {
            ApplyRoutinePhase(npc, NpcPhase.Festival);
            npc.PhaseCheckedAt = _uptime;
        }

        QueueFestivalTune(run);
        SetSettlementEvent(s, PlaceStates.Festival);
        _log.Info($"'{s.Name}' gathers for a calendar festival: {run.Spots.Count} villagers.");
    }

    /// <summary>Whether a player on foot near the plaza has one of today's calendar festivals switched on.</summary>
    private bool AnyPlayerCelebratesNear(Vector3f plaza)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            if (!session.Spectating && OnFootHere(session) && WrapDistSq(session.State.Position, plaza) <= (double)PlaceEventRange * PlaceEventRange
                && ActiveFestivalsFor(session).Count > 0)
            {
                return true;
            }
        }

        return false;
    }

    // =====================================================================================================
    // A train arrives
    // =====================================================================================================

    /// <summary>The platform bell of a town (a client sound; a client without the clip stays silent).</summary>
    internal const string PlatformBellSound = "place_platform_bell";

    /// <summary>People this close to the stop wave at the train.</summary>
    private const float PlatformWaveRange = 9f;

    /// <summary>At most this many people wave.</summary>
    private const int PlatformWavers = 3;

    /// <summary>A train halted at a stop: when the stop is a settlement's, its bell rings and the people standing at the
    /// platform wave — an arrival is something that happens.</summary>
    private void OnTrainHaltedAtPlace(Vector3i stop)
    {
        if (RailTownAt(stop) is null)
        {
            return;
        }

        var at = new Vector3f(stop.X + 0.5f, stop.Y + 1f, stop.Z + 0.5f);
        BroadcastToWorld(new SoundFx { SoundId = PlatformBellSound, X = at.X, Y = at.Y, Z = at.Z });
        int waved = 0;
        foreach (var npc in _npcs)
        {
            if (waved >= PlatformWavers)
            {
                break;
            }

            if (npc.Pose == 0 && npc.Goal is null && npc.TrainId.Length == 0 && !npc.VisitingAway && npc.Role != "guardian"
                && WrapDistSq(npc.Pos, at) <= PlatformWaveRange * PlatformWaveRange)
            {
                BroadcastNpcEmote(npc, NpcGestures.Wave);
                waved++;
            }
        }
    }

    /// <summary>Test seam: runs the place-event pass of the active world now.</summary>
    internal void TickPlaceEventsForTest()
    {
        _placeEventTickAt.Remove(_world.LocationId);
        TickPlaceEvents(0.0);
    }

    /// <summary>Test seam: the ids of the travelling merchants at the settlement of this name (empty = no market).</summary>
    internal IReadOnlyList<int> MarketTradersForTest(string settlementName)
    {
        var s = _settlements.FirstOrDefault(x => x.Name == settlementName);
        return s is not null && _markets.TryGetValue(_world.LocationId + "|" + SettlementPlaceKey(s), out var ids) ? ids : Array.Empty<int>();
    }

    /// <summary>Test seam: whether today is a market day of the settlement of this name.</summary>
    internal bool IsMarketDayForTest(string settlementName)
        => _settlements.FirstOrDefault(x => x.Name == settlementName) is { } s && IsMarketDayOf(s);
}
