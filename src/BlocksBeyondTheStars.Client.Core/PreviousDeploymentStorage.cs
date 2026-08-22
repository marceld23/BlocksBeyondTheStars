// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Recovers files a previous browser deployment left behind (#1177). Unity WebGL keeps
    /// <c>Application.persistentDataPath</c> on one IDBFS mount and hashes the page URL path into the
    /// leaf folder; glitch.fun serves every release from a new content path — so each release starts
    /// with an EMPTY folder while the old one (the singleplayer world, the settings) still sits right
    /// next to it under the same mount. This picks the newest sibling copy of a file and adopts it into
    /// the current folder: never overwriting what the current deployment already has, never deleting
    /// the source. Unity-free so the selection rules are unit-tested; the Unity side only supplies the
    /// storage root (<c>WebGlStorage</c>).
    /// </summary>
    public static class PreviousDeploymentStorage
    {
        /// <summary>The sibling storage root (a directory next to <paramref name="currentRoot"/>) that
        /// holds the NEWEST copy of <paramref name="relativePath"/>, by last-write time; null when no
        /// sibling has one. The current root itself is never a candidate. Unreadable siblings are skipped.</summary>
        public static string? FindNewestSiblingRoot(string currentRoot, string relativePath)
        {
            string fullCurrent;
            string? parent;
            try
            {
                fullCurrent = Path.GetFullPath(currentRoot);
                parent = Path.GetDirectoryName(fullCurrent);
            }
            catch (Exception)
            {
                return null;
            }

            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
            {
                return null;
            }

            IEnumerable<string> siblings;
            try
            {
                siblings = Directory.EnumerateDirectories(parent);
            }
            catch (Exception)
            {
                return null;
            }

            string? best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string dir in siblings)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(dir), fullCurrent, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string candidate = Path.Combine(dir, relativePath);
                    if (!File.Exists(candidate))
                    {
                        continue;
                    }

                    DateTime written = File.GetLastWriteTimeUtc(candidate);
                    if (best == null || written > bestTime)
                    {
                        best = dir;
                        bestTime = written;
                    }
                }
                catch (Exception)
                {
                    // An unreadable sibling is not a reason to give up on the others.
                }
            }

            return best;
        }

        /// <summary>
        /// Copies the newest sibling copy of <paramref name="relativePath"/> into <paramref name="currentRoot"/>
        /// when the current root has none yet; <paramref name="companions"/> (relative paths) come along from
        /// the SAME source root when they exist there and are missing here. Returns the source root it copied
        /// from, or null when nothing was copied (file already present, no candidate, or the copy failed —
        /// a failed copy leaves no partial target behind).
        /// </summary>
        public static string? TryAdopt(string currentRoot, string relativePath, params string[] companions)
        {
            string target = Path.Combine(currentRoot, relativePath);
            try
            {
                if (File.Exists(target))
                {
                    return null;
                }

                string? sourceRoot = FindNewestSiblingRoot(currentRoot, relativePath);
                if (sourceRoot == null)
                {
                    return null;
                }

                CopyInto(sourceRoot, currentRoot, relativePath);
                foreach (string companion in companions)
                {
                    string source = Path.Combine(sourceRoot, companion);
                    if (File.Exists(source) && !File.Exists(Path.Combine(currentRoot, companion)))
                    {
                        try
                        {
                            CopyInto(sourceRoot, currentRoot, companion);
                        }
                        catch (Exception)
                        {
                            // A companion (version meta) is a nice-to-have; the main file is what matters.
                        }
                    }
                }

                return sourceRoot;
            }
            catch (Exception)
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target); // never leave a half-written adoption behind
                    }
                }
                catch (Exception)
                {
                    // best effort
                }

                return null;
            }
        }

        private static void CopyInto(string sourceRoot, string targetRoot, string relativePath)
        {
            string target = Path.Combine(targetRoot, relativePath);
            string? dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.Copy(Path.Combine(sourceRoot, relativePath), target, overwrite: false);
        }
    }
}
