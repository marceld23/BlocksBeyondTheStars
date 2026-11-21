// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// How the other side looks (#3906, epic #3891) — the thin Unity half of <see cref="OtherSideView"/>:
    /// <list type="bullet">
    /// <item><b>The grey look</b>: eases the model's blend and hands it to the colour grade
    /// (<see cref="UrpScenePost.SetOtherSide"/>) — everything white, grey and black, the Lights too. No second world
    /// render, no shader of its own.</item>
    /// <item><b>The step through a gate</b>: a soft white veil (<see cref="UrpScenePost.PulseOtherSide"/>) and the sound
    /// <c>otherside_cross</c> / <c>otherside_return</c> (silent while the file is missing).</item>
    /// <item><b>The gates' lights</b>: over there every rune gate the server lists (<c>OtherSideGates</c>) carries a tall
    /// soft white pillar that fades in with the look and is gone on our side, a glow between its stones, the entry gate
    /// brighter and breathing; a cracked gate shows a dark jagged crack between the stones until the server clears the
    /// flag. A handful of pooled <see cref="LineRenderer"/>s with the existing beam material — no real lights.</item>
    /// <item><b>The door on our side</b>: while the crossing disc sits in the worn obsidian chest's socket, a faint glow
    /// stands between the stones of the rune gates near the player (found by <see cref="RuneGateFinder"/>, a slab of
    /// cells per frame), so the player sees where the door is.</item>
    /// </list>
    /// Added to the bootstrap's object by <c>GameBootstrap.WireOtherSide</c>.
    /// </summary>
    public sealed class OtherSideFx : MonoBehaviour
    {
        /// <summary>The 2D sound of a crossing (the lead supplies the file; missing = silent).</summary>
        public const string CrossSound = "otherside_cross";

        /// <summary>The 2D sound of a return at a gate.</summary>
        public const string ReturnSound = "otherside_return";

        private const float GlowHeight = 5f, GlowWidth = 7f, CrackHeight = 3.2f, CrackWidth = 0.22f, CrackTowardPlayer = 0.7f;
        private const int CrackPoints = 7, RealSideDoors = 4;
        private const float ScanEverySeconds = 3f;

        private static readonly float[] CrackJag = { 0f, 0.34f, -0.22f, 0.4f, -0.38f, 0.16f, -0.3f, 0.26f, -0.1f };

        /// <summary>The game this draws for.</summary>
        public GameBootstrap Game;

        private sealed class GateLight
        {
            public GameObject Go;
            public LineRenderer Pillar, Glow, Crack;
        }

        private readonly GateLight[] _lights = new GateLight[OtherSideGateBook.Capacity];
        private readonly LineRenderer[] _doorGlows = new LineRenderer[RealSideDoors];
        private readonly RuneGateFinder _finder = new RuneGateFinder();
        private System.Func<int, int, int, bool> _isRune;
        private Transform _root;
        private Material _pillarMat, _glowMat, _crackMat;
        private bool _materialsTried;
        private int _lit, _doorsLit;
        private bool _doorsOn;
        private float _nextScanAt;
        private object _equipmentSeen, _contentSeen;
        private bool _discWorn;
        private ushort _runeId;

        /// <summary>The player just stepped through a gate: the veil and the sound.</summary>
        public void Stage(OtherSideCue cue)
        {
            if (cue == OtherSideCue.None)
            {
                return;
            }

            UrpScenePost.Instance?.PulseOtherSide(1f);
            var audio = ClientAudio.Instance;
            string id = cue == OtherSideCue.Cross ? CrossSound : ReturnSound;
            if (audio != null && audio.Has(id))
            {
                audio.Cue(id, 0.85f);
            }
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var view = Game.OtherSide;
            view.Tick(Time.deltaTime);
            float look = Game.SpaceViewActive ? 0f : view.Look;
            UrpScenePost.Instance?.SetOtherSide(look);
            DrawGateLights(view, look);
            DrawRealSideDoors(view, look);
        }

        private void OnDisable()
        {
            UrpScenePost.Instance?.SetOtherSide(0f);
        }

        // ---------------- The gates over there ----------------

        private void DrawGateLights(OtherSideView view, float look)
        {
            int count = look > 0.001f && EnsureMaterials() ? view.Gates.Count : 0;
            double now = Time.unscaledTimeAsDouble;
            var me = Game.PlayerPosition;
            for (int i = 0; i < count; i++)
            {
                var gate = view.Gates[i];
                var light = LightAt(i);
                var at = Game.ScenePos(gate.X, gate.Y, gate.Z);
                float strength = OtherSideLook.PillarStrength(look, gate.Entry, now, UiKit.ReducedMotion);
                float dx = at.x - me.x, dz = at.z - me.z;
                float dist = Mathf.Sqrt((dx * dx) + (dz * dz));
                PlacePillar(light.Pillar, at, strength, OtherSideLook.PillarWidthAt(dist));
                PlaceGlow(light.Glow, at, strength * (gate.Cracked ? 0.42f : 0.55f)); // a crack reads against a calmer glow
                // The crack hangs a little on the player's side of the gate's middle, so the glow behind it never washes it out.
                float pull = dist > 0.01f ? CrackTowardPlayer / dist : 0f;
                PlaceCrack(light, new Vector3(at.x - (dx * pull), at.y, at.z - (dz * pull)), gate.Cracked, i);
            }

            for (int i = count; i < _lit; i++)
            {
                _lights[i].Go.SetActive(false);
            }

            _lit = count;
        }

        private GateLight LightAt(int index)
        {
            var light = _lights[index];
            if (light == null)
            {
                light = new GateLight { Go = new GameObject("OtherSideGate") };
                light.Go.transform.SetParent(Root(), false);
                light.Pillar = NewLine(light.Go.transform, "Pillar", _pillarMat, 2);
                light.Glow = NewLine(light.Go.transform, "Glow", _glowMat, 2);
                _lights[index] = light;
            }

            if (!light.Go.activeSelf)
            {
                light.Go.SetActive(true);
            }

            return light;
        }

        private static void PlacePillar(LineRenderer line, Vector3 at, float strength, float width)
        {
            line.SetPosition(0, new Vector3(at.x, at.y - 1f, at.z));
            line.SetPosition(1, new Vector3(at.x, at.y + OtherSideLook.PillarHeight, at.z));
            line.widthMultiplier = width;
            line.startColor = new Color(1f, 1f, 1f, strength);
            line.endColor = new Color(1f, 1f, 1f, 0f); // fades out toward the sky
        }

        /// <summary>The soft glow between and on a gate's stones: a short, wide, faint line from the ground up.</summary>
        private static void PlaceGlow(LineRenderer line, Vector3 at, float strength)
        {
            line.SetPosition(0, new Vector3(at.x, at.y - 0.6f, at.z));
            line.SetPosition(1, new Vector3(at.x, at.y + GlowHeight, at.z));
            line.widthMultiplier = GlowWidth;
            line.startColor = new Color(0.9f, 0.95f, 1f, strength);
            line.endColor = new Color(0.9f, 0.95f, 1f, 0f);
        }

        /// <summary>A cracked gate: a dark jagged line hovering between the stones; gone when the flag clears.</summary>
        private void PlaceCrack(GateLight light, Vector3 at, bool cracked, int index)
        {
            if (!cracked || _crackMat == null)
            {
                if (light.Crack != null && light.Crack.gameObject.activeSelf)
                {
                    light.Crack.gameObject.SetActive(false);
                }

                return;
            }

            if (light.Crack == null)
            {
                light.Crack = NewLine(light.Go.transform, "Crack", _crackMat, CrackPoints);
                light.Crack.widthMultiplier = CrackWidth;
                var dark = new Color(0.03f, 0.03f, 0.04f, 1f);
                light.Crack.startColor = dark;
                light.Crack.endColor = dark;
            }

            if (!light.Crack.gameObject.activeSelf)
            {
                light.Crack.gameObject.SetActive(true);
            }

            for (int k = 0; k < CrackPoints; k++)
            {
                float t = k / (float)(CrackPoints - 1);
                float jx = CrackJag[(k + index) % CrackJag.Length];
                float jz = CrackJag[((k * 3) + index + 2) % CrackJag.Length];
                light.Crack.SetPosition(k, new Vector3(at.x + jx, at.y + 0.2f + (t * CrackHeight), at.z + jz));
            }
        }

        // ---------------- The door on our side ----------------

        private void DrawRealSideDoors(OtherSideView view, float look)
        {
            bool on = !view.Over && look <= 0.001f && !Game.SpaceViewActive && Game.World != null && CrossingDiscWorn() && EnsureMaterials();
            if (!on)
            {
                if (_doorsOn)
                {
                    _doorsOn = false;
                    _finder.Clear();
                    HideDoorGlows(0);
                }

                return;
            }

            _doorsOn = true;
            ScanForDoors();
            int count = Mathf.Min(_finder.Gates.Count, RealSideDoors);
            float breath = UiKit.ReducedMotion ? 1f : 0.8f + (0.2f * Mathf.Sin(Time.unscaledTime * 1.6f));
            for (int i = 0; i < count; i++)
            {
                var gate = _finder.Gates[i];
                var line = _doorGlows[i];
                if (line == null)
                {
                    line = _doorGlows[i] = NewLine(Root(), "DoorGlow", _glowMat, 2);
                }

                if (!line.gameObject.activeSelf)
                {
                    line.gameObject.SetActive(true);
                }

                PlaceGlow(line, new Vector3(gate.X, gate.Y, gate.Z), 0.3f * breath);
            }

            HideDoorGlows(count);
        }

        private void HideDoorGlows(int from)
        {
            for (int i = from; i < _doorsLit; i++)
            {
                if (_doorGlows[i] != null)
                {
                    _doorGlows[i].gameObject.SetActive(false);
                }
            }

            _doorsLit = from;
        }

        /// <summary>A scan of the cells around the player every few seconds, one slab of it per frame.</summary>
        private void ScanForDoors()
        {
            if (_isRune == null)
            {
                _isRune = IsRuneStoneAt;
            }

            if (_finder.Scanning)
            {
                _finder.Advance(_isRune, 1);
                return;
            }

            if (Time.unscaledTime < _nextScanAt)
            {
                return;
            }

            _nextScanAt = Time.unscaledTime + ScanEverySeconds;
            var p = Game.PlayerPosition;
            _finder.Begin(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
        }

        private bool IsRuneStoneAt(int x, int y, int z)
        {
            if (!ReferenceEquals(Game.Content, _contentSeen))
            {
                _contentSeen = Game.Content;
                var rune = Game.Content != null ? Game.Content.GetBlock("rune_stone") : null;
                _runeId = rune != null ? rune.NumericId.Value : (ushort)0;
            }

            return _runeId != 0 && Game.World.GetBlock(x, y, z).Value == _runeId;
        }

        /// <summary>Whether the crossing disc sits in the worn obsidian chest's socket (looked up once per equipment change).</summary>
        private bool CrossingDiscWorn()
        {
            if (!ReferenceEquals(Game.Equipment, _equipmentSeen))
            {
                _equipmentSeen = Game.Equipment;
                _discWorn = Game.Wears(SoundersIds.ObsidianChest) && Game.Wears(SoundersIds.DiscCrossing);
            }

            return _discWorn;
        }

        // ---------------- Lines and materials ----------------

        private Transform Root()
        {
            if (_root == null)
            {
                _root = new GameObject("OtherSideLights").transform;
                _root.SetParent(transform, false);
            }

            return _root;
        }

        private static LineRenderer NewLine(Transform parent, string name, Material material, int points)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = points;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.widthMultiplier = 1f;
            line.sharedMaterial = material;
            return line;
        }

        /// <summary>The existing beam material in two looks (a white-cored pillar, a coreless soft glow) and a dark unlit
        /// one for a crack. False when the beam shader is missing from the build — then nothing is drawn.</summary>
        private bool EnsureMaterials()
        {
            if (!_materialsTried)
            {
                _materialsTried = true;
                _pillarMat = FxKit.BeamMaterial("otherside_gate", Color.white, coreWidth: 0.22f, noiseScale: 5f, noiseSpeed: 0.35f, pulse: 0f, intensity: 2.4f);
                _glowMat = FxKit.BeamMaterial("otherside_glow", Color.black, coreWidth: 0.3f, noiseScale: 2.5f, noiseSpeed: 0.8f, pulse: 0f, intensity: 1.5f);
                _crackMat = FxKit.Unlit(new Color(0.03f, 0.03f, 0.04f));
            }

            return _pillarMat != null && _glowMat != null;
        }
    }
}
