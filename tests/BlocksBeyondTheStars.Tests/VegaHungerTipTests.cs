// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// VEGA's hunger tip (#2560): Screelit starved with two pieces of creature meat in his quick-bar while VEGA told him where
/// food grows. With something edible in the pockets the tip names it and the key that eats it; an empty pocket — or
/// only toxic food — keeps the old line. Same tip, same pacing: only the line differs.
/// </summary>
public sealed class VegaHungerTipTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public VegaHungerTipTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_vegahunger_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Joins a German-speaking player over the loopback, skips the basic training, empties the pockets (the
    /// starter kit carries berries) and fills them with <paramref name="pocket"/>, then lets them go hungry on foot for a
    /// few advisor seconds. Returns every VEGA line the client received.</summary>
    private List<ShipAiLine> HungryWith(string world, params string[] pocket)
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, world));
        var link = new LoopbackLink();
        using var st = new LoopbackServerTransport(link);
        using var client = new LoopbackClientTransport(link);
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };

        var config = new ServerConfig
        {
            WorldName = world,
            Seed = 123456,
            StartPlanet = "rocky",
            AutoSaveIntervalMinutes = 9999,
            ViewDistanceChunks = 1,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, st, repo);
        server.Start();

        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest
        {
            ContentFingerprint = TestJoin.Fingerprint,
            PlayerName = "Screelit",
            Locale = "de",
            Features = new[] { ClientFeatures.Packs1 },
        }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var p = server.Sessions.Values.Single(s => s.State.Name == "Screelit").State;
        p.AboardShip = false;
        for (int i = 0; i < p.Inventory.SlotCount; i++)
        {
            p.Inventory.SetSlot(i, null);
        }

        foreach (string item in pocket)
        {
            p.Inventory.Add(item, 2, 20);
        }

        lines.Clear();
        for (int i = 0; i < 3; i++)
        {
            p.Hunger = 10f; // the stomach stays empty, nothing else goes wrong
            p.Health = 100f;
            p.Oxygen = 100f;
            server.Tick(1.0);
        }

        client.Poll();
        return lines;
    }

    [Fact]
    public void HungryWithMeatInThePocket_VegaNamesTheMeatAndTheKeyThatEatsIt()
    {
        var lines = HungryWith("vh_meat", "creature_meat");

        var line = Assert.Single(lines, l => l.LineKey.StartsWith("vega.hint.hunger", StringComparison.Ordinal));
        Assert.Equal("vega.hint.hunger_food", line.LineKey);
        Assert.Equal(TestLocales.Load("de")["item.creature_meat.name"], line.LineArg); // in the player's own language
    }

    [Fact]
    public void HungryWithAnEmptyPocket_OrOnlyToxicFood_VegaKeepsTheOldLine()
    {
        var empty = HungryWith("vh_empty");
        var line = Assert.Single(empty, l => l.LineKey.StartsWith("vega.hint.hunger", StringComparison.Ordinal));
        Assert.Equal("vega.hint.hunger", line.LineKey);
        Assert.Equal(string.Empty, line.LineArg);

        // Toxic berries fill a little too — but VEGA never tells a starving child to eat them.
        var toxic = HungryWith("vh_toxic", "toxic_berries");
        Assert.Equal("vega.hint.hunger", Assert.Single(toxic, l => l.LineKey.StartsWith("vega.hint.hunger", StringComparison.Ordinal)).LineKey);
    }

    [Fact]
    public void TheFoodLine_ExistsInBothLanguages_AndNamesTheEatKey()
    {
        foreach (string code in new[] { "en", "de" })
        {
            string text = TestLocales.Load(code)["vega.hint.hunger_food"];
            Assert.Contains("{0}", text);
            Assert.Contains("{key:Place}", text); // the held consumable is eaten with the place action (right-click)
        }
    }
}
