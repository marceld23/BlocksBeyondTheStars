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
    /// static voxel housings the world mesher already draws (mounted on the housing front face, see
    /// <see cref="BuildMachine"/>). Each machine runs one of a few procedural
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
                    // A big flywheel mounted on the housing front, spinning about its axle (world Z).
                    m.Mover.localRotation = Quaternion.Euler(0f, 0f, t * 140f + m.Phase * 57f);
                    break;
                case "conveyor":
                    if (m.Parts != null)
                    {
                        for (int i = 0; i < m.Parts.Length; i++)
                        {
                            float u = Mathf.Repeat(t * 0.45f + i / (float)m.Parts.Length, 1f);
                            m.Parts[i].localPosition = new Vector3(Mathf.Lerp(-1.25f, 1.25f, u), 0.24f, 0f);
                        }
                    }

                    // The drive rollers at either end turn with the band.
                    if (m.Mover != null)
                    {
                        m.Mover.localRotation = Quaternion.Euler(0f, 0f, -t * 160f);
                    }

                    break;
                default: // "press" — a piston head that hammers down onto the anvil and back up
                    float drop = Mathf.Abs(Mathf.Sin(p)) * PressStroke;
                    m.Mover.localPosition = new Vector3(0f, PressTop - drop, PressZ);
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

        // Machine geometry lives on the FRONT face of the 3×3 housing (the −Z face, towards the hall door),
        // in local units relative to the anchor the server sends — the centre of the roof-top pipe block.
        // The housing top is therefore at local y −0.5 and its front face at local z −1.5; the housing is
        // always ≥ 4 tall, so anything within y −0.5 … −4.5 is guaranteed to hang on real housing. (#1052:
        // the parts used to sit INSIDE the pipe block — rotor spokes fully buried, half the press stroke
        // hidden — so all a player saw of a "machine" was the status light.)
        private const float FrontZ = -1.5f;
        private const float PressZ = FrontZ - 0.55f;
        private const float PressTop = -2.0f;    // piston head at the top of its stroke
        private const float PressStroke = 1.0f;  // how far it hammers down towards the anvil

        private Machine BuildMachine(Transform parent, NetMachine nm, int idx)
        {
            var root = new GameObject($"Machine {nm.Archetype}").transform;
            root.SetParent(parent, true);

            var metal = new Material(_litShader) { color = ShaderColor.Srgb(new Color(0.34f, 0.36f, 0.40f)) };
            var dark = new Material(_litShader) { color = ShaderColor.Srgb(new Color(0.16f, 0.17f, 0.20f)) };
            var accent = new Material(_litShader) { color = ShaderColor.Srgb(new Color(0.72f, 0.42f, 0.14f)) };
            var hot = new Material(_litShader) { color = ShaderColor.Srgb(new Color(1f, 0.6f, 0.15f)) };

            // Two mounting rails on the housing front that every archetype bolts onto.
            Box(root, "Rail L", new Vector3(-1.2f, -2.4f, FrontZ - 0.08f), new Vector3(0.16f, 3.4f, 0.16f), dark);
            Box(root, "Rail R", new Vector3(1.2f, -2.4f, FrontZ - 0.08f), new Vector3(0.16f, 3.4f, 0.16f), dark);

            Transform mover = null;
            Transform[] parts = null;

            switch (nm.Archetype)
            {
                case "rotor":
                {
                    // A vertical flywheel: hub axle, four spokes and an eight-segment rim, spinning about Z.
                    Box(root, "Bearing", new Vector3(0f, -2.3f, FrontZ - 0.15f), new Vector3(0.7f, 0.7f, 0.3f), dark);
                    var wheel = new GameObject("Wheel").transform;
                    wheel.SetParent(root, false);
                    wheel.localPosition = new Vector3(0f, -2.3f, FrontZ - 0.5f);
                    Box(wheel, "Axle", Vector3.zero, new Vector3(0.36f, 0.36f, 0.5f), metal);
                    for (int i = 0; i < 4; i++)
                    {
                        var spoke = Box(wheel, "Spoke", Vector3.zero, new Vector3(2.2f, 0.18f, 0.16f), metal);
                        spoke.localRotation = Quaternion.Euler(0f, 0f, i * 45f);
                    }

                    const float rimR = 1.1f;
                    for (int i = 0; i < 8; i++)
                    {
                        float ang = i * 45f;
                        float rad = ang * Mathf.Deg2Rad;
                        var seg = Box(wheel, "Rim", new Vector3(Mathf.Cos(rad) * rimR, Mathf.Sin(rad) * rimR, 0f), new Vector3(0.2f, 0.9f, 0.22f), accent);
                        seg.localRotation = Quaternion.Euler(0f, 0f, ang);
                    }

                    mover = wheel;
                    break;
                }

                case "conveyor":
                {
                    // A belt across the housing front at working height: a bed, two drive rollers and parts scrolling along it.
                    Box(root, "Bed", new Vector3(0f, -2.75f, FrontZ - 0.45f), new Vector3(3.0f, 0.14f, 0.7f), dark);
                    Box(root, "Belt", new Vector3(0f, -2.66f, FrontZ - 0.45f), new Vector3(2.8f, 0.06f, 0.6f), metal);
                    var rollers = new GameObject("Rollers").transform;
                    rollers.SetParent(root, false);
                    rollers.localPosition = new Vector3(0f, -2.75f, FrontZ - 0.45f);
                    Box(rollers, "Roller L", new Vector3(-1.45f, 0f, 0f), new Vector3(0.28f, 0.28f, 0.72f), accent);
                    Box(rollers, "Roller R", new Vector3(1.45f, 0f, 0f), new Vector3(0.28f, 0.28f, 0.72f), accent);

                    var band = new GameObject("Band").transform;
                    band.SetParent(root, false);
                    band.localPosition = new Vector3(0f, -2.75f, FrontZ - 0.45f);
                    parts = new Transform[4];
                    for (int i = 0; i < parts.Length; i++)
                    {
                        parts[i] = Box(band, "Part", new Vector3(0f, 0.24f, 0f), new Vector3(0.36f, 0.36f, 0.36f), metal);
                    }

                    mover = rollers;
                    break;
                }

                default: // "press"
                {
                    // A piston cylinder on top, a wide head hammering down between the rails onto an anvil.
                    Box(root, "Cylinder", new Vector3(0f, -1.15f, PressZ), new Vector3(0.6f, 1.0f, 0.6f), dark);
                    Box(root, "Anvil", new Vector3(0f, -3.55f, PressZ), new Vector3(1.6f, 0.34f, 0.9f), dark);
                    Box(root, "Anvil Top", new Vector3(0f, -3.35f, PressZ), new Vector3(1.2f, 0.08f, 0.7f), accent);
                    var head = Box(root, "Head", new Vector3(0f, PressTop, PressZ), new Vector3(1.4f, 0.4f, 0.8f), metal);
                    Box(head, "Rod", new Vector3(0f, 1.75f, 0f), new Vector3(0.2f, 3.0f, 0.35f), metal); // child of the scaled head → 0.28 × 1.2 × 0.28 m, retracting into the cylinder
                    mover = head;
                    break;
                }
            }

            // A glowing status light on the frame, top-right, where it reads from across the hall.
            Box(root, "Status", new Vector3(1.2f, -0.65f, FrontZ - 0.2f), Vector3.one * 0.22f, hot);

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

        /// <summary>A collider-less cube primitive parented under <paramref name="parent"/> (machine part).</summary>
        private static Transform Box(Transform parent, string name, Vector3 localPos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Strip(go);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
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
