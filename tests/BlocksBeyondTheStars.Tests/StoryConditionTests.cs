// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2465 — the condition grammar of the story packs (<c>requires</c>, <c>feasibleIf</c>, gates): atoms
/// <c>step:</c>/<c>chapter:</c>/<c>pack:</c>/<c>blueprint:</c>/<c>world:</c>, combinators <c>all:[…]</c>/<c>any:[…]</c>/
/// <c>not:</c>; content errors for every malformed form; pure evaluation over a context; and the server's context
/// (milestone keys, pack states, the world-predicate registry kept identical to the published list).
/// </summary>
public sealed class StoryConditionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_cond_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort temp cleanup */ }
    }

    /// <summary>A dictionary-backed context: the set holds the true atoms in their canonical text.</summary>
    private sealed class FakeContext : IStoryConditionContext
    {
        public HashSet<string> True { get; } = new(StringComparer.Ordinal);

        public List<string> Asked { get; } = new();

        private bool Ask(string atom)
        {
            Asked.Add(atom);
            return True.Contains(atom);
        }

        public bool IsStepDone(string packId, string stepId) => Ask($"step:{packId}:{stepId}");

        public bool IsChapterDone(string packId, string chapterId) => Ask($"chapter:{packId}:{chapterId}");

        public bool IsPackInState(string packId, StoryPackState state) => Ask($"pack:{packId}:{StoryConditionParser.PackStateName(state)}");

        public bool HasBlueprint(string blueprintKey) => Ask($"blueprint:{blueprintKey}");

        public bool IsWorldPredicateTrue(string predicate) => Ask($"world:{predicate}");
    }

    // ---------------- Grammar ----------------

    [Theory]
    [InlineData("step:tutorial:b2.2", StoryConditionKind.Step, "tutorial", "b2.2")]
    [InlineData("chapter:tutorial:b6", StoryConditionKind.Chapter, "tutorial", "b6")]
    [InlineData("blueprint:comm_radio", StoryConditionKind.Blueprint, "", "comm_radio")]
    [InlineData("world:station_in_system", StoryConditionKind.World, "", "station_in_system")]
    public void Atoms_ParseIntoTheirParts(string text, StoryConditionKind kind, string pack, string id)
    {
        var c = StoryConditionParser.Parse(text);

        Assert.Equal(kind, c.Kind);
        Assert.Equal(pack, c.PackId);
        Assert.Equal(id, c.Id);
        Assert.True(c.IsAtom);
        Assert.Equal(text, c.ToString());
    }

    [Theory]
    [InlineData("started", StoryPackState.Started)]
    [InlineData("done", StoryPackState.Done)]
    [InlineData("enabled", StoryPackState.Enabled)]
    [InlineData("optedout", StoryPackState.OptedOut)]
    public void PackAtoms_KnowTheFourStates(string word, StoryPackState state)
    {
        var c = StoryConditionParser.Parse("pack:vega_protocol:" + word);

        Assert.Equal(StoryConditionKind.Pack, c.Kind);
        Assert.Equal("vega_protocol", c.PackId);
        Assert.Equal(state, c.PackState);
    }

    /// <summary>The synthesis' own examples parse, spaces around brackets and commas are fine, and the canonical text
    /// round-trips.</summary>
    [Fact]
    public void Groups_NestAndRoundTrip()
    {
        var gate = StoryConditionParser.Parse("all:[step:tutorial:b2.2, step:tutorial:b3.1 ,step:tutorial:b4.1]");
        Assert.Equal(StoryConditionKind.All, gate.Kind);
        Assert.Equal(3, gate.Children.Count);
        Assert.Equal("all:[step:tutorial:b2.2,step:tutorial:b3.1,step:tutorial:b4.1]", gate.ToString());

        var banter = StoryConditionParser.Parse(" any:[ chapter:tutorial:b6 , pack:tutorial:optedout ] ");
        Assert.Equal("any:[chapter:tutorial:b6,pack:tutorial:optedout]", banter.ToString());

        var nested = StoryConditionParser.Parse("not:any:[world:enemies,all:[blueprint:comm_radio,not:world:hunger]]");
        Assert.Equal(StoryConditionKind.Not, nested.Kind);
        Assert.Equal(nested.ToString(), StoryConditionParser.Parse(nested.ToString()).ToString());
        Assert.Equal(new[] { "world:enemies", "blueprint:comm_radio", "world:hunger" }, nested.Atoms().Select(a => a.ToString()));
    }

    [Theory]
    [InlineData("", "missing")]
    [InlineData("   ", "missing")]
    [InlineData("step", "':'")]
    [InlineData("step:tutorial", "needs ':<step>'")]
    [InlineData("step:tutorial:", "step id")]
    [InlineData("chapter::b6", "pack id")]
    [InlineData("pack:tutorial:finished", "unknown pack state")]
    [InlineData("pack:tutorial", "':started'")]
    [InlineData("blueprint:", "blueprint key")]
    [InlineData("world:lava_everywhere", "unknown world predicate")]
    [InlineData("world:", "world predicate")]
    [InlineData("mission:crystal_workshop_1", "unknown condition 'mission:'")]
    [InlineData("ui:menu_open", "client claims")]
    [InlineData("all:[]", "empty group")]
    [InlineData("all:step:a:b", "'['")]
    [InlineData("any:[step:a:b", "',' or ']'")]
    [InlineData("any:[step:a:b;step:a:c]", "',' or ']'")]
    [InlineData("step:a:b step:a:c", "unexpected text")]
    [InlineData("step:none:b1", "reserved pack id")]
    [InlineData("pack:vega:started", "reserved pack id")]
    [InlineData("chapter:mem:x", "reserved pack id")]
    public void MalformedConditions_AreContentErrors_ThatSayWhy(string text, string reason)
    {
        var ex = Assert.Throws<StoryConditionException>(() => StoryConditionParser.Parse(text));
        Assert.Contains(reason, ex.Message, StringComparison.Ordinal);

        Assert.False(StoryConditionParser.TryParse(text, out var parsed, out string error));
        Assert.Null(parsed);
        Assert.Contains(reason, error, StringComparison.Ordinal);
    }

    [Fact]
    public void Nesting_IsCapped()
    {
        string deep = string.Concat(Enumerable.Repeat("not:", StoryConditionParser.MaxDepth + 2)) + "world:enemies";
        Assert.Throws<StoryConditionException>(() => StoryConditionParser.Parse(deep));
        Assert.NotNull(StoryConditionParser.Parse(string.Concat(Enumerable.Repeat("not:", 3)) + "world:enemies"));
    }

    /// <summary>A pack validator may pass its own predicate set (e.g. the server's registry).</summary>
    [Fact]
    public void TheKnownPredicates_CanBeSupplied()
    {
        var only = new[] { "custom_fact" };
        Assert.Equal("custom_fact", StoryConditionParser.Parse("world:custom_fact", only).Id);
        Assert.Throws<StoryConditionException>(() => StoryConditionParser.Parse("world:enemies", only));
    }

    // ---------------- Evaluation ----------------

    [Fact]
    public void Evaluation_FollowsTheCombinators_AndShortCircuits()
    {
        var ctx = new FakeContext();
        ctx.True.Add("step:tutorial:b2.2");
        ctx.True.Add("blueprint:comm_radio");

        Assert.True(StoryConditionParser.Parse("step:tutorial:b2.2").Evaluate(ctx));
        Assert.False(StoryConditionParser.Parse("step:tutorial:b3.1").Evaluate(ctx));
        Assert.True(StoryConditionParser.Parse("not:step:tutorial:b3.1").Evaluate(ctx));
        Assert.False(StoryConditionParser.Parse("all:[step:tutorial:b2.2,step:tutorial:b3.1]").Evaluate(ctx));
        Assert.True(StoryConditionParser.Parse("any:[step:tutorial:b3.1,blueprint:comm_radio]").Evaluate(ctx));

        ctx.Asked.Clear();
        Assert.False(StoryConditionParser.Parse("all:[world:enemies,blueprint:comm_radio]").Evaluate(ctx));
        Assert.Equal(new[] { "world:enemies" }, ctx.Asked); // all stops at the first false

        ctx.Asked.Clear();
        Assert.True(StoryConditionParser.Parse("any:[blueprint:comm_radio,world:enemies]").Evaluate(ctx));
        Assert.Equal(new[] { "blueprint:comm_radio" }, ctx.Asked); // any stops at the first true
    }

    /// <summary>A JSON list is one implicit <c>all</c>; an empty or missing list is "no condition" and holds.</summary>
    [Fact]
    public void Lists_AreAnImplicitAll_AndNothingAlwaysHolds()
    {
        var ctx = new FakeContext();
        ctx.True.Add("world:free_flight");

        Assert.Null(StoryConditionParser.ParseList(null));
        Assert.Null(StoryConditionParser.ParseList(Array.Empty<string>()));
        Assert.True(StoryConditionParser.Holds(null, ctx));

        var single = StoryConditionParser.ParseList(new[] { "world:free_flight" });
        Assert.Equal(StoryConditionKind.World, single!.Kind);

        var both = StoryConditionParser.ParseList(new[] { "world:free_flight", "world:station_in_system" });
        Assert.Equal(StoryConditionKind.All, both!.Kind);
        Assert.False(StoryConditionParser.Holds(both, ctx));
        ctx.True.Add("world:station_in_system");
        Assert.True(StoryConditionParser.Holds(both, ctx));

        Assert.Throws<StoryConditionException>(() => StoryConditionParser.ParseList(new[] { "world:free_flight", "nonsense" }));
    }

    // ---------------- The server's context ----------------

    private SvGameServer Started(string world, Action<ServerConfig>? configure = null)
    {
        var config = new ServerConfig { WorldName = world, Seed = 3, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        configure?.Invoke(config);
        var server = new SvGameServer(config, ContentLoader.LoadFromDirectory(TestPaths.DataDir()),
            new LoopbackServerTransport(new LoopbackLink()), new SqliteWorldRepository(new SaveGamePaths(_root, world)));
        server.Start();
        return server;
    }

    /// <summary>Every published predicate has a server implementation and the server has no unpublished one — a new
    /// predicate is one name in <see cref="StoryWorldPredicates"/> plus one line in the server's registry.</summary>
    [Fact]
    public void TheServerEvaluatesExactlyThePublishedPredicates()
    {
        var server = Started("cond_registry");

        Assert.Equal(
            StoryWorldPredicates.All.OrderBy(n => n, StringComparer.Ordinal),
            server.WorldPredicateNames.OrderBy(n => n, StringComparer.Ordinal));

        var p = server.AddLocalPlayer("Ada");
        foreach (string predicate in StoryWorldPredicates.All)
        {
            server.EvaluateStoryConditionForTest("world:" + predicate, p); // every one runs without throwing
        }
    }

    /// <summary>Steps, chapters and pack states read the §5.6 milestone keys; blueprints the player's list; rule
    /// predicates the save's rules; <c>pack:…:enabled</c> knows the world story and a "none" world.</summary>
    [Fact]
    public void TheServerContext_ReadsMilestonesBlueprintsRulesAndPacks()
    {
        var server = Started("cond_context", c =>
        {
            c.Rules.HungerConsumption = HungerConsumption.Off;
            c.Rules.FreeSpaceFlight = true;
        });
        var p = server.AddLocalPlayer("Ada");
        bool Eval(string text) => server.EvaluateStoryConditionForTest(text, p);

        Assert.False(Eval("step:tutorial:b2.2"));
        p.State.Milestones.Add(StoryPackKeys.StepDone("tutorial", "b2.2"));
        p.State.Milestones.Add(StoryPackKeys.StepSkipped("tutorial", "b3.1"));
        p.State.Milestones.Add(StoryPackKeys.ChapterSkipped("tutorial", "b6"));
        p.State.Milestones.Add(StoryPackKeys.Started("tutorial"));
        Assert.True(Eval("all:[step:tutorial:b2.2,step:tutorial:b3.1]")); // skipped counts as done
        Assert.True(Eval("chapter:tutorial:b6"));
        Assert.True(Eval("pack:tutorial:started"));
        Assert.False(Eval("pack:tutorial:done"));
        Assert.False(Eval("pack:tutorial:optedout"));

        Assert.False(Eval("blueprint:comm_radio"));
        p.State.UnlockedBlueprints.Add("comm_radio");
        Assert.True(Eval("blueprint:comm_radio"));

        Assert.False(Eval("world:hunger"));
        Assert.True(Eval("world:free_flight"));
        Assert.True(Eval("world:story_active"));
        Assert.True(Eval("pack:vega_protocol:enabled"));

        server.SetActiveStory(StoryRegistry.NoneStoryId);
        Assert.False(Eval("world:story_active"));
        Assert.False(Eval("pack:vega_protocol:enabled"));
        Assert.False(Eval("pack:no_such_pack:enabled"));
    }

    [Fact]
    public void TheStationPredicate_MirrorsTheDockLessonsSkipRule()
    {
        var withStations = Started("cond_station_on");
        Assert.True(withStations.EvaluateStoryConditionForTest("world:station_in_system", withStations.AddLocalPlayer("Ada")));

        var without = Started("cond_station_off", c => c.World.SpaceStations = BlocksBeyondTheStars.Shared.World.Frequency.Off);
        var p = without.AddLocalPlayer("Ada");
        Assert.False(without.EvaluateStoryConditionForTest("world:station_in_system", p));
        Assert.False(without.EvaluateStoryConditionForTest("world:space_stations", p));
    }
}
