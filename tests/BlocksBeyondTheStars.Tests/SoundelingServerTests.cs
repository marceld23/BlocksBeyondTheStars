// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The Soundelings carry lights into the dark (#3366) on a live server: by day a Soundeling takes a lamp handed over with the
/// Feed action and puts it on a dark free cell under a roof (as the giver's block); at night it takes nothing and stays curled
/// up — nothing wakes it, it does not move; with no dark spot near it keeps its paws free and the lamp stays with the player; by
/// day in the dark it hides curled up until a light comes near; and it never carries a light into somebody else's base.
/// </summary>
public sealed class SoundelingServerTests : IDisposable
{
    private const int Cx = 300, Cz = 300;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_soundeling_" + Guid.NewGuid().ToString("N"));
    private readonly GameContent _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    public void Dispose() => SoundersStage.Delete(_root);

    /// <summary>A pad with a roof over its east part (x Cx+5..Cx+13, z Cz−4..Cz+4, three cells over the floor) — the only dark
    /// place around.</summary>
    private (SvGameServer Server, SqliteWorldRepository Repo, int PadY) Stage(string name, bool roof = true)
    {
        var server = SoundersStage.Started(_root, name, _content, out var repo);
        SoundersStage.Pacify(server);
        int padY = SoundersStage.Pad(server, _content, Cx, Cz, 16);
        if (roof)
        {
            SoundersStage.Roof(server, _content, Cx + 5, Cx + 13, Cz - 4, Cz + 4, padY + 4);
        }

        return (server, repo, padY);
    }

    private static PlayerSession Giver(SvGameServer server, int padY, string name = "Kid")
    {
        var p = SoundersStage.OnFoot(server, name, new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f));
        p.State.Inventory.SetSlot(0, new ItemStack("torch", 3));
        return p;
    }

    private static bool IsTorch(SvGameServer server, GameContent content, Vector3i cell)
        => server.World.GetBlock(cell).Value == content.GetBlock("torch")!.NumericId.Value;

    [Fact]
    public void ByDay_ItTakesTheLamp_AndPutsItOnADarkFreeCellUnderTheRoof()
    {
        var (server, repo, padY) = Stage("day");
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundeling)!;
            var p = Giver(server, padY);
            string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f), sp.Id);
            server.SetLocalDayFractionForTest(0.5, Cx);
            server.TickForTest(0.1);
            int torches = p.State.Inventory.CountOf("torch");

            server.ThrowFoodForTest(p.State.PlayerId, 0);

            Assert.Equal("torch", server.SoundelingCarryingForTest(id));
            Assert.Equal(torches - 1, p.State.Inventory.CountOf("torch"));
            var target = server.SoundelingTargetForTest(id)!.Value;
            Assert.InRange(target.X, Cx + 5, Cx + 13); // under the roof — the only dark place
            Assert.InRange(target.Z, Cz - 4, Cz + 4);
            Assert.Equal(padY + 1, target.Y);          // on the floor
            Assert.True(server.NetCreatureForTest(id).CarriesLight);

            for (int i = 0; i < 400 && server.SoundelingCarryingForTest(id).Length > 0; i++)
            {
                p.State.Position = new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f);
                server.TickForTest(0.1);
            }

            Assert.Equal(string.Empty, server.SoundelingCarryingForTest(id));
            Assert.False(server.NetCreatureForTest(id).CarriesLight);
            int placed = 0;
            for (int x = Cx + 4; x <= Cx + 14; x++)
            {
                for (int z = Cz - 5; z <= Cz + 5; z++)
                {
                    if (IsTorch(server, _content, new Vector3i(x, padY + 1, z)))
                    {
                        placed++;
                    }
                }
            }

            Assert.Equal(1, placed); // one lamp, in the dark under the roof, on a free cell
        }
    }

    [Fact]
    public void AtNight_ItTakesNothing_StaysCurledUp_AndNothingWakesIt()
    {
        var (server, repo, padY) = Stage("night");
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundeling)!;
            var p = Giver(server, padY);
            var at = new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f);
            string id = server.SpawnCreatureAtForTest(at, sp.Id);
            server.SetLocalDayFractionForTest(0.0, Cx);
            int torches = p.State.Inventory.CountOf("torch");

            for (int i = 0; i < 50; i++)
            {
                p.State.Position = new Vector3f(Cx - 0.5f, padY + 1, Cz + 0.5f); // right beside it
                server.TickForTest(0.1);
            }

            Assert.True(server.NetCreatureForTest(id).Asleep, "curled into a fluff ball at night");
            var now = server.NetCreatureForTest(id);
            Assert.InRange(Math.Abs(now.X - at.X) + Math.Abs(now.Z - at.Z), 0f, 0.05f); // it hardly moves

            server.ThrowFoodForTest(p.State.PlayerId, 0);
            Assert.Equal(string.Empty, server.SoundelingCarryingForTest(id));
            Assert.Equal(torches, p.State.Inventory.CountOf("torch")); // the lamp stays with the player
        }
    }

    [Fact]
    public void WithNoDarkSpotNear_ItKeepsItsPawsFree_AndTheLampStaysWithThePlayer()
    {
        var (server, repo, padY) = Stage("bright", roof: false);
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundeling)!;
            var p = Giver(server, padY);
            string id = server.SpawnCreatureAtForTest(new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f), sp.Id);
            server.SetLocalDayFractionForTest(0.5, Cx);
            server.TickForTest(0.1);
            int torches = p.State.Inventory.CountOf("torch");

            server.ThrowFoodForTest(p.State.PlayerId, 0);

            Assert.Equal(string.Empty, server.SoundelingCarryingForTest(id));
            Assert.Equal(torches, p.State.Inventory.CountOf("torch"));
        }
    }

    [Fact]
    public void ByDayInTheDark_ItHidesCurledUp_UntilALightComesNear()
    {
        var (server, repo, padY) = Stage("dark");
        using (repo)
        {
            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundeling)!;
            var p = SoundersStage.OnFoot(server, "Kid", new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f));
            var at = new Vector3f(Cx + 9.5f, padY + 1, Cz + 0.5f);
            string id = server.SpawnCreatureAtForTest(at, sp.Id);
            server.SetLocalDayFractionForTest(0.5, Cx);
            for (int i = 0; i < 30; i++)
            {
                p.State.Position = new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f);
                server.TickForTest(0.1);
            }

            Assert.True(server.SoundelingCurledForTest(id), "under the roof with no light near it hides");
            Assert.True(server.NetCreatureForTest(id).Asleep);
            var now = server.NetCreatureForTest(id);
            Assert.InRange(Math.Abs(now.X - at.X) + Math.Abs(now.Z - at.Z), 0f, 0.05f);

            server.World.SetBlock(new Vector3i(Cx + 9, padY + 1, Cz + 3), _content.GetBlock("torch")!.NumericId);
            for (int i = 0; i < 30; i++)
            {
                p.State.Position = new Vector3f(Cx - 1.5f, padY + 1, Cz + 0.5f);
                server.TickForTest(0.1);
            }

            Assert.False(server.SoundelingCurledForTest(id), "a light near it — it comes out of hiding");
            Assert.False(server.NetCreatureForTest(id).Asleep);
        }
    }

    [Fact]
    public void ItNeverCarriesALight_IntoSomebodyElsesBase()
    {
        var (server, repo, padY) = Stage("base");
        using (repo)
        {
            // Builder founds a base whose zone covers the whole roofed area (the zone reaches 8 cells from the core).
            var builder = SoundersStage.OnFoot(server, "Builder", new Vector3f(Cx + 7.5f, padY + 1, Cz + 0.5f));
            builder.State.Inventory.Add("base_core", 2, 16);
            server.PlaceBlock(builder.State.PlayerId, Cx + 9, padY + 1, Cz, "base_core");
            Assert.Single(server.BaseSnapshots);

            var kid = Giver(server, padY);
            var from = new Vector3f(Cx + 0.5f, padY + 1, Cz + 0.5f);
            Assert.Null(server.FindDarkCellForTest(from, kid.State.PlayerId));             // the kid may not build there …
            Assert.NotNull(server.FindDarkCellForTest(from, builder.State.PlayerId));      // … the owner may

            var sp = server.EnsureAuthoredSpeciesForTest(SoundersIds.Soundeling)!;
            string id = server.SpawnCreatureAtForTest(from, sp.Id);
            server.SetLocalDayFractionForTest(0.5, Cx);
            server.TickForTest(0.1);
            int torches = kid.State.Inventory.CountOf("torch");
            server.ThrowFoodForTest(kid.State.PlayerId, 0);
            Assert.Equal(string.Empty, server.SoundelingCarryingForTest(id));
            Assert.Equal(torches, kid.State.Inventory.CountOf("torch"));
        }
    }
}
