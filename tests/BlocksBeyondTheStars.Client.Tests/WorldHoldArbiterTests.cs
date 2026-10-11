// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The world-hold arbiter (#2461): the pause menu, the feedback dialog and its reply (and later a cutscene) each hold
/// the world under their own name, and the ONE pause intent the server knows follows "anyone still holding" — closing
/// one of them no longer releases the hold the other one still needs.
/// </summary>
public sealed class WorldHoldArbiterTests
{
    private readonly List<bool> _sent = new();
    private readonly WorldHoldArbiter _arbiter;

    public WorldHoldArbiterTests()
    {
        _arbiter = new WorldHoldArbiter(_sent.Add);
    }

    [Fact]
    public void TheFirstHolderSendsTheHold_AndTheLastOneTheRelease()
    {
        _arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        _arbiter.Hold(WorldHoldArbiter.Feedback, now: 1f); // the F1 dialog over the pause menu
        Assert.Equal(new[] { true }, _sent);
        Assert.Equal(2, _arbiter.HolderCount);

        _arbiter.Release(WorldHoldArbiter.Feedback); // the report is sent — the pause menu is still up
        Assert.Equal(new[] { true }, _sent);
        Assert.True(_arbiter.Holding);
        Assert.True(_arbiter.IsHeldBy(WorldHoldArbiter.Menu));

        _arbiter.Release(WorldHoldArbiter.Menu);
        Assert.Equal(new[] { true, false }, _sent);
        Assert.False(_arbiter.Holding);
    }

    [Fact]
    public void ClosingTheMenuFirst_KeepsTheDialogsHold()
    {
        // The other order: the menu closes under an open report dialog (or, later, under a held cutscene).
        _arbiter.Hold(WorldHoldArbiter.Feedback, now: 0f);
        _arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        _arbiter.Release(WorldHoldArbiter.Menu);
        Assert.True(_arbiter.Holding);
        Assert.Equal(new[] { true }, _sent);

        _arbiter.Release(WorldHoldArbiter.Feedback);
        Assert.Equal(new[] { true, false }, _sent);
    }

    [Fact]
    public void RepeatedHoldsAndStrayReleases_SendNothingExtra()
    {
        _arbiter.Release(WorldHoldArbiter.Menu); // nothing held: every close path may call it
        Assert.Empty(_sent);

        _arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        _arbiter.Hold(WorldHoldArbiter.Menu, now: 1f);
        _arbiter.Release(WorldHoldArbiter.Feedback); // a holder that never held
        Assert.Equal(new[] { true }, _sent);

        _arbiter.Release(WorldHoldArbiter.Menu);
        _arbiter.Release(WorldHoldArbiter.Menu);
        Assert.Equal(new[] { true, false }, _sent);
    }

    [Fact]
    public void TheKeepAliveRunsOnce_ForAllHolders()
    {
        _arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        _arbiter.Hold(WorldHoldArbiter.FeedbackReply, now: 5f);

        _arbiter.Tick(WorldHoldIntent.KeepAliveSeconds - 0.01f);
        Assert.Single(_sent);
        _arbiter.Tick(WorldHoldIntent.KeepAliveSeconds);
        Assert.Equal(new[] { true, true }, _sent); // one repeat, not one per holder

        _arbiter.Release(WorldHoldArbiter.Menu);
        _arbiter.Release(WorldHoldArbiter.FeedbackReply);
        _arbiter.Tick(100f);
        Assert.Equal(new[] { true, true, false }, _sent);
    }

    [Fact]
    public void Forget_DropsEveryHolderSilently()
    {
        _arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        _arbiter.Hold(WorldHoldArbiter.Cutscene, now: 0f);
        _arbiter.Forget();

        Assert.False(_arbiter.Holding);
        Assert.Equal(0, _arbiter.HolderCount);
        _arbiter.Tick(100f);
        Assert.Equal(new[] { true }, _sent);

        _arbiter.Hold(WorldHoldArbiter.Menu, now: 100f); // the next world starts fresh
        Assert.Equal(new[] { true, true }, _sent);
    }

    [Fact]
    public void AnUnnamedHolder_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => _arbiter.Hold(string.Empty, 0f));
        Assert.Throws<ArgumentNullException>(() => new WorldHoldArbiter(null!));
    }

    /// <summary>End to end against the real server: a lone player's world stays held while the report dialog is still
    /// open over a pause menu that just closed — before #2461 the menu's release resumed it.</summary>
    [Fact]
    public void AgainstTheRealServer_TheWorldStaysHeldUntilTheLastHolderLetsGo()
    {
        var content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        using var h = new ClientServerHarness(content);
        h.Join("Holder");
        Assert.NotNull(h.JoinAccepted);

        var arbiter = new WorldHoldArbiter(h.Client.SendPause);
        arbiter.Hold(WorldHoldArbiter.Menu, now: 0f);
        Assert.True(h.PumpUntil(() => h.Server.IsPaused, maxTicks: 10));

        arbiter.Hold(WorldHoldArbiter.Feedback, now: 1f);
        arbiter.Release(WorldHoldArbiter.Menu);
        h.Tick();
        h.Tick();
        Assert.True(h.Server.IsPaused);

        arbiter.Release(WorldHoldArbiter.Feedback);
        Assert.True(h.PumpUntil(() => !h.Server.IsPaused, maxTicks: 10));
    }
}
