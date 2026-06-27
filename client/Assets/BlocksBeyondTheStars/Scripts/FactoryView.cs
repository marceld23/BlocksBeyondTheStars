// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Renders the animated <b>machines</b> inside a factory (<see cref="FactoryList"/>) — overlaid on the
    /// static voxel housings the world mesher already draws. Each machine runs one of a few procedural
    /// archetypes (a piston <c>press</c> that hammers up/down, a <c>rotor</c> that spins, a <c>conveyor</c> of
    /// scrolling parts) with a pulsing status light, all driven entirely client-side. The factory's production
    /// terminal shows an "operate" prompt up close — crafting itself goes through the normal crafting menu and
    /// is gated to this factory's roster server-side. Mirrors <see cref="DoorView"/> / <see cref="StationDecorView"/>
    /// (server-authoritative entities animated locally). Animation is gated by camera proximity to stay cheap.
    /// </summary>
    public sealed class FactoryView : MonoBehaviour
    {
        public GameBootstrap Game;

        public static FactoryView Instance { get; private set; }

        private sealed class Machine
        {
            public string Archetype;
            public Vector3 World;         // anchor world pos (top-centre of the housing)
            public Transform Root;        // positioned by ScenePos each frame
            public Transform Mover;       // the moving part (piston head / rotor / conveyor band)
            public Transform[] Parts;     // conveyor cubes (else null)
            public Material StatusMat;    // pulsing status light
            public float Phase;           // per-machine animation phase offset
        }

        private sealed class Factory
        {
            public int Id;
            public string Name;
            public Vector3 Terminal;      // raw world pos
            public string[] Roster;
            public bool Claimable;
            public string OwnerId;
            public GameObject Go;
            public readonly List<Machine> Machines = new List<Machine>();
        }

        private readonly Dictionary<int, Factory> _factories = new Dictionary<int, Factory>();
        private bool _subscribed;

        private const float AnimRange = 42f;   // only animate machines within this distance of the camera
        private const float TerminalReach = 4f;
        private const float HumRange = 12f;     // play the working-machine hum within this distance
        private float _humTimer;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static Shader _litShader;

        private void Awake() => Instance = this;

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.FactoriesReceived += OnFactories;
                _subscribed = true;
            }

            var cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Game?.PlayerPosition ?? Vector3.zero;
            float t = Time.time;

            foreach (var f in _factories.Values)
            {
                foreach (var m in f.Machines)
                {
                    var basePos = Game != null ? Game.ScenePos(m.World.x, m.World.y, m.World.z) : m.World;
                    m.Root.position = basePos;

                    bool near = (basePos - camPos).sqrMagnitude < AnimRange * AnimRange;
                    if (m.Root.gameObject.activeSelf != near)
                    {
                        m.Root.gameObject.SetActive(near);
                    }

                    if (!near)
                    {
                        continue;
                    }

                    Animate(m, t);
                }
            }

            PlayWorkingHum(camPos);
        }

        /// <summary>Plays a positional machine hum on a slow loop while the player is near a running factory, so
        /// the moving machines actually sound like they're working.</summary>
        private void PlayWorkingHum(Vector3 camPos)
        {
            _humTimer -= Time.deltaTime;
            if (_humTimer > 0f || ClientAudio.Instance == null)
            {
                return;
            }

            foreach (var f in _factories.Values)
            {
                if (f.Machines.Count == 0)
                {
                    continue;
                }

                var m0 = f.Machines[0];
                Vector3 scene = Game != null ? Game.ScenePos(m0.World.x, m0.World.y, m0.World.z) : m0.World;
                if ((scene - camPos).sqrMagnitude < HumRange * HumRange)
                {
                    ClientAudio.Instance.At("factory_hum", scene, 1f, 0.6f);
                    _humTimer = 3.4f; // roughly the clip length, so it reads as a continuous loop
                    return;
                }
            }
        }

        private static void Animate(Machine m, float t)
        {
            float p = t * 2.4f + m.Phase;
            switch (m.Archetype)
            {
                case "rotor":
                    m.Mover.localRotation = Quaternion.Euler(0f, t * 220f + m.Phase * 57f, 0f);
                    break;
                case "conveyor":
                    if (m.Parts != null)
                    {
                        for (int i = 0; i < m.Parts.Length; i++)
                        {
                            float u = Mathf.Repeat(t * 0.6f + i / (float)m.Parts.Length, 1f);
                            m.Parts[i].localPosition = new Vector3(Mathf.Lerp(-1.1f, 1.1f, u), 0.05f, 0f);
                        }
                    }

                    break;
                default: // "press" — a piston head that hammers down and back up
                    float drop = Mathf.Abs(Mathf.Sin(p)) * 0.7f;
                    m.Mover.localPosition = new Vector3(0f, 0.7f - drop, 0f);
                    break;
            }

            if (m.StatusMat != null)
            {
                float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f + m.Phase);
                m.StatusMat.SetColor(ColorId, ShaderColor.Srgb(new Color(1f, 0.55f + 0.3f * pulse, 0.1f, 1f)));
            }
        }

        private void OnFactories(FactoryList msg)
        {
            var seen = new HashSet<int>();
            foreach (var nf in msg.Factories)
            {
                seen.Add(nf.Id);
                if (!_factories.TryGetValue(nf.Id, out var existing))
                {
                    _factories[nf.Id] = Build(nf);
                }
                else
                {
                    // Refresh mutable claim state (a fresh snapshot after someone claims it).
                    existing.Claimable = nf.Claimable;
                    existing.OwnerId = nf.OwnerId ?? string.Empty;
                }
            }

            if (_factories.Count > seen.Count)
            {
                var stale = new List<int>();
                foreach (var id in _factories.Keys)
                {
                    if (!seen.Contains(id)) stale.Add(id);
                }

                foreach (var id in stale)
                {
                    Destroy(_factories[id].Go);
                    _factories.Remove(id);
                }
            }
        }

        private Factory Build(NetFactory nf)
        {
            if (_litShader == null) _litShader = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");

            var go = new GameObject($"Factory {nf.Id}");
            go.transform.SetParent(transform, true);

            var f = new Factory
            {
                Id = nf.Id,
                Name = nf.Name ?? string.Empty,
                Terminal = new Vector3(nf.TerminalX, nf.TerminalY, nf.TerminalZ),
                Roster = nf.Roster ?? System.Array.Empty<string>(),
                Claimable = nf.Claimable,
                OwnerId = nf.OwnerId ?? string.Empty,
                Go = go,
            };

            int idx = 0;
            foreach (var nm in nf.Machines)
            {
                f.Machines.Add(BuildMachine(go.transform, nm, idx++));
            }

            return f;
        }

        private Machine BuildMachine(Transform parent, NetMachine nm, int idx)
        {
            var root = new GameObject($"Machine {nm.Archetype}").transform;
            root.SetParent(parent, true);

            var metal = new Material(_litShader) { color = ShaderColor.Srgb(new Color(0.34f, 0.36f, 0.40f)) };
            var hot = new Material(_litShader) { color = ShaderColor.Srgb(new Color(1f, 0.6f, 0.15f)) };

            Transform mover = null;
            Transform[] parts = null;

            switch (nm.Archetype)
            {
                case "rotor":
                {
                    var hub = new GameObject("Rotor").transform;
                    hub.SetParent(root, false);
                    hub.localPosition = new Vector3(0f, 0.4f, 0f);
                    for (int i = 0; i < 4; i++)
                    {
                        var spoke = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Strip(spoke);
                        spoke.transform.SetParent(hub, false);
                        spoke.transform.localRotation = Quaternion.Euler(0f, i * 45f, 0f);
                        spoke.transform.localScale = new Vector3(1.4f, 0.18f, 0.22f);
                        spoke.GetComponent<Renderer>().sharedMaterial = metal;
                    }

                    mover = hub;
                    break;
                }

                case "conveyor":
                {
                    var band = new GameObject("Band").transform;
                    band.SetParent(root, false);
                    parts = new Transform[4];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        Strip(box);
                        box.transform.SetParent(band, false);
                        box.transform.localScale = new Vector3(0.3f, 0.3f, 0.3f);
                        box.GetComponent<Renderer>().sharedMaterial = metal;
                        parts[i] = box.transform;
                    }

                    mover = band;
                    break;
                }

                default: // "press"
                {
                    // A fixed frame post + a piston head that hammers down between the posts.
                    var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Strip(head);
                    head.transform.SetParent(root, false);
                    head.transform.localScale = new Vector3(0.8f, 0.4f, 0.8f);
                    head.GetComponent<Renderer>().sharedMaterial = metal;
                    mover = head.transform;
                    break;
                }
            }

            // A small glowing status light cube on a stalk.
            var status = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Strip(status);
            status.transform.SetParent(root, false);
            status.transform.localPosition = new Vector3(0.6f, 1.0f, 0.6f);
            status.transform.localScale = Vector3.one * 0.18f;
            status.GetComponent<Renderer>().sharedMaterial = hot;

            return new Machine
            {
                Archetype = nm.Archetype ?? "press",
                World = new Vector3(nm.X, nm.Y, nm.Z),
                Root = root,
                Mover = mover,
                Parts = parts,
                StatusMat = hot,
                Phase = (idx * 1.7f) % 6.283f,
            };
        }

        private static void Strip(GameObject go)
        {
            var c = go.GetComponent<Collider>();
            if (c != null) Destroy(c);
        }

        /// <summary>The factory whose production terminal is within reach of a point (for the operate prompt /
        /// opening the roster-filtered crafting menu). Returns the factory id (0 if none) + its roster.</summary>
        public int NearestTerminal(Vector3 worldPos, float reach, out string[] roster, out string name)
        {
            int best = 0; float bestSq = reach * reach; roster = System.Array.Empty<string>(); name = string.Empty;
            foreach (var kv in _factories)
            {
                var f = kv.Value;
                Vector3 scene = Game != null ? Game.ScenePos(f.Terminal.x, f.Terminal.y, f.Terminal.z) : f.Terminal;
                float sq = (scene - worldPos).sqrMagnitude;
                if (sq <= bestSq)
                {
                    bestSq = sq; best = kv.Key; roster = f.Roster; name = f.Name;
                }
            }

            return best;
        }

        /// <summary>True when the player stands at a factory production terminal (used by the crafting menu to
        /// enable the factory station). The server still enforces the actual roster.</summary>
        public bool PlayerAtTerminal(out string[] roster)
        {
            roster = System.Array.Empty<string>();
            if (Game == null) return false;
            return NearestTerminal(Game.PlayerPosition, TerminalReach, out roster, out _) != 0;
        }

        /// <summary>The nearest claimable, not-yet-claimed factory terminal within reach (0 if none) — for the
        /// player's E-claim with an access code.</summary>
        public int NearestClaimable(Vector3 worldPos, float reach)
        {
            int best = 0; float bestSq = reach * reach;
            foreach (var kv in _factories)
            {
                var f = kv.Value;
                if (!f.Claimable || !string.IsNullOrEmpty(f.OwnerId)) continue;
                Vector3 scene = Game != null ? Game.ScenePos(f.Terminal.x, f.Terminal.y, f.Terminal.z) : f.Terminal;
                float sq = (scene - worldPos).sqrMagnitude;
                if (sq <= bestSq) { bestSq = sq; best = kv.Key; }
            }

            return best;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || Game == null || ScreenLabelLayer.Instance == null) return;

            int near = NearestTerminal(Game.PlayerPosition, TerminalReach + 0.6f, out _, out string name);
            if (near == 0 || !_factories.TryGetValue(near, out var f)) return;

            Vector3 scene = Game.ScenePos(f.Terminal.x, f.Terminal.y, f.Terminal.z);
            string label;
            Color colour;
            if (f.Claimable && string.IsNullOrEmpty(f.OwnerId))
            {
                label = Game.Localizer?.Get("ui.factory.claim_prompt") ?? "E: Claim with an access code";
                colour = UiKit.Ok;
            }
            else
            {
                label = Game.Localizer?.Get("ui.factory.prompt") ?? "Factory terminal — craft from the menu";
                colour = UiKit.Cyan;
            }

            ScreenLabelLayer.Instance.World(cam, scene + Vector3.up * 1.2f, label, colour);
        }

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null) Game.Network.FactoriesReceived -= OnFactories;
            if (Instance == this) Instance = null;
        }
    }
}
