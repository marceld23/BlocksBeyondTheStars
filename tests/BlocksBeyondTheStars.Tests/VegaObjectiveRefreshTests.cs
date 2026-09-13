// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #1859: the story objective "a net fragment lies on this world — follow VEGA's signal" names the body,
/// follows the player (the chip is re-sent by VEGA's 1 Hz tick whenever the live objective differs from the
/// one last sent — launch, station, pickup, travel), and puts the fragment signal on the map without waiting
/// for the tip-gated hint. Plus the wire: <see cref="ShipAiLine.ObjectiveArg"/> is an additive field.
/// </summary>
public sealed class VegaObjectiveRefreshTests : IDisposable
{
    private static readonly string[] OnboardingStages = { "mine", "craft", "eat", "scan", "unlock", "launch", "dock", "trade", "land" };

    private readonly string _root;
    private readonly GameContent _content;

    public VegaObjectiveRefreshTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_vegaobj_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private ServerConfig Config(string world) => new()
    {
        WorldName = world,
        Seed = 4242,
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false,
        PlaceSettlements = false,
    };

    /// <summary>Collects every VEGA line the client receives (handler must be attached before joining).</summary>
    private static List<ShipAiLine> CaptureVega(LoopbackClientTransport client)
    {
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };
        return lines;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    /// <summary>Guarantees at least one unread net fragment on the active world (the surface roll may be dry):
    /// walks data-terminal spots until the deterministic per-spot roll places one.</summary>
    private static void EnsureFragmentOnWorld(SvGameServer server)
    {
        for (int i = 0; i < 64 && server.NetFragmentCount == 0; i++)
        {
            server.PlaceStructureFragmentForTest("data_terminal", 100f + (i * 7f), 40f, -80f + (i * 5f));
        }

        Assert.True(server.NetFragmentCount > 0, "no data_terminal marker rolled a fragment across 64 spots");
    }

    private CelestialBody OtherPlanet(SvGameServer server, string homeId)
    {
        string sys = server.Galaxy.FindBody(homeId)!.SystemId;
        return server.Galaxy.AllBodies().First(b =>
            b.Kind is CelestialKind.Planet or CelestialKind.Moon
            && b.SystemId == sys
            && !string.IsNullOrEmpty(b.PlanetType)
            && _content.GetPlanet(b.PlanetType!) is not null
            && b.Id != homeId);
    }

    [Fact]
    public void FragmentObjective_NamesTheBody_IsResentByTheTick_AndMovesOnWhenThePlayerLeaves()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "objective"));
        var link = new LoopbackLink();
        using var serverTransport = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config("objective"), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Pilot");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey); // the join sent the tutorial chip

        var session = server.Sessions[1];
        string homeId = session.CurrentLocationId;
        string homeName = server.Galaxy.FindBody(homeId)!.Name;
        Assert.False(string.IsNullOrEmpty(homeName));
        EnsureFragmentOnWorld(server);

        // Finish the onboarding behind VEGA's back: no handler fires, so nothing re-sends the chip — only the
        // tick's "differs from the last sent" check can move it on (#1859 staleness).
        foreach (var stage in OnboardingStages)
        {
            session.State.Milestones.Add("vega:stage:" + stage);
        }

        lines.Clear();
        server.Tick(1.1);
        client.Poll();
        var chip = Assert.Single(lines.Where(l => l.LineKey.Length == 0 && l.Text.Length == 0)); // objective-only refresh
        Assert.Equal("story.obj.fragment_on", chip.ObjectiveKey);
        Assert.Equal(homeName, chip.ObjectiveArg);
        Assert.Equal("story.obj.fragment_on", server.ObjectiveKeyForTest("Pilot"));
        Assert.Equal(homeName, server.ObjectiveArgForTest("Pilot"));

        // The signal is on the map without the tip having fired — the chip says "follow VEGA's signal".
        Assert.Contains(server.PlanetPoisForTest("Pilot"), poi => poi.Type == "fragment_signal");

        // Nothing changed ⇒ the tick stays quiet (no chip spam at 1 Hz).
        lines.Clear();
        server.Tick(1.1);
        client.Poll();
        Assert.DoesNotContain(lines, l => l.LineKey.Length == 0 && l.Text.Length == 0);

        // Leaving the world: the objective must stop pointing at the body the player is no longer on.
        var other = OtherPlanet(server, homeId);
        server.SetInstantTravelForTest(true);
        Assert.True(server.QuickTravelForTest("Pilot", other.Id));
        server.Tick(1.1);
        client.Poll();
        var moved = lines.Last();
        Assert.NotEqual("story.obj.fragment_here", moved.ObjectiveKey);
        Assert.True(
            moved.ObjectiveKey == "story.obj.search"
            || (moved.ObjectiveKey == "story.obj.fragment_on" && moved.ObjectiveArg == other.Name),
            $"chip after travel: {moved.ObjectiveKey} / '{moved.ObjectiveArg}' — still '{homeName}'?");
        Assert.NotEqual(homeName, moved.ObjectiveArg);

        server.Stop();
    }

    [Fact]
    public void FragmentObjective_MovesOnAfterThePickup()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pickup"));
        var server = new SvGameServer(Config("pickup"), _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        var p = server.AddLocalPlayer("Reader");
        foreach (var stage in OnboardingStages)
        {
            p.State.Milestones.Add("vega:stage:" + stage);
        }

        EnsureFragmentOnWorld(server);
        Assert.Equal("story.obj.fragment_on", server.ObjectiveKeyForTest("Reader"));

        foreach (var frag in server.NetFragmentSnapshots.ToList())
        {
            Assert.True(server.PickUpNetFragmentForTest(frag.Id));
        }

        Assert.Equal(0, server.NetFragmentCount);
        Assert.Equal("story.obj.search", server.ObjectiveKeyForTest("Reader")); // no fragment left here
        Assert.Equal(string.Empty, server.ObjectiveArgForTest("Reader"));       // …and no body name to show
        server.Stop();
    }

    [Fact]
    public void ObjectiveLocaleKeys_ExistInBothLanguages_WithThePlaceholder()
    {
        var en = _content.CreateLocalizer(Shared.Localization.GameLocale.English);
        var de = _content.CreateLocalizer(Shared.Localization.GameLocale.German);
        foreach (var key in new[] { "story.obj.fragment_on", "ui.hud.compass_fragment" })
        {
            Assert.True(en.Has(key), $"missing '{key}' in en");
            Assert.True(de.Has(key), $"missing '{key}' in de");
            Assert.Contains("{0}", en.Get(key));
            Assert.Contains("{0}", de.Get(key));
        }
    }

    [Fact]
    public void ShipAiLine_ObjectiveArg_RoundTrips_AndDefaultsToEmpty()
    {
        // Additive field on an existing contractless message: a legacy line reads as "no argument".
        Assert.Equal(string.Empty, new ShipAiLine().ObjectiveArg);

        var line = new ShipAiLine
        {
            ObjectiveKey = "story.obj.fragment_on",
            ObjectiveArg = "Kepler-3 b",
            ObjectiveProgress = 128,
            ObjectiveTarget = 204,
        };
        var decoded = Assert.IsType<ShipAiLine>(NetCodec.Decode(NetCodec.Encode(line)));
        Assert.Equal("story.obj.fragment_on", decoded.ObjectiveKey);
        Assert.Equal("Kepler-3 b", decoded.ObjectiveArg);
        Assert.Equal(128, decoded.ObjectiveProgress);
        Assert.Equal(204, decoded.ObjectiveTarget);
    }
}
