// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BlocksBeyondTheStars.Client;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Golden checksums of the chunk mesher's output (#1528): a deterministic synthetic world — terrain, caves,
    /// water and lava, glass, foliage, cross-plants, solid flora, props, ladders, hull plates, light blocks,
    /// shaped blocks — is meshed with the real atlas for three stacked chunks (deep, surface, sky), and every
    /// output list plus the packed GPU vertex stream is hashed. The pinned values come from the mesher as it
    /// was before the #1528 refactor (fixed neighbourhood, trait table, dense scratch), so any later change to
    /// the mesher must either reproduce them bit for bit or consciously re-pin — the same contract the world-gen
    /// goldens (#1503) give the generator.
    /// </summary>
    public sealed class ChunkMesherGoldenEditModeTests
    {
        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        // Pinned 2026-09-04 from the pre-#1528 mesher (Windows, Unity 6000.4.9f1). 0 = not pinned yet: the test
        // then fails with the value to paste here.
        private static readonly Dictionary<string, ulong> Pinned = new Dictionary<string, ulong>
        {
            ["deep (5,1,7)"] = 0xf9a25fe3c1b17825UL,
            ["surface (5,2,7)"] = 0xeeb72bd29d8aa44fUL,
            ["sky (5,3,7)"] = 0xd6a8cf1a27eab505UL,
        };

        private static GameContent LoadContentOrIgnore()
        {
            string dataDir = Path.Combine(Application.streamingAssetsPath, "data");
            if (!File.Exists(Path.Combine(dataDir, "blocks.json")))
            {
                Assert.Ignore("StreamingAssets/data not present — run scripts/sync-client-libs.ps1 first.");
            }

            return ContentLoader.LoadFromDirectory(dataDir);
        }

        private static ushort IdOf(GameContent content, string key)
        {
            var def = content.GetBlock(key);
            Assert.IsNotNull(def, $"Block '{key}' missing from content.");
            return def.NumericId.Value;
        }

        /// <summary>A small deterministic integer hash (the synthetic world's "noise").</summary>
        private static uint H(int x, int y, int z, int salt)
        {
            unchecked
            {
                uint h = 2166136261u ^ (uint)salt;
                h = (h ^ (uint)x) * 16777619u;
                h = (h ^ (uint)y) * 16777619u;
                h = (h ^ (uint)z) * 16777619u;
                h ^= h >> 13;
                h *= 0x5BD1E995u;
                h ^= h >> 15;
                return h;
            }
        }

        private static int SurfaceY(int x, int z)
            => 36 + (int)(H(x >> 2, 0, z >> 2, 1) % 7) + (int)(H(x, 0, z, 2) % 2); // gentle steps, a little roughness

        /// <summary>The synthetic world: one block per cell, purely a function of the world position.</summary>
        private sealed class SyntheticWorld
        {
            private readonly ushort _stone, _dirt, _grass, _sand, _snow, _basalt, _ironWall, _water, _lava, _glass, _glassClear;
            private readonly ushort _leaves, _log, _fern, _puffball, _torch, _lantern, _ladder, _lightWhite, _flowerPot, _forceField;
            public readonly Dictionary<ChunkCoord, ChunkData> Chunks = new Dictionary<ChunkCoord, ChunkData>();

            public SyntheticWorld(GameContent content)
            {
                _stone = IdOf(content, "stone");
                _dirt = IdOf(content, "dirt");
                _grass = IdOf(content, "grass");
                _sand = IdOf(content, "sand");
                _snow = IdOf(content, "snow");
                _basalt = IdOf(content, "basalt");
                _ironWall = IdOf(content, "iron_wall");
                _water = IdOf(content, "water");
                _lava = IdOf(content, "lava");
                _glass = IdOf(content, "glass");
                _glassClear = IdOf(content, "glass_clear");
                _leaves = IdOf(content, "tree_leaves");
                _log = IdOf(content, "wood_log");
                _fern = IdOf(content, "flora_fern");
                _puffball = IdOf(content, "flora_puffball");
                _torch = IdOf(content, "torch");
                _lantern = IdOf(content, "lantern");
                _ladder = IdOf(content, "ladder");
                _lightWhite = IdOf(content, "light_white");
                _flowerPot = IdOf(content, "flower_pot");
                _forceField = IdOf(content, "force_field");
            }

            public ushort BlockAt(int x, int y, int z)
            {
                int sy = SurfaceY(x, z);
                if (y > sy)
                {
                    if (y == sy + 1)
                    {
                        uint r = H(x, y, z, 3) % 61;
                        if (r == 0) return _fern;
                        if (r == 1) return _puffball;
                        if (r == 2) return _torch;
                        if (r == 3) return _lantern;
                        if (r == 4) return _flowerPot;
                        if (r == 5) return _lightWhite;
                        if (r == 6 && sy < 39) return _water; // a puddle
                    }

                    // a few tree crowns + trunks
                    if (H(x >> 3, 0, z >> 3, 4) % 5 == 0)
                    {
                        int tx = (x & ~7) + 4, tz = (z & ~7) + 4, ty = SurfaceY(tx, tz);
                        if (x == tx && z == tz && y <= ty + 5) return _log;
                        int dx = x - tx, dy = y - (ty + 5), dz = z - tz;
                        if (dx * dx + dy * dy + dz * dz <= 5) return _leaves;
                    }

                    // a glass pane + a force field + a ladder against the tree line
                    if (x == 88 && y <= sy + 3 && z >= 118 && z <= 122) return (z & 1) == 0 ? _glass : _glassClear;
                    if (x == 91 && y <= sy + 2 && z == 120) return _forceField;
                    if (x == 86 && y <= sy + 4 && z == 116) return _ladder;
                    return 0;
                }

                if (y == sy)
                {
                    uint r = H(x, 0, z, 5) % 11;
                    return r < 6 ? _grass : r < 8 ? _dirt : r == 8 ? _sand : r == 9 ? _snow : _basalt;
                }

                if (y >= sy - 3)
                {
                    return H(x, y, z, 6) % 9 == 0 ? _ironWall : _dirt;
                }

                // caves with water and lava pockets, and a glass window in the rock
                uint c = H(x >> 1, y >> 1, z >> 1, 7) % 23;
                if (c == 0) return 0;
                if (c == 1) return y < 20 ? _lava : _water;
                if (c == 2) return _glass;
                return _stone;
            }

            public int ShapeAt(int x, int y, int z)
            {
                // shaped blocks only on solid rock/dirt cells, one in fourteen
                ushort b = BlockAt(x, y, z);
                if (b == 0 || b == _water || b == _lava || H(x, y, z, 8) % 14 != 0)
                {
                    return 0;
                }

                int shape = 1 + (int)(H(x, y, z, 9) % 18); // Slab .. Pot
                return ShapeCode.Pack(shape, (int)(H(x, y, z, 10) % 4));
            }

            public ChunkData ChunkAt(ChunkCoord coord)
            {
                if (Chunks.TryGetValue(coord, out var chunk))
                {
                    return chunk;
                }

                var origin = WorldConstants.ChunkOrigin(coord);
                chunk = new ChunkData(coord);
                for (int x = 0; x < WorldConstants.ChunkSize; x++)
                    for (int y = 0; y < WorldConstants.ChunkSize; y++)
                        for (int z = 0; z < WorldConstants.ChunkSize; z++)
                        {
                            chunk.Set(x, y, z, new BlockId(BlockAt(origin.X + x, origin.Y + y, origin.Z + z)));
                        }

                Chunks[coord] = chunk;
                return chunk;
            }
        }

        private static ulong Mix(ulong h, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                h ^= (value >> (i * 8)) & 0xFF;
                h *= FnvPrime;
            }

            return h;
        }

        private static ulong MixF(ulong h, float f) => Mix(h, (ulong)(uint)System.BitConverter.SingleToInt32Bits(f));

        private static ulong HashData(ChunkMeshData d)
        {
            ulong h = FnvOffset;
            foreach (var v in d.Verts) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); }
            foreach (int i in d.OpaqueTris) h = Mix(h, (ulong)(uint)i);
            foreach (int i in d.TransparentTris) h = Mix(h, (ulong)(uint)i);
            foreach (int i in d.PaintTris) h = Mix(h, (ulong)(uint)i);
            foreach (int i in d.ColliderTris) h = Mix(h, (ulong)(uint)i);
            foreach (var c in d.Colors) { h = MixF(h, c.r); h = MixF(h, c.g); h = MixF(h, c.b); h = MixF(h, c.a); }
            foreach (var u in d.Uvs) { h = MixF(h, u.x); h = MixF(h, u.y); }
            foreach (var u in d.SkyUv) { h = MixF(h, u.x); h = MixF(h, u.y); }
            foreach (var u in d.LeafUv) { h = MixF(h, u.x); h = MixF(h, u.y); h = MixF(h, u.z); h = MixF(h, u.w); }
            foreach (var v in d.BlockLight) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); }
            foreach (var v in d.BlockLightDir) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); }
            foreach (var t in d.Tangents) { h = MixF(h, t.x); h = MixF(h, t.y); h = MixF(h, t.z); h = MixF(h, t.w); }
            foreach (var n in d.Normals) { h = MixF(h, n.x); h = MixF(h, n.y); h = MixF(h, n.z); }
            foreach (var v in d.ColliderVerts) { h = MixF(h, v.x); h = MixF(h, v.y); h = MixF(h, v.z); }
            foreach (var s in d.Scatter) { h = MixF(h, s.x); h = MixF(h, s.y); h = MixF(h, s.z); h = MixF(h, s.w); }
            h = MixF(h, d.Bounds.min.x); h = MixF(h, d.Bounds.min.y); h = MixF(h, d.Bounds.min.z);
            h = MixF(h, d.Bounds.max.x); h = MixF(h, d.Bounds.max.y); h = MixF(h, d.Bounds.max.z);
            h = MixF(h, d.ColliderBounds.min.x); h = MixF(h, d.ColliderBounds.min.y); h = MixF(h, d.ColliderBounds.min.z);
            h = MixF(h, d.ColliderBounds.max.x); h = MixF(h, d.ColliderBounds.max.y); h = MixF(h, d.ColliderBounds.max.z);
            var packed = MemoryMarshal.AsBytes(new System.ReadOnlySpan<ChunkMeshData.PackedVertex>(d.Packed, 0, d.PackedCount));
            foreach (byte b in packed) { h ^= b; h *= FnvPrime; }
            h = Mix(h, (ulong)(uint)d.PackedCount);
            return h;
        }

        [Test]
        public void SyntheticWorld_MeshesToThePinnedChecksums()
        {
            var content = LoadContentOrIgnore();
            LogAssert.ignoreFailingMessages = true; // BlockTextureAtlas' per-tile Object.Destroy logs an error outside play mode
            var world = new SyntheticWorld(content);
            var atlas = new BlockTextureAtlas(content);
            var failures = new List<string>();
            var report = new List<string>();
            try
            {
                var center = new ChunkCoord(5, 2, 7);
                // the mesher's neighbourhood (GameBootstrap snapshots dx/dz ∈ [-2,2], dy ∈ [-2,4] around the chunk)
                for (int dx = -2; dx <= 2; dx++)
                    for (int dy = -3; dy <= 5; dy++)
                        for (int dz = -2; dz <= 2; dz++)
                        {
                            world.ChunkAt(new ChunkCoord(center.X + dx, center.Y + dy, center.Z + dz));
                        }

                BlockId World(int x, int y, int z)
                    => world.Chunks.TryGetValue(WorldConstants.WorldToChunk(new Vector3i(x, y, z)), out var ch)
                        ? ch.Get(((x % 16) + 16) % 16, ((y % 16) + 16) % 16, ((z % 16) + 16) % 16)
                        : BlockId.Air;
                bool Loaded(int x, int y, int z) => world.Chunks.ContainsKey(WorldConstants.WorldToChunk(new Vector3i(x, y, z)));
                int Shape(int x, int y, int z) => world.ShapeAt(x, y, z);
                Color FloraTint(BlockId id) => new Color(0.2f + (id.Value % 7) * 0.1f, 0.6f, 0.1f + (id.Value % 3) * 0.2f);

                var lights = new List<(Vector3i Pos, int Rgb)>
                {
                    (new Vector3i(82, 40, 114), 0xFFFFFF),
                    (new Vector3i(90, 30, 118), 0xFF4020),
                    (new Vector3i(75, 45, 125), 0x2080FF),
                };

                foreach (var (name, coord) in new[] { ("deep (5,1,7)", new ChunkCoord(5, 1, 7)), ("surface (5,2,7)", center), ("sky (5,3,7)", new ChunkCoord(5, 3, 7)) })
                {
                    var data = ChunkMesher.BuildGeometry(world.ChunkAt(coord), content, World, atlas, FloraTint, null, lights, Shape, null, Loaded);
                    try
                    {
                        ulong actual = HashData(data);
                        report.Add($"{name}: 0x{actual:x16} ({data.Verts.Count} verts, {data.OpaqueTris.Count / 3} opaque + {data.TransparentTris.Count / 3} transparent tris, {data.ColliderTris.Count / 3} collider tris)");
                        ulong pinned = Pinned[name];
                        if (pinned != actual)
                        {
                            failures.Add($"{name}: pinned 0x{pinned:x16}, actual 0x{actual:x16}");
                        }
                    }
                    finally
                    {
                        data.Release();
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(atlas.NormalTexture); // EditMode: Object.Destroy logs an error outside play mode
                Object.DestroyImmediate(atlas.Texture);
                LogAssert.ignoreFailingMessages = false;
            }

            TestContext.WriteLine(string.Join("\n", report));
            Assert.IsEmpty(failures, "Mesher output changed:\n" + string.Join("\n", failures) + "\n\n" + string.Join("\n", report));
        }
    }
}
