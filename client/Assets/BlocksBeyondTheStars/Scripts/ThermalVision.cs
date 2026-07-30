// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Infrared mode of the upgraded optic (item <c>thermal_binoculars</c>, driven by <see cref="BinocularOptic"/>).
    /// Two layers:
    /// <list type="bullet">
    ///   <item>a full-screen grade (<c>BlocksBeyondTheStars/Thermal</c>) that turns the world cold, so warm
    ///   contacts pop — a camera-parented quad exactly like <see cref="HeatShimmer"/>, skipped under the
    ///   reduced-effects setting;</item>
    ///   <item><b>contact blobs</b> (<c>BlocksBeyondTheStars/ThermalMarker</c>): pooled additive quads drawn in
    ///   the Overlay queue with <c>ZTest Always</c>, so a heat signature stays visible THROUGH terrain, plus a
    ///   range tag per contact on the shared <see cref="ScreenLabelLayer"/>.</item>
    /// </list>
    ///
    /// Everything is read from state the client already holds: creatures, planet enemies (bandits + drones),
    /// NPCs and the planet POI list are all broadcast world-wide, so this needs no server round-trip and no new
    /// message. Lava is the exception — terrain only exists inside the streamed chunks, so a lava sweep is
    /// coarse, throttled and capped.
    ///
    /// Contacts further away than <see cref="MarkerRange"/> are pinned at that distance along their true bearing
    /// (their tag still reports the real range): a marker parked kilometres away would be clipped by the far
    /// plane and shimmer with float error, and "off-scale contact" is what a real scope shows anyway.
    /// </summary>
    public sealed class ThermalVision : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        /// <summary>Accessibility "reduced effects": skip the full-screen grade, keep the contacts.</summary>
        public bool ReducedEffects;

        /// <summary>Optional multiplayer presence source, for other players' heat signatures.</summary>
        public RemotePlayers Remotes;

        private const float MarkerRange = 220f;   // metres a marker may sit at before it is pinned to the bearing
        private const float LabelRange = 900f;    // beyond this a contact gets no range tag at all
        private const int MaxLabels = 16;         // tag clutter cap — nearest contacts win
        private const float LavaScanInterval = 1.0f;
        private const int LavaScanRadius = 48;    // horizontal sweep radius, in blocks
        private const int LavaScanHeight = 18;
        private const int LavaScanStep = 3;       // sample every Nth block — a lava lake is never one cell wide
        private const int LavaCellSize = 6;       // merge hits into cells this big so a lake is a few blobs
        private const int MaxLavaBlobs = 90;

        private static readonly int ThermalAmtId = Shader.PropertyToID("_ThermalAmt");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private bool _active;
        private float _amt;               // eased 0..1 so the mode fades in instead of snapping
        private Transform _quad;          // full-screen grade
        private Transform _root;          // parent for the contact blobs
        private Shader _markerShader;
        private readonly List<Marker> _pool = new List<Marker>();
        private int _used;
        private readonly List<Contact> _contacts = new List<Contact>();
        private readonly List<Vector3> _lavaBlobs = new List<Vector3>();
        private float _lavaTimer;

        private sealed class Marker
        {
            public GameObject Go;
            public Transform T;
            public Material Mat;
        }

        private struct Contact
        {
            public Vector3 Rel;     // camera-relative scene offset (already wrap-resolved)
            public float Dist;
            public Vector2 Size;    // blob width/height in metres
            public Color Tint;
            public string Label;    // empty = no range tag
        }

        /// <summary>Whether infrared mode is running. Set by <see cref="BinocularOptic"/>.</summary>
        public bool Active
        {
            get => _active;
            set
            {
                if (_active == value)
                {
                    return;
                }

                _active = value;
                if (value)
                {
                    _lavaTimer = 0f; // sweep for lava immediately, not a second after switching on
                }
            }
        }

        private void Update()
        {
            float target = _active && Game != null && Camera != null && !Game.SpaceViewActive ? 1f : 0f;
            _amt = Mathf.MoveTowards(_amt, target, Time.deltaTime * 4f);
            Shader.SetGlobalFloat(ThermalAmtId, ReducedEffects ? 0f : _amt);

            // Also bail while the rig is being torn down (world switch): the contact pass dereferences both.
            if (_amt <= 0.001f || Game == null || Camera == null)
            {
                ShowQuad(false);
                ReleaseMarkers();
                return;
            }

            UpdateGradeQuad();
            CollectContacts();
            DrawContacts();
        }

        // ---- full-screen grade ---------------------------------------------------------------------------

        private void UpdateGradeQuad()
        {
            if (ReducedEffects)
            {
                ShowQuad(false);
                return;
            }

            if (_quad == null)
            {
                var shader = Shader.Find("BlocksBeyondTheStars/Thermal");
                if (shader == null)
                {
                    return; // shader stripped/unavailable — the contact blobs still carry the mode
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "ThermalGrade";
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader);
                _quad = go.transform;
                _quad.SetParent(Camera.transform, false);
            }

            ShowQuad(true);

            // Fit the quad to the frustum just past the near plane. Recomputed every frame because the optic's
            // zoom changes the field of view — a quad sized for 60° leaves the frame uncovered at 10°.
            float z = Mathf.Max(Camera.nearClipPlane + 0.05f, 0.2f);
            float h = 2f * z * Mathf.Tan(Camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float w = h * Camera.aspect;
            _quad.localPosition = new Vector3(0f, 0f, z);
            _quad.localRotation = Quaternion.identity;
            _quad.localScale = new Vector3(w * 1.05f, h * 1.05f, 1f);
        }

        private void ShowQuad(bool on)
        {
            if (_quad != null && _quad.gameObject.activeSelf != on)
            {
                _quad.gameObject.SetActive(on);
            }
        }

        // ---- contacts ------------------------------------------------------------------------------------

        private void CollectContacts()
        {
            _contacts.Clear();
            var loc = Game.Localizer;

            // Fauna: wild animals run warm, a tamed companion reads friendly, a sleeping one has cooled down
            // and one held in stasis is ice cold — all flags the creature snapshot already carries.
            foreach (var c in Game.Creatures)
            {
                Color tint = c.Hostile ? new Color(1f, 0.42f, 0.16f) : new Color(1f, 0.72f, 0.22f);
                if (!string.IsNullOrEmpty(c.OwnerId))
                {
                    tint = new Color(0.36f, 1f, 0.48f);
                }

                if (c.Frozen)
                {
                    tint = new Color(0.55f, 0.82f, 1f);
                }
                else if (c.Asleep)
                {
                    tint *= 0.45f;
                }

                float s = Mathf.Max(0.9f, c.Size * 1.6f);
                string name = !string.IsNullOrEmpty(c.CustomName) ? c.CustomName
                    : !string.IsNullOrEmpty(c.Name) ? c.Name
                    : loc?.Get(c.NameKey) ?? string.Empty;
                Add(new Vector3(c.X, c.Y, c.Z), new Vector2(s, s), tint, name);
            }

            // Bandits, raiders and scan drones — the things you actually want to see before they see you.
            foreach (var e in Game.PlanetEnemies)
            {
                var tint = e.Hostile ? new Color(1f, 0.30f, 0.12f) : new Color(1f, 0.62f, 0.30f);
                float s = Mathf.Max(1.2f, e.Scale * 1.8f);
                Add(new Vector3(e.X, e.Y, e.Z), new Vector2(s, s * 1.2f), tint, e.Name);
            }

            // Settlement + station inhabitants.
            foreach (var n in Game.Npcs)
            {
                string name = !string.IsNullOrEmpty(n.Name) ? n.Name : loc?.Get(n.NameKey) ?? string.Empty;
                Add(new Vector3(n.X, n.Y, n.Z), new Vector2(1.2f, 1.8f), new Color(0.62f, 0.95f, 1f), name);
            }

            // Other players (multiplayer). Presence is the one contact class the server limits by distance, so
            // these simply stop appearing once a player is outside the streamed neighbourhood — as they must.
            if (Remotes != null)
            {
                foreach (var (name, scene) in Remotes.Contacts())
                {
                    AddScene(scene, new Vector2(1.3f, 2f), Color.white, name);
                }
            }

            // Structures: villages, factories, ruins, the wreck, revealed caches. The POI list is planet-wide
            // but carries no height, so these are drawn as tall columns at the player's own altitude — a bearing
            // beacon rather than a fake silhouette hovering over unloaded terrain.
            var eye = Game.PlayerPosition;
            var structure = new Color(0.95f, 0.45f, 1f);
            foreach (var p in Game.PlanetPois)
            {
                Add(new Vector3(p.X, eye.y, p.Z), new Vector2(7f, 30f), structure, p.Name);
            }

            foreach (var b in Game.Bases)
            {
                Add(new Vector3(b.X, b.Y, b.Z), new Vector2(6f, 22f), new Color(0.45f, 0.9f, 0.95f), b.Name);
            }

            if (Game.ShipPosition.HasValue)
            {
                var s = Game.ShipPosition.Value;
                Add(new Vector3(s.x, s.y, s.z), new Vector2(8f, 12f), new Color(0.55f, 0.85f, 1f), string.Empty);
            }

            // Lava: the only source that lives in the terrain rather than in an entity list, so it is swept
            // coarsely, merged into cells and capped — a lava sea would otherwise cost hundreds of blobs.
            _lavaTimer -= Time.deltaTime;
            if (_lavaTimer <= 0f)
            {
                _lavaTimer = LavaScanInterval;
                ScanLava();
            }

            string lavaLabel = loc?.Get("ui.optic.lava") ?? "Lava";
            foreach (var blob in _lavaBlobs)
            {
                Add(blob, new Vector2(LavaCellSize * 1.4f, LavaCellSize), new Color(1f, 0.32f, 0.06f), lavaLabel);
            }
        }

        /// <summary>Adds a contact from a canonical WORLD position (the wrap is resolved once, here, so the
        /// per-frame sort and draw never call back into the world mapping).</summary>
        private void Add(Vector3 world, Vector2 size, Color tint, string label)
            => AddScene(Game.ScenePos(world.x, world.y, world.z), size, tint, label);

        /// <summary>Adds a contact already expressed in scene space (remote avatars are placed by their own
        /// renderer, so re-deriving a world position for them would only add drift).</summary>
        private void AddScene(Vector3 scene, Vector2 size, Color tint, string label)
        {
            var rel = scene - Camera.transform.position;
            _contacts.Add(new Contact
            {
                Rel = rel,
                Dist = rel.magnitude,
                Size = size,
                Tint = tint,
                Label = label ?? string.Empty,
            });
        }

        private void ScanLava()
        {
            _lavaBlobs.Clear();
            var world = Game.World;
            var lava = Game.Content?.GetBlock("lava");
            if (world == null || lava == null)
            {
                return;
            }

            ushort lavaId = lava.NumericId.Value;
            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            var seen = new HashSet<Vector3Int>();

            for (int dy = -LavaScanHeight; dy <= LavaScanHeight; dy += LavaScanStep)
            for (int dz = -LavaScanRadius; dz <= LavaScanRadius; dz += LavaScanStep)
            for (int dx = -LavaScanRadius; dx <= LavaScanRadius; dx += LavaScanStep)
            {
                int wx = px + dx, wy = py + dy, wz = pz + dz;
                if (world.GetBlock(wx, wy, wz).Value != lavaId)
                {
                    continue;
                }

                var cell = new Vector3Int(
                    Mathf.FloorToInt(wx / (float)LavaCellSize),
                    Mathf.FloorToInt(wy / (float)LavaCellSize),
                    Mathf.FloorToInt(wz / (float)LavaCellSize));
                if (!seen.Add(cell))
                {
                    continue;
                }

                _lavaBlobs.Add(new Vector3(
                    (cell.x + 0.5f) * LavaCellSize,
                    (cell.y + 0.5f) * LavaCellSize,
                    (cell.z + 0.5f) * LavaCellSize));
                if (_lavaBlobs.Count >= MaxLavaBlobs)
                {
                    return;
                }
            }
        }

        // ---- drawing -------------------------------------------------------------------------------------

        private void DrawContacts()
        {
            EnsureRoot();
            _used = 0;
            var camPos = Camera.transform.position;
            var labels = ScreenLabelLayer.Instance;
            int labelled = 0;

            // Nearest first, so the label budget is spent on the contacts the player is actually looking for.
            _contacts.Sort((a, b) => a.Dist.CompareTo(b.Dist));

            foreach (var c in _contacts)
            {
                Vector3 rel = c.Rel;
                float dist = c.Dist;
                if (dist < 0.01f)
                {
                    continue;
                }

                // Pin off-scale contacts to the display range along their bearing (see the class summary).
                Vector3 pos = dist > MarkerRange ? camPos + rel * (MarkerRange / dist) : camPos + rel;
                float shrink = dist > MarkerRange ? MarkerRange / dist : 1f;

                var m = Acquire();
                m.T.position = pos;
                m.T.forward = rel / dist;
                // Off-scale blobs keep a floor size so a distant village stays a readable dot rather than a pixel.
                m.T.localScale = new Vector3(
                    Mathf.Max(c.Size.x * shrink, c.Size.x * 0.25f),
                    Mathf.Max(c.Size.y * shrink, c.Size.y * 0.25f),
                    1f);
                m.Mat.SetColor(ColorId, ShaderColor.Srgb(c.Tint * _amt));

                if (labels != null && labelled < MaxLabels && dist <= LabelRange && !string.IsNullOrEmpty(c.Label))
                {
                    labelled++;
                    labels.World(Camera, pos, $"{c.Label} · {Mathf.RoundToInt(dist)} m",
                        new Color(c.Tint.r, c.Tint.g, c.Tint.b, _amt));
                }
            }

            // Hide whatever the pool did not need this frame.
            for (int i = _used; i < _pool.Count; i++)
            {
                if (_pool[i].Go.activeSelf)
                {
                    _pool[i].Go.SetActive(false);
                }
            }
        }

        private void EnsureRoot()
        {
            if (_root == null)
            {
                var go = new GameObject("ThermalContacts");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }

            _markerShader ??= Shader.Find("BlocksBeyondTheStars/ThermalMarker") ?? Shader.Find("Unlit/Color");
        }

        private Marker Acquire()
        {
            if (_used < _pool.Count)
            {
                var reused = _pool[_used++];
                if (!reused.Go.activeSelf)
                {
                    reused.Go.SetActive(true);
                }

                return reused;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "ThermalContact";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            go.transform.SetParent(_root, true);
            var mat = new Material(_markerShader);
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;

            var m = new Marker { Go = go, T = go.transform, Mat = mat };
            _pool.Add(m);
            _used++;
            return m;
        }

        private void ReleaseMarkers()
        {
            foreach (var m in _pool)
            {
                if (m.Go != null && m.Go.activeSelf)
                {
                    m.Go.SetActive(false);
                }
            }

            _used = 0;
        }

        private void OnDisable()
        {
            Shader.SetGlobalFloat(ThermalAmtId, 0f); // never leave the world graded when we stop driving it
            _amt = 0f;
            _active = false;
            ShowQuad(false);
            ReleaseMarkers();
        }
    }
}
