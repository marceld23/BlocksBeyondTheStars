// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The lively places, aboard a station (docs/developer/LIVELY_PLACES.md): what one hears and is told there.
/// <list type="bullet">
/// <item><b>Its rooms sound.</b> The hangar, the canteen and the rooms with equipment (the greenhouse, the medbay) carry a
/// sound of their own — derived from the markers every station has, like a settlement's tavern and workshop
/// (<see cref="PlaceEmittersOfActiveWorld"/>), so it works on every station of every world. The deck's hum under it all is
/// the client's place layer.</item>
/// <item><b>It makes announcements.</b> A short line with a gong to everybody aboard: a trader has docked, the night rest
/// begins, the day shift begins — at most one a minute per station, never a wall of text.</item>
/// </list>
/// Nothing here changes a block, a stock or a rule.
/// </summary>
public sealed partial class GameServer
{
    internal const string StationHangarSound = "amb_station_hangar";
    internal const string StationCanteenSound = "amb_station_canteen";
    internal const string StationLabSound = "amb_station_lab";

    /// <summary>The gong before a station announcement (a 2D cue the client plays).</summary>
    internal const string StationGongSound = "station_gong";

    /// <summary>Seconds between two announcements of one station.</summary>
    private const double StationAnnounceGap = 60.0;

    /// <summary>Uptime of the last announcement per station id.</summary>
    private readonly Dictionary<string, double> _stationAnnouncedAt = new(System.StringComparer.Ordinal);

    /// <summary>Whether it was night aboard a station at the last look (per station id) — the announcements come at the turn.</summary>
    private readonly Dictionary<string, bool> _stationNight = new(System.StringComparer.Ordinal);

    /// <summary>The sound a station marker makes, or null.</summary>
    private static (string Sound, float Radius, float Volume, byte When)? StationSoundOf(string marker) => marker switch
    {
        "hangar" => (StationHangarSound, 26f, 0.7f, SoundEmitterTimes.Always),
        "lounge" => (StationCanteenSound, 16f, 0.7f, SoundEmitterTimes.Waking),
        "greenhouse" or "heal_tank" => (StationLabSound, 12f, 0.5f, SoundEmitterTimes.Always),
        _ => null,
    };

    /// <summary>The derived emitters of the boarded station (none on a derelict: nothing runs there).</summary>
    private void AddStationEmitters(List<LandmarkEmitter> emitters)
    {
        if (ActiveStationInterior() is not { } station || IsGeneratedDerelictId(station.Id))
        {
            return;
        }

        var counts = new Dictionary<string, int>(System.StringComparer.Ordinal);
        foreach (var (type, pos) in station.Markers)
        {
            if (StationSoundOf(type) is not { } sound)
            {
                continue;
            }

            counts.TryGetValue(sound.Sound, out int count);
            if (count >= PlaceEmittersPerKind)
            {
                continue;
            }

            counts[sound.Sound] = count + 1;
            emitters.Add(new LandmarkEmitter
            {
                Position = new Vector3f(pos.X, pos.Y + 1f, pos.Z),
                Sound = sound.Sound,
                Radius = sound.Radius,
                Volume = sound.Volume,
                When = sound.When,
            });
        }
    }

    /// <summary>
    /// Says something to everybody aboard a station: the gong, then a short status line (a locale key under
    /// <c>srv.station.announce.</c>, with one optional name). False when the station spoke less than
    /// <see cref="StationAnnounceGap"/> ago or nobody is aboard.
    /// </summary>
    private bool AnnounceAboardStation(string stationId, string key, string arg = "")
    {
        if (_stationAnnouncedAt.TryGetValue(stationId, out double last) && _uptime - last < StationAnnounceGap)
        {
            return false;
        }

        string text = "@srv.station.announce." + key + (arg.Length > 0 ? ":" + arg : string.Empty);
        bool anyone = false;
        foreach (var (playerId, boarded) in _boardedStation)
        {
            if (boarded != stationId || FindSessionByPlayerId(playerId) is not { } session)
            {
                continue;
            }

            anyone = true;
            Send(session, new SoundFx { SoundId = StationGongSound, X = session.State.Position.X, Y = session.State.Position.Y, Z = session.State.Position.Z });
            Send(session, new ServerMessage { Text = text });
        }

        if (anyone)
        {
            _stationAnnouncedAt[stationId] = _uptime;
        }

        return anyone;
    }

    /// <summary>Once a second for the boarded station (from the place-event pass): the turn of its day announces itself.</summary>
    private void TickStationAnnouncements()
    {
        if (ActiveStationInterior() is not { } station || IsGeneratedDerelictId(station.Id) || IsPlayerStationId(station.Id))
        {
            return;
        }

        SyncVisitingPilot(station); // the merchant stands aboard exactly while its ship lies at the berth
        double t = LocalDayFraction(station.Spawn);
        bool night = t >= RoutineNightStart || t < RoutineNightEnd;
        if (!_stationNight.TryGetValue(station.Id, out bool was))
        {
            _stationNight[station.Id] = night; // the first look only notes the hour: boarding is not a turn of the day
            return;
        }

        if (night != was && AnnounceAboardStation(station.Id, night ? "night" : "day"))
        {
            _stationNight[station.Id] = night;
        }
    }

    /// <summary>Test seam: announces aboard a station as an event would.</summary>
    internal bool AnnounceAboardStationForTest(string stationId, string key, string arg = "") => AnnounceAboardStation(stationId, key, arg);
}
