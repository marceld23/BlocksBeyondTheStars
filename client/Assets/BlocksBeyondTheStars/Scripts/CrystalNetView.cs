// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What the Crystal Net looks like (#2049, #2093, #2094):
    /// <list type="bullet">
    /// <item>every cell of an ON network wears a translucent violet shell, and <b>bright bands travel along the wire away
    /// from the sources that drive it</b> — a switch, a plate or sensor reporting ON, or the cell a logic block, timer block or
    /// Device Eye sends into. The distance of every cell from those sources comes from a breadth-first walk over the
    /// network's cells; no active source found → the whole net breathes evenly;</item>
    /// <item>an <b>arrow</b> on the face a logic block, timer block, watcher or Device Eye points to (the way the player
    /// looked when placing it): a gate sends that way, a watcher and an eye look that way. It lights up while the
    /// block's own output is ON;</item>
    /// <item>a small amber <b>status light</b> on top of every other device while it reports ON (a flipped switch, a
    /// sensor that sees something, a blocked sender, a full drill, a growing tank, …);</item>
    /// <item>#2263: what a <b>display</b> shows, floating over it — its symbol, its own line or its count.</item>
    /// </list>
    /// #2267: every device that points somewhere (a gate, a piston, a bridge motor, a watcher, an eye) gets the arrow — up
    /// and down included. #2268: a parked ship's net arrives already moved into world cells (<see cref="ClientCrystalNet"/>).
    /// Built from <see cref="GameBootstrap.CrystalNets"/> and <see cref="GameBootstrap.CrystalDevices"/>: two meshes,
    /// rebuilt only when a list arrives (and every few seconds, so the wrap-around seam follows the player); the wave
    /// only rewrites the shell's vertex colours per frame — eight shared vertices per cell. Vertex colours need the
    /// Always-Included <c>BlocksBeyondTheStars/ParticleAlpha</c> shader. Client only: nothing here is authoritative.
    /// </summary>
    public sealed class CrystalNetView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Inflate = 0.03f;       // the shell sits just outside the block faces
        private const int MaxCells = 4096;          // 64 nets × 64 cells is a big base; beyond that only the first cells glow
        private const float BandSpacing = 6f;       // cells between two bright bands
        private const float BandSpeed = 5f;         // cells per second the bands travel away from their source
        private const float RebuildSeconds = 3f;    // re-place the meshes now and then so the lap seam follows the player

        private static readonly Color32 ArrowOn = new Color32(120, 235, 255, 245);
        private static readonly Color32 ArrowOff = new Color32(70, 90, 120, 200);
        private static readonly Color32 StatusOn = new Color32(255, 180, 40, 245);

        private GameObject _netGo, _devGo, _hiGo;
        private Mesh _netMesh, _devMesh, _hiMesh;

        // #2267: the network under the crosshair, outlined — built when the aimed network (or the lists) change.
        private int _hiNetId = -1;
        private NetCrystalNet[] _hiBuiltFrom;
        private static readonly Color32 Highlight = new Color32(225, 235, 255, 70);
        private Material _mat;
        private NetCrystalNet[] _builtNets;
        private NetCrystalDevice[] _builtDevices;
        private float _builtAt;
        private bool _subscribed;

        // Per shell cell: its distance from the nearest active source (-1 = no source in its net → even breathing).
        private readonly List<float> _cellDistance = new List<float>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<int> _tris = new List<int>();

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
            var devices = Game.CrystalDevices;
            if (!ReferenceEquals(nets, _builtNets) || !ReferenceEquals(devices, _builtDevices) || Time.time - _builtAt > RebuildSeconds)
            {
                Rebuild(nets, devices);
            }

            bool visible = !Game.SpaceViewActive;
            if (_netGo != null)
            {
                _netGo.SetActive(visible && _cellDistance.Count > 0);
                if (_netGo.activeSelf)
                {
                    Animate();
                }
            }

            if (_devGo != null)
            {
                _devGo.SetActive(visible && _devMesh.vertexCount > 0);
            }

            UpdateHighlight(nets, visible);
        }

        /// <summary>#2267: outlines every cell of the network the player aims at — ON or OFF — so a kid sees what one wire
        /// reaches. Nothing is rebuilt while the same network stays in the crosshair.</summary>
        private void UpdateHighlight(NetCrystalNet[] nets, bool visible)
        {
            if (_hiGo == null)
            {
                return;
            }

            int netId = -1;
            NetCrystalNet aimedNet = null;
            if (visible && nets != null && Game.AimedCell is { } aim)
            {
                foreach (var net in nets)
                {
                    var cells = net.Cells;
                    if (cells == null)
                    {
                        continue;
                    }

                    for (int i = 0; i + 2 < cells.Length; i += 3)
                    {
                        if (cells[i] == aim.x && cells[i + 1] == aim.y && cells[i + 2] == aim.z)
                        {
                            aimedNet = net;
                            break;
                        }
                    }

                    if (aimedNet != null)
                    {
                        netId = net.Id;
                        break;
                    }
                }
            }

            if (aimedNet == null)
            {
                _hiNetId = -1;
                _hiGo.SetActive(false);
                return;
            }

            if (netId != _hiNetId || !ReferenceEquals(nets, _hiBuiltFrom))
            {
                _hiNetId = netId;
                _hiBuiltFrom = nets;
                _verts.Clear();
                _tris.Clear();
                var cells = aimedNet.Cells;
                for (int i = 0; i + 2 < cells.Length && _verts.Count < MaxCells * 8; i += 3)
                {
                    var p = Game.ScenePos(cells[i], cells[i + 1], cells[i + 2]);
                    const float o = 0.06f;
                    AddBox(p + new Vector3(-o, -o, -o), p + new Vector3(1f + o, 1f + o, 1f + o));
                }

                var colors = new List<Color32>(_verts.Count);
                for (int i = 0; i < _verts.Count; i++)
                {
                    colors.Add(Highlight);
                }

                _hiMesh.Clear();
                _hiMesh.SetVertices(_verts);
                _hiMesh.SetTriangles(_tris, 0);
                _hiMesh.SetColors(colors);
                _hiMesh.RecalculateBounds();
            }

            _hiGo.SetActive(true);
        }

        /// <summary>#2263: the displays' faces, as floating labels (pushed every frame, like the beacon names).</summary>
        private void LateUpdate()
        {
            if (Game == null || Game.SpaceViewActive || Game.MenuOpen)
            {
                return;
            }

            var devices = Game.CrystalDevices;
            var cam = Camera.main;
            if (devices == null || devices.Length == 0 || cam == null)
            {
                return;
            }

            ScreenLabelLayer labels = null;
            var here = Game.PlayerPosition;
            foreach (var d in devices)
            {
                if (d.Kind != nameof(CrystalDeviceKind.SignalDisplay))
                {
                    continue;
                }

                string text = DisplayText(d);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                var pos = Game.ScenePos(d.X + 0.5f, d.Y + 1.35f, d.Z + 0.5f);
                if ((pos - here).sqrMagnitude > 30f * 30f)
                {
                    continue;
                }

                labels ??= ScreenLabelLayer.Instance;
                var col = d.Output ? new Color(0.55f, 0.95f, 1f) : new Color(0.55f, 0.6f, 0.75f);
                labels.World(cam, pos, text, col, true, 16f, 24f);
            }
        }

        /// <summary>What a display shows: its ON / OFF symbol, its own line while ON, or its count.</summary>
        private static string DisplayText(NetCrystalDevice d)
        {
            switch ((DisplayMode)d.Mode)
            {
                case DisplayMode.Text:
                    return d.Output ? d.Label : string.Empty;
                case DisplayMode.Counter:
                    return CrystalMenuEdits.ValueOf(d.Config, "n") ?? "0";
                default:
                    return CrystalDeviceUi.SymbolOf(CrystalMenuEdits.ValueOf(d.Config, d.Output ? "on" : "off") ?? (d.Output ? "0" : "12"));
            }
        }

        private void Clear()
        {
            _builtNets = null;
            _builtDevices = null;
            _cellDistance.Clear();
            _hiNetId = -1;
            if (_netGo != null) _netGo.SetActive(false);
            if (_devGo != null) _devGo.SetActive(false);
            if (_hiGo != null) _hiGo.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------------------
        // Build
        // ---------------------------------------------------------------------------------------------------

        private void Rebuild(NetCrystalNet[] nets, NetCrystalDevice[] devices)
        {
            _builtNets = nets;
            _builtDevices = devices;
            _builtAt = Time.time;
            EnsureObjects();

            var byCell = new Dictionary<Vector3i, NetCrystalDevice>();
            if (devices != null)
            {
                foreach (var d in devices)
                {
                    byCell[new Vector3i(d.X, d.Y, d.Z)] = d;
                }
            }

            BuildShells(nets, byCell);
            BuildDeviceOverlay(devices);
        }

        private void BuildShells(NetCrystalNet[] nets, Dictionary<Vector3i, NetCrystalDevice> byCell)
        {
            _verts.Clear();
            _tris.Clear();
            _cellDistance.Clear();
            if (nets != null)
            {
                // The cells a gate or eye with an ON output sends into are sources of the wave in that net.
                var driven = new HashSet<Vector3i>();
                foreach (var d in byCell.Values)
                {
                    var kind = CrystalDeviceUi.KindOf(d);
                    if (d.Output && CrystalNetRules.IsGate(kind))
                    {
                        driven.Add(new Vector3i(d.X, d.Y, d.Z) + CrystalNetRules.DriveFace(kind, YawOf(d)));
                    }
                }

                foreach (var net in nets)
                {
                    if (!net.On || net.Cells == null)
                    {
                        continue;
                    }

                    var cells = new List<Vector3i>(net.Cells.Length / 3);
                    for (int i = 0; i + 2 < net.Cells.Length; i += 3)
                    {
                        cells.Add(new Vector3i(net.Cells[i], net.Cells[i + 1], net.Cells[i + 2]));
                    }

                    var dist = Distances(cells, byCell, driven);
                    for (int i = 0; i < cells.Count && _cellDistance.Count < MaxCells; i++)
                    {
                        var c = cells[i];
                        var p = Game.ScenePos(c.X, c.Y, c.Z);
                        AddBox(p + new Vector3(-Inflate, -Inflate, -Inflate), p + new Vector3(1f + Inflate, 1f + Inflate, 1f + Inflate));
                        _cellDistance.Add(dist.TryGetValue(c, out float d) ? d : -1f);
                    }
                }
            }

            _netMesh.Clear();
            if (_cellDistance.Count == 0)
            {
                return;
            }

            _netMesh.SetVertices(_verts);
            _netMesh.SetTriangles(_tris, 0);
            _colors.Clear();
            for (int i = 0; i < _verts.Count; i++)
            {
                _colors.Add(new Color32(158, 115, 255, 90));
            }

            _netMesh.SetColors(_colors);
            _netMesh.RecalculateBounds();
        }

        /// <summary>Breadth-first distance of every cell of one network from its active sources: members that are
        /// sources reporting ON, and cells a gate drives. Empty when the net has no active source it can see.</summary>
        private static Dictionary<Vector3i, float> Distances(List<Vector3i> cells, Dictionary<Vector3i, NetCrystalDevice> byCell, HashSet<Vector3i> driven)
        {
            var result = new Dictionary<Vector3i, float>();
            var set = new HashSet<Vector3i>(cells);
            var queue = new Queue<Vector3i>();
            foreach (var c in cells)
            {
                bool seed = driven.Contains(c)
                    || (byCell.TryGetValue(c, out var d) && d.Output && CrystalNetRules.IsSource(CrystalDeviceUi.KindOf(d)));
                if (seed)
                {
                    result[c] = 0f;
                    queue.Enqueue(c);
                }
            }

            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                float next = result[c] + 1f;
                foreach (var f in CrystalNetRules.Faces)
                {
                    var n = c + f;
                    if (set.Contains(n) && !result.ContainsKey(n))
                    {
                        result[n] = next;
                        queue.Enqueue(n);
                    }
                }
            }

            return result;
        }

        private void BuildDeviceOverlay(NetCrystalDevice[] devices)
        {
            _verts.Clear();
            _tris.Clear();
            var colors = new List<Color32>();
            if (devices != null)
            {
                foreach (var d in devices)
                {
                    var kind = CrystalDeviceUi.KindOf(d);
                    if (kind == CrystalDeviceKind.Light || kind == CrystalDeviceKind.None)
                    {
                        continue;
                    }

                    var p = Game.ScenePos(d.X, d.Y, d.Z);
                    bool pointed = CrystalNetRules.IsDirectional(kind);
                    if (pointed)
                    {
                        int before = _verts.Count;
                        AddArrow(p, CrystalNetRules.OutputFace(YawOf(d)));
                        for (int i = before; i < _verts.Count; i++)
                        {
                            colors.Add(d.Output ? ArrowOn : ArrowOff);
                        }
                    }

                    if (d.Output && !CrystalNetRules.IsGate(kind) && kind != CrystalDeviceKind.SignalDisplay)
                    {
                        int before = _verts.Count;
                        var c = p + new Vector3(0.5f, 1.04f, 0.5f);
                        AddBox(c - new Vector3(0.1f, 0.04f, 0.1f), c + new Vector3(0.1f, 0.1f, 0.1f));
                        for (int i = before; i < _verts.Count; i++)
                        {
                            colors.Add(StatusOn);
                        }
                    }
                }
            }

            _devMesh.Clear();
            if (_verts.Count == 0)
            {
                return;
            }

            _devMesh.SetVertices(_verts);
            _devMesh.SetTriangles(_tris, 0);
            _devMesh.SetColors(colors);
            _devMesh.RecalculateBounds();
        }

        /// <summary>The yaw a device was placed with, from its config line ("yaw=N"); 0 when it has none.</summary>
        private static int YawOf(NetCrystalDevice d)
        {
            if (string.IsNullOrEmpty(d.Config))
            {
                return 0;
            }

            foreach (var part in d.Config.Split(';'))
            {
                if (part.StartsWith("yaw=", System.StringComparison.Ordinal) && int.TryParse(part.Substring(4), out int yaw))
                {
                    return yaw;
                }
            }

            return 0;
        }

        // ---------------------------------------------------------------------------------------------------
        // The travelling wave
        // ---------------------------------------------------------------------------------------------------

        private void Animate()
        {
            float t = Time.time;
            float k = 2f * Mathf.PI / BandSpacing;
            float breathe = 0.30f + 0.12f * Mathf.Sin(t * 3.2f);
            _colors.Clear();
            for (int i = 0; i < _cellDistance.Count; i++)
            {
                float d = _cellDistance[i];
                float a;
                if (d < 0f)
                {
                    a = breathe;
                }
                else
                {
                    // cos(k·d − k·v·t) peaks move outward at v cells per second; ^4 keeps the bands narrow and bright.
                    float wave = Mathf.Max(0f, Mathf.Cos(k * d - k * BandSpeed * t));
                    wave *= wave;
                    wave *= wave;
                    a = 0.16f + 0.62f * wave;
                }

                byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(a * 255f), 0, 255);
                byte g = (byte)Mathf.Clamp(115 + (int)(90 * (a - 0.16f)), 0, 255);
                var col = new Color32(158, g, 255, alpha);
                for (int v = 0; v < 8; v++)
                {
                    _colors.Add(col);
                }
            }

            _netMesh.SetColors(_colors);
        }

        // ---------------------------------------------------------------------------------------------------
        // Geometry
        // ---------------------------------------------------------------------------------------------------

        private void EnsureObjects()
        {
            if (_netGo != null)
            {
                return;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/ParticleAlpha") ?? Shader.Find("Unlit/Transparent");
            _mat = new Material(shader) { renderQueue = 3050 };
            _netGo = MakeLayer("CrystalNetGlow", out _netMesh);
            _devGo = MakeLayer("CrystalDeviceMarks", out _devMesh);
            _hiGo = MakeLayer("CrystalNetHighlight", out _hiMesh);
        }

        private GameObject MakeLayer(string name, out Mesh mesh)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>An axis-aligned box from <paramref name="lo"/> to <paramref name="hi"/>: eight shared vertices.</summary>
        private void AddBox(Vector3 lo, Vector3 hi)
        {
            int i = _verts.Count;
            _verts.Add(new Vector3(lo.x, lo.y, lo.z)); // 0
            _verts.Add(new Vector3(hi.x, lo.y, lo.z)); // 1
            _verts.Add(new Vector3(hi.x, hi.y, lo.z)); // 2
            _verts.Add(new Vector3(lo.x, hi.y, lo.z)); // 3
            _verts.Add(new Vector3(lo.x, lo.y, hi.z)); // 4
            _verts.Add(new Vector3(hi.x, lo.y, hi.z)); // 5
            _verts.Add(new Vector3(hi.x, hi.y, hi.z)); // 6
            _verts.Add(new Vector3(lo.x, hi.y, hi.z)); // 7
            Quad(i + 0, i + 3, i + 2, i + 1); // -Z
            Quad(i + 5, i + 6, i + 7, i + 4); // +Z
            Quad(i + 4, i + 7, i + 3, i + 0); // -X
            Quad(i + 1, i + 2, i + 6, i + 5); // +X
            Quad(i + 3, i + 7, i + 6, i + 2); // +Y
            Quad(i + 4, i + 0, i + 1, i + 5); // -Y
        }

        /// <summary>A small pyramid on the face of the block at <paramref name="p"/> that <paramref name="dir"/> points
        /// out of: its square base lies on the face, its tip sticks out — an arrow you can read from any angle.</summary>
        private void AddArrow(Vector3 p, Vector3i dir)
        {
            var n = new Vector3(dir.X, dir.Y, dir.Z);
            var centre = p + new Vector3(0.5f, 0.5f, 0.5f) + n * 0.51f;
            var u = Mathf.Abs(n.y) > 0.5f ? Vector3.right : Vector3.up;
            var v = Vector3.Cross(n, u).normalized;
            u = Vector3.Cross(v, n).normalized;
            const float half = 0.2f, tip = 0.22f;
            int i = _verts.Count;
            _verts.Add(centre + (u + v) * half);
            _verts.Add(centre + (u - v) * half);
            _verts.Add(centre + (-u - v) * half);
            _verts.Add(centre + (-u + v) * half);
            _verts.Add(centre + n * tip);
            _tris.Add(i + 0); _tris.Add(i + 1); _tris.Add(i + 4);
            _tris.Add(i + 1); _tris.Add(i + 2); _tris.Add(i + 4);
            _tris.Add(i + 2); _tris.Add(i + 3); _tris.Add(i + 4);
            _tris.Add(i + 3); _tris.Add(i + 0); _tris.Add(i + 4);
            _tris.Add(i + 0); _tris.Add(i + 2); _tris.Add(i + 1);
            _tris.Add(i + 0); _tris.Add(i + 3); _tris.Add(i + 2);
        }

        private void Quad(int a, int b, int c, int d)
        {
            _tris.Add(a); _tris.Add(b); _tris.Add(c);
            _tris.Add(a); _tris.Add(c); _tris.Add(d);
        }
    }
}
