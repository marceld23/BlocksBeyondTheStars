// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>
    /// The durable local queue for F1 player feedback whose upload failed (offline, inbox down): the exact
    /// request body is kept on disk and retried on later sessions — with a BOUNDED number of attempts,
    /// unlike the crash spool's retry-forever. Failed attempts are counted in the file name
    /// (<c>…_try2.json</c>; incrementing is an atomic rename, no sidecar state). A report that exhausts
    /// <see cref="MaxAttempts"/> is parked in <c>givenup/</c> — kept, never silently deleted, so a player
    /// can still attach it to a manual report — and an accepted one is moved to <c>sent/</c>. Stores opaque
    /// JSON strings (the same body the uploader posts); Unity-free + best-effort (never throws), so it runs
    /// in the player and in the headless tests.
    /// </summary>
    public sealed class FeedbackSpool
    {
        /// <summary>Automatic retry attempts per queued report (one per session start) before it is parked
        /// in <c>givenup/</c>. The failed live send that queued the report is not counted.</summary>
        public const int MaxAttempts = 5;

        private const string FilePrefix = "feedback_";
        private const string AttemptMarker = "_try";

        private readonly string _directory;
        private readonly object _gate = new object();
        private int _seq;

        /// <param name="directory">Folder for the queue (typically <c>persistentDataPath/feedback</c>).
        /// Empty disables the spool.</param>
        public FeedbackSpool(string directory)
        {
            _directory = directory ?? string.Empty;
        }

        /// <summary>The queue folder (for a UI hint pointing the player at unsent files).</summary>
        public string DirectoryPath => _directory;

        /// <summary>Queues one already-serialized report body under a uniquely-named <c>…_try0.json</c>
        /// file. Returns its path, or null when nothing was written (no directory configured, empty body,
        /// or a write failure — all swallowed).</summary>
        public string? Write(string json, string? timestampStem = null)
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

                string stamp = Sanitize(timestampStem ?? DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                string file = Path.Combine(_directory, $"{FilePrefix}{stamp}_{n:D3}{AttemptMarker}0.json");
                File.WriteAllText(file, json);
                return file;
            }
            catch
            {
                return null; // losing one queued report must never hurt the game
            }
        }

        /// <summary>The queued report files awaiting a retry (best-effort; empty on any error). The
        /// <c>sent/</c> and <c>givenup/</c> subfolders are excluded because the scan is non-recursive.</summary>
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

        /// <summary>Moves an accepted report into <c>sent/</c> so <see cref="ListPending"/> no longer returns
        /// it. Best-effort; on failure the file stays queued and is retried (a harmless duplicate send).</summary>
        public void MarkSent(string path)
        {
            try
            {
                Move(path, "sent");
            }
            catch
            {
                // best-effort
            }
        }

        /// <summary>Counts one failed retry: renames <c>…_tryN</c> to <c>…_tryN+1</c>, or parks the report in
        /// <c>givenup/</c> once it has used its <see cref="MaxAttempts"/> attempts. Returns true while the
        /// report stays queued for another session.</summary>
        public bool RegisterFailedAttempt(string path)
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(path);
                int attempts = 0;
                string stem = name;
                int idx = name.LastIndexOf(AttemptMarker, StringComparison.Ordinal);
                if (idx >= 0 && int.TryParse(name.Substring(idx + AttemptMarker.Length), out int parsed))
                {
                    attempts = parsed;
                    stem = name.Substring(0, idx);
                }

                attempts++;
                if (attempts >= MaxAttempts)
                {
                    Move(path, "givenup");
                    return false;
                }

                string next = Path.Combine(_directory, stem + AttemptMarker + attempts + ".json");
                if (File.Exists(next))
                {
                    File.Delete(next);
                }

                File.Move(path, next);
                return true;
            }
            catch
            {
                return false; // unknown state — promise nothing
            }
        }

        private void Move(string path, string subfolder)
        {
            string dir = Path.Combine(_directory, subfolder);
            Directory.CreateDirectory(dir);
            string target = Path.Combine(dir, Path.GetFileName(path));
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            File.Move(path, target);
        }

        private static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "feedback";
            }

            foreach (var c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
}
