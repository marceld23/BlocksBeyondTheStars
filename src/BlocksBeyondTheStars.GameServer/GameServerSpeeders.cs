// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

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
/// </summary>
public sealed partial class GameServer
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
    private const float SpeederCreatureDamageShare = 0.6f; // fraction of a wildlife bite the hull soaks while driving
    private const float SpeederDestroyDriverDamage = 18f; // jolt to the driver when the speeder is destroyed under them
    private const double SpeederGaugeInterval = 0.4;    // min seconds between HUD hull/fuel pushes to the driver
    private const double SpeederDeployCooldown = 1.0;   // gadget cooldown after deploying
    private const int SpeederDeployHeadroom = 2;        // air cells a deployed speeder needs over its feet cell (#1660)
    private const int SpeederDeployScan = 4;            // how far above/below the player's feet the deploy spot may sit
    private const int SpeederWetReports = 30;           // consecutive "water under the hull" move reports (~3 s) before the snap back to dry ground
    private const int VehicleRecallRingMin = 2;         // recall parks the vehicle this far outside the pad rim …
    private const int VehicleRecallRingMax = 10;        // … up to this far (#1661)
    private const int BoatRecallRadius = 14;            // how far around the pad the recall looks for water for a boat

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

    private List<ServerSpeeder> _speeders => _worlds.Active.Speeders;

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

    private SpeederList SpeederListMessage() => new() { Speeders = _speeders.Select(ToNetSpeeder).ToArray() };

    private void BroadcastSpeeders() => BroadcastToWorld(SpeederListMessage());

    private void SendSpeeders(PlayerSession session) => Send(session, SpeederListMessage());

    // ---------------------------------------------------------------------------------------------
    // Deploy (from the gadget-use path) / pack up.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Unfolds a vehicle in front of the player (routed from the gadget handler for the <c>speeder</c> and
    /// <c>boat</c> items — the item's <see cref="Shared.Definitions.VehicleProperties"/> says which kind). Consumes
    /// the item; refused in space. A ground vehicle appears a couple of metres ahead at the player's height; a
    /// water vehicle needs a water column ahead and is set onto its waterline (#1215). The record is persisted so
    /// the vehicle survives a reload, and reconciliation keeps it live while the owner is on this body.</summary>
    private void DeployVehicle(PlayerSession session, string itemKey)
    {
        var p = session.State;
        var vehicle = _content.GetItem(itemKey)?.Vehicle;
        if (vehicle is null)
        {
            Reject(session, "gadget", "@srv.gadget.unknown");
            return;
        }

        bool boat = vehicle.Kind == "boat";
        if (InSpace(p.PlayerId))
        {
            Reject(session, "speeder", boat ? "@srv.boat.surface_only" : "@srv.speeder.surface_only");
            return;
        }

        if (!p.Inventory.Has(itemKey, 1))
        {
            Reject(session, "speeder", boat ? "@srv.boat.none" : "@srv.speeder.none");
            return;
        }

        float x, y, z;
        if (vehicle.Medium == "water")
        {
            if (!TryFindBoatLaunch(p, out var launch))
            {
                Reject(session, "speeder", "@srv.boat.need_water");
                return;
            }

            (x, y, z) = (launch.X, launch.Y, launch.Z);
        }
        else
        {
            double yawRad = p.Yaw * Math.PI / 180.0;
            float fx = (float)Math.Sin(yawRad);
            float fz = (float)Math.Cos(yawRad);
            int circ = _world.Circumference;
            x = (float)WorldConstants.WrapX(p.Position.X + fx * SpeederDeployDistance, circ);
            z = (float)WorldConstants.WrapZ((double)(p.Position.Z + fz * SpeederDeployDistance), circ);

            // The spot used to be taken at the player's own height with no look at the ground (#1660): deployed
            // from a bank it unfolded over water and sank, deployed mid-jump it unfolded in the air. Now it snaps
            // to the standable cell in that column nearest the player's feet when there is one (a column the
            // probe cannot read — unloaded, or a stamped floor the generator knows nothing about — keeps the
            // player's height, as before), and a wet column is refused outright: the speeder is a land vehicle.
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            int feetY = TryGroundFeetYAt(cx, cz, (int)Math.Floor(p.Position.Y), SpeederDeployHeadroom, SpeederDeployScan, out int ground)
                ? ground
                : (int)Math.Floor(p.Position.Y);
            if (IsWaterCell(new Vector3i(cx, feetY, cz)) || IsWaterCell(new Vector3i(cx, feetY - 1, cz)) || IsWaterCell(new Vector3i(cx, feetY - 2, cz)))
            {
                Reject(session, "speeder", "@srv.speeder.need_land");
                return;
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
            HomeBodyId = _world.LocationId,
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
        _speeders.Add(new ServerSpeeder { Id = rec.Id, OwnerId = p.PlayerId, Rec = rec, LastDriverPos = new Vector3f(x, y, z) });
        _repo.SavePlayer(p);

        SendInventory(session);
        BroadcastSpeeders();
        BroadcastToWorld(new SpeederFx { X = x, Y = y, Z = z, Kind = boat ? "splash" : "deploy" });
        Send(session, new ServerMessage { Text = boat ? "@srv.boat.deployed" : "@srv.speeder.deployed" });
    }

    /// <summary>Packs a deployed speeder back into the item (owner only, within reach, not being driven by anyone
    /// else). If the owner is the driver, they dismount first. Returns one <c>speeder</c> item.</summary>
    private void HandleStowSpeeder(PlayerSession session, StowSpeederIntent intent)
    {
        var p = session.State;
        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId) && s.DriverId != p.PlayerId)
        {
            Reject(session, "speeder", VehicleMsg(s.Rec, "driven"));
            return;
        }

        if (s.DriverId == p.PlayerId)
        {
            HandleExitSpeeder(session); // dismount, then pack up
        }

        if (WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            Reject(session, "speeder", VehicleMsg(s.Rec, "closer_pack"));
            return;
        }

        bool boat = IsBoat(s.Rec);
        p.DeployedSpeeders.RemoveAll(r => r.Id == s.Id);
        _speeders.Remove(s);
        p.Inventory.Add(VehicleItem(s.Rec), 1, 1);
        _repo.SavePlayer(p);

        SendInventory(session);
        BroadcastSpeeders();
        Send(session, new ServerMessage { Text = boat ? "@srv.boat.packed" : "@srv.speeder.packed" });
    }

    // ---------------------------------------------------------------------------------------------
    // Board / dismount / drive.
    // ---------------------------------------------------------------------------------------------

    private void HandleEnterSpeeder(PlayerSession session, EnterSpeederIntent intent)
    {
        var p = session.State;
        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId))
        {
            Reject(session, "speeder", VehicleMsg(s.Rec, "occupied"));
            return;
        }

        if (WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederBoardRange * SpeederBoardRange)
        {
            Reject(session, "speeder", VehicleMsg(s.Rec, "closer_board"));
            return;
        }

        s.DriverId = p.PlayerId;
        s.LastDriverPos = p.Position;
        s.LastGaugeSentAt = _uptime;
        p.InSpeeder = s.Id;

        SendPlayerState(session); // flips the client into vehicle-drive mode + speeder HUD
        SendSpeeders(session);
        BroadcastSpeeders();      // others see the DriverId bond
    }

    private void HandleExitSpeeder(PlayerSession session)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return;
        }

        var s = _speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
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
                Send(session, new BeamTeleported { X = spot.X, Y = spot.Y, Z = spot.Z });
            }

            _repo.SavePlayer(p);
            BroadcastSpeeders();
        }

        SendPlayerState(session);
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
        int circ = _world.Circumference;

        Vector3f? Probe(double wx, double wz)
        {
            int cx = (int)Math.Floor(WorldConstants.WrapX(wx, circ));
            int cz = (int)Math.Floor(WorldConstants.WrapZ(wz, circ));
            for (int y = fy + 1; y >= fy - 2; y--)
            {
                if (StandableSpot(cx, y, cz) is { } spot)
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

        if (IsBoat(s.Rec))
        {
            // Mid-lake (#1671): into the water beside the hull, feet a block under the waterline so the client's
            // swim physics takes over at once. Same side cells as the dry search, nearest first.
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
    private void ReleaseDrivenVehicle(PlayerState p)
    {
        bool changed = false;
        foreach (var s in _speeders)
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
    private void UpdateDrivingSpeeder(PlayerSession session)
    {
        var p = session.State;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return;
        }

        var s = _speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
        if (s is null)
        {
            p.InSpeeder = string.Empty; // desynced (speeder gone) — drop the bond and tell the client
            SendPlayerState(session);
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

        if (_uptime - s.LastGaugeSentAt >= SpeederGaugeInterval)
        {
            s.LastGaugeSentAt = _uptime;
            BroadcastSpeeders(); // driver's HUD gauges + other players' view of the moving speeder stay current
        }
    }

    /// <summary>The speeder's mirror of <see cref="TickBoatAshore"/> (#1660): a hover speeder is a land vehicle —
    /// the client stops it at the shoreline — but a client that keeps driving into the sea anyway (or one that
    /// sank before the shore stop existed) is set back onto the last pose judged dry after
    /// <see cref="SpeederWetReports"/> consecutive wet reports. Judged only in loaded chunks, like the boat.</summary>
    private void TickSpeederInWater(PlayerSession session, ServerSpeeder s)
    {
        var p = session.State;
        bool? wet = BoatOverWater(p.Position);
        if (wet != true)
        {
            if (wet == false)
            {
                s.LastDryPos = p.Position;
                s.HasDryPos = true;
            }

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
        SendPlayerState(session);
        Send(session, new BeamTeleported { X = p.Position.X, Y = p.Position.Y, Z = p.Position.Z });
        Send(session, new ServerMessage { Text = "@srv.speeder.in_water" });
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
    private void HandleRecallVehicle(PlayerSession session, RecallVehicleIntent intent)
    {
        var p = session.State;
        var rec = p.DeployedSpeeders.FirstOrDefault(r => r.Id == intent.VehicleId);
        if (rec is null)
        {
            Reject(session, "speeder", NotYoursKey(null));
            return;
        }

        var live = _speeders.FirstOrDefault(v => v.Id == rec.Id);
        if (live != null && !string.IsNullOrEmpty(live.DriverId))
        {
            Reject(session, "speeder", VehicleMsg(rec, "driven"));
            return;
        }

        if (rec.HomeBodyId != _world.LocationId)
        {
            Reject(session, "speeder", VehicleMsg(rec, "other_body"));
            return;
        }

        var landed = _worlds.Active.LandedFor(p.PlayerId);
        if (!landed.Placed)
        {
            Reject(session, "speeder", "@srv.speeder.recall_no_ship");
            return;
        }

        // The same reach rule the ship stations use — server-authoritative, so the recall is really tied to
        // standing at the helm and not just to owning a ship somewhere on the body.
        var console = StationPosition("cockpit") ?? StationPosition("console");
        if (console is null || !p.AboardShip || WrapDistSq(p.Position, console.Value) > ShipStationReach * ShipStationReach)
        {
            Reject(session, "station", "@srv.station.too_far");
            return;
        }

        bool boat = IsBoat(rec);
        if (p.Inventory.Add(VehicleItem(rec), 1, 1) == 0)
        {
            // Room in the inventory: the recall IS a pack-up (#1668).
            p.DeployedSpeeders.Remove(rec);
            if (live != null)
            {
                _speeders.Remove(live);
            }

            _repo.SavePlayer(p);
            SendInventory(session);
            BroadcastSpeeders();
            Send(session, new ServerMessage { Text = VehicleMsg(rec, "recalled_packed") });
            return;
        }

        var pad = PlayerPad(session);
        Vector3f? spot = boat ? FindBoatWaterNearPad(pad, p.Position) : FindSpeederParkingNearPad(pad, p.Position);
        if (spot is null)
        {
            Reject(session, "speeder", boat ? "@srv.boat.recall_no_water" : "@srv.speeder.recall_no_room");
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
        BroadcastToWorld(new SpeederFx { X = at.X, Y = at.Y, Z = at.Z, Kind = boat ? "splash" : "deploy" });
        RaisePingAt(session, at); // the #1217 "look here" pulse on the spot — a parked-beside vehicle is never a search
        int metres = (int)Math.Round(Math.Sqrt(WrapDistSq(p.Position, at)));
        Send(session, new ServerMessage { Text = VehicleMsg(rec, "recalled_parked") + ":" + metres });
    }

    /// <summary>A dry standable cell for a recalled speeder that did not fit the inventory: on the rings just
    /// outside the pad rim, feet within three cells of the pad surface, the candidate <b>nearest <paramref
    /// name="near"/></b> (the player at the cockpit) — not the first ring cell in scan order, which was always the
    /// same corner 14 m behind the ship (#1668). Never on the pad (the reserved landing volume) and never inside
    /// the parked ship (<see cref="StandableSpot"/> checks the hull).</summary>
    private Vector3f? FindSpeederParkingNearPad(LandingPad pad, Vector3f near)
    {
        int refY = PadSurfaceY(pad.CenterX, pad.CenterZ);
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        for (int r = pad.Radius + VehicleRecallRingMin; r <= pad.Radius + VehicleRecallRingMax; r++)
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r)
                    {
                        continue;
                    }

                    for (int y = refY + 3; y >= refY - 3; y--)
                    {
                        if (StandableSpot(pad.CenterX + dx, y, pad.CenterZ + dz) is { } spot)
                        {
                            double d = WrapDistSq(near, spot);
                            if (d < bestSq)
                            {
                                bestSq = d;
                                best = spot;
                            }

                            break;
                        }
                    }
                }

        return best;
    }

    /// <summary>The waterline around the pad nearest <paramref name="near"/> for a recalled boat that did not fit
    /// the inventory (the launch rule's search window per column, so the boat gets the same headroom it needs
    /// to launch).</summary>
    private Vector3f? FindBoatWaterNearPad(LandingPad pad, Vector3f near)
    {
        int refY = PadSurfaceY(pad.CenterX, pad.CenterZ);
        int circ = _world.Circumference;
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
                        double d = WrapDistSq(near, spot);
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
    private void HandleRefuelSpeeder(PlayerSession session, RefuelSpeederIntent intent)
    {
        var p = session.State;
        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            Reject(session, "speeder", NotYoursKey(s));
            return;
        }

        if (s.Rec.FuelMax <= 0f)
        {
            Reject(session, "speeder", "@srv.boat.no_fuel"); // the boat has no cell to fill (#1215)
            return;
        }

        bool seated = p.InSpeeder == s.Id;
        if (!seated && WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            Reject(session, "speeder", "@srv.speeder.closer_refuel");
            return;
        }

        if (s.Rec.Fuel >= s.Rec.FuelMax)
        {
            Send(session, new ServerMessage { Text = "@srv.speeder.cell_full" });
            return;
        }

        bool free = !Rules.CraftingCostsMaterialsFor(p.ModeOverride) || p.InstantBuild;
        if (!free)
        {
            if (!p.Inventory.Has(SpeederRefuelItem, 1))
            {
                Reject(session, "speeder", "@srv.speeder.need_cell");
                return;
            }

            p.Inventory.Remove(SpeederRefuelItem, 1);
        }

        s.Rec.Fuel = Math.Min(s.Rec.FuelMax, s.Rec.Fuel + SpeederRefuelPerCell);
        _repo.SavePlayer(p);
        SendInventory(session);
        SendSpeeders(session);
        Send(session, new ServerMessage { Text = "@srv.speeder.refueled" });
    }

    // ---------------------------------------------------------------------------------------------
    // Damage / destruction.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The driving client reports a hard collision (it owns the hover physics, like on-foot fall damage).
    /// The server scales the hull damage from the impact speed and applies a smaller jolt to the driver.</summary>
    private void HandleSpeederImpact(PlayerSession session, SpeederImpactIntent intent)
    {
        var p = session.State;
        if (!float.IsFinite(intent.Speed))
        {
            return;
        }

        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId && v.DriverId == p.PlayerId);
        if (s is null)
        {
            return;
        }

        float over = intent.Speed - SpeederSafeImpactSpeed;
        if (over <= 0f)
        {
            return;
        }

        float damage = Math.Min(SpeederImpactDamageCap, over * SpeederImpactDamagePerSpeed);
        if (damage <= 0f)
        {
            return;
        }

        DamageSpeeder(s, damage, "collision");

        // The driver gets rattled too (but never killed by a fender-bender — God-mode + armor still apply).
        if (!p.GodMode)
        {
            float jolt = Mitigate(p, damage * SpeederDriverJoltShare);
            p.Health = Math.Max(0f, p.Health - jolt);
            if (p.Health <= 0f)
            {
                RespawnPlayer(session, "@srv.death.crash");
            }
            else
            {
                SendPlayerState(session);
            }
        }
    }

    /// <summary>Applies hull damage to a speeder from any source (collision, wildlife, hazards). Destroys it at 0.</summary>
    private void DamageSpeeder(ServerSpeeder s, float amount, string reason)
    {
        if (amount <= 0f || s.Rec.Hull <= 0f)
        {
            return;
        }

        s.Rec.Hull = Math.Max(0f, s.Rec.Hull - amount);
        if (s.Rec.Hull <= 0f)
        {
            DestroySpeeder(s, reason);
            return;
        }

        if (FindSessionByPlayerId(s.OwnerId) is { } owner)
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

        var driver = string.IsNullOrEmpty(s.DriverId) ? null : FindSessionByPlayerId(s.DriverId);
        if (driver != null)
        {
            driver.State.InSpeeder = string.Empty;
            if (!driver.State.GodMode)
            {
                driver.State.Health = Math.Max(0f, driver.State.Health - Mitigate(driver.State, SpeederDestroyDriverDamage));
            }

            if (driver.State.Health <= 0f)
            {
                RespawnPlayer(driver, IsBoat(s.Rec) ? "@srv.death.boat" : "@srv.death.speeder");
            }
            else
            {
                SendPlayerState(driver);
            }
        }

        var owner = FindSessionByPlayerId(s.OwnerId);
        owner?.State.DeployedSpeeders.RemoveAll(r => r.Id == s.Id);
        _speeders.Remove(s);
        if (owner != null)
        {
            _repo.SavePlayer(owner.State);
            Send(owner, new ServerMessage { Text = VehicleMsg(s.Rec, "destroyed") });
        }

        BroadcastToWorld(new SpeederFx { X = pos.X, Y = pos.Y, Z = pos.Z, Kind = "explode" });
        BroadcastSpeeders();
    }

    /// <summary>The live speeder the given player is currently driving, if any (used by hazard damage paths).</summary>
    private bool TryGetDrivenSpeeder(PlayerState p, out ServerSpeeder speeder)
    {
        speeder = null!;
        if (string.IsNullOrEmpty(p.InSpeeder))
        {
            return false;
        }

        var s = _speeders.FirstOrDefault(v => v.Id == p.InSpeeder && v.DriverId == p.PlayerId);
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
    private bool ReconcileSpeeders()
    {
        string body = _world.LocationId;
        // Observers are not "present" for this purpose (issue #487) — a parked speeder with no visible owner
        // would betray the invisible admin.
        // #1530: runs every tick — scratch collections + plain loops instead of two LINQ chains, a HashSet and a
        // closure per deployed record (same order, same outcome).
        var present = _reconcileSessions;
        present.Clear();
        var presentOwners = _reconcileOwners;
        presentOwners.Clear();
        foreach (var s in JoinedInActiveWorld())
        {
            if (!s.Spectating && !InSpace(s.State.PlayerId))
            {
                present.Add(s);
                presentOwners.Add(s.State.PlayerId);
            }
        }

        int before = _speeders.Count;
        for (int i = _speeders.Count - 1; i >= 0; i--)
        {
            if (!presentOwners.Contains(_speeders[i].OwnerId))
            {
                _speeders.RemoveAt(i);
            }
        }

        bool changed = _speeders.Count != before;

        foreach (var s in present)
        {
            foreach (var rec in s.State.DeployedSpeeders)
            {
                if (rec.HomeBodyId != body || HasSpeeder(rec.Id))
                {
                    continue;
                }

                _speeders.Add(new ServerSpeeder
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
        foreach (var v in _speeders)
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
    private void SpawnSpeedersForSession(PlayerSession session)
    {
        session.State.InSpeeder = string.Empty; // never start a session "inside" a speeder
        ReconcileSpeeders();
        SendSpeeders(session);
    }

    // ---------------------------------------------------------------------------------------------
    // Test hooks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Vehicle states (id/owner/driver/pos/hull/fuel/fuel-max/kind) for tests + inspection.</summary>
    public IReadOnlyList<(string Id, string OwnerId, string DriverId, Vector3f Pos, float Hull, float Fuel, float FuelMax, string Kind)> SpeederSnapshots
        => _speeders.Select(s => (s.Id, s.OwnerId, s.DriverId, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z), s.Rec.Hull, s.Rec.Fuel, s.Rec.FuelMax, VehicleKind(s.Rec))).ToList();

    /// <summary>Number of live vehicles (speeders + boats) in the active world.</summary>
    public int SpeederCount => _speeders.Count;

    /// <summary>Test/util: deploy a speeder for a player (mirrors the gadget-use path).</summary>
    public string DeploySpeederForTest(string playerId) => DeployVehicleForTest(playerId, "speeder");

    /// <summary>Test/util: deploy a vehicle item ("speeder" / "boat") for a player (mirrors the gadget-use path).
    /// Returns the new vehicle's id, or "" when the deploy was refused (e.g. a boat with no water ahead).</summary>
    public string DeployVehicleForTest(string playerId, string itemKey)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return string.Empty;
        }

        Serve(s);
        int before = _speeders.Count;
        DeployVehicle(s, itemKey);
        return _speeders.Count > before ? _speeders[^1].Id : string.Empty;
    }

    /// <summary>Test/util: board a speeder as a given player.</summary>
    public void EnterSpeederForTest(string playerId, string speederId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleEnterSpeeder(s, new EnterSpeederIntent { SpeederId = speederId });
        }
    }

    /// <summary>Test/util: dismount the player's current speeder.</summary>
    public void ExitSpeederForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleExitSpeeder(s);
        }
    }

    /// <summary>Test/util: pack a speeder back into the item.</summary>
    public void StowSpeederForTest(string playerId, string speederId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleStowSpeeder(s, new StowSpeederIntent { SpeederId = speederId });
        }
    }

    /// <summary>Test/util: ask the landed ship to bring a deployed vehicle back beside it (#1661).</summary>
    public void RecallVehicleForTest(string playerId, string vehicleId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleRecallVehicle(s, new RecallVehicleIntent { VehicleId = vehicleId });
        }
    }

    /// <summary>Test/util: the player's persisted vehicle records (id, kind, home body, pose).</summary>
    public IReadOnlyList<(string Id, string Kind, string HomeBodyId, Vector3f Pos)> DeployedVehiclesForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s
            ? s.State.DeployedSpeeders.Select(r => (r.Id, VehicleKind(r), r.HomeBodyId, new Vector3f(r.X, r.Y, r.Z))).ToList()
            : new List<(string, string, string, Vector3f)>();

    /// <summary>Test/util: refuel a speeder.</summary>
    public void RefuelSpeederForTest(string playerId, string speederId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleRefuelSpeeder(s, new RefuelSpeederIntent { SpeederId = speederId });
        }
    }

    /// <summary>Test/util: report a collision impact for the player's driven speeder.</summary>
    public void ImpactSpeederForTest(string playerId, string speederId, float speed)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleSpeederImpact(s, new SpeederImpactIntent { SpeederId = speederId, Speed = speed });
        }
    }

    /// <summary>Test/util: re-materialise the player's persisted speeders on their current world (mirrors the
    /// reconciliation that runs on join + each environment tick). Returns true if the live set changed.</summary>
    public bool ReconcileSpeedersForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            return ReconcileSpeeders();
        }

        return false;
    }

    /// <summary>Test/util: apply raw hull damage to a speeder (simulates a hazard/wildlife hit).</summary>
    public void DamageSpeederForTest(string speederId, float amount)
    {
        var s = _speeders.FirstOrDefault(v => v.Id == speederId);
        if (s != null)
        {
            DamageSpeeder(s, amount, "test");
        }
    }

    /// <summary>Test/util: drive a step — set the player's position (as a MoveIntent would) and run the fuel/sync
    /// update, returning the speeder's remaining fuel.</summary>
    public float DriveSpeederStepForTest(string playerId, Vector3f to)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return 0f;
        }

        Serve(s);
        s.State.Position = to;
        UpdateDrivingSpeeder(s);
        return _speeders.FirstOrDefault(v => v.DriverId == playerId)?.Rec.Fuel ?? 0f;
    }
}
