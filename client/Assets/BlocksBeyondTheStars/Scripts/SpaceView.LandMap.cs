// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The landing-pad chooser (item 38): the planet map with its pads, the day/night terminator and the weather layer
    /// (#2176), opened before a landing and torn down after it.
    /// </summary>
    public sealed partial class SpaceView
    {
        private LandMapWeather _landWeather; // #2176: the weather layer of the open pad chooser

        /// <summary>Opens the landing-pad chooser for a body (item 38): asks the server for its pads + occupancy
        /// and shows the keyboard chooser. An empty body id means the current body (land back where you launched).</summary>
        private void OpenPadChooser(string bodyId)
        {
            _confirmLand = true;
            // Resolve the "" home-body shorthand to the real id before asking (#956): released servers
            // silently drop an empty-id request, and the reply echoes the id the chooser gates on.
            if (string.IsNullOrEmpty(bodyId) && Game.StarMap != null)
            {
                bodyId = Game.StarMap.ActiveLocationId;
            }

            _choosePadBody = bodyId ?? string.Empty;
            _padRequestAt = Time.time;
            _padRequestRetried = false;
            Game.Network?.SendRequestLandingPads(_choosePadBody);
        }

        /// <summary>Closes the landing-pad map without landing (Esc / cancel).</summary>
        private void CancelLandChooser()
        {
            _confirmLand = false;
            _choosePadBody = null;
            _landDestBody = null; // chooser cancelled — no committed landing destination
            HideLandMap();
        }

        /// <summary>Touches down on the chosen pad and tears the chooser down.</summary>
        private void LandOnPad(int padIndex)
        {
            // Remember the chosen destination before clearing _choosePadBody: the descent animation
            // (BeginLandingFlyAway) only fires a frame later, once the server confirms we left space, and
            // would otherwise read a nulled _choosePadBody → "" → fly back to the home/launch body instead.
            _landDestBody = _choosePadBody;
            Game.Network?.SendLeaveSpace(_choosePadBody, padIndex);
            _confirmLand = false;
            _choosePadBody = null;
            HideLandMap();
        }

        /// <summary>Builds (once per body) the planet map shown before landing: a top-down plan of the body's
        /// fixed landing pads plotted by their world X/Z, each a clickable marker — green = free (click to land
        /// here), red = taken by another player. The cursor is freed so the player can click a pad (item 38).</summary>
        private void ShowLandMap(BlocksBeyondTheStars.Networking.Messages.NetLandingPad[] pads)
        {
            if (_landMapGo != null && _landMapBody == _choosePadBody)
            {
                return; // already showing this body's pads
            }

            HideLandMap();
            _landMapBody = _choosePadBody;
            _captureFreePads.Clear(); // re-collected below for the synthetic capture cursor
            EnsureUi();

            var loc = Game.Localizer;
            // Cursor-only owner (#413): the pad map needs a free cursor for its click-to-land buttons,
            // but flight holds through _confirmLand, not MenuOpen.
            Game.SetCursorOwner(this, true);

            // Centre panel in the 1536×864 HUD space (equirect map strip + button rows below).
            const float pw = 760f, ph = 540f;
            float px = (UiKit.HudRefW - pw) * 0.5f, py = (UiKit.HudRefH - ph) * 0.5f;
            var panel = UiKit.AddPanel(_ui.transform, px, py, pw, ph, new Color(0.03f, 0.07f, 0.13f, 0.97f));
            panel.raycastTarget = true; // eat clicks behind the map
            _landMapGo = panel.gameObject;
            UiNav.Enable(_landMapGo); // pad: the stick walks the pad markers + Cancel, A lands (#1043) — no number keys on a pad

            string title = loc != null ? loc.Get("ui.space.pad_choose") : "Choose a landing pad";
            string bodyName = string.IsNullOrEmpty(_landTargetName) ? string.Empty : $" — {_landTargetName}";
            UiKit.AddText(panel.transform, 24, 18, pw - 48, 30, title + bodyName, 22, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            // REAL planet map (the request "pads must be where they actually are"): an equirect strip of
            // the whole body — full circumference × full latitude band — baked from the actual world
            // generation (seas, lakes, height-shaded ground, this world's flora hue). Pads sit at their
            // TRUE longitudes on it; before this the pads were normalised into their own bounding box on
            // a blank panel (distorted positions, no terrain reference).
            const float pad = 40f;
            float mapW = pw - pad * 2, mapH = mapW * 0.5f; // equirect 2:1 (latitude period = circ/2)
            float mapTop = 70f;

            var body = FindStarMapBody(_choosePadBody, out string bodySystem);
            string typeKey = body?.PlanetType;
            if (string.IsNullOrEmpty(typeKey)) { typeKey = Game?.Environment?.Biome; }
            string locName = !string.IsNullOrEmpty(body?.Name)
                ? PlanetOrbitLook.LocationKeyFor(bodySystem, body.Name)
                : Game?.LocationName ?? string.Empty;
            // #2691: the server's size of the body (seeded by the world from terrain generation 26). Missing from the star
            // map: the world we are on takes the size the server sent for it; the classic size only for anything else.
            bool padIsHere = string.IsNullOrEmpty(_choosePadBody) || _choosePadBody == Game?.StarMap?.ActiveLocationId;
            int circ = BodySize.CircumferenceOf(body,
                !string.IsNullOrEmpty(_choosePadBody) ? _choosePadBody : Game?.LocationName ?? "home", body?.Kind, typeKey,
                padIsHere ? (Game?.Circumference ?? 0) : 0);
            int latP = WorldConstants.LatitudePeriodFor(circ);

            var mapGo = new GameObject("PadMap", typeof(RectTransform));
            mapGo.transform.SetParent(panel.transform, false);
            UiKit.Place(mapGo, pad, mapTop, mapW, mapH);
            var raw = mapGo.AddComponent<UnityEngine.UI.RawImage>();
            raw.color = new Color(0.10f, 0.13f, 0.18f, 1f); // dark until the map is baked (#2172: off the main thread)
            raw.raycastTarget = true;
            string padBodyId = !string.IsNullOrEmpty(_choosePadBody) ? _choosePadBody : Game?.LocationName ?? "home";
            var mapLoading = UiKit.AddText(mapGo.transform, 0, 0, mapW, mapH, Loc("ui.map.mapping_surface", "Mapping the surface…"),
                16, UiKit.CyanDim, TextAnchor.MiddleCenter);

            // The weather layer (#2176): over the terrain, under the day/night band and the pad markers. The pads' own
            // weather comes from the server (exact); the layer is the shared projection, fronts drawn wider.
            var weatherGo = new GameObject("WeatherLayer", typeof(RectTransform));
            weatherGo.transform.SetParent(mapGo.transform, false);
            UiKit.Place(weatherGo, 0, 0, mapW, mapH);
            var weatherRaw = weatherGo.AddComponent<UnityEngine.UI.RawImage>();
            weatherRaw.raycastTarget = false;
            weatherRaw.enabled = false;
            var padPlanet = Game?.Content?.GetPlanet(typeKey ?? string.Empty);
            var lw = new LandMapWeather
            {
                Raw = weatherRaw,
                BodyId = padBodyId,
                Planet = padPlanet,
                MapW = mapW,
                MapH = mapH,
                Atmosphere = OrbitLook.For(padPlanet, Game?.WorldSeed ?? 0L, padBodyId, Game?.TerrainGeneration ?? 0),
                Seed = CloudShellSeed(padBodyId),
            };
            _landWeather = lw;
            bool cratered = body?.Kind == "Moon" && padPlanet is { IsAirless: true };
            var mapRequest = WorldMinimap.MakeRequest(Game.Content, Game.Atlas, Game.WorldSeed, locName, typeKey, circ, 256, 128,
                padBodyId, Game.TerrainContinents, Game.TerrainGeneration, cratered, Game.TerrainLavaCoreVolcanoes);
            bool sharpShown = false;
            void ShowMap(Texture2D tex, PlanetMapData data, bool sharp)
            {
                if (raw == null || sharpShown)
                {
                    return; // the chooser closed while the map baked, or the sharp map is already in
                }

                raw.texture = tex;
                raw.color = Color.white;
                if (lw.Tex != null && (lw.Tex.width != data.Width || lw.Tex.height != data.Height))
                {
                    lw.Raw.texture = null; // hidden until the next refresh rebuilds the layer at the new map's size
                    lw.Raw.enabled = false;
                    Destroy(lw.Tex);
                    lw.Tex = null;
                    lw.Buffer = null;
                }

                lw.Map = data;
                lw.SeenVersion = -1;
                if (mapLoading == null)
                {
                    return;
                }

                if (sharp)
                {
                    sharpShown = true;
                    Destroy(mapLoading.gameObject);
                }
                else
                {
                    // #2760: the coarse map is up — the note moves to a corner while the detail is still coming.
                    mapLoading.fontSize = 12;
                    mapLoading.alignment = TextAnchor.LowerRight;
                    mapLoading.color = new Color(1f, 1f, 1f, 0.85f);
                }
            }

            // #2760: never a blank panel — the coarse map of the body (the orbit sphere's, usually baked already) shows at
            // once, stretched; the sharp map replaces it when ready. A coarse map not baked yet comes first: it is cheap.
            if (!WorldMinimap.TryGet(mapRequest, out _, out _))
            {
                var coarseRequest = WorldMinimap.MakeRequest(Game.Content, Game.Atlas, Game.WorldSeed, locName, typeKey, circ, 96, 48,
                    padBodyId, Game.TerrainContinents, Game.TerrainGeneration, cratered, Game.TerrainLavaCoreVolcanoes);
                WorldMinimap.Request(coarseRequest, (tex, data) => ShowMap(tex, data, sharp: false),
                    priority: PlanetMapSchedule.InteractivePriority + 1);
            }

            WorldMinimap.Request(mapRequest, (tex, data) => ShowMap(tex, data, sharp: true),
                priority: PlanetMapSchedule.InteractivePriority);

            // Weather layer toggle (remembered — the same switch as the M map's layer).
            Button weatherBtn = null;
            weatherBtn = UiKit.AddButton(panel.transform, pw - 24 - 170, 18, 170, 30, WeatherToggleLabel(), () =>
            {
                MapWeatherLayer.On = !MapWeatherLayer.On;
                if (weatherBtn != null && weatherBtn.GetComponentInChildren<Text>() is { } t)
                {
                    t.text = WeatherToggleLabel();
                }

                RefreshLandWeather(force: true);
            });

            // Day/night terminator: shade the night half of the strip + mark dawn/dusk, so you can see which pads
            // sit in daylight vs darkness right now. Local time at longitude u is (arrivalTime + u); landing puts
            // you at the pad's longitude with that same arrival time, so a pad under the dark band = a night-side
            // touchdown. Children of the map (drawn over the texture, under the pad markers added below).
            DrawDayNightOverlay(mapGo.transform, mapW, mapH, Game != null ? Game.LandingPadsTimeOfDay : 0.35f);

            const float marker = 40f;
            foreach (var p in pads)
            {
                // True position on the strip: u = longitude / circumference, v = latitude in the band
                // (north up). Pads are scattered across BOTH longitude and latitude, so they spread over the
                // map at their real spots (the server sends each pad's true X/Z; this is the same data the
                // ship actually lands on).
                float u = Mathf.Repeat(p.X / (float)circ, 1f);
                float vNorm = Mathf.Clamp01(0.5f + p.Z / (float)latP);
                float mx = pad + u * mapW - marker * 0.5f;
                float my = mapTop + (1f - vNorm) * mapH - marker * 0.5f;
                // Keep the whole marker INSIDE the map rect, not just its centre — a pad on the seam (the home
                // pad is at longitude 0) would otherwise hang half off the left edge ("marker 1 too far left").
                mx = Mathf.Clamp(mx, pad, pad + mapW - marker);
                my = Mathf.Clamp(my, mapTop, mapTop + mapH - marker);

                // Your OWN pad is selectable (#977): you keep it reserved while you are up in space, and it is
                // the pad your ship is parked on — the one you most likely want to come back down to. It is
                // drawn cyan and captioned with your name, so it reads as yours rather than as just free.
                bool free = !p.Occupied;
                var col = free ? new Color(0.16f, 0.55f, 0.30f, 0.98f) : new Color(0.45f, 0.12f, 0.12f, 0.98f);
                int padIndex = p.Index;
                string label = (p.Index + 1).ToString();
                if (free)
                {
                    var btn = UiKit.AddButton(panel.transform, mx, my, marker, marker, label, () => LandOnPad(padIndex));
                    if (p.Lava && btn != null && btn.TryGetComponent<Image>(out var lavaImg))
                    {
                        lavaImg.color = new Color(0.78f, 0.30f, 0.08f, 0.98f); // a pad standing in lava (old saves) is orange-red
                    }
                    else if (p.Wet && btn != null && btn.TryGetComponent<Image>(out var wetImg))
                    {
                        wetImg.color = new Color(0.18f, 0.40f, 0.72f, 0.98f); // a seabed pad is blue on the map (#1622)
                    }

                    _captureFreePads.Add((new Vector2(mx + marker * 0.5f, my + marker * 0.5f), padIndex));
                    float captionY = my + marker;
                    if (p.Mine)
                    {
                        UiKit.AddText(panel.transform, mx - 30, captionY, marker + 60, 18,
                            string.IsNullOrEmpty(p.Occupant) ? Loc("ui.space.pad_yours", "your pad") : p.Occupant,
                            12, UiKit.Cyan, TextAnchor.UpperCenter);
                        captionY += 16;
                    }

                    if (p.Lava)
                    {
                        // A pad in lava (a save from before terrain generation 8): the server only hands it out when
                        // nothing else is free — say so before the player tries.
                        UiKit.AddText(panel.transform, mx - 50, captionY, marker + 100, 18, Loc("ui.space.pad_lava", "lava!"),
                            12, new Color(1f, 0.55f, 0.35f), TextAnchor.UpperCenter);
                    }
                    else if (p.Wet)
                    {
                        // A seabed pad (#1454): still selectable — the shaft is dry — but say so, and how deep
                        // (#1622), before the player commits.
                        string wetCaption = p.Depth > 0 ? $"{Loc("ui.space.pad_wet", "underwater")} · {p.Depth} m" : Loc("ui.space.pad_wet", "underwater");
                        UiKit.AddText(panel.transform, mx - 50, captionY, marker + 100, 18, wetCaption,
                            12, new Color(0.55f, 0.75f, 1f), TextAnchor.UpperCenter);
                    }

                    if (p.Lava || p.Wet)
                    {
                        captionY += 16;
                    }

                    AddPadWeatherCaption(panel.transform, mx, captionY, marker, p.Weather);
                }
                else
                {
                    var occ = UiKit.AddPanel(panel.transform, mx, my, marker, marker, col);
                    occ.raycastTarget = true;
                    UiKit.AddText(occ.transform, 0, 0, marker, marker, label, 18, UiKit.TextCol, TextAnchor.MiddleCenter, FontStyle.Bold);
                    string who = string.IsNullOrEmpty(p.Occupant) ? "—" : p.Occupant;
                    UiKit.AddText(panel.transform, mx - 30, my + marker, marker + 60, 18, who, 12, new Color(1f, 0.6f, 0.55f), TextAnchor.UpperCenter);
                    AddPadWeatherCaption(panel.transform, mx, my + marker + 16, marker, p.Weather);
                }
            }

            string hint = loc != null ? loc.Get("ui.space.pad_full") : string.Empty;
            bool anyFree = false;
            foreach (var p in pads) { if (!p.Occupied) { anyFree = true; break; } }
            UiKit.AddText(panel.transform, 24, ph - 96, pw - 48, 22,
                anyFree ? (loc != null ? loc.Get("ui.space.pad_choose") : "Click a free (green) pad to land")
                        : (loc != null ? loc.Get("ui.space.pad_full") : "All pads are occupied — wait for one to free up"),
                15, anyFree ? UiKit.CyanDim : new Color(1f, 0.6f, 0.55f), TextAnchor.MiddleCenter);

            UiKit.AddButton(panel.transform, (pw - 220) * 0.5f, ph - 64, 220, 46,
                loc != null ? loc.Get("ui.action.close") : "Cancel (Esc)", CancelLandChooser);
        }

        /// <summary>Draws the day/night terminator onto the equirect pad map (children of <paramref name="map"/>,
        /// in its local 0..mapW × 0..mapH space). A pad's local time is <paramref name="arrivalTod"/> + its
        /// longitude fraction; night is when that is outside dawn..dusk (0.25..0.75). Shades the night band dark
        /// (1–2 rects, longitude wraps) and draws warm/indigo lines at sunrise/sunset. Non-interactive overlay.</summary>
        private void DrawDayNightOverlay(Transform map, float mapW, float mapH, float arrivalTod)
        {
            // Night band spans half the circumference, beginning at sunset (local time 0.75).
            float nightStart = Mathf.Repeat(0.75f - arrivalTod, 1f); // u of sunset (start of night)
            var nightCol = new Color(0f, 0.012f, 0.05f, 0.46f);
            void Band(float u0, float uw)
            {
                if (uw <= 0.0005f) { return; }
                UiKit.AddPanel(map, u0 * mapW, 0f, uw * mapW, mapH, nightCol).raycastTarget = false;
            }

            if (nightStart + 0.5f <= 1f)
            {
                Band(nightStart, 0.5f);
            }
            else
            {
                Band(nightStart, 1f - nightStart);     // to the right edge …
                Band(0f, nightStart + 0.5f - 1f);      // … and wrapped past the seam
            }

            // Terminator lines: sunrise (local 0.25, warm) and sunset (local 0.75, indigo).
            float dawnU = Mathf.Repeat(0.25f - arrivalTod, 1f);
            float duskU = Mathf.Repeat(0.75f - arrivalTod, 1f);
            UiKit.AddPanel(map, dawnU * mapW - 1.5f, 0f, 3f, mapH, new Color(1f, 0.72f, 0.34f, 0.85f)).raycastTarget = false;
            UiKit.AddPanel(map, duskU * mapW - 1.5f, 0f, 3f, mapH, new Color(0.45f, 0.55f, 1f, 0.85f)).raycastTarget = false;
        }

        /// <summary>Finds a star-map body by id (the active system is searched too), or null.
        /// <paramref name="systemName"/> carries the owning system's name for the flora location key.</summary>
        private BlocksBeyondTheStars.Networking.Messages.NetBody FindStarMapBody(string bodyId, out string systemName)
        {
            systemName = string.Empty;
            var map = Game?.StarMap;
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies)
                {
                    if (b.Id == bodyId)
                    {
                        systemName = sys.Name;
                        return b;
                    }
                }
            }

            return null;
        }

        /// <summary>The weather layer state of the open pad chooser (#2176).</summary>
        private sealed class LandMapWeather
        {
            public RawImage Raw;
            public string BodyId = string.Empty;
            public BlocksBeyondTheStars.Shared.Definitions.PlanetType Planet;
            public OrbitAtmosphere Atmosphere;
            public PlanetMapData Map;
            public WeatherMapPixels Pixels;
            public Texture2D Tex;
            public Color32[] Buffer;
            public float MapW;
            public float MapH;
            public int Seed;
            public int SeenVersion = -1;
            public double NextRefresh;
            public readonly List<GameObject> FrontMarks = new List<GameObject>();
        }

        private string WeatherToggleLabel()
            => "☁ " + (MapWeatherLayer.On ? Loc("ui.map.weather_on", "Weather: on") : Loc("ui.map.weather_off", "Weather: off"));

        /// <summary>A pad's weather caption: glyph + name, in the HUD's warning colour for violent/exotic weather.</summary>
        private void AddPadWeatherCaption(Transform parent, float mx, float y, float marker, string state)
        {
            if (string.IsNullOrEmpty(state))
            {
                return; // an older server sends no pad weather
            }

            string family = WeatherLook.Family(state);
            Color col = WeatherLook.FamilyColorHex(family) is { } hex && ColorUtility.TryParseHtmlString(hex, out var c)
                ? c
                : new Color(0.85f, 0.92f, 1f);
            UiKit.AddText(parent, mx - 50, y, marker + 100, 18, $"{WeatherLook.Glyph(state)} {Loc("weather." + state, state)}",
                12, col, TextAnchor.UpperCenter);
        }

        /// <summary>Re-projects the chooser's weather layer when a snapshot arrived (and every 2 s as fronts drift).</summary>
        private void RefreshLandWeather(bool force)
        {
            var lw = _landWeather;
            if (lw == null || lw.Raw == null || Game == null)
            {
                return;
            }

            bool on = MapWeatherLayer.On;
            foreach (var mark in lw.FrontMarks)
            {
                if (mark != null)
                {
                    mark.SetActive(on);
                }
            }

            if (lw.Map == null || !PlanetWeatherVisuals.TryBodyWeather(Game, lw.BodyId, out var bw))
            {
                lw.Raw.enabled = false;
                return;
            }

            double now = Time.realtimeSinceStartupAsDouble;
            int version = Game.SystemWeather.Version;
            if (!force && lw.SeenVersion == version && now < lw.NextRefresh)
            {
                lw.Raw.enabled = on && lw.Tex != null;
                return;
            }

            lw.Pixels ??= new WeatherMapPixels();
            PlanetWeatherVisuals.Project(Game, bw, lw.Map, lw.Planet, lw.Atmosphere.CloudDensity, lw.Pixels);
            if (lw.Tex == null)
            {
                lw.Tex = new Texture2D(lw.Map.Width, lw.Map.Height, TextureFormat.RGBA32, false)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    name = "LandMapWeather",
                };
                lw.Buffer = new Color32[lw.Map.Width * lw.Map.Height];
            }

            PlanetWeatherVisuals.ComposeMapLayer(lw.Tex, lw.Buffer, lw.Map, lw.Pixels, PlanetWeatherVisuals.Noise(lw.Seed), lw.Atmosphere.CloudRgb);
            lw.Raw.texture = lw.Tex;
            lw.Raw.enabled = on;

            // Fronts: a drift arrow at the top of each band ("◀ front" moving west, "front ▶" moving east).
            foreach (var mark in lw.FrontMarks)
            {
                if (mark != null)
                {
                    Destroy(mark);
                }
            }

            lw.FrontMarks.Clear();
            string frontWord = Loc("ui.map.weather_front", "front");
            foreach (var f in Game.SystemWeather.Fronts(bw, now, lw.Map.Circumference))
            {
                float x = (float)(f.CenterX / lw.Map.Circumference) * lw.MapW;
                string text = f.Drift < 0 ? $"◀ {frontWord}" : $"{frontWord} ▶";
                var t = UiKit.AddText(lw.Raw.transform, Mathf.Clamp(x - 50f, 0f, lw.MapW - 100f), 2f, 100f, 18f, text, 12,
                    new Color(1f, 1f, 1f, 0.9f), TextAnchor.UpperCenter, FontStyle.Bold);
                t.raycastTarget = false;
                t.gameObject.SetActive(on);
                lw.FrontMarks.Add(t.gameObject);
            }

            lw.SeenVersion = version;
            lw.NextRefresh = now + 2.0;
        }

        /// <summary>Tears down the landing-pad map + releases its cursor ownership (the arbiter re-locks
        /// for flight only once no other panel is open, #413).</summary>
        private void HideLandMap()
        {
            if (_landMapGo != null)
            {
                Destroy(_landMapGo);
                _landMapGo = null;
            }

            if (_landWeather?.Tex != null)
            {
                Destroy(_landWeather.Tex);
            }

            _landWeather = null;

            _landMapBody = null;
            Game?.SetCursorOwner(this, false);
        }
    }
}
