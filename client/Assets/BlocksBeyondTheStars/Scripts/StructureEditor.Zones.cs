// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A landmark's RUIN ZONES and its SEAT in the town editor's landmark form (the school club's landmarks, terrain generation 39).
    /// <list type="bullet">
    /// <item><b>Ruin zones</b> (<see cref="StructureTemplate.RuinZones"/>): boxes of the design that fall with a kind and a strength
    /// of decay of their own — a lattice tower on a masonry pyramid. The form lists them (add, remove, the two corners as numbers,
    /// the kind, the strength); the room draws each as a coloured wire box (<see cref="EditorZoneFrames"/>). The <i>zone tool</i> of
    /// the palette sets a box from two clicks: they set the zone marked ◉, or — with none marked — add a new one. Where zones
    /// overlap the first one wins, as in the server's pass. The ruin preview builds through <see cref="BuildLandmarkTemplate"/>, so
    /// it honours them.</item>
    /// <item><b>Saving</b> clamps a zone that reaches past the design to the design's box (the cells outside it do not exist) and
    /// refuses a ruin whose zone lies wholly outside — the file always passes <see cref="LandmarkRules.ZoneProblems"/>.</item>
    /// <item><b>Seat</b>: on the ground (the guaranteed search) or a span over water or a valley (<see cref="LandmarkRules.SeatSpan"/>;
    /// the ground row is then its deck row).</item>
    /// </list>
    /// The fields the form does not show — <see cref="StructureTemplate.MinTerrainGeneration"/> and the legacy <c>protected</c> flag
    /// worlds below generation 39 read — go from the loaded file to the saved one.
    /// </summary>
    public sealed partial class StructureEditor
    {
        /// <summary>A ruin zone in a landmark or entry file (the server's <see cref="RuinZone"/> JSON).</summary>
        [Serializable] private sealed class ZoneJson { public int x0, y0, z0, x1, y1, z1; public string profile = string.Empty; public double strength; }

        private readonly List<RuinZone> _zones = new();

        /// <summary>The zone two clicks of the zone tool set; -1: they add a new one.</summary>
        private int _zoneSel = -1;

        /// <summary>The zone tool's first corner, waiting for the second click.</summary>
        private Vector3i? _zoneCorner;

        private EditorZoneFrames _zoneFrames;
        private string _lmSeat = string.Empty;

        /// <summary><see cref="StructureTemplate.MinTerrainGeneration"/> of the loaded landmark, written back unchanged.</summary>
        private int _lmMinGeneration;

        /// <summary>A zone's colour (its name in the form, its wire box in the room): hues a golden angle apart.</summary>
        internal static Color ZoneColor(int i) => Color.HSVToRGB(((i * 0.618034f) + 0.08f) % 1f, 0.7f, 1f);

        /// <summary>Forgets the landmark fields a load does not set (<see cref="LoadLandmark"/> sets them again).</summary>
        private void ResetLandmarkExtras()
        {
            _zones.Clear();
            _zoneSel = -1;
            _zoneCorner = null;
            _lmSeat = string.Empty;
            _lmMinGeneration = 0;
        }

        // ---------------------------------------------------------------- the seat

        private float BuildSeatRow(Transform p, float y)
        {
            RowLabel(p, y, L("ui.ed.landmark.seat"));
            ValueLabel(p, y, L(IsSpan(_lmSeat) ? "ui.ed.landmark.seat.span" : "ui.ed.landmark.seat.ground"));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                _lmSeat = IsSpan(_lmSeat) ? string.Empty : LandmarkRules.SeatSpan;
                RebuildUi();
            });
            return y + ValueRowStep;
        }

        private static bool IsSpan(string seat) => string.Equals(seat, LandmarkRules.SeatSpan, StringComparison.OrdinalIgnoreCase);

        // ---------------------------------------------------------------- the zone list

        /// <summary>The title with the count, "add a zone" (over the whole design), the hint, then one block per zone.</summary>
        private float BuildZoneRows(Transform p, float y)
        {
            SectionTitle(p, y, string.Format(L("ui.ed.zone.title"), _zones.Count, LandmarkRules.MaxRuinZones));
            y += 30f;
            if (_zones.Count < LandmarkRules.MaxRuinZones)
            {
                UiKit.AddFitButton(p, 16f, y, 348f, 34f, L("ui.ed.zone.add"), AddWholeDesignZone);
                y += 40f;
            }

            UiKit.AddMeasuredText(p, 16f, y, 348f, L("ui.ed.zone.hint"), 13, UiKit.CyanDim, out float h);
            y += h + 8f;
            for (int i = 0; i < _zones.Count; i++)
            {
                y = BuildZoneRow(p, y, i);
            }

            return y;
        }

        /// <summary>One zone: its name in its colour and Remove, the two corners, "two clicks set it", the kind and the strength.</summary>
        private float BuildZoneRow(Transform p, float y, int i)
        {
            var zone = _zones[i];
            int index = i;
            var name = UiKit.AddText(p, 16f, y, 200f, RowH, string.Format(L("ui.ed.zone.name"), i + 1), 16, ZoneColor(i), TextAnchor.MiddleLeft,
                FontStyle.Bold);
            UiKit.FitLabelSingleLine(name, 200f, UiKit.EditorMinFontSize, 16);
            UiKit.AddFitButton(p, 244f, y, 120f, RowH, L("ui.ed.zone.remove"), () => RemoveZone(index));
            y += 36f;
            y = ZoneCornerRow(p, y, "ui.ed.zone.from", zone, first: true);
            y = ZoneCornerRow(p, y, "ui.ed.zone.to", zone, first: false);
            UiKit.AddFitButton(p, 16f, y, 348f, 34f, (_zoneSel == i ? "◉ " : "○ ") + L("ui.ed.zone.select"), () =>
            {
                _zoneSel = _zoneSel == index ? -1 : index;
                _zoneCorner = null;
                RebuildUi();
            });
            y += 40f;
            RowLabel(p, y, L("ui.ed.landmark.profile"));
            ValueLabel(p, y, L(string.IsNullOrEmpty(zone.Profile) ? "ui.ed.zone.profile_own" : "ui.ed.landmark.profile." + zone.Profile));
            UiKit.AddFitButton(p, ArrowX, y + 3f, ArrowW, RowH, "→", () =>
            {
                zone.Profile = NextZoneProfile(zone.Profile);
                RebuildUi();
            });
            y += ValueRowStep;
            y = LandmarkNumber(p, y, "ui.ed.landmark.strength", StrengthText(zone.Strength),
                () => zone.Strength = StepStrength(zone.Strength, -1), () => zone.Strength = StepStrength(zone.Strength, +1));
            return y + 8f;
        }

        /// <summary>"From" or "To" and the corner's x, y and z fields; a typed number moves the wire box at once.</summary>
        private float ZoneCornerRow(Transform p, float y, string key, RuinZone zone, bool first)
        {
            var label = UiKit.AddText(p, 16f, y, 60f, RowH, L(key), 15, UiKit.CyanDim, TextAnchor.MiddleLeft);
            UiKit.FitLabelSingleLine(label, 60f, UiKit.EditorMinFontSize, 15);
            int[] values = first ? new[] { zone.X0, zone.Y0, zone.Z0 } : new[] { zone.X1, zone.Y1, zone.Z1 };
            for (int axis = 0; axis < 3; axis++)
            {
                int a = axis;
                var input = UiKit.AddInput(p, 80f + (a * 96f), y, 88f, RowH, values[a].ToString(CultureInfo.InvariantCulture), v =>
                {
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
                    {
                        SetCorner(zone, first, a, n);
                        SyncZoneBoxes();
                    }
                });
                input.contentType = InputField.ContentType.IntegerNumber;
            }

            return y + 36f;
        }

        private static void SetCorner(RuinZone z, bool first, int axis, int v)
        {
            switch (axis + (first ? 0 : 3))
            {
                case 0: z.X0 = v; break;
                case 1: z.Y0 = v; break;
                case 2: z.Z0 = v; break;
                case 3: z.X1 = v; break;
                case 4: z.Y1 = v; break;
                default: z.Z1 = v; break;
            }
        }

        /// <summary>The landmark's own kind (empty) → masonry → lattice → underground → the landmark's own.</summary>
        private static string NextZoneProfile(string current)
        {
            int i = Array.IndexOf(LandmarkRules.Profiles, current ?? string.Empty);
            return i + 1 < LandmarkRules.Profiles.Length ? LandmarkRules.Profiles[i + 1] : string.Empty;
        }

        /// <summary>A strength 0 (the kind's own) … 1 in steps of a tenth.</summary>
        private static double StepStrength(double s, int dir) => Math.Max(0.0, Math.Min(1.0, Math.Round((s * 10.0) + dir) / 10.0));

        private string StrengthText(double s)
            => s <= 0 ? L("ui.ed.landmark.strength_auto") : Math.Round(s * 100.0).ToString(CultureInfo.InvariantCulture) + " %";

        private static string CellText(Vector3i c)
            => string.Format(CultureInfo.InvariantCulture, "{0}, {1}, {2}", c.X, c.Y, c.Z);

        /// <summary>A zone over the whole design (the cells' box), marked ◉ so two clicks can shrink it straight away.</summary>
        private void AddWholeDesignZone()
        {
            var cells = DesignCells();
            if (cells.Count == 0)
            {
                SetStatus(L("ui.submit.empty"));
                return;
            }

            int x0 = int.MaxValue, y0 = int.MaxValue, z0 = int.MaxValue, x1 = 0, y1 = 0, z1 = 0;
            foreach (var c in cells)
            {
                (x0, y0, z0) = (Math.Min(x0, c.x), Math.Min(y0, c.y), Math.Min(z0, c.z));
                (x1, y1, z1) = (Math.Max(x1, c.x), Math.Max(y1, c.y), Math.Max(z1, c.z));
            }

            _zones.Add(new RuinZone { X0 = x0, Y0 = y0, Z0 = z0, X1 = x1, Y1 = y1, Z1 = z1 });
            _zoneSel = _zones.Count - 1;
            _zoneCorner = null;
            RebuildUi();
        }

        private void RemoveZone(int index)
        {
            if (index < 0 || index >= _zones.Count)
            {
                return;
            }

            _zones.RemoveAt(index);
            _zoneSel = _zoneSel == index ? -1 : _zoneSel > index ? _zoneSel - 1 : _zoneSel;
            _zoneCorner = null;
            RebuildUi();
        }

        // ---------------------------------------------------------------- the zone tool

        /// <summary>The cell a click of the zone tool names: the design cell under the cursor, else the free cell in front of it (or
        /// the floor's).</summary>
        private bool ZoneCornerCell(out Vector3i cell)
        {
            if (TryGetHitCell(out cell) && _design.ContainsKey(cell))
            {
                return true;
            }

            return TryGetTargetCell(out cell) && InBounds(cell);
        }

        /// <summary>Where a tool's ghost stands and whether it is green: the zone tool on its corner cell, every other tool on the
        /// design cell under the cursor it works on.</summary>
        private bool ToolGhostCell(string kind, out Vector3i cell)
            => kind == StructureStampRules.KindZone
                ? ZoneCornerCell(out cell)
                : TryGetHitCell(out cell) && _design.TryGetValue(cell, out var d) && ToolFits(kind, d);

        /// <summary>A click with the zone tool: the first corner, then the opposite one — which sets the zone marked ◉, else adds one.</summary>
        private void ZoneClick()
        {
            if (!_lmRuin)
            {
                SetStatus(L("ui.ed.zone.not_ruin"));
                return;
            }

            if (!ZoneCornerCell(out var cell))
            {
                return;
            }

            if (_zoneCorner is not { } first)
            {
                _zoneCorner = cell;
                SetStatus(string.Format(L("ui.ed.zone.corner"), CellText(cell)));
                SyncZoneBoxes();
                return;
            }

            _zoneCorner = null;
            int index = _zoneSel >= 0 && _zoneSel < _zones.Count ? _zoneSel : _zones.Count;
            if (index == _zones.Count)
            {
                if (_zones.Count >= LandmarkRules.MaxRuinZones)
                {
                    SetStatus(string.Format(L("ui.ed.zone.full"), LandmarkRules.MaxRuinZones));
                    SyncZoneBoxes();
                    return;
                }

                _zones.Add(new RuinZone());
            }

            var z = _zones[index];
            (z.X0, z.Y0, z.Z0) = (Math.Min(first.X, cell.X), Math.Min(first.Y, cell.Y), Math.Min(first.Z, cell.Z));
            (z.X1, z.Y1, z.Z1) = (Math.Max(first.X, cell.X), Math.Max(first.Y, cell.Y), Math.Max(first.Z, cell.Z));
            _status = string.Format(L("ui.ed.zone.set"), index + 1, CellText(new Vector3i(z.X0, z.Y0, z.Z0)), CellText(new Vector3i(z.X1, z.Y1, z.Z1)));
            RebuildUi();
        }

        // ---------------------------------------------------------------- template ↔ form

        /// <summary>The zones as the template stores them: each box with its corners ordered and clamped to the design's box
        /// (0 … size − 1 on each axis — the cells outside it do not exist). A zone wholly outside is left out;
        /// <paramref name="outside"/> names the first such (1-based), else 0.</summary>
        private List<RuinZone> ClampedZones(int w, int h, int l, out int outside)
        {
            outside = 0;
            var list = new List<RuinZone>(_zones.Count);
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (!ClampAxis(z.X0, z.X1, w, out int x0, out int x1) || !ClampAxis(z.Y0, z.Y1, h, out int y0, out int y1)
                    || !ClampAxis(z.Z0, z.Z1, l, out int z0, out int z1))
                {
                    outside = outside == 0 ? i + 1 : outside;
                    continue;
                }

                list.Add(new RuinZone
                {
                    X0 = x0, Y0 = y0, Z0 = z0, X1 = x1, Y1 = y1, Z1 = z1,
                    Profile = z.Profile ?? string.Empty, Strength = Math.Max(0.0, Math.Min(1.0, z.Strength)),
                });
            }

            return list;
        }

        private static bool ClampAxis(int a, int b, int size, out int lo, out int hi)
        {
            lo = Math.Max(0, Math.Min(a, b));
            hi = Math.Min(size - 1, Math.Max(a, b));
            return lo <= hi;
        }

        private static ZoneJson ToZoneJson(RuinZone z) => new ZoneJson
        {
            x0 = z.X0, y0 = z.Y0, z0 = z.Z0, x1 = z.X1, y1 = z.Y1, z1 = z.Z1, profile = z.Profile ?? string.Empty, strength = z.Strength,
        };

        private static RuinZone FromZoneJson(ZoneJson j) => new RuinZone
        {
            X0 = j.x0, Y0 = j.y0, Z0 = j.z0, X1 = j.x1, Y1 = j.y1, Z1 = j.z1, Profile = j.profile ?? string.Empty, Strength = j.strength,
        };

        private static List<ZoneJson> ToZoneJsons(IEnumerable<RuinZone> zones)
        {
            var list = new List<ZoneJson>();
            foreach (var z in zones ?? Array.Empty<RuinZone>())
            {
                if (z != null)
                {
                    list.Add(ToZoneJson(z));
                }
            }

            return list;
        }

        private static List<RuinZone> FromZoneJsons(IEnumerable<ZoneJson> zones)
        {
            var list = new List<RuinZone>();
            foreach (var z in zones ?? Array.Empty<ZoneJson>())
            {
                if (z != null)
                {
                    list.Add(FromZoneJson(z));
                }
            }

            return list;
        }

        /// <summary>Copies a loaded template's zones into the form (each its own object: the form edits them in place).</summary>
        private void LoadZones(IEnumerable<RuinZone> zones)
        {
            _zones.Clear();
            foreach (var z in zones ?? Array.Empty<RuinZone>())
            {
                if (z != null && _zones.Count < LandmarkRules.MaxRuinZones)
                {
                    _zones.Add(new RuinZone { X0 = z.X0, Y0 = z.Y0, Z0 = z.Z0, X1 = z.X1, Y1 = z.Y1, Z1 = z.Z1, Profile = z.Profile ?? string.Empty, Strength = z.Strength });
                }
            }

            _zoneSel = -1;
            _zoneCorner = null;
        }

        /// <summary>Draws the zones (and a waiting first corner) while a landmark that stands as a ruin is in the form; nothing
        /// otherwise. Cheap to call every UI build: the frames are rebuilt only when what they show changed.</summary>
        private void SyncZoneBoxes()
        {
            bool show = _landmarkMode && _lmRuin && !_entryMode;
            if (_zoneFrames == null)
            {
                if (!show)
                {
                    return;
                }

                _zoneFrames = new EditorZoneFrames(transform);
            }

            int next = _zoneSel >= 0 && _zoneSel < _zones.Count ? _zoneSel : _zones.Count;
            _zoneFrames.Show(show ? _zones : null, _zoneSel, show ? _zoneCorner : null, next);
        }
    }

    /// <summary>
    /// The structure editor's ruin zones in the room (the school club's landmarks): one wire box per zone in its colour
    /// (<see cref="StructureEditor.ZoneColor"/>) — twelve thin bars from its lower corner to the far side of its upper corner cell,
    /// thicker for the zone the zone tool sets — and a unit box on the zone tool's first corner. Each box is one mesh without a
    /// collider (never in the picking ray's way), rebuilt only when the zones change.
    /// </summary>
    internal sealed class EditorZoneFrames
    {
        private const float Edge = 0.16f, SelectedEdge = 0.34f;

        private readonly Transform _parent;
        private readonly Dictionary<int, Material> _materials = new();
        private readonly List<Mesh> _meshes = new();
        private GameObject _root;
        private string _shown = string.Empty;

        public EditorZoneFrames(Transform parent) => _parent = parent;

        /// <summary>Shows <paramref name="zones"/> (null: none) with <paramref name="selected"/> drawn thicker, and the first corner
        /// in the colour of the zone it will set (<paramref name="cornerZone"/>).</summary>
        public void Show(IReadOnlyList<RuinZone> zones, int selected, Vector3i? corner, int cornerZone)
        {
            string signature = Signature(zones, selected, corner, cornerZone);
            if (signature == _shown)
            {
                return;
            }

            _shown = signature;
            Clear();
            if (signature.Length == 0)
            {
                return;
            }

            _root = new GameObject("RuinZoneFrames");
            _root.transform.SetParent(_parent, false);
            for (int i = 0; zones != null && i < zones.Count; i++)
            {
                var z = zones[i];
                var min = new Vector3(Math.Min(z.X0, z.X1), Math.Min(z.Y0, z.Y1), Math.Min(z.Z0, z.Z1));
                var max = new Vector3(Math.Max(z.X0, z.X1) + 1, Math.Max(z.Y0, z.Y1) + 1, Math.Max(z.Z0, z.Z1) + 1);
                AddFrame(min, max, i == selected ? SelectedEdge : Edge, i);
            }

            if (corner is { } c)
            {
                var at = new Vector3(c.X, c.Y, c.Z);
                AddFrame(at, at + Vector3.one, SelectedEdge, cornerZone);
            }
        }

        private static string Signature(IReadOnlyList<RuinZone> zones, int selected, Vector3i? corner, int cornerZone)
        {
            if ((zones == null || zones.Count == 0) && corner == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            sb.Append(selected).Append('|');
            foreach (var z in zones ?? Array.Empty<RuinZone>())
            {
                sb.Append(z.X0).Append(',').Append(z.Y0).Append(',').Append(z.Z0).Append(',')
                    .Append(z.X1).Append(',').Append(z.Y1).Append(',').Append(z.Z1).Append(';');
            }

            if (corner is { } c)
            {
                sb.Append('@').Append(c.X).Append(',').Append(c.Y).Append(',').Append(c.Z).Append('#').Append(cornerZone);
            }

            return sb.ToString();
        }

        /// <summary>One wire box from <paramref name="min"/> to <paramref name="max"/> (world space) as one mesh.</summary>
        private void AddFrame(Vector3 min, Vector3 max, float t, int colour)
        {
            var verts = new List<Vector3>(12 * 8);
            var tris = new List<int>(12 * 36);
            var size = max - min;
            var centre = (min + max) / 2f;
            foreach (float y in new[] { min.y, max.y })
            {
                foreach (float z in new[] { min.z, max.z })
                {
                    AddBar(verts, tris, new Vector3(centre.x, y, z), new Vector3(size.x + t, t, t));
                }

                foreach (float x in new[] { min.x, max.x })
                {
                    AddBar(verts, tris, new Vector3(x, y, centre.z), new Vector3(t, t, size.z + t));
                }
            }

            foreach (float x in new[] { min.x, max.x })
            {
                foreach (float z in new[] { min.z, max.z })
                {
                    AddBar(verts, tris, new Vector3(x, centre.y, z), new Vector3(t, size.y + t, t));
                }
            }

            var mesh = new Mesh { name = "RuinZoneFrame" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, new List<Vector2>(new Vector2[verts.Count]));
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            _meshes.Add(mesh);

            var go = new GameObject("Zone", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(_root.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(colour);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        /// <summary>A box (eight corners, twelve triangles) around <paramref name="centre"/>.</summary>
        private static void AddBar(List<Vector3> verts, List<int> tris, Vector3 centre, Vector3 size)
        {
            int b = verts.Count;
            var h = size / 2f;
            for (int i = 0; i < 8; i++)
            {
                verts.Add(centre + new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z));
            }

            int[] faces =
            {
                0, 2, 3, 0, 3, 1, // -z
                4, 5, 7, 4, 7, 6, // +z
                0, 4, 6, 0, 6, 2, // -x
                1, 3, 7, 1, 7, 5, // +x
                0, 1, 5, 0, 5, 4, // -y
                2, 6, 7, 2, 7, 3, // +y
            };
            foreach (int f in faces)
            {
                tris.Add(b + f);
            }
        }

        private Material MaterialFor(int colour)
        {
            if (_materials.TryGetValue(colour, out var m))
            {
                return m;
            }

            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var c = StructureEditor.ZoneColor(colour);
            m = new Material(shader) { renderQueue = 3001, name = "RuinZone" + colour.ToString(CultureInfo.InvariantCulture) };
            m.SetColor("_Color", ShaderColor.Srgb(new Color(c.r, c.g, c.b, 0.85f)));
            _materials[colour] = m;
            return m;
        }

        private void Clear()
        {
            if (_root != null)
            {
                Object.Destroy(_root);
                _root = null;
            }

            foreach (var mesh in _meshes)
            {
                Object.Destroy(mesh);
            }

            _meshes.Clear();
        }

        public void Dispose()
        {
            Clear();
            foreach (var m in _materials.Values)
            {
                Object.Destroy(m);
            }

            _materials.Clear();
            _shown = string.Empty;
        }
    }
}
