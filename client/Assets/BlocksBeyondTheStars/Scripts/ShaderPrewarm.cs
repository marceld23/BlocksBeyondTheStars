// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #1521: compiles the shader variants the world will need BEFORE the first frame that needs them. Unity
    /// builds a variant on first use — with no preloaded collection (GraphicsSettings had none) every variant
    /// switch mid-game cost a 20–100 ms hitch: the fog mode the weather changes, the cascade keyword when the
    /// sun comes up, the soft-shadow keyword on a preset change. The four world shaders (terrain, see-through
    /// terrain, lit models, vertex-coloured models) × fog × shadow × soft-shadow keywords are warmed once per
    /// process, behind the opaque world-loading veil (GameBootstrap.Start), so the cost lands where a load is
    /// expected. Variants a shader does not declare are skipped (the collection refuses them).
    /// <para>#3261: the desktop loading screen now warms the same collection a few variants per frame while it waits
    /// for the bundled server (<see cref="WarmUpSome"/>); <see cref="WarmUp"/> in GameBootstrap then only finishes what
    /// is left — usually nothing. Caveat unchanged: on DX12/Vulkan a variant warm-up is not a full pipeline warm-up.</para>
    /// </summary>
    public static class ShaderPrewarm
    {
        private static bool _done;
        private static ShaderVariantCollection _collection; // built once, warmed piecewise and/or at once (#3261)
        private static int _variants;                       // variants the collection holds

        private static readonly string[] Shaders =
        {
            "BlocksBeyondTheStars/BlockAtlas",
            "BlocksBeyondTheStars/BlockAtlasTransparent",
            "BlocksBeyondTheStars/LitColor",
            "BlocksBeyondTheStars/VertexColorOpaque",
        };

        /// <summary>The effect shaders of the VFX overhaul (#2152). They declare no keywords, so one variant each — but
        /// without warming, the FIRST shot, scan or explosion of a session compiled its shader mid-action (a visible
        /// hitch exactly when the effect should pop).</summary>
        private static readonly string[] FxShaders =
        {
            "BlocksBeyondTheStars/Particle",
            "BlocksBeyondTheStars/ParticleAlpha",
            "BlocksBeyondTheStars/FxBeam",
            "BlocksBeyondTheStars/FxRing",
            "BlocksBeyondTheStars/FxShell",
            "BlocksBeyondTheStars/FxHolo",
            "BlocksBeyondTheStars/FxDebris",
            "BlocksBeyondTheStars/FxCrack",
            "BlocksBeyondTheStars/FxOutline", // #2556: the selection frame — on screen from the first frame of play
            "BlocksBeyondTheStars/FxSpaceDust",
            "BlocksBeyondTheStars/FxTunnel",
            "BlocksBeyondTheStars/Wormhole", // #2242: no hitch when the first rift comes into view
            "BlocksBeyondTheStars/Decal", // #3058: the first scorch mark (High) compiles behind the loading veil
        };

        /// <summary>The ground detail only ever draws GPU-instanced, so its variants carry INSTANCING_ON next to the sun-shadow
        /// keywords (#3063 tufts, #3056 meadow) — warmed so the first tuft or meadow in a tree's shade costs no hitch. The
        /// meadow runs on desktop clients only, so the browser skips its shaders.</summary>
        private static readonly (string Name, bool DesktopOnly)[] InstancedShaders =
        {
            ("BlocksBeyondTheStars/ScatterLit", false),
            ("BlocksBeyondTheStars/GrassShell", true),
            ("BlocksBeyondTheStars/MeadowFlower", true),
        };

        // Keyword axes as declared by the shaders' multi_compile lines (only the modes the game actually sets:
        // Sky uses linear fog, WeatherFx3D switches to exponential-squared; screen-space shadows are unused).
        private static readonly string[] FogKeywords = { null, "FOG_LINEAR", "FOG_EXP2" };
        private static readonly string[] ShadowKeywords = { null, "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE" };
        private static readonly string[] SoftKeywords = { null, "_SHADOWS_SOFT" };

        /// <summary>Variants the loading screen warmed ahead of the world while it waited for the server (#3261).</summary>
        public static int WarmedEarly { get; private set; }

        /// <summary>Warms the variant set once per process — whatever <see cref="WarmUpSome"/> has not warmed yet. Returns
        /// the number of variants this call warmed (0 on later calls, and when the server wait already warmed them all).</summary>
        public static int WarmUp()
        {
            if (_done)
            {
                return 0;
            }

            var collection = Collection();
            int left = Mathf.Max(0, _variants - collection.warmedUpVariantCount);
            if (left > 0)
            {
                collection.WarmUp();
            }

            Finish();
            return left;
        }

        /// <summary>
        /// #3261: warms variants one at a time until <paramref name="budgetMs"/> is spent (always at least one), so the
        /// loading screen spreads the compile over the frames in which it waits for the bundled server anyway — a new
        /// world boots for 10–20 s, and these ~100 variants used to be compiled in one block AFTER that, behind the
        /// world veil. Returns true once every variant is warm; <see cref="WarmUp"/> then has nothing left to do.
        /// </summary>
        public static bool WarmUpSome(double budgetMs)
        {
            if (_done)
            {
                return true;
            }

            var collection = Collection();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            bool all;
            do
            {
                all = collection.WarmUpProgressively(1);
            }
            while (!all && watch.Elapsed.TotalMilliseconds < budgetMs);

            WarmedEarly = collection.warmedUpVariantCount;
            if (all)
            {
                Finish();
            }

            return all;
        }

        /// <summary>Everything is warm: drop the collection (the compiled variants stay with their shaders).</summary>
        private static void Finish()
        {
            _done = true;
            _collection = null;
        }

        /// <summary>The variant set, built once (no compiling — that is <see cref="WarmUp"/>'s and
        /// <see cref="WarmUpSome"/>'s job). Rebuilt if Unity unloaded it in between.</summary>
        private static ShaderVariantCollection Collection()
        {
            if (_collection != null)
            {
                return _collection;
            }

            var collection = new ShaderVariantCollection();
            var keywords = new List<string>(3);
            int added = 0;
            foreach (var name in Shaders)
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    continue;
                }

                foreach (var fog in FogKeywords)
                    foreach (var shadow in ShadowKeywords)
                        foreach (var soft in SoftKeywords)
                        {
                            keywords.Clear();
                            if (fog != null) keywords.Add(fog);
                            if (shadow != null) keywords.Add(shadow);
                            if (soft != null) keywords.Add(soft);
                            if (TryAdd(collection, shader, PassType.ScriptableRenderPipeline, keywords.ToArray()))
                            {
                                added++;
                            }
                        }

                if (TryAdd(collection, shader, PassType.ShadowCaster, System.Array.Empty<string>()))
                {
                    added++;
                }
            }

            foreach (var name in FxShaders)
            {
                var shader = Shader.Find(name);
                if (shader != null && TryAdd(collection, shader, PassType.ScriptableRenderPipeline, System.Array.Empty<string>()))
                {
                    added++;
                }
            }

            added += AddInstanced(collection, keywords);
            _variants = added;
            _collection = collection;
            return collection;
        }

        /// <summary>Adds the instanced × shadow × soft-shadow variants of <see cref="InstancedShaders"/>.</summary>
        private static int AddInstanced(ShaderVariantCollection collection, List<string> keywords)
        {
            int added = 0;
            foreach (var (name, desktopOnly) in InstancedShaders)
            {
                var shader = desktopOnly && !HighDetails.DesktopClass ? null : Shader.Find(name);
                if (shader == null)
                {
                    continue;
                }

                foreach (var shadow in ShadowKeywords)
                    foreach (var soft in SoftKeywords)
                    {
                        keywords.Clear();
                        keywords.Add("INSTANCING_ON");
                        if (shadow != null) keywords.Add(shadow);
                        if (soft != null) keywords.Add(soft);
                        if (TryAdd(collection, shader, PassType.ScriptableRenderPipeline, keywords.ToArray()))
                        {
                            added++;
                        }
                    }
            }

            return added;
        }

        private static bool TryAdd(ShaderVariantCollection collection, Shader shader, PassType pass, string[] keywords)
        {
            try
            {
                collection.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords));
                return true;
            }
            catch (System.ArgumentException)
            {
                return false; // this shader does not declare that keyword combination
            }
        }
    }
}
