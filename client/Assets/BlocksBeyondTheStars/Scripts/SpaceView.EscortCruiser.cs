// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The escort cruiser (#3900) — the space boss — in the flight view. On the wire it is an ordinary entity of kind
    /// <c>Cruiser</c>; it is drawn as the boss when a live <c>BossStatus</c> names its id
    /// (<see cref="BossBarState.IsSpaceBoss"/>) or, as a fallback, when it is as big as the boss
    /// (<see cref="EscortCruiserLook.IsEscortCruiser"/>). The model is the cruiser's Guardian look, clearly bigger and
    /// longer, with a row of red glowing windows along the bridge and a long barrel above them; the barrel's muzzle
    /// (<see cref="EscortCruiserMuzzleLocal"/>) is where the mega laser gathers and leaves (SpaceView.MegaLaser.cs).
    /// The model turns its barrel toward the pilot it hunts — presentation only, the server sends no heading for it. In
    /// its second phase the windows pulse brighter and the engines burn hotter. Its brackets, plumes and explosion
    /// follow the model's scale; the HUD's boss bar shows its hull, so no small health bar floats over it.
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>The barrel's muzzle in the boss model's own frame — effects start at
        /// <c>model.transform.TransformPoint(EscortCruiserMuzzleLocal)</c> (<see cref="EscortCruiserRig.Muzzle"/>).</summary>
        internal static readonly Vector3 EscortCruiserMuzzleLocal =
            new Vector3(EscortCruiserLook.MuzzleX, EscortCruiserLook.MuzzleY, EscortCruiserLook.MuzzleZ);

        private const float EscortWarpSize = 3f; // its warp flash against a trader's

        private static readonly Color EscortWindowCalm = new Color(0.75f, 0.04f, 0.03f);
        private static readonly Color EscortWindowHot = new Color(1f, 0.4f, 0.3f);
        private static readonly Color EscortEngineCalm = new Color(1f, 0.42f, 0.24f);
        private static readonly Color EscortEngineHot = new Color(1f, 0.88f, 0.66f);
        private static readonly Color EscortPlumeCalm = new Color(1f, 0.55f, 0.35f);
        private static readonly Color EscortPlumeHot = new Color(1f, 0.8f, 0.55f);
        private static readonly float[] EscortEngineX = { -0.8f, 0f, 0.8f };

        // The boss models in the scene by entity id. An entry whose model went (destroyed with the entity or the scene)
        // reads as null and is dropped on the next frame.
        private readonly Dictionary<string, EscortCruiserRig> _escortRigs = new Dictionary<string, EscortCruiserRig>();
        private readonly List<string> _escortGone = new List<string>();

        /// <summary>What a boss model knows about itself: its scale, its phase, the two materials that glow and its
        /// plumes. It sits on the model's root, so it goes with the model.</summary>
        internal sealed class EscortCruiserRig : MonoBehaviour
        {
            private readonly List<ParticleSystem> _plumes = new List<ParticleSystem>();
            private Material _windows;
            private Material _engines;
            private float _shown = -1f;

            /// <summary>The model's scale (the entity's wire scale).</summary>
            public float Scale { get; private set; } = 1f;

            /// <summary>The second phase began.</summary>
            public bool Enraged { get; private set; }

            /// <summary>The model was turned toward its quarry once (the first turn is a snap, the later ones are eased).</summary>
            public bool Faced { get; set; }

            /// <summary>The barrel's muzzle in the world.</summary>
            public Vector3 Muzzle => transform.TransformPoint(EscortCruiserMuzzleLocal);

            public void Init(Material windows, Material engines, float scale)
            {
                // Only a material with a colour of its own can pulse (the unlit fallback shader may have none).
                _windows = windows != null && windows.HasProperty("_Color") ? windows : null;
                _engines = engines != null && engines.HasProperty("_Color") ? engines : null;
                Scale = scale;
            }

            public void AddPlume(ParticleSystem plume)
            {
                if (plume != null)
                {
                    _plumes.Add(plume);
                }
            }

            /// <summary>Switches to the second phase (it never goes back): the engines burn hotter from here on, the
            /// windows pulse (<see cref="Update"/>). True when this call made the change.</summary>
            public bool SetEnraged(bool enraged)
            {
                if (!enraged || Enraged)
                {
                    return false;
                }

                Enraged = true;
                float size = EscortCruiserLook.PlumeSize(Scale, true);
                foreach (var plume in _plumes)
                {
                    if (plume == null)
                    {
                        continue;
                    }

                    var main = plume.main; // the same three values SpaceFx.EnginePlume sets, for the hotter jet
                    main.startSpeed = 6f * size;
                    main.startSize = new ParticleSystem.MinMaxCurve(0.15f * size, 0.36f * size);
                    main.startColor = FxKit.Lin(EscortPlumeHot);
                }

                return true;
            }

            private void Update()
            {
                float glow = EscortCruiserLook.Glow(Enraged, Time.time, UiKit.ReducedMotion);
                if (Mathf.Abs(glow - _shown) < 0.004f)
                {
                    return;
                }

                _shown = glow;
                if (_windows != null)
                {
                    _windows.color = ShaderColor.Srgb(Color.Lerp(EscortWindowCalm, EscortWindowHot, glow));
                }

                if (_engines != null)
                {
                    _engines.color = ShaderColor.Srgb(Color.Lerp(EscortEngineCalm, EscortEngineHot, glow));
                }
            }
        }

        /// <summary>The escort cruiser's model (also its Codex portrait): the cruiser's iron hull and Guardian plating,
        /// longer and heavier, a bridge with a row of red glowing windows, a long barrel above them pointing forward,
        /// three engines. It faces +Z; <paramref name="scale"/> is the model's scale. The parts are placed by
        /// <see cref="EscortCruiserLook"/>, so the muzzle is the barrel's real front end.</summary>
        internal static GameObject BuildEscortCruiserModel(Transform parent, float scale = 1f)
        {
            var root = new GameObject("EscortCruiser");
            root.transform.SetParent(parent, false);
            root.transform.localScale = Vector3.one * Mathf.Max(0.1f, scale);
            var hull = LitTex("iron_wall", new Color(0.7f, 0.55f, 0.55f));
            var dark = GuardianPlating(new Color(0.8f, 0.78f, 0.8f));
            var deep = GuardianPlating(new Color(0.56f, 0.55f, 0.58f));
            float calm = EscortCruiserLook.Glow(false, 0f, true);
            var windows = Unlit(Color.Lerp(EscortWindowCalm, EscortWindowHot, calm));
            var engines = Unlit(Color.Lerp(EscortEngineCalm, EscortEngineHot, calm));

            BuildEscortHull(root.transform, hull, dark, deep, Unlit(new Color(1f, 0.2f, 0.14f)));
            BuildEscortBridge(root.transform, dark, windows);
            BuildEscortBarrel(root.transform, dark, deep, windows);
            BuildEscortEngines(root.transform, dark, engines);
            root.AddComponent<EscortCruiserRig>().Init(windows, engines, scale);
            return root;
        }

        private static void BuildEscortHull(Transform t, Material hull, Material dark, Material deep, Material threat)
        {
            float w = EscortCruiserLook.HullWidth, h = EscortCruiserLook.HullHeight, len = EscortCruiserLook.HullLength;
            Cube("Hull", t, Vector3.zero, new Vector3(w, h, len), hull);
            Cube("Prow", t, new Vector3(0f, -0.1f, (len * 0.5f) + 0.45f), new Vector3(w * 0.62f, h * 0.7f, 0.9f), dark);
            Cube("ProwTip", t, new Vector3(0f, -0.15f, (len * 0.5f) + 1.1f), new Vector3(w * 0.34f, h * 0.45f, 0.5f), deep);
            Cube("Keel", t, new Vector3(0f, -(h * 0.5f) - 0.15f, -0.3f), new Vector3(w * 0.5f, 0.3f, len * 0.8f), deep);
            Cube("Spine", t, new Vector3(0f, (h * 0.5f) + 0.25f, -1.9f), new Vector3(1f, 0.5f, 3.6f), dark);
            Cube("Fin", t, new Vector3(0f, (h * 0.5f) + 0.95f, -2.7f), new Vector3(0.2f, 0.9f, 1.5f), deep);
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * w * 0.5f;
                Cube("Flank", t, new Vector3(x + (side * 0.14f), -0.1f, -0.5f), new Vector3(0.3f, h * 0.7f, len * 0.68f), dark);
                Cube("Wing", t, new Vector3(x + (side * 0.75f), 0f, -2.3f), new Vector3(1f, 0.16f, 2.2f), deep);
                // A thin red strip along each flank (it pokes a little out of the plating): the Guardian machines' threat mark.
                Cube("ThreatStrip", t, new Vector3(x + (side * 0.3f), 0.2f, -0.5f), new Vector3(0.03f, 0.08f, len * 0.62f), threat);
            }
        }

        /// <summary>The bridge block and its red windows: a row along each side, and three panes on the front — the side
        /// a hunted pilot sees.</summary>
        private static void BuildEscortBridge(Transform t, Material dark, Material windows)
        {
            float half = EscortCruiserLook.BridgeWidth * 0.5f;
            float y = EscortCruiserLook.WindowY;
            Cube("Bridge", t, new Vector3(0f, EscortCruiserLook.BridgeY, EscortCruiserLook.BridgeZ),
                new Vector3(EscortCruiserLook.BridgeWidth, EscortCruiserLook.BridgeHeight, EscortCruiserLook.BridgeLength), dark);
            var pane = new Vector3(0.06f, EscortCruiserLook.WindowHeight, EscortCruiserLook.WindowLength);
            for (int i = 0; i < EscortCruiserLook.WindowsPerSide; i++)
            {
                float z = EscortCruiserLook.WindowZ(i);
                Cube("Window", t, new Vector3(-half - 0.01f, y, z), pane, windows);
                Cube("Window", t, new Vector3(half + 0.01f, y, z), pane, windows);
            }

            float front = EscortCruiserLook.BridgeZ + (EscortCruiserLook.BridgeLength * 0.5f) + 0.01f;
            var frontPane = new Vector3(EscortCruiserLook.WindowLength, EscortCruiserLook.WindowHeight, 0.06f);
            for (int i = -1; i <= 1; i++)
            {
                Cube("Window", t, new Vector3(i * half * 0.6f, y, front), frontPane, windows);
            }
        }

        /// <summary>The long barrel above the windows: a mount on the bridge roof, the barrel itself, a sleeve, the
        /// muzzle ring and a glowing bore at its front end.</summary>
        private static void BuildEscortBarrel(Transform t, Material dark, Material deep, Material glow)
        {
            float y = EscortCruiserLook.BarrelY, z0 = EscortCruiserLook.BarrelStartZ;
            float len = EscortCruiserLook.BarrelLength, thick = EscortCruiserLook.BarrelThickness;
            Cube("BarrelMount", t, new Vector3(0f, y - 0.02f, z0 - 0.35f), new Vector3(0.85f, 0.5f, 1.7f), dark);
            Cube("BarrelRest", t, new Vector3(0f, y - 0.2f, z0 + 1.25f), new Vector3(0.3f, 0.12f, 0.5f), dark);
            Cube("Barrel", t, new Vector3(0f, y, z0 + (len * 0.5f)), new Vector3(thick, thick, len), deep);
            Cube("BarrelSleeve", t, new Vector3(0f, y, z0 + (len * 0.4f)), new Vector3(thick + 0.14f, thick + 0.14f, 0.5f), dark);
            Cube("MuzzleRing", t, new Vector3(0f, y, z0 + len - 0.2f), new Vector3(thick + 0.2f, thick + 0.2f, 0.4f), dark);
            Cube("MuzzleBore", t, new Vector3(0f, y, z0 + len + 0.01f), new Vector3(thick * 0.7f, thick * 0.7f, 0.04f), glow);
        }

        private static void BuildEscortEngines(Transform t, Material dark, Material glow)
        {
            float back = -(EscortCruiserLook.HullLength * 0.5f) - 0.4f;
            for (int i = 0; i < EscortEngineX.Length; i++)
            {
                var at = new Vector3(EscortEngineX[i], i == 1 ? 0.12f : -0.05f, back);
                Cube("EscortEngine", t, at, new Vector3(0.7f, 0.7f, 1.1f), dark);
                Cube("EscortEngineGlow", t, at + new Vector3(0f, 0f, -0.58f), new Vector3(0.46f, 0.46f, 0.1f), glow);
            }
        }

        /// <summary>A plume on each of the boss's engines, sized by its scale — and kept by the rig, which turns them up
        /// when it enrages. (The plain cruiser's two plumes come from <see cref="AttachHostilePlumes"/>, which finds no
        /// part of its names here.)</summary>
        private static void AttachEscortPlumes(GameObject model, EscortCruiserRig rig)
        {
            float size = EscortCruiserLook.PlumeSize(rig.Scale, rig.Enraged);
            var color = rig.Enraged ? EscortPlumeHot : EscortPlumeCalm;
            foreach (Transform child in model.transform)
            {
                if (child.name == "EscortEngine")
                {
                    var at = child.localPosition + (Vector3.back * ((child.localScale.z * 0.5f) + 0.1f));
                    rig.AddPlume(SpaceFx.EnginePlume(model.transform, at, size, color));
                }
            }
        }

        /// <summary>The model of an entity of kind <c>Cruiser</c>: the escort cruiser when it is the boss, else the plain
        /// cruiser as ever. Runs inside <see cref="BuildEntityModel"/>'s owned scope like every entity builder.</summary>
        private GameObject BuildCruiserEntityModel(Transform parent, NetCombatEntity e)
        {
            float now = Game.WorldTime;
            if (!EscortCruiserLook.IsEscortCruiser(e.Kind, e.Scale, Game.BossBar.IsSpaceBoss(e.Id, now)))
            {
                return BuildCruiserModel(parent);
            }

            var go = BuildEscortCruiserModel(parent, EscortCruiserLook.ModelScale(e.Scale));
            var rig = go.GetComponent<EscortCruiserRig>();
            rig.SetEnraged(Game.BossBar.PhaseOf(e.Id, now) >= EscortCruiserRules.PhaseEnraged); // a pilot who joins late sees the phase it is in
            AttachEscortPlumes(go, rig);
            _escortRigs[e.Id] = rig;
            return go;
        }

        /// <summary>Per frame: the mega laser's countdown, then every boss model — its phase from the boss status (the
        /// change gets a flash) and its barrel turned toward its quarry.</summary>
        private void UpdateEscortCruisers()
        {
            UpdateMegaLaserWarning();
            if (_escortRigs.Count == 0)
            {
                return;
            }

            float now = Game.WorldTime;
            _escortGone.Clear();
            foreach (var kv in _escortRigs)
            {
                var rig = kv.Value;
                if (rig == null)
                {
                    _escortGone.Add(kv.Key);
                    continue;
                }

                if (rig.SetEnraged(Game.BossBar.PhaseOf(kv.Key, now) >= EscortCruiserRules.PhaseEnraged))
                {
                    EscortEnrageFx(rig);
                }

                FaceEscortQuarry(kv.Key, rig);
            }

            foreach (string id in _escortGone)
            {
                _escortRigs.Remove(id);
            }
        }

        /// <summary>The moment it enrages: a red flash and a ring running out from the hull (VEGA says the rest).</summary>
        private static void EscortEnrageFx(EscortCruiserRig rig)
        {
            Vector3 at = rig.transform.position;
            float r = EscortCruiserLook.TargetRadius(rig.Scale);
            FxKit.Flash(at, EscortWindowHot, r * 1.2f * FxKit.FlashScale, 0.3f);
            FxKit.Ring(at, Vector3.zero, MegaCol, r * 0.3f, r * 2.2f, 0.9f, thickness: 0.1f, intensity: 2.2f);
            FxLights.Flash(at, MegaCol, 3f, r * 3f, 0.6f);
        }

        /// <summary>Turns the model so its barrel points at its quarry: at once the first time, eased afterwards.</summary>
        private void FaceEscortQuarry(string id, EscortCruiserRig rig)
        {
            var tr = rig.transform;
            if (!TryEscortQuarry(id, tr.position, out var quarry))
            {
                return;
            }

            var to = quarry - tr.position;
            if (to.sqrMagnitude < 1f)
            {
                return;
            }

            var want = Quaternion.LookRotation(to.normalized, Vector3.up);
            tr.rotation = rig.Faced ? Quaternion.Slerp(tr.rotation, want, Mathf.Clamp01(Time.deltaTime * 1.6f)) : want;
            rig.Faced = true;
        }

        /// <summary>Whom the boss model looks at: while its mega laser charges, the pilot the shot is for; otherwise the
        /// nearest pilot's ship (the NPC traders are nobody's quarry).</summary>
        private bool TryEscortQuarry(string id, Vector3 from, out Vector3 quarry)
        {
            quarry = default;
            if (_megaEntityId == id && Game.WorldTime < _megaUntil
                && TryHeavyTarget(_megaTargetId, _megaTargetId == Game.LocalPlayerId, out var aimed))
            {
                quarry = aimed.position;
                return true;
            }

            float best = float.MaxValue;
            if (_ship != null)
            {
                quarry = _ship.transform.position;
                best = (quarry - from).sqrMagnitude;
            }

            foreach (var kv in _remotePlayers)
            {
                var root = kv.Value.Root;
                if (root == null || kv.Key.StartsWith("npc:", System.StringComparison.Ordinal))
                {
                    continue;
                }

                float d = (root.transform.position - from).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    quarry = root.transform.position;
                }
            }

            return best < float.MaxValue;
        }

        /// <summary>#3900: the escort cruiser comes out of hyperspace, or jumps away: the warp streak of every ship, much
        /// bigger, and its own sound. It arrives more than 200 units out — further than any positional cue carries — so
        /// the sound is a 2D cue, softer with the distance. A missing clip falls back to the raider's sting.</summary>
        private void EscortCruiserWarp(Vector3 at, bool arriving)
        {
            var dir = Camera != null ? (at - Camera.transform.position) : Vector3.forward;
            float distance = dir.magnitude;
            dir = Vector3.Cross(distance > 0.01f ? dir / distance : Vector3.forward, Vector3.up);
            SpaceFx.WarpZip(at, dir.sqrMagnitude > 1e-4f ? dir : Vector3.right, arriving, EscortWarpSize);

            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            float vol = Mathf.Clamp(1.15f - (distance / 500f), 0.35f, 1f);
            if (arriving)
            {
                audio.Cue(audio.Has("cruiser_warp_in") ? "cruiser_warp_in" : "raider_warp_in", vol);
            }
            else if (audio.Has("cruiser_warp_out"))
            {
                audio.Cue("cruiser_warp_out", vol);
            }
            else
            {
                audio.At("raider_warp_in", at, 1.2f, 1f);
            }
        }

        /// <summary>The boss model of an entity, or null (it is no boss, or its model is gone).</summary>
        private EscortCruiserRig EscortRig(string id)
            => id != null && _escortRigs.TryGetValue(id, out var rig) && rig != null ? rig : null;

        /// <summary>A cruiser's rough radius: the plain one's 4.5, the boss's by its model's scale.</summary>
        private float CruiserTargetRadius(NetCombatEntity e)
        {
            var rig = EscortRig(e.Id);
            return rig != null ? EscortCruiserLook.TargetRadius(rig.Scale) : 4.5f;
        }

        /// <summary>A destroyed cruiser's explosion size: the plain one's 2.2, the boss's by its model's scale.</summary>
        private float CruiserExplosionScale(string id)
        {
            var rig = EscortRig(id);
            return rig != null ? EscortCruiserLook.ExplosionScale(rig.Scale) : 2.2f;
        }

        /// <summary>What the lock-on label calls a cruiser: the boss by its own name.</summary>
        private string CruiserName(NetCombatEntity e)
            => EscortRig(e.Id) != null
                ? Loc(EscortCruiserRules.NameKey, "Escort cruiser")
                : Loc("ui.scan.subject.cruiser", "Guardian cruiser");

        /// <summary>Whether the HUD's boss bar shows this entity's hull — then no small health bar floats over it.</summary>
        private bool BossBarSpeaksFor(string id) => Game.BossBar.SpeaksFor(id, Game.WorldTime);

        /// <summary>The flight view closes: no boss model is left, and a mega laser still charging ends with the scene.</summary>
        private void ResetEscortCruisers()
        {
            _escortRigs.Clear();
            _megaRun++;
            _megaUntil = 0f;
            _megaWarnUntil = 0f;
        }
    }
}
