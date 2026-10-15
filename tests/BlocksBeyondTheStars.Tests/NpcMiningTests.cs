// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// The miner's work core (#2643): an NPC breaks a cell through the machine path without ever marking it as a player edit,
/// banks the drop, and the cell fills up again after a while — only while nobody stands near, never over a player's own
/// build, and across a restart (the refill registry rides in the world metadata). The prospector's search finds open
/// surface rock and never a player-edited or protected cell.
/// <para>The arena is a stone pad stamped WITHOUT an owner (as worldgen and the structure stamps write), floating above
/// every natural feature, with the player standing on it and a plain NPC as the miner.</para>
/// </summary>
public sealed class NpcMiningTests : IDisposable
{
    private const int Cx = 40, Cz = 40, PadHalf = 6;

    private readonly string _root;
    private readonly GameContent _content;

    public NpcMiningTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_npcmine_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>One server on the test's save. <paramref name="padY"/> null = a fresh world: the pad is stamped; a number
    /// = the same save started again, the pad already there.</summary>
    private sealed class MineWorld : IDisposable
    {
        public readonly SvGameServer Server;
        public readonly SqliteWorldRepository Repo;
        public readonly NpcLifeWorld.RecordingTransport Transport = new();
        public readonly PlayerSession Player;
        public readonly int PadY;
        public readonly int Miner;

        public MineWorld(GameContent content, string root, int? padY = null, Action<SvGameServer, int>? beforePlayer = null)
        {
            Repo = new SqliteWorldRepository(new SaveGamePaths(root, "npcmine"));
            var config = new ServerConfig
            {
                WorldName = "npcmine",
                Seed = 4242,
                StartPlanet = "rocky",
                AutoSaveIntervalMinutes = 9999,
                PlaceStarterShip = false,
                PlaceSettlements = false,
                PlaceWrecks = false,
                PlaceBanditCamps = false,
            };
            Server = new SvGameServer(config, content, Transport, Repo);
            Server.Start();

            if (padY is { } y)
            {
                PadY = y;
            }
            else
            {
                PadY = MaxTopY() + 8;
                var stone = content.GetBlock("stone")!.NumericId;
                for (int dx = -PadHalf; dx <= PadHalf; dx++)
                    for (int dz = -PadHalf; dz <= PadHalf; dz++)
                    {
                        Server.World.SetBlock(new Vector3i(Cx + dx, PadY, Cz + dz), stone); // no owner: natural ground
                    }
            }

            // The chunks around the pad, as a player's streaming area keeps them.
            for (int dx = -PadHalf - 2; dx <= PadHalf + 2; dx += 2)
                for (int dz = -PadHalf - 2; dz <= PadHalf + 2; dz += 2)
                    for (int dy = -2; dy <= 2; dy++)
                    {
                        Server.World.GetBlock(new Vector3i(Cx + dx, PadY + dy, Cz + dz));
                    }

            beforePlayer?.Invoke(Server, PadY);
            Player = Server.AddLocalPlayer("Miner");
            Player.State.AboardShip = false;
            Player.State.Position = new Vector3f(Cx + 0.5f, PadY + 1, Cz + 0.5f);
            Miner = Server.AddNpcForTest(new Vector3f(Cx + 0.5f, PadY + 1, Cz + 2.5f));
        }

        public string Loc => Server.World.LocationId;

        public bool PlayerEdited(Vector3i cell) => Repo.HasPlayerBlockEdits(Loc, cell, cell);

        public void PlayerAt(float x, float z) => Player.State.Position = new Vector3f(x, PadY + 1, z);

        /// <summary>The player far off the pad (beyond the refill's 12 blocks).</summary>
        public void PlayerAway() => PlayerAt(Cx + 30.5f, Cz + 30.5f);

        public IEnumerable<BlockChanged> BlockChangesAt(Vector3i cell)
            => Transport.Sent.Select(s => s.Msg).OfType<BlockChanged>().Where(b => b.X == cell.X && b.Y == cell.Y && b.Z == cell.Z);

        private int MaxTopY()
        {
            int top = 0;
            for (int dx = -PadHalf; dx <= PadHalf; dx += PadHalf)
                for (int dz = -PadHalf; dz <= PadHalf; dz += PadHalf)
                {
                    for (int y = 200; y > -200; y--)
                    {
                        if (!Server.World.GetBlock(new Vector3i(Cx + dx, y, Cz + dz)).IsAir)
                        {
                            top = Math.Max(top, y);
                            break;
                        }
                    }
                }

            return top;
        }

        public void Dispose()
        {
            Transport.Dispose();
            Repo.Dispose();
        }
    }

    private ushort Id(string key) => _content.GetBlock(key)!.NumericId.Value;

    // ---------------- mining ----------------

    [Fact]
    public void MiningAFaceCell_BanksTheDrop_AndLeavesNoPlayerEditMark()
    {
        using var w = new MineWorld(_content, _root);
        var cell = new Vector3i(Cx + 3, w.PadY, Cz + 3);
        var banked = new List<ItemAmount>();

        Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, banked));

        Assert.True(w.Server.World.GetBlock(cell).IsAir);
        Assert.Equal(new[] { ("stone", 1) }, banked.Select(b => (b.Item, b.Count)).ToArray());
        Assert.False(w.PlayerEdited(cell), "an NPC's break must never read as a player edit");
        Assert.Contains(w.BlockChangesAt(cell), b => b.Block == 0); // every client sees the break

        var refill = Assert.Single(w.Server.NpcRefillsForTest);
        Assert.Equal((cell.X, cell.Y, cell.Z, "stone"), (refill.X, refill.Y, refill.Z, refill.Block));
        Assert.True(refill.DueAt >= 600, "the cell refills after about ten minutes of play");
    }

    [Fact]
    public void TheMiner_RefusesAirPlayerBuildsProtectedCellsAndWhatIsNoRock()
    {
        using var w = new MineWorld(_content, _root);
        var banked = new List<ItemAmount>();
        var air = new Vector3i(Cx, w.PadY + 3, Cz);
        Assert.False(w.Server.NpcMineCellForTest(w.Miner, air, banked));

        // A block the player placed is theirs.
        var built = new Vector3i(Cx + 1, w.PadY, Cz - 4);
        w.Server.World.SetBlock(built, _content.GetBlock("stone")!.NumericId, 0, 0, 0, w.Player.State.Name);
        Assert.False(w.Server.NpcMineCellForTest(w.Miner, built, banked));

        // A panel, a crate, bedrock: nothing a miner breaks.
        foreach (var key in new[] { "lab_panel", "wood_crate", "bedrock" })
        {
            var cell = new Vector3i(Cx - 4, w.PadY, Cz - 4);
            w.Server.World.SetBlock(cell, _content.GetBlock(key)!.NumericId);
            Assert.False(w.Server.NpcMineCellForTest(w.Miner, cell, banked), key);
        }

        // Water beside the cell would run in; sand on top would slide in.
        var wet = new Vector3i(Cx - 5, w.PadY, Cz + 5);
        w.Server.World.SetBlock(new Vector3i(wet.X, wet.Y + 1, wet.Z), _content.GetBlock("water")!.NumericId);
        Assert.False(w.Server.NpcMineCellForTest(w.Miner, wet, banked));
        var sandy = new Vector3i(Cx + 5, w.PadY, Cz - 5);
        w.Server.World.SetBlock(new Vector3i(sandy.X, sandy.Y + 1, sandy.Z), _content.GetBlock("sand")!.NumericId);
        Assert.False(w.Server.NpcMineCellForTest(w.Miner, sandy, banked));

        // A settlement's box is protected for the miner too.
        w.Server.RegisterTestSettlementForTest("Testville", new Vector3i(Cx - 6, w.PadY - 2, Cz + 4), new Vector3i(Cx - 2, w.PadY + 8, Cz + 6));
        Assert.False(w.Server.NpcMineCellForTest(w.Miner, new Vector3i(Cx - 3, w.PadY, Cz + 5), banked));

        Assert.Empty(banked);
        Assert.Empty(w.Server.NpcRefillsForTest);
    }

    // ---------------- the refill ----------------

    [Fact]
    public void TheCell_FillsUpAfterTheDelay_OnlyWhileNoPlayerIsNear()
    {
        using var w = new MineWorld(_content, _root);
        var cell = new Vector3i(Cx + 3, w.PadY, Cz - 3);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, new List<ItemAmount>()));
        long due = w.Server.NpcRefillsForTest.Single().DueAt;

        // Not yet due: nothing happens, however far away the player is.
        w.PlayerAway();
        w.Server.SetPlaytimeForTest(due - 1);
        w.Server.RunNpcRestoresForTest();
        Assert.True(w.Server.World.GetBlock(cell).IsAir);

        // Due, but the player stands within 12 blocks: it waits.
        w.Server.SetPlaytimeForTest(due);
        w.PlayerAt(cell.X + 8.5f, cell.Z + 0.5f);
        w.Server.RunNpcRestoresForTest();
        Assert.True(w.Server.World.GetBlock(cell).IsAir);
        Assert.Single(w.Server.NpcRefillsForTest);

        // The player walks off: the stone is back, as natural ground, and the registry is empty.
        w.PlayerAway();
        w.Server.RunNpcRestoresForTest();
        Assert.Equal(Id("stone"), w.Server.World.GetBlock(cell).Value);
        Assert.False(w.PlayerEdited(cell));
        Assert.Empty(w.Server.NpcRefillsForTest);
        Assert.Contains(w.BlockChangesAt(cell), b => b.Block == Id("stone"));
    }

    [Fact]
    public void TheRefill_RunsInTheTickLoop_OnceItsTimeIsUp()
    {
        using var w = new MineWorld(_content, _root);
        var cell = new Vector3i(Cx - 3, w.PadY, Cz - 3);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, new List<ItemAmount>(), refillSeconds: 2));
        w.PlayerAway();

        for (int i = 0; i < 20 && w.Server.World.GetBlock(cell).IsAir; i++)
        {
            w.Player.State.Health = 100f;
            w.Player.State.Oxygen = 100f;
            w.Server.TickForTest(0.5);
            w.PlayerAway(); // the player floats in the sky out there — keep them put
        }

        Assert.Equal(Id("stone"), w.Server.World.GetBlock(cell).Value);
        Assert.Empty(w.Server.NpcRefillsForTest);
    }

    [Fact]
    public void APendingRefill_SurvivesASaveAndReload()
    {
        var cell = new Vector3i(Cx + 2, 0, Cz + 4);
        int padY;
        long due;
        using (var first = new MineWorld(_content, _root))
        {
            padY = first.PadY;
            cell = new Vector3i(cell.X, padY, cell.Z);
            Assert.True(first.Server.NpcMineCellForTest(first.Miner, cell, new List<ItemAmount>()));
            due = first.Server.NpcRefillsForTest.Single().DueAt;
            first.Server.SaveAllForTest(); // the autosave (or the shutdown) carries the registry
        } // a restart

        using var second = new MineWorld(_content, _root, padY);
        Assert.True(second.Server.World.GetBlock(cell).IsAir, "the mined cell is still air after the restart");
        var refill = Assert.Single(second.Server.NpcRefillsForTest);
        Assert.Equal((cell.X, cell.Y, cell.Z, "stone", due), (refill.X, refill.Y, refill.Z, refill.Block, refill.DueAt));

        second.PlayerAway();
        second.Server.SetPlaytimeForTest(due);
        second.Server.RunNpcRestoresForTest();
        Assert.Equal(Id("stone"), second.Server.World.GetBlock(cell).Value);
        Assert.Empty(second.Server.NpcRefillsForTest);
    }

    [Fact]
    public void TheRegistry_IsWrittenCoalesced_AtMostOncePerMinute_AndWithEveryFullSave()
    {
        using var w = new MineWorld(_content, _root);
        var a = new Vector3i(Cx + 2, w.PadY, Cz - 2);
        var b = new Vector3i(Cx + 3, w.PadY, Cz - 2);
        var c = new Vector3i(Cx + 4, w.PadY, Cz - 2);
        bool Stored(Vector3i cell) => w.Repo.LoadMetadata()!.NpcRefills.TryGetValue(w.Loc, out var list)
                                      && list.Any(r => r.X == cell.X && r.Y == cell.Y && r.Z == cell.Z);

        // A mined cell only marks the registry dirty — the next refill step writes it (the first after a quiet spell).
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, a, new List<ItemAmount>()));
        Assert.False(Stored(a), "the whole metadata was written for one mined cell");
        w.Server.RunNpcRestoresForTest();
        Assert.True(Stored(a));

        // Within the minute: the next cell waits for the coalesced write ...
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, b, new List<ItemAmount>()));
        w.Server.RunNpcRestoresForTest();
        Assert.False(Stored(b));
        w.Server.AdvanceUptimeForTest(61);
        w.Server.RunNpcRestoresForTest();
        Assert.True(Stored(b));

        // ... and any full save (autosave, checkpoint, shutdown) carries it at once.
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, c, new List<ItemAmount>()));
        w.Server.SaveAllForTest();
        Assert.True(Stored(c));
    }

    [Fact]
    public void ACellAPlayerBuiltInto_OrDugAgain_IsLeftAlone()
    {
        using var w = new MineWorld(_content, _root);
        var built = new Vector3i(Cx + 4, w.PadY, Cz);
        var dug = new Vector3i(Cx - 4, w.PadY, Cz);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, built, new List<ItemAmount>()));
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, dug, new List<ItemAmount>()));

        string name = w.Player.State.Name;
        w.Server.World.SetBlock(built, _content.GetBlock("dirt")!.NumericId, 0, 0, 0, name); // built into the hole
        w.Server.World.SetBlock(dug, _content.GetBlock("dirt")!.NumericId, 0, 0, 0, name);   // filled ...
        w.Server.World.SetBlock(dug, BlockId.Air, 0, 0, 0, name);                               // ... and dug out again

        w.PlayerAway();
        w.Server.SetPlaytimeForTest(w.Server.NpcRefillsForTest.Max(r => r.DueAt));
        w.Server.RunNpcRestoresForTest();

        Assert.Equal(Id("dirt"), w.Server.World.GetBlock(built).Value);
        Assert.True(w.Server.World.GetBlock(dug).IsAir, "a player's own dig is never filled over");
        Assert.Empty(w.Server.NpcRefillsForTest);
    }

    [Fact]
    public void ACellSomebodyStandsIn_Waits()
    {
        using var w = new MineWorld(_content, _root);
        var cell = new Vector3i(Cx + 1, w.PadY, Cz + 2);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, new List<ItemAmount>()));
        int other = w.Server.AddNpcForTest(new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f)); // standing IN the hole

        w.PlayerAway();
        w.Server.SetPlaytimeForTest(w.Server.NpcRefillsForTest.Single().DueAt);
        w.Server.RunNpcRestoresForTest();
        Assert.True(w.Server.World.GetBlock(cell).IsAir, "nobody is buried in stone");

        w.Server.RemoveNpcForTest(other);
        w.Server.RunNpcRestoresForTest();
        Assert.Equal(Id("stone"), w.Server.World.GetBlock(cell).Value);
    }

    [Fact]
    public void AFullRegistry_RestoresItsOldestCellFirst()
    {
        using var w = new MineWorld(_content, _root);
        w.Server.NpcRefillCapForTest = 2;
        w.PlayerAway();
        var a = new Vector3i(Cx + 5, w.PadY, Cz + 5);
        var b = new Vector3i(Cx + 4, w.PadY, Cz + 5);
        var c = new Vector3i(Cx + 3, w.PadY, Cz + 5);
        foreach (var cell in new[] { a, b, c })
        {
            Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, new List<ItemAmount>()));
        }

        Assert.Equal(Id("stone"), w.Server.World.GetBlock(a).Value); // the oldest made room
        Assert.True(w.Server.World.GetBlock(b).IsAir);
        Assert.True(w.Server.World.GetBlock(c).IsAir);
        Assert.Equal(new[] { b, c }, w.Server.NpcRefillsForTest.Select(r => new Vector3i(r.X, r.Y, r.Z)).ToArray());
    }

    [Fact]
    public void AFullRegistry_DropsAnOldCellInAnUnloadedChunk_WithoutLoadingIt()
    {
        using var w = new MineWorld(_content, _root);
        w.Server.NpcRefillCapForTest = 2;
        w.PlayerAway();

        // An outcrop a chunk east of the pad, mined while its chunk (and its neighbours') were loaded ...
        var far = new Vector3i(Cx + 20, w.PadY, Cz);
        w.Server.World.SetBlock(far, _content.GetBlock("stone")!.NumericId); // no owner: natural rock
        for (int dy = -1; dy <= 1; dy++)
        {
            w.Server.World.GetBlock(new Vector3i(far.X, far.Y + dy, far.Z));
        }

        Assert.True(w.Server.NpcMineCellForTest(w.Miner, far, new List<ItemAmount>()));
        // ... then nobody streams it any more.
        w.Server.World.ForgetChunksIn(far, far);
        var b = new Vector3i(Cx + 4, w.PadY, Cz + 5);
        var c = new Vector3i(Cx + 3, w.PadY, Cz + 5);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, b, new List<ItemAmount>()));
        int loaded = w.Server.World.LoadedChunkCount;

        Assert.True(w.Server.NpcMineCellForTest(w.Miner, c, new List<ItemAmount>())); // the full registry makes room

        Assert.Equal(loaded, w.Server.World.LoadedChunkCount);
        Assert.Equal(new[] { b, c }, w.Server.NpcRefillsForTest.Select(r => new Vector3i(r.X, r.Y, r.Z)).ToArray());
        Assert.True(w.Server.World.GetBlock(far).IsAir, "the dropped entry's cell was filled through a chunk load");
    }

    [Fact]
    public void ABodyJustAcrossTheLatitudeSeam_StandsInTheCell()
    {
        using var w = new MineWorld(_content, _root);
        var cell = new Vector3i(Cx + 1, w.PadY, Cz + 2);
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, cell, new List<ItemAmount>()));
        // The same cell one latitude period over — what a position just across the north–south seam reads as.
        int period = BlocksBeyondTheStars.Shared.World.WorldConstants.LatitudePeriodFor(w.Server.World.Circumference);
        int other = w.Server.AddNpcForTest(new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + period + 0.5f));

        w.PlayerAway();
        w.Server.SetPlaytimeForTest(w.Server.NpcRefillsForTest.Single().DueAt);
        w.Server.RunNpcRestoresForTest();
        Assert.True(w.Server.World.GetBlock(cell).IsAir, "an NPC across the seam was buried in stone");

        w.Server.RemoveNpcForTest(other);
        w.Server.RunNpcRestoresForTest();
        Assert.Equal(Id("stone"), w.Server.World.GetBlock(cell).Value);
    }

    [Fact]
    public void TheMiner_NeverMinesUnderATorchOrAPlayersWork_NorAboveAHangingPlant()
    {
        using var w = new MineWorld(_content, _root);
        var banked = new List<ItemAmount>();
        string name = w.Player.State.Name;

        var underTorch = new Vector3i(Cx - 3, w.PadY, Cz - 3);
        w.Server.World.SetBlock(new Vector3i(underTorch.X, underTorch.Y + 1, underTorch.Z), _content.GetBlock("torch")!.NumericId);
        var underLantern = new Vector3i(Cx - 3, w.PadY, Cz + 3);
        w.Server.World.SetBlock(new Vector3i(underLantern.X, underLantern.Y + 1, underLantern.Z), _content.GetBlock("lantern")!.NumericId);
        var underBuild = new Vector3i(Cx + 3, w.PadY, Cz - 3);
        w.Server.World.SetBlock(new Vector3i(underBuild.X, underBuild.Y + 1, underBuild.Z), _content.GetBlock("stone")!.NumericId, 0, 0, 0, name);
        string hanging = FloraCatalog.All.First(s => s.Hanging && !s.Fruit && !s.Authored && _content.GetBlock(s.Key) is not null).Key;
        var overVine = new Vector3i(Cx + 3, w.PadY, Cz + 3);
        w.Server.World.SetBlock(new Vector3i(overVine.X, overVine.Y - 1, overVine.Z), _content.GetBlock(hanging)!.NumericId);

        foreach (var cell in new[] { underTorch, underLantern, underBuild, overVine })
        {
            Assert.False(w.Server.NpcMineCellForTest(w.Miner, cell, banked), $"{cell} was mined");
        }

        Assert.Empty(banked);
        var outcrops = w.Server.FindNpcOutcropsForTest(new Vector3f(Cx + 0.5f, w.PadY + 1, Cz + 0.5f), PadHalf, 400);
        Assert.Contains(new Vector3i(Cx, w.PadY, Cz), outcrops);
        Assert.DoesNotContain(overVine, outcrops); // open to the sky, but a plant hangs from it
    }

    // ---------------- the lodge face on load ----------------

    [Fact]
    public void TheLodgeFace_IsRestoredOnLoad_AndItsPendingRefillsGo()
    {
        var face = new List<Vector3i>();
        int padY;
        using (var first = new MineWorld(_content, _root))
        {
            padY = first.PadY;
            for (int dx = -1; dx <= 1; dx++)
            {
                face.Add(new Vector3i(Cx + dx, padY, Cz - 5));
            }

            Assert.True(first.Server.NpcMineCellForTest(first.Miner, face[0], new List<ItemAmount>()));
            Assert.True(first.Server.NpcMineCellForTest(first.Miner, face[1], new List<ItemAmount>()));
            // A player digs the third face cell out by hand — that hole is theirs.
            first.Server.World.SetBlock(face[2], BlockId.Air, 0, 0, 0, first.Player.State.Name);
            first.Server.SaveAllForTest(); // the two pending refills are in the save
        }

        using var second = new MineWorld(_content, _root, padY);
        ushort stone = Id("stone");
        // The lodge's stamp asks for its quarry face back on load (#2640 calls this from StampLodges).
        second.Server.RestoreNpcCellsNowForTest(face.Select(f => (f, stone)));

        Assert.Equal(stone, second.Server.World.GetBlock(face[0]).Value);
        Assert.Equal(stone, second.Server.World.GetBlock(face[1]).Value);
        Assert.True(second.Server.World.GetBlock(face[2]).IsAir, "a player's edit inside the face is not overwritten");
        Assert.False(second.PlayerEdited(face[0]));
        Assert.Empty(second.Server.NpcRefillsForTest);
    }

    // ---------------- the prospector ----------------

    [Fact]
    public void TheProspector_FindsOpenSurfaceRock_NearestFirst_NeverPlayerEditedOrProtected()
    {
        using var w = new MineWorld(_content, _root);
        var centre = new Vector3f(Cx + 0.5f, w.PadY + 1, Cz + 0.5f);
        var edited = new Vector3i(Cx + 1, w.PadY, Cz);
        w.Server.World.SetBlock(edited, _content.GetBlock("stone")!.NumericId, 0, 0, 0, w.Player.State.Name);
        var soil = new Vector3i(Cx - 1, w.PadY, Cz);
        w.Server.World.SetBlock(soil, _content.GetBlock("dirt")!.NumericId);       // no outcrop: soil
        var covered = new Vector3i(Cx, w.PadY, Cz + 1);
        w.Server.World.SetBlock(new Vector3i(covered.X, covered.Y + 1, covered.Z), _content.GetBlock("lab_panel")!.NumericId); // not open to the sky
        w.Server.RegisterTestSettlementForTest("Testville", new Vector3i(Cx - 6, w.PadY - 2, Cz - 6), new Vector3i(Cx + 6, w.PadY + 8, Cz - 3));

        var found = w.Server.FindNpcOutcropsForTest(centre, PadHalf, 400);

        Assert.NotEmpty(found);
        Assert.Equal(new Vector3i(Cx, w.PadY, Cz), found[0]); // right under the centre
        Assert.DoesNotContain(edited, found);
        Assert.DoesNotContain(soil, found);
        Assert.DoesNotContain(covered, found);
        Assert.DoesNotContain(found, f => f.Z <= Cz - 3); // the settlement's box
        Assert.All(found, f => Assert.Equal(w.PadY, f.Y));
        double last = 0;
        foreach (var f in found)
        {
            double d = Math.Pow(f.X + 0.5 - centre.X, 2) + Math.Pow(f.Z + 0.5 - centre.Z, 2);
            Assert.True(d + 1e-6 >= last, "nearest first");
            last = d;
        }

        Assert.Equal(3, w.Server.FindNpcOutcropsForTest(centre, PadHalf, 3).Count);

        // Every cell it hands out is one the miner can actually take.
        Assert.True(w.Server.NpcMineCellForTest(w.Miner, found[^1], new List<ItemAmount>()));
    }
}
