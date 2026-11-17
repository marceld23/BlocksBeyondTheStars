// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3766 on the client: the Feed control lifts the block in the hand when there is nothing to feed, the attack control
/// throws it and the hands do nothing else meanwhile; the figure — the own one and every other player's — holds the
/// block over the head; the flight is drawn on the server's arc. The rules are the server suite's
/// (<c>BlockCarryThrowTests</c>); here the pose numbers and the Unity wiring (as source text — the scripts cannot load
/// headless, the <see cref="ClientQuirksWiringTests"/> pattern).
/// </summary>
public sealed class BlockCarryWiringTests
{
    private static string Script(string file)
        => File.ReadAllText(Path.Combine(ClientTestPaths.RepoRoot(), "client", "Assets", "BlocksBeyondTheStars", "Scripts", file));

    [Fact]
    public void ThePose_RaisesQuickly_AndTheCarriedBlockIsHalfABlock()
    {
        float blend = 0f;
        for (int i = 0; i < 12; i++)
        {
            blend = BlockCarryPose.Ease(blend, carrying: true, 1f / 60f);
        }

        Assert.Equal(1f, blend, 3); // up within RaiseSeconds (0.18 s = 11 frames at 60 Hz)
        Assert.True(BlockCarryPose.Ease(1f, carrying: false, 0.05f) < BlockCarryPose.Ease(0f, carrying: true, 0.05f) + 0.5f);
        Assert.Equal(0f, BlockCarryPose.Ease(0.1f, carrying: false, 1f));

        Assert.Equal(BlockCarryPose.BlockEdge, 0.22f * BlockCarryPose.ScaleFor(0.22f), 4); // the palm cube becomes half a block
        Assert.Equal(1f, BlockCarryPose.ScaleFor(0f));                                     // a model without a size keeps its scale
        Assert.InRange(BlockCarryPose.ArmLift, 150f, 180f);                                // straight up
        Assert.True(BlockCarryPose.BlockHeight - (BlockCarryPose.BlockEdge / 2f) > 2f, "the block's underside clears the head");
    }

    [Fact]
    public void TheClientsArc_IsTheServersArc()
    {
        // A throw the server flies: from 12 up, 9 blocks a second forward and 4 up, for 1.1 seconds.
        const float fromY = 12f, vy = 4f, seconds = 1.1f;
        BlockThrow.PositionAt(0f, fromY, 0f, 9f, vy, 0f, seconds, out _, out float toY, out _);
        foreach (float t in new[] { 0f, 0.3f, 0.7f, seconds })
        {
            BlockThrow.PositionAt(0f, fromY, 0f, 9f, vy, 0f, t, out _, out float y, out _);
            Assert.Equal(y, BlockThrow.ArcY(fromY, toY, seconds, t), 3); // rebuilt from the two ends and the time
        }

        Assert.Equal(7f, BlockThrow.ArcY(3f, 7f, 0f, 0f)); // no flight time: it is where it ends
    }

    [Fact]
    public void TheFeedControl_LiftsABlock_AndTheAttackControlThrowsIt()
    {
        string update = Script("PlayerController.cs");
        Assert.Contains("else if (InputMap.Down(InputAction.FeedCreature))", update);
        Assert.Contains("ToggleCarry();", update);
        Assert.Contains("if (!CarryTick() && InputMap.Down(InputAction.PrimaryFire) && WeaponSwingReady())", update); // no swing with the block up

        string carry = Script("PlayerController.Carry.cs");
        Assert.Contains("Game.Network?.SendCarryBlock(true);", carry);
        Assert.Contains("Game.Network?.SendThrowBlock(dir.x, dir.y, dir.z);", carry);
        Assert.Contains("Game.SelectedHotbarSlot != _carrySlot || !CanLiftBlock", carry);   // another slot puts it back
        Assert.Contains("Game.CarryRejections != _carryRejectionsSeen", carry);             // the server said no
        Assert.Contains("_carryPrimaryFrame == Time.frameCount", carry);                    // the throwing press does not also mine
        Assert.Contains("CarryRejections++;", Script("GameBootstrap.Messages.cs"));

        // Both hands hold the block: no mining, no placing, no digging loop.
        string interact = Script("PlayerController.Interact.cs");
        Assert.Contains("if (Game?.Network == null || Camera == null || CarryHoldsHands)", interact);
        Assert.Contains("!InputMap.PrimaryHeld() || CarryHoldsHands)", interact);
    }

    [Fact]
    public void TheFigure_HoldsTheBlockOverTheHead_OnTheOwnBodyAndOnOtherPlayers()
    {
        string avatar = Script("PlayerAvatar.cs");
        Assert.Contains("if (_cheerTimer > 0f || CarryShows)", avatar);
        Assert.Contains("!_fightGloves && !PoseCarriedBlock())", avatar);
        Assert.Contains("if (_cheerTimer <= 0f && CarryShows)", Script("PlayerAvatar.Startle.cs")); // a cheer still plays over it
        Assert.Contains("_carryingBlock && !_lying && !_seated && !_climbing && !_gliding", Script("PlayerAvatar.CarryBlock.cs"));

        Assert.Contains("Avatar?.SetCarryingBlock(on);", Script("PlayerController.Carry.cs"));
        Assert.Contains("_viewmodel?.SetCarrying(on);", Script("PlayerController.Carry.cs"));
        Assert.Contains("RestPos + _stanceShift + CarryShift() + bob", Script("Viewmodel.cs"));
        Assert.Contains("GearLook.CarryingOverhead));", Script("RemotePlayers.cs")); // the presence's bit
    }

    [Fact]
    public void TheFlight_IsDrawnFromTheServersEffect()
    {
        Assert.Contains("Game.Network.WorldFxReceived += OnThrownBlockFx;", Script("WorldEntities.cs"));
        Assert.Contains("Game.Network.WorldFxReceived -= OnThrownBlockFx;", Script("WorldEntities.cs"));
        string fx = Script("WorldEntities.ThrownBlocks.cs");
        Assert.Contains("fx.Kind != \"block_throw\"", fx);
        Assert.Contains("BlockThrow.ArcY(from.y, to.y, seconds, time)", fx);
        Assert.Contains("HeldItem.For(Game.Content, fx.Item)", fx);
    }

    [Fact]
    public void TheTexts_ExistInEnglishAndGerman_AndNameBothControls()
    {
        foreach (string code in new[] { "en", "de" })
        {
            var table = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "locales", code + ".json")));
            Assert.True(table.TryGetValue("srv.carry.cannot", out string? no) && !string.IsNullOrWhiteSpace(no), code);
            Assert.True(table.TryGetValue("ui.hud.carry_lifted", out string? lifted), code);
            Assert.Contains("{key:PrimaryFire}", lifted);
            Assert.Contains("{key:FeedCreature}", lifted);

            var wiki = ContentLoader.ParseLocaleTable(File.ReadAllText(Path.Combine(ClientTestPaths.DataDir(), "wiki", "locales", code + ".json")));
            Assert.Contains("{key:FeedCreature}", wiki["wiki.combat.body"]); // the Codex explains it
        }
    }
}
