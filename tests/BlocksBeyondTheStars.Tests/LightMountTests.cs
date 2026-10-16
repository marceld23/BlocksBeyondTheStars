// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Torches and lanterns on walls and ceilings (#2683 — "when it is placed on a wall it should hang on the wall!"). The
/// face the player clicked is stamped into the light's descriptor like the ladder's mount (#909): a wall, the floor, or
/// (the lantern only) the ceiling; a torch under a ceiling is refused with a hint. A light without that stamp — older
/// saves, worldgen, an intent without a face — stays upright with the #2532 support reading, and a stamped light falls
/// exactly when the block it is fixed to goes. Built high in an empty air column (y ≥ 199).
/// </summary>
public sealed class LightMountTests : IDisposable
{
    private const int Y = 201; // the light's row; a dirt floor lies at Y - 2 so nothing holds a light from below by accident
    private readonly string _root;
    private readonly GameContent _content;

    public LightMountTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_lightmount_" + Guid.NewGuid().ToString("N"));
        _content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());
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
        catch (IOException)
        {
            // best effort
        }
    }

    /// <summary>Records every decoded outbound message so a test can read the reject reasons.</summary>
    private sealed class RecordingTransport : IServerTransport
    {
        public event Action<int>? ClientConnected;
        public event Action<int>? ClientDisconnected;
        public event Action<int, byte[]>? PayloadReceived;

        public readonly List<(int Conn, object Msg)> Sent = new();

        public void Start(int port) { }
        public void Send(int connectionId, byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((connectionId, m));
        }
        public void Broadcast(byte[] payload, DeliveryMode mode)
        {
            if (NetCodec.Decode(payload) is { } m) Sent.Add((int.MinValue, m));
        }
        public void Poll() { _ = ClientConnected; _ = ClientDisconnected; _ = PayloadReceived; }
        public void Stop() { }
        public void Dispose() { }
    }

    private SvGameServer Started(out SqliteWorldRepository repo, IServerTransport transport)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lightmount"));
        var config = new ServerConfig
        {
            WorldName = "lightmount",
            Seed = 9,
            StartPlanet = _content.Planets.Values.First(p => !p.IsAirless && !p.SpaceSky).Key, // a torch needs air
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
            PlaceSettlements = false,
        };
        var server = new SvGameServer(config, _content, transport, repo);
        server.Start();
        return server;
    }

    private BlockId Id(string key) => _content.GetBlock(key)!.NumericId;

    /// <summary>A dirt floor two rows under the lights and a builder standing on it, out of the way of the test cells.</summary>
    private SvGameServer Workshop(out SqliteWorldRepository repo, out RecordingTransport transport, out BlocksBeyondTheStars.GameServer.PlayerSession builder)
    {
        transport = new RecordingTransport();
        var server = Started(out repo, transport);
        for (int x = -3; x <= 3; x++)
        {
            for (int z = -3; z <= 3; z++)
            {
                server.World.SetBlock(new Vector3i(x, Y - 2, z), Id("dirt"));
            }
        }

        builder = server.AddLocalPlayer("Builder");
        builder.State.AboardShip = false;
        builder.State.Position = new Vector3f(2.5f, Y - 1, 2.5f);
        builder.State.Inventory.Add("torch", 8, _content.MaxStackOf("torch"));
        builder.State.Inventory.Add("lantern", 8, _content.MaxStackOf("lantern"));
        return server;
    }

    private static IEnumerable<string> RejectionsTo(RecordingTransport t, BlocksBeyondTheStars.GameServer.PlayerSession who)
        => t.Sent.Where(s => s.Conn == who.ConnectionId).Select(s => s.Msg).OfType<ActionRejected>().Select(m => m.Reason);

    private static int DroppedCount(SvGameServer server, string item)
        => server.DropPackets.SelectMany(p => p.Items).Where(s => s.Item == item).Sum(s => s.Count);

    private static int Mount(int face) => ShapeCode.Pack(PropShapes.LightMountShape, 0, face);

    // ── the shared rule ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LightForm_TheClickedFace_PicksTheMount()
    {
        static bool Holds(int face) => true;

        foreach (string torch in new[] { "torch", "torch_off" })
        {
            for (int wall = 2; wall <= 5; wall++)
            {
                Assert.Equal(Mount(wall), PropShapes.LightForm(torch, wall, Holds)); // on that wall
            }

            Assert.Equal(Mount(ShapeCode.UpPlusY), PropShapes.LightForm(torch, ShapeCode.UpPlusY, Holds)); // standing, stamped
            Assert.Equal(PropShapes.LightRefused, PropShapes.LightForm(torch, 1, Holds)); // never under a ceiling
        }

        foreach (string lantern in new[] { "lantern", "lantern_off" })
        {
            Assert.Equal(Mount(1), PropShapes.LightForm(lantern, 1, Holds)); // the lantern hangs
            Assert.Equal(Mount(4), PropShapes.LightForm(lantern, 4, Holds));
        }

        // No face, no block to hold it, or no light at all: the unstamped light, upright as it always was.
        Assert.Equal(0, PropShapes.LightForm("torch", -1, Holds));
        Assert.Equal(0, PropShapes.LightForm("torch", 3, _ => false));
        Assert.Equal(0, PropShapes.LightForm("torch", 1, _ => false)); // a stale "ceiling" with nothing above is no refusal
        Assert.Equal(0, PropShapes.LightForm("torch", 2, null));
        Assert.Equal(0, PropShapes.LightForm("stone", 2, Holds));
    }

    [Fact]
    public void LightMountOf_ReadsOnlyAStampedLight()
    {
        Assert.Equal(-1, PropShapes.LightMountOf("torch", 0)); // unstamped (older saves, worldgen)
        Assert.Equal(2, PropShapes.LightMountOf("torch", Mount(2)));
        Assert.Equal(0, PropShapes.LightMountOf("torch_off", Mount(0)));
        Assert.Equal(1, PropShapes.LightMountOf("lantern", Mount(1)));
        Assert.Equal(-1, PropShapes.LightMountOf("torch", Mount(1))); // a forged hanging torch stands
        Assert.Equal(-1, PropShapes.LightMountOf("stone", Mount(2)));
        Assert.Equal(-1, PropShapes.LightMountOf("torch", ShapeCode.WithDesign(0, 7))); // painted, still unstamped
        Assert.Equal(5, PropShapes.LightMountOf("lantern", ShapeCode.WithDesign(Mount(5), 7))); // paint keeps the mount

        Assert.Equal(PropOrientation.LightMount, PropShapes.OrientationOf("torch"));
        Assert.Equal(PropOrientation.LightMount, PropShapes.OrientationOf("lantern"));
        Assert.Equal(0, PropShapes.DefaultPlaceShape("torch")); // icons, held items and the tutorial still see a plain light
        Assert.True(PropShapes.IsStampedForm("torch", PropShapes.LightMountShape)); // the mined drop stacks with crafted ones
        Assert.True(PropShapes.IsStampedForm("lantern_off", PropShapes.LightMountShape));
        Assert.False(PropShapes.IsStampedForm("stone", PropShapes.LightMountShape));
    }

    [Fact]
    public void HoldsLight_IsASolidBlock_NotAPropOrAFluid()
    {
        Assert.True(PropShapes.HoldsLight(_content.GetBlock("stone")));
        Assert.True(PropShapes.HoldsLight(_content.GetBlock("dirt")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("torch")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("lantern")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("ladder")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("flora_plant")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("water")));
        Assert.False(PropShapes.HoldsLight(_content.GetBlock("lava")));
        Assert.False(PropShapes.HoldsLight(null));
    }

    // ── placing ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ATorchPlacedAgainstAWall_HangsOnIt()
    {
        var server = Workshop(out var repo, out var t, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(-1, Y, 0), Id("dirt")); // the wall at −X → clicked face +X (2)

            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: 2);

            Assert.Equal(Id("torch").Value, server.World.GetBlock(cell).Value);
            Assert.Equal(Mount(2), server.World.GetShape(cell));
            Assert.Equal(7, p.State.Inventory.CountOf("torch"));
            Assert.Empty(RejectionsTo(t, p));
        }
    }

    [Fact]
    public void ATorchPlacedOnTheFloor_StandsStamped()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(0, Y - 1, 0), Id("dirt"));

            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: ShapeCode.UpPlusY);

            Assert.Equal(Mount(ShapeCode.UpPlusY), server.World.GetShape(cell));
        }
    }

    [Fact]
    public void ATorchUnderACeiling_IsRefusedWithAHint_AndNotConsumed()
    {
        var server = Workshop(out var repo, out var t, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(0, Y + 1, 0), Id("dirt"));

            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: 1);

            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Contains("@srv.place.torch_ceiling", RejectionsTo(t, p));
            Assert.Equal(8, p.State.Inventory.CountOf("torch"));
        }
    }

    [Fact]
    public void ALanternUnderACeiling_HangsFromIt()
    {
        var server = Workshop(out var repo, out var t, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(0, Y + 1, 0), Id("dirt"));

            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "lantern", upFace: 1);

            Assert.Equal(Id("lantern").Value, server.World.GetBlock(cell).Value);
            Assert.Equal(Mount(1), server.World.GetShape(cell));
            Assert.Empty(RejectionsTo(t, p));
        }
    }

    [Fact]
    public void WithoutAFaceOrABlockThere_TheLightStaysUnstamped()
    {
        var server = Workshop(out var repo, out var t, out var p);
        using (repo)
        {
            // No face at all (an old client, an internal placement): exactly the light of before, even beside a wall.
            var old = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(-1, Y, 0), Id("dirt"));
            server.PlaceBlock(p.State.PlayerId, old.X, old.Y, old.Z, "torch");
            Assert.Equal(0, server.World.GetShape(old));

            // A wall face whose block is not there (a client that saw a stale world): no mount onto thin air.
            var gone = new Vector3i(0, Y, -2);
            server.PlaceBlock(p.State.PlayerId, gone.X, gone.Y, gone.Z, "torch", upFace: 2);
            Assert.Equal(0, server.World.GetShape(gone));

            // A "ceiling" with nothing above (a stale rotate state from an old client) is no reason to refuse a torch.
            var open = new Vector3i(-2, Y, 0);
            server.PlaceBlock(p.State.PlayerId, open.X, open.Y, open.Z, "torch", upFace: 1);
            Assert.Equal(Id("torch").Value, server.World.GetBlock(open).Value);
            Assert.Equal(0, server.World.GetShape(open));
            Assert.Empty(RejectionsTo(t, p));
        }
    }

    [Fact]
    public void AMinedWallTorch_DropsThePlainTorch()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            server.World.SetBlock(new Vector3i(0, Y, -1), Id("dirt"));
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: 4);
            Assert.Equal(Mount(4), server.World.GetShape(cell));

            server.MineBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z);

            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Equal(8, p.State.Inventory.CountOf("torch")); // back on the stack — no "torch#s.." key of its own
        }
    }

    // ── what holds it ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AWallTorch_FallsWhenItsWallGoes()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            var wall = new Vector3i(1, Y, 0);
            server.World.SetBlock(wall, Id("dirt")); // the wall at +X → clicked face −X (3)
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: 3);
            Assert.Equal(Mount(3), server.World.GetShape(cell));

            server.MineBlock(p.State.PlayerId, wall.X, wall.Y, wall.Z);

            Assert.True(server.World.GetBlock(wall).IsAir);
            Assert.True(server.World.GetBlock(cell).IsAir, "a wall torch must not float where its wall was");
            Assert.Equal(1, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void AWallTorch_StaysWhenAnotherBlockAroundItGoes()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            var floor = new Vector3i(0, Y - 1, 0);
            var side = new Vector3i(0, Y, 1);
            server.World.SetBlock(new Vector3i(-1, Y, 0), Id("dirt")); // its wall
            server.World.SetBlock(floor, Id("dirt"));
            server.World.SetBlock(side, Id("dirt"));
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: 2);

            server.MineBlock(p.State.PlayerId, floor.X, floor.Y, floor.Z);
            server.MineBlock(p.State.PlayerId, side.X, side.Y, side.Z);

            Assert.Equal(Id("torch").Value, server.World.GetBlock(cell).Value);
            Assert.Equal(0, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void AStandingTorch_FallsWithItsFloor_EvenBesideAWall()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            var floor = new Vector3i(0, Y - 1, 0);
            server.World.SetBlock(floor, Id("dirt"));
            server.World.SetBlock(new Vector3i(-1, Y, 0), Id("dirt")); // a wall beside it holds an UNSTAMPED torch, not this one
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "torch", upFace: ShapeCode.UpPlusY);
            Assert.Equal(Mount(ShapeCode.UpPlusY), server.World.GetShape(cell));

            server.MineBlock(p.State.PlayerId, floor.X, floor.Y, floor.Z);

            Assert.True(server.World.GetBlock(cell).IsAir, "a standing torch stands on its floor");
            Assert.Equal(1, DroppedCount(server, "torch"));
        }
    }

    [Fact]
    public void AHangingLantern_FallsWithItsCeiling_NotWithTheBlockUnderIt()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            var cell = new Vector3i(0, Y, 0);
            var ceiling = new Vector3i(0, Y + 1, 0);
            var below = new Vector3i(0, Y - 1, 0);
            server.World.SetBlock(ceiling, Id("dirt"));
            server.World.SetBlock(below, Id("dirt"));
            server.PlaceBlock(p.State.PlayerId, cell.X, cell.Y, cell.Z, "lantern", upFace: 1);
            Assert.Equal(Mount(1), server.World.GetShape(cell));

            server.MineBlock(p.State.PlayerId, below.X, below.Y, below.Z);
            Assert.Equal(Id("lantern").Value, server.World.GetBlock(cell).Value); // it hangs, nothing stands on that block

            server.MineBlock(p.State.PlayerId, ceiling.X, ceiling.Y, ceiling.Z);
            Assert.True(server.World.GetBlock(cell).IsAir, "a hanging lantern falls with its ceiling");
            Assert.Equal(1, DroppedCount(server, "lantern"));
        }
    }

    [Fact]
    public void InAShipCabin_ALanternHangsOnTheHull_AndATorchUnderTheCeilingIsRefused()
    {
        var t = new RecordingTransport();
        var repo = new SqliteWorldRepository(new SaveGamePaths(_root, "lightmount_ship"));
        var config = new ServerConfig { WorldName = "lightmount_ship", Seed = 1, AutoSaveIntervalMinutes = 9999, PlaceStarterShip = true };
        var server = new SvGameServer(config, _content, t, repo);
        server.Start();
        using (repo)
        {
            server.AddLocalPlayer("Host");
            var p = server.AddLocalPlayer("Builder");
            p.State.InstantBuild = true; // no materials needed for the test placements
            var (origin, _) = server.LandedShipBoundsForTest("Builder");
            var s = server.BuildShipStructureForTest("Builder");

            // A free interior cell beside a hull wall, and one under a hull ceiling.
            Vector3i? wallCell = null, roofedCell = null;
            int wallFace = -1;
            for (int x = 1; x < s.Width - 1; x++)
            {
                for (int y = 1; y < s.Height - 1; y++)
                {
                    for (int z = 1; z < s.Length - 1; z++)
                    {
                        var c = new Vector3i(x, y, z);
                        if (!s.Get(c).IsAir || s.DoorCells.Contains(c))
                        {
                            continue;
                        }

                        for (int face = 2; face <= 5 && wallCell is null; face++)
                        {
                            var (dx, _, dz) = ShapeCode.FaceDirection(face);
                            if (PropShapes.HoldsLight(_content.BlockById(s.Get(new Vector3i(x - dx, y, z - dz)))))
                            {
                                wallCell = c;
                                wallFace = face;
                            }
                        }

                        if (roofedCell is null && c != wallCell && PropShapes.HoldsLight(_content.BlockById(s.Get(new Vector3i(x, y + 1, z)))))
                        {
                            roofedCell = c;
                        }
                    }
                }
            }

            Assert.NotNull(wallCell);
            Assert.NotNull(roofedCell);
            var lit = wallCell!.Value;
            p.State.Position = new Vector3f(origin.X + lit.X + 0.5f, origin.Y + lit.Y, origin.Z + lit.Z + 0.5f);
            server.HandleStructureEditForTest("Builder", new StructureEditIntent
            {
                StructureId = "ship:Builder",
                X = lit.X,
                Y = lit.Y,
                Z = lit.Z,
                Mine = false,
                ItemKey = "lantern",
                UpFace = wallFace,
            });

            Assert.Equal(Mount(wallFace), server.BuildShipStructureForTest("Builder").Shapes[lit]);

            var roofed = roofedCell!.Value;
            server.HandleStructureEditForTest("Builder", new StructureEditIntent
            {
                StructureId = "ship:Builder",
                X = roofed.X,
                Y = roofed.Y,
                Z = roofed.Z,
                Mine = false,
                ItemKey = "torch",
                UpFace = 1,
            });

            Assert.Contains("@srv.place.torch_ceiling", RejectionsTo(t, p));
            Assert.True(server.BuildShipStructureForTest("Builder").Get(roofed).IsAir);
        }
    }

    [Fact]
    public void AnUnstampedTorch_KeepsTheOldReading()
    {
        var server = Workshop(out var repo, out _, out var p);
        using (repo)
        {
            // A torch from an older save beside a wall: mining the wall leaves it (it might stand on its floor)…
            var cell = new Vector3i(0, Y, 0);
            var wall = new Vector3i(-1, Y, 0);
            var floor = new Vector3i(0, Y - 1, 0);
            server.World.SetBlock(wall, Id("dirt"));
            server.World.SetBlock(floor, Id("dirt"));
            server.World.SetBlock(cell, Id("torch"));

            server.MineBlock(p.State.PlayerId, wall.X, wall.Y, wall.Z);
            Assert.Equal(Id("torch").Value, server.World.GetBlock(cell).Value);

            // …and with the wall gone, its floor is all that holds it.
            server.MineBlock(p.State.PlayerId, floor.X, floor.Y, floor.Z);
            Assert.True(server.World.GetBlock(cell).IsAir);
            Assert.Equal(1, DroppedCount(server, "torch"));
        }
    }
}
