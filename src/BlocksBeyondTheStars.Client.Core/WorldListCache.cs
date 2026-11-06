// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One singleplayer save as the world picker lists it (#3259).</summary>
    public sealed class SavedWorld
    {
        public SavedWorld(string name, DateTime lastWriteUtc, long playtimeSeconds)
        {
            Name = name;
            LastWriteUtc = lastWriteUtc;
            PlaytimeSeconds = playtimeSeconds;
        }

        /// <summary>The save folder's name — the world's name.</summary>
        public string Name { get; }

        /// <summary>When the world's <c>world.db</c> was last written: the picker's "newest first" order.</summary>
        public DateTime LastWriteUtc { get; }

        /// <summary>Seconds played, from the <c>world.meta.json</c> sidecar; -1 when the world has no readable sidecar
        /// (a save from before playtime tracking, a locked or broken file) — the picker then shows no figure.</summary>
        public long PlaytimeSeconds { get; }
    }

    /// <summary>
    /// The singleplayer world list, read off the main thread (#3259). Clicking "Singleplayer" used to scan the saves
    /// folder inside the frame that built the picker: a sort whose comparator stat-ed <c>world.db</c> twice per comparison
    /// (4 544 stats for 264 worlds), then every world's sidecar — 0.3 s with a warm file cache and 5.8 s on the first,
    /// cold access after a game start, during which the menu was frozen. This cache scans once on a background task as
    /// soon as the main menu shows, with ONE timestamp per world, so by the time the player clicks the list is usually
    /// already in memory. It is dropped when a world is created, played or deleted.
    /// <para>Pure System.IO (no Unity): the picker, the launcher's <c>ListWorlds</c> and the headless tests share it.</para>
    /// </summary>
    public sealed class WorldListCache
    {
        /// <summary>The file that makes a folder a world.</summary>
        public const string WorldFile = "world.db";

        /// <summary>The headline-stats sidecar the server writes beside <see cref="WorldFile"/> (WorldSaveSummary).</summary>
        public const string SidecarFile = "world.meta.json";

        private readonly object _gate = new object();
        private readonly Func<string, IReadOnlyList<SavedWorld>> _scan;
        private IReadOnlyList<SavedWorld>? _worlds;
        private bool _running;
        private int _generation;

        /// <param name="savesRoot">The folder holding one subfolder per world.</param>
        /// <param name="scan">The scan to run in the background — <see cref="Scan(string)"/> unless a test steps in.</param>
        public WorldListCache(string savesRoot, Func<string, IReadOnlyList<SavedWorld>>? scan = null)
        {
            SavesRoot = savesRoot;
            _scan = scan ?? Scan;
        }

        /// <summary>The folder this cache lists.</summary>
        public string SavesRoot { get; }

        /// <summary>True once a list is in memory (<see cref="TryGet"/> answers without waiting).</summary>
        public bool IsLoaded
        {
            get
            {
                lock (_gate)
                {
                    return _worlds != null;
                }
            }
        }

        /// <summary>Starts the background scan unless one is already running or its list is still valid — cheap to call
        /// every time the main menu shows.</summary>
        public void Prefetch()
        {
            int generation;
            lock (_gate)
            {
                if (_worlds != null || _running)
                {
                    return;
                }

                _running = true;
                generation = _generation;
            }

            string root = SavesRoot;
            _ = Task.Run(() => RunScan(root, generation));
        }

        /// <summary>The list, newest first — or null while the scan still runs (one is started when none is).</summary>
        public IReadOnlyList<SavedWorld>? TryGet()
        {
            lock (_gate)
            {
                if (_worlds != null)
                {
                    return _worlds;
                }
            }

            Prefetch();
            return null;
        }

        /// <summary>Drops the list: the next <see cref="Prefetch"/> or <see cref="TryGet"/> scans again. A scan still
        /// running finishes unseen — what it read may already be stale.</summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _generation++;
                _worlds = null;
                _running = false;
            }
        }

        /// <summary>A world was deleted: a list in memory simply loses it, so the picker rebuilt right after the delete
        /// shows at once instead of scanning every world again. A scan still running is restarted instead.</summary>
        public void Forget(string name)
        {
            lock (_gate)
            {
                if (_worlds == null)
                {
                    if (_running)
                    {
                        _generation++;
                        _running = false;
                    }

                    return;
                }

                var kept = new List<SavedWorld>(_worlds.Count);
                foreach (var world in _worlds)
                {
                    if (!string.Equals(world.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        kept.Add(world);
                    }
                }

                _worlds = kept;
            }
        }

        private void RunScan(string root, int generation)
        {
            IReadOnlyList<SavedWorld> found;
            try
            {
                found = _scan(root);
            }
#pragma warning disable CA1031 // a background scan must never take the menu down — an unreadable folder lists as empty
            catch (Exception)
#pragma warning restore CA1031
            {
                found = Array.Empty<SavedWorld>();
            }

            lock (_gate)
            {
                if (generation == _generation)
                {
                    _worlds = found;
                    _running = false;
                }
            }
        }

        /// <summary>Scans <paramref name="savesRoot"/> now, on the calling thread: every subfolder holding a
        /// <see cref="WorldFile"/>, with its timestamp and playtime, newest first.</summary>
        public static IReadOnlyList<SavedWorld> Scan(string savesRoot) => Scan(savesRoot, withPlaytime: true);

        /// <summary>As <see cref="Scan(string)"/>; <paramref name="withPlaytime"/> false skips the sidecars (callers that
        /// only need the names and their order).</summary>
        public static IReadOnlyList<SavedWorld> Scan(string savesRoot, bool withPlaytime)
        {
            var worlds = new List<SavedWorld>();
            string[] dirs;
            try
            {
                if (string.IsNullOrEmpty(savesRoot) || !Directory.Exists(savesRoot))
                {
                    return worlds;
                }

                dirs = Directory.GetDirectories(savesRoot);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return worlds;
            }

            foreach (string dir in dirs)
            {
                // ONE stat per world: FileInfo answers "exists" and "last written" from the same call. The old
                // comparator asked the file system twice per comparison of the sort.
                var db = new FileInfo(Path.Combine(dir, WorldFile));
                if (!db.Exists)
                {
                    continue;
                }

                long played = withPlaytime ? ReadPlaytimeSeconds(dir) : -1;
                worlds.Add(new SavedWorld(Path.GetFileName(dir), db.LastWriteTimeUtc, played));
            }

            worlds.Sort(NewestFirst);
            return worlds;
        }

        /// <summary>The picker's order: newest <c>world.db</c> first; equal timestamps by name, so two scans of the same
        /// folder always agree.</summary>
        public static int NewestFirst(SavedWorld a, SavedWorld b)
        {
            int byTime = b.LastWriteUtc.CompareTo(a.LastWriteUtc);
            return byTime != 0 ? byTime : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The playtime (seconds) in a world folder's <see cref="SidecarFile"/>; -1 when there is none, it is
        /// locked or it does not parse.</summary>
        public static long ReadPlaytimeSeconds(string worldDirectory)
        {
            try
            {
                string path = Path.Combine(worldDirectory, SidecarFile);
                if (!File.Exists(path))
                {
                    return -1;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return -1;
                }

                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    // PascalCase from the server (WorldSaveSummary), read case-blind like any sidecar reader should.
                    if (string.Equals(property.Name, "PlaytimeSeconds", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt64(out long seconds))
                    {
                        return seconds;
                    }
                }

                return -1;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return -1;
            }
        }
    }
}
