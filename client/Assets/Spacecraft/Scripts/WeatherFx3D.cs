using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// In-world weather (M27 polish, P7 weather rest): actual 3D rain falling around the player during
    /// rain/storm, plus storm fog that cuts view distance. The rain is a recycled pool of thin unlit
    /// streaks (the same robust "cubes in code" approach as the space view — no particle-shader stripping
    /// risk in builds). Both are gated like the rest of weather: only with open sky overhead (not in caves
    /// or under a roof), not in the space view, and hidden while a menu is up. Density/speed/slant scale
    /// with the authoritative <c>WorldEnvironment.Intensity</c>. The looping rain/storm bed + thunder live
    /// in <see cref="ClientAudio"/>; the screen wash + lightning flash in <see cref="WeatherFx"/>.
    /// </summary>
    public sealed class WeatherFx3D : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Cam;

        private const int Pool = 280;
        private const float SpawnRadius = 18f; // box half-extent around the camera the rain spawns in
        private const float SpawnUp = 12f;     // height above the camera it spawns at
        private Transform[] _drops;
        private float[] _speed;
        private Material _mat;
        private readonly System.Random _rng = new System.Random(13);

        // Saved global fog state so storm fog restores cleanly when the weather clears / we leave the world.
        private bool _fogSaved;
        private bool _prevFog;
        private Color _prevFogColor;
        private float _prevFogDensity;
        private FogMode _prevFogMode;

        private void Start()
        {
            _mat = new Material(Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque"))
            {
                color = new Color(0.68f, 0.80f, 1f),
            };

            _drops = new Transform[Pool];
            _speed = new float[Pool];
            for (int i = 0; i < Pool; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = "RainDrop";
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.transform.SetParent(transform, false);
                go.transform.localScale = new Vector3(0.03f, 0.5f, 0.03f);
                go.GetComponent<Renderer>().sharedMaterial = _mat;
                go.SetActive(false);
                _drops[i] = go.transform;
                _speed[i] = 26f + (float)_rng.NextDouble() * 16f;
            }
        }

        private void LateUpdate()
        {
            var env = Game?.Environment;
            bool raining = env != null
                           && (env.Weather == "rain" || env.Weather == "storm")
                           && Game.ExposedToSky && !Game.SpaceViewActive && !Game.MenuOpen;

            ApplyFog(env, raining);

            if (Cam == null || !raining)
            {
                HideAll();
                return;
            }

            bool storm = env.Weather == "storm";
            int active = Mathf.RoundToInt(Pool * Mathf.Clamp01(0.4f + env.Intensity * 0.6f));
            float fall = storm ? 1.45f : 1f;
            float slant = storm ? 7f : 2f;
            var camPos = Cam.transform.position;
            var tilt = Quaternion.Euler(storm ? 14f : 4f, 0f, 0f);
            float dt = Time.deltaTime;

            for (int i = 0; i < Pool; i++)
            {
                var d = _drops[i];
                if (i >= active)
                {
                    if (d.gameObject.activeSelf)
                    {
                        d.gameObject.SetActive(false);
                    }

                    continue;
                }

                if (!d.gameObject.activeSelf)
                {
                    d.gameObject.SetActive(true);
                    Respawn(d, camPos);
                }

                var p = d.position + new Vector3(slant * dt, -_speed[i] * fall * dt, 0f);
                if (p.y < camPos.y - 7f || (p - camPos).sqrMagnitude > (SpawnRadius * 1.6f) * (SpawnRadius * 1.6f))
                {
                    Respawn(d, camPos);
                }
                else
                {
                    d.position = p;
                    d.rotation = tilt;
                    d.localScale = new Vector3(0.03f, storm ? 0.75f : 0.5f, 0.03f);
                }
            }
        }

        private void Respawn(Transform d, Vector3 camPos)
        {
            d.position = camPos + new Vector3(
                (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius,
                SpawnUp + (float)_rng.NextDouble() * 6f,
                (float)(_rng.NextDouble() * 2 - 1) * SpawnRadius);
        }

        private void HideAll()
        {
            if (_drops == null)
            {
                return;
            }

            for (int i = 0; i < _drops.Length; i++)
            {
                if (_drops[i] != null && _drops[i].gameObject.activeSelf)
                {
                    _drops[i].gameObject.SetActive(false);
                }
            }
        }

        /// <summary>Storm fog: a low grey-blue fog that cuts view distance during a storm (scaled by
        /// intensity), restoring the previous fog state when the storm passes or we leave the world.</summary>
        private void ApplyFog(Spacecraft.Networking.Messages.WorldEnvironment env, bool raining)
        {
            bool stormFog = raining && env.Weather == "storm";
            if (stormFog)
            {
                if (!_fogSaved)
                {
                    _prevFog = RenderSettings.fog;
                    _prevFogColor = RenderSettings.fogColor;
                    _prevFogDensity = RenderSettings.fogDensity;
                    _prevFogMode = RenderSettings.fogMode;
                    _fogSaved = true;
                }

                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = new Color(0.45f, 0.50f, 0.58f);
                RenderSettings.fogDensity = 0.004f + Mathf.Clamp01(env.Intensity) * 0.010f;
            }
            else if (_fogSaved)
            {
                RestoreFog();
            }
        }

        private void RestoreFog()
        {
            RenderSettings.fog = _prevFog;
            RenderSettings.fogColor = _prevFogColor;
            RenderSettings.fogDensity = _prevFogDensity;
            RenderSettings.fogMode = _prevFogMode;
            _fogSaved = false;
        }

        private void OnDestroy()
        {
            if (_fogSaved)
            {
                RestoreFog(); // don't leave storm fog on when the world tears down
            }
        }
    }
}
