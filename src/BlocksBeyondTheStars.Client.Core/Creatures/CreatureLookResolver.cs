// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Where a creature's coat came from (#3972).</summary>
    public enum CreatureLookSource
    {
        Rolled,   // the species seed rolled pattern, pattern colour and hides (on a palette world the marks keep its colours)
        Authored, // a designed species without an explicit coat: its designed colours and hide, no pattern
        Coat,     // an explicit coat the server picked from the species' coat variants (#3972)
    }

    /// <summary>
    /// The finished look of one creature (#3972): everything the builder paints a coat with. Body and belly are the wire's
    /// colours (the server already put a picked coat variant or a world palette on them); the rest is decided here.
    /// </summary>
    public sealed class CreatureLook
    {
        public CreatureLook(CreatureLookSource source, uint speciesSeed, FaunaThemes.Look? world, int bodyRgb, int bellyRgb,
            CoatPattern pattern, int patternRgb, CreatureHides.HideSet hides, uint maskSeed, GlowPattern glow, int glowRgb)
        {
            Source = source;
            SpeciesSeed = speciesSeed;
            World = world;
            BodyRgb = bodyRgb;
            BellyRgb = bellyRgb;
            Pattern = pattern;
            PatternRgb = patternRgb;
            Hides = hides;
            MaskSeed = maskSeed;
            Glow = glow;
            GlowRgb = glowRgb;
        }

        /// <summary>Which source decided the coat.</summary>
        public CreatureLookSource Source { get; }

        /// <summary>The seed every per-species look decision starts from (<see cref="CreatureCoat.SpeciesSeed"/>).</summary>
        public uint SpeciesSeed { get; }

        /// <summary>The look of the world the creature belongs to (theme, motif, palette), or null without one.</summary>
        public FaunaThemes.Look? World { get; }

        /// <summary>The body colour (0xRRGGBB).</summary>
        public int BodyRgb { get; }

        /// <summary>The belly and accent colour (0xRRGGBB).</summary>
        public int BellyRgb { get; }

        /// <summary>The coat pattern (<see cref="CoatPattern.None"/> = a plain coat).</summary>
        public CoatPattern Pattern { get; }

        /// <summary>The pattern colour (0xRRGGBB).</summary>
        public int PatternRgb { get; }

        /// <summary>The hide of every body part.</summary>
        public CreatureHides.HideSet Hides { get; }

        /// <summary>The seed the pattern masks are drawn with.</summary>
        public uint MaskSeed { get; }

        /// <summary>The glow pattern (#2862; None unless the species glows).</summary>
        public GlowPattern Glow { get; }

        /// <summary>The glow colour (0xRRGGBB).</summary>
        public int GlowRgb { get; }
    }

    /// <summary>
    /// The ONE place a creature's coat is decided on the client (#3972). The sources, in order: an explicit coat on the wire
    /// (a species with coat variants — the server picked one per animal); then the authored rule (a designed species without
    /// an explicit coat wears no pattern); then the rolled coat from the species seed, leaning toward the world's theme and
    /// motif and keeping a palette world's marks in its colours. Tile availability and the world motifs are the client's,
    /// so the rolled choice stays here; the result is bit-identical to the decisions before the resolver existed.
    /// </summary>
    public static class CreatureLookResolver
    {
        /// <summary>The finished look of <paramref name="c"/>. <paramref name="world"/> is its world's look
        /// (<see cref="FaunaThemes.For"/>), <paramref name="patternTileAvailable"/> says whether a pattern tile loaded (null =
        /// none did) and <paramref name="hideAvailable"/> whether a hide tile did.</summary>
        public static CreatureLook Resolve(NetCreature c, FaunaThemes.Look? world, Func<string, bool>? patternTileAvailable,
            Func<string, bool> hideAvailable)
        {
            uint seed = CreatureCoat.SpeciesSeed(c.SpeciesId, c.VoiceSeed);
            if (!string.IsNullOrEmpty(c.CoatPattern))
            {
                return ResolveCoat(c, seed, world, patternTileAvailable, hideAvailable);
            }

            bool authored = CreatureCoat.IsAuthored(c.SpeciesId);
            var pattern = authored ? CoatPattern.None : CreatureCoat.RolledPattern(c, world, patternTileAvailable);
            return new CreatureLook(
                authored ? CreatureLookSource.Authored : CreatureLookSource.Rolled,
                seed,
                world,
                c.ColorRgb,
                c.BellyRgb,
                pattern,
                CreatureCoat.PatternRgb(seed, c.ColorRgb, pattern, world), // #3448: a palette world keeps its marks in it
                CreatureHides.Pick(c, hideAvailable, world),
                CreatureCoat.MaskSeed(c),
                GlowMasks.PatternFor(c),
                GlowMasks.ColourFor(seed, c.ColorRgb));
        }

        /// <summary>The explicit coat the server picked (#3972): its pattern by name (an unknown name, or a tile pattern whose
        /// tile did not load, wears none), its pattern colour, its hide head to tail (the species' pick when the hide did not
        /// load), and masks drawn from the species seed and the animal's mask variant — at most
        /// <see cref="CoatVariants.MaskVariants"/> layouts per coat.</summary>
        private static CreatureLook ResolveCoat(NetCreature c, uint seed, FaunaThemes.Look? world,
            Func<string, bool>? patternTileAvailable, Func<string, bool> hideAvailable)
        {
            var pattern = ExplicitPattern(c.CoatPattern, patternTileAvailable);
            string? hide = CreatureHides.IsKnown(c.Hide) && hideAvailable(c.Hide) ? c.Hide : null;
            var hides = hide != null ? new CreatureHides.HideSet(hide, hide, hide, hide) : CreatureHides.Pick(c, hideAvailable, world);
            int variant = Math.Max(0, Math.Min(CoatVariants.MaskVariants - 1, c.CoatMask));
            uint maskSeed = CoatMix.Mix(CreatureCoat.MaskSeed(c) ^ (uint)variant, CoatMix.SaltMaskVariant);
            return new CreatureLook(CreatureLookSource.Coat, seed, world, c.ColorRgb, c.BellyRgb, pattern, c.CoatPatternRgb,
                hides, maskSeed, GlowMasks.PatternFor(c), GlowMasks.ColourFor(seed, c.ColorRgb));
        }

        /// <summary>The pattern an explicit coat names, or <see cref="CoatPattern.None"/>.</summary>
        public static CoatPattern ExplicitPattern(string? name, Func<string, bool>? patternTileAvailable)
        {
            if (!CreatureLookNames.IsPattern(name) || !Enum.TryParse(name!, out CoatPattern pattern))
            {
                return CoatPattern.None;
            }

            bool tileMissing = CreatureCoat.IsTilePattern(pattern)
                && (patternTileAvailable == null || !patternTileAvailable(CoatMasks.TileKey(pattern)));
            return tileMissing ? CoatPattern.None : pattern;
        }
    }
}
