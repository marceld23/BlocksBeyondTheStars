using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Drives the visible day/night + weather + sun colour from the server's `WorldEnvironment`
    /// (World systems). Because the world uses unlit shaders, the main effect is a **global tint**
    /// (`_Sc_Light`) multiplied into the block shaders — sun colour × day brightness × weather dim.
    /// Also drives the sky (camera background) and a rotating directional sun light. Time is
    /// advanced locally between the server's periodic updates. Suspended while the space view owns
    /// the camera.
    /// </summary>
    public sealed class Sky : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");

        private Light _sun;
        private float _time;        // local 0..1 day fraction
        private float _dayLength = 600f;
        private bool _haveEnv;

        private void Awake()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.shadows = LightShadows.None;
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            var env = Game.Environment;
            if (env != null)
            {
                if (!_haveEnv)
                {
                    _time = env.TimeOfDay;
                    _haveEnv = true;
                }

                _dayLength = Mathf.Max(10f, env.DayLengthSeconds);

                // Re-sync toward the server time (it broadcasts periodically); advance locally too.
                _time = Mathf.LerpAngle(_time * 360f, env.TimeOfDay * 360f, 0.02f) / 360f;
            }

            _time = Mathf.Repeat(_time + Time.deltaTime / _dayLength, 1f);

            float intensity = env?.Intensity ?? 0f;
            Color sun = env != null ? Rgb(env.SunColor) : new Color(1f, 0.96f, 0.9f);
            bool spaceSky = env != null && env.SpaceSky;
            ApplyLighting(_time, intensity, sun, spaceSky);
        }

        private void ApplyLighting(float time, float weatherIntensity, Color sunColor, bool spaceSky)
        {
            // Sun height: peaks at noon (0.5), lowest at midnight (0/1).
            float sunHeight = Mathf.Sin((time - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            float brightness = Mathf.Lerp(0.20f, 1f, day);            // night floor → noon
            float weatherDim = Mathf.Lerp(1f, 0.5f, weatherIntensity); // storms darken

            Color tint = sunColor * (brightness * weatherDim);
            tint.a = 1f; // marks the global as "set" for the shaders
            Shader.SetGlobalColor(LightId, tint);

            // Sky colour + sun light (skip while the space view controls the camera).
            if (Camera != null && (Game == null || !Game.SpaceViewActive))
            {
                Camera.clearFlags = CameraClearFlags.SolidColor;
                if (spaceSky)
                {
                    // Airless body (asteroid): the sky stays space-black even on the surface.
                    Camera.backgroundColor = new Color(0.01f, 0.01f, 0.03f);
                }
                else
                {
                    Color daySky = Color.Lerp(new Color(0.55f, 0.75f, 0.95f), new Color(0.6f, 0.62f, 0.68f), weatherIntensity);
                    Color nightSky = new Color(0.03f, 0.04f, 0.09f);
                    Camera.backgroundColor = Color.Lerp(nightSky, daySky, day);
                }
            }

            if (_sun != null)
            {
                _sun.color = sunColor;
                _sun.intensity = brightness;
                _sun.transform.rotation = Quaternion.Euler(time * 360f - 90f, 160f, 0f);
            }
        }

        private void OnDisable()
        {
            // Clear the tint so other scenes (menu) aren't affected.
            Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 0f));
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
