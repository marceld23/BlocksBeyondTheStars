// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.Shared.Textures;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// "Calm when saving" is baked into the saved pixels, and the painting as painted
    /// is kept on the player's own machine so the value can be changed later without calming twice.
    /// <para>
    /// Where the original lives: while the work is unsaved, in the editor's draft (<c>TextureEditor.Document.cs</c>: the draft
    /// holds the model's frames, which are always the unmodified painting, plus a <c>calm</c> field — an older draft without
    /// the field reads 0, an older client ignores the field). The shell deletes the draft on Save, so on Save and on
    /// "For this world" the editor also writes the painting and its value to <c>&lt;data&gt;/texture_originals/&lt;key&gt;.json</c>
    /// — outside the texture pack folder (the pack loader never sees it), never sent anywhere. Opening a texture looks there:
    /// when the pixels the game shows are exactly that painting calmed by that value (<see cref="TileFilters.SameShown"/>),
    /// the canvas gets the painting and the value back; anything else (a pack file replaced by hand, another world's
    /// texture) leaves the record unused. A value of 0 removes the record.
    /// </para>
    /// </summary>
    public sealed partial class TextureEditor
    {
        [Serializable]
        private sealed class CalmOriginal
        {
            public string key;
            public int calm;
            public int fps;
            public string[] frames;
        }

        private static string OriginalsRoot => Path.Combine(AppPaths.Root, "texture_originals");

        /// <summary>What Save, publish, export, submit and the share code hand on: the painting calmed by "calm when saving"
        /// (an icon: as painted).</summary>
        private byte[][] SavedFrames()
            => _entry != null && !_entry.IsIcon ? _model.SavedFrames() : _model.CopyFrames();

        /// <summary>After a save or a publish: keeps the painting and its value for the next opening (value 0: forgets it).</summary>
        private void RememberCalmOriginal()
        {
            if (_entry == null || _entry.IsIcon || !TextureTiles.IsValidKey(_entry.Key))
            {
                return;
            }

            string file = Path.Combine(OriginalsRoot, _entry.Key + ".json");
            try
            {
                if (_model.SaveCalm <= 0)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        WebGlStorage.Sync();
                    }

                    return;
                }

                var frames = _model.CopyFrames();
                var kept = new CalmOriginal { key = _entry.Key, calm = _model.SaveCalm, fps = _model.Fps, frames = new string[frames.Length] };
                for (int i = 0; i < frames.Length; i++)
                {
                    kept.frames[i] = Convert.ToBase64String(frames[i]);
                }

                Directory.CreateDirectory(OriginalsRoot);
                File.WriteAllText(file, JsonUtility.ToJson(kept));
                WebGlStorage.Sync(); // in the browser a write lives in memory until synced (#1179)
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextureEditor] The painting of '{_entry.Key}' could not be kept: {e.Message}");
            }
        }

        /// <summary>On opening: when the shown texture is a painting this player saved calmed, the canvas gets the painting
        /// as painted and its "calm when saving" value back (still the saved state: nothing is unsaved).</summary>
        private void RestoreCalmOriginal(TextureEntry entry, TextureFrames current, TextureAlphaMode mode)
        {
            if (entry == null || entry.IsIcon || current == null || current.Layer == TextureLayer.Official
                || !TextureTiles.IsValidKey(entry.Key))
            {
                return;
            }

            var kept = ReadCalmOriginal(entry.Key);
            var frames = kept != null ? DecodeFrames(kept.frames) : null;
            if (frames == null || frames.Length != current.Frames.Length || kept.calm <= 0)
            {
                return;
            }

            for (int i = 0; i < frames.Length; i++)
            {
                var shown = TileFilters.Calm(frames[i], Mathf.Clamp(kept.calm, 0, 100) / 100.0);
                TextureTiles.EnforceAlpha(shown, mode);
                if (!TileFilters.SameShown(shown, current.Frames[i]))
                {
                    return; // the game shows something else now
                }
            }

            _model.LoadWithCalm(frames, kept.fps, mode, kept.calm);
        }

        private static CalmOriginal ReadCalmOriginal(string key)
        {
            try
            {
                string file = Path.Combine(OriginalsRoot, key + ".json");
                if (!File.Exists(file))
                {
                    return null;
                }

                var kept = JsonUtility.FromJson<CalmOriginal>(File.ReadAllText(file));
                return kept != null && kept.key == key ? kept : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[TextureEditor] The kept painting of '{key}' could not be read: {e.Message}");
                return null;
            }
        }
    }
}
