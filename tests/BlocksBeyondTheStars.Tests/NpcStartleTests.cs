// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3769 (Justus, 2026-11-17: "when you shoot in a station, NPCs are startled, raise their hands, open their mouth wide and
/// call out"): a weapon fired or swung near an NPC makes it call out — a bubble of kind Alert with the hands-up gesture, the
/// NPC looking at the shooter — startled and funny, never afraid. Only a shot or a weapon's swing does it (never the fist or
/// a tool), only awake NPCs in range, one bout per shooter every few seconds and one call-out per NPC in a while; one line
/// names the shooter; nobody calls for help.
/// </summary>
public sealed class NpcStartleTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A base with its founding resident standing idle five blocks out, the owner three blocks from them.</summary>
    private static (NpcLifeWorld W, int NpcId, Vector3f Spot) ResidentWorld(string name)
    {
        var w = new NpcLifeWorld(Content, name);
        int baseId = w.FoundBase();
        w.Server.ScanBaseLifeForTest();
        int npcId = w.Server.BaseResidentsForTest(baseId).Single().NpcId;
        var spot = new Vector3f(NpcLifeWorld.Cx + 5.5f, w.Feet, NpcLifeWorld.Cz + 5.5f);
        w.Server.ConfigureNpcForTest(npcId, spot);
        w.Player.State.Position = new Vector3f(spot.X - 3f, w.Feet, spot.Z);
        return (w, npcId, spot);
    }

    private static List<NpcGreeting> Alerts(NpcLifeWorld w, PlayerSession p)
        => w.Transport.Sent.Where(s => s.Conn == p.ConnectionId && s.Msg is NpcGreeting { Kind: NpcGreetingKinds.Alert })
            .Select(s => (NpcGreeting)s.Msg).ToList();

    /// <summary>The player's weapon effect right where they stand: a shot (kind 1) or a swing (kind 2) with <paramref name="item"/>.</summary>
    private static void Fire(NpcLifeWorld w, PlayerSession p, byte kind, string item)
    {
        var at = p.State.Position;
        w.Server.FxIntentForTest(p, new FxIntent
        {
            Kind = kind,
            ItemKey = item,
            FromX = at.X,
            FromY = at.Y + 1.4f,
            FromZ = at.Z,
            ToX = at.X + 4f,
            ToY = at.Y + 1.4f,
            ToZ = at.Z + 4f,
        });
    }

    private static string[] Lines(GameLocale locale, string name)
        => Enumerable.Range(1, SvGameServer.StartleLineCount)
            .Select(i => Content.CreateLocalizer(locale).Get("npc.startle." + i).Replace("{0}", name)).ToArray();

    [Fact]
    public void AShotNearby_MakesTheNpcCallOut_HandsUp_AndLookAtTheShooter()
    {
        var (w, npcId, _) = ResidentWorld("startle_shot");
        using (w)
        {
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");

            var g = Assert.Single(Alerts(w, w.Player));
            Assert.Equal(npcId, g.NpcId);
            Assert.Equal(NpcGestures.HandsUp, g.Gesture);
            Assert.True(g.GreetingId > 0);
            Assert.Contains(g.Text, Lines(GameLocale.English, NpcLifeWorld.Owner));
            Assert.True(w.Server.NpcAttendingForTest(npcId), "the startled NPC does not stop and look at the shooter");
        }
    }

    [Fact]
    public void OnlyAShot_OrAWeaponsSwing_Startles_NeverTheFistOrATool()
    {
        var (w, _, _) = ResidentWorld("startle_what");
        using (w)
        {
            Fire(w, w.Player, FxActionKinds.Melee, string.Empty);   // the bare fist
            Fire(w, w.Player, FxActionKinds.Melee, "basic_drill");  // a tool swung
            Fire(w, w.Player, FxActionKinds.Mine, "basic_drill");
            Fire(w, w.Player, FxActionKinds.Place, string.Empty);
            Fire(w, w.Player, FxActionKinds.Scan, "hand_scanner");
            Fire(w, w.Player, FxActionKinds.Shot, "mining_beam");   // a tool "fired" at something sends a shot too
            Assert.Empty(Alerts(w, w.Player));
            Assert.False(w.Server.StartlingEffect(FxActionKinds.Shot, "mining_beam"));
            Assert.False(w.Server.StartlingEffect(FxActionKinds.Shot, "guardian_drill"));

            Fire(w, w.Player, FxActionKinds.Melee, "machete");      // a weapon swung
            Assert.Single(Alerts(w, w.Player));

            Assert.True(w.Server.StartlingEffect(FxActionKinds.Shot, "laser_pistol"));
            Assert.True(w.Server.StartlingEffect(FxActionKinds.Melee, "wood_club"));
            Assert.False(w.Server.StartlingEffect(FxActionKinds.Melee, "wood_pickaxe"));
            Assert.False(w.Server.StartlingEffect(FxActionKinds.Gadget, "field_medkit"));
        }
    }

    [Fact]
    public void ABurstOfFire_IsOneFright_AndTheSameNpcRestsBeforeItCallsOutAgain()
    {
        var (w, _, _) = ResidentWorld("startle_pace");
        using (w)
        {
            for (int i = 0; i < 6; i++)
            {
                Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
                w.Server.AdvanceUptimeForTest(0.2); // the fx bucket refills; still inside the shooter's gap
            }

            Assert.Single(Alerts(w, w.Player));

            w.Server.AdvanceUptimeForTest(SvGameServer.StartleShooterGapSeconds + 0.5);
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
            Assert.Single(Alerts(w, w.Player)); // the shooter may startle again, but this NPC just called out
            Assert.True(SvGameServer.StartlePlaceGapSeconds > SvGameServer.StartleShooterGapSeconds); // and the place rests longer than any one shooter

            w.Server.AdvanceUptimeForTest(SvGameServer.StartleNpcGapSeconds);
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
            Assert.Equal(2, Alerts(w, w.Player).Count);
        }
    }

    [Fact]
    public void ASleeper_AndAnNpcOutOfRange_AreNotStartled()
    {
        var (w, npcId, spot) = ResidentWorld("startle_who");
        using (w)
        {
            w.Server.ConfigureNpcForTest(npcId, spot, pose: 2); // asleep
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
            Assert.Empty(Alerts(w, w.Player));

            w.Server.ConfigureNpcForTest(npcId, spot);
            w.Player.State.Position = new Vector3f(spot.X - (SvGameServer.StartleRange + 3f), w.Feet, spot.Z);
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
            Assert.Empty(Alerts(w, w.Player));

            w.Player.State.Position = new Vector3f(spot.X - (SvGameServer.StartleRange - 2f), w.Feet, spot.Z);
            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");
            Assert.Single(Alerts(w, w.Player));
        }
    }

    [Fact]
    public void EveryoneNearby_HearsTheCallOut_InTheirOwnLanguage_WithTheShootersName()
    {
        var (w, _, spot) = ResidentWorld("startle_heard");
        using (w)
        {
            var friend = w.Server.AddLocalPlayer("Friend");
            friend.State.AboardShip = false;
            friend.State.Position = new Vector3f(spot.X + 6f, w.Feet, spot.Z);
            friend.Locale = "de";

            Fire(w, w.Player, FxActionKinds.Shot, "scrap_pistol");

            var mine = Assert.Single(Alerts(w, w.Player));
            var theirs = Assert.Single(Alerts(w, friend));
            Assert.Equal(mine.GreetingId, theirs.GreetingId); // one bubble
            Assert.Contains(theirs.Text, Lines(GameLocale.German, NpcLifeWorld.Owner));
        }
    }

    [Fact]
    public void TheLines_TakeTurns_OneNamesTheShooter_AndNobodyCallsForHelp()
    {
        // Two NPCs of one bout never say the same, and over the bouts every line comes up.
        var seen = new HashSet<string>();
        for (int bout = 0; bout < 16; bout++)
        {
            var keys = Enumerable.Range(0, SvGameServer.StartleMaxNpcs).Select(i => SvGameServer.StartleLineKey(bout, i)).ToList();
            Assert.Equal(keys.Count, keys.Distinct().Count());
            seen.UnionWith(keys);
        }

        Assert.Equal(SvGameServer.StartleLineCount, seen.Count);

        foreach (var locale in new[] { GameLocale.English, GameLocale.German })
        {
            var loc = Content.CreateLocalizer(locale);
            var lines = Enumerable.Range(1, SvGameServer.StartleLineCount).Select(i => loc.Get("npc.startle." + i)).ToList();
            Assert.All(lines, l => Assert.False(l.StartsWith("[", StringComparison.Ordinal), l));
            Assert.All(lines, l => Assert.InRange(l.Length, 4, 80)); // a call-out, not a speech
            Assert.Single(lines, l => l.Contains("{0}", StringComparison.Ordinal)); // "… the bounty hunter {0} …"
            // Startled and funny, never afraid: the game has no fear-driven mechanics.
            Assert.DoesNotContain(lines, l => l.Contains("Help", StringComparison.OrdinalIgnoreCase) || l.Contains("Hilfe", StringComparison.OrdinalIgnoreCase));
        }
    }
}
