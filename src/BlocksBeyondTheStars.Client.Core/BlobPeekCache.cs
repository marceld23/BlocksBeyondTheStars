// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using System;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Remembers the result of an expensive look into a file (the browser save blob's sole player name, #1322)
    /// for as long as the file is unchanged. The menu asked for that name on EVERY build while the settings
    /// held none — each ask read and gunzipped the whole blob synchronously, a visible hitch once a world's
    /// blob reached a few megabytes (#1368). The stamp is (path, length, last write time): a save rewrites
    /// the blob and bumps both, so a fresh answer follows the next save without any explicit invalidation.
    /// A missing file is never cached — the loader may adopt a blob from an older deployment on that call.
    /// Pure file-system bookkeeping, no Unity — unit-tested headless.
    /// </summary>
    public sealed class BlobPeekCache
    {
        private string? _path;
        private long _length;
        private long _writeTicks;
        private string? _value;
        private bool _valid;

        /// <summary>How many times <see cref="Get"/> had to run the computation (diagnostics / tests).</summary>
        public int Misses { get; private set; }

        /// <summary>The cached value while the file at <paramref name="blobPath"/> still has the stamp it had when
        /// the value was computed; otherwise runs <paramref name="compute"/> and remembers its result.</summary>
        public string? Get(string blobPath, Func<string?> compute)
        {
            if (compute is null)
            {
                throw new ArgumentNullException(nameof(compute));
            }

            bool stamped = TryStamp(blobPath, out long length, out long writeTicks);
            if (_valid && stamped && string.Equals(_path, blobPath, StringComparison.Ordinal)
                && length == _length && writeTicks == _writeTicks)
            {
                return _value;
            }

            Misses++;
            string? value = compute();
            if (stamped)
            {
                _path = blobPath;
                _length = length;
                _writeTicks = writeTicks;
                _value = value;
                _valid = true;
            }
            else
            {
                _valid = false; // no file to stamp → ask again next time (it may appear or be adopted)
            }

            return value;
        }

        /// <summary>Forgets the remembered value (a "New world" reset deletes the blob; harmless otherwise).</summary>
        public void Invalidate() => _valid = false;

        private static bool TryStamp(string path, out long length, out long writeTicks)
        {
            length = 0;
            writeTicks = 0;
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            try
            {
                var info = new FileInfo(path);
                if (!info.Exists)
                {
                    return false;
                }

                length = info.Length;
                writeTicks = info.LastWriteTimeUtc.Ticks;
                return true;
            }
            catch (Exception)
            {
                return false; // unreadable metadata → treat as unstamped (compute, don't cache)
            }
        }
    }
}
