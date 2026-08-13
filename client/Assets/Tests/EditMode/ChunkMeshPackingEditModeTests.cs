// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Covers the compact chunk-mesh vertex format (#966): the quantisers that turn the build lists into
    /// <see cref="ChunkMeshData.PackedVertex"/>, and the upload that hands them to the GPU as a non-readable
    /// mesh. Both are pure memory optimisations, so what has to hold is that nothing about the mesh's SHAPE
    /// changes — same vertex count, same submesh split, same index order, positions and atlas UVs bit-identical
    /// — and that the mesh really did drop its system-RAM copy.
    /// </summary>
    public sealed class ChunkMeshPackingEditModeTests
    {
        private static float Unpack(sbyte v) => v / 127f;

        private static float Unpack(byte v) => v / 255f;

        /// <summary>A minimal hand-built mesh data instance with distinctive per-channel values, so a swapped
        /// or dropped channel shows up rather than reading as "some plausible number".</summary>
        private static ChunkMeshData MakeData(int verts)
        {
            var data = ChunkMeshData.Rent();
            for (int i = 0; i < verts; i++)
            {
                data.Verts.Add(new Vector3(i * 0.5f, i * 0.25f, -i));
                data.Normals.Add(new Vector3(0f, 1f, 0f));
                data.Tangents.Add(new Vector4(1f, 0f, 0f, -1f));
                data.Colors.Add(new Color(0.2f, 0.4f, 0.6f, 0.8f));
                data.Uvs.Add(new Vector2(0.123456f, 0.987654f));
                data.SkyUv.Add(new Vector2(0.5f, 4f));
                data.LeafUv.Add(new Vector4(1f, 0.25f, 0.5f, 0.75f));
                data.BlockLight.Add(new Vector3(0.125f, 1.5f, 3f));
                data.BlockLightDir.Add(new Vector3(0f, -1f, 0f));
            }

            return data;
        }

        [Test]
        public void Half_RoundTripsTheValuesVertexChannelsCarry()
        {
            // Skylight/tints (0..1) and block-light intensities (can exceed 1) — the range these channels use.
            foreach (float v in new[] { 0f, 1f, 0.5f, 0.25f, -1f, 2f, 4f, 6f, 0.1f, 12.5f })
            {
                float back = Mathf.HalfToFloat(ChunkMeshData.Half(v));
                Assert.That(back, Is.EqualTo(v).Within(Mathf.Max(Mathf.Abs(v), 1f) * 0.001f), $"half round-trip of {v}");
            }

            // Small integers (the face/tint MODES the shader branches on) must survive exactly, not "close".
            for (int mode = 0; mode <= 6; mode++)
            {
                Assert.AreEqual((float)mode, Mathf.HalfToFloat(ChunkMeshData.Half(mode)), $"mode {mode} must be exact");
            }

            // A NaN or Inf must never reach a vertex buffer — it would poison the whole triangle's interpolation.
            Assert.AreEqual(0f, Mathf.HalfToFloat(ChunkMeshData.Half(float.NaN)));
            Assert.IsFalse(float.IsInfinity(Mathf.HalfToFloat(ChunkMeshData.Half(float.PositiveInfinity))));
        }

        [Test]
        public void Norms_QuantiseWithinOneQuantumAndHitTheEndpointsExactly()
        {
            Assert.AreEqual(127, ChunkMeshData.SNorm(1f));
            Assert.AreEqual(-127, ChunkMeshData.SNorm(-1f));
            Assert.AreEqual(0, ChunkMeshData.SNorm(0f));
            Assert.AreEqual(255, ChunkMeshData.UNorm(1f));
            Assert.AreEqual(0, ChunkMeshData.UNorm(0f));

            // Out-of-range input clamps instead of wrapping (a wrapped normal would flip a face's lighting).
            Assert.AreEqual(127, ChunkMeshData.SNorm(3f));
            Assert.AreEqual(255, ChunkMeshData.UNorm(2f));
            Assert.AreEqual(0, ChunkMeshData.UNorm(-1f));

            for (float v = 0f; v <= 1f; v += 0.05f)
            {
                Assert.That(Unpack(ChunkMeshData.UNorm(v)), Is.EqualTo(v).Within(1f / 255f));
                Assert.That(Unpack(ChunkMeshData.SNorm(v)), Is.EqualTo(v).Within(1f / 127f));
            }
        }

        [Test]
        public void Pack_KeepsPositionsAndAtlasUvsBitExactAndEveryChannelInPlace()
        {
            var data = MakeData(4);
            try
            {
                data.Pack();
                Assert.AreEqual(4, data.PackedCount);

                for (int i = 0; i < 4; i++)
                {
                    var p = data.Packed[i];
                    // Position and atlas UV stay Float32 — a UV off by a quantum bleeds the neighbouring tile in.
                    Assert.AreEqual(data.Verts[i], p.Position, $"position {i}");
                    Assert.AreEqual(data.Uvs[i], p.Uv, $"uv {i}");

                    Assert.AreEqual(0f, Unpack(p.Nx));
                    Assert.AreEqual(1f, Unpack(p.Ny));
                    Assert.AreEqual(0f, Unpack(p.Nz));
                    Assert.AreEqual(1f, Unpack(p.Tx));
                    Assert.AreEqual(-1f, Unpack(p.Tw), "tangent handedness");
                    Assert.That(Unpack(p.Cr), Is.EqualTo(0.2f).Within(1f / 255f));
                    Assert.That(Unpack(p.Ca), Is.EqualTo(0.8f).Within(1f / 255f), "emission lives in colour.a");
                    Assert.AreEqual(0.5f, Mathf.HalfToFloat(p.SkyX), "skylight");
                    Assert.AreEqual(4f, Mathf.HalfToFloat(p.SkyY), "tint mode");
                    Assert.AreEqual(0.75f, Mathf.HalfToFloat(p.LeafW));
                    Assert.AreEqual(3f, Mathf.HalfToFloat(p.BlZ), "block light above 1 must not clamp");
                    Assert.AreEqual(-1f, Unpack(p.Dy), "block-light direction");
                }
            }
            finally
            {
                data.Release();
            }
        }

        [Test]
        public void Pack_ReusesItsBufferAcrossBuildsAndTracksTheCurrentCount()
        {
            var data = MakeData(6);
            try
            {
                data.Pack();
                var buffer = data.Packed;
                int capacity = buffer.Length;
                Assert.AreEqual(6, data.PackedCount);

                // A smaller follow-up build must keep the array and only shrink the valid count — otherwise every
                // remesh would allocate a fresh multi-hundred-KB array, exactly the churn the buffer pool avoids.
                data.Verts.RemoveRange(3, 3);
                data.Pack();
                Assert.AreEqual(3, data.PackedCount);
                Assert.AreSame(buffer, data.Packed, "the packed buffer must be reused, not reallocated");
                Assert.AreEqual(capacity, data.Packed.Length);
            }
            finally
            {
                data.Release();
            }

            Assert.AreEqual(0, data.PackedCount, "Release must invalidate the packed data");
        }

        [Test]
        public void ToMeshes_KeepsTheSubmeshSplitAndUploadsANonReadableMesh()
        {
            var data = MakeData(8);
            // 0 = opaque, 1 = see-through, 2 = paint — one triangle each, into one shared index buffer.
            data.OpaqueTris.AddRange(new[] { 0, 1, 2 });
            data.TransparentTris.AddRange(new[] { 3, 4, 5 });
            data.PaintTris.AddRange(new[] { 5, 6, 7 });
            data.Bounds = new Bounds(new Vector3(8f, 8f, 8f), new Vector3(16f, 16f, 16f));
            data.Pack();

            Mesh mesh = null;
            try
            {
                var built = data.ToMeshes();
                mesh = built.Render;

                Assert.AreEqual(8, mesh.vertexCount);
                Assert.AreEqual(3, mesh.subMeshCount);
                Assert.AreEqual(data.Bounds, mesh.bounds, "analytic bounds must survive the upload");

                // Index ranges: opaque first, then see-through, then paint — the order the materials come in.
                Assert.AreEqual(0, (int)mesh.GetIndexStart(0));
                Assert.AreEqual(3, (int)mesh.GetIndexCount(0));
                Assert.AreEqual(3, (int)mesh.GetIndexStart(1));
                Assert.AreEqual(3, (int)mesh.GetIndexCount(1));
                Assert.AreEqual(6, (int)mesh.GetIndexStart(2));
                Assert.AreEqual(3, (int)mesh.GetIndexCount(2));

                // The point of the exercise: no system-RAM copy alongside the GPU one.
                Assert.IsFalse(mesh.isReadable, "the chunk render mesh must be uploaded with markNoLongerReadable");
            }
            finally
            {
                data.Release();
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void ToMeshes_HandlesAnEmptyChunkWithoutProducingGeometry()
        {
            // All-air chunks reach this path constantly (the streamer meshes every coord in range), and the
            // callers key off vertexCount == 0 to destroy the mesh again — so it must be exactly that, not a throw.
            var data = ChunkMeshData.Rent();
            data.Pack();

            Mesh mesh = null;
            try
            {
                var built = data.ToMeshes();
                mesh = built.Render;
                Assert.AreEqual(0, mesh.vertexCount);
                Assert.IsNull(built.Collider, "no collider triangles → no collision mesh to cook");
            }
            finally
            {
                data.Release();
                if (mesh != null)
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}
