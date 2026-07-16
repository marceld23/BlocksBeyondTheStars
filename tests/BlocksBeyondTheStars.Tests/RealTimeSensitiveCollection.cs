// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A sequential island for wall-clock-sensitive tests: classes in this collection run while NO other
/// collection runs in parallel. Real-time server loops (<c>GameServer.Run</c>), HTTP round-trips and
/// async hand-offs otherwise starve behind the CPU-bound worldgen/sim tests saturating the thread
/// pool — a 9 ms cache test has measured 454 s of wall time inside the parallel suite, tripping the
/// fast-tier duration guardrail (scripts/check-test-durations.py) with pure scheduling noise.
/// These tests are fast in isolation, so the serial window costs only seconds.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class RealTimeSensitiveCollection
{
    public const string Name = "RealTimeSensitive";
}
