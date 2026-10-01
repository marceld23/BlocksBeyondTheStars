// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The ship's shield bubble (#2156): a hexagon-patterned <c>FxShell</c> ellipsoid around the hull that is invisible
    /// until something hits it — then it flares up with a ripple racing outward from the impact point (up to 8 at once,
    /// Star Citizen style, as a uniform array) and fades again. Its colour follows the shield level (cyan → amber →
    /// red), and when the shield breaks the bubble shatters into hex sparkles. Hits on an empty shield go to the hull
    /// instead (sparks — see SpaceView). The shield used to be a HUD number only.
    /// </summary>
    public sealed class FxShield : MonoBehaviour
    {
        private const int Slots = 8;
        private const float HitLife = 0.9f;

        private MeshRenderer _renderer;
        private readonly Vector4[] _hits = new Vector4[Slots];
        private int _next;
        private float _visible;
        private float _level = 1f;
        private static readonly int HitsId = Shader.PropertyToID("_FxHits");
        private static readonly int HitCountId = Shader.PropertyToID("_FxHitCount");
        private static readonly int HitParamsId = Shader.PropertyToID("_FxHitParams");

        /// <summary>Adds a shield bubble around <paramref name="ship"/> sized to its local bounds (null if the shader is missing).</summary>
        public static FxShield Attach(Transform ship, Bounds local)
        {
            var mat = FxKit.Cached("BlocksBeyondTheStars/FxShell", "shield", m =>
            {
                m.SetFloat("_Hex", 1f);
                m.SetFloat("_HexScale", 22f);
                m.SetFloat("_RimPower", 2.2f);
                m.SetFloat("_Fill", 0.02f);
            });
            if (mat == null || ship == null)
            {
                return null;
            }

            var go = new GameObject("ShieldBubble");
            go.transform.SetParent(ship, false);
            go.transform.localPosition = local.center;
            go.transform.localScale = Vector3.Max(local.size * 1.35f, Vector3.one * 2f);
            go.AddComponent<MeshFilter>().sharedMesh = FxKit.SphereMesh;
            var shield = go.AddComponent<FxShield>();
            shield._renderer = go.AddComponent<MeshRenderer>();
            shield._renderer.sharedMaterial = mat;
            shield._renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shield._renderer.receiveShadows = false;
            shield._renderer.enabled = false;
            for (int i = 0; i < Slots; i++)
            {
                shield._hits[i] = new Vector4(0f, 1f, 0f, -100f);
            }

            return shield;
        }

        /// <summary>A hit on the shield at world <paramref name="point"/> (the side facing the attacker);
        /// <paramref name="level"/> = the shield's remaining fraction 0..1.</summary>
        public void Hit(Vector3 point, float level)
        {
            _level = Mathf.Clamp01(level);
            var local = transform.InverseTransformPoint(point);
            if (local.sqrMagnitude < 1e-6f)
            {
                local = Random.onUnitSphere;
            }

            local.Normalize();
            _hits[_next] = new Vector4(local.x, local.y, local.z, Time.timeSinceLevelLoad);
            _next = (_next + 1) % Slots;
            _visible = 1f;
            FxKit.Burst(FxKit.Kind.Motes, point, 6, (point - transform.position), 50f, 2f, 6f, 0.08f, 0.14f, 0.25f, 0.5f, LevelColor(), Color.white);
            FxLights.Flash(point, LevelColor(), 1.2f, 8f, 0.2f);
        }

        /// <summary>The shield broke: the bubble flares and bursts into hex sparkles.</summary>
        public void Shatter()
        {
            _level = 0f;
            var c = new Color(0.55f, 0.85f, 1f);
            var size = transform.lossyScale.magnitude * 0.3f;
            FxKit.Burst(FxKit.Kind.Motes, transform.position, 40, Vector3.zero, 0f, 3f, 9f, 0.1f, 0.22f, 0.5f, 1.1f, c, Color.white);
            FxKit.Ring(transform.position, Vector3.zero, c, size, size * 2.4f, 0.5f, thickness: 0.1f, intensity: 2.2f);
            FxKit.Flash(transform.position, c, size * 1.5f * FxKit.FlashScale, 0.2f);
            ClientAudio.Instance?.Cue("ship_shield_hit", 1f);
            _visible = 1.2f;
        }

        private Color LevelColor()
            => _level > 0.5f ? Color.Lerp(new Color(1f, 0.7f, 0.25f), new Color(0.35f, 0.8f, 1f), (_level - 0.5f) * 2f)
                : Color.Lerp(new Color(1f, 0.32f, 0.22f), new Color(1f, 0.7f, 0.25f), _level * 2f);

        private void LateUpdate()
        {
            if (_renderer == null)
            {
                return;
            }

            _visible = Mathf.MoveTowards(_visible, 0f, Time.deltaTime * 1.1f);
            bool on = _visible > 0.01f;
            _renderer.enabled = on;
            if (!on)
            {
                return;
            }

            var lin = FxKit.Lin(LevelColor());
            var b = FxKit.Block;
            b.Clear();
            b.SetColor("_Color", new Color(lin.r, lin.g, lin.b, Mathf.Clamp01(_visible)));
            b.SetFloat("_Intensity", 1.6f);
            b.SetVectorArray(HitsId, _hits);
            b.SetFloat(HitCountId, Slots);
            // The shader ages hits with _Time.y — the same clock as Time.timeSinceLevelLoad.
            b.SetVector(HitParamsId, new Vector4(2.4f, 0.28f, HitLife, 0f));
            _renderer.SetPropertyBlock(b);
        }
    }
}
