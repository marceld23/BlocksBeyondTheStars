// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The colour of a world as seen from ORBIT (space view spheres, distant bodies in the surface
    /// sky, the planet outside a station window) — derived from DATA instead of hand-kept palettes:
    ///
    ///   ground = the average colour of the planet type's actual surface-block atlas tile,
    ///   → blended toward this world's OWN vegetation colour by the type's flora density
    ///     (each planet rolls its own flora hue — the same <see cref="BlocksBeyondTheStars.Shared.World.FloraTints"/>
    ///     roll that paints the plants on the ground),
    ///   → toward water-blue by water abundance, toward lava-glow by lava abundance.
    ///
    /// One source of truth: a mud flat reads brown, a lush world reads in ITS vegetation colour,
    /// an ocean world blue — and every future planet type is automatically right.
    /// </summary>
    public static class PlanetOrbitLook
    {
        private static readonly Color WaterBlue = new Color(0.24f, 0.46f, 0.72f);
        private static readonly Color LavaGlow = new Color(0.82f, 0.34f, 0.16f);

        /// <summary>The location key a body's WORLD uses for its per-planet flora colours — MUST mirror
        /// GameBootstrap's LocationName composition ("System · Planet"). Previews keyed on the bare body
        /// name rolled a DIFFERENT vegetation hue than the leaves on the ground (the "purple planet from
        /// orbit, green trees after landing" bug).</summary>
        public static string LocationKeyFor(string systemName, string bodyName)
            => string.IsNullOrEmpty(systemName) ? bodyName ?? string.Empty : $"{systemName} · {bodyName}";

        /// <summary>The mixed orbital ground colour for a body, or <paramref name="fallback"/> when the
        /// planet type is unknown to the content registry. With the body's id and the save's terrain generation the
        /// vegetation and the water are the ones of the world's colour harmony (generation 48) — the colours the baked map
        /// shows a moment later, so the sphere does not change its hue when the map arrives. Without them (the planet
        /// behind a station window, which names no body) the classic rolls.</summary>
        public static Color GroundColor(GameContent content, BlockTextureAtlas atlas, long worldSeed,
            string locationKey, string planetTypeKey, Color fallback, string bodyId = null, int terrainGeneration = 0)
        {
            var planet = content?.GetPlanet(planetTypeKey ?? string.Empty);
            if (planet == null)
            {
                return fallback;
            }

            var block = content.GetBlock(planet.SurfaceBlock);
            Color col = block != null && atlas != null ? atlas.AverageColor(block.NumericId.Value) : fallback;

            // Lush worlds read as their own per-planet vegetation colour from orbit.
            // Generation 48: the world's colour harmony (inactive on an older world, a void world or without a body id).
            var harmony = string.IsNullOrEmpty(bodyId)
                ? BlocksBeyondTheStars.Shared.World.WorldHarmony.None
                : BlocksBeyondTheStars.Shared.World.WorldHarmony.For(worldSeed, bodyId, planet, terrainGeneration);
            float flora = Mathf.Clamp01((float)planet.FloraDensity);
            if (flora > 0.01f)
            {
                var (r, g, b) = BlocksBeyondTheStars.Shared.World.FloraTints.For(
                    worldSeed, locationKey ?? string.Empty, FloraKeyFor(planet.SurfaceBlock), planet, harmony); // #3423: the type's palette
                col = Color.Lerp(col, new Color(r, g, b), Mathf.Min(0.6f, flora * 0.9f));
            }

            // null abundance = the generator's "auto, a modest amount" — approximate it the same way.
            float water = Mathf.Clamp01((float)(planet.WaterAbundance ?? 0.3));
            col = Color.Lerp(col, harmony.Active ? WaterOf(worldSeed, bodyId, planet, terrainGeneration) : WaterBlue, water * 0.6f);

            float lava = Mathf.Clamp01((float)(planet.LavaAbundance ?? (planet.SurfaceBlock == "basalt" ? 0.5 : 0.0)));
            if (lava > 0.01f)
            {
                col = Color.Lerp(col, LavaGlow, lava * 0.7f);
            }

            return col;
        }

        /// <summary>The water colour of a generation-48 world as seen from orbit: its own tint where the type rolls one (the
        /// harmony keeps it near the sky), softened like <see cref="WaterBlue"/> softens the classic atlas blue; the classic
        /// blue for an untinted or a rainbow sea. Clean water is assumed — whether a toxic world's water rolled toxic is
        /// the baked map's to show.</summary>
        private static Color WaterOf(long worldSeed, string bodyId, BlocksBeyondTheStars.Shared.Definitions.PlanetType planet, int terrainGeneration)
        {
            var (rgb, mode) = BlocksBeyondTheStars.Shared.World.FluidTints.ForWorld(worldSeed, bodyId, planet, terrainGeneration);
            if (mode != BlocksBeyondTheStars.Shared.World.FluidTints.Mode.Tint)
            {
                return WaterBlue;
            }

            var tint = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
            return Color.Lerp(tint, new Color(0.5f, 0.5f, 0.5f), 0.2f);
        }

        /// <summary>The world's signature flora species per ground type (fungus caps on mycelium, tree
        /// crowns elsewhere) — the species whose per-world colour paints the orbit view.</summary>
        private static string FloraKeyFor(string surfaceBlock) => surfaceBlock switch
        {
            "mycelium" => "mushroom_cap",
            _ => "tree_leaves",
        };
    }
}
