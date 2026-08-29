// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Shows WHERE the ship is breached (#1368): while the repair panel reports missing design hull cells
    /// (<see cref="GameBootstrap.ShipRepair"/>, <c>MissingCells &gt; 0</c>), every listed cell of the parked ship
    /// gets a translucent hologram box in the world — the placement ghost's look — so the player can walk to
    /// the holes instead of hunting for them along the hull. The cells arrive structure-local with the
    /// readout and are mapped through the parked ship's origin (<see cref="LandedShipModel"/>); the space
    /// scene (EVA) renders its own ship and is left alone.
    ///
    /// Cheap: one mesh for all cells, rebuilt only when a NEW readout arrives (the reference changes), moved
    /// with the ship's nearest world copy every frame like <see cref="LandedShipView"/> does.
    /// </summary>
    public sealed class HullBreachView : MonoBehaviour
    {
        public GameBootstrap Game;

        // The placement ghost's hologram blue — a breach reads as "a block belongs here", not as damage FX.
        private static readonly Color BreachColor = new Color(0.55f, 0.85f, 1f, 0.35f);

        // Faces sit a hair inside the cell so they never z-fight with the intact hull faces around the hole.
        private const float Inset = 0.04f;

        private GameObject _go;
        private Mesh _mesh;
        private ShipRepairStatus _built;
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();

        private void Update()
        {
            var sr = Game != null ? Game.ShipRepair : null;
            LandedShipModel ship = null;
            bool show = sr != null && sr.MissingCells > 0 && sr.MissingX != null && sr.MissingX.Length > 0
                && !Game.SpaceViewActive
                && Game.LandedShips.TryGetValue(sr.StructureId ?? string.Empty, out ship);
            if (!show)
            {
                _built = null;
                if (_go != null && _go.activeSelf)
                {
                    _go.SetActive(false);
                }

                return;
            }

            if (_go == null)
            {
                Create();
            }

            if (!ReferenceEquals(sr, _built))
            {
                _built = sr;
                BuildMesh(sr);
            }

            // Round worlds: the parked ship is drawn at the copy nearest the player — follow it.
            _go.transform.position = new Vector3(Game.SceneX(ship.Origin.X), ship.Origin.Y, Game.SceneZ(ship.Origin.Z));
            if (!_go.activeSelf)
            {
                _go.SetActive(true);
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null)
            {
                Destroy(_mesh);
                _mesh = null;
            }

            if (_go != null)
            {
                Destroy(_go);
                _go = null;
            }
        }

        private void Create()
        {
            _go = new GameObject("HullBreaches");
            _go.transform.SetParent(transform, true); // under the game root — torn down with the world
            var filter = _go.AddComponent<MeshFilter>();
            var renderer = _go.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // The project's translucent-runtime-mesh pattern (PlacementGhost, BaseShieldView): the Cloud shader
            // is in Always-Included Shaders, so Shader.Find survives build stripping.
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.SetColor(Shader.PropertyToID("_Color"), ShaderColor.Srgb(BreachColor));
            mat.renderQueue = 3001; // over the world, so a breach on the far side still shows through nothing
            renderer.sharedMaterial = mat;

            _mesh = new Mesh { name = "HullBreaches" };
            filter.sharedMesh = _mesh;
        }

        /// <summary>One inset unit box per listed cell (structure-local offsets; the object sits at the origin).</summary>
        private void BuildMesh(ShipRepairStatus sr)
        {
            _verts.Clear();
            _tris.Clear();
            int n = Mathf.Min(sr.MissingX.Length, Mathf.Min(sr.MissingY?.Length ?? 0, sr.MissingZ?.Length ?? 0));
            for (int i = 0; i < n; i++)
            {
                Box(new Vector3(sr.MissingX[i], sr.MissingY[i], sr.MissingZ[i]));
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        /// <summary>Six outward-wound quads of the cell at <paramref name="o"/>, inset by <see cref="Inset"/>.</summary>
        private void Box(Vector3 o)
        {
            float a = Inset, b = 1f - Inset;
            void Quad(Vector3 p, Vector3 q, Vector3 r, Vector3 s)
            {
                int i = _verts.Count;
                _verts.Add(o + p); _verts.Add(o + q); _verts.Add(o + r); _verts.Add(o + s);
                _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
                _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
            }

            Quad(new Vector3(a, b, a), new Vector3(a, b, b), new Vector3(b, b, b), new Vector3(b, b, a)); // +Y
            Quad(new Vector3(a, a, b), new Vector3(a, a, a), new Vector3(b, a, a), new Vector3(b, a, b)); // -Y
            Quad(new Vector3(b, a, a), new Vector3(b, b, a), new Vector3(b, b, b), new Vector3(b, a, b)); // +X
            Quad(new Vector3(a, a, b), new Vector3(a, b, b), new Vector3(a, b, a), new Vector3(a, a, a)); // -X
            Quad(new Vector3(b, a, b), new Vector3(b, b, b), new Vector3(a, b, b), new Vector3(a, a, b)); // +Z
            Quad(new Vector3(a, a, a), new Vector3(a, b, a), new Vector3(b, b, a), new Vector3(b, a, a)); // -Z
        }
    }
}
