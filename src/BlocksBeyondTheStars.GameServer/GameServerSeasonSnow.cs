// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3994: settling snow on a planet with opposite hemispheres. The snow pass decides "is snow falling" and "does it thaw" once
/// for the whole world on every other planet; here the north can lie in winter while the south has summer, so both questions
/// are asked at the latitude of each player (laying) and of each drift (thawing). The reading stays the world-level one at the
/// reference altitude — only the season's warmth follows the latitude.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The air temperature at the reference altitude of the active world with the season of a world Z.</summary>
    private float ReferenceTemperatureAtLatitude(double z)
    {
        var planet = _content.GetPlanet(_worlds.Active.PlanetType);
        if (planet?.Void == true)
        {
            return CabinComfortC;
        }

        double warmth = SeasonOf(_world.LocationId, planet, _dayLength, z, _world.Circumference).Warmth;
        return TemperatureAt(planet, _sim, _weatherState, _dayFraction, _breathable, default, warmth);
    }

    /// <summary>Whether snow settles at a world Z right now: snow is the falling form and the air is at freezing.</summary>
    private bool SnowFallsAtLatitude(double z)
    {
        float t = ReferenceTemperatureAtLatitude(z);
        return PrecipitationFor(_weatherState, t) is "snow" && t <= 1f;
    }

    /// <summary>Whether snow settles at the latitude of any player out on the surface.</summary>
    private bool SnowFallsNearAnyPlayer()
    {
        foreach (var session in JoinedInActiveWorld())
        {
            var p = session.State;
            if (!p.InEva && !p.AboardShip && SnowFallsAtLatitude(p.Position.Z))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam (#3994): whether snow would settle at a world Z now.</summary>
    public bool SnowFallsAtLatitudeForTest(double z) => SnowFallsAtLatitude(z);

    /// <summary>Test seam (#3994): the reference air temperature at a world Z.</summary>
    public float ReferenceTemperatureAtLatitudeForTest(double z) => ReferenceTemperatureAtLatitude(z);
}
