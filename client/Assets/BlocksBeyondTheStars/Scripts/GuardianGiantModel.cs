// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// One guardian giant's model (#3676): a root and one bone per <see cref="GuardianGiantPart"/> (the head and the arms hang
    /// from the torso, the torso and the legs from the root), each bone carrying its limb's block mesh, the light overlay of
    /// its lights and its hit box. Built by <see cref="GuardianGiantModel"/>; posed by <see cref="GuardianGiantView"/>.
    /// </summary>
    public sealed class GuardianGiantRig
    {
        public GameObject Root;
        public readonly Transform[] Bones = new Transform[6];
        public readonly Vector3[] Rest = new Vector3[6];
        public readonly GameObject[] Limbs = new GameObject[6];
        public readonly MeshRenderer[] Glows = new MeshRenderer[6];
        public readonly BoxCollider[] Colliders = new BoxCollider[6];
        public Material GlowBody;
        public Material GlowVisor;
        public int Seed = int.MinValue;
        public bool Lit;

        public Transform Bone(GuardianGiantPart part) => Bones[(int)part];
    }

    /// <summary>
    /// Builds the guardian giant's block-limb model (#3676) from <see cref="GuardianGiantBody.Standing"/> with the real block
    /// atlas and <see cref="ChunkMesher"/> — the way <see cref="SpeederView"/> meshes its voxel hull — so the giant that stands
    /// up is cell for cell the block body that lay there: one mesh per limb (six draw calls), its lights meshed as the lit or
    /// the dark light block, each limb offset so its bone turns about <see cref="GuardianGiantBody.Joint"/>. The lights' blink
    /// is a separate overlay per limb (a slightly larger cube over each light cell in a self-lit material), so blinking costs
    /// a colour change, not a re-mesh. Also the thrown rock (a small block-textured cube, never a world block) and the Codex
    /// portrait.
    /// </summary>
    public static class GuardianGiantModel
    {
        /// <summary>The chunk-local cell of body cell (0, 0, 0): the body spans x −6 … 5, y 0 … 13, z −2 … 1, so it fits one
        /// 16³ chunk with a free cell on every side (the mesher's neighbour reads stay inside).</summary>
        private static readonly Vector3Int ChunkOffset = new Vector3Int(8, 1, 8);

        /// <summary>How much larger than its cell a light's overlay cube is (it must cover the block's faces).</summary>
        private const float GlowScale = 1.06f;

        /// <summary>A joint in body-local scene units.</summary>
        public static Vector3 Joint(GuardianGiantPart part)
        {
            var j = GuardianGiantBody.Joint(part);
            return new Vector3(j.X, j.Y, j.Z);
        }

        /// <summary>A joint relative to its parent bone (the root for the torso and the legs).</summary>
        public static Vector3 JointLocal(GuardianGiantPart part)
        {
            var parent = GuardianGiantBody.Parent(part);
            return Joint(part) - (parent.HasValue ? Joint(parent.Value) : Vector3.zero);
        }

        /// <summary>The visor's centre in the head bone's frame.</summary>
        public static Vector3 VisorLocal
        {
            get
            {
                var v = GuardianGiantBody.Visor;
                return new Vector3(v.X, v.Y, v.Z) - Joint(GuardianGiantPart.Head);
            }
        }

        /// <summary>The right hand's centre in the right arm bone's frame (where the rock is held).</summary>
        public static Vector3 RightHandLocal => new Vector3(4f, 2.5f, 0.6f) - Joint(GuardianGiantPart.ArmRight);

        /// <summary>The chest core light's front in the torso bone's frame (the giant's own lamp).</summary>
        public static Vector3 ChestLocal => new Vector3(0f, 8.5f, 1.05f) - Joint(GuardianGiantPart.Torso);

        /// <summary>The empty rig: the root under <paramref name="parent"/>, the bones at their joints, and — for a live giant —
        /// one hit box per limb on the giants' layer and a kinematic body on the root (the moving compound collides with the
        /// player's capsule without a static-collider rebuild every frame, like the creature giants).</summary>
        public static GuardianGiantRig CreateRig(Transform parent, string name, bool colliders)
        {
            var rig = new GuardianGiantRig { Root = new GameObject(name) };
            rig.Root.transform.SetParent(parent, false);
            foreach (var part in GuardianGiantBody.Parts)
            {
                int i = (int)part;
                var parentPart = GuardianGiantBody.Parent(part);
                var bone = new GameObject(part.ToString()).transform;
                bone.SetParent(parentPart.HasValue ? rig.Bones[(int)parentPart.Value] : rig.Root.transform, false);
                bone.localPosition = JointLocal(part);
                rig.Bones[i] = bone;
                rig.Rest[i] = bone.localPosition;
            }

            if (colliders)
            {
                AddHitBoxes(rig);
            }

            return rig;
        }

        /// <summary>(Re)meshes every limb for <paramref name="seed"/> (the rusted plates) and <paramref name="lit"/> (the lights'
        /// block). False when the atlas or the chunk material is not ready yet (the caller tries again next frame).</summary>
        public static bool MeshLimbs(GuardianGiantRig rig, GameContent content, BlockTextureAtlas atlas, Material opaque,
            Material transparent, int seed, bool lit)
        {
            if (rig?.Root == null || content == null || atlas == null || opaque == null)
            {
                return false;
            }

            var mats = transparent != null ? new[] { opaque, transparent } : new[] { opaque };
            var byPart = CellsByPart(content, seed, lit);
            foreach (var part in GuardianGiantBody.Parts)
            {
                int i = (int)part;
                if (rig.Limbs[i] != null)
                {
                    Object.Destroy(rig.Limbs[i]);
                }

                rig.Limbs[i] = MeshCells(byPart[i], content, atlas, mats, rig.Bones[i], LimbOffset(part), part.ToString());
            }

            rig.Seed = seed;
            rig.Lit = lit;
            return true;
        }

        /// <summary>Adds the light overlays (once per rig): per limb with lights, one mesh of slightly enlarged cubes over its
        /// light cells in <paramref name="body"/>'s material (<paramref name="visor"/>'s for the head). Starts hidden.</summary>
        public static void AddGlows(GuardianGiantRig rig, Material body, Material visor)
        {
            rig.GlowBody = body;
            rig.GlowVisor = visor;
            var cells = GuardianGiantBody.Standing(0, lit: true);
            foreach (var part in GuardianGiantBody.Parts)
            {
                var verts = new List<Vector3>();
                var tris = new List<int>();
                foreach (var c in cells)
                {
                    if (c.Part == part && c.IsLight)
                    {
                        var centre = new Vector3(c.X + ChunkOffset.x + 0.5f, c.Y + ChunkOffset.y + 0.5f, c.Z + ChunkOffset.z + 0.5f);
                        AddCube(verts, tris, centre, GlowScale);
                    }
                }

                if (verts.Count == 0)
                {
                    continue;
                }

                var mesh = new UnityEngine.Mesh { name = "GiantGlow_" + part };
                mesh.SetVertices(verts);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();
                var go = new GameObject("Glow_" + part);
                go.transform.SetParent(rig.Bones[(int)part], false);
                go.transform.localPosition = LimbOffset(part);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = part == GuardianGiantPart.Head ? visor : body;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.enabled = false;
                go.AddComponent<OwnedProceduralMesh>();
                rig.Glows[(int)part] = mr;
            }
        }

        /// <summary>The thrown rock: a 2×2×2 block of stone meshed with the atlas, centred on its root, under
        /// <paramref name="parent"/>. Null when the atlas is not ready.</summary>
        public static GameObject BuildRock(GameContent content, BlockTextureAtlas atlas, Material opaque, Transform parent)
        {
            var def = content?.GetBlock("stone") ?? content?.GetBlock("basalt") ?? content?.GetBlock(GuardianGiantBody.Dark);
            if (def == null || atlas == null || opaque == null)
            {
                return null;
            }

            var cells = new Dictionary<Vector3i, BlockId>();
            for (int x = 0; x < 2; x++)
            for (int y = 0; y < 2; y++)
            for (int z = 0; z < 2; z++)
            {
                cells[new Vector3i(x + 1, y + 1, z + 1)] = def.NumericId;
            }

            var root = new GameObject("GiantRock");
            root.transform.SetParent(parent, false);
            var limb = MeshCells(cells, content, atlas, new[] { opaque }, root.transform, new Vector3(-2f, -2f, -2f), "Rock");
            if (limb == null)
            {
                Object.Destroy(root);
                return null;
            }

            root.transform.localScale = Vector3.one * 0.8f;
            return root;
        }

        /// <summary>#3676: the Codex portrait (<see cref="EnemyPortraitRig"/>) — the very model, standing, lit, seed 0, without
        /// hit boxes. An empty object when no game (atlas, chunk material) is there to mesh it with.</summary>
        public static GameObject BuildPortrait(Transform parent)
        {
            var game = Object.FindAnyObjectByType<GameBootstrap>();
            var rig = CreateRig(parent, "GuardianGiantPortrait", colliders: false);
            if (game != null)
            {
                MeshLimbs(rig, game.Content, game.Atlas, game.ChunkMaterial, game.ChunkMaterialTransparent, 0, lit: true);
            }

            return rig.Root;
        }

        /// <summary>A limb mesh's position in its bone's frame: the body cell (x, y, z) sits at chunk-local cell
        /// (x, y, z) + <see cref="ChunkOffset"/>, and the bone's origin is its joint.</summary>
        private static Vector3 LimbOffset(GuardianGiantPart part) => -(Vector3)ChunkOffset - Joint(part);

        /// <summary>The body's cells per limb, chunk-local, as block ids (a missing block key falls back to the plate's).</summary>
        private static Dictionary<Vector3i, BlockId>[] CellsByPart(GameContent content, int seed, bool lit)
        {
            var byPart = new Dictionary<Vector3i, BlockId>[6];
            for (int i = 0; i < byPart.Length; i++)
            {
                byPart[i] = new Dictionary<Vector3i, BlockId>();
            }

            var fallback = content.GetBlock(GuardianGiantBody.Plate)?.NumericId ?? BlockId.Air;
            foreach (var c in GuardianGiantBody.Standing(seed, lit))
            {
                var id = content.GetBlock(c.Block)?.NumericId ?? fallback;
                if (!id.IsAir)
                {
                    byPart[(int)c.Part][new Vector3i(c.X + ChunkOffset.x, c.Y + ChunkOffset.y, c.Z + ChunkOffset.z)] = id;
                }
            }

            return byPart;
        }

        /// <summary>Meshes a few chunk-local cells (one chunk at (0, 0, 0)) into a child of <paramref name="bone"/> at
        /// <paramref name="offset"/>; the lights among them flood their colour onto their neighbours. Display only: the
        /// mesher's collider mesh is freed. Null when nothing was meshed.</summary>
        private static GameObject MeshCells(Dictionary<Vector3i, BlockId> cells, GameContent content, BlockTextureAtlas atlas,
            Material[] mats, Transform bone, Vector3 offset, string name)
        {
            if (cells.Count == 0)
            {
                return null;
            }

            var chunk = new ChunkData(new ChunkCoord(0, 0, 0));
            foreach (var kv in cells)
            {
                chunk.Set(kv.Key.X, kv.Key.Y, kv.Key.Z, kv.Value);
            }

            BlockId CellAt(int x, int y, int z) => cells.TryGetValue(new Vector3i(x, y, z), out var b) ? b : BlockId.Air;
            var lights = ShipMeshBuilder.LightSources(content, cells);
            var (mesh, collider) = ChunkMesher.Build(chunk, content, CellAt, atlas, lights: lights);
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            if (mesh.vertexCount == 0)
            {
                Object.Destroy(mesh);
                return null;
            }

            var go = new GameObject("Limb_" + name);
            go.transform.SetParent(bone, false);
            go.transform.localPosition = offset;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            go.AddComponent<OwnedProceduralMesh>(); // freed with the limb (a re-mesh, the giant gone)
            return go;
        }

        /// <summary>One box per limb around its cells, in the bone's frame, on the giants' layer — the player's aim, melee sweep
        /// and capsule meet the body there (<see cref="CreatureView.RegisterGiant"/>).</summary>
        private static void AddHitBoxes(GuardianGiantRig rig)
        {
            var min = new Vector3[6];
            var max = new Vector3[6];
            for (int i = 0; i < 6; i++)
            {
                min[i] = Vector3.one * float.MaxValue;
                max[i] = Vector3.one * float.MinValue;
            }

            foreach (var c in GuardianGiantBody.Standing(0, lit: false))
            {
                int i = (int)c.Part;
                min[i] = Vector3.Min(min[i], new Vector3(c.X, c.Y, c.Z));
                max[i] = Vector3.Max(max[i], new Vector3(c.X + 1, c.Y + 1, c.Z + 1));
            }

            foreach (var part in GuardianGiantBody.Parts)
            {
                int i = (int)part;
                var go = new GameObject("Hit_" + part);
                go.layer = CreatureView.GiantLayer;
                go.transform.SetParent(rig.Bones[i], false);
                var box = go.AddComponent<BoxCollider>();
                box.center = ((min[i] + max[i]) * 0.5f) - Joint(part);
                box.size = max[i] - min[i];
                rig.Colliders[i] = box;
            }

            var body = rig.Root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        /// <summary>Appends an axis-aligned cube (24 vertices, outward faces) of edge <paramref name="size"/> around
        /// <paramref name="c"/>.</summary>
        private static void AddCube(List<Vector3> verts, List<int> tris, Vector3 c, float size)
        {
            float h = size * 0.5f;
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var normal in n)
            {
                var u = new Vector3(normal.y, normal.z, normal.x); // two axes across the face
                var v = Vector3.Cross(normal, u);
                int b = verts.Count;
                verts.Add(c + ((normal - u - v) * h));
                verts.Add(c + ((normal - u + v) * h));
                verts.Add(c + ((normal + u + v) * h));
                verts.Add(c + ((normal + u - v) * h));
                tris.Add(b); // clockwise seen from outside (Unity's front faces)
                tris.Add(b + 2);
                tris.Add(b + 1);
                tris.Add(b);
                tris.Add(b + 3);
                tris.Add(b + 2);
            }
        }
    }
}
