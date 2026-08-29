// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlocksBeyondTheStars.Client.Feedback
{
    /// <summary>One report this install sent successfully (the inbox's id, the title the player typed,
    /// and when).</summary>
    public sealed class SentReport
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public long SentUnix { get; set; }
    }

    /// <summary>
    /// The client's own memory of which F1 reports it sent (#1328): <c>feedback/sent.json</c> under the
    /// data root. Its one job is to decide whether polling the inbox for developer replies is warranted at
    /// all — an install that never sent feedback (or whose reports are older than <see cref="MaxAgeDays"/>)
    /// causes zero requests, which keeps the "nothing phones home without a reason" rule intact. Best-effort
    /// like the spool: never throws, a lost file merely means no polling until the next report.
    /// </summary>
    public sealed class SentReportsLog
    {
        /// <summary>Reports older than this are forgotten on load: a reply that late is not expected, and the
        /// poll would otherwise run forever for one report from years ago.</summary>
        public const int MaxAgeDays = 90;

        /// <summary>Upper bound on remembered reports (oldest are dropped first).</summary>
        public const int MaxEntries = 50;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };

        private readonly string _path;
        private readonly object _gate = new object();
        private List<SentReport>? _entries;

        /// <param name="path">The JSON file (typically <c>&lt;data root&gt;/feedback/sent.json</c>); empty disables the log.</param>
        public SentReportsLog(string path)
        {
            _path = path ?? string.Empty;
        }

        public string Path => _path;

        /// <summary>The remembered reports, newest last — loaded on first use and pruned to
        /// <see cref="MaxAgeDays"/> relative to <paramref name="nowUnix"/>.</summary>
        public IReadOnlyList<SentReport> List(long nowUnix)
        {
            lock (_gate)
            {
                EnsureLoaded();
                Prune(nowUnix);
                return _entries!.ToArray();
            }
        }

        /// <summary>True when at least one report is young enough to still expect an answer for — the only
        /// condition under which the client polls the inbox.</summary>
        public bool ShouldPoll(long nowUnix) => List(nowUnix).Count > 0;

        /// <summary>Remembers a successfully uploaded report. Returns false when nothing was recorded (no path,
        /// no id, or the write failed — all swallowed).</summary>
        public bool Record(string reportId, string title, long nowUnix)
        {
            if (string.IsNullOrEmpty(_path) || string.IsNullOrWhiteSpace(reportId))
            {
                return false;
            }

            lock (_gate)
            {
                EnsureLoaded();
                _entries!.RemoveAll(e => e.Id == reportId);
                _entries.Add(new SentReport { Id = reportId, Title = title ?? string.Empty, SentUnix = nowUnix });
                Prune(nowUnix);
                return Save();
            }
        }

        /// <summary>Forgets one report (e.g. after the inbox answered 404 for it).</summary>
        public void Forget(string reportId)
        {
            if (string.IsNullOrEmpty(_path))
            {
                return;
            }

            lock (_gate)
            {
                EnsureLoaded();
                if (_entries!.RemoveAll(e => e.Id == reportId) > 0)
                {
                    Save();
                }
            }
        }

        private void EnsureLoaded()
        {
            if (_entries != null)
            {
                return;
            }

            _entries = new List<SentReport>();
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path))
            {
                return;
            }

            try
            {
                var loaded = JsonSerializer.Deserialize<List<SentReport>>(File.ReadAllText(_path), JsonOptions);
                if (loaded != null)
                {
                    foreach (var e in loaded)
                    {
                        if (e != null && !string.IsNullOrEmpty(e.Id))
                        {
                            _entries.Add(e);
                        }
                    }
                }
            }
            catch
            {
                // a corrupt file just means "no memory" — the next successful report rewrites it
            }
        }

        private void Prune(long nowUnix)
        {
            long cutoff = nowUnix - MaxAgeDays * 86400L;
            _entries!.RemoveAll(e => e.SentUnix < cutoff);
            _entries.Sort((a, b) => a.SentUnix.CompareTo(b.SentUnix));
            while (_entries.Count > MaxEntries)
            {
                _entries.RemoveAt(0);
            }
        }

        private bool Save()
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(_path) ?? string.Empty;
                if (dir.Length > 0)
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(_path, JsonSerializer.Serialize(_entries, JsonOptions));
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
