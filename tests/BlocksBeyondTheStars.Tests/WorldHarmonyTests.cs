// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The colour harmony (terrain generation 48, <see cref="WorldHarmony"/>): a world's sky is the anchor and its plants,
/// clouds, mist and water answer it, in a mood and a plant scheme rolled from what fits the planet's kind
/// (<see cref="WorldPalettes"/>). A world of generation 47 or older keeps every colour bit for bit; authored colours and
/// poison water are never touched.
/// </summary>
public sealed class WorldHarmonyTests : IDisposable
{
    private const int Gen = WorldDescription.ColourHarmonyGeneration;
    private const int OldGen = Gen - 1;
    private const float HueTolerance = 0.03f;

    private static readonly string[] RolledTypes =
    {
        "rocky", "jungle", "ocean", "toxic_world", "meadowlands", "desert", "fungal", "ice", "lava", "crystal_living", "swamp", "savanna",
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_harmony_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static IEnumerable<(long Seed, string Id, string Name)> Worlds(int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return (4242 + (i * 7919L), $"sys{i % 7}-p{i % 5}", $"System {i % 7} · World {i}");
        }
    }

    [Fact]
    public void NewWorlds_AreCreatedWithTheHarmony()
    {
        Assert.Equal(48, Gen);
        Assert.True(WorldDescription.CurrentTerrainGeneration >= Gen);
        var rocky = _content.GetPlanet("rocky");
        Assert.False(WorldHarmony.For(1, "sys0-p1", rocky, OldGen).Active);
        Assert.True(WorldHarmony.For(1, "sys0-p1", rocky, Gen).Active);
        Assert.Equal(WorldHarmony.None, WorldHarmony.For(1, "sys0-p1", rocky, OldGen));
        Assert.Equal(WorldHarmony.For(1, "sys0-p1", rocky, Gen), WorldHarmony.For(1, "sys0-p1", rocky, Gen + 3)); // deterministic, and no later gate
        Assert.False(WorldHarmony.For(1, "ship:pilot", _content.GetPlanet("ship_interior"), Gen).Active); // a cabin has no sky to answer
    }

    [Fact]
    public void OlderWorlds_KeepEveryColour()
    {
        // Nothing between the hash-variety generation (26) and the harmony reads the generation for a colour, so a
        // world of generation 47 must equal the same world at 26 — and the harmony of an older world changes nothing.
        int classicGen = WorldDescription.HashVarietyGeneration;
        foreach (var (seed, id, name) in Worlds(60))
        {
            foreach (string type in RolledTypes.Concat(new[] { "titas", "toxica_maxima", "arena_nigra" }))
            {
                var planet = _content.GetPlanet(type)!;
                int sky = AtmosphereTints.SkyRgb(seed, id, planet, OldGen);
                Assert.Equal(AtmosphereTints.SkyRgb(seed, id, planet, classicGen), sky);
                Assert.Equal(AtmosphereTints.CloudRgb(seed, id, planet, sky, classicGen), AtmosphereTints.CloudRgb(seed, id, planet, sky, OldGen));
                Assert.Equal(FloraTints.ForWorld(seed, id, classicGen, planet), FloraTints.ForWorld(seed, id, OldGen, planet));
                Assert.Equal(FloraTints.ForWorld(seed, id, OldGen), FloraTints.ForWorld(seed, id, OldGen, planet)); // none of these types has a palette
                foreach (bool toxic in new[] { false, true })
                {
                    Assert.Equal(FluidTints.ForWorld(seed, id, planet, classicGen, toxic), FluidTints.ForWorld(seed, id, planet, OldGen, toxic));
                }

                Assert.Equal(MistProfiles.For(seed, id, planet, 0.6, airless: false), MistProfiles.For(seed, id, planet, 0.6, airless: false, null, OldGen));
                var none = WorldHarmony.For(seed, id, planet, OldGen);
                foreach (string key in new[] { "tree_leaves", "flora_fern", "flora_grasstuft", "mushroom_cap" })
                {
                    Assert.Equal(FloraTints.For(seed, name, key, planet), FloraTints.For(seed, name, key, planet, none));
                }
            }
        }
    }

    [Fact]
    public void TheSky_WearsTheWorldsMood_AndTheHarmonyIsBuiltAroundIt()
    {
        var planet = _content.GetPlanet("meadowlands")!;
        var seen = new HashSet<WorldMood>();
        foreach (var (seed, id, _) in Worlds(200))
        {
            int sky = AtmosphereTints.SkyRgb(seed, id, planet, Gen);
            Assert.Equal(sky, AtmosphereTints.SkyRgb(seed, id, planet, Gen)); // deterministic
            var harmony = WorldHarmony.For(seed, id, planet, Gen);
            Assert.Equal(WorldHarmony.MoodOf(seed, id, planet), harmony.Mood);
            Assert.Equal(HueOf(sky), harmony.SkyHue, 4);
            seen.Add(harmony.Mood);
            float value = ValueOf(sky);
            switch (harmony.Mood)
            {
                case WorldMood.Gloomy:
                    Assert.InRange(value, 0.45f, 0.70f); // dim, never black
                    Assert.True(SaturationOf(sky) <= 0.35f);
                    break;
                case WorldMood.Pastel:
                    Assert.True(value >= 0.86f, $"a pastel sky is light, got {value}");
                    break;
                default:
                    Assert.True(value >= 0.66f, $"a {harmony.Mood} sky is bright, got {value}");
                    break;
            }
        }

        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void TheCrowns_ShareOneHue_ThatAnswersTheSky()
    {
        var planet = _content.GetPlanet("meadowlands")!;
        var steps = new HashSet<int>();
        foreach (var (seed, id, name) in Worlds(120))
        {
            var harmony = WorldHarmony.For(seed, id, planet, Gen);

            // Beside the sky (a tenth of the wheel), a third away, or opposite — with two hundredths of its own.
            float apart = HueDistance(harmony.TreeHue, harmony.SkyHue);
            int step = apart < 0.2f ? 0 : apart < 0.42f ? 1 : 2;
            float expected = step == 0 ? 0.10f : step == 1 ? 0.33f : 0.5f;
            Assert.InRange(apart, expected - 0.021f, expected + 0.021f);
            steps.Add(step);

            foreach (string crown in TreeFoliage.LeafSlotKeys)
            {
                var c = FloraTints.For(seed, name, crown, planet, harmony);
                WorldHarmony.ToHsv(c.R, c.G, c.B, out float h, out _, out _);
                Assert.True(HueDistance(h, harmony.TreeHue) <= HueTolerance, $"{crown} on {id}: hue {h} is not the crowns' {harmony.TreeHue}");
            }
        }

        Assert.Equal(3, steps.Count); // all three answers occur
    }

    [Fact]
    public void EverySpecies_SitsInItsWorldsSchemeAndMood_AndStaysBrighterThanTheTrunks()
    {
        var seen = new HashSet<(WorldMood, HarmonyScheme)>();
        foreach (string type in new[] { "jungle", "ice", "lava", "crystal_living" })
        {
            var planet = _content.GetPlanet(type)!;
            foreach (var (seed, id, name) in Worlds(60))
            {
                var harmony = WorldHarmony.For(seed, id, planet, Gen);
                seen.Add((harmony.Mood, harmony.Scheme));
                var (satLo, satHi, valLo, valHi) = WorldHarmony.SpeciesBands(harmony.Mood);
                if (harmony.Scheme == HarmonyScheme.Monochrome)
                {
                    satLo *= 0.7f;
                    valLo = Math.Max(WorldHarmony.MinSpeciesValue, valLo - 0.1f);
                }

                var (wr, wg, wb) = FloraTints.ForWood(seed, name);
                float wood = Math.Max(wr, Math.Max(wg, wb));
                foreach (var species in FloraCatalog.All)
                {
                    var c = FloraTints.For(seed, name, species.Key, planet, harmony);
                    Assert.Equal(c, FloraTints.For(seed, name, species.Key, planet, harmony)); // deterministic
                    WorldHarmony.ToHsv(c.R, c.G, c.B, out float h, out float s, out float v);
                    Assert.InRange(s, satLo - 0.01f, satHi + 0.01f);
                    Assert.InRange(v, valLo - 0.01f, valHi + 0.01f);
                    Assert.True(v > wood, $"{species.Key} on {id} ({harmony.Mood}) is not brighter than the trunk");

                    float apart = HueDistance(h, harmony.TreeHue);
                    bool fits = TreeFoliage.IsTinted(species.Key)
                        ? apart <= HueTolerance
                        : harmony.Scheme switch
                        {
                            HarmonyScheme.Monochrome => apart <= HueTolerance,
                            HarmonyScheme.Rainbow => Math.Abs(apart - (MathF.Round(apart * 6f) / 6f)) <= HueTolerance,
                            _ => apart <= HueTolerance || apart is >= 0.09f and <= 0.20f || apart is >= 0.33f and <= 0.49f,
                        };
                    Assert.True(fits, $"{species.Key} on {id}: {apart} from the crowns' hue fits no family of {harmony.Scheme}");
                }
            }
        }

        Assert.True(seen.Count >= 12, $"only {seen.Count} of the 15 mood and scheme pairs occurred");
    }

    [Fact]
    public void Clouds_Mist_AndWater_AnswerTheSky_AndWhiteStaysWhite()
    {
        int tintedClouds = 0, whiteClouds = 0, darkDecks = 0, tintedMists = 0, whiteMists = 0, waters = 0;
        foreach (string type in RolledTypes)
        {
            var planet = _content.GetPlanet(type)!;
            foreach (var (seed, id, _) in Worlds(80))
            {
                var harmony = WorldHarmony.For(seed, id, planet, Gen);
                int sky = AtmosphereTints.SkyRgb(seed, id, planet, Gen);
                int cloudBefore = AtmosphereTints.CloudRgb(seed, id, planet, sky, OldGen); // the classic tint against the same sky
                int cloud = AtmosphereTints.CloudRgb(seed, id, planet, sky, Gen);
                Assert.Equal(AtmosphereTints.Harmonised(cloudBefore, sky, harmony), cloud);
                if (harmony.Mood == WorldMood.Gloomy)
                {
                    Assert.True(ValueOf(harmony.Cloud(cloudBefore)) <= 0.43f, "a gloomy world wears a dark deck");
                    darkDecks++;
                }
                else if (SaturationOf(cloudBefore) < WorldHarmony.MinTintSaturation)
                {
                    Assert.Equal(cloudBefore, cloud);
                    whiteClouds++;
                }
                else
                {
                    int turned = harmony.Cloud(cloudBefore);
                    Assert.InRange(HueDistance(HueOf(turned), harmony.SkyHue), WorldHarmony.CloudStep - HueTolerance, WorldHarmony.CloudStep + HueTolerance);
                    Assert.True(SaturationOf(turned) <= WorldHarmony.MaxCloudSaturation + 0.02f);
                    tintedClouds++;
                }

                int mistBefore = MistProfiles.For(seed, id, planet, 0.6, airless: false, null, OldGen).ColorRgb;
                int mist = MistProfiles.For(seed, id, planet, 0.6, airless: false, null, Gen).ColorRgb;
                Assert.True(Luma(mist) >= MistProfiles.MinLuma - 0.01f, $"the mist of {type} {id} went dark");
                if (SaturationOf(mistBefore) < WorldHarmony.MinMistSaturation)
                {
                    Assert.Equal(mistBefore, mist);
                    whiteMists++;
                }
                else if (SaturationOf(mist) >= 0.08f)
                {
                    Assert.InRange(HueDistance(HueOf(mist), harmony.SkyHue), WorldHarmony.MistStep - HueTolerance, WorldHarmony.MistStep + HueTolerance);
                    tintedMists++;
                }

                var (waterBefore, modeBefore) = FluidTints.ForWorld(seed, id, planet, OldGen);
                var (water, mode) = FluidTints.ForWorld(seed, id, planet, Gen);
                Assert.Equal(modeBefore, mode);
                if (mode == FluidTints.Mode.Tint && SaturationOf(waterBefore) >= WorldHarmony.MinTintSaturation)
                {
                    // Clean water stays in the inviting arc — sea green to indigo — whatever the sky.
                    Assert.InRange(HueOf(water), WorldHarmony.WaterHueMin - 0.01f, WorldHarmony.WaterHueMax + 0.01f);
                    waters++;
                }
                else
                {
                    Assert.Equal(waterBefore, water);
                }

                // The plants' base hue — the grass — is the crowns'.
                int flora = FloraTints.ForWorld(seed, id, Gen, planet);
                Assert.True(HueDistance(HueOf(flora), harmony.TreeHue) <= HueTolerance, $"the grass of {type} {id} is not the crowns' hue");
            }
        }

        Assert.True(tintedClouds > 50 && whiteClouds > 50 && darkDecks > 50, $"clouds: {tintedClouds} tinted, {whiteClouds} white, {darkDecks} dark");
        Assert.True(tintedMists > 100 && whiteMists > 100, $"mists: {tintedMists} tinted, {whiteMists} white");
        Assert.True(waters > 100, $"only {waters} tinted waters");
    }

    [Fact]
    public void PoisonWater_KeepsItsWarningColours_OutsideTheArcOfCleanWater()
    {
        var toxic = _content.GetPlanet("toxic_world")!;
        foreach (var (seed, id, _) in Worlds(200))
        {
            var poison = FluidTints.ForWorld(seed, id, toxic, Gen, toxicWater: true);
            Assert.Equal(FluidTints.ForWorld(seed, id, toxic, OldGen, toxicWater: true), poison);
            Assert.True(HueOf(poison.Rgb) < WorldHarmony.WaterHueMin - 0.05f, $"poison water on {id} reads like clean water");
        }
    }

    [Fact]
    public void AuthoredColours_Stay_AndArenaNigraIsGloomyOnEverySave()
    {
        var arena = _content.GetPlanet("arena_nigra")!;
        Assert.Equal("gloomy", arena.ColourMood);
        foreach (var (seed, id, name) in Worlds(30))
        {
            // A type that authors its sky keeps it, the clouds painted against it, and its authored mist.
            foreach (string type in new[] { "toxica_maxima", "sounder_world", "arena_nigra" })
            {
                var planet = _content.GetPlanet(type)!;
                Assert.True(planet.SkyColor > 0);
                int sky = AtmosphereTints.SkyRgb(seed, id, planet, Gen);
                Assert.Equal(planet.SkyColor, sky);
                Assert.Equal(AtmosphereTints.CloudRgb(seed, id, planet, sky, OldGen), AtmosphereTints.CloudRgb(seed, id, planet, sky, Gen));
                if (planet.MistColor > 0)
                {
                    Assert.Equal(MistProfiles.For(seed, id, planet, 0.6, airless: false, null, OldGen), MistProfiles.For(seed, id, planet, 0.6, airless: false, null, Gen));
                }
            }

            // The school club's world of the giant sandworm is gloomy, whatever the roll.
            Assert.Equal(WorldMood.Gloomy, WorldHarmony.MoodOf(seed, id, arena));
            Assert.Equal(WorldMood.Gloomy, WorldHarmony.For(seed, id, arena, Gen).Mood);

            // A flora palette (Soaniri's light greens) stays the palette.
            var soaniri = _content.GetPlanet("sounder_world")!;
            Assert.NotEmpty(soaniri.FloraPalette);
            var harmony = WorldHarmony.For(seed, id, soaniri, Gen);
            Assert.True(harmony.Active);
            Assert.Equal(FloraTints.For(seed, name, "flora_fern", soaniri), FloraTints.For(seed, name, "flora_fern", soaniri, harmony));
            Assert.Equal(FloraTints.ForWorld(seed, id, OldGen, soaniri), FloraTints.ForWorld(seed, id, Gen, soaniri));

            // Fixed and rainbow water stay.
            foreach (string type in new[] { "titas", "toxica_maxima", "rainbow_sea" })
            {
                var planet = _content.GetPlanet(type)!;
                Assert.Equal(FluidTints.ForWorld(seed, id, planet, OldGen), FluidTints.ForWorld(seed, id, planet, Gen));
            }
        }
    }

    [Fact]
    public void TheChoices_FollowThePlanetsKind_AndWorldsRarelyLookAlike()
    {
        static int Bin(float hue) => (int)(hue * 12f) % 12;
        var signatures = new Dictionary<string, int>();
        var share = new Dictionary<string, Dictionary<WorldMood, int>>();
        int total = 0;
        const int PerType = 500;
        foreach (string type in new[] { "ice", "lava", "jungle", "crystal_living", "desert", "rocky" })
        {
            var planet = _content.GetPlanet(type)!;
            var moods = share[type] = new Dictionary<WorldMood, int>();
            var schemes = new HashSet<HarmonyScheme>();
            for (int i = 0; i < PerType; i++)
            {
                var h = WorldHarmony.For(1000 + (i * 104729L), $"sys{i % 40}-p{i % 9}", planet, Gen);
                moods[h.Mood] = moods.GetValueOrDefault(h.Mood) + 1;
                schemes.Add(h.Scheme);
                string signature = $"{Bin(h.SkyHue)}/{h.Mood}/{h.Scheme}/{Bin(h.TreeHue)}";
                signatures[signature] = signatures.GetValueOrDefault(signature) + 1;
                total++;
            }

            Assert.Equal(5, moods.Count);   // every kind can roll every mood …
            Assert.Equal(3, schemes.Count); // … and every scheme
        }

        // What fits the kind is rolled more often: pale ice, gloomy or earthy volcanoes, vivid jungles and crystal worlds.
        Assert.True(share["ice"][WorldMood.Pastel] > 2 * share["lava"][WorldMood.Pastel]);
        Assert.True(share["lava"][WorldMood.Gloomy] > 2 * share["jungle"][WorldMood.Gloomy]);
        Assert.True(share["lava"][WorldMood.Gloomy] + share["lava"][WorldMood.Earthy] > PerType / 2);
        Assert.True(share["jungle"][WorldMood.Vivid] > 2 * share["lava"][WorldMood.Vivid]);
        Assert.True(share["crystal_living"][WorldMood.Vivid] > share["rocky"][WorldMood.Vivid]);
        Assert.True(share["desert"][WorldMood.Earthy] > share["jungle"][WorldMood.Earthy]);

        // Variety: by sky hue, mood, scheme and crown hue (twelve steps each) the worlds spread over hundreds of looks.
        Assert.True(signatures.Count > 500, $"only {signatures.Count} distinct looks over {total} worlds");
        Assert.True(signatures.Values.Max() < total * 0.02, $"one look covers {signatures.Values.Max()} of {total} worlds");
    }

    [Fact]
    public void AGlowingPlant_CastsTheColourItWears()
    {
        var planet = _content.GetPlanet("jungle")!;
        var vine = _content.Blocks.Values.First(b => b.LightColor is int l && (l & 0xFFFFFF) != 0 && b.Key.StartsWith("flora_", StringComparison.Ordinal));
        foreach (var (seed, id, name) in Worlds(20))
        {
            var harmony = WorldHarmony.For(seed, id, planet, Gen);
            Assert.Equal(PlantGlowLight.PackedFor(seed, name, vine, planet), PlantGlowLight.PackedFor(seed, name, vine, planet, WorldHarmony.None));
            var worn = FloraTints.For(seed, name, vine.Key, planet, harmony);
            int light = BlockLight.ColorFrom(PlantGlowLight.PackedFor(seed, name, vine, planet, harmony));
            WorldHarmony.ToHsv(worn.R, worn.G, worn.B, out float wornHue, out float wornSaturation, out _);
            if (wornSaturation >= 0.2f)
            {
                Assert.True(HueDistance(HueOf(light), wornHue) <= HueTolerance, $"{vine.Key} on {id}: the light is not the leaves' colour");
            }
        }
    }

    [Fact]
    public void TheServer_ShipsTheHarmony_AndTheOlderSaveItsClassicColours()
    {
        foreach (int generation in new[] { OldGen, Gen })
        {
            var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "harmony" + generation));
            using (repo)
            {
                var config = new ServerConfig { WorldName = "harmony" + generation, Seed = 4242, StartPlanet = "jungle", AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
                config.World.TerrainGeneration = generation;
                var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
                server.Start();
                server.AddLocalPlayer("Pilot");

                var body = server.Galaxy.FindBody(server.ActiveLocationId)!;
                var planet = _content.GetPlanet(body.PlanetType!);
                int sky = AtmosphereTints.SkyRgb(4242, body.Id, planet, generation);
                Assert.Equal(sky, server.SkyColor);
                Assert.Equal(AtmosphereTints.CloudRgb(4242, body.Id, planet, sky, generation), server.CloudColorForTest);
                Assert.Equal(FloraTints.ForWorld(4242, body.Id, generation, planet), server.FloraTint);
                if (generation == OldGen)
                {
                    Assert.Equal(FloraTints.ForWorld(4242, body.Id, generation), server.FloraTint); // the classic roll
                }
            }
        }
    }

    private static float HueOf(int rgb)
    {
        WorldHarmony.ToHsv(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, out float h, out _, out _);
        return h;
    }

    private static float SaturationOf(int rgb)
    {
        WorldHarmony.ToHsv(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, out _, out float s, out _);
        return s;
    }

    private static float ValueOf(int rgb)
    {
        WorldHarmony.ToHsv(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, out _, out _, out float v);
        return v;
    }

    private static float Luma(int rgb)
        => (0.299f * ((rgb >> 16) & 0xFF) / 255f) + (0.587f * ((rgb >> 8) & 0xFF) / 255f) + (0.114f * (rgb & 0xFF) / 255f);

    /// <summary>The distance of two hues on the colour wheel, 0..0.5.</summary>
    private static float HueDistance(float a, float b)
    {
        float d = Math.Abs(a - b) % 1f;
        return d > 0.5f ? 1f - d : d;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort — a still-open SQLite handle on Windows
        }
    }
}
