// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The given placement seat (#3980): a structure placed at a fixed block position handed in by data or a caller — no
/// search. <see cref="GivenSeat"/> carries the footprint's centre column, its quarter turn and (optionally) the site height;
/// the stamp takes the origin corner from the centre, the ground row from the seat or the footprint's median ground
/// (<see cref="GivenGround"/>), grades the ground under the footprint to it with the generator's site flats
/// (<see cref="ReserveSiteGround"/> with seat <see cref="GivenSeat.SeatName"/>, terrain generation 40+ on a body stamped for
/// the first time), registers the box in the footprint registry so every structure rolled after it keeps clear (terrain
/// generation 38+), and pins a placement record like every other kind, replayed on every later load.
/// <list type="bullet">
/// <item><b>Landmarks.</b> <see cref="GivenLandmarkSource"/> names the templates a body carries at given seats (later: the body's
/// place list from data). They are stamped first in the structures stage, before every rolled kind, as landmark records with
/// indices from <see cref="GivenLandmarkIndexBase"/> on (the place's position in the list), so they never meet the rolled
/// landmark (index 0) or an admin's stamps (the next index below the base). A record replays whatever the source says now: a
/// place that left the list keeps its landmark. A place added to a body that was played on is not stamped over a build.</item>
/// <item><b>Cities.</b> A <see cref="WorldGeneration.CityPlace"/> carries its centre as a given seat (GameServerCityPlaces.cs).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The first landmark record index of a given place; the place at list position <c>i</c> is pinned under
    /// <c>GivenLandmarkIndexBase + i</c>.</summary>
    internal const int GivenLandmarkIndexBase = 1000;

    /// <summary>A landmark template a body carries at a given seat.</summary>
    internal sealed class GivenLandmarkPlace
    {
        public GivenLandmarkPlace(string template, GivenSeat seat)
        {
            Template = template;
            Seat = seat;
        }

        /// <summary>The landmark template's key.</summary>
        public string Template { get; }

        public GivenSeat Seat { get; }
    }

    /// <summary>The given landmark places of a body (by its location id), asked once per load in the structures stage; null or
    /// an empty list = none. Nothing sets it yet — the body place lists of a later package will.</summary>
    internal System.Func<string, IReadOnlyList<GivenLandmarkPlace>>? GivenLandmarkSource { get; set; }

    /// <summary>Whether a landmark record belongs to a given place (index from <see cref="GivenLandmarkIndexBase"/> on).</summary>
    private static bool IsGivenLandmarkIndex(int index) => index >= GivenLandmarkIndexBase;

    /// <summary>The structures stage's first stamper: replays every given landmark of the body, then plans the listed places
    /// that carry no record yet.</summary>
    private void StampGivenPlaces()
    {
        var records = _meta.Placements
            .Where(r => r.LocationId == _world.LocationId && r.Kind == LandmarkRules.RecordKind && IsGivenLandmarkIndex(r.Index))
            .OrderBy(r => r.Index).ToList();
        foreach (var rec in records)
        {
            if (rec.Placed && LandmarkPlanFromRecord(rec) is { } pinned)
            {
                CommitLandmark(pinned);
            }
        }

        var places = GivenLandmarkSource?.Invoke(_world.LocationId);
        for (int i = 0; places is not null && i < places.Count; i++)
        {
            int index = GivenLandmarkIndexBase + i;
            if (records.Exists(r => r.Index == index))
            {
                continue; // decided before: placed (replayed above) or skipped
            }

            if (PlanGivenLandmark(places[i], index) is { } plan)
            {
                CommitLandmark(plan);
                continue;
            }

            RecordPlacementSkip(LandmarkRules.RecordKind, index);
            SavePlacementRecords();
        }
    }

    /// <summary>A given landmark's plan: its seed hashed from its key and the body, the place's turn (a generator entry stands
    /// as built), its seat on the given centre. Null (warned) when the template is unknown or cannot be built, or when the body
    /// was played on and somebody built where it would stand.</summary>
    private LandmarkPlan? PlanGivenLandmark(GivenLandmarkPlace place, int index)
    {
        var template = _content.LandmarkByKey(place.Template);
        string lane = "landmark:given:" + place.Template + ":" + _world.LocationId;
        var plan = template is null ? null : new LandmarkPlan
        {
            Template = template,
            Seed = unchecked((long)StableHashing.Mixed(lane, _meta.Seed)),
            Turns = template.IsSource ? 0 : place.Seat.Turns,
            Index = index,
            Fresh = true,
        };
        if (plan is not null)
        {
            plan.Layout = BuildLandmarkLayout(plan);
        }

        var s = plan?.Layout?.Structure;
        if (plan is null || s is null || GivenGround(place.Seat, s.Width, s.Height, s.Length) is not { } origin)
        {
            _log.Warn($"The given landmark '{place.Template}' (record {index.ToString(CultureInfo.InvariantCulture)}) cannot stand at " +
                      $"{place.Seat.X},{place.Seat.Z} on '{_world.LocationId}'.");
            ReportStamp(LandmarkRules.RecordKind, 1, 0);
            return null;
        }

        (plan.OriginX, plan.OriginZ, plan.GroundY, plan.Seat) = (origin.X, origin.Z, origin.Y, GivenSeat.SeatName);
        return plan;
    }

    /// <summary>The given seat of a footprint (<see cref="GivenOrigin"/>), null when the body was played on and the footprint
    /// holds a player's edit (a given seat has no other spot to go to).</summary>
    private Vector3i? GivenGround(GivenSeat seat, int width, int height, int length)
    {
        var origin = GivenOrigin(seat, width, length);
        return !_worlds.Active.VirginAtLoad && FootprintHasPlayerEdits(origin.X, origin.Z, origin.Y, width, height, length) ? null : origin;
    }

    /// <summary>A footprint on a given seat: its origin corner from the centre, its Y the ground row — the seat's, else the
    /// footprint's median ground height (the shelf's rule: the grade splits the relief evenly).</summary>
    private Vector3i GivenOrigin(GivenSeat seat, int width, int length)
    {
        var (ox, oz) = seat.OriginFor(width, length);
        int groundY = seat.GroundY ?? EvaluateFootprint(_world.Planet, ox, oz, width, length).MedianY;
        return new Vector3i(ox, groundY, oz);
    }
}
