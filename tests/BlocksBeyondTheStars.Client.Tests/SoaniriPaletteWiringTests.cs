// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3423 (Justus, 2026-11-08: everything on Soaniri light blue and light green) on the client: the season look keeps an
/// evergreen world's plants in their colours (<see cref="SeasonLook.TargetFor"/>, unit-tested), and the Unity wiring is
/// guarded as source text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern): the flora
/// colours are built with the world's planet type (its flora palette), the sky asks whether the type is evergreen, and the
/// Soundeling, Soundrak and Sounder builders take their colours from the shared rules. #3448: the rolled animals wear fur and
/// skin (the "cold" theme), the builder keeps their coat marks in the planet's fauna palette and the micro-fauna take their
/// colours from it.
/// </summary>
public sealed class SoaniriPaletteWiringTests
{
    private static string ScriptsDir() => Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");

    private static string Script(string file) => File.ReadAllText(Path.Combine(ScriptsDir(), file));

    [Fact]
    public void AnEvergreenWorld_KeepsItsPlantsThroughAutumnAndWinter_ButStillBlooms()
    {
        foreach (string season in new[] { PlanetSeasons.Spring, PlanetSeasons.Summer, PlanetSeasons.Autumn, PlanetSeasons.Winter })
        {
            for (int step = 0; step <= 10; step++)
            {
                float p = step / 10f;
                var look = SeasonLook.TargetFor(season, p, evergreenFlora: true);
                Assert.Equal(0f, look.Autumn);
                Assert.Equal(0f, look.Winter);
                Assert.Equal(SeasonLook.Target(season, p).Bloom, look.Bloom); // spring's blossoms still open
                Assert.Equal(SeasonLook.Target(season, p), SeasonLook.TargetFor(season, p, evergreenFlora: false));
            }
        }

        Assert.Equal(1f, SeasonLook.TargetFor(PlanetSeasons.Spring, 0.6f, evergreenFlora: true).Bloom, 3);
    }

    [Fact]
    public void TheSky_AsksWhetherTheWorldsTypeIsEvergreen()
    {
        string sky = Script("Sky.Seasons.cs");
        Assert.Contains("SeasonLook.TargetFor(env.Season, env.SeasonProgress, EvergreenFlora())", sky);
        Assert.Contains("Game.Content.GetPlanet(Game.LoadingPlanetType ?? string.Empty)", sky);
        Assert.Contains("planet.EvergreenFlora", sky);
    }

    [Fact]
    public void TheFloraColours_AreBuiltWithTheWorldsPlanetType()
    {
        string boot = Script("GameBootstrap.cs");
        Assert.Equal(2, CountOf(boot, "RebuildFloraTints(m.PlanetType);")); // the join and every world change
        string env = Script("GameBootstrap.Environment.cs");
        Assert.Contains("_floraPlanet = Content.GetPlanet(planetType ?? string.Empty);", env);
        Assert.Contains("FloraTints.For(_worldSeed, LocationName, def.Key, _floraPlanet)", env);
        Assert.Contains("PlantGlowLight.PackedFor(_worldSeed, LocationName, glower, _floraPlanet)", env);
        Assert.Contains("FloraTints.For(_worldSeed, LocationName, sp.Key, _floraPlanet)", env);
        Assert.Contains("\"tree_leaves\", planet); // #3423", Script("FarTerrainView.cs"));
        Assert.Contains("FloraKeyFor(planet.SurfaceBlock), planet); // #3423", Script("PlanetOrbitLook.cs"));
    }

    [Fact]
    public void TheBuilders_TakeSoaniriColoursFromTheSharedRules()
    {
        Assert.Contains("SoundelingPastels = SoundelingRules.Pastels;", Script("CreatureBuilder.Soundeling.cs"));
        string rak = Script("CreatureBuilder.Soundrak.cs");
        Assert.Contains("SoundrakRules.Skin", rak);
        Assert.Contains("SoundrakRules.Belly", rak);
        string sounder = Script("CreatureBuilder.Sounder.cs");
        Assert.Contains("SounderRules.SkinTones[0]", sounder);
        Assert.DoesNotContain("0.95f, 0.75f, 0.8f", sounder); // the pink inner ears are gone
    }

    [Fact]
    public void SoanirisRolledAnimals_WearFurAndSkin_AndTheirMarksAndTheBugsStayInThePalette()
    {
        // #3448: the "cold" theme (warm coats, soft skins) instead of the crystal world's derived crystal and armour hides.
        var soaniri = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()).GetPlanet("sounder_world")!;
        Assert.Equal("cold", FaunaThemes.ThemeOf(soaniri));
        Assert.Contains(FaunaThemes.Themes["cold"], f => f.Family == "warmcoat");
        Assert.Contains(FaunaThemes.Themes["cold"], f => f.Family == "softskin");
        Assert.NotEmpty(FaunaTints.PaletteOf(soaniri));

        // The builder hands the world's look (with its palette) to the pattern colour; the micro-fauna read the type's palette.
        Assert.Contains("CreatureCoat.PatternRgb(seed, c.ColorRgb, pattern, _look)", Script("CreatureBuilder.cs"));
        string bugs = Script("MicroFaunaView.cs");
        Assert.Contains("RefreshFaunaPalette();", bugs);
        Assert.Contains("FaunaTints.PaletteOf(Game.Content.GetPlanet(type))", bugs);
        Assert.Contains("Color[] palette = inPalette ? _faunaPalette : kind.Palette;", bugs);
        Assert.Contains("c.Tint = TintFor(kind);", bugs);
    }

    private static int CountOf(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
