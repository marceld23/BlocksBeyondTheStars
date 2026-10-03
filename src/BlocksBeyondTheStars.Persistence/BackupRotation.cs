// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;

namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Naming and pruning of the backups the server takes by itself (#2223), so a save's <c>backups/</c> folder stays
/// bounded. Every such backup is named <c>&lt;kind prefix&gt;&lt;UTC time&gt;</c> plus the backend's extension; the
/// time is fixed-width, so sorting by name is sorting by age. A kind only ever prunes its own files — a backup
/// made by hand (admin UI, tools: <c>backup_…</c>) is never deleted.
/// </summary>
public static class BackupRotation
{
    /// <summary>The rotating backups of a running server (<c>ServerConfig.BackupIntervalMinutes</c>).</summary>
    public const string AutoPrefix = "auto_";

    /// <summary>The copy taken before a save's block ids are remapped to a changed block set.</summary>
    public const string PreRemapPrefix = "pre-remap_";

    /// <summary>A backup is written under this suffix and renamed when complete, so a copy that was cut short
    /// never looks like a backup.</summary>
    public const string TempSuffix = ".tmp";

    /// <summary>The label of a backup of one kind taken at <paramref name="utcNow"/>.</summary>
    public static string Label(string prefix, DateTime utcNow)
        => prefix + utcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);

    /// <summary>
    /// Deletes the oldest backups of one kind until at most <paramref name="keep"/> remain, and any unfinished
    /// copy of that kind a crash left behind. Returns how many finished backups were deleted. A file that cannot
    /// be deleted (locked, read-only) is skipped — pruning never fails the backup that triggered it.
    /// </summary>
    public static int Prune(string directory, string prefix, int keep)
    {
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(prefix) || !Directory.Exists(directory))
        {
            return 0;
        }

        var finished = new List<string>();
        foreach (var file in Directory.GetFiles(directory, prefix + "*"))
        {
            if (file.EndsWith(TempSuffix, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(file);
            }
            else
            {
                finished.Add(file);
            }
        }

        // Newest first: the timestamp is the first thing after the prefix, so the file name orders by age.
        finished.Sort((a, b) => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(a)));

        int deleted = 0;
        for (int i = Math.Max(0, keep); i < finished.Count; i++)
        {
            if (TryDelete(finished[i]))
            {
                deleted++;
            }
        }

        return deleted;
    }

    private static bool TryDelete(string file)
    {
        try
        {
            File.Delete(file);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
