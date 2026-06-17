using UnityEngine;

namespace BlocksBeyondTheStars.Client
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

        /// <summary>The player's view distance in chunks (from settings) — the linear distance fog is scaled to
        /// this so the haze engages within the streamed terrain (otherwise it sits beyond the render distance and
        /// is never seen) and softly hides the chunk pop-in at the edge.</summary>
        public int ViewChunks = 4;

        private static readonly int LightId = Shader.PropertyToID("_Sc_Light");
        private static readonly int SunDirId = Shader.PropertyToID("_Sc_SunDir");
        private static readonly int SkyId = Shader.PropertyToID("_Sc_Sky");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int GradeTintId = Shader.PropertyToID("_Sc_GradeTint");
        private static readonly int GradeParamsId = Shader.PropertyToID("_Sc_GradeParams");
        private static readonly int IndoorId = Shader.PropertyToID("_Sc_Indoor");
        private static readonly int FloraTintId = Shader.PropertyToID("_Sc_FloraTint");
        // Per-world+weather volumetric-fog density (the FullScreenPassRendererFeature reads this global); derived
        // from the same view distance the linear fog uses, 0 in space/airless so no fog is drawn there.
        private static readonly int VolFogDensityId = Shader.PropertyToID("_VolFogDensity");
        // Explicit distance haze for the block shaders (Unity's MixFog doesn't engage on the unlit voxels):
        // x=start, y=end, z=max strength (already faded out indoors), w=on.
        private static readonly int FogId = Shader.PropertyToID("_Sc_Fog");
        private float _indoor; // smoothed ship-interior fill (0 outside → 1 aboard)

        private Light _sun;
        private Transform _sunDisc;     // visible glowing sun billboard in the sky
        private Material _sunDiscMat;
        private float _time;        // local 0..1 day fraction
        private float _dayLength = 600f;
        private bool _haveEnv;

        private void Awake()
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            // Real-time sun shadows under URP (the headline of the migration); Built-in RP stays shadowless
            // (its custom block shader doesn't receive shadow maps, so they'd cost without showing).
            bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
            _sun.shadows = urp ? LightShadows.Soft : LightShadows.None;
            _sun.shadowStrength = 0.7f; // soft, not pitch-black shadows

            BuildSunDisc();
        }

        private void BuildSunDisc()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SunDisc";
            var col = quad.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }

            var shader = Shader.Find("BlocksBeyondTheStars/SunGlow");
            _sunDiscMat = new Material(shader != null ? shader : Shader.Find("Unlit/Color"))
            {
                mainTexture = GenerateGlowTexture(),
            };
            _sunDiscMat.SetColor(ColorId, Color.white);

            var mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _sunDiscMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            _sunDisc = quad.transform;
            _sunDisc.SetParent(transform, false);
            _sunDisc.gameObject.SetActive(false);
        }

        private static Texture2D GenerateGlowTexture()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Clamp01(Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c);
                    float core = Mathf.Clamp01(1f - d * 4f);              // tight bright disc
                    float halo = Mathf.Pow(Mathf.Clamp01(1f - d), 2.5f); // soft surrounding glow
                    float a = Mathf.Clamp01(core * 0.8f + halo * 0.6f);
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            }

            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // The space view manages its own dark background + body lighting. Don't bleed the planet's
            // day/night tint or the biome colour-grade into it — that turned the whole space view reddish.
            if (Game.SpaceViewActive)
            {
                Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 1f));   // neutral, full-bright
                Shader.SetGlobalColor(GradeTintId, new Color(0f, 0f, 0f, 0f)); // colour grade off
                UrpScenePost.Instance?.ApplyGrade(Color.white, 1f, 1f);        // …and off on the URP volume too
                Shader.SetGlobalColor(Shader.PropertyToID("_Sc_LampColor"), new Color(0f, 0f, 0f, 0f));
                Shader.SetGlobalFloat(IndoorId, 0f);
                Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f)); // no planet flora tint in space
                Shader.SetGlobalFloat(VolFogDensityId, 0f); // no volumetric fog in the space view
                Shader.SetGlobalVector(FogId, new Vector4(0f, 1f, 0f, 0f)); // distance haze off in space
                RenderSettings.fog = false;
                if (_sunDisc != null)
                {
                    _sunDisc.gameObject.SetActive(false);
                }

                return;
            }

            var env = Game.Environment;
            if (env != null)
            {
                // Local time-of-day: the server's global day fraction shifted by the player's longitude
                // (world X), so the planet has a day/night terminator across its surface.
                float localTime = Game.LocalTimeOfDay;
                if (!_haveEnv)
                {
                    _time = localTime;
                    _haveEnv = true;
                }

                _dayLength = Mathf.Max(10f, env.DayLengthSeconds);

                // Re-sync toward the local time (server broadcasts + the player's longitude); advance locally.
                _time = Mathf.LerpAngle(_time * 360f, localTime * 360f, 0.02f) / 360f;
            }

            _time = Mathf.Repeat(_time + Time.deltaTime / _dayLength, 1f);

            float intensity = env?.Intensity ?? 0f;
            Color sun = env != null ? Rgb(env.SunColor) : new Color(1f, 0.96f, 0.88f); // match Space/Station fallback
            // Per-world daytime sky/atmosphere hue (server-seeded; blue → green → yellow → red). Only worlds with an
            // atmosphere actually show it (airless bodies go to the space sky below). Fallback = the old fixed blue.
            Color skyBase = env != null ? Rgb(env.SkyColor) : new Color(0.55f, 0.75f, 0.95f);
            // Boarded on an orbital station: it floats free in space, so show the space sky (black, no fog)
            // and treat it like a lit, life-supported interior — independent of the planet far below.
            bool boarded = !string.IsNullOrEmpty(Game.StationName);
            // Built above the atmosphere (item 10): the sky turns to space even on a normal planet.
            bool spaceSky = (env != null && env.SpaceSky) || boarded || Game.OnFootInSpace;

            // Planet flora re-tint hue for the block shader (a>0.5 = active). Off on stations (no planet flora).
            if (env != null && !boarded)
            {
                Color flora = Rgb(env.FloraTint);
                flora.a = 1f;
                Shader.SetGlobalColor(FloraTintId, ShaderColor.Srgb(flora));
            }
            else
            {
                Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f));
            }

            ApplyLighting(_time, intensity, sun, skyBase, spaceSky, constantLight: boarded);
        }

        private void ApplyLighting(float time, float weatherIntensity, Color sunColor, Color skyBase, bool spaceSky, bool constantLight = false)
        {
            // Sun height: peaks at noon (0.5), lowest at midnight (0/1).
            float sunHeight = Mathf.Sin((time - 0.25f) * Mathf.PI * 2f);
            float day = Mathf.Clamp01(sunHeight * 0.5f + 0.5f);
            // Inside an orbital station there is no day/night — it's lit by its own constant lighting.
            float brightness = constantLight ? 1f : Mathf.Lerp(0.20f, 1f, day); // night floor → noon
            float weatherDim = constantLight ? 1f : Mathf.Lerp(1f, 0.5f, weatherIntensity); // storms darken

            // Stations use a clean neutral interior light (not the system sun's tint).
            Color tint = constantLight ? new Color(0.95f, 0.96f, 1f) : sunColor * (brightness * weatherDim);
            tint.a = 1f; // marks the global as "set" for the shaders
            Shader.SetGlobalColor(LightId, ShaderColor.Srgb(tint));

            // Ship interior lighting: the block shader darkens indoors/underground by skylight occlusion
            // (so caves need a lamp). The ship is your home, though, so feed an "indoor fill" the shader
            // adds to skylight-occluded faces only — lighting the cabin (day or night) without touching the
            // sunlit outdoors seen through the windows. Smoothed so boarding/leaving fades.
            // Interior fill light: aboard your ship, or boarded on a station (its life-support lighting).
            bool litInterior = Game != null && (Game.Aboard || !string.IsNullOrEmpty(Game.StationName));
            float indoorTarget = litInterior ? 1f : 0f;
            _indoor = Mathf.MoveTowards(_indoor, indoorTarget, Time.deltaTime * 3f);
            Shader.SetGlobalFloat(IndoorId, _indoor);

            // Sky colour: drives both the camera background and the global the lit shader samples
            // for grazing-angle environment reflections (glossy/metallic blocks).
            Color sky;
            if (spaceSky)
            {
                // Airless body (asteroid): the sky stays space-black even on the surface.
                sky = new Color(0.01f, 0.01f, 0.03f);
            }
            else
            {
                // The per-world atmosphere hue (server-seeded; blue → green → yellow → red) is the daytime base,
                // washing toward overcast grey with weather. Each world's sky now reads distinct instead of a
                // single fixed blue.
                Color daySky = Color.Lerp(skyBase, new Color(0.6f, 0.62f, 0.68f), weatherIntensity);

                // Tint the daytime sky a touch toward the system star's hue, so a warm / red star gives a warmer
                // sky and a blue-white star a cooler one (B37). Normalise the sun colour to a pure hue first so
                // only its tint shifts the sky, not its brightness. Kept light (0.2) so the per-world atmosphere
                // colour above reads through. The directional sun light + block diffuse already use the star colour.
                float sm = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
                Color sunHue = sm > 0.001f ? new Color(sunColor.r / sm, sunColor.g / sm, sunColor.b / sm) : Color.white;
                daySky = Color.Lerp(daySky, daySky * sunHue, 0.2f);

                // Night sky: a dark base nudged toward the world's atmosphere hue, so a green/red-skied world also
                // tints its night instead of always reading the same blue-black.
                Color nightSky = Color.Lerp(new Color(0.03f, 0.04f, 0.09f), skyBase * 0.12f, 0.5f);
                sky = Color.Lerp(nightSky, daySky, day);
            }
            sky.a = 1f;
            // The shader global gets the linear value; the engine-managed consumers below (ambient, camera
            // background, fog) keep the sRGB-composed `sky` — Unity converts those itself.
            Shader.SetGlobalColor(SkyId, ShaderColor.Srgb(sky));

            // Star-tinted flat ambient (B37 rest): the custom block shader ignores Unity's ambient, but
            // standard/Lit-shaded props (and URP's ambient term) pick it up — so even those follow the
            // system star + time of day instead of Unity's fixed grey.
            UnityEngine.RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            UnityEngine.RenderSettings.ambientLight = Color.Lerp(sky, tint, 0.35f);

            // Apply to the camera background (skip while the space view controls the camera).
            if (Camera != null && (Game == null || !Game.SpaceViewActive))
            {
                Camera.clearFlags = CameraClearFlags.SolidColor;
                Camera.backgroundColor = sky;
            }

            ApplyFog(sky, weatherIntensity, day, spaceSky);

            if (_sun != null)
            {
                _sun.color = sunColor;
                _sun.intensity = brightness;
                _sun.transform.rotation = Quaternion.Euler(time * 360f - 90f, 160f, 0f);
                // The lit block shader reads the sun direction from this global (direction TO the sun).
                Shader.SetGlobalVector(SunDirId, -_sun.transform.forward);
            }

            UpdateSunDisc(sunHeight, sunColor, spaceSky);
            SetGrade(Game?.Environment?.Biome, sunColor);
        }

        /// <summary>Drives the post-FX colour grade (the per-system/biome "mood LUT"): a biome tint +
        /// saturation/contrast, folded with the star system's sun colour as a subtle hue shift. Read by
        /// <c>BlocksBeyondTheStars/PostComposite</c>; only visible when tonemapping/post is on.</summary>
        private void SetGrade(string biome, Color sunColor)
        {
            var (tint, sat, contrast) = GradeFor(biome);
            float m = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            Color norm = m > 0.001f ? new Color(sunColor.r / m, sunColor.g / m, sunColor.b / m) : Color.white;
            // The star's hue folds into the grade so each system visibly tints its worlds (0.4 keeps it a
            // light, atmospheric wash — raised from 0.25 so the per-system sun colour clearly reads).
            Color blended = tint * Color.Lerp(Color.white, norm, 0.4f);
            blended.a = 0.7f; // grade strength
            Shader.SetGlobalColor(GradeTintId, ShaderColor.Srgb(blended));
            Shader.SetGlobalVector(GradeParamsId, new Vector4(sat, contrast, 0f, 0f));
            // ApplyGrade keeps the sRGB value: URP's ColorAdjustments colorFilter converts internally.
            UrpScenePost.Instance?.ApplyGrade(blended, sat, contrast); // URP path (PostComposite is Built-in-only)
        }

        /// <summary>Per-biome colour-grade mood: (tint multiply, saturation, contrast).</summary>
        private static (Color tint, float sat, float contrast) GradeFor(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "jungle": case "forest": return (new Color(0.98f, 1.05f, 0.96f), 1.12f, 1.05f);
                case "desert": return (new Color(1.07f, 1.00f, 0.90f), 0.95f, 1.12f);
                case "ice": case "frozen": return (new Color(0.94f, 1.00f, 1.09f), 0.90f, 1.06f);
                case "lava": case "volcanic": return (new Color(1.10f, 0.95f, 0.86f), 1.05f, 1.14f);
                case "swamp": return (new Color(0.97f, 1.03f, 0.95f), 0.85f, 1.03f);
                case "crystal": return (new Color(1.04f, 0.97f, 1.09f), 1.10f, 1.05f);
                default: return (new Color(1f, 1f, 1f), 1.00f, 1.03f);
            }
        }

        /// <summary>
        /// Distance fog in the sky colour, so the world fades into the horizon. Storms pull it in,
        /// night thickens it a little; airless bodies (space sky) and the space view stay clear. The
        /// fog-enabled block shader applies it; other scenes' shaders ignore RenderSettings.fog.
        /// </summary>
        private void ApplyFog(Color sky, float weatherIntensity, float day, bool spaceSky)
        {
            bool fog = !spaceSky && (Game == null || !Game.SpaceViewActive);
            RenderSettings.fog = fog;
            if (!fog)
            {
                Shader.SetGlobalFloat(VolFogDensityId, 0f); // airless body / space → no volumetric fog
                Shader.SetGlobalVector(FogId, new Vector4(0f, 1f, 0f, 0f)); // distance haze off
                return;
            }

            RenderSettings.fogColor = sky;
            RenderSettings.fogMode = FogMode.Linear;

            // Scale the fog to the actual render distance (view-distance chunks × 16-block chunk), so the haze
            // engages WITHIN the streamed terrain instead of beyond it (the old fixed 240 m sat past the ~32 m
            // render edge → invisible) and softly hides the chunk pop-in at the boundary.
            float renderDist = Mathf.Max(32f, ViewChunks * 16f);

            // Per-world air thickness sets where the haze sits: thin air fogs near the far edge, soupy air hazes
            // well inside the view. Kept well within the render distance so the haze is actually visible and masks
            // the chunk pop-in at the edge (tuned aggressive for now; raise the factors to push the haze farther).
            float airDensity = Game?.Environment?.AtmosphereDensity ?? 0.4f;
            float far = renderDist * Mathf.Lerp(1.6f, 1.0f, Mathf.Clamp01(airDensity));

            far *= Mathf.Lerp(1f, 0.8f, weatherIntensity); // storms haze in a bit more
            far *= Mathf.Lerp(0.9f, 1f, day);                // night a touch hazier than day

            // Fog weather (a transient state like rain/storm): a real pea-souper — well inside the view.
            if (string.Equals(Game?.Environment?.Weather, "fog", System.StringComparison.Ordinal))
            {
                far = Mathf.Min(far, renderDist * Mathf.Lerp(0.5f, 0.28f, Mathf.Clamp01(weatherIntensity)));
            }

            // Sandstorms / ash storms CRUSH visibility — a wall of dust you can get lost in.
            string precip = Game?.Environment?.Precipitation ?? string.Empty;
            if (precip is "sandstorm" or "ash")
            {
                far = Mathf.Min(far, renderDist * 0.3f);
            }

            // Dawn/dusk valley fog: a soft haze band around sunrise/sunset on calm days, burning off toward noon.
            float tod = Game?.Environment != null ? Game.LocalTimeOfDay : 0.5f;
            float dawn = Mathf.Max(0f, 1f - Mathf.Abs(tod - 0.27f) * 14f) + Mathf.Max(0f, 1f - Mathf.Abs(tod - 0.73f) * 14f);
            if (dawn > 0f && weatherIntensity < 0.4f)
            {
                far = Mathf.Lerp(far, far * 0.7f, dawn);
            }

            RenderSettings.fogStartDistance = far * 0.55f;
            RenderSettings.fogEndDistance = far;

            // The actual visible haze: an explicit distance blend the block shader applies (Unity's MixFog is dead
            // on the unlit voxels). Strong toward the sky at the far edge so it masks chunk pop-in; faded out
            // indoors via _indoor so the cabin never hazes.
            float maxHaze = 0.7f * (1f - _indoor);
            Shader.SetGlobalVector(FogId, new Vector4(far * 0.55f, far, maxHaze, 1f));

            // Drive the volumetric fog (full-screen pass) from the same final view distance, so the screen-space
            // haze matches the linear fog's reach: ~0.6 raw fog at the far edge (then capped by the material's
            // _MaxFog). Storms/sandstorms/cloudy worlds → shorter far → denser fog, all from one value.
            // Faded out indoors (_indoor = ship/station interior fill) so the exterior haze never darkens the
            // cabin — a screen-space depth fog can't tell inside from out, so we gate it by the interior state.
            Shader.SetGlobalFloat(VolFogDensityId, (1.5f / Mathf.Max(40f, far)) * (1f - _indoor));
        }

        /// <summary>Places the glowing sun billboard in the sky in the sun direction, tinted by the
        /// system sun colour and faded near/below the horizon. Hidden during the space view.</summary>
        private void UpdateSunDisc(float sunHeight, Color sunColor, bool spaceSky)
        {
            if (_sunDisc == null || _sun == null)
            {
                return;
            }

            bool active = Camera != null && sunHeight > -0.12f && (Game == null || !Game.SpaceViewActive);
            _sunDisc.gameObject.SetActive(active);
            if (!active)
            {
                return;
            }

            Vector3 camPos = Camera.transform.position;
            Vector3 dir = (-_sun.transform.forward).normalized; // direction TO the sun
            // Kept comfortably inside the frustum (the disc isn't depth-tested, so distance only sets
            // its placement + angular size); avoids clipping against the camera's far plane.
            float dist = Mathf.Clamp(Camera.farClipPlane * 0.5f, 60f, 400f);
            _sunDisc.position = camPos + dir * dist;
            _sunDisc.rotation = Quaternion.LookRotation(camPos - _sunDisc.position); // billboard (Cull Off)
            float size = dist * (spaceSky ? 0.26f : 0.20f);
            _sunDisc.localScale = new Vector3(size, size, size);

            // Fade in as the sun rises; a touch brighter against an airless black sky.
            float a = Mathf.Clamp01(sunHeight * 1.6f + 0.3f) * (spaceSky ? 1.25f : 1f);
            Color c = sunColor;
            c.a = Mathf.Clamp01(a);
            _sunDiscMat.SetColor(ColorId, ShaderColor.Srgb(c));
        }

        private void OnDisable()
        {
            // Clear the tint so other scenes (menu) aren't affected.
            Shader.SetGlobalColor(LightId, new Color(1f, 1f, 1f, 0f));
            Shader.SetGlobalColor(Shader.PropertyToID("_Sc_LampColor"), new Color(0f, 0f, 0f, 0f)); // headlamp off
            Shader.SetGlobalColor(GradeTintId, new Color(0f, 0f, 0f, 0f)); // colour grade off (menu/space)
            Shader.SetGlobalFloat(IndoorId, 0f); // interior fill off (menu/space)
            Shader.SetGlobalColor(FloraTintId, new Color(0f, 0f, 0f, 0f)); // flora tint off (menu/space)
            Shader.SetGlobalFloat(VolFogDensityId, 0f); // volumetric fog off (menu/space)
            Shader.SetGlobalVector(FogId, new Vector4(0f, 1f, 0f, 0f)); // distance haze off (menu/space)
            RenderSettings.fog = false; // don't leak fog into the menu / space view
            if (_sunDisc != null)
            {
                _sunDisc.gameObject.SetActive(false);
            }
        }

        private static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);
    }
}
