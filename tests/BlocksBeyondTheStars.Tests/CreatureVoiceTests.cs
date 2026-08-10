// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The generated creature voice (#902–#907). The properties under test are the ones that are invisible
/// by ear until they have already shipped: determinism, the bounded sampler key space that keeps the
/// client's bake cache from growing with the world seed, and the pool-growth guarantee.
/// </summary>
public class CreatureVoiceTests
{
    private static VoiceTraits Traits(
        string habitat = "Land", string temperament = "Passive", string bodyPlan = "Standard",
        float size = 1.2f, int legs = 4, int eyes = 2, int horns = 0, int segments = 1,
        int tentacles = 0, bool gasSac = false, bool glows = false)
        => new VoiceTraits(habitat, temperament, bodyPlan, size, legs, eyes, horns, segments,
            tentacles, gasSac, glows);

    [Fact]
    public void Derive_IsDeterministic()
    {
        var a = CreatureVoices.Derive(12345, Traits());
        var b = CreatureVoices.Derive(12345, Traits());

        Assert.Equal(a.Call, b.Call);
        Assert.Equal(a.Op, b.Op);
        Assert.Equal(a.Pulses, b.Pulses);
        Assert.Equal(a.PulseGapMs, b.PulseGapMs);
        Assert.Equal(a.Contour, b.Contour);
        Assert.Equal(a.CadenceMin, b.CadenceMin);
        Assert.Equal(a.BakeKey, b.BakeKey);
    }

    [Fact]
    public void Derive_UsesAStableHash_NotTheProcessRandomisedStringHash()
    {
        // .NET randomises string.GetHashCode per process, which would re-voice every species between
        // runs (#720). These constants pin the mixer: if they ever change, the hash changed — which is
        // fine deliberately, and a bug by accident.
        Assert.Equal("creature_call_yelp", CreatureVoices.Derive(1, Traits()).Call);
        Assert.Equal(23, CreatureVoices.Derive(1, Traits()).PitchStep);
        Assert.Equal(VoiceContour.Bowl, CreatureVoices.Derive(1, Traits()).Contour);
    }

    [Fact]
    public void DifferentSeeds_ProduceDifferentVoices()
    {
        var seen = new HashSet<string>();
        for (int seed = 1; seed <= 400; seed++)
        {
            var v = CreatureVoices.Derive(seed, Traits());
            seen.Add($"{v.Call}|{v.Op}|{v.Pulses}|{v.Contour}|{(int)v.CadenceMin}");
        }

        // The whole point of the feature: before it, a land species had 22 possible voices in total.
        Assert.True(seen.Count > 150, $"only {seen.Count} distinct voices across 400 seeds");
    }

    [Fact]
    public void EveryBakeKeyComponent_ComesFromAFiniteDomain()
    {
        // The design rule behind #901. The key space is genuinely large (call x layer x op x amount x
        // tail runs into six figures) — what matters is that every component is DISCRETE. One continuous
        // float in here (a raw op amount, a raw detune ratio) and no two species could ever share a bake,
        // which is what turns the client's cache from "bounded working set" into "grows with the seed".
        var calls = new HashSet<string>(CreatureVoices.AllCalls) { "-", string.Empty };
        var offsets = new HashSet<int> { 0, 45, 70, 100 };
        string[] habitats = { "Land", "Cave", "Water", "Lava", "Air", "Amphibian" };

        foreach (var habitat in habitats)
        {
            for (int seed = 1; seed <= 4000; seed++)
            {
                var v = CreatureVoices.Derive(seed, Traits(habitat: habitat));
                var parts = v.BakeKey.Split('|');
                Assert.Equal(7, parts.Length);
                Assert.Contains(parts[0], calls);
                Assert.Contains(parts[1], calls);
                Assert.Contains(int.Parse(parts[2]), offsets);
                Assert.InRange(int.Parse(parts[3]), 0, 2); // layer detune step
                Assert.InRange(int.Parse(parts[4]), 0, 8); // VoiceOp
                Assert.InRange(int.Parse(parts[5]), 0, 2); // op amount
                Assert.InRange(int.Parse(parts[6]), 0, 2); // reverb tail
            }
        }
    }

    [Fact]
    public void OnePlanetsFauna_FitsTheClientsSamplerCache()
    {
        // What actually decides whether #901 stays fixed: a planet's whole roster, with every combat cue,
        // has to fit inside the client's 64-entry LRU. Otherwise voices thrash and re-bake mid-play.
        var roster = CreatureGenerator.GenerateRoster(TestPlanet(), worldSeed: 20260810);
        var keys = new HashSet<string>();
        foreach (var sp in roster)
        {
            var voice = CreatureVoices.Derive(sp.VoiceSeed, VoiceTraits.From(sp));
            keys.Add("call|" + voice.BakeKey);
            foreach (var cue in new[] { "_hurt", "_alert", "_attack", "_die" })
            {
                keys.Add(cue + "|" + voice.BakeKey);
            }
        }

        Assert.True(keys.Count <= 64, $"a single planet needs {keys.Count} baked variants — the client caches 64");
    }

    [Theory]
    [InlineData("Cave")]
    [InlineData("Water")]
    public void EyelessDarkDwellers_SpeakInClickTrains(string habitat)
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var v = CreatureVoices.Derive(seed, Traits(habitat: habitat, eyes: 0));
            Assert.True(v.Pulses >= 4, $"seed {seed}: {v.Pulses} pulses");
            Assert.True(v.PulseGapMs <= 110, $"seed {seed}: {v.PulseGapMs} ms gap");
            Assert.Equal(VoiceContour.Rising, v.Contour);
        }
    }

    [Fact]
    public void Titans_BellowSlowlyAndRarely()
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var v = CreatureVoices.Derive(seed, Traits(bodyPlan: "Titan", size: 4.5f));
            Assert.True(v.Pulses <= 2, $"seed {seed}: {v.Pulses} pulses");
            Assert.True(v.CadenceMin >= 14f, $"seed {seed}: cadence {v.CadenceMin}");
        }
    }

    [Fact]
    public void Medusae_AreAlmostSilent()
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            var v = CreatureVoices.Derive(seed, Traits(bodyPlan: "Medusa", legs: 0, tentacles: 8));
            Assert.Equal(1, v.Pulses);
            Assert.True(v.CadenceMin >= 26f, $"seed {seed}: cadence {v.CadenceMin}");
        }
    }

    [Fact]
    public void Hunters_GetTheSaturatedVoice()
    {
        for (int seed = 1; seed <= 50; seed++)
        {
            Assert.Equal(VoiceOp.Drive, CreatureVoices.Derive(seed, Traits(temperament: "Aggressive")).Op);
            Assert.Equal(VoiceOp.Drive, CreatureVoices.Derive(seed, Traits(temperament: "PackHunter")).Op);
        }
    }

    [Fact]
    public void GrowingACallPool_KeepsMostSpeciesOnTheirOldVoice()
    {
        // The #905 guarantee. With `pool[hash % length]`, adding one sample re-rolled EVERY species in
        // that habitat on every existing world; rendezvous hashing moves only ~1/N of them.
        var before = new List<string> { "a", "b", "c", "d", "e", "f", "g", "h" };
        var after = new List<string>(before) { "i" };

        int changed = 0;
        const int species = 4000;
        for (int seed = 1; seed <= species; seed++)
        {
            if (CreatureVoices.Rendezvous(seed, "call", before) != CreatureVoices.Rendezvous(seed, "call", after))
            {
                changed++;
            }
        }

        double ratio = changed / (double)species;
        Assert.True(ratio < 0.16, $"{ratio:P1} of species changed voice — expected roughly 1/9");

        // And every species that moved must have moved TO the new call, never between two old ones.
        for (int seed = 1; seed <= species; seed++)
        {
            string a = CreatureVoices.Rendezvous(seed, "call", before);
            string b = CreatureVoices.Rendezvous(seed, "call", after);
            if (a != b)
            {
                Assert.Equal("i", b);
            }
        }
    }

    [Fact]
    public void EveryCallThePoolsCanPick_HasAShippedAsset()
    {
        // A missing sample is a silent animal, and silence is exactly the kind of bug nobody files.
        var audioDir = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "Resources", "audio");
        var missing = new List<string>();
        foreach (var call in CreatureVoices.AllCalls)
        {
            if (!File.Exists(Path.Combine(audioDir, call + ".mp3")))
            {
                missing.Add(call);
            }
        }

        Assert.True(missing.Count == 0, "no audio asset for: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryVoiceDescriptor_HasAGermanAndEnglishString()
    {
        var en = TestLocales.Load("en");
        var de = TestLocales.Load("de");
        var keys = new HashSet<string>();
        string[] habitats = { "Land", "Cave", "Water", "Lava", "Air", "Amphibian" };
        foreach (var habitat in habitats)
        {
            for (int seed = 1; seed <= 500; seed++)
            {
                keys.Add(CreatureVoices.DescriptorKey(CreatureVoices.Derive(seed, Traits(habitat: habitat))));
                keys.Add(CreatureVoices.DescriptorKey(CreatureVoices.Derive(seed, Traits(habitat: habitat, eyes: 0))));
                keys.Add(CreatureVoices.DescriptorKey(CreatureVoices.Derive(seed, Traits(habitat: habitat, bodyPlan: "Titan", size: 4f))));
            }
        }

        foreach (var key in keys)
        {
            Assert.True(en.ContainsKey(key), "missing English string for " + key);
            Assert.True(de.ContainsKey(key), "missing German string for " + key);
        }
    }

    [Fact]
    public void GeneratedSpecies_GetDistinctVoiceSeeds_AcrossPlanetsToo()
    {
        // Species ids are "sp0".."sp8" and repeat on EVERY planet — hashing the id (as the client used
        // to) gave the whole game nine voices per habitat. The seed must break that.
        var planet = TestPlanet();
        var a = CreatureGenerator.GenerateRoster(planet, worldSeed: 4242);
        var b = CreatureGenerator.GenerateRoster(planet, worldSeed: 9999);

        Assert.NotEmpty(a);
        Assert.All(a, sp => Assert.NotEqual(0, sp.VoiceSeed));
        Assert.Equal(a.Count, a.Select(sp => sp.VoiceSeed).Distinct().Count());

        // Same species index, different world → a different voice.
        Assert.NotEqual(a[0].VoiceSeed, b[0].VoiceSeed);
    }

    [Fact]
    public void VoiceSeed_IsStableForAGivenWorld()
    {
        var planet = TestPlanet();
        var a = CreatureGenerator.GenerateRoster(planet, worldSeed: 777);
        var b = CreatureGenerator.GenerateRoster(planet, worldSeed: 777);

        Assert.Equal(a.Select(sp => sp.VoiceSeed), b.Select(sp => sp.VoiceSeed));
    }

    private static PlanetType TestPlanet() => new PlanetType
    {
        Key = "voice_test",
        CreatureAbundance = "many",
        CaveThreshold = 0.4,
        WaterAbundance = 0.5,
    };
}
