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

            _canvas = UiKit.CreateCanvas("Space Radar");
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

            int i = 0;
            foreach (var e in Game.Space.Entities)
            {
                var world = new Vector3(e.X, e.Y, e.Z);
                var dir = world - camPos;
                var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                if (v.magnitude > Radius)
                {
                    v = v.normalized * Radius;
                }

                bool station = e.Kind == "SpaceStation";
                if (station && dir.magnitude < nearestDist)
                {
                    nearestDist = dir.magnitude;
                    nearestStation = e.Name;
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

            for (; i < _blips.Count; i++)
            {
                if (_blips[i].gameObject.activeSelf)
                {
                    _blips[i].gameObject.SetActive(false);
                }
            }

            if (nearestStation != null)
            {
                _stationLabel.text = $"{nearestStation} · {Mathf.RoundToInt(nearestDist)}m";
            }

            _stationLabel.gameObject.SetActive(nearestStation != null);
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
