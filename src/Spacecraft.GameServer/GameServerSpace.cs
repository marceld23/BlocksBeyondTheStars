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

    private readonly Dictionary<string, LandingZone> _landingZones = new();

    private void LoadLandingZones()
    {
        _landingZones.Clear();
        foreach (var zone in _repo.ListLandingZones(_world.PlanetKey))
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

        var zone = new LandingZone
        {
            PlayerId = playerId,
            LocationId = _world.PlanetKey,
            CenterX = index * LandingZoneSpacing,
            CenterZ = 0,
            Radius = LandingZoneRadius,
            Protected = isProtected,
        };

        _landingZones[playerId] = zone;
        _repo.SaveLandingZone(zone);
        return zone;
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
