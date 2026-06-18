using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Night auroras on cold worlds ("Welten reicher" W-R4): a few softly waving translucent ribbons high in
    /// the sky, in shifting green–teal–violet, that fade in during deep night on ice/tundra-class worlds
    /// (or anywhere the air is well below freezing) and vanish by day. Pure client ambience — alpha-blended
    /// quads (the always-included Cloud shader), depth-tested so terrain occludes them and they never show
    /// underground. Follows the player so the bands always hang overhead.
    /// </summary>
    public sealed class AuroraView : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int Ribbons = 3;
        private readonly Transform[] _ribbons = new Transform[Ribbons];
        private readonly Material[] _mats = new Material[Ribbons];
        private float _fade; // 0 hidden .. 1 fully visible (smoothed)

        private void Start()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Aurora")
                         ?? Shader.Find("BlocksBeyondTheStars/Cloud") ?? Shader.Find("Unlit/Color");
            for (int i = 0; i < Ribbons; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "Aurora" + i;
                var col = go.GetComponent<Collider>();
                if (col != null)
                {
                    Destroy(col);
                }

                go.transform.SetParent(transform, true);
                go.transform.localScale = new Vector3(300f, 150f + i * 34f, 1f); // tall hanging curtains, not flat sheets
                _mats[i] = new Material(shader);
                go.GetComponent<Renderer>().sharedMaterial = _mats[i];
                _ribbons[i] = go.transform;
                go.SetActive(false);
            }
        }

        private void Update()
        {
            if (Game == null || _ribbons[0] == null)
            {
                return;
            }

            var env = Game.Environment;
            // Auroras need an atmosphere: skip airless bodies (asteroids / space-sky), which also report the
            // NoAirTemperature sentinel (-999) — that used to read as "very cold" and drew auroras in their
            // black, airless sky. Guard the sentinel too so only genuinely cold AIR worlds qualify.
            bool coldWorld = env != null && !env.SpaceSky
                && (env.Biome is "ice" or "tundra" || (env.Temperature > -100f && env.Temperature < -8f));
            float tod = Game.LocalTimeOfDay;
            bool deepNight = tod < 0.20f || tod > 0.80f;
            bool show = coldWorld && deepNight && !Game.SpaceViewActive && Game.ExposedToSky;

            _fade = Mathf.MoveTowards(_fade, show ? 1f : 0f, Time.deltaTime * 0.25f); // slow, atmospheric fade
            bool visible = _fade > 0.01f;

            for (int i = 0; i < Ribbons; i++)
            {
                if (_ribbons[i].gameObject.activeSelf != visible)
                {
                    _ribbons[i].gameObject.SetActive(visible);
                }

                if (!visible)
                {
                    continue;
                }

                float t = Time.time * 0.06f + i * 2.1f;

                // Hang high over the player, each band at its own bearing, slowly drifting + undulating.
                var p = Game.PlayerPosition;
                float bearing = i * 1.25f + Mathf.Sin(t * 0.4f) * 0.15f;
                var offset = new Vector3(Mathf.Sin(bearing), 0f, Mathf.Cos(bearing)) * (140f + i * 60f);
                _ribbons[i].position = p + offset + new Vector3(0f, 130f + i * 18f + Mathf.Sin(t * 1.7f) * 6f, 0f);
                _ribbons[i].rotation = Quaternion.LookRotation(_ribbons[i].position - p)
                                       * Quaternion.Euler(Mathf.Sin(t * 1.3f) * 8f, 0f, Mathf.Sin(t) * 10f);

                // Shifting polar colours: green → teal → violet, slowly drifting per band; the shader does the
                // waving folds + ray streaks, here we drive the hue + a slow brightness pulse (additive glow).
                float hue = Mathf.Repeat(0.34f + 0.12f * Mathf.Sin(t * 0.5f + i * 1.7f), 1f);
                var c = Color.HSVToRGB(hue, 0.8f, 1f);
                c.a = _fade * (0.5f + 0.28f * Mathf.Sin(t * 0.8f + i * 2.1f)); // additive brightness, gently pulsing
                _mats[i].color = c;
            }
        }
    }
}
