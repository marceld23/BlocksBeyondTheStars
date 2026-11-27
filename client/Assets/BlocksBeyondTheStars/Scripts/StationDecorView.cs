// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// In-world decor for the ship's interactive stations (cockpit/room-identity pass): the cockpit
    /// gets a console with an animated screen and a holographic system map (built from the star-map
    /// data, visible when the player stands near), the medbay a softly pulsing heal-tank, the lab and
    /// ship console flickering terminal screens, and the workshop an occasional spark burst. Entirely
    /// code-built (DoorView pattern) and render-only — the server stays authoritative over what the
    /// stations DO. Decor follows <see cref="GameBootstrap.ScenePos"/> every frame (torus wrap).
    /// </summary>
    public sealed partial class StationDecorView : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private NetShipStation[] _builtFor;
        private readonly List<(GameObject Go, Vector3 World)> _decor = new();

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            if (!ReferenceEquals(_builtFor, Game.Stations))
            {
                Rebuild();
            }

            TurnPendingForms(); // the forms face their users once the blocks around them are known
        }

        /// <summary>Places the decor — in LateUpdate, after every Update moved what it follows (the player's scene copy, and
        /// since #3258 a flying hull: the transition director moves it in its Update, at a later execution order).</summary>
        private void LateUpdate()
        {
            if (Game == null)
            {
                return;
            }

            // #3258: while the own ship sinks onto its pad or lifts off in a transition cinematic, its decor rides with the
            // flying hull — the cockpit camera looks right at the console.
            Transform hull = null;
            Vector3 rest = default;
            var transitions = Game.Views.Transitions;
            bool ride = transitions != null && transitions.TryOwnShipRide(out hull, out rest);
            foreach (var (go, world) in _decor)
            {
                if (go == null)
                {
                    continue;
                }

                var at = Game.ScenePos(world.x, world.y, world.z);
                if (ride)
                {
                    go.transform.SetPositionAndRotation(hull.TransformPoint(at - rest), hull.rotation);
                }
                else
                {
                    go.transform.SetPositionAndRotation(at, Quaternion.identity);
                }
            }
        }

        private void Rebuild()
        {
            foreach (var (go, _) in _decor)
            {
                if (go != null)
                {
                    Destroy(go);
                }
            }

            _decor.Clear();
            _builtFor = Game.Stations;
            foreach (var s in _builtFor ?? System.Array.Empty<NetShipStation>())
            {
                GameObject go = s.Type switch
                {
                    "cockpit" => BuildCockpit(),
                    "medbay" => BuildMedbay(),
                    "lab" => BuildTerminal(new Color(0.35f, 1f, 0.60f)),    // lab reads green
                    "console" => BuildTerminal(new Color(0.40f, 0.85f, 1f)), // ship console reads cyan
                    "workshop" => BuildWorkshop(),
                    _ => null,
                };

                if (go == null)
                {
                    continue;
                }

                go.transform.SetParent(transform, false);
                // The station marker block fills its cell — the decor sits on top of it.
                _decor.Add((go, new Vector3(s.X, s.Y + 1f, s.Z)));
                QueueFormTurn(go, s); // the forms face whoever uses them
            }
        }

        // --- builders -------------------------------------------------------------------------

        /// <summary>The console form with its animated screen + the holographic system map over it. The map hangs on the
        /// form's turning child, so it leans toward the pilot whichever way the console faces (#4054).</summary>
        private GameObject BuildCockpit()
        {
            var root = new GameObject("CockpitDecor");
            AddForm(root.transform, StationDecorForms.Kind.Cockpit, new Color(0.30f, 0.75f, 0.95f)); // the console body

            var holo = new GameObject("HoloMap");
            holo.transform.SetParent(root.transform.Find(FormTurnName) ?? root.transform, false);
            holo.transform.localPosition = new Vector3(0f, StationDecorForms.HoloMapHeight, 0f);
            holo.transform.localRotation = Quaternion.Euler(StationDecorForms.HoloMapTilt, 0f, 0f);
            var map = holo.AddComponent<HoloMap>();
            map.Game = Game;

            return root;
        }

        /// <summary>The heal-tank: a translucent capsule that breathes a soft cyan glow.</summary>
        private GameObject BuildMedbay()
        {
            var root = new GameObject("MedbayDecor");
            var tank = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            tank.name = "HealTank";
            StripCollider(tank);
            tank.transform.SetParent(root.transform, false);
            tank.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            tank.transform.localScale = new Vector3(0.55f, 0.75f, 0.55f);
            var mat = Translucent(new Color(0.45f, 0.90f, 1f, 0.30f));
            tank.GetComponent<Renderer>().sharedMaterial = mat;
            var pulse = tank.AddComponent<GlowPulse>();
            pulse.Mat = mat;
            pulse.Base = new Color(0.45f, 0.90f, 1f, 0.30f);
            AddForm(root.transform, StationDecorForms.Kind.Medbay, default); // plinth, collar and cap
            return root;
        }

        /// <summary>A small terminal: housing, deck and a framed flickering coloured screen.</summary>
        private GameObject BuildTerminal(Color screenColor)
        {
            var root = new GameObject("TerminalDecor");
            AddForm(root.transform, StationDecorForms.Kind.Terminal, screenColor);
            return root;
        }

        /// <summary>The workshop spits a short orange spark burst every few seconds while the player is near.</summary>
        private GameObject BuildWorkshop()
        {
            var root = new GameObject("WorkshopDecor");
            root.AddComponent<SparkTicker>();
            return root;
        }

        // --- shared helpers (Unlit and StripCollider come from ModelKit) ----------------------

        private static GameObject Cube(Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            StripCollider(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        private static Material Translucent(Color c)
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Transparent");
            var mat = new Material(shader);
            mat.SetColor("_Color", ShaderColor.Srgb(c));
            mat.renderQueue = 3000;
            return mat;
        }

        private static bool CameraNear(Transform t, float dist)
        {
            var cam = Camera.main;
            return cam != null && (cam.transform.position - t.position).sqrMagnitude < dist * dist;
        }

        // --- animators ------------------------------------------------------------------------

        /// <summary>A display that breathes and occasionally blips brighter (Perlin-driven, no per-frame GC).</summary>
        private sealed class ScreenFlicker : MonoBehaviour
        {
            public Color Base;

            private Material _mat;
            private float _seed;

            private void Start()
            {
                _mat = GetComponent<Renderer>()?.sharedMaterial;
                _seed = (transform.position.x + transform.position.z) * 0.37f;
            }

            private void Update()
            {
                if (_mat == null || !CameraNear(transform, 24f))
                {
                    return;
                }

                float n = Mathf.PerlinNoise(Time.time * 2.2f, _seed);
                float k = 0.65f + 0.35f * n + (n > 0.92f ? 0.4f : 0f); // breathing + rare bright blip
                _mat.color = ShaderColor.Srgb(Base * k);
            }
        }

        /// <summary>A slow alpha breathing on a translucent material (the medbay tank glow).</summary>
        private sealed class GlowPulse : MonoBehaviour
        {
            public Material Mat;
            public Color Base;

            private void Update()
            {
                if (Mat == null || !CameraNear(transform, 24f))
                {
                    return;
                }

                var c = Base;
                c.a = Base.a * (0.75f + 0.25f * Mathf.Sin(Time.time * 1.6f));
                Mat.SetColor("_Color", ShaderColor.Srgb(c));
            }
        }

        /// <summary>Workshop ambience: a small orange spark burst every 4–7 s while the player is near.</summary>
        private sealed class SparkTicker : MonoBehaviour
        {
            private float _next;
            private Material _mat;

            private void Start()
            {
                _mat = Unlit(new Color(1f, 0.62f, 0.18f));
                _next = Time.time + 2f;
            }

            private void Update()
            {
                if (Time.time < _next || !CameraNear(transform, 18f))
                {
                    return;
                }

                _next = Time.time + Random.Range(4f, 7f);
                for (int i = 0; i < 4; i++)
                {
                    var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    StripCollider(p);
                    p.transform.position = transform.position + new Vector3(0f, 0.4f, 0f);
                    p.transform.localScale = Vector3.one * 0.06f;
                    p.GetComponent<Renderer>().sharedMaterial = _mat;
                    var bit = p.AddComponent<SparkBit>();
                    bit.Vel = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(1.0f, 2.2f), Random.Range(-1.2f, 1.2f));
                }
            }
        }

        /// <summary>A short-lived spark cube: arcs under gravity, shrinks, self-destroys.</summary>
        private sealed class SparkBit : MonoBehaviour
        {
            public Vector3 Vel;

            private const float Life = 0.5f;
            private float _t;

            private void Update()
            {
                _t += Time.deltaTime;
                Vel += Vector3.down * 9f * Time.deltaTime;
                transform.position += Vel * Time.deltaTime;
                transform.localScale = Vector3.one * 0.06f * Mathf.Max(0f, 1f - _t / Life);
                if (_t >= Life)
                {
                    Destroy(gameObject);
                }
            }
        }
    }
}
