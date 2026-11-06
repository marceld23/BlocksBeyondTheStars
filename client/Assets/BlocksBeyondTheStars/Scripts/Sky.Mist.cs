// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The haze of the Sky (#2393, #2406, #3270–#3272): the distance veil, the underwater murk, the height fog with its wisps
    /// (#3271) and its own lit colour (the world's <c>MistColor</c>, #3269), the cloud band at altitude (#3272), the colour a
    /// fog, dust or ion-storm episode gives all of it (#3270) and the cloud shadows. The rules are <see cref="MistLook"/>
    /// (Client.Core, tested headless); this file turns them into the globals <c>AtmosphereCommon.hlsl</c> reads (its header
    /// lists the layouts). Unity's own fog is only mirrored for the few legacy <c>Unlit/*</c> materials
    /// (<see cref="MirrorLegacyFog"/>) — the project's shaders never read it, and the Sky is its only writer.
    /// </summary>
    public sealed partial class Sky
    {
        private static readonly int MistWispId = Shader.PropertyToID("_Sc_MistWisp");
        private static readonly int MistCamId = Shader.PropertyToID("_Sc_MistCam");
        private static readonly int MistColorId = Shader.PropertyToID("_Sc_MistColor");
        private static readonly int CloudBandId = Shader.PropertyToID("_Sc_CloudBand");
        private static readonly int CloudBandColorId = Shader.PropertyToID("_Sc_CloudBandColor");

        /// <summary>Seconds the cloud band takes to come or go completely when the weather changes.</summary>
        private const float BandEaseSeconds = 20f;

        /// <summary>#3272: 0..1 how much of the world's cloud band the weather brings right now (eased over ~20 s; 0 for a world
        /// without a band, in an airless sky, in the flight view or with "Cloud fog" off). Independent of where the camera is,
        /// so the cloud deck can read it from above, below or inside a cave.</summary>
        public float CloudBandCover01 { get; private set; }

        /// <summary>#3271: 0..1 the world's height-fog strength right now — its mist amount × the time of day × the weather
        /// (<see cref="MistLook.HeightFogStrength"/>). Independent of the player's switches and of where the camera is; 0 in an
        /// airless sky and in the flight view. The mist cards read it.</summary>
        public float MistStrength01 { get; private set; }

        /// <summary>#3271: the lit mist colour (sRGB, alpha 1): the world's own mist colour under the daylight (a dark blue-grey
        /// at night, never black), tinted by a fog, dust or ion-storm episode and a festival; on a world without a mist colour
        /// of its own, the sky colour. The mist cards and the cloud deck tint with it.</summary>
        public Color MistColorLit { get; private set; } = Color.white;

        private byte[] _noiseG, _noiseB;  // CPU copies of the noise texture's even green and blue sheets (CloudNoiseSheet)
        private Vector2 _mistUv;          // the wisp sheet's wind drift (texture units, wrapped)
        private Vector4 _mistCam = new Vector4(1f, 0.5f, 1f, 0f); // _Sc_MistCam, see AtmosphereCommon.hlsl
        private float _bandCover;         // eased cover (CloudBandCover01)
        private bool _bandEased;          // false until the first band frame on this world (snaps)
        private float _bandBaseY, _bandTopY;
        private float _inCloud;           // 0..1 the camera stands in the band: the sky behind turns cloud, the sun dims
        private Color _bandColorSrgb = Color.white;
        private MistRgb _tintColor = new MistRgb(1f, 1f, 1f); // the weather's haze colour (#3270), eased
        private float _tintSky, _tintMist;                    // ... and its eased weights
        private float _nextAtmosphereLog;

        /// <summary>A small tiling noise texture (#2394), baked once, no asset: red = the cloud-shadow sheet (unchanged), green
        /// and blue = two even sheets the mist's wisps morph between and the cloud band reads its holes off (#3271/#3272). The
        /// green and blue bytes stay on the CPU too, for the camera's wisp factor and the camera-in-cloud value.</summary>
        private Texture2D BuildCloudNoise()
        {
            byte[] shadow = CloudNoiseSheet.Bake(CloudNoiseSheet.ShadowSeed);
            _noiseG = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedA);
            _noiseB = CloudNoiseSheet.BakeEven(CloudNoiseSheet.WispSeedB);
            int size = CloudNoiseSheet.Size;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = "CloudShadowNoise",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(shadow[i], _noiseG[i], _noiseB[i], 255);
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>#3271: the wisp sheet drifts downwind, slower than the clouds (a calm day still creeps).</summary>
        private void TickMistDrift(Vector2 windDir, float worldDt)
        {
            float freq = MistLook.WispFrequency(Game?.Environment?.MistWispiness ?? 0.5f);
            float speed = 0.3f + 2.2f * (Game != null ? Game.WindSpeed : 0f); // m/s
            // uv = position × freq + drift: subtracting moves the pattern along +windDir, the way the grit and the leaves go.
            _mistUv -= windDir * (speed * worldDt * freq);
            _mistUv = new Vector2(Mathf.Repeat(_mistUv.x, 1f), Mathf.Repeat(_mistUv.y, 1f));
        }

        /// <summary>
        /// Distance fog in the sky colour, so the world fades into the horizon. Storms pull it in, night thickens it a little;
        /// airless bodies (space sky) and the space view stay clear. Then the underwater murk, the height fog with its wisps and
        /// colour, the cloud band and the cloud shadows — every value a global the shared haze applies.
        /// </summary>
        private void ApplyFog(Color sky, Color sun, float weatherIntensity, float day, bool spaceSky)
        {
            bool fog = !spaceSky && (Game == null || !Game.SpaceViewActive);
            if (Game != null)
            {
                Game.FogActive = fog; // #1822: the renderer cull reads it
            }

            if (!fog)
            {
                SetFog(FogOff); // distance haze off
                SetCloudShadow(Vector4.zero);
                ClearMist();
                return;
            }

            var env = Game?.Environment;
            float airDensity = env?.AtmosphereDensity ?? 0.4f;
            float tod = env != null ? Game.LocalTimeOfDay : 0.5f;
            // Scale the fog to the actual render distance (view-distance chunks × 16-block chunk), so the haze engages WITHIN
            // the streamed terrain instead of beyond it and softly hides the chunk pop-in at the boundary.
            float renderDist = Mathf.Max(32f, ViewChunks * 16f);
            float far = DistanceFogEnd(env, renderDist, airDensity, weatherIntensity, day, tod);

            // Where the haze begins, as a fraction of the far edge: thin/clear-air worlds stay CRISP across the near + mid
            // distance and only veil the far edge (high start), while soupy worlds haze from much closer in (low start).
            // Weather that already crushed `far` keeps its near, dense look — a small `far` lands the haze close anyway.
            float fogStart = far * Mathf.Lerp(0.78f, 0.55f, Mathf.Clamp01(airDensity));
            MirrorLegacyFog(sky, fogStart, far);

            // The actual visible haze: an explicit distance blend the shaders apply. Reaches FULL opacity at the far edge, so
            // the last ring dissolves completely into the sky colour before it appears; per-world character lives in WHERE the
            // haze sits, not how opaque it gets. Faded out indoors via _indoor so the cabin never hazes.
            float maxHaze = (FogEnabled ? 1f : 0f) * (1f - _indoor); // 0 indoors or when the player disabled fog
            ApplyUnderwater(sky, day, airDensity, ref fogStart, ref far, ref maxHaze);
            SetFog(new Vector4(fogStart, far, maxHaze, (FogEnabled || _submerged > 0.001f) ? 1f : 0f));

            float exposure = Probe != null ? Probe.CameraExposure : 1f;
            float mist = ApplyHeightFog(env, tod, day, exposure);
            ApplyMistColours(env, sky, sun, day, weatherIntensity);
            ApplyCloudBand(env, weatherIntensity);
            Shader.SetGlobalVector(MistCamId, _mistCam);
            float cover = ApplyCloudShadows(env, weatherIntensity, day, exposure);
            LogAtmosphere(fogStart, far, maxHaze, mist, exposure, tod, cover);
        }

        /// <summary>Where the distance haze is complete (metres): the far-view range by the air's thinness, pulled in by storms,
        /// the night, fog weather, a festival's cosy haze, sand and ash storms and the dawn/dusk valley haze.</summary>
        private float DistanceFogEnd(WorldEnvironment env, float renderDist, float airDensity, float weatherIntensity, float day, float tod)
        {
            // Per-world air thickness sets where the haze sits. The multiplier is capped so the haze never sits past the
            // streamed terrain edge (#388). #1822: with far terrain behind the chunks the haze no longer has to hide their
            // edge — it reaches out toward the far-view range, by how thin the air is (FarHaze).
            int farView = Game?.FarView != null ? Game.FarView.ActiveRange : 0;
            float far = BlocksBeyondTheStars.Client.FarTerrain.FarHaze.BaseFar(renderDist, farView, airDensity);

            far *= Mathf.Lerp(1f, 0.8f, weatherIntensity); // storms haze in a bit more
            far *= Mathf.Lerp(0.9f, 1f, day);                // night a touch hazier than day

            // Fog weather (a transient state like rain/storm): a real pea-souper — well inside the view.
            if (string.Equals(env?.Weather, "fog", System.StringComparison.Ordinal))
            {
                far = Mathf.Min(far, renderDist * Mathf.Lerp(0.5f, 0.28f, Mathf.Clamp01(weatherIntensity)));
            }

            // #3004: a festival's cosy haze pulls the fog in a little (never a pea-souper: at most 40 % nearer).
            var festivalFx = FestivalMood.Effects(Game);
            if (festivalFx != null && festivalFx.FogBoost > 0f)
            {
                far *= 1f - 0.4f * Mathf.Clamp01(festivalFx.FogBoost);
            }

            // Sandstorms / ash storms CRUSH visibility — a wall of dust you can get lost in.
            string precip = env?.Precipitation ?? string.Empty;
            if (precip is "sandstorm" or "ash")
            {
                far = Mathf.Min(far, renderDist * 0.3f);
            }

            // Dawn/dusk valley fog: a soft haze band around sunrise/sunset on calm days, burning off toward noon.
            float dawn = Mathf.Max(0f, 1f - Mathf.Abs(tod - 0.27f) * 14f) + Mathf.Max(0f, 1f - Mathf.Abs(tod - 0.73f) * 14f);
            if (dawn > 0f && weatherIntensity < 0.4f)
            {
                far = Mathf.Lerp(far, far * 0.7f, dawn);
            }

            return far;
        }

        /// <summary>#3270: Unity's own fog. Only the few materials still on Unity's legacy <c>Unlit/Color</c> and
        /// <c>Unlit/Transparent</c> shaders read it (geyser cubes, lava falls, mining sparks, unlit creature parts, ship build
        /// and damage effects, drop-packet icons); every project shader hazes through AtmosphereCommon. Mirrored from the real
        /// distance haze — the same (weather-tinted) sky colour, start and end, off with the haze switch — so those objects
        /// fade with the landscape instead of standing out of it. The Sky is its only writer.</summary>
        private void MirrorLegacyFog(Color sky, float start, float end)
        {
            RenderSettings.fog = FogEnabled;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = sky;
            RenderSettings.fogStartDistance = start;
            RenderSettings.fogEndDistance = end;
        }

        /// <summary>#2401: under water the view closes in to a few metres in the water's own colour (clear seas see farther
        /// than murky swamps — the atmosphere density stands in for the water's clarity), independent of the player's haze
        /// switch: this is the water, not the weather. Eased over ~0.3 s so a dive does not snap.</summary>
        private void ApplyUnderwater(Color sky, float day, float airDensity, ref float fogStart, ref float far, ref float maxHaze)
        {
            bool under = UnderwaterEnabled && Camera != null && Game != null && !Game.SpaceViewActive
                         && Game.IsWaterAt(Camera.transform.position);
            _submerged = Mathf.MoveTowards(_submerged, under ? 1f : 0f, Time.deltaTime * 3.5f);
            UnderwaterFog = _submerged;
            if (_submerged > 0.001f)
            {
                float clarity = 1f - Mathf.Clamp01(airDensity) * 0.6f;
                float waterFar = 5f + 18f * clarity;
                fogStart = Mathf.Lerp(fogStart, 0.5f, _submerged);
                far = Mathf.Lerp(far, waterFar, _submerged);
                maxHaze = Mathf.Lerp(maxHaze, 1f, _submerged);
                Color water = WaterColours.Blend(new Color(0.08f, 0.26f, 0.42f), Game.Environment, 0.8f, Time.time);
                Color deep = Color.Lerp(sky, water * Mathf.Lerp(0.6f, 1f, day), _submerged);
                deep.a = 1f;
                Shader.SetGlobalColor(FogSkyId, ShaderColor.Srgb(deep));
                Shader.SetGlobalVector(FogSunId, new Vector4(0f, 0f, 0f, 0f)); // no golden haze under water
            }

            Shader.SetGlobalVector(UnderwaterId, new Vector4(under ? 1f : 0f, under ? WaterSurfaceAbove(Camera.transform.position) : 0f, Time.time, 0f));
            // #2414: the sea surface (from the far-terrain world info the server sends on join) keeps wet ground and snow
            // caps off the seabed; a world without a sea (or before the info arrived) publishes a very low value.
            Shader.SetGlobalFloat(SeaLevelId, Game?.FarView != null ? Game.FarView.SeaSurfaceY : -1e9f);
        }

        /// <summary>The world Y of the water surface above a submerged point: walk up through the water cells (the
        /// caustics only play on faces below it).</summary>
        private float WaterSurfaceAbove(Vector3 p)
        {
            int x = Mathf.FloorToInt(p.x), z = Mathf.FloorToInt(p.z), y = Mathf.FloorToInt(p.y);
            for (int i = 0; i < 48; i++)
            {
                if (!Game.IsWaterAt(new Vector3(x + 0.5f, y + i + 0.5f, z + 0.5f)))
                {
                    return y + i;
                }
            }

            return y + 48;
        }

        /// <summary>#2406: height fog — mist in the low ground. The floor comes from the probe (the valleys around the player);
        /// the strength from the world's mist amount (#3269: the server's value, the air density already in it), the time of
        /// day and the weather (<see cref="MistLook"/>). The camera's own exposure fades it out underground; the shaders mask it
        /// further with the skylight. Returns the strength the shaders get (0 with the switch off).</summary>
        private float ApplyHeightFog(WorldEnvironment env, float tod, float day, float exposure)
        {
            string weather = env?.Weather ?? string.Empty;
            float world = env != null && Probe != null ? MistLook.HeightFogStrength(env.MistAmount, tod, day, weather) : 0f;
            MistStrength01 = world;
            float mist = HeightFogEnabled ? world : 0f;
            float floor = MistLook.FogFloor(Probe != null ? Probe.FogFloor : 0f, weather);
            Shader.SetGlobalVector(FogHeightId, new Vector4(floor, MistLook.Falloff(mist), mist, exposure));
            ApplyWisps(env, mist);
            return mist;
        }

        /// <summary>#3271: the wisps — the world's wispiness (0 with the switch off: the smooth blanket), the noise frequency,
        /// the wind drift, the slow morph between the two sheets, and the wisp factor at the camera (read on the CPU off the
        /// same bytes the GPU samples, so the shader needs one texture fetch less).</summary>
        private void ApplyWisps(WorldEnvironment env, float mist)
        {
            float wispiness = env?.MistWispiness ?? 0f;
            float strength = mist > 0f ? MistLook.WispStrength(wispiness, FogWispsEnabled) : 0f;
            float freq = MistLook.WispFrequency(wispiness);
            float morph = MistLook.Morph(_windTime);
            float contrast = MistLook.MorphContrast(morph);
            Shader.SetGlobalVector(MistWispId, new Vector4(strength, freq, _mistUv.x, _mistUv.y));

            float camFactor = 1f;
            if (strength > 0f && Camera != null)
            {
                Vector3 p = Camera.transform.position;
                float u = (p.x + p.y * MistLook.WispShearX) * freq + _mistUv.x;
                float v = (p.z + p.y * MistLook.WispShearZ) * freq + _mistUv.y;
                camFactor = MistLook.WispFactor(strength, CloudNoiseSheet.Sample(_noiseG, u, v), CloudNoiseSheet.Sample(_noiseB, u, v), morph, contrast);
            }

            _mistCam.x = camFactor;
            _mistCam.y = morph;
            _mistCam.z = contrast;
        }

        /// <summary>#3271/#3272: the lit colours of the mist and the cloud band. A world without a mist colour of its own keeps
        /// the haze colour for its mist (the look before #3271) unless a fog episode or a festival tints it; the band always
        /// takes the world's lit cloud colour.</summary>
        private void ApplyMistColours(WorldEnvironment env, Color sky, Color sun, float day, float weatherIntensity)
        {
            int mistColor = env?.MistColor ?? -1;
            MistRgb skyRgb = ToRgb(sky), sunRgb = ToRgb(sun);
            MistRgb lit = MistLook.LitMist(mistColor, skyRgb, sunRgb, day, weatherIntensity);
            bool own = mistColor >= 0;
            if (own)
            {
                lit = MistLook.ApplyTint(lit, _tintColor, _tintMist);
            }
            else if (_tintMist > _tintSky + 0.01f)
            {
                // A colourless mist follows the sky, which carries the sky share of the tint already; ground fog tints the mist
                // only, so the rest goes on here.
                lit = MistLook.ApplyTint(lit, _tintColor, _tintMist - _tintSky);
                own = true;
            }

            // #3004: a festival's fog tint (Halloween's violet-grey, Hanami's pink) colours the valley mist; it used to go to
            // Unity's fog only, which no project shader reads (#3270). Brightness kept, so it never glows at night.
            var festivalFx = FestivalMood.Effects(Game);
            if (festivalFx != null && FestivalMood.TryParseTint(festivalFx.FogTint, out var festivalTint))
            {
                lit = MistLook.ApplyTint(lit, ToRgb(festivalTint), 0.5f);
                own = true;
            }

            MistColorLit = ToColor(lit);
            Color mistLinear = ShaderColor.Srgb(MistColorLit);
            Shader.SetGlobalVector(MistColorId, new Vector4(mistLinear.r, mistLinear.g, mistLinear.b, own ? 1f : 0f));

            MistRgb cloud = MistLook.LitCloud(env?.CloudColor ?? 0xEDEFF2, mistColor, skyRgb, sunRgb, day, weatherIntensity);
            _bandColorSrgb = ToColor(MistLook.ApplyTint(cloud, _tintColor, _tintMist));
            Color bandLinear = ShaderColor.Srgb(_bandColorSrgb);
            Shader.SetGlobalVector(CloudBandColorId, new Vector4(bandLinear.r, bandLinear.g, bandLinear.b, 1f));
        }

        /// <summary>#3272: the cloud band — its cover from the weather (<see cref="MistLook.BandCover"/>, eased so clouds form
        /// and clear over ~20 s), its camera gate (none underground, aboard or under water) and how far the camera stands in
        /// it right now (the sky behind turns to cloud, the sun dims).</summary>
        private void ApplyCloudBand(WorldEnvironment env, float weatherIntensity)
        {
            float baseY = env?.CloudBaseY ?? 0f, topY = env?.CloudTopY ?? 0f;
            float target = env != null ? MistLook.BandCover(env.Weather, weatherIntensity, env.CloudDensity, baseY, topY, CloudFogEnabled) : 0f;
            bool snap = !_bandEased || (Game != null && Game.CaptureEnvActive)
                        || !Mathf.Approximately(baseY, _bandBaseY) || !Mathf.Approximately(topY, _bandTopY);
            _bandCover = snap ? target : Mathf.MoveTowards(_bandCover, target, Time.deltaTime / BandEaseSeconds);
            _bandEased = true;
            _bandBaseY = baseY;
            _bandTopY = topY;
            CloudBandCover01 = _bandCover;

            float gate = (Probe != null ? 1f - Probe.UndergroundFactor : 1f) * (1f - _indoor) * (1f - _submerged);
            _mistCam.w = gate;
            Shader.SetGlobalVector(CloudBandId, new Vector4(baseY, topY, _bandCover, MistLook.BandEdge(baseY, topY)));

            _inCloud = 0f;
            if (_bandCover > 0.001f && Camera != null && FogEnabled)
            {
                Vector3 c = Camera.transform.position;
                float n = CloudNoiseSheet.Sample(_noiseG, (c.x + _cloudOffset.x) / MistLook.BandHoleTile, (c.z + _cloudOffset.y) / MistLook.BandHoleTile);
                _inCloud = MistLook.BandDensityAt(c.y, baseY, topY) * MistLook.BandFill(n, _bandCover) * gate;
            }
        }

        /// <summary>#3272: the camera background while the camera stands in a cloud — the sky behind turns to the cloud's colour,
        /// so the open sky does not shine blue through a cloud the terrain has vanished into.</summary>
        private Color InCloudSky(Color sky) => _inCloud > 0.001f ? Color.Lerp(sky, _bandColorSrgb, _inCloud * 0.9f) : sky;

        /// <summary>#2394: cloud shadows — the sheet drifts with the wind (Update accumulates the offset); cover follows the
        /// planet's cloud density and the weather; the strength is capped at a quarter of the direct sun so no scene turns
        /// gloomy, and it fades out at low sun and in airless skies. Returns the cover.</summary>
        private float ApplyCloudShadows(WorldEnvironment env, float weatherIntensity, float day, float exposure)
        {
            float cover = Mathf.Clamp01((env?.CloudDensity ?? 0.45f) * 0.9f + weatherIntensity * 0.4f);
            float strength = CloudShadowsEnabled ? 0.25f * Mathf.Clamp01(day * 2f) * exposure : 0f;
            SetCloudShadow(new Vector4(cover, _cloudOffset.x, _cloudOffset.y, strength));
            return cover;
        }

        /// <summary>#3270: a fog, dust, ion-storm or storm episode tints the sky — and with it the distance haze, the camera
        /// background and Unity's legacy fog — toward its colour (<see cref="MistLook.WeatherTint"/>), keeping the sky's
        /// brightness so the night never glows; the weights ease over ~2 s. Before, these colours went to Unity's fog only
        /// (<c>WeatherFx3D</c>), which none of the project's shaders read.</summary>
        private Color WeatherHazeSky(Color sky, float weatherIntensity)
        {
            var env = Game?.Environment;
            HazeTint target = env != null
                ? MistLook.WeatherTint(env.Weather, env.Precipitation, weatherIntensity, ToRgb(WeatherFx3D.GritWash))
                : HazeTint.None;
            bool snap = Game != null && Game.CaptureEnvActive;
            float step = Time.deltaTime * 0.5f;
            _tintSky = snap ? target.SkyWeight : Mathf.MoveTowards(_tintSky, target.SkyWeight, step);
            _tintMist = snap ? target.MistWeight : Mathf.MoveTowards(_tintMist, target.MistWeight, step);
            if (target.SkyWeight > 0f || target.MistWeight > 0f)
            {
                _tintColor = snap ? target.Color : MistRgb.Lerp(_tintColor, target.Color, Mathf.Clamp01(Time.deltaTime * 2f));
            }

            if (_tintSky <= 0.001f)
            {
                return sky;
            }

            MistRgb tinted = MistLook.ApplyTint(ToRgb(sky), _tintColor, _tintSky);
            return new Color(tinted.R, tinted.G, tinted.B, sky.a);
        }

        /// <summary>No haze at all (an airless sky, the flight view, the menu): the mist, the band and the colours off.</summary>
        private void ClearMist()
        {
            MistStrength01 = 0f;
            CloudBandCover01 = 0f;
            _bandCover = 0f;
            _bandEased = false;
            _inCloud = 0f;
            _mistCam.w = 0f;
            Shader.SetGlobalVector(MistWispId, Vector4.zero);
            Shader.SetGlobalVector(MistColorId, Vector4.zero);
            Shader.SetGlobalVector(CloudBandId, Vector4.zero);
            Shader.SetGlobalVector(MistCamId, _mistCam);
            RenderSettings.fog = false;
        }

        /// <summary>Capture diagnostics (#2405): one line every two seconds while a capture pins the environment, so a
        /// look-check run's Player.log says what the atmosphere globals were.</summary>
        private void LogAtmosphere(float fogStart, float far, float maxHaze, float mist, float exposure, float tod, float cover)
        {
            if (Game == null || !Game.CaptureEnvActive || Time.unscaledTime < _nextAtmosphereLog)
            {
                return;
            }

            _nextAtmosphereLog = Time.unscaledTime + 2f;
            Vector4 height = Shader.GetGlobalVector(FogHeightId);
            Debug.Log($"[Atmosphere] fog start={fogStart:F1} end={far:F1} max={maxHaze:F2} | mist={mist:F2} floor={height.x:F1} k={height.y:F3} exposure={exposure:F2} wisp={Shader.GetGlobalVector(MistWispId)} cam={_mistCam} mistColor={MistColorLit} | band cover={_bandCover:F2} inCloud={_inCloud:F2} y={_bandBaseY:F0}..{_bandTopY:F0} | tint sky={_tintSky:F2} mist={_tintMist:F2} | adaptEv={(Probe != null ? Probe.AdaptationEv : 0f):F2} modelLight={_modelLight:F2} biome={Game.Environment?.Biome} tod={tod:F2} | cloud cover={cover:F2} strength={_cloudShadowVector.w:F2} | wet={_wet:F2} snow={_snow:F2} submerged={_submerged:F2} | aboard={Game.Aboard} station='{Game.StationName}' sky={Game.ExposedToSky} indoor={_indoor:F2} pos={Game.PlayerPosition} | globals surface={Shader.GetGlobalVector(SurfaceId)} height={height} cloud={Shader.GetGlobalVector(CloudShadowId)} noise={(Shader.GetGlobalTexture(CloudNoiseId) != null)} | lights={Game.World?.LightSourceCount ?? -1} natural={Game.World?.NaturalLightSourceCount ?? -1} emitters={BlocksBeyondTheStars.Client.ClientWorld.NaturalEmittersLight}");
        }

        private static MistRgb ToRgb(Color c) => new MistRgb(c.r, c.g, c.b);

        private static Color ToColor(MistRgb c) => new Color(c.R, c.G, c.B, 1f);
    }
}
