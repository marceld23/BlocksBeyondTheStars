// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The shared server logger fake: keeps what the server logs, so a test can read what an operator would — per level, and
/// as one ordered list of every line with its level prefix ("INFO ", "WARN ", "ERROR ").
/// </summary>
public sealed class CapturingLogger : IGameLogger
{
    /// <summary>The info lines, in order.</summary>
    public List<string> Infos { get; } = new();

    /// <summary>The warnings, in order.</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>The errors, in order.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Every line with its level prefix, in order.</summary>
    public List<string> Lines { get; } = new();

    public void Info(string message)
    {
        Infos.Add(message);
        Lines.Add("INFO " + message);
    }

    public void Warn(string message)
    {
        Warnings.Add(message);
        Lines.Add("WARN " + message);
    }

    public void Error(string message)
    {
        Errors.Add(message);
        Lines.Add("ERROR " + message);
    }

    /// <summary>How often exactly this prefixed line was logged.</summary>
    public int Count(string line) => Lines.Count(l => l == line);
}
