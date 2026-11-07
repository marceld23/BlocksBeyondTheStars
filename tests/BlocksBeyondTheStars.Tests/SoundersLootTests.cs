// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3352 — the Sounders' story loot: once disc 1 is decoded and the pack runs, the next relic cache a player loots holds the
/// next story disc they were never handed — builders, hearing, hunger, in that order, per player and never twice; the
/// alien debris field's flight recorder hums the hunger disc once; and every relic cache may hold a Song Spindle (0.5 %) or a
/// collectible disc (5 %), rolled seed-pure from its own key.
/// </summary>
public sealed class SoundersLootTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private static NpcLifeWorld World(string name) => new(Content.Value, name, padHalf: 8);

    private static void ReadyForDiscs(PlayerSession p)
    {
        p.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));
        p.State.Milestones.Add(SoundersIds.DiscMilestone(SoundersIds.DiscBeginning));
    }

    private static string LootNewCache(NpcLifeWorld w, PlayerSession who, int dx)
    {
        string id = w.Server.AddRelicCacheForTest(new Vector3i(NpcLifeWorld.Cx + dx, w.Feet, NpcLifeWorld.Cz));
        Assert.Contains(w.Server.Containers, c => c.Id == id);
        w.Server.LootContainer(who.State.PlayerId, id);
        return id;
    }

    private static bool Has(PlayerSession p, string item) => p.State.Inventory.Has(item, 1);

    [Fact]
    public void TheNextRelicCaches_HandOutBuildersHearingHunger_InOrder_AndNeverTwice()
    {
        using var w = World("loot_order");
        ReadyForDiscs(w.Player);

        LootNewCache(w, w.Player, 1);
        Assert.True(Has(w.Player, SoundersIds.DiscBuilders));
        Assert.False(Has(w.Player, SoundersIds.DiscHearing));

        LootNewCache(w, w.Player, 2);
        Assert.True(Has(w.Player, SoundersIds.DiscHearing));

        LootNewCache(w, w.Player, 3);
        Assert.True(Has(w.Player, SoundersIds.DiscHunger));

        LootNewCache(w, w.Player, -1);
        foreach (string disc in SongDiscs.LootOrder)
        {
            Assert.Equal(1, w.Player.State.Inventory.CountOf(disc)); // never a duplicate
            Assert.Contains(SongDiscs.GotMilestone(disc), w.Player.State.Milestones);
        }
    }

    [Fact]
    public void TheStoryLoot_IsPerPlayer_AndWaitsForDiscOneAndThePack()
    {
        using var w = World("loot_players");
        var friend = w.Server.AddLocalPlayer("Friend");
        friend.State.AboardShip = false;
        friend.State.Position = w.Player.State.Position;

        LootNewCache(w, w.Player, 1); // no pack, no disc 1: archaeology only
        Assert.DoesNotContain(SongDiscs.All, d => Has(w.Player, d) && SongDiscs.IsStoryDisc(d));

        w.Player.State.Milestones.Add(StoryPackKeys.Started(SoundersIds.PackId));
        LootNewCache(w, w.Player, 2); // the pack runs, but disc 1 is not decoded yet
        Assert.False(Has(w.Player, SoundersIds.DiscBuilders));

        ReadyForDiscs(w.Player);
        ReadyForDiscs(friend);
        LootNewCache(w, friend, 3);
        Assert.True(Has(friend, SoundersIds.DiscBuilders));
        Assert.False(Has(w.Player, SoundersIds.DiscBuilders)); // the friend's loot is the friend's

        LootNewCache(w, w.Player, -1);
        Assert.True(Has(w.Player, SoundersIds.DiscBuilders)); // the player's own next cache brings theirs
    }

    [Fact]
    public void ADecodedDisc_CountsAsHad()
    {
        using var w = World("loot_decoded");
        ReadyForDiscs(w.Player);
        w.Player.State.Milestones.Add(SoundersIds.DiscMilestone(SoundersIds.DiscBuilders));

        LootNewCache(w, w.Player, 1);

        Assert.False(Has(w.Player, SoundersIds.DiscBuilders));
        Assert.True(Has(w.Player, SoundersIds.DiscHearing));
    }

    [Fact]
    public void TheAlienFlightRecorder_HumsTheHungerDisc_Once()
    {
        using var w = World("loot_recorder");
        w.Server.FlightRecorderReadForTest(w.Player, SvGameServer.AlienDebrisTheme);
        Assert.False(Has(w.Player, SoundersIds.DiscHunger)); // not before the story hands out discs

        ReadyForDiscs(w.Player);
        w.Server.FlightRecorderReadForTest(w.Player, "freighter");
        Assert.False(Has(w.Player, SoundersIds.DiscHunger));

        w.Server.FlightRecorderReadForTest(w.Player, SvGameServer.AlienDebrisTheme);
        w.Server.FlightRecorderReadForTest(w.Player, SvGameServer.AlienDebrisTheme);
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SoundersIds.DiscHunger));
        Assert.Contains("@srv.sounders.recorder_disc", w.Transport.TextsTo(w.Player));

        LootNewCache(w, w.Player, 1);
        LootNewCache(w, w.Player, 2);
        LootNewCache(w, w.Player, 3);
        Assert.Equal(1, w.Player.State.Inventory.CountOf(SoundersIds.DiscHunger)); // the caches skip it
    }

    [Fact]
    public void GiveSoundersItem_RecordsAStoryDisc_AsHandedOut()
    {
        using var w = World("loot_give");
        w.Server.GiveSoundersItem(w.Player, SoundersIds.DiscBeginning, 1);
        w.Server.GiveSoundersItem(w.Player, SoundersIds.SongSpindle, 1);

        Assert.True(Has(w.Player, SoundersIds.DiscBeginning));
        Assert.True(Has(w.Player, SoundersIds.SongSpindle));
        Assert.Contains(SongDiscs.GotMilestone(SoundersIds.DiscBeginning), w.Player.State.Milestones);
    }

    [Fact]
    public void TheRareFinds_ComeAtAboutTheirRates_AndAreSeedPurePerCache()
    {
        using var w = World("loot_rates");
        int spindles = 0, discs = 0;
        const int Caches = 4000;
        for (int i = 0; i < Caches; i++)
        {
            string key = $"monument:relic_cache:{i}:64:{-i}";
            var finds = w.Server.SounderRelicFindsForTest(key);
            spindles += finds.Count(s => s.Item == SoundersIds.SongSpindle);
            discs += finds.Count(s => s.Item == SoundersIds.DiscSong);
            Assert.Equal(finds.Select(s => s.Item), w.Server.SounderRelicFindsForTest(key).Select(s => s.Item));
        }

        Assert.InRange(discs, Caches * SvGameServer.SongDiscRelicChance * 0.6, Caches * SvGameServer.SongDiscRelicChance * 1.4);
        Assert.InRange(spindles, 4, 45); // 0.5 % of 4000 = 20
    }
}
