using System.Collections.Generic;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.Shared.World;
using Spacecraft.WorldGeneration;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Bakes a small equirect map (full circumference × full latitude band) of a body's REAL
    /// generated world — the generator ships with the client and is deterministic from
    /// (seed, planet type, circumference), so this is the actual terrain the player will land on:
    /// seas and lava seas with depth shading, upland ponds/lakes, height-shaded ground in the
    /// true surface-block colour, and a vegetation wash in this world's own flora hue.
    /// Used by the landing-pad chooser (pads drawn at their true longitudes) and as the orbital
    /// planet-sphere texture, so the planet you see IS the world you get. Cached per body+size.
    /// </summary>
    public static class WorldMinimap
    {
        private static readonly Dictionary<string, Texture2D> _cache = new();

        public static Texture2D Bake(GameContent content, BlockTextureAtlas atlas, long worldSeed,
            string locationName, string planetTypeKey, int circumference, int texW, int texH)
        {
            string key = $"{worldSeed}|{locationName}|{planetTypeKey}|{circumference}|{texW}x{texH}";
            if (_cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var planet = content?.GetPlanet(planetTypeKey ?? string.Empty);
            var tex = new Texture2D(texW, texH, TextureFormat.RGBA32, mipChain: true)
            {
                wrapMode = TextureWrapMode.Repeat,     // longitude wraps; the sphere/strips tile seamlessly
                filterMode = FilterMode.Bilinear,
            };

            if (planet == null || content == null)
            {
                var flat = new Color[texW * texH];
                for (int i = 0; i < flat.Length; i++) { flat[i] = new Color(0.45f, 0.44f, 0.42f); }
                tex.SetPixels(flat); tex.Apply(true);
                _cache[key] = tex;
                return tex;
            }

            var gen = new WorldGenerator(worldSeed, content);
            gen.SetCircumference(circumference);
            int latPeriod = WorldConstants.LatitudePeriodFor(circumference);
            int sea = gen.SeaLevel(planet);

            // Which fluid fills the sea (mirrors the generator's water-vs-lava rule).
            bool hasAir = !planet.IsAirless;
            bool volcanic = planet.SurfaceBlock == "basalt" || planet.DeepBlock == "basalt";
            double waterAb = planet.WaterAbundance ?? (hasAir ? 0.55 : 0.0);
            bool lavaSea = waterAb <= 0.0 && (planet.LavaAbundance ?? (volcanic ? 0.7 : 0.0)) > 0.0;

            Color ground = atlas != null && content.GetBlock(planet.SurfaceBlock) is { } sb
                ? atlas.AverageColor(sb.NumericId.Value)
                : new Color(0.45f, 0.44f, 0.42f);

            // Vegetation wash in this world's OWN flora colour (the same per-planet FloraTints roll).
            float floraW = Mathf.Min(0.5f, Mathf.Clamp01((float)planet.FloraDensity) * 0.9f);
            var (fr, fg, fb) = FloraTints.For(worldSeed, locationName ?? string.Empty,
                planet.SurfaceBlock == "mycelium" ? "mushroom_cap" : "tree_leaves");
            var floraCol = new Color(fr, fg, fb);

            var shallow = lavaSea ? new Color(0.95f, 0.45f, 0.15f) : new Color(0.30f, 0.55f, 0.78f);
            var deep = lavaSea ? new Color(0.55f, 0.15f, 0.05f) : new Color(0.10f, 0.22f, 0.45f);

            var px = new Color[texW * texH];
            for (int y = 0; y < texH; y++)
            {
                int wz = (int)(((y + 0.5) / texH - 0.5) * latPeriod);
                for (int x = 0; x < texW; x++)
                {
                    int wx = (int)((x + 0.5) / texW * circumference);
                    int h = gen.SurfaceHeight(planet, wx, wz);

                    Color c;
                    if (sea != int.MinValue && h <= sea)
                    {
                        c = Color.Lerp(shallow, deep, Mathf.Clamp01((sea - h) / 14f)); // depth-shaded sea
                    }
                    else if (!lavaSea && gen.SurfacePondDepth(planet, wx, wz) > 0)
                    {
                        c = shallow; // an upland pond/lake flush with the terrain
                    }
                    else
                    {
                        float baseY = sea != int.MinValue ? sea : planet.BaseHeight - planet.Amplitude;
                        float rel = Mathf.Clamp01((h - baseY) / Mathf.Max(1f, planet.Amplitude * 2f));
                        c = ground * Mathf.Lerp(0.7f, 1.2f, rel); // height-shaded ground
                        if (floraW > 0.01f)
                        {
                            c = Color.Lerp(c, floraCol * Mathf.Lerp(0.75f, 1.1f, rel), floraW);
                        }
                    }

                    c.a = 1f;
                    px[y * texW + x] = c;
                }
            }

            tex.SetPixels(px);
            tex.Apply(true);
            _cache[key] = tex;
            return tex;
        }
    }
}
