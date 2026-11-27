// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The station decor's forms. The cockpit console, the terminals of the lab station and the ship console and the
    /// medbay's capsule device are built from the chunky boxes of <see cref="StationDecorForms"/>, rounded a little
    /// (<see cref="DecorFormsRound"/>, a share of each box's thinnest side), and lit by the model shader in the look's
    /// light, with the ambient floor and fill of the figures aboard. They wear the device tiles named below: one on the
    /// housing, one on the decks, one on the screens, which keep their colour per station and their flicker and glow.
    /// <para>
    /// Draw calls: one renderer for housing + deck (two sub-meshes, two shared materials) and one for the screen (its own
    /// material, because the flicker animates it). The meshes are built once per station kind and shared. A missing tile
    /// falls back quietly: the housing to the texturable decor housing (<see cref="PropTextures.DecorHousing"/>), the deck
    /// to the housing, the screen to a flat unlit screen. Render-only like the rest of the decor.
    /// </para>
    /// </summary>
    public sealed partial class StationDecorView
    {
        private const float DecorFormsRound = 0.25f;

        // The housing is light (a dark one reads as almost black in the cabin): the same
        // plates, joints and rivets as the devices' dark housing metal, on a near-white base.
        private const string MetalTileKey = "devform_metal_light", DeckTileKey = "devform_deck", ScreenTileKey = "devform_screen";

        /// <summary>The darkest a screen texel gets, as a share of the screen colour (the tile's black reads as a dim glass).</summary>
        private const float ScreenFloor = 0.22f;

        private static readonly Dictionary<StationDecorForms.Kind, (Mesh Body, Mesh Screen)> FormMeshes =
            new Dictionary<StationDecorForms.Kind, (Mesh Body, Mesh Screen)>();

        private static bool _formTilesRead;
        private static Material _formHousing, _formDeck;
        private static Texture2D _formScreenTex;
        private static Shader _formShader;

        /// <summary>Hangs a station's form under <paramref name="parent"/>: the housing-and-deck body and, for a console, its
        /// flickering screen in <paramref name="screenColor"/> — both on a child (<see cref="FormTurnName"/>) that turns the
        /// form toward whoever uses it (<see cref="QueueFormTurn"/>) about the cell's centre, so the root's placement stays as
        /// it is. The cockpit's hologram hangs on that child too and turns with its console.</summary>
        private static void AddForm(Transform parent, StationDecorForms.Kind kind, Color screenColor)
        {
            ReadFormTiles();
            var meshes = FormMeshesOf(kind);

            var turn = new GameObject(FormTurnName);
            turn.transform.SetParent(parent, false);
            parent = turn.transform;

            var body = new GameObject("DecorForm");
            body.transform.SetParent(parent, false);
            body.AddComponent<MeshFilter>().sharedMesh = meshes.Body;
            body.AddComponent<MeshRenderer>().sharedMaterials = meshes.Body.subMeshCount > 1
                ? new[] { _formHousing, _formDeck }
                : new[] { _formHousing };

            if (meshes.Screen == null)
            {
                return;
            }

            var screen = new GameObject("DecorFormScreen");
            screen.transform.SetParent(parent, false);
            screen.AddComponent<MeshFilter>().sharedMesh = meshes.Screen;
            var mat = FormScreenMaterial(screenColor);
            screen.AddComponent<MeshRenderer>().sharedMaterial = mat;
            screen.AddComponent<ScreenFlicker>().Base = screenColor;
            screen.AddComponent<FormMaterialOwner>().Mat = mat; // one material per screen (the flicker animates it): freed with it
        }

        // --- materials ------------------------------------------------------------------------

        /// <summary>Reads the three device tiles once and makes the shared housing and deck materials. Read again when a
        /// material is gone (the fallback is <see cref="PropTextures"/>' shared material, which its session teardown frees).</summary>
        private static void ReadFormTiles()
        {
            if (_formTilesRead && _formHousing != null && _formDeck != null)
            {
                return;
            }

            _formTilesRead = true;
            _formShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");

            var metal = FormTileTexture(FormTileBytes(MetalTileKey), MetalTileKey);
            _formHousing = metal != null ? FormLit("StationForm_housing", metal, 0f) : PropTextures.MaterialFor(PropTextures.DecorHousing);

            // Lit keys of the deck glow where the tile marks them with alpha 0 (the model shader's glow); else it is lit only.
            var deck = FormTileTexture(FormTileBytes(DeckTileKey), DeckTileKey);
            _formDeck = deck != null ? FormLit("StationForm_deck", deck, 1f) : _formHousing;

            _formScreenTex = FormTileTexture(StationDecorForms.GlowScreenPixels(FormTileBytes(ScreenTileKey), ScreenFloor), ScreenTileKey);
            Debug.Log($"[Look] station decor forms: housing {(metal != null ? "tile" : "fallback")}, deck {(deck != null ? "tile" : "fallback")}, screen {(_formScreenTex != null ? "tile" : "fallback")}");
        }

        /// <summary>A tile's pixels from <see cref="GameTextures"/> (a texture pack or the world may replace the key; the
        /// bundled tile otherwise), or null when no layer has it.</summary>
        private static byte[] FormTileBytes(string key)
        {
            byte[] raw = GameTextures.TileBytes(key);
            return raw != null && raw.Length == TextureTiles.BytesPerFrame ? raw : null;
        }

        /// <summary>A tile texture set up like every block tile on a model: point-filtered, no mips, repeating.</summary>
        private static Texture2D FormTileTexture(byte[] raw, string name)
        {
            if (raw == null || raw.Length != TextureTiles.BytesPerFrame)
            {
                return null;
            }

            var tex = new Texture2D(TextureTiles.Size, TextureTiles.Size, TextureFormat.RGBA32, false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(raw);
            tex.Apply();
            return tex;
        }

        private static Material FormLit(string name, Texture2D tex, float glow)
        {
            var mat = new Material(_formShader) { name = name, color = Color.white, mainTexture = tex };
            if (glow > 0f && mat.HasProperty("_Glow"))
            {
                mat.SetFloat("_Glow", glow);
            }

            // Housing and deck take the ambient floor and fill light of the FIGURES aboard (PlayerAvatar, CreatureBuilder,
            // WorldEntities) instead of the props' shader defaults (0.35 and 0), so the housing does not read as almost
            // black in the cabin.
            if (mat.HasProperty("_Floor") && mat.HasProperty("_Fill"))
            {
                mat.SetFloat("_Floor", 0.62f);
                mat.SetFloat("_Fill", 0.3f);
            }

            return mat;
        }

        /// <summary>A screen's own material: the grey glow tile under the station's colour, added on top of the light (no
        /// ambient floor, so the dark glass stays dark while the data glows) — or a flat unlit screen without the tile.</summary>
        private static Material FormScreenMaterial(Color tint)
        {
            if (_formScreenTex == null)
            {
                return Unlit(tint);
            }

            var mat = new Material(_formShader) { name = "StationForm_screen", color = ShaderColor.Srgb(tint), mainTexture = _formScreenTex };
            if (mat.HasProperty("_Floor"))
            {
                mat.SetFloat("_Floor", 0f);
            }

            if (mat.HasProperty("_Glow"))
            {
                mat.SetFloat("_Glow", 1f);
            }

            return mat;
        }

        /// <summary>Destroys a screen's own material with the screen (the decor is rebuilt when the station list changes).</summary>
        private sealed class FormMaterialOwner : MonoBehaviour
        {
            public Material Mat;

            private void OnDestroy()
            {
                if (Mat != null)
                {
                    Destroy(Mat);
                }
            }
        }
    }
}
