// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A coat pattern (#2830): a second colour laid over the hide. The first eight are SURFACE patterns — tileable masks
    /// drawn over the whole body (<see cref="CoatMasks"/>); the rest are PLACED on one part role: a band (eye mask, tail
    /// rings), a whole part in the pattern colour (socks, crest band, belly blaze), or an overlay slab the builder adds
    /// on the back of a box torso (dorsal stripe, saddle, two-tone).
    /// </summary>
    public enum CoatPattern
    {
        None,
        CrossStripes, // bands across the spine — zebra
        TigerStripes, // irregular tapering bands
        Spots,
        Rosettes,     // broken rings — leopard
        Patches,      // large blobs — cow, giraffe
        Speckles,     // fine dots
        Marbling,     // thin wandering veins
        ScaleBands,   // rows of scallops, every other band in the pattern colour
        DorsalStripe, // a stripe along the spine (overlay)
        Saddle,       // a patch over the middle of the back (overlay)
        TwoTone,      // a dark cape over the upper body, the belly stays light (overlay)
        EyeMask,      // a band through the eyes (head only)
        TailRings,    // rings around the tail (tail only)
        CrestBand,    // crest, fins and other accents in the pattern colour
        BellyBlaze,   // a light blaze on the belly
        Socks,        // lower legs in the pattern colour
        Countershade, // a dark back fading toward the belly on the flanks of torso, neck and tail (#2863, pinned)

        // Tile patterns (#2863): masks loaded from the bundled tile creature_pattern_<name> (CoatMasks.TileName).
        Giraffe,      // a net of big patches
        Dalmatian,    // scattered round spots
        Pinto,        // a few big patches
        Cheetah,      // many small solid spots
        Jaguar,       // rosettes with a dot inside
        Tabby,        // wavy cat stripes
        Brindle,      // many thin streaks
        Okapi,        // bold bars
        Ladybird,     // a few large dots
        PoisonFrog,   // rounded blotches
        Salamander,   // big blotches and dashes
        EyeSpots,     // rings with a filled centre (moth, peacock)
        Tortoise,     // hexagonal plates outlined
        Diamondback,  // a chain of diamonds along the back
        Chevrons,     // stacked V shapes
        Lengthwise,   // stripes running along the body
    }

    /// <summary>The body part a coat material is for — the builder passes it, the pattern decides whether it shows there.
    /// </summary>
    public enum CoatPart
    {
        Torso,     // the body segments (long axis Z)
        Neck,      // a neck or trunk chain (long axis Y) — the torso's hide
        Head,      // the skull box (where the eye mask goes)
        Jaw,       // the jaw, eyelids, eye stalks, antenna stalks and mesh heads — the head's hide, never patterned
        Limb,      // thighs and arms (long axis Y)
        LowerLimb, // shins and soles (the socks)
        Tail,      // tail links (long axis Z)
        Belly,     // the belly slab — the belly colour
        Wing,      // wings, ears, wing panels — the body colour
        Ornament,  // fins, crests, lobes, tentacles, bobbles — the belly (accent) colour
        Overlay,   // the slabs a placed back pattern adds — the pattern colour
    }

    /// <summary>How a pattern covers one part.</summary>
    public enum CoatCoverage
    {
        None,   // the part keeps its plain colour
        Full,   // the whole part takes the pattern colour (no texture to bake)
        Masked, // a mask mixes body and pattern colour — a baked texture
    }

    /// <summary>The seed mixing the coat decisions share (#2829): a murmur3 finaliser over the species seed, salted per
    /// decision, so consecutive seeds give unrelated picks and no two decisions read the same bits.</summary>
    public static class CoatMix
    {
        public const uint SaltBody = 1;
        public const uint SaltHideTorso = 2;
        public const uint SaltHideUniform = 3;
        public const uint SaltHideHeadSame = 4;
        public const uint SaltHideHead = 5;
        public const uint SaltHideLimbs = 6;
        public const uint SaltHideOrnament = 7;
        public const uint SaltPatternNone = 8;
        public const uint SaltPatternKind = 9;
        public const uint SaltPatternColour = 10;
        public const uint SaltMask = 11;
        public const uint SaltJitter = 12;
        public const uint SaltTheme = 13;
        public const uint SaltMotif = 14;
        public const uint SaltPatternMotif = 15;
        public const uint SaltBold = 16;
        public const uint SaltGlow = 17;
        public const uint SaltGlowColour = 18;
        public const uint SaltPlacement = 19;
        public const uint SaltEye = 20;
        public const uint SaltIris = 21;
        public const uint SaltHorn = 22;
        public const uint SaltHornColour = 23;
        public const uint SaltPatternChoice = 24;

        /// <summary>The seed mixed with a salt: <c>fmix32(seed ^ salt·φ)</c>.</summary>
        public static uint Mix(uint seed, uint salt)
        {
            uint x = unchecked(seed ^ (salt * 0x9E3779B9u));
            x ^= x >> 16;
            x = unchecked(x * 0x85EBCA6Bu);
            x ^= x >> 13;
            x = unchecked(x * 0xC2B2AE35u);
            x ^= x >> 16;
            return x;
        }
    }

    /// <summary>
    /// A species' coat (#2829, #2830): which pattern it wears, in which colour, and how each body part shows it; plus the
    /// species seed every per-species look decision is keyed off, and the small per-animal colour jitter (#2831). Pure
    /// decisions — <see cref="CoatMasks"/> draws the masks, <see cref="CoatBake"/> mixes the texels, <c>CreatureBuilder</c>
    /// turns them into materials.
    /// </summary>
    public static class CreatureCoat
    {
        /// <summary>The roster id prefix of an authored species (<c>data/creatures.json</c>).</summary>
        public const string AuthoredPrefix = "au_";

        /// <summary>Roughly one species in this many wears no pattern at all.</summary>
        public const int NoneOneIn = 6;

        /// <summary>The surface patterns drawn in code — tileable masks that fit every body plan.</summary>
        public static readonly IReadOnlyList<CoatPattern> SurfacePatterns = new[]
        {
            CoatPattern.CrossStripes, CoatPattern.TigerStripes, CoatPattern.Spots, CoatPattern.Rosettes,
            CoatPattern.Patches, CoatPattern.Speckles, CoatPattern.Marbling, CoatPattern.ScaleBands,
        };

        /// <summary>The surface patterns whose mask is a bundled tile (#2863); one is offered only while its tile loaded.
        /// </summary>
        public static readonly IReadOnlyList<CoatPattern> TilePatterns = new[]
        {
            CoatPattern.Giraffe, CoatPattern.Dalmatian, CoatPattern.Pinto, CoatPattern.Cheetah, CoatPattern.Jaguar,
            CoatPattern.Tabby, CoatPattern.Brindle, CoatPattern.Okapi, CoatPattern.Ladybird, CoatPattern.PoisonFrog,
            CoatPattern.Salamander, CoatPattern.EyeSpots, CoatPattern.Tortoise, CoatPattern.Diamondback,
            CoatPattern.Chevrons, CoatPattern.Lengthwise,
        };

        /// <summary>True for a pattern whose mask is a bundled tile.</summary>
        public static bool IsTilePattern(CoatPattern pattern) => pattern >= CoatPattern.Giraffe;

        /// <summary>True for a species from <c>data/creatures.json</c> — its look is designed, not rolled.</summary>
        public static bool IsAuthored(string? speciesId)
            => speciesId != null && speciesId.StartsWith(AuthoredPrefix, StringComparison.Ordinal);

        /// <summary>The classic client-side id hash (31-polynomial, non-negative).</summary>
        public static int StableIdHash(string? s)
        {
            int h = 0;
            foreach (char ch in s ?? string.Empty)
            {
                h = unchecked(h * 31 + ch);
            }

            return h & 0x7fffffff;
        }

        /// <summary>The seed every per-species look decision starts from (#2829): the species' world-unique
        /// <c>VoiceSeed</c> (#907) for a rolled species — its slot id <c>sp0..sp8</c> repeats on every planet — and the id
        /// hash for an authored species (its id is unique and its look should not change per world) or when a legacy
        /// server sends no seed.</summary>
        public static uint SpeciesSeed(string? speciesId, int voiceSeed)
            => IsAuthored(speciesId) || voiceSeed == 0 ? (uint)StableIdHash(speciesId) : unchecked((uint)voiceSeed);

        /// <summary>The hash the builder draws a body's proportions (head size, girth, leg length) and the animator its gait
        /// phase from. An authored species (and a legacy server's) keeps the raw id hash, so its designed proportions are
        /// exactly what they were; a rolled species mixes its world-unique seed.</summary>
        public static int ProportionHash(string? speciesId, int voiceSeed)
            => IsAuthored(speciesId) || voiceSeed == 0
                ? StableIdHash(speciesId)
                : (int)(CoatMix.Mix(unchecked((uint)voiceSeed), CoatMix.SaltBody) & 0x7fffffff);

        /// <summary>The patterns a body can carry: every surface pattern (the tile patterns only when
        /// <paramref name="tileAvailable"/> is given — <see cref="PatternFor"/> probes past one whose tile did not load, so a
        /// missing tile never reshuffles the other species), plus the placed ones whose part it has — the back
        /// overlays and the countershading on a box torso, the eye mask on a box head with eyes, rings on a real tail, socks
        /// on legs, the crest band on a crest or fins, the blaze on a belly slab. The sandworm's armour hides its core, so
        /// it only takes the plate and belly colours.</summary>
        public static IReadOnlyList<CoatPattern> PatternsFor(NetCreature c, Func<string, bool>? tileAvailable = null)
        {
            string plan = string.IsNullOrEmpty(c.BodyPlan) ? "Standard" : c.BodyPlan;
            var list = new List<CoatPattern>();
            if (plan == "Sandworm")
            {
                list.Add(CoatPattern.CrestBand);  // the armour plates and petals
                list.Add(CoatPattern.BellyBlaze);
                return list;
            }

            list.AddRange(SurfacePatterns);
            if (tileAvailable != null)
            {
                list.AddRange(TilePatterns);
            }

            bool boxTorso = plan is "Standard" or "Titan" or "Colossus";
            bool boxHead = plan is "Standard" or "Titan" or "Colossus" or "Biped" or "Worm";
            bool legs = c.Legs > 0 && plan is "Standard" or "Titan" or "Colossus" or "Biped" or "Arachnid";
            bool tail = plan == "Worm" || (c.HasTail && plan is "Standard" or "Titan" or "Colossus" or "Biped");
            bool accents = c.HasCrest || c.HasFins || plan is "Leviathan" or "SkyGiant";
            if (boxTorso)
            {
                list.Add(CoatPattern.DorsalStripe);
                list.Add(CoatPattern.Saddle);
                list.Add(CoatPattern.TwoTone);
            }

            // The countershading maps v once over a flank (a pinned mesh), so the hide stretches by the flank's repeats: kept
            // to the bodies whose flanks need at most two (a standard or biped body up to size 2).
            if (plan is "Standard" or "Biped" && c.Size <= 2f)
            {
                list.Add(CoatPattern.Countershade);
            }

            if (boxHead && c.Eyes > 0)
            {
                list.Add(CoatPattern.EyeMask);
            }

            if (tail)
            {
                list.Add(CoatPattern.TailRings);
            }

            if (accents)
            {
                list.Add(CoatPattern.CrestBand);
            }

            if (plan != "Medusa")
            {
                list.Add(CoatPattern.BellyBlaze);
            }

            if (legs)
            {
                list.Add(CoatPattern.Socks);
            }

            return list;
        }

        /// <summary>The species' pattern: none for an authored species (its look is designed) and for about one in
        /// <see cref="NoneOneIn"/> rolled ones, otherwise one of <see cref="PatternsFor"/> by the species seed — for about a
        /// third of them one of the world motif's favourite group (#2868) when the body can carry one.</summary>
        public static CoatPattern PatternFor(NetCreature c, FaunaThemes.Look? look = null, Func<string, bool>? tileAvailable = null)
        {
            if (IsAuthored(c.SpeciesId))
            {
                return CoatPattern.None;
            }

            uint seed = SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            if (CoatMix.Mix(seed, CoatMix.SaltPatternNone) % (uint)NoneOneIn == 0u)
            {
                return CoatPattern.None;
            }

            var options = PatternsFor(c, tileAvailable);
            if (look != null && CoatMix.Mix(seed, CoatMix.SaltPatternChoice) % 100u < FaunaThemes.PatternMotifPercent)
            {
                var favoured = new List<CoatPattern>();
                foreach (var option in options)
                {
                    if (FaunaThemes.InGroup(option, look.PatternGroup))
                    {
                        favoured.Add(option);
                    }
                }

                if (favoured.Count > 0)
                {
                    options = favoured;
                }
            }

            // Probe onward past a tile pattern whose tile did not load (a pack that dropped it): only the species that picked
            // it change, never the others.
            int start = (int)(CoatMix.Mix(seed, CoatMix.SaltPatternKind) % (uint)options.Count);
            for (int i = 0; i < options.Count; i++)
            {
                var option = options[(start + i) % options.Count];
                if (!IsTilePattern(option) || tileAvailable == null || tileAvailable(CoatMasks.TileKey(option)))
                {
                    return option;
                }
            }

            return CoatPattern.None;
        }

        /// <summary>The seed a species' masks are drawn with (spot layout, stripe count, …).</summary>
        public static uint MaskSeed(NetCreature c) => CoatMix.Mix(SpeciesSeed(c.SpeciesId, c.VoiceSeed), CoatMix.SaltMask);

        /// <summary>How <paramref name="pattern"/> shows on <paramref name="part"/>.</summary>
        public static CoatCoverage Coverage(CoatPattern pattern, CoatPart part)
        {
            switch (pattern)
            {
                case CoatPattern.None:
                    return CoatCoverage.None;
                case CoatPattern.DorsalStripe:
                case CoatPattern.Saddle:
                case CoatPattern.TwoTone:
                    return part == CoatPart.Overlay ? CoatCoverage.Full : CoatCoverage.None;
                case CoatPattern.EyeMask:
                    return part == CoatPart.Head ? CoatCoverage.Masked : CoatCoverage.None;
                case CoatPattern.TailRings:
                    return part == CoatPart.Tail ? CoatCoverage.Masked : CoatCoverage.None;
                case CoatPattern.CrestBand:
                    return part == CoatPart.Ornament ? CoatCoverage.Full : CoatCoverage.None;
                case CoatPattern.BellyBlaze:
                    return part == CoatPart.Belly ? CoatCoverage.Full : CoatCoverage.None;
                case CoatPattern.Socks:
                    return part == CoatPart.LowerLimb ? CoatCoverage.Full : CoatCoverage.None;
                case CoatPattern.Countershade:
                    // Not the neck: a titan's neck and trunk are stacks of segments long in Y, each would band again.
                    return part is CoatPart.Torso or CoatPart.Tail ? CoatCoverage.Masked : CoatCoverage.None;
                default:
                    // A surface pattern covers the body, head, neck, limbs, tail and wings; the belly keeps its accent
                    // colour, the jaw and the accents stay plain (a calmer muzzle, clean fins).
                    return part is CoatPart.Torso or CoatPart.Neck or CoatPart.Head or CoatPart.Limb or CoatPart.LowerLimb
                        or CoatPart.Tail or CoatPart.Wing
                        ? CoatCoverage.Masked
                        : CoatCoverage.None;
            }
        }

        /// <summary>True when the builder adds overlay slabs on the back for this pattern.</summary>
        public static bool IsOverlay(CoatPattern pattern)
            => pattern is CoatPattern.DorsalStripe or CoatPattern.Saddle or CoatPattern.TwoTone;

        /// <summary>True for a part whose texture should map once over its height (the eye band sits at eye level, the
        /// countershading runs from the back down the flank): the builder gives that part a pinned-v mesh
        /// (<see cref="TiledCube"/>).</summary>
        public static bool PinsV(CoatPattern pattern, CoatPart part)
            => (pattern == CoatPattern.EyeMask && part == CoatPart.Head)
               || (pattern == CoatPattern.Countershade && part is CoatPart.Torso or CoatPart.Tail);

        private enum Shade
        {
            Darker,
            Lighter,
            Shifted,
        }

        /// <summary>The minimum luma step between body and pattern colour, so the pattern reads on every body.</summary>
        public const float MinLumaStep = 0.14f;

        /// <summary>The pattern colour (packed 0xRRGGBB) for a body colour: a darker shade (about half the species), a
        /// pastel lighter one, or a gentle hue shift toward a neighbouring colour — never the complementary hue (garish) and
        /// never a saturation past 0.9. The two-tone cape and the eye mask are darker, the belly blaze lighter (unless the
        /// body is too dark or too light to go that way). Then pushed until it differs from the body by
        /// <see cref="MinLumaStep"/> in luma.</summary>
        public static int PatternRgb(uint speciesSeed, int bodyRgb, CoatPattern pattern)
        {
            if (BoldRgb(speciesSeed, bodyRgb, pattern) is int bold)
            {
                return bold;
            }

            uint h = CoatMix.Mix(speciesSeed, CoatMix.SaltPatternColour);
            Shade shade = pattern switch
            {
                CoatPattern.TwoTone or CoatPattern.EyeMask or CoatPattern.Countershade => Shade.Darker,
                CoatPattern.BellyBlaze => Shade.Lighter,
                _ => (h % 20u) switch
                {
                    < 9u => Shade.Darker,
                    < 15u => Shade.Lighter,
                    _ => Shade.Shifted,
                },
            };

            Unpack(bodyRgb, out float r, out float g, out float b);
            RgbToHsv(r, g, b, out float hue, out float sat, out float val);
            float bodyLuma = Luma(r, g, b);
            switch (shade)
            {
                case Shade.Darker:
                    val *= 0.5f;
                    sat = Math.Min(0.9f, sat * 1.1f + 0.05f);
                    break;
                case Shade.Lighter:
                    val += (1f - val) * 0.6f;
                    sat *= 0.45f;
                    break;
                default:
                    float step = 0.06f + ((h >> 8) % 7u) * 0.01f;       // 22°..43° round the wheel
                    hue += ((h >> 4) & 1u) == 0u ? step : -step;
                    hue -= (float)Math.Floor(hue);
                    sat = Math.Max(0.25f, Math.Min(0.85f, sat * 0.9f));
                    val = bodyLuma > 0.5f ? val * 0.62f : val + (1f - val) * 0.5f;
                    break;
            }

            // Readability: push the value away from the body until the luma step holds. A body too dark to go darker by
            // the step (a deep blue's luma is ~0.1 even at full value) goes lighter, a body too light goes darker.
            bool darker = shade == Shade.Darker || (shade == Shade.Shifted && bodyLuma > 0.5f);
            if (darker && bodyLuma < MinLumaStep + 0.02f)
            {
                darker = false;
            }
            else if (!darker && bodyLuma > 1f - MinLumaStep - 0.02f)
            {
                darker = true;
            }

            for (int i = 0; i < 24; i++)
            {
                HsvToRgb(hue, sat, val, out float pr, out float pg, out float pb);
                if (Math.Abs(Luma(pr, pg, pb) - bodyLuma) >= MinLumaStep)
                {
                    break;
                }

                if (darker)
                {
                    val = Math.Max(0f, val - 0.07f);
                }
                else
                {
                    val = Math.Min(1f, val + 0.07f);
                    sat = Math.Max(0f, sat - 0.07f);
                }
            }

            HsvToRgb(hue, sat, val, out float fr, out float fg, out float fb);
            return Pack(fr, fg, fb);
        }

        /// <summary>Percent of patterned species that wear a bold pattern colour (#2864).</summary>
        public const int BoldPercent = 20;

        private const int BoldBlack = 0x1E1B1A;
        private const int BoldCharcoal = 0x34302E;
        private const int BoldWhite = 0xF2F0E8;
        private const int BoldCream = 0xE8D9B0;
        private const int BoldYellow = 0xF2C230;
        private const int BoldRed = 0xD8342B;

        /// <summary>True when the species wears a bold pattern colour (about <see cref="BoldPercent"/> %).</summary>
        public static bool IsBold(uint speciesSeed) => CoatMix.Mix(speciesSeed, CoatMix.SaltBold) % 100u < BoldPercent;

        /// <summary>The bold pattern colour of a species (#2864), or null when it wears a derived one: black marks on a warm
        /// vivid body (a warning look: wasp, ladybird, fire salamander), light or warning marks on a very dark body (white,
        /// cream, yellow, red), black or white on everything else (zebra, panda, magpie). A pattern that must go darker
        /// (two-tone cape, eye mask, countershading) or lighter (belly blaze) keeps the derived colour when the bold one goes
        /// the other way.
        /// Every bold colour differs from the body by at least <see cref="MinLumaStep"/> in luma.</summary>
        public static int? BoldRgb(uint speciesSeed, int bodyRgb, CoatPattern pattern)
        {
            if (pattern == CoatPattern.None || !IsBold(speciesSeed))
            {
                return null;
            }

            uint h = CoatMix.Mix(speciesSeed, CoatMix.SaltPatternColour) >> 7;
            Unpack(bodyRgb, out float r, out float g, out float b);
            RgbToHsv(r, g, b, out float hue, out float sat, out _);
            float luma = Luma(r, g, b);
            int mark;
            if (sat >= 0.45f && luma >= 0.35f && (hue <= 0.17f || hue >= 0.95f))
            {
                mark = h % 3u == 0u ? BoldCharcoal : BoldBlack;          // red, orange, yellow body: warning marks
            }
            else if (luma < 0.3f)
            {
                mark = (h % 4u) switch
                {
                    0u => BoldWhite,
                    1u => BoldCream,
                    2u => BoldYellow,
                    _ => luma < 0.16f ? BoldRed : BoldYellow,
                };
            }
            else
            {
                mark = luma > 0.6f || h % 2u == 0u ? BoldBlack : BoldWhite;
            }

            Unpack(mark, out float mr, out float mg, out float mb);
            float markLuma = Luma(mr, mg, mb);
            bool darker = markLuma < luma;
            if ((pattern is CoatPattern.TwoTone or CoatPattern.EyeMask or CoatPattern.Countershade && !darker)
                || (pattern == CoatPattern.BellyBlaze && darker)
                || Math.Abs(markLuma - luma) < MinLumaStep)
            {
                return null;
            }

            return mark;
        }

        /// <summary>The number of coat placements per species (#2865): an animal's part cubes shift and mirror the coat by
        /// one of these, so a herd's spots do not all sit in the same places.</summary>
        public const int PlacementVariants = 8;

        /// <summary>The coat placement of one animal, from its entity id (0..<see cref="PlacementVariants"/> − 1).</summary>
        public static int PlacementVariant(string? entityId)
            => (int)(CoatMix.Mix((uint)StableIdHash(entityId), CoatMix.SaltPlacement) % (uint)PlacementVariants);

        /// <summary>The number of per-animal jitter buckets (#2831).</summary>
        public const int JitterBuckets = 5;

        /// <summary>The jitter bucket of one animal, from its entity id: individuals of a species differ a little while the
        /// handful of buckets keeps the materials shared.</summary>
        public static int JitterBucket(string? entityId)
            => (int)(CoatMix.Mix((uint)StableIdHash(entityId), CoatMix.SaltJitter) % (uint)JitterBuckets);

        /// <summary>A bucket's colour multiplier: plain, ±5 % brightness, or a 4 % warm / cool hue lean.</summary>
        public static void JitterRgb(int bucket, out float r, out float g, out float b)
        {
            switch (bucket)
            {
                case 1: r = g = b = 0.95f; break;
                case 2: r = g = b = 1.05f; break;
                case 3: r = 1.04f; g = 1f; b = 0.96f; break;
                case 4: r = 0.96f; g = 1f; b = 1.04f; break;
                default: r = g = b = 1f; break;
            }
        }

        /// <summary>Rec. 601 luma of an sRGB colour (0..1).</summary>
        public static float Luma(float r, float g, float b) => 0.299f * r + 0.587f * g + 0.114f * b;

        /// <summary>0xRRGGBB → 0..1 channels.</summary>
        public static void Unpack(int rgb, out float r, out float g, out float b)
        {
            r = ((rgb >> 16) & 0xFF) / 255f;
            g = ((rgb >> 8) & 0xFF) / 255f;
            b = (rgb & 0xFF) / 255f;
        }

        /// <summary>0..1 channels → 0xRRGGBB (clamped, rounded).</summary>
        public static int Pack(float r, float g, float b)
            => (To8(r) << 16) | (To8(g) << 8) | To8(b);

        private static int To8(float v) => (int)Math.Round(Math.Max(0f, Math.Min(1f, v)) * 255f);

        /// <summary>RGB (0..1) → HSV (all 0..1).</summary>
        public static void RgbToHsv(float r, float g, float b, out float h, out float s, out float v)
        {
            float max = Math.Max(r, Math.Max(g, b));
            float min = Math.Min(r, Math.Min(g, b));
            float d = max - min;
            v = max;
            s = max <= 0f ? 0f : d / max;
            if (d <= 0f)
            {
                h = 0f;
                return;
            }

            if (max == r)
            {
                h = (g - b) / d / 6f;
            }
            else if (max == g)
            {
                h = ((b - r) / d + 2f) / 6f;
            }
            else
            {
                h = ((r - g) / d + 4f) / 6f;
            }

            h -= (float)Math.Floor(h);
        }

        /// <summary>HSV (all 0..1) → RGB (0..1).</summary>
        public static void HsvToRgb(float h, float s, float v, out float r, out float g, out float b)
        {
            h -= (float)Math.Floor(h);
            float hf = h * 6f;
            int i = (int)hf % 6;
            float f = hf - (int)hf;
            float p = v * (1f - s);
            float q = v * (1f - s * f);
            float t = v * (1f - s * (1f - f));
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
        }
    }
}
