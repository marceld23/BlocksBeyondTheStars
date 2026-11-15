// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Where a local world's "last played" picture lives (My worlds, #3664): <see cref="FileName"/> inside the world's own
    /// save folder — so deleting the world deletes its picture too — and never inside the server's database. The client
    /// takes it while the player is in the world (<c>WorldPreviewCapture</c>) and writes it atomically; the world picker's
    /// detail pane and the main menu's Continue card read it (<c>WorldPreviewFrame</c>). Pure System.IO: the Unity client
    /// and the headless tests share it.
    /// </summary>
    public static class WorldPreviewFiles
    {
        /// <summary>The picture's file name inside the world's save folder.</summary>
        public const string FileName = "preview.jpg";

        /// <summary>The suffix of the temporary file a write goes through before it replaces the picture.</summary>
        public const string TempSuffix = ".tmp";

        /// <summary>The picture's size: 16:9 and small (a few dozen KB as a JPG).</summary>
        public const int Width = 640, Height = 360;

        /// <summary>The JPG quality the picture is encoded with.</summary>
        public const int JpegQuality = 80;

        /// <summary>A picture larger than this is neither written nor read (a 640×360 JPG is far below it).</summary>
        public const long MaxBytes = 2L * 1024 * 1024;

        private static readonly object WriteGate = new object();

        /// <summary>The picture's path for a world, or null when the name cannot be a save folder of
        /// <paramref name="savesRoot"/> (empty, "." / "..", or anything else that would not land directly inside it).</summary>
        public static string? PathFor(string? savesRoot, string? worldName)
        {
            string? dir = WorldDirectory(savesRoot, worldName);
            return dir == null ? null : Path.Combine(dir, FileName);
        }

        /// <summary>The world's save folder — <paramref name="savesRoot"/> + <see cref="FolderName"/> — or null when it would
        /// not be a direct child of the saves root (the same guard the world delete uses).</summary>
        public static string? WorldDirectory(string? savesRoot, string? worldName)
        {
            if (string.IsNullOrWhiteSpace(savesRoot) || string.IsNullOrWhiteSpace(worldName))
            {
                return null;
            }

            try
            {
                string root = Path.GetFullPath(savesRoot);
                string dir = Path.GetFullPath(Path.Combine(root, FolderName(worldName!)));
                string? parent = Path.GetDirectoryName(dir);
                return parent != null && string.Equals(TrimSeparators(parent), TrimSeparators(root), StringComparison.Ordinal) ? dir : null;
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException
                                      || e is System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <summary>The save folder's name for a world, as the server derives it (<c>SaveGamePaths</c>): every character a
        /// file name may not hold becomes '_', and a blank name is "world". The world picker strips such characters before
        /// a world is created, so for its worlds the folder is the name itself.</summary>
        public static string FolderName(string worldName)
        {
            string name = worldName ?? string.Empty;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }

            return string.IsNullOrWhiteSpace(name) ? "world" : name;
        }

        /// <summary>
        /// Writes <paramref name="jpg"/> as the picture at <paramref name="path"/> atomically: into a temp file first, which
        /// then replaces the old picture, so a reader never sees half a file. Only into an EXISTING world folder — a world
        /// deleted meanwhile is not brought back as an empty folder. Thread-safe; never throws; false when nothing was written.
        /// </summary>
        public static bool Write(string? path, byte[]? jpg)
        {
            if (string.IsNullOrEmpty(path) || jpg == null || jpg.Length == 0 || jpg.Length > MaxBytes)
            {
                return false;
            }

            string tmp = path + TempSuffix;
            lock (WriteGate)
            {
                try
                {
                    string? dir = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    {
                        return false;
                    }

                    File.WriteAllBytes(tmp, jpg);
                    if (File.Exists(path))
                    {
                        try
                        {
                            File.Replace(tmp, path, null);
                        }
                        catch (Exception e) when (e is IOException || e is PlatformNotSupportedException || e is UnauthorizedAccessException)
                        {
                            // Some file systems have no replace: the same result, just not atomic.
                            File.Delete(path);
                            File.Move(tmp, path);
                        }
                    }
                    else
                    {
                        File.Move(tmp, path);
                    }

                    return true;
                }
                catch (Exception e) when (IsFileError(e))
                {
                    TryDelete(tmp);
                    return false;
                }
            }
        }

        /// <summary>The picture's bytes, or null when there is none, it is empty or larger than <see cref="MaxBytes"/>, or it
        /// cannot be read. Never throws.</summary>
        public static byte[]? TryRead(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var info = new FileInfo(path);
                return info.Exists && info.Length > 0 && info.Length <= MaxBytes ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e) when (IsFileError(e))
            {
                return null;
            }
        }

        /// <summary>When the picture was last written (UTC), or null when there is none — what a loaded picture is
        /// remembered by, so a newer one replaces it.</summary>
        public static DateTime? WrittenUtc(string? path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }

            try
            {
                var info = new FileInfo(path);
                return info.Exists ? info.LastWriteTimeUtc : (DateTime?)null;
            }
            catch (Exception e) when (IsFileError(e))
            {
                return null;
            }
        }

        /// <summary>
        /// The part of a <paramref name="width"/> × <paramref name="height"/> picture that fills a box of
        /// <paramref name="boxWidth"/> × <paramref name="boxHeight"/> without stretching ("cover"): x, y, width and height in
        /// 0..1 texture coordinates, centred. A wider picture loses its sides, a taller one its top and bottom; a picture of
        /// the box's shape (or a degenerate size) is shown whole.
        /// </summary>
        public static (float X, float Y, float W, float H) CoverRect(int width, int height, float boxWidth, float boxHeight)
        {
            if (width <= 0 || height <= 0 || !(boxWidth > 0f) || !(boxHeight > 0f))
            {
                return (0f, 0f, 1f, 1f);
            }

            float picture = (float)width / height, box = boxWidth / boxHeight;
            if (picture > box)
            {
                float w = box / picture;
                return ((1f - w) / 2f, 0f, w, 1f);
            }

            float h = picture / box;
            return (0f, (1f - h) / 2f, 1f, h);
        }

        private static string TrimSeparators(string path) => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static bool IsFileError(Exception e)
            => e is IOException || e is UnauthorizedAccessException || e is NotSupportedException || e is ArgumentException
               || e is System.Security.SecurityException;

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e) when (IsFileError(e))
            {
                // A leftover temp file is overwritten by the next write.
            }
        }
    }
}
