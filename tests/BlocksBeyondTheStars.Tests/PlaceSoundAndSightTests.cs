// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The lively places — what a place sounds and looks like: the sounds the server derives from a settlement's markers (the
/// tavern in the evening, the workshop by day, the well round the clock) and the far silhouette that rides in the landmark
/// list.
/// </summary>
public sealed class PlaceSoundAndSightTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private const string Village = "Soundham";

    private static NpcLifeWorld World(string name)
    {
        var w = new NpcLifeWorld(Content.Value, name);
        w.Server.RegisterTestSettlementForTest(Village,
            new Vector3i(NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), new Vector3i(NpcLifeWorld.Cx + 20, w.PadY + 10, NpcLifeWorld.Cz + 20));
        return w;
    }

    private static Vector3f Marker(NpcLifeWorld w, float dx, float dz) => new(NpcLifeWorld.Cx + dx + 0.5f, w.Feet + 0.5f, NpcLifeWorld.Cz + dz + 0.5f);

    [Fact]
    public void ASettlementsMarkers_BecomeItsSounds_EachWithItsHours()
    {
        using var w = World("place_sounds");
        w.Server.AddSettlementMarkerForTest(Village, SettlementGenerator.TavernMarker, Marker(w, -8, 3));
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.WellMarker, Marker(w, 2, 1));
        for (int i = 0; i < 5; i++)
        {
            w.Server.AddSettlementMarkerForTest(Village, SettlementGenerator.WorkshopMarker, Marker(w, 6 + (2 * i), -6));
        }

        w.Server.AddSettlementMarkerForTest(Village, "vendor", Marker(w, 12, 0)); // a post without a sound of its own
        w.Server.AddSettlementMarkerForTest(Village, SettlementPlanBaker.StallMarker, Marker(w, 4, 4)); // #3868: a market stall

        var emitters = w.Server.PlaceEmittersForTest;
        var stall = Assert.Single(emitters, e => e.Sound == "amb_place_stall");
        Assert.Equal(SoundEmitterTimes.Day, stall.When);
        Assert.True(stall.Radius < 20f && stall.Volume < 0.8f, "a stall is a small, quiet source");
        var tavern = Assert.Single(emitters, e => e.Sound == "amb_place_tavern");
        Assert.Equal(SoundEmitterTimes.Evening, tavern.When);
        Assert.InRange(tavern.Position.X, NpcLifeWorld.Cx - 8, NpcLifeWorld.Cx - 7);
        Assert.Equal(SoundEmitterTimes.Always, Assert.Single(emitters, e => e.Sound == "amb_place_well").When);
        var workshops = emitters.Where(e => e.Sound == "amb_place_workshop").ToList();
        Assert.Equal(3, workshops.Count); // five workshops, three sources: one voice plays anyway
        Assert.All(workshops, e => Assert.Equal(SoundEmitterTimes.Day, e.When));
        Assert.Equal(6, emitters.Count);

        // They ride in the landmark list like an authored sound marker, hours included.
        var sent = w.Server.LandmarkListForTest(w.Player).Emitters;
        Assert.Contains(sent, e => e.Sound == "amb_place_tavern" && e.When == SoundEmitterTimes.Evening && e.Radius > 10f && e.Volume > 0f);
        Assert.Equal(3, sent.Count(e => e.Sound == "amb_place_workshop" && e.When == SoundEmitterTimes.Day));
    }

    [Fact]
    public void ARuin_MakesNoSound()
    {
        using var w = World("place_sounds_ruin");
        w.Server.AddSettlementMarkerForTest(Village, SettlementGenerator.TavernMarker, Marker(w, -8, 3));
        Assert.Single(w.Server.PlaceEmittersForTest);
        w.Server.SetSettlementRuinedForTest(Village, true);
        Assert.Empty(w.Server.PlaceEmittersForTest);
    }

    // ---------------------------------------------------------------- the far silhouette

    private static readonly List<StructureTemplate> Pool = Content.Value.SettlementTemplates.Where(t => t.IsModule).ToList();

    private static SettlementStructure BakedVillage(string kit, string tier, long seed)
    {
        var plan = SettlementPlanner.Plan(new SettlementPlanRequest
        {
            Tier = tier,
            Seed = seed,
            Kit = Content.Value.KitByKey(kit)!,
            Pool = Pool,
            Ground = SettlementPlannerTests.Hills,
            Revision = SettlementPlan.LivelyRevision,
        });
        return SettlementPlanBaker.Bake(plan, tier, false, seed, "grass", Content.Value, Pool);
    }

    [Fact]
    public void ASettlementWithAColumnMap_SendsItsSilhouette_WithoutItsPaving()
    {
        using var w = World("place_silhouette");
        var layout = BakedVillage("village_grown_2", "village", 7);
        w.Server.SetSettlementLayoutForTest(Village, layout);

        var list = w.Server.LandmarkListForTest(w.Player);
        var model = Assert.Single(list.Landmarks, l => l.Kind == PlaceKinds.Settlement);
        Assert.Equal(Village, model.Name);
        Assert.Equal((NpcLifeWorld.Cx - 20, w.PadY, NpcLifeWorld.Cz - 20), (model.X, model.Y, model.Z));
        Assert.Equal(model.Width * model.Height * model.Length, model.Cells.Length);
        Assert.True(model.Cells.Count(c => c != 0) > 20, "the village's houses fill only a handful of model cells");
        Assert.True((model.Width * model.Step) >= layout.Width && (model.Length * model.Step) >= layout.Length);

        // The paving is the far terrain's: no model cell is made of a path block alone at the ground.
        ushort lantern = Content.Value.GetBlock("lantern")!.NumericId.Value;
        Assert.DoesNotContain(lantern, model.Blocks); // a single lantern never fills a model cell

        // Built once: the second list carries the same arrays.
        Assert.Same(model.Cells, w.Server.LandmarkListForTest(w.Player).Landmarks.Single(l => l.Kind == PlaceKinds.Settlement).Cells);
    }

    [Fact]
    public void ASettlementWithoutALayout_SendsNoSilhouette()
    {
        using var w = World("place_no_silhouette");
        Assert.DoesNotContain(w.Server.LandmarkListForTest(w.Player).Landmarks, l => l.Kind == PlaceKinds.Settlement);
    }
}
