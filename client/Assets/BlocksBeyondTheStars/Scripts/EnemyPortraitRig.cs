// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Codex's enemy portraits (#3032). Every enemy is built in code — the flight scene's drone, UFO, cruiser and raider
    /// (<see cref="SpaceView"/>), the planet's scan drone, Guardian robot, hunter and robbers (<see cref="WorldEntities"/>) —
    /// so instead of shipping pictures the game draws its own: one camera far from the world (like
    /// <see cref="ShipPreviewRig"/>), one model at a time, one frame each into the kind's own small RenderTexture. When the
    /// last asked-for portrait is done the camera switches off and the model goes, so the rig costs nothing while the Codex
    /// is closed, adds nothing to the download, and a portrait always shows the real look.
    /// </summary>
    public sealed class EnemyPortraitRig : MonoBehaviour
    {
        private const int Width = 192, Height = 144;
        private const float Fov = 30f;

        // Far from any terrain, player and the ship preview (y 100000), so no other camera sees the model.
        private static readonly Vector3 Origin = new Vector3(0f, 100500f, 0f);

        private static EnemyPortraitRig _instance;

        private readonly Dictionary<string, RenderTexture> _portraits = new Dictionary<string, RenderTexture>();
        private readonly Queue<string> _pending = new Queue<string>();
        private Camera _cam;
        private Light _lamp;
        private GameObject _shown; // the model in front of the camera this frame

        /// <summary>The portrait of an enemy kind: a texture that holds the picture within a frame or two of the first ask.
        /// The rig is made on first use under <paramref name="host"/>.</summary>
        public static Texture Portrait(Transform host, string enemyKey)
        {
            if (_instance == null)
            {
                var go = new GameObject("EnemyPortraitRig");
                go.transform.SetParent(host, false);
                _instance = go.AddComponent<EnemyPortraitRig>();
            }

            return _instance.Get(enemyKey);
        }

        private Texture Get(string key)
        {
            if (_portraits.TryGetValue(key, out var rt) && rt != null)
            {
                return rt;
            }

            rt = new RenderTexture(Width, Height, 16) { name = "EnemyPortrait_" + key };
            _portraits[key] = rt;
            _pending.Enqueue(key);
            return rt;
        }

        /// <summary>Runs before the frame is drawn: the model of last frame was drawn into its texture, so it goes, and the
        /// next one in the queue takes its place in front of the camera.</summary>
        private void LateUpdate()
        {
            if (_shown != null)
            {
                Destroy(_shown);
                _shown = null;
            }

            if (_pending.Count == 0)
            {
                if (_cam != null && _cam.enabled)
                {
                    _cam.enabled = false;
                    _lamp.enabled = false;
                }

                return;
            }

            EnsureCamera();
            string key = _pending.Dequeue();
            _shown = BuildModel(key, transform);
            _shown.transform.position = Origin;
            _shown.transform.rotation = Quaternion.identity;
            Frame(_shown);
            _cam.targetTexture = _portraits[key];
            _cam.enabled = true;
            _lamp.enabled = true;
        }

        private static GameObject BuildModel(string key, Transform parent) => key switch
        {
            EnemyCatalog.SpaceDrone => SpaceView.BuildDroneModel(parent),
            EnemyCatalog.Ufo => SpaceView.BuildUfoModel(parent),
            EnemyCatalog.Cruiser => SpaceView.BuildCruiserModel(parent),
            EnemyCatalog.BanditShip => SpaceView.BuildBanditShipModel(parent), // the hand-built raider (its voxel hull is the server's)
            _ => WorldEntities.BuildPortraitModel(key, parent),
        };

        /// <summary>A three-quarter view from the front (every model faces +Z), far enough back that the whole model fits.</summary>
        private void Frame(GameObject model)
        {
            var bounds = new Bounds(model.transform.position, Vector3.zero);
            bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
            {
                if (any)
                {
                    bounds.Encapsulate(r.bounds);
                }
                else
                {
                    bounds = r.bounds;
                    any = true;
                }
            }

            float radius = Mathf.Max(0.3f, bounds.extents.magnitude);
            float dist = radius / Mathf.Sin(Fov * 0.5f * Mathf.Deg2Rad) * 1.05f;
            var dir = new Vector3(0.55f, 0.3f, 1f).normalized;
            _cam.transform.position = bounds.center + dir * dist;
            _cam.transform.LookAt(bounds.center);
            _cam.farClipPlane = dist + radius * 3f;
            _lamp.transform.position = bounds.center + new Vector3(radius * 1.6f, radius * 2.2f, radius * 2.4f);
            _lamp.range = radius * 10f;
        }

        private void EnsureCamera()
        {
            if (_cam != null)
            {
                return;
            }

            var camGo = new GameObject("EnemyPortraitCam");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.05f, 0.09f, 0.15f, 1f);
            _cam.fieldOfView = Fov;
            _cam.nearClipPlane = 0.05f;
            _cam.enabled = false;

            var lampGo = new GameObject("EnemyPortraitLight");
            lampGo.transform.SetParent(transform, false);
            _lamp = lampGo.AddComponent<Light>();
            _lamp.type = LightType.Point; // localized — lights nothing else in the scene
            _lamp.intensity = 2f;
            _lamp.enabled = false;
        }

        private void OnDestroy()
        {
            foreach (var rt in _portraits.Values)
            {
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
            }

            _portraits.Clear();
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
