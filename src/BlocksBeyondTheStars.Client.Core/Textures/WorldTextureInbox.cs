// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Textures;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One decoded world texture, ready for the client's texture source.</summary>
    public sealed class WorldTextureEntry
    {
        public WorldTextureEntry(string key, byte[][] frames, int fps, string owner)
        {
            Key = key;
            Frames = frames;
            Fps = fps;
            Owner = owner;
        }

        public string Key { get; }

        public byte[][] Frames { get; }

        public int Fps { get; }

        /// <summary>Display name of the admin who published it (credit + moderation).</summary>
        public string Owner { get; }
    }

    /// <summary>A set of changes to apply AT ONCE — the atlas repaint a change triggers is the expensive part.</summary>
    public sealed class WorldTextureBatch
    {
        /// <summary>True when this is the world's complete list: world textures not named in
        /// <see cref="Changes"/> are gone.</summary>
        public bool Complete { get; set; }

        /// <summary>Key → texture; a null value removes the key (the official texture shows again).</summary>
        public Dictionary<string, WorldTextureEntry?> Changes { get; } = new Dictionary<string, WorldTextureEntry?>(StringComparer.Ordinal);
    }

    /// <summary>
    /// The receiving end of the world texture stream (#1959). The server sends the list in pages, one per tick
    /// (a whole list can exceed the message cap); this collects them and hands out ONE batch when the last page
    /// is in, so a join repaints the atlas once instead of once per page. A single publish or wipe while playing
    /// comes through as a batch of one.
    ///
    /// Nothing the server says is trusted further than the server trusts a client: key, animation and pixel
    /// size are checked again, and the alpha rule is enforced — a modified server cannot make terrain
    /// see-through. An entry that fails is skipped; the rest of the list still applies.
    /// </summary>
    public sealed class WorldTextureInbox
    {
        private WorldTextureBatch? _pending;

        // Keys changed by a single message WHILE a list streams. The pages were cut when the list was queued, so
        // a later page may still carry the older state of such a key — the single message is the newer truth.
        private readonly HashSet<string> _changedWhileStreaming = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Number of entries that were skipped because they did not decode (diagnostics).</summary>
        public int Rejected { get; private set; }

        /// <summary>Feeds one page; returns the batch when the page was the last one, else null.</summary>
        public WorldTextureBatch? Accept(WorldTextureList? page)
        {
            if (page == null)
            {
                return null;
            }

            if (page.Reset || _pending == null)
            {
                // A page without a preceding Reset (lost first page, older server) still yields a usable,
                // merely incremental, batch.
                _pending = new WorldTextureBatch { Complete = page.Reset };
                _changedWhileStreaming.Clear();
            }

            foreach (var texture in page.Textures ?? Array.Empty<NetWorldTexture>())
            {
                if (texture == null || !_changedWhileStreaming.Contains(texture.Key ?? string.Empty))
                {
                    Add(_pending, texture);
                }
            }

            if (!page.Final)
            {
                return null;
            }

            var done = _pending;
            _pending = null;
            _changedWhileStreaming.Clear();
            return done;
        }

        /// <summary>One texture published or wiped while playing.</summary>
        public WorldTextureBatch? Accept(WorldTextureData? single)
        {
            if (single?.Texture == null)
            {
                return null;
            }

            var batch = new WorldTextureBatch();
            Add(batch, single.Texture);
            if (_pending != null && batch.Changes.Count > 0)
            {
                Add(_pending, single.Texture); // a list is still streaming: the newer state must win there too
                _changedWhileStreaming.Add(single.Texture.Key);
            }

            return batch.Changes.Count > 0 ? batch : null;
        }

        /// <summary>Forgets a half-received list (the connection ended).</summary>
        public void Reset()
        {
            _pending = null;
            _changedWhileStreaming.Clear();
        }

        private void Add(WorldTextureBatch batch, NetWorldTexture? texture)
        {
            if (texture == null || !TextureTiles.IsValidKey(texture.Key))
            {
                Rejected++;
                return;
            }

            if (string.IsNullOrEmpty(texture.Data))
            {
                batch.Changes[texture.Key] = null; // wiped
                return;
            }

            if (!TextureTiles.IsValidAnimation(texture.Frames, texture.Fps)
                || !WorldTextureCodec.TryDecode(texture.Data, texture.Frames, out byte[][] frames))
            {
                Rejected++;
                return;
            }

            TextureAlphaMode mode = TextureTiles.AlphaModeOf(texture.Key);
            foreach (byte[] frame in frames)
            {
                TextureTiles.EnforceAlpha(frame, mode);
            }

            batch.Changes[texture.Key] = new WorldTextureEntry(texture.Key, frames, frames.Length > 1 ? texture.Fps : 0, texture.Owner ?? string.Empty);
        }
    }
}
