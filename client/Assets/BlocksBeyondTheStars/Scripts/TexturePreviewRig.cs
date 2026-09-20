// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The texture editor's 3-D preview (#1955): a small render-texture stage, parked far from any world, that shows
    /// the tile being painted the way the game shows it — on a cube, on the built-in form that cuts a PICTURE tile
    /// into its parts (the bed as both halves, the campfire, the rug, the pot, the ladder), or on crossed cutout
    /// cards. It samples the editor's standalone 64×64 texture, so a brush stroke shows at once and the big block
    /// atlas is never touched while painting.
    /// </summary>
    internal sealed class TexturePreviewRig
    {
        private const int Resolution = 384;

        private readonly GameObject _root;
        private readonly Transform _spin;
        private readonly MeshFilter _filter;
        private readonly MeshRenderer _renderer;
        private readonly Material _opaque;
        private readonly Material _cutout;
        private readonly Camera _camera;
        private Mesh _mesh;

        public TexturePreviewRig(Texture2D tile)
        {
            Target = new RenderTexture(Resolution, Resolution, 16) { name = "TexturePreviewRT" };
            _root = new GameObject("TexturePreviewStage");
            _root.transform.position = new Vector3(0f, -6000f, 0f); // parked far from the world and the menu backdrop

            var spinGo = new GameObject("Spin");
            spinGo.transform.SetParent(_root.transform, false);
            _spin = spinGo.transform;

            var modelGo = new GameObject("Model", typeof(MeshFilter), typeof(MeshRenderer));
            modelGo.transform.SetParent(_spin, false);
            _filter = modelGo.GetComponent<MeshFilter>();
            _renderer = modelGo.GetComponent<MeshRenderer>();

            var lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Texture");
            _opaque = new Material(lit) { color = Color.white, mainTexture = tile };
            if (_opaque.HasProperty("_Floor"))
            {
                _opaque.SetFloat("_Floor", 0.55f); // a bright studio look — this is about the pixels, not the mood
            }

            // The cloud shader with sun shading and the UV bulge off is a plain "texture × colour, alpha blended" —
            // what a cutout card needs. (LitColor writes alpha 1, so it cannot show a silhouette.)
            var alpha = Shader.Find("BlocksBeyondTheStars/Cloud") ?? lit;
            _cutout = new Material(alpha) { color = Color.white, mainTexture = tile };

            var camGo = new GameObject("TexturePreviewCam", typeof(Camera));
            camGo.transform.SetParent(_root.transform, false);
            _camera = camGo.GetComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 1.05f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.03f, 0.07f, 0.14f, 1f);
            _camera.targetTexture = Target;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 20f;
            camGo.transform.localPosition = new Vector3(2.4f, 1.9f, -2.4f);
            camGo.transform.LookAt(_root.transform.position + new Vector3(0f, 0.15f, 0f));
        }

        public RenderTexture Target { get; }

        /// <summary>Degrees per second the model turns; 0 holds it still (a pad player steers it instead).</summary>
        public float SpinSpeed { get; set; } = 24f;

        public void Tick(float dt, float steer)
        {
            if (_spin != null)
            {
                _spin.Rotate(0f, (SpinSpeed * dt) + steer, 0f, Space.Self);
            }
        }

        /// <summary>Rebuilds the model for another texture.</summary>
        public void Show(TextureEntry entry, GameContent content)
        {
            if (_mesh != null)
            {
                Object.Destroy(_mesh);
                _mesh = null;
            }

            switch (entry != null ? entry.Preview : TexturePreviewKind.Cube)
            {
                case TexturePreviewKind.ShapedPicture:
                    _mesh = BuildShaped(entry.Block, content);
                    _renderer.sharedMaterial = _opaque;
                    break;
                case TexturePreviewKind.Billboard:
                    _mesh = BuildBillboard();
                    _renderer.sharedMaterial = _cutout;
                    break;
                default:
                    _mesh = BuildCube();
                    _renderer.sharedMaterial = _opaque;
                    break;
            }

            _filter.sharedMesh = _mesh;
        }

        public void Destroy()
        {
            if (_mesh != null)
            {
                Object.Destroy(_mesh);
            }

            Object.Destroy(_opaque);
            Object.Destroy(_cutout);
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            Target.Release();
            Object.Destroy(Target);
            Object.Destroy(_root);
        }

        // ---------------------------------------------------------------- meshes (centred on the origin)

        private static Mesh BuildCube()
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            const float h = 0.5f;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            Quad(new Vector3(-h, -h, -h), new Vector3(-h, h, -h), new Vector3(h, h, -h), new Vector3(h, -h, -h));   // -Z
            Quad(new Vector3(h, -h, h), new Vector3(h, h, h), new Vector3(-h, h, h), new Vector3(-h, -h, h));       // +Z
            Quad(new Vector3(h, -h, -h), new Vector3(h, h, -h), new Vector3(h, h, h), new Vector3(h, -h, h));       // +X
            Quad(new Vector3(-h, -h, h), new Vector3(-h, h, h), new Vector3(-h, h, -h), new Vector3(-h, -h, -h));   // -X
            Quad(new Vector3(-h, h, -h), new Vector3(-h, h, h), new Vector3(h, h, h), new Vector3(h, h, -h));       // +Y
            Quad(new Vector3(-h, -h, h), new Vector3(-h, -h, -h), new Vector3(h, -h, -h), new Vector3(h, -h, h));   // -Y
            return Finish("TexturePreviewCube", v, uv, t);
        }

        private static Mesh BuildBillboard()
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            const float h = 0.5f;
            void Card(Vector3 dir)
            {
                int i = v.Count;
                v.Add((-dir * h) + new Vector3(0f, -h, 0f));
                v.Add((-dir * h) + new Vector3(0f, h, 0f));
                v.Add((dir * h) + new Vector3(0f, h, 0f));
                v.Add((dir * h) + new Vector3(0f, -h, 0f));
                uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(0f, 1f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(1f, 0f));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }

            // The rosette the chunk mesher plants: three cards at 0° / 60° / 120° (the shader draws both sides).
            for (int k = 0; k < 3; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                Card(new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)));
            }

            return Finish("TexturePreviewBillboard", v, uv, t);
        }

        /// <summary>The block's built-in form with the face slots of <c>data/blocks.json</c> — the same geometry and
        /// the same UV rule the chunk mesher uses, over a tile rect of (0,0,1,1). The bed gets its second half.</summary>
        private static Mesh BuildShaped(BlockDefinition def, GameContent content)
        {
            if (def == null)
            {
                return BuildCube();
            }

            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            var slots = ShapeFaceTextures.SlotsFor(content, new BlockId(def.NumericId.Value));
            int shape = PropShapes.DefaultPlaceShape(def.Key);
            var offset = Vector3.zero;
            Vector3 centre = new Vector3(0.5f, 0.5f, 0.5f);
            AddForm(shape, 0, offset, slots, v, uv, t);

            int descriptor = ShapeCode.Pack(shape, 0);
            if (FurnitureShapes.TryBedPartnerOffset(descriptor, out int dx, out int dz))
            {
                int partner = ShapeCode.ShapeOf(FurnitureShapes.BedPartnerDescriptor(descriptor));
                AddForm(partner, 0, new Vector3(dx, 0f, dz), slots, v, uv, t);
                centre += new Vector3(dx, 0f, dz) * 0.5f;
            }

            for (int i = 0; i < v.Count; i++)
            {
                v[i] -= centre;
            }

            return v.Count == 0 ? BuildCube() : Finish("TexturePreviewForm", v, uv, t);
        }

        private static void AddForm(int shape, int yaw, Vector3 offset, FaceSlot[] slots,
            List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var faces = BlockShapeGeometry.Build(shape, yaw);
            if (faces == null)
            {
                return;
            }

            var tile = new Rect(0f, 0f, 1f, 1f);
            foreach (var f in faces)
            {
                ShapeFaceTextures.FaceUvs(f, tile, slots, null, out var a, out var b, out var c, out var d);
                int i = v.Count;
                v.Add(f.A + offset); v.Add(f.B + offset); v.Add(f.C + offset);
                uv.Add(a); uv.Add(b); uv.Add(c);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                if (f.IsQuad)
                {
                    v.Add(f.D + offset);
                    uv.Add(d);
                    t.Add(i); t.Add(i + 2); t.Add(i + 3);
                }
            }
        }

        private static Mesh Finish(string name, List<Vector3> v, List<Vector2> uv, List<int> t)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(v);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
