// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #2525: the server saves the selected quick-bar slot and restores it on join, but a restarted client always shows
/// slot 0. Mining carries no tool, so the server swung with whatever its saved slot held: a tester who quit while
/// building saw her drill in hand and had plain stone refused. The client now re-sends the slot it shows whenever it
/// enters a world, so both sides agree on the hand.
/// </summary>
[Trait("Suite", "ClientCore")]
public sealed class HotbarSlotSyncTests : IDisposable
{
    private const string Palette = "palette-a";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_hotbar_" + Guid.NewGuid().ToString("N"));

    private static GameContent LoadContent() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir());

    private static SelectHotbarIntent LastSelect(RecordingClientTransport transport)
        => transport.Sent.OfType<SelectHotbarIntent>().Last();

    [Fact]
    public void TheShownSlot_IsResent_OnEveryJoin_Reconnect_AndWorldChange()
    {
        var transport = new RecordingClientTransport();
        using var client = new NetworkClient(transport) { ContentFingerprint = Palette };
        Assert.Equal(0, client.SelectedHotbarSlot); // a fresh client shows slot 0, like the HUD

        client.Connect("server", 0);
        client.Join("Pilot");
        Assert.Empty(transport.Sent.OfType<SelectHotbarIntent>()); // nothing before the server has the player
        transport.Deliver(new JoinAccepted { PlayerId = "p1", ContentFingerprint = Palette, WorldId = 1 });
        client.Poll();

        var onJoin = Assert.Single(transport.Sent.OfType<SelectHotbarIntent>());
        Assert.Equal(0, onJoin.Slot);
        Assert.True(onJoin.Resync, "the join resync is marked, so the server counts it as no pick");

        client.SendSelectHotbar(3);
        Assert.Equal(3, client.SelectedHotbarSlot);
        Assert.False(LastSelect(transport).Resync); // the player's own pick is a plain selection

        transport.Deliver(new WorldReset { WorldId = 2 });
        client.Poll();
        Assert.Equal(3, LastSelect(transport).Slot);
        Assert.True(LastSelect(transport).Resync);

        // A reconnect keeps the slot on screen, and the new join is told it again.
        transport.Disconnect();
        client.Connect("server", 0);
        client.Join("Pilot");
        transport.Deliver(new JoinAccepted { PlayerId = "p1", ContentFingerprint = Palette, WorldId = 3 });
        client.Poll();
        Assert.Equal(3, client.SelectedHotbarSlot);
        Assert.Equal(3, LastSelect(transport).Slot);
        Assert.True(LastSelect(transport).Resync);
    }

    [Fact]
    public void ARestartedClient_MinesStone_WithTheDrillItShows_NotTheBlockTheServerSaved()
    {
        var content = LoadContent();
        byte[] blob;
        string playerId;

        // Session 1: the player builds — a stone block in quick-bar slot 4 is selected — and quits.
        var repoA = new MemoryWorldRepository(new SaveGamePaths(_root, "hotbar_a"));
        using (var h = new ClientServerHarness(content, repository: repoA))
        {
            h.Join("Builder");
            Assert.NotNull(h.JoinAccepted);
            var player = h.Server.Sessions[1].State;
            playerId = player.PlayerId;
            Assert.Equal("basic_drill", player.Inventory.Slots[0]?.Item); // the starter drill in slot 0
            player.Inventory.SetSlot(4, new ItemStack("stone", 10));

            h.Client.SendSelectHotbar(4);
            Assert.True(h.PumpUntil(() => player.SelectedHotbarSlot == 4, maxTicks: 10), "the server should adopt slot 4");

            h.Server.Stop(); // no run loop → saves synchronously into the memory repo
            Assert.Equal(4, repoA.LoadPlayer(playerId)!.SelectedHotbarSlot); // the slot the server will restore
            blob = repoA.ExportSnapshotBlob();
        }

        // Session 2: the game restarts — a fresh server on the save, a fresh client that shows slot 0 (the drill).
        var repoB = new MemoryWorldRepository(new SaveGamePaths(_root, "hotbar_b"));
        repoB.ImportSnapshotBlob(blob);
        using (var h2 = new ClientServerHarness(content, repository: repoB))
        {
            h2.Join("Builder");
            Assert.NotNull(h2.JoinAccepted);
            Assert.Equal(0, h2.Client.SelectedHotbarSlot);

            var player = h2.Server.Sessions[1].State;
            Assert.Equal(playerId, player.PlayerId);
            Assert.Equal("stone", player.Inventory.Slots[4]?.Item); // the save came back
            Assert.True(h2.PumpUntil(() => player.SelectedHotbarSlot == 0, maxTicks: 10),
                "the client's resync should move the server's hand back to the slot the HUD shows");

            // Plain stone (drill, tier 1) right under the player.
            var target = new Vector3i((int)Math.Floor(player.Position.X), (int)Math.Floor(player.Position.Y) - 1, (int)Math.Floor(player.Position.Z));
            h2.Server.World.SetBlock(target, content.GetBlock("stone")!.NumericId);

            for (int hit = 0; hit < 40 && !h2.Server.World.GetBlock(target).IsAir; hit++)
            {
                h2.Client.SendMine(target.X, target.Y, target.Z);
                h2.Tick(0.1);
            }

            Assert.DoesNotContain(h2.Rejections, r => r.Action == "mine");
            Assert.True(h2.Server.World.GetBlock(target).IsAir, "The starter drill on screen must break plain stone.");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // ignore Windows file-lock cleanup races
        }
    }
}
