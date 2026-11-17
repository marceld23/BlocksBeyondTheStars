// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A soft round contact shadow under a figure — the players, the NPCs and the animals. Where the sun's shadow map
    /// does not reach (the Potato preset has none; a cave, a room, a station and the night have no sun), a figure used
    /// to stand on the ground without touching it. The blob is one small alpha-blended quad on the ground under the
    /// figure: it fades as the figure rises above the ground and is gone a few metres up, and it stays away wherever
    /// the real sun shadow already grounds the figure (out in the daylight on a preset with shadows), so the two never
    /// stack. Cheap by rule: the ground is probed a few times a second, only near the camera, and nothing is allocated
    /// per frame.
    /// </summary>
    public sealed class BlobShadow : MonoBehaviour
    {
        private const float ProbeSeconds = 0.12f;   // how often the ground under the figure is looked up
        private const float ProbeUp = 0.6f;         // the probe starts this far above the feet
        private const float ProbeDown = 5f;         // … and looks this far down: higher than that there is no shadow
        private const float MaxDistance = 48f;      // beyond this the blob would be a few pixels
        private const float Lift = 0.03f;           // above the ground, clear of z-fighting
        private const float MaxAlpha = 0.42f;

        private static Mesh _quad;
        private static int _strengthFrame = -1;
        private static float _strength;

        private GameBootstrap _game;
        private PlayerAvatar _avatar; // set for a player-shaped figure: it hides with the figure
        private Transform _blob;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private float _radius;
        private float _probeAt;
        private float _gameLookupAt;
        private float _groundY = float.NaN;
        private float _shownAlpha = -1f;
        private readonly Color32[] _colors = new Color32[4];

        /// <summary>Gives <paramref name="figure"/> a contact shadow of <paramref name="radius"/> metres. A second call
        /// only updates the radius.</summary>
        public static void Attach(GameObject figure, GameBootstrap game, float radius)
        {
            if (figure == null)
            {
                return;
            }

            var shadow = figure.GetComponent<BlobShadow>();
            if (shadow == null)
            {
                shadow = figure.AddComponent<BlobShadow>();
            }

            shadow._game = game;
            shadow._radius = Mathf.Max(0.15f, radius);
            shadow._avatar = figure.GetComponent<PlayerAvatar>();
        }

        private void Build()
        {
            var material = FxKit.SoftAlphaMaterial();
            if (material == null)
            {
                enabled = false; // the shader is not in this build: no blob rather than a pink square
                return;
            }

            var go = new GameObject("BlobShadow");
            _blob = go.transform; // deliberately NOT a child: it lies flat on the ground whatever the figure's pose is
            _mesh = new Mesh { name = "BlobShadow" };
            _mesh.vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f) };
            _mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            _mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(2f, 0.1f, 2f));
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.enabled = false;
        }

        private void LateUpdate()
        {
            if (_blob == null)
            {
                Build();
                if (_blob == null)
                {
                    return;
                }
            }

            if (_game == null && Time.unscaledTime >= _gameLookupAt)
            {
                _gameLookupAt = Time.unscaledTime + 1f; // a figure built without the world's link finds it once it is there
                _game = FindAnyObjectByType<GameBootstrap>();
            }

            float strength = Strength(_game);
            var cam = Camera.main;
            var feet = transform.position;
            if (_avatar != null && !_avatar.IsVisible)
            {
                Hide(); // a hidden figure casts none: the own body in first person, a cloaked player
                return;
            }

            if (strength <= 0.01f || cam == null || (cam.transform.position - feet).sqrMagnitude > MaxDistance * MaxDistance)
            {
                Hide();
                return;
            }

            ProbeGround(feet);
            if (float.IsNaN(_groundY))
            {
                Hide();
                return;
            }

            Place(feet, strength);
        }

        /// <summary>Looks up the ground under the feet — a few times a second, not every frame.</summary>
        private void ProbeGround(Vector3 feet)
        {
            if (Time.time < _probeAt)
            {
                return;
            }

            _probeAt = Time.time + ProbeSeconds;
            _groundY = Physics.Raycast(feet + (Vector3.up * ProbeUp), Vector3.down, out var hit, ProbeUp + ProbeDown,
                Physics.DefaultRaycastLayers & PlayerController.WorldRayMask, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : float.NaN;
        }

        /// <summary>Lays the blob on the ground under the feet: full while standing; rising above the ground it thins
        /// out and shrinks a little, like a real one.</summary>
        private void Place(Vector3 feet, float strength)
        {
            float height = Mathf.Max(0f, feet.y - _groundY);
            float near = 1f - Mathf.Clamp01(height / ProbeDown);
            float alpha = MaxAlpha * strength * near * near;
            if (alpha <= 0.01f)
            {
                Hide();
                return;
            }

            _blob.position = new Vector3(feet.x, _groundY + Lift, feet.z);
            float size = _radius * Mathf.Max(0.2f, transform.lossyScale.x) * (0.7f + (0.3f * near)); // a scaled figure (a big robber) casts a bigger one
            _blob.localScale = new Vector3(size, 1f, size);
            if (Mathf.Abs(alpha - _shownAlpha) > 0.01f)
            {
                _shownAlpha = alpha;
                var c = new Color32(0, 0, 0, (byte)Mathf.RoundToInt(alpha * 255f));
                _colors[0] = _colors[1] = _colors[2] = _colors[3] = c;
                _mesh.colors32 = _colors;
            }

            if (!_renderer.enabled)
            {
                _renderer.enabled = true;
            }
        }

        private void Hide()
        {
            if (_renderer != null && _renderer.enabled)
            {
                _renderer.enabled = false;
            }
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (_blob != null)
            {
                Destroy(_blob.gameObject);
            }

            if (_mesh != null)
            {
                Destroy(_mesh);
            }
        }

        /// <summary>How much the blobs show this frame, 0…1: fully where there is no sun shadow at all (no shadow map on
        /// this preset, under a roof, underground, aboard), with the night out in the open, not in the daylight.</summary>
        private static float Strength(GameBootstrap game)
        {
            if (Time.frameCount == _strengthFrame)
            {
                return _strength;
            }

            _strengthFrame = Time.frameCount;
            bool sunShadows = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp && urp.shadowDistance > 0.5f;
            if (!sunShadows || game == null)
            {
                _strength = 1f;
            }
            else if (game.SpaceViewActive)
            {
                _strength = 0f; // the flight view has no ground to stand on
            }
            else
            {
                float night = NightAmbience.NightLevel(game.LocalTimeOfDay);
                _strength = game.ExposedToSky ? night : 1f;
            }

            return _strength;
        }
    }
}
