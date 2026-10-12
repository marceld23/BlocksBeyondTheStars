// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Story;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2465 (N11, B70) — the guard against beat-less world stories and the pack loading order. The built-in VEGA pack is
/// always installed (it used to fill in only when NO pack loaded); a beat-less or player-scoped pack such as the coming
/// tutorial can never become the save's world story — not as the default, not by <c>--story</c>, not by an admin
/// switch, not from a stored row; pack folders load in ordinal order and a pack that overwrites another's locale key is
/// reported; reserved ids are skipped.
/// </summary>
public sealed class StoryPackGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_packguard_" + Guid.NewGuid().ToString("N"));
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

    private static StoryDefinition TutorialPack() => new()
    {
        Id = "tutorial",
        NameKey = "story.tutorial.name",
        Kind = StoryRegistry.KindTutorial,
        Scope = StoryRegistry.ScopePlayer,
    };

    private static StoryDefinition WithBeats(string id, string kind = StoryRegistry.KindStory, string scope = "") => new()
    {
        Id = id,
        Kind = kind,
        Scope = scope,
        Beats = { new StoryBeat { Index = 0, TextKey = "story." + id + ".beat0", Threshold = 0 } },
    };

    // ---------------- GameContent ----------------

    /// <summary>A data folder with only a beat-less player pack still has VEGA, and the default story is VEGA.</summary>
    [Fact]
    public void SetStories_AlwaysInstallsTheBuiltInVega_WhenTheDataLacksIt()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStories(new[] { TutorialPack() });

        Assert.True(content.Stories.ContainsKey(StoryRegistry.DefaultStoryId));
        Assert.Same(StoryRegistry.Default, content.Stories[StoryRegistry.DefaultStoryId]);
        Assert.Equal(StoryRegistry.DefaultStoryId, content.DefaultStory.Id);
        Assert.True(content.TryGetStory("tutorial", out var found)); // the pack itself is installed and readable…
        Assert.Equal("tutorial", found.Id);
        Assert.False(content.TryGetWorldStory("tutorial", out var world)); // …but never a world story
        Assert.Equal(StoryRegistry.DefaultStoryId, world.Id);
    }

    /// <summary>A broken data copy of VEGA without beats is replaced by the built-in one, so its row keeps resolving.</summary>
    [Fact]
    public void ABeatlessVegaDataCopy_IsReplacedByTheBuiltIn()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStories(new[] { new StoryDefinition { Id = StoryRegistry.DefaultStoryId } });

        Assert.Same(StoryRegistry.Default, content.Stories[StoryRegistry.DefaultStoryId]);
        Assert.True(content.TryGetWorldStory(StoryRegistry.DefaultStoryId, out _));
    }

    [Theory]
    [InlineData(StoryRegistry.KindStory, "", true)]                          // beats, scope derived → world
    [InlineData(StoryRegistry.KindStory, StoryRegistry.ScopeWorld, true)]
    [InlineData(StoryRegistry.KindSide, StoryRegistry.ScopePlayer, false)]   // explicitly per player
    [InlineData(StoryRegistry.KindTutorial, StoryRegistry.ScopeWorld, false)] // a tutorial is always per player
    public void OnlyWorldScopedPacksWithBeats_CanBeTheWorldStory(string kind, string scope, bool world)
    {
        var pack = WithBeats("side_arc", kind, scope);
        Assert.Equal(world, StoryRegistry.CanBeWorldStory(pack));

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStories(content.Stories.Values.Append(pack));
        Assert.Equal(world, content.TryGetWorldStory("side_arc", out _));
        Assert.True(content.TryGetStory("side_arc", out _));
    }

    [Fact]
    public void TheScope_IsDerivedFromTheBeats_WhenUnset()
    {
        Assert.Equal(StoryRegistry.ScopeWorld, StoryRegistry.EffectiveScope(WithBeats("a")));
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(new StoryDefinition { Id = "b" }));
        Assert.Equal(StoryRegistry.ScopePlayer, StoryRegistry.EffectiveScope(TutorialPack()));
        Assert.False(StoryRegistry.CanBeWorldStory(null));
        Assert.Equal(StoryRegistry.KindStory, StoryRegistry.Default.Kind);   // the built-in VEGA keeps today's shape
        Assert.Equal(StoryRegistry.ScopeWorld, StoryRegistry.EffectiveScope(StoryRegistry.Default));
    }

    [Fact]
    public void ReservedPackIds_AreDropped()
    {
        foreach (string id in new[] { "none", "mem", "insight", "obj", "vega", "track", "cut", "NONE" })
        {
            Assert.True(StoryRegistry.IsReservedPackId(id), id);
        }

        Assert.False(StoryRegistry.IsReservedPackId("vega_protocol"));
        Assert.False(StoryRegistry.IsReservedPackId("tutorial"));

        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStories(new[] { WithBeats("none"), WithBeats("mem"), WithBeats("tutorial_two") });
        Assert.False(content.Stories.ContainsKey("none"));
        Assert.False(content.Stories.ContainsKey("mem"));
        Assert.True(content.Stories.ContainsKey("tutorial_two"));
    }

    // ---------------- The server ----------------

    private GameContent ContentWithTutorial()
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        content.SetStories(content.Stories.Values.Append(TutorialPack()));
        return content;
    }

    private SvGameServer Started(GameContent content, string world, Action<ServerConfig>? configure = null, SqliteWorldRepository? repo = null)
    {
        var config = new ServerConfig { WorldName = world, Seed = 7, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = false };
        configure?.Invoke(config);
        repo ??= Repo(world);
        var server = new SvGameServer(config, content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    private SqliteWorldRepository Repo(string world)
    {
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        _repos.Add(repo);
        return repo;
    }

    /// <summary><c>--story tutorial</c> on a fresh world starts VEGA instead, and an admin switch to the tutorial is
    /// ignored — the story stays VEGA with its progress.</summary>
    [Fact]
    public void ThePlayerPack_NeverBecomesTheWorldStory_ByRuleOrByAdminSwitch()
    {
        var server = Started(ContentWithTutorial(), "guard_rule", c => c.Rules.StoryId = "tutorial");
        Assert.Equal(StoryRegistry.DefaultStoryId, server.StorySnapshot.StoryId);

        server.RecordStoryMilestoneForTest();
        int milestones = server.StorySnapshot.Milestones;
        server.SetActiveStoryForTest("tutorial");
        Assert.Equal(StoryRegistry.DefaultStoryId, server.StorySnapshot.StoryId);
        Assert.Equal(milestones, server.StorySnapshot.Milestones); // nothing was reset
    }

    /// <summary>A stored row naming the player pack (whatever wrote it) is not loaded as the world story: the save
    /// starts the default story and leaves that row untouched; a real VEGA row next to it wins.</summary>
    [Fact]
    public void AStoredRowOfThePlayerPack_IsNeverLoadedAsTheWorldStory()
    {
        var content = ContentWithTutorial();

        var lone = Repo("guard_row_lone");
        lone.Initialize();
        lone.SaveStoryState(new StoredStoryState { StoryId = "tutorial", Milestones = 9, Active = true, UpdatedUtc = "2026-10-11T10:00:00Z" });
        var server = Started(content, "guard_row_lone", repo: lone);
        Assert.Equal(StoryRegistry.DefaultStoryId, server.StorySnapshot.StoryId);
        Assert.Equal(9, lone.ListStoryStates().Single(r => r.StoryId == "tutorial").Milestones);

        var mixed = Repo("guard_row_mixed");
        mixed.Initialize();
        mixed.SaveStoryState(new StoredStoryState { StoryId = StoryRegistry.DefaultStoryId, Milestones = 5, Active = false, UpdatedUtc = "2026-10-01T10:00:00Z" });
        mixed.SaveStoryState(new StoredStoryState { StoryId = "tutorial", Milestones = 9, Active = true, UpdatedUtc = "2026-10-11T10:00:00Z" });
        var restored = Started(content, "guard_row_mixed", repo: mixed);
        Assert.Equal(StoryRegistry.DefaultStoryId, restored.StorySnapshot.StoryId);
        Assert.Equal(5, restored.StorySnapshot.Milestones); // the real VEGA progress, not a fresh start
    }

    // ---------------- Loader: ordinal order, locale collisions, reserved ids ----------------

    private string TempDataDir()
    {
        string dir = Path.Combine(_root, "data_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "locales"));
        File.WriteAllText(Path.Combine(dir, "locales", "en.json"), "{ \"ui.menu.play\": \"Play\", \"story.base.key\": \"base\" }");
        return dir;
    }

    private static void WritePack(string dataDir, string folder, string id, Dictionary<string, string> en)
    {
        string packDir = Path.Combine(dataDir, "stories", folder);
        Directory.CreateDirectory(Path.Combine(packDir, "locales"));
        File.WriteAllText(Path.Combine(packDir, "story.json"), "{ \"id\": \"" + id + "\", \"nameKey\": \"story." + id + ".name\" }");
        File.WriteAllText(Path.Combine(packDir, "locales", "en.json"), JsonSerializer.Serialize(en));
    }

    /// <summary>"Zeta" sorts before "alpha" ordinally (upper case first) but after it on a case-insensitive file
    /// system listing — the ordinal order decides, so alpha's text wins the shared key on every OS, and the overwrite is
    /// reported. The built-in VEGA fills in, the beat-less packs are no world story.</summary>
    [Fact]
    public void PackFolders_LoadInOrdinalOrder_AndAnOverwrittenKeyIsReported()
    {
        string data = TempDataDir();
        WritePack(data, "Zeta", "zeta", new() { ["story.shared.line"] = "from Zeta", ["story.zeta.name"] = "Zeta" });
        WritePack(data, "alpha", "alpha", new() { ["story.shared.line"] = "from alpha", ["story.alpha.name"] = "Alpha" });
        var warnings = new List<string>();

        var content = ContentLoader.LoadFromDirectory(data, warn: warnings.Add);

        Assert.Equal("from alpha", content.CreateLocalizer(GameLocale.English).Get("story.shared.line"));
        string warning = Assert.Single(warnings);
        Assert.Contains("stories/alpha/locales/en.json", warning, StringComparison.Ordinal);
        Assert.Contains("stories/Zeta/locales/en.json", warning, StringComparison.Ordinal);
        Assert.Contains("story.shared.line", warning, StringComparison.Ordinal);

        Assert.True(content.Stories.ContainsKey("alpha"));
        Assert.True(content.Stories.ContainsKey("zeta"));
        Assert.Equal(StoryRegistry.DefaultStoryId, content.DefaultStory.Id);
        Assert.False(content.TryGetWorldStory("alpha", out _));
    }

    /// <summary>A pack key that overwrites the base table is reported too; a pack with a reserved id is skipped (its
    /// texts do not merge) and reported; a repeated id is reported.</summary>
    [Fact]
    public void BaseOverrides_ReservedIds_AndRepeatedIds_AreReported()
    {
        string data = TempDataDir();
        WritePack(data, "a_pack", "shadow", new() { ["story.base.key"] = "shadowed" });
        WritePack(data, "b_pack", "none", new() { ["story.none.secret"] = "never merged" });
        WritePack(data, "c_pack", "shadow", new() { ["story.shadow.again"] = "again" });
        var warnings = new List<string>();

        var content = ContentLoader.LoadFromDirectory(data, warn: warnings.Add);

        Assert.Contains(warnings, w => w.Contains("stories/a_pack/locales/en.json", StringComparison.Ordinal)
                                       && w.Contains("locales/en.json", StringComparison.Ordinal) && w.Contains("story.base.key", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("reserved id 'none'", StringComparison.Ordinal));
        Assert.Contains(warnings, w => w.Contains("'shadow' is used by more than one folder", StringComparison.Ordinal));
        Assert.False(content.Stories.ContainsKey("none"));
        Assert.False(content.CreateLocalizer(GameLocale.English).Has("story.none.secret"));
    }

    /// <summary>#2489: one malformed pack — its <c>story.json</c>, or only its <c>cutscenes.json</c> — used to abort the whole
    /// content load. Now that pack is skipped with its texts, the error names the pack folder and the file, the other packs
    /// load, and the content remembers which packs failed (an error callback, falling back to the warning one).</summary>
    [Fact]
    public void AMalformedPack_IsSkippedWithItsTexts_TheOthersLoad_AndTheErrorNamesPackAndFile()
    {
        string data = TempDataDir();
        WritePack(data, "alpha", "alpha", new() { ["story.alpha.name"] = "Alpha" });
        WritePack(data, "broken", "broken", new() { ["story.broken.secret"] = "never merged" });
        File.WriteAllText(Path.Combine(data, "stories", "broken", "story.json"), "{ \"id\": \"broken\", \"chapters\": [ { \"id\": ");
        WritePack(data, "scenes", "scenes", new() { ["story.scenes.secret"] = "never merged either" });
        File.WriteAllText(Path.Combine(data, "stories", "scenes", "cutscenes.json"), "{ \"cutscenes\": [ { \"id\": \"x\", ");
        WritePack(data, "zeta", "zeta", new() { ["story.zeta.name"] = "Zeta" });
        var warnings = new List<string>();
        var errors = new List<string>();

        var content = ContentLoader.LoadFromDirectory(data, warn: warnings.Add, error: errors.Add);

        Assert.True(content.Stories.ContainsKey("alpha"));
        Assert.True(content.Stories.ContainsKey("zeta"));
        Assert.False(content.Stories.ContainsKey("broken"));
        Assert.False(content.Stories.ContainsKey("scenes"));
        var en = content.CreateLocalizer(GameLocale.English);
        Assert.Equal("Alpha", en.Get("story.alpha.name"));
        Assert.False(en.Has("story.broken.secret"));
        Assert.False(en.Has("story.scenes.secret"));

        Assert.Equal(2, errors.Count);
        Assert.Contains(errors, e => e.Contains("'broken'", StringComparison.Ordinal) && e.Contains("stories/broken/story.json", StringComparison.Ordinal));
        Assert.Contains(errors, e => e.Contains("'scenes'", StringComparison.Ordinal) && e.Contains("stories/scenes/cutscenes.json", StringComparison.Ordinal));
        Assert.DoesNotContain(warnings, w => w.Contains("could not be loaded", StringComparison.Ordinal));
        Assert.True(content.StoryPackFailedToLoad("broken"));
        Assert.True(content.StoryPackFailedToLoad("SCENES"));
        Assert.False(content.StoryPackFailedToLoad("alpha"));

        // Without an error callback the report goes to the warnings.
        var only = new List<string>();
        ContentLoader.LoadFromDirectory(data, warn: only.Add);
        Assert.Equal(2, only.Count(w => w.Contains("could not be loaded", StringComparison.Ordinal)));
    }

    /// <summary>#2489: a save whose own story pack failed to load runs the default story for the session — but never writes
    /// it as the save's active row (not even next to a usable row of an older story), so a restart with the pack repaired
    /// carries on with the save's own progress. An admin's explicit switch ends the hold.</summary>
    [Fact]
    public void ASaveWhosePackFailedToLoad_KeepsItsActiveRow_ForWhenThePackLoadsAgain()
    {
        var broken = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        broken.SetFailedStoryPacks(new[] { "my_arc" });
        var repo = Repo("held_row");
        repo.Initialize();
        repo.SaveStoryState(new StoredStoryState { StoryId = StoryRegistry.DefaultStoryId, Milestones = 3, Active = false, UpdatedUtc = "2026-10-01T10:00:00Z" });
        repo.SaveStoryState(new StoredStoryState { StoryId = "my_arc", Milestones = 7, Active = true, UpdatedUtc = "2026-10-11T10:00:00Z" });

        var server = Started(broken, "held_row", repo: repo);
        Assert.Equal(StoryRegistry.DefaultStoryId, server.StorySnapshot.StoryId); // the stand-in for this session
        server.RecordStoryMilestoneForTest();
        server.Stop();

        var rows = repo.ListStoryStates();
        var own = rows.Single(r => r.StoryId == "my_arc");
        Assert.True(own.Active);
        Assert.Equal(7, own.Milestones);
        var vega = rows.Single(r => r.StoryId == StoryRegistry.DefaultStoryId);
        Assert.False(vega.Active); // the stand-in never became the save's story
        Assert.Equal(3, vega.Milestones);

        // The pack is repaired: the save's own story and progress are back.
        var repaired = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        repaired.SetStories(repaired.Stories.Values.Append(WithBeats("my_arc")));
        var again = Started(repaired, "held_row", repo: Repo("held_row"));
        Assert.Equal("my_arc", again.StorySnapshot.StoryId);
        Assert.Equal(7, again.StorySnapshot.Milestones);

        // An admin's switch while the pack is broken is a choice: the new story is saved, the broken one steps back.
        var switched = Repo("held_switch");
        switched.Initialize();
        switched.SaveStoryState(new StoredStoryState { StoryId = "my_arc", Milestones = 7, Active = true, UpdatedUtc = "2026-10-11T10:00:00Z" });
        var admin = Started(broken, "held_switch", repo: switched);
        admin.SetActiveStoryForTest(StoryRegistry.NoneStoryId);
        Assert.True(switched.ListStoryStates().Single(r => r.StoryId == StoryRegistry.NoneStoryId).Active);
        Assert.False(switched.ListStoryStates().Single(r => r.StoryId == "my_arc").Active);
    }

    /// <summary>The shipped data loads without a single collision or pack warning.</summary>
    [Fact]
    public void TheShippedData_LoadsWithoutPackWarnings()
    {
        var warnings = new List<string>();
        ContentLoader.LoadFromDirectory(TestPaths.DataDir(), warn: warnings.Add);
        Assert.DoesNotContain(warnings, w => w.Contains("Locale file", StringComparison.Ordinal) || w.Contains("Story pack", StringComparison.Ordinal));
    }

    /// <summary>Every pack ships English and German with the same key set (AGENTS.md: DE + EN mandatory).</summary>
    [Fact]
    public void EveryPack_HasEnglishAndGermanWithTheSameKeys()
    {
        foreach (string packDir in Directory.GetDirectories(Path.Combine(TestPaths.DataDir(), "stories")))
        {
            string locales = Path.Combine(packDir, "locales");
            if (!Directory.Exists(locales))
            {
                continue;
            }

            var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(locales, "en.json")))!;
            var de = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(locales, "de.json")))!;
            string pack = Path.GetFileName(packDir);
            Assert.True(en.Keys.ToHashSet().SetEquals(de.Keys),
                $"{pack}: en/de key sets differ — only en: {string.Join(", ", en.Keys.Except(de.Keys).Take(10))}; only de: {string.Join(", ", de.Keys.Except(en.Keys).Take(10))}");
        }
    }
}
