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

        // Lightning bolt (B8): a brief jagged streak drawn with the flash.
        private float _boltTimer;
        private float _boltBaseX;
        private readonly float[] _boltJitter = new float[12];
        private readonly System.Random _boltRng = new System.Random(91);

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
            _boltTimer = Mathf.Max(0f, _boltTimer - Time.deltaTime);
            // Lightning only in a rain thunderstorm — not a blizzard, sandstorm or ashfall.
            if (Game.Environment.Weather == "storm" && Game.Environment.Precipitation == "rain")
            {
                _flashTimer -= Time.deltaTime;
                if (_flashTimer <= 0f)
                {
                    _flashTimer = 4f + Random.value * 7f;
                    _flash = 1f;
                    _boltTimer = 0.18f; // a bolt accompanies most strikes
                    _boltBaseX = 0.15f + Random.value * 0.7f;
                    for (int i = 0; i < _boltJitter.Length; i++)
                    {
                        _boltJitter[i] = (float)(_boltRng.NextDouble() * 2 - 1); // stable zig-zag for this strike
                    }
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

            string precip = env.Precipitation ?? "none";
            if (precip == "none" && _flash <= 0.01f)
            {
                return;
            }

            if (!_init)
            {
                Init();
            }

            GUI.depth = 10; // behind the HUD
            var prevColor = GUI.color;

            if (precip != "none" && !Game.MenuOpen)
            {
                float h = Screen.height, w = Screen.width, t = Time.time;

                // A faint wash sells the heavy air, tinted to the precipitation (wet blue, smoky ash, tan dust…).
                Color wash = precip switch
                {
                    "ash" => new Color(0.30f, 0.22f, 0.18f),
                    "sandstorm" => new Color(0.74f, 0.62f, 0.40f),
                    "snow" or "hail" => new Color(0.72f, 0.78f, 0.84f),
                    _ => new Color(0.42f, 0.52f, 0.62f),
                };
                float washStrength = precip == "sandstorm" ? 0.22f : 0.05f;
                GUI.color = new Color(wash.r, wash.g, wash.b, washStrength + env.Intensity * 0.07f);
                GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

                // 2D streaks only for actual rain — the 3D pool carries snow/hail/ash/sand, and blue
                // screen-streaks over those would read as rain.
                if (precip == "rain")
                {
                    int count = Mathf.RoundToInt(Max * Mathf.Clamp01(0.45f + env.Intensity * 0.55f));
                    float speedBoost = env.Weather == "storm" ? 1.5f : 1f;
                    float slant = env.Weather == "storm" ? 12f : 5f;
                    GUI.color = new Color(0.62f, 0.78f, 1f, 0.42f);
                    for (int i = 0; i < count; i++)
                    {
                        float y = ((_phase[i] + t * _speed[i] * speedBoost) % 1f) * (h + 40f) - 20f;
                        float x = _x[i] * w + Mathf.Sin(t + i) * slant;
                        GUI.DrawTexture(new Rect(x, y, 2f, _len[i] * speedBoost), Texture2D.whiteTexture);
                    }
                }
            }

            if (_flash > 0.01f)
            {
                GUI.color = new Color(0.85f, 0.92f, 1f, _flash * 0.6f); // brighter sky-lighting flash
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            }

            if (_boltTimer > 0.01f && !Game.MenuOpen)
            {
                DrawBolt();
            }

            GUI.color = prevColor;
        }

        /// <summary>A brief jagged white bolt from the top of the screen down to mid-screen (B8).</summary>
        private void DrawBolt()
        {
            float w = Screen.width, h = Screen.height;
            float alpha = Mathf.Clamp01(_boltTimer / 0.18f);
            int segs = _boltJitter.Length;
            float segH = (h * 0.6f) / segs;
            float x = _boltBaseX * w;
            float prevX = x, prevY = 0f;
            for (int i = 0; i < segs; i++)
            {
                float nextY = prevY + segH;
                float nextX = x + _boltJitter[i] * (w * 0.045f);
                // Approximate the slanted segment with a stack of short horizontal-stepped slabs.
                int steps = 6;
                for (int k = 0; k < steps; k++)
                {
                    float f = k / (float)steps;
                    float sx = Mathf.Lerp(prevX, nextX, f);
                    float sy = Mathf.Lerp(prevY, nextY, f);
                    GUI.color = new Color(1f, 1f, 1f, alpha);                       // hot core
                    GUI.DrawTexture(new Rect(sx - 1.5f, sy, 3f, segH / steps + 1f), Texture2D.whiteTexture);
                    GUI.color = new Color(0.7f, 0.85f, 1f, alpha * 0.5f);            // soft glow
                    GUI.DrawTexture(new Rect(sx - 4f, sy, 8f, segH / steps + 1f), Texture2D.whiteTexture);
                }

                prevX = nextX;
                prevY = nextY;
            }
        }
    }
}
