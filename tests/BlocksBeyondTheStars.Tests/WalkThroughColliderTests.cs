// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3422: the client's chunk mesher and the player's out-of-world guard (<c>PlayerController.GuardAgainstFallingOut</c> →
/// <c>EnclosedAt</c> → <c>IsCollidingKey</c>) must agree on which blocks have a collider. A cell the mesher draws without one
/// is walked into; when the guard still reads its key as solid, it calls the body "stuck in a block" and after twelve frames
/// snaps the player back to the last spot before it. Soaniri's singing plants did that, and so did an open phase-block door,
/// a switched-off force field and an open trapdoor (only the open lift gate, #3174, had been taught to the guard). Both now
/// read the Shared rule (<see cref="WalkThroughBlocks.CubeHasCollider"/>, <see cref="WalkThroughBlocks.IsProp"/>); the Unity
/// client cannot be compiled here, so its two ends are pinned in the source.
/// </summary>
public sealed class WalkThroughColliderTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The open twins and the shut blocks they replace.</summary>
    private static readonly (string Open, string Shut)[] Twins =
    {
        ("phase_block_open", "phase_block"), ("force_field_off", "force_field"), ("trapdoor_open", "trapdoor"),
        ("lift_gate_open", "lift_gate"),
    };

    [Fact]
    public void EveryBlockTheMesherDrawsWithoutACollider_IsNoBlockTheGuardIsStuckIn()
    {
        foreach (var def in Content.Blocks.Values)
        {
            bool noCollider = !WalkThroughBlocks.CubeHasCollider(def.Key, def.Liquid)
                || (FloraCatalog.IsPlantKey(def.Key) && !FloraCatalog.IsSolid(def.Key)) // the cross-billboard plants
                || def.Key is "torch" or "lantern" or "torch_off" or "lantern_off" or "ladder"; // the slim props' own geometry
            if (noCollider)
            {
                Assert.False(GuardCollides(def), $"'{def.Key}' is drawn without a collider, but the out-of-world guard reads it as solid");
            }
        }
    }

    [Fact]
    public void TheOpenTwins_AreWalkedThrough_AndTheShutOnesStillStopYou()
    {
        foreach (var (open, shut) in Twins)
        {
            var openDef = Content.GetBlock(open);
            var shutDef = Content.GetBlock(shut);
            Assert.True(openDef != null && shutDef != null, $"'{open}' / '{shut}' missing from data/blocks.json");
            Assert.False(openDef!.Solid, open);
            Assert.True(WalkThroughBlocks.IsOpenTwin(open), open);
            Assert.False(WalkThroughBlocks.CubeHasCollider(open, openDef.Liquid), open);
            Assert.False(GuardCollides(openDef), open);

            Assert.False(WalkThroughBlocks.IsOpenTwin(shut), shut);
            Assert.True(WalkThroughBlocks.CubeHasCollider(shut, shutDef!.Liquid), shut);
            Assert.True(GuardCollides(shutDef), shut);
        }

        Assert.True(WalkThroughBlocks.CubeHasCollider("lava", liquid: false)); // lava is stood on, not sunk into
        Assert.True(WalkThroughBlocks.CubeHasCollider("stone", liquid: false));
        Assert.True(WalkThroughBlocks.CubeHasCollider(null, liquid: false)); // an id without a definition is a wall
        Assert.False(WalkThroughBlocks.CubeHasCollider("oil", liquid: true));
    }

    /// <summary>The Unity client cannot be compiled here: its two ends of the rule are pinned in the source — the mesher's
    /// collider trait reads the Shared rule (no private open-twin list), and the guard's key test is the one
    /// <see cref="GuardCollides"/> mirrors (<c>LineOfSightTests</c> pins its <c>IsProp</c> half).</summary>
    [Fact]
    public void TheMesherAndTheGuard_ReadTheSharedRule()
    {
        string scripts = Path.Combine(TestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts");
        string mesher = File.ReadAllText(Path.Combine(scripts, "ChunkMesher.cs"));
        Assert.Contains("WalkThroughBlocks.CubeHasCollider(key, liquid)) f |= TraitCollidable;", mesher);
        Assert.DoesNotContain("\"trapdoor_open\"", mesher);

        string player = TestPaths.PlayerControllerSources();
        Assert.Contains("=> !string.IsNullOrEmpty(key) && key != \"air\" && key != \"water\" && key != \"lava\" && !LiquidKeys.Contains(key)", player);
        Assert.Contains("&& !TreeFoliage.IsKey(key);", player);
        Assert.Contains("=> IsSolidKey(key) && !BlocksBeyondTheStars.Shared.Definitions.WalkThroughBlocks.IsProp(key);", player);
    }

    /// <summary><c>PlayerController.IsCollidingKey</c>: a key the guard counts as solid — not air, water, lava, a liquid
    /// (<c>LiquidKeys</c>, from the data's <c>liquid</c> flag) or a tree crown — and no walk-through prop.</summary>
    private static bool GuardCollides(BlockDefinition def)
        => def.Key is not ("air" or "water" or "lava") && !def.Liquid && !TreeFoliage.IsKey(def.Key)
           && !WalkThroughBlocks.IsProp(def.Key);
}
