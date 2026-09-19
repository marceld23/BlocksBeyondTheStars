// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player's local texture pack (#1952): a plain folder of PNGs under the game's data root. A file
    /// <c>&lt;key&gt;.png</c> overrides the texture <c>key</c> on this install only — 64×64 for a still texture, or
    /// a horizontal strip of 64×64 frames for an animated one. <c>icons/&lt;name&gt;.png</c> overrides an icon.
    /// The folder is the interface: the project has no file dialog, so "open folder", drop a PNG and "reload" is
    /// how files get in, and the texture editor writes its "use for me" results here.
    /// </summary>
    public static class TexturePackFolder
    {
        public const string FolderName = "texture_overrides";
        private const string MetaFile = "textures.json";

        [Serializable]
        private sealed class MetaEntry
        {
            public string key;
            public int fps;
        }

        [Serializable]
        private sealed class MetaJson
        {
            public List<MetaEntry> textures = new List<MetaEntry>();
        }

        public static string Root => Path.Combine(AppPaths.Root, FolderName);

        public static string IconRoot => Path.Combine(Root, "icons");

        /// <summary>Reads the whole folder and makes it the local layer. Safe to call again ("reload"). Files that
        /// are not valid tiles are skipped with a warning — a player's stray PNG must never break the atlas.</summary>
        public static int Reload()
        {
            var loaded = new Dictionary<string, TextureFrames>(StringComparer.Ordinal);
            try
            {
                if (Directory.Exists(Root))
                {
                    var fps = ReadMeta();
                    foreach (string file in Directory.GetFiles(Root, "*.png"))
                    {
                        string key = Path.GetFileNameWithoutExtension(file);
                        if (!TextureTiles.IsValidKey(key))
                        {
                            Debug.LogWarning("[TexturePack] skipped '" + Path.GetFileName(file) + "': not a texture key");
                            continue;
                        }

                        var frames = TryReadPng(file, key, fps.TryGetValue(key, out int f) ? f : 0);
                        if (frames != null)
                        {
                            loaded[key] = frames;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not read " + Root + ": " + ex.Message);
            }

            ClearIconCache();
            GameTextures.SetLocalLayer(loaded);
            return loaded.Count;
        }

        /// <summary>Saves a texture into the pack ("use for me") and applies it at once.</summary>
        public static bool Save(string key, byte[][] frames, int fps)
        {
            if (!TextureTiles.IsValidKey(key) || frames == null || frames.Length == 0
                || frames.Length > TextureTiles.MaxFrames)
            {
                return false;
            }

            var mode = TextureTiles.AlphaModeOf(key);
            var clean = new byte[frames.Length][];
            for (int i = 0; i < frames.Length; i++)
            {
                if (frames[i] == null || frames[i].Length != TextureTiles.BytesPerFrame)
                {
                    return false;
                }

                clean[i] = (byte[])frames[i].Clone();
                TextureTiles.EnforceAlpha(clean[i], mode);
            }

            if (clean.Length > 1 && !TextureTiles.IsValidAnimation(clean.Length, fps))
            {
                fps = 8;
            }

            try
            {
                Directory.CreateDirectory(Root);
                File.WriteAllBytes(Path.Combine(Root, key + ".png"), EncodeStrip(clean));
                var meta = ReadMeta();
                if (clean.Length > 1)
                {
                    meta[key] = fps;
                }
                else
                {
                    meta.Remove(key);
                }

                WriteMeta(meta);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not save " + key + ": " + ex.Message);
                return false;
            }

            GameTextures.SetLocal(key, new TextureFrames(clean, fps, TextureLayer.Local));
            return true;
        }

        /// <summary>Removes a texture from the pack — the official (or world) texture shows again.</summary>
        public static void Remove(string key)
        {
            try
            {
                string file = Path.Combine(Root, key + ".png");
                if (File.Exists(file))
                {
                    File.Delete(file);
                }

                var meta = ReadMeta();
                if (meta.Remove(key))
                {
                    WriteMeta(meta);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not remove " + key + ": " + ex.Message);
            }

            GameTextures.RemoveLocal(key);
        }

        // ---------------------------------------------------------------- icons

        private static readonly Dictionary<string, Texture2D> IconCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        /// <summary>The player's replacement for the icon <paramref name="name"/> (e.g. <c>item_torch</c>), or null.
        /// Cached; the cache owns the textures and frees them on <see cref="Reload"/>.</summary>
        public static Texture2D IconOverride(string name)
        {
            if (!GameTextures.UseLocalPack || string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (IconCache.TryGetValue(name, out var cached))
            {
                return cached;
            }

            Texture2D tex = null;
            try
            {
                string file = Path.Combine(IconRoot, name + ".png");
                if (File.Exists(file))
                {
                    tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
                    {
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                    if (!tex.LoadImage(File.ReadAllBytes(file)))
                    {
                        UnityEngine.Object.Destroy(tex);
                        tex = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not read icon " + name + ": " + ex.Message);
                tex = null;
            }

            IconCache[name] = tex; // null is cached too — most icons have no override
            return tex;
        }

        public static bool SaveIcon(string name, Texture2D icon)
        {
            if (string.IsNullOrEmpty(name) || icon == null)
            {
                return false;
            }

            try
            {
                Directory.CreateDirectory(IconRoot);
                File.WriteAllBytes(Path.Combine(IconRoot, name + ".png"), icon.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not save icon " + name + ": " + ex.Message);
                return false;
            }

            ClearIconCache();
            IconResolver.ClearCache();
            UiKit.ClearIconCache();
            return true;
        }

        public static void RemoveIcon(string name)
        {
            try
            {
                string file = Path.Combine(IconRoot, name + ".png");
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not remove icon " + name + ": " + ex.Message);
            }

            ClearIconCache();
            IconResolver.ClearCache();
            UiKit.ClearIconCache();
        }

        private static void ClearIconCache()
        {
            foreach (var tex in IconCache.Values)
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }

            IconCache.Clear();
        }

        // ---------------------------------------------------------------- PNG <-> frames

        /// <summary>Decodes a PNG into frames: width must be a multiple of 64 (1..8 frames), height 64. The alpha
        /// rule is enforced here, so a see-through "stone" can never reach the atlas.</summary>
        public static TextureFrames TryReadPng(string file, string key, int fps)
        {
            Texture2D tex = null;
            try
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (!tex.LoadImage(File.ReadAllBytes(file)))
                {
                    Debug.LogWarning("[TexturePack] skipped '" + Path.GetFileName(file) + "': not a readable PNG");
                    return null;
                }

                var frames = SplitStrip(tex);
                if (frames == null)
                {
                    Debug.LogWarning("[TexturePack] skipped '" + Path.GetFileName(file) + "': must be 64 px high and "
                        + "64, 128, … " + (TextureTiles.Size * TextureTiles.MaxFrames) + " px wide");
                    return null;
                }

                var mode = TextureTiles.AlphaModeOf(key);
                foreach (var frame in frames)
                {
                    TextureTiles.EnforceAlpha(frame, mode);
                }

                if (frames.Length > 1 && !TextureTiles.IsValidAnimation(frames.Length, fps))
                {
                    fps = 8;
                }

                return new TextureFrames(frames, fps, TextureLayer.Local);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] skipped '" + Path.GetFileName(file) + "': " + ex.Message);
                return null;
            }
            finally
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }
        }

        /// <summary>Cuts a decoded strip texture into raw frames (bottom-up rows), or null for a wrong size.</summary>
        public static byte[][] SplitStrip(Texture2D tex)
        {
            int size = TextureTiles.Size;
            if (tex == null || tex.height != size || tex.width < size || tex.width % size != 0
                || tex.width / size > TextureTiles.MaxFrames)
            {
                return null;
            }

            int count = tex.width / size;
            var all = tex.GetPixels32(); // rows bottom-up — the same order the raw tiles use
            var frames = new byte[count][];
            for (int f = 0; f < count; f++)
            {
                var raw = new byte[TextureTiles.BytesPerFrame];
                for (int y = 0; y < size; y++)
                {
                    int src = (y * tex.width) + (f * size);
                    int dst = y * size * 4;
                    for (int x = 0; x < size; x++)
                    {
                        var c = all[src + x];
                        raw[dst++] = c.r;
                        raw[dst++] = c.g;
                        raw[dst++] = c.b;
                        raw[dst++] = c.a;
                    }
                }

                frames[f] = raw;
            }

            return frames;
        }

        /// <summary>Encodes frames as one PNG strip (frame 0 leftmost).</summary>
        public static byte[] EncodeStrip(byte[][] frames)
        {
            int size = TextureTiles.Size;
            int width = size * frames.Length;
            var tex = new Texture2D(width, size, TextureFormat.RGBA32, mipChain: false);
            try
            {
                var px = new Color32[width * size];
                for (int f = 0; f < frames.Length; f++)
                {
                    byte[] raw = frames[f];
                    for (int y = 0; y < size; y++)
                    {
                        int src = y * size * 4;
                        int dst = (y * width) + (f * size);
                        for (int x = 0; x < size; x++)
                        {
                            px[dst + x] = new Color32(raw[src], raw[src + 1], raw[src + 2], raw[src + 3]);
                            src += 4;
                        }
                    }
                }

                tex.SetPixels32(px);
                tex.Apply(false);
                return tex.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        // ---------------------------------------------------------------- meta (animation speeds)

        private static Dictionary<string, int> ReadMeta()
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                string file = Path.Combine(Root, MetaFile);
                if (File.Exists(file))
                {
                    var json = JsonUtility.FromJson<MetaJson>(File.ReadAllText(file));
                    if (json?.textures != null)
                    {
                        foreach (var e in json.textures)
                        {
                            if (e != null && !string.IsNullOrEmpty(e.key))
                            {
                                result[e.key] = e.fps;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[TexturePack] could not read " + MetaFile + ": " + ex.Message);
            }

            return result;
        }

        private static void WriteMeta(Dictionary<string, int> fps)
        {
            var json = new MetaJson();
            foreach (var kv in fps)
            {
                json.textures.Add(new MetaEntry { key = kv.Key, fps = kv.Value });
            }

            json.textures.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            File.WriteAllText(Path.Combine(Root, MetaFile), JsonUtility.ToJson(json, prettyPrint: true));
        }
    }
}
