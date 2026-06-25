// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Local, per-world store for camera photos (item: <c>camera</c>). Photos are JPGs written under
    /// <c>&lt;persistentDataPath&gt;/Photos/&lt;worldKey&gt;/</c> (on Windows that is
    /// <c>%AppData%\..\LocalLow\&lt;company&gt;\&lt;product&gt;\Photos\…</c>), with a small <c>index.json</c>
    /// sidecar carrying each photo's capture time and an editable note. Purely client-side — no server
    /// involvement — so it works the same in singleplayer and multiplayer; worlds are kept apart by the
    /// world seed. Textures are loaded lazily and cached so the gallery can show thumbnails without
    /// re-reading files every frame.
    /// </summary>
    public sealed class PhotoStore
    {
        /// <summary>One stored photo: the file name (within the world folder), its capture time and a note.</summary>
        public sealed class Entry
        {
            public string File = string.Empty;       // file name only, e.g. photo_20260619_143501.jpg
            public long TakenUtcTicks;                // DateTime.UtcNow.Ticks at capture
            public string Note = string.Empty;        // free-text caption, edited in the gallery
        }

        // JsonUtility can only (de)serialize a class with fields, not a bare List<>, so wrap it.
        [Serializable]
        private sealed class IndexFile
        {
            public List<Entry> Entries = new();
        }

        private readonly string _dir;
        private readonly string _indexPath;
        private readonly List<Entry> _entries = new();
        private readonly Dictionary<string, Texture2D> _textures = new();

        /// <summary>The root for ALL worlds' photos (one folder per world below this).</summary>
        public static string Root => Path.Combine(Application.persistentDataPath, "Photos");

        /// <summary>The folder this store reads/writes (the per-world directory).</summary>
        public string Directory => _dir;

        /// <summary>Photos newest-first (capture time descending).</summary>
        public IReadOnlyList<Entry> Entries => _entries;

        private PhotoStore(string dir)
        {
            _dir = dir;
            _indexPath = Path.Combine(_dir, "index.json");
        }

        /// <summary>Maps a world seed to a stable, filesystem-safe folder name.</summary>
        public static string WorldKey(long worldSeed) => "world_" + worldSeed.ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Opens (and loads) the photo store for the given world. Creates the folder lazily on first write.</summary>
        public static PhotoStore Open(long worldSeed)
        {
            var store = new PhotoStore(Path.Combine(Root, WorldKey(worldSeed)));
            store.Reload();
            return store;
        }

        /// <summary>Re-reads the index from disk and reconciles it with the actual JPGs present (so files
        /// deleted or added outside the game don't desync the gallery).</summary>
        public void Reload()
        {
            _entries.Clear();
            var known = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                if (File.Exists(_indexPath))
                {
                    var idx = JsonUtility.FromJson<IndexFile>(File.ReadAllText(_indexPath));
                    if (idx?.Entries != null)
                    {
                        foreach (var e in idx.Entries)
                        {
                            if (e != null && !string.IsNullOrEmpty(e.File))
                            {
                                known[e.File] = e;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not read index: {ex.Message}");
            }

            // Source of truth = the JPGs on disk. Carry over notes/times from the index where present;
            // synthesize a sensible time from the file for any orphan photo.
            try
            {
                if (System.IO.Directory.Exists(_dir))
                {
                    foreach (var path in System.IO.Directory.GetFiles(_dir, "*.jpg"))
                    {
                        string name = Path.GetFileName(path);
                        if (known.TryGetValue(name, out var e))
                        {
                            _entries.Add(e);
                        }
                        else
                        {
                            _entries.Add(new Entry { File = name, TakenUtcTicks = File.GetCreationTimeUtc(path).Ticks });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not scan folder: {ex.Message}");
            }

            _entries.Sort((a, b) => b.TakenUtcTicks.CompareTo(a.TakenUtcTicks)); // newest first
        }

        /// <summary>Writes a freshly captured JPG and records it in the index. Returns the new entry (or null
        /// on failure). <paramref name="takenUtc"/> is also used to name the file.</summary>
        public Entry Add(byte[] jpg, DateTime takenUtc)
        {
            if (jpg == null || jpg.Length == 0)
            {
                return null;
            }

            try
            {
                System.IO.Directory.CreateDirectory(_dir);
                string name = "photo_" + takenUtc.ToString("yyyyMMdd_HHmmss") + ".jpg";
                // Avoid clobbering if two shots land in the same second.
                int n = 2;
                while (File.Exists(Path.Combine(_dir, name)))
                {
                    name = "photo_" + takenUtc.ToString("yyyyMMdd_HHmmss") + "_" + n++ + ".jpg";
                }

                File.WriteAllBytes(Path.Combine(_dir, name), jpg);
                var e = new Entry { File = name, TakenUtcTicks = takenUtc.Ticks, Note = string.Empty };
                _entries.Insert(0, e); // newest first
                WriteIndex();
                return e;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not save photo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Sets (and persists) the note for a photo.</summary>
        public void SetNote(string file, string note)
        {
            var e = _entries.FirstOrDefault(x => x.File == file);
            if (e == null)
            {
                return;
            }

            e.Note = note ?? string.Empty;
            WriteIndex();
        }

        /// <summary>Deletes a photo (file + index entry + cached texture).</summary>
        public void Delete(string file)
        {
            try
            {
                string path = Path.Combine(_dir, file);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not delete photo: {ex.Message}");
            }

            _entries.RemoveAll(x => x.File == file);
            if (_textures.TryGetValue(file, out var tex))
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }

                _textures.Remove(file);
            }

            WriteIndex();
        }

        /// <summary>Lazily loads (and caches) the texture for a photo, or null if it can't be read.</summary>
        public Texture2D GetTexture(string file)
        {
            if (_textures.TryGetValue(file, out var cached) && cached != null)
            {
                return cached;
            }

            try
            {
                string path = Path.Combine(_dir, file);
                if (!File.Exists(path))
                {
                    return null;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGB24, mipChain: false);
                if (tex.LoadImage(File.ReadAllBytes(path)))
                {
                    tex.wrapMode = TextureWrapMode.Clamp;
                    _textures[file] = tex;
                    return tex;
                }

                UnityEngine.Object.Destroy(tex);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not load photo: {ex.Message}");
            }

            return null;
        }

        /// <summary>Frees all cached textures (call when the gallery closes to release memory).</summary>
        public void UnloadTextures()
        {
            foreach (var tex in _textures.Values)
            {
                if (tex != null)
                {
                    UnityEngine.Object.Destroy(tex);
                }
            }

            _textures.Clear();
        }

        private void WriteIndex()
        {
            try
            {
                System.IO.Directory.CreateDirectory(_dir);
                File.WriteAllText(_indexPath, JsonUtility.ToJson(new IndexFile { Entries = _entries }, prettyPrint: true));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhotoStore] could not write index: {ex.Message}");
            }
        }
    }
}
