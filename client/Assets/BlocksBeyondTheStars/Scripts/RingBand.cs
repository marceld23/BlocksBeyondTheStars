// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The planet's OWN ring seen from its surface (#596): standing on a ringed planet, the ring system
    /// arcs across the sky as a pale banded ribbon — bold at night, a faint sky-washed arc by day (the
    /// real-Saturn look). A camera-following annulus (the shared <see cref="PlanetRings"/> mesh/texture,
    /// so the bands match what you saw from orbit) whose plane sits offset from the eye, which turns the
    /// ring into a wide arc instead of a hairline. Terrain occludes it naturally; the far half sits below
    /// the horizon. Purely cosmetic, driven by the star map's RingSeed for the active body.
    /// </summary>
    public sealed class RingBand : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private Transform _band;
        private Material _mat;
        private string _builtFor = "\0";
        private int _ringSeed;
        private Color _tint;
        private float _fade = -1f; // smoothed day/night strength; <0 = snap to the target on first sight
                                   // (playtest: the ramp-in from 0 left the band invisible for minutes)

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/PlanetRing") ?? Shader.Find("BlocksBeyondTheStars/ParticleAlpha");
            if (shader == null)
            {
                enabled = false;
                return;
            }

            _mat = new Material(shader) { renderQueue = 3001 };
            var go = new GameObject("RingBand");
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = PlanetRings.AnnulusMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            _band = go.transform;
            go.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_band == null || Game == null || Camera == null)
            {
                return;
            }

            string active = Game.StarMap?.ActiveLocationId ?? "\0";
            if (active != _builtFor)
            {
                Rebuild(active);
            }

            bool show = _ringSeed != 0
                && !Game.SpaceViewActive                       // the space view renders the real ring disc itself
                && string.IsNullOrEmpty(Game.StationName)      // not from inside a station
                && !Game.OnFootInSpace
                && Game.Environment != null;
            if (_band.gameObject.activeSelf != show)
            {
                _band.gameObject.SetActive(show);
            }

            if (!show)
            {
                return;
            }

            // The ring plane: seeded near-vertical inclination (a flat one would circle the horizon like a
            // halo; steep arcs read as "the ring stands in the sky"), seeded azimuth. Offsetting the plane
            // from the eye along its normal is what gives the arc its visible width (~10-12°).
            float r = Mathf.Max(200f, Camera.farClipPlane) * 0.35f;
            var rot = Quaternion.AngleAxis(PlanetRings.AzimuthDegrees(_ringSeed), Vector3.up)
                      * Quaternion.AngleAxis(90f - Mathf.Abs(PlanetRings.TiltDegrees(_ringSeed)), Vector3.right);
            Vector3 normal = rot * Vector3.up;
            _band.SetPositionAndRotation(Camera.transform.position + normal * (r * 0.3f), rot);
            _band.localScale = new Vector3(r, r, r);

            // Day/night strength, matched to the starfield's dusk ramp; airless worlds keep the deep-space
            // look at all hours. By day the colour washes toward the sky base (#585's lesson: an unwashed
            // sky object reads as a black silhouette against the bright tonemapped sky).
            var env = Game.Environment;
            float night;
            if (env.SpaceSky)
            {
                night = 1f;
            }
            else
            {
                float sunHeight = Mathf.Sin((Game.LocalTimeOfDay - 0.25f) * Mathf.PI * 2f);
                float t = Mathf.Clamp01((0.12f - sunHeight) / 0.52f);
                night = t * t * (3f - 2f * t);
            }

            float dayLen = Mathf.Max(30f, env.DayLengthSeconds);
            float rate = Mathf.Clamp(240f / dayLen, 0.25f, 1.0f);
            _fade = env.SpaceSky || _fade < 0f ? night : Mathf.MoveTowards(_fade, night, Time.deltaTime * rate);

            // By day the band washes toward the sky colour but stays a clearly visible pale arc (the
            // playtest read the original 0.10 day alpha as "no rings at all"); at night it dominates.
            int packed = env.SkyColor;
            var skyCol = new Color(((packed >> 16) & 0xFF) / 255f, ((packed >> 8) & 0xFF) / 255f, (packed & 0xFF) / 255f);
            float day = 1f - _fade;
            var col = Color.Lerp(_tint, skyCol * 1.05f, day * 0.45f);
            col.a = Mathf.Lerp(0.28f, 0.55f, _fade);
            _mat.color = ShaderColor.Srgb(col);
        }

        private void Rebuild(string activeId)
        {
            _builtFor = activeId;
            _ringSeed = 0;
            var map = Game.StarMap;
            if (map?.Systems != null)
            {
                foreach (var sys in map.Systems)
                {
                    foreach (var b in sys.Bodies)
                    {
                        if (b.Id == activeId)
                        {
                            _ringSeed = b.RingSeed;
                            break;
                        }
                    }
                }
            }

            if (_ringSeed != 0)
            {
                _mat.mainTexture = PlanetRings.BandTexture(_ringSeed);
                _tint = PlanetRings.TintFor(_ringSeed, Color.white);
            }
        }
    }
}
