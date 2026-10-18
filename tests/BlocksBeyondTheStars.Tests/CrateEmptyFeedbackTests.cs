// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #2621 — G (take all) on an EMPTY storage crate used to answer nothing, so "the crate is empty" read exactly like "the
/// key is broken" (Lyxette: "neither E nor G or H does anything"). Now it says so; a full crate is still emptied into
/// the backpack without a word, and H with nothing stashable keeps its own answer.
/// </summary>
public sealed class CrateEmptyFeedbackTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public CrateEmptyFeedbackTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_crate_empty_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    private SvGameServer Started(RecordingTransport transport, out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "crate_empty"));
        var config = new ServerConfig
        {
            WorldName = "crate_empty",
            Seed = 1,
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceLodges = false, // #2640: a hut's store is a crate too — keep the test's own crate the only one
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    /// <summary>A builder up in the air (the cells around are empty) with a crate and some stone; the crate stands beside them.</summary>
    private static (BlocksBeyondTheStars.GameServer.PlayerSession Player, string CrateId) WithCrate(SvGameServer server)
    {
        var p = server.AddLocalPlayer("Builder");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(0, 200, 0);
        for (int i = 0; i < p.State.Inventory.SlotCount; i++)
        {
            p.State.Inventory.SetSlot(i, null); // no starter kit in the counts below
        }

        p.State.Inventory.Add("crate", 1, 99);
        p.State.Inventory.Add("stone", 12, 99);
        server.PlaceBlock("Builder", 1, 200, 0, "crate");
        var crate = server.Containers.Single(c => c.Kind == "crate");
        return (p, crate.Id);
    }

    private static string[] RejectionsTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who, string action)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>()
            .Where(m => m.Action == action).Select(m => m.Reason).ToArray();

    [Fact]
    public void TakingFromAnEmptyCrate_SaysTheCrateIsEmpty()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var (p, crateId) = WithCrate(server);

            server.LootContainer("Builder", crateId); // G on the fresh, empty crate

            Assert.Equal(new[] { "@srv.loot.crate_empty" }, RejectionsTo(t, p, "loot"));
            Assert.Contains(server.Containers, c => c.Id == crateId); // the crate still stands (#1153)
        }
    }

    [Fact]
    public void TakingFromAFullCrate_StillTakesEverything_WithoutARefusal()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var (p, crateId) = WithCrate(server);
            server.DepositToContainer("Builder", crateId); // H: the stone goes in
            Assert.Equal(0, p.State.Inventory.CountOf("stone"));

            server.LootContainer("Builder", crateId); // G: and comes back out

            Assert.Equal(12, p.State.Inventory.CountOf("stone"));
            Assert.Empty(RejectionsTo(t, p, "loot"));

            server.LootContainer("Builder", crateId); // G again: now it IS empty, and says so
            Assert.Equal(new[] { "@srv.loot.crate_empty" }, RejectionsTo(t, p, "loot"));
        }
    }

    [Fact]
    public void StoringWithNothingStashable_SaysSo()
    {
        var t = new RecordingTransport();
        var server = Started(t, out var repo);
        using (repo)
        {
            var (p, crateId) = WithCrate(server);
            var inv = p.State.Inventory;
            for (int i = 0; i < inv.SlotCount; i++)
            {
                inv.SetSlot(i, null);
            }

            inv.Add("machete", 1, 1); // a tool only — H never packs those away

            server.DepositToContainer("Builder", crateId);

            Assert.Equal(1, inv.CountOf("machete"));

            Assert.Equal(new[] { "@srv.loot.nothing_to_stash" }, RejectionsTo(t, p, "stash"));
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void TheEmptyCrateAnswer_IsWrittenInBothMandatoryLanguages(string code)
    {
        var table = TestLocales.Load(code);
        Assert.True(table.TryGetValue("srv.loot.crate_empty", out var text) && !string.IsNullOrWhiteSpace(text));
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort — a temp folder left behind is harmless
        }
    }
}
