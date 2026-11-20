// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — a villager's day: somebody without a post goes round the anchors of its settlement by day (the
/// plaza, the market stall, the notice board, a well …), stays a while at each with something to do on its nameplate, and
/// is home again in the evening; arriving beside a neighbour both nod; the village gardener works the field's edge and
/// carries a basket to the stall. Nothing changes hands.
/// </summary>
public sealed class NpcErrandTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Errandville";
    private const double Day = 0.40;
    private const double Evening = 0.72;

    private static readonly string[] ErrandActivities =
    {
        "npc.activity.shopping", "npc.activity.reading_board", "npc.activity.visiting", "npc.activity.checking_crops",
        "npc.activity.watching_animals", "npc.activity.fetching_water", "npc.activity.resting", "npc.activity.warming",
        "npc.activity.looking_out", "npc.activity.strolling",
    };

    /// <summary>A village box of 41 × 41 round the pad's middle, its ground row the pad.</summary>
    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        w.Server.SetLocalDayFractionForTest(Day, NpcLifeWorld.Cx);
        return w;
    }

    private static Vector3f At(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet, NpcLifeWorld.Cz + dz + 0.5f);

    private static Vector3f Marker(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet + 0.5f, NpcLifeWorld.Cz + dz + 0.5f);

    [Fact]
    public void TheAnchors_ComeFromTheMarkers_AndEveryOneIsASpotToStandOn()
    {
        using var w = World("errand_anchors");
        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 12, 0));
        w.Server.AddSettlementMarkerForTest(Village, "mission_board", Marker(w, -12, 6));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.WellMarker, Marker(w, 4, 8));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.BenchMarker, Marker(w, -5, -9));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.FieldMarker, Marker(w, 8, -16), "w=6;l=4");
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.RoadExitMarker, Marker(w, 0, -19), "side=0");

        var anchors = w.Server.PlaceAnchorsForTest(Village);
        Assert.Equal(4, anchors.Count(a => a.Kind == "Plaza"));
        foreach (string kind in new[] { "Market", "Board", "Well", "Bench", "Field", "Exit" })
        {
            Assert.Single(anchors, a => a.Kind == kind);
        }

        Assert.All(anchors, a => Assert.Equal(w.Feet, a.Spot.Y)); // on the pad, feet level
        Assert.Equal(anchors.Count, anchors.Select(a => a.Spot.ToBlock()).Distinct().Count()); // no two on one cell

        // The visitor's place at a post is a step away from it — the vendor stands on the marker.
        var market = anchors.Single(a => a.Kind == "Market").Spot;
        double fromPost = Math.Sqrt(Math.Pow(market.X - (NpcLifeWorld.Cx + 12.5), 2) + Math.Pow(market.Z - (NpcLifeWorld.Cz + 0.5), 2));
        Assert.InRange(fromPost, 1.2, 4.5);

        // A field's anchor lies on the ring just outside it.
        var field = anchors.Single(a => a.Kind == "Field").Spot.ToBlock();
        int fx = NpcLifeWorld.Cx + 8, fz = NpcLifeWorld.Cz - 16;
        Assert.True(field.X == fx - 1 || field.X == fx + 6 || field.Z == fz - 1 || field.Z == fz + 4, $"the field anchor {field} is not on the field's ring");
    }

    [Fact]
    public void AVillager_GoesRoundTheAnchorsByDay_StaysAWhileAtEach_AndIsHomeInTheEvening()
    {
        using var w = World("errand_round");
        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 12, 0));
        w.Server.AddSettlementMarkerForTest(Village, "mission_board", Marker(w, -12, 6));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.WellMarker, Marker(w, 4, 8));
        var home = At(w, -14, -14);
        int id = w.Server.SpawnVillagerForTest(Village, home, errands: true);

        var visited = new List<string>();
        var activities = new HashSet<string>();
        bool leftHome = false;
        w.TickAt(Day, 600, until: () => visited.Count >= 3, every: () =>
        {
            var e = w.Server.NpcErrandForTest(id);
            var r = w.Server.NpcRoutineForTest(id);
            leftHome |= Dist(r.Pos, home) > 6;
            if (e.OnErrand && e.Arrived)
            {
                activities.Add(r.Activity);
                if (visited.Count == 0 || visited[^1] != e.Kind + "@" + r.Pos.ToBlock())
                {
                    if (!visited.Contains(e.Kind + "@" + r.Pos.ToBlock()) || visited[^1] != e.Kind + "@" + r.Pos.ToBlock())
                    {
                        visited.Add(e.Kind + "@" + r.Pos.ToBlock());
                    }
                }
            }
        });

        Assert.True(leftHome, "the villager never left home");
        Assert.True(visited.Count >= 3, $"only {visited.Count} stays in ten minutes: {string.Join(", ", visited)}");
        Assert.All(activities, a => Assert.Contains(a, ErrandActivities));
        Assert.DoesNotContain(visited, v => v.StartsWith("Lamp", StringComparison.Ordinal));

        // The evening: the errand ends, the villager walks home and rests there.
        w.TickAt(Evening, 120, until: () => !w.Server.NpcErrandForTest(id).OnErrand && Dist(w.Server.NpcRoutineForTest(id).Pos, home) < 2.5);
        var evening = w.Server.NpcRoutineForTest(id);
        Assert.False(w.Server.NpcErrandForTest(id).OnErrand);
        Assert.True(Dist(evening.Pos, home) < 2.5, $"not home in the evening: {evening.Pos}");
        Assert.DoesNotContain(evening.Activity, ErrandActivities.Where(a => a != "npc.activity.resting"));
        Assert.Equal(string.Empty, evening.Held);
    }

    [Fact]
    public void AVillagerThatNeverSetsOut_StandsWhereItStood()
    {
        using var w = World("errand_off");
        var home = At(w, 6, 6);
        int id = w.Server.SpawnVillagerForTest(Village, home); // the fixtures' default: no errands
        w.TickAt(Day, 120);
        Assert.False(w.Server.NpcErrandForTest(id).OnErrand);
        Assert.True(Dist(w.Server.NpcRoutineForTest(id).Pos, home) < 2.2); // it strolls on its leash, as it always did
    }

    [Fact]
    public void ArrivingBesideANeighbour_BothNod_WithoutALine()
    {
        using var w = World("errand_nod");
        // The only anchors are the plaza's four spots; a craftsman stands in their middle (no errands for somebody with a post).
        int neighbour = w.Server.SpawnVillagerForTest(Village, At(w, 0, 0), job: "craftsman");
        int walker = w.Server.SpawnVillagerForTest(Village, At(w, -14, -14), errands: true);

        w.TickAt(Day, 300, until: () => Emotes(w).Count >= 2);

        var emotes = Emotes(w);
        Assert.Contains(emotes, e => e.NpcId == walker && e.Gesture == NpcGestures.Nod);
        Assert.Contains(emotes, e => e.NpcId == neighbour && e.Gesture == NpcGestures.Nod);
        Assert.Equal(2, emotes.Count); // once each — the next nod waits
    }

    [Fact]
    public void TheVillageGardener_WorksTheFieldsEdge_AndCarriesABasketToTheStall()
    {
        using var w = World("errand_gardener");
        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 10, 4));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.FieldMarker, Marker(w, -6, -16), "w=6;l=4");
        var greenhouse = At(w, -10, 2);
        int id = w.Server.SpawnVillagerForTest(Village, greenhouse, job: "gardener");

        bool atField = false, carried = false, delivered = false, backWithoutBasket = false;
        var field = w.Server.PlaceAnchorsForTest(Village).Single(a => a.Kind == "Field").Spot;
        var stall = w.Server.PlaceAnchorsForTest(Village).Single(a => a.Kind == "Market").Spot;
        w.TickAt(Day, 600, until: () => backWithoutBasket, every: () =>
        {
            var r = w.Server.NpcRoutineForTest(id);
            atField |= Dist(r.Pos, field) < 1.6;
            carried |= atField && r.Held == "npc_basket" && r.Activity == "npc.activity.carrying";
            delivered |= carried && Dist(r.Pos, stall) < 1.6 && r.Held == "npc_basket";
            backWithoutBasket |= delivered && Dist(r.Pos, greenhouse) < 1.6 && r.Held.Length == 0 && r.Activity == "npc.activity.tending";
        });

        Assert.True(atField, "the gardener never reached the field's edge");
        Assert.True(carried, "the gardener never carried a basket away from the field");
        Assert.True(delivered, "the basket never reached the stall");
        Assert.True(backWithoutBasket, "the gardener did not come back to the greenhouse with empty hands");
    }

    [Fact]
    public void TheAnimalKeeper_WalksToThePenAndBack_ButNotAwayFromACustomer_AndNotWithoutAPen()
    {
        using var w = World("errand_keeper");
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.PenMarker, Marker(w, 8, -14), "w=5;l=4");
        var post = At(w, -8, 4);
        int id = w.Server.SpawnVillagerForTest(Village, post, job: "tamer");
        var pen = w.Server.PlaceAnchorsForTest(Village).Single(a => a.Kind == "Pen").Spot;

        // A customer at the stall: the keeper stays at its post, however long.
        w.Player.State.Position = new Vector3f(post.X + 2f, post.Y, post.Z);
        bool left = false;
        w.TickAt(Day, 200, every: () => left |= Dist(w.Server.NpcRoutineForTest(id).Pos, post) > 4);
        Assert.False(left, "the keeper walked off from a customer at its stall");

        // Alone again: over to the pen (watching the animals), and back to the post.
        w.Player.State.Position = At(w, 18, 18);
        bool atPen = false, watching = false, back = false;
        w.TickAt(Day, 700, until: () => back, every: () =>
        {
            var r = w.Server.NpcRoutineForTest(id);
            atPen |= Dist(r.Pos, pen) < 2.2;
            watching |= atPen && r.Activity == "npc.activity.watching_animals";
            back |= watching && Dist(r.Pos, post) < 2.2 && r.Activity != "npc.activity.watching_animals";
        });
        Assert.True(atPen, "the keeper never reached the pen");
        Assert.True(watching, "the keeper never stood at the fence watching the animals");
        Assert.True(back, "the keeper did not come back to its post");

        // A place without a pen: the post is the whole job.
        using var bare = World("errand_keeper_nopen");
        var barePost = At(bare, -8, 4);
        int lone = bare.Server.SpawnVillagerForTest(Village, barePost, job: "tamer");
        bare.Player.State.Position = At(bare, 18, 18);
        bool moved = false;
        bare.TickAt(Day, 200, every: () => moved |= Dist(bare.Server.NpcRoutineForTest(lone).Pos, barePost) > 4);
        Assert.False(moved, "a keeper without a pen left its post");
    }

    [Fact]
    public void TheKeeperOfAVillageTavern_StaysAtTheCounterInTheEvening_WhileTheOthersRest()
    {
        using var w = World("errand_keeper");
        int keeper = w.Server.SpawnVillagerForTest(Village, At(w, 5, 5), job: "innkeeper");
        int villager = w.Server.SpawnVillagerForTest(Village, At(w, -5, 5));
        w.TickAt(Evening, 6);
        Assert.Equal("npc.activity.working", w.Server.NpcRoutineForTest(keeper).Activity);
        Assert.Equal("npc.activity.resting", w.Server.NpcRoutineForTest(villager).Activity);
    }

    private static List<NpcEmote> Emotes(NpcLifeWorld w)
        => w.Transport.Sent.Where(s => s.Conn == w.Player.ConnectionId).Select(s => s.Msg).OfType<NpcEmote>().ToList();

    private static double Dist(Vector3f a, Vector3f b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));
}
