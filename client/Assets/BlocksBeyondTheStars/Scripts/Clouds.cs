// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The surface cloud layer (weather). A two-layer dome of soft, camera-facing cloud billboards drifting
    /// with the wind around the player. There are three cloud archetypes — puffy <b>cumulus</b> and broad flat
    /// <b>stratus</b> low down, thin wispy <b>cirrus</b> high up — each from its own pool of noise-broken
    /// textures (no two neighbours share a silhouette). Each puff drifts at its own speed (near clouds faster),
    /// slowly swells and fades on a lifecycle, and is lit from the real sun direction so it has a bright and a
    /// shadowed side (warm at sunset). Colour, count and opacity come from the authoritative
    /// <see cref="Networking.Messages.WorldEnvironment"/>: each planet has its own base cloud colour + density,
    /// and the live weather thickens/darkens them, raises low storm towers and hides the high cirrus in storms.
    /// Hidden in space, on airless bodies, and on planets with no cloud cover. Built-in/URP, always-included
    /// <c>BlocksBeyondTheStars/Cloud</c> shader.
    /// <para>#3202: the drift and the look live in <see cref="CloudDeck"/> (Client.Core): each cloud's azimuth advances by
    /// one world step, so a weather flip or a wind ramp changes the speed and never where the clouds are; cover, darkening,
    /// wind, storm towers and cirrus ease toward the new weather over ~10 s and single clouds fade in and out. The deck
    /// snaps when it re-appears (after space, a hyperjump, another world) and on a capture snap.</para>
    /// <para>#3272: the dome rides with the camera, so on a summit or the lift it used to float "above" you. Climbing through
    /// the world's cloud band (<c>CloudBaseY</c>..<c>CloudTopY</c>) fades the low cumulus and stratus out
    /// (<see cref="CloudSeaRules.DomeLowFade"/>); above it the sea of clouds (<see cref="CloudSea"/>) lies below and only
    /// the high cirrus stay overhead.</para>
    /// </summary>
    public sealed class Clouds : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const int ClassicClouds = 20;
        private const int HighFrom = 14;       // clouds [HighFrom..ClassicClouds) are the high cirrus layer

        /// <summary>Billowy clouds: ten more low clouds behind the classic twenty — shown on the High preset.</summary>
        private static readonly int MaxClouds = GameLook.BillowClouds ? ClassicClouds + 10 : ClassicClouds;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int ShadeColorId = Shader.PropertyToID("_ShadeColor");
        private static readonly int CloudSunDirId = Shader.PropertyToID("_CloudSunDir");
        private static readonly int SunShadeId = Shader.PropertyToID("_SunShade");
        private static readonly int BulgeId = Shader.PropertyToID("_Bulge");
        private static readonly int BillowId = Shader.PropertyToID("_Billow");
        private static readonly int AmbSkyId = Shader.PropertyToID("_Sc_AmbSky");
        private static readonly int LookSunHueId = Shader.PropertyToID("_Sc_LookSunHue");
        private static readonly int ScSunDirId = Shader.PropertyToID("_Sc_SunDir");

        private enum CloudKind { Cumulus, Stratus, Cirrus }

        private readonly Transform[] _clouds = new Transform[MaxClouds];
        private readonly Material[] _mats = new Material[MaxClouds];
        private readonly float[] _angle = new float[MaxClouds];   // starting azimuth around the camera (radians)
        private readonly float[] _elev = new float[MaxClouds];    // elevation above the horizon (radians)
        private readonly float[] _dist = new float[MaxClouds];
        private readonly float[] _size = new float[MaxClouds];
        private readonly float[] _aspect = new float[MaxClouds];  // width / height
        private readonly float[] _vscale = new float[MaxClouds];  // vertical scale (flat stratus < cumulus)
        private readonly float[] _drift = new float[MaxClouds];   // per-cloud wind multiplier (near = faster)
        private readonly float[] _phase = new float[MaxClouds];   // lifecycle phase offset
        private readonly float[] _pulse = new float[MaxClouds];   // lifecycle speed
        private readonly bool[] _high = new bool[MaxClouds];      // the high cirrus layer
        private bool _built;

        private CloudDeck _deck;       // #3202: accumulated drift + eased look (Client.Core)
        private bool _shown;           // the deck was on screen last frame (re-appearing snaps instead of easing)
        private bool _snapPending;     // a capture asked every eased look to settle (GameBootstrap.CaptureSnap)
        private string _shownBiome;    // the world the deck last showed — another world snaps
        private Sky _sky;              // the smoothed day fraction (sits on the same rig object)
        private bool _skyLooked;
        private float _lowFade = 1f;   // #3272: how much of the LOW clouds stays at the camera's height (cirrus unaffected)

        private void Awake()
        {
            var shader = Shader.Find("BlocksBeyondTheStars/Cloud");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }

            bool billows = GameLook.BillowClouds; // cumulus and stratus as heaps of round billows
            Texture2D[] textures = GenerateSilhouettes(billows);

            var rng = new System.Random(20260602);
            // Shuffled per-archetype pools so adjacent clouds rarely repeat a silhouette.
            int[] cumulus = Shuffle(new[] { 0, 1, 2, 3, 4, 5 }, rng);
            int[] stratus = Shuffle(new[] { 6, 7, 8 }, rng);
            int[] cirrus = Shuffle(new[] { 9, 10, 11 }, rng);
            int ci = 0, si = 0, ri = 0;

            for (int i = 0; i < MaxClouds; i++)
            {
                BuildCloud(i, shader, textures, billows, rng, cumulus, stratus, cirrus, ref ci, ref si, ref ri);
            }

            _deck = new CloudDeck(_angle, _drift);
            _built = true;
        }

        /// <summary>Twelve silhouettes: 6 cumulus, 3 stratus, 3 cirrus — far more variety than the old four. With
        /// <paramref name="billows"/> cumulus and stratus are heaps of round billows; the cirrus always stay soft cards.</summary>
        private static Texture2D[] GenerateSilhouettes(bool billows)
        {
            var textures = new Texture2D[12];
            for (int i = 0; i < 6; i++)
            {
                textures[i] = billows ? GenerateBillowTexture(1234 + i * 97, CloudKind.Cumulus) : GenerateCloudTexture(1234 + i * 97, CloudKind.Cumulus);
            }

            for (int i = 0; i < 3; i++)
            {
                textures[6 + i] = billows ? GenerateBillowTexture(5500 + i * 71, CloudKind.Stratus) : GenerateCloudTexture(5500 + i * 71, CloudKind.Stratus);
            }

            for (int i = 0; i < 3; i++)
            {
                textures[9 + i] = GenerateCloudTexture(8800 + i * 53, CloudKind.Cirrus);
            }

            return textures;
        }

        /// <summary>Builds cloud <paramref name="i"/>: its quad, its silhouette and place in the dome
        /// (<see cref="PlaceInDome"/>), its material and its lifecycle. One step per cloud, drawing from the shared
        /// <paramref name="rng"/> in the dome's fixed order, so every cloud keeps the look it always had.</summary>
        private void BuildCloud(int i, Shader shader, Texture2D[] textures, bool billows, System.Random rng,
            int[] cumulus, int[] stratus, int[] cirrus, ref int ci, ref int si, ref int ri)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Cloud" + i;
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            bool high = i >= HighFrom && i < ClassicClouds;
            int texIndex = PlaceInDome(i, high, rng, cumulus, stratus, cirrus, ref ci, ref si, ref ri);
            var mat = new Material(shader) { mainTexture = textures[texIndex] };
            mat.renderQueue = 3000;
            mat.SetFloat(SunShadeId, 1f);
            mat.SetFloat(BulgeId, 1f);    // surface puffs fake a volume normal from the UV
            mat.SetFloat(BillowId, billows && texIndex < 9 ? 1f : 0f); // a billow heap is lit per billow (cirrus stays a soft card)
            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _mats[i] = mat;
            _high[i] = high;
            _clouds[i] = quad.transform;
            _clouds[i].SetParent(transform, false);
            _clouds[i].gameObject.SetActive(false);

            _angle[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
            _drift[i] = 200f / _dist[i];                                  // near clouds appear to drift faster
            _phase[i] = (float)(rng.NextDouble() * System.Math.PI * 2.0);
            _pulse[i] = 0.05f + (float)rng.NextDouble() * 0.08f;
        }

        /// <summary>Cloud <paramref name="i"/>'s silhouette from the archetype pools (returned) and its elevation, distance,
        /// aspect, vertical scale and size: the far low bank of the High preset's additional clouds, the high cirrus,
        /// broad flat stratus or puffy cumulus.</summary>
        private int PlaceInDome(int i, bool high, System.Random rng, int[] cumulus, int[] stratus, int[] cirrus, ref int ci, ref int si, ref int ri)
        {
            bool extra = i >= ClassicClouds; // the High preset's additional low clouds
            int texIndex;
            if (extra)
            {
                // Low toward the horizon and far out: the bank of cumulus the eye reads as "a sky full of clouds".
                bool slab = rng.NextDouble() < 0.25;
                texIndex = slab ? stratus[si++ % stratus.Length] : cumulus[ci++ % cumulus.Length];
                _elev[i] = 0.05f + (float)rng.NextDouble() * 0.24f;       // ~3°..17°
                _dist[i] = 200f + (float)rng.NextDouble() * 90f;
                _aspect[i] = slab ? 3.0f + (float)rng.NextDouble() * 1.6f : 1.4f + (float)rng.NextDouble() * 0.9f;
                _vscale[i] = slab ? 0.5f : 1.0f;
                _size[i] = 90f + (float)rng.NextDouble() * 80f;
            }
            else if (high)
            {
                texIndex = cirrus[ri++ % cirrus.Length];
                _elev[i] = 0.60f + (float)rng.NextDouble() * 0.45f;       // ~34°..60° (toward the zenith)
                _dist[i] = 240f + (float)rng.NextDouble() * 90f;
                _aspect[i] = 2.6f + (float)rng.NextDouble() * 1.9f;
                _vscale[i] = 0.45f;
                _size[i] = 90f + (float)rng.NextDouble() * 70f;
            }
            else if (rng.NextDouble() < 0.3)
            {
                // Broad flat stratus.
                texIndex = stratus[si++ % stratus.Length];
                _elev[i] = 0.06f + (float)rng.NextDouble() * 0.26f;       // ~3°..18° (low)
                _dist[i] = 150f + (float)rng.NextDouble() * 100f;
                _aspect[i] = 3.0f + (float)rng.NextDouble() * 2.0f;
                _vscale[i] = 0.5f;
                _size[i] = 80f + (float)rng.NextDouble() * 90f;
            }
            else
            {
                // Puffy cumulus.
                texIndex = cumulus[ci++ % cumulus.Length];
                _elev[i] = 0.12f + (float)rng.NextDouble() * 0.48f;       // ~7°..34°
                _dist[i] = 150f + (float)rng.NextDouble() * 110f;
                _aspect[i] = 1.3f + (float)rng.NextDouble() * 0.9f;
                _vscale[i] = 1.0f;
                _size[i] = 70f + (float)rng.NextDouble() * 90f;
            }

            return texIndex;
        }

        private void OnEnable() => GameBootstrap.CaptureSnap += OnCaptureSnap;

        /// <summary>A capture asks every eased look to settle (#2405): the next frame snaps instead of easing.</summary>
        private void OnCaptureSnap() => _snapPending = true;

        private void Update()
        {
            if (!_built || Camera == null)
            {
                return;
            }

            var env = Game != null ? Game.Environment : null;
            bool show = env != null && !env.SpaceSky && !Game.SpaceViewActive && env.CloudDensity > 0.001f;
            if (!show)
            {
                HideAll();
                _shown = false;
                return;
            }

            // World time, not frame time (#908): the deck drifts, eases and boils on this clock, so it parks itself while
            // the world is held instead of sailing on over a frozen landscape.
            float worldDt = Game.WorldDeltaTime;
            CloudStyle look = TickDeck(env, worldDt);

            // Day brightness (mirrors Sky): clouds darken at night. The Sky's own smoothed day fraction — the raw local
            // time only moves on the server's 5 s heartbeat (#3202).
            float tod = SkyDayFraction();
            float sunHeight = Mathf.Sin((tod - 0.25f) * Mathf.PI * 2f);
            // #3414: under night sight the clouds are lit like the sky around them (the raised day of NightSightRules).
            float day = NightSightRules.SeenDay(Mathf.Clamp01(sunHeight * 0.5f + 0.5f), BioSenses.NightSight);
            Color tint = CloudTint(env, day, look.Darken);
            float opacity = Mathf.Lerp(0.45f, 0.95f, look.Cover);
            if (GameLook.BillowClouds)
            {
                opacity = Mathf.Lerp(0.68f, 0.97f, look.Cover); // a billow heap is a body, not a veil
                BillowColours(tint);
            }

            // World-space direction toward the sun (shared with Sky/SkyBodies). Lets each puff have a lit
            // and a shadowed side; falls back to straight up before the first lighting update.
            Vector4 sunDir = Shader.GetGlobalVector(ScSunDirId);
            if (((Vector3)sunDir).sqrMagnitude < 1e-4f)
            {
                sunDir = new Vector4(0f, 1f, 0f, 0f);
            }

            Vector3 camPos = Camera.transform.position;
            // #3272: climbing through the world's cloud band, the low clouds fade out — they no longer hover overhead while
            // you stand in a cloud or look down on the sea of clouds (CloudSea). The high cirrus stay.
            _lowFade = CloudSeaRules.DomeLowFade(camPos.y, env.CloudBaseY, env.CloudTopY);
            float t = Game.WorldTime; // the constant-rate lifecycle only — the drift is accumulated in the deck
            for (int i = 0; i < MaxClouds; i++)
            {
                PlaceCloud(i, camPos, t, look, opacity, tint, sunDir);
            }
        }

        /// <summary>One frame of the deck (#3202): snap or ease the look toward the weather's target, then drift every cloud
        /// by one world step at the eased wind. Snaps when the deck re-appears, on another world and on a capture snap.</summary>
        private CloudStyle TickDeck(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env, float worldDt)
        {
            // Cover: per-planet base raised by the live weather. Storms also raise low cloud "towers" (stormTall) and pull
            // the thin high cirrus out of the sky (cirrusFade). #2174: one table for every view that shows weather.
            CloudStyle target = CloudDeck.Target(env.Weather, env.CloudDensity);
            if (!_shown || _snapPending || !string.Equals(_shownBiome, env.Biome, System.StringComparison.Ordinal))
            {
                _deck.Snap(target);
                _shown = true;
                _snapPending = false;
                _shownBiome = env.Biome;
            }
            else
            {
                _deck.EaseToward(target, worldDt);
            }

            // The authoritative wind (#900, already smoothed by GameBootstrap) rides on top of the per-state scale, so the
            // sky's drift speed and the precipitation's slant agree with each other instead of each guessing on its own.
            CloudStyle look = _deck.Look;
            _deck.Drift(worldDt, look.WindScale * (0.6f + 1.8f * Game.WindSpeed));
            return look;
        }

        /// <summary>The Sky's smoothed local day fraction (it sits on the same rig object), or the raw local time without one.</summary>
        private float SkyDayFraction()
        {
            if (!_skyLooked)
            {
                _skyLooked = true;
                _sky = GetComponent<Sky>();
            }

            return _sky != null ? _sky.DayFraction : Game.LocalTimeOfDay;
        }

        /// <summary>The deck's colour: the planet's cloud colour, darkened by night and storms, with a touch of the star's
        /// colour by day (#3125) and more at low sun (sunset glow) — the rule the cloud sea and the landing cover share
        /// (<see cref="CoverLook.DeckColour"/>, #3635).</summary>
        private static Color CloudTint(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env, float day, float darken)
        {
            MistRgb lit = CoverLook.DeckColour(env.CloudColor, -1, 0f, env.SunColor, day, darken);
            return new Color(lit.R, lit.G, lit.B, 1f);
        }

        /// <summary>Positions, sizes and colours cloud <paramref name="i"/> for this frame — or hides it when the cover does
        /// not reach it (or it is high cirrus in heavy weather).</summary>
        private void PlaceCloud(int i, Vector3 camPos, float t, in CloudStyle look, float opacity, Color tint, Vector4 sunDir)
        {
            // Each cloud fades in and out with its share of the cover instead of popping (#3202).
            // (The additional low clouds [ClassicClouds..) fade in over the cover like the classic ones, on High only.)
            float presence = i < ClassicClouds
                ? CloudDeck.Presence(i, ClassicClouds, look.Cover) * (_high[i] ? 1f : _lowFade)
                : CloudDeck.Presence(i - ClassicClouds, MaxClouds - ClassicClouds, look.Cover) * _lowFade
                    * (FxKit.Preset == QualityPreset.High && HighDetails.DesktopClass ? 1f : 0f);
            bool active = presence > 0.001f && !(_high[i] && look.CirrusFade <= 0.01f);
            if (_clouds[i].gameObject.activeSelf != active)
            {
                _clouds[i].gameObject.SetActive(active);
            }

            if (!active)
            {
                return;
            }

            // Lifecycle: each puff slowly swells and fades, and gently bobs — clouds form and dissipate
            // instead of riding a rigid ring.
            float life = 0.6f + 0.4f * Mathf.Sin(t * _pulse[i] + _phase[i]);
            float sizeMul = 0.82f + 0.32f * life;
            float alphaLife = 0.55f + 0.45f * life;
            float elev = _elev[i] + 0.015f * Mathf.Sin(t * 0.04f + _phase[i]);

            float a = _deck.Angle(i);
            float ce = Mathf.Cos(elev);
            Vector3 dir = new Vector3(Mathf.Cos(a) * ce, Mathf.Sin(elev), Mathf.Sin(a) * ce);
            _clouds[i].position = camPos + dir * _dist[i];
            _clouds[i].rotation = Quaternion.LookRotation(camPos - _clouds[i].position);

            float vs = _vscale[i] * (_high[i] ? 1f : 1f + look.StormTall);     // storm towers grow low clouds tall
            float s = _size[i] * sizeMul;
            _clouds[i].localScale = new Vector3(s * _aspect[i], s * vs, s);

            float alpha = opacity * alphaLife * presence;
            if (_high[i])
            {
                alpha *= look.CirrusFade * 0.7f;                               // cirrus is thinner / fades first
            }

            ColourCloud(i, alpha, tint, sunDir);
        }

        /// <summary>The colour step of <see cref="PlaceCloud"/>: cloud <paramref name="i"/>'s lit and shaded colour at
        /// this frame's <paramref name="alpha"/>, and the sun direction its shader lights it from.</summary>
        private void ColourCloud(int i, float alpha, Color tint, Vector4 sunDir)
        {
            Color cloudTint = tint;
            cloudTint.a = Mathf.Clamp01(alpha);
            Color shade = cloudTint * 0.45f;
            shade.a = cloudTint.a;

            if (GameLook.BillowClouds)
            {
                // Lit in the sun's hue, shaded in the sky's (both linear, set once a frame in BillowColours).
                _mats[i].SetColor(ColorId, new Color(_billowLit.r, _billowLit.g, _billowLit.b, cloudTint.a));
                _mats[i].SetColor(ShadeColorId, new Color(_billowShade.r, _billowShade.g, _billowShade.b, cloudTint.a));
            }
            else
            {
                _mats[i].SetColor(ColorId, ShaderColor.Srgb(cloudTint));
                _mats[i].SetColor(ShadeColorId, ShaderColor.Srgb(shade));
            }

            _mats[i].SetVector(CloudSunDirId, sunDir);
        }

        private Color _billowLit, _billowShade;

        /// <summary>Billowy clouds: this frame's two colours (linear) from the deck's tint — the side toward the
        /// sun takes the sun light's hue (white at noon, orange and pink at a low sun), the side away from it the hue of
        /// the sky above, a good deal darker: violet shadows under an evening sky, blue-grey ones at noon. Both hues are
        /// the look's own globals (Sky.Look.cs); without them the tint and a darker tint stand in.</summary>
        private void BillowColours(Color tint)
        {
            Color lit = ShaderColor.Srgb(tint);
            Vector4 sun = Shader.GetGlobalVector(LookSunHueId);
            Vector4 sky = Shader.GetGlobalVector(AmbSkyId);
            (_billowLit, _billowShade) = BillowPair(lit, new Color(sun.x, sun.y, sun.z), new Color(sky.x, sky.y, sky.z));
        }

        /// <summary>The lit and the shaded colour of a billow cloud from its tint, the sun light's colour and the sky's
        /// (all linear). Only the HUES of the two lights count — each is scaled so its strongest channel is 1, so a deep
        /// blue sky cannot push the shade past the tint and a low orange sun does not burn the lit side out.
        /// The result is what <see cref="Material.SetColor"/> must be handed: the shader's two colour properties are
        /// declared as colours, and Unity converts such a property from sRGB to linear on the way in (measured in the
        /// off-screen preview: a linear value handed over raw arrived as its 2.2nd power). So the pair is returned
        /// sRGB-encoded and arrives linear.</summary>
        public static (Color Lit, Color Shade) BillowPair(Color tint, Color sunLight, Color skyLight)
        {
            static Color Hue(Color c)
            {
                float top = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                return top > 1e-4f ? new Color(c.r / top, c.g / top, c.b / top) : Color.white;
            }

            Color lit = tint * Color.Lerp(Color.white, Hue(sunLight), 0.62f) * 1.05f;
            Color shade = tint * Color.Lerp(Color.white, Hue(skyLight), 0.7f) * 0.6f;
            return (lit.gamma, shade.gamma);
        }

        private void HideAll()
        {
            if (!_built)
            {
                return;
            }

            for (int i = 0; i < MaxClouds; i++)
            {
                if (_clouds[i] != null && _clouds[i].gameObject.activeSelf)
                {
                    _clouds[i].gameObject.SetActive(false);
                }
            }
        }

        private void OnDisable()
        {
            GameBootstrap.CaptureSnap -= OnCaptureSnap;
            HideAll();
            _shown = false; // re-enabled later: snap, there is nothing on screen to ease from
        }

        private static int[] Shuffle(int[] a, System.Random rng)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }

            return a;
        }

        /// <summary>A soft cloud puff with noise-broken, fractal edges. The archetype sets the silhouette:
        /// cumulus = billowy lobes, stratus = a broad flat slab, cirrus = thin wind-stretched streaks.</summary>
        private static Texture2D GenerateCloudTexture(int seed, CloudKind kind)
        {
            const int n = 160;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var rng = new System.Random(seed);
            int lobes = kind == CloudKind.Cirrus ? 3 + rng.Next(2)
                      : kind == CloudKind.Stratus ? 5 + rng.Next(3)
                      : 4 + rng.Next(3);

            var lx = new float[lobes];
            var ly = new float[lobes];
            var lrx = new float[lobes];
            var lry = new float[lobes];
            PlaceLobes(kind, rng, lx, ly, lrx, lry);
            Color[] px = PaintPuff(n, seed, kind, lx, ly, lrx, lry);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>The lobes of a soft puff (centre and radii in the card's 0..1 space), drawn lobe by lobe from
        /// <paramref name="rng"/>: the archetype's silhouette before the noise breaks it.</summary>
        private static void PlaceLobes(CloudKind kind, System.Random rng, float[] lx, float[] ly, float[] lrx, float[] lry)
        {
            for (int k = 0; k < lx.Length; k++)
            {
                switch (kind)
                {
                    case CloudKind.Cirrus: // long, paper-thin horizontal strokes
                        lx[k] = 0.20f + (float)rng.NextDouble() * 0.60f;
                        ly[k] = 0.30f + (float)rng.NextDouble() * 0.40f;
                        lrx[k] = 0.30f + (float)rng.NextDouble() * 0.20f;
                        lry[k] = 0.015f + (float)rng.NextDouble() * 0.030f;
                        break;
                    case CloudKind.Stratus: // wide, low slabs
                        lx[k] = 0.15f + (float)rng.NextDouble() * 0.70f;
                        ly[k] = 0.42f + (float)rng.NextDouble() * 0.16f;
                        lrx[k] = 0.18f + (float)rng.NextDouble() * 0.16f;
                        lry[k] = 0.06f + (float)rng.NextDouble() * 0.05f;
                        break;
                    default: // cumulus cauliflower
                        lx[k] = 0.30f + (float)rng.NextDouble() * 0.40f;
                        ly[k] = 0.36f + (float)rng.NextDouble() * 0.26f;
                        lrx[k] = 0.12f + (float)rng.NextDouble() * 0.10f;
                        lry[k] = lrx[k] * (0.8f + (float)rng.NextDouble() * 0.5f);
                        break;
                }
            }
        }

        /// <summary>The pixels of a soft puff of <paramref name="n"/>² from its lobes: the summed lobes broken by fractal
        /// noise and cut into a soft edge (alpha), brighter on top and greyer below (grey).</summary>
        private static Color[] PaintPuff(int n, int seed, CloudKind kind, float[] lx, float[] ly, float[] lrx, float[] lry)
        {
            int lobes = lx.Length;
            float cut = kind == CloudKind.Cirrus ? 0.35f : 0.5f;
            float edge = kind == CloudKind.Cirrus ? 1.2f : 1.6f;
            float cap = kind == CloudKind.Cirrus ? 0.55f : kind == CloudKind.Stratus ? 0.85f : 0.95f;

            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1), v = y / (float)(n - 1);
                    float field = 0f;
                    for (int k = 0; k < lobes; k++)
                    {
                        float dx = (u - lx[k]) / lrx[k];
                        float dy = (v - ly[k]) / lry[k];
                        field += Mathf.Exp(-(dx * dx + dy * dy));
                    }

                    // Fractal noise breaks the smooth blob into wisps. Cirrus stretches the noise along u.
                    float nf = kind == CloudKind.Cirrus ? Fbm(u * 9f, v * 26f, seed) : Fbm(u * 4.5f, v * 4.5f, seed);
                    field *= kind == CloudKind.Cirrus ? 0.2f + 1.4f * nf : 0.5f + 0.95f * nf;

                    float a = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(field - cut) * edge) * cap;

                    // Brighter tops, greyer undersides, plus internal density variation for depth.
                    float shade = Mathf.Lerp(0.78f, 1f, Mathf.Clamp01(v * 1.2f));
                    shade *= 0.85f + 0.3f * Fbm(u * 6f + 11f, v * 6f + 7f, seed + 5);
                    shade = Mathf.Clamp01(shade);
                    px[y * n + x] = new Color(shade, shade, shade, a);
                }
            }

            return px;
        }

        /// <summary>A billowy cumulus or stratus: a heap of round billows on a flat underside — a
        /// row of large ones resting on the base line, smaller ones heaped on top toward the middle (a stratus: wide,
        /// flat ones). The texture holds what the shader lights it with instead of a painted shade: the surface normal in
        /// r and g, the cloud's thickness in b, its cover in a. Linear data, so the texture is created linear.</summary>
        private static Texture2D GenerateBillowTexture(int seed, CloudKind kind)
        {
            const int n = 192;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var rng = new System.Random(seed);
            bool flat = kind == CloudKind.Stratus;
            float baseY = flat ? 0.40f : 0.30f;     // the flat underside (v: 0 = the bottom of the card)
            float wide = flat ? 1.9f : 1f;          // a stratus billow is a wide, low dome
            int row = flat ? 6 : 5;
            int count = row + (flat ? 4 + rng.Next(3) : 7 + rng.Next(5));
            var cx = new float[count];
            var cy = new float[count];
            var cr = new float[count];
            PlaceBillows(rng, flat, baseY, wide, row, cx, cy, cr);
            float[] height = BillowHeight(n, seed, baseY, wide, cx, cy, cr, out float top);
            Color[] px = BillowPixels(n, height, top);
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>The billows of a heap (centre and radius in the card's 0..1 space), drawn billow by billow from
        /// <paramref name="rng"/>: the first <paramref name="row"/> rest on the base line, the rest are heaped on earlier ones.</summary>
        private static void PlaceBillows(System.Random rng, bool flat, float baseY, float wide, int row, float[] cx, float[] cy, float[] cr)
        {
            int count = cx.Length;
            for (int k = 0; k < count; k++)
            {
                if (k < row)
                {
                    cr[k] = flat ? 0.085f + (float)rng.NextDouble() * 0.04f : 0.125f + (float)rng.NextDouble() * 0.06f;
                    cx[k] = 0.20f + (0.60f * (k + (float)rng.NextDouble()) / row);
                    cy[k] = baseY + (cr[k] * (flat ? 0.3f : 0.5f));
                }
                else
                {
                    // Heaped on an earlier billow (so the heap grows upward in one piece, never a ball of its own).
                    int on = rng.Next(k);
                    cr[k] = flat ? 0.06f + (float)rng.NextDouble() * 0.035f : 0.075f + (float)rng.NextDouble() * 0.065f;
                    cx[k] = Mathf.Clamp(cx[on] + (((float)rng.NextDouble() - 0.5f) * 1.1f * cr[on] * wide), 0.22f, 0.78f);
                    cy[k] = Mathf.Min(cy[on] + ((0.45f + (0.5f * (float)rng.NextDouble())) * cr[on] * (flat ? 0.5f : 1f)), 0.9f - cr[k]);
                }
            }
        }

        /// <summary>The height of the heap over the card: the billows' domes, joined softly (a 4-norm, so two neighbours
        /// melt into a crease instead of meeting in a sharp line), a little fine relief, cut flat at the base.
        /// <paramref name="top"/> is the highest point (at least 1e-4, the thickness channel's scale).</summary>
        private static float[] BillowHeight(int n, int seed, float baseY, float wide, float[] cx, float[] cy, float[] cr, out float top)
        {
            int count = cx.Length;
            var height = new float[n * n];
            top = 1e-4f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float u = x / (float)(n - 1), v = y / (float)(n - 1);
                    float sum = 0f;
                    for (int k = 0; k < count; k++)
                    {
                        float dx = (u - cx[k]) / (cr[k] * wide), dy = (v - cy[k]) / cr[k];
                        float d2 = (dx * dx) + (dy * dy);
                        if (d2 < 1f)
                        {
                            float dome = Mathf.Sqrt(1f - d2) * cr[k];
                            sum += dome * dome * dome * dome;
                        }
                    }

                    float h = Mathf.Sqrt(Mathf.Sqrt(sum));
                    if (h > 0f)
                    {
                        h = Mathf.Max(0f, h + ((Fbm(u * 6f, v * 6f, seed) - 0.47f) * 0.012f));
                    }

                    h *= Ramp(baseY - 0.035f, baseY + 0.025f, v);
                    height[(y * n) + x] = h;
                    top = Mathf.Max(top, h);
                }
            }

            return height;
        }

        /// <summary>The billow texture's pixels from the heap's height: the surface normal in r and g, the thickness
        /// (height over <paramref name="top"/>) in b, the cover in a.</summary>
        private static Color[] BillowPixels(int n, float[] height, float top)
        {
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float h = height[(y * n) + x];
                    float hx = height[(y * n) + Mathf.Min(n - 1, x + 1)] - height[(y * n) + Mathf.Max(0, x - 1)];
                    float hy = height[(Mathf.Min(n - 1, y + 1) * n) + x] - height[(Mathf.Max(0, y - 1) * n) + x];
                    // The domes are true hemispheres in the card's units, so the height's slope is the surface's.
                    var normal = new Vector3(-hx * (n - 1) * 0.5f, -hy * (n - 1) * 0.5f, 1f).normalized;
                    float cover = Ramp(0.006f, 0.05f, h) * 0.97f;
                    px[(y * n) + x] = new Color((normal.x * 0.5f) + 0.5f, (normal.y * 0.5f) + 0.5f, Mathf.Clamp01(h / top), cover);
                }
            }

            return px;
        }

        /// <summary>0 below <paramref name="from"/>, 1 above <paramref name="to"/>, a smooth ramp between (the shader
        /// languages' smoothstep; Unity's Mathf.SmoothStep is an interpolation and takes its arguments differently).</summary>
        private static float Ramp(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - (2f * t));
        }

        // --- Lightweight value-noise fBm (no Unity Perlin dependency, deterministic per seed) ---
        private static float Fbm(float x, float y, int seed)
        {
            float sum = 0f, amp = 0.5f, freq = 1f;
            for (int o = 0; o < 4; o++)
            {
                sum += amp * ValueNoise(x * freq, y * freq, seed + o * 131);
                freq *= 2f;
                amp *= 0.5f;
            }

            return sum;
        }

        private static float ValueNoise(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float u = xf * xf * (3f - 2f * xf), v = yf * yf * (3f - 2f * yf);
            float v00 = Hash01(xi, yi, seed), v10 = Hash01(xi + 1, yi, seed);
            float v01 = Hash01(xi, yi + 1, seed), v11 = Hash01(xi + 1, yi + 1, seed);
            return Mathf.Lerp(Mathf.Lerp(v00, v10, u), Mathf.Lerp(v01, v11, u), v);
        }

        private static float Hash01(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 982451653;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
            }
        }
    }
}
