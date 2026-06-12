using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Walking around inside your own ship while it floats in space. This reuses the <b>existing</b> ship
/// interior — the very same <see cref="StampShip"/> layout you walk through when landed on a planet — by
/// loading it into a void world, the ship staying put in its space instance. From the flight view you step
/// inside; from inside, a helm console returns you to the flight view (no take-off — you never landed) and
/// an airlock starts an EVA (those interactions are wired in later stages). Server-authoritative; the client
/// renders blocks and sends intents only.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Void world type backing the in-space ship interior (space sky, life support, no terrain or
    /// weather). See data/planets.json.</summary>
    private const string ShipInteriorType = "ship_interior";

    // Players currently walking inside their ship in space → how to drop them back into the flight view at
    // the ship's parked position, so the ship "stays where it is" across the visit.
    private readonly Dictionary<string, ShipInteriorReturn> _inShipInterior = new();

    private readonly record struct ShipInteriorReturn(string InstanceId, Vector3f ShipPos, string ReturnLoc, string ReturnType);

    /// <summary>True while the player is walking inside their ship in space (not piloting, not on a surface).</summary>
    public bool InShipInterior(string playerId) => _inShipInterior.ContainsKey(playerId);

    /// <summary>Step out of the pilot seat — or in from an EVA — into your ship's walkable interior while in
    /// space. Loads the ship interior as a void world and stamps your existing ship layout into it; the
    /// ship's flight-view position is remembered so taking the helm again puts it back exactly where it was.</summary>
    public void EnterShipInterior(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        if (!_playerInstance.TryGetValue(playerId, out var instanceId) ||
            !_spaceInstances.TryGetValue(instanceId, out var instance))
        {
            Reject(session, "ship", "You must be out in space to step inside the ship.");
            return;
        }

        // Remember how to drop back into the flight view (and the ship's parked spot, even if the now-empty
        // instance unloads while we're inside).
        _inShipInterior[playerId] = new ShipInteriorReturn(instanceId, instance.ShipPosition, session.CurrentLocationId, _world.PlanetKey);

        instance.Players.Remove(playerId);
        _playerInstance.Remove(playerId);
        if (instance.Players.Count == 0)
        {
            _spaceInstances.Remove(instanceId); // ShipPosition is saved above; restored on return
        }

        // Load the ship interior as its own void world and stamp the existing ship layout into it.
        string shipLoc = "shipint:" + playerId;
        LoadWorld(ShipInteriorType, shipLoc);
        SetCurrent(session);
        StampShip();

        session.CurrentLocationId = shipLoc;
        session.State.Position = _shipStamped ? _healTank : session.State.Position;
        session.State.AboardShip = true; // inside the hull → life support, oxygen safe
        session.State.InEva = false;     // entering from an EVA ends the spacewalk
        session.SentChunks.Clear();

        Send(session, new SpaceClosed { Reason = "Stepped inside the ship.", ShipDisabled = false });
        Send(session, new WorldReset { PlanetType = ShipInteriorType, PlanetName = string.Empty, SystemName = string.Empty, Hyperjump = false });
        SendPlayerState(session);
        SendEnvironment(session);
        SendInventory(session);
        SendDoors(session);
        _log.Info($"Player '{session.State.Name}' stepped inside their ship (world '{shipLoc}').");
    }

    /// <summary>Take the helm again: leave the ship interior straight back into the flight view, the ship
    /// restored to exactly where it was parked (no take-off animation — you never landed).</summary>
    public void ExitShipToFlight(string playerId) => ReturnToFlight(playerId, eva: false);

    /// <summary>Cycle out through the airlock: leave the ship interior into the flight view as a floating EVA
    /// suit next to the parked ship — the spacewalk begins (oxygen now drains).</summary>
    public void StartEvaFromShip(string playerId) => ReturnToFlight(playerId, eva: true);

    private void ReturnToFlight(string playerId, bool eva)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null || !_inShipInterior.TryGetValue(playerId, out var ret))
        {
            return;
        }

        _inShipInterior.Remove(playerId);

        // Restore the planet world under the flight view (so a later landing drops you there), like LeaveStation.
        LoadWorld(ret.ReturnType, ret.ReturnLoc);
        SetCurrent(session);
        session.CurrentLocationId = ret.ReturnLoc;
        session.State.AboardShip = true;
        session.State.InEva = false;
        session.SentChunks.Clear();

        // Back into the flight view, and put the ship back exactly where it was parked. Skip the take-off
        // sequence — you never landed, you just stepped out of the hull.
        EnterSpace(playerId, skipLaunch: true);
        if (_playerInstance.TryGetValue(playerId, out var iid) && _spaceInstances.TryGetValue(iid, out var inst))
        {
            inst.ShipPosition = ret.ShipPos;
            inst.ShipLastPosition = ret.ShipPos;
        }

        if (eva)
        {
            session.State.InEva = true; // stepping out the airlock starts the spacewalk → oxygen drains
            SendPlayerState(session);   // tell the client it is now floating in EVA next to the ship
        }

        _log.Info($"Player '{session.State.Name}' {(eva ? "stepped out for an EVA" : "took the helm again")} (flight view).");
    }
}
