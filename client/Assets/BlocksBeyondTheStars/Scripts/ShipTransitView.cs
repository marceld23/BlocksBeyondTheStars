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
    /// (who, where, landing vs launching, hull colour). Your own landing/launch is the flight-view sequence. Since #3177
    /// the parked hull itself flies whenever it is known (the Cinematic partial); the ghost below is the fallback.
    /// </summary>
    public sealed partial class ShipTransitView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const float Duration = 3.6f;
        private const float HighOffset = 60f;   // how far up the ship starts (land) / ends (launch)
        private const float PadOffset = 1.4f;   // resting height of the hull above the ground

        private sealed class Transit
        {
            public Transform Root;
            public Light Engine;
            public List<(Vector3 Local, float Size)> Exhausts; // #2162: the voxel ship's engines (root-local); empty → the light's spot
            public float T;
            public bool Landing;
            public float GroundY;
            public bool Dusted; // pad dust fired (touchdown / lift-off moment)
            public Color Dust;  // #3140: the colour of the ground under the pad
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
                    // #2162: out of the ship's real engines, bigger engines breathing bigger glow.
                    int engines = tr.Exhausts?.Count ?? 0;
                    int n = Mathf.RoundToInt(3f * near * FxKit.Density * Mathf.Sqrt(Mathf.Max(1, engines))) + (near > 0.2f ? 1 : 0);
                    for (int k = 0; k < n; k++)
                    {
                        var nozzle = tr.Engine.transform.position;
                        float size = 1f;
                        if (engines > 0)
                        {
                            var ex = tr.Exhausts[Random.Range(0, engines)];
                            nozzle = tr.Root.TransformPoint(ex.Local);
                            size = ex.Size;
                        }

                        FxKit.Emit(FxKit.Kind.Glow, nozzle + Random.insideUnitSphere * 0.3f * size, Vector3.down * Random.Range(6f, 12f),
                            Random.Range(0.4f, 0.8f) * size, Random.Range(0.12f, 0.22f), Color.Lerp(new Color(1f, 0.6f, 0.25f), new Color(1f, 0.9f, 0.6f), Random.value));
                    }
                }

                // Pad dust: a ground burst right at touchdown (landing) / at lift-off (launch start).
                bool dustMoment = tr.Landing ? tr.T >= 0.93f : tr.T <= 0.05f;
                if (!tr.Dusted && dustMoment)
                {
                    tr.Dusted = true;
                    var ground = new Vector3(tr.Root.position.x, tr.GroundY + 0.6f, tr.Root.position.z);
                    ShipCinematicFx.DustBurst(ground, 3f, tr.Dust); // #3140: dust in the colour of the ground it stands on
                    if (tr.Landing)
                    {
                        ClientAudio.Instance?.At("ship_touchdown", ground, 1f, 0.9f);
                    }
                }
            }
        }

        private void OnDisable()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.ShipTransitReceived -= OnTransit;
                _subscribed = false;
            }

            EndAllFlights(); // #3177: a hidden parked hull shows again, a stand-in goes
        }

        private void OnTransit(BlocksBeyondTheStars.Networking.Messages.ShipTransitFx fx)
        {
            // Only relevant while walking the surface — not in the flight view / menus.
            if (Game == null || Game.SpaceViewActive)
            {
                return;
            }

            if (fx.ApproachSeconds > 0f)
            {
                StartContrail(fx); // #3177: still high up — a glint and a contrail; the descent comes with the touchdown FX
                return;
            }

            if (QueueHullFlight(fx))
            {
                return; // #3177: the parked hull itself flies once it is built (or handed over); the ghost only if it never is
            }

            StartGhost(fx);
        }

        /// <summary>The fallback (item 38): a separate ghost of the ship slides down onto (or up off) the pad.</summary>
        private void StartGhost(BlocksBeyondTheStars.Networking.Messages.ShipTransitFx fx)
        {
            float startY = fx.Landing ? fx.Y + HighOffset : fx.Y + PadOffset;
            var pos = new Vector3(Game.SceneX(fx.X), startY, Game.SceneZ(fx.Z));

            // The mover's REAL voxel ship when its design is cached (the server sends it ahead of the
            // FX), painted in their hull colour (item 32); the hand-built silhouette in the same colour
            // stays as the fallback.
            int hullRgb = fx.Hull != 0 ? fx.Hull : 0xD1D6E0; // 0 = never set — the default steel tint
            var hull = new Color(((hullRgb >> 16) & 0xFF) / 255f, ((hullRgb >> 8) & 0xFF) / 255f, (hullRgb & 0xFF) / 255f);
            GameObject root = null;
            Light engine = null;
            List<(Vector3 Local, float Size)> exhausts = null;
            var design = Game.RemoteShipDesignFor(fx.PlayerId);
            if (ShipMeshBuilder.HasDesign(design))
            {
                root = new GameObject("ShipTransit");
                root.transform.position = pos;
                if (ShipMeshBuilder.BuildVoxelShip(Game, root.transform, design, out _, hull) != null)
                {
                    // #2162: fire + glow at the ship's engines (the voxel ship sits unscaled at the root's origin).
                    exhausts = ShipMeshBuilder.ExhaustPoints(Game.Content, design);
                    engine = AddEngineGlow(root.transform, GlowPoint(exhausts));
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

            var below = new BlocksBeyondTheStars.Shared.Geometry.Vector3i(Mathf.FloorToInt(fx.X), Mathf.FloorToInt(fx.Y) - 1, Mathf.FloorToInt(fx.Z));
            _active.Add(new Transit
            {
                Root = root.transform, Engine = engine, Exhausts = exhausts, T = 0f, Landing = fx.Landing, GroundY = fx.Y,
                Dust = ShipCinematicFx.GroundColor(Game, below),
            });
            ClientAudio.Instance?.Cue("ship_launch"); // thruster roar (same cue your own launch uses)
        }

        /// <summary>The fallback silhouette's under-hull glow spot.</summary>
        private static readonly Vector3 SilhouetteGlow = new Vector3(0f, -1.4f, -1.8f);

        /// <summary>Where the thruster glow light hangs for a voxel ship: a little below the middle of its engines
        /// (#2162), so it lights the pad under the stern.</summary>
        private static Vector3 GlowPoint(List<(Vector3 Local, float Size)> exhausts)
        {
            if (exhausts == null || exhausts.Count == 0)
            {
                return SilhouetteGlow;
            }

            var sum = Vector3.zero;
            foreach (var (local, _) in exhausts)
            {
                sum += local;
            }

            return sum / exhausts.Count + Vector3.down;
        }

        /// <summary>The warm thruster glow shared by both the voxel and the fallback ship.</summary>
        private static Light AddEngineGlow(Transform parent, Vector3 localPos)
        {
            var lightGo = new GameObject("EngineGlow");
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.localPosition = localPos;
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

            engine = AddEngineGlow(root.transform, SilhouetteGlow);
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
