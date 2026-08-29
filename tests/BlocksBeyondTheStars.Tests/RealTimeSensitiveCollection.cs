// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// A sequential island for wall-clock-sensitive tests: classes in this collection run while NO other
/// collection runs in parallel. Real-time server loops (<c>GameServer.Run</c>), HTTP round-trips and
/// async hand-offs otherwise measure pure scheduling noise instead of their own work — a 9 ms cache test
/// has measured 454 s of wall time inside the parallel suite, tripping the fast-tier duration guardrail
/// (scripts/check-test-durations.py). These tests are fast in isolation, so the serial window costs only
/// seconds.
///
/// <para>The mechanism, nailed down in #1362: with <c>maxParallelThreads: 4</c> xunit installs a
/// <c>MaxConcurrencySyncContext</c> — ONE FIFO queue served by four worker threads — and
/// <c>XunitTestAssemblyRunner</c> posts every test collection of the assembly into that queue up front.
/// An <c>await</c> that really yields (any real I/O; a synchronously-completing one such as Kestrel's
/// <c>StartAsync</c> does not) posts its continuation to the BACK of that queue, behind every collection
/// that has not started yet, and so resumes only once the queue has drained — near the end of the shard.
/// The test is billed for that wait. It is not thread-pool starvation and it is not the I/O: the shard-5
/// trx of run #1366 shows the runner completing 170 other tests at 4.0-of-4 average concurrency during an
/// "82.5 s" loopback request that itself took a millisecond. Nothing is stuck and no CI time is lost —
/// the shard ran 498 test-seconds in 131 s of wall clock — which is exactly why the symptom is a bogus
/// duration rather than a slow run, and why the cure is this collection and not the <c>Slow</c> trait.</para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public static class RealTimeSensitiveCollection
{
    public const string Name = "RealTimeSensitive";
}
