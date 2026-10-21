// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.WorldHost;

namespace BlocksBeyondTheStars.Tests;

/// <summary>The shared in-memory stand-in for Docker: containers "run" until stopped (or told to die).</summary>
public sealed class FakeLauncher : IInstanceLauncher
{
    public int StartCount;
    public bool FailStart;
    public readonly HashSet<string> Running = new(StringComparer.Ordinal);

    public string Start(WorldRecord world)
    {
        if (FailStart)
        {
            throw new InvalidOperationException("docker run failed (simulated)");
        }

        StartCount++;
        string id = "container-" + StartCount;
        Running.Add(id);
        return id;
    }

    public void Stop(string containerId) => Running.Remove(containerId);

    /// <summary>World ids whose container object was removed (`docker rm -f bbs-world-&lt;id&gt;`).</summary>
    public readonly List<string> Removed = new();

    public void Remove(string worldId) => Removed.Add(worldId);

    public bool IsRunning(string containerId) => containerId != null && Running.Contains(containerId);

    public IReadOnlyList<ContainerStat> ContainerStats() => Array.Empty<ContainerStat>();
}
