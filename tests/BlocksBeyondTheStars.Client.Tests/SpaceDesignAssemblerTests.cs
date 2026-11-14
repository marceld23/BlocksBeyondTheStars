// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.GameServer;
using BlocksBeyondTheStars.Networking;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using Xunit;

namespace BlocksBeyondTheStars.Client.Tests;

/// <summary>
/// #3612: a giant wreck's design is far bigger than one network message may be (1 MB), so the server sends it in parts of
/// one serial and <see cref="SpaceDesignAssembler"/> joins them: a whole design passes straight through, parts join in any
/// order, a newer serial replaces an unfinished older design, a part of an out-of-date serial is dropped — and, against the
/// real server, a 200 000-cell structure's parts each stay under the cap in both encodings and join to the very cells sent.
/// </summary>
public sealed class SpaceDesignAssemblerTests
{
    private static readonly Lazy<GameContent> Content = new(() => ContentLoader.LoadFromDirectory(ClientTestPaths.DataDir()));

    /// <summary>A design part holding cells <paramref name="from"/> … <paramref name="from"/> + <paramref name="n"/> − 1 of a
    /// row along X (block = 1 + index), with a dye on every third cell when <paramref name="dyed"/>.</summary>
    private static SpaceShipDesign Part(string id, int serial, int index, int count, int from, int n, bool dyed = false)
    {
        var d = new SpaceShipDesign
        {
            Id = id,
            Kind = "station",
            Serial = serial,
            PartIndex = index,
            PartCount = count,
            PosX = 5f,
            Width = 99,
            X = Enumerable.Range(from, n).ToArray(),
            Y = new int[n],
            Z = new int[n],
            Block = Enumerable.Range(from, n).Select(i => (ushort)(1 + i)).ToArray(),
            Tint = dyed ? Enumerable.Range(from, n).Select(i => i % 3 == 0 ? 0x123456 : 0).ToArray() : Array.Empty<int>(),
            Glow = dyed ? new int[n] : Array.Empty<int>(),
            Shape = dyed ? new int[n] : Array.Empty<int>(),
        };
        if (index == 0)
        {
            d.DoorX = new[] { 7 };
            d.DoorY = new[] { 1 };
            d.DoorZ = new[] { 0 };
            d.DoorKind = new[] { "slide" };
            d.DoorAxisX = new[] { true };
        }

        return d;
    }

    [Fact]
    public void AWholeDesign_PassesStraightThrough()
    {
        var asm = new SpaceDesignAssembler();
        var old = Part("station:a", 0, 0, 0, 0, 5); // an older server: no serial, no parts
        var one = Part("station:a", 4, 0, 1, 0, 5);

        Assert.Same(old, asm.Add(old));
        Assert.Same(one, asm.Add(one));
        Assert.Equal(0, asm.PendingCount);
    }

    [Fact]
    public void Parts_InOrder_JoinToTheWholeDesign_WithPartZerosHeaderAndDoors()
    {
        var asm = new SpaceDesignAssembler();
        Assert.Null(asm.Add(Part("derelict:giant-a", 7, 0, 3, 0, 4)));
        Assert.Null(asm.Add(Part("derelict:giant-a", 7, 1, 3, 4, 4)));
        var whole = asm.Add(Part("derelict:giant-a", 7, 2, 3, 8, 2));

        Assert.NotNull(whole);
        Assert.Equal(Enumerable.Range(0, 10), whole!.X);
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (ushort)i), whole.Block);
        Assert.Equal(10, whole.Y.Length);
        Assert.Equal(new[] { 7 }, whole.DoorX);
        Assert.Equal("station", whole.Kind);
        Assert.Equal(99, whole.Width);
        Assert.Equal(1, whole.PartCount);
        Assert.Equal(7, whole.Serial);
        Assert.Equal(0, asm.PendingCount);
    }

    [Fact]
    public void Parts_OutOfOrder_JoinTheSame()
    {
        var asm = new SpaceDesignAssembler();
        Assert.Null(asm.Add(Part("w", 3, 2, 3, 8, 2)));
        Assert.Null(asm.Add(Part("w", 3, 0, 3, 0, 4)));
        Assert.Null(asm.Add(Part("w", 3, 0, 3, 0, 4))); // a duplicate changes nothing
        var whole = asm.Add(Part("w", 3, 1, 3, 4, 4));

        Assert.NotNull(whole);
        Assert.Equal(Enumerable.Range(0, 10), whole!.X);
        Assert.Equal(new[] { 7 }, whole.DoorX);
    }

    [Fact]
    public void ANewerSerial_ReplacesAnUnfinishedOlderDesign_AndTheOldOnesPartsAreDropped()
    {
        var asm = new SpaceDesignAssembler();
        Assert.Null(asm.Add(Part("w", 1, 0, 2, 0, 4)));
        Assert.Null(asm.Add(Part("w", 2, 0, 2, 100, 3))); // the structure changed: a new design starts
        Assert.Null(asm.Add(Part("w", 1, 1, 2, 4, 4))); // the old design's last part is out of date
        var whole = asm.Add(Part("w", 2, 1, 2, 103, 2));

        Assert.NotNull(whole);
        Assert.Equal(Enumerable.Range(100, 5), whole!.X);

        // A whole design older than the one being collected is dropped too; a newer one replaces it.
        Assert.Null(asm.Add(Part("w", 9, 0, 2, 0, 1)));
        Assert.Null(asm.Add(Part("w", 8, 0, 1, 0, 1)));
        var newer = Part("w", 10, 0, 1, 0, 1);
        Assert.Same(newer, asm.Add(newer));
        Assert.Equal(0, asm.PendingCount);
    }

    [Fact]
    public void Modifiers_FillEveryCell_WhenAnyPartCarriesThem()
    {
        var asm = new SpaceDesignAssembler();
        Assert.Null(asm.Add(Part("w", 1, 0, 2, 0, 3, dyed: true)));
        var whole = asm.Add(Part("w", 1, 1, 2, 3, 3, dyed: false));

        Assert.NotNull(whole);
        Assert.Equal(6, whole!.Tint.Length);
        Assert.Equal(new[] { 0x123456, 0, 0, 0, 0, 0 }, whole.Tint);
        Assert.Equal(6, whole.Shape.Length);
    }

    [Fact]
    public void AMalformedPart_IsDropped_AndForgetAndClearDropUnfinishedDesigns()
    {
        var asm = new SpaceDesignAssembler();
        var broken = Part("w", 1, 0, 2, 0, 3);
        broken.Y = new int[2];
        Assert.Null(asm.Add(broken));
        Assert.Null(asm.Add(Part("w", 1, 5, 2, 0, 3))); // index out of range
        Assert.Equal(0, asm.PendingCount);

        Assert.Null(asm.Add(Part("w", 1, 0, 2, 0, 3)));
        Assert.Null(asm.Add(Part("v", 1, 0, 2, 0, 3)));
        asm.Forget("w");
        Assert.Equal(1, asm.PendingCount);
        asm.Clear();
        Assert.Equal(0, asm.PendingCount);
    }

    // ---------------------------------------------------------------- against the real server

    /// <summary>A 200 000-cell structure (a hollow 120 × 40 × 120 box with decks every five blocks and room walls every twelve)
    /// sent WHOLE (not as a shell): every part, MessagePack and the browser's JSON envelope alike, stays well under 1 MB, and
    /// the parts — decoded from the wire — join to exactly the structure's cells.</summary>
    [Fact]
    public void ABigDesign_GoesInPartsUnderTheCap_InBothEncodings_AndJoinsToItsCells()
    {
        using var h = new ClientServerHarness(Content.Value);
        var s = new SpaceStructure { Id = "station:big", Kind = "station" };
        ushort wall = Content.Value.GetBlock("iron_wall")!.NumericId.Value;
        foreach (var c in GiantCells())
        {
            s.Set(c, new BlockId(wall), (c.X * 7) % 5 == 0 ? 0x334455 : 0, 0, 0);
        }

        Assert.True(s.Cells.Count >= 200_000, $"the structure holds {s.Cells.Count} cells");
        var parts = h.Server.ShipDesignPartsForTest(s);
        Assert.True(parts.Count >= 4, $"{parts.Count} parts");

        var asm = new SpaceDesignAssembler();
        SpaceShipDesign? whole = null;
        foreach (var part in parts.Reverse())
        {
            byte[] native = NetCodec.Encode(part);
            byte[] json = NetCodec.EncodeJson(part);
            Assert.True(native.Length < 800_000, $"part {part.PartIndex}: {native.Length} bytes MessagePack");
            Assert.True(json.Length < 800_000, $"part {part.PartIndex}: {json.Length} bytes JSON");
            var decoded = Assert.IsType<SpaceShipDesign>(NetCodec.Decode(native));
            Assert.IsType<SpaceShipDesign>(NetCodec.Decode(json));
            whole = asm.Add(decoded) ?? whole;
        }

        Assert.NotNull(whole);
        var got = new Dictionary<Vector3i, (ushort, int)>();
        for (int i = 0; i < whole!.Block.Length; i++)
        {
            got[new Vector3i(whole.X[i], whole.Y[i], whole.Z[i])] = (whole.Block[i], whole.Tint[i]);
        }

        Assert.Equal(s.Cells.Count, got.Count);
        foreach (var kv in s.Cells)
        {
            int tint = s.Mods.TryGetValue(kv.Key, out var m) ? m.Tint : 0;
            Assert.Equal((kv.Value.Value, tint), got[kv.Key]);
        }
    }

    /// <summary>A derived giant station seen by a real client over the encoded loopback: its design arrives in parts, the
    /// assembler joins them, and the client holds exactly the visible shell the server sent — far fewer cells than the hull.</summary>
    [Fact]
    public void AGiantStation_ReachesARealClient_AsItsShellInParts()
    {
        using var h = new ClientServerHarness(Content.Value, c => c.Rules.FreeSpaceFlight = true, passObjects: false);
        h.Join("Pilot");
        Assert.NotNull(h.JoinAccepted);
        string pid = h.JoinAccepted!.PlayerId;
        ushort wall = Content.Value.GetBlock("iron_wall")!.NumericId.Value;
        var cells = GiantCells().ToDictionary(c => c, _ => new BlockId(wall));
        const string id = "derelict:giant-test-0";
        h.Server.RegisterDerivedStationForTest(id, "Test Giant", cells, new Vector3i(54, 1, 2), new Vector3f(0f, 300f, -400f));

        var asm = new SpaceDesignAssembler();
        var parts = new List<SpaceShipDesign>();
        SpaceShipDesign? whole = null;
        h.Client.SpaceShipDesignReceived += m =>
        {
            if (m.Id == id)
            {
                parts.Add(m);
                whole = asm.Add(m) ?? whole;
            }
        };

        h.Server.EnterSpace(pid);
        h.Server.ShowDerivedStationForTest(pid, id);
        Assert.True(h.PumpUntil(() => whole is not null, maxTicks: 40), $"the design arrived whole ({parts.Count} parts so far)");

        Assert.True(parts.Count >= 2, $"{parts.Count} parts");
        Assert.All(parts, p => Assert.Equal(parts[0].Serial, p.Serial));
        Assert.Equal(Enumerable.Range(0, parts.Count), parts.Select(p => p.PartIndex).OrderBy(i => i));
        var held = new HashSet<Vector3i>(Enumerable.Range(0, whole!.Block.Length).Select(i => new Vector3i(whole.X[i], whole.Y[i], whole.Z[i])));
        Assert.True(held.Count < cells.Count / 2, $"the shell holds {held.Count} of {cells.Count} cells");
        Assert.Contains(new Vector3i(0, 20, 30), held); // an outer wall
        Assert.Contains(new Vector3i(60, 0, 60), held); // the bottom
        Assert.DoesNotContain(new Vector3i(30, 20, 30), held); // a deck deep inside
        Assert.Contains(new Vector3i(54, 0, 3), held); // the floor of the room behind the open entrance
    }

    /// <summary>The cells of the synthetic giant: a hollow 120 × 40 × 120 box, a deck every five blocks, room walls every
    /// twelve along X and Z, a 2 × 3 opening in the −Z wall at (54…55, 1…3, 0), a fin on the roof and a mast at a corner.</summary>
    internal static IEnumerable<Vector3i> GiantCells()
    {
        const int w = 120, hgt = 40, l = 120;
        for (int x = 0; x < w; x++)
            for (int y = 0; y < hgt; y++)
                for (int z = 0; z < l; z++)
                {
                    bool shell = x == 0 || x == w - 1 || y == 0 || y == hgt - 1 || z == 0 || z == l - 1;
                    bool inner = y % 5 == 0 || x % 12 == 0 || z % 12 == 0;
                    bool entrance = z == 0 && (x == 54 || x == 55) && y >= 1 && y <= 3;
                    if ((shell || inner) && !entrance)
                    {
                        yield return new Vector3i(x, y, z);
                    }
                }

        for (int y = hgt; y <= hgt + 2; y++)
        {
            yield return new Vector3i(30, y, 30); // the fin
        }

        for (int y = hgt; y <= hgt + 5; y++)
        {
            yield return new Vector3i(100, y, 100); // the mast (the box's top)
        }
    }
}
