// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The optional portable-data redirect (#1285). A file named <see cref="MarkerFileName"/> next to the game
    /// executable moves the whole persistent-data root (settings, token, singleplayer saves, user content,
    /// exports, spools, photos …) away from Unity's per-user folder — so a copy on a USB stick keeps its data
    /// with it. Rules: one directory per file, absolute or relative to the executable's folder; environment
    /// variables (<c>%LOCALAPPDATA%</c>-style) are expanded; blank lines and <c>#</c> comments are ignored; an
    /// empty marker means <c>&lt;exe folder&gt;/<see cref="DefaultSubfolder"/></c>. Unity-free so the parsing
    /// rules are unit-tested; the Unity side (<c>AppPaths</c>) supplies the executable folder and the fallback.
    /// </summary>
    public static class PortableDataDir
    {
        /// <summary>The marker file a player drops next to the executable.</summary>
        public const string MarkerFileName = "portable_data_dir.txt";

        /// <summary>Folder used when the marker exists but names no directory.</summary>
        public const string DefaultSubfolder = "userdata";

        /// <summary>
        /// The redirected data root for an executable living in <paramref name="exeDir"/>, or null when no
        /// marker file is present there (= keep the platform default). Never throws: an unreadable marker is
        /// reported through <paramref name="error"/> and yields null.
        /// </summary>
        public static string? ResolveFromMarker(string exeDir, out string? error)
        {
            error = null;
            string marker;
            try
            {
                marker = Path.Combine(exeDir, MarkerFileName);
                if (!File.Exists(marker))
                {
                    return null;
                }
            }
            catch (Exception e)
            {
                error = $"Could not look for '{MarkerFileName}' in '{exeDir}': {e.Message}";
                return null;
            }

            string content;
            try
            {
                content = File.ReadAllText(marker);
            }
            catch (Exception e)
            {
                error = $"Could not read '{marker}': {e.Message}";
                return null;
            }

            try
            {
                return Resolve(exeDir, content);
            }
            catch (Exception e)
            {
                error = $"'{marker}' does not name a usable directory: {e.Message}";
                return null;
            }
        }

        /// <summary>
        /// Pure resolution of a marker's text: the first non-blank, non-comment line is the directory
        /// (relative paths are anchored at <paramref name="exeDir"/>, environment variables expanded); no such
        /// line means <see cref="DefaultSubfolder"/> under <paramref name="exeDir"/>. Returns a full, normalised
        /// path. Throws on syntactically invalid paths.
        /// </summary>
        public static string Resolve(string exeDir, string? markerContent)
        {
            string? line = FirstDirective(markerContent);
            string target = string.IsNullOrEmpty(line)
                ? Path.Combine(exeDir, DefaultSubfolder)
                : Environment.ExpandEnvironmentVariables(line!);

            // A quoted path ("C:\My Games\BBTS") is a natural thing to type; accept it.
            target = target.Trim().Trim('"');
            if (target.Length == 0)
            {
                target = Path.Combine(exeDir, DefaultSubfolder);
            }

            if (!Path.IsPathRooted(target))
            {
                target = Path.Combine(exeDir, target);
            }

            return Path.GetFullPath(target);
        }

        /// <summary>
        /// Makes sure <paramref name="dir"/> exists and is writable (a short-lived probe file is created and
        /// removed). False with a message when it is not — the caller then falls back to the default root.
        /// </summary>
        public static bool TryPrepare(string dir, out string? error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(dir);
                string probe = Path.Combine(dir, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception e)
            {
                error = $"Portable data folder '{dir}' is not writable: {e.Message}";
                return false;
            }
        }

        private static string? FirstDirective(string? content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return null;
            }

            foreach (string raw in content!.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF').Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                return line;
            }

            return null;
        }
    }
}
