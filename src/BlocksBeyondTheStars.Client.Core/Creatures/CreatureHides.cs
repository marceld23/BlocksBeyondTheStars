// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The hides a creature can wear (#2829, #2831, #2861, #2868): the one name → tile table the client loads its grayscale
    /// hide tiles from (<c>creature_&lt;name&gt;</c>), the pools a species picks from, and the per-part pick itself. Unity-free
    /// so the headless suite pins the decisions; <c>CreatureBuilder</c> resolves the names to textures.
    /// <para>The pick is keyed off the species' world-unique seed (<see cref="CreatureCoat.SpeciesSeed"/>), never the slot
    /// id (rolled species are <c>sp0..sp8</c> on every planet, so the old id hash gave slot 3 the same hide everywhere), and
    /// off the species' disposition (<see cref="SpeciesHostile"/>), never the wire's momentary <c>Hostile</c> flag (that one
    /// is also true while a passive animal is provoked).</para>
    /// <para>Glow is not a pool (#2861): a glowing species wears the hide its habitat and temper give it, and its glow is a
    /// pattern on top (<see cref="GlowMasks"/>). The old glow pool held 8 hides and took 30 % of all species, so those 8
    /// were the 8 most common torsos everywhere.</para>
    /// <para>A tile that is missing (not bundled yet, or a pack that dropped it) is skipped, so a pool never yields a hole
    /// while any hide exists.</para>
    /// </summary>
    public static class CreatureHides
    {
        /// <summary>The tile key prefix: hide <c>fur</c> is the tile <c>creature_fur</c>.</summary>
        public const string TilePrefix = "creature_";

        /// <summary>Every hide the client knows, in a stable order. The first 25 are the original set (#1760 petal, #2083
        /// skin, #2111/#2112 slick); the 24 after them are the hide-variety wave (#2831).</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            "scales", "fur", "chitin", "hide", "slime", "feathers", "spots", "stripes", "warty", "plated",
            "finned", "tentacled", "mossy", "crystalline", "metallic", "banded", "shaggy", "spined", "mottled",
            "iridescent", "barkskin", "veined", "petal", "skin", "slick",
            "finefeathers", "down", "wool", "leather", "quilted", "fungal", "barkmoss", "coarsescales", "finescales",
            "lizard", "bristles", "crystalveins", "cracked", "honeycomb", "ridged", "velvet", "snake", "roughbark",
            "pebbled", "knobbly", "coralskin", "wrinkled", "featherscales", "lichen",
        };

        /// <summary>Hides whose shapes are big (plates, bands, big scales): they repeat every
        /// <see cref="CoarseTileMetres"/> instead of <see cref="TiledCube.TileMetres"/>, so the shapes read at a distance
        /// (#2860).</summary>
        public static readonly IReadOnlyList<string> Coarse = new[]
        {
            "plated", "banded", "honeycomb", "quilted", "coarsescales", "cracked", "petal", "metallic", "chitin",
            "crystalline", "spots", "stripes", "knobbly",
        };

        /// <summary>World units per repeat of a coarse hide.</summary>
        public const float CoarseTileMetres = 1.2f;

        /// <summary>Water and amphibian species.</summary>
        public static readonly IReadOnlyList<string> AquaticPool = new[]
        {
            "finned", "tentacled", "slime", "scales", "iridescent", "banded", "slick", "finescales", "coralskin",
            "pebbled", "snake", "ridged", "warty", "coarsescales", "metallic",
        };

        /// <summary>Winged species: plumage, and the skins of bats and pterosaurs (#2861: was 6 hides).</summary>
        public static readonly IReadOnlyList<string> WingedPool = new[]
        {
            "feathers", "finefeathers", "down", "featherscales", "mottled", "iridescent", "leather", "velvet",
            "finescales", "quilted", "spots", "stripes",
        };

        /// <summary>Lava dwellers: baked, cracked and armoured skins.</summary>
        public static readonly IReadOnlyList<string> LavaPool = new[]
        {
            "cracked", "plated", "crystalline", "metallic", "chitin", "coarsescales", "ridged", "roughbark", "honeycomb",
            "crystalveins",
        };

        /// <summary>Cave dwellers: pale, wet, crystal and fungal skins (they used to fall into the glow pool).</summary>
        public static readonly IReadOnlyList<string> CavePool = new[]
        {
            "slime", "veined", "crystalline", "crystalveins", "fungal", "lichen", "pebbled", "wrinkled", "chitin", "skin",
            "velvet", "iridescent", "coralskin", "knobbly", "spined",
        };

        /// <summary>Species that hunt (Aggressive / PackHunter): armour, spines and rough skins.</summary>
        public static readonly IReadOnlyList<string> HostilePool = new[]
        {
            "chitin", "plated", "scales", "spined", "metallic", "crystalline", "barkskin", "coarsescales", "lizard",
            "bristles", "crystalveins", "cracked", "honeycomb", "ridged", "knobbly", "featherscales", "roughbark",
            "leather", "wrinkled", "snake",
        };

        /// <summary>Everything else: the peaceful land fauna, the widest pool (a little armour too — armoured grazers).</summary>
        public static readonly IReadOnlyList<string> PassivePool = new[]
        {
            "fur", "hide", "scales", "spots", "stripes", "warty", "plated", "mossy", "shaggy", "mottled", "banded",
            "barkskin", "veined", "wool", "leather", "quilted", "down", "barkmoss", "finescales", "lizard", "bristles",
            "velvet", "snake", "roughbark", "pebbled", "knobbly", "wrinkled", "lichen", "fungal", "honeycomb", "coralskin",
            "skin", "slime", "spined", "cracked",
        };

        /// <summary>Every pool, for the table checks.</summary>
        public static IEnumerable<IReadOnlyList<string>> Pools
        {
            get
            {
                yield return AquaticPool;
                yield return WingedPool;
                yield return LavaPool;
                yield return CavePool;
                yield return HostilePool;
                yield return PassivePool;
            }
        }

        /// <summary>The family a server-named hide stands for (#2861): the server's roster arrays (giants, worms, arachnids,
        /// bipeds) name a hide from the original 25; a rolled species wears a member of that family by its seed, so the
        /// newer hides reach those bodies too. An authored species keeps its designed hide exactly.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> NamedFamilies = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["hide"] = new[] { "hide", "leather", "wrinkled", "skin" },
            ["plated"] = new[] { "plated", "ridged", "honeycomb", "coarsescales", "quilted" },
            ["scales"] = new[] { "scales", "coarsescales", "finescales", "lizard", "snake" },
            ["shaggy"] = new[] { "shaggy", "wool", "fur", "bristles" },
            ["mossy"] = new[] { "mossy", "barkmoss", "lichen" },
            ["barkskin"] = new[] { "barkskin", "roughbark", "barkmoss" },
            ["mottled"] = new[] { "mottled", "pebbled", "lichen" },
            ["banded"] = new[] { "banded", "ridged", "quilted" },
            ["chitin"] = new[] { "chitin", "plated", "honeycomb", "ridged" },
            ["warty"] = new[] { "warty", "knobbly", "pebbled" },
            ["spined"] = new[] { "spined", "bristles", "ridged" },
            ["slick"] = new[] { "slick", "finescales", "wrinkled" },
            ["skin"] = new[] { "skin", "leather", "velvet", "wrinkled" },
            ["fur"] = new[] { "fur", "velvet", "bristles", "wool" },
            ["spots"] = new[] { "spots", "mottled" },
            ["stripes"] = new[] { "stripes", "banded" },
            ["feathers"] = new[] { "feathers", "finefeathers", "featherscales" },
        };

        private static readonly HashSet<string> Known = new HashSet<string>(All, StringComparer.Ordinal);
        private static readonly HashSet<string> CoarseSet = new HashSet<string>(Coarse, StringComparer.Ordinal);

        /// <summary>The tile key of a hide (<c>creature_&lt;name&gt;</c>).</summary>
        public static string TileKey(string hide) => TilePrefix + hide;

        /// <summary>True for a hide name in <see cref="All"/>.</summary>
        public static bool IsKnown(string? hide) => hide != null && Known.Contains(hide);

        /// <summary>World units per repeat of <paramref name="hide"/> on a part (#2860): coarse hides repeat at twice the
        /// size of fine ones.</summary>
        public static float TileMetresOf(string? hide)
            => hide != null && CoarseSet.Contains(hide) ? CoarseTileMetres : TiledCube.TileMetres;

        /// <summary>The species' disposition from the wire's temperament (a species-level trait): Aggressive and PackHunter
        /// hunt, everything else is peaceful — the server's own <c>CreatureSpecies.Hostile</c> rule. Unlike the wire's
        /// <c>Hostile</c> flag it does not flip while a passive animal is provoked, nor for a companion.</summary>
        public static bool SpeciesHostile(string? temperament)
            => string.Equals(temperament, "Aggressive", StringComparison.OrdinalIgnoreCase)
               || string.Equals(temperament, "PackHunter", StringComparison.OrdinalIgnoreCase);

        /// <summary>The pool a species draws from: water → wings → lava → cave → hunting → peaceful. Glow plays no part
        /// (#2861).</summary>
        public static IReadOnlyList<string> PoolFor(NetCreature c)
        {
            if (c.Habitat == "Water" || c.Habitat == "Amphibian")
            {
                return AquaticPool;
            }

            if (c.HasWings)
            {
                return WingedPool;
            }

            if (c.Habitat == "Lava")
            {
                return LavaPool;
            }

            if (c.Habitat == "Cave")
            {
                return CavePool;
            }

            return SpeciesHostile(c.Temperament) ? HostilePool : PassivePool;
        }

        /// <summary>The hides of one species, per body part. All four are null only when no hide tile is available at all.
        /// </summary>
        public sealed class HideSet
        {
            public HideSet(string? torso, string? head, string? limbs, string? ornament)
            {
                Torso = torso;
                Head = head;
                Limbs = limbs;
                Ornament = ornament;
            }

            /// <summary>The body, its tail, neck, belly and the pattern overlays.</summary>
            public string? Torso { get; }

            /// <summary>The head and the jaw.</summary>
            public string? Head { get; }

            /// <summary>The legs and arms.</summary>
            public string? Limbs { get; }

            /// <summary>Wings, fins, ears, crests.</summary>
            public string? Ornament { get; }

            /// <summary>True when every part wears the same hide.</summary>
            public bool Uniform => Torso == Head && Torso == Limbs && Torso == Ornament;

            /// <summary>The hide of a part role.</summary>
            public string? For(CoatPart part) => part switch
            {
                CoatPart.Head or CoatPart.Jaw => Head,
                CoatPart.Limb or CoatPart.LowerLimb => Limbs,
                CoatPart.Wing or CoatPart.Ornament => Ornament,
                _ => Torso,
            };
        }

        /// <summary>Percent of rolled species that wear one hide everywhere; the rest mix hides per part (a furry torso on
        /// scaly legs).</summary>
        public const int UniformPercent = 50;

        /// <summary>Picks the hides of a species. An authored species wears its designed hide everywhere (or one hashed
        /// pick when it names none); a server-named hide of a rolled species names a family the torso picks from; the rest
        /// comes from the species pool by the species seed, leaning toward the world's theme and motif when
        /// <paramref name="look"/> is known (#2868). <paramref name="available"/> says whether a hide's tile loaded — a
        /// missing one is skipped.</summary>
        public static HideSet Pick(NetCreature c, Func<string, bool> available, FaunaThemes.Look? look = null)
        {
            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            bool authored = CreatureCoat.IsAuthored(c.SpeciesId);
            var pool = PoolFor(c);
            IReadOnlyList<string> source = pool;
            string? named = !string.IsNullOrEmpty(c.Hide) && IsKnown(c.Hide) ? c.Hide : null;
            string? torso;
            if (named != null && authored)
            {
                torso = available(named) ? named : null;
            }
            else if (named != null)
            {
                torso = PickFrom(NamedFamily(named), CoatMix.Mix(seed, CoatMix.SaltHideTorso), available);
            }
            else
            {
                source = authored ? pool : ThemedSource(seed, pool, look); // a designed species is never themed
                torso = PickFrom(source, CoatMix.Mix(seed, CoatMix.SaltHideTorso), available);
            }

            torso ??= PickFrom(pool, CoatMix.Mix(seed, CoatMix.SaltHideTorso), available) ?? Fallback(available);
            if (authored || CoatMix.Mix(seed, CoatMix.SaltHideUniform) % 100u < UniformPercent)
            {
                return new HideSet(torso, torso, torso, torso);
            }

            // The head mostly follows the torso (a face rarely differs from the flank); limbs and ornaments roll freely.
            string? head = CoatMix.Mix(seed, CoatMix.SaltHideHeadSame) % 100u < 65u
                ? torso
                : PickFrom(source, CoatMix.Mix(seed, CoatMix.SaltHideHead), available) ?? torso;
            string? limbs = PickFrom(source, CoatMix.Mix(seed, CoatMix.SaltHideLimbs), available) ?? torso;
            string? ornament = PickFrom(source, CoatMix.Mix(seed, CoatMix.SaltHideOrnament), available) ?? torso;
            return new HideSet(torso, head, limbs, ornament);
        }

        /// <summary>The family a server-named hide stands for — itself alone when it has none.</summary>
        public static IReadOnlyList<string> NamedFamily(string hide)
            => NamedFamilies.TryGetValue(hide, out var family) ? family : new[] { hide };

        /// <summary>The list a rolled species picks its hides from: the theme's hides in its pool for about half the
        /// species, the world motif's family for a fifth, its whole pool otherwise (and whenever the theme or the motif
        /// shares nothing with the pool).</summary>
        public static IReadOnlyList<string> ThemedSource(uint seed, IReadOnlyList<string> pool, FaunaThemes.Look? look)
        {
            if (look == null)
            {
                return pool;
            }

            uint roll = CoatMix.Mix(seed, CoatMix.SaltTheme) % 100u;
            if (roll < FaunaThemes.ThemePercent)
            {
                var themed = FaunaThemes.ThemeCandidates(look.Theme, pool);
                if (themed.Count > 0)
                {
                    return themed;
                }
            }
            else if (roll < FaunaThemes.ThemePercent + FaunaThemes.MotifPercent)
            {
                var motif = FaunaThemes.FamilyCandidates(look.MotifFamily, pool);
                if (motif.Count > 0)
                {
                    return motif;
                }
            }

            return pool;
        }

        /// <summary>The pool entry at the hashed index, probing onward past missing tiles; null when none is available.
        /// Probing (rather than filtering first) keeps every other species' pick when one tile goes missing.</summary>
        public static string? PickFrom(IReadOnlyList<string> pool, uint hash, Func<string, bool> available)
        {
            if (pool.Count == 0)
            {
                return null;
            }

            int start = (int)(hash % (uint)pool.Count);
            for (int i = 0; i < pool.Count; i++)
            {
                string name = pool[(start + i) % pool.Count];
                if (available(name))
                {
                    return name;
                }
            }

            return null;
        }

        /// <summary>The plain hide when a whole pool is missing, else any hide at all; null only when none loaded.</summary>
        private static string? Fallback(Func<string, bool> available)
        {
            if (available("hide"))
            {
                return "hide";
            }

            foreach (string name in All)
            {
                if (available(name))
                {
                    return name;
                }
            }

            return null;
        }
    }
}
