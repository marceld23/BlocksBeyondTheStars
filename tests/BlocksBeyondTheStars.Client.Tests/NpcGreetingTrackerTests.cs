// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2593 — a new NPC bubble vs. the AI upgrade of one that shows (<see cref="NpcGreetingTracker"/>): a new id plays voice
/// and gesture once, the same id only swaps the text (never a second babble), a late or overtaken message is dropped, and
/// an older server without ids is guessed by timing.
/// </summary>
public sealed class NpcGreetingTrackerTests
{
    private const float Hold = 7f;

    [Fact]
    public void ANewId_IsNew_TheSameIdIsItsUpgrade()
    {
        var t = new NpcGreetingTracker();
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 5, 0f, Hold));
        Assert.Equal(Hold, t.BubbleUntil(1));

        Assert.Equal(NpcGreetingDecision.Upgrade, t.OnGreeting(1, 5, 2f, 10f));
        Assert.Equal(12f, t.BubbleUntil(1)); // the longer AI line holds longer

        Assert.Equal(NpcGreetingDecision.Upgrade, t.OnGreeting(1, 5, 3f, 1f));
        Assert.Equal(12f, t.BubbleUntil(1)); // never shortened

        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 9, 4f, Hold));
    }

    [Fact]
    public void ALateUpgrade_AfterTheBubbleExpired_IsDropped()
    {
        var t = new NpcGreetingTracker();
        t.OnGreeting(1, 5, 0f, Hold);
        Assert.Equal(NpcGreetingDecision.Upgrade, t.Decide(1, 5, Hold + 2.9f)); // just after it faded: still welcome
        Assert.Equal(NpcGreetingDecision.Drop, t.OnGreeting(1, 5, Hold + 3.1f, Hold));
        Assert.Equal(Hold, t.BubbleUntil(1)); // a dropped message changes nothing
    }

    [Fact]
    public void AnOvertakenId_IsDropped_UnlessTheServerStartedCountingAnew()
    {
        var t = new NpcGreetingTracker();
        t.OnGreeting(1, 50, 0f, Hold);
        Assert.Equal(NpcGreetingDecision.Drop, t.OnGreeting(1, 49, 1f, Hold));
        Assert.Equal(NpcGreetingDecision.Drop, t.Decide(1, 3, 59f));
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 3, 61f, Hold)); // a restarted server, a minute later
    }

    [Fact]
    public void EachNpc_IsTrackedOnItsOwn()
    {
        var t = new NpcGreetingTracker();
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 5, 0f, Hold));
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(2, 5, 0f, Hold)); // another NPC, (theoretically) the same id
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(3, 2, 0f, Hold));
        Assert.Equal(float.NegativeInfinity, t.BubbleUntil(4));
    }

    [Fact]
    public void AnOlderServer_WithoutIds_IsGuessedByTiming()
    {
        var t = new NpcGreetingTracker();
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 0, 0f, Hold));
        Assert.Equal(NpcGreetingDecision.Upgrade, t.OnGreeting(1, 0, 4f, Hold)); // the AI line, seconds later
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 0, 26f, Hold));   // that server re-greets at most every 25 s

        // A legacy upgrade that comes long after the bubble faded is dropped too.
        var late = new NpcGreetingTracker();
        late.OnGreeting(1, 0, 0f, Hold);
        Assert.Equal(NpcGreetingDecision.Drop, late.OnGreeting(1, 0, Hold + 4f, Hold));
    }

    [Fact]
    public void ABubbleWithoutIdAfterOneWithAnId_IsNew()
    {
        var t = new NpcGreetingTracker();
        t.OnGreeting(1, 12, 0f, Hold);
        Assert.Equal(NpcGreetingDecision.New, t.OnGreeting(1, 0, 1f, Hold)); // e.g. an unstamped sleepy greeting
    }

    [Fact]
    public void HideForgetAndClear()
    {
        var t = new NpcGreetingTracker();
        t.OnGreeting(1, 5, 0f, Hold);
        t.Hide(1, 2f);
        Assert.Equal(2f, t.BubbleUntil(1));
        Assert.Equal(NpcGreetingDecision.Drop, t.Decide(1, 4, 3f)); // the id order survives a hidden bubble

        t.Forget(1);
        Assert.Equal(NpcGreetingDecision.New, t.Decide(1, 4, 3f));

        t.OnGreeting(2, 8, 0f, Hold);
        t.Clear();
        Assert.Equal(NpcGreetingDecision.New, t.Decide(2, 1, 0.5f)); // a world change: the ids restart
    }
}
