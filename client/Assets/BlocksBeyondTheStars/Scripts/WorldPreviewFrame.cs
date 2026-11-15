// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shows a local world's "last played" picture (<see cref="WorldPreviewFiles"/>, taken by <see cref="WorldPreviewCapture"/>):
    /// the 16:9 frame at the top of the detail pane of "My worlds" (<see cref="Add"/>) — with a neutral placeholder, a planet
    /// icon on the card fill, while the world has no picture yet — and the small picture on the main menu's Continue card
    /// (<see cref="TryAddPicture"/>, only when there is one). A picture is read when its world is shown
    /// (<c>Texture2D.LoadImage</c>), never for the list's rows; the last <see cref="CacheSize"/>
    /// stay loaded (a newer file replaces its old texture), and every texture goes with the frame — the screen closing.
    /// </summary>
    public sealed class WorldPreviewFrame : MonoBehaviour
    {
        /// <summary>How many pictures a frame keeps loaded (the worlds the player clicked through last).</summary>
        public const int CacheSize = 4;

        private const string PlaceholderIcon = "cat_planet";
        private const float PlaceholderIconSize = 72f;
        private static readonly Color FrameFill = new Color(0.06f, 0.13f, 0.23f, 1f);
        private static readonly Color PlaceholderTint = new Color(UiKit.CyanDim.r, UiKit.CyanDim.g, UiKit.CyanDim.b, 0.75f);

        private readonly List<Loaded> _cache = new List<Loaded>(CacheSize);
        private RawImage _picture;
        private GameObject _placeholder;
        private float _w, _h;

        /// <summary>The saves folder the pictures are read from; null = <see cref="LocalServerLauncher.SavesRoot"/>.</summary>
        public string SavesRoot { get; set; }

        /// <summary>The picture (shown only while there is one).</summary>
        public RawImage Picture => _picture;

        /// <summary>The placeholder (null on the Continue card's picture, which has none).</summary>
        public GameObject Placeholder => _placeholder;

        /// <summary>True while a picture is on show.</summary>
        public bool ShowsPicture => _picture != null && _picture.gameObject.activeSelf && _picture.texture != null;

        /// <summary>The frame at a top-left rect of <paramref name="parent"/>: the card fill with the placeholder icon, the
        /// picture over it once <see cref="Show"/> finds one.</summary>
        public static WorldPreviewFrame Add(Transform parent, float x, float y, float w, float h)
        {
            var frame = Create(parent, "WorldPreview", x, y, w, h);
            UiKit.AddPanel(frame.transform, 0f, 0f, w, h, FrameFill).gameObject.name = "PreviewFill";
            frame._placeholder = UiKit.AddStretchChild(frame.transform, "PreviewPlaceholder");
            var icon = UiKit.AddIcon(frame._placeholder.transform, (w - PlaceholderIconSize) / 2f, (h - PlaceholderIconSize) / 2f,
                PlaceholderIconSize, PlaceholderIcon);
            if (icon != null)
            {
                icon.color = PlaceholderTint;
            }

            frame._picture = AddPicture(frame.transform, w, h);
            frame.ShowTexture(null);
            return frame;
        }

        /// <summary>The bare picture at a top-left rect of <paramref name="parent"/> — only when <paramref name="path"/> holds
        /// one; null (and nothing added) otherwise. The main menu's Continue card.</summary>
        public static WorldPreviewFrame TryAddPicture(Transform parent, float x, float y, float w, float h, string path)
        {
            DateTime? written = WorldPreviewFiles.WrittenUtc(path);
            var tex = written.HasValue ? LoadTexture(path) : null;
            if (tex == null)
            {
                return null;
            }

            var frame = Create(parent, "WorldPreviewPicture", x, y, w, h);
            frame._picture = AddPicture(frame.transform, w, h);
            frame._cache.Add(new Loaded(path, written.Value, tex));
            frame.ShowTexture(tex);
            return frame;
        }

        /// <summary>Shows the picture of the world <paramref name="worldName"/> (in <see cref="SavesRoot"/>), or the placeholder.</summary>
        public void Show(string worldName)
            => ShowFile(WorldPreviewFiles.PathFor(SavesRoot ?? LocalServerLauncher.SavesRoot, worldName));

        /// <summary>Shows the picture at <paramref name="path"/>, or the placeholder when there is none (null: none).</summary>
        public void ShowFile(string path) => ShowTexture(Load(path));

        /// <summary>Reads a picture file into a texture (the caller destroys it); null when there is none or it does not decode.</summary>
        public static Texture2D LoadTexture(string path)
        {
            byte[] bytes = WorldPreviewFiles.TryRead(path);
            if (bytes == null)
            {
                return null;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false) { name = "WorldPreview", wrapMode = TextureWrapMode.Clamp };
            if (tex.LoadImage(bytes, markNonReadable: true))
            {
                return tex;
            }

            BlockCubeFaces.DestroyAsset(tex);
            return null;
        }

        /// <summary>Destroys every loaded picture and shows the placeholder. The frame does this itself when it goes.</summary>
        public void Clear()
        {
            ReleaseTextures();
            ShowTexture(null);
        }

        private void OnDestroy() => ReleaseTextures();

        private static WorldPreviewFrame Create(Transform parent, string name, float x, float y, float w, float h)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, x, y, w, h);
            var frame = go.AddComponent<WorldPreviewFrame>();
            frame._w = w;
            frame._h = h;
            return frame;
        }

        private static RawImage AddPicture(Transform parent, float w, float h)
        {
            var go = new GameObject("PreviewPicture", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            UiKit.Place(go, 0f, 0f, w, h);
            var picture = go.AddComponent<RawImage>();
            picture.raycastTarget = false; // a click goes to the button or card under it
            return picture;
        }

        private void ShowTexture(Texture2D tex)
        {
            if (_picture != null)
            {
                _picture.texture = tex;
                if (tex != null)
                {
                    var (x, y, w, h) = WorldPreviewFiles.CoverRect(tex.width, tex.height, _w, _h);
                    _picture.uvRect = new Rect(x, y, w, h);
                }

                _picture.gameObject.SetActive(tex != null);
            }

            if (_placeholder != null)
            {
                _placeholder.SetActive(tex == null);
            }
        }

        /// <summary>The picture at <paramref name="path"/>: the loaded one while the file is unchanged, else read now.</summary>
        private Texture2D Load(string path)
        {
            DateTime? written = WorldPreviewFiles.WrittenUtc(path);
            if (!written.HasValue)
            {
                return null;
            }

            int i = _cache.FindIndex(e => string.Equals(e.Path, path, StringComparison.Ordinal));
            if (i >= 0)
            {
                var hit = _cache[i];
                _cache.RemoveAt(i);
                if (hit.Written == written.Value && hit.Texture != null)
                {
                    _cache.Add(hit); // the most recently shown goes last
                    return hit.Texture;
                }

                Forget(hit.Texture); // the file was written again since — read the new one
            }

            var tex = LoadTexture(path);
            if (tex == null)
            {
                return null;
            }

            if (_cache.Count >= CacheSize)
            {
                Forget(_cache[0].Texture); // the least recently shown — never the one on show, which is always last
                _cache.RemoveAt(0);
            }

            _cache.Add(new Loaded(path, written.Value, tex));
            return tex;
        }

        private void Forget(Texture2D tex)
        {
            if (_picture != null && _picture.texture == tex)
            {
                _picture.texture = null;
            }

            BlockCubeFaces.DestroyAsset(tex);
        }

        private void ReleaseTextures()
        {
            foreach (var loaded in _cache)
            {
                BlockCubeFaces.DestroyAsset(loaded.Texture);
            }

            _cache.Clear();
        }

        /// <summary>A loaded picture: its file, when that file was written, and the texture.</summary>
        private readonly struct Loaded
        {
            public Loaded(string path, DateTime written, Texture2D texture)
            {
                Path = path;
                Written = written;
                Texture = texture;
            }

            public string Path { get; }
            public DateTime Written { get; }
            public Texture2D Texture { get; }
        }
    }
}
