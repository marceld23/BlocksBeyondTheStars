// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Carrying a block over the head and throwing it (#3766 — Justus, 2026-11-17: "carry things with both hands and throw
/// them at enemies"): the rules (<see cref="BlockThrow"/>) and a live server. Lifting is a stance of the selected slot —
/// the block stays in the stack; a throw takes one block, hurts a machine or a wild animal by the block's material,
/// never a tame animal, and always leaves the block on the ground as an item.
/// </summary>
public sealed class BlockCarryThrowTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_throw_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    /// <summary>The first block item of a weight class (items and blocks are data — the test takes what is there).</summary>
    private string BlockItem(BlockWeight weight)
        => _content.Items.Values
            .Where(i => i.PlacesBlock is { Length: > 0 } b && _content.GetBlock(b) is { } block && BlockThrow.WeightOf(block.Hardness) == weight)
            .Select(i => i.Key)
            .OrderBy(k => k, StringComparer.Ordinal)
            .First();

    /// <summary>A player on foot in the middle of a flat pad, facing east (+X), with a stack in hotbar slot 0.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, PlayerSession Player, int PadY) Stage(string name, string item, int count = 3)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo);
        SoundersStage.Pacify(server);
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 20);
        var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f));
        p.State.Yaw = 90f;
        p.State.Inventory.SetSlot(0, new ItemStack(item, count));
        p.State.SelectedHotbarSlot = 0;
        server.TickForTest(0.1);
        return (server, repo, p, padY);
    }

    private static int Lying(SvGameServer server, string item)
        => server.DropPackets.Sum(c => c.Items.Where(s => s.Item == item).Sum(s => s.Count));

    // ---- the rules -------------------------------------------------------------------------------------------------

    [Fact]
    public void TheMaterialDecides_HowHardAHitIs_AndHowFarItFlies()
    {
        Assert.Equal(BlockWeight.Light, BlockThrow.WeightOf(0.3f));
        Assert.Equal(BlockWeight.Medium, BlockThrow.WeightOf(BlockThrow.MediumFromHardness));
        Assert.Equal(BlockWeight.Medium, BlockThrow.WeightOf(3.9f));
        Assert.Equal(BlockWeight.Heavy, BlockThrow.WeightOf(BlockThrow.HeavyFromHardness));

        Assert.True(BlockThrow.Damage(BlockWeight.Light) < BlockThrow.Damage(BlockWeight.Medium));
        Assert.True(BlockThrow.Damage(BlockWeight.Medium) < BlockThrow.Damage(BlockWeight.Heavy));
        Assert.InRange(BlockThrow.Damage(BlockWeight.Heavy), 7f, 12f); // moderate by decision: about a club blow, a touch more
        Assert.True(BlockThrow.Speed(BlockWeight.Heavy) < BlockThrow.Speed(BlockWeight.Light), "a heavy block does not fly as far");

        // Every weight class really exists in the game's blocks.
        foreach (BlockWeight w in Enum.GetValues<BlockWeight>())
        {
            Assert.False(string.IsNullOrEmpty(BlockItem(w)));
        }
    }

    [Fact]
    public void ABrokenDirection_BecomesTheLevelFacing_AndASteepOneIsCapped()
    {
        BlockThrow.Direction(0f, 0f, 0f, 90f, out float x, out float y, out float z);
        Assert.Equal(1f, x, 3);
        Assert.Equal(0f, y, 3);
        Assert.Equal(0f, z, 3);

        BlockThrow.Direction(float.NaN, 1f, 0f, 0f, out x, out y, out z);
        Assert.Equal(1f, z, 3); // yaw 0 = +Z

        BlockThrow.Direction(0f, 5f, 0f, 90f, out x, out y, out z); // straight up: capped, leaning along the facing
        Assert.Equal(Math.Sin(BlockThrow.MaxPitchDegrees * Math.PI / 180.0), y, 3);
        Assert.True(x > 0f);
        Assert.Equal(1.0, Math.Sqrt(x * x + y * y + z * z), 3);

        BlockThrow.Direction(30f, 0f, 40f, 0f, out x, out y, out z);
        Assert.Equal(0.6f, x, 3);
        Assert.Equal(0.8f, z, 3);
    }

    // ---- lifting ---------------------------------------------------------------------------------------------------

    [Fact]
    public void Lifting_IsAStance_TheBlockStaysInTheStack_AndTheOthersSeeThePose()
    {
        string item = BlockItem(BlockWeight.Medium);
        var (server, repo, p, _) = Stage("lift", item);
        using (repo)
        {
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId));
            server.CarryBlockForTest(p.State.PlayerId, true);

            Assert.True(server.CarriesBlockForTest(p.State.PlayerId));
            Assert.Equal(3, p.State.Inventory.CountOf(item)); // nothing left the stack
            Assert.Equal(0, GearLook.Mask(_ => true) & GearLook.CarryingOverhead); // a state of the server's presence, never gear

            server.CarryBlockForTest(p.State.PlayerId, false);
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId));
        }
    }

    [Fact]
    public void OnlyABlock_OnFoot_CanBeLifted_AndAnotherSlotPutsItBack()
    {
        string item = BlockItem(BlockWeight.Medium);
        var (server, repo, p, _) = Stage("gates", item);
        using (repo)
        {
            string notABlock = _content.Items.Values.First(i => string.IsNullOrEmpty(i.PlacesBlock)).Key;
            p.State.Inventory.SetSlot(1, new ItemStack(notABlock, 1));

            p.State.SelectedHotbarSlot = 1;
            server.CarryBlockForTest(p.State.PlayerId, true);
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId), "a tool or a berry is not lifted");

            p.State.SelectedHotbarSlot = 0;
            p.State.Seated = true;
            server.CarryBlockForTest(p.State.PlayerId, true);
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId), "not while seated");

            p.State.Seated = false;
            server.CarryBlockForTest(p.State.PlayerId, true);
            Assert.True(server.CarriesBlockForTest(p.State.PlayerId));

            p.State.AboardShip = true; // boarded with the block up: the pose is gone without any message
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId));
            p.State.AboardShip = false;

            server.SelectHotbarForTest(p.State.PlayerId, 1);
            server.SelectHotbarForTest(p.State.PlayerId, 0);
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId), "selecting another slot put the block back");
        }
    }

    // ---- throwing --------------------------------------------------------------------------------------------------

    [Fact]
    public void AThrow_TakesOneBlock_AndLeavesItOnTheGroundAsAnItem()
    {
        string item = BlockItem(BlockWeight.Medium);
        var (server, repo, p, padY) = Stage("land", item);
        using (repo)
        {
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0.2f, 0f);
            Assert.Equal(3, p.State.Inventory.CountOf(item)); // not lifted: nothing is thrown

            server.CarryBlockForTest(p.State.PlayerId, true);
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0.2f, 0f);

            Assert.Equal(2, p.State.Inventory.CountOf(item));
            Assert.Equal(1, Lying(server, item));
            Assert.False(server.CarriesBlockForTest(p.State.PlayerId), "the hands are empty after the throw");

            var packet = server.DropPackets.Single(c => c.Items.Any(s => s.Item == item));
            Assert.True(packet.Position.X > Cx + 3, "it flew east");
            Assert.Equal(padY + 1, packet.Position.Y); // it lies on the pad — not placed, not in the air
            Assert.NotEqual(item, _content.BlockById(server.World.GetBlock(packet.Position))?.Key); // an item, never a block in the world

            // The last block of a stack empties the slot.
            p.State.Inventory.SetSlot(0, new ItemStack(item, 1));
            server.TickForTest(1.0);
            server.CarryBlockForTest(p.State.PlayerId, true);
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0.2f, 0f);
            Assert.Equal(0, p.State.Inventory.CountOf(item));
            Assert.Equal(2, Lying(server, item));
        }
    }

    [Theory]
    [InlineData(BlockWeight.Light)]
    [InlineData(BlockWeight.Medium)]
    [InlineData(BlockWeight.Heavy)]
    public void AMachineInTheWay_TakesTheMaterialsDamage(BlockWeight weight)
    {
        string item = BlockItem(weight);
        var (server, repo, p, padY) = Stage("hit" + weight, item);
        using (repo)
        {
            server.SpawnPlanetEnemyAtForTest(new Vector3f(Cx + 5.5f, padY + 1, Cz + 0.5f), damagePerSecond: 0f);
            var machine = server.PlanetEnemies.Single();
            float before = machine.Hull;

            server.CarryBlockForTest(p.State.PlayerId, true);
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0f, 0f);

            Assert.Equal(before - BlockThrow.Damage(weight), machine.Hull, 3);
            Assert.Equal(1, Lying(server, item)); // the block is not lost on a hit either
        }
    }

    [Fact]
    public void ThrowAfterThrow_BeatsAMachine_ByTheSameRulesAsABlow()
    {
        string item = BlockItem(BlockWeight.Heavy);
        var (server, repo, p, padY) = Stage("beat", item, count: 8);
        using (repo)
        {
            server.SpawnPlanetEnemyAtForTest(new Vector3f(Cx + 5.5f, padY + 1, Cz + 0.5f), damagePerSecond: 0f);
            for (int i = 0; i < 8 && server.PlanetEnemies.Count > 0; i++)
            {
                server.CarryBlockForTest(p.State.PlayerId, true);
                server.ThrowBlockForTest(p.State.PlayerId, 1f, 0f, 0f);
                server.CarryBlockForTest(p.State.PlayerId, true);
                server.ThrowBlockForTest(p.State.PlayerId, 1f, 0f, 0f); // inside the gap: refused, the block stays up
                server.TickForTest(BlockThrow.ThrowGapSeconds + 0.1);
            }

            Assert.Empty(server.PlanetEnemies);
            Assert.Equal(3, 8 - p.State.Inventory.CountOf(item)); // 30 hull, 10 a throw: three blocks, none wasted in the gap
        }
    }

    [Fact]
    public void ATameAnimal_IsNeverHit_AWildOneIs()
    {
        string item = BlockItem(BlockWeight.Medium);
        var (server, repo, p, padY) = Stage("tame", item, count: 4);
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundi)!;
            string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 4.5f, padY + 1, Cz + 0.5f), sp.Id);
            var animal = server.Creatures.Single(c => c.Id == id);
            server.PauseCreatureForTest(id, 60f);
            float full = animal.Hull;

            animal.OwnerId = p.State.PlayerId; // tamed
            server.CarryBlockForTest(p.State.PlayerId, true);
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0f, 0f);
            Assert.Equal(full, animal.Hull); // the block passed the pet
            Assert.Equal(1, Lying(server, item));

            animal.OwnerId = string.Empty; // wild again
            animal.Position = new Vector3f(Cx + 4.5f, padY + 1, Cz + 0.5f);
            server.TickForTest(BlockThrow.ThrowGapSeconds + 0.1);
            animal.Position = new Vector3f(Cx + 4.5f, padY + 1, Cz + 0.5f);
            server.CarryBlockForTest(p.State.PlayerId, true);
            server.ThrowBlockForTest(p.State.PlayerId, 1f, 0f, 0f);
            Assert.True(animal.Hull < full || server.Creatures.All(c => c.Id != id), "a wild animal in the way is hit");
        }
    }

    [Fact]
    public void TheNewMessages_SurviveTheWire()
    {
        var carry = Assert.IsType<CarryBlockIntent>(BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(new CarryBlockIntent { Carry = true })));
        Assert.True(carry.Carry);

        var thrown = Assert.IsType<ThrowBlockIntent>(BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(new ThrowBlockIntent { DirX = 0.6f, DirY = 0.1f, DirZ = -0.8f })));
        Assert.Equal(-0.8f, thrown.DirZ);

        var fx = Assert.IsType<WorldFx>(BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(new WorldFx { Kind = SvGameServer.BlockThrowFx, Item = "stone", Strength = 0.7f })));
        Assert.Equal("stone", fx.Item);
        Assert.Null(Assert.IsType<WorldFx>(BlocksBeyondTheStars.Networking.NetCodec.Decode(BlocksBeyondTheStars.Networking.NetCodec.Encode(new WorldFx { Kind = "thump" }))).Item);
    }
}
