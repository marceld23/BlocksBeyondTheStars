// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The four seasons on the server (#3041, see <see cref="PlanetSeasons"/>): a body's season comes from the saved system
/// clock, its day length and its seeded phase. The season's warmth joins the air temperature (<c>TemperatureAt</c>, so the
/// suit, the precipitation form — winter snow — and the HUD all feel it), plant regrowth runs × the season's growth factor
/// (<c>WeatherRegrowFactor</c>), and the environment broadcast carries season, progress and warmth for the client's look.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>A body's season, how far into it the year is and the season's warmth (°C); a world without seasons reads
    /// ("", 0, 0).</summary>
    private (string Season, double Progress, double Warmth) SeasonOf(string bodyId, PlanetType? planet, double dayLengthSeconds)
    {
        if (!PlanetSeasons.HasSeasons(planet) || string.IsNullOrEmpty(bodyId))
        {
            return (string.Empty, 0.0, 0.0);
        }

        double year = PlanetSeasons.YearPosition(_systemTimeDays, dayLengthSeconds, PlanetSeasons.Phase(_meta.Seed, bodyId));
        return (PlanetSeasons.SeasonAt(year), PlanetSeasons.Progress(year), PlanetSeasons.TemperatureOffset(year, planet!.SeasonTemperature));
    }

    /// <summary>The active world's season.</summary>
    private (string Season, double Progress, double Warmth) ActiveSeason()
        => SeasonOf(_world.LocationId, _content.GetPlanet(_worlds.Active.PlanetType), _dayLength);

    /// <summary>Test seam (#3041): the active world's season.</summary>
    public (string Season, double Progress, double Warmth) ActiveSeasonForTest => ActiveSeason();

    /// <summary>Test seam (#3041): moves the system clock (the seasons run on it).</summary>
    public void SetSystemTimeDaysForTest(double days) => _systemTimeDays = days;
}
