// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Hover speeders and boats on the server: the facade of <see cref="VehicleSystem"/> (ADR 0019), which owns deploying,
/// boarding, driving, dismounting, packing up, recalling, refuelling, hull damage and the reconciliation of the live
/// vehicles. This partial keeps the vehicle list accessor and the two balance values the sibling partials read, the
/// members the tick, the join, the respawn, the gadget path, the dispatch table and the tests call — same names, same
/// signatures, each one line delegating — the test seams, and the explicit <see cref="IVehicleHost"/> implementation.
/// </summary>
public sealed partial class GameServer : IVehicleHost
{
    /// <summary>Fraction of a wildlife bite the hull soaks while driving, read by the creature and giant partials (the
    /// value lives in VehicleSystem).</summary>
    private const float SpeederCreatureDamageShare = VehicleSystem.SpeederCreatureDamageShare;

    /// <summary>Gadget cooldown after deploying a vehicle, read by the gadget partial (the value lives in
    /// VehicleSystem).</summary>
    private const double SpeederDeployCooldown = VehicleSystem.SpeederDeployCooldown;

    private List<ServerSpeeder> _speeders => _worlds.Active.Speeders;

    private void BroadcastSpeeders() => _vehicleSystem.BroadcastSpeeders();

    private void SendSpeeders(PlayerSession session) => _vehicleSystem.SendSpeeders(session);

    /// <summary>Unfolds a vehicle in front of the player (routed from the gadget handler for the <c>speeder</c> and
    /// <c>boat</c> items — the item's <see cref="Shared.Definitions.VehicleProperties"/> says which kind). Consumes
    /// the item; refused in space. A ground vehicle appears a couple of metres ahead at the player's height; a
    /// water vehicle needs a water column ahead and is set onto its waterline (#1215). The record is persisted so
    /// the vehicle survives a reload, and reconciliation keeps it live while the owner is on this body. Returns whether
    /// the vehicle was deployed (false = refused, with the reason already sent).</summary>
    private bool DeployVehicle(PlayerSession session, string itemKey) => _vehicleSystem.DeployVehicle(session, itemKey);

    /// <summary>Packs a deployed speeder back into the item (owner only, within reach, not being driven by anyone
    /// else). If the owner is the driver, they dismount first. Returns one <c>speeder</c> item.</summary>
    private void HandleStowSpeeder(PlayerSession session, StowSpeederIntent intent) => _vehicleSystem.HandleStowSpeeder(session, intent);

    private void HandleEnterSpeeder(PlayerSession session, EnterSpeederIntent intent) => _vehicleSystem.HandleEnterSpeeder(session, intent);

    private void HandleExitSpeeder(PlayerSession session) => _vehicleSystem.HandleExitSpeeder(session);

    /// <summary>Drops a player's driver bond on death/respawn (#1661): the seat used to survive a respawn — the
    /// vehicle kept its <c>DriverId</c>, so the owner could neither board nor pack it until they left the body and
    /// came back. The vehicle stays parked where it was.</summary>
    private void ReleaseDrivenVehicle(PlayerState p) => _vehicleSystem.ReleaseDrivenVehicle(p);

    /// <summary>Called from <c>HandleMove</c>: while a player drives, slave the live speeder to their reported
    /// pose, drain its energy cell by the distance covered, and push the driver an occasional HUD gauge update.</summary>
    private void UpdateDrivingSpeeder(PlayerSession session) => _vehicleSystem.UpdateDrivingSpeeder(session);

    /// <summary>Whether a vehicle at <paramref name="pos"/> is out on the water like a boat (#2535): a boat always, a
    /// speeder that hovers over water when there is water under its hull. The leviathan strikes and hears these.</summary>
    private bool VehicleOnWater(ServerSpeeder s, Vector3f pos) => _vehicleSystem.VehicleOnWater(s, pos);

    /// <summary>The owner at their landed ship's cockpit/console asks for a deployed vehicle back (#1661): packed into
    /// the inventory, or parked beside the ship when no slot is free (#1668).</summary>
    private void HandleRecallVehicle(PlayerSession session, RecallVehicleIntent intent) => _vehicleSystem.HandleRecallVehicle(session, intent);

    /// <summary>Refuels a speeder from one energy cell in the owner's inventory (seated or within reach).</summary>
    private void HandleRefuelSpeeder(PlayerSession session, RefuelSpeederIntent intent) => _vehicleSystem.HandleRefuelSpeeder(session, intent);

    /// <summary>The driving client reports a hard collision (it owns the hover physics, like on-foot fall damage).
    /// The server scales the hull damage from the impact speed and applies a smaller jolt to the driver.</summary>
    private void HandleSpeederImpact(PlayerSession session, SpeederImpactIntent intent) => _vehicleSystem.HandleSpeederImpact(session, intent);

    /// <summary>Applies hull damage to a speeder from any source (collision, wildlife, hazards). Destroys it at 0.
    /// A Sandbox vehicle takes none (<see cref="VehicleTakesDamage"/>, #2712).</summary>
    private void DamageSpeeder(ServerSpeeder s, float amount, string reason) => _vehicleSystem.DamageSpeeder(s, amount, reason);

    /// <summary>The live speeder the given player is currently driving, if any (used by hazard damage paths).</summary>
    private bool TryGetDrivenSpeeder(PlayerState p, out ServerSpeeder speeder) => _vehicleSystem.TryGetDrivenSpeeder(p, out speeder);

    /// <summary>Despawns live speeders whose owner is not on this body and (re)spawns each present owner's speeders
    /// bound to it. Mirrors companion reconciliation; called from the environment tick + on join. Returns true if
    /// the live set changed.</summary>
    private bool ReconcileSpeeders() => _vehicleSystem.ReconcileSpeeders();

    /// <summary>Materialises a joining/landing player's speeders immediately, clears any stale drive bond, and
    /// sends them the current speeder set.</summary>
    private void SpawnSpeedersForSession(PlayerSession session) => _vehicleSystem.SpawnSpeedersForSession(session);

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

    // IVehicleHost: each member delegates to the server's own member of the same name (never to the interface member,
    // which would recurse).
    List<ServerSpeeder> IVehicleHost.Speeders => _speeders;

    ushort IVehicleHost.WaterId => _waterId;

    IEnumerable<PlayerSession> IVehicleHost.JoinedInActiveWorld() => JoinedInActiveWorld();

    bool IVehicleHost.PlayerTakesDamage(PlayerState p) => PlayerTakesDamage(p);

    float IVehicleHost.DamagePlayer(PlayerState p, float amount, bool mitigated) => DamagePlayer(p, amount, mitigated);

    bool IVehicleHost.VehicleTakesDamage(ServerSpeeder s) => VehicleTakesDamage(s);

    void IVehicleHost.RespawnPlayer(PlayerSession session, string reason) => RespawnPlayer(session, reason);

    bool IVehicleHost.TryGroundFeetYAt(int x, int z, int refY, int headroom, int maxScan, out int feetY)
        => TryGroundFeetYAt(x, z, refY, headroom, maxScan, out feetY);

    Vector3f? IVehicleHost.StandableSpot(int x, int y, int z) => StandableSpot(x, y, z);

    Vector3f? IVehicleHost.NearestStandableSpotOutsidePad(LandingPad pad, Vector3f near, int ringMin, int ringMax)
        => NearestStandableSpotOutsidePad(pad, near, ringMin, ringMax);

    LandingPad IVehicleHost.PlayerPad(PlayerSession session) => PlayerPad(session);

    int IVehicleHost.PadSurfaceY(int cx, int cz) => PadSurfaceY(cx, cz);

    Vector3f? IVehicleHost.StationPosition(string type) => StationPosition(type);

    void IVehicleHost.RaisePingAt(PlayerSession session, Vector3f at) => RaisePingAt(session, at);

    void IVehicleHost.EmitVibration(Vector3f at, Shared.Definitions.VibrationSource source, string playerId) => EmitVibration(at, source, playerId);
}
