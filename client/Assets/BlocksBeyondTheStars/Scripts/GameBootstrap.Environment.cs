// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's world environment (#2912): the held world clock, the per-world flora looks, the server's
    /// environment and weather (smoothed for the effects), the capture environment, and the sky and home scans.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>The live weather of every body in the current star system (#2173) — what the orbit cloud shells,
        /// the landing-pad map and the other maps project onto their planets.</summary>
        public SystemWeatherState SystemWeather { get; } = new SystemWeatherState();

        // Per-species flora tints for the current world (seed + location → one colour per flora block),
        // resolved by the chunk mesher into TEXCOORD2.yzw. Rebuilt on join and on every world change.
        private System.Collections.Generic.Dictionary<ushort, Color> _floraTintByBlock;

        // --- The held world (#908) -----------------------------------------------------------------------
        // #612 made the Esc menu stop the SERVER simulation, but the client was never told about it, so
        // everything it animates itself kept running behind the dialog. Fed from the server's PauseState.
        private readonly BlocksBeyondTheStars.Client.WorldClock _worldClock = new BlocksBeyondTheStars.Client.WorldClock();

        /// <summary>True while the server is holding the world for our pause menu. False while somebody else is
        /// still playing (#973: the world only holds once every player is in their menu) — it really is still
        /// running then.</summary>
        public bool WorldPaused => _worldClock.Paused;

        /// <summary>How many joined players are asking for the hold, and how many there are — the "2 of 3 ready"
        /// the pause dialog shows. Both 0 until the first <c>PauseState</c> arrives.</summary>
        public int PauseHoldingPlayers { get; private set; }
        public int PauseJoinedPlayers { get; private set; }

        /// <summary>The players the hold is still waiting for, comma-separated (empty while it is running or
        /// while nobody is waiting on anybody).</summary>
        public string PauseWaitingFor { get; private set; } = string.Empty;

        /// <summary>Seconds to advance CLIENT-SIDE world simulation by this frame — <c>Time.deltaTime</c> while
        /// the world runs, 0 while it is held. Deliberately derived from the live frame rather than from the
        /// clock's stored delta so it is correct regardless of script execution order.
        /// <para>Use this instead of <c>Time.deltaTime</c> for anything that is part of the WORLD (fauna,
        /// weather, creature bodies). Menus, music, the camera and shader time keep real time on purpose: a
        /// completely frozen image reads as a crash rather than as a pause.</para></summary>
        public float WorldDeltaTime => _worldClock.Paused ? 0f : Time.deltaTime;

        /// <summary>Monotonic count of unpaused seconds — the <c>Time.time</c> equivalent for world simulation.
        /// Timers scheduled against it (a creature's next idle call) do not all fire at once after a long pause.</summary>
        public float WorldTime => _worldClock.Now;

        /// <summary>Recomputes the world's per-species flora colours (deterministic from seed + location,
        /// so every client agrees). Chunks meshed afterwards pick them up via the tint resolver.</summary>
        private void RebuildFloraTints()
        {
            if (Content == null)
            {
                return;
            }

            var map = new System.Collections.Generic.Dictionary<ushort, Color>();
            foreach (var def in Content.Blocks.Values)
            {
                // Leaves (small flora + the tinted tree crowns) roll a per-species per-world hue; the trunks roll their
                // own per-world bark hue (ForWood) — DARK for the common trunks, so they always read clearly darker than
                // the leaves they carry, LIGHT for the pale bark (#2851). #2851: each leaf slot (tree_leaves, _b, _c) is a
                // block of its own and so rolls its own colour; the blossom crown and the Fifi plant's leaves keep their
                // painted colours (TreeFoliage.IsTinted leaves them out). The saguaro is a solid plant body that takes
                // its species colour like the cactus.
                bool isLeaf = def.Key.StartsWith("flora_", System.StringComparison.Ordinal)
                    || TreeFoliage.IsTinted(def.Key) || def.Key == ChunkMesher.SaguaroKey;
                bool isWood = TreeWood.IsKey(def.Key);
                if (!isLeaf && !isWood)
                {
                    continue;
                }

                // Farmed crops (#627) keep their own colour on every world. The per-world hue is what makes a
                // world's WILD plant life its own; a greenhouse berry has to read as ripe fruit whether it grows
                // in a village on a green world or on an alien one, or the player can't tell food from scenery. An
                // authored plant (#2085, the Fifi plant's berries) keeps the colours its designer chose, too.
                if (BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.KeepsOwnColour(def.Key))
                {
                    continue;
                }

                var (r, g, b) = isWood
                    ? BlocksBeyondTheStars.Shared.World.FloraTints.ForWood(_worldSeed, LocationName, def.Key)
                    : BlocksBeyondTheStars.Shared.World.FloraTints.For(_worldSeed, LocationName, def.Key);
                // The mesher writes these into TEXCOORD2 and the block shader multiplies them raw —
                // convert the sRGB-authored hue at this boundary (no-op in Gamma space).
                map[def.NumericId.Value] = ShaderColor.Srgb(new Color(r, g, b));
            }

            _floraTintByBlock = map;
            ChunkMesher.LinearColorSpace = QualitySettings.activeColorSpace == ColorSpace.Linear; // main thread: the mesher may run on a worker
            ChunkMesher.GroveCircumference = Circumference; // #2856: the grove colour drift wraps with this world's seams
            RebuildPlantLights();

            // #2835: the ground-scatter tufts take the world's grass-tuft colour (ScatterLit), like the grass billboards.
            var grassTuft = Content.GetBlock("flora_grasstuft");
            Shader.SetGlobalVector(GrassTintId, grassTuft != null && map.TryGetValue(grassTuft.NumericId.Value, out var grass)
                ? new Vector4(grass.r, grass.g, grass.b, 1f) : Vector4.zero);
            ApplyFloraLooks();
        }

        private static readonly int GrassTintId = Shader.PropertyToID("_Sc_GrassTint");

        /// <summary>#2832: every flora archetype with extra looks wears the one this world picks (seed + location, so every
        /// client agrees). The atlas repaints the block's own slot, so nothing re-meshes. #2851: the three leaf slots
        /// (<see cref="TreeFoliage.LeafSlotKeys"/>) pick from one shared pool as a group, so the tree kinds of one wood never
        /// wear the same leaves; every other block (the bark of wood_log and wood_log_b included — its end-grain cap is
        /// re-derived from the repainted slot) picks on its own.</summary>
        private void ApplyFloraLooks()
        {
            if (Atlas == null || Content == null)
            {
                return;
            }

            var slots = new System.Collections.Generic.List<BlockDefinition>();
            foreach (string slotKey in TreeFoliage.LeafSlotKeys)
            {
                if (Content.GetBlock(slotKey) is { } slot)
                {
                    slots.Add(slot);
                }
            }

            var slotLooks = FloraLooks.TextureKeysForGroup(_worldSeed, LocationName, slots);
            var looks = new System.Collections.Generic.Dictionary<ushort, string>();
            foreach (var def in Content.Blocks.Values)
            {
                string key;
                if (slotLooks.TryGetValue(def.Key, out string slotLook))
                {
                    key = slotLook;
                }
                else if (def.Looks is { Count: > 0 })
                {
                    key = FloraLooks.TextureKeyFor(_worldSeed, LocationName, def);
                }
                else
                {
                    continue;
                }

                if (key != def.Key)
                {
                    looks[def.NumericId.Value] = key;
                }
            }

            Atlas.SetLooks(looks);
        }

        /// <summary>Generation 11: the glowing plants that light their surroundings (<c>FloraCatalog.Species.Light</c>)
        /// cast a dim version of their own colour — this world's colour of the species, or the plant's own cell colour
        /// for the rainbow class. #2858: the classic glowers (a declared <c>lightColor</c>) cast this world's colour of
        /// their species too, at the brightness and reach they always had (<see cref="PlantGlowLight"/>); authored plants
        /// keep their own light. Registered with the light-source index before the world's chunks arrive.</summary>
        private void RebuildPlantLights()
        {
            if (World == null || Content == null)
            {
                return;
            }

            var lightById = new System.Collections.Generic.Dictionary<ushort, int>();
            var rainbowStrength = new System.Collections.Generic.Dictionary<ushort, float>();
            foreach (var sp in BlocksBeyondTheStars.Shared.Definitions.FloraCatalog.All)
            {
                if (Content.GetBlock(sp.Key) is { } glower && PlantGlowLight.TakesWorldColour(sp, glower))
                {
                    lightById[glower.NumericId.Value] = PlantGlowLight.PackedFor(_worldSeed, LocationName, glower);
                    continue;
                }

                if (sp.Light <= 0f || sp.Cultivated || Content.GetBlock(sp.Key) is not { } def)
                {
                    continue;
                }

                if (sp.Rainbow)
                {
                    rainbowStrength[def.NumericId.Value] = sp.Light;
                }
                else
                {
                    lightById[def.NumericId.Value] = FloraTints.ToRgb24(FloraTints.For(_worldSeed, LocationName, sp.Key), sp.Light);
                }
            }

            var ids = new System.Collections.Generic.List<ushort>(lightById.Keys);
            ids.AddRange(rainbowStrength.Keys);
            World.SetCellLightResolver((id, pos) =>
            {
                if (lightById.TryGetValue(id, out int rgb))
                {
                    return rgb;
                }

                return rainbowStrength.TryGetValue(id, out float strength)
                    ? FloraTints.ToRgb24(FloraTints.RainbowAt(pos.X, pos.Y, pos.Z), strength)
                    : 0;
            }, ids);

            // #2209: a bred plant keeps its packed form in the cell's glow channel — the index must read that as
            // the form (a light only when the form glows), never as a light colour.
            World.SetFormBlocks(Content.GetBlock(BlocksBeyondTheStars.Shared.Bio.FloraForm.BlockKey) is { } bred
                ? new[] { bred.NumericId.Value }
                : null);
        }

        /// <summary>The mesher's tint lookup: a flora block's per-world colour, black (= "use the global
        /// planet hue") when unknown.</summary>
        private Color FloraTintFor(BlockId id)
            => _floraTintByBlock != null && _floraTintByBlock.TryGetValue(id.Value, out var c) ? c : Color.black;

        /// <summary>Latest day/night + weather + sun colour (World systems).</summary>
        public WorldEnvironment Environment { get; private set; }

        /// <summary>Capture-only environment override (set by <see cref="ScreenshotDirector"/> and
        /// <see cref="ClipDirector"/>): when active, the current and every later <see cref="WorldEnvironment"/>
        /// broadcast is forced to a pinned weather (clear by default) at a pinned local time-of-day, so marketing
        /// screenshots aren't gloomy regardless of the world's spawn time-of-day or weather roll — and a look check
        /// can ask for dawn, rain or fog on purpose (#2405). No effect on normal play (the directors never set it).</summary>
        public bool CaptureEnvActive { get; private set; }

        /// <summary>Capture-only (#2765): while true the first-person hand (<see cref="Viewmodel"/>) and the block selection
        /// frame (<see cref="MiningFx"/>) are hidden, so a capture shows the world and nothing in front of it. Set by the
        /// capture directors; never true in normal play.</summary>
        public bool CaptureCleanView { get; set; }

        private float? _captureLocalTime = 0.5f;
        private string _captureWeather = "clear";

        /// <summary>Raised by <see cref="RequestCaptureSnap"/>: every time-smoothed look (weather ramps, eye adaptation,
        /// wet and snowy ground …) jumps straight to its target so the first recorded frame is already settled and two
        /// captures of the same seed come out identical (#2405).</summary>
        public static event System.Action CaptureSnap;

        /// <summary>Pin the visible environment for a capture: <paramref name="localTimeOfDay"/> (0.5 = noon;
        /// <c>null</c> = keep the world's own clock) and <paramref name="weather"/> (a weather state or precipitation
        /// form; <c>""</c> = keep the world's own weather). Applied to the current environment immediately and
        /// re-applied to every later broadcast until the session ends. The pinned server <c>TimeOfDay</c> is offset by
        /// the player's longitude so <see cref="LocalTimeOfDay"/> lands exactly on the requested value.</summary>
        public void SetCaptureEnvironment(float? localTimeOfDay = 0.5f, string weather = "clear")
        {
            CaptureEnvActive = true;
            _captureLocalTime = localTimeOfDay;
            _captureWeather = string.IsNullOrEmpty(weather) ? string.Empty : weather.Trim().ToLowerInvariant();
            if (Environment != null)
            {
                ApplyCaptureEnv(Environment);
            }
        }

        /// <summary>Snap every smoothed look to its target right now (see <see cref="CaptureSnap"/>).</summary>
        public void RequestCaptureSnap()
        {
            SnapWeatherSmoothing();
            CaptureSnap?.Invoke();
        }

        private void ApplyCaptureEnv(WorldEnvironment env)
        {
            if (_captureLocalTime is float local)
            {
                env.TimeOfDay = Mathf.Repeat(local - (string.IsNullOrEmpty(StationName) ? PlayerPosition.x / Circumference : 0f), 1f); // #1869: no longitude aboard a station
            }

            if (_captureWeather.Length == 0)
            {
                return; // the world's own weather
            }

            // A pinned weather is either a catalogue state (clear, rain, fog, blizzard …) or a precipitation form
            // (snow, sandstorm, hail …), which rides on the ladder state that produces it. The wire fields the look
            // reads — state, family, precipitation, intensity, wind — are filled the way the server would.
            string key = _captureWeather;
            var def = BlocksBeyondTheStars.Shared.Weather.WeatherCatalog.Find(key);
            string precip = "none";
            if (def != null)
            {
                precip = def.Precip.Length > 0 ? def.Precip[0] : "none";
            }
            else
            {
                precip = key;
                bool violent = key is "hail" or "sandstorm" or "dust" or "acid" or "meteor";
                key = violent ? "storm" : "rain";
                def = BlocksBeyondTheStars.Shared.Weather.WeatherCatalog.Find(key);
            }

            env.Weather = key;
            env.Precipitation = precip;
            env.WeatherFamily = (def?.Family ?? BlocksBeyondTheStars.Shared.Weather.WeatherFamily.Calm).ToString().ToLowerInvariant();
            env.Intensity = def != null && key != "clear" ? Mathf.Max(0.5f, BlocksBeyondTheStars.Shared.Weather.WeatherCatalog.MidPeak(def)) : 0f;
            env.IntensityRate = 0f;
            env.WindSpeed = def != null ? (def.WindLo + def.WindHi) * 0.5f : 0f;
            SnapWeatherSmoothing();
        }

        /// <summary>Jump the client-side weather easing to the current environment (captures only).</summary>
        private void SnapWeatherSmoothing()
        {
            var env = Environment;
            if (env == null)
            {
                return;
            }

            _weatherIntensity = Mathf.Clamp01(env.Intensity);
            _windSpeed = Mathf.Clamp01(env.WindSpeed);
            _windDirection = env.WindDirection;
            _envAge = 0f;
        }

        // --- Weather smoothing (#900) ---------------------------------------------------------------
        // The server broadcasts the environment on a 5 s heartbeat (plus on state changes and on a big
        // intensity move). Reading Intensity/WindSpeed straight off the message would step visibly, so the
        // client extrapolates with the authoritative IntensityRate and eases toward the result. Every FX
        // reads these instead of env.Intensity.
        private float _weatherIntensity;
        private float _windSpeed;
        private float _windDirection;
        private float _envAge;

        /// <summary>Smoothed, extrapolated weather strength 0..1 — a swelling storm ramps instead of stepping.</summary>
        public float WeatherIntensity => _weatherIntensity;

        /// <summary>Smoothed wind strength 0..1: cloud speed, precipitation slant, flora sway, jetpack drift.</summary>
        public float WindSpeed => _windSpeed;

        /// <summary>Smoothed wind direction in radians on the world XZ plane.</summary>
        public float WindDirection => _windDirection;

        /// <summary>Wind as a world-space direction vector, scaled by strength — ready to add to a velocity.</summary>
        public Vector3 WindVector => new Vector3(Mathf.Cos(_windDirection), 0f, Mathf.Sin(_windDirection)) * _windSpeed;

        private void TickWeatherSmoothing()
        {
            var env = Environment;
            if (env == null)
            {
                _weatherIntensity = Mathf.MoveTowards(_weatherIntensity, 0f, Time.deltaTime);
                _windSpeed = Mathf.MoveTowards(_windSpeed, 0f, Time.deltaTime);
                return;
            }

            _envAge += Time.deltaTime;
            // Extrapolate along the reported rate, but never past the 0..1 band or more than one heartbeat
            // out — a stale message must not run away on its own.
            float target = Mathf.Clamp01(env.Intensity + env.IntensityRate * Mathf.Min(_envAge, 6f));
            _weatherIntensity = Mathf.MoveTowards(_weatherIntensity, target, Time.deltaTime * 0.6f);
            _windSpeed = Mathf.MoveTowards(_windSpeed, Mathf.Clamp01(env.WindSpeed), Time.deltaTime * 0.35f);
            _windDirection = Mathf.MoveTowardsAngle(
                _windDirection * Mathf.Rad2Deg, env.WindDirection * Mathf.Rad2Deg, Time.deltaTime * 20f) * Mathf.Deg2Rad;
        }

        /// <summary>
        /// Renders a weather-scanner reading (#900) as a short, localized block: what the sky is doing here
        /// now, the next episodes with their ETA, and the nearest front. This is what turns weather from
        /// something that happens TO the player into something they can plan around.
        /// </summary>
        private void OnWeatherForecast(WeatherForecast m)
        {
            string Name(string state) => Localizer?.Get("weather." + state) ?? state;
            string Eta(float seconds) => seconds >= 90f
                ? string.Format(Localizer?.Get("weather.forecast.minutes") ?? "{0} min", Mathf.RoundToInt(seconds / 60f))
                : string.Format(Localizer?.Get("weather.forecast.seconds") ?? "{0} s", Mathf.RoundToInt(seconds));

            var sb = new System.Text.StringBuilder();
            sb.Append(Localizer?.Get("weather.forecast.title") ?? "Weather reading").Append(": ");
            sb.Append(string.Format(Localizer?.Get("weather.forecast.now") ?? "Now: {0}", Name(m.Current)));
            foreach (var entry in m.Upcoming)
            {
                sb.Append("  •  ").Append(string.Format(
                    Localizer?.Get("weather.forecast.then") ?? "in {0}: {1}", Eta(entry.StartsInSeconds), Name(entry.State)));
            }

            sb.Append("  •  ").Append(m.FrontDistance >= 0f
                ? string.Format(Localizer?.Get("weather.forecast.front") ?? "Front {0} blocks away", Mathf.RoundToInt(m.FrontDistance))
                : Localizer?.Get("weather.forecast.no_front") ?? "No front nearby");
            sb.Append("  •  ").Append(Localizer?.Get(
                m.SeasonWetness >= 0.5f ? "weather.forecast.season_wet" : "weather.forecast.season_dry") ?? string.Empty);
            if (!string.IsNullOrEmpty(Environment?.Season))
            {
                sb.Append("  •  ").Append(Localizer?.Get("ui.season." + Environment.Season) ?? Environment.Season); // #3042
            }

            LastMessage = sb.ToString();
        }

        /// <summary>True when the player has open sky overhead (no roof/cave ceiling). Drives weather
        /// suppression in caves / indoors. Refreshed a few times a second.</summary>
        public bool ExposedToSky { get; private set; } = true;
        private float _skyScanTimer;

        /// <summary>Cozy package (#2813): the player is at home — aboard their ship, or under a roof inside the air of a
        /// base they own (a sealed room of it, or the core's cube with a ceiling overhead). Drives the warm home grade,
        /// the evening cabin light, the lamplight motes and the once-only warning tones. Refreshed with <see cref="ExposedToSky"/>.</summary>
        public bool AtHome { get; private set; }

        /// <summary>How far a sealed room may reach from its base core and still count as that base (blocks).</summary>
        private const float HomeBaseReach = 96f;

        private bool ComputeAtHome()
        {
            if (InEva || SpaceViewActive)
            {
                return false; // a spacewalk keeps the bond to the ship (Aboard) but is anything but home; the flight view is the void
            }

            if (Aboard)
            {
                // Aboard also counts a friend's parked or docked hull (#2723) — a guest is welcome, but not at home.
                if (Visiting)
                {
                    return false;
                }

                if (LoadingPlanetType == "ship_interior")
                {
                    return true; // our own interior out in space
                }

                var ship = LandedShipBoundsAt(Mathf.FloorToInt(PlayerPosition.x), Mathf.FloorToInt(PlayerPosition.y), Mathf.FloorToInt(PlayerPosition.z), out _);
                return ship == null || ship.OwnerId == LocalPlayerId; // no hull model around us = our own sealed extension
            }

            if (LifeSupportSource != 3 || ExposedToSky || string.IsNullOrEmpty(LocalPlayerId) || Bases == null)
            {
                return false;
            }

            foreach (var b in Bases)
            {
                if (b == null || b.OwnerId != LocalPlayerId)
                {
                    continue;
                }

                // The scene position runs on past the world seam (SceneX), the base's position is canonical — compare
                // across the wrap, like the base-zone reach test does.
                float dx = (float)WorldConstants.WrapDeltaX((double)(b.X - PlayerPosition.x), Circumference);
                float dz = (float)WorldConstants.WrapDeltaZ((double)(b.Z - PlayerPosition.z), Circumference);
                float dy = b.Y - PlayerPosition.y;
                if (dx * dx + dy * dy + dz * dz <= HomeBaseReach * HomeBaseReach)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Scans straight up from the player's head for a roof/ceiling — false = covered (cave/indoors).</summary>
        private bool ComputeExposedToSky()
        {
            if (World == null)
            {
                return true;
            }

            if (Aboard)
            {
                return false; // inside the ship (server-authoritative) — the hull object roofs the player
            }

            int px = Mathf.FloorToInt(PlayerPosition.x);
            int py = Mathf.FloorToInt(PlayerPosition.y);
            int pz = Mathf.FloorToInt(PlayerPosition.z);
            for (int y = py + 2; y <= py + 50; y++)
            {
                if (!World.GetBlock(px, y, pz).IsAir)
                {
                    return false; // a solid block overhead = no open sky
                }
            }

            // Parked ship OBJECTS are not in the world grid — standing under a wing/hull still counts as covered.
            return !LandedShipCovers(px, py + 2, pz);
        }

        /// <summary>Start's subscriptions for the environment: the world pause, system weather, the world environment
        /// and the weather forecast. Called once from <see cref="Start"/>, before the transport connects.</summary>
        private void WireEnvironment()
        {
            // The server's answer to our pause intent (#908). Honours Allowed implicitly: a declined hold comes
            // back with Paused = false, so the client keeps simulating exactly as it did before.
            Network.PauseStateReceived += m =>
            {
                _worldClock.SetPaused(m.Paused);
                PauseHoldingPlayers = m.HoldingPlayers;
                PauseJoinedPlayers = m.JoinedPlayers;
                PauseWaitingFor = m.WaitingFor ?? string.Empty;
            };
            Network.SystemWeatherReceived += m => SystemWeather.Apply(m, Time.realtimeSinceStartupAsDouble); // #2173
            Network.WorldEnvironmentReceived += m =>
            {
                Environment = m;
                _envAge = 0f; // restart the extrapolation window (#900)
                Circumference = m.Circumference > 0 ? m.Circumference : WorldConstants.Circumference;
                World?.SetCircumference(Circumference); // chunk/block wrap at this world's size
                ChunkMesher.GroveCircumference = Circumference; // #2856: so does the grove colour drift
                if (CaptureEnvActive)
                {
                    ApplyCaptureEnv(Environment); // keep marketing shots clear-weather daylight across re-broadcasts
                }
            };
            Network.WeatherForecastReceived += OnWeatherForecast;
        }

        /// <summary>Update's sky scan: five times a second, whether the player stands under open sky and whether
        /// they are at home.</summary>
        private void TickSkyScan()
        {
            _skyScanTimer -= Time.deltaTime;
            if (_skyScanTimer <= 0f)
            {
                _skyScanTimer = 0.2f;
                ExposedToSky = ComputeExposedToSky();
                AtHome = ComputeAtHome();
            }
        }
    }
}
