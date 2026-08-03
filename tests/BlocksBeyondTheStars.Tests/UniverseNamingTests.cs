// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Text.RegularExpressions;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>Naming rework (#678): several system-name registries, Roman/letter planet designations,
/// coined proper names for landmark worlds, and no baked-in English kind words anywhere.</summary>
public sealed class UniverseNamingTests
{
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    private static WorldDescription Desc(int systems, bool variance = true) => new()
    {
        StarSystemCount = systems,
        PlanetsPerSystemMin = 2,
        PlanetsPerSystemMax = 6,
        MoonsPerPlanetMin = 0,
        MoonsPerPlanetMax = 2,
        SystemVariance = variance,
        SpaceStations = Frequency.Frequent,
    };

    private static readonly Regex CatalogPattern = new(@"^[BCDFGHKLMNPRSTVXZ]{2}-\d{3,4}$", RegexOptions.None, System.TimeSpan.FromSeconds(1));

    /// <summary>A planet designation is "&lt;system&gt; &lt;Roman or letter&gt;" — proper names never carry the system prefix.</summary>
    private static bool IsDesignation(StarSystem sys, CelestialBody planet)
        => planet.Name.StartsWith(sys.Name + " ", System.StringComparison.Ordinal);

    [Fact]
    public void SystemNames_AreUnique_AndDrawFromSeveralRegistries()
    {
        var galaxy = new UniverseGenerator(42, Desc(200), _content).Generate();
        var names = galaxy.Systems.Select(s => s.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(System.StringComparer.OrdinalIgnoreCase).Count());

        int catalog = names.Count(n => CatalogPattern.IsMatch(n));
        int twoPart = names.Count(n => n.Contains(' ') && !CatalogPattern.IsMatch(n));
        int coined = names.Count(n => !n.Contains(' ') && !CatalogPattern.IsMatch(n));

        // ~25 % catalog / ~20 % two-part (region + archetype) / ~55 % coined — loose bands, exact
        // ratios are seed luck. The point: all three registries actually show up.
        Assert.InRange(catalog, 20, 80);
        Assert.InRange(twoPart, 10, 80);
        Assert.InRange(coined, 70, 160);
    }

    [Fact]
    public void Names_AreDeterministic_ForSameSeedAndDescription()
    {
        var a = new UniverseGenerator(7, Desc(40), _content).Generate();
        var b = new UniverseGenerator(7, Desc(40), _content).Generate();
        Assert.Equal(a.Systems.Select(s => s.Name), b.Systems.Select(s => s.Name));
        Assert.Equal(a.AllBodies().Select(x => x.Name), b.AllBodies().Select(x => x.Name));
    }

    [Fact]
    public void PlanetNames_AreDesignations_ExceptLandmarks()
    {
        const long seed = 42;
        var galaxy = new UniverseGenerator(seed, Desc(120), _content).Generate();
        for (int i = 0; i < galaxy.Systems.Count; i++)
        {
            var sys = galaxy.Systems[i];
            var archetype = SystemArchetypes.ForIndex(seed, i);
            var planets = sys.Bodies.Where(b => b.Kind == CelestialKind.Planet).ToList();
            bool catalogStyle = CatalogPattern.IsMatch(sys.Name);

            int extraProper = 0;
            for (int p = 0; p < planets.Count; p++)
            {
                bool landmark = planets[p].RingSeed != 0
                    || archetype == SystemArchetype.LoneGiant
                    || (archetype == SystemArchetype.TwinWorlds && p <= 1);
                if (landmark)
                {
                    Assert.False(IsDesignation(sys, planets[p]), $"landmark {planets[p].Id} kept designation {planets[p].Name}");
                }
                else if (!IsDesignation(sys, planets[p]))
                {
                    extraProper++; // the Hub capital is the only non-landmark allowed a proper name
                }
                else if (catalogStyle)
                {
                    Assert.Matches($"^{Regex.Escape(sys.Name)} [b-z]$", planets[p].Name); // exoplanet letters
                }
                else
                {
                    Assert.Matches($"^{Regex.Escape(sys.Name)} [IVXLC]+$", planets[p].Name); // Roman numerals
                }
            }

            Assert.True(extraProper <= (archetype == SystemArchetype.Hub ? 1 : 0),
                $"system {sys.Id} ({archetype}) has {extraProper} unexplained proper-named planets");
        }
    }

    [Fact]
    public void TwinWorlds_ShareANameStem()
    {
        const long seed = 42;
        var galaxy = new UniverseGenerator(seed, Desc(150), _content).Generate();
        bool sawTwins = false;
        for (int i = 0; i < galaxy.Systems.Count; i++)
        {
            if (SystemArchetypes.ForIndex(seed, i) != SystemArchetype.TwinWorlds)
            {
                continue;
            }

            var twins = galaxy.Systems[i].Bodies.Where(b => b.Kind == CelestialKind.Planet).Take(2).ToList();
            if (twins.Count < 2)
            {
                continue;
            }

            sawTwins = true;
            Assert.NotEqual(twins[0].Name, twins[1].Name);
            Assert.Equal(twins[0].Name[..2], twins[1].Name[..2]); // one coined stem, two endings
        }

        Assert.True(sawTwins, "expected at least one TwinWorlds system in 150 varied systems");
    }

    [Fact]
    public void Moons_FollowTheirParentsNamingStyle()
    {
        var galaxy = new UniverseGenerator(42, Desc(80), _content).Generate();
        int lettered = 0, coined = 0;
        foreach (var sys in galaxy.Systems)
        {
            foreach (var moon in sys.Bodies.Where(b => b.Kind == CelestialKind.Moon))
            {
                var parent = sys.Bodies.First(b => b.Id == moon.ParentId);
                if (IsDesignation(sys, parent))
                {
                    Assert.Matches($"^{Regex.Escape(parent.Name)}-[a-z]$", moon.Name);
                    lettered++;
                }
                else if (moon.Name.StartsWith(parent.Name + "-", System.StringComparison.Ordinal))
                {
                    // A Hub capital is renamed AFTER its moons were lettered — they follow the rename
                    // and stay lettered ("Meridian-a"), which is the designed post-hoc behavior.
                    Assert.Matches($"^{Regex.Escape(parent.Name)}-[a-z]$", moon.Name);
                    lettered++;
                }
                else
                {
                    coined++; // landmark-at-creation planets coin their moons ("Skell", "Vore")
                }
            }
        }

        Assert.True(lettered > 0, "expected lettered moons around designation planets");
        Assert.True(coined > 0, "expected coined moons around landmark planets");
    }

    [Fact]
    public void CoinedNames_NeverContainBlockedWords()
    {
        // The syllable mill produced "Rapeearr" during development — a kids' game cannot ship that.
        // Sweep several large galaxies and check every generated name against the blocklist classes.
        string[] blocked = { "rape", "nazi", "anal", "penis", "fuck", "shit", "cunt", "arsch", "fotze", "hure" };
        foreach (long seed in new long[] { 1, 7, 42, 99, 1234 })
        {
            var galaxy = new UniverseGenerator(seed, Desc(150), _content).Generate();
            foreach (var name in galaxy.Systems.Select(s => s.Name).Concat(galaxy.AllBodies().Select(b => b.Name)))
            {
                foreach (var bad in blocked)
                {
                    Assert.DoesNotContain(bad, name, System.StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    [Fact]
    public void NoBodyName_CarriesBakedEnglishKindWords()
    {
        var galaxy = new UniverseGenerator(42, Desc(150), _content).Generate();
        foreach (var body in galaxy.AllBodies())
        {
            Assert.DoesNotContain("Asteroid", body.Name);
            Assert.DoesNotContain("Wreck", body.Name);
        }

        // Asteroid fields and wrecks are single coined words — the map adds the localized kind label.
        foreach (var body in galaxy.AllBodies().Where(b => b.Kind is CelestialKind.AsteroidField or CelestialKind.Wreck))
        {
            Assert.DoesNotContain(' ', body.Name);
        }
    }

    [Fact]
    public void HubStations_ArePorts_AllOthersStayAttributive()
    {
        const long seed = 7;
        var galaxy = new UniverseGenerator(seed, Desc(150), _content).Generate();
        bool sawPort = false;
        for (int i = 0; i < galaxy.Systems.Count; i++)
        {
            var sys = galaxy.Systems[i];
            bool hub = SystemArchetypes.ForIndex(seed, i) == SystemArchetype.Hub;
            var stations = sys.Bodies.Where(b => b.Kind == CelestialKind.SpaceStation).ToList();
            var planetNames = sys.Bodies.Where(b => b.Kind == CelestialKind.Planet).Select(p => p.Name).ToHashSet();
            for (int s = 0; s < stations.Count; s++)
            {
                if (hub && s == 0)
                {
                    Assert.StartsWith("Port ", stations[s].Name);
                    sawPort = true;
                }
                else
                {
                    Assert.EndsWith(" Station", stations[s].Name);
                    Assert.Contains(stations[s].Name[..^" Station".Length], planetNames);
                }
            }
        }

        Assert.True(sawPort, "expected at least one Hub port station in 150 varied systems");
    }

    [Fact]
    public void StartPlanet_TradesItsDesignationForAProperName_Deterministically()
    {
        var desc = Desc(40);
        var galaxy = new UniverseGenerator(42, desc, _content).Generate();

        // Pick a designation-named planet that owns moons and an attributive station, if any.
        var (sys, planet) = galaxy.Systems
            .SelectMany(s => s.Bodies.Where(b => b.Kind == CelestialKind.Planet).Select(b => (s, b)))
            .First(x => IsDesignation(x.s, x.b) && x.s.Bodies.Any(m => m.Kind == CelestialKind.Moon && m.ParentId == x.b.Id));
        string oldName = planet.Name;

        UniverseGenerator.EnsureStartPlanetProperName(sys, planet);
        Assert.NotEqual(oldName, planet.Name);
        Assert.False(IsDesignation(sys, planet));
        foreach (var moon in sys.Bodies.Where(b => b.Kind == CelestialKind.Moon && b.ParentId == planet.Id))
        {
            Assert.Matches($"^{Regex.Escape(planet.Name)}-[a-z]$", moon.Name);
        }

        // Idempotent (a proper name is kept) and deterministic (a fresh galaxy re-derives the same name).
        string first = planet.Name;
        UniverseGenerator.EnsureStartPlanetProperName(sys, planet);
        Assert.Equal(first, planet.Name);

        var again = new UniverseGenerator(42, desc, _content).Generate();
        var sys2 = again.Systems.First(s => s.Id == sys.Id);
        var planet2 = sys2.Bodies.First(b => b.Id == planet.Id);
        UniverseGenerator.EnsureStartPlanetProperName(sys2, planet2);
        Assert.Equal(first, planet2.Name);
    }
}
