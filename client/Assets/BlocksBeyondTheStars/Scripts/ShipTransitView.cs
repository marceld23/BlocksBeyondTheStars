// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In multiplayer, plays a short landing/launch animation of ANOTHER player's ship at a landing pad (item 38):
    /// when someone lands on the body you're on, you see their ship descend onto the pad; when they launch, you
    /// see it rise off the pad with engine fire. Purely cosmetic + client-local — the server only sends the event
    /// (who, where, landing vs launching, hull colour). Your own landing/launch is the flight-view sequence.
    /// </summary>
    public sealed class ShipTransitView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Duration = 3.6f;
        private const float HighOffset = 60f;   // how far up the ship starts (land) / ends (launch)
        private const float PadOffset = 1.4f;   // resting height of the hull above the ground

        private sealed class Transit
        {
            public Transform Root;
            public Light Engine;
            public float T;
            public bool Landing;
            public float GroundY;
            public bool Dusted; // pad dust fired (touchdown / lift-off moment)
        }

        private readonly List<Transit> _active = new();
        private bool _subscribed;

        private void Update()
        {
            if (Game?.Network == null)
            {
                return;
            }

            if (!_subscribed)
            {
                Game.Network.ShipTransitReceived += OnTransit;
                _subscribed = true;
            }

            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var tr = _active[i];
                tr.T += Time.deltaTime / Duration;
                if (tr.Root == null || tr.T >= 1f)
                {
                    if (tr.Root != null) Destroy(tr.Root.gameObject);
                    _active.RemoveAt(i);
                    continue;
                }

                // Landing decelerates to a soft touchdown (ease-out); launch accelerates away (ease-in).
                float e = tr.Landing ? 1f - Mathf.Pow(1f - tr.T, 3f) : tr.T * tr.T * tr.T;
                float hi = tr.GroundY + HighOffset, lo = tr.GroundY + PadOffset;
                float y = tr.Landing ? Mathf.Lerp(hi, lo, e) : Mathf.Lerp(lo, hi, e);
                var p = tr.Root.position;
                tr.Root.position = new Vector3(p.x, y, p.z);

                // Engine fire is strongest at the thrust-heavy end (near the ground for both phases) and fades out.
                float near = tr.Landing ? tr.T : 1f - tr.T;
                if (tr.Engine != null)
                {
                    tr.Engine.intensity = 2.5f * near;

                    // #2157: visible engine fire under every ship — the voxel ship used to land with no flame at all
                    // (only the fallback silhouette had a flame cube): a downward stream of glow + a ground glow.
                    var nozzle = tr.Engine.transform.position;
                    int n = Mathf.RoundToInt(3f * near * FxKit.Density) + (near > 0.2f ? 1 : 0);
                    for (int k = 0; k < n; k++)
                    {
                        FxKit.Emit(FxKit.Kind.Glow, nozzle + Random.insideUnitSphere * 0.3f, Vector3.down * Random.Range(6f, 12f),
                            Random.Range(0.4f, 0.8f), Random.Range(0.12f, 0.22f), Color.Lerp(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.9f, 0.6f), Random.value));
                    }
                }

                // Pad dust: a ground burst right at touchdown (landing) / at lift-off (launch start).
                bool dustMoment = tr.Landing ? tr.T >= 0.93f : tr.T <= 0.05f;
                if (!tr.Dusted && dustMoment)
                {
                    tr.Dusted = true;
                    var ground = new Vector3(tr.Root.position.x, tr.GroundY + 0.6f, tr.Root.position.z);
                    DustBurst(ground);
                    FxKit.Ring(ground, Vector3.up, new Color(1f, 0.75f, 0.45f), 0.6f, 7f, 0.6f, thickness: 0.12f, fill: 0.06f, intensity: 1.6f);
                }
            }
        }

        /// <summary>A radial pad-dust burst kicked up by the thrusters (#2157: soft particle puffs racing outward along
        /// the ground — was 8 opaque cubes).</summary>
        private static void DustBurst(Vector3 pos)
        {
            int n = FxKit.Scaled(24);
            for (int i = 0; i < n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a));
                FxKit.Emit(FxKit.Kind.Dust, pos, dir * Random.Range(4f, 8f), Random.Range(0.5f, 1f), Random.Range(0.8f, 1.4f), new Color(0.62f, 0.57f, 0.5f, 0.8f));
            }
        }

        private void OnDisable()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ShipTransitReceived -= OnTransit;
                _subscribed = false;
            }
        }

        private void OnTransit(BlocksBeyondTheStars.Networking.Messages.ShipTransitFx fx)
        {
            // Only relevant while walking the surface — not in the flight view / menus.
            if (Game == null || Game.SpaceViewActive)
            {
                return;
            }

            float startY = fx.Landing ? fx.Y + HighOffset : fx.Y + PadOffset;
            var pos = new Vector3(Game.SceneX(fx.X), startY, Game.SceneZ(fx.Z));

            // The mover's REAL voxel ship when its design is cached (the server sends it ahead of the
            // FX), painted in their hull colour (item 32); the hand-built silhouette in the same colour
            // stays as the fallback.
            int hullRgb = fx.Hull != 0 ? fx.Hull : 0xD1D6E0; // 0 = never set — the default steel tint
            var hull = new Color(((hullRgb >> 16) & 0xFF) / 255f, ((hullRgb >> 8) & 0xFF) / 255f, (hullRgb & 0xFF) / 255f);
            GameObject root = null;
            Light engine = null;
            var design = Game.RemoteShipDesignFor(fx.PlayerId);
            if (ShipMeshBuilder.HasDesign(design))
            {
                root = new GameObject("ShipTransit");
                root.transform.position = pos;
                if (ShipMeshBuilder.BuildVoxelShip(Game, root.transform, design, out _, hull) != null)
                {
                    engine = AddEngineGlow(root.transform);
                }
                else
                {
                    Destroy(root); // atlas/material not ready — fall back below
                    root = null;
                }
            }

            if (root == null)
            {
                root = BuildShip(pos, hull, out engine);
            }

            _active.Add(new Transit { Root = root.transform, Engine = engine, T = 0f, Landing = fx.Landing, GroundY = fx.Y });
            ClientAudio.Instance?.Cue("ship_launch"); // thruster roar (same cue your own launch uses)
        }

        /// <summary>The warm under-hull thruster glow shared by both the voxel and the fallback ship.</summary>
        private static Light AddEngineGlow(Transform parent)
        {
            var lightGo = new GameObject("EngineGlow");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = new Vector3(0f, -1.4f, -1.8f);
            var engine = lightGo.AddComponent<Light>();
            FxLightBridge.Mirror(engine); // #2151: URP additional lights are off — light the pad through the FX lights
            engine.type = LightType.Point;
            engine.color = new Color(1f, 0.65f, 0.3f);
            engine.range = 14f;
            engine.intensity = 0f;
            return engine;
        }

        private static GameObject BuildShip(Vector3 pos, Color hull, out Light engine)
        {
            var root = new GameObject("ShipTransit");
            root.transform.position = pos;

            var shader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            var body = new Material(shader) { color = ShaderColor.Srgb(hull) };
            var glass = new Material(shader) { color = ShaderColor.Srgb(new Color(0.4f, 0.7f, 1f)) };

            Cube("Hull", root.transform, new Vector3(0f, 0f, 0f), new Vector3(2.4f, 1.1f, 4.2f), body);
            Cube("Nose", root.transform, new Vector3(0f, 0.1f, 2.6f), new Vector3(1.4f, 0.8f, 1.4f), body);
            Cube("Canopy", root.transform, new Vector3(0f, 0.6f, 1.1f), new Vector3(1.1f, 0.6f, 1.4f), glass);
            Cube("WingL", root.transform, new Vector3(-2.0f, -0.1f, -0.6f), new Vector3(2.0f, 0.3f, 2.0f), body);
            Cube("WingR", root.transform, new Vector3(2.0f, -0.1f, -0.6f), new Vector3(2.0f, 0.3f, 2.0f), body);

            // Engine block at the rear + a downward thruster flame (bright unlit) with a glow light.
            Cube("Engine", root.transform, new Vector3(0f, -0.1f, -2.2f), new Vector3(1.6f, 0.9f, 0.8f), body);
            var flameMat = new Material(Shader.Find("Unlit/Color") ?? shader) { color = ShaderColor.Srgb(new Color(1f, 0.6f, 0.2f)) };
            Cube("Flame", root.transform, new Vector3(0f, -0.9f, -1.8f), new Vector3(0.9f, 1.4f, 0.9f), flameMat);

            engine = AddEngineGlow(root.transform);
            return root;
        }

        private static void Cube(string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
