// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>How busy a star system's lanes are with peaceful NPC trader traffic. Deterministic per system.</summary>
public enum TraderTraffic
{
    None,
    Rare,
    Often,
}

/// <summary>Where a trader is in its visit lifecycle (server-only).</summary>
internal enum NpcTraderPhase
{
    Cruising,   // heading for its destination (a station to dock at, or an inner waypoint to pass)
    Departing,  // heading back out to the system edge to warp away
    Done,       // reached its end this tick — removed after the sweep
}

/// <summary>
/// A peaceful NPC trader ship cruising a space instance. Transient + server-only: it is never persisted,
/// never hostile, never damages anyone, and cannot be targeted/attacked. It is rendered on every client in
/// the instance through the SAME remote-ship path a real pilot uses (a synthetic <see cref="NetSpacePlayer"/>
/// pose plus a "ship_remote" <see cref="SpaceShipDesign"/>), so it shows the real voxel hull of an in-game
/// ship type with no extra client code.
/// </summary>
internal sealed class NpcTrader
{
    public string Id = string.Empty;
    public string ShipType = "starter";
    public string Name = string.Empty;
    public int HullRgb;
    public Vector3f Pos;
    public Vector3f Target;
    public float Yaw;
    public NpcTraderPhase Phase = NpcTraderPhase.Cruising;

    /// <summary>The station body id this trader is flying in to dock at — empty = no station leg.</summary>
    public string DestStationId = string.Empty;

    /// <summary>The planet/moon body id this trader will try to LAND on when it reaches the inner system
    /// (if a landing pad is free) — empty = a pure fly-through visit.</summary>
    public string DestBodyId = string.Empty;

    /// <summary>Structure id of the trader's voxel hull (the flight view keys remote designs off this).</summary>
    public string StructureId => "ship:npc:" + Id;

    /// <summary>Synthetic "player id" for the trader's flight-view pose (kept distinct from real player ids).</summary>
    public string PoseId => "npc:" + Id;
}

/// <summary>
/// Peaceful NPC trader spaceships — ambient traffic that makes space feel alive. Traders warp into an
/// occupied space instance (scaled by the system's traffic level), fly to a station and dock — their pilot
/// then appears on the station as a merchant you can trade with — or pass through and warp out. Everything is
/// server-authoritative and transient; clients just render the reported poses.
/// </summary>
public sealed partial class GameServer
{
    private const float Rad2Deg = 57.29578f;

    // Tuning — all server-side, no content needed.
    private const float TraderArriveRadius = 260f; // warp-in distance from the scene centre
    private const float TraderExitRadius = 320f;    // warp-out distance
    private const float TraderCruiseSpeed = 22f;    // units/sec (a touch quicker than a UFO so they traverse)
    private const float TraderDockRange = 16f;      // how close to a station contact counts as "docked"
    private const float TraderWaypointRange = 6f;   // reached an inner/exit waypoint
    private const int TraderCapOften = 3;           // max concurrent traders in a busy system's instance
    private const int TraderCapRare = 1;            // max concurrent traders in a quiet system's instance

    /// <summary>A docked merchant lingers on the station at least this long (7 minutes) — #1904: the old
    /// 150–300 s was often over before a player had even flown across and boarded.</summary>
    internal const double TraderDwellMin = 420.0;

    /// <summary>…and at most this long (12 minutes), then is gone.</summary>
    internal const double TraderDwellMax = 720.0;

    private readonly System.Random _traderRng = new();
    private int _nextTraderId = 1;

    /// <summary>Friendly hull tints so the peaceful traders read as a varied civilian fleet.</summary>
    private static readonly int[] TraderHulls =
        { 0xC7A35A, 0x6E8FB8, 0xB86E6E, 0x6FB890, 0x9A7FB8, 0xB8A36F, 0x7FA9B8, 0xC98F5A };

    /// <summary>Stations a trader has docked at recently — its merchant is then spawned for anyone who
    /// boards that station until the dwell expires. Keyed by station body id; survives the flight instance.</summary>
    private readonly Dictionary<string, VisitingTrader> _visitingTraders = new();

    private sealed class VisitingTrader
    {
        public string Name = string.Empty;
        public string Theme = "traders";
        public uint SkinRgb;
        public uint OutfitRgb;
        public double ExpiresAt;
    }

    /// <summary>Planets/moons a trader has LANDED on — its parked ship + pilot are (re)materialized on that
    /// body's surface world while the dwell lasts, and its landing pad is reserved. One per body at a time.
    /// Keyed by body location id; the authoritative record (survives the body world being unloaded).</summary>
    private readonly Dictionary<string, NpcLandedTrader> _landedTraders = new();

    private sealed class NpcLandedTrader
    {
        public string Id = string.Empty;

        /// <summary>The body this trader is parked on (#1680). The map is keyed by it, but every cleanup path
        /// used to reach for the ACTIVE world's id instead — which is the trader's body only by accident of
        /// who is being ticked, and left parked hulls behind on the pads it thought it had freed.</summary>
        public string BodyId = string.Empty;
        public string ShipType = "starter";
        public string Name = string.Empty;
        public int HullRgb;
        public uint SkinRgb;
        public uint OutfitRgb;
        public int PadIndex;
        public double ExpiresAt;

        /// <summary>Uptime the ship sets down (#2603): a trader the planet-side scheduler sent is on its 20 s approach
        /// until then — the pad is reserved, nothing stands on it yet. 0 = landed on registration (the space path).</summary>
        public double TouchdownAt;

        /// <summary>The planet-side arrival (#2603): announce the touchdown (toast, radio / VEGA) once it happened.</summary>
        public bool AnnounceOnTouchdown;
        public bool ArrivalFxPending;   // play the descent FX once, the moment it touches down with watchers present
        public int PilotNpcId;          // the pilot NPC's id in the body world (for removal on departure)
        public Vector3f PilotPos;       // where the pilot stands (for the "near a trader" barter check)

        /// <summary>Uptime a player was last within <see cref="TraderStayNearRadius"/> of the pilot or ship
        /// (#1904) — an expired trader lifts off only once this lies <see cref="TraderLeaveGraceSeconds"/> back.</summary>
        public double LastPlayerNearAt = double.NegativeInfinity;

        public string OwnerId => "npc:" + Id;
        public string StructureId => "ship:npc:" + Id;
    }

    /// <summary>A landed trader stays on the surface at least this long (10 minutes) — #1904: the old 180–360 s
    /// was often gone before a player had walked over from a pad hundreds of blocks away.</summary>
    internal const double TraderLandDwellMin = 600.0;

    /// <summary>…and at most this long (15 minutes) before it WANTS to leave; it still waits for nearby players.</summary>
    internal const double TraderLandDwellMax = 900.0;

    /// <summary>A player on the body this close (blocks) to a landed trader's pilot or parked ship holds it on the
    /// ground once its time is up (#1904) — a walk-over or a trade in progress is never cut off. Well past the
    /// 4-block barter reach, so someone browsing the trade window always counts.</summary>
    internal const float TraderStayNearRadius = 32f;

    /// <summary>How long nobody may have been near an expired landed trader before it lifts off (#1904) — a
    /// player stepping back to fetch goods from their ship finds it still there.</summary>
    internal const double TraderLeaveGraceSeconds = 30.0;

    /// <summary>Planet-POI type of a landed trader's live map marker (#1904). Built fresh from
    /// <see cref="_landedTraders"/> for every POI list — never revealed or persisted.</summary>
    internal const string TraderShipPoiType = "trader_ship";

    // ------------------------------------------------------------------ traffic level

    /// <summary>Deterministic ambient-traffic level for a star system (None / Rare / Often) — stable from the
    /// world seed + system id, so no persistence is needed and a given save always feels the same. Systems
    /// with a station are trade hubs and lean busier. A completed SPS relay (#1125 F-2) lifts the ambient
    /// level one step — the network draws traders, the promised world effect of rebuilding it.</summary>
    private TraderTraffic TrafficFor(string systemId)
    {
        var ambient = AmbientTrafficFor(systemId);
        if (ambient != TraderTraffic.Often && _relaySystems.Contains(systemId))
        {
            // Deliberately overrides the sparse-archetype cap below (decision 2026-08-21): a relay CHANGES
            // the system's character — even a lone giant or a belt becomes a stop on the network's routes.
            return ambient == TraderTraffic.None ? TraderTraffic.Rare : TraderTraffic.Often;
        }

        return ambient;
    }

    private TraderTraffic AmbientTrafficFor(string systemId)
    {
        if (string.IsNullOrEmpty(systemId))
        {
            return TraderTraffic.None;
        }

        var system = _galaxy.Systems.FirstOrDefault(s => s.Id == systemId);
        bool hasStation = system?.Bodies.Any(b => b.Kind == CelestialKind.SpaceStation && !IsDerelictId(b.Id)) ?? false; // #2632: no freighter calls at a derelict

        uint h = (uint)(_meta.Seed ^ WorldGenerator.StableHash("traffic:" + systemId));
        int bucket = (int)(h % 100u);

        // The system archetype (#547) sets the trade character; Standard (and every pre-variance save)
        // keeps the classic buckets. Sparse archetypes never exceed Rare — a lone outpost station out
        // there shouldn't turn a backwater into a shipping lane.
        switch (SystemArchetypeOf(systemId))
        {
            case SystemArchetype.Hub:
                return TraderTraffic.Often; // civilised space: the lanes are always busy
            case SystemArchetype.Desolate:
                return TraderTraffic.None; // nobody trades with empty space
            case SystemArchetype.PirateHaven:
                return bucket < 60 ? TraderTraffic.None : TraderTraffic.Rare; // only the brave run this route
            case SystemArchetype.LoneGiant:
            case SystemArchetype.Belt:
                // A lone outpost station still draws the odd freighter, but never a busy lane.
                return hasStation || bucket >= 30 ? TraderTraffic.Rare : TraderTraffic.None;
        }

        var level = bucket < 30 ? TraderTraffic.None : bucket < 75 ? TraderTraffic.Rare : TraderTraffic.Often;

        if (hasStation)
        {
            level = level == TraderTraffic.None ? TraderTraffic.Rare : TraderTraffic.Often; // hubs draw trade
        }

        return level;
    }

    /// <summary>Test/inspection: the ambient-traffic level a system rolls.</summary>
    public string TrafficLevelForTest(string systemId) => TrafficFor(systemId).ToString();

    /// <summary>Test/inspection: number of NPC traders currently flying in a player's space instance.</summary>
    public int TraderCountForTest(string playerId)
        => _playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst)
            ? inst.Traders.Count
            : 0;

    /// <summary>Test hook: force-spawn a trader into a player's instance (deterministic enough for assertions).</summary>
    public bool SpawnTraderForTest(string playerId)
    {
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            return SpawnTrader(inst);
        }

        return false;
    }

    // ------------------------------------------------------------------ per-instance tick

    private void TickSpaceTraders(SpaceInstance instance, double dt)
    {
        string locationId = instance.Id.StartsWith("space:", System.StringComparison.Ordinal)
            ? instance.Id.Substring(6)
            : instance.Id;
        var body = _galaxy.FindBody(locationId);
        string systemId = body?.SystemId ?? _galaxy.Systems.FirstOrDefault()?.Id ?? string.Empty;
        var traffic = TrafficFor(systemId);

        if (!instance.TraderScheduleInit)
        {
            instance.TraderScheduleInit = true;
            instance.NextTraderSpawnAt = _uptime + TraderSpawnGap(traffic, first: true);
        }

        int cap = traffic switch
        {
            TraderTraffic.Often => TraderCapOften,
            TraderTraffic.Rare => TraderCapRare,
            _ => 0,
        };

        if (traffic != TraderTraffic.None && instance.Traders.Count < cap && _uptime >= instance.NextTraderSpawnAt)
        {
            SpawnTrader(instance);
            instance.NextTraderSpawnAt = _uptime + TraderSpawnGap(traffic, first: false);
        }

        bool moved = MoveTraders(instance, dt);
        instance.TraderSyncTimer += dt;
        if (moved && instance.TraderSyncTimer >= 0.15)
        {
            instance.TraderSyncTimer = 0;
            BroadcastSpaceState(instance);
        }
    }

    private double TraderSpawnGap(TraderTraffic traffic, bool first)
    {
        double r = _traderRng.NextDouble();
        return traffic switch
        {
            TraderTraffic.Often => (first ? 8.0 : 25.0) + r * 45.0,    // ~25–70 s between arrivals
            TraderTraffic.Rare => (first ? 25.0 : 90.0) + r * 150.0,   // ~90–240 s, often empty
            _ => double.PositiveInfinity,
        };
    }

    // ------------------------------------------------------------------ spawn

    /// <summary>Coins a trader (#2603, shared by the flight scene and the planet-side arrivals): a ship type — ANY defined
    /// non-starter type, weighted toward roomy cargo hulls so traffic reads as traders (enumerating
    /// <c>GameContent.Ships</c> means new ship types appear automatically) — a pilot's name and a hull tint, all drawn from
    /// <paramref name="rng"/> in that order. Null when there is no ship content at all.</summary>
    private NpcTrader? CoinTrader(System.Random rng)
    {
        var pool = _content.Ships.Values.Where(s => s.Key != "starter").ToList();
        if (pool.Count == 0)
        {
            pool = _content.Ships.Values.ToList();
        }

        if (pool.Count == 0)
        {
            return null; // no ship content at all → nothing to fly
        }

        var def = WeightedShip(pool, rng);
        return new NpcTrader
        {
            Id = "t" + _nextTraderId++,
            ShipType = def.Key,
            Name = NameGenerator.Person(rng),
            HullRgb = TraderHulls[rng.Next(TraderHulls.Length)],
        };
    }

    private bool SpawnTrader(SpaceInstance instance)
    {
        if (CoinTrader(_traderRng) is not { } trader)
        {
            return false;
        }

        // Arrive on the system edge, anywhere around the orbital plane.
        double ang = _traderRng.NextDouble() * System.Math.PI * 2;
        float y = (float)(_traderRng.NextDouble() * 70 - 25);
        trader.Pos = new Vector3f(
            (float)System.Math.Cos(ang) * TraderArriveRadius, y, (float)System.Math.Sin(ang) * TraderArriveRadius);

        // Destination: dock at a station contact (about half the time, if one exists), else head into the inner
        // system to LAND on a planet/moon (if a pad is free on arrival) or simply pass through and warp out.
        var stations = instance.Entities.Where(e => e.Kind == CombatEntityKind.SpaceStation && !IsDerelictId(e.Id)).ToList(); // #2632
        if (stations.Count > 0 && _traderRng.NextDouble() < 0.5)
        {
            var target = stations[_traderRng.Next(stations.Count)];
            trader.DestStationId = target.Id;
            // #1917: a generated station's real hull is in the way of its middle — the freighter heads for the hangar mouth.
            trader.Target = _stationsById.TryGetValue(target.Id, out var st) && st.Hull != null
                ? StationDockPoint(target.Id, standOff: 2f)
                : target.Position;
        }
        else
        {
            trader.DestBodyId = PickLandableBody(instance); // empty → pure fly-through
            double a2 = _traderRng.NextDouble() * System.Math.PI * 2;
            float r2 = 40f + (float)_traderRng.NextDouble() * 60f;
            trader.Target = new Vector3f(
                (float)System.Math.Cos(a2) * r2, (float)(_traderRng.NextDouble() * 30 - 10), (float)System.Math.Sin(a2) * r2);
        }

        trader.Phase = NpcTraderPhase.Cruising;
        FaceTarget(trader);
        instance.Traders.Add(trader);

        // Cache + hand out the trader's real voxel ship (rendered exactly like a remote player's ship). Storing
        // it in instance.Structures means players who ENTER later get it via the existing cross-send.
        var design = BuildNpcShipStructure(trader.StructureId, trader.ShipType);
        design.Position = trader.Pos;
        instance.Structures[design.Id] = design;
        foreach (var pid in instance.Players)
        {
            if (FindSessionByPlayerId(pid) is { } s)
            {
                SendShipDesign(s, design, "ship_remote");
            }
        }

        BroadcastWarpFx(instance, trader.Pos, arriving: true);
        BroadcastSpaceState(instance);
        _log.Info($"NPC trader '{trader.Name}' ({trader.ShipType}) warped into {instance.Id}"
            + (trader.DestStationId.Length > 0 ? $" → docking {trader.DestStationId}." : " (passing through)."));
        return true;
    }

    private ShipDefinition WeightedShip(List<ShipDefinition> pool, System.Random rng)
    {
        int total = pool.Sum(s => System.Math.Max(1, _content.StartCargoSlots(s)));
        int roll = rng.Next(total);
        foreach (var s in pool)
        {
            roll -= System.Math.Max(1, _content.StartCargoSlots(s));
            if (roll < 0)
            {
                return s;
            }
        }

        return pool[pool.Count - 1];
    }

    // ------------------------------------------------------------------ movement + phases

    private bool MoveTraders(SpaceInstance instance, double dt)
    {
        if (instance.Traders.Count == 0)
        {
            return false;
        }

        bool moved = false;
        List<NpcTrader>? finished = null;
        foreach (var t in instance.Traders)
        {
            if (t.Phase == NpcTraderPhase.Done)
            {
                (finished ??= new List<NpcTrader>()).Add(t);
                continue;
            }

            float dx = t.Target.X - t.Pos.X, dy = t.Target.Y - t.Pos.Y, dz = t.Target.Z - t.Pos.Z;
            float dist = (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist > 0.001f)
            {
                float step = System.Math.Min((float)(TraderCruiseSpeed * dt), dist) / dist;
                t.Pos = new Vector3f(t.Pos.X + dx * step, t.Pos.Y + dy * step, t.Pos.Z + dz * step);
                t.Yaw = (float)System.Math.Atan2(dx, dz) * Rad2Deg; // degrees — the client rotates by Euler Y
                moved = true;
            }

            switch (t.Phase)
            {
                case NpcTraderPhase.Cruising:
                    if (!string.IsNullOrEmpty(t.DestStationId))
                    {
                        if (dist <= TraderDockRange)
                        {
                            DockTrader(instance, t);
                            t.Phase = NpcTraderPhase.Done;
                            (finished ??= new List<NpcTrader>()).Add(t);
                        }
                    }
                    else if (dist <= TraderWaypointRange)
                    {
                        if (!string.IsNullOrEmpty(t.DestBodyId) && TryLandTraderOnBody(instance, t))
                        {
                            t.Phase = NpcTraderPhase.Done; // descended to the surface — it leaves the flight view
                            (finished ??= new List<NpcTrader>()).Add(t);
                        }
                        else
                        {
                            SetExitTarget(t); // no pad free / no landable body → pass through and warp out
                            t.Phase = NpcTraderPhase.Departing;
                        }
                    }

                    break;

                case NpcTraderPhase.Departing:
                    float fromCentre = (float)System.Math.Sqrt(t.Pos.X * t.Pos.X + t.Pos.Y * t.Pos.Y + t.Pos.Z * t.Pos.Z);
                    if (dist <= TraderWaypointRange || fromCentre >= TraderExitRadius)
                    {
                        BroadcastWarpFx(instance, t.Pos, arriving: false);
                        t.Phase = NpcTraderPhase.Done;
                        (finished ??= new List<NpcTrader>()).Add(t);
                    }

                    break;
            }
        }

        if (finished != null)
        {
            foreach (var t in finished)
            {
                RemoveTrader(instance, t);
            }

            BroadcastSpaceState(instance);
        }

        return moved;
    }

    /// <summary>The trader reached a station: it slips into the bay (warp-out flash + leaves the flight view)
    /// and registers its pilot as a visiting merchant on that station for a while.</summary>
    private void DockTrader(SpaceInstance instance, NpcTrader t)
    {
        BroadcastWarpFx(instance, t.Pos, arriving: false);
        RegisterVisitingTrader(t.DestStationId, t);
        _log.Info($"NPC trader '{t.Name}' docked at station {t.DestStationId}.");
    }

    private void RemoveTrader(SpaceInstance instance, NpcTrader t)
    {
        instance.Traders.Remove(t);
        instance.Structures.Remove(t.StructureId);
        // The client drops the avatar + its meshed hull when the pose disappears from the next SpaceState.
    }

    private void FaceTarget(NpcTrader t)
    {
        float dx = t.Target.X - t.Pos.X, dz = t.Target.Z - t.Pos.Z;
        t.Yaw = (float)System.Math.Atan2(dx, dz) * Rad2Deg;
    }

    private void SetExitTarget(NpcTrader t)
    {
        double ang = _traderRng.NextDouble() * System.Math.PI * 2;
        float y = (float)(_traderRng.NextDouble() * 70 - 25);
        t.Target = new Vector3f(
            (float)System.Math.Cos(ang) * TraderExitRadius, y, (float)System.Math.Sin(ang) * TraderExitRadius);
    }

    // ------------------------------------------------------------------ flight-view poses + warp FX

    /// <summary>Appends the instance's NPC traders to the remote-pose array a <see cref="SpaceState"/> carries,
    /// so the flight view renders them as ships alongside the real pilots.</summary>
    private NetSpacePlayer[] AppendTraderPoses(NetSpacePlayer[] players, SpaceInstance instance)
    {
        if (instance.Traders.Count == 0)
        {
            return players;
        }

        var list = new List<NetSpacePlayer>(players);
        foreach (var t in instance.Traders)
        {
            if (t.Phase == NpcTraderPhase.Done)
            {
                continue;
            }

            list.Add(new NetSpacePlayer
            {
                PlayerId = t.PoseId,
                Name = t.Name,
                X = t.Pos.X,
                Y = t.Pos.Y,
                Z = t.Pos.Z,
                Yaw = t.Yaw,
                Eva = false,
                Hull = t.HullRgb,
            });
        }

        return list.ToArray();
    }

    private void BroadcastWarpFx(SpaceInstance instance, Vector3f pos, bool arriving, bool raider = false)
        => BroadcastToInstance(instance, new SpaceWarpFx { X = pos.X, Y = pos.Y, Z = pos.Z, Arriving = arriving, Raider = raider });

    // ------------------------------------------------------------------ docked merchant (on the station)

    private void RegisterVisitingTrader(string stationId, NpcTrader t)
    {
        if (string.IsNullOrEmpty(stationId))
        {
            return;
        }

        // A merchant with a varied civilian look; deterministic body colours from its name. #2630: it brings goods of its
        // own, drawn per visit — never the station shop's "traders", so the visitor sells what the shop does not.
        var look = new System.Random(unchecked((int)WorldGenerator.StableHash("trader-look:" + t.Name)));
        uint[] skins = { 0xF2C9A0, 0xD9A066, 0x8D5524, 0xC68642, 0xFFDBAC };
        uint[] outfits = { 0x2E5E8C, 0x6A4C93, 0xC9A227, 0x3A7D5A };
        _visitingTraders[stationId] = new VisitingTrader
        {
            Name = t.Name,
            Theme = VisitingTraderTheme("docked:" + stationId + ":" + t.Id + ":" + t.Name, StationShopTheme),
            SkinRgb = skins[look.Next(skins.Length)],
            OutfitRgb = outfits[look.Next(outfits.Length)],
            ExpiresAt = _uptime + TraderDwellMin + _traderRng.NextDouble() * (TraderDwellMax - TraderDwellMin),
        };
    }

    /// <summary>Called from <see cref="SpawnStationNpcs"/>: if a trader has recently docked here and is still
    /// dwelling, stand its pilot at the trade post as a merchant so the existing station-vendor barter works.</summary>
    private void MaybeSpawnVisitingTrader(BoardableStation station)
    {
        if (!_visitingTraders.TryGetValue(station.Id, out var vt))
        {
            return;
        }

        if (vt.ExpiresAt <= _uptime)
        {
            _visitingTraders.Remove(station.Id); // the merchant has moved on
            return;
        }

        // Stand the visitor BESIDE a vendor marker so NearSpaceStationVendor finds the post — the themed-market barter
        // then accepts its trades (#2626: any vendor in reach selling a deal counts, so its own goods trade even with the
        // shop's keeper a step closer). Fall back to the spawn point.
        var marker = station.Markers.FirstOrDefault(m => m.Type == "vendor");
        var at = marker.Type == "vendor"
            ? new Vector3f(marker.Pos.X + 1.5f, (float)System.Math.Floor(marker.Pos.Y), marker.Pos.Z + 0.5f)
            : station.Spawn;

        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash("visiting-npc:" + station.Id + ":" + vt.Name)));
        var npc = MakeNpc("vendor", vt.Theme, robotic: false, at, rng);
        npc.Name = vt.Name;
        npc.SkinRgb = vt.SkinRgb;
        npc.OutfitRgb = vt.OutfitRgb;
        _npcs.Add(npc);
        _log.Info($"Visiting trader '{vt.Name}' is trading at station '{station.Name}'.");
    }

    // ------------------------------------------------------------------ planet landing (P3)

    /// <summary>Picks a planet/moon in this instance's system that could host a landing — not void, and not
    /// already hosting a landed trader. Prefers the body the instance is anchored on (a player is likely to
    /// return there). Returns empty when none is suitable (the trader then just passes through).</summary>
    private string PickLandableBody(SpaceInstance instance)
    {
        string locationId = instance.Id.StartsWith("space:", System.StringComparison.Ordinal)
            ? instance.Id.Substring(6)
            : instance.Id;
        var here = _galaxy.FindBody(locationId);
        string systemId = here?.SystemId ?? _galaxy.Systems.FirstOrDefault()?.Id ?? string.Empty;
        var system = _galaxy.Systems.FirstOrDefault(s => s.Id == systemId);
        if (system is null)
        {
            return string.Empty;
        }

        var candidates = system.Bodies
            .Where(b => (b.Kind == CelestialKind.Planet || b.Kind == CelestialKind.Moon)
                        && !_landedTraders.ContainsKey(b.Id)
                        && !(_content.GetPlanet(b.PlanetType ?? string.Empty)?.Void ?? false))
            .ToList();
        if (candidates.Count == 0)
        {
            return string.Empty;
        }

        // The world notices your base (#1120): a body with a founded base draws traders — customers live
        // there. The current body still wins (the trader is already headed in), then base worlds, then any.
        var prefer = candidates.FirstOrDefault(b => b.Id == locationId);
        if (prefer is null)
        {
            var baseWorlds = candidates.Where(b => _bases.Any(x => x.Planet == b.Id)).ToList();
            if (baseWorlds.Count > 0 && _traderRng.Next(100) < 70)
            {
                return baseWorlds[_traderRng.Next(baseWorlds.Count)].Id;
            }
        }

        return (prefer ?? candidates[_traderRng.Next(candidates.Count)]).Id;
    }

    /// <summary>The trader reached the inner system: try to land on its chosen body — but ONLY if a dry landing pad
    /// is free there (player- or NPC-occupied pads don't count). It takes the same pad a planet-side arrival takes
    /// (<see cref="NearestFreeDryPad"/>, K12) — the one nearest a base owner's core — so the hail's "parked near your
    /// base" holds. On success it reserves the pad and registers a landed trader (its parked ship + pilot materialize on
    /// that surface); on failure the caller departs.</summary>
    private bool TryLandTraderOnBody(SpaceInstance instance, NpcTrader t)
    {
        var body = _galaxy.FindBody(t.DestBodyId);
        if (body is null || _landedTraders.ContainsKey(t.DestBodyId))
        {
            return false; // gone, or a trader is already parked there (one at a time)
        }

        string planetKey = body.PlanetType ?? _meta.DefaultPlanetType;
        if (_content.GetPlanet(planetKey)?.Void ?? false)
        {
            return false;
        }

        int pad = NearestFreeDryPad(t.DestBodyId, PlayersOnBodySurface(t.DestBodyId), hailedOwnersFirst: true); // respects player + NPC pad use
        if (pad < 0)
        {
            return false; // no dry pad is free — the trader can't set down
        }

        var lt = RegisterLandedTrader(t, t.DestBodyId, pad, _uptime, _traderRng);
        lt.ArrivalFxPending = OccupiedLocations().Contains(t.DestBodyId); // descend with watchers present

        BroadcastWarpFx(instance, t.Pos, arriving: false); // it drops out of the flight scene to descend
        _log.Info($"NPC trader '{t.Name}' ({t.ShipType}) is setting down on {t.DestBodyId} (pad {pad}).");
        NpcRadioOnTraderLanded(t.DestBodyId, t.Name); // #1120: base owners on that world get a hail
        return true;
    }

    /// <summary>Registers a trader as landed on a body (#2603, shared by the flight scene and the planet-side arrivals):
    /// reserves the pad, gives the pilot a look derived from its name, and rolls the stay — <see cref="TraderLandDwellMin"/>
    /// to <see cref="TraderLandDwellMax"/> from <paramref name="touchdownAt"/> on (one draw of <paramref name="dwellRng"/>).
    /// The ship stands on the pad from <paramref name="touchdownAt"/> (the space path: now).</summary>
    private NpcLandedTrader RegisterLandedTrader(NpcTrader t, string bodyId, int pad, double touchdownAt, System.Random dwellRng)
    {
        var look = new System.Random(unchecked((int)WorldGenerator.StableHash("trader-look:" + t.Name)));
        uint[] skins = { 0xF2C9A0, 0xD9A066, 0x8D5524, 0xC68642, 0xFFDBAC };
        uint[] outfits = { 0x2E5E8C, 0x6A4C93, 0xC9A227, 0x3A7D5A };
        var lt = new NpcLandedTrader
        {
            Id = t.Id,
            BodyId = bodyId,
            ShipType = t.ShipType,
            Name = t.Name,
            HullRgb = t.HullRgb,
            SkinRgb = skins[look.Next(skins.Length)],
            OutfitRgb = outfits[look.Next(outfits.Length)],
            PadIndex = pad,
            TouchdownAt = touchdownAt,
            ExpiresAt = touchdownAt + TraderLandDwellMin + dwellRng.NextDouble() * (TraderLandDwellMax - TraderLandDwellMin),
        };
        _landedTraders[bodyId] = lt;
        return lt;
    }

    /// <summary>A pad is reserved while a trader is landed on that body (so players never get assigned it).</summary>
    private bool PadReservedByTrader(string locationId, int padIndex)
        => _landedTraders.TryGetValue(locationId, out var lt) && lt.PadIndex == padIndex;

    /// <summary>The name shown for a trader-reserved pad in the landing chooser (or null if not reserved).</summary>
    private string? TraderPadOccupant(string locationId, int padIndex)
        => _landedTraders.TryGetValue(locationId, out var lt) && lt.PadIndex == padIndex ? lt.Name : null;

    /// <summary>The trader that has really set down on the active world, or null. A planet-side arrival still on its
    /// approach (#2603) has only reserved its pad — villagers' talk and VEGA's tips must not count it as landed yet.</summary>
    private NpcLandedTrader? TouchedDownTraderHere()
        => _world is not null && _landedTraders.TryGetValue(_world.LocationId, out var lt) && lt.TouchdownAt <= _uptime ? lt : null;

    /// <summary>Per-world tick (active = a body world): materializes a freshly-landed trader's parked ship +
    /// pilot, and lifts it off again once its dwell has expired AND nobody has been near it for the grace
    /// period (#1904). Setting down and lifting off both re-publish the pad list and the POI list to the
    /// body's players, so their maps show the trader's pad and marker live.</summary>
    private void TickLandedTraders(double dt)
    {
        _ = dt;
        if (_landedTraders.Count == 0 || _world is null)
        {
            return;
        }

        if (!_landedTraders.TryGetValue(_world.LocationId, out var lt))
        {
            return;
        }

        if (AnyPlayerNearLandedTrader(lt))
        {
            lt.LastPlayerNearAt = _uptime;
        }

        if (lt.ExpiresAt <= _uptime)
        {
            // #1904: time is up, but someone walking over, browsing or mid-trade holds it on the ground — it
            // lifts off only after nobody has been near for the grace period. No hard cap: a held trader
            // blocks just its own pad and its body's one trader slot; other traders pick other bodies.
            if (_uptime - lt.LastPlayerNearAt < TraderLeaveGraceSeconds)
            {
                return;
            }

            DepartLandedTrader(lt);
            BroadcastLandedTraderMapChange();
            return;
        }

        if (MaterializeLandedTraderHere(lt))
        {
            BroadcastLandedTraderMapChange();
            if (lt.AnnounceOnTouchdown && lt.PilotNpcId != 0 && _landedTraders.ContainsKey(lt.BodyId))
            {
                lt.AnnounceOnTouchdown = false;
                AnnouncePlanetTraderLanded(lt); // #2603: the ship the players watched come in has set down
            }
        }
    }

    /// <summary>True if a player on the active body (on foot or aboard, not up in space, not an invisible
    /// observer) is within <see cref="TraderStayNearRadius"/> of the trader's pilot or parked ship (#1904). A
    /// trader that never set down has neither, so nobody can hold it.</summary>
    private bool AnyPlayerNearLandedTrader(NpcLandedTrader lt)
    {
        if (!_worlds.Active.LandedShips.TryGetValue(lt.OwnerId, out var rec) || !rec.Placed)
        {
            return false;
        }

        var hull = new Vector3f(
            rec.Origin.X + rec.Structure.Width / 2f, rec.Origin.Y + rec.Structure.Height / 2f, rec.Origin.Z + rec.Structure.Length / 2f);
        double reachSq = (double)TraderStayNearRadius * TraderStayNearRadius;
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Spectating || InSpace(s.State.PlayerId))
            {
                continue;
            }

            if (WrapDistSq(s.State.Position, lt.PilotPos) <= reachSq || WrapDistSq(s.State.Position, hull) <= reachSq)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A trader set down or lifted off the active body (#1904): its pad changed hands and its map marker
    /// appeared or vanished. The pad list used to stay a stale world-entry snapshot for everyone already there
    /// (the #1020 class of bug), so both lists are re-sent to the body's players.</summary>
    private void BroadcastLandedTraderMapChange()
    {
        BroadcastLandingPads();
        BroadcastPlanetPois();
    }

    /// <summary>The live planet-map marker of the trader landed on the active body (#1904), at its pilot, named
    /// "Trader ship &lt;name&gt;" in the receiver's language. Only while its hull actually stands here — a
    /// registered trader that has not set down yet has no pilot position.</summary>
    private void AppendLandedTraderPoi(PlayerSession session, List<NetPoi> pois)
    {
        if (_world is null
            || !_landedTraders.TryGetValue(_world.LocationId, out var lt)
            || !_worlds.Active.LandedShips.TryGetValue(lt.OwnerId, out var rec)
            || !rec.Placed)
        {
            return;
        }

        pois.Add(new NetPoi
        {
            Type = TraderShipPoiType,
            Name = string.Format(Localize(session.Locale, "poi.trader_ship"), lt.Name),
            X = lt.PilotPos.X,
            Z = lt.PilotPos.Z,
        });
    }

    /// <summary>Re-creates a landed trader's parked ship + pilot on the active world if they aren't there yet —
    /// called when a body world (re)loads with one registered. The arriving player's world-entry snapshot
    /// carries the pads and POIs, so nothing is re-broadcast from here.</summary>
    private void MaterializeLandedTraderHere(bool ignoreOverlap = false)
    {
        if (_world is not null && _landedTraders.TryGetValue(_world.LocationId, out var lt) && lt.ExpiresAt > _uptime)
        {
            MaterializeLandedTraderHere(lt, ignoreOverlap);
        }
    }

    /// <summary>Stands a landed trader's hull + pilot on the active world. Returns true when that changed what the
    /// body's maps must show — the hull set down, or the pad was given back because another hull stands there.</summary>
    private bool MaterializeLandedTraderHere(NpcLandedTrader lt, bool ignoreOverlap = false)
    {
        if (_uptime < lt.TouchdownAt)
        {
            return false; // #2603: still on its approach — the pad is reserved, the hull sets down at touchdown
        }

        var world = _worlds.Active;
        if (world.LandedShips.TryGetValue(lt.OwnerId, out var existing) && existing.Placed)
        {
            return false; // already standing on this resident world
        }

        LandingPad? pad = null;
        foreach (var p in _landingPads)
        {
            if (p.Index == lt.PadIndex)
            {
                pad = p;
                break;
            }
        }

        if (pad is null)
        {
            return false; // the pad set isn't built here (e.g. a void world) — shouldn't happen on a landable body
        }

        var s = BuildNpcShipStructure(lt.StructureId, lt.ShipType);
        if (s.Cells.Count == 0)
        {
            return false;
        }

        // #1678: never stamp a hull onto ground another hull already occupies. The pad reservation is derived
        // state and can disagree with what is actually standing there (a player who kept a stale pad claim, a
        // record whose hull outlived its sweep) — and the player walled into the resulting overlap has no way
        // out. Give up the pad instead: this trader simply never sets down.
        var (fx, fz) = FootprintOriginFor(pad, s.Width, s.Length);
        if (!ignoreOverlap && LandedFootprintTaken(fx, fz, s.Width, s.Length, lt.OwnerId))
        {
            _log.Warn($"Trader '{lt.Name}' cannot set down on {lt.BodyId} pad {lt.PadIndex} — another hull stands there; the pad is released.");
            _landedTraders.Remove(lt.BodyId);
            return true;
        }

        // Use the pad's stored ground height (computed + worldgen-levelled at pad build time) rather than a
        // fresh PadGroundY query — the generator may not be configured for this world during the per-world tick.
        int y0 = pad.CenterY;
        var rec = world.LandedFor(lt.OwnerId);
        rec.Structure = s;
        rec.Origin = new Vector3i(pad.CenterX - s.Width / 2, y0 + 1, pad.CenterZ - s.Length / 2);
        rec.Stations.Clear();
        rec.Doors.Clear();
        rec.HealTank = new Vector3f(rec.Origin.X + s.Width / 2 + 0.5f, rec.Origin.Y + 1f, rec.Origin.Z + s.Length / 2 + 0.5f);
        rec.Placed = true;
        BroadcastToWorld(LandedShipMessage(lt.OwnerId, rec, removed: false));

        // The pilot stands a couple of blocks in front of the ship (its window end), feet on the pad surface.
        lt.PilotPos = new Vector3f(pad.CenterX + 0.5f, y0 + 1f, pad.CenterZ + s.Length / 2f + 2.5f);
        var rng = new System.Random(unchecked((int)WorldGenerator.StableHash("landed-pilot:" + lt.Id)));
        var npc = MakeNpc("vendor", LandedTraderTheme(lt), robotic: false, lt.PilotPos, rng); // #2630: its own goods per visit
        npc.Name = lt.Name;
        npc.SkinRgb = lt.SkinRgb;
        npc.OutfitRgb = lt.OutfitRgb;
        _npcs.Add(npc);
        lt.PilotNpcId = npc.Id;
        BroadcastNpcs();

        if (lt.ArrivalFxPending)
        {
            lt.ArrivalFxPending = false; // play the descent once, the moment it touches down with watchers present
            BroadcastNpcShipTransit(_world!.LocationId, lt, new Vector3f(pad.CenterX + 0.5f, y0, pad.CenterZ + 0.5f), landing: true);
        }

        _log.Info($"Trader '{lt.Name}' ({lt.ShipType}) is parked on {_world!.LocationId} (pad {lt.PadIndex}).");
        return true;
    }

    private void DepartLandedTrader(NpcLandedTrader lt)
    {
        // #1680: everything here is keyed on the trader's OWN body. Reaching for the active world's id worked
        // only while the caller happened to be that body's tick — the sweep of unwatched bodies dropped the
        // record while its hull kept standing on the pad it had just declared free.
        RemoveLandedTraderObjects(lt);
        _landedTraders.Remove(lt.BodyId);
        _log.Info($"Trader '{lt.Name}' lifted off {lt.BodyId} — pad {lt.PadIndex} free again.");
    }

    /// <summary>Takes a landed trader's parked hull and pilot back out of its body world (#1680). Works on a
    /// world that is loaded but not active; a world that is not loaded at all holds no runtime objects, so
    /// there is nothing to remove there. The lift-off FX only plays for the body's own watchers.</summary>
    private void RemoveLandedTraderObjects(NpcLandedTrader lt)
    {
        var world = _worlds.Find(lt.BodyId);
        if (world is null)
        {
            return;
        }

        // Every broadcast path is scoped to the ACTIVE world, which is right: the only case where a trader's
        // body is not the active one is the sweep of bodies nobody is on, and there is nobody there to tell.
        bool watched = string.Equals(lt.BodyId, _worlds.Active?.LocationId, System.StringComparison.Ordinal);
        if (world.LandedShips.TryGetValue(lt.OwnerId, out var rec) && rec.Placed)
        {
            if (watched)
            {
                var at = new Vector3f(
                    rec.Origin.X + rec.Structure.Width / 2f + 0.5f, rec.Origin.Y - 1f, rec.Origin.Z + rec.Structure.Length / 2f + 0.5f);
                BroadcastNpcShipTransit(lt.BodyId, lt, at, landing: false); // others see it lift off
            }

            rec.Placed = false;
            world.LandedShips.Remove(lt.OwnerId);
            if (watched)
            {
                BroadcastToWorld(new LandedShipState { PlayerId = lt.OwnerId, StructureId = lt.StructureId, Removed = true });
            }
        }

        // A trader registered but never materialised has no pilot, and its id field is still the default 0 —
        // removing "the NPC with id 0" would take an unrelated one out of the world.
        if (lt.PilotNpcId != 0 && world.Npcs.RemoveAll(n => n.Id == lt.PilotNpcId) > 0)
        {
            lt.PilotNpcId = 0;
            world.NpcListDirty = true; // flushed by that world's own tick (a no-op while nobody is on it)
        }
    }

    /// <summary>Plays a landing/launch animation of a trader's REAL ship for everyone already on the body
    /// (the same third-person FX a player's ship plays — item 38).</summary>
    private void BroadcastNpcShipTransit(string bodyId, NpcLandedTrader lt, Vector3f at, bool landing)
    {
        ShipTransitFx? msg = null;
        SpaceStructure? design = null;
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined || session.CurrentLocationId != bodyId || InSpace(session.State.PlayerId))
            {
                continue;
            }

            msg ??= new ShipTransitFx
            {
                PlayerId = lt.OwnerId,
                Name = lt.Name,
                X = at.X,
                Y = at.Y,
                Z = at.Z,
                Landing = landing,
                Hull = lt.HullRgb,
            };
            design ??= BuildNpcShipStructure(lt.StructureId, lt.ShipType);
            SendShipDesign(session, design, "ship_remote");
            Send(session, msg);
        }
    }

    /// <summary>Hull tint of a landed trader's parked ship (so its <see cref="LandedShipState"/> snapshot shows
    /// the right colour for joiners too), or 0 if not a landed trader.</summary>
    private int NpcLandedHull(string ownerId)
    {
        foreach (var lt in _landedTraders.Values)
        {
            if (lt.OwnerId == ownerId)
            {
                return lt.HullRgb;
            }
        }

        return 0;
    }

    /// <summary>True if the served player is standing at a landed trader's pilot — enables the merchant barter
    /// on a planet surface (the pilot isn't at a settlement/station vendor marker).</summary>
    private bool NearLandedTraderPilot(Shared.State.PlayerState player)
        => _world is not null
           && _landedTraders.TryGetValue(_world.LocationId, out var lt)
           && lt.PilotNpcId != 0 // #2603: a trader still on its approach has no pilot standing anywhere yet
           && WrapDistSq(player.Position, lt.PilotPos) <= 16f; // 4-block reach (squared)

    /// <summary>Drops expired landed-trader records for bodies nobody is on (frees the pad) — the per-world
    /// tick handles occupied bodies with a proper lift-off (and holds a trader while players are near, #1904);
    /// this catches the unwatched ones. A body whose world is not loaded is never ticked (a pilot who
    /// hyperjumped in occupies it from orbit, #1566) and nobody can stand near a trader there, so it counts
    /// as unwatched too — otherwise its trader would hold the pad forever.</summary>
    private void SweepExpiredLandedTraders()
    {
        if (_landedTraders.Count == 0)
        {
            return;
        }

        var occupied = OccupiedLocations();
        List<string>? drop = null;
        foreach (var kv in _landedTraders)
        {
            if (kv.Value.ExpiresAt <= _uptime && (!occupied.Contains(kv.Key) || !_worlds.IsLoaded(kv.Key)))
            {
                (drop ??= new List<string>()).Add(kv.Key);
            }
        }

        if (drop is not null)
        {
            foreach (var b in drop)
            {
                // #1680: the record used to be dropped on its own, leaving a materialised hull standing on the
                // very pad this sweep had just declared free — the next arrival was then stamped on top of it.
                if (_landedTraders.TryGetValue(b, out var lt))
                {
                    RemoveLandedTraderObjects(lt);
                }

                _landedTraders.Remove(b);
            }
        }
    }

    // ------------------------------------------------------------------ planet-side arrivals (#2603)

    /// <summary>How often each occupied body asks whether a trader comes in.</summary>
    private const double PlanetTraderCheckInterval = 5.0;

    /// <summary>The approach: from the decision ("look up!") to the touchdown — the descent FX plays at its end.</summary>
    internal const double PlanetTraderApproachSeconds = 20.0;

    /// <summary>A body with a present base owner draws traders more often (Often/Rare gaps × this).</summary>
    private const double PlanetTraderBaseFactor = 0.75;

    /// <summary>Most characters an AI re-wording of the trader's radio hail may take.</summary>
    private const int TraderHailMaxChars = 140;

    /// <summary>The planet-side scheduler of one body (RAM only): when the next trader comes, when to look again, and
    /// how many came (the seed of the next roll).</summary>
    private sealed class PlanetTraderSchedule
    {
        public double NextAt = double.NaN;
        public double CheckAt;
        public int Count;
    }

    private readonly Dictionary<string, PlanetTraderSchedule> _planetTraderSchedules = new(System.StringComparer.Ordinal);

    /// <summary>
    /// The gap between two planet-side trader arrivals on a body (#2603, E11), from a roll in [0, 1]: a busy system
    /// (Often) 15–25 minutes, a quiet one (Rare) 30–45 minutes — each × <see cref="PlanetTraderBaseFactor"/> while a base
    /// owner is present (customers live there) — and a system without traffic 45–60 minutes, but only for a body with a
    /// present player's base (that IS the base case); without one, never.
    /// </summary>
    internal static double PlanetTraderGap(TraderTraffic traffic, bool presentBase, double roll)
    {
        double r = System.Math.Clamp(roll, 0.0, 1.0);
        return traffic switch
        {
            TraderTraffic.Often => (900.0 + r * 600.0) * (presentBase ? PlanetTraderBaseFactor : 1.0),
            TraderTraffic.Rare => (1800.0 + r * 900.0) * (presentBase ? PlanetTraderBaseFactor : 1.0),
            _ => presentBase ? 2700.0 + r * 900.0 : double.PositiveInfinity,
        };
    }

    /// <summary>
    /// Traders land on planets even when nobody flies (#2603, Guard-registered per world before
    /// <see cref="TickLandedTraders"/>): every <see cref="PlanetTraderCheckInterval"/> s per occupied, loaded, non-void
    /// planet or moon with a player on it (not in space, not observing) and no trader parked — gated on
    /// <see cref="Shared.Configuration.GameRules.NeutralNpcShips"/>. A first sighting arms 0.3–0.6 of the gap; a due
    /// window picks the free dry pad nearest a present player's base (else nearest a player there), reserves it and
    /// brings the ship in over <see cref="PlanetTraderApproachSeconds"/> (toast "look up!"); at touchdown the existing
    /// descent FX plays, the toast says who landed and the base owners hear the radio hail — or VEGA's relay without a
    /// radio. The roll is seeded per world seed, body and arrival count.
    /// </summary>
    private void TickPlanetTraders(double dt)
    {
        _ = dt;
        if (_world is null || !Rules.NeutralNpcShips)
        {
            return;
        }

        string bodyId = _world.LocationId;
        if (!_planetTraderSchedules.TryGetValue(bodyId, out var schedule))
        {
            _planetTraderSchedules[bodyId] = schedule = new PlanetTraderSchedule();
        }

        if (_uptime < schedule.CheckAt)
        {
            return;
        }

        schedule.CheckAt = _uptime + PlanetTraderCheckInterval;
        RunPlanetTraderSchedule(bodyId, schedule, force: false);
    }

    /// <summary>One look at a body's schedule; <paramref name="force"/> (tests) treats the window as due. Returns whether
    /// a trader is on its way in.</summary>
    private bool RunPlanetTraderSchedule(string bodyId, PlanetTraderSchedule schedule, bool force)
    {
        if (_world.Planet?.Void == true || _landedTraders.ContainsKey(bodyId)
            || _galaxy?.FindBody(bodyId) is not { Kind: CelestialKind.Planet or CelestialKind.Moon } body)
        {
            return false;
        }

        var present = PlayersOnBodySurface();
        if (present.Count == 0)
        {
            return false;
        }

        bool presentBase = _bases.Any(b => b.Planet == bodyId && present.Any(s => s.State.PlayerId == b.OwnerId));
        var rng = PlanetTraderRng(bodyId, schedule.Count);
        double gap = PlanetTraderGap(TrafficFor(body.SystemId), presentBase, rng.NextDouble());
        if (double.IsPositiveInfinity(gap))
        {
            schedule.NextAt = double.NaN; // a quiet system and no base owner here: nobody comes (re-armed once one is)
            return false;
        }

        if (double.IsNaN(schedule.NextAt))
        {
            schedule.NextAt = _uptime + gap * (0.3 + rng.NextDouble() * 0.3); // first sighting: not the moment one lands
            if (!force)
            {
                return false;
            }
        }

        if (!force && _uptime < schedule.NextAt)
        {
            return false;
        }

        schedule.Count++;
        schedule.NextAt = _uptime + gap;
        return BringPlanetTraderIn(bodyId, present, rng);
    }

    /// <summary>The joined players standing on the active world (on foot or aboard a landed ship; not in space, not observing).</summary>
    private List<PlayerSession> PlayersOnBodySurface()
    {
        var list = new List<PlayerSession>();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId))
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>The joined players standing on <paramref name="bodyId"/>'s surface, whichever world is active (the flight
    /// scene's landing decision runs while a space instance ticks): not in space, not observing.</summary>
    private List<PlayerSession> PlayersOnBodySurface(string bodyId)
    {
        var list = new List<PlayerSession>();
        foreach (var s in _sessions.Values)
        {
            if (s.Joined && !s.Spectating && s.CurrentLocationId == bodyId && !InSpace(s.State.PlayerId))
            {
                list.Add(s);
            }
        }

        return list;
    }

    /// <summary>The seeded roll of a body's <paramref name="count"/>-th planet-side arrival — a well-mixed seed (#2688), so
    /// consecutive arrivals do not draw in step. Runtime only: an arrival is rolled when it happens, never re-derived.</summary>
    private System.Random PlanetTraderRng(string bodyId, int count)
        => new(StableHashing.Seed32("ptrader:" + bodyId + ":" + count.ToString(System.Globalization.CultureInfo.InvariantCulture), _meta.Seed));

    /// <summary>Decides an arrival: the pad, the trader, the reservation and the "look up!" toast. False when no dry pad is free.</summary>
    private bool BringPlanetTraderIn(string bodyId, List<PlayerSession> present, System.Random rng)
    {
        int pad = NearestFreeDryPad(bodyId, present);
        if (pad < 0 || CoinTrader(rng) is not { } t)
        {
            return false;
        }

        t.DestBodyId = bodyId;
        var lt = RegisterLandedTrader(t, bodyId, pad, _uptime + PlanetTraderApproachSeconds, rng);
        lt.ArrivalFxPending = true; // the descent plays at touchdown — the players are right here
        lt.AnnounceOnTouchdown = true;
        foreach (var s in present)
        {
            Send(s, new ServerMessage { Text = "@srv.trader.approaching" });
        }

        PrefetchTraderHail(bodyId, lt);
        _log.Info($"NPC trader '{t.Name}' ({t.ShipType}) is coming in to land on {bodyId} (pad {pad}, {PlanetTraderApproachSeconds:0} s).");
        return true;
    }

    /// <summary>The free pad (#2603, K12) an arriving trader takes — planet-side and from the flight scene alike: not held
    /// by a player or another trader, not under water, not in lava — the one nearest a present player's base core on this
    /// body, else nearest a player here, so "we're parked near your base" is true. With nobody to be near, the lowest free
    /// dry pad. -1 when none is free.</summary>
    /// <param name="hailedOwnersFirst">The flight scene's landing: nobody here watched it come in, its only "near" claim is
    /// the base owners' hail — so, without a present owner, the base of any joined owner (the hail reaches them wherever
    /// they are, up in space too) anchors the pad before the players standing here.</param>
    private int NearestFreeDryPad(string bodyId, List<PlayerSession> present, bool hailedOwnersFirst = false)
    {
        var anchors = new List<Vector3f>();
        AddBaseAnchors(anchors, bodyId, ownerId => present.Any(s => s.State.PlayerId == ownerId));
        if (anchors.Count == 0 && hailedOwnersFirst)
        {
            AddBaseAnchors(anchors, bodyId, ownerId => FindSessionByPlayerId(ownerId) is { Joined: true, Spectating: false });
        }

        if (anchors.Count == 0)
        {
            anchors.AddRange(present.Select(s => s.State.Position));
        }

        int circumference = BodyCircumference(bodyId);
        int best = -1;
        double bestSq = double.MaxValue;
        foreach (var pad in PadsForBody(bodyId))
        {
            if (pad.Wet || pad.Molten || PadOccupiedByOther(bodyId, pad.Index, string.Empty))
            {
                continue;
            }

            if (anchors.Count == 0)
            {
                best = best < 0 ? pad.Index : System.Math.Min(best, pad.Index);
                continue;
            }

            var centre = new Vector3f(pad.CenterX + 0.5f, pad.CenterY, pad.CenterZ + 0.5f);
            foreach (var a in anchors)
            {
                double d = WorldConstants.WrapDistanceSquared(a, centre, circumference);
                if (d < bestSq)
                {
                    bestSq = d;
                    best = pad.Index;
                }
            }
        }

        return best;
    }

    /// <summary>Adds the core cell of every base on <paramref name="bodyId"/> whose owner passes <paramref name="ownerMatches"/>.</summary>
    private void AddBaseAnchors(List<Vector3f> anchors, string bodyId, System.Func<string, bool> ownerMatches)
    {
        foreach (var b in _bases)
        {
            if (b.Planet == bodyId && ownerMatches(b.OwnerId))
            {
                anchors.Add(new Vector3f(b.Cell.X + 0.5f, b.Cell.Y, b.Cell.Z + 0.5f));
            }
        }
    }

    /// <summary>A body's circumference: the active world's own, or the one its world would be generated with.</summary>
    private int BodyCircumference(string bodyId)
    {
        if (_world is not null && string.Equals(_world.LocationId, bodyId, System.StringComparison.Ordinal))
        {
            return _world.Circumference;
        }

        return _galaxy?.FindBody(bodyId) is { } body
            ? WorldCircumferenceOf(body)
            : 0;
    }

    /// <summary>The touchdown of a planet-side arrival (#2603): a toast for everyone on the body, and the base owners'
    /// hail — the radio call (a cached AI re-wording on hosted worlds) or, without a radio, VEGA's relay.</summary>
    private void AnnouncePlanetTraderLanded(NpcLandedTrader lt)
    {
        foreach (var s in PlayersOnBodySurface())
        {
            Send(s, new ServerMessage { Text = "@srv.trader.landed:" + lt.Name });
        }

        string bodyName = _galaxy?.FindBody(lt.BodyId)?.Name ?? lt.BodyId;
        NpcRadioOnTraderLanded(lt.BodyId, lt.Name, owner => TraderHailText(owner, lt, bodyName));
    }

    /// <summary>The cache key of a trader's hail: by persona, language — never the player.</summary>
    private static string TraderHailCacheKey(NpcLandedTrader lt, string locale)
        => AiLineCacheKey(NpcLinePurposes.TraderHail, "landed", PersonaIndexFor("trader:" + lt.Name, false, "vendor"), locale, "stranger");

    /// <summary>Asks for the hail's AI re-wording at the decision (hosted worlds): the 20 s approach covers the latency.
    /// One request per language of the body's base owners.</summary>
    private void PrefetchTraderHail(string bodyId, NpcLandedTrader lt)
    {
        if (!AiLinesEnabled)
        {
            return;
        }

        string bodyName = _galaxy?.FindBody(bodyId)?.Name ?? bodyId;
        var asked = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var b in _bases)
        {
            if (b.Planet != bodyId || FindSessionByPlayerId(b.OwnerId) is not { Joined: true } owner || !asked.Add(owner.Locale))
            {
                continue;
            }

            string locale = owner.Locale;
            var anchors = new List<string> { Localize(locale, "npc.call.trader").Replace("{0}", AiLineTokens.Place) };
            AiLinesFor(TraderHailCacheKey(lt, locale), AiLinePriority.Personal, () => new NpcLinesRequest
            {
                Purpose = NpcLinePurposes.TraderHail,
                Language = locale,
                Place = bodyName,
                Theme = "traders",
                Stage = "stranger",
                Situation = "a trader ship just landed on the pad nearest the player's base and hails them over the radio",
                Speakers =
                {
                    new NpcLinesSpeaker
                    {
                        Role = "vendor",
                        Job = "trader",
                        IsRobot = false,
                        Persona = PersonaFor("trader:" + lt.Name, "traders", false, "vendor"),
                    },
                },
                Tokens = { AiLineTokens.Place, AiLineTokens.Trader, AiLineTokens.Player },
                Authored = anchors,
                LineCount = 1,
                MaxChars = TraderHailMaxChars,
            }, requiredTokens: AiLineText.TokensIn(anchors));
        }
    }

    /// <summary>The hail a base owner hears: the cached AI re-wording with the body, the trader and their own name filled
    /// in — or null (the authored <c>npc.call.trader</c> line is used then).</summary>
    private string? TraderHailText(PlayerSession owner, NpcLandedTrader lt, string bodyName)
    {
        if (CachedAiLines(TraderHailCacheKey(lt, owner.Locale)) is not { Length: > 0 } ai)
        {
            return null;
        }

        return FillAiLine(ai[0], owner, new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [AiLineTokens.Place] = bodyName,
            [AiLineTokens.Trader] = lt.Name,
        });
    }

    /// <summary>Test seam (#2603): one planet-side scheduler look at the active body now, the window treated as due.
    /// Returns whether a trader is on its way in.</summary>
    public bool ForcePlanetTraderForTest()
    {
        if (_world is null || !Rules.NeutralNpcShips)
        {
            return false;
        }

        string bodyId = _world.LocationId;
        if (!_planetTraderSchedules.TryGetValue(bodyId, out var schedule))
        {
            _planetTraderSchedules[bodyId] = schedule = new PlanetTraderSchedule();
        }

        return RunPlanetTraderSchedule(bodyId, schedule, force: true);
    }

    /// <summary>Test seam (#2603): one regular scheduler pass on the active body (the 5 s throttle skipped). Returns the
    /// seconds until the next arrival, or null when none is armed.</summary>
    public double? TickPlanetTradersForTest()
    {
        if (_world is null)
        {
            return null;
        }

        if (_planetTraderSchedules.TryGetValue(_world.LocationId, out var schedule))
        {
            schedule.CheckAt = 0;
        }

        TickPlanetTraders(0.0);
        return _planetTraderSchedules.TryGetValue(_world.LocationId, out var s) && !double.IsNaN(s.NextAt) ? s.NextAt - _uptime : null;
    }

    /// <summary>Test seam (#2603): <see cref="PlanetTraderGap"/> for a traffic level name.</summary>
    public static double PlanetTraderGapForTest(string traffic, bool presentBase, double roll)
        => PlanetTraderGap(System.Enum.Parse<TraderTraffic>(traffic), presentBase, roll);

    /// <summary>Test/inspection (#2603): the pad a body's landed (or approaching) trader holds, or -1.</summary>
    public int LandedTraderPadForTest(string bodyId) => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.PadIndex : -1;

    /// <summary>Test/inspection (#2603): seconds until a body's trader touches down (≤ 0 = it stands there), or null.</summary>
    public double? LandedTraderTouchdownInForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.TouchdownAt - _uptime : null;

    /// <summary>Test/inspection (#2603): the name of a body's landed (or approaching) trader, or null.</summary>
    public string? LandedTraderNameForTest(string bodyId) => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.Name : null;

    /// <summary>Test seam (#2603): the cache key of a body's trader hail in a language (for the AI tests).</summary>
    public string? TraderHailCacheKeyForTest(string bodyId, string locale)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? TraderHailCacheKey(lt, locale) : null;

    /// <summary>Test/inspection: number of traders currently landed on planet/moon surfaces.</summary>
    public int LandedTraderCountForTest() => _landedTraders.Count;

    /// <summary>Test hook: land a trader on a body iff a pad is free (mirrors the in-flight landing decision),
    /// reserving the pad. Returns false if the body is unknown, full, or already hosts a trader.</summary>
    public bool LandTraderForTest(string bodyId)
    {
        var body = _galaxy.FindBody(bodyId);
        if (body is null || _landedTraders.ContainsKey(bodyId))
        {
            return false;
        }

        string planetKey = body.PlanetType ?? _meta.DefaultPlanetType;
        int total = PadCountFor(bodyId, planetKey, body.Kind);
        int pad = FirstFreePadIndex(bodyId, total, string.Empty);
        if (pad < 0)
        {
            return false;
        }

        _landedTraders[bodyId] = new NpcLandedTrader
        {
            Id = "test" + _nextTraderId++,
            BodyId = bodyId,
            ShipType = _content.Ships.Keys.FirstOrDefault(k => k != "starter") ?? "starter",
            Name = "Test Trader",
            PadIndex = pad,
            ExpiresAt = _uptime + 9999.0,
        };
        return true;
    }

    /// <summary>Test hook: run the materialisation the per-world tick does, on the ACTIVE world. Returns
    /// whether the trader's hull ended up standing there (#1678/#1680).</summary>
    public bool MaterializeLandedTraderForTest(bool ignoreOverlap = false)
    {
        MaterializeLandedTraderHere(ignoreOverlap); // ignoreOverlap reproduces the pre-#1678 stamp
        return _world is not null
            && _landedTraders.TryGetValue(_world.LocationId, out var lt)
            && _worlds.Active.LandedShips.TryGetValue(lt.OwnerId, out var rec)
            && rec.Placed;
    }

    /// <summary>Test hook: forget a body's landed-trader record while its hull keeps standing — the
    /// reservation-vs-reality desync that put two ships on one pad (#1678/#1680).</summary>
    public bool ReleaseLandedTraderRecordForTest(string bodyId) => _landedTraders.Remove(bodyId);

    /// <summary>Test hook: move a body's landed trader onto a given pad, bypassing the reservation — the
    /// bookkeeping desync the stamp guard has to survive (#1678).</summary>
    public bool ForceTraderPadForTest(string bodyId, int padIndex)
    {
        if (!_landedTraders.TryGetValue(bodyId, out var lt))
        {
            return false;
        }

        lt.PadIndex = padIndex;
        return true;
    }

    /// <summary>Test hook: drop the landed-trader records whose dwell has run out on bodies nobody is on.</summary>
    public void SweepExpiredLandedTradersForTest() => SweepExpiredLandedTraders();

    /// <summary>Test hook: expire a body's landed trader right now (its dwell is otherwise minutes long).</summary>
    public bool ExpireLandedTraderForTest(string bodyId)
    {
        if (!_landedTraders.TryGetValue(bodyId, out var lt))
        {
            return false;
        }

        lt.ExpiresAt = _uptime - 1.0;
        return true;
    }

    /// <summary>Test hook: land a trader on a body through the REAL in-flight landing decision (#1904) — the
    /// player must be flying in a space instance. Rolls the real dwell, unlike <see cref="LandTraderForTest"/>.</summary>
    public bool LandTraderFromFlightForTest(string playerId, string bodyId)
    {
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var inst))
        {
            return false;
        }

        var trader = new NpcTrader
        {
            Id = "t" + _nextTraderId++,
            ShipType = _content.Ships.Keys.FirstOrDefault(k => k != "starter") ?? "starter",
            Name = "Flight Trader",
            DestBodyId = bodyId,
        };
        return TryLandTraderOnBody(inst, trader);
    }

    /// <summary>Test hook: dock a trader at a station through the REAL registration (#1904), rolling its dwell.</summary>
    public void DockTraderForTest(string stationId)
        => RegisterVisitingTrader(stationId, new NpcTrader { Id = "t" + _nextTraderId++, Name = "Docked Trader" });

    /// <summary>Test/inspection: seconds until a body's landed trader wants to leave, or null if none is landed.</summary>
    public double? LandedTraderSecondsLeftForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) ? lt.ExpiresAt - _uptime : null;

    /// <summary>Test/inspection: seconds a docked trader still lingers at a station, or null if none is docked.</summary>
    public double? VisitingTraderSecondsLeftForTest(string stationId)
        => _visitingTraders.TryGetValue(stationId, out var vt) ? vt.ExpiresAt - _uptime : null;

    /// <summary>Test/inspection: where a body's landed trader's pilot stands, or null if it has not set down.</summary>
    public Vector3f? LandedTraderPilotPosForTest(string bodyId)
        => _landedTraders.TryGetValue(bodyId, out var lt) && lt.PilotNpcId != 0 ? lt.PilotPos : null;

    /// <summary>Test/inspection: free landing pads on a body (counts player + reserved-trader occupancy).</summary>
    public int FreePadsForTest(string bodyId)
    {
        var body = _galaxy.FindBody(bodyId);
        string planetKey = body?.PlanetType ?? _meta.DefaultPlanetType;
        int total = PadCountFor(bodyId, planetKey, body?.Kind ?? CelestialKind.Planet);
        return FreePadCount(bodyId, total, string.Empty);
    }
}
