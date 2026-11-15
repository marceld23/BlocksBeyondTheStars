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
        /// <summary>The sidecar's <see cref="Mode"/> of a normal world (the server's <c>WorldSaveSummary.ModeExplorer</c>).</summary>
        public const string ModeExplorer = "explorer";

        /// <summary>The sidecar's <see cref="Mode"/> of a head-start world — a Creative world option is set
        /// (<c>WorldSaveSummary.ModeCreative</c>).</summary>
        public const string ModeCreative = "creative";

        /// <summary>The sidecar's <see cref="Mode"/> of a sandbox world — its rules run in Creative mode
        /// (<c>WorldSaveSummary.ModeSandbox</c>).</summary>
        public const string ModeSandbox = "sandbox";

        public SavedWorld(string name, DateTime lastWriteUtc, long playtimeSeconds)
            : this(name, lastWriteUtc, playtimeSeconds, string.Empty)
        {
        }

        public SavedWorld(string name, DateTime lastWriteUtc, long playtimeSeconds, string? mode)
        {
            Name = name;
            LastWriteUtc = lastWriteUtc;
            PlaytimeSeconds = playtimeSeconds;
            Mode = mode ?? string.Empty;
        }

        /// <summary>The save folder's name — the world's name.</summary>
        public string Name { get; }

        /// <summary>When the world's <c>world.db</c> was last written: the picker's "newest first" order.</summary>
        public DateTime LastWriteUtc { get; }

        /// <summary>Seconds played, from the <c>world.meta.json</c> sidecar; -1 when the world has no readable sidecar
        /// (a save from before playtime tracking, a locked or broken file) — the picker then shows no figure.</summary>
        public long PlaytimeSeconds { get; }

        /// <summary>The world's game mode from the sidecar (#3664): <see cref="ModeExplorer"/>, <see cref="ModeCreative"/> or
        /// <see cref="ModeSandbox"/>, as the server wrote it; empty when the sidecar names none (a save not played since
        /// #3664) or cannot be read — the picker then shows no mode.</summary>
        public string Mode { get; }

        /// <summary>The locale key naming <see cref="Mode"/> (<see cref="ModeKeyOf"/>); empty when the mode is unknown.</summary>
        public string ModeKey => ModeKeyOf(Mode);

        /// <summary>The locale key naming a sidecar mode (#3664): <c>ui.save.mode_explorer</c>, <c>ui.save.mode_creative</c> or
        /// <c>ui.save.mode_sandbox</c> — the labels of the new-world mode choice; matched case-blind. Empty for an empty or
        /// unknown mode, so the caller shows nothing rather than a raw key.</summary>
        public static string ModeKeyOf(string? mode)
        {
            if (string.Equals(mode, ModeExplorer, StringComparison.OrdinalIgnoreCase))
            {
                return "ui.save.mode_explorer";
            }

            if (string.Equals(mode, ModeCreative, StringComparison.OrdinalIgnoreCase))
            {
                return "ui.save.mode_creative";
            }

            return string.Equals(mode, ModeSandbox, StringComparison.OrdinalIgnoreCase) ? "ui.save.mode_sandbox" : string.Empty;
        }
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

        /// <summary>What a world without a readable sidecar shows: no playtime (-1), no mode.</summary>
        private static readonly (long PlaytimeSeconds, string Mode) NoSidecar = (-1, string.Empty);

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
        /// <see cref="WorldFile"/>, with its timestamp, playtime and mode, newest first.</summary>
        public static IReadOnlyList<SavedWorld> Scan(string savesRoot) => Scan(savesRoot, withPlaytime: true);

        /// <summary>As <see cref="Scan(string)"/>; <paramref name="withPlaytime"/> false skips the sidecars — no playtime,
        /// no mode (callers that only need the names and their order).</summary>
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

                var (played, mode) = withPlaytime ? ReadSidecar(dir) : NoSidecar;
                worlds.Add(new SavedWorld(Path.GetFileName(dir), db.LastWriteTimeUtc, played, mode));
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
        public static long ReadPlaytimeSeconds(string worldDirectory) => ReadSidecar(worldDirectory).PlaytimeSeconds;

        /// <summary>What the picker reads from a world folder's <see cref="SidecarFile"/>, in ONE parse: the playtime in
        /// seconds (-1 when the sidecar has none) and the game mode (#3664: <see cref="SavedWorld.Mode"/>, empty when the
        /// sidecar has none). (-1, "") when there is no sidecar, it is locked or it does not parse.</summary>
        public static (long PlaytimeSeconds, string Mode) ReadSidecar(string worldDirectory)
        {
            try
            {
                string path = Path.Combine(worldDirectory, SidecarFile);
                if (!File.Exists(path))
                {
                    return NoSidecar;
                }

                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return NoSidecar;
                }

                long seconds = -1;
                string mode = string.Empty;
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    // PascalCase from the server (WorldSaveSummary), read case-blind like any sidecar reader should.
                    if (string.Equals(property.Name, "PlaytimeSeconds", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.Number
                        && property.Value.TryGetInt64(out long played))
                    {
                        seconds = played;
                    }
                    else if (string.Equals(property.Name, "Mode", StringComparison.OrdinalIgnoreCase)
                             && property.Value.ValueKind == JsonValueKind.String)
                    {
                        mode = property.Value.GetString() ?? string.Empty;
                    }
                }

                return (seconds, mode);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            {
                return NoSidecar;
            }
        }
    }
}
