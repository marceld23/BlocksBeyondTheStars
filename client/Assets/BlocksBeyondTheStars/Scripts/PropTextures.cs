// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Textures for the things the game builds from primitives with a hard-coded colour (#1956): door leaves and
    /// their trim, the machines of a factory, the base of a station terminal. Each such part has a TEXTURE KEY
    /// (<c>prop_door_wood_panel</c>, <c>prop_factory_metal</c>, …). The build ships no tile for these keys — the
    /// part keeps its colour — but the texture editor lists them, starting from that colour, and as soon as the
    /// player's pack or the world has a texture under the key, every part with that key shows it.
    ///
    /// One shared material per key: a prop used to get a new material per part per build, never destroyed with its
    /// cube. Because the material is shared, a texture change updates it IN PLACE — placed doors and running
    /// machines change their look without being rebuilt.
    /// </summary>
    public static class PropTextures
    {
        /// <summary>A texturable prop part: its key, the colour it has without a texture, and a name for the editor.</summary>
        public readonly struct Part
        {
            public Part(string key, Color color, string labelKey)
            {
                Key = key;
                Color = color;
                LabelKey = labelKey;
            }

            public string Key { get; }

            public Color Color { get; }

            /// <summary>Locale key of the part's name in the texture editor.</summary>
            public string LabelKey { get; }
        }

        // Doors: three kinds × (leaf, trim). The colours are the ones DoorView always used.
        public static readonly Part DoorSlidePanel = new Part("prop_door_slide_panel", new Color(0.62f, 0.69f, 0.78f), "ui.tex.prop.door_slide_panel");
        public static readonly Part DoorSlideTrim = new Part("prop_door_slide_trim", new Color(0.30f, 0.85f, 0.95f), "ui.tex.prop.door_slide_trim");
        public static readonly Part DoorHingePanel = new Part("prop_door_hinge_panel", new Color(0.45f, 0.30f, 0.16f), "ui.tex.prop.door_hinge_panel");
        public static readonly Part DoorHingeTrim = new Part("prop_door_hinge_trim", new Color(0.30f, 0.19f, 0.10f), "ui.tex.prop.door_hinge_trim");
        public static readonly Part DoorWoodPanel = new Part("prop_door_wood_panel", new Color(0.58f, 0.40f, 0.22f), "ui.tex.prop.door_wood_panel");
        public static readonly Part DoorWoodTrim = new Part("prop_door_wood_trim", new Color(0.38f, 0.25f, 0.13f), "ui.tex.prop.door_wood_trim");

        // Factory machines: the materials every archetype is bolted together from (the pulsing status light is
        // animated per machine and stays as it is).
        public static readonly Part FactoryMetal = new Part("prop_factory_metal", new Color(0.34f, 0.36f, 0.40f), "ui.tex.prop.factory_metal");
        public static readonly Part FactoryDark = new Part("prop_factory_dark", new Color(0.16f, 0.17f, 0.20f), "ui.tex.prop.factory_dark");
        public static readonly Part FactoryAccent = new Part("prop_factory_accent", new Color(0.72f, 0.42f, 0.14f), "ui.tex.prop.factory_accent");

        // Station decor: the unanimated housings (screens and tanks pulse their colour every frame and stay as they are).
        public static readonly Part DecorHousing = new Part("prop_decor_housing", new Color(0.15f, 0.16f, 0.19f), "ui.tex.prop.decor_housing");

        /// <summary>Every texturable part — what the texture editor lists under "Doors and machines".</summary>
        public static readonly IReadOnlyList<Part> All = new[]
        {
            DoorSlidePanel, DoorSlideTrim, DoorHingePanel, DoorHingeTrim, DoorWoodPanel, DoorWoodTrim,
            FactoryMetal, FactoryDark, FactoryAccent, DecorHousing,
        };

        private sealed class Entry
        {
            public Part Part;
            public Material Material;
            public Texture2D Texture;
        }

        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private static Shader _shader;
        private static bool _subscribed;

        /// <summary>The colour of a part without a texture, or null for an unknown key — the texture editor starts
        /// a prop texture from a tile of this colour.</summary>
        public static Color? BaseColor(string key)
        {
            foreach (var part in All)
            {
                if (part.Key == key)
                {
                    return part.Color;
                }
            }

            return null;
        }

        /// <summary>The shared material of a part: textured when a layer has a texture under its key, flat otherwise.</summary>
        public static Material MaterialFor(Part part)
        {
            if (!_subscribed)
            {
                _subscribed = true;
                GameTextures.Changed += OnTexturesChanged;
            }

            if (Entries.TryGetValue(part.Key, out var entry) && entry.Material != null)
            {
                return entry.Material;
            }

            if (_shader == null)
            {
                // A project shader (always in the build): the primitives' default material is stripped from
                // player builds and renders magenta.
                _shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            entry = new Entry { Part = part, Material = new Material(_shader) { name = part.Key } };
            Entries[part.Key] = entry;
            Apply(entry);
            return entry.Material;
        }

        private static void Apply(Entry entry)
        {
            if (entry.Texture != null)
            {
                UnityEngine.Object.Destroy(entry.Texture);
                entry.Texture = null;
            }

            // Frame 0 only: a prop is not drawn by the block shaders, which are what plays a frame strip.
            entry.Texture = GameTextures.LoadTileTexture(entry.Part.Key, TextureWrapMode.Repeat);
            entry.Material.mainTexture = entry.Texture;
            entry.Material.color = entry.Texture != null ? Color.white : ShaderColor.Srgb(entry.Part.Color);
        }

        private static void OnTexturesChanged(IReadOnlyCollection<string> keys)
        {
            foreach (var entry in Entries.Values)
            {
                if (entry.Material != null && (keys == null || keys.Count == 0 || Contains(keys, entry.Part.Key)))
                {
                    Apply(entry);
                }
            }
        }

        private static bool Contains(IReadOnlyCollection<string> keys, string key)
        {
            foreach (string k in keys)
            {
                if (k == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A flat 64×64 tile of a part's colour — what the texture editor opens for a prop key that has no
        /// texture yet. Raw RGBA32, rows bottom-up, fully opaque.</summary>
        public static byte[] FlatTile(Color color)
        {
            var c = (Color32)color;
            var raw = new byte[TextureTiles.BytesPerFrame];
            for (int i = 0; i < raw.Length; i += 4)
            {
                raw[i] = c.r;
                raw[i + 1] = c.g;
                raw[i + 2] = c.b;
                raw[i + 3] = 255;
            }

            return raw;
        }

        /// <summary>Drops the shared materials and textures (session teardown).</summary>
        public static void Release()
        {
            foreach (var entry in Entries.Values)
            {
                if (entry.Texture != null)
                {
                    UnityEngine.Object.Destroy(entry.Texture);
                }

                if (entry.Material != null)
                {
                    UnityEngine.Object.Destroy(entry.Material);
                }
            }

            Entries.Clear();
        }
    }
}
