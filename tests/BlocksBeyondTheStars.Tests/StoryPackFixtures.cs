// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Story;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The test story packs of the player-pack runtime (#2466) under <c>tests/BlocksBeyondTheStars.Tests/Fixtures/story_packs/</c>
/// — never under <c>data/stories/</c>, so the game never ships them: <c>runtime_test</c> (a side story with every step kind,
/// an alternative, a CORE step, an optional chapter with a mission bridge), <c>tutorial_test</c> (a tiny tutorial with
/// legacy keys and gates), <c>cutscene_test</c> (#2467: every cutscene binding, with its <c>cutscenes.json</c>) and
/// <c>side_pilot</c> (#2483: the pilot side story — the "new story = data only" proof and the authoring guide's example).
/// </summary>
internal static class StoryPackFixtures
{
    public const string Runtime = "runtime_test";
    public const string Tutorial = "tutorial_test";
    public const string Cutscenes = "cutscene_test";
    public const string SidePilot = "side_pilot";

    /// <summary>The fixture pack's folder.</summary>
    public static string Dir(string id)
        => Path.Combine(TestPaths.RepoRoot(), "tests", "BlocksBeyondTheStars.Tests", "Fixtures", "story_packs", id);

    /// <summary>The fixture pack's <c>story.json</c> text.</summary>
    public static string RawJson(string id) => File.ReadAllText(Path.Combine(Dir(id), "story.json"));

    /// <summary>The fixture pack, deserialized exactly like the content loader does (with its <c>cutscenes.json</c>).</summary>
    public static StoryDefinition Load(string id)
    {
        var def = JsonSerializer.Deserialize<StoryDefinition>(RawJson(id), ContentLoader.JsonOptions)!;
        def.Cutscenes.AddRange(ContentLoader.LoadPackCutscenes(Dir(id)));
        return def;
    }

    /// <summary>One locale table of the fixture pack.</summary>
    public static Dictionary<string, string> Locale(string id, string code)
        => ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(Dir(id), "locales", code + ".json")));

    /// <summary>The shipped content plus the named fixture packs — without the shipped player packs (#2469: the official
    /// tutorial), so a fixture's runtime mechanics run alone and its chip is not outranked by the real basic training.</summary>
    public static GameContent ContentWith(params string[] ids)
    {
        var content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
        var shippedWorldPacks = content.Stories.Values.Where(d => StoryRegistry.EffectiveScope(d) != StoryRegistry.ScopePlayer);
        content.SetStories(shippedWorldPacks.Concat(ids.Select(Load)).ToList());
        return content;
    }

    /// <summary>The shipped content without its player packs (#2469: a world with VEGA but no basic training).</summary>
    public static GameContent ContentWithoutPlayerPacks() => ContentWith();

    /// <summary>#2483: a copy of the repository's <c>data/</c> folder under <paramref name="targetDir"/> with the named fixture
    /// packs dropped in as <c>stories/&lt;id&gt;/</c> — exactly what an author does to install a pack. Returns the copy's path
    /// (load it with <see cref="ContentLoader.LoadFromDirectory"/>; the caller deletes <paramref name="targetDir"/>).</summary>
    public static string CopyDataWith(string targetDir, params string[] ids)
    {
        string data = Path.Combine(targetDir, "data");
        CopyTree(TestPaths.DataDir(), data);
        foreach (string id in ids)
        {
            CopyTree(Dir(id), Path.Combine(data, "stories", id));
        }

        return data;
    }

    private static void CopyTree(string from, string to)
    {
        foreach (string file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }
}
