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
    /// </summary>
    public static class ShaderPrewarm
    {
        private static bool _done;

        private static readonly string[] Shaders =
        {
            "BlocksBeyondTheStars/BlockAtlas",
            "BlocksBeyondTheStars/BlockAtlasTransparent",
            "BlocksBeyondTheStars/LitColor",
            "BlocksBeyondTheStars/VertexColorOpaque",
        };

        // Keyword axes as declared by the shaders' multi_compile lines (only the modes the game actually sets:
        // Sky uses linear fog, WeatherFx3D switches to exponential-squared; screen-space shadows are unused).
        private static readonly string[] FogKeywords = { null, "FOG_LINEAR", "FOG_EXP2" };
        private static readonly string[] ShadowKeywords = { null, "_MAIN_LIGHT_SHADOWS", "_MAIN_LIGHT_SHADOWS_CASCADE" };
        private static readonly string[] SoftKeywords = { null, "_SHADOWS_SOFT" };

        /// <summary>Warms the variant set once per process. Returns the number of variants added (0 on later calls).</summary>
        public static int WarmUp()
        {
            if (_done)
            {
                return 0;
            }

            _done = true;
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

            if (added > 0)
            {
                collection.WarmUp();
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
