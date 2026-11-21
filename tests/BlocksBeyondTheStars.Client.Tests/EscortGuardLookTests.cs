// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3897 — the sleeper's look on the client (<see cref="EscortGuardLook"/>): the look id, the full Guardian set by the
/// players' own mask rule, the Guardian sword with its lights turned blue, when he holds it — and the Unity wiring as
/// source text (the Unity scripts cannot load headless).
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class EscortGuardLookTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void OnlyTheSleepersLook_IsAnEscortGuard()
    {
        Assert.True(EscortGuardLook.Is("sps_guard"));
        Assert.False(EscortGuardLook.Is("gds_guard")); // the G.D.S. machines keep their own look
        Assert.False(EscortGuardLook.Is(string.Empty));
        Assert.False(EscortGuardLook.Is(null));
    }

    [Fact]
    public void HeWears_TheFullGuardianSet_AsAPlayerDoes()
    {
        int mask = EscortGuardLook.GearMask;

        Assert.True(GearLook.Has(mask, GearLook.Helmet));
        Assert.True(GearLook.Has(mask, GearLook.Chest));
        Assert.True(GearLook.Has(mask, GearLook.Legs));
        Assert.True(GearLook.Has(mask, GearLook.Boots));
        Assert.Equal(GearLook.StyleGuardian, GearLook.HeadStyle(mask));
        Assert.Equal(GearLook.StyleGuardian, GearLook.ChestStyle(mask));
        Assert.Equal(GearLook.StyleGuardian, GearLook.LegsStyle(mask));
        Assert.Equal(GearLook.StyleGuardian, GearLook.BootsStyle(mask));
        Assert.False(GearLook.Has(mask, GearLook.Jetpack)); // the armour, nothing else

        // The four pieces are real items, so a player's mask of the same set is the same mask.
        foreach (string piece in new[] { "guardian_helmet", "guardian_chest", "guardian_legs", "guardian_boots" })
        {
            Assert.NotNull(Content.Value.GetItem(piece));
        }
    }

    [Fact]
    public void HisSword_IsTheGuardianSword_WithItsLightsBlue()
    {
        var sword = Content.Value.GetItem(EscortGuardLook.SwordItemKey);
        Assert.NotNull(sword);
        var parts = EscortGuardLook.SwordParts(sword);

        Assert.Equal(sword!.HeldModel!.Count, parts.Count);
        Assert.Contains(parts, p => p.M == "energy");
        for (int i = 0; i < parts.Count; i++)
        {
            var was = sword.HeldModel[i];
            Assert.Equal(was.P, parts[i].P);
            Assert.Equal(was.S, parts[i].S);
            Assert.Equal(was.M, parts[i].M);
            if (was.M == "energy")
            {
                Assert.Equal(EscortGuardLook.SwapRedBlue(was.C), parts[i].C);
                Assert.NotEqual(was.C, parts[i].C); // no glowing part keeps the red
            }
            else
            {
                Assert.Equal(was.C, parts[i].C); // the dark metal stays
            }
        }

        Assert.Equal("#2a2aff", EscortGuardLook.SwapRedBlue("#ff2a2a"));
        Assert.Equal("tint", EscortGuardLook.SwapRedBlue("tint"));
        Assert.Empty(EscortGuardLook.SwordParts(null));
        Assert.Equal(0x3D8CFF, EscortGuardLook.LightRgb);
        Assert.Equal(Content.Value.GetBlock("light_blue")!.LightColor, EscortGuardLook.LightRgb); // the blue lamp's own light
    }

    [Fact]
    public void HeHoldsTheSword_OnlyStanding_AndOnlyWhenTheServerNamesNoTool()
    {
        Assert.Equal(EscortGuardLook.SwordHeldId, EscortGuardLook.HeldFor("sps_guard", 0, string.Empty));
        Assert.Equal(EscortGuardLook.SwordHeldId, EscortGuardLook.HeldFor("sps_guard", 0, null));
        Assert.Equal(string.Empty, EscortGuardLook.HeldFor("sps_guard", 1, string.Empty)); // seated
        Assert.Equal(string.Empty, EscortGuardLook.HeldFor("sps_guard", 2, string.Empty)); // asleep
        Assert.Equal("npc_hoe", EscortGuardLook.HeldFor("sps_guard", 0, "npc_hoe"));       // a tool the server names wins
        Assert.Equal("npc_hoe", EscortGuardLook.HeldFor("settler", 0, "npc_hoe"));
        Assert.Equal(string.Empty, EscortGuardLook.HeldFor("settler", 0, string.Empty));
    }

    [Fact]
    public void TheUnitySide_DressesHim_InTheSuit_WithBlueLights_AndNeverTouchesTheGearMaskOnTheWire()
    {
        string view = Script("NpcView.cs");
        Assert.Contains("bool escort = EscortGuardLook.Is(nd.Look);", view);
        Assert.Contains("spacesuit: escort", view);
        Assert.Contains("avatar.SetEscortGuardLook();", view);
        Assert.Contains("held = EscortGuardLook.HeldFor(nd.Look, nd.Pose, held);", view);
        Assert.Contains("if (!HoldEscortSword(n, held))", view);

        string avatar = Script("PlayerAvatar.EscortGuard.cs");
        Assert.Contains("EscortGuardLook.LightRgb", avatar);
        Assert.Contains("SetGear(EscortGuardLook.GearMask);", avatar);

        string sword = Script("NpcView.EscortGuard.cs");
        Assert.Contains("EscortGuardLook.SwordParts(", sword);
        Assert.DoesNotContain("PlayerPresence", avatar + sword);
    }
}
