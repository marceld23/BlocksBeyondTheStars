// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using static BlocksBeyondTheStars.Client.ConnectRetryPolicy;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The connect re-dial policy. A fresh singleplayer world took 16 s of server-side generation while
    /// the client's budget was the remote one (initial dial + 6 × 2 s ≈ 14 s), so the client gave up in
    /// the very second the server reported ready. Local servers now get a patient budget and an
    /// immediate dial on the launcher's ready signal; remote hosts keep the short budget of #409.
    /// </summary>
    public sealed class ConnectRetryPolicyEditModeTests
    {
        [Test]
        public void Remote_RetriesOnTheIntervalThenGivesUp()
        {
            Assert.That(Next(false, false, 1.9f, 0, 1.9f), Is.EqualTo(Verdict.Wait));
            Assert.That(Next(false, false, 2.0f, 0, 2.0f), Is.EqualTo(Verdict.Retry));
            Assert.That(Next(false, false, 2.0f, RemoteRetries - 1, 12f), Is.EqualTo(Verdict.Retry));
            Assert.That(Next(false, false, 2.0f, RemoteRetries, 14f), Is.EqualTo(Verdict.GiveUp));
            Assert.That(Next(false, false, 1.0f, RemoteRetries, 14f), Is.EqualTo(Verdict.Wait), "give-up also waits for the interval");
        }

        [Test]
        public void Remote_IgnoresTheLocalReadySignal()
        {
            Assert.That(Next(false, true, 0.5f, 0, 0.5f), Is.EqualTo(Verdict.Wait));
        }

        [Test]
        public void Local_KeepsKnockingWellPastTheRemoteBudget()
        {
            // The 2026-09-12 case: the server needed 16 s; the remote budget had already given up.
            Assert.That(Next(true, false, LocalIntervalSeconds, RemoteRetries + 4, 16f), Is.EqualTo(Verdict.Retry));
            Assert.That(Next(true, false, LocalIntervalSeconds, 60, 60f), Is.EqualTo(Verdict.Retry));
            Assert.That(Next(true, false, LocalIntervalSeconds, 119, 119f), Is.EqualTo(Verdict.Retry));
        }

        [Test]
        public void Local_KnocksOncePerIntervalNotEveryFrame()
        {
            Assert.That(Next(true, false, LocalIntervalSeconds - 0.1f, 3, 5f), Is.EqualTo(Verdict.Wait));
            Assert.That(Next(true, false, LocalIntervalSeconds, 3, 5f), Is.EqualTo(Verdict.Retry));
        }

        [Test]
        public void Local_ReadySignalDialsImmediately()
        {
            Assert.That(Next(true, true, 0.05f, 3, 5f), Is.EqualTo(Verdict.Retry), "no waiting for the interval once the server says it listens");
        }

        [Test]
        public void Local_GivesUpOnlyAtTheCeiling()
        {
            Assert.That(Next(true, false, LocalIntervalSeconds, 200, LocalBudgetSeconds - 0.5f), Is.EqualTo(Verdict.Retry));
            Assert.That(Next(true, false, LocalIntervalSeconds, 200, LocalBudgetSeconds), Is.EqualTo(Verdict.GiveUp));
            Assert.That(Next(true, true, 0f, 200, LocalBudgetSeconds), Is.EqualTo(Verdict.GiveUp), "the ceiling beats a late ready signal");
        }

        [Test]
        public void Budgets_AreWhatTheDocsPromise()
        {
            Assert.That(RemoteRetries * RemoteIntervalSeconds, Is.LessThanOrEqualTo(15f), "a mistyped address must still fail fast (#409)");
            Assert.That(LocalBudgetSeconds, Is.GreaterThanOrEqualTo(60f), "a slow machine's fresh world must fit");
        }
    }
}
