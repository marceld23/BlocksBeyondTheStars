using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.World;

namespace Spacecraft.GameServer;

/// <summary>
/// Personal landing zones (technical requirements / `anf_space_flight.md` §3): each player
/// gets a reserved, optionally protected area on the start planet where their ship lands.
/// Zones are persisted; protection is enforced server-side (others can't edit them).
/// </summary>
public sealed partial class GameServer
{
    private const int LandingZoneRadius = 8;
    private const int LandingZoneSpacing = 24; // center-to-center so zones never overlap

    private Dictionary<string, LandingZone> _landingZones => _worlds.Active.LandingZones;

    private void LoadLandingZones()
    {
        _landingZones.Clear();
        foreach (var zone in _repo.ListLandingZones(_world.LocationId))
        {
            _landingZones[zone.PlayerId] = zone;
        }
    }

    /// <summary>Ensures the player has a landing zone on the active planet, creating one if needed.</summary>
    private LandingZone EnsureLandingZone(string playerId)
    {
        if (_landingZones.TryGetValue(playerId, out var existing))
        {
            return existing;
        }

        int index = _landingZones.Count;
        bool isProtected = Rules.PersonalLandingZoneProtection != LandingZoneProtection.Off;

        // March the zone along +X, skipping any spot whose footprint would land on the settlement, so a
        // ship (which stamps at the zone centre) never carves into a town — for every player, not just
        // the first. Stations are in space; wrecks are placed clear of the zones already.
        int cx = WorldConstants.WrapX(index * LandingZoneSpacing); // canonical longitude (wraps the world)
        for (int guard = 0; guard < 128 && OverlapsSettlement(cx); guard++)
        {
            index++;
            cx = WorldConstants.WrapX(index * LandingZoneSpacing);
        }

        var zone = new LandingZone
        {
            PlayerId = playerId,
            LocationId = _world.LocationId,
            CenterX = cx,
            CenterZ = 0,
            Radius = LandingZoneRadius,
            Protected = isProtected,
        };

        _landingZones[playerId] = zone;
        _repo.SaveLandingZone(zone);
        return zone;
    }

    /// <summary>True if a landing zone centred at (cx, 0) — incl. the ship footprint — would overlap the
    /// settlement (expanded by a clearance margin). Used to keep ships from stamping onto a town.</summary>
    private bool OverlapsSettlement(int cx)
    {
        if (!_settlementStamped)
        {
            return false;
        }

        const int margin = 16; // landing-zone radius + ship footprint slack
        return cx >= _settlementMin.X - margin && cx <= _settlementMax.X + margin
            && 0 >= _settlementMin.Z - margin && 0 <= _settlementMax.Z + margin;
    }

    /// <summary>True if the position lies in another player's protected landing zone.</summary>
    private bool IsLandingZoneBlockedForOther(string actorId, Vector3i pos)
    {
        foreach (var zone in _landingZones.Values)
        {
            if (zone.Protected && zone.PlayerId != actorId && zone.Contains(pos.X, pos.Z))
            {
                return true;
            }
        }

        return false;
    }
}
