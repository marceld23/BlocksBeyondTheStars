// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The optional portable-data redirect (#1285). A file named <see cref="MarkerFileName"/> next to the game
    /// executable moves the whole persistent-data root (settings, token, singleplayer saves, user content,
    /// exports, spools, photos …) away from Unity's per-user folder — so a copy on a USB stick keeps its data
    /// with it. Rules: one directory per file, absolute or relative to the executable's folder; environment
    /// variables (<c>%LOCALAPPDATA%</c>-style) are expanded; blank lines and <c>#</c> comments are ignored; an
    /// empty marker means <c>&lt;exe folder&gt;/<see cref="DefaultSubfolder"/></c>. Besides the directory the file
    /// may carry option lines <c>key=value</c> for the keys in <see cref="OptionKeys"/> (today only
    /// <see cref="PortKey"/>, the bundled server's port — see <see cref="LaunchOptions"/>); any other line is the
    /// directory as before, so a path that happens to contain '=' still works. Unity-free so the parsing rules
    /// are unit-tested; the Unity side (<c>AppPaths</c>) supplies the executable folder and the fallback.
    /// </summary>
    public static class PortableDataDir
    {
        /// <summary>The marker file a player drops next to the executable.</summary>
        public const string MarkerFileName = "portable_data_dir.txt";

        /// <summary>Folder used when the marker exists but names no directory.</summary>
        public const string DefaultSubfolder = "userdata";

        /// <summary>The option line naming the port of the bundled server (<c>port=31600</c>).</summary>
        public const string PortKey = "port";

        /// <summary>The keys an option line may carry. A line whose part before '=' is none of these is a directory.</summary>
        private static readonly string[] OptionKeys = { PortKey };

        /// <summary>
        /// The redirected data root for an executable living in <paramref name="exeDir"/>, or null when no
        /// marker file is present there (= keep the platform default). Never throws: an unreadable marker is
        /// reported through <paramref name="error"/> and yields null.
        /// </summary>
        public static string? ResolveFromMarker(string exeDir, out string? error)
        {
            string? content = ReadMarker(exeDir, out error);
            if (content == null)
            {
                return null;
            }

            try
            {
                return Resolve(exeDir, content);
            }
            catch (Exception e)
            {
                error = $"'{Path.Combine(exeDir, MarkerFileName)}' does not name a usable directory: {e.Message}";
                return null;
            }
        }

        /// <summary>
        /// The marker's text, or null when there is no marker next to the executable in <paramref name="exeDir"/>.
        /// Never throws: a marker that cannot be read is reported through <paramref name="error"/> and yields null.
        /// </summary>
        public static string? ReadMarker(string exeDir, out string? error)
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

            try
            {
                return File.ReadAllText(marker);
            }
            catch (Exception e)
            {
                error = $"Could not read '{marker}': {e.Message}";
                return null;
            }
        }

        /// <summary>
        /// Pure resolution of a marker's text: the first non-blank, non-comment line that is no option line is the
        /// directory (relative paths are anchored at <paramref name="exeDir"/>, environment variables expanded); no
        /// such line means <see cref="DefaultSubfolder"/> under <paramref name="exeDir"/>. Returns a full, normalised
        /// path. Throws on syntactically invalid paths.
        /// </summary>
        public static string Resolve(string exeDir, string? markerContent)
            => ResolveDirectory(exeDir, FirstDirective(markerContent));

        /// <summary>
        /// One directory as a player writes it — in the marker or after <c>-dataDir</c> on the command line: quotes
        /// stripped, environment variables expanded, a relative path anchored at <paramref name="exeDir"/>, empty =
        /// <see cref="DefaultSubfolder"/> next to the executable. Returns a full, normalised path; throws on a
        /// syntactically invalid one.
        /// </summary>
        public static string ResolveDirectory(string exeDir, string? text)
        {
            string target = string.IsNullOrEmpty(text)
                ? Path.Combine(exeDir, DefaultSubfolder)
                : Environment.ExpandEnvironmentVariables(text!);

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

        /// <summary>The value of the option line <paramref name="key"/> in a marker's text (the first one wins), or null
        /// when the marker has none. The value is trimmed; it may be empty.</summary>
        public static string? OptionValue(string? markerContent, string key)
        {
            foreach (string line in Directives(markerContent))
            {
                if (TrySplitOption(line, out string lineKey, out string value)
                    && string.Equals(lineKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
            }

            return null;
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

        /// <summary>The first line that is neither blank, a comment nor an option line \u2014 the directory.</summary>
        private static string? FirstDirective(string? content)
        {
            foreach (string line in Directives(content))
            {
                if (!TrySplitOption(line, out _, out _))
                {
                    return line;
                }
            }

            return null;
        }

        /// <summary>The marker's lines that say something: trimmed, byte-order mark removed, blank lines and <c>#</c>
        /// comments skipped.</summary>
        private static IEnumerable<string> Directives(string? content)
        {
            if (string.IsNullOrEmpty(content))
            {
                yield break;
            }

            foreach (string raw in content!.Split('\n'))
            {
                string line = raw.Trim().TrimStart('\uFEFF').Trim();
                if (line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
                {
                    yield return line;
                }
            }
        }

        /// <summary>Whether a line is an option line: the part before its first '=' is one of <see cref="OptionKeys"/>.</summary>
        private static bool TrySplitOption(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            int eq = line.IndexOf('=');
            if (eq <= 0)
            {
                return false;
            }

            string candidate = line.Substring(0, eq).Trim();
            if (Array.FindIndex(OptionKeys, k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase)) < 0)
            {
                return false;
            }

            key = candidate;
            value = line.Substring(eq + 1).Trim();
            return true;
        }
    }
}
