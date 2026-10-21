// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.WorldGeneration;

/// <summary>
/// Deterministically derives a body's roster of <see cref="FloraSpecies"/> from the roster seed (the world
/// seed salted with the body, <see cref="WorldGenerator.RosterSeedFor"/>) + planet type — the flora
/// counterpart to <see cref="CreatureGenerator"/>. Each entry in the fixed archetype catalogue
/// (<see cref="FloraCatalog"/>) becomes a named, edible-or-toxic species for this world, so two worlds that
/// both grow the "bush" archetype name and classify it differently. Combined with the per-species colours
/// (<see cref="BlocksBeyondTheStars.Shared.World.FloraTints"/>, client-side from the same seed + body) and the
/// coined names, each world's plant life reads as its own flora. Same seed + body + planet → the same
/// roster, so nothing needs storing.
/// </summary>
public static class FloraGenerator
{
    /// <summary>This world's roster. <paramref name="terrainGeneration"/> is the world's
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.TerrainGeneration"/>: from
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration"/> the biome
    /// themes take part in the activation roll (#1715); older worlds keep the planet-theme-only roll — and
    /// with it every species they ever grew. From <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.TreeVarietyGeneration"/>
    /// the glowing surface species follow the world's <see cref="GlowRules.LevelFor">glow level</see> (#2857).
    /// <paramref name="worldSeed"/> is the ROSTER seed (<see cref="WorldGenerator.RosterSeedFor"/>) — the one the glow level
    /// is rolled from as well.</summary>
    public static IReadOnlyList<FloraSpecies> GenerateRoster(PlanetType planet, long worldSeed, int terrainGeneration = 0)
    {
        var list = new List<FloraSpecies>();
        long planetSeed = worldSeed ^ WorldGenerator.StableHash(planet.Key) ^ 0x5EEDF10A;
        bool caveWave = terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.CaveFloraGeneration;
        if (planet.IsAirless || planet.FloraDensity <= 0)
        {
            // Airless (asteroids / airless moons+planets) + barren worlds grow nothing on the surface. Since generation
            // 11 their caves may still grow (Marcel: "everywhere in caves, with a certain probability"): a per-world
            // roll decides, and the roster then holds only the species that live in caves.
            if (!caveWave || !BarrenCavesGrow(planet, planetSeed))
            {
                return list;
            }

            return CaveOnlyRoster(planet, planetSeed, terrainGeneration);
        }

        // The world's flora theme biases WHICH forms grow: species whose climate tags match the theme are
        // common, off-theme species an occasional find — so a tropical world and a savanna world (both grass)
        // grow visibly different plant life. Coverage is enforced afterwards so no surface ever goes bare.
        //
        // #1715 (generation ≥ 4): the biome themes count too. A `varied` world is temperate with desert, swamp
        // and alpine biomes; under the planet-only roll its swamp drew from a pool thinned by the temperate
        // theme's 40 % — a wetland species that rolled inactive could never grow there, and PickWeight (which
        // does read the biome theme) cannot add back what was never activated. The union of preferred tags is
        // data-only, so the roster stays a pure function of (type, seed, generation).
        FloraTag preferred = PreferredTags(planet, terrainGeneration);

        // #1760 (generation ≥ 5): a strict planet theme (the flower fields) activates only its preferred tags.
        // No theme older than this wave is strict, so the roll below is unchanged for every existing world.
        bool strict = IsStrict(planet, terrainGeneration);

        // #2857 (generation 29): the world's glow level decides how much of its surface plant life glows. Older generations
        // never read it (GlowRules answers Normal for them, and every rule below sits behind glowWave).
        bool glowWave = terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.TreeVarietyGeneration;

        int i = 0;
        foreach (var archetype in FloraCatalog.All)
        {
            // Farmed crops (#627) are not part of this world's plant life: they never grow wild, and above all
            // they must never take on a rolled species identity — a roster entry would let the toxic roll swap
            // their edible berries for toxic ones. The index still advances so the WILD species keep their
            // seed salt (and their "fl<i>" ids) no matter where a crop sits in the catalog. An authored plant (#2085, the
            // Fifi plant's berries) stays out for the same reason: its identity is designed, never rolled.
            if (archetype.Cultivated || archetype.Authored)
            {
                i++;
                continue;
            }

            var rng = SpeciesRng(planetSeed, i);
            // A cave-only species (generation 11) rolls without the strict theme: the flower planet's rule is about
            // its fields, and its caves grow like every other world's.
            bool rollStrict = strict && archetype.OnSurface;
            // #2857 (generation 29): a glowing surface species is no signature plant of its OTHER tags any more (the glow
            // vine's Lush made it one on every green world) — only a world that prefers glow itself rolls it at 85 %. The
            // roll is still drawn below, so the stream of every later species stays as it is.
            double chance = glowWave && IsSurfaceGlower(archetype)
                ? GlowActivationChance(preferred, rollStrict)
                : FloraThemes.ActivationChance(preferred, archetype.Tags, rollStrict);
            list.Add(new FloraSpecies
            {
                Id = "fl" + i,
                Name = NameGenerator.Flora(rng),
                BlockKey = archetype.Key,
                Toxic = rng.NextDouble() < 0.3,  // most flora is benign; a notable minority is toxic
                Aquatic = archetype.Aquatic,
                // A species of a later wave (#1756, MinGeneration) draws its roll like every other — the draw keeps
                // the rng stream identical — but stays inactive on a world whose generation predates it.
                Active = rng.NextDouble() < chance && archetype.MinGeneration <= terrainGeneration,
            });
            i++;
        }

        // #2857 (generation 29): the glow level overrides the rolls of the glowing surface species — before the coverage rule,
        // which on a dark world must then never bring one back.
        var glow = GlowRules.LevelFor(planet, worldSeed, terrainGeneration);
        if (glowWave)
        {
            ApplyGlowLevel(list, planet, worldSeed, terrainGeneration, glow, strict ? preferred : FloraTag.None);
        }

        EnsureCoverage(list, planet, terrainGeneration, strict ? preferred : FloraTag.None,
            shunGlow: glowWave && (glow is GlowLevel.None or GlowLevel.Low));

        // #2038: a fruit is toxic exactly when the world's tree species is — a tree and what hangs from it are one
        // identity, so the tree's flag (a scan label until now) finally means something. The fruit species' own roll
        // above is still drawn (the rng stream of every later species stays as it is) and then overridden. The tree
        // species is derived from the same seed the server names it with (RosterSeedFor), so the scan of a trunk and
        // of its fruit always agree.
        bool treeToxic = TreeGenerator.Generate(planet, worldSeed)?.Toxic ?? false;
        foreach (var fs in list)
        {
            if (FloraCatalog.IsFruit(fs.BlockKey))
            {
                fs.Toxic = treeToxic;
            }
        }

        // #2084 (generation 16): the type's guaranteed fruit is always active — its palms and jungle trees bear it (FruitRules).
        // Its activation roll above is still drawn, so no other species' stream moves.
        if (FruitRules.GuaranteedFruitFor(planet, terrainGeneration) is { } guaranteed)
        {
            foreach (var fs in list)
            {
                if (fs.BlockKey == guaranteed)
                {
                    fs.Active = true;
                }
            }
        }
        if (caveWave)
        {
            EnsureCaveCoverage(list, terrainGeneration);
        }

        return list;
    }

    /// <summary>The share of barren / airless worlds (generation 11) whose caves grow plants at all.</summary>
    public const double BarrenCaveFloraChance = 0.5;

    /// <summary>Whether a barren or airless world's caves grow plants (generation 11): a world without caves never
    /// does, every other one rolls its type's <see cref="PlanetType.CaveFloraChance"/> (#2029: the toxic worlds' rare
    /// 0.18) — or <see cref="BarrenCaveFloraChance"/> for every type that names none — from its own seed.</summary>
    private static bool BarrenCavesGrow(PlanetType planet, long planetSeed)
    {
        if (planet.Void || planet.CaveThreshold <= 0.0)
        {
            return false;
        }

        long s = unchecked(planetSeed ^ 0x0CA7EF10L);
        return new System.Random(unchecked((int)(s ^ (s >> 32)))).NextDouble() < (planet.CaveFloraChance ?? BarrenCaveFloraChance);
    }

    /// <summary>A barren world's roster (generation 11): only the species that live in caves, each drawn from the SAME
    /// per-species stream as on a green world (so an id always names the same kind of plant), rolled against the
    /// planet's theme without the strict rule — then the cave coverage rule.</summary>
    private static List<FloraSpecies> CaveOnlyRoster(PlanetType planet, long planetSeed, int terrainGeneration)
    {
        var list = new List<FloraSpecies>();
        FloraTag preferred = FloraThemes.Resolve(planet.FloraTheme).Preferred;
        int i = 0;
        foreach (var archetype in FloraCatalog.All)
        {
            if (archetype.Cultivated || archetype.Authored || !archetype.InCaves)
            {
                i++;
                continue;
            }

            var rng = SpeciesRng(planetSeed, i);
            list.Add(new FloraSpecies
            {
                Id = "fl" + i,
                Name = NameGenerator.Flora(rng),
                BlockKey = archetype.Key,
                Toxic = rng.NextDouble() < 0.3,
                Aquatic = archetype.Aquatic,
                Active = rng.NextDouble() < FloraThemes.ActivationChance(preferred, archetype.Tags, strict: false)
                    && archetype.MinGeneration <= terrainGeneration,
            });
            i++;
        }

        EnsureCaveCoverage(list, terrainGeneration);
        return list;
    }

    /// <summary>The per-species random stream: the planet seed salted with the catalog index (the index is the
    /// species' roster id, which is why new species are only ever appended).</summary>
    private static System.Random SpeciesRng(long planetSeed, int index)
    {
        const long golden = unchecked((long)0x9E3779B97F4A7C15UL);
        long s = unchecked(planetSeed ^ ((long)index * golden));
        return new System.Random(unchecked((int)(s ^ (s >> 32))));
    }

    /// <summary>The tags a world's roll prefers: its planet theme's, and from
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration"/> every biome theme's too (#1715).</summary>
    private static FloraTag PreferredTags(PlanetType planet, int terrainGeneration)
    {
        FloraTag preferred = FloraThemes.Resolve(planet.FloraTheme).Preferred;
        if (terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.BiomeThemeRosterGeneration)
        {
            foreach (var biome in planet.Biomes)
            {
                if (!string.IsNullOrWhiteSpace(biome.FloraTheme))
                {
                    preferred |= FloraThemes.Resolve(biome.FloraTheme).Preferred;
                }
            }
        }

        return preferred;
    }

    /// <summary>Whether the planet theme is strict on this generation (#1760, from
    /// <see cref="BlocksBeyondTheStars.Shared.World.WorldDescription.AuthoredContentGeneration"/>).</summary>
    private static bool IsStrict(PlanetType planet, int terrainGeneration)
        => terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.AuthoredContentGeneration
           && FloraThemes.Resolve(planet.FloraTheme).Strict;

    // #2857: the salt of a low-glow world's one glowing species — its own stateless roll, no species stream moves.
    private const long LowGlowPickSalt = 0x2857_1060_0E1A;

    /// <summary>True for a wild species that glows on the SURFACE (#2857) — tagged <see cref="FloraTag.Glow"/> and able to keep
    /// a surface planted: the glow cap, ember bloom, crystal flower, glow vine, spore pod, bulb, shard bloom and cinder bush.
    /// These follow the world's <see cref="GlowLevel"/> from generation 29. The cave-only glowers (moss, threads) are the light
    /// of the caves on every world, and the prism bloom stays active for the caves too — only its surface clusters follow the
    /// level (<c>WorldGenerator.StampFloraGen11</c>).</summary>
    public static bool IsSurfaceGlower(FloraCatalog.Species species)
        => (species.Tags & FloraTag.Glow) != 0 && SurfaceCover(species) && !species.Hanging && !species.Aquatic && !species.Fruit
           && !species.Cultivated && !species.Authored;

    /// <summary>The generation-29 activation chance of a glowing surface species on a <see cref="GlowLevel.Normal"/> world
    /// (#2857): 85 % only when the world prefers <see cref="FloraTag.Glow"/> itself, else 40 % — or none under a strict theme
    /// that does not prefer glow. Its other tags no longer count: the glow vine is no signature plant of a green world.</summary>
    private static double GlowActivationChance(FloraTag preferred, bool strict)
        => FloraThemes.ActivationChance(preferred & FloraTag.Glow, FloraTag.Glow, strict);

    /// <summary>The glowing surface species that can grow on this planet's surface (#2857), in catalog order: a host among the
    /// planet's surface, beach and biome surface blocks, a generation this world has, and — under a strict theme — on-theme.
    /// A <see cref="GlowLevel.Low"/> world picks its one glowing species among them, a <see cref="GlowLevel.Luminous"/> world
    /// grows them all.</summary>
    public static IReadOnlyList<string> GlowCandidatesFor(PlanetType planet, int terrainGeneration)
        => GlowCandidates(planet, terrainGeneration,
            IsStrict(planet, terrainGeneration) ? PreferredTags(planet, terrainGeneration) : FloraTag.None);

    private static List<string> GlowCandidates(PlanetType planet, int terrainGeneration, FloraTag strictTags)
    {
        var own = new HashSet<string>();
        AddOwn(planet.SurfaceBlock);
        AddOwn(planet.BeachBlock);
        foreach (var biome in planet.Biomes)
        {
            AddOwn(biome.SurfaceBlock);
        }

        var keys = new List<string>();
        foreach (var sp in FloraCatalog.All)
        {
            if (IsSurfaceGlower(sp) && sp.MinGeneration <= terrainGeneration
                && (strictTags == FloraTag.None || (sp.Tags & strictTags) != 0)
                && (sp.Hosts.Any(own.Contains) || sp.LateHosts.Any(own.Contains)))
            {
                keys.Add(sp.Key);
            }
        }

        return keys;

        void AddOwn(string? key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                own.Add(key!);
            }
        }
    }

    /// <summary>The one glowing species of a <see cref="GlowLevel.Low"/> world (#2857), or null when no glowing species can
    /// grow on its surface: a stateless pick among <see cref="GlowCandidatesFor"/> from the roster seed and the planet.</summary>
    private static string? LowGlowPick(PlanetType planet, long rosterSeed, IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        long s = unchecked(rosterSeed ^ WorldGenerator.StableHash(planet.Key) ^ LowGlowPickSalt);
        return candidates[new System.Random(unchecked((int)(s ^ (s >> 32)))).Next(candidates.Count)];
    }

    /// <summary>Generation 29 (#2857): the world's glow level overrides the rolls of the glowing surface species. None: none
    /// grows. Low: exactly the one <see cref="LowGlowPick"/>. Normal: the rolls stand (<see cref="GlowActivationChance"/>).
    /// Luminous: every one that can grow on this planet's surface, the others keep their roll. The rolls themselves were
    /// drawn as always, so no species stream moves.</summary>
    private static void ApplyGlowLevel(List<FloraSpecies> roster, PlanetType planet, long rosterSeed, int terrainGeneration,
        GlowLevel level, FloraTag strictTags)
    {
        if (level == GlowLevel.Normal)
        {
            return;
        }

        var candidates = GlowCandidates(planet, terrainGeneration, strictTags);
        string? lowPick = level == GlowLevel.Low ? LowGlowPick(planet, rosterSeed, candidates) : null;
        foreach (var r in roster)
        {
            if (FloraCatalog.Find(r.BlockKey) is not { } sp || !IsSurfaceGlower(sp))
            {
                continue;
            }

            r.Active = level switch
            {
                GlowLevel.None => false,
                GlowLevel.Low => r.BlockKey == lowPick,
                _ => r.Active || candidates.Contains(r.BlockKey),
            };
        }
    }

    /// <summary>Generation 11: a world that grows plants keeps its caves planted — at least one species of the cave
    /// habitat itself (the first eligible one in catalog order when none rolled active) — and the rainbow class grows
    /// on every such world (Marcel: "on every world with plant life").</summary>
    private static void EnsureCaveCoverage(List<FloraSpecies> roster, int terrainGeneration)
    {
        static bool CaveOnly(FloraSpecies r) => FloraCatalog.Find(r.BlockKey)?.Habitat == FloraHabitat.Cave;
        bool Eligible(FloraSpecies r) => FloraCatalog.Find(r.BlockKey) is { } sp && sp.MinGeneration <= terrainGeneration;

        if (!roster.Any(r => r.Active && CaveOnly(r)) && roster.FirstOrDefault(r => CaveOnly(r) && Eligible(r)) is { } pick)
        {
            pick.Active = true;
        }

        foreach (var r in roster)
        {
            if (Eligible(r) && FloraCatalog.IsRainbow(r.BlockKey))
            {
                r.Active = true;
            }
        }
    }

    /// <summary>Force-activates the minimum flora so no part of the world goes bare: every land host surface
    /// keeps at least one active land species, and the seas keep at least one active aquatic species.
    /// <paramref name="shunGlow"/> (#2857, a generation-29 world of glow level None or Low) keeps every glowing surface
    /// species out of the picks.</summary>
    private static void EnsureCoverage(List<FloraSpecies> roster, PlanetType planet, int terrainGeneration, FloraTag strictTags,
        bool shunGlow = false)
    {
        // Every host surface used by a land archetype must have an active species, or that surface grows nothing.
        // A hanging species (#1759) roots in a ceiling, never on a surface, so it neither needs cover nor gives it;
        // a species gated behind a later generation (#1756) must never be the pick that covers an older world; and
        // under a strict theme (#1760, strictTags ≠ None) only an on-theme species may be the pick — a bare patch
        // beats a bush on the flower planet. On a dark world (#2857, shunGlow) the same holds for a glowing species: the
        // first non-glowing one covers the host, and a host only glowing plants could cover stays bare.
        bool strict = strictTags != FloraTag.None;
        bool Eligible(string blockKey)
            => FloraCatalog.All.FirstOrDefault(s => s.Key == blockKey) is { } sp && !sp.Hanging && sp.MinGeneration <= terrainGeneration
               && SurfaceCover(sp) && (!strict || (sp.Tags & strictTags) != 0) && !(shunGlow && IsSurfaceGlower(sp));

        var landHosts = new HashSet<string>();
        foreach (var sp in FloraCatalog.All)
        {
            // A crop's host (a greenhouse bed / hydroponic tray) is no world surface — nothing to cover; a cave-only
            // species and the rainbow class (generation 11) are never what keeps a surface planted, so their hosts
            // stay out of this set (which also keeps its insertion order, and so every older roster, as it was).
            if (sp.Cultivated || sp.Hanging || !SurfaceCover(sp))
            {
                continue;
            }

            if (!sp.Aquatic)
            {
                foreach (var h in sp.Hosts)
                {
                    landHosts.Add(h);
                }
            }
        }

        // A strict theme (#1760) covers only the surfaces this planet actually has: forcing a reed onto "mud" for
        // a world without mud would put an off-theme plant on the roster for nothing.
        if (strict)
        {
            var own = new HashSet<string> { planet.SurfaceBlock, planet.BeachBlock };
            foreach (var biome in planet.Biomes)
            {
                own.Add(biome.SurfaceBlock);
            }

            landHosts.IntersectWith(own);
        }

        // #2857 (generation 29): a hanging species gives no cover either — the island-underside kelp lists stone and dirt as
        // its roots, and on an older world it counted as the plant of a stone biome. The glowers that used to keep such a host
        // planted as well are gone from a dark world, so from generation 29 only a plant that stands on the host covers it.
        bool groundCoverOnly = terrainGeneration >= BlocksBeyondTheStars.Shared.World.WorldDescription.TreeVarietyGeneration;
        foreach (var host in landHosts)
        {
            bool covered = roster.Any(r => r.Active && !r.Aquatic && SurfaceCover(r.BlockKey) && HostsFor(r.BlockKey).Contains(host)
                && !(groundCoverOnly && FloraCatalog.IsHanging(r.BlockKey)));
            if (!covered)
            {
                var pick = roster.FirstOrDefault(r => !r.Aquatic && Eligible(r.BlockKey) && HostsFor(r.BlockKey).Contains(host));
                if (pick != null)
                {
                    pick.Active = true;
                }
            }
        }

        // Keep the seas planted: at least one aquatic species active if any exist.
        if (roster.Any(r => r.Aquatic) && !roster.Any(r => r.Aquatic && r.Active))
        {
            roster.First(r => r.Aquatic).Active = true;
        }
    }

    /// <summary>Whether a species can be what keeps a SURFACE host planted: a cave-only species (generation 11) grows
    /// underground and the rainbow class only in rare clusters, so neither may stand in for a surface's plant life.
    /// Both are new in generation 11, so no older roster ever met them here.</summary>
    private static bool SurfaceCover(FloraCatalog.Species sp) => sp.OnSurface && !sp.Rainbow;

    private static bool SurfaceCover(string blockKey) => FloraCatalog.Find(blockKey) is { } sp && SurfaceCover(sp);

    private static IReadOnlyList<string> HostsFor(string blockKey)
        => FloraCatalog.All.FirstOrDefault(s => s.Key == blockKey)?.Hosts ?? System.Array.Empty<string>();
}
