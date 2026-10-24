// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2563: the Blueprints tab's "Researchable now" filter keeps only what the player can research right now —
/// prerequisites done, knowledge covering the cost AND the research materials owned. Its predecessor ("Enough
/// knowledge") looked at knowledge alone and so still listed cards reading "Materials missing" or "Locked". The card
/// status, the filter, the header's "next" line and the Tech badge share <see cref="BlueprintResearch"/>.
/// </summary>
public sealed class BlueprintResearchTests
{
    private static BlueprintDefinition Blueprint(int knowledge, string[]? prerequisites = null, params (string Item, int Count)[] cost)
        => new()
        {
            Key = "test_bp",
            Prerequisites = (prerequisites ?? Array.Empty<string>()).ToList(),
            UnlockCost = cost.Select(c => new ItemAmount(c.Item, c.Count)).ToList(),
            KnowledgeCost = knowledge,
        };

    private static Func<string, int> Owning(params (string Item, int Count)[] stacks)
        => item => stacks.Where(s => s.Item == item).Sum(s => s.Count);

    [Fact]
    public void EverythingThere_IsResearchable()
    {
        var bp = Blueprint(20, new[] { "pre" }, ("iron_plate", 2));

        Assert.Equal(BlueprintResearchState.Researchable,
            BlueprintResearch.StateOf(bp, new HashSet<string> { "pre" }, 20, Owning(("iron_plate", 2))));
        Assert.True(BlueprintResearch.IsResearchableNow(bp, new HashSet<string> { "pre" }, 25, Owning(("iron_plate", 3))));
    }

    [Fact]
    public void EnoughKnowledge_ButMaterialsMissing_IsNotResearchableNow()
    {
        // The player report: "Genug Wissen" ticked, yet a card reading "Material fehlt" stayed in the list.
        var bp = Blueprint(10, null, ("iron_plate", 2), ("data_fragment", 1));

        Assert.Equal(BlueprintResearchState.MaterialsMissing,
            BlueprintResearch.StateOf(bp, new HashSet<string>(), 50, Owning(("iron_plate", 2))));
        Assert.False(BlueprintResearch.IsResearchableNow(bp, new HashSet<string>(), 50, Owning(("iron_plate", 2))));
    }

    [Fact]
    public void EnoughKnowledge_ButAPrerequisiteMissing_IsLocked()
    {
        var bp = Blueprint(10, new[] { "pre_a", "pre_b" });

        Assert.Equal(BlueprintResearchState.Locked,
            BlueprintResearch.StateOf(bp, new HashSet<string> { "pre_a" }, 50, Owning()));
        Assert.False(BlueprintResearch.IsResearchableNow(bp, new HashSet<string> { "pre_a" }, 50, Owning()));
    }

    [Fact]
    public void KnowledgeIsNamedBeforeMaterials()
    {
        var bp = Blueprint(30, null, ("iron_plate", 2));

        Assert.Equal(BlueprintResearchState.KnowledgeMissing,
            BlueprintResearch.StateOf(bp, new HashSet<string>(), 29, Owning()));
    }

    [Fact]
    public void AlreadyResearched_IsNeverListedAsResearchable()
    {
        var bp = Blueprint(0);

        Assert.Equal(BlueprintResearchState.Researched,
            BlueprintResearch.StateOf(bp, new HashSet<string> { "test_bp" }, 100, Owning()));
        Assert.False(BlueprintResearch.IsResearchableNow(bp, new HashSet<string> { "test_bp" }, 100, Owning()));
    }

    /// <summary>#2958: a research cost that takes any log (the field kitchen) is paid with pale logs once the content is
    /// passed — the groups live there, as on the server.</summary>
    [Fact]
    public void AGroupCost_CountsEveryMemberOfItsGroup()
    {
        var content = BlocksBeyondTheStars.Shared.Content.ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());
        var kitchen = content.Blueprints["field_kitchen"];
        var owned = Owning(("plant_fiber", 4), ("berries", 2), ("wood_log_b", 2));

        Assert.Equal(BlueprintResearchState.MaterialsMissing, BlueprintResearch.StateOf(kitchen, new HashSet<string>(), 100, owned));
        Assert.Equal(BlueprintResearchState.Researchable, BlueprintResearch.StateOf(kitchen, new HashSet<string>(), 100, owned, content));
    }
}
