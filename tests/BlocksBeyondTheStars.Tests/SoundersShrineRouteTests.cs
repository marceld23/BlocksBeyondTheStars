// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3415 — the Sounders' way to the Singing Shrine (once per galaxy, on one world): the shrine steps' <c>unique_site:shrine</c>
/// marker names the shrine's world and its system from another system, makes that world the flight target inside its
/// system and marks the shrine itself on its world — each once, and a few seconds after the step's start line, as every
/// Sounders "where" step does (their hints say "I marked it"). A shrine the player cannot reach hands every shrine step
/// over to the ring of rune stones (<c>world:shrine_reachable</c>, the lead's rule). #3416: on a world with rune shrines the
/// nearest complete one is marked first — any singing shrine answers; the worlds of the route tests predate them.
/// </summary>
public sealed class SoundersShrineRouteTests : IDisposable
{
    private const string Pack = SoundersIds.PackId;
    private const string Pilot = "Pilot";

    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(TestPaths.DataDir()));

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_snd_route_" + Guid.NewGuid().ToString("N"));
    private readonly List<SqliteWorldRepository> _repos = new();

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var repo in _repos)
        {
            try { repo.Dispose(); } catch { /* best effort */ }
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>A world without rune shrines (terrain generation before #3416): the unique shrine is the only one.</summary>
    private const int BeforeRuneShrines = WorldDescription.RuneShrineGeneration - 1;

    private (SvGameServer Server, RecordingTransport Transport) Boot(string world, int generation = BeforeRuneShrines,
        long seed = 4242, string startPlanet = "rocky")
    {
        var transport = new RecordingTransport();
        var config = new ServerConfig
        {
            WorldName = world,
            Seed = seed,
            StartPlanet = startPlanet,
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            MaxPlayers = 6,
            PlaceStarterShip = false,
        };
        config.World.TerrainGeneration = generation;
        config.Rules.TutorialPolicy = TutorialPolicy.Off;
        config.Rules.InstantTravel = false;
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        var server = new SvGameServer(config, Content.Value, transport, repo);
        server.Start();
        return (server, transport);
    }

    /// <summary>The real join path of a current client (<c>packs1</c>).</summary>
    private static PlayerSession Join(SvGameServer server)
    {
        server.HandlePayloadForTest(1, NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = Pilot,
            Features = new[] { ClientFeatures.Packs1 },
        }));
        return server.Sessions[1];
    }

    private static void FitJumpGenerator(PlayerSession p) => p.Ships[p.ActiveShipId].Modules.Add("jump_generator");

    /// <summary>The shrine's body and system for this seed; the start world must lie in another system.</summary>
    private static (CelestialBody Shrine, StarSystem System) ShrineFarFromHome(SvGameServer server, PlayerSession p)
    {
        var shrine = server.Galaxy.FindBody(server.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey)!)!;
        var system = server.Galaxy.Systems.Single(s => s.Id == shrine.SystemId);
        Assert.NotEqual(server.Galaxy.FindBody(p.CurrentLocationId)!.SystemId, system.Id); // the seed's precondition
        return (shrine, system);
    }

    private static int EffectiveMarkerAfter(StoryDefinition pack, StoryStep step)
        => step.Escalate?.MarkerAfter ?? pack.Escalate.MarkerAfter ?? StoryEscalation.DefaultMarkerAfter;

    private static int EffectiveHintAfter(StoryDefinition pack, StoryStep step)
        => step.Escalate?.HintAfter ?? pack.Escalate.HintAfter ?? StoryEscalation.DefaultHintAfter;

    [Fact]
    public void EverySoundersWhereStep_MarksItsPlaceSecondsAfterItsStart_BeforeItsHintSaysSo()
    {
        var pack = Content.Value.Stories[Pack];
        var where = pack.Chapters.SelectMany(c => c.Steps).Where(s => s.OnStart.Reveal.Length > 0).ToList();
        Assert.Equal(new[] { "s1.2", "s2.1", "s2.1a", "s3.4", "s4.4", "s5.1", "s5.1a", "s6.2", "s6.2a", "s7.1", "s7.3", "s8.1" },
            where.Select(s => s.Id));
        Assert.All(where.Where(s => s.Id.EndsWith('a')),
            s => Assert.Equal(StoryRevealProviders.NearestRuneShrine, s.OnStart.Reveal)); // #3416: the stand-ins mark a rune shrine
        foreach (var step in where)
        {
            int marker = EffectiveMarkerAfter(pack, step);
            Assert.True(marker is > 0 and <= 10, $"{step.Id}: the marker comes after {marker} s");
            Assert.True(marker < EffectiveHintAfter(pack, step), $"{step.Id}: the hint would say 'marked' before the marker is up");
        }
    }

    [Fact]
    public void EveryShrineStep_AsksWhetherTheShrineCanBeReached_AndHasTheRingAsItsAlternative()
    {
        var pack = Content.Value.Stories[Pack];
        var shrineSteps = pack.Chapters.SelectMany(c => c.Steps).Where(s => s.OnStart.Reveal == "unique_site:shrine").ToList();
        Assert.Equal(new[] { "s2.1", "s5.1", "s6.2" }, shrineSteps.Select(s => s.Id));
        foreach (var step in shrineSteps)
        {
            Assert.Equal(new[] { "world:" + StoryWorldPredicates.ShrineReachable }, step.FeasibleIf);
            Assert.Equal(step.Id + "a", step.Alternative);
        }
    }

    /// <summary>#3416: a world with rune shrines — the nearest complete one is the singing shrine VEGA marks, for the shrine's
    /// echo game (s2.1) and Elora's voice (s5.1) alike; the one-of-a-kind shrine in another system is not named.</summary>
    [Fact]
    public void ARuneShrineNear_IsMarkedOnItsAltar_InsteadOfTheFarShrine()
    {
        var (server, t) = Boot("snd_route_rune", WorldDescription.CurrentTerrainGeneration, seed: 20261108, startPlanet: "meadowlands");
        var p = Join(server);
        FitJumpGenerator(p); // the unique shrine stays reachable, so s2.1 / s5.1 run (not their ring stand-ins)
        var altar = server.NearestRuneShrine(p.State.Position);
        Assert.NotNull(altar); // the seed's precondition (RuneShrineServerTests' world)
        Assert.Equal(Content.Value.GetBlock("ancient_brick")!.NumericId, server.World.GetBlock(altar!.Value));

        foreach (var (chapter, step) in new[] { ("chapter 2", "s2.1"), ("chapter 5", "s5.1") })
        {
            server.SoundersAdminForTest(p, chapter);
            Assert.Equal(step, server.ChipStepForTest(p, Pack));
            server.TickStoryPacksForTest(p, 6); // past markerAfter 5
            var marker = Assert.Single(server.StoryMarkersForTest(Pilot));
            Assert.Equal((altar.Value.X + 0.5f, altar.Value.Z + 0.5f), (marker.X, marker.Z));
        }

        Assert.Equal(2, t.LineKeys(p).Count(k => k == SvGameServer.StepMarkedLine));
        Assert.Empty(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.Empty(t.Lines(p, StoryNarratorLines.StepMarkedStation));
    }

    /// <summary>A world before the rune shrines (generation &lt; 36) keeps the unique shrine's route.</summary>
    [Fact]
    public void TheShrineOnAnotherWorld_IsNamedWithItsSystem_ItsSystemSetsTheFlightTarget_AndItsWorldTheMarker()
    {
        var (server, t) = Boot("snd_route_journey");
        var p = Join(server);
        FitJumpGenerator(p); // the shrine lies in another system: reachable with a jump generator
        var (shrine, system) = ShrineFarFromHome(server, p);
        Assert.Null(server.NearestRuneShrine(p.State.Position, 2000)); // an older world: no rune shrine anywhere
        Assert.True(server.EvaluateStoryConditionForTest("world:shrine_reachable", p));

        server.SoundersAdminForTest(p, "chapter 2");
        Assert.Equal("s2.1", server.ChipStepForTest(p, Pack));
        server.TickStoryPacksForTest(p, 6); // past markerAfter 5

        // Another system: VEGA names the world and its system — once, the retries on the same spot stay quiet.
        var far = Assert.Single(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.Equal(shrine.Name + LineText.ArgSeparator + system.Name, far.LineArg);
        Assert.Equal(Pack, far.PackId);
        server.TickStoryPacksForTest(p, 70);
        Assert.Single(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.DoesNotContain(server.VisibleMarkersForTest(Pilot), m => m.Ping);
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, t.LineKeys(p));

        // Another world of the shrine's system: the shrine's world becomes the flight target, with its name.
        var neighbour = system.Bodies.First(b => b.Id != shrine.Id && b.Kind is CelestialKind.Planet or CelestialKind.Moon
                                                 && !string.IsNullOrEmpty(b.PlanetType));
        server.Travel(p.State.PlayerId, neighbour.Id);
        Assert.Equal(neighbour.Id, p.CurrentLocationId);
        server.TickStoryPacksForTest(p);
        var near = Assert.Single(t.Lines(p, StoryNarratorLines.StepMarkedStation));
        Assert.Equal((shrine.Name, shrine.Id), (near.LineArg, near.SpaceWaypointId));
        server.TickStoryPacksForTest(p, 40);
        Assert.Single(t.Lines(p, StoryNarratorLines.StepMarkedStation));

        // On the shrine's world (a new place: probed at once): the shrine is marked for this player, with "I've marked it".
        server.Travel(p.State.PlayerId, shrine.Id);
        Assert.Equal(shrine.Id, p.CurrentLocationId);
        Assert.Equal("s2.1", server.ChipStepForTest(p, Pack));
        server.TickStoryPacksForTest(p);
        Assert.Contains(server.VisibleMarkersForTest(Pilot), m => m.Ping && m.OwnerId == Pilot);
        Assert.Single(t.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
        Assert.Single(t.Lines(p, SvGameServer.StepMarkedWorldLine));
    }

    [Fact]
    public void AShrineStepStartedOnTheShrinesWorld_MarksTheShrineSecondsAfterItsStart()
    {
        var (server, t) = Boot("snd_route_on_site");
        var p = Join(server);
        FitJumpGenerator(p);
        var (shrine, _) = ShrineFarFromHome(server, p);
        server.Travel(p.State.PlayerId, shrine.Id);
        Assert.Equal(shrine.Id, p.CurrentLocationId);

        server.SoundersAdminForTest(p, "chapter 2");
        Assert.Equal("s2.1", server.ChipStepForTest(p, Pack));
        server.TickStoryPacksForTest(p, 3);
        Assert.DoesNotContain(server.VisibleMarkersForTest(Pilot), m => m.Ping); // not before markerAfter

        server.TickStoryPacksForTest(p, 3);
        Assert.Contains(server.VisibleMarkersForTest(Pilot), m => m.Ping && m.OwnerId == Pilot);
        Assert.Single(t.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
        Assert.Empty(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.Empty(t.Lines(p, StoryNarratorLines.StepMarkedStation));
    }

    [Fact]
    public void AnUnreachableShrine_HandsEveryShrineStepOverToTheRing()
    {
        var (server, _) = Boot("snd_route_unreachable");
        var p = Join(server);
        ShrineFarFromHome(server, p); // another system, no jump generator, never landed there, no instant travel
        Assert.True(server.EvaluateStoryConditionForTest("world:shrine", p)); // the shrine exists …
        Assert.False(server.EvaluateStoryConditionForTest("world:shrine_reachable", p)); // … but not for this player

        server.SoundersAdminForTest(p, "chapter 2");
        Assert.Contains(StoryPackKeys.StepSkipped(Pack, "s2.1"), p.State.Milestones);
        Assert.Equal("s2.1a", server.ChipStepForTest(p, Pack));

        server.SoundersAdminForTest(p, "chapter 5");
        Assert.Contains(StoryPackKeys.StepSkipped(Pack, "s5.1"), p.State.Milestones);
        Assert.Equal("s5.1a", server.ChipStepForTest(p, Pack));

        server.SoundersAdminForTest(p, "chapter 6");
        Assert.Contains(StoryPackKeys.StepSkipped(Pack, "s6.2"), p.State.Milestones);
        Assert.True(p.State.StepProgress.ContainsKey(StoryPackKeys.ProgressKey(Pack, "s6.2a")));

        FitJumpGenerator(p);
        Assert.True(server.EvaluateStoryConditionForTest("world:shrine_reachable", p)); // a jump generator gets them there
    }

    /// <summary>#3416: the ring stand-in (the unique shrine out of reach) marks the altar of the nearest complete rune shrine
    /// on the player's world, with "I've marked your goal" — and never names a far place.</summary>
    [Fact]
    public void TheRingStandIn_MarksTheNearestRuneShrinesAltar()
    {
        var (server, t) = Boot("snd_route_ring_rune", WorldDescription.CurrentTerrainGeneration, seed: 20261108, startPlanet: "meadowlands");
        var p = Join(server);
        var altar = server.NearestRuneShrine(p.State.Position);
        Assert.NotNull(altar); // the seed's precondition (RuneShrineServerTests' world)
        server.RecordUniqueSiteSkipForTest(server.UniqueSiteBodyForTest(SvGameServer.ShrineSiteKey)!, "shrine"); // no unique shrine
        server.SoundersAdminForTest(p, "chapter 2");
        Assert.Equal("s2.1a", server.ChipStepForTest(p, Pack));

        server.TickStoryPacksForTest(p, 6); // past markerAfter 5
        var marker = Assert.Single(server.StoryMarkersForTest(Pilot));
        Assert.Equal((altar!.Value.X + 0.5f, altar.Value.Z + 0.5f), (marker.X, marker.Z));
        Assert.Single(t.LineKeys(p), k => k == SvGameServer.StepMarkedLine);
        Assert.Empty(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.Empty(t.Lines(p, StoryNarratorLines.StepMarkedStation));
    }

    /// <summary>#3416: on an older world (no rune shrines) the ring stand-in marks nothing and says nothing — no far fallback —
    /// however long the player plays there.</summary>
    [Fact]
    public void TheRingStandIn_OnAnOlderWorld_StaysSilent()
    {
        var (server, t) = Boot("snd_route_ring_old");
        var p = Join(server);
        ShrineFarFromHome(server, p); // unreachable: the stand-in runs
        server.SoundersAdminForTest(p, "chapter 2");
        Assert.Equal("s2.1a", server.ChipStepForTest(p, Pack));

        server.TickStoryPacksForTest(p, 70); // past markerAfter 5 and two retries
        Assert.Empty(server.StoryMarkersForTest(Pilot));
        Assert.DoesNotContain(SvGameServer.StepMarkedLine, t.LineKeys(p));
        Assert.Empty(t.Lines(p, SvGameServer.StepMarkedWorldLine));
        Assert.Empty(t.Lines(p, StoryNarratorLines.StepMarkedStation));
    }
}
