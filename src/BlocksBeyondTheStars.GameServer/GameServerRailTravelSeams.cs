// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>The test seams of rail travel (#3446/#3447): the trips, the places, the passenger switch.</summary>
public sealed partial class GameServer
{
    /// <summary>Test seam (#3446): the train trips on the active world.</summary>
    public IReadOnlyList<(int NpcId, string Kind, string Phase, bool Returning, string Home, string Away)> RailTripsForTest
        => Rails.Trips.Select(t => (t.NpcId, t.Kind.ToString(), t.Phase.ToString(), t.Returning, t.HomeName, t.AwayName)).ToList();

    /// <summary>Test seam (#3446): starts a trip now on the first line that has two places and a train for villagers (the
    /// daytime and the pace are skipped; who may go still applies). <paramref name="homePlace"/> narrows the home place.
    /// Returns the traveller's NPC id, or null.</summary>
    public int? StartRailTripForTest(string? homePlace = null)
    {
        foreach (var line in Rails.Lines.Values.OrderBy(l => l.Id))
        {
            var places = RailStopPlaces(line);
            if (places.Count >= 2 && LineCarriesNpcs(line.Id) && TryStartRailTrip(line, places, homePlace, admin: true) is { } trip)
            {
                return trip.NpcId;
            }
        }

        return null;
    }

    /// <summary>Test seam (#3446): one planner pass with every line due now (the 1 Hz throttle and the pace skipped).</summary>
    public void PlanRailTripsForTest()
    {
        var rails = Rails;
        rails.NextTripPlanAt = 0;
        foreach (var line in rails.Lines.Values)
        {
            rails.NextTripAt[line.Id] = 0;
        }

        PlanRailTrips();
    }

    /// <summary>Test seam (#3446): the planner starts no trips on its own while paused (a test starts the one it follows).</summary>
    public void PauseRailTripPlannerForTest(bool paused = true) => _railTripPlannerPaused = paused;

    /// <summary>Test seam (#3446): the first place a stop belongs to (the town's before a base's, #3457) — its name and
    /// "settlement" / "base" — or null.</summary>
    public (string Name, string Kind)? RailPlaceForTest(Vector3i stop)
        => RailPlacesAt(stop).FirstOrDefault() is { } p ? (p.Name, p.Base is null ? "settlement" : "base") : null;

    /// <summary>Test seam (#3446): whether a villager may go on a train trip right now.</summary>
    public bool MayTravelByRailForTest(int npcId) => NpcWithId(npcId) is { } n && MayTravelByRail(n);

    /// <summary>Test seam (#3446): ends every traveller's stay at the other place now (they head back on the next step).</summary>
    public void EndRailTripVisitsForTest()
    {
        foreach (var trip in Rails.Trips)
        {
            trip.StayUntil = _uptime;
        }
    }

    /// <summary>Test seam (#3446): whether a villager is on a train trip.</summary>
    public bool NpcOnRailTripForTest(int npcId) => NpcWithId(npcId)?.Trip is not null;

    /// <summary>Test seam (#3446): the train's passenger switch (false when there is no such train).</summary>
    public bool TrainTakesPassengersForTest(string trainId) => Rails.Trains.FirstOrDefault(t => t.Id == trainId)?.TakesPassengers ?? false;

    /// <summary>Test seam (#3447): whether a guest came by train (and so leaves by train).</summary>
    public bool BaseGuestByTrainForTest(int guestNpcId) => _baseGuests.FirstOrDefault(g => g.NpcId == guestNpcId)?.ByTrain ?? false;

    /// <summary>Test seam (#3447): the towns that may send a base its guest by train (far enough, on a line that carries
    /// villagers).</summary>
    public IReadOnlyList<string> TrainGuestTownsForTest(int baseId)
        => _bases.FirstOrDefault(b => b.Id == baseId) is { } b ? TrainGuestRoutes(b).Select(r => r.Town.Name).Distinct().ToList() : new List<string>();

    /// <summary>Test seam (#3446): loads the active world's trains from a saved record, as a world load does.</summary>
    public void LoadTrainRecordForTest(string record)
    {
        _meta.Trains[_world.LocationId] = record;
        LoadRails();
    }
}
