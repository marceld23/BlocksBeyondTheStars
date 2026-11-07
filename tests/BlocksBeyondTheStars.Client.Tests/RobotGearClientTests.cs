// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3398/#3399 — the robot's gear on the client: with a weapon or a jetpack in hand E at the own robot GIVES it (the prompt says
/// "Give … to Robo"), with anything else it talks; the Companions tab lists the robot's pieces with "Take back"; the creature
/// wire tells the body whether it wears a jetpack and flies. No armour is ever a piece of gear.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class RobotGearClientTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    [Fact]
    public void AWeaponOrAJetpackInHand_MakesEAtTheRobotAGift_AnythingElseATalk()
    {
        var f = new InteractFacts { AimedCompanion = true, Crate = true };
        Assert.Equal(InteractKind.AimedCompanion, InteractResolver.Resolve(f));

        f.HoldsRobotGear = true;
        Assert.Equal(InteractKind.GiveCompanion, InteractResolver.Resolve(f));
        Assert.True(InteractResolver.IsAimed(InteractKind.GiveCompanion));
        Assert.False(InteractResolver.HasWorldLabel(InteractKind.GiveCompanion)); // the HUD names it

        f.AimedNpc = true; // a person under the crosshair still stands first
        Assert.Equal(InteractKind.AimedTalk, InteractResolver.Resolve(f));

        f.Clear();
        Assert.False(f.HoldsRobotGear);
        f.HoldsRobotGear = true; // a weapon in hand, but no robot aimed at: nothing to give
        Assert.Equal(InteractKind.None, InteractResolver.Resolve(f));
    }

    [Theory]
    [InlineData("laser_pistol", RobotGear.WeaponPiece)]
    [InlineData("guardian_sword", RobotGear.WeaponPiece)]
    [InlineData("shock_gloves", RobotGear.WeaponPiece)]
    [InlineData("jetpack", RobotGear.JetpackPiece)]
    [InlineData("stone", "")]
    [InlineData("glider", "")]
    [InlineData("", "")]
    public void TheHeldItem_IsAPieceOfGear_OnlyWhenItIsAWeaponOrTheJetpack(string held, string piece)
    {
        Assert.Equal(piece, RobotGearView.GiftPiece(Content, held));
        Assert.Equal(string.Empty, RobotGearView.GiftPiece(null, held));
    }

    [Fact]
    public void NoArmour_IsEverAPieceOfGear()
    {
        foreach (var item in Content.Items.Values)
        {
            if (!string.IsNullOrEmpty(item.EquipSlot) && item.Key != RobotGear.JetpackItem)
            {
                Assert.Equal(string.Empty, RobotGear.PieceOf(item)); // worn suit parts (armour, boots, glider …) stay with the player
            }
        }
    }

    [Fact]
    public void TheCompanionsTab_ListsTheRobotsPieces_WeaponFirst()
    {
        var robot = new NetCompanion { Id = "r", Kind = "robot", GearWeapon = "laser_pistol", GearJetpack = "jetpack" };
        var pieces = RobotGearView.Pieces(robot);

        Assert.True(RobotGearView.HasGearRow(robot));
        Assert.Equal(new[] { (RobotGear.WeaponPiece, "laser_pistol"), (RobotGear.JetpackPiece, "jetpack") }, pieces);
        Assert.Empty(RobotGearView.Pieces(new NetCompanion { Kind = "robot" }));
        Assert.False(RobotGearView.HasGearRow(new NetCompanion { Kind = "reindeer" })); // only the robot takes gear
    }

    [Fact]
    public void TheCreatureWire_SaysWhetherItWearsAJetpackAndFlies()
    {
        var c = new NetCreature { GearFlags = RobotGear.FlagJetpack };
        Assert.True(RobotGearView.WearsJetpack(c));
        Assert.False(RobotGearView.Flying(c));

        c.GearFlags |= RobotGear.FlagFlying;
        Assert.True(RobotGearView.Flying(c));
        Assert.False(RobotGearView.WearsJetpack(new NetCreature())); // an older server, or no jetpack
    }

    [Fact]
    public void TheWeaponRules_ScaleTheShot_AndClampTheReach()
    {
        var laser = Content.GetItem("laser_pistol");
        var gloves = Content.GetItem("shock_gloves");

        Assert.Equal(21f, RobotGear.ShotDamage(laser, 6f), 3);
        Assert.Equal(6f, RobotGear.ShotDamage(gloves, 6f));        // 60 % of 3 is less than its own shot
        Assert.Equal(6f, RobotGear.ShotDamage(null, 6f));
        Assert.Equal(RobotGear.MaxReach, RobotGear.Reach(laser, 14f));
        Assert.Equal(RobotGear.MinReach, RobotGear.Reach(gloves, 14f));
        Assert.Equal(14f, RobotGear.Reach(null, 14f));
        Assert.Equal(0xFF4A3D, RobotGear.TracerRgb(laser));
        Assert.Equal(0, RobotGear.TracerRgb(null));
    }
}
