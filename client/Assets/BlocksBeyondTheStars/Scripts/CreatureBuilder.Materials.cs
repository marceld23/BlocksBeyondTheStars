// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The materials of a creature body: colours, shaders and material caches, hide and pattern textures, eye, horn,
    /// tusk and tooth materials, the mood tints and the baked coat (<see cref="CoatKit"/>).
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        // #1514: creature materials are never mutated after creation, so identical (shader, colour, texture)
        // requests share ONE Material — every build used to create 8–16 fresh Materials (each behind a
        // Shader.Find string lookup) that nothing ever destroyed, growing for the whole session as fauna
        // spawned and despawned. Shared materials also let the SRP batcher group the parts.
        private static Shader _unlitShader, _litShader;
        private static readonly Dictionary<Color, Material> UnlitCache = new Dictionary<Color, Material>();
        private static readonly Dictionary<(Color Color, Texture2D Tex, float Glow), Material> LitCache = new Dictionary<(Color, Texture2D, float), Material>();

        /// <summary>#1999: the shared unlit colour material, for the view's own markers (the stomp ring).</summary>
        internal static Material UnlitMaterial(Color color) => Unlit(color);

        private static Material Unlit(Color color)
        {
            if (UnlitCache.TryGetValue(color, out var cached) && cached != null)
            {
                return cached;
            }

            _unlitShader ??= Shader.Find("Unlit/Color") ?? Shader.Find("BlocksBeyondTheStars/VertexColorOpaque");
            var m = new Material(_unlitShader) { color = ShaderColor.Srgb(color) };
            UnlitCache[color] = m;
            return m;
        }

        // Shared (loaded once) tintable grayscale hide tiles by hide name (#2831: one table, CreatureHides.All); the body
        // multiplies them by the species colour. A tile that did not load is simply absent — the pools skip it. #2860: each
        // tile is normalised (CoatBake.Normalize) once; the normalised bytes are kept for the coat bakes.
        private static readonly Dictionary<string, Texture2D> HideTextures = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, byte[]> NormalizedHides = new Dictionary<string, byte[]>();
        private static readonly System.Func<string, bool> HideAvailable = name => name != null && HideTextures.ContainsKey(name);
        private static bool _texLoaded;

        // #2863: the pattern mask tiles (raw bytes, read through the texture source like every tile), loaded on first ask.
        private static readonly Dictionary<string, byte[]> PatternTiles = new Dictionary<string, byte[]>();
        private static readonly System.Func<string, bool> PatternTileAvailable = key => PatternTile(key) != null;
        private static readonly System.Func<string, byte[]> PatternTileBytes = PatternTile;

        // #2867: the horn tiles by name, normalised like the hides.
        private static readonly Dictionary<string, Texture2D> HornTextures = new Dictionary<string, Texture2D>();

        private static void EnsureTextures()
        {
            if (_texLoaded)
            {
                return;
            }

            _texLoaded = true;
            foreach (string name in CreatureHides.All)
            {
                var tex = LoadTex(CreatureHides.TileKey(name), out byte[] normalized);
                if (tex != null)
                {
                    HideTextures[name] = tex;
                    NormalizedHides[name] = normalized;
                }
            }

            foreach (string name in CreatureHorns.Tiles)
            {
                var tex = LoadTex(CreatureHorns.TileKey(name), out _);
                if (tex != null)
                {
                    HornTextures[name] = tex;
                }
            }
        }

        private static byte[] PatternTile(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (!PatternTiles.TryGetValue(key, out var raw))
            {
                raw = GameTextures.TileBytes(key);
                if (raw != null && raw.Length != CoatBake.TileBytes)
                {
                    raw = null;
                }

                PatternTiles[key] = raw;
            }

            return raw;
        }

        /// <summary>A hide's shared (lifted) texture by name; null when the name is unknown or its tile did not load.</summary>
        private static Texture2D HideTex(string name)
            => name != null && HideTextures.TryGetValue(name, out var tex) ? tex : null;

        /// <summary>A named hide, else the plain hide (a fixed accent like the colossus' back plates).</summary>
        private static Texture2D HideOr(string name) => HideTex(name) ?? HideTex("hide");

        private static Texture2D LoadTex(string key, out byte[] normalized)
        {
            // The winning layer of the texture source (#1952): world texture, local pack, or the bundled tile.
            normalized = null;
            byte[] raw = GameTextures.TileBytes(key);
            if (raw == null || raw.Length != CoatBake.TileBytes)
            {
                return null;
            }

            normalized = CoatBake.Normalize(raw); // #2860: one bright mean, one contrast for every tile
            if (!GameTextures.ShowsPaintedTile(key))
            {
                CalmHide(normalized); // A hide a player painted stays as painted
            }
            return CoatTexture(normalized, "CreatureHide");
        }

        /// <summary>A 64² coat texture from RGBA32 bytes: repeating, point-filtered, with mip-maps (#2860) so a fine hide
        /// calms down at a distance instead of shimmering, the pixel look kept up close.</summary>
        /// <summary>Calmer hides: every texel of a hide tile moves a share of the way to the tile's mean
        /// colour (<see cref="GameLook.HideCalmNow"/>; 0 = no calming) — the hide keeps its pattern at a lower
        /// contrast, so a body reads as one soft surface instead of a busy picture. The alpha stays.</summary>
        private static void CalmHide(byte[] rgba)
        {
            float calm = GameLook.HideCalmNow;
            if (calm <= 0f || rgba == null || rgba.Length < 4)
            {
                return;
            }

            long r = 0, g = 0, b = 0;
            int texels = rgba.Length / 4;
            for (int i = 0; i < texels; i++)
            {
                r += rgba[i * 4];
                g += rgba[(i * 4) + 1];
                b += rgba[(i * 4) + 2];
            }

            float mr = r / (float)texels, mg = g / (float)texels, mb = b / (float)texels;
            for (int i = 0; i < texels; i++)
            {
                rgba[i * 4] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(rgba[i * 4], mr, calm)), 0, 255);
                rgba[(i * 4) + 1] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(rgba[(i * 4) + 1], mg, calm)), 0, 255);
                rgba[(i * 4) + 2] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(rgba[(i * 4) + 2], mb, calm)), 0, 255);
            }
        }

        private static Texture2D CoatTexture(byte[] rgba, string name)
        {
            var tex = new Texture2D(CoatMasks.Size, CoatMasks.Size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
                ignoreMipmapLimit = true, // the low presets' global mip limit would halve a 64² coat to 32²
            };
            tex.SetPixelData(rgba, 0);
            tex.Apply(true, true); // build the mip chain; no CPU copy needed once uploaded
            return tex;
        }

        // #2866: the eye textures, one per (look, iris colour, glow).
        private static readonly Dictionary<(EyeLook Look, int Iris, bool Glows), Texture2D> EyeTextures
            = new Dictionary<(EyeLook, int, bool), Texture2D>();

        private static readonly Dictionary<Texture2D, Material> EyeMats = new Dictionary<Texture2D, Material>();
        private static Mesh _eyeSphere;

        /// <summary>The eye material of a species (#2866): its eye look drawn on a small texture, lit flat (floor 1) so the
        /// eye reads like the self-lit eyes it replaces; a shining iris adds its colour as glow.</summary>
        private static Material EyeFaceMaterial(NetCreature c)
        {
            var look = CreatureEyes.LookFor(c);
            int iris = CreatureEyes.IrisRgb(c);
            bool glows = CreatureEyes.IrisGlows(c);
            var key = (look, iris, glows);
            if (!EyeTextures.TryGetValue(key, out var tex) || tex == null)
            {
                tex = new Texture2D(CreatureEyes.Size, CreatureEyes.Size, TextureFormat.RGBA32, false)
                {
                    name = "CreatureEye",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point,
                };
                tex.LoadRawTextureData(CreatureEyes.Draw(look, iris, glows));
                tex.Apply(false, true);
                EyeTextures[key] = tex;
            }

            if (EyeMats.TryGetValue(tex, out var cached) && cached != null)
            {
                return cached;
            }

            _litShader ??= Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(_litShader) { color = Color.white, mainTexture = tex };
            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", 1f);
                m.SetFloat("_Fill", 0f);
            }

            if (m.HasProperty("_Glow"))
            {
                m.SetFloat("_Glow", glows ? GlowMasks.Strength : 0f);
            }

            EyeMats[tex] = m;
            return m;
        }

        /// <summary>The eye sphere (#2866, <see cref="CreatureEyes.BuildSphere"/>): diameter 1 like Unity's sphere, but with
        /// its poles on ±Z, so the texture lies on the front half seen straight on and the back half shows the sclera without
        /// a triangle smearing the iris round the side.</summary>
        private static Mesh EyeSphereMesh()
        {
            if (_eyeSphere != null)
            {
                return _eyeSphere;
            }

            var d = CreatureEyes.BuildSphere();
            int n = d.Positions.Length / 3;
            var verts = new Vector3[n];
            var normals = new Vector3[n];
            var uvs = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                verts[i] = new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                normals[i] = new Vector3(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(d.Uvs[i * 2], d.Uvs[i * 2 + 1]);
            }

            _eyeSphere = new Mesh
            {
                name = "CreatureEyeSphere",
                vertices = verts,
                normals = normals,
                uv = uvs,
                triangles = d.Triangles,
            };
            _eyeSphere.RecalculateBounds();
            return _eyeSphere;
        }

        /// <summary>#2867: the species' horn material — its horn tile in its horn colour.</summary>
        private Material HornMaterial(NetCreature c)
        {
            string tile = CreatureHorns.TileFor(c, _look);
            return Lit(Rgb(CreatureHorns.ColourFor(c, tile)), HornTextures.TryGetValue(tile, out var tex) ? tex : null);
        }

        /// <summary>#2867: a dark horn (an arachnid's fangs, a sandworm's spikes) on the species' horn tile.</summary>
        private Material DarkHornMaterial(NetCreature c)
        {
            string tile = CreatureHorns.TileFor(c, _look);
            return Lit(Rgb(CreatureHorns.DarkColourFor(c)), HornTextures.TryGetValue(tile, out var tex) ? tex : null);
        }

        /// <summary>#2867: tusks — an ivory per species on the smooth grain.</summary>
        private static Material TuskMaterial(NetCreature c)
            => Lit(Rgb(CreatureHorns.TuskColourFor(c)), HornTextures.TryGetValue("grain", out var tex) ? tex : null);

        /// <summary>#2867: teeth — a white per species on the smooth grain.</summary>
        private static Material ToothMaterial(NetCreature c)
            => Lit(Rgb(CreatureHorns.ToothColourFor(c)), HornTextures.TryGetValue("grain", out var tex) ? tex : null);

        // #2830: the baked coats — hide luminance × (body colour, pattern colour by the mask), 64 × 64, one per (hide,
        // pattern, mask variant, mask seed, mood-tinted colours). Unpatterned and fully covered parts never bake (they use
        // the shared hide with a material colour), so a species bakes one to a few (every glowing part bakes, #2862). Never
        // destroyed: they live as long as the shared hide tiles. The cap keeps a marathon session bounded (~17 MB with the
        // mip chains); past it new species simply go unpatterned and unlit.
        private const int MaxBakedCoats = 768;
        private static readonly Dictionary<(string Hide, CoatPattern Pattern, CoatMasks.Variant Variant, uint Seed, int Body, int Mark, GlowPattern Glow, int GlowRgb, CoatPart GlowPart), Texture2D> BakedCoats
            = new Dictionary<(string, CoatPattern, CoatMasks.Variant, uint, int, int, GlowPattern, int, CoatPart), Texture2D>();

        private static int Pack(Color c) => CreatureCoat.Pack(c.r, c.g, c.b);

        /// <summary>The mood tints of one build — the hostile red and the companion green-cyan are lerps baked into the coat
        /// colours (they follow the mood, the body is rebuilt when it changes); the sleep dim is a plain multiplier.</summary>
        private readonly struct Mood
        {
            public Mood(float hostileLerp, bool companion, float dim)
            {
                HostileLerp = hostileLerp;
                Companion = companion;
                Dim = dim;
            }

            public float HostileLerp { get; }

            public bool Companion { get; }

            public float Dim { get; }

            /// <summary>The colour with the mood lerps applied (hostile first, then the companion cast — the order the
            /// builds always used; a companion is never asleep, so moving the dim to the end changes nothing).</summary>
            public Color Tint(Color c)
            {
                if (HostileLerp > 0f)
                {
                    c = Color.Lerp(c, new Color(0.85f, 0.2f, 0.15f), HostileLerp);
                }

                if (Companion)
                {
                    // Tamed companions read with a gentle friendly green-cyan cast (never the hostile red), so you can
                    // tell your pet apart from wild fauna at a glance (design: docs/developer/CREATURE_TAMING.md).
                    c = Color.Lerp(c, new Color(0.35f, 0.85f, 0.65f), 0.18f);
                }

                return c;
            }
        }

        /// <summary>The mood of a creature for a build path: which tints that path applies (the giants keep their own
        /// smaller set).</summary>
        private static Mood MoodOf(NetCreature c, float hostileLerp, bool sleepDims, bool companionTint)
            => new Mood(c.Hostile ? hostileLerp : 0f,
                companionTint && !string.IsNullOrEmpty(c.OwnerId),
                sleepDims && c.Asleep ? 0.85f : 1f); // a gentle dim only — the "z z z" already reads sleep

        /// <summary>Sets up the coat of this build (#2829-#2831): the species' per-part hides and pattern, the mood-tinted
        /// colours, the glow factors (body, belly/accents) and this animal's jitter bucket.</summary>
        private void NewCoat(NetCreature c, Mood mood, float bodyGlow, float bellyGlow)
        {
            // #3972: the whole coat from the one look resolver — explicit coat, authored rule or rolled (with the home world's
            // theme, motif and palette, #2868/#3448).
            var look = CreatureLookResolver.Resolve(c, FaunaThemes.For(c, Planets), PatternTileAvailable, HideAvailable);
            _look = look.World;
            _placement = CreatureCoat.PlacementVariant(c.Id);    // #2865: this animal's own coat placement
            CreatureCoat.JitterRgb(CreatureCoat.JitterBucket(c.Id), out float jr, out float jg, out float jb);
            // #2862: a glowing species shines in its glow pattern now; only a soft rest of the old whole-body brightening
            // stays, so a glower still reads a touch brighter.
            float bodyMul = c.Glows ? 1f + (bodyGlow - 1f) * 0.3f : 1f;
            float bellyMul = c.Glows ? 1f + (bellyGlow - 1f) * 0.3f : 1f;
            _coat = new CoatKit(
                look.Pattern,
                look.Hides,
                mood.Tint(Rgb(look.BodyRgb)),
                mood.Tint(Rgb(look.BellyRgb)),
                mood.Tint(Rgb(look.PatternRgb)),
                bodyMul * mood.Dim,
                bellyMul * mood.Dim,
                new Color(jr, jg, jb, 1f),
                look.MaskSeed,
                look.Glow,
                Rgb(look.GlowRgb));
        }

        /// <summary>
        /// One build's coat (#2830, #2831, #2862): the material of each body-part role. A part the pattern does not reach
        /// wears its hide in its own colour (the body's, or the belly's for the belly and the accents); a part the pattern
        /// covers fully wears its hide in the pattern colour; a masked part — and every part a glow pattern reaches — wears a
        /// baked coat with a neutral material colour (the glow sits in the coat's alpha, the material's _Glow lights it).
        /// The sleep dim, the glow factor and the animal's jitter are the material colour's multiplier in every case. It also
        /// remembers, per material, the hide's repeat size (#2860) and whether the part maps v once (#2863).
        /// </summary>
        private sealed class CoatKit
        {
            private readonly CreatureHides.HideSet _hides;
            private readonly Color _body, _belly, _mark, _jitter, _glowColour;
            private readonly float _bodyMul, _bellyMul;
            private readonly uint _maskSeed;
            private readonly Dictionary<CoatPart, Material> _mats = new Dictionary<CoatPart, Material>();
            private readonly Dictionary<(CoatMasks.Variant, CoatPart), float[]> _masks = new Dictionary<(CoatMasks.Variant, CoatPart), float[]>();
            private readonly Dictionary<Material, float> _metres = new Dictionary<Material, float>();
            private readonly HashSet<Material> _pinned = new HashSet<Material>();

            public CoatKit(CoatPattern pattern, CreatureHides.HideSet hides, Color body, Color belly, Color mark,
                float bodyMul, float bellyMul, Color jitter, uint maskSeed, GlowPattern glow, Color glowColour)
            {
                Pattern = pattern;
                _hides = hides;
                _body = body;
                _belly = belly;
                _mark = mark;
                _bodyMul = bodyMul;
                _bellyMul = bellyMul;
                _jitter = jitter;
                _maskSeed = maskSeed;
                Glow = glow;
                _glowColour = glowColour;
            }

            public CoatPattern Pattern { get; }

            /// <summary>#2862: the species' glow pattern (None unless it glows).</summary>
            public GlowPattern Glow { get; }

            /// <summary>The hide a part role wears (null without hide tiles) — the fur asks it.</summary>
            public string HideOf(CoatPart part) => _hides.For(part);

            /// <summary>The repeat size of the hide a material of this coat wears (#2860).</summary>
            public float MetresOf(Material mat)
                => mat != null && _metres.TryGetValue(mat, out float m) ? m : TiledCube.TileMetres;

            /// <summary>True for a material of this coat whose part maps v once (the countershaded flanks, #2863).</summary>
            public bool Pins(Material mat) => mat != null && _pinned.Contains(mat);

            /// <summary>True when the head's skull box takes the pinned mesh (the eye band sits at eye height).</summary>
            public bool PinsHead => CreatureCoat.PinsV(Pattern, CoatPart.Head);

            /// <summary>The material of a part role (cached per build; the materials themselves are shared, #1514).</summary>
            public Material Mat(CoatPart part)
            {
                if (_mats.TryGetValue(part, out var cached))
                {
                    return cached;
                }

                string hide = _hides.For(part);
                bool accent = part is CoatPart.Belly or CoatPart.Ornament;
                Color own = accent ? _belly : _body;
                float mul = accent ? _bellyMul : _bodyMul;
                Material m = null;
                bool bakedMaterial = false;
                var coverage = CreatureCoat.Coverage(Pattern, part);
                if (GlowMasks.Coverage(Glow, part) != CoatCoverage.None)
                {
                    // #2862: a glowing part always bakes — the pattern (if any) and the glow mask in one coat.
                    var baked = Baked(part, hide, own, coverage);
                    if (baked != null)
                    {
                        m = Lit(Scaled(Color.white, mul), baked, GlowMasks.Strength);
                        bakedMaterial = true;
                    }
                }

                if (m == null)
                {
                    switch (coverage)
                    {
                        case CoatCoverage.Full:
                            m = Lit(Scaled(_mark, mul), HideTex(hide));
                            break;
                        case CoatCoverage.Masked:
                            var baked = Baked(part, hide, own, coverage);
                            if (baked != null)
                            {
                                m = Lit(Scaled(Color.white, mul), baked);
                                bakedMaterial = true;
                            }

                            break;
                    }
                }

                if (m == null)
                {
                    m = Lit(Scaled(own, mul), HideTex(hide));
                }

                _metres[m] = CreatureHides.TileMetresOf(hide);
                if (bakedMaterial && CreatureCoat.PinsV(Pattern, part))
                {
                    // Only a baked coat is this part's own material; a plain fallback (past the bake cap) is shared with
                    // limbs, jaw and lids, which must not take a pinned mesh.
                    _pinned.Add(m);
                }

                _mats[part] = m;
                return m;
            }

            /// <summary>An accent colour that follows the crest band: the pattern colour on a crest-banded species, else
            /// <paramref name="normal"/> (the giants' fixed plate, fin and sail tones).</summary>
            public Color Accent(Color normal) => Pattern == CoatPattern.CrestBand ? _mark * _bodyMul : normal;

            private Color Scaled(Color c, float mul)
                => new Color(c.r * mul * _jitter.r, c.g * mul * _jitter.g, c.b * mul * _jitter.b, 1f);

            private Texture2D Baked(CoatPart part, string hide, Color own, CoatCoverage coverage)
            {
                var variant = CoatMasks.VariantOf(Pattern, part);
                var glowCoverage = GlowMasks.Coverage(Glow, part);
                // The glow mask depends on the part's orientation and role: key it by the part when the part glows.
                var glowPart = glowCoverage == CoatCoverage.None ? CoatPart.Torso : part;
                var glow = glowCoverage == CoatCoverage.None ? GlowPattern.None : Glow;
                var key = (hide ?? string.Empty, Pattern, variant, _maskSeed, Pack(own), Pack(_mark), glow, Pack(_glowColour), glowPart);
                if (BakedCoats.TryGetValue(key, out var tex) && tex != null)
                {
                    return tex;
                }

                if (BakedCoats.Count >= MaxBakedCoats)
                {
                    return null;
                }

                float[] mask = null;
                if (coverage != CoatCoverage.None)
                {
                    if (!_masks.TryGetValue((variant, CoatPart.Torso), out mask))
                    {
                        mask = CoatMasks.Draw(Pattern, part, _maskSeed, PatternTileBytes);
                        _masks[(variant, CoatPart.Torso)] = mask;
                    }
                }

                float[] glowMask = null;
                if (glow != GlowPattern.None && !_masks.TryGetValue((CoatMasks.Variant.None, part), out glowMask))
                {
                    glowMask = GlowMasks.Draw(glow, part, _maskSeed);
                    _masks[(CoatMasks.Variant.None, part)] = glowMask;
                }

                byte[] hideBytes = hide != null && NormalizedHides.TryGetValue(hide, out var norm) ? norm : null;
                tex = CoatTexture(CoatBake.Bake(hideBytes, own.r, own.g, own.b, _mark.r, _mark.g, _mark.b, mask,
                    glowMask, _glowColour.r, _glowColour.g, _glowColour.b), "CreatureCoat");
                BakedCoats[key] = tex;
                return tex;
            }
        }

        // Ambient floor for creature bodies. Higher than the LitColor default (0.35) because a creature's
        // textured, camera-away faces (a flier's belly seen from below, a back turned to the fixed key light)
        // sit at the floor and otherwise sink to a black silhouette against a bright sky. Only creatures raise
        // it — every other LitColor user keeps the 0.35 shader default.
        private const float CreatureFloor = 0.62f;

        // Fill light from the flank opposite the fixed key. The single key light leaves the away-facing side of
        // a creature at the floor, so it still read dark; this unshadowed fill lifts that flank. Only creatures
        // set it — every other LitColor user keeps the 0 default (single key light, unchanged).
        private const float CreatureFill = 0.3f;

        private static Material Lit(Color color, Texture2D tex, float glow = 0f)
        {
            var key = (color, tex, glow);
            if (LitCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            _litShader ??= Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var m = new Material(_litShader) { color = ShaderColor.Srgb(color) };
            if (tex != null)
            {
                m.mainTexture = tex;
            }

            if (m.HasProperty("_Floor"))
            {
                m.SetFloat("_Floor", CreatureFloor); // no-op on the Unlit/Color fallback
                m.SetFloat("_Fill", CreatureFill);
            }

            if (glow > 0f && m.HasProperty("_Glow"))
            {
                m.SetFloat("_Glow", glow); // #2862: the coat's alpha-0 texels shine (LitColor)
            }

            LitCache[key] = m;
            return m;
        }
    }
}
