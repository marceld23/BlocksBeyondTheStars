// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — children and household pets: a child plays by day (short runs, a cheer when it catches a
/// playmate), answers a player with a written line of its own and is nobody's counterpart for wishes, dialogues or
/// friendship; children talk about their own things with each other only; a family house's marked bed houses a child and
/// no grown-up; a household or two keep a small animal that exists while a player is near.
/// </summary>
public sealed class ChildVillagerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Kidsham";
    private const double Day = 0.40;

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        return w;
    }

    private static Vector3f At(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet, NpcLifeWorld.Cz + dz + 0.5f);

    private static int Child(NpcLifeWorld w, float dx, float dz)
    {
        int id = w.Server.SpawnVillagerForTest(Village, At(w, dx, dz));
        w.Server.MakeChildForTest(id);
        return id;
    }

    private static IEnumerable<T> Sent<T>(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<T>();

    private static readonly Lazy<BlocksBeyondTheStars.Shared.Localization.Localizer> English = new(() => Content.Value.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.English));

    private static string En(string key) => English.Value.Get(key);

    [Fact]
    public void AChild_AnswersWithItsOwnLines_AndIsNobodysCounterpart()
    {
        using var w = World("child_talk");
        int child = Child(w, 3, 0);
        w.Player.State.Position = At(w, 2, 0);
        string me = w.Player.State.PlayerId;

        w.Server.TalkToNpcForTest(me, child);
        var first = Sent<NpcGreeting>(w).Last(g => g.NpcId == child);
        Assert.Equal(En("npc.child.talk.1"), first.Text);
        Assert.Equal(NpcGestures.Wave, first.Gesture);
        Assert.Equal(SvGameServer.ChildRole, first.Role);
        Assert.Null(w.Server.ActiveDialogForTest(me)); // no dialogue panel

        w.Server.TalkToNpcForTest(me, child);
        Assert.Equal(En("npc.child.talk.2"), Sent<NpcGreeting>(w).Last(g => g.NpcId == child).Text); // the next one, in turn

        Assert.Empty(w.Player.State.NpcMemory);                          // nothing remembered, no standing
        Assert.DoesNotContain(child, w.Server.WishGiverIdsForTest(me)); // no wish
        Assert.DoesNotContain(child, w.Server.PetKeepersForTest);

        // Every answer and every hello is written, in English and in German.
        var de = Content.Value.CreateLocalizer(BlocksBeyondTheStars.Shared.Localization.GameLocale.German);
        for (int i = 1; i <= SvGameServer.ChildTalkLines; i++)
        {
            Assert.NotEqual($"npc.child.talk.{i}", En($"npc.child.talk.{i}"));
            Assert.NotEqual($"npc.child.talk.{i}", de.Get($"npc.child.talk.{i}"));
        }

        for (int i = 1; i <= SvGameServer.ChildHelloLines; i++)
        {
            Assert.NotEqual($"npc.child.hello.{i}", En($"npc.child.hello.{i}"));
            Assert.NotEqual($"npc.child.hello.{i}", de.Get($"npc.child.hello.{i}"));
        }
    }

    [Fact]
    public void Children_PlayByDay_RunToEachOther_AndWaveAtThePlayer()
    {
        using var w = World("child_play");
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.BenchMarker, new Vector3f(NpcLifeWorld.Cx + 6.5f, w.Feet + 0.5f, NpcLifeWorld.Cz + 6.5f));
        int a = Child(w, -6, 0), b = Child(w, 6, 0);
        var startA = w.Server.NpcRoutineForTest(a).Pos;
        var startB = w.Server.NpcRoutineForTest(b).Pos;

        bool ran = false, moved = false;
        var activities = new HashSet<string>();
        w.TickAt(Day, 240, until: () => ran && moved && Sent<NpcEmote>(w).Any(e => e.Gesture == NpcGestures.Cheer), every: () =>
        {
            ran |= w.Server.ChildPlayingForTest(a) || w.Server.ChildPlayingForTest(b);
            var ra = w.Server.NpcRoutineForTest(a);
            var rb = w.Server.NpcRoutineForTest(b);
            moved |= Dist(ra.Pos, startA) > 3 || Dist(rb.Pos, startB) > 3;
            activities.Add(ra.Activity);
            activities.Add(rb.Activity);
        });

        Assert.True(ran, "no child ever set out on a run");
        Assert.True(moved, "the children stood where they were put");
        Assert.Contains("npc.activity.playing", activities);
        Assert.Contains(Sent<NpcEmote>(w), e => e.Gesture == NpcGestures.Cheer && (e.NpcId == a || e.NpcId == b)); // caught!
        Assert.Contains(Sent<NpcEmote>(w), e => e.Gesture == NpcGestures.Wave);                                    // the player stands on the pad
    }

    [Fact]
    public void AChildsEvening_BeginsBeforeTheGrownUps()
    {
        using var w = World("child_evening");
        int child = Child(w, 4, 4);
        int adult = w.Server.SpawnVillagerForTest(Village, At(w, -4, 4), errands: true);
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.BenchMarker, new Vector3f(NpcLifeWorld.Cx + 8.5f, w.Feet + 0.5f, NpcLifeWorld.Cz - 8.5f));

        // 0.60: the child's day is over (no more runs, the "playing" is off its nameplate), the grown-up's is not.
        w.TickAt(0.40, 30);
        w.TickAt(0.60, 20);
        Assert.False(w.Server.ChildPlayingForTest(child));
        Assert.NotEqual("npc.activity.playing", w.Server.NpcRoutineForTest(child).Activity);
        bool adultStillOut = false;
        w.TickAt(0.60, 120, until: () => adultStillOut, every: () => adultStillOut |= w.Server.NpcErrandForTest(adult).OnErrand);
        Assert.True(adultStillOut, "the grown-up's day ended with the child's");
    }

    [Fact]
    public void Children_TalkAboutTheirOwnThings_WithEachOtherOnly()
    {
        using var w = World("child_chatter");
        int a = Child(w, 2, 0), b = Child(w, 3, 0);
        int grownA = w.Server.SpawnVillagerForTest(Village, At(w, -2, 0)), grownB = w.Server.SpawnVillagerForTest(Village, At(w, -3, 0));
        string me = w.Player.State.PlayerId;

        var kids = w.Server.ChatterTopicsForTest(a, b, me);
        Assert.NotEmpty(kids);
        Assert.All(kids, t => Assert.StartsWith("kids_", t.Topic));

        var grown = w.Server.ChatterTopicsForTest(grownA, grownB, me);
        Assert.NotEmpty(grown);
        Assert.All(grown, t => Assert.False(t.Topic.StartsWith("kids_", StringComparison.Ordinal), $"grown-ups got the children's topic {t.Topic}"));

        Assert.Empty(w.Server.ChatterTopicsForTest(a, grownA, me)); // a child and a grown-up share none
        Assert.Empty(w.Server.ChatterTopicsForTest(grownA, a, me));

        // The children's topics are data: "who" marks them, and each has its lines in both mandatory languages.
        var topics = Content.Value.VillageLife.Chatter.Where(t => t.Who == BlocksBeyondTheStars.Shared.Definitions.VillageChatterTopic.WhoChildren).ToList();
        Assert.InRange(topics.Count, 6, 8);
        Assert.All(topics, t => Assert.Equal(string.Empty, t.Needs));
    }

    [Fact]
    public void AHouseholdPet_ExistsWhileAPlayerIsNear_AndFollowsNobodyElse()
    {
        using var w = World("house_pet");
        int keeper = w.Server.SpawnVillagerForTest(Village, At(w, 5, 0));
        int other = w.Server.SpawnVillagerForTest(Village, At(w, -5, 0));
        w.Server.MakePetKeeperForTest(keeper);
        w.Player.State.Position = At(w, 0, 0);

        w.Server.TickProfessionsForTest();
        var pet = Assert.Single(w.Server.NpcPetsForTest);
        Assert.Equal(keeper, pet.NpcId);
        Assert.False(string.IsNullOrEmpty(pet.SpeciesId));
        w.Server.TickProfessionsForTest();
        Assert.Single(w.Server.NpcPetsForTest); // one, not one per look

        // Nobody near any more: the animal is gone — and back when somebody comes again.
        w.Player.State.Position = new Vector3f(NpcLifeWorld.Cx + 400, w.Feet, NpcLifeWorld.Cz);
        w.Server.TickProfessionsForTest();
        Assert.Empty(w.Server.NpcPetsForTest);
        w.Player.State.Position = At(w, 0, 0);
        w.Server.TickProfessionsForTest();
        Assert.Equal(keeper, Assert.Single(w.Server.NpcPetsForTest).NpcId);
        Assert.DoesNotContain(w.Server.NpcPetsForTest, p => p.NpcId == other);
    }

    // ---------------------------------------------------------------- a generated lively world

    private static SvGameServer Start(string root, string world, long seed, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(root, world));
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = true,
            PlaceWrecks = false,
            AiLevel = AiLevel.Off,
        };
        var server = new SvGameServer(config, Content.Value, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    [Fact]
    public void AFamilyHousesMarkedBed_HousesAChild_NeverAGrownUp_AndTheWorldStaysTheSameOnReload()
    {
        string root = Path.Combine(Path.GetTempPath(), "bbts_children_" + Guid.NewGuid().ToString("N"));
        try
        {
            int worldsWithChildren = 0;
            for (long seed = 1; seed <= 12 && worldsWithChildren < 2; seed++)
            {
                var server = Start(root, $"kids_{seed}", seed, out var repo);
                List<(int Id, string Settlement, string Name, float Size, Vector3i? Bed)> children;
                using (repo)
                {
                    children = server.ChildrenForTest.ToList();
                    var people = server.PersonKeysForTest;
                    foreach (var group in children.GroupBy(c => c.Settlement))
                    {
                        Assert.InRange(group.Count(), 1, 3); // two in a hamlet or a village, three in a town or a city
                    }

                    Assert.All(children, c =>
                    {
                        Assert.NotNull(c.Bed);
                        Assert.InRange(c.Size, 0.55f, 0.72f);
                        Assert.False(string.IsNullOrWhiteSpace(c.Name));
                    });
                    Assert.Equal(children.Count, children.Select(c => c.Bed).Distinct().Count()); // a bed each

                    // A child has no person key (no memory hangs on it), no job, and the role "child".
                    var childIds = children.Select(c => c.Id).ToHashSet();
                    Assert.All(people.Where(p => childIds.Contains(p.Id)), p =>
                    {
                        Assert.Equal(SvGameServer.ChildRole, p.Role);
                        Assert.Equal(string.Empty, p.PersonKey);
                        Assert.Equal(string.Empty, p.Job);
                    });

                    // At most two households of a place keep a pet, and never a child or a post holder.
                    foreach (var group in people.Where(p => server.PetKeepersForTest.Contains(p.Id)).GroupBy(p => p.Settlement))
                    {
                        Assert.InRange(group.Count(), 1, SvGameServer.HouseholdPetsPerPlace);
                        Assert.All(group, p => Assert.Equal(("settler", string.Empty), (p.Role, p.Job)));
                    }

                    server.Stop();
                }

                if (children.Count == 0)
                {
                    continue;
                }

                worldsWithChildren++;
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                var again = Start(root, $"kids_{seed}", seed, out var repo2);
                using (repo2)
                {
                    Assert.Equal(children.Select(c => (c.Settlement, c.Name, c.Bed)).OrderBy(c => c.Name, StringComparer.Ordinal),
                        again.ChildrenForTest.Select(c => (c.Settlement, c.Name, c.Bed)).OrderBy(c => c.Name, StringComparer.Ordinal));
                    again.Stop();
                }
            }

            Assert.True(worldsWithChildren > 0, "twelve lively worlds without a single child: no family house was ever placed");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort temp cleanup
            }
        }
    }

    private static double Dist(Vector3f a, Vector3f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
}
