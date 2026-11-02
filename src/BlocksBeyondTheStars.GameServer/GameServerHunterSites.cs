// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3097: the camp a hunter shuttle leaves when it gets away (<see cref="LandHunterShuttle"/>). Decided in space, stamped on
/// the ground: <see cref="RegisterHunterSite"/> plans a spot 180–320 blocks from the pilot's base (or the body's first landing
/// pad) and persists it in <see cref="WorldMetadata.HunterShuttleSites"/>; the first time a player stands on that body the
/// per-world tick looks for dry, fairly flat ground near the plan and stamps the shuttle there from existing blocks — the hull
/// of the flight view (<see cref="HunterShuttleHull"/>), its own robot dock in the hold, one or two more docks
/// (<c>guardian_spawner</c>) on the ground beside it — and broadcasts every changed cell. The blocks are not protected (the
/// bandit camps' rule): they are ordinary block edits from then on.
/// <para>While a player on foot is within <see cref="HunterSiteNearRange"/> of it, the camp is awake (1 Hz, nothing at all
/// otherwise): the shuttle's hunter guards it (once defeated, never again — persisted), and each dock releases a Guardian robot
/// or a scan-drone beside itself on a cooldown, at most <see cref="HunterDockCap"/> of its own alive (the derelict bay's rule,
/// #2635). Mining every dock clears the camp for good (persisted): a loot crate appears in the hold and VEGA says so. Gated
/// like the planet machines (<see cref="PlanetEnemiesActive"/>): in Peaceful no shuttle lands at all, and a camp from before
/// a rule change only sleeps.</para>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A player on foot this close (flat distance to the hull's centre) wakes the camp.</summary>
    private const float HunterSiteNearRange = 56f;

    /// <summary>A guard that vanishes while a player stands this close to where it was was defeated (the far-despawn of the
    /// planet machines only takes one that is 150 from every player).</summary>
    private const float HunterGuardWitnessRange = 64f;

    private const double HunterDockFirstSeconds = 6.0;   // after a player came near, staggered per dock below
    private const double HunterDockStaggerSeconds = 5.0;
    private const double HunterDockIntervalSeconds = 45.0;
    private const double HunterDockRetrySeconds = 5.0;   // nothing free beside a dock: try again this much later

    /// <summary>Camps that are not cleared yet, galaxy-wide — a further shuttle that gets away just jumps off.</summary>
    private const int HunterSitesMaxOpen = 3;

    private const int HunterSiteMinDistance = 180;
    private const int HunterSiteMaxDistance = 320;
    private const int HunterSiteSearchStep = 14;      // the spot search's ring spacing around the planned spot
    private const int HunterSiteSearchRings = 6;      // … out to 84 blocks from it
    private const int HunterSiteMaxSpread = 4;        // the most the ground may rise across the camp's footprint

    /// <summary>The planet-map POI type of a camp (live, from the site record).</summary>
    internal const string HunterSitePoiType = "hunter_shuttle";

    /// <summary>At most this many of a dock's own robots are alive at once: one on the calmer machine settings, two on
    /// Frequent and Extreme.</summary>
    private int HunterDockCap => Rules.PlanetEnemies is AlienActivity.Frequent or AlienActivity.Extreme ? 2 : 1;

    /// <summary>The probes of the spot search, relative to the hull's centre: the centre, the hull's corners, the docks.</summary>
    private static readonly (int Dx, int Dz)[] HunterSpotProbes =
    {
        (0, 0), (-3, -5), (3, -5), (-3, 4), (3, 4), (-6, 0), (6, 0), (0, -8),
    };

    /// <summary>One camp's clock (runtime only — a restart or a world reload starts it over; the record keeps the rest).</summary>
    private sealed class HunterSiteRuntime
    {
        public LoadedWorld? World;
        public double Clock;
        public string GuardId = string.Empty;
        public Vector3f GuardLast;
        public double[] DockTimers = System.Array.Empty<double>();
        public int[] DockReleased = System.Array.Empty<int>();
        public HashSet<string>[] DockRobots = System.Array.Empty<HashSet<string>>();
    }

    private readonly Dictionary<string, HunterSiteRuntime> _hunterSiteRuntime = new(System.StringComparer.Ordinal);
    private readonly List<PlayerSession> _hunterSitePresent = new(); // reused per pass
    private readonly List<PlayerSession> _hunterSiteOnFoot = new();

    /// <summary>Whether a body has a camp that is not cleared yet (planned or stamped).</summary>
    private bool HunterSiteOpenOn(string bodyId)
        => _meta.HunterShuttleSites.TryGetValue(bodyId, out var rec) && rec.State != HunterShuttleSiteRecord.Cleared;

    private int OpenHunterSiteCount()
    {
        int n = 0;
        foreach (var rec in _meta.HunterShuttleSites.Values)
        {
            n += rec.State != HunterShuttleSiteRecord.Cleared ? 1 : 0;
        }

        return n;
    }

    /// <summary>A shuttle got away and lands on <paramref name="bodyId"/> (whose world may not be loaded): plans the spot and
    /// persists the camp as pending. False — no camp — in Peaceful/Creative or with the machines off, when the body already
    /// has an open camp, or when <see cref="HunterSitesMaxOpen"/> camps are open.</summary>
    private bool RegisterHunterSite(string bodyId, SpaceInstance? instance)
    {
        if (!PlanetEnemiesActive || HunterSiteOpenOn(bodyId) || OpenHunterSiteCount() >= HunterSitesMaxOpen)
        {
            return false;
        }

        var (ax, az) = HunterSiteAnchor(bodyId, instance);
        long seed = ((long)_hunterShuttleRng.Next() << 31) ^ _hunterShuttleRng.Next();
        var rng = new System.Random(StableHashing.Seed32("huntersite-spot:" + bodyId, seed));
        double ang = rng.NextDouble() * System.Math.PI * 2.0;
        int dist = HunterSiteMinDistance + rng.Next(HunterSiteMaxDistance - HunterSiteMinDistance + 1);
        int x = ax + (int)System.Math.Round(System.Math.Cos(ang) * dist);
        int z = az + (int)System.Math.Round(System.Math.Sin(ang) * dist);
        int circ = BodyCircumference(bodyId);
        if (circ > 0)
        {
            x = WorldConstants.WrapX(x, circ);
            z = WorldConstants.WrapZ(z, circ);
        }

        _meta.HunterShuttleSites[bodyId] = new HunterShuttleSiteRecord
        {
            State = HunterShuttleSiteRecord.Pending,
            X = x,
            Z = z,
            Seed = seed,
            LandedAt = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        _hunterSiteRuntime.Remove(bodyId);
        _repo.SaveMetadata(_meta);
        return true;
    }

    /// <summary>What the camp's distance is measured from: the base of a pilot of this instance on the body, else any base
    /// there, else the body's first landing pad (where a ship comes down), else the origin.</summary>
    private (int X, int Z) HunterSiteAnchor(string bodyId, SpaceInstance? instance)
    {
        ServerBase? home = null;
        if (instance is not null)
        {
            home = _bases.FirstOrDefault(b => b.Planet == bodyId && instance.Players.Contains(b.OwnerId));
        }

        home ??= _bases.FirstOrDefault(b => b.Planet == bodyId);
        if (home is not null)
        {
            return (home.Cell.X, home.Cell.Z);
        }

        var pads = PadsForBody(bodyId);
        return pads.Count > 0 ? (pads[0].CenterX, pads[0].CenterZ) : (0, 0);
    }

    /// <summary>Per-world tick step (1 Hz per camp): stamps a pending camp the first time someone is on the body, and runs a
    /// stamped one while someone is near it. Nothing at all on a body without a camp.</summary>
    private void TickHunterSites(double dt)
    {
        if (_world.Planet.Void
            || !_meta.HunterShuttleSites.TryGetValue(_world.LocationId, out var rec)
            || rec.State == HunterShuttleSiteRecord.Cleared)
        {
            return;
        }

        var rt = HunterSiteRuntimeFor(_world.LocationId);
        rt.Clock += dt;
        if (rt.Clock < 1.0)
        {
            return;
        }

        double step = rt.Clock;
        rt.Clock = 0;
        RunHunterSitePass(rec, rt, step);
    }

    /// <summary>The camp's runtime on the active world — started over when the world was reloaded (its machines went with it).</summary>
    private HunterSiteRuntime HunterSiteRuntimeFor(string bodyId)
    {
        if (!_hunterSiteRuntime.TryGetValue(bodyId, out var rt) || !ReferenceEquals(rt.World, _worlds.Active))
        {
            rt = new HunterSiteRuntime { World = _worlds.Active };
            _hunterSiteRuntime[bodyId] = rt;
        }

        return rt;
    }

    private void RunHunterSitePass(HunterShuttleSiteRecord rec, HunterSiteRuntime rt, double step)
    {
        var present = _hunterSitePresent;
        var onFoot = _hunterSiteOnFoot;
        present.Clear();
        onFoot.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId))
            {
                present.Add(s);
                if (!s.State.AboardShip)
                {
                    onFoot.Add(s);
                }
            }
        }

        if (present.Count == 0)
        {
            return;
        }

        if (rec.State == HunterShuttleSiteRecord.Pending)
        {
            MaterializeHunterSite(rec, present);
            return;
        }

        if (onFoot.Count > 0)
        {
            TickActiveHunterSite(rec, rt, onFoot, step);
        }
    }

    /// <summary>The awake camp: the guard's bookkeeping, VEGA's first word, the clearing check, the guard and the docks.</summary>
    private void TickActiveHunterSite(HunterShuttleSiteRecord rec, HunterSiteRuntime rt, List<PlayerSession> onFoot, double step)
    {
        TrackHunterGuard(rec, rt, onFoot);
        var centre = HunterSiteCentre(rec);
        bool near = false;
        foreach (var s in onFoot)
        {
            if (WrapDistSq(s.State.Position, centre) <= HunterSiteNearRange * HunterSiteNearRange)
            {
                near = true;
                WarnOfHunterSite(s, rec);
            }
        }

        if (!near)
        {
            return;
        }

        if (WorkingHunterDocks(rec) == 0)
        {
            ClearHunterSite(rec, onFoot);
            return;
        }

        if (!PlanetEnemiesActive)
        {
            return; // a camp from before a rule change sleeps: no guard, no releases
        }

        if (rt.GuardId.Length == 0 && !rec.GuardDefeated && !RemnantEra)
        {
            SpawnHunterGuard(rec, rt);
        }

        ReleaseFromHunterDocks(rec, rt, step);
    }

    /// <summary>Follows the guard: where it is, or — gone while a player stood near where it was — defeated for good. Gone
    /// with nobody near means the far-despawn took it; it comes back when someone returns.</summary>
    private void TrackHunterGuard(HunterShuttleSiteRecord rec, HunterSiteRuntime rt, List<PlayerSession> onFoot)
    {
        if (rt.GuardId.Length == 0)
        {
            return;
        }

        foreach (var e in _planetEnemies)
        {
            if (e.Id == rt.GuardId)
            {
                rt.GuardLast = e.Position;
                return;
            }
        }

        rt.GuardId = string.Empty;
        if (rec.GuardDefeated || !PlanetEnemiesActive)
        {
            return;
        }

        foreach (var s in onFoot)
        {
            if (WrapDistSq(s.State.Position, rt.GuardLast) <= HunterGuardWitnessRange * HunterGuardWitnessRange)
            {
                rec.GuardDefeated = true;
                _repo.SaveMetadata(_meta);
                return;
            }
        }
    }

    /// <summary>The hunter on guard, outside the hatch (in the hold when that ground is wet).</summary>
    private void SpawnHunterGuard(HunterShuttleSiteRecord rec, HunterSiteRuntime rt)
    {
        int x = rec.X + HunterShuttleHull.GuardX;
        int z = rec.Z + HunterShuttleHull.GuardZ;
        int feet = HunterGroundFeet(x, z);
        if (MachineWaterDepth(x, z, feet) > 0)
        {
            x = rec.X + HunterShuttleHull.LootCell.X;
            z = rec.Z + HunterShuttleHull.LootCell.Z;
            feet = rec.Y + 1;
        }

        var guard = NewPlanetMachine(new Vector3f(x + 0.5f, feet, z + 0.5f), asDrone: false, tougher: true);
        _planetEnemies.Add(guard);
        rt.GuardId = guard.Id;
        rt.GuardLast = guard.Position;
        BroadcastPlanetEnemies();
    }

    /// <summary>How many of the camp's docks still stand (a real read — the player is near, so the chunks are in anyway).</summary>
    private int WorkingHunterDocks(HunterShuttleSiteRecord rec)
    {
        int working = 0;
        foreach (var c in rec.Spawners)
        {
            working += GuardianBayBlock != 0 && _world.GetBlock(new Vector3i(c.X, c.Y, c.Z)).Value == GuardianBayBlock ? 1 : 0;
        }

        return working;
    }

    /// <summary>Each standing dock's cooldown: the first release a few seconds after a player came near (staggered per dock),
    /// then one every <see cref="HunterDockIntervalSeconds"/>; at its cap the clock holds at zero, so a destroyed robot is
    /// replaced a full interval later, never at once.</summary>
    private void ReleaseFromHunterDocks(HunterShuttleSiteRecord rec, HunterSiteRuntime rt, double step)
    {
        int n = rec.Spawners.Count;
        if (rt.DockTimers.Length != n)
        {
            rt.DockTimers = new double[n];
            rt.DockReleased = new int[n];
            rt.DockRobots = new HashSet<string>[n];
            for (int i = 0; i < n; i++)
            {
                rt.DockRobots[i] = new HashSet<string>(System.StringComparer.Ordinal);
            }
        }

        int cap = HunterDockCap;
        for (int i = 0; i < n; i++)
        {
            var c = rec.Spawners[i];
            var cell = new Vector3i(c.X, c.Y, c.Z);
            if (_world.GetBlock(cell).Value != GuardianBayBlock)
            {
                continue; // mined — this one is done
            }

            if (PruneHunterDockRobots(rt.DockRobots[i]) >= cap)
            {
                rt.DockTimers[i] = 0;
                continue;
            }

            rt.DockTimers[i] += step;
            double due = rt.DockReleased[i] == 0 ? HunterDockFirstSeconds + i * HunterDockStaggerSeconds : HunterDockIntervalSeconds;
            if (rt.DockTimers[i] < due)
            {
                continue;
            }

            if (ReleaseHunterDockRobot(cell, rt.DockReleased[i], rt.DockRobots[i]))
            {
                rt.DockTimers[i] = 0;
                rt.DockReleased[i]++;
            }
            else
            {
                rt.DockTimers[i] -= HunterDockRetrySeconds;
            }
        }
    }

    /// <summary>Drops the ids of a dock's robots that are no longer about and returns how many are alive.</summary>
    private int PruneHunterDockRobots(HashSet<string> robots)
    {
        if (robots.Count == 0)
        {
            return 0;
        }

        robots.RemoveWhere(id => !_planetEnemies.Any(e => e.Id == id));
        return robots.Count;
    }

    /// <summary>Releases one machine beside a dock: every other time a scan-drone where it can hover (when the world keeps
    /// drones; only drones once the galaxy is pacified — the planet rule), else a walking Guardian robot. The derelict bay's
    /// ring of spots, cue and spark (#2635). False when nothing beside the dock is free.</summary>
    private bool ReleaseHunterDockRobot(Vector3i cell, int released, HashSet<string> robots)
    {
        bool asDrone = Rules.PlanetDrones && (RemnantEra || released % 2 == 1);
        for (int k = 0; k < GuardianReleaseRing.Length; k++)
        {
            var (dx, dz) = GuardianReleaseRing[(k + released) % GuardianReleaseRing.Length];
            int x = cell.X + dx, z = cell.Z + dz;
            for (int dy = 0; dy <= 1; dy++)
            {
                int y = cell.Y + (dy == 0 ? 0 : 1);
                bool drone = asDrone && StandableAt(x, y, z, ScanDroneHover + 2);
                if (!drone && !StandableAt(x, y, z))
                {
                    continue;
                }

                var robot = NewPlanetMachine(new Vector3f(x + 0.5f, y + (drone ? ScanDroneHover : 0), z + 0.5f), drone, tougher: false);
                _planetEnemies.Add(robot);
                robots.Add(robot.Id);
                BroadcastPlanetEnemies();

                float fx = cell.X + 0.5f, fy = cell.Y + 0.5f, fz = cell.Z + 0.5f;
                BroadcastToWorld(new SoundFx { SoundId = GuardianBayCue, X = fx, Y = fy, Z = fz });
                BroadcastToWorld(new WorldFx { Kind = GuardianBayCue, X = fx, Y = fy, Z = fz, Strength = 1f });
                return true;
            }
        }

        return false;
    }

    /// <summary>VEGA's word the first time a player comes near a camp (once per player and camp).</summary>
    private void WarnOfHunterSite(PlayerSession s, HunterShuttleSiteRecord rec)
    {
        string key = "huntersite:" + _world.LocationId + ":" + rec.X + ":" + rec.Z;
        if (s.State.Milestones.Add(key))
        {
            SendVegaLine(s, "vega.hunter_site.near", 2);
            _repo.SavePlayer(s.State);
        }
    }

    /// <summary>Every dock is gone: the camp is cleared for good, a loot crate stands in the hold (once — the structure-loot
    /// ledger), VEGA cheers, and the map marker goes.</summary>
    private void ClearHunterSite(HunterShuttleSiteRecord rec, List<PlayerSession> onFoot)
    {
        rec.State = HunterShuttleSiteRecord.Cleared;
        var loot = new Vector3f(rec.X + HunterShuttleHull.LootCell.X + 0.5f, rec.Y + HunterShuttleHull.LootCell.Y + 0.5f,
            rec.Z + HunterShuttleHull.LootCell.Z + 0.5f);
        SpawnStructureLoot("hunter_shuttle", "chest", loot, new System.Random(StableHashing.Seed32("huntersite-loot:" + _world.LocationId, rec.Seed)));
        _repo.SaveMetadata(_meta);
        var centre = HunterSiteCentre(rec);
        foreach (var s in onFoot)
        {
            if (WrapDistSq(s.State.Position, centre) <= HunterSiteNearRange * HunterSiteNearRange)
            {
                SendVegaLine(s, "vega.hunter_site.cleared", 2);
            }
        }

        BroadcastPlanetPois();
        _log.Info($"The hunter shuttle camp on '{_world.LocationId}' at {rec.X},{rec.Y},{rec.Z} was cleared.");
    }

    /// <summary>The middle of a stamped camp's hull (at its deck, one above the floor row).</summary>
    private static Vector3f HunterSiteCentre(HunterShuttleSiteRecord rec)
        => new(rec.X + HunterShuttleHull.Width / 2f, rec.Y + 1, rec.Z + HunterShuttleHull.Length / 2f);

    /// <summary>The live planet-map marker of the active body's open camp (planned or stamped), named in the receiver's
    /// language — derived from the record on every build, never revealed separately.</summary>
    private void AppendHunterSitePoi(PlayerSession session, List<NetPoi> pois)
    {
        if (_world is null || !_meta.HunterShuttleSites.TryGetValue(_world.LocationId, out var rec)
            || rec.State == HunterShuttleSiteRecord.Cleared)
        {
            return;
        }

        bool stamped = rec.State == HunterShuttleSiteRecord.Active;
        pois.Add(new NetPoi
        {
            Type = HunterSitePoiType,
            Name = Localize(session.Locale, "poi.hunter_shuttle"),
            X = stamped ? HunterSiteCentre(rec).X : rec.X + 0.5f,
            Z = stamped ? HunterSiteCentre(rec).Z : rec.Z + 0.5f,
        });
    }
}
