// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What <see cref="VehicleSystem"/> asks of the server beyond <see cref="IServerHost"/> (ADR 0019): the active world's
/// live vehicles and the water block, who is present, the driver's damage and respawn, the ground and pad probes a
/// deploy, dismount or recall reads, the ship's helm stations, the recall's "look here" ping and the crash vibration the
/// giants hear. Implemented explicitly by <c>GameServer</c> in <c>GameServerSpeeders.cs</c>, each member delegating to
/// the server's own member of the same name. A wide host — a vehicle touches the ground, the ship, the player and the
/// fauna (ADR 0019, "wide hosts, shrink later").
/// </summary>
internal interface IVehicleHost : IServerHost
{
    /// <summary>The active world's live, materialised vehicles — speeders and boats (the server's <c>_speeders</c>).
    /// Follows the world cursor, so read it at use time.</summary>
    List<ServerSpeeder> Speeders { get; }

    /// <summary>The numeric id of the water block, 0 when the content has none (the server's <c>_waterId</c>).</summary>
    ushort WaterId { get; }

    // Presence
    IEnumerable<PlayerSession> JoinedInActiveWorld();

    // The driver's damage (GameServerPlayerDamage.cs) and death
    bool PlayerTakesDamage(PlayerState p);

    float DamagePlayer(PlayerState p, float amount, bool mitigated = false);

    bool VehicleTakesDamage(ServerSpeeder s);

    void RespawnPlayer(PlayerSession session, string reason);

    // Ground and pad probes (GameServerCreatures.cs, GameServerBaseLife.cs, GameServerSpace.cs)
    bool TryGroundFeetYAt(int x, int z, int refY, int headroom, int maxScan, out int feetY);

    Vector3f? StandableSpot(int x, int y, int z);

    Vector3f? NearestStandableSpotOutsidePad(GameServer.LandingPad pad, Vector3f near, int ringMin, int ringMax);

    GameServer.LandingPad PlayerPad(PlayerSession session);

    int PadSurfaceY(int cx, int cz);

    // The ship's helm (GameServerShipStructure.cs), the recall's ping (GameServerMarkers.cs) and the crash the giants hear
    Vector3f? StationPosition(string type);

    void RaisePingAt(PlayerSession session, Vector3f at);

    void EmitVibration(Vector3f at, Shared.Definitions.VibrationSource source, string playerId = "");
}

/// <summary>A live, materialised speeder on a world. Its durable state (position, hull, fuel, paint) lives on
/// the owner's <see cref="DeployedSpeeder"/> record; this adds the runtime driver bond + bookkeeping.</summary>
internal sealed class ServerSpeeder
{
    public string Id = string.Empty;
    public string OwnerId = string.Empty;
    public DeployedSpeeder Rec = null!;     // the persistent record on the owner's PlayerState
    public string DriverId = string.Empty;  // empty = parked
    public Vector3f LastDriverPos;          // for per-block fuel-drain accounting while driving
    public double LastGaugeSentAt;          // throttles HUD gauge pushes to the driver
    public Vector3f LastWaterPos;           // boat: last driven pose with water under the hull (#1215)
    public bool HasWaterPos;                // boat: LastWaterPos is valid
    public int AshoreReports;               // boat: consecutive move reports without water under the hull
    public Vector3f LastDryPos;             // speeder: last driven pose with no water under the hull (#1660)
    public bool HasDryPos;                  // speeder: LastDryPos is valid
    public int WetReports;                  // speeder: consecutive move reports with water under the hull
}

/// <summary>
/// Hover speeders: craftable single-seat surface vehicles. The owner deploys one from the <c>speeder</c> hotbar
/// item (routed through the gadget-use path), walks up and boards it, then drives it across the planet surface;
/// it hovers over the terrain (the client owns the arcade hover physics, like on-foot movement), runs on its own
/// energy cell, and can take damage and be destroyed. Lifecycle = "deployable &amp; retrievable": deploying
/// consumes the item, packing it back up returns it, destruction loses it.
///
/// <para>Like a tamed companion, a deployed speeder is persisted in the owner's player blob
/// (<see cref="PlayerState.DeployedSpeeders"/>) and materialised as a live <see cref="ServerSpeeder"/> only while
/// the owner is present on its home body — so it needs no per-world DB table, and reconciliation mirrors the
/// companion system. Movement is server-authoritative for fuel + damage but rides the existing presence stream:
/// while driving, the live position follows the driver's reported position (no high-rate movement message).</para>
///
/// <para>The boat (#1215) is the water <c>Kind</c> of the same system. Everything that is the same — deploy from the
/// gadget path, board, dismount, pack up, hull damage, persistence in the player blob, reconciliation — is shared and
/// keyed on the item's <see cref="Shared.Definitions.VehicleProperties"/>. The boat section at the end holds only what
/// differs: the boat needs a water column to launch into, it never drains a cell, and a driver who reports it "ashore"
/// for long enough is set back onto the last water pose (the client owns the floating physics and grounds it itself;
/// the server rule is the lenient safety net, exactly like the speeder's collision reports are the client's and the
/// hull damage is the server's).</para>
///
/// <para>Extracted from the <c>GameServerSpeeders.cs</c> and <c>GameServerBoats.cs</c> partials (ADR 0019); those keep
/// one-line facades, the first-join boat check and the test seams.</para>
/// </summary>
internal sealed class VehicleSystem
{
    // --- balance ---
    private const float SpeederHullMax = 120f;          // structural integrity at full repair
    private const float SpeederFuelMax = 100f;          // onboard energy-cell charge at full
    private const float SpeederDeployDistance = 2.5f;   // how far in front of the player the speeder unfolds
    private const float SpeederBoardRange = 4f;         // walk this close to board (matches a generous reach)
    private const float SpeederStowRange = 5f;          // pack-up reach
    private const float SpeederFuelDrainPerBlock = 0.12f; // energy spent per block driven (~830 blocks per tank)
    private const float SpeederRefuelPerCell = 60f;     // fuel restored per energy_cell_1 inserted
    private const string SpeederRefuelItem = "energy_cell_1";
    private const float SpeederSafeImpactSpeed = 12f;   // collisions slower than this don't dent the hull
    private const float SpeederImpactDamagePerSpeed = 5f; // hull lost per unit of impact speed over the safe cap
    private const float SpeederImpactDamageCap = 90f;   // a single collision can't one-shot a full-hull speeder
    private const float SpeederDriverJoltShare = 0.25f; // fraction of a collision's force the driver also takes
    internal const float SpeederCreatureDamageShare = 0.6f; // fraction of a wildlife bite the hull soaks while driving
    private const float SpeederDestroyDriverDamage = 18f; // jolt to the driver when the speeder is destroyed under them
    private const double SpeederGaugeInterval = 0.4;    // min seconds between HUD hull/fuel pushes to the driver
    internal const double SpeederDeployCooldown = 1.0;   // gadget cooldown after deploying
    private const int SpeederDeployHeadroom = 2;        // air cells a deployed speeder needs over its feet cell (#1660)
    private const int SpeederDeployScan = 4;            // how far above/below the player's feet the deploy spot may sit
    private const int SpeederWetReports = 30;           // consecutive "water under the hull" move reports (~3 s) before the snap back to dry ground
    private const int VehicleRecallRingMin = 2;         // recall parks the vehicle this far outside the pad rim …
    private const int VehicleRecallRingMax = 10;        // … up to this far (#1661)
    private const int BoatRecallRadius = 14;            // how far around the pad the recall looks for water for a boat

    private readonly IVehicleHost _host;
    private readonly GameContent _content;
    private readonly IWorldRepository _repo;

    /// <summary>Stores the references only; built once in <c>GameServer.ConstructSystems</c> (ADR 0019).</summary>
    internal VehicleSystem(IVehicleHost host, GameContent content, IWorldRepository repo)
    {
        _host = host;
        _content = content;
        _repo = repo;
    }

    /// <summary>The "that isn't yours" reject in the vehicle's own wording (#1301): <c>@srv.boat.not_yours</c> for a
    /// boat, <c>@srv.speeder.not_yours</c> otherwise. An unknown id (null) has no kind to speak of — the speeder
    /// wording is the generic one.</summary>
    private static string NotYoursKey(ServerSpeeder? s) => s is null ? "@srv.speeder.not_yours" : VehicleMsg(s.Rec, "not_yours");

    // ---------------------------------------------------------------------------------------------
    // Snapshots / sync.
    // ---------------------------------------------------------------------------------------------

    private static NetSpeeder ToNetSpeeder(ServerSpeeder s) => new()
    {
        Id = s.Id,
        OwnerId = s.OwnerId,
        DriverId = s.DriverId,
        X = s.Rec.X,
        Y = s.Rec.Y,
        Z = s.Rec.Z,
        Yaw = s.Rec.Yaw,
        Hull = s.Rec.Hull,
        HullMax = s.Rec.HullMax,
        Fuel = s.Rec.Fuel,
        FuelMax = s.Rec.FuelMax,
        HullColor = s.Rec.HullColor,
        Kind = VehicleKind(s.Rec),
    };

    private SpeederList SpeederListMessage() => new() { Speeders = _host.Speeders.Select(ToNetSpeeder).ToArray() };

    internal void BroadcastSpeeders() => _host.BroadcastToWorld(SpeederListMessage());

    internal void SendSpeeders(PlayerSession session) => _host.Send(session, SpeederListMessage());

    // ---------------------------------------------------------------------------------------------
    // Deploy (from the gadget-use path) / pack up.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Unfolds a vehicle in front of the player (routed from the gadget handler for the <c>speeder</c> and
    /// <c>boat</c> items — the item's <see cref="Shared.Definitions.VehicleProperties"/> says which kind). Consumes
    /// the item; refused in space. A ground vehicle appears a couple of metres ahead at the player's height; a
    /// water vehicle needs a water column ahead and is set onto its waterline (#1215). The record is persisted so
    /// the vehicle survives a reload, and reconciliation keeps it live while the owner is on this body. Returns whether
    /// the vehicle was deployed (false = refused, with the reason already sent).</summary>
    internal bool DeployVehicle(PlayerSession session, string itemKey)
    {
        var p = session.State;
        var vehicle = _content.GetItem(itemKey)?.Vehicle;
        if (vehicle is null)
        {
            _host.Reject(session, "gadget", "@srv.gadget.unknown");
            return false;
        }

        bool boat = vehicle.Kind == "boat";
        if (_host.InSpace(p.PlayerId))
        {
            _host.Reject(session, "speeder", boat ? "@srv.boat.surface_only" : "@srv.speeder.surface_only");
            return false;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            _host.Reject(session, "speeder", boat ? "@srv.boat.none" : "@srv.speeder.none");
            return false;
        }

        float x, y, z;
        if (vehicle.Medium == "water")
        {
            if (!TryFindBoatLaunch(p, out var launch))
            {
                _host.Reject(session, "speeder", "@srv.boat.need_water");
                return false;
            }

            (x, y, z) = (launch.X, launch.Y, launch.Z);
        }
        else
        {
            double yawRad = p.Yaw * Math.PI / 180.0;
            float fx = (float)Math.Sin(yawRad);
            float fz = (float)Math.Cos(yawRad);
            int circ = _host.World.Circumference;
            x = (float)WorldConstants.WrapX(p.Position.X + fx * SpeederDeployDistance, circ);
            z = (float)WorldConstants.WrapZ((double)(p.Position.Z + fz * SpeederDeployDistance), circ);

            // The spot used to be taken at the player's own height with no look at the ground (#1660): deployed
            // from a bank it unfolded over water and sank, deployed mid-jump it unfolded in the air. Now it snaps
            // to the standable cell in that column nearest the player's feet when there is one (a column the
            // probe cannot read — unloaded, or a stamped floor the generator knows nothing about — keeps the
            // player's height, as before), and a wet column is refused outright: the speeder is a land vehicle.
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            int feetY = _host.TryGroundFeetYAt(cx, cz, (int)Math.Floor(p.Position.Y), SpeederDeployHeadroom, SpeederDeployScan, out int ground)
                ? ground
                : (int)Math.Floor(p.Position.Y);
            if (IsWaterCell(new Vector3i(cx, feetY, cz)) || IsWaterCell(new Vector3i(cx, feetY - 1, cz)) || IsWaterCell(new Vector3i(cx, feetY - 2, cz)))
            {
                _host.Reject(session, "speeder", "@srv.speeder.need_land");
                return false;
            }

            y = feetY;
        }

        p.Inventory.Remove(itemKey, 1);

        // FuelMax 0 = a vehicle that never drains (the boat): the drive tick skips the drain, the HUD hides
        // the gauge, and refuelling is refused — one number, no second flag to keep in step.
        float fuelMax = vehicle.Fuel ? SpeederFuelMax : 0f;
        var rec = new DeployedSpeeder
        {
            Id = "sp" + Guid.NewGuid().ToString("N").Substring(0, 12),
            HomeBodyId = _host.World.LocationId,
            X = x,
            Y = y,
            Z = z,
            Yaw = p.Yaw,
            Hull = SpeederHullMax,
            HullMax = SpeederHullMax,
            Fuel = fuelMax,
            FuelMax = fuelMax,
            HullColor = session.HullColor,
            Kind = vehicle.Kind,
        };
        p.DeployedSpeeders.Add(rec);
        _host.Speeders.Add(new ServerSpeeder { Id = rec.Id, OwnerId = p.PlayerId, Rec = rec, LastDriverPos = new Vector3f(x, y, z) });
        _repo.SavePlayer(p);

        _host.SendInventory(session);
        BroadcastSpeeders();
        _host.BroadcastToWorld(new SpeederFx { X = x, Y = y, Z = z, Kind = boat ? "splash" : "deploy" });
        _host.Send(session, new ServerMessage { Text = boat ? "@srv.boat.deployed" : "@srv.speeder.deployed" });
        return true;
    }

    /// <summary>Packs a deployed speeder back into the item (owner only, within reach, not being driven by anyone
    /// else). If the owner is the driver, they dismount first. Returns one <c>speeder</c> item.</summary>
    internal void HandleStowSpeeder(PlayerSession session, StowSpeederIntent intent)
    {
        var p = session.State;
        var s = _host.Speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            _host.Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId) && s.DriverId != p.PlayerId)
        {
            _host.Reject(session, "speeder", VehicleMsg(s.Rec, "driven"));
            return;
        }

        if (s.DriverId == p.PlayerId)
        {
            HandleExitSpeeder(session); // dismount, then pack up
        }

        if (_host.WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            _host.Reject(session, "speeder", VehicleMsg(s.Rec, "closer_pack"));
            return;
        }

        bool boat = IsBoat(s.Rec);
        p.DeployedSpeeders.RemoveAll(r => r.Id == s.Id);
        _host.Speeders.Remove(s);
        p.Inventory.Add(VehicleItem(s.Rec), 1, 1);
        _repo.SavePlayer(p);

        _host.SendInventory(session);
        BroadcastSpeeders();
        _host.Send(session, new ServerMessage { Text = boat ? "@srv.boat.packed" : "@srv.speeder.packed" });
    }

    // ---------------------------------------------------------------------------------------------
    // Board / dismount / drive.
    // ---------------------------------------------------------------------------------------------

    internal void HandleEnterSpeeder(PlayerSession session, EnterSpeederIntent intent)
    {
        var p = session.State;
        var s = _host.Speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            _host.Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId))
        {
            _host.Reject(session, "speeder", VehicleMsg(s.Rec, "occupied"));
            return;
        }

        if (_host.WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederBoardRange * SpeederBoardRange)
        {
            _host.Reject(session, "speeder", VehicleMsg(s.Rec, "closer_board"));
            return;
        }

        s.DriverId = p.PlayerId;
        s.LastDriverPos = p.Position;
        s.LastGaugeSentAt = _host.Uptime;
        p.InSpeeder = s.Id;

        _host.SendPlayerState(session); // flips the client into vehicle-drive mode + speeder HUD
        SendSpeeders(session);
        BroadcastSpeeders();      // others see the DriverId bond
        _host.RaiseGameEvent(session, Shared.Story.GameEventKeys.VehicleBoarded, IsBoat(s.Rec) ? Shared.Story.GameEventKeys.VehicleBoat : Shared.Story.GameEventKeys.VehicleSpeeder); // #2482
    }

    internal void HandleExitSpeeder(PlayerSession session)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return;
        }

        var s = _host.Speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
        p.InSpeeder = string.Empty;
        if (s != null)
        {
            // Park where the player currently is (they ride at the speeder's position) and unbind the driver.
            s.Rec.X = p.Position.X;
            s.Rec.Y = p.Position.Y;
            s.Rec.Z = p.Position.Z;
            s.Rec.Yaw = p.Yaw;
            s.DriverId = string.Empty;

            // Then step the player off the seat (#1662). The seat is the hull's centre, so leaving them there put
            // them inside a 3×5 hull that — now that a parked vehicle is solid — would hold them. Nearest standable
            // cell beside the hull; a boat in open water finds none and the driver simply drops into the water.
            var spot = DismountSpot(s, p);
            if (!spot.Equals(p.Position))
            {
                p.Position = spot;
                session.AwaitingSpawnAdopt = true; // #865: the client still streams the seat pose for a beat
                _host.Send(session, new BeamTeleported { X = spot.X, Y = spot.Y, Z = spot.Z });
            }

            _repo.SavePlayer(p);
            BroadcastSpeeders();
        }

        _host.SendPlayerState(session);
    }

    /// <summary>Where a driver stands after dismounting: the nearest standable cell just outside the hull, side
    /// cells first (the hull is 3 wide and 5 long, so two to three cells to either side clear it at any
    /// yaw), then a wider ring. A boat with nothing dry around (mid-lake) puts the driver <b>in the water beside
    /// the hull</b> (#1671) — the seat is the hull's centre, and a driver left there sank through the now-solid
    /// hull and fought its collider from below. The seat itself only when there is no water either.</summary>
    private Vector3f DismountSpot(ServerSpeeder s, PlayerState p)
    {
        double yawRad = p.Yaw * Math.PI / 180.0;
        double fx = Math.Sin(yawRad), fz = Math.Cos(yawRad);
        double rx = fz, rz = -fx; // right-hand perpendicular
        int fy = (int)Math.Floor(p.Position.Y);
        int circ = _host.World.Circumference;

        Vector3f? Probe(double wx, double wz)
        {
            int cx = (int)Math.Floor(WorldConstants.WrapX(wx, circ));
            int cz = (int)Math.Floor(WorldConstants.WrapZ(wz, circ));
            for (int y = fy + 1; y >= fy - 2; y--)
            {
                if (_host.StandableSpot(cx, y, cz) is { } spot)
                {
                    return spot;
                }
            }

            return null;
        }

        foreach (int side in new[] { 2, -2, 3, -3 })
        {
            foreach (int ahead in new[] { 0, 1, -1, 2, -2 })
            {
                if (Probe(p.Position.X + rx * side + fx * ahead, p.Position.Z + rz * side + fz * ahead) is { } spot)
                {
                    return spot;
                }
            }
        }

        for (int r = 3; r <= 5; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                    {
                        continue;
                    }

                    if (Probe(p.Position.X + dx, p.Position.Z + dz) is { } spot)
                    {
                        return spot;
                    }
                }

        if (IsBoat(s.Rec) || HoversOverWater(s.Rec))
        {
            // Mid-lake (#1671): into the water beside the hull, feet a block under the waterline so the client's
            // swim physics takes over at once. Same side cells as the dry search, nearest first. A speeder that
            // hovers over water (#2535) parks out there like a boat, so its driver gets off the same way.
            foreach (int side in new[] { 2, -2, 3, -3 })
            {
                foreach (int ahead in new[] { 0, 1, -1 })
                {
                    int cx = (int)Math.Floor(WorldConstants.WrapX(p.Position.X + rx * side + fx * ahead, circ));
                    int cz = (int)Math.Floor(WorldConstants.WrapZ(p.Position.Z + rz * side + fz * ahead, circ));
                    if (TryFindWaterline(cx, cz, fy, out float waterline) && Math.Abs(waterline - p.Position.Y) <= 2f)
                    {
                        return new Vector3f(cx + 0.5f, waterline - 1f, cz + 0.5f);
                    }
                }
            }
        }

        return p.Position;
    }

    /// <summary>Drops a player's driver bond on death/respawn (#1661): the seat used to survive a respawn — the
    /// vehicle kept its <c>DriverId</c>, so the owner could neither board nor pack it until they left the body and
    /// came back. The vehicle stays parked where it was.</summary>
    internal void ReleaseDrivenVehicle(PlayerState p)
    {
        bool changed = false;
        foreach (var s in _host.Speeders)
        {
            if (s.DriverId == p.PlayerId)
            {
                s.DriverId = string.Empty;
                changed = true;
            }
        }

        p.InSpeeder = string.Empty;
        if (changed)
        {
            BroadcastSpeeders();
        }
    }

    /// <summary>Called from <c>HandleMove</c>: while a player drives, slave the live speeder to their reported
    /// pose, drain its energy cell by the distance covered, and push the driver an occasional HUD gauge update.</summary>
    internal void UpdateDrivingSpeeder(PlayerSession session)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return;
        }

        var s = _host.Speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
        if (s is null)
        {
            p.InSpeeder = string.Empty; // desynced (speeder gone) — drop the bond and tell the client
            _host.SendPlayerState(session);
            return;
        }

        float dx = p.Position.X - s.LastDriverPos.X;
        float dz = p.Position.Z - s.LastDriverPos.Z;
        float dist = (float)Math.Sqrt(dx * dx + dz * dz);
        // Ignore world-wrap jumps; only drain when fuelled — and a FuelMax of 0 (the boat) never drains.
        if (dist > 0.0001f && dist < 1000f && s.Rec.Fuel > 0f && s.Rec.FuelMax > 0f)
        {
            s.Rec.Fuel = Math.Max(0f, s.Rec.Fuel - dist * SpeederFuelDrainPerBlock);
        }

        if (IsBoat(s.Rec))
        {
            TickBoatAshore(session, s); // may set the driver back onto the last water pose (#1215)
        }
        else
        {
            TickSpeederInWater(session, s); // may set the driver back onto the last dry pose (#1660)
        }

        s.Rec.X = p.Position.X;
        s.Rec.Y = p.Position.Y;
        s.Rec.Z = p.Position.Z;
        s.Rec.Yaw = p.Yaw;
        s.LastDriverPos = p.Position;

        if (_host.Uptime - s.LastGaugeSentAt >= SpeederGaugeInterval)
        {
            s.LastGaugeSentAt = _host.Uptime;
            BroadcastSpeeders(); // driver's HUD gauges + other players' view of the moving speeder stay current
        }
    }

    /// <summary>What lies under a ground vehicle's hull (#2535): the first cell from the feet down that is not air or a
    /// walk-through prop (grass on a bank), up to <see cref="BoatHullProbeDepth"/> below. Judged only in loaded chunks,
    /// like the boat (<see cref="BoatOverWater"/>) — an unloaded probe is <see cref="HullFluid.Unknown"/>, never a verdict.</summary>
    private enum HullFluid
    {
        Unknown,
        Dry,
        Water,
        Hazard, // VehicleProperties.IsHoverHazard: lava, oil, a gas sea — never a surface to hover over
    }

    private HullFluid FluidUnderHull(Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X);
        int z = (int)Math.Floor(pos.Z);
        int feet = (int)Math.Floor(pos.Y);
        var probe = WorldConstants.CanonicalBlock(new Vector3i(x, feet, z), _host.World.Circumference);
        if (!_host.World.IsChunkLoaded(WorldConstants.WorldToChunk(probe)))
        {
            return HullFluid.Unknown;
        }

        for (int dy = 0; dy <= BoatHullProbeDepth; dy++)
        {
            var id = _host.World.GetBlockIfLoaded(new Vector3i(x, feet - dy, z));
            if (id.IsAir)
            {
                continue;
            }

            var def = _content.BlockById(id);
            if (def?.Key == Shared.Definitions.VehicleProperties.HoverWaterKey)
            {
                return HullFluid.Water;
            }

            if (Shared.Definitions.VehicleProperties.IsHoverHazard(def))
            {
                return HullFluid.Hazard; // lava, oil, a gas sea — the same set the client's shore stop reads
            }

            if (def != null && Shared.Definitions.WalkThroughBlocks.IsWalkThrough(def.Key))
            {
                continue;
            }

            return HullFluid.Dry; // real ground (a bridge over a lake included)
        }

        return HullFluid.Dry;
    }

    /// <summary>Whether this ground vehicle hovers over water too (#2535) — the item's
    /// <see cref="Shared.Definitions.VehicleProperties.HoverOverWater"/> flag; never a boat.</summary>
    private bool HoversOverWater(DeployedSpeeder rec)
        => !IsBoat(rec) && (_content.GetItem(VehicleItem(rec))?.Vehicle?.HoverOverWater ?? false);

    /// <summary>Whether a vehicle at <paramref name="pos"/> is out on the water like a boat (#2535): a boat always, a
    /// speeder that hovers over water when there is water under its hull. The leviathan strikes and hears these.</summary>
    internal bool VehicleOnWater(ServerSpeeder s, Vector3f pos)
        => IsBoat(s.Rec) || (HoversOverWater(s.Rec) && FluidUnderHull(pos) == HullFluid.Water);

    /// <summary>The speeder's mirror of <see cref="TickBoatAshore"/> (#1660): the client stops a hover speeder at the
    /// edge of a fluid it cannot cross, but a client that keeps driving in anyway (or one that sank before the shore
    /// stop existed) is set back onto the last safe pose after <see cref="SpeederWetReports"/> consecutive reports
    /// over it. Since #2535 a speeder flagged <see cref="Shared.Definitions.VehicleProperties.HoverOverWater"/> crosses
    /// water freely — only lava, oil and gas (<see cref="Shared.Definitions.VehicleProperties.IsHoverHazard"/>) set it back. Judged only in loaded chunks, like the boat.</summary>
    private void TickSpeederInWater(PlayerSession session, ServerSpeeder s)
    {
        var p = session.State;
        var under = FluidUnderHull(p.Position);
        if (under == HullFluid.Unknown)
        {
            s.WetReports = 0;
            return;
        }

        bool stranded = under == HullFluid.Hazard || (under == HullFluid.Water && !HoversOverWater(s.Rec));
        if (!stranded)
        {
            s.LastDryPos = p.Position; // a safe pose — dry ground, or water under a speeder that hovers over it
            s.HasDryPos = true;
            s.WetReports = 0;
            return;
        }

        if (!s.HasDryPos || ++s.WetReports < SpeederWetReports)
        {
            return;
        }

        s.WetReports = 0;
        p.Position = s.LastDryPos;
        s.Rec.X = p.Position.X;
        s.Rec.Y = p.Position.Y;
        s.Rec.Z = p.Position.Z;
        s.LastDriverPos = p.Position;
        session.AwaitingSpawnAdopt = true;
        _host.SendPlayerState(session);
        _host.Send(session, new BeamTeleported { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z });
        _host.Send(session, new ServerMessage { Text = under == HullFluid.Hazard ? "@srv.speeder.in_liquid" : "@srv.speeder.in_water" });
    }

    // ---------------------------------------------------------------------------------------------
    // Recall (#1661): the landed ship brings a stranded vehicle back beside it.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The owner at their landed ship's cockpit/console asks for a deployed vehicle back. Looks the record
    /// up on the player blob (so a vehicle that is not materialised right now is still addressable), refuses while
    /// it is driven, when the ship is not landed on this body, when the player is not at the cockpit or console,
    /// or when the vehicle lives on another world; then <b>packs it into the inventory</b> (#1668: X means "pack
    /// up" everywhere else, and a recall that parked the speeder 14 m behind the ship read as "it vanished").
    /// Only when no slot is free is the vehicle parked beside the ship — on the dry standable cell (speeder) /
    /// waterline (boat) <b>nearest the player</b>, with a "look here" ping on the spot and the distance in the
    /// message, so a parked-beside vehicle is never a search.</summary>
    internal void HandleRecallVehicle(PlayerSession session, RecallVehicleIntent intent)
    {
        var p = session.State;
        var rec = p.DeployedSpeeders.FirstOrDefault(r => r.Id == intent.VehicleId);
        if (rec is null)
        {
            _host.Reject(session, "speeder", NotYoursKey(null));
            return;
        }

        var live = _host.Speeders.FirstOrDefault(v => v.Id == rec.Id);
        if (live != null && !string.IsNullOrEmpty(live.DriverId))
        {
            _host.Reject(session, "speeder", VehicleMsg(rec, "driven"));
            return;
        }

        if (rec.HomeBodyId != _host.World.LocationId)
        {
            _host.Reject(session, "speeder", VehicleMsg(rec, "other_body"));
            return;
        }

        var landed = _host.Worlds.Active.LandedFor(p.PlayerId);
        if (!landed.Placed)
        {
            _host.Reject(session, "speeder", "@srv.speeder.recall_no_ship");
            return;
        }

        // The same reach rule the ship stations use — server-authoritative, so the recall is really tied to
        // standing at the helm and not just to owning a ship somewhere on the body.
        var console = _host.StationPosition("cockpit") ?? _host.StationPosition("console");
        if (console is null || !p.AboardShip || _host.WrapDistSq(p.Position, console.Value) > GameServer.ShipStationReach * GameServer.ShipStationReach)
        {
            _host.Reject(session, "station", "@srv.station.too_far");
            return;
        }

        bool boat = IsBoat(rec);
        if (p.Inventory.Add(VehicleItem(rec), 1, 1) == 0)
        {
            // Room in the inventory: the recall IS a pack-up (#1668).
            p.DeployedSpeeders.Remove(rec);
            if (live != null)
            {
                _host.Speeders.Remove(live);
            }

            _repo.SavePlayer(p);
            _host.SendInventory(session);
            BroadcastSpeeders();
            _host.Send(session, new ServerMessage { Text = VehicleMsg(rec, "recalled_packed") });
            return;
        }

        var pad = _host.PlayerPad(session);
        Vector3f? spot = boat ? FindBoatWaterNearPad(pad, p.Position) : FindSpeederParkingNearPad(pad, p.Position);
        if (spot is null)
        {
            _host.Reject(session, "speeder", boat ? "@srv.boat.recall_no_water" : "@srv.speeder.recall_no_room");
            return;
        }

        var at = spot.Value;
        rec.X = at.X;
        rec.Y = at.Y;
        rec.Z = at.Z;
        rec.Yaw = (float)(Math.Atan2(at.X - pad.CenterX, at.Z - pad.CenterZ) * 180.0 / Math.PI); // nose away from the pad
        if (live != null)
        {
            live.LastDriverPos = at;
            live.HasWaterPos = false;
            live.AshoreReports = 0;
            live.HasDryPos = false;
            live.WetReports = 0;
        }
        else
        {
            ReconcileSpeeders(); // the owner is here and the record is home — materialise it
        }

        _repo.SavePlayer(p);
        BroadcastSpeeders();
        _host.BroadcastToWorld(new SpeederFx { X = at.X, Y = at.Y, Z = at.Z, Kind = boat ? "splash" : "deploy" });
        _host.RaisePingAt(session, at); // the #1217 "look here" pulse on the spot — a parked-beside vehicle is never a search
        int metres = (int)Math.Round(Math.Sqrt(_host.WrapDistSq(p.Position, at)));
        _host.Send(session, new ServerMessage { Text = VehicleMsg(rec, "recalled_parked") + ":" + metres });
    }

    /// <summary>A dry standable cell for a recalled speeder that did not fit the inventory: on the rings just
    /// outside the pad rim, feet within three cells of the pad surface, the candidate <b>nearest <paramref
    /// name="near"/></b> (the player at the cockpit) — not the first ring cell in scan order, which was always the
    /// same corner 14 m behind the ship (#1668). Never on the pad (the reserved landing volume) and never inside
    /// the parked ship (<see cref="IVehicleHost.StandableSpot"/> checks the hull).</summary>
    private Vector3f? FindSpeederParkingNearPad(GameServer.LandingPad pad, Vector3f near)
        => _host.NearestStandableSpotOutsidePad(pad, near, VehicleRecallRingMin, VehicleRecallRingMax);

    /// <summary>The waterline around the pad nearest <paramref name="near"/> for a recalled boat that did not fit
    /// the inventory (the launch rule's search window per column, so the boat gets the same headroom it needs
    /// to launch).</summary>
    private Vector3f? FindBoatWaterNearPad(GameServer.LandingPad pad, Vector3f near)
    {
        int refY = _host.PadSurfaceY(pad.CenterX, pad.CenterZ);
        int circ = _host.World.Circumference;
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        for (int r = 1; r <= BoatRecallRadius; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                    {
                        continue;
                    }

                    int cx = WorldConstants.WrapX(pad.CenterX + dx, circ);
                    int cz = WorldConstants.WrapZ(pad.CenterZ + dz, circ);
                    if (TryFindWaterline(cx, cz, refY, out float waterline))
                    {
                        var spot = new Vector3f(cx + 0.5f, waterline + BoatFloatAboveWaterline, cz + 0.5f);
                        double d = _host.WrapDistSq(near, spot);
                        if (d < bestSq)
                        {
                            bestSq = d;
                            best = spot;
                        }
                    }
                }

        return best;
    }

    /// <summary>Refuels a speeder from one energy cell in the owner's inventory (seated or within reach).</summary>
    internal void HandleRefuelSpeeder(PlayerSession session, RefuelSpeederIntent intent)
    {
        var p = session.State;
        var s = _host.Speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            _host.Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (s.Rec.FuelMax <= 0f)
        {
            _host.Reject(session, "speeder", "@srv.boat.no_fuel"); // the boat has no cell to fill (#1215)
            return;
        }

        bool seated = p.InSpeeder == s.Id;
        if (!seated && _host.WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            _host.Reject(session, "speeder", "@srv.speeder.closer_refuel");
            return;
        }

        if (s.Rec.Fuel >= s.Rec.FuelMax)
        {
            _host.Send(session, new ServerMessage { Text = "@srv.speeder.cell_full" });
            return;
        }

        bool free = !_host.Rules.CraftingCostsMaterialsFor(p.ModeOverride) || p.InstantBuild;
        if (!free)
        {
            if (!p.Inventory.Has(SpeederRefuelItem, 1))
            {
                _host.Reject(session, "speeder", "@srv.speeder.need_cell");
                return;
            }

            p.Inventory.Remove(SpeederRefuelItem, 1);
        }

        s.Rec.Fuel = Math.Min(s.Rec.FuelMax, s.Rec.Fuel + SpeederRefuelPerCell);
        _repo.SavePlayer(p);
        _host.SendInventory(session);
        SendSpeeders(session);
        _host.Send(session, new ServerMessage { Text = "@srv.speeder.refueled" });
    }

    // ---------------------------------------------------------------------------------------------
    // Damage / destruction.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The driving client reports a hard collision (it owns the hover physics, like on-foot fall damage).
    /// The server scales the hull damage from the impact speed and applies a smaller jolt to the driver.</summary>
    internal void HandleSpeederImpact(PlayerSession session, SpeederImpactIntent intent)
    {
        var p = session.State;
        if (!float.IsFinite(intent.Speed))
        {
            return;
        }

        var s = _host.Speeders.FirstOrDefault(v => v.Id == intent.SpeederId && v.DriverId == p.PlayerId);
        if (s is null)
        {
            return;
        }

        float over = intent.Speed - SpeederSafeImpactSpeed;
        if (over <= 0f)
        {
            return;
        }

        _host.EmitVibration(p.Position, Shared.Definitions.VibrationSource.SpeederCrash, p.PlayerId); // #2001: a crash on the sand is heard far

        float damage = Math.Min(SpeederImpactDamageCap, over * SpeederImpactDamagePerSpeed);
        if (damage <= 0f)
        {
            return;
        }

        DamageSpeeder(s, damage, "collision");

        // The driver gets rattled too (but never killed by a fender-bender — God-mode + armor still apply; Sandbox
        // drivers ride it out unhurt, #2712).
        if (_host.PlayerTakesDamage(p))
        {
            _host.DamagePlayer(p, damage * SpeederDriverJoltShare, mitigated: true);
            if (p.Health <= 0f)
            {
                _host.RespawnPlayer(session, "@srv.death.crash");
            }
            else
            {
                _host.SendPlayerState(session);
            }
        }
    }

    /// <summary>Applies hull damage to a speeder from any source (collision, wildlife, hazards). Destroys it at 0.
    /// A Sandbox vehicle takes none (<see cref="IVehicleHost.VehicleTakesDamage"/>, #2712).</summary>
    internal void DamageSpeeder(ServerSpeeder s, float amount, string reason)
    {
        if (amount <= 0f || s.Rec.Hull <= 0f || !_host.VehicleTakesDamage(s))
        {
            return;
        }

        s.Rec.Hull = Math.Max(0f, s.Rec.Hull - amount);
        if (s.Rec.Hull <= 0f)
        {
            DestroySpeeder(s, reason);
            return;
        }

        if (_host.FindSessionByPlayerId(s.OwnerId) is { } owner)
        {
            _repo.SavePlayer(owner.State);
        }

        BroadcastSpeeders();
    }

    /// <summary>Destroys a speeder: ejects + jolts the driver, drops the record (the item is lost), and broadcasts
    /// the explosion. The owner is told if they're online.</summary>
    private void DestroySpeeder(ServerSpeeder s, string reason)
    {
        var pos = new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z);

        var driver = string.IsNullOrEmpty(s.DriverId) ? null : _host.FindSessionByPlayerId(s.DriverId);
        if (driver != null)
        {
            driver.State.InSpeeder = string.Empty;
            _host.DamagePlayer(driver.State, SpeederDestroyDriverDamage, mitigated: true); // god mode and Sandbox: unhurt (#2712)
            if (driver.State.Health <= 0f)
            {
                _host.RespawnPlayer(driver, IsBoat(s.Rec) ? "@srv.death.boat" : "@srv.death.speeder");
            }
            else
            {
                _host.SendPlayerState(driver);
            }
        }

        var owner = _host.FindSessionByPlayerId(s.OwnerId);
        owner?.State.DeployedSpeeders.RemoveAll(r => r.Id == s.Id);
        _host.Speeders.Remove(s);
        if (owner != null)
        {
            _repo.SavePlayer(owner.State);
            _host.Send(owner, new ServerMessage { Text = VehicleMsg(s.Rec, "destroyed") });
        }

        _host.BroadcastToWorld(new SpeederFx { X = pos.X, Y = pos.Y, Z = pos.Z, Kind = "explode" });
        BroadcastSpeeders();
    }

    /// <summary>The live speeder the given player is currently driving, if any (used by hazard damage paths).</summary>
    internal bool TryGetDrivenSpeeder(PlayerState p, out ServerSpeeder speeder)
    {
        speeder = null!;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return false;
        }

        var s = _host.Speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
        if (s is null)
        {
            return false;
        }

        speeder = s;
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Reconciliation (materialise present owners' speeders; despawn departed owners').
    // ---------------------------------------------------------------------------------------------

    /// <summary>Despawns live speeders whose owner isn't on this body and (re)spawns each present owner's speeders
    /// bound to it. Mirrors companion reconciliation; called from the environment tick + on join. Returns true if
    /// the live set changed.</summary>
    internal bool ReconcileSpeeders()
    {
        string body = _host.World.LocationId;
        // Observers are not "present" for this purpose (issue #487) — a parked speeder with no visible owner
        // would betray the invisible admin.
        // #1530: runs every tick — scratch collections + plain loops instead of two LINQ chains, a HashSet and a
        // closure per deployed record (same order, same outcome).
        var present = _reconcileSessions;
        present.Clear();
        var presentOwners = _reconcileOwners;
        presentOwners.Clear();
        foreach (var s in _host.JoinedInActiveWorld())
        {
            if (!s.Spectating && !_host.InSpace(s.State.PlayerId))
            {
                present.Add(s);
                presentOwners.Add(s.State.PlayerId);
            }
        }

        int before = _host.Speeders.Count;
        for (int i = _host.Speeders.Count - 1; i >= 0; i--)
        {
            if (!presentOwners.Contains(_host.Speeders[i].OwnerId))
            {
                _host.Speeders.RemoveAt(i);
            }
        }

        bool changed = _host.Speeders.Count != before;

        foreach (var s in present)
        {
            foreach (var rec in s.State.DeployedSpeeders)
            {
                if (rec.HomeBodyId != body || HasSpeeder(rec.Id))
                {
                    continue;
                }

                _host.Speeders.Add(new ServerSpeeder
                {
                    Id = rec.Id,
                    OwnerId = s.State.PlayerId,
                    Rec = rec,
                    LastDriverPos = new Vector3f(rec.X, rec.Y, rec.Z),
                });
                changed = true;
            }
        }

        return changed;
    }

    private readonly List<PlayerSession> _reconcileSessions = new();
    private readonly HashSet<string> _reconcileOwners = new();

    private bool HasSpeeder(string id)
    {
        foreach (var v in _host.Speeders)
        {
            if (v.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Materialises a joining/landing player's speeders immediately, clears any stale drive bond, and
    /// sends them the current speeder set.</summary>
    internal void SpawnSpeedersForSession(PlayerSession session)
    {
        session.State.InSpeeder = string.Empty; // never start a session "inside" a speeder
        ReconcileSpeeders();
        SendSpeeders(session);
    }

    // ---------------------------------------------------------------------------------------------
    // Boats (#1215): what differs for the water kind.
    // ---------------------------------------------------------------------------------------------

    // --- balance ---
    private const float BoatFloatAboveWaterline = 0.3f;   // deploy Y above the top water cell
    private const int BoatLaunchMinAhead = 2;             // scan for water this many blocks in front of the player …
    private const int BoatLaunchMaxAhead = 5;             // … up to this far, …
    private const int BoatLaunchSideways = 3;             // … and this far to either side (the issue's ±3)
    private const int BoatLaunchDown = 6;                 // waterline may sit this far below the player's feet (a bank)
    private const int BoatLaunchUp = 3;                   // … or this far above (standing in a dip beside a pool)
    private const int BoatHeadroom = 2;                   // air cells required above the waterline
    private const int BoatAshoreReports = 30;             // consecutive "no water under the hull" move reports (~3 s @10 Hz) before the snap-back
    private const int BoatHullProbeDepth = 2;             // water counts if it sits within this many cells below the driver's feet

    /// <summary>Vehicle kind of a persisted record — empty (pre-#1215 save) reads as a speeder.</summary>
    internal static string VehicleKind(DeployedSpeeder rec) => string.IsNullOrEmpty(rec.Kind) ? "speeder" : rec.Kind;

    private static bool IsBoat(DeployedSpeeder rec) => VehicleKind(rec) == "boat";

    /// <summary>The item that packing this vehicle up returns (the item that deployed it).</summary>
    private static string VehicleItem(DeployedSpeeder rec) => IsBoat(rec) ? "boat" : "speeder";

    /// <summary>A server message key in the vehicle's own wording: <c>@srv.boat.&lt;key&gt;</c> for a boat,
    /// <c>@srv.speeder.&lt;key&gt;</c> otherwise ("That isn't your boat" instead of "…speeder").</summary>
    private static string VehicleMsg(DeployedSpeeder rec, string key) => (IsBoat(rec) ? "@srv.boat." : "@srv.speeder.") + key;

    private bool IsWaterCell(Vector3i cell) => _host.WaterId != 0 && _host.World.GetBlock(cell).Value == _host.WaterId;

    /// <summary>Finds the waterline in the column (x, z): the highest water cell within the search window that has
    /// <see cref="BoatHeadroom"/> air cells above it. Returns the Y of that cell's top face.</summary>
    private bool TryFindWaterline(int x, int z, int nearY, out float waterlineY)
    {
        for (int y = nearY + BoatLaunchUp; y >= nearY - BoatLaunchDown; y--)
        {
            if (!IsWaterCell(new Vector3i(x, y, z)))
            {
                continue;
            }

            bool clear = true;
            for (int h = 1; h <= BoatHeadroom && clear; h++)
            {
                clear = _host.World.GetBlock(new Vector3i(x, y + h, z)).IsAir;
            }

            if (clear)
            {
                waterlineY = y + 1f;
                return true;
            }
        }

        waterlineY = 0f;
        return false;
    }

    /// <summary>Picks the launch spot for a boat: the nearest water column in front of the player (2–5 blocks
    /// ahead, up to 3 to either side) whose waterline lies within reach of the player's feet. Nearest-ahead wins,
    /// then the smallest sideways offset, so the boat appears where the player is looking.</summary>
    private bool TryFindBoatLaunch(PlayerState p, out Vector3f at)
    {
        double yawRad = p.Yaw * Math.PI / 180.0;
        double fx = Math.Sin(yawRad);
        double fz = Math.Cos(yawRad);
        double rx = fz;   // right-hand perpendicular
        double rz = -fx;
        int circ = _host.World.Circumference;
        int feetY = (int)Math.Floor(p.Position.Y);

        for (int ahead = BoatLaunchMinAhead; ahead <= BoatLaunchMaxAhead; ahead++)
        {
            for (int side = 0; side <= BoatLaunchSideways; side++)
            {
                foreach (int sign in side == 0 ? new[] { 0 } : new[] { -1, 1 })
                {
                    double wx = p.Position.X + fx * ahead + rx * side * sign;
                    double wz = p.Position.Z + fz * ahead + rz * side * sign;
                    int cx = (int)Math.Floor(WorldConstants.WrapX(wx, circ));
                    int cz = (int)Math.Floor(WorldConstants.WrapZ(wz, circ));
                    if (TryFindWaterline(cx, cz, feetY, out float waterline))
                    {
                        at = new Vector3f(cx + 0.5f, waterline + BoatFloatAboveWaterline, cz + 0.5f);
                        return true;
                    }
                }
            }
        }

        at = default;
        return false;
    }

    /// <summary>Whether a driven boat at this pose still has water under its hull — judged ONLY in loaded chunks.
    /// An unloaded cell reads as air, and far water is the #987 LOD case, so "not loaded" must mean "don't judge",
    /// never "ashore". Returns null when it cannot tell.</summary>
    private bool? BoatOverWater(Vector3f pos)
    {
        int x = (int)Math.Floor(pos.X);
        int z = (int)Math.Floor(pos.Z);
        int feet = (int)Math.Floor(pos.Y);
        var probe = WorldConstants.CanonicalBlock(new Vector3i(x, feet, z), _host.World.Circumference);
        if (!_host.World.IsChunkLoaded(WorldConstants.WorldToChunk(probe)))
        {
            return null;
        }

        for (int dy = 0; dy <= BoatHullProbeDepth; dy++)
        {
            if (_host.World.GetBlockIfLoaded(new Vector3i(x, feet - dy, z)).Value == _host.WaterId && _host.WaterId != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Per move report while driving a boat: remember the last pose that had water under the hull; after
    /// <see cref="BoatAshoreReports"/> consecutive dry reports set the driver back onto it. Lenient by design — the
    /// client grounds a beached boat itself (speed decays, it nudges back); this only catches a client that keeps
    /// driving over land anyway, and it never damages anything.</summary>
    private void TickBoatAshore(PlayerSession session, ServerSpeeder s)
    {
        var p = session.State;
        bool? wet = BoatOverWater(p.Position);
        if (wet != false)
        {
            // Only a pose JUDGED wet is a good pose to come back to. An unloaded probe chunk is "don't judge":
            // it neither counts as ashore nor becomes the snap-back target (#1301) — the previous water pose
            // stands, or none is known yet and the rule stays disarmed until the boat is seen floating.
            if (wet == true)
            {
                s.LastWaterPos = p.Position;
                s.HasWaterPos = true;
            }

            s.AshoreReports = 0;
            return;
        }

        if (!s.HasWaterPos)
        {
            return; // no water pose known yet (boarded a boat that was itself beached) — nothing sane to snap to
        }

        if (++s.AshoreReports < BoatAshoreReports)
        {
            return;
        }

        s.AshoreReports = 0;
        p.Position = s.LastWaterPos;
        s.Rec.X = p.Position.X;
        s.Rec.Y = p.Position.Y;
        s.Rec.Z = p.Position.Z;
        s.LastDriverPos = p.Position;
        session.AwaitingSpawnAdopt = true; // #865: the client keeps streaming its ashore pose for a beat — it must not drag the boat back
        _host.SendPlayerState(session);
        // The plain "the server moved you" the client already understands (it snaps the body, #beam); no FX.
        _host.Send(session, new BeamTeleported { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z });
        _host.Send(session, new ServerMessage { Text = "@srv.boat.aground" });
    }
}
