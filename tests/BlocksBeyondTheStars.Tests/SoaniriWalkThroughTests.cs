// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using Xunit;
using SvGameServer = BlocksBeyondTheStars.GameServer.GameServer;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// #3422 "Ich bleibe stecken": on Soaniri a player could not walk forward — every step into the meadow was undone, and a
/// jetpack hop landed him on the very spot again, ten blocks west of the world seam. Neither the seam nor the server: the
/// server keeps every position the walk reports, across the seam in both directions. The set-back was the client's
/// out-of-world guard (<c>PlayerController.GuardAgainstFallingOut</c>): it asks the Shared walk-through list
/// (<see cref="WalkThroughBlocks.IsProp"/>, through <c>PlayerController.IsCollidingKey</c> — pinned by
/// <c>LineOfSightTests</c>) whether the block at the body's chest or eye is one it can be stuck in, and that list knew plants
/// only by their <c>flora_</c> prefix. Soaniri's singing plants carry none; the mesher draws them without a collider
/// (<see cref="FloraCatalog.IsPlantKey"/>), so the body walked into a chime reed, the guard read "inside a block" and after
/// twelve frames snapped the player back to the last spot before the plant.
/// </summary>
public sealed class SoaniriWalkThroughTests : IDisposable
{
    private const float StepLength = 0.25f;
    private const float FeetLift = 0.03f; // the capsule's feet stand this far above the floor (the report: y 67.03)
    private const float ChestProbe = 0.9f; // the out-of-world guard's two probes above the feet: chest …
    private const float EyeProbe = 1.6f; // … and the standing eye (PlayerController.FirstPersonEye)
    private const string Walker = "Screelit";
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>The singing plants that stand on the ground (the harp vine hangs under an arch's lintel).</summary>
    private static readonly string[] MeadowPlants =
    {
        FloraCatalog.ChimeReedKey, FloraCatalog.BellBloomKey, FloraCatalog.BellBloomOpenKey, FloraCatalog.EchoFlowerKey,
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "bbts_soaniri_walk_" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<(int X, int Z), int> _feet = new();
    private SqliteWorldRepository? _repo;

    public void Dispose()
    {
        _repo?.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a SQLite handle may linger on Windows; the temp folder is cleaned by the OS
        }
    }

    [Fact]
    public void EveryPlant_IsAWalkThroughProp_TheSingingPlantsToo()
    {
        foreach (var def in Content.Blocks.Values)
        {
            if (FloraCatalog.IsPlantKey(def.Key))
            {
                Assert.True(WalkThroughBlocks.IsProp(def.Key), $"'{def.Key}' is drawn as a plant (no collider) but is no walk-through prop");
            }
        }

        foreach (var key in FloraCatalog.SingingPlantKeys)
        {
            Assert.NotNull(Content.GetBlock(key));
            Assert.True(WalkThroughBlocks.IsProp(key), key);
            Assert.True(WalkThroughBlocks.IsWalkThrough(key), key);
        }

        Assert.False(WalkThroughBlocks.IsProp("chime_moss")); // the moss they grow on is ground
        Assert.False(WalkThroughBlocks.IsWalkThrough("resonance_crystal"));
    }

    [Fact]
    public void AWalkThroughAReedBand_AndAcrossTheSeam_IsNeverSetBack()
    {
        var transport = new RecordingTransport();
        _repo = new SqliteWorldRepository(new SaveGamePaths(_root, "soaniri_walk"));
        var server = SoaniriSitesTests.StartOn(_repo, "soaniri_walk", 3422, transport);
        int circ = server.World.Circumference;

        // The report's spot: x = circumference − 10.3, walking north (yaw ≈ 0) into a band of singing plants at z 11–13;
        // then east along the band across the seam (the client reports x beyond the circumference) …
        PlantBand(server, circ - 12, circ + 11, 11, 13);
        var walker = server.AddLocalPlayer(Walker);
        walker.State.AboardShip = false;
        walker.State.Position = new Vector3f((float)WorldConstants.WrapX(circ - 10.3, circ), Feet(server, circ - 11, 8) + FeetLift, 8.5f);
        Walk(server, walker, (circ - 10.3f, 8.5f), (circ - 10.3f, 15.5f), (circ - 10.3f, 12.5f), (circ + 10.3f, 12.5f));

        // … and back west, the way a client that has not lapped reports it: x running below zero.
        Walk(server, walker, (10.3f, 12.5f), (-10.3f, 12.5f));

        Assert.Empty(transport.To<RespawnNotice>(walker)); // no rescue, no snap — the server never moved him
    }

    /// <summary>Walks the player along straight legs through the real movement handler, a server tick after every step, and
    /// checks both halves of "can walk on": the server keeps the reported position (wrapped onto the planet), and every cell
    /// the client's out-of-world guard probes on the way is one the body walks through.</summary>
    private void Walk(SvGameServer server, PlayerSession walker, params (float X, float Z)[] waypoints)
    {
        int circ = server.World.Circumference;
        for (int leg = 1; leg < waypoints.Length; leg++)
        {
            var (fromX, fromZ) = waypoints[leg - 1];
            var (toX, toZ) = waypoints[leg];
            float length = MathF.Sqrt((toX - fromX) * (toX - fromX) + (toZ - fromZ) * (toZ - fromZ));
            int steps = (int)MathF.Ceiling(length / StepLength);
            for (int i = 0; i <= steps; i++)
            {
                float x = fromX + (toX - fromX) * i / steps;
                float z = fromZ + (toZ - fromZ) * i / steps;
                float wrappedX = (float)WorldConstants.WrapX(x, circ);
                float y = Feet(server, (int)MathF.Floor(wrappedX), (int)MathF.Floor(z)) + FeetLift;
                server.MoveForTest(Walker, x, y, z);
                server.TickForTest(0.05);
                server.RunVoidRescueForTest();

                var at = walker.State.Position;
                Assert.True(MathF.Abs(at.X - wrappedX) < 1e-3f && MathF.Abs(at.Y - y) < 1e-3f && MathF.Abs(at.Z - z) < 1e-3f,
                    $"set back: walked to ({wrappedX:0.00}, {y:0.00}, {z:0.00}), the server holds {at}");
                AssertBodyPasses(server, wrappedX, y + ChestProbe, z);
                AssertBodyPasses(server, wrappedX, y + EyeProbe, z);
            }
        }
    }

    /// <summary>The out-of-world guard's question for one probe: a cell the body can be stuck in is a colliding block — not
    /// air, a fluid or a walk-through prop (<c>PlayerController.IsCollidingKey</c>).</summary>
    private static void AssertBodyPasses(SvGameServer server, float x, float y, float z)
    {
        var cell = new Vector3i((int)MathF.Floor(x), (int)MathF.Floor(y), (int)MathF.Floor(z));
        var def = Content.BlockById(server.World.GetBlock(cell));
        if (def == null || def.Key == "air" || def.Liquid || def.Key is "water" or "lava")
        {
            return;
        }

        Assert.True(WalkThroughBlocks.IsProp(def.Key), $"the client would read the body as stuck in '{def.Key}' at {cell}");
    }

    /// <summary>A band of singing plants across the seam: every free feet cell of the columns
    /// [<paramref name="x0"/>, <paramref name="x1"/>] × [<paramref name="z0"/>, <paramref name="z1"/>] gets one, in turn.</summary>
    private void PlantBand(SvGameServer server, int x0, int x1, int z0, int z1)
    {
        int circ = server.World.Circumference;
        int planted = 0;
        for (int x = x0; x <= x1; x++)
        {
            for (int z = z0; z <= z1; z++)
            {
                int cx = (int)WorldConstants.WrapX(x, circ);
                var cell = new Vector3i(cx, Feet(server, cx, z), z);
                if (server.World.GetBlock(cell).IsAir)
                {
                    server.World.SetBlock(cell, Content.GetBlock(MeadowPlants[planted++ % MeadowPlants.Length])!.NumericId);
                }
            }
        }

        Assert.True(planted > (x1 - x0) * (z1 - z0), $"only {planted} plants in the band");
    }

    /// <summary>The feet cell of a column: one above its topmost ground block (no plant, no fluid). Cached, so the walk keeps
    /// standing on the ground it measured before any plant went in.</summary>
    private int Feet(SvGameServer server, int x, int z)
    {
        if (_feet.TryGetValue((x, z), out int cached))
        {
            return cached;
        }

        int top = server.SurfaceHeightForTest(x, z) + 12;
        for (int y = top; y > top - 40; y--)
        {
            var def = Content.BlockById(server.World.GetBlock(new Vector3i(x, y, z)));
            if (def is { Solid: true, Liquid: false } && def.Key is not ("water" or "lava") && !FloraCatalog.IsPlantKey(def.Key))
            {
                _feet[(x, z)] = y + 1;
                return y + 1;
            }
        }

        throw new InvalidOperationException($"no ground under column ({x}, {z})");
    }
}
