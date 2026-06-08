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

        int baseIndex = _landingZones.Count;
        bool isProtected = Rules.PersonalLandingZoneProtection != LandingZoneProtection.Off;

        // March the zone along +X to a touchdown longitude that (hard rules) never lands on the settlement
        // and never collides with another player's zone, and — where the world allows — sits on DRY land
        // rather than in a sea or an upland pond (B36; a ship in the water looks wrong). The first merely
        // town/zone-clear spot is kept as a fallback: on an all-ocean world no dry column exists, so the ship
        // lands there on the seabed (Task 1 gives it a dry, watertight cabin) instead of marching the zone
        // right around the planet. Stations are in space; wrecks are placed clear of the zones already.
        int fallbackIndex = -1, dryIndex = -1;
        for (int step = 0; step < 256; step++)
        {
            int cxTry = WorldConstants.WrapX((baseIndex + step) * LandingZoneSpacing); // wraps the world
            if (OverlapsSettlement(cxTry) || OverlapsExistingZone(cxTry))
            {
                continue;
            }

            if (fallbackIndex < 0)
            {
                fallbackIndex = baseIndex + step; // first town/zone-clear spot (water tolerated)
            }

            if (!LandingFootprintWet(cxTry, 0))
            {
                dryIndex = baseIndex + step; // town/zone-clear AND dry — take it
                break;
            }
        }

        int chosen = dryIndex >= 0 ? dryIndex : (fallbackIndex >= 0 ? fallbackIndex : baseIndex);
        int cx = WorldConstants.WrapX(chosen * LandingZoneSpacing);

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

    /// <summary>True if a landing centred at (cx, 0) would sit within one zone-spacing of an existing zone on
    /// this world (longitude-wrap aware), so two players never get overlapping ships (B36 search invariant).</summary>
    private bool OverlapsExistingZone(int cx)
    {
        foreach (var z in _landingZones.Values)
        {
            if (z.LocationId == _world.LocationId
                && System.Math.Abs(WorldConstants.WrapDeltaX(cx - z.CenterX, _world.Circumference)) < LandingZoneSpacing)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if the landing pad (its centre or any zone-radius edge) sits over surface water — a sea or
    /// an upland pond — so a lake covering part of the pad still counts as wet (B36).</summary>
    private bool LandingFootprintWet(int cx, int cz)
    {
        int r = LandingZoneRadius;
        return _generator.IsSurfaceWater(_world.Planet, cx, cz)
            || _generator.IsSurfaceWater(_world.Planet, cx - r, cz)
            || _generator.IsSurfaceWater(_world.Planet, cx + r, cz)
            || _generator.IsSurfaceWater(_world.Planet, cx, cz - r)
            || _generator.IsSurfaceWater(_world.Planet, cx, cz + r);
    }

    /// <summary>Test hook: true if the given player's landing pad sits on dry land (B36). False if the pad is
    /// over water — which on a normal world means the dry-land search failed, and on an all-ocean world is the
    /// accepted seabed-landing fallback.</summary>
    public bool LandingPadIsDry(string playerId)
        => _landingZones.TryGetValue(playerId, out var z) && !LandingFootprintWet(z.CenterX, z.CenterZ);

    /// <summary>Test helper: seeds another player's protected landing zone on the active world (keyed to its
    /// real location id, so the protection check actually finds it regardless of which body is active).</summary>
    public void SeedProtectedZoneForTest(string ownerId, int centerX, int centerZ, int radius)
    {
        var zone = new Spacecraft.Shared.World.LandingZone
        {
            PlayerId = ownerId, LocationId = _world.LocationId,
            CenterX = centerX, CenterZ = centerZ, Radius = radius, Protected = true,
        };
        _repo.SaveLandingZone(zone);
        _landingZones[ownerId] = zone;
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
