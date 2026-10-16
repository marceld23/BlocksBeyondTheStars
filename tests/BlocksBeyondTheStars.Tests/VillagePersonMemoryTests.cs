// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Friends with names (#2598) and "they remember when they last saw you" (#2595): every villager has a memory key of
/// their own (their bed or post cell), the old shared standing is a floor up to "known" that the named villager inherits
/// in full, talking to a person pays every 10 minutes, a friend hands over a present every half hour of playtime, and the
/// wall-clock stamps behind "welcome back".
/// <para>Every server start bakes a world (~10 s locally, ~16 s on a CI runner) — each test starts at most three, on a
/// seed known to qualify (the search only runs on if world generation ever changes).</para>
/// </summary>
[Collection(RealTimeSensitiveCollection.Name)]
public sealed class VillagePersonMemoryTests : IDisposable
{
    /// <summary>The first jungle seed whose primary settlement has a classic vendor, a quartermaster and three plain
    /// settlers with a bed — searched from here on when a world-generation change ever disqualifies it.</summary>
    private const long VillageSeed = 1;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_villagers_" + Guid.NewGuid().ToString("N"));
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    private SvGameServer Start(string world, long seed, out SqliteWorldRepository repo, bool settlements = true, int? terrainGeneration = null)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = settlements,
            PlaceWrecks = false,
            AiLevel = AiLevel.Off,
        };
        if (terrainGeneration is int generation)
        {
            config.World.TerrainGeneration = generation;
        }

        var server = new SvGameServer(config, Content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Plain settlers with a bed — and (#2601) without a personal wish for the test player "Friend": a wish opens
    /// before every other dialogue, so the memory tests talk to villagers who have none.</summary>
    private static List<(int Id, string Settlement, string Name, string Role, string Job, string PersonKey)> PlainSettlers(SvGameServer s, string settlement)
    {
        var wishGivers = s.WishGiverIdsForTest("Friend");
        return s.PersonKeysForTest.Where(n => n.Settlement == settlement && n.Role == "settler" && n.Job.Length == 0
                && n.PersonKey.Contains("#bed@", StringComparison.Ordinal)
                && s.NpcRosterForTest().First(r => r.Id == n.Id).CharacterId.Length == 0
                && !wishGivers.Contains(n.Id))
            .ToList();
    }

    private static bool Qualifies(SvGameServer s)
    {
        if (!s.HasSettlement || s.SettlementRuined)
        {
            return false;
        }

        string name = s.SettlementName;
        var people = s.PersonKeysForTest.Where(n => n.Settlement == name).ToList();
        return people.Any(n => n.Job == "vendor") && people.Any(n => n.Role == "quartermaster") && PlainSettlers(s, name).Count >= 3;
    }

    /// <summary>A started server on <see cref="VillageSeed"/> (or the next qualifying seed) with one player.</summary>
    private SvGameServer Village(string tag, out SqliteWorldRepository repo, out BlocksBeyondTheStars.GameServer.PlayerSession player, out long seed)
    {
        for (seed = VillageSeed; seed < VillageSeed + 40; seed++)
        {
            var server = Start($"{tag}_{seed}", seed, out repo);
            if (Qualifies(server))
            {
                player = server.AddLocalPlayer("Friend");
                player.State.AboardShip = false;
                return server;
            }

            server.Stop();
            repo.Dispose();
        }

        throw new Xunit.Sdk.XunitException("no qualifying village across 40 seeds");
    }

    private static void StandAt(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession player, int npcId)
        => player.State.Position = server.NpcSnapshots.First(n => n.Id == npcId).Home;

    private static void Talk(SvGameServer server, BlocksBeyondTheStars.GameServer.PlayerSession player, int npcId, int choice = 0)
    {
        StandAt(server, player, npcId);
        server.TalkToNpcForTest(player.State.PlayerId, npcId);
        server.ChooseDialogForTest(player.State.PlayerId, choice);
    }

    // ---------------- The person keys ----------------

    [Fact]
    public void EveryVillagerHasTheirOwnKey_TheStallAndTheBoardKeepTheRoleKey_AndARestartKeepsEveryoneInTheirSlot()
    {
        var server = Village("keys", out var repo, out _, out long seed);
        Dictionary<string, string> before;
        using (repo)
        {
            foreach (var group in server.PersonKeysForTest.GroupBy(n => n.Settlement))
            {
                string loc = server.SettlementLocationKeyForTest(group.Key);
                var keyed = group.Where(n => n.PersonKey.Length > 0).ToList();
                Assert.Equal(keyed.Count, keyed.Select(n => n.PersonKey).Distinct(StringComparer.Ordinal).Count()); // unique per villager
                foreach (var n in group)
                {
                    bool service = n.Job is "vendor" or "quartermaster" || n.Role is "quartermaster" or "guardian";
                    if (service)
                    {
                        Assert.Equal(string.Empty, n.PersonKey); // board, market, chains and radio hang on the role key
                        continue;
                    }

                    Assert.StartsWith(loc + "#", n.PersonKey, StringComparison.Ordinal);
                    Assert.EndsWith(":" + n.Role, n.PersonKey, StringComparison.Ordinal);
                    string slot = n.PersonKey.Substring(loc.Length + 1);
                    Assert.True(n.Job.Length > 0 ? slot.StartsWith(n.Job + "@", StringComparison.Ordinal)
                        : slot.StartsWith("bed@", StringComparison.Ordinal) || slot.StartsWith("settler#", StringComparison.Ordinal),
                        $"{n.Name} ({n.Role}/{n.Job}): unexpected slot '{slot}'");
                }
            }

            before = server.PersonKeysForTest.Where(n => n.PersonKey.Length > 0).ToDictionary(n => n.PersonKey, n => n.Name);
            server.Stop();
        }

        // The same world loaded again: every slot holds the same person (the memory key → the name a player knows).
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var again = Start($"keys_{seed}", seed, out var repo2);
        using (repo2)
        {
            var after = again.PersonKeysForTest.Where(n => n.PersonKey.Length > 0).ToDictionary(n => n.PersonKey, n => n.Name);
            Assert.Equal(before.OrderBy(kv => kv.Key, StringComparer.Ordinal), after.OrderBy(kv => kv.Key, StringComparer.Ordinal));
            again.Stop();
        }
    }

    /// <summary>E6 (Marcel 2026-10-14): today's villager names are kept — but they come from ONE world-wide random stream,
    /// so any change to an earlier settlement's draws renames everyone after it. This pin makes such a change deliberate:
    /// when it fails, decide whether renaming the villagers of existing worlds is acceptable, then update the list.
    /// Pinned per generation: from generation 26 the bodies are sized by the world seed (#2691), which moves the
    /// settlements of a new world and with them the draws — the generation-25 rows are the EXISTING worlds, the
    /// generation-26 row the worlds created today; a rename of either must be deliberate.</summary>
    [Theory]
    [InlineData(1L, 25)]
    [InlineData(2L, 25)]
    [InlineData(1L, 26)]
    [InlineData(2L, 26)]
    public void VillagerNames_ArePinnedPerSeed(long seed, int generation)
    {
        var server = Start($"names_{seed}_{generation}", seed, out var repo, terrainGeneration: generation);
        using (repo)
        {
            // The residents whose names come from the world-wide stream (not the professions, who draw from a generator of
            // their own; not an authored story face; not the machines): the first six, and the last four — those depend on
            // every draw before them.
            var authored = server.NpcRosterForTest().Where(r => r.CharacterId.Length > 0).Select(r => r.Id).ToHashSet();
            var stream = server.PersonKeysForTest
                .Where(n => n.Role != "guardian" && NpcProfessions.ByJob(n.Job) == null && !authored.Contains(n.Id))
                .OrderBy(n => n.Id)
                .Select(n => n.Name + "|" + n.Role + "|" + n.Job)
                .ToList();
            var actual = stream.Take(6).Concat(stream.Skip(Math.Max(6, stream.Count - 4))).ToArray();
            string[] expected = PinnedNames(seed, generation);
            Assert.True(expected.SequenceEqual(actual),
                $"Villager names of seed {seed} (generation {generation}) changed — expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
            server.Stop();
        }
    }

    private static string[] PinnedNames(long seed, int generation) => (seed, generation) switch
    {
        // Generation 26 coins the quartermasters from their settlement's place (#2698) and themes the settlement from it, so
        // the quartermasters' names differ — and a different trade changes which residents roll the android coin, so the
        // later draws of the world-wide stream differ too.
        (1, >= 26) => new[]
        {
            "Moust Gheizrullsko|vendor|vendor", "Stoth Shiotiokklaeth|quartermaster|quartermaster", "Kloch Laishqukro|settler|gardener",
            "Zych Toozress|settler|craftsman", "Vhoth Groossspuvheek|settler|innkeeper", "Gheich Yeqiol|settler|",
            "Druax Traiphuaz|settler|", "Me Speekivo|settler|", "Sheedrech Mexaen|settler|", "Kuam Zreeljedrei|settler|",
        },
        (1, _) => new[]
        {
            "Moust Gheizrullsko|vendor|vendor", "Kron Zuavroo|quartermaster|quartermaster", "Kloch Laishqukro|settler|gardener",
            "Zych Toozress|settler|craftsman", "Vhoth Groossspuvheek|settler|innkeeper", "Gheich Yeqiol|settler|",
            "Telae Klaskgroum|settler|", "Shooth Thoosill|settler|", "Phoutrioch Kruqum|settler|", "Kethourk Maeghill|settler|",
        },
        (_, >= 26) => new[]
        {
            "Ghuast Dromeyok|vendor|vendor", "Speishreex Naephkispeiz|vendor|vendor", "Troophstai Skivheingyyz|quartermaster|quartermaster",
            "Tuarkvuash Plioghorn|vendor|vendor", "Grillkum Klovreerk|quartermaster|quartermaster", "Laph Roobreesk|settler|gardener",
            "Souplo Qemiokdrou|settler|", "Kistthul Kreneimeich|settler|", "Lyzroox Rerusk|settler|", "Vrooch Ghekoorkkreem|settler|",
        },
        _ => new[]
        {
            "Ghuast Dromeyok|vendor|vendor", "Klush Manaephki|vendor|vendor", "Leng Truagruvrio|quartermaster|quartermaster",
            "Briork Xeetuash|vendor|vendor", "Rooth Plaedruaqouph|quartermaster|quartermaster", "Qeek Drairkleerkplir|settler|gardener",
            "Yist Rioriornyai|settler|", "Stost Treross|settler|", "Tystooth Phavhithith|settler|", "Zrualljaill Sykroox|settler|",
        },
    };

    // ---------------- Two people, two friends to make ----------------

    [Fact]
    public void TwoVillagers_AreTwoPeopleToKnow_FirstMeetingPays_AndTalkingPaysEveryTenMinutes()
    {
        var server = Village("two", out var repo, out var player, out _);
        using (repo)
        {
            var p = player.State;
            var settlers = PlainSettlers(server, server.SettlementName);
            var (a, b) = (settlers[0], settlers[1]);

            Talk(server, player, a.Id); // settler_neighbours, "how is life here?" → standing:1
            Talk(server, player, b.Id);

            // Two entries, each with its own name — no longer one shared "settler" renamed to whoever spoke last.
            Assert.Equal(a.Name, p.NpcMemory[a.PersonKey].Name);
            Assert.Equal(b.Name, p.NpcMemory[b.PersonKey].Name);
            Assert.Equal(server.SettlementName, p.NpcMemory[a.PersonKey].Place);

            // First meeting lifts to 3, the talk +1, the answer's standing +1.
            Assert.Equal(5, p.NpcMemory[a.PersonKey].Value);
            Assert.Equal(5, p.NpcMemory[b.PersonKey].Value);
            Assert.False(string.IsNullOrEmpty(p.NpcMemory[a.PersonKey].LastSeenUtc)); // #2595: the interaction stamps

            // Spam pays nothing; ten minutes later (a person's cooldown, not the role key's hour) it pays again.
            Talk(server, player, a.Id);
            Assert.Equal(5, p.NpcMemory[a.PersonKey].Value);
            server.AdvanceUptimeForTest(601);
            Talk(server, player, a.Id);
            Assert.Equal(7, p.NpcMemory[a.PersonKey].Value);

            // A passing hello: +1 once per session for someone already met, never for a stranger, never past 39.
            var c = settlers[2];
            Assert.False(server.TryHelloStandingForTest(p.PlayerId, c.PersonKey));
            Assert.False(p.NpcMemory.ContainsKey(c.PersonKey));
            Assert.True(server.TryHelloStandingForTest(p.PlayerId, a.PersonKey));
            Assert.Equal(8, p.NpcMemory[a.PersonKey].Value);
            Assert.False(server.TryHelloStandingForTest(p.PlayerId, a.PersonKey)); // once per session
            p.NpcMemory[b.PersonKey].Value = 38;
            Assert.True(server.TryHelloStandingForTest(p.PlayerId, b.PersonKey));
            Assert.Equal(39, p.NpcMemory[b.PersonKey].Value); // a hello makes you known, never a friend
            p.NpcMemory[b.PersonKey].Value = 39;
            Assert.False(server.TryHelloStandingForTest(p.PlayerId, b.PersonKey));

            // The festival / camp bonus helper: villagers the player knows, only.
            Assert.Equal(2, server.AddStandingToKnownVillagersForTest(p.PlayerId, server.SettlementName, 2));
            Assert.Equal(10, p.NpcMemory[a.PersonKey].Value);
            Assert.False(p.NpcMemory.ContainsKey(c.PersonKey));
            server.Stop();
        }
    }

    // ---------------- The old shared standing: floor + migration ----------------

    [Fact]
    public void TheOldSharedStanding_IsAFloorUpToKnown_TheNamedVillagerInheritsIt_AndTheRestBecomesVillagers()
    {
        var server = Village("floor", out var repo, out var player, out _);
        using (repo)
        {
            var p = player.State;
            string place = server.SettlementName;
            string loc = server.SettlementLocationKeyForTest(place);
            var settlers = PlainSettlers(server, place);
            var (named, other) = (settlers[0], settlers[1]);

            // A save from before #2598: one shared "settler" entry, named after whoever the player spoke to last.
            p.NpcMemory[loc + ":settler"] = new NpcRelationship
            {
                Name = named.Name,
                Role = "settler",
                Place = place,
                Value = 45,
                Log = { new NpcInteraction { Kind = NpcInteractionKind.Dialog }, new NpcInteraction { Kind = NpcInteractionKind.Trade } },
            };

            // Before the migration: the floor — everyone reads "known", nobody a friend.
            Assert.Equal(39, server.RelationshipValueForTest(p.PlayerId, named.PersonKey));
            Assert.Equal("npc.stage.known", server.NpcStageForTest(p.PlayerId, other.Id));

            server.MigrateNpcMemoryForTest(p.PlayerId);

            // The villager "People you know" showed inherits the full standing and the log …
            var heir = p.NpcMemory[named.PersonKey];
            Assert.Equal(45, heir.Value);
            Assert.Equal(named.Name, heir.Name);
            Assert.Equal(2, heir.Log.Count);
            Assert.Equal("npc.stage.trusted", server.NpcStageForTest(p.PlayerId, named.Id));

            // … the rest of the old entry is "<place> · Villagers", capped at known — the floor for everyone else.
            var villagers = p.NpcMemory[loc + ":settler"];
            Assert.Equal("villagers", villagers.Role);
            Assert.Equal(place, villagers.Name);
            Assert.Equal(39, villagers.Value);
            Assert.Equal("npc.stage.known", server.NpcStageForTest(p.PlayerId, other.Id));
            Assert.False(p.NpcMemory.ContainsKey(other.PersonKey));
            Assert.Contains("npcmig:" + loc, p.Milestones);

            // Once only: a second pass changes nothing.
            heir.Value = 50;
            server.MigrateNpcMemoryForTest(p.PlayerId);
            Assert.Equal(50, p.NpcMemory[named.PersonKey].Value);
            Assert.Equal(39, p.NpcMemory[loc + ":settler"].Value);

            // The roster: the friend by name, the village as "Villagers".
            var roster = server.KnownNpcsForTest(p.PlayerId);
            Assert.Contains(roster, r => r.Name == named.Name && r.RoleKey == "npc.role.settler" && r.StageKey == "npc.stage.trusted");
            Assert.Contains(roster, r => r.Name == place && r.RoleKey == "npc.role.villagers" && r.StageKey == "npc.stage.known");

            // Meeting the other villager starts from the floor (no first-meeting lift on top of an acquaintance): 39 + the
            // talk, + 1 more when the answer carries standing (a "known" settler may open with the crystal gift instead).
            Talk(server, player, other.Id);
            Assert.InRange(p.NpcMemory[other.PersonKey].Value, 40, 41);
            server.Stop();
        }
    }

    // ---------------- The friend's present ----------------

    [Fact]
    public void AFriendHandsOverAPresent_EveryHalfHourOfPlaytime_AndAFullBagKeepsItForNextTime()
    {
        var server = Village("gift", out var repo, out var player, out _);
        using (repo)
        {
            var p = player.State;
            var friend = PlainSettlers(server, server.SettlementName)[0];
            var floorOnly = PlainSettlers(server, server.SettlementName)[1];
            string loc = server.SettlementLocationKeyForTest(server.SettlementName);
            p.NpcMemory[friend.PersonKey] = new NpcRelationship { Name = friend.Name, Role = "settler", Value = 45 };
            p.NpcMemory[loc + ":settler"] = new NpcRelationship { Name = "Old", Role = "villagers", Value = 60 }; // floor 39

            // A friend of only the floor gets the ordinary talk — "friend" is earned per person.
            StandAt(server, player, floorOnly.Id);
            server.TalkToNpcForTest(p.PlayerId, floorOnly.Id);
            Assert.NotEqual("friend_gift", server.ActiveDialogForTest(p.PlayerId)?.Key);
            server.ChooseDialogForTest(p.PlayerId, 1);

            // The friend: a present first.
            StandAt(server, player, friend.Id);
            int itemsBefore = p.Inventory.Slots.Sum(s => s?.Count ?? 0);
            server.TalkToNpcForTest(p.PlayerId, friend.Id);
            Assert.Equal(("friend_gift", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.True(p.Inventory.Slots.Sum(s => s?.Count ?? 0) > itemsBefore, "the present should be in the bag");
            double givenAt = p.NpcMemory[friend.PersonKey].LastGiftAt;
            Assert.True(givenAt > 0);

            // Not again right away …
            server.TalkToNpcForTest(p.PlayerId, friend.Id);
            Assert.NotEqual("friend_gift", server.ActiveDialogForTest(p.PlayerId)?.Key);
            server.ChooseDialogForTest(p.PlayerId, 1);

            // … but after half an hour of world playtime.
            server.Metadata.CumulativePlaytimeSeconds += (long)SvGameServer.FriendGiftCooldownSecondsForTest + 1;
            for (int i = 0; i < p.Inventory.SlotCount; i++)
            {
                p.Inventory.SetSlot(i, new ItemStack("stone", 1024)); // a bag that fits nothing more
            }

            server.TalkToNpcForTest(p.PlayerId, friend.Id);
            Assert.Equal(("friend_gift", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.Equal(givenAt, p.NpcMemory[friend.PersonKey].LastGiftAt); // the full bag did not spend the cooldown

            // Room again → the same present waits.
            p.Inventory.SetSlot(0, null);
            server.TalkToNpcForTest(p.PlayerId, friend.Id);
            Assert.Equal(("friend_gift", 0), server.ActiveDialogForTest(p.PlayerId));
            server.ChooseDialogForTest(p.PlayerId, 0);
            Assert.True(p.NpcMemory[friend.PersonKey].LastGiftAt > givenAt);

            // The stall and the board never hand out friend presents (villagers only, E8).
            var vendor = server.PersonKeysForTest.First(n => n.Settlement == server.SettlementName && n.Job == "vendor");
            p.NpcMemory[loc + ":vendor"] = new NpcRelationship { Name = vendor.Name, Role = "vendor", Value = 60 };
            server.Metadata.CumulativePlaytimeSeconds += 4000;
            StandAt(server, player, vendor.Id);
            server.TalkToNpcForTest(p.PlayerId, vendor.Id);
            Assert.NotEqual("friend_gift", server.ActiveDialogForTest(p.PlayerId)?.Key);
            server.Stop();
        }
    }

    [Fact]
    public void TheFriendGiftPool_NamesRealItems_AndTheDialogueTextsResolve()
    {
        Assert.NotEmpty(Content.VillageLife.FriendGifts);
        foreach (var gift in Content.VillageLife.FriendGifts)
        {
            Assert.NotNull(Content.GetItem(gift.Item));
            Assert.InRange(gift.Count, 1, 10); // small presents, never power creep
        }

        var dialog = Assert.Single(Content.Dialogs, d => d.Key == "friend_gift");
        Assert.Equal("_friend", dialog.Role); // never matches an NPC on its own — only the friend pre-check picks it

        // The raw tables (a German Has() would fall back to English and hide a missing translation).
        foreach (string locale in new[] { "en", "de" })
        {
            var table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(Path.Combine(TestPaths.DataDir(), "locales", locale + ".json")))!;
            foreach (string key in new[]
            {
                "dlg.friend_gift.prompt", "dlg.friend_gift.c_take", "dlg.friend_gift.r_take", "dlg.friend_gift.full",
                "npc.role.villagers", "npc.role.innkeeper",
            })
            {
                Assert.True(table.ContainsKey(key) && table[key].Length > 0, $"{locale}: missing '{key}'");
            }
        }
    }

    [Fact]
    public void AVillageLifeFile_WithAnUnknownGiftItem_FailsValidation()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetVillageLife(new VillageLifeDefinition { FriendGifts = { new ItemAmount("no_such_item", 2) } });
        var ex = Assert.Throws<ContentValidationException>(() => content.Validate());
        Assert.Contains(ex.Problems, m => m.Contains("no_such_item", StringComparison.Ordinal));
    }

    // ---------------- Keys as strings ----------------

    [Theory]
    [InlineData("settle_123#bed@4.1.7:settler", "settle_123", true, "")]
    [InlineData("settle_123#innkeeper@3.1.9:settler", "settle_123", true, "innkeeper")]
    [InlineData("settle_123#doctor@0.1.2:vendor", "settle_123", true, "doctor")]
    [InlineData("settle_123#settler#2:settler", "settle_123", true, "")]
    [InlineData("settle_123:vendor", "settle_123", false, "")]
    [InlineData("base_5#2:settler", "base_5", false, "")]
    [InlineData("station_77:quartermaster", "station_77", false, "")]
    public void PersonKeys_ParseToTheirPlaceAndJob(string key, string place, bool person, string job)
    {
        Assert.Equal(place, SvGameServer.PlaceOfNpcKeyForTest(key));
        Assert.Equal(person, SvGameServer.IsPersonKeyForTest(key));
        Assert.Equal(job, SvGameServer.JobOfPersonKeyForTest(key));
    }

    // ---------------- Last online + welcome back (#2595) ----------------

    private static string Iso(DateTime t) => t.ToString("o", CultureInfo.InvariantCulture);

    [Fact]
    public void LastOnline_IsStampedOnLeaveAndOnSave_AndTheNextJoinKeepsThePreviousValue()
    {
        var server = Start("lastseen", 3, out var repo, settlements: false);
        using (repo)
        {
            var t0 = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            server.SetWallClockForTest(t0);
            var first = server.AddLocalPlayer("Wanderer");
            Assert.Equal(string.Empty, first.PreviousSeenUtc); // a brand-new player was never online before
            Assert.Equal(Iso(t0), first.State.LastSeenUtc);

            server.SetWallClockForTest(t0.AddMinutes(30));
            server.SaveAllForTest(); // online right now: an autosave keeps "last seen" current
            Assert.Equal(Iso(t0.AddMinutes(30)), first.State.LastSeenUtc);

            server.SetWallClockForTest(t0.AddHours(1));
            server.DisconnectLocalPlayerForTest("Wanderer"); // leaving writes the moment they left

            server.SetWallClockForTest(t0.AddDays(2));
            var back = server.AddLocalPlayer("Wanderer");
            Assert.Equal(Iso(t0.AddHours(1)), back.PreviousSeenUtc); // read BEFORE the join overwrote it
            Assert.Equal(Iso(t0.AddDays(2)), back.State.LastSeenUtc);
            server.Stop();
        }
    }

    [Fact]
    public void WelcomeBack_IsDueAfter18Hours_ForAcquaintancesOnly_AndAnEmptyStampOnlyStamps()
    {
        var server = Start("welcome", 4, out var repo, settlements: false);
        using (repo)
        {
            var t0 = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
            server.SetWallClockForTest(t0);
            var p = server.AddLocalPlayer("Returner").State;
            const string Known = "settle_1#bed@1.2.3:settler";
            const string Stranger = "settle_1#bed@4.2.3:settler";
            p.NpcMemory[Known] = new NpcRelationship { Name = "Mira", Role = "settler", Value = 20 }; // a save from before the stamp
            p.NpcMemory[Stranger] = new NpcRelationship { Name = "Tom", Role = "settler", Value = 5, LastSeenUtc = Iso(t0.AddDays(-3)) };

            // An empty stamp never reads as a long absence — it is only stamped (no flood of welcomes after the update).
            Assert.False(server.NpcWelcomeBackDueForTest("Returner", Known));
            server.StampNpcSeenForTest("Returner", Known);
            Assert.Equal(Iso(t0), p.NpcMemory[Known].LastSeenUtc);

            server.SetWallClockForTest(t0.AddHours(17));
            Assert.False(server.NpcWelcomeBackDueForTest("Returner", Known));
            server.SetWallClockForTest(t0.AddHours(19));
            Assert.True(server.NpcWelcomeBackDueForTest("Returner", Known));

            // Seen again → no longer due; a stranger is never welcomed back, however long ago.
            server.StampNpcSeenForTest("Returner", Known);
            Assert.False(server.NpcWelcomeBackDueForTest("Returner", Known));
            Assert.False(server.NpcWelcomeBackDueForTest("Returner", Stranger));

            // Stamping never creates an acquaintance.
            server.StampNpcSeenForTest("Returner", "settle_1#bed@9.2.3:settler");
            Assert.False(p.NpcMemory.ContainsKey("settle_1#bed@9.2.3:settler"));
            server.Stop();
        }
    }
}
