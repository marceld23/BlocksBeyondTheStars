// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Persistence;

/// <summary>
/// Decides where <c>/bump</c> bug reports are written. When the server runs from inside the GitHub
/// working tree (e.g. a client build launched from <c>./client/Build/Windows/…</c>, or the Unity Editor),
/// reports go to <c>&lt;repoRoot&gt;/bugreports/server/</c> so a developer finds them next to the source.
/// Otherwise (an installed build under %LocalAppData%) they stay in the per-world fallback directory,
/// exactly where they used to live.
/// </summary>
public static class BugReportPaths
{
    /// <summary>Resolves the directory bug reports should be written to.</summary>
    /// <param name="perWorldFallback">The directory to use when no repository is detected
    /// (historically <c>&lt;world&gt;/bumps</c>).</param>
    /// <param name="startDirectory">Where to begin the upward search; defaults to the running
    /// executable's base directory.</param>
    public static string Resolve(string perWorldFallback, string? startDirectory = null)
    {
        var repoRoot = FindRepositoryRoot(startDirectory ?? System.AppContext.BaseDirectory);
        return repoRoot != null
            ? Path.Combine(repoRoot, "bugreports", "server")
            : perWorldFallback;
    }

    /// <summary>Walks up from <paramref name="startDirectory"/> looking for the repository root — the nearest
    /// ancestor that contains a <c>.git</c> entry (a <c>BlocksBeyondTheStars.sln</c> is accepted as a secondary
    /// marker). Returns null when none is found within a bounded number of levels.</summary>
    public static string? FindRepositoryRoot(string startDirectory)
    {
        if (string.IsNullOrEmpty(startDirectory))
        {
            return null;
        }

        var dir = new DirectoryInfo(startDirectory);
        for (int depth = 0; dir != null && depth < 12; depth++, dir = dir.Parent)
        {
            // ".git" is a directory in a normal clone but a file in a worktree/submodule — accept both.
            if (Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, "BlocksBeyondTheStars.sln")))
            {
                return dir.FullName;
            }
        }

        return null;
    }
}
