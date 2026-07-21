// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// The durable local queue for client crash reports: each fault is written to disk FIRST (it survives a
    /// crash-on-exit or an offline session, which an HTTP POST can't) and only then sent. A report whose upload
    /// the website accepts is moved into a <c>sent/</c> subfolder, so what remains in the top level is exactly
    /// the retry queue, and a player who declines automatic sending can attach those files to a bug report by
    /// hand. Both folders are capped (<see cref="MaxPending"/>/<see cref="MaxSent"/>, oldest pruned first) so
    /// an install that can never upload doesn't grow the spool forever (#421 M14). Stores opaque JSON strings
    /// (the same body the uploader posts); Unity-free + best effort (never throws), so it runs in the player
    /// and in the headless tests.
    /// </summary>
    public sealed class CrashReportSpool
    {
        /// <summary>Upper bound on queued (unsent) reports; <see cref="Write"/> prunes the oldest beyond it.
        /// Keeps a player who can never reach the inbox (offline installs, and formerly the whole WebGL
        /// population, #421 M14) from growing the spool forever — on WebGL it lives in IndexedDB.</summary>
        public const int MaxPending = 50;

        /// <summary>Upper bound on the <c>sent/</c> archive; <see cref="MarkSent"/> prunes the oldest beyond
        /// it. Sent reports are kept only as a local reference, so a small tail is enough.</summary>
        public const int MaxSent = 50;

        private const string FilePrefix = "crash_";

        private readonly string _directory;
        private readonly object _gate = new object();
        private int _seq;

        /// <param name="directory">Folder for the queue (typically <c>persistentDataPath/crashreports</c>).
        /// Empty disables the spool.</param>
        public CrashReportSpool(string directory)
        {
            _directory = directory ?? string.Empty;
        }

        /// <summary>The queue folder (for a UI hint pointing the player at unsent files).</summary>
        public string DirectoryPath => _directory;

        /// <summary>Writes one report body to a uniquely-named file. Returns its path, or null when nothing was
        /// written (no directory configured, empty body, or a write failure — all swallowed).</summary>
        public string? Write(string json, string timestampStem)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(_directory))
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(_directory);

                int n;
                lock (_gate)
                {
                    n = ++_seq;
                }

                string stem = $"{FilePrefix}{Sanitize(timestampStem)}_{n:D3}";
                string file = Path.Combine(_directory, stem + ".json");

                // Publish atomically: the log callback runs on a background thread while startup's FlushPending
                // enumerates the spool, so a reader must never observe a half-written body (#425 N14). We write
                // the full JSON to a sibling temp file — its ".tmp" tail is excluded from the "crash_*.json"
                // scans, so ListPending/Prune ignore it — then rename it into place, which is atomic on every
                // supported filesystem. Readers therefore only ever see a complete file.
                string temp = Path.Combine(_directory, stem + ".json.tmp");
                File.WriteAllText(temp, json);
                try
                {
                    File.Move(temp, file);
                }
                catch
                {
                    try { File.Delete(temp); } catch { /* best-effort */ }
                    throw;
                }

                Prune(_directory, MaxPending); // file names lead with the UTC timestamp → oldest sorts first
                return file;
            }
            catch
            {
                return null; // a crash reporter must never throw
            }
        }

        /// <summary>The queued report files awaiting upload (best-effort; empty on any error). The <c>sent/</c>
        /// subfolder is excluded because the scan is non-recursive.</summary>
        public IReadOnlyList<string> ListPending()
        {
            if (string.IsNullOrEmpty(_directory) || !Directory.Exists(_directory))
            {
                return Array.Empty<string>();
            }

            try
            {
                return Directory.GetFiles(_directory, FilePrefix + "*.json");
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>How many unsent reports are queued (for a startup hint). Never throws.</summary>
        public int CountPending() => ListPending().Count;

        /// <summary>Reads one queued report's body, or null if it can't be read (locked / mid-write / gone).</summary>
        public string? Read(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Moves an accepted report into <c>sent/</c> so <see cref="ListPending"/> no longer returns it.
        /// Best-effort; on failure the file stays queued and is retried (a harmless duplicate send).</summary>
        public void MarkSent(string path)
        {
            try
            {
                string sentDir = Path.Combine(_directory, "sent");
                Directory.CreateDirectory(sentDir);
                string target = Path.Combine(sentDir, Path.GetFileName(path));
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(path, target);
                Prune(sentDir, MaxSent);
            }
            catch
            {
                // best-effort
            }
        }

        /// <summary>Deletes the oldest report files beyond <paramref name="keep"/> in one folder. Names sort
        /// chronologically (timestamp stem), so ordinal order is age order. Best-effort; never throws — a
        /// pending file deleted while an upload loop holds its path just reads back null and is skipped.</summary>
        private static void Prune(string directory, int keep)
        {
            try
            {
                var files = Directory.GetFiles(directory, FilePrefix + "*.json");
                if (files.Length <= keep)
                {
                    return;
                }

                Array.Sort(files, StringComparer.Ordinal);
                for (int i = 0; i < files.Length - keep; i++)
                {
                    File.Delete(files[i]);
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "crash";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
