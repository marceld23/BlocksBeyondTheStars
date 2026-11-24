// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The geometry primitives of a creature body: mesh parts, cube parts and eyes, the tiled and rounded cube meshes
    /// they draw with.
    /// </summary>
    public sealed partial class CreatureBuilder
    {
        /// <summary>#2009: a part with its own mesh (the pyramid heads) — the primitive helpers only make cubes and spheres.
        /// Render-only unless colliders are being kept, in which case it gets a box collider of the mesh's bounds.</summary>
        private void AddMeshPart(Transform parent, string partName, Vector3 localPos, Vector3 scale, Material mat, Mesh mesh)
        {
            if (mesh == null)
            {
                AddPartTo(parent, partName, localPos, scale, mat);
                return;
            }

            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            _renderers.Add(mr);
            if (_keepColliders)
            {
                var col = go.AddComponent<BoxCollider>();
                col.center = mesh.bounds.center;
                col.size = mesh.bounds.size;
            }
        }

        /// <summary>Adds a render-only cube parented to an arbitrary transform (e.g. eyes on the head pivot).</summary>
        private GameObject AddPartTo(Transform parent, string partName, Vector3 localPos, Vector3 scale, Material mat, PrimitiveType shape = PrimitiveType.Cube)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null && !_keepColliders)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            TileCube(go, shape, scale, mat);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            if (shape == PrimitiveType.Cube)
            {
                RecordCoatPart(go, scale, mat);
            }

            return go;
        }

        /// <summary>#2866: one eye — a sphere whose front carries the species' eye look (iris, pupil, highlight), turned to
        /// face along <paramref name="normal"/> (the head's front unless an arachnid's sloped face says otherwise).</summary>
        private void AddEye(Transform parent, string partName, Vector3 localPos, float size, Vector3 normal, Material mat)
        {
            var go = AddPartTo(parent, partName, localPos, Vector3.one * size, mat, PrimitiveType.Sphere);
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null)
            {
                filter.sharedMesh = EyeSphereMesh();
            }

            if (normal != Vector3.forward && normal.sqrMagnitude > 1e-6f)
            {
                go.transform.localRotation = Quaternion.LookRotation(normal);
            }
        }

        private void AddPart(GameObject root, string partName, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null && !_keepColliders)
            {
                Object.Destroy(col); // render-only; never blocks the player (a giant's core parts excepted, #1998)
            }

            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            TileCube(go, PrimitiveType.Cube, scale, mat);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
            RecordCoatPart(go, scale, mat);
        }

        /// <summary>#2831: swaps a cube primitive's mesh for the tiled cube of its size (same shape and bounds, so its box
        /// collider is unchanged) — the hide repeats per metre instead of stretching over each face. #2860: a coarse hide
        /// repeats at twice the size; #2863: a countershaded flank maps v once; #2865: the animal's placement shifts the
        /// coat.</summary>
        private void TileCube(GameObject go, PrimitiveType shape, Vector3 scale, Material mat)
        {
            if (shape != PrimitiveType.Cube)
            {
                return;
            }

            var filter = go.GetComponent<MeshFilter>();
            if (filter != null)
            {
                float metres = _coat != null ? _coat.MetresOf(mat) : TiledCube.TileMetres;
                bool pin = _pinV || (_coat != null && _coat.Pins(mat));
                // The part's edges are rounded (RoundedCube) — same cube, same texture coordinates.
                // From far away the plain tiled cube again (RoundLod): the rounding is under a pixel there.
                var tiled = TiledMesh(scale, pin, metres, _placement);
                if (Rounds(scale))
                {
                    RoundLod.Assign(filter, RoundedMesh(scale, pin, metres, _placement, GameLook.RoundShare), tiled, GameLook.RoundShare);
                }
                else
                {
                    filter.sharedMesh = tiled;
                }
            }
        }

        /// <summary>True when a part of this size gets rounded edges — the rounding is on and the part has
        /// a real thickness (an eyelid is built at zero height and scaled up to blink: it keeps the plain cube).</summary>
        private static bool Rounds(Vector3 scale)
            => GameLook.RoundShare > 0f && Mathf.Min(Mathf.Abs(scale.x), Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))) > 0.004f;

        private static readonly Dictionary<long, Mesh> RoundedMeshes = new Dictionary<long, Mesh>();

        /// <summary>The rounded cube for a part of <paramref name="size"/> — one mesh per (repeats, pinning,
        /// placement, proportions): the proportions are quantised to 1/255 of the longest side, so every animal of a
        /// species (and most parts of a kind) share it.</summary>
        private static Mesh RoundedMesh(Vector3 size, bool pinV, float metres, int placement, float share)
        {
            int rx = TiledCube.Repeats(Mathf.Abs(size.x), metres);
            int ry = TiledCube.Repeats(Mathf.Abs(size.y), metres);
            int rz = TiledCube.Repeats(Mathf.Abs(size.z), metres);
            float longest = Mathf.Max(Mathf.Abs(size.x), Mathf.Max(Mathf.Abs(size.y), Mathf.Abs(size.z)));
            int qx = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(size.x) / longest * 255f), 1, 255);
            int qy = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(size.y) / longest * 255f), 1, 255);
            int qz = Mathf.Clamp(Mathf.RoundToInt(Mathf.Abs(size.z) / longest * 255f), 1, 255);
            long key = (uint)TiledCube.Key(rx, ry, rz, pinV, placement) | ((long)qx << 20) | ((long)qy << 28) | ((long)qz << 36)
                       | ((long)Mathf.Clamp(Mathf.RoundToInt(share * 100f), 0, 99) << 44);
            if (RoundedMeshes.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var shape = new Vector3(qx / 255f, qy / 255f, qz / 255f);
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            RoundedCube.Build(shape, RoundedCube.Radius(shape, share), TiledCube.Build(rx, ry, rz, pinV, placement), verts, normals, uvs, tris);
            var mesh = new Mesh { name = "CreatureRoundCube" };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            RoundedMeshes[key] = mesh;
            return mesh;
        }

        // #2831: the part cubes, one shared mesh per (repeats x/y/z, pinned) — see TiledCube.
        private static readonly Dictionary<int, Mesh> TiledMeshes = new Dictionary<int, Mesh>();

        /// <summary>The cube mesh for a part of <paramref name="size"/> (build-space units): the hide repeats once per
        /// <see cref="TiledCube.TileMetres"/> on every face, so a toe and a colossus show the same texel density.</summary>
        private static Mesh TiledMesh(Vector3 size, bool pinV, float metres, int placement)
        {
            int rx = TiledCube.Repeats(Mathf.Abs(size.x), metres);
            int ry = TiledCube.Repeats(Mathf.Abs(size.y), metres);
            int rz = TiledCube.Repeats(Mathf.Abs(size.z), metres);
            int key = TiledCube.Key(rx, ry, rz, pinV, placement);
            if (TiledMeshes.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var d = TiledCube.Build(rx, ry, rz, pinV, placement);
            var verts = new Vector3[24];
            var normals = new Vector3[24];
            var uvs = new Vector2[24];
            for (int i = 0; i < 24; i++)
            {
                verts[i] = new Vector3(d.Positions[i * 3], d.Positions[i * 3 + 1], d.Positions[i * 3 + 2]);
                normals[i] = new Vector3(d.Normals[i * 3], d.Normals[i * 3 + 1], d.Normals[i * 3 + 2]);
                uvs[i] = new Vector2(d.Uvs[i * 2], d.Uvs[i * 2 + 1]);
            }

            var mesh = new Mesh { name = "CreatureCube" + key };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = d.Triangles;
            mesh.RecalculateBounds();
            TiledMeshes[key] = mesh;
            return mesh;
        }
    }
}
