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
    }

    private List<ServerSpeeder> _speeders => _worlds.Active.Speeders;

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
    };

    private SpeederList SpeederListMessage() => new() { Speeders = _speeders.Select(ToNetSpeeder).ToArray() };

    private void BroadcastSpeeders() => BroadcastToWorld(SpeederListMessage());

    private void SendSpeeders(PlayerSession session) => Send(session, SpeederListMessage());

    // ---------------------------------------------------------------------------------------------
    // Deploy (from the gadget-use path) / pack up.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Unfolds a speeder a couple of metres in front of the player (routed from the gadget handler for
    /// the <c>speeder</c> item). Consumes the item; refused in space. The record is persisted so the speeder
    /// survives a reload, and reconciliation keeps it live while the owner is on this body.</summary>
    private void DeploySpeeder(PlayerSession session)
    {
        var p = session.State;
        if (InSpace(p.PlayerId))
        {
            Reject(session, "speeder", "You can only deploy a speeder on a planet surface.");
            return;
        }

        if (!p.Inventory.Has("speeder", 1))
        {
            Reject(session, "speeder", "You don't have a speeder to deploy.");
            return;
        }

        double yawRad = p.Yaw * Math.PI / 180.0;
        float fx = (float)Math.Sin(yawRad);
        float fz = (float)Math.Cos(yawRad);
        int circ = _world.Circumference;
        float x = (float)WorldConstants.WrapX(p.Position.X + fx * SpeederDeployDistance, circ);
        float z = (float)WorldConstants.WrapZ((double)(p.Position.Z + fz * SpeederDeployDistance), circ);

        p.Inventory.Remove("speeder", 1);

        var rec = new DeployedSpeeder
        {
            Id = "sp" + Guid.NewGuid().ToString("N").Substring(0, 12),
            HomeBodyId = _world.LocationId,
            X = x,
            Y = p.Position.Y,
            Z = z,
            Yaw = p.Yaw,
            Hull = SpeederHullMax,
            HullMax = SpeederHullMax,
            Fuel = SpeederFuelMax,
            FuelMax = SpeederFuelMax,
            HullColor = session.HullColor,
        };
        p.DeployedSpeeders.Add(rec);
        _speeders.Add(new ServerSpeeder { Id = rec.Id, OwnerId = p.PlayerId, Rec = rec, LastDriverPos = new Vector3f(x, p.Position.Y, z) });
        _repo.SavePlayer(p);

        SendInventory(session);
        BroadcastSpeeders();
        BroadcastToWorld(new SpeederFx { X = x, Y = p.Position.Y, Z = z, Kind = "deploy" });
        Send(session, new ServerMessage { Text = "Speeder deployed." });
    }

    /// <summary>Packs a deployed speeder back into the item (owner only, within reach, not being driven by anyone
    /// else). If the owner is the driver, they dismount first. Returns one <c>speeder</c> item.</summary>
    private void HandleStowSpeeder(PlayerSession session, StowSpeederIntent intent)
    {
        var p = session.State;
        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            Reject(session, "speeder", "That isn't your speeder.");
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId) && s.DriverId != p.PlayerId)
        {
            Reject(session, "speeder", "Someone is driving it.");
            return;
        }

        if (s.DriverId == p.PlayerId)
        {
            HandleExitSpeeder(session); // dismount, then pack up
        }

        if (WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            Reject(session, "speeder", "Walk up to the speeder to pack it up.");
            return;
        }

        p.DeployedSpeeders.RemoveAll(r => r.Id == s.Id);
        _speeders.Remove(s);
        p.Inventory.Add("speeder", 1, 1);
        _repo.SavePlayer(p);

        SendInventory(session);
        BroadcastSpeeders();
        Send(session, new ServerMessage { Text = "Speeder packed up." });
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
            Reject(session, "speeder", "That isn't your speeder.");
            return;
        }

        if (!string.IsNullOrEmpty(s.DriverId))
        {
            Reject(session, "speeder", "The speeder is occupied.");
            return;
        }

        if (WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederBoardRange * SpeederBoardRange)
        {
            Reject(session, "speeder", "Walk closer to board the speeder.");
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
            _repo.SavePlayer(p);
            BroadcastSpeeders();
        }

        SendPlayerState(session);
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
        if (dist > 0.0001f && dist < 1000f && s.Rec.Fuel > 0f) // ignore world-wrap jumps; only drain when fuelled
        {
            s.Rec.Fuel = Math.Max(0f, s.Rec.Fuel - dist * SpeederFuelDrainPerBlock);
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

    /// <summary>Refuels a speeder from one energy cell in the owner's inventory (seated or within reach).</summary>
    private void HandleRefuelSpeeder(PlayerSession session, RefuelSpeederIntent intent)
    {
        var p = session.State;
        var s = _speeders.FirstOrDefault(v => v.Id == intent.SpeederId);
        if (s is null || s.OwnerId != p.PlayerId)
        {
            Reject(session, "speeder", "That isn't your speeder.");
            return;
        }

        bool seated = p.InSpeeder == s.Id;
        if (!seated && WrapDistSq(p.Position, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z)) > SpeederStowRange * SpeederStowRange)
        {
            Reject(session, "speeder", "Walk up to the speeder to refuel it.");
            return;
        }

        if (s.Rec.Fuel >= s.Rec.FuelMax)
        {
            Send(session, new ServerMessage { Text = "The speeder's energy cell is already full." });
            return;
        }

        bool free = !Rules.CraftingCostsMaterials || p.InstantBuild;
        if (!free)
        {
            if (!p.Inventory.Has(SpeederRefuelItem, 1))
            {
                Reject(session, "speeder", "Refuelling needs an energy cell.");
                return;
            }

            p.Inventory.Remove(SpeederRefuelItem, 1);
        }

        s.Rec.Fuel = Math.Min(s.Rec.FuelMax, s.Rec.Fuel + SpeederRefuelPerCell);
        _repo.SavePlayer(p);
        SendInventory(session);
        SendSpeeders(session);
        Send(session, new ServerMessage { Text = "Speeder refueled." });
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
                RespawnPlayer(session, "You did not survive the crash.");
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
                RespawnPlayer(driver, "Your speeder was destroyed under you.");
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
            Send(owner, new ServerMessage { Text = "Your speeder was destroyed!" });
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
        var present = JoinedInActiveWorld().Where(s => !InSpace(s.State.PlayerId)).ToList();
        var presentOwners = new HashSet<string>(present.Select(s => s.State.PlayerId));

        int before = _speeders.Count;
        _speeders.RemoveAll(v => !presentOwners.Contains(v.OwnerId));
        bool changed = _speeders.Count != before;

        foreach (var s in present)
        {
            foreach (var rec in s.State.DeployedSpeeders)
            {
                if (rec.HomeBodyId != body || _speeders.Any(v => v.Id == rec.Id))
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

    /// <summary>Speeder states (id/owner/driver/pos/hull/fuel) for tests + inspection.</summary>
    public IReadOnlyList<(string Id, string OwnerId, string DriverId, Vector3f Pos, float Hull, float Fuel)> SpeederSnapshots
        => _speeders.Select(s => (s.Id, s.OwnerId, s.DriverId, new Vector3f(s.Rec.X, s.Rec.Y, s.Rec.Z), s.Rec.Hull, s.Rec.Fuel)).ToList();

    /// <summary>Number of live speeders in the active world.</summary>
    public int SpeederCount => _speeders.Count;

    /// <summary>Test/util: deploy a speeder for a player (mirrors the gadget-use path).</summary>
    public string DeploySpeederForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } s)
        {
            return string.Empty;
        }

        Serve(s);
        DeploySpeeder(s);
        return _speeders.LastOrDefault(v => v.OwnerId == playerId)?.Id ?? string.Empty;
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
