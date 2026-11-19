// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Playtests;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// The playtest watch end to end, headless: the real <c>NetworkClient</c> names the game events a playtest task waits
/// for, the real server watches them and reports the ones this player causes — over the wire, through the dispatch
/// (also while a singleplayer world is held), until a step of the task ticks. Plus the client's pacing of the watch
/// list and its verdict on a server that never answers.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class PlaytestWatchClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static (ClientServerHarness Harness, List<PlaytestWatchAck> Acks, List<PlaytestEvent> Events) Joined()
    {
        var harness = new ClientServerHarness(Content);
        var acks = new List<PlaytestWatchAck>();
        var events = new List<PlaytestEvent>();
        harness.Client.PlaytestWatchAckReceived += acks.Add;
        harness.Client.PlaytestEventReceived += events.Add;
        harness.Join();
        Assert.NotNull(harness.JoinAccepted);
        return (harness, acks, events);
    }

    [Fact]
    public void TheServer_WatchesOnlyPublishedGameEvents_AndSaysWhichOnes()
    {
        var (harness, acks, _) = Joined();
        using (harness)
        {
            harness.Client.SendPlaytestWatch(new[]
            {
                "hotbar:select", "craft:*", "craft:any", "hotbar:select", // a double
                "ui:menu_open", "editor:saved", "nope:any", "*", "cra*ft", string.Empty, // the client's own, unknown, too greedy, malformed
            });

            Assert.True(harness.PumpUntil(() => acks.Count == 1));
            Assert.Equal(new[] { "hotbar:select", "craft:*", "craft:any" }, acks[0].Patterns);
        }
    }

    [Fact]
    public void AWatchedEvent_ComesBack_AndTicksTheStepOfTheTask()
    {
        var (harness, acks, events) = Joined();
        using (harness)
        {
            Assert.True(PlaytestCatalogJson.TryReadCatalog("""
                { "tasks": [ { "id": "quick-bar", "title": { "de": "Schnellleiste", "en": "Quick bar" },
                    "steps": [ { "id": "pick", "text": { "de": "Wähle zwei Plätze.", "en": "Pick two slots." },
                                 "check": { "event": "hotbar:select", "required": 2 } } ] } ] }
                """, out var catalog));
            var tracker = new PlaytestTracker(new PlaytestProgress(string.Empty));
            tracker.SetCatalog(catalog, "0.1.0-dev");
            Assert.True(tracker.Activate("quick-bar", 100));
            harness.Client.PlaytestEventReceived += e => tracker.OnEvent(e.Key, e.Arg, e.Amount, 100);

            harness.Client.SendPlaytestWatch(tracker.WatchPatterns());
            Assert.True(harness.PumpUntil(() => acks.Count == 1));

            harness.Client.SendSelectHotbar(3);
            harness.Tick();
            harness.Client.SendSelectHotbar(5);
            Assert.True(harness.PumpUntil(() => events.Sum(e => e.Amount) >= 2));

            Assert.All(events, e => Assert.Equal("hotbar:select", e.Key));
            Assert.Equal(PlaytestTaskStatus.ReadyToReport, tracker.Progress.StatusOf(tracker.Find("quick-bar")!));
            Assert.Empty(tracker.WatchPatterns()); // the step is closed — nothing left to ask for
        }
    }

    [Fact]
    public void WithoutAWatch_NothingIsReported_AndAnEmptyListEndsOne()
    {
        var (harness, acks, events) = Joined();
        using (harness)
        {
            harness.Client.SendSelectHotbar(3);
            harness.PumpUntil(() => false, maxTicks: 5);
            Assert.Empty(events); // nobody asked

            harness.Client.SendPlaytestWatch(new[] { "hotbar:select" });
            Assert.True(harness.PumpUntil(() => acks.Count == 1));
            harness.PumpUntil(() => false, maxTicks: 5); // past the server's gap between two lists
            harness.Client.SendPlaytestWatch(Array.Empty<string>());
            Assert.True(harness.PumpUntil(() => acks.Count == 2));
            Assert.Empty(acks[1].Patterns);

            harness.Client.SendSelectHotbar(6);
            harness.PumpUntil(() => false, maxTicks: 5);
            Assert.Empty(events); // the watch is over
        }
    }

    [Fact]
    public void AWatchList_PassesAHeldWorld()
    {
        // The Test Pilot page opens from the pause menu, which holds a singleplayer world: a task started there must
        // still reach the server (the pause whitelist) — otherwise its steps would never be recognised.
        var (harness, acks, _) = Joined();
        using (harness)
        {
            harness.Client.SendPause(true);
            Assert.True(harness.PumpUntil(() => harness.Server.IsPaused));

            harness.Client.SendPlaytestWatch(new[] { "space:enter" });

            Assert.True(harness.PumpUntil(() => acks.Count == 1));
            Assert.Equal(new[] { "space:enter" }, acks[0].Patterns);
        }
    }

    [Fact]
    public void TheSync_SendsAChangedListOnce_AndNotFasterThanItsGap()
    {
        var sent = new List<string[]>();
        var sync = new PlaytestWatchSync(list => sent.Add(list.ToArray()));

        sync.Update(Array.Empty<string>(), 0); // nothing to watch, nothing to end
        Assert.Empty(sent);

        sync.Update(new[] { "space:enter", "mine:any" }, 1.0);
        sync.Update(new[] { "space:enter", "mine:any" }, 1.1); // unchanged
        sync.Update(new[] { "mine:any" }, 1.2);                // changed, but inside the gap
        Assert.Single(sent);
        sync.OnAck();

        sync.Update(new[] { "mine:any" }, 1.0 + PlaytestWatchSync.MinGapSeconds);
        Assert.Equal(2, sent.Count);
        Assert.Equal(new[] { "mine:any" }, sent[1]);

        sync.Update(Array.Empty<string>(), 5.0); // the task was put aside: end the watch
        Assert.Equal(3, sent.Count);
        Assert.Empty(sent[2]);
        Assert.True(sync.Supported);
    }

    [Fact]
    public void TheSync_TakesSilenceForAnOlderServer_AndStartsOverOnANewConnection()
    {
        var sent = new List<string[]>();
        var sync = new PlaytestWatchSync(list => sent.Add(list.ToArray()));

        sync.Update(new[] { "space:enter" }, 10.0);
        Assert.Null(sync.Supported); // not known yet
        sync.Update(new[] { "space:enter" }, 10.0 + PlaytestWatchSync.AckTimeoutSeconds - 0.1);
        Assert.Null(sync.Supported);
        sync.Update(new[] { "space:enter" }, 10.0 + PlaytestWatchSync.AckTimeoutSeconds);
        Assert.False(sync.Supported); // silence: the player ticks these steps off by hand

        sync.Update(new[] { "mine:any" }, 30.0);
        Assert.Single(sent); // nothing more is asked of that server

        sync.Reset(); // another world
        Assert.Null(sync.Supported);
        sync.Update(new[] { "mine:any" }, 40.0);
        Assert.Equal(2, sent.Count);
        sync.OnAck();
        sync.Update(new[] { "mine:any", "scan:any" }, 50.0);
        sync.Update(new[] { "mine:any", "scan:any" }, 60.0); // an ack that never comes for a LATER list changes nothing
        Assert.True(sync.Supported);
    }
}
