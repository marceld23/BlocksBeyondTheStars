using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Weather effects (M27 polish): an IMGUI rain overlay during rain/storm (density scaled by the
    /// authoritative <c>WorldEnvironment.Intensity</c>) and periodic lightning flashes in storms.
    /// Drawn behind the HUD (high GUI.depth), hidden in space. No shaders/particles — robust in builds.
    /// </summary>
    public sealed class WeatherFx : MonoBehaviour
    {
        public GameBootstrap Game;

        private const int Max = 140;
        private readonly float[] _x = new float[Max];
        private readonly float[] _phase = new float[Max];
        private readonly float[] _len = new float[Max];
        private readonly float[] _speed = new float[Max];
        private bool _init;
        private float _flash;
        private float _flashTimer;
        private float _underwater; // smoothed 0..1 blue submerged wash

        private void Init()
        {
            var rng = new System.Random(7);
            for (int i = 0; i < Max; i++)
            {
                _x[i] = (float)rng.NextDouble();
                _phase[i] = (float)rng.NextDouble();
                _len[i] = 10f + (float)rng.NextDouble() * 18f;
                _speed[i] = 0.8f + (float)rng.NextDouble() * 0.6f;
            }

            _init = true;
        }

        private void Update()
        {
            // Underwater blue wash — independent of weather (works in caves / at night too).
            _underwater = Mathf.MoveTowards(_underwater, EyeUnderwater() ? 1f : 0f, Time.deltaTime * 3.5f);

            if (Game?.Environment == null)
            {
                return;
            }

            _flash = Mathf.Max(0f, _flash - Time.deltaTime * 4f);
            if (Game.Environment.Weather == "storm")
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    _flashTimer = 6f + Random.value * 10f;
                    _flash = 1f;
                }
            }
        }

        /// <summary>True when the first-person eye (≈ the head, ~1.5 above the player root) is inside water.</summary>
        private bool EyeUnderwater()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var p = Game.PlayerPosition;
            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y + 1.5f), Mathf.FloorToInt(p.z)));
            return def?.Key == "water";
        }

        private void OnGUI()
        {
            // Subtle blue submerged wash, drawn before (and independent of) the weather overlay so it shows
            // even underwater in a cave or at night. Hidden in space and while the menu is open.
            if (_underwater > 0.01f && Game != null && !Game.SpaceViewActive && !Game.MenuOpen)
            {
                GUI.depth = 10;
                var prevWater = GUI.color;
                GUI.color = new Color(0.15f, 0.40f, 0.62f, 0.34f * _underwater);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = prevWater;
            }

            var env = Game?.Environment;
            if (env == null || Game.SpaceViewActive || !Game.ExposedToSky)
            {
                return; // no rain/lightning under a roof or in a cave
            }

            bool rain = env.Weather == "rain" || env.Weather == "storm";
            if (!rain && _flash <= 0.01f)
            {
                return;
            }

            if (!_init)
            {
                Init();
            }

            GUI.depth = 10; // behind the HUD
            var prevColor = GUI.color;

            if (rain && !Game.MenuOpen)
            {
                float h = Screen.height, w = Screen.width, t = Time.time;

                // A faint cool wash sells the wet, overcast air (heavier in a storm).
                GUI.color = new Color(0.42f, 0.52f, 0.62f, 0.05f + env.Intensity * 0.07f);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

                int count = Mathf.RoundToInt(Max * Mathf.Clamp01(0.45f + env.Intensity * 0.55f));
                // Storms drive the rain harder (faster, longer, more slanted) than a light shower.
                float speedBoost = env.Weather == "storm" ? 1.5f : 1f;
                float slant = (env.Weather == "storm" ? 12f : 5f);
                GUI.color = new Color(0.62f, 0.78f, 1f, 0.42f);
                for (int i = 0; i < count; i++)
                {
                    float y = ((_phase[i] + t * _speed[i] * speedBoost) % 1f) * (h + 40f) - 20f;
                    float x = _x[i] * w + Mathf.Sin(t + i) * slant;
                    GUI.DrawTexture(new Rect(x, y, 2f, _len[i] * speedBoost), Texture2D.whiteTexture);
                }
            }

            if (_flash > 0.01f)
            {
                GUI.color = new Color(0.82f, 0.9f, 1f, _flash * 0.35f);
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            GUI.color = prevColor;
        }
    }
}
