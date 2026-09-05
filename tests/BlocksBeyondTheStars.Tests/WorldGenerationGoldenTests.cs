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
            ["varied-default"] = 0x8cbecc0ad775fbd8UL,
            ["rocky-default"] = 0x6a2db75f8a72597cUL,
            ["desert-default"] = 0x2d0f68b62a055e40UL,
            ["ocean-default"] = 0xcd5b96126e2b095fUL,
            ["ice-default"] = 0xca83725ec07d4f02UL,
            ["jungle-default"] = 0xfafe0246f460230cUL,
            ["varied-world-5472"] = 0xd184d06aee4c17feUL,
            ["asteroid-cratered-800"] = 0xea45216efb76ba71UL,
            // Pinned 2026-09-05 (#1645, Windows 11, .NET 10).
            ["varied-gen1"] = 0x06b7238c0bfd64a9UL,
            ["desert-gen1"] = 0x3a9c31b0e73aca33UL,
            ["highland-gen1"] = 0x350b70a0088675a2UL,
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
    internal static ulong HashChunk(ChunkData chunk)
    {
        ulong h = FnvOffset;
        foreach (ushort id in chunk.RawBlocks)
        {
            h ^= (ulong)(id & 0xFF);
            h *= FnvPrime;
            h ^= (ulong)(id >> 8);
            h *= FnvPrime;
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
