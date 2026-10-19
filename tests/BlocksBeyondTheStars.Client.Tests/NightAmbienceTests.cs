// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2825: the night layer picks crickets, frogs, a night wind, a hum or chimes from what the procedural world actually
/// has — Marcel: "remember the worlds are procedural, frogs on a lava planet would be wrong". The sweeps run every planet
/// type of the data, so a new type is held to the same rules the day it is added.
/// </summary>
public sealed class NightAmbienceTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    /// <summary>The most inviting spot there is: plants all around and a pond in reach.</summary>
    private static readonly NightAmbience.Surroundings Lush = new(vegetation: 40, water: true, lava: false);

    private static readonly NightAmbience.Surroundings Meadow = new(vegetation: 40, water: false, lava: false);

    private static readonly NightAmbience.Surroundings Bare = new(vegetation: 0, water: false, lava: false);

    private static PlanetType Planet(string key) => Content.GetPlanet(key) ?? throw new Xunit.Sdk.XunitException($"no planet type '{key}'");

    /// <summary>The environment the server sends at the planet type's own midnight (base temperature, minus the night swing:
    /// 6 °C with breathable air, 16 °C without).</summary>
    private static WorldEnvironment Midnight(PlanetType p, float? temperature = null, float season = 0.5f, string precipitation = "none")
    {
        bool airless = p.IsAirless || p.SpaceSky;
        bool breathable = string.Equals(p.Atmosphere, "breathable", System.StringComparison.Ordinal);
        return new WorldEnvironment
        {
            TimeOfDay = 0f,
            Biome = p.Key,
            Breathable = breathable,
            SpaceSky = p.SpaceSky,
            AtmosphereDensity = airless ? 0f : 0.4f,
            Temperature = temperature ?? (float)(p.BaseTemperature - (breathable ? 6.0 : 16.0)),
            SeasonWetness = season,
            Precipitation = precipitation,
        };
    }

    private static string PickAt(string key, NightAmbience.Surroundings near, float? temperature = null, float season = 0.5f,
        string precipitation = "none")
    {
        var p = Planet(key);
        return NightAmbience.Pick(p, Midnight(p, temperature, season, precipitation), near);
    }

    [Fact]
    public void NightLevel_IsZeroByDay_FullAtNight_AndRisesThroughDusk()
    {
        Assert.Equal(0f, NightAmbience.NightLevel(0.5f));
        Assert.Equal(0f, NightAmbience.NightLevel(0.3f));
        Assert.Equal(1f, NightAmbience.NightLevel(0f));
        Assert.Equal(1f, NightAmbience.NightLevel(0.9f));

        float previous = 0f;
        for (float t = 0.70f; t <= 0.80f; t += 0.005f)
        {
            float level = NightAmbience.NightLevel(t);
            Assert.True(level >= previous, $"the dusk fade dips at t = {t}");
            previous = level;
        }

        float sunset = NightAmbience.NightLevel(0.75f);
        Assert.True(sunset > 0f && sunset < 1f, "the layer is still fading in at sunset");
        Assert.Equal(NightAmbience.NightLevel(0.75f), NightAmbience.NightLevel(0.25f), 3); // dawn mirrors dusk
    }

    [Fact]
    public void EveryPlanetType_NoChorusWhereNothingCanSing()
    {
        // The most inviting surroundings on every type, at its own midnight and on a mild 20 °C night: crickets and frogs
        // only on a living world with breathable air that is neither scorched nor poisoned; nothing at all without air.
        foreach (var p in Content.Planets.Values)
        {
            foreach (float? temperature in new float?[] { null, 20f })
            {
                var env = Midnight(p, temperature);
                string pick = NightAmbience.Pick(p, env, Lush);
                if (p.IsAirless || p.SpaceSky || p.Void || p.IsGasWorld)
                {
                    Assert.True(pick == NightAmbience.None, $"{p.Key}: '{pick}' without air to carry it");
                }

                if (pick is NightAmbience.Crickets or NightAmbience.Frogs)
                {
                    Assert.True(env.Breathable && p.HasLife, $"{p.Key}: '{pick}' on a world where nothing breathes or lives");
                    Assert.False(NightAmbience.IsScorched(p, env, Lush), $"{p.Key}: '{pick}' on a scorched world");
                    Assert.False(NightAmbience.IsPoisoned(p, env), $"{p.Key}: '{pick}' on a poisoned world");
                }
            }
        }
    }

    [Fact]
    public void LavaAndAshWorlds_StaySilent_EvenBesideAPond()
    {
        var volcanic = Content.Planets.Values
            .Where(p => p.FloraTheme == "ashen" || p.LavaAbundance >= NightAmbience.VolcanicLava || p.Key == "lava")
            .ToList();
        Assert.Contains(volcanic, p => p.Key == "lava");
        Assert.Contains(volcanic, p => p.Key == "ashen");

        foreach (var p in volcanic)
        {
            Assert.Equal(NightAmbience.None, NightAmbience.Pick(p, Midnight(p), Lush));
        }
    }

    [Theory]
    [InlineData("toxic_world")]
    [InlineData("toxica_maxima")]
    public void PoisonedWorlds_StaySilent(string key)
        => Assert.Equal(NightAmbience.None, PickAt(key, Lush, temperature: 20f));

    [Theory]
    [InlineData("asteroid")]
    [InlineData("asteroid_icy")]
    [InlineData("crystal")]
    [InlineData("ship_interior")]
    [InlineData("orbital_station")]
    [InlineData("gas_giant")]
    public void NoAirOrNoGround_StaysSilent(string key)
        => Assert.Equal(NightAmbience.None, PickAt(key, Lush, temperature: 20f));

    [Fact]
    public void AWarmJungleBesideAPond_Frogs() => Assert.Equal(NightAmbience.Frogs, PickAt("jungle", Lush));

    [Fact]
    public void AJungleWithoutWaterInReach_Crickets() => Assert.Equal(NightAmbience.Crickets, PickAt("jungle", Meadow));

    [Fact]
    public void AMeadowAtNight_Crickets() => Assert.Equal(NightAmbience.Crickets, PickAt("meadowlands", Meadow));

    [Fact]
    public void ATemperatePond_FrogsOnlyInTheWetSeason()
    {
        Assert.Equal(NightAmbience.Crickets, PickAt("meadowlands", Lush, temperature: 15f, season: 0.4f));
        Assert.Equal(NightAmbience.Frogs, PickAt("meadowlands", Lush, temperature: 15f, season: 0.8f));
    }

    [Fact]
    public void TooColdForFrogs_StillWarmEnoughForCrickets()
        => Assert.Equal(NightAmbience.Crickets, PickAt("jungle", Lush, temperature: NightAmbience.FrogMinC - 1f));

    [Fact]
    public void ABareSpotOfALivingWorld_StaysQuiet() => Assert.Equal(NightAmbience.None, PickAt("jungle", Bare));

    [Theory]
    [InlineData("ice")]
    [InlineData("tundra")]
    [InlineData("glacier")]
    [InlineData("desert")]
    [InlineData("rocky")]
    public void ColdOrBarrenWorlds_TheNightWind(string key) => Assert.Equal(NightAmbience.Wind, PickAt(key, Meadow));

    [Fact]
    public void AFrostyNightOnAGreenWorld_TheNightWind()
        => Assert.Equal(NightAmbience.Wind, PickAt("meadowlands", Meadow, temperature: NightAmbience.InsectMinC - 1f));

    [Theory]
    [InlineData("fungal")]
    [InlineData("corrupted")]
    public void FungalAndAlienFlora_TheHum(string key) => Assert.Equal(NightAmbience.Hum, PickAt(key, Meadow));

    [Fact]
    public void CrystalFlora_TheChimes() => Assert.Equal(NightAmbience.Chimes, PickAt("crystal_living", Meadow));

    [Fact]
    public void LavaInReach_SilencesEvenAJungle()
        => Assert.Equal(NightAmbience.None, PickAt("jungle", new NightAmbience.Surroundings(40, water: true, lava: true)));

    [Theory]
    [InlineData("acid")]
    [InlineData("ash")]
    public void AcidOrAshFalling_Silences(string precipitation)
        => Assert.Equal(NightAmbience.None, PickAt("jungle", Lush, precipitation: precipitation));

    [Fact]
    public void AScorchingNight_Silences() => Assert.Equal(NightAmbience.None, PickAt("jungle", Lush, temperature: NightAmbience.ScorchedC));

    [Fact]
    public void UnknownWorld_Silence()
    {
        Assert.Equal(NightAmbience.None, NightAmbience.Pick(null, new WorldEnvironment(), Lush));
        Assert.Equal(NightAmbience.None, NightAmbience.Pick(Planet("jungle"), null, Lush));
    }

    [Theory]
    [InlineData("tree_leaves", "flora", true)]
    [InlineData("flora_fern", "flora", true)]
    [InlineData("grass", "terrain", true)]
    [InlineData("alien_grass", "terrain", true)]
    [InlineData("stone", "terrain", false)]
    [InlineData("water", "terrain", false)]
    public void Vegetation_IsFloraOrGrassyGround(string key, string category, bool expected)
        => Assert.Equal(expected, NightAmbience.IsVegetation(key, category));

    [Fact]
    public void WaterAndLava_AreTheBlocksThemselves()
    {
        Assert.True(NightAmbience.IsWater("water"));
        Assert.False(NightAmbience.IsWater("water_spout")); // a machine, standing on dry land
        Assert.True(NightAmbience.IsLava("lava"));
        Assert.False(NightAmbience.IsLava("water"));
    }

    [Fact]
    public void EveryNightLoop_IsBundled()
    {
        // The clips ClientAudio binds to the night layer — under Resources/audio, next to the other beds.
        foreach (string id in NightAmbience.All)
        {
            string path = System.IO.Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "Resources", "audio", id + ".mp3");
            Assert.True(System.IO.File.Exists(path), $"missing night loop {id}.mp3");
            Assert.True(System.IO.File.Exists(path + ".meta"), $"missing meta of {id}.mp3");
        }
    }
}
