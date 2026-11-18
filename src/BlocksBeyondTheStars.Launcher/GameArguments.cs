// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Diagnostics;

namespace BlocksBeyondTheStars.Launcher;

/// <summary>
/// The launcher's own command line, handed on to the Unity player it starts. The installed game's shortcut points at the
/// launcher, so the start options a player puts there (<c>-dataDir</c>, <c>-hostPort</c> — read by the game's
/// <c>LaunchOptions</c>) reach the game only through here; the update restart passes them the same way. Velopack's own hook
/// arguments (<c>--veloapp-…</c>) are the launcher's business and never forwarded. Shared by the Windows and the Linux
/// launcher (linked into the console project).
/// </summary>
internal static class GameArguments
{
    private const string VelopackHookPrefix = "--veloapp-";

    /// <summary>Adds every forwardable argument of <paramref name="args"/> to <paramref name="psi"/>, one by one, so a path
    /// with spaces stays one argument.</summary>
    internal static void Forward(string[] args, ProcessStartInfo psi)
    {
        foreach (string arg in args)
        {
            if (!arg.StartsWith(VelopackHookPrefix, StringComparison.OrdinalIgnoreCase))
            {
                psi.ArgumentList.Add(arg);
            }
        }
    }
}
