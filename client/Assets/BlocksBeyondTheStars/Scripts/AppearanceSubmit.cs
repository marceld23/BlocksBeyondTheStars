// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using BlocksBeyondTheStars.Client.HudFrames;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The files and pictures the character editor submits (#3262 skins, #3319 HUD frames) — shared by the main-menu Avatar
    /// Designer's own Submit button and the editor's Submit in both hosts, so a skin sent from inside a world is the same
    /// file as one sent from the menu.
    /// <para>
    /// <c>skin.json</c> carries the WHOLE look — the four base colours, the 32×32 face and the four body paintings — in the
    /// format <c>tools/gen_pioneer_skins.py --from-skin</c> reads; its picture comes from a throwaway
    /// <see cref="AvatarPreviewRig"/> (the figure alone, head to toe). A HUD frame's picture is the frame itself, composed
    /// onto a dark 960 × 540 screen by the same layout the game draws it with (<see cref="HudFrameComposer"/>).
    /// </para>
    /// </summary>
    public static class AppearanceSubmit
    {
        /// <summary>The skin file's format: 2 = colours + face + body paintings (1 was colours only).</summary>
        public const int SkinFormatVersion = 2;

        /// <summary>The preview rig's parking slot: the Character tab uses 0 and the appearance editor 1.</summary>
        private const int PreviewSlot = 3;

        /// <summary>The HUD frame picture: a 16:9 screen, dark like a night sky.</summary>
        private const int HudPictureW = 960, HudPictureH = 540, HudPictureBackground = 0x141C2A;

        [Serializable]
        private sealed class SkinJson
        {
            public int formatVersion = SkinFormatVersion;
            public string key, name, skin, torso, arms, legs;
            public string facePixels = string.Empty, torsoPixels = string.Empty, armPixels = string.Empty,
                legPixels = string.Empty, helmetPixels = string.Empty;
        }

        /// <summary>The skin file of <paramref name="look"/> (colours as #RRGGBB, paintings as the game stores them).</summary>
        public static string SkinFile(string key, string name, FaceEditor.AppearanceSnapshot look, bool pretty)
        {
            string Paint(int part) => look.Paints != null && part < look.Paints.Length ? look.Paints[part] ?? string.Empty : string.Empty;
            var skin = new SkinJson
            {
                key = key,
                name = name ?? string.Empty,
                skin = Hex(look.Skin),
                torso = Hex(look.Torso),
                arms = Hex(look.Arms),
                legs = Hex(look.Legs),
                facePixels = look.Face ?? string.Empty,
                torsoPixels = Paint(BlocksBeyondTheStars.Shared.State.BodyPaint.Torso),
                armPixels = Paint(BlocksBeyondTheStars.Shared.State.BodyPaint.Arms),
                legPixels = Paint(BlocksBeyondTheStars.Shared.State.BodyPaint.Legs),
                helmetPixels = Paint(BlocksBeyondTheStars.Shared.State.BodyPaint.Helmet),
            };
            return JsonUtility.ToJson(skin, pretty);
        }

        /// <summary>A throwaway <see cref="AvatarPreviewRig"/> under <paramref name="parent"/>, dressed in
        /// <paramref name="look"/>, read once and destroyed; <paramref name="done"/> gets the PNG (or null).</summary>
        public static IEnumerator CaptureSkinPicture(Transform parent, FaceEditor.AppearanceSnapshot look, Action<byte[]> done)
        {
            var go = new GameObject("SubmitPreviewRig");
            go.transform.SetParent(parent, false);
            var rig = go.AddComponent<AvatarPreviewRig>();
            rig.EnsureBuilt(look.Skin, look.Torso, look.Arms, look.Legs, slot: PreviewSlot);
            rig.SetFace(look.Face ?? string.Empty);
            for (int part = 0; look.Paints != null && part < look.Paints.Length; part++)
            {
                rig.SetBodyPaint(part, look.Paints[part] ?? string.Empty);
            }

            rig.SetActive(true);
            byte[] png = null;
            yield return EditorPreviewCapture.FromRenderTexture(rig.Texture, bytes => png = bytes);
            UnityEngine.Object.Destroy(go);
            done?.Invoke(png);
        }

        /// <summary>The picture of a HUD frame at the player's size, as a PNG (null when it cannot be made).</summary>
        public static byte[] HudFramePicture(HudFrameDesign design, int sizeLevel)
        {
            if (design == null || design.IsEmpty)
            {
                return null;
            }

            var palette = new int[HudFrameDesign.PaletteSize];
            for (int i = 0; i < palette.Length; i++)
            {
                Color32 c = FacePalette.ColorOf(i);
                palette[i] = (c.r << 16) | (c.g << 8) | c.b;
            }

            int tile = HudFrameLayout.TilePixels(HudPictureW, HudPictureH, sizeLevel);
            byte[] topDown = HudFrameComposer.Compose(design, HudPictureW, HudPictureH, tile, 0, palette, HudPictureBackground);
            var bottomUp = new byte[topDown.Length];
            int stride = HudPictureW * 4;
            for (int y = 0; y < HudPictureH; y++)
            {
                Buffer.BlockCopy(topDown, y * stride, bottomUp, (HudPictureH - 1 - y) * stride, stride); // Unity's row 0 is the bottom
            }

            var tex = new Texture2D(HudPictureW, HudPictureH, TextureFormat.RGBA32, false);
            try
            {
                tex.LoadRawTextureData(bottomUp);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[HudFrame] picture failed: {e.Message}");
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        public static string Hex(Color c)
            => $"#{Mathf.RoundToInt(c.r * 255f):X2}{Mathf.RoundToInt(c.g * 255f):X2}{Mathf.RoundToInt(c.b * 255f):X2}";
    }
}
