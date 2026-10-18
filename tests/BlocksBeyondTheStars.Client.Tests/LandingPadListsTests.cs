// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2740: the client keeps one landing-pad list per body. The world map reads the list of the body it shows, so a pad
/// list for another body — the flight pad chooser looking at a padless body — never wipes the current body's markers.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class LandingPadListsTests
{
    private static LandingPadList List(string bodyId, int pads, float timeOfDay = 0.35f)
    {
        var list = new LandingPadList { BodyId = bodyId, Pads = new NetLandingPad[pads], TimeOfDay = timeOfDay };
        for (int i = 0; i < pads; i++)
        {
            list.Pads[i] = new NetLandingPad { Index = i };
        }

        return list;
    }

    [Fact]
    public void AListForAnotherBody_LeavesTheCurrentBodysPadsAlone()
    {
        var book = new LandingPadLists();
        Assert.Null(book.PadsFor("home")); // nothing known yet

        book.Apply(List("home", 4, 0.6f));
        int version = book.Version;
        book.Apply(List("padless_moon", 0));

        Assert.True(book.Version > version);
        Assert.Equal(4, book.PadsFor("home")!.Length);
        Assert.Empty(book.PadsFor("padless_moon")!); // known, and without pads — not unknown
        Assert.Null(book.PadsFor("elsewhere"));
        Assert.Null(book.PadsFor(null));
        Assert.Equal(0.6f, book.TimeOfDayFor("home"));
        Assert.Null(book.TimeOfDayFor("elsewhere"));

        // A fresh list for the same body replaces only that body's.
        book.Apply(List("home", 5));
        Assert.Equal(5, book.PadsFor("home")!.Length);
        Assert.Equal(2, book.Count);

        // The chooser's "the current body" request is echoed with an empty id and kept under it.
        book.Apply(List(string.Empty, 3));
        Assert.Equal(3, book.PadsFor(string.Empty)!.Length);
        Assert.Equal(5, book.PadsFor("home")!.Length);

        book.Apply(null);
        Assert.Equal(3, book.Count);
        book.Clear();
        Assert.Equal(0, book.Count);
        Assert.Null(book.PadsFor("home"));
    }

    [Fact]
    public void TheServersReplyForAnotherBody_LeavesTheActiveBodysPadsUntouched()
    {
        using var h = new ClientServerHarness(ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));
        var book = new LandingPadLists();
        LandingPadList? last = null;
        h.Client.LandingPadsReceived += m =>
        {
            book.Apply(m);
            last = m;
        };
        h.Join();
        Assert.NotNull(h.JoinAccepted);

        string home = h.Server.ActiveLocationId;
        Assert.True(h.PumpUntil(() => book.PadsFor(home) != null, 30), "the join must send the active body's pads");
        int homePads = book.PadsFor(home)!.Length;
        Assert.True(homePads > 0, "the start body has landing pads");

        // The flight pad chooser asks about another body (one without pads) — the server answers for that body only.
        last = null;
        h.Client.SendRequestLandingPads("no-such-body");
        Assert.True(h.PumpUntil(() => last != null && last.BodyId == "no-such-body", 30), "every pad request is answered");

        Assert.Empty(book.PadsFor("no-such-body")!);
        Assert.Equal(homePads, book.PadsFor(home)!.Length); // the world map still has the body's markers
    }
}
