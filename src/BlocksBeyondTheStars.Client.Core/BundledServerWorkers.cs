// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3581: how many chunk-generation workers the server that the desktop client starts for singleplayer and "host a game"
/// runs with. The server's own default (2) fits every host it cannot see — Docker, the hosted fleet's 2-CPU containers, a
/// Raspberry Pi — so the scaling lives here, where the machine is known.
/// <para>A stream batch is workers + 1 chunks generated in parallel and the next pass's prefetch is workers × 6, so from 3
/// workers on the prefetch covers a whole 16-chunk pass. The per-player ceiling stays 16 chunks per tick at 15 Hz and the
/// client meshes 4 chunks per frame: more workers mean fewer tick stalls while exploring, not a faster fill — hence the cap.
/// Half the logical processors approximates the physical (or performance) cores, and two of them stay with the Unity
/// client's main and render threads. Each worker owns a world generator with its column memos, so a machine with little
/// memory keeps the server default.</para>
/// </summary>
public static class BundledServerWorkers
{
    /// <summary>The server's own default (<c>ServerConfig.ChunkGenWorkers</c>) — never less than that here.</summary>
    public const int Minimum = 2;

    /// <summary>At most this many workers, whatever the machine.</summary>
    public const int Maximum = 4;

    /// <summary>Below this much system memory (MB) the server keeps its default.</summary>
    public const int MinMemoryMb = 12 * 1024;

    /// <summary>The variable a player can set to choose the count; the launcher then passes nothing, because the server's
    /// command line beats its environment and would silently override that choice.</summary>
    public const string EnvironmentVariable = "BBS_CHUNK_GEN_WORKERS";

    /// <summary>The worker count for a machine with <paramref name="logicalProcessors"/> logical processors and
    /// <paramref name="systemMemoryMb"/> MB of memory (0 or less = unknown, which keeps the default).</summary>
    public static int For(int logicalProcessors, int systemMemoryMb)
    {
        if (systemMemoryMb < MinMemoryMb)
        {
            return Minimum;
        }

        return Math.Clamp(logicalProcessors / 2 - 2, Minimum, Maximum);
    }

    /// <summary>The launcher's command-line fragment (with a leading space), or empty when
    /// <paramref name="environmentValue"/> (the player's <see cref="EnvironmentVariable"/>) is set.</summary>
    public static string Argument(int logicalProcessors, int systemMemoryMb, string? environmentValue)
        => string.IsNullOrWhiteSpace(environmentValue)
            ? " --chunk-gen-workers " + For(logicalProcessors, systemMemoryMb).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : string.Empty;
}
