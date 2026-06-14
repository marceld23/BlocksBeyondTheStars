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

    /// <summary>The station body id this trader is flying in to dock at — empty = a fly-through visit.</summary>
    public string DestStationId = string.Empty;

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
    private const double TraderDwellMin = 150.0;    // a docked merchant lingers on the station this long…
    private const double TraderDwellMax = 300.0;    // …up to this long, then is gone

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

    // ------------------------------------------------------------------ traffic level

    /// <summary>Deterministic ambient-traffic level for a star system (None / Rare / Often) — stable from the
    /// world seed + system id, so no persistence is needed and a given save always feels the same. Systems
    /// with a station are trade hubs and lean busier.</summary>
    private TraderTraffic TrafficFor(string systemId)
    {
        if (string.IsNullOrEmpty(systemId))
        {
            return TraderTraffic.None;
        }

        var system = _galaxy.Systems.FirstOrDefault(s => s.Id == systemId);
        bool hasStation = system?.Bodies.Any(b => b.Kind == CelestialKind.SpaceStation) ?? false;

        uint h = (uint)(_meta.Seed ^ WorldGenerator.StableHash("traffic:" + systemId));
        int bucket = (int)(h % 100u);
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

    private bool SpawnTrader(SpaceInstance instance)
    {
        // Future-proof ship choice: ANY defined non-starter ship type, weighted toward roomy cargo hulls so
        // traffic reads as traders. Enumerating GameContent.Ships means new ship types appear automatically.
        var pool = _content.Ships.Values.Where(s => s.Key != "starter").ToList();
        if (pool.Count == 0)
        {
            pool = _content.Ships.Values.ToList();
        }

        if (pool.Count == 0)
        {
            return false; // no ship content at all → nothing to fly
        }

        var def = WeightedShip(pool);
        var trader = new NpcTrader
        {
            Id = "t" + _nextTraderId++,
            ShipType = def.Key,
            Name = NameGenerator.Person(_traderRng),
            HullRgb = TraderHulls[_traderRng.Next(TraderHulls.Length)],
        };

        // Arrive on the system edge, anywhere around the orbital plane.
        double ang = _traderRng.NextDouble() * System.Math.PI * 2;
        float y = (float)(_traderRng.NextDouble() * 70 - 25);
        trader.Pos = new Vector3f(
            (float)System.Math.Cos(ang) * TraderArriveRadius, y, (float)System.Math.Sin(ang) * TraderArriveRadius);

        // Destination: dock at a station contact if the instance has one, else fly through the inner system.
        var stations = instance.Entities.Where(e => e.Kind == CombatEntityKind.SpaceStation).ToList();
        if (stations.Count > 0)
        {
            var target = stations[_traderRng.Next(stations.Count)];
            trader.DestStationId = target.Id;
            trader.Target = target.Position;
        }
        else
        {
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

    private ShipDefinition WeightedShip(List<ShipDefinition> pool)
    {
        int total = pool.Sum(s => System.Math.Max(1, s.CargoSlots));
        int roll = _traderRng.Next(total);
        foreach (var s in pool)
        {
            roll -= System.Math.Max(1, s.CargoSlots);
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
                        SetExitTarget(t); // reached the inner waypoint → turn around and leave
                        t.Phase = NpcTraderPhase.Departing;
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

    private void BroadcastWarpFx(SpaceInstance instance, Vector3f pos, bool arriving)
        => BroadcastToInstance(instance, new SpaceWarpFx { X = pos.X, Y = pos.Y, Z = pos.Z, Arriving = arriving });

    // ------------------------------------------------------------------ docked merchant (on the station)

    private void RegisterVisitingTrader(string stationId, NpcTrader t)
    {
        if (string.IsNullOrEmpty(stationId))
        {
            return;
        }

        // A "traders"-themed merchant with a varied civilian look; deterministic body colours from its name.
        var look = new System.Random(unchecked((int)WorldGenerator.StableHash("trader-look:" + t.Name)));
        uint[] skins = { 0xF2C9A0, 0xD9A066, 0x8D5524, 0xC68642, 0xFFDBAC };
        uint[] outfits = { 0x2E5E8C, 0x6A4C93, 0xC9A227, 0x3A7D5A };
        _visitingTraders[stationId] = new VisitingTrader
        {
            Name = t.Name,
            Theme = "traders",
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

        // Stand the visitor BESIDE a vendor marker so NearSpaceStationVendor + NearestNpc("vendor") resolve to
        // it — the existing themed-market barter then accepts its trades. Fall back to the spawn point.
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
}
