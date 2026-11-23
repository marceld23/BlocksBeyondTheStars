// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3763 (Justus, 2026-11-17: "sometimes you do not hold weapons and tools properly! The guardian sword, for example. The
/// wooden pickaxe has a drill animation???"): the stance of a held item. A blade rests tipped up instead of pointing into the
/// screen like a gun; the wooden pickaxe — a <c>drill</c> by its tool kind — is recognised as a pick from its model (no bit
/// on its axis), rests further up, chops over the head and has neither the drill's flourish nor its sound. The Unity wiring
/// is guarded as source text (the scripts cannot load headless — the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class HeldStancesTests
{
    private const float Frame = 1f / 60f;
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void ThePickaxe_IsAPick_ByItsModel_AndEveryRealDrillStaysADrill()
    {
        var drillKind = Content.Items.Values.Where(i => i.Tool?.Kind == ToolKind.Drill).ToList();
        Assert.True(drillKind.Count >= 6);
        var picks = drillKind.Where(i => HeldStances.IsPickModel(i.HeldModel)).Select(i => i.Key).ToList();
        Assert.Equal(new[] { "wood_pickaxe" }, picks); // from the model: its head sits across the handle, no bit on the axis

        Assert.False(HeldStances.IsPickModel(null)); // a drill without a model of its own (the basic drill) is the kind's drill
        Assert.False(HeldStances.IsPickModel(Array.Empty<HeldModelPart>()));
        Assert.False(HeldStances.IsPick(null));
        Assert.False(HeldStances.IsPick(HeldItemShapes.Parts("Drill", new HeldItemShapes.Rgb(0.6f, 0.6f, 0.6f)))); // the default drill has its bit
    }

    [Fact]
    public void APick_IsDrawnFromItsModel_HasNoFlourish_AndIsHeldInTheFist()
    {
        Assert.Equal("Pick", HeldStances.PickKind);
        Assert.True(HeldItemShapes.IsShapedKind(HeldStances.PickKind));
        var model = Content.GetItem("wood_pickaxe")!.HeldModel;
        var tint = new HeldItemShapes.Rgb(0.6f, 0.6f, 0.6f);
        Assert.Equal(HeldItemShapes.Parts("Drill", tint, model)!.Count, HeldItemShapes.Parts(HeldStances.PickKind, tint, model)!.Count);

        Assert.Equal(HeldFlourishKind.None, HeldFlourishes.ForKind(HeldStances.PickKind)); // no spin-up, no drill_spinup cue
        Assert.Equal(HeldFlourishKind.DrillSpinUp, HeldFlourishes.ForKind("Drill"));
        Assert.Equal(HeldGrips.Fist, HeldHands.GripFor(null, HeldStances.PickKind));
    }

    [Theory]
    [InlineData("Blade", HeldStance.Raised)]
    [InlineData("Pick", HeldStance.Pick)]
    [InlineData("Drill", HeldStance.Level)]
    [InlineData("Gun", HeldStance.Level)]
    [InlineData("Scanner", HeldStance.Level)]
    [InlineData("Gadget", HeldStance.Level)]
    [InlineData("Block", HeldStance.Level)]
    [InlineData("Hand", HeldStance.Level)]
    [InlineData(null, HeldStance.Level)]
    public void EachKind_HasItsStance(string? kind, HeldStance stance) => Assert.Equal(stance, HeldStances.For(kind));

    [Fact]
    public void ABladeAndAPick_RestTippedUp_EverythingElseStaysLevel()
    {
        Assert.Equal((0f, 0f, 0f), HeldStances.RestTurn(HeldStance.Level));
        Assert.Equal((0f, 0f, 0f), HeldStances.RestShift(HeldStance.Level));
        Assert.Equal(0f, HeldStances.CarryTip(HeldStance.Level));

        // A negative X turn tips the far end of a model built along +Z up: the blade's length and the pick's head show.
        Assert.InRange(HeldStances.RestTurn(HeldStance.Raised).X, -50f, -25f);
        Assert.True(HeldStances.RestTurn(HeldStance.Pick).X < HeldStances.RestTurn(HeldStance.Raised).X, "the pick further up than a blade");
        Assert.InRange(HeldStances.RestTurn(HeldStance.Pick).X, -60f, -35f);
        Assert.InRange(HeldStances.CarryTip(HeldStance.Raised), 25f, 50f);
        Assert.InRange(HeldStances.CarryTip(HeldStance.Pick), HeldStances.CarryTip(HeldStance.Raised), 60f);

        // Tipped up, the item sits a little lower and nearer so its upper end stays in the view — never further away.
        foreach (var stance in new[] { HeldStance.Raised, HeldStance.Pick })
        {
            var shift = HeldStances.RestShift(stance);
            Assert.InRange(shift.Y, -0.10f, 0f);
            Assert.InRange(shift.Z, -0.08f, 0f);
        }
    }

    [Fact]
    public void ThePicksChop_WindsUp_StrikesDown_AndComesBackToRest_WithoutAJump()
    {
        Assert.True(HeldStances.Chops(HeldStance.Pick));
        Assert.False(HeldStances.Chops(HeldStance.Raised)); // a blade keeps its slash
        Assert.False(HeldStances.Chops(HeldStance.Level));  // a drill keeps its jab

        Assert.Equal(0f, HeldStances.ChopPitch(0f), 3);
        Assert.Equal(0f, HeldStances.ChopPitch(1f), 3);
        Assert.Equal(-HeldStances.ChopBack, HeldStances.ChopPitch(HeldStances.ChopWindUp), 2);  // back over the shoulder
        Assert.Equal(HeldStances.ChopDown, HeldStances.ChopPitch(HeldStances.ChopStrike), 2);   // the head lands
        Assert.True(HeldStances.ChopWindUp < HeldStances.ChopStrike && HeldStances.ChopStrike < 0.7f, "the strike lands in the first two thirds");
        Assert.Equal(0f, HeldStances.ChopPitch(-1f), 3); // clamped
        Assert.Equal(0f, HeldStances.ChopPitch(2f), 3);

        // A swing lasts about a third of a second: frame to frame the pitch never jumps.
        const float swing = 0.3f;
        float last = HeldStances.ChopPitch(0f);
        for (float time = Frame; time <= swing; time += Frame)
        {
            float now = HeldStances.ChopPitch(time / swing);
            Assert.True(Math.Abs(now - last) < 30f, $"a jump of {now - last:0.0}° at {time:0.000} s");
            last = now;
        }
    }

    /// <summary>Justus, 2026-11-23 ("when I use a drill it goes up and down — take that out, keep only the spin"): a drill
    /// whose bit spins holds still while it mines, in first person and on every figure; nothing else changes its motion.</summary>
    [Fact]
    public void ADrillWithASpinningBit_HoldsStillWhileItMines_EverythingElseKeepsItsMotion()
    {
        Assert.True(HeldStances.HoldsStillWhileUsed(HeldStances.For("Drill"), spinningBit: true));
        Assert.False(HeldStances.HoldsStillWhileUsed(HeldStances.For("Drill"), spinningBit: false)); // no bit: the jab stays
        Assert.False(HeldStances.HoldsStillWhileUsed(HeldStances.For(HeldStances.PickKind), spinningBit: true)); // a pick chops
        Assert.False(HeldStances.HoldsStillWhileUsed(HeldStances.For("Blade"), spinningBit: false));
        Assert.False(HeldStances.HoldsStillWhileUsed(HeldStances.For("Block"), spinningBit: false));

        Assert.Contains("HeldStances.HoldsStillWhileUsed(_stance, _spin != null && _spin.HasBit)", Script("Viewmodel.cs"));
        string flourish = Script("PlayerAvatar.Flourish.cs");
        Assert.Contains("HeldStances.HoldsStillWhileUsed(_heldStance, _heldSpin != null && _heldSpin.HasBit)", flourish);
        Assert.Contains("PoseSwing(dt, ref armR, ref elbowR, ref level);", Script("PlayerAvatar.cs"));
        Assert.Contains("public bool HasBit => _bit != null && _bit.Length > 0;", Script("HeldSpin.cs"));
    }

    [Fact]
    public void TheClient_UsesTheStances()
    {
        Assert.Contains("HeldStances.IsPickModel(ModelResolver?.Invoke(itemKey)) ? Kind.Pick : Kind.Drill", Script("HeldItem.cs"));
        Assert.Matches(@"enum Kind \{[^}]*\bPick\b", Script("HeldItem.cs"));

        string view = Script("Viewmodel.cs");
        Assert.Contains("var rot = RestEuler + _stanceTurn;", view);
        Assert.Contains("var posOff = RestPos + _stanceShift + CarryShift() + bob;", view);
        Assert.Contains("PoseJabOrChop(t, jab, aroundForearm, ref posOff, ref rot);", view);
        Assert.Contains("SetKind(kind);", view);
        string stance = Script("Viewmodel.Stance.cs");
        Assert.Contains("HeldStances.For(kind.ToString())", stance);
        Assert.Contains("HeldStances.ChopPitch(t)", stance);

        Assert.Contains("- _carryTip) * level", Script("PlayerAvatar.cs")); // the third-person hand tips a blade and a pick up
        string flourish = Script("PlayerAvatar.Flourish.cs");
        Assert.Contains("_heldStance = HeldStances.For(kind.ToString());", flourish);
        Assert.Contains("_carryTip = HeldStances.CarryTip(_heldStance);", flourish);

        Assert.Contains("if (drill && !_heldPick)", Script("PlayerController.Interact.cs")); // no drill loop from a pick
        Assert.Contains("_heldPick = kind == HeldItem.Kind.Pick;", Script("PlayerController.View.cs"));
    }
}
