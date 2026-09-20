// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Where a texture came from. Later layers win over earlier ones.</summary>
    public enum TextureLayer
    {
        /// <summary>Shipped with the build (<c>Resources/textures</c>).</summary>
        Official = 0,

        /// <summary>The player's own texture pack (<see cref="TexturePackFolder"/>) — "use for me".</summary>
        Local = 1,

        /// <summary>Published by an admin for everyone in this save (#1958). Wins over the local pack.</summary>
        World = 2,
    }

    /// <summary>The pixels of one texture: one or more 64×64 RGBA32 frames (rows bottom-up — the layout
    /// <c>Texture2D.LoadRawTextureData</c> expects and the bundled <c>.bytes</c> tiles use).</summary>
    public sealed class TextureFrames
    {
        public TextureFrames(byte[][] frames, int fps, TextureLayer layer)
        {
            Frames = frames;
            Fps = fps;
            Layer = layer;
        }

        /// <summary>Each entry is <see cref="TextureTiles.BytesPerFrame"/> bytes. Never empty.</summary>
        public byte[][] Frames { get; }

        /// <summary>Animation speed; meaningless for a single frame.</summary>
        public int Fps { get; }

        public TextureLayer Layer { get; }

        public bool Animated => Frames.Length > 1;
    }

    /// <summary>
    /// The single place that resolves a texture key to pixels (#1952). Three layers: the textures shipped with the
    /// build, the player's local texture pack, and the textures an admin published for the current world — the
    /// world wins, then the local pack, then the build. Everything that used to call
    /// <c>Resources.Load("textures/…")</c> asks here instead, so an override reaches blocks, creatures, the avatar
    /// and the menu backdrop alike. Main thread only.
    /// </summary>
    public static class GameTextures
    {
        /// <summary>Resource-name suffix of the extra animation frames of an official texture
        /// (<c>Resources/textures/&lt;key&gt;__anim.bytes</c>, frames 1..n back to back).</summary>
        public const string AnimSuffix = "__anim";

        private static readonly Dictionary<string, TextureFrames> Local = new Dictionary<string, TextureFrames>(StringComparer.Ordinal);
        private static readonly Dictionary<string, TextureFrames> World = new Dictionary<string, TextureFrames>(StringComparer.Ordinal);

        /// <summary>Official animation speeds by key, from the content data (blocks' <c>anim</c> entry).</summary>
        private static readonly Dictionary<string, int> OfficialFps = new Dictionary<string, int>(StringComparer.Ordinal);

        private static bool _useLocalPack = true;
        private static bool _showWorld = true;
        private static volatile BlocksBeyondTheStars.Client.Feedback.TexturePackReportInfo _reportInfo
            = BlocksBeyondTheStars.Client.Feedback.TexturePackReportInfo.None;

        /// <summary>What a report says about the replaced textures (#1964). An immutable snapshot, rebuilt on every
        /// layer change — the crash reporter reads it from the log callback's thread, where the layers themselves
        /// must not be touched.</summary>
        public static BlocksBeyondTheStars.Client.Feedback.TexturePackReportInfo ReportInfo => _reportInfo;

        /// <summary>Raised after a layer changed, with the affected keys (empty = "anything may have changed").
        /// The block atlas repaints those tiles; model builders rebuild their materials.</summary>
        public static event Action<IReadOnlyCollection<string>> Changed;

        /// <summary>Player setting: apply the local texture pack at all.</summary>
        public static bool UseLocalPack
        {
            get => _useLocalPack;
            set
            {
                if (_useLocalPack != value)
                {
                    _useLocalPack = value;
                    Raise(new List<string>(Local.Keys));
                }
            }
        }

        /// <summary>Player setting: show the world's textures (the safety valve against an unsuitable one).</summary>
        public static bool ShowWorldTextures
        {
            get => _showWorld;
            set
            {
                if (_showWorld != value)
                {
                    _showWorld = value;
                    Raise(new List<string>(World.Keys));
                }
            }
        }

        public static int LocalCount => Local.Count;

        public static int WorldCount => World.Count;

        public static IEnumerable<string> LocalKeys => Local.Keys;

        public static IEnumerable<string> WorldKeys => World.Keys;

        /// <summary>The winning frames for <paramref name="key"/>, or null when no layer has the texture (a block
        /// without a bundled tile is then painted in code by the atlas).</summary>
        public static TextureFrames Resolve(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            if (_showWorld && World.TryGetValue(key, out var world))
            {
                return world;
            }

            if (_useLocalPack && Local.TryGetValue(key, out var local))
            {
                return local;
            }

            return Official(key);
        }

        /// <summary>Frame 0 of the winning layer as raw RGBA32 — the drop-in for the old
        /// <c>Resources.Load&lt;TextAsset&gt;("textures/" + key).bytes</c>. Null when the texture does not exist.
        /// The array is shared: callers that modify pixels must clone it first.</summary>
        public static byte[] TileBytes(string key) => Resolve(key)?.Frames[0];

        /// <summary>Which layer currently provides <paramref name="key"/>, or null.</summary>
        public static TextureLayer? LayerOf(string key) => Resolve(key)?.Layer;

        /// <summary>The texture exactly as shipped, ignoring every override — what "reset" and "before / after"
        /// in the texture editor show. Null when the build has no tile for the key.</summary>
        public static TextureFrames Official(string key)
        {
            var asset = Resources.Load<TextAsset>("textures/" + key);
            if (asset == null)
            {
                return null;
            }

            byte[] first = asset.bytes;
            if (first == null || first.Length != TextureTiles.BytesPerFrame)
            {
                return null;
            }

            var frames = new List<byte[]> { first };
            var anim = Resources.Load<TextAsset>("textures/" + key + AnimSuffix);
            byte[] rest = anim != null ? anim.bytes : null;
            if (rest != null && rest.Length > 0 && rest.Length % TextureTiles.BytesPerFrame == 0)
            {
                int extra = Mathf.Min(rest.Length / TextureTiles.BytesPerFrame, TextureTiles.MaxFrames - 1);
                for (int i = 0; i < extra; i++)
                {
                    var frame = new byte[TextureTiles.BytesPerFrame];
                    Buffer.BlockCopy(rest, i * TextureTiles.BytesPerFrame, frame, 0, TextureTiles.BytesPerFrame);
                    frames.Add(frame);
                }
            }

            int fps = OfficialFps.TryGetValue(key, out int f) ? f : 0;
            if (frames.Count > 1 && !TextureTiles.IsValidAnimation(frames.Count, fps))
            {
                fps = 8; // frames without a declared speed still animate, at the middle speed
            }

            return new TextureFrames(frames.ToArray(), fps, TextureLayer.Official);
        }

        /// <summary>Declares the animation speed of an official texture (from content data). No event: this is
        /// set once while the content loads, before any atlas exists.</summary>
        public static void SetOfficialFps(string key, int fps)
        {
            if (!string.IsNullOrEmpty(key) && fps > 0)
            {
                OfficialFps[key] = fps;
            }
        }

        // ---------------------------------------------------------------- local pack

        /// <summary>Replaces the whole local layer (the pack folder was (re)loaded).</summary>
        public static void SetLocalLayer(IDictionary<string, TextureFrames> textures)
        {
            var touched = new HashSet<string>(Local.Keys, StringComparer.Ordinal);
            Local.Clear();
            if (textures != null)
            {
                foreach (var kv in textures)
                {
                    Local[kv.Key] = kv.Value;
                    touched.Add(kv.Key);
                }
            }

            Raise(touched);
        }

        public static void SetLocal(string key, TextureFrames frames)
        {
            if (string.IsNullOrEmpty(key) || frames == null)
            {
                return;
            }

            Local[key] = frames;
            Raise(new[] { key });
        }

        public static void RemoveLocal(string key)
        {
            if (Local.Remove(key))
            {
                Raise(new[] { key });
            }
        }

        public static bool HasLocal(string key) => Local.ContainsKey(key);

        // ---------------------------------------------------------------- world layer

        /// <summary>Applies a batch of world textures at once (the join stream, or one publish / wipe): entries
        /// with null frames remove the key. One event for the whole batch, because the atlas rebuild it triggers
        /// is the expensive part.</summary>
        public static void ApplyWorldBatch(IDictionary<string, TextureFrames> changes)
        {
            if (changes == null || changes.Count == 0)
            {
                return;
            }

            var touched = new List<string>(changes.Count);
            foreach (var kv in changes)
            {
                if (kv.Value == null)
                {
                    if (World.Remove(kv.Key))
                    {
                        touched.Add(kv.Key);
                    }
                }
                else
                {
                    World[kv.Key] = kv.Value;
                    touched.Add(kv.Key);
                }
            }

            if (touched.Count > 0)
            {
                Raise(touched);
            }
        }

        /// <summary>Drops the world layer — the session ended. The atlas is shared with the menu and the editors,
        /// so a world's textures must not outlive the world.</summary>
        public static void ClearWorldLayer()
        {
            if (World.Count == 0)
            {
                return;
            }

            var touched = new List<string>(World.Keys);
            World.Clear();
            Raise(touched);
        }

        public static bool HasWorld(string key) => World.ContainsKey(key);

        // ---------------------------------------------------------------- helpers

        /// <summary>A standalone point-filtered texture of frame 0 — for the model builders that put a tile on a
        /// material of their own (creatures, avatar, robots, the menu planet). The caller owns the texture.</summary>
        public static Texture2D LoadTileTexture(string key, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            byte[] bytes = TileBytes(key);
            if (bytes == null)
            {
                return null;
            }

            var tex = new Texture2D(TextureTiles.Size, TextureTiles.Size, TextureFormat.RGBA32, false)
            {
                wrapMode = wrap,
                filterMode = FilterMode.Point,
            };
            tex.LoadRawTextureData(bytes);
            tex.Apply();
            return tex;
        }

        private static void Raise(IReadOnlyCollection<string> keys)
        {
            _reportInfo = BlocksBeyondTheStars.Client.Feedback.TexturePackReportInfo.Create(_useLocalPack, Local.Keys, _showWorld, World.Keys);
            var handlers = Changed;
            if (handlers == null)
            {
                return;
            }

            // Invoked one by one: a throwing subscriber must not keep the others (the atlas!) from repainting.
            foreach (var handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action<IReadOnlyCollection<string>>)handler)(keys);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[GameTextures] a Changed subscriber threw: " + ex.Message);
                }
            }
        }
    }
}
