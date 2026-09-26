// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Crystal Net's glow (#2049): every cell of an ON network wears a translucent violet shell that pulses
    /// softly, so a kid can see the signal run along the conduit. Built from <see cref="GameBootstrap.CrystalNets"/>
    /// — one mesh for the whole world, rebuilt only when a new list arrives (a level change is one message per
    /// world, never a chunk re-mesh) and positioned through <c>ScenePos</c> so the wrap-around seam is honoured.
    /// </summary>
    public sealed class CrystalNetView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Inflate = 0.03f;      // the shell sits just outside the block faces
        private const int MaxCells = 4096;         // 64 nets × 64 cells is a big base; beyond that only the first cells glow

        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private GameObject _go;
        private Mesh _mesh;
        private Material _mat;
        private NetCrystalNet[] _built;
        private int _builtCount;
        private bool _subscribed;

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!_subscribed && Game.Network != null)
            {
                Game.Network.WorldResetReceived += _ => Clear();
                _subscribed = true;
            }

            var nets = Game.CrystalNets;
            if (!ReferenceEquals(nets, _built))
            {
                Rebuild(nets);
            }

            if (_go != null && _builtCount > 0)
            {
                _go.SetActive(!Game.SpaceViewActive);
                // A slow breathing pulse: the net is alive, not painted on.
                float a = 0.30f + 0.12f * Mathf.Sin(Time.time * 3.2f);
                _mat.SetColor(ColorId, new Color(0.62f, 0.45f, 1f, a));
            }
        }

        private void Clear()
        {
            _built = null;
            _builtCount = 0;
            if (_go != null)
            {
                _go.SetActive(false);
            }
        }

        private void Rebuild(NetCrystalNet[] nets)
        {
            _built = nets;
            _builtCount = 0;
            if (nets == null || nets.Length == 0)
            {
                if (_go != null)
                {
                    _go.SetActive(false);
                }

                return;
            }

            EnsureObjects();
            var verts = new List<Vector3>();
            var tris = new List<int>();
            foreach (var net in nets)
            {
                if (!net.On || net.Cells == null)
                {
                    continue;
                }

                for (int i = 0; i + 2 < net.Cells.Length && _builtCount < MaxCells; i += 3)
                {
                    var p = Game.ScenePos(net.Cells[i], net.Cells[i + 1], net.Cells[i + 2]);
                    AddCube(verts, tris, p);
                    _builtCount++;
                }
            }

            _mesh.Clear();
            if (_builtCount == 0)
            {
                _go.SetActive(false);
                return;
            }

            _mesh.SetVertices(verts);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateBounds();
            _go.SetActive(true);
        }

        private void EnsureObjects()
        {
            if (_go != null)
            {
                return;
            }

            _go = new GameObject("CrystalNetGlow");
            _go.transform.SetParent(transform, false);
            _mesh = new Mesh { name = "crystal_net_glow" };
            _mesh.MarkDynamic();
            _go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(shader) { renderQueue = 3050 };
            _mat.SetColor(ColorId, new Color(0.62f, 0.45f, 1f, 0.35f));
            var mr = _go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>An inflated unit cube around the block cell whose min corner is <paramref name="p"/>.</summary>
        private static void AddCube(List<Vector3> verts, List<int> tris, Vector3 p)
        {
            float lo = -Inflate, hi = 1f + Inflate;
            Vector3 a = p + new Vector3(lo, lo, lo), b = p + new Vector3(hi, lo, lo), c = p + new Vector3(hi, hi, lo), d = p + new Vector3(lo, hi, lo);
            Vector3 e = p + new Vector3(lo, lo, hi), f = p + new Vector3(hi, lo, hi), g = p + new Vector3(hi, hi, hi), h = p + new Vector3(lo, hi, hi);
            Quad(verts, tris, a, d, c, b); // -Z
            Quad(verts, tris, f, g, h, e); // +Z
            Quad(verts, tris, e, h, d, a); // -X
            Quad(verts, tris, b, c, g, f); // +X
            Quad(verts, tris, d, h, g, c); // +Y
            Quad(verts, tris, e, a, b, f); // -Y
        }

        private static void Quad(List<Vector3> verts, List<int> tris, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            int i = verts.Count;
            verts.Add(v0);
            verts.Add(v1);
            verts.Add(v2);
            verts.Add(v3);
            tris.Add(i);
            tris.Add(i + 1);
            tris.Add(i + 2);
            tris.Add(i);
            tris.Add(i + 2);
            tris.Add(i + 3);
        }
    }
}
