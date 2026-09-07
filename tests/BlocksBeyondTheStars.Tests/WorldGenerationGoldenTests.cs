// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;
using Xunit;

namespace BlocksBeyondTheStars.Tests;

/// <summary>
/// Golden checksums for the world generator (#1503). <c>Generation_IsDeterministic_ForSameSeedAndCoord</c> only
/// proves two generators of the SAME build agree; nothing pinned the CURRENT output across versions, so an
/// "output-preserving" optimisation (column caches, noise lattice reuse, tree pre-filters — #1526/#1527) could
/// not prove it kept every saved world's terrain. These tests pin an FNV-1a checksum of the generated blocks
/// (+ colour modifiers + shape descriptors) for a matrix of seeds × planet types × world modes × chunk kinds
/// (surface, deep solid, all-air above the surface) at a few columns. Any single-block change in a pinned chunk
/// fails the test — which is the point: a deliberate terrain change must re-pin the goldens in the same PR
/// (and needs a world-version gate for existing saves); an accidental one must not slip through.
///
/// Goldens are pinned PER OPERATING SYSTEM: terrain uses <c>Math.Cos/Sin</c>, and the Windows and Linux libm
/// may differ in the last ulp, which can flip a floor at a boundary. When the running OS has no pin for a group
/// the Windows value is tried; a mismatch then reports every group's checksum so a single CI run yields all the
/// values to pin. Never derive goldens from trig floats by hand — always from a run.
/// </summary>
public sealed class WorldGenerationGoldenTests
{
    private static GameContent Content() => ContentLoader.LoadFromDirectory(TestPaths.DataDir());

    /// <summary>One pinned group: a generator configuration whose chunks are hashed together in a fixed order.
    /// <see cref="Circumference"/> 0 = the generator's default mode (no <c>SetWorldMode</c> call, the legacy
    /// per-type seeding); otherwise the full per-world mode is applied like <c>ServerWorld.GetOrLoadChunk</c> does.</summary>
    private sealed record Group(string Name, long Seed, string Planet, int Circumference, bool Cratered, string? LocationId,
        int Generation = 0);

    private static readonly Group[] Groups =
    {
        new("varied-default", 424242, "varied", 0, false, null),
        new("rocky-default", 1, "rocky", 0, false, null),
        new("desert-default", 1, "desert", 0, false, null),
        new("ocean-default", 424242, "ocean", 0, false, null),
        new("ice-default", 20260903, "ice", 0, false, null),
        new("jungle-default", 20260903, "jungle", 0, false, null),
        new("varied-world-5472", 424242, "varied", 5472, false, "golden:varied-body"),
        new("asteroid-cratered-800", 14, "asteroid", 800, true, "golden:asteroid"),
        // Terrain generation 1 (#1645, landscape-variety part 2): the gen-0 groups above must never move; these
        // pin the generation-1 relief (style regions, scale jitter, biome relief, new archetypes, regimes).
        new("varied-gen1", 424242, "varied", 5472, false, "golden:varied-body", 1),
        new("desert-gen1", 1, "desert", 0, false, null, 1),
        new("highland-gen1", 20260903, "highland", 0, false, null, 1),
        // #1646 (part 3): landmark families, bands and underground finds on generation-1 worlds.
        new("tundra-gen1", 424242, "tundra", 0, false, null, 1),
        new("rocky-gen1", 1, "rocky", 5472, false, "golden:rocky-body", 1),
        // #1647 (part 4): water / lava bodies and paints on generation-1 worlds.
        new("ocean-gen1", 424242, "ocean", 0, false, null, 1),
        new("jungle-gen1", 20260903, "jungle", 0, false, null, 1),
        // #1648 (part 5): props, micro-ruins, new tree kinds and giant flora on generation-1 worlds.
        new("savanna-gen1", 424242, "savanna", 0, false, null, 1),
        new("swamp-gen1", 20260903, "swamp", 0, false, null, 1),
        // Terrain generation 3, part 1 (the landform completion package): seamounts (sea-relative rows),
        // icebergs (material bands), underground river reaches (sub-surface fluid spans), glacier-tongue fill
        // (paint fill). highland-gen3 activated none of them through part 4 (= highland-gen1); since part 5 the wet alpine
        // world has river morphology and rias, and the no-family control lives in the gate-searching test.
        new("ocean-gen3", 424242, "ocean", 0, false, null, 3),
        new("frozen_ocean-gen3", 20260903, "frozen_ocean", 0, false, null, 3),
        new("karst-gen3", 20260903, "karst", 0, false, null, 3),
        new("highland-gen3", 20260903, "highland", 0, false, null, 3),
        // Part 2 (rock): slot canyons / labyrinth / petrified dunes on the deserts, rainbow strata and the
        // hoodoo-butte families on red_desert, pavement on the dust bowl.
        new("desert-gen3", 1, "desert", 0, false, null, 3),
        new("red_desert-gen3", 1, "red_desert", 0, false, null, 3),
        new("dust_bowl-gen3", 20260903, "dust_bowl", 0, false, null, 3),
        // Part 3 (caves): dripstone in tunnels and caverns, karst cathedrals — karst-gen3 re-pinned, jungle drips too.
        new("jungle-gen3", 20260903, "jungle", 0, false, null, 3),
        // Part 4 (volcanic + desert): lava flows / obsidian fields on the lava world, frost polygons on the tundra;
        // barchans may move the desert groups (re-pinned).
        new("lava-gen3", 20260903, "lava", 0, false, null, 3),
        new("tundra-gen3", 20260903, "tundra", 0, false, null, 3),
        // Part 5 (wetlands + rivers): mats and peat on the swamp, peat and morphology on the boreal world.
        new("swamp-gen3", 20260903, "swamp", 0, false, null, 3),
        new("boreal-gen3", 20260903, "boreal", 0, false, null, 3),
        // Part 6 (coast + sea floor): reef rings, reef fields, causeways, arches, blue holes on the archipelago.
        new("archipelago-gen3", 20260903, "archipelago", 0, false, null, 3),
        // Part 7 (ice): glaciers, ice sheets and nunataks, hanging valleys, ice caves on the two ice worlds.
        new("glacier-gen3", 20260903, "glacier", 0, false, null, 3),
        new("ice-gen3", 20260903, "ice", 0, false, null, 3),
        // Part 8: the three generation-3 planet types, where the new families are dense.
        new("coral_sea-gen3", 20260903, "coral_sea", 0, false, null, 3),
        new("icecap-gen3", 20260903, "icecap", 0, false, null, 3),
        new("river_lowlands-gen3", 20260903, "river_lowlands", 0, false, null, 3),
    };

    /// <summary>Sample columns: the spawn column (pad 0 sits at (0,0) on every world), one ordinary inland
    /// column and one with negative X (the seam-wrap path).</summary>
    private static readonly (int X, int Z)[] Columns = { (0, 0), (100, 37), (-200, 150) };

    /// <summary>Expected combined checksums per OS and group. Pin values from a test run, never by hand.</summary>
    private static readonly Dictionary<string, Dictionary<string, ulong>> Goldens = new()
    {
        // Pinned 2026-09-04 from main @ 75fe9397 (Windows 11, .NET 10). Per-chunk values are in the failure report.
        ["windows"] = new()
        {
            ["varied-default"] = 0x41037a332a1ecbe6UL,
            ["rocky-default"] = 0x018f3fcd29dc3072UL,
            ["desert-default"] = 0xc9aebd668ca714e7UL,
            ["ocean-default"] = 0x161920f6b7dc1f68UL,
            ["ice-default"] = 0xad1d02f2b2814251UL,
            ["jungle-default"] = 0x9f198f0eb8281da4UL,
            ["varied-world-5472"] = 0x2dcf8cfc85d02359UL,
            ["asteroid-cratered-800"] = 0x0ccae399034733eeUL,
            // Pinned 2026-09-05 (#1645, Windows 11, .NET 10).
            ["varied-gen1"] = 0xaba279484571bcb4UL, // re-pinned for #1647 (gen-1 bodies + paints; unreleased)
            ["desert-gen1"] = 0x5e5d6d6f809bb612UL, // re-pinned for #1647
            ["highland-gen1"] = 0x49f4107c54801291UL, // re-pinned for #1646 + #1647
            // Pinned 2026-09-05 (#1646, Windows 11, .NET 10).
            ["tundra-gen1"] = 0x6e7d5380a92cd7f3UL, // re-pinned for #1647
            ["rocky-gen1"] = 0x9981bc39fdae70e5UL, // re-pinned for #1647
            // Pinned 2026-09-05 (#1647, Windows 11, .NET 10).
            ["ocean-gen1"] = 0xcd2223af53fc3766UL,
            ["jungle-gen1"] = 0xc9e7499dcae9f034UL,
            // Pinned 2026-09-06 (#1648, Windows 11, .NET 10).
            ["savanna-gen1"] = 0x1f2b23eba6c17a34UL,
            ["swamp-gen1"] = 0x5c5a1959a7982a19UL,
            // Pinned 2026-09-07 (terrain generation 3 part 1, Windows 11, .NET 10; unreleased — later parts
            // re-pin these). ocean-gen3 equals ocean-gen1 (no seamount under the three sample columns) and
            // highland-gen3 equalled highland-gen1 through part 4 (no family active); part 5 re-pinned it.
            ["ocean-gen3"] = 0xd332372ad8f67b85UL, // re-pinned for part 5: a ria drowns a coast column
            ["frozen_ocean-gen3"] = 0x13b8385bbef612d6UL, // re-pinned for part 7 (glaciers, ice sheet; part 4: frost polygons)
            ["karst-gen3"] = 0xbb494ccb1798e108UL, // re-pinned for part 5 (river morphology; part 2: the stone-forest style)
            ["highland-gen3"] = 0x7f796ec06cb54308UL, // re-pinned for part 5: since then the wet alpine world has river morphology + rias — the no-family control lives in the gate-searching test
            // Pinned 2026-09-07 (part 2, the rock landforms; unreleased — later parts re-pin these).
            ["desert-gen3"] = 0x12d1f68f2b91c7ddUL, // re-pinned for part 5: flora follows the generation-3 paints
            ["red_desert-gen3"] = 0xc9b35bc8e7953302UL, // re-pinned for part 5
            ["dust_bowl-gen3"] = 0xdbd5c91922585dd8UL, // re-pinned for part 5
            // Pinned 2026-09-07 (part 3, the caves; unreleased — later parts re-pin these).
            ["jungle-gen3"] = 0x604c4eaed2ed4148UL, // re-pinned for part 5 (river morphology, flora on paints)
            // Pinned 2026-09-07 (part 4, volcanic + desert; unreleased — later parts re-pin these).
            ["lava-gen3"] = 0xe08b8deab2d122deUL,
            ["tundra-gen3"] = 0xd568581839e0daf1UL, // re-pinned for part 7 (glaciers, ice sheet, hanging valleys)
            // Pinned 2026-09-07 (part 5, wetlands + rivers; unreleased — later parts re-pin these).
            ["swamp-gen3"] = 0x6f5636cb565bf8cfUL,
            ["boreal-gen3"] = 0x0db4acbd71f6b6bdUL,
            // Pinned 2026-09-08 (part 6, coast + sea floor; unreleased — later parts re-pin these).
            ["archipelago-gen3"] = 0x819963dbc81a43bbUL,
            // Pinned 2026-09-08 (part 7, ice; unreleased — later parts re-pin these).
            ["glacier-gen3"] = 0xaaf721d431838ceaUL,
            ["ice-gen3"] = 0x3e04e32f31a07609UL,
            // Pinned 2026-09-08 (part 8, the new planet types).
            ["coral_sea-gen3"] = 0x1fb61378b5bb1fa2UL,
            ["icecap-gen3"] = 0x786cf683677e2526UL,
            ["river_lowlands-gen3"] = 0xee25a930cd9958d9UL,
        },
        // Linux (ubuntu CI runners): filled in from the first CI run of this test; a group absent here falls
        // back to the Windows value above and fails with the value to pin if the libm differs.
        ["linux"] = new()
        {
        },
    };

    [Fact]
    public void GeneratedChunks_MatchThePinnedGoldens()
    {
        var content = Content();
        string os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux" : "other";
        var failures = new List<string>();
        var report = new StringBuilder();

        foreach (var group in Groups)
        {
            var planet = content.GetPlanet(group.Planet);
            Assert.NotNull(planet);
            var gen = new WorldGenerator(group.Seed, content);
            if (group.Circumference > 0)
            {
                gen.SetWorldMode(group.Circumference, group.Cratered, null, group.LocationId);
            }

            if (group.Generation > 0)
            {
                gen.SetTerrainGeneration(group.Generation); // #1645
            }

            var perChunk = new List<string>();
            ulong combined = FnvOffset;
            foreach (var (x, z) in Columns)
            {
                int cx = WorldConstants.WorldToChunk(x), cz = WorldConstants.WorldToChunk(z);
                int surfaceCy = WorldConstants.WorldToChunk(gen.SurfaceHeight(planet!, x, z));
                foreach (int cy in new[] { surfaceCy, surfaceCy - 3, surfaceCy + 2 })
                {
                    var chunk = gen.Generate(planet!, new ChunkCoord(cx, cy, cz));
                    ulong h = HashChunk(chunk);
                    perChunk.Add($"({cx},{cy},{cz})={h:x16}");
                    combined = FnvMix(combined, h);
                }
            }

            report.AppendLine($"  [\"{group.Name}\"] = 0x{combined:x16}UL, // {string.Join(" ", perChunk)}");

            if (Goldens.TryGetValue(os, out var table) && table.TryGetValue(group.Name, out ulong expected))
            {
                if (expected != combined)
                {
                    failures.Add($"{group.Name} ({os}): expected 0x{expected:x16}, got 0x{combined:x16} — chunks {string.Join(" ", perChunk)}");
                }
            }
            else if (Goldens["windows"].TryGetValue(group.Name, out ulong windowsValue))
            {
                if (windowsValue != combined)
                {
                    failures.Add($"{group.Name}: no golden pinned for '{os}' and the Windows value 0x{windowsValue:x16} differs (got 0x{combined:x16}) — pin the '{os}' table");
                }
            }
            else
            {
                failures.Add($"{group.Name}: no golden pinned for '{os}' (got 0x{combined:x16})");
            }
        }

        Assert.True(failures.Count == 0,
            "Worldgen golden mismatch:\n" + string.Join("\n", failures)
            + "\n\nIf the terrain change is deliberate (and gated for existing saves), re-pin this OS table with:\n" + report);
    }

    [Fact]
    public void ChunkHash_CoversBlocksModifiersAndShapes()
    {
        // The checksum must notice a block, a colour modifier and a shape descriptor — otherwise a refactor that
        // drops sparse data would still pass the goldens.
        var a = new ChunkData(new ChunkCoord(0, 0, 0));
        ulong empty = HashChunk(a);

        a.Set(1, 2, 3, new BlockId(7));
        ulong withBlock = HashChunk(a);
        Assert.NotEqual(empty, withBlock);

        a.SetModifier(1, 2, 3, 0x102030, 0);
        ulong withTint = HashChunk(a);
        Assert.NotEqual(withBlock, withTint);

        a.SetShape(1, 2, 3, 5);
        ulong withShape = HashChunk(a);
        Assert.NotEqual(withTint, withShape);

        // And it is a pure function of the content (a fresh identical chunk hashes the same).
        var b = new ChunkData(new ChunkCoord(0, 0, 0));
        b.Set(1, 2, 3, new BlockId(7));
        b.SetModifier(1, 2, 3, 0x102030, 0);
        b.SetShape(1, 2, 3, 5);
        Assert.Equal(withShape, HashChunk(b));
    }

    private const ulong FnvOffset = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;

    private static ulong FnvMix(ulong h, ulong value)
    {
        for (int i = 0; i < 8; i++)
        {
            h ^= (value >> (i * 8)) & 0xFF;
            h *= FnvPrime;
        }

        return h;
    }

    /// <summary>FNV-1a over the dense block ids, then the sparse colour modifiers and shape descriptors in
    /// ascending cell order (dictionary order is not deterministic, so they are sorted first).</summary>
    /// <summary>Per numeric id, the FNV hash of the block's KEY (#1647): numeric ids are assigned alphabetically
    /// at content load, so adding any block shifts the ids of every block sorting after it — hashing raw ids
    /// made every golden move whenever a block was added, although no terrain had changed. Hashing the key
    /// keeps the goldens about the terrain. Ids without a block (synthetic test chunks) hash as themselves.</summary>
    private static readonly ulong[] KeyHashById = BuildKeyHashes();

    private static ulong[] BuildKeyHashes()
    {
        var content = Content();
        ushort max = 0;
        foreach (var b in content.Blocks.Values)
        {
            max = Math.Max(max, b.NumericId.Value);
        }

        var table = new ulong[max + 1];
        foreach (var b in content.Blocks.Values)
        {
            ulong kh = FnvOffset;
            foreach (char c in b.Key)
            {
                kh ^= c;
                kh *= FnvPrime;
            }

            table[b.NumericId.Value] = kh == 0 ? 1UL : kh;
        }

        return table;
    }

    internal static ulong HashChunk(ChunkData chunk)
    {
        ulong h = FnvOffset;
        foreach (ushort id in chunk.RawBlocks)
        {
            ulong v = id < KeyHashById.Length && KeyHashById[id] != 0 ? KeyHashById[id] : id;
            h = FnvMix(h, v);
        }

        if (chunk.Modifiers is { Count: > 0 } mods)
        {
            foreach (var kv in mods.OrderBy(k => k.Key))
            {
                h = FnvMix(h, (ulong)kv.Key);
                h = FnvMix(h, (ulong)(uint)kv.Value.Tint);
                h = FnvMix(h, (ulong)(uint)kv.Value.Glow);
            }
        }

        if (chunk.Shapes is { Count: > 0 } shapes)
        {
            foreach (var kv in shapes.OrderBy(k => k.Key))
            {
                h = FnvMix(h, (ulong)kv.Key);
                h = FnvMix(h, (ulong)(uint)kv.Value);
            }
        }

        return h;
    }
}
