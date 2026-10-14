// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BlocksBeyondTheStars.Networking.Transport;
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
/// Animals and ponds (#2552, #2553). A player reported "a Guardian in the water": it was a flying animal with an
/// eleven-block hover band sitting in the sand floor of a pond, a cave under it. Its band was measured from a reference
/// cell eleven blocks below its body — inside the pond floor — and from there the probe looked through the rock down
/// into the cave; it had got there by spawning at "surface + 4" (the pond floor + 4) or by sagging over kelp, which
/// read as dry ground under the water. The same plants let land animals walk the pond floor under water.
/// Every arena here is built block by block, high above the terrain, so no change to terrain generation can move it.
/// </summary>
public sealed class CreaturePondTests : IDisposable
{
    private const float Hover = 11f;
    private const int ArenaR = 10; // the stone block's half size
    private const int PondR = 6;   // the pond's half size, inside it

    private readonly string _root;
    private readonly GameContent _content;

    public CreaturePondTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bbts_pond_" + Guid.NewGuid().ToString("N"));
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
            // A leftover temp dir is not worth failing a test run over.
        }
    }

    private SvGameServer Started(out SqliteWorldRepository repo)
    {
        repo = new SqliteWorldRepository(new SaveGamePaths(_root, "pond"));
        var config = new ServerConfig
        {
            WorldName = "pond",
            Seed = 4242,
            StartPlanet = "jungle",
            AutoSaveIntervalMinutes = 9999,
            PlaceStarterShip = false,
        };
        var server = new SvGameServer(config, _content, new LoopbackServerTransport(new LoopbackLink()), repo);
        server.Start();
        return server;
    }

    /// <summary>Forces roster slot 0 into a plain, always-awake, solitary species of the wanted shape.</summary>
    private static CreatureSpecies Force(SvGameServer server, CreatureHabitat habitat, int legs, LocomotionStyle style,
        bool wings = false, bool gasSac = false, float hover = 0f)
    {
        var sp = server.SpeciesRoster.First();
        sp.Habitat = habitat;
        sp.Legs = legs;
        sp.LocoStyle = style;
        sp.Temperament = CreatureTemperament.Passive;
        sp.HasWings = wings;
        sp.HasGasSac = gasSac;
        sp.Size = 1f;
        sp.BodyPlan = CreatureBodyPlan.Standard;
        sp.HoverAltitude = hover;
        sp.Activity = CreatureActivity.Cathemeral;
        sp.SocialGroupSize = 1;
        sp.BiomeExclusive = false; // a roster slot that only spawns on snow would reject every spot here
        return sp;
    }

    private static CreatureSpecies ForceAirHoverer(SvGameServer server)
    {
        var sp = Force(server, CreatureHabitat.Air, legs: 2, LocomotionStyle.Drifter, wings: true, gasSac: true, hover: Hover);
        Assert.Equal(MotionClass.Hoverer, CreatureMotion.ClassOf(sp));
        return sp;
    }

    private static int MaxTopY(SvGameServer server, int cx, int cz, int r)
    {
        int max = int.MinValue;
        for (int dx = -r; dx <= r; dx++)
        {
            for (int dz = -r; dz <= r; dz++)
            {
                for (int y = 200; y > -200; y--)
                {
                    if (!server.World.GetBlock(new Vector3i(cx + dx, y, cz + dz)).IsAir)
                    {
                        max = Math.Max(max, y);
                        break;
                    }
                }
            }
        }

        return max;
    }

    /// <summary>The built pond. <see cref="WaterTop"/> is its topmost water cell (the banks are flush with it);
    /// <see cref="SandTop"/> the top of its two-cell sand floor; the cave (when built) is an air pocket of
    /// <see cref="CaveFloor"/>..<see cref="CaveTop"/> under the pond, eight cells of rock below the sand — twelve cells
    /// tall, so from its top the feet probe cannot see its floor (the reported cave was 41–52 under a pond floor of 61–62).</summary>
    private sealed record Pond(int Cx, int Cz, int WaterTop, int SandTop, int CaveFloor, int CaveTop,
        (int X, int Z) Open, (int X, int Z) Kelp, (int X, int Z) Seagrass, (int X, int Z) Coral)
    {
        public bool InsideArena(Vector3f p)
            => Math.Abs(Math.Floor(p.X) - Cx) < ArenaR && Math.Abs(Math.Floor(p.Z) - Cz) < ArenaR;
    }

    /// <summary>Builds a stone block (half size <see cref="ArenaR"/>) high above the terrain with a pond of
    /// <paramref name="depth"/> water cells in its top (half size <see cref="PondR"/>) on two cells of sand. Plants:
    /// a 3×3 kelp bed and a 3×3 seagrass bed (two-cell stalks, open water above) and a coral clump — or, with
    /// <paramref name="plantAll"/>, a two-cell stalk in every pond column except the centre. <paramref name="cave"/>
    /// hollows a cave under the pond; <paramref name="walls"/> rings the block with a wall tall enough to keep a
    /// hoverer's band inside.</summary>
    private Pond BuildPond(SvGameServer server, int cx, int cz, int depth, bool cave, bool plantAll, bool walls)
    {
        var stone = _content.GetBlock("stone")!.NumericId;
        var sand = _content.GetBlock("sand")!.NumericId;
        var water = _content.GetBlock("water")!.NumericId;
        var kelp = _content.GetBlock("flora_kelp")!.NumericId;
        var seagrass = _content.GetBlock("flora_seagrass")!.NumericId;
        var coral = _content.GetBlock("flora_coral")!.NumericId;

        int b = MaxTopY(server, cx, cz, ArenaR + 1) + 8;
        int caveFloor = b + 2, caveTop = b + 13;
        int sandTop = b + 23;
        int waterTop = sandTop + depth;

        for (int dx = -ArenaR; dx <= ArenaR; dx++)
        {
            for (int dz = -ArenaR; dz <= ArenaR; dz++)
            {
                bool pond = Math.Abs(dx) <= PondR && Math.Abs(dz) <= PondR;
                bool hollow = cave && Math.Abs(dx) <= 3 && Math.Abs(dz) <= 3;
                for (int y = b; y <= waterTop; y++)
                {
                    BlockId id = stone;
                    if (hollow && y >= caveFloor && y <= caveTop)
                    {
                        id = BlockId.Air;
                    }
                    else if (pond && y > sandTop)
                    {
                        id = water;
                    }
                    else if (pond && y > sandTop - 2)
                    {
                        id = sand;
                    }

                    server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), id);
                }

                if (walls && (Math.Abs(dx) == ArenaR || Math.Abs(dz) == ArenaR))
                {
                    for (int y = waterTop + 1; y <= waterTop + 20; y++)
                    {
                        server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), stone);
                    }
                }
            }
        }

        void Stalk(int x, int z, BlockId plant)
        {
            server.World.SetBlock(new Vector3i(x, sandTop + 1, z), plant);
            server.World.SetBlock(new Vector3i(x, sandTop + 2, z), plant);
        }

        if (plantAll)
        {
            for (int dx = -PondR; dx <= PondR; dx++)
            {
                for (int dz = -PondR; dz <= PondR; dz++)
                {
                    if (dx != 0 || dz != 0)
                    {
                        Stalk(cx + dx, cz + dz, ((dx + dz) & 1) == 0 ? kelp : seagrass);
                    }
                }
            }
        }
        else
        {
            for (int d = -1; d <= 1; d++)
            {
                for (int k = 1; k <= 3; k++)
                {
                    Stalk(cx + k, cz + d, kelp);
                    Stalk(cx - k, cz + d, seagrass);
                }
            }

            server.World.SetBlock(new Vector3i(cx, sandTop + 1, cz + 3), coral);
        }

        return new Pond(cx, cz, waterTop, sandTop, caveFloor, caveTop,
            Open: (cx, cz), Kelp: (cx + 2, cz), Seagrass: (cx - 2, cz), Coral: (cx, cz + 3));
    }

    private static void Watcher(SvGameServer server, Pond pond)
    {
        // Creatures only move while a player is on the ground near them: stand one on the bank.
        var p = server.AddLocalPlayer("Watcher");
        p.State.AboardShip = false;
        p.State.Position = new Vector3f(pond.Cx - 8.5f, pond.WaterTop + 1, pond.Cz + 0.5f);
    }

    // ---------------- #2552: the band of a flying animal over a pond ----------------

    /// <summary>The probe itself, column by column: whatever the reference cell under the band — the air over the pond,
    /// the water, the kelp, seagrass or coral on the floor, the sand, the rock between the pond and the cave — the band
    /// is measured from the water's surface. Measured on the reported pond, a reference in the floor answered the cave
    /// floor (and over the plants the sand), which is how the animal ended up in the pond floor. The cave keeps its own
    /// rules: a reference inside it, or in the rock under it, rests on the cave floor.</summary>
    [Fact]
    public void RestSurface_OverAPlantedPondWithACaveBelow_IsTheWaterSurface()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var pond = BuildPond(server, 3000, 3000, depth: 4, cave: true, plantAll: false, walls: false);
            foreach (var (x, z) in new[] { pond.Open, pond.Kelp, pond.Seagrass, pond.Coral })
            {
                for (int refY = pond.CaveTop + 1; refY <= pond.WaterTop + 6; refY++)
                {
                    int rest = server.RestSurfaceYForTest(x, z, refY);
                    Assert.True(rest == pond.WaterTop + 1,
                        $"column ({x - pond.Cx},{z - pond.Cz}), reference {refY - pond.WaterTop} from the water's top: rest {rest - pond.WaterTop}, expected 1");
                }
            }

            Assert.Equal(pond.CaveFloor, server.RestSurfaceYForTest(pond.Open.X, pond.Open.Z, pond.CaveFloor + 2));
            Assert.Equal(pond.CaveFloor, server.RestSurfaceYForTest(pond.Open.X, pond.Open.Z, pond.CaveFloor - 1));
        }
    }

    /// <summary>A dozen hoverers at their band over the pond, roaming over kelp, seagrass, coral and open water for a
    /// minute and a half — plus the reported one, planted in the pond's sand floor (its band reference then lies in the
    /// cave), and one planted in the water. No hoverer is ever below the water's surface over the pond, and the two
    /// planted ones are out of the water within fifteen seconds and stay out.</summary>
    [Fact]
    public void AnAirHoverer_OverAPlantedPondWithACaveBelow_NeverSinksIntoIt()
    {
        var server = Started(out var repo);
        using (repo)
        {
            ForceAirHoverer(server);
            var pond = BuildPond(server, 3200, 3200, depth: 4, cave: true, plantAll: false, walls: true);
            Watcher(server, pond);

            var roamers = new List<string>();
            for (int dx = -5; dx <= 5; dx += 2)
            {
                for (int dz = -4; dz <= 4; dz += 8)
                {
                    roamers.Add(server.SpawnCreatureAtForTest(new Vector3f(pond.Cx + dx + 0.5f, pond.WaterTop + 1 + Hover, pond.Cz + dz + 0.5f)));
                }
            }

            foreach (var (x, z) in new[] { pond.Kelp, pond.Seagrass, pond.Coral })
            {
                roamers.Add(server.SpawnCreatureAtForTest(new Vector3f(x + 0.5f, pond.WaterTop + 1 + Hover, z + 0.5f)));
            }

            string inFloor = server.SpawnCreatureAtForTest(new Vector3f(pond.Cx + 0.5f, pond.SandTop + 0.3f, pond.Cz + 0.5f));
            string inWater = server.SpawnCreatureAtForTest(new Vector3f(pond.Cx - 4.5f, pond.SandTop + 2.5f, pond.Cz + 4.5f));

            int samples = 0;
            for (int i = 0; i < 900; i++)
            {
                server.TickForTest(0.1);
                foreach (var c in server.Creatures.Where(c => pond.InsideArena(c.Position)))
                {
                    bool planted = c.Id == inFloor || c.Id == inWater;
                    if (!planted && !roamers.Contains(c.Id))
                    {
                        continue;
                    }

                    if (planted && i < 150)
                    {
                        continue; // 15 s to come up out of the floor and the water
                    }

                    samples++;
                    Assert.True(c.Position.Y >= pond.WaterTop + 1,
                        $"{(planted ? "a planted" : "a roaming")} hoverer is under the water at tick {i}: Y {c.Position.Y - pond.WaterTop:F2} from the water's top, " +
                        $"column ({Math.Floor(c.Position.X) - pond.Cx},{Math.Floor(c.Position.Z) - pond.Cz})");
                }
            }

            Assert.True(samples > 5000, $"the hoverers spent the run over the arena ({samples} samples)");
            Assert.Contains(server.Creatures, c => c.Id == inFloor);
            Assert.Contains(server.Creatures, c => c.Id == inWater);
        }
    }

    /// <summary>The spawner placed air animals at "surface + 4" — over a pond, the pond floor + 4: in the water. They
    /// start at their own band now, over the water's surface whatever grows on the floor, and stay above it.</summary>
    [Fact]
    public void AnAirAnimal_SpawnsAtItsBandOverThePond_AndStaysAboveTheWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = ForceAirHoverer(server);
            var pond = BuildPond(server, 3400, 3400, depth: 4, cave: true, plantAll: false, walls: true);
            Watcher(server, pond);

            var ids = new List<string>();
            foreach (var (x, z) in new[] { pond.Open, pond.Kelp, pond.Seagrass, pond.Coral })
            {
                // Over a generated pond the spawner's surface is the generator's — the top of the pond floor.
                float y = server.AirSpawnYForTest(sp.Id, x, z, pond.SandTop);
                Assert.Equal(pond.WaterTop + 1 + Hover, y, 3);
                var at = new Vector3f(x + 0.5f, y, z + 0.5f);
                Assert.True(server.SpawnSpotClearForTest(at), $"the band over ({x - pond.Cx},{z - pond.Cz}) is a spawn spot");
                ids.Add(server.SpawnCreatureAtForTest(at));
            }

            Assert.Equal(pond.WaterTop + 1 + Hover, server.AirSpawnYForTest(sp.Id, pond.Cx - 8, pond.Cz, pond.WaterTop), 3);

            for (int i = 0; i < 300; i++)
            {
                server.TickForTest(0.1);
                foreach (var c in server.Creatures.Where(c => ids.Contains(c.Id) && pond.InsideArena(c.Position)))
                {
                    Assert.True(c.Position.Y >= pond.WaterTop + 1,
                        $"a hoverer spawned over the pond sank to Y {c.Position.Y - pond.WaterTop:F2} from the water's top at tick {i}");
                }
            }
        }
    }

    /// <summary>The guard of #1711 still holds for water: a pocket of water sealed in the rock, or a flooded cave with a
    /// roof over its air, is not a pond open to the sky — a reference under it is not lifted onto its water. In the
    /// sealed pocket the through-rock search (#1854) answers, as it did, with the top of the block.</summary>
    [Fact]
    public void RestSurface_UnderAFloodedCaveWithARoof_IsNotLiftedOntoItsWater()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var stone = _content.GetBlock("stone")!.NumericId;
            var water = _content.GetBlock("water")!.NumericId;

            // A sealed pocket of water in a stone block floating above the terrain.
            const int cx = 3600, cz = 3600;
            int b = MaxTopY(server, cx, cz, 3) + 8;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    for (int y = b; y <= b + 20; y++)
                    {
                        bool pocket = Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1 && y >= b + 8 && y <= b + 10;
                        server.World.SetBlock(new Vector3i(cx + dx, y, cz + dz), pocket ? water : stone);
                    }
                }
            }

            Assert.Equal(b + 21, server.RestSurfaceYForTest(cx, cz, b + 7));

            // A flooded cave in the ground — three cells of water, three of air, a thick roof — under the real surface.
            const int x = 3800, z = 3800;
            int surface = server.SurfaceHeightForTest(x, z);
            server.World.GetBlock(new Vector3i(x, surface, z)); // stream the column in
            int caveFloor = surface - 20;
            for (int y = caveFloor - 3; y <= surface; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), stone);
            }

            for (int y = caveFloor; y < caveFloor + 6; y++)
            {
                server.World.SetBlock(new Vector3i(x, y, z), y < caveFloor + 3 ? water : BlockId.Air);
            }

            int rest = server.RestSurfaceYForTest(x, z, caveFloor - 1);
            Assert.NotEqual(caveFloor + 3, rest);
        }
    }

    // ---------------- #2553: land animals and planted pond floors ----------------

    /// <summary>A pond floor planted with kelp and seagrass is water three deep — the stalks and the water above them —
    /// and the walker on the bank does not step in, exactly as at open water. An amphibian still swims in.</summary>
    [Fact]
    public void ALandWalker_IsStoppedAtAPlantedPond_AnAmphibianIsNot()
    {
        var server = Started(out var repo);
        using (repo)
        {
            var sp = Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider);
            var pond = BuildPond(server, 4000, 4000, depth: 3, cave: false, plantAll: true, walls: false);
            int bankFeet = pond.WaterTop + 1;
            int edgeX = pond.Cx - PondR; // the pond's west column, planted

            Assert.Equal(3, server.WaterDepthAtFeetForTest(edgeX, pond.Cz, pond.SandTop + 1));
            Assert.Equal(3, server.WaterDepthAtFeetForTest(edgeX, pond.Cz + 1, pond.SandTop + 1));
            Assert.Equal(0, server.WaterDepthAtFeetForTest(edgeX - 1, pond.Cz, bankFeet));

            string id = server.SpawnCreatureAtForTest(new Vector3f(edgeX - 0.5f, bankFeet, pond.Cz + 0.5f));
            Assert.True(server.TerrainStepBlockedForTest(id, new Vector3f(edgeX + 0.5f, bankFeet, pond.Cz + 0.5f)),
                "a kelp column over a pond floor is three cells of water to a land walker");
            Assert.True(server.TerrainStepBlockedForTest(id, new Vector3f(edgeX + 0.5f, bankFeet, pond.Cz + 1.5f)),
                "so is a seagrass column");
            Assert.False(server.TerrainStepBlockedForTest(id, new Vector3f(edgeX - 0.5f, bankFeet, pond.Cz + 1.5f)),
                "along the bank it walks on");

            sp.Habitat = CreatureHabitat.Amphibian;
            Assert.False(server.TerrainStepBlockedForTest(id, new Vector3f(edgeX + 0.5f, bankFeet, pond.Cz + 0.5f)),
                "an amphibian swims into the planted pond as before");
        }
    }

    /// <summary>Walkers roaming the banks of a planted pond for two minutes never end up on its floor — on the
    /// reported pond a walker went in over the plants and spent 887 of 900 samples under water.</summary>
    [Fact]
    public void LandWalkers_RoamingAroundAPlantedPond_NeverWalkItsFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider);
            var pond = BuildPond(server, 4200, 4200, depth: 3, cave: false, plantAll: true, walls: false);
            Watcher(server, pond);

            int edge = PondR + 1; // the first bank column
            var ids = new List<string>();
            foreach (var (dx, dz) in new[] { (-edge, -3), (-edge, 3), (edge, 0), (0, edge), (0, -edge), (edge, edge) })
            {
                ids.Add(server.SpawnCreatureAtForTest(new Vector3f(pond.Cx + dx + 0.5f, pond.WaterTop + 1, pond.Cz + dz + 0.5f)));
            }

            for (int i = 0; i < 1200; i++)
            {
                server.TickForTest(0.1);
                foreach (var c in server.Creatures.Where(c => ids.Contains(c.Id)))
                {
                    Assert.True(c.Position.Y >= pond.WaterTop + 1 - 1e-3f,
                        $"a land walker is under water at tick {i}: Y {c.Position.Y - pond.WaterTop:F2} from the water's top, " +
                        $"column ({Math.Floor(c.Position.X) - pond.Cx},{Math.Floor(c.Position.Z) - pond.Cz})");
                }
            }

            Assert.Equal(ids.Count, server.Creatures.Count(c => ids.Contains(c.Id)));
        }
    }

    /// <summary>The land spawn over water fell back to the generator surface — the pond floor — and nothing checked
    /// for water: land animals appeared on pond beds. A spot with water on its feet is rejected now, planted or open;
    /// the bank beside it is not.</summary>
    [Fact]
    public void NoLandAnimal_SpawnsOnAPondFloor()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Land, legs: 4, LocomotionStyle.Strider);
            var pond = BuildPond(server, 4400, 4400, depth: 3, cave: false, plantAll: true, walls: false);
            int bed = pond.SandTop + 1;

            Assert.False(server.SpawnSpotClearForTest(new Vector3f(pond.Cx + 0.5f, bed, pond.Cz + 0.5f)), "open water on the pond floor");
            Assert.False(server.SpawnSpotClearForTest(new Vector3f(pond.Cx + 2.5f, bed, pond.Cz + 0.5f)), "a kelp bed on the pond floor");
            Assert.False(server.SpawnSpotClearForTest(new Vector3f(pond.Cx + 2.5f, bed, pond.Cz + 1.5f)), "a seagrass bed on the pond floor");
            Assert.True(server.SpawnSpotClearForTest(new Vector3f(pond.Cx - PondR - 1.5f, pond.WaterTop + 1, pond.Cz + 0.5f)), "the dry bank");
        }
    }

    /// <summary>A side effect of the stalks counting as water, pinned here: a fish no longer treats a kelp bed as dry
    /// ground at the edge of its water (it used to turn back from every stalk) — it swims in among the plants.</summary>
    [Fact]
    public void AFish_SwimsInAmongTheKelp()
    {
        var server = Started(out var repo);
        using (repo)
        {
            Force(server, CreatureHabitat.Water, legs: 0, LocomotionStyle.Schooler);
            var pond = BuildPond(server, 4600, 4600, depth: 4, cave: false, plantAll: false, walls: false);
            float midWater = pond.SandTop + 2.5f;
            string id = server.SpawnCreatureAtForTest(new Vector3f(pond.Cx + 0.5f, midWater, pond.Cz + 0.5f));

            Assert.False(server.TerrainStepBlockedForTest(id, new Vector3f(pond.Cx + 1.5f, midWater, pond.Cz + 0.5f)),
                "the kelp bed is part of the pond");
            Assert.True(server.TerrainStepBlockedForTest(id, new Vector3f(pond.Cx - PondR - 0.5f, midWater, pond.Cz + 0.5f)),
                "the bank is still the edge of the water");
        }
    }
}
