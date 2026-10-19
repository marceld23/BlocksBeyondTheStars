// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the ambient motes around the camera do right now (#2808, #2818).</summary>
    public enum AmbientMoteMode
    {
        /// <summary>No motes: space, a station, an airless sky, a cave, a roofed room that is not home, or aboard
        /// with Reduced effects on.</summary>
        None,

        /// <summary>The planet's own motes (dust, pollen, embers, spores …) drifting in the open air.</summary>
        Outdoor,

        /// <summary>A few slow, warm dust motes in the lamplight at home: aboard the own ship or under a roof in the own
        /// base's air.</summary>
        Lamplight,
    }

    /// <summary>
    /// The look of one kind of ambient mote. The two colours are sRGB-authored (0..1); each mote picks a random mix of
    /// them, and the Unity side converts them for the linear pipeline.
    /// </summary>
    public readonly struct AmbientMoteStyle
    {
        public AmbientMoteStyle(string name, float r, float g, float b, float r2, float g2, float b2,
            float rate = 1f, float rise = 0f, float size = 1f, float life = 6f)
        {
            Name = name;
            R = r;
            G = g;
            B = b;
            R2 = r2;
            G2 = g2;
            B2 = b2;
            Rate = rate;
            Rise = rise;
            Size = size;
            Life = life;
        }

        /// <summary>A short name for logs and tests ("dust", "pollen", "spores" …).</summary>
        public string Name { get; }

        /// <summary>The first colour, sRGB.</summary>
        public float R { get; }

        public float G { get; }

        public float B { get; }

        /// <summary>The second colour, sRGB (the same as the first for a single-colour style).</summary>
        public float R2 { get; }

        public float G2 { get; }

        public float B2 { get; }

        /// <summary>Multiplier on the base spawn rate.</summary>
        public float Rate { get; }

        /// <summary>Steady vertical drift in metres per second: spores and embers rise, ash and frost settle.</summary>
        public float Rise { get; }

        /// <summary>Multiplier on the base mote size.</summary>
        public float Size { get; }

        /// <summary>Mean lifetime in seconds.</summary>
        public float Life { get; }
    }

    /// <summary>
    /// The rules of the ambient motes (#2808, #2818), Unity-free so the headless tests pin them: when they show
    /// (<see cref="ModeFor"/>) and how they look on each planet type (<see cref="StyleFor"/>). The Unity side
    /// (<c>AmbientParticles</c>) scales the rates by the effect density of the quality preset and Reduced effects.
    /// </summary>
    public static class AmbientMotes
    {
        /// <summary>Outdoor motes spawned per second at full effect density (High, Reduced effects off).</summary>
        public const float OutdoorRate = 15f;

        /// <summary>Lamplight motes spawned per second at full effect density, shared by all lamps in reach.</summary>
        public const float LamplightRate = 2.5f;

        /// <summary>Plain air dust — the planets without a look of their own (rocky, the alpine highland).</summary>
        public static readonly AmbientMoteStyle Dust = new AmbientMoteStyle("dust", 0.90f, 0.92f, 1.00f, 0.90f, 0.92f, 1.00f);

        /// <summary>The warm amber-cream dust in a home's lamplight: few, tiny, very slow and long-lived.</summary>
        public static readonly AmbientMoteStyle Lamplight =
            new AmbientMoteStyle("lamplight", 1.00f, 0.84f, 0.58f, 1.00f, 0.93f, 0.78f, rate: 1f, rise: 0.01f, size: 0.5f, life: 10f);

        private static readonly AmbientMoteStyle Frost = new AmbientMoteStyle("frost", 0.82f, 0.90f, 1.00f, 0.96f, 0.98f, 1.00f, rise: -0.03f);
        private static readonly AmbientMoteStyle SulfurFrost = new AmbientMoteStyle("sulfur_frost", 0.96f, 0.94f, 0.70f, 0.86f, 0.92f, 1.00f, rate: 0.9f, rise: -0.03f);
        private static readonly AmbientMoteStyle Embers = new AmbientMoteStyle("embers", 1.00f, 0.52f, 0.22f, 1.00f, 0.74f, 0.34f, rise: 0.15f, life: 5f);
        private static readonly AmbientMoteStyle Ash = new AmbientMoteStyle("ash", 0.80f, 0.76f, 0.72f, 1.00f, 0.60f, 0.32f, rate: 1.1f, rise: -0.04f, size: 1.3f, life: 7f);
        private static readonly AmbientMoteStyle Toxic = new AmbientMoteStyle("toxic", 0.78f, 0.95f, 0.38f, 0.60f, 0.85f, 0.30f, rise: 0.02f, size: 1.1f);
        private static readonly AmbientMoteStyle Pollen = new AmbientMoteStyle("pollen", 0.72f, 1.00f, 0.62f, 0.95f, 1.00f, 0.60f, rate: 1.1f, rise: 0.01f);
        private static readonly AmbientMoteStyle MeadowPollen = new AmbientMoteStyle("meadow_pollen", 1.00f, 0.96f, 0.66f, 0.90f, 1.00f, 0.70f, rise: 0.01f);
        private static readonly AmbientMoteStyle Petals = new AmbientMoteStyle("petals", 1.00f, 0.90f, 0.50f, 1.00f, 0.72f, 0.86f, rate: 1.3f, rise: 0.01f, size: 1.2f, life: 7f);
        private static readonly AmbientMoteStyle Seeds = new AmbientMoteStyle("seeds", 1.00f, 0.88f, 0.60f, 0.96f, 0.94f, 0.76f);
        private static readonly AmbientMoteStyle Sand = new AmbientMoteStyle("sand", 1.00f, 0.90f, 0.62f, 1.00f, 0.86f, 0.56f, rise: -0.01f);
        private static readonly AmbientMoteStyle RustDust = new AmbientMoteStyle("rust_dust", 1.00f, 0.70f, 0.48f, 0.95f, 0.80f, 0.62f, rise: -0.01f);
        private static readonly AmbientMoteStyle Salt = new AmbientMoteStyle("salt", 0.98f, 0.98f, 1.00f, 1.00f, 0.96f, 0.88f, rate: 0.9f, rise: -0.01f);
        private static readonly AmbientMoteStyle BlackSand = new AmbientMoteStyle("black_sand", 0.58f, 0.56f, 0.60f, 0.80f, 0.62f, 0.45f, rate: 0.8f, rise: -0.01f);
        private static readonly AmbientMoteStyle Crystal = new AmbientMoteStyle("crystal", 0.85f, 0.80f, 1.00f, 0.75f, 0.95f, 1.00f, rise: 0.02f);
        private static readonly AmbientMoteStyle Marsh = new AmbientMoteStyle("marsh", 0.80f, 0.90f, 0.48f, 0.66f, 0.80f, 0.42f, rate: 1.2f, rise: 0.02f, life: 7f);
        private static readonly AmbientMoteStyle Spores = new AmbientMoteStyle("spores", 0.78f, 0.62f, 1.00f, 0.92f, 0.76f, 1.00f, rate: 1.3f, rise: 0.10f, size: 1.4f, life: 9f);
        private static readonly AmbientMoteStyle Alien = new AmbientMoteStyle("alien", 0.62f, 1.00f, 0.84f, 0.88f, 0.62f, 1.00f, rise: 0.04f, size: 1.1f, life: 7f);

        /// <summary>
        /// Which motes show. Home wins (the lamplight motes replace the outdoor ones, and Reduced effects turns them
        /// off); otherwise the outdoor motes need breathable-looking air (not <paramref name="airless"/>: space, a
        /// station, an airless sky), not aboard, and open air overhead — never in a cave or under a roof.
        /// </summary>
        public static AmbientMoteMode ModeFor(bool airless, bool aboard, bool atHome, bool openAir, bool reducedEffects)
        {
            if (atHome)
            {
                return reducedEffects ? AmbientMoteMode.None : AmbientMoteMode.Lamplight;
            }

            return airless || aboard || !openAir ? AmbientMoteMode.None : AmbientMoteMode.Outdoor;
        }

        /// <summary>
        /// The outdoor look for a planet type (<c>data/planets.json</c> key, which the client sees as the
        /// environment's biome). A type with a look of its own wins; otherwise the planet's flora theme decides;
        /// otherwise plain <see cref="Dust"/>.
        /// </summary>
        public static AmbientMoteStyle StyleFor(string? planetType, string? floraTheme)
        {
            switch ((planetType ?? string.Empty).ToLowerInvariant())
            {
                case "lava":
                case "volcanic":
                    return Embers;
                case "ice":
                case "frozen":
                case "glacier":
                case "icecap":
                case "frozen_ocean":
                    return Frost;
                case "titas":
                    return SulfurFrost;
                case "toxic_world":
                case "toxica_maxima":
                    return Toxic;
                case "red_desert":
                case "badlands":
                case "scrapyard":
                    return RustDust;
                case "salt_flats":
                case "bone_desert":
                    return Salt;
                case "arena_nigra":
                    return BlackSand;
                case "jungle":
                case "forest":
                    return Pollen;
                case "meadowlands":
                    return MeadowPollen;
                case "fungal":
                    return Spores;
            }

            switch ((floraTheme ?? string.Empty).ToLowerInvariant())
            {
                case "tundra":
                    return Frost;
                case "ashen":
                    return Ash;
                case "tropical":
                    return Pollen;
                case "temperate":
                    return MeadowPollen;
                case "floral":
                    return Petals;
                case "savanna":
                    return Seeds;
                case "desert":
                    return Sand;
                case "crystal":
                    return Crystal;
                case "swamp":
                    return Marsh;
                case "fungal":
                    return Spores;
                case "alien":
                    return Alien;
                default:
                    return Dust;
            }
        }
    }
}
