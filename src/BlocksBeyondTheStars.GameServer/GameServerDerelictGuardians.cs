// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Derelict guardians (#2635, epic #2637): every generated derelict holds one guardian bay (<c>guardian_spawner</c>,
/// placed by <see cref="WorldGeneration.DerelictGenerator"/> at the layout's spawner spot). While a player is aboard and
/// fewer than <see cref="GuardianCapPerBay"/> of its guardians are alive, the bay releases a Guardian robot or a
/// scan-drone beside itself — the first <see cref="GuardianFirstReleaseSeconds"/> after someone came aboard, then one
/// every <see cref="GuardianReleaseIntervalSeconds"/>. Mining the bay stops it for good: the bay is a block of the
/// derelict's interior world, so its absence persists with the world's block edits (and the derelict's build row).
/// <para>Station worlds are hostile-free; this is the one deliberate exception, and a narrow one: guardians only, from
/// the bay only, inside the hull only (a guardian that ends up outside the hull is removed). Gated exactly like the
/// planet machines (<see cref="PlanetEnemiesActive"/>: none in Peaceful, in Creative or with hostile machines off — any
/// still aboard power down), the same bodies and stats (<see cref="NewPlanetMachine"/>), the same movement, damage, kill
/// and loot path (<see cref="RunMachinePass"/>, <see cref="AttackEntity"/>); only the drone hovers lower under a station
/// ceiling (<see cref="DroneHoverHere"/>). Runtime state only — the bay's clock is not persisted.</para>
/// See docs/developer/DERELICT_STATIONS.md §8.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The bay's block key (data/blocks.json).</summary>
    internal const string GuardianBayBlockKey = "guardian_spawner";

    /// <summary>Seconds aboard before the bay releases its first guardian — time to look around and hear VEGA.</summary>
    internal const double GuardianFirstReleaseSeconds = 60.0;

    /// <summary>The bay's cadence after the first release (server time, only while a player is aboard).</summary>
    internal const double GuardianReleaseIntervalSeconds = 300.0;

    /// <summary>At most this many of a bay's guardians are alive at once; at the cap the clock holds at zero, so a
    /// destroyed guardian is replaced one full interval later, never at once.</summary>
    internal const int GuardianCapPerBay = 3;

    /// <summary>When nothing fits beside the bay (a boarder walled it in), the next try comes this much later.</summary>
    private const double GuardianReleaseRetrySeconds = 10.0;

    /// <summary>A guardian drone's hover above the deck: a station room is four or five blocks high, the planet's
    /// <see cref="ScanDroneHover"/> would put it into the ceiling.</summary>
    private const int GuardianDroneHover = 2;

    /// <summary>A hunting guardian drone's ring round its target (the planet's 7 ± 3 would lie behind the walls).</summary>
    private const float GuardianDroneStandoff = 3f;

    private const float GuardianDroneStrafe = 1f;

    /// <summary>Blocks beyond the derelict's stamped box a guardian may stand before it counts as out of the hull.</summary>
    private const int GuardianHullMargin = 1;

    /// <summary>The bay's release cue (client Resources/audio) and its world effect kind.</summary>
    private const string GuardianBayCue = "guardian_bay";

    /// <summary>A drone's hover in the active world: lower aboard a station (#2635).</summary>
    private int DroneHoverHere => _world.Planet.Void ? GuardianDroneHover : ScanDroneHover;

    /// <summary>The spots beside the bay a guardian is released on, tried in turn (the start rotates per release).</summary>
    private static readonly (int Dx, int Dz)[] GuardianReleaseRing =
    {
        (1, 0), (0, 1), (-1, 0), (0, -1), (1, 1), (-1, 1), (-1, -1), (1, -1), (2, 0), (0, 2), (-2, 0), (0, -2),
    };

    /// <summary>One derelict's bay clock (runtime only).</summary>
    private sealed class GuardianBay
    {
        public double Timer;
        public int Released;

        /// <summary>The bay's state at the last tick (null before the first look) — its fall is VEGA's cue.</summary>
        public bool? Working;

        public int AboardCount;
        public readonly HashSet<string> Guardians = new(System.StringComparer.Ordinal);
    }

    private readonly Dictionary<string, GuardianBay> _guardianBays = new(System.StringComparer.Ordinal);
    private readonly List<PlayerSession> _guardianTargets = new(); // reused per tick
    private readonly List<string> _guardianScratch = new();       // reused per tick
    private ushort? _guardianBayBlock;

    private ushort GuardianBayBlock => _guardianBayBlock ??= _content.GetBlock(GuardianBayBlockKey)?.NumericId.Value ?? 0;

    /// <summary>The enemy tick of a generated derelict's interior world (from <see cref="TickEnemies"/>).</summary>
    private void TickDerelictGuardians(double dt)
    {
        string stationId = _world.LocationId.Substring(StationLocationIdPrefix.Length);
        if (!_stationsById.TryGetValue(stationId, out var station) || !station.Stamped)
        {
            return;
        }

        if (!_guardianBays.TryGetValue(stationId, out var bay))
        {
            bay = new GuardianBay();
            _guardianBays[stationId] = bay;
        }

        // Peaceful, Creative, hostile machines off: no guardian at all — any still aboard power down.
        if (!PlanetEnemiesActive)
        {
            bay.Timer = 0;
            if (_planetEnemies.Count > 0)
            {
                foreach (var e in _planetEnemies)
                {
                    ForgetMachine(e.Id);
                }

                _planetEnemies.Clear();
                bay.Guardians.Clear();
                BroadcastPlanetEnemies();
            }

            return;
        }

        // Inside the hull only: a guardian beyond the stamped box (a breach, a mined deck) is gone, never falling for ever.
        bool listDirty = false;
        for (int i = _planetEnemies.Count - 1; i >= 0; i--)
        {
            var e = _planetEnemies[i];
            if (BeyondStationBox(station, e.Position, GuardianHullMargin))
            {
                _planetEnemies.RemoveAt(i);
                ForgetMachine(e.Id);
                listDirty = true;
            }
        }

        int alive = PruneBayGuardians(bay);

        var targets = _guardianTargets;
        targets.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.State.AboardShip && !InSpace(s.State.PlayerId))
            {
                targets.Add(s);
            }
        }

        if (targets.Count > bay.AboardCount)
        {
            listDirty = true; // a new boarder gets the machines already aboard with this tick's list
        }

        bay.AboardCount = targets.Count;
        if (listDirty)
        {
            BroadcastPlanetEnemies();
        }

        if (targets.Count == 0)
        {
            return;
        }

        var cell = DerelictSpawnerSpotWorld(stationId);
        bool working = cell is { } c && GuardianBayBlock != 0 && _world.GetBlock(c).Value == GuardianBayBlock;
        if (bay.Working == true && !working && bay.Released > 0)
        {
            foreach (var s in targets)
            {
                SendVegaLine(s, "vega.derelict.bay_down", 2);
            }

            _log.Info($"The guardian bay of derelict '{station.Name}' is gone — no more guardians from it.");
        }

        bay.Working = working;
        if (working)
        {
            if (alive >= GuardianCapPerBay)
            {
                bay.Timer = 0; // at the cap the clock never banks time
            }
            else if ((bay.Timer += dt) >= (bay.Released == 0 ? GuardianFirstReleaseSeconds : GuardianReleaseIntervalSeconds))
            {
                if (ReleaseGuardian(station, bay, cell!.Value))
                {
                    bay.Timer = 0;
                    bay.Released++;
                    alive++;
                }
                else
                {
                    bay.Timer -= GuardianReleaseRetrySeconds;
                }
            }
        }

        if (alive > 0)
        {
            WarnOfGuardians(targets, station);
        }

        RunMachinePass(targets, dt);
    }

    /// <summary>Drops the ids of a bay's guardians that are no longer in the world (destroyed, removed) and returns how
    /// many are alive. No per-tick allocation: the scratch list is reused.</summary>
    private int PruneBayGuardians(GuardianBay bay)
    {
        if (bay.Guardians.Count == 0)
        {
            return 0;
        }

        _guardianScratch.Clear();
        foreach (string id in bay.Guardians)
        {
            bool found = false;
            foreach (var e in _planetEnemies)
            {
                if (e.Id == id)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                _guardianScratch.Add(id);
            }
        }

        foreach (string id in _guardianScratch)
        {
            bay.Guardians.Remove(id);
        }

        return bay.Guardians.Count;
    }

    /// <summary>Releases one guardian beside the bay: a scan-drone every other time (when the world keeps drones, and
    /// only drones once the galaxy is pacified — the planet machines' rule), else a walking robot; the hunter variant
    /// when hostile machines are set to Frequent or Extreme. A drone needs a room high enough to hover in, else a robot
    /// comes. False when no spot beside the bay is free.</summary>
    private bool ReleaseGuardian(BoardableStation station, GuardianBay bay, Vector3i cell)
    {
        bool asDrone = Rules.PlanetDrones && (RemnantEra || bay.Released % 2 == 1);
        bool tougher = Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme;
        for (int k = 0; k < GuardianReleaseRing.Length; k++)
        {
            var (dx, dz) = GuardianReleaseRing[(k + bay.Released) % GuardianReleaseRing.Length];
            int x = cell.X + dx, y = cell.Y, z = cell.Z + dz;
            bool drone = asDrone && StandableAt(x, y, z, GuardianDroneHover + 2);
            if (!drone && !StandableAt(x, y, z))
            {
                continue;
            }

            var at = new Vector3f(x + 0.5f, y + (drone ? GuardianDroneHover : 0), z + 0.5f);
            if (BeyondStationBox(station, at, 0))
            {
                continue;
            }

            var guardian = NewPlanetMachine(at, drone, tougher && !drone);
            _planetEnemies.Add(guardian);
            bay.Guardians.Add(guardian.Id);
            BroadcastPlanetEnemies();

            float fx = cell.X + 0.5f, fy = cell.Y + 0.5f, fz = cell.Z + 0.5f;
            BroadcastToWorld(new SoundFx { SoundId = GuardianBayCue, X = fx, Y = fy, Z = fz });
            BroadcastToWorld(new WorldFx { Kind = GuardianBayCue, X = fx, Y = fy, Z = fz, Strength = 1f });
            _log.Info($"The guardian bay of derelict '{station.Name}' released a {(drone ? "scan-drone" : "robot")}.");
            return true;
        }

        return false;
    }

    /// <summary>VEGA's warning, once per player and derelict, the first time that player is aboard while one of its
    /// guardians is about.</summary>
    private void WarnOfGuardians(List<PlayerSession> targets, BoardableStation station)
    {
        foreach (var s in targets)
        {
            if (s.State.Milestones.Add("derelict:guardian:" + station.Id))
            {
                SendVegaLine(s, "vega.derelict.guardian", 2, station.Name);
                _repo.SavePlayer(s.State);
            }
        }
    }

    /// <summary>Drops a removed machine's movement memory.</summary>
    private void ForgetMachine(string id)
    {
        _enemyWander.Remove(id);
        _enemySightSeenAt.Remove(id);
        ForgetWadePlan(id);
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam: how many guardians a derelict's bay has released since the server started.</summary>
    public int GuardiansReleasedForTest(string stationId) => _guardianBays.TryGetValue(stationId, out var bay) ? bay.Released : 0;

    /// <summary>Test seam: whether a position lies inside a boarded derelict's hull box (as the guardians' leash reads it).</summary>
    public bool InsideDerelictHullForTest(string stationId, Vector3f pos)
        => _stationsById.TryGetValue(stationId, out var st) && !BeyondStationBox(st, pos, GuardianHullMargin);
}
