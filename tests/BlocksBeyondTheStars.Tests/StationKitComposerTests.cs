// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>#1874: stations assembled from docking modules — placement, joints, the seal, ladders, replay.</summary>
public sealed class StationKitComposerTests
{
    private static readonly GameContent Content = ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>A hollow 7×5×7 iron room with a 2 × 2 door port centred on each listed wall (y 1..2), a floor marker
    /// and optional extras. Faces: "x+", "x-", "z+", "z-", "y+" (ceiling ladder port), "y-" (floor ladder port).</summary>
    internal static StructureTemplate Room(string key, string function, string kit = "k", string[]? faces = null,
        (string Id, int X, int Y, int Z)[]? markers = null, int w = 7, int h = 5, int l = 7, string portTag = "door", bool rotate = true)
    {
        var t = new StructureTemplate { Key = key, Name = key, Tier = "small", Kind = "station", Kit = kit, Function = function, Width = w, Height = h, Length = l };
        faces ??= new[] { "x+", "x-", "z+", "z-" };
        bool IsPort(int x, int y, int z, out string port)
        {
            port = portTag;
            int cx = w / 2, cz = l / 2;
            foreach (var f in faces)
            {
                switch (f)
                {
                    case "x+": if (x == w - 1 && y >= 1 && y <= 2 && (z == cz - 1 || z == cz)) return true; break;
                    case "x-": if (x == 0 && y >= 1 && y <= 2 && (z == cz - 1 || z == cz)) return true; break;
                    case "z+": if (z == l - 1 && y >= 1 && y <= 2 && (x == cx - 1 || x == cx)) return true; break;
                    case "z-": if (z == 0 && y >= 1 && y <= 2 && (x == cx - 1 || x == cx)) return true; break;
                    case "y+": port = "ladder"; if (y == h - 1 && (x == cx - 1 || x == cx) && (z == cz - 1 || z == cz)) return true; break;
                    case "y-": port = "ladder"; if (y == 0 && (x == cx - 1 || x == cx) && (z == cz - 1 || z == cz)) return true; break;
                }
            }

            port = string.Empty;
            return false;
        }

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                for (int z = 0; z < l; z++)
                {
                    bool shell = x == 0 || y == 0 || z == 0 || x == w - 1 || y == h - 1 || z == l - 1;
                    if (!shell)
                    {
                        continue;
                    }

                    IsPort(x, y, z, out string port);
                    t.Cells.Add(new TemplateCell { X = x, Y = y, Z = z, Kind = "block", Id = y == 0 ? "steel_floor" : "iron_wall", Port = port });
                }

        t.Cells.Add(new TemplateCell { X = 1, Y = 1, Z = 1, Kind = "marker", Id = function == "cabins" ? "cabin" : "room" });
        foreach (var m in markers ?? Array.Empty<(string, int, int, int)>())
        {
            t.Cells.Add(new TemplateCell { X = m.X, Y = m.Y, Z = m.Z, Kind = "marker", Id = m.Id });
        }

        return t;
    }

    private static Dictionary<string, StructureTemplate> Pool(params StructureTemplate[] modules) => modules.ToDictionary(m => m.Key);

    private static Func<string, StructureTemplate?> ByKey(Dictionary<string, StructureTemplate> pool) => key => pool.TryGetValue(key, out var t) ? t : null;

    private static StructureKit Kit(params KitEntry[] entries) => new() { Key = "k", Kind = "station", Tier = "small", Entries = entries.ToList() };

    private static string Signature(StationStructure s)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(s.Width).Append('x').Append(s.Height).Append('x').Append(s.Length).Append('|');
        for (int x = 0; x < s.Width; x++)
            for (int y = 0; y < s.Height; y++)
                for (int z = 0; z < s.Length; z++)
                {
                    ushort b = s.Get(x, y, z);
                    if (b != 0)
                    {
                        sb.Append(x).Append(',').Append(y).Append(',').Append(z).Append(':').Append(b).Append(':').Append(s.GetShape(x, y, z)).Append(';');
                    }
                }

        foreach (var m in s.Markers.OrderBy(m => m.Type, StringComparer.Ordinal).ThenBy(m => m.LocalPos.X).ThenBy(m => m.LocalPos.Y).ThenBy(m => m.LocalPos.Z))
        {
            sb.Append(m.Type).Append('@').Append(m.LocalPos.X).Append(',').Append(m.LocalPos.Y).Append(',').Append(m.LocalPos.Z).Append(';');
        }

        return sb.ToString();
    }

    /// <summary>Air reachable from the spawn through 6-connected air; true when it touches the structure's box.</summary>
    private static bool LeaksFromSpawn(StationStructure s)
    {
        var spawn = s.Markers.First(m => m.Type == "spawn").LocalPos;
        var seen = new HashSet<Vector3i> { spawn };
        var queue = new Queue<Vector3i>();
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            var p = queue.Dequeue();
            if (p.X == 0 || p.Y == 0 || p.Z == 0 || p.X == s.Width - 1 || p.Y == s.Height - 1 || p.Z == s.Length - 1)
            {
                return true;
            }

            foreach (var d in new[] { new Vector3i(1, 0, 0), new Vector3i(-1, 0, 0), new Vector3i(0, 1, 0), new Vector3i(0, -1, 0), new Vector3i(0, 0, 1), new Vector3i(0, 0, -1) })
            {
                var n = p + d;
                if (s.InBounds(n.X, n.Y, n.Z) && s.Get(n.X, n.Y, n.Z) == 0 && seen.Add(n))
                {
                    queue.Enqueue(n);
                }
            }
        }

        return false;
    }

    [Fact]
    public void Compose_PlacesTheRequiredModules_OpensEveryJoint_KeepsTheSeal_AndIsDeterministic()
    {
        var pool = Pool(
            Room("hub", "hub", markers: new[] { ("spawn", 3, 1, 3) }),
            Room("corridor", "corridor", faces: new[] { "x+", "x-" }, w: 5, l: 9),
            Room("cabins", "cabins", faces: new[] { "z-" }),
            Room("canteen", "canteen", faces: new[] { "x-", "z+" }));
        var kit = Kit(
            new KitEntry { Module = "hub", Required = true, Max = 1 },
            new KitEntry { Module = "cabins", Required = true, Min = 2, Max = 3 },
            new KitEntry { Module = "corridor", Min = 1, Max = 3, Weight = 2 },
            new KitEntry { Module = "canteen", Max = 1 });
        kit.Start = "hub";

        var a = StationKitComposer.Compose(kit, ByKey(pool), 4242, Content, out var compA, out var failA);
        var b = StationKitComposer.Compose(kit, ByKey(pool), 4242, Content, out var compB, out _);
        Assert.NotNull(a);
        Assert.Null(failA);
        Assert.Equal(Signature(a!), Signature(b!));
        Assert.Equal(compA.Modules.Select(m => (m.Key, m.X, m.Y, m.Z, m.Turns)), compB.Modules.Select(m => (m.Key, m.X, m.Y, m.Z, m.Turns)));

        // Counts: the hub once, at least two cabin modules, never more than the kit allows.
        Assert.Equal(1, compA.Modules.Count(m => m.Key == "hub"));
        Assert.InRange(compA.Modules.Count(m => m.Key == "cabins"), 2, 3);
        Assert.InRange(compA.Modules.Count, 3, 8);
        Assert.Equal("hub", compA.Modules[0].Key);
        Assert.Equal(new Vector3i(compA.Modules[0].X, compA.Modules[0].Y, compA.Modules[0].Z), a!.Modules[0].Origin); // the pin and the module list agree

        // No two modules overlap (touching walls are fine).
        var boxes = compA.Modules.Select(m => (Min: new Vector3i(m.X, m.Y, m.Z), T: TemplateTransform.RotateY(pool[m.Key], m.Turns))).ToList();
        for (int i = 0; i < boxes.Count; i++)
            for (int j = i + 1; j < boxes.Count; j++)
            {
                var (p, pt) = boxes[i];
                var (q, qt) = boxes[j];
                bool apart = p.X + pt.Width - 1 < q.X || q.X + qt.Width - 1 < p.X
                    || p.Y + pt.Height - 1 < q.Y || q.Y + qt.Height - 1 < p.Y
                    || p.Z + pt.Length - 1 < q.Z || q.Z + qt.Length - 1 < p.Z;
                Assert.True(apart, $"modules {i} and {j} overlap");
            }

        // Every door marker sits in an opened two-deep doorway: its cell and the cell across the joint are air.
        var doors = a!.Markers.Where(m => m.Type.StartsWith("door_", StringComparison.Ordinal)).ToList();
        Assert.True(doors.Count >= compA.Modules.Count - 1, "a tree of N modules has at least N − 1 joints");
        foreach (var d in doors)
        {
            var p = d.LocalPos;
            Assert.Equal(0, a.Get(p.X, p.Y, p.Z));
            bool acrossX = a.Get(p.X + 1, p.Y, p.Z) == 0 && a.InBounds(p.X + 2, p.Y, p.Z) && a.Get(p.X + 2, p.Y, p.Z) == 0
                || a.Get(p.X - 1, p.Y, p.Z) == 0 && a.InBounds(p.X - 2, p.Y, p.Z) && a.Get(p.X - 2, p.Y, p.Z) == 0;
            bool acrossZ = a.Get(p.X, p.Y, p.Z + 1) == 0 && a.InBounds(p.X, p.Y, p.Z + 2) && a.Get(p.X, p.Y, p.Z + 2) == 0
                || a.Get(p.X, p.Y, p.Z - 1) == 0 && a.InBounds(p.X, p.Y, p.Z - 2) && a.Get(p.X, p.Y, p.Z - 2) == 0;
            Assert.True(acrossX || acrossZ, $"door at {p} is not a through passage");
        }

        // The whole station is airtight from the spawn, the lounge marker is emitted for the canteen, every module
        // is listed with its function, and the essentials exist.
        Assert.False(LeaksFromSpawn(a));
        Assert.Contains(a.Markers, m => m.Type == "spawn");
        Assert.Contains(a.Markers, m => m.Type == "vendor");
        Assert.Contains(a.Markers, m => m.Type == "mission_board");
        Assert.Equal(compA.Modules.Count(m => m.Key == "cabins"), a.Markers.Count(m => m.Type == "cabin"));
        if (compA.Modules.Any(m => m.Key == "canteen"))
        {
            Assert.Contains(a.Markers, m => m.Type == "lounge");
        }

        Assert.Equal(compA.Modules.Count, a.Modules.Count);
        Assert.Equal("hub", a.Modules[0].Type);
    }

    [Fact]
    public void UnmatchedPorts_StayWalls_AndAModuleThatCannotDock_IsSkipped_OrFailsTheKitWhenRequired()
    {
        var pool = Pool(
            Room("hub", "hub", faces: new[] { "x+" }, markers: new[] { ("spawn", 3, 1, 3) }),
            Room("wing", "room", faces: new[] { "x-", "z+" }),
            Room("wide", "room", faces: new[] { "x-" }, portTag: "wide"));

        // The wing docks on the hub's +X port; its own +Z port finds no partner and stays a wall.
        var kit = Kit(new KitEntry { Module = "hub", Required = true }, new KitEntry { Module = "wing", Required = true }, new KitEntry { Module = "wide", Max = 2, Weight = 5 });
        kit.Start = "hub";
        var s = StationKitComposer.Compose(kit, ByKey(pool), 7, Content, out var comp, out var failure);
        Assert.NotNull(s);
        Assert.Null(failure);
        Assert.Equal(new[] { "hub", "wing" }, comp.Modules.Select(m => m.Key)); // "wide" never docks (no wide port anywhere) → skipped
        var wing = comp.Modules[1];
        var wingT = TemplateTransform.RotateY(pool["wing"], wing.Turns);
        int wallsLeft = 0;
        foreach (var c in wingT.Cells)
        {
            if (c.Port.Length > 0 && s!.Get(wing.X + c.X, wing.Y + c.Y, wing.Z + c.Z) != 0)
            {
                wallsLeft++;
            }
        }

        Assert.Equal(4, wallsLeft); // one 2 × 2 port (the +Z one) remains sealed
        Assert.False(LeaksFromSpawn(s!));

        // A REQUIRED module that cannot dock fails the whole kit (after every attempt).
        var broken = Kit(new KitEntry { Module = "hub", Required = true }, new KitEntry { Module = "wide", Required = true });
        broken.Start = "hub";
        Assert.Null(StationKitComposer.Compose(broken, ByKey(pool), 7, Content, out _, out var why));
        Assert.Contains("wide", why);

        // A start module missing from the pool fails at once.
        var missing = Kit(new KitEntry { Module = "ghost", Required = true });
        Assert.Null(StationKitComposer.Compose(missing, ByKey(pool), 7, Content, out _, out var why2));
        Assert.Contains("ghost", why2);
    }

    [Fact]
    public void AVerticalJoint_BecomesAShaftWithALadder()
    {
        var pool = Pool(
            Room("hub", "hub", faces: new[] { "y+" }, markers: new[] { ("spawn", 3, 1, 3) }),
            Room("deck", "room", faces: new[] { "y-" }));
        var kit = Kit(new KitEntry { Module = "hub", Required = true }, new KitEntry { Module = "deck", Required = true });
        kit.Start = "hub";
        var s = StationKitComposer.Compose(kit, ByKey(pool), 3, Content, out var comp, out var failure);
        Assert.NotNull(s);
        Assert.Null(failure);
        var hub = comp.Modules[0];
        var deck = comp.Modules.Single(m => m.Key == "deck");
        Assert.Equal(hub.Y + 5, deck.Y); // stacked right on the hub's ceiling (hub height 5 → the deck's floor row)
        ushort ladder = Content.GetBlock("ladder")!.NumericId.Value;
        // The shaft: the hub's ceiling cells and the deck's floor cells over the 2 × 2 port (hub-local x, z = 2..3)
        // are open, a ladder column climbs the port's corner from the hub's floor to the deck's floor row.
        Assert.Equal(0, s!.Get(hub.X + 3, hub.Y + 4, hub.Z + 3));
        Assert.Equal(0, s.Get(hub.X + 3, hub.Y + 5, hub.Z + 3));
        for (int y = hub.Y + 1; y <= deck.Y; y++)
        {
            Assert.Equal(ladder, s.Get(hub.X + 2, y, hub.Z + 2));
        }

        Assert.DoesNotContain(s.Markers, m => m.Type.StartsWith("door_", StringComparison.Ordinal));
        Assert.False(LeaksFromSpawn(s));
    }

    [Fact]
    public void RotateFalse_KeepsTheModuleFacing_AndReplay_BakesTheSameStation()
    {
        var pool = Pool(
            Room("hub", "hub", markers: new[] { ("spawn", 3, 1, 3) }),
            Room("hangar", "hangar", faces: new[] { "z+" }, markers: new[] { ("hangar", 3, 1, 3) }),
            Room("cabins", "cabins", faces: new[] { "x+", "x-" }));
        var kit = Kit(
            new KitEntry { Module = "hub", Required = true },
            new KitEntry { Module = "hangar", Required = true, Rotate = false },
            new KitEntry { Module = "cabins", Min = 1, Max = 2 });
        kit.Start = "hub";

        var s = StationKitComposer.Compose(kit, ByKey(pool), 99, Content, out var comp, out var failure);
        Assert.NotNull(s);
        Assert.Null(failure);
        var hangar = comp.Modules.Single(m => m.Key == "hangar");
        Assert.Equal(0, hangar.Turns);
        Assert.True(hangar.Z < comp.Modules[0].Z, "the hangar's +Z port can only meet the hub's −Z wall: it sits north of the hub");

        var again = StationKitComposer.Replay(comp, ByKey(pool), Content, "small", out var replayFailure);
        Assert.NotNull(again);
        Assert.Null(replayFailure);
        Assert.Equal(Signature(s!), Signature(again!));

        // A pinned module that vanished from the pool is reported, not silently dropped.
        pool.Remove("cabins");
        Assert.Null(StationKitComposer.Replay(comp, ByKey(pool), Content, "small", out var missing));
        Assert.Contains("cabins", missing);
    }

    [Fact]
    public void Rooms_AreFurnishedByFunction_AndACabinKeepsItsAuthoredBed()
    {
        ushort bed = Content.GetBlock("bed")!.NumericId.Value;
        var cabin = Room("cabins", "cabins", faces: new[] { "x-" });
        // The author placed a two-cell bed along the +X wall; the furnisher must not add a second one.
        cabin.Cells.Add(new TemplateCell { X = 5, Y = 1, Z = 2, Kind = "block", Id = "bed", Shape = ShapeCode.Pack(BlockShape.BedHead, 0) });
        cabin.Cells.Add(new TemplateCell { X = 5, Y = 1, Z = 3, Kind = "block", Id = "bed", Shape = ShapeCode.Pack(BlockShape.BedFoot, 0) });
        var pool = Pool(Room("hub", "hub", markers: new[] { ("spawn", 3, 1, 3) }), cabin, Room("canteen", "canteen", faces: new[] { "z-" }));
        var kit = Kit(new KitEntry { Module = "hub", Required = true }, new KitEntry { Module = "cabins", Required = true }, new KitEntry { Module = "canteen", Required = true });
        kit.Start = "hub";
        var s = StationKitComposer.Compose(kit, ByKey(pool), 5, Content, out var comp, out _);
        Assert.NotNull(s);

        var cabinAt = comp.Modules.Single(m => m.Key == "cabins");
        int beds = 0, furniture = 0;
        for (int x = cabinAt.X + 1; x < cabinAt.X + 6; x++)
            for (int z = cabinAt.Z + 1; z < cabinAt.Z + 6; z++)
            {
                ushort b = s!.Get(x, cabinAt.Y + 1, z);
                if (b == bed && ShapeCode.ShapeOf(s.GetShape(x, cabinAt.Y + 1, z)) == (int)BlockShape.BedHead) beds++;
                if (b != 0) furniture++;
            }

        Assert.Equal(1, beds);
        Assert.True(furniture > 2, "the cabin gets a locker / a table / a chair beside its bed");

        // The canteen holds seats for the crew's evening.
        var canteenAt = comp.Modules.Single(m => m.Key == "canteen");
        int seats = 0;
        for (int x = canteenAt.X + 1; x < canteenAt.X + 6; x++)
            for (int z = canteenAt.Z + 1; z < canteenAt.Z + 6; z++)
            {
                if (FurnitureShapes.IsSeat(ShapeCode.ShapeOf(s!.GetShape(x, canteenAt.Y + 1, z)))) seats++;
            }

        Assert.True(seats >= 1, "a canteen has at least one chair");
        Assert.Contains(s!.Markers, m => m.Type == "lounge");
    }
}
