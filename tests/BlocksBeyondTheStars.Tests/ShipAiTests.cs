// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Ship AI companion "VEGA": the onboarding stage chain (server-authoritative, per-player, persisted via
/// <see cref="PlayerState.Milestones"/>), the veteran auto-skip, the explicit skip intent, and the
/// memory-fragment story arc (redeemed aboard, knowledge reward, Mk3 blueprint at the arc's end).
/// </summary>
public sealed class ShipAiTests : IDisposable
{
    private readonly string _root;
    private readonly GameContent _content;

    public ShipAiTests()
    {
        _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bbts_vega_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        try { System.IO.Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private ServerConfig Config() => new()
    {
        WorldName = "vega",
        Seed = 123456,
        StartPlanet = "rocky",
        AutoSaveIntervalMinutes = 9999,
        ViewDistanceChunks = 1,
        MaxPlayers = 4,
        PlaceStarterShip = false, // bare terrain at the spawn column (mining test digs straight down)
    };

    private static LoopbackLink NewLink(out LoopbackLink link)
    {
        link = new LoopbackLink();
        return link;
    }

    private static void JoinAndDrain(SvGameServer server, LoopbackClientTransport client, string name)
    {
        client.Connect("loopback", 0);
        client.Send(NetCodec.Encode(new JoinRequest { PlayerName = name }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
    }

    /// <summary>Collects every VEGA line the client receives (handler must be attached before joining).</summary>
    private static List<ShipAiLine> CaptureVega(LoopbackClientTransport client)
    {
        var lines = new List<ShipAiLine>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is ShipAiLine l)
            {
                lines.Add(l);
            }
        };
        return lines;
    }

    [Fact]
    public void NewPlayer_BootsVega_AndMineStageAdvancesToCraft()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Rookie");

        // The intro + first objective arrived, and the chain starts at the mining stage.
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.Contains(lines, l => l.LineKey == "vega.intro.menu" && l.Kind == 0);
        Assert.Contains(lines, l => l.LineKey == "vega.intro.codex" && l.Kind == 0);
        var introKeys = lines.Select(l => l.LineKey).ToList();
        Assert.True(introKeys.IndexOf("vega.intro.2") < introKeys.IndexOf("vega.intro.menu")
            && introKeys.IndexOf("vega.intro.menu") < introKeys.IndexOf("vega.intro.codex")
            && introKeys.IndexOf("vega.intro.codex") < introKeys.IndexOf("vega.s.mine.start"),
            "menu + Codex are introduced after the greeting and before the first task (#1015)");
        Assert.Contains(lines, l => l.LineKey == "vega.s.mine.start");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey);
        Assert.Contains("vega:intro", server.MilestonesForTest("Rookie"));

        // Mine three nearby blocks (own + neighbour columns, a few cells below the head, all within reach —
        // the spawn column may sit over a cave, so straight-down alone can run out of reachable rock).
        var session = server.Sessions[1];
        int px = (int)Math.Floor(session.State.Position.X);
        int pz = (int)Math.Floor(session.State.Position.Z);
        int topY = (int)Math.Ceiling(session.State.Position.Y);
        int mined = 0;
        for (int dx = -2; dx <= 2 && mined < 3; dx++)
            for (int dz = -2; dz <= 2 && mined < 3; dz++)
                for (int y = topY; y > topY - 7 && mined < 3; y--)
                {
                    var pos = new Vector3i(px + dx, y, pz + dz);
                    if (server.World.GetBlock(pos).IsAir)
                    {
                        continue;
                    }

                    for (int hit = 0; hit < 12 && !server.World.GetBlock(pos).IsAir; hit++)
                    {
                        client.Send(NetCodec.Encode(new MineBlockIntent { X = pos.X, Y = pos.Y, Z = pos.Z }),
                            DeliveryMode.ReliableOrdered);
                        server.Tick(0.1);
                    }

                    if (server.World.GetBlock(pos).IsAir)
                    {
                        mined++;
                    }
                }

        Assert.Equal(3, mined);
        client.Poll();

        Assert.Contains("vega:stage:mine", server.MilestonesForTest("Rookie"));
        Assert.Contains(lines, l => l.LineKey == "vega.s.mine.done");
        Assert.Contains(lines, l => l.LineKey == "vega.s.craft.start");
        Assert.Equal("vega.obj.craft", lines.Last().ObjectiveKey);
    }

    [Fact]
    public void NewPlayer_GetsThePrologue_BeforeVegasIntro_WhenAStoryIsActive()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo); // default pack active
        server.Start();
        JoinAndDrain(server, client, "Dreamer");

        // All three prologue pages arrive as Kind 4 (full-screen overlay), strictly before the intro.
        var keys = lines.Where(l => !string.IsNullOrEmpty(l.LineKey)).Select(l => l.LineKey).ToList();
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.1" && l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.2" && l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.prologue.3" && l.Kind == 4);
        Assert.True(keys.IndexOf("vega.prologue.3") < keys.IndexOf("vega.intro.1"),
            "the prologue frames the premise BEFORE VEGA's first spoken line");
    }

    [Fact]
    public void SandboxWithoutStory_SkipsThePrologue_ButStillRunsTheTutorial()
    {
        var config = Config();
        config.Rules.StoryId = "none"; // the world opted out of narrative

        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(config, _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Sandboxer");

        Assert.DoesNotContain(lines, l => l.Kind == 4);
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1"); // the tutorial itself is unaffected
    }

    [Fact]
    public void Join_SendsTheVegaJournalSnapshot_AndSkipRefreshesIt()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var journals = new List<VegaJournal>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is VegaJournal j)
            {
                journals.Add(j);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Reader");

        // The join snapshot carries the freshly-burned intro milestone — the client's tips log source.
        Assert.NotEmpty(journals);
        Assert.Contains("vega:intro", journals.Last().Milestones);
        Assert.DoesNotContain(journals.Last().Milestones, m => !m.StartsWith("vega:", StringComparison.Ordinal));

        // Skipping grants the whole chain — and re-sends the snapshot so the log is complete immediately.
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Contains("vega:stage:mine", journals.Last().Milestones);
        Assert.Contains("vega:stage:land", journals.Last().Milestones);
    }

    [Fact]
    public void NewPlayer_StartsWithFood_AndEatingAdvancesTheEatStage_ToScan()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Eater");

        // Startvorrat (option A): a fresh pilot can eat from minute one — berries to hand, rations pre-loaded
        // into the suit dispenser so the low-hunger auto-feed safety net works before any are crafted.
        var p = server.Sessions[1].State;
        Assert.True(p.Inventory.CountOf("berries") > 0, "new players start with berries to eat");
        Assert.True(p.RationStore.CountOf("emergency_ration") > 0, "suit dispenser is pre-loaded with rations");

        // Fast-forward the chain to the new "eat" lesson (after mine + craft), then eat one berry.
        p.Milestones.Add("vega:stage:mine");
        p.Milestones.Add("vega:stage:craft");
        client.Send(NetCodec.Encode(new ConsumeItemIntent { ItemKey = "berries" }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Contains("vega:stage:eat", server.MilestonesForTest("Eater"));
        Assert.Contains(lines, l => l.LineKey == "vega.s.eat.done");
        Assert.Contains(lines, l => l.LineKey == "vega.s.scan.start"); // the chain moves on to scanning
        Assert.Equal("vega.obj.scan", lines.Last().ObjectiveKey);
    }

    [Fact]
    public void VeteranSave_AutoSkipsOnboarding_WithOneLine()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        repo.Initialize();

        // A save that has clearly played before: knowledge already earned.
        repo.SavePlayer(new PlayerState { PlayerId = "Vet", Name = "Vet", KnowledgePoints = 12 });

        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Vet");

        var milestones = server.MilestonesForTest("Vet");
        Assert.Contains("vega:stage:mine", milestones);
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.veteran");
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.intro.1");
        Assert.StartsWith("story.obj.", lines.Last().ObjectiveKey); // post-tutorial the chip shows the story objective (#1110)
    }

    [Fact]
    public void SkipOnboardingIntent_GrantsTheWholeChain()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Skipper");

        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var milestones = server.MilestonesForTest("Skipper");
        Assert.Contains("vega:stage:mine", milestones);
        Assert.Contains("vega:stage:trade", milestones);
        Assert.Contains("vega:stage:land", milestones);
        Assert.Contains(lines, l => l.LineKey == "vega.skip");
        Assert.StartsWith("story.obj.", lines.Last().ObjectiveKey); // post-tutorial the chip shows the story objective (#1110)
    }

    [Fact]
    public void Restart_AfterSkip_RunsTheTutorialAgain()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Returner");

        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.Contains("vega:stage:land", server.MilestonesForTest("Returner"));

        // The way back: restart wipes the stage chain and re-runs the intro + first objective.
        client.Send(NetCodec.Encode(new SkipOnboardingIntent { Restart = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        var milestones = server.MilestonesForTest("Returner");
        Assert.DoesNotContain("vega:stage:mine", milestones);
        Assert.DoesNotContain("vega:stage:land", milestones);
        Assert.Contains("vega:intro", milestones); // re-armed by the fresh boot
        Assert.Contains(lines, l => l.LineKey == "vega.intro.1");
        Assert.Equal("vega.obj.mine", lines.Last().ObjectiveKey); // back at lesson one
    }

    [Fact]
    public void MemoryFragments_RedeemAboard_PacedWithKnowledgeReward()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Archivist");

        var p = server.Sessions[1].State;
        Assert.True(p.AboardShip, "players start aboard the ship");
        int knowledgeBefore = p.KnowledgePoints;
        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 2));

        // The advisor poll runs at 1 Hz and redemption is paced (~6 s apart): tick well past both.
        for (int i = 0; i < 10; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        var milestones = server.MilestonesForTest("Archivist");
        Assert.Contains("vega:mem:1", milestones);
        Assert.Contains("vega:mem:2", milestones);
        Assert.Equal(0, p.Inventory.CountOf("ai_memory_fragment"));
        Assert.Equal(knowledgeBefore + 6, p.KnowledgePoints); // +3 per restored fragment
        Assert.Contains(lines, l => l.LineKey == "vega.mem.1" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.mem.2" && l.Kind == 2);
    }

    /// <summary>#1104: the arc's finale hands over the Mk3 core's research MATERIALS, not the blueprint — the
    /// 200-KP threshold and the cockpit still gate the actual research.</summary>
    [Fact]
    public void TenthFragment_CompletesTheArc_AndHandsOverTheMk3Parts()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Historian");

        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 9; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat); // nine beats already restored on earlier worlds
        }

        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();

        Assert.Contains("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.DoesNotContain("ai_core_mk3", p.UnlockedBlueprints); // still has to be researched
        foreach (var part in _content.GetBlueprint("ai_core_mk3")!.UnlockCost)
        {
            Assert.True(p.Inventory.CountOf(part.Item) + server.Ship.Cargo.CountOf(part.Item) >= part.Count, part.Item);
        }

        Assert.Contains(lines, l => l.LineKey == "vega.mem.10" && l.Kind == 2);
        Assert.Contains(lines, l => l.LineKey == "vega.sys.mk3bp");
    }

    /// <summary>With no room for the Mk3 parts the last fragment waits (nothing consumed, one warning line);
    /// making room lets VEGA finish reading it.</summary>
    [Fact]
    public void TenthFragment_WaitsWhileTheHoldIsFull_ThenPaysOut()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Historian");

        var p = server.Sessions[1].State;
        for (int beat = 1; beat <= 9; beat++)
        {
            p.Milestones.Add("vega:mem:" + beat);
        }

        // Every backpack slot but the fragment's holds a full stack of an unrelated material, and the hold too.
        int max = _content.MaxStackOf("stone");
        for (int i = 0; i < p.Inventory.SlotCount; i++)
        {
            p.Inventory.SetSlot(i, new ItemStack("stone", max));
        }

        for (int i = 0; i < server.Ship.Cargo.SlotCount; i++)
        {
            server.Ship.Cargo.SetSlot(i, new ItemStack("stone", max));
        }

        p.Inventory.SetSlot(10, new ItemStack("ai_memory_fragment", 1));
        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();
        Assert.DoesNotContain("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.Equal(1, p.Inventory.CountOf("ai_memory_fragment"));                 // not consumed
        Assert.Single(lines.Where(l => l.LineKey == "vega.sys.mk3parts_full"));   // said once, not per tick

        // Make room → the fragment is read and the parts arrive.
        for (int i = 0; i < server.Ship.Cargo.SlotCount; i++)
        {
            server.Ship.Cargo.SetSlot(i, null);
        }

        for (int i = 0; i < 4; i++)
        {
            server.Tick(1.1);
        }

        client.Poll();
        Assert.Contains("vega:mem:10", server.MilestonesForTest("Historian"));
        Assert.Contains(lines, l => l.LineKey == "vega.sys.mk3bp");
    }

    [Fact]
    public void AiCoreTier_FollowsTheBuiltModules_InPlayerState()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var states = new List<PlayerStateUpdate>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is PlayerStateUpdate u)
            {
                states.Add(u);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Engineer");

        Assert.Equal(1, states.Last().AiCoreTier); // bare VEGA

        var session = server.Sessions[1];
        session.Ships[session.ActiveShipId].Modules.Add("ai_core_mk2");
        // Trigger a fresh authoritative state send (stealth toggle succeeds with a suit carried).
        session.State.Inventory.SetSlot(11, new ItemStack("stealth_suit", 1));
        client.Send(NetCodec.Encode(new ToggleStealthIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();

        Assert.Equal(2, states.Last().AiCoreTier);
    }

    [Fact]
    public void Milestones_PersistAcrossReload()
    {
        using (var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        using (var serverTransport = new LoopbackServerTransport(NewLink(out var link)))
        using (var client = new LoopbackClientTransport(link))
        {
            var server = new SvGameServer(Config(), _content, serverTransport, repo);
            server.Start();
            JoinAndDrain(server, client, "Saver");
            client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
            server.Tick(0.1);
            server.Stop();
        }

        SqliteWorldRepositoryReset();

        using (var repo2 = new SqliteWorldRepository(new SaveGamePaths(_root, "vega")))
        {
            repo2.Initialize();
            var loaded = repo2.LoadPlayer("Saver");
            Assert.NotNull(loaded);
            Assert.Contains("vega:intro", loaded!.Milestones);
            Assert.Contains("vega:stage:land", loaded.Milestones);
        }
    }

    // --- Context tips (#1077): throttled, repeatable situational advice ---

    /// <summary>Ticks the server one simulated second at a time (the advisor poll is 1 Hz), sending a
    /// harmless keep-alive every half minute so the silent-session sweep (90 s) never drops the player.</summary>
    private static void TickSeconds(SvGameServer server, LoopbackClientTransport client, int seconds, Action? eachSecond = null)
    {
        for (int i = 0; i < seconds; i++)
        {
            if (i % 30 == 0)
            {
                client.Send(NetCodec.Encode(new SelectHotbarIntent { Slot = 0 }), DeliveryMode.ReliableOrdered);
            }

            eachSecond?.Invoke();
            server.Tick(1.0);
        }

        client.Poll();
    }

    [Fact]
    public void LampOffTip_WaitsForDwellAndCadence_RepeatsAfterCooldown_AndRetiresOnceLearned()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Nightwalker");

        var session = server.Sessions[1];
        var p = session.State;
        p.AboardShip = false; // on foot on the surface (no starter ship in this config)
        p.Inventory.Add("suit_lamp", 1, 1);
        server.ResetVegaProbeForTest("Nightwalker");
        // A rocky night is cold and a long night makes hungry — retire the other safety hints up front so
        // only the deliberately triggered O2 hint competes with the lamp tip for the cadence.
        foreach (var other in new[] { "cold", "heat", "hunger", "energy", "medkit" })
        {
            p.Milestones.Add("vega:hint:" + other + "#done");
        }

        lines.Clear();

        // Ticks with the world clock pinned to deep night (a full day is only 600 s here).
        float oxygenPin = 100f;
        void Night(int seconds) => TickSeconds(server, client, seconds, () => { server.SetDayFractionForTest(0.95); p.Oxygen = oxygenPin; p.Hunger = 100f; });

        // The join-quiet minute: nothing yet, however long the lamp stays off.
        Night(30);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.hint.lamp_off");

        // A first SAFETY hint ignores the cadence entirely (the old teaching moment) …
        oxygenPin = 10f;
        Night(2);
        Assert.Contains(lines, l => l.LineKey == "vega.hint.o2" && l.Kind == 1);
        Assert.Contains("vega:hint:o2", server.MilestonesForTest("Nightwalker"));
        int afterO2 = lines.Count;
        oxygenPin = 100f;

        // … and it starts the 120 s cadence: the lamp tip (dwell 8 s long satisfied) has to wait for it.
        Night(100);
        var diag = server.VegaTipCandidatesForTest("Nightwalker");
        Assert.True(diag.Candidates.Contains("lamp_off"),
            $"lamp_off should be a candidate at night with a lamp carried and off — candidates: [{string.Join(",", diag.Candidates)}], solidAbove={diag.SolidAbove}, lightNear={diag.LightNear}, aboard={diag.Aboard}");
        Assert.DoesNotContain(lines.Skip(afterO2), l => l.LineKey == "vega.hint.lamp_off");
        Night(25);
        var first = Assert.Single(lines, l => l.LineKey == "vega.hint.lamp_off");
        Assert.Equal(1, first.Kind); // first occurrence = advisor kind (goes to the tips log)
        Assert.Contains("vega:hint:lamp_off", server.MilestonesForTest("Nightwalker"));

        // Still dark, lamp still off: the per-tip cooldown (10 min) keeps VEGA quiet …
        Night(60);
        Assert.Single(lines, l => l.LineKey == "vega.hint.lamp_off");

        // … and once it is over the tip repeats (dwell again) as a Kind-5 context repeat with a #2 marker.
        server.SkipVegaTipCooldownsForTest("Nightwalker");
        Night(12);
        var st = server.VegaTipStateForTest("Nightwalker", "lamp_off");
        Assert.True(lines.Count(l => l.LineKey == "vega.hint.lamp_off") == 2,
            $"expected the repeat — count={st.Count} cooldownUntil={st.CooldownUntil} readyAt={st.ReadyAt} since={st.Since} uptime={st.Uptime}; lines=[{string.Join(",", lines.Select(l => l.LineKey + ":" + l.Kind))}]");
        Assert.Equal(5, lines.Last(l => l.LineKey == "vega.hint.lamp_off").Kind);
        Assert.Contains("vega:hint:lamp_off#2", server.MilestonesForTest("Nightwalker"));

        // The player switches the lamp on right after the tip: learned → retired for this save.
        client.Send(NetCodec.Encode(new SetLampIntent { On = true }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        Assert.True(server.LampOnForTest("Nightwalker"));
        Assert.Contains("vega:hint:lamp_off#done", server.MilestonesForTest("Nightwalker"));

        // Lamp off again, cooldown over, still night — VEGA never brings it up again.
        client.Send(NetCodec.Encode(new SetLampIntent { On = false }), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.SkipVegaTipCooldownsForTest("Nightwalker");
        Night(15);
        Assert.Equal(2, lines.Count(l => l.LineKey == "vega.hint.lamp_off"));
    }

    [Fact]
    public void VitalsHint_RepeatsWithCooldown_UpToTheCap_AndTheSnapshotCarriesTheMarkers()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var journals = new List<VegaJournal>();
        client.PayloadReceived += payload =>
        {
            if (NetCodec.Decode(payload) is VegaJournal j)
            {
                journals.Add(j);
            }
        };
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Gasper");
        var p = server.Sessions[1].State;
        p.AboardShip = false;

        int Fired() => lines.Count(l => l.LineKey == "vega.hint.energy");
        void Starve(int seconds)
        {
            for (int i = 0; i < seconds; i++)
            {
                p.SuitEnergy = 5f;
                server.Tick(1.0);
            }

            client.Poll();
        }

        Starve(2);
        Assert.Equal(1, Fired());
        Starve(5);
        Assert.Equal(1, Fired()); // inside the 15-min cooldown: silent

        for (int repeat = 2; repeat <= 4; repeat++)
        {
            server.SkipVegaTipCooldownsForTest("Gasper");
            Starve(2);
            Assert.Equal(repeat, Fired());
            Assert.Equal(5, lines.Last(l => l.LineKey == "vega.hint.energy").Kind);
            Assert.Contains("vega:hint:energy#" + repeat, server.MilestonesForTest("Gasper"));
        }

        server.SkipVegaTipCooldownsForTest("Gasper");
        Starve(3);
        Assert.Equal(4, Fired()); // the cap: never a fifth time

        // The join snapshot carries the repeat markers verbatim (the client-side journal filters them —
        // covered by VegaTextTests.JournalKeys_IgnoresContextTipRepeatMarkers).
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        client.Poll();
        var snapshot = journals.Last().Milestones;
        Assert.Contains("vega:hint:energy", snapshot);
        Assert.Contains("vega:hint:energy#2", snapshot);
    }

    [Fact]
    public void ContextTips_ProbeFindsRareOre_AndOpportunityTipsWaitForTheScanStage()
    {
        using var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "vega"));
        using var serverTransport = new LoopbackServerTransport(NewLink(out var link));
        using var client = new LoopbackClientTransport(link);
        var lines = CaptureVega(client);
        var server = new SvGameServer(Config(), _content, serverTransport, repo);
        server.Start();
        JoinAndDrain(server, client, "Prospector");
        var session = server.Sessions[1];
        var p = session.State;
        p.AboardShip = false;
        server.SetDayFractionForTest(0.5); // daylight — no lamp talk

        // Plant an exposed vein of the rarest ore of the starter world two blocks in front of the player.
        int px = (int)Math.Floor(p.Position.X), py = (int)Math.Floor(p.Position.Y), pz = (int)Math.Floor(p.Position.Z);
        var gold = _content.GetBlock("gold_ore")!;
        server.World.SetBlock(new Vector3i(px + 2, py, pz), gold.NumericId);
        server.World.SetBlock(new Vector3i(px + 2, py, pz + 1), gold.NumericId);
        server.ResetVegaProbeForTest("Prospector");

        // Fresh save (onboarding stage 0): opportunity tips stay quiet even past the join-quiet minute.
        TickSeconds(server, client, 75);
        Assert.DoesNotContain(lines, l => l.LineKey == "vega.hint.rare_ore_near");

        // Grant the tutorial (all stages) → the same situation now produces the tip, naming the ore.
        client.Send(NetCodec.Encode(new SkipOnboardingIntent()), DeliveryMode.ReliableOrdered);
        server.Tick(0.1);
        server.SkipVegaTipCooldownsForTest("Prospector");
        server.ResetVegaProbeForTest("Prospector");
        TickSeconds(server, client, 15);
        var tip = Assert.Single(lines, l => l.LineKey == "vega.hint.rare_ore_near");
        Assert.False(string.IsNullOrEmpty(tip.LineArg));
        Assert.Contains("vega:hint:rare_ore_near", server.MilestonesForTest("Prospector"));

        // Mining an ore right after the tip counts as learned.
        server.VegaTipLearnedForTest("Prospector", "rare_ore_near");
        Assert.Contains("vega:hint:rare_ore_near#done", server.MilestonesForTest("Prospector"));
    }

    private static void SqliteWorldRepositoryReset()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
