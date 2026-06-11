using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Spacecraft.Client
{
    /// <summary>
    /// Space radar (M27 polish): a HUD minimap of nearby space entities while flying — colour-coded
    /// (white = neutral asteroids/NPCs, red = hostile drones/UFOs), placed by bearing relative to the
    /// flight camera (forward = up). Shown only in space; reads the authoritative <c>SpaceState</c>.
    /// Modern uGUI build (round face + pooled blips on a DPI-scaled overlay canvas).
    /// </summary>
    public sealed class SpaceRadar : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public SpaceView SpaceView; // source of the landable-body bearings (planets/moons to fly to)

        private const float Radius = 72f;
        private const float DefaultRange = 130f; // world units mapped to the radar edge (no radar module)

        private Canvas _canvas;
        private RectTransform _center;
        private Text _stationLabel;
        private readonly List<Image> _blips = new List<Image>();

        private void EnsureBuilt()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = UiKit.CreateDiegeticCanvas("Space Radar"); // visor HUD camera when active
            _canvas.sortingOrder = 10; // HUD level
            var root = _canvas.transform;

            // Round radar face, top-centre.
            var faceGo = new GameObject("Face", typeof(RectTransform));
            faceGo.transform.SetParent(root, false);
            var face = faceGo.GetComponent<RectTransform>();
            face.anchorMin = face.anchorMax = new Vector2(0.5f, 1f);
            face.pivot = new Vector2(0.5f, 1f);
            face.sizeDelta = new Vector2((Radius + 8f) * 2f, (Radius + 8f) * 2f);
            face.anchoredPosition = new Vector2(0f, -16f);
            var faceImg = faceGo.AddComponent<RawImage>();
            faceImg.texture = UiKit.RadarCircle;
            faceImg.raycastTarget = false;

            // A centred anchor that the ship marker + blips position around (+y = up/forward).
            var centerGo = new GameObject("Center", typeof(RectTransform));
            centerGo.transform.SetParent(face, false);
            _center = centerGo.GetComponent<RectTransform>();
            _center.anchorMin = _center.anchorMax = _center.pivot = new Vector2(0.5f, 0.5f);
            _center.sizeDelta = Vector2.zero;

            Dot(_center, new Vector2(0f, 0f), new Vector2(4f, 4f), UiKit.TextCol);          // ship at centre
            Dot(_center, new Vector2(0f, Radius - 3.5f), new Vector2(2f, 7f), UiKit.TextCol); // forward tick

            // Nearest-station readout under the radar.
            var labelGo = new GameObject("StationLabel", typeof(RectTransform));
            labelGo.transform.SetParent(root, false);
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 1f);
            lrt.pivot = new Vector2(0.5f, 1f);
            lrt.sizeDelta = new Vector2(280f, 22f);
            lrt.anchoredPosition = new Vector2(0f, -(16f + (Radius + 8f) * 2f + 4f));
            _stationLabel = labelGo.AddComponent<Text>();
            _stationLabel.font = UiKit.Font;
            _stationLabel.fontSize = 15;
            _stationLabel.color = new Color(0.4f, 0.85f, 1f);
            _stationLabel.alignment = TextAnchor.MiddleCenter;
            _stationLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            _stationLabel.raycastTarget = false;
        }

        private static Image Dot(Transform parent, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.AddComponent<Image>();
            img.sprite = UiKit.SolidSprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private void LateUpdate()
        {
            bool show = Game != null && Camera != null && Game.InSpace && Game.Space != null && !Game.MenuOpen;
            EnsureBuilt();
            if (_canvas.enabled != show)
            {
                _canvas.enabled = show;
            }

            if (!show)
            {
                return;
            }

            // The flight camera is parented to the (unrotated) space scene root, so its local
            // position + world right/forward give a stable frame for the entity bearings.
            var camPos = Camera.transform.localPosition;
            var camR = Camera.transform.right;
            var camF = Camera.transform.forward;

            // Radar range comes from the ship's radar module(s) (radar_array widens it).
            float range = Game.ShipCombat != null && Game.ShipCombat.RadarRange > 1f ? Game.ShipCombat.RadarRange : DefaultRange;
            float scale = Radius / range;

            string nearestStation = null;
            float nearestDist = float.MaxValue;
            float nearestUp = 0f; // station height relative to the ship — the radar's 2D disc drops it

            int i = 0;
            foreach (var e in Game.Space.Entities)
            {
                bool station = e.Kind == "SpaceStation";
                var world = new Vector3(e.X, e.Y, e.Z);
                var dir = world - camPos;
                var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                if (v.magnitude > Radius)
                {
                    // A station stays as a rim direction-marker (it's a fixed navigation point); an asteroid
                    // or enemy beyond radar range is simply not detected yet — don't paint a phantom blip at
                    // the rim where nothing actually is.
                    if (!station)
                    {
                        continue;
                    }

                    v = v.normalized * Radius;
                }
                if (station && dir.magnitude < nearestDist)
                {
                    nearestDist = dir.magnitude;
                    nearestStation = e.Name;
                    nearestUp = world.y - camPos.y;
                }

                var blip = Blip(i++);
                blip.rectTransform.anchoredPosition = v; // +y already maps to up/forward
                blip.rectTransform.sizeDelta = station ? new Vector2(9f, 9f) : new Vector2(6f, 6f);
                blip.color = station ? new Color(0.4f, 0.85f, 1f)
                    : e.Kind == "ResourceDrop" ? new Color(0.5f, 0.9f, 1f)
                    : e.Hostile ? new Color(1f, 0.35f, 0.35f)
                    : new Color(0.9f, 0.95f, 1f);
                blip.gameObject.SetActive(true);
            }

            // Landable planets/moons: a green bearing marker each, clamped to the rim so a far body reads as
            // a direction arrow ("that way"). Helps you navigate the system from the cockpit.
            string nearestBody = null;
            float nearestBodyDist = float.MaxValue;
            if (SpaceView != null)
            {
                foreach (var body in SpaceView.Landables)
                {
                    var dir = body.Pos - camPos;
                    var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                    bool offEdge = v.magnitude > Radius;
                    if (offEdge)
                    {
                        v = v.normalized * Radius; // pin to the rim → a direction arrow toward the planet
                    }

                    if (dir.magnitude < nearestBodyDist)
                    {
                        nearestBodyDist = dir.magnitude;
                        nearestBody = body.Name;
                    }

                    var blip = Blip(i++);
                    blip.rectTransform.anchoredPosition = v;
                    blip.rectTransform.sizeDelta = new Vector2(10f, 10f);
                    blip.color = new Color(0.45f, 1f, 0.55f); // green = a planet/moon you can land on
                    blip.gameObject.SetActive(true);
                }
            }

            for (; i < _blips.Count; i++)
            {
                if (_blips[i].gameObject.activeSelf)
                {
                    _blips[i].gameObject.SetActive(false);
                }
            }

            // Readout under the radar: prefer a station name (dockable), else the nearest planet to head for.
            if (nearestStation != null)
            {
                // The disc is flat — an arrow says "it's above/below you" so a station parked over the
                // flight plane isn't searched for at eye level.
                string vert = nearestUp > 10f ? " ▲" : nearestUp < -10f ? " ▼" : string.Empty;
                _stationLabel.text = $"{nearestStation} · {Mathf.RoundToInt(nearestDist)}m{vert}";
            }
            else if (nearestBody != null)
            {
                _stationLabel.text = $"➜ {nearestBody} · {Mathf.RoundToInt(nearestBodyDist)}m";
            }

            _stationLabel.gameObject.SetActive(nearestStation != null || nearestBody != null);
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }

        private Image Blip(int index)
        {
            while (index >= _blips.Count)
            {
                _blips.Add(Dot(_center, Vector2.zero, new Vector2(6f, 6f), Color.white));
            }

            return _blips[index];
        }
    }
}
