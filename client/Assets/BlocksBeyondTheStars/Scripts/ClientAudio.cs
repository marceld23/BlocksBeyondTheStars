using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The game's audio router (M26). Loads the bundled SFX from <c>Resources/audio</c> and plays
    /// them on gameplay events; falls back to a few code-synth tones where no recording exists
    /// (craft / reject / scan). Other components reach it through the static <see cref="Instance"/>
    /// — <see cref="Cue"/> for 2D one-shots, <see cref="At"/> for 3D positional sounds (creatures),
    /// <see cref="SetAmbience"/> for the looping weather bed. Respects master/SFX volumes from
    /// <see cref="ClientSettings"/>. Recorded SFX are ElevenLabs-generated (see NOTICES / SOUND_DESIGN).
    /// </summary>
    public sealed class ClientAudio : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        /// <summary>Set in Awake so any component can play a cue without a wired reference.</summary>
        public static ClientAudio Instance { get; private set; }

        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private AudioSource _src;       // 2D one-shots
        private AudioSource _ambience;  // looping weather bed
        private AudioSource _hum;       // looping ship interior hum (procedural)
        private AudioSource _fluid;     // looping lava/water bed when near a fluid
        private AudioSource _drill;     // looping drill while mining
        private AudioSource _jet;       // looping jetpack thrust while firing
        private AudioSource _speeder;   // looping hover-speeder engine while driving

        // Underwater muffle: a low-pass on this GameObject filters every source on it — and ClientMusic lives
        // on the same object, so the music ducks too. Engages when the player's head is inside a fluid block.
        private const float UnderwaterCutoff = 680f;   // Hz — heavily muffled while submerged
        private const float OpenCutoff = 22000f;        // Hz — effectively off above water
        private AudioLowPassFilter _lowpass;
        private bool _submerged;                        // true while the head is in water/lava (for 3D one-shots)

        /// <summary>True while the player's head is inside water/lava — the music director reads this to
        /// muffle background music underwater the same way SFX are muffled here.</summary>
        public bool Submerged => _submerged;

        private AudioClip _ok, _err, _blip; // procedural fallbacks (no recorded equivalent)

        private bool _subscribed;
        private float _lastHull = -1f, _lastShield = -1f;
        private float _lastHealth = -1f;
        private string _ambienceId = string.Empty;
        private string _envBedId = "wind_light"; // last weather/biome bed; cave bed overrides it underground
        private string _fluidId = string.Empty;
        private float _fluidScanTimer;
        private float _caveScanTimer;
        private float _drillRefresh = -10f;
        private float _jetRefresh = -10f;
        private float _speederRefresh = -10f;
        private float _speederIntensity;
        private bool _speederBoost;
        private float _thunderTimer;
        private readonly System.Random _rng = new System.Random();

        private void Awake()
        {
            Instance = this;

            foreach (var clip in Resources.LoadAll<AudioClip>("audio"))
            {
                _clips[clip.name] = clip;
            }

            // Fill any cue that has no bundled recording with a code-synthesized version, so the whole
            // game is audible even with no recorded assets (recordings, when present, take priority).
            foreach (var id in ProceduralAudio.KnownIds)
            {
                if (!_clips.ContainsKey(id))
                {
                    var c = ProceduralAudio.Generate(id);
                    if (c != null)
                    {
                        _clips[id] = c;
                    }
                }
            }

            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = false;
            _src.spatialBlend = 0f;

            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.playOnAwake = false;
            _ambience.loop = true;
            _ambience.spatialBlend = 0f;
            _ambience.volume = 0f;

            _hum = gameObject.AddComponent<AudioSource>();
            _hum.playOnAwake = true;
            _hum.loop = true;
            _hum.spatialBlend = 0f;
            _hum.clip = Hum();
            _hum.volume = 0f;
            _hum.Play();

            _fluid = gameObject.AddComponent<AudioSource>();
            _fluid.playOnAwake = false;
            _fluid.loop = true;
            _fluid.spatialBlend = 0f;
            _fluid.volume = 0f;

            _drill = gameObject.AddComponent<AudioSource>();
            _drill.playOnAwake = false;
            _drill.loop = true;
            _drill.spatialBlend = 0f;
            _drill.volume = 0f;
            if (_clips.TryGetValue("drill_loop", out var drillClip))
            {
                _drill.clip = drillClip;
                _drill.Play();
            }

            _jet = gameObject.AddComponent<AudioSource>();
            _jet.playOnAwake = false;
            _jet.loop = true;
            _jet.spatialBlend = 0f;
            _jet.volume = 0f;
            _jet.clip = _clips.TryGetValue("jetpack_loop", out var jetClip) ? jetClip : JetClip();
            _jet.Play();

            _speeder = gameObject.AddComponent<AudioSource>();
            _speeder.playOnAwake = false;
            _speeder.loop = true;
            _speeder.spatialBlend = 0f;
            _speeder.volume = 0f;
            _speeder.clip = _clips.TryGetValue("vehicle_engine_loop", out var spClip) ? spClip
                : (_clips.TryGetValue("jetpack_loop", out var jc2) ? jc2 : JetClip()); // recorded engine, else a rumble
            _speeder.Play();

            _ok = Tone(660f, 0.12f, 0.4f);
            _err = Tone(110f, 0.20f, 0.5f);
            _blip = Tone(900f, 0.06f, 0.35f);

            _lowpass = gameObject.AddComponent<AudioLowPassFilter>();
            _lowpass.cutoffFrequency = OpenCutoff; // starts open (above water)
            _lowpass.lowpassResonanceQ = 1f;
        }

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                var n = Game.Network;
                n.BlockChanged += OnBlock;
                n.CraftCompleted += m => Play2D(m.Success ? _ok : _err);
                n.ActionRejected += _ => Play2D(_err);
                n.ScanResultReceived += _ => Play2D(_blip);
                n.ShipCombatStatusChanged += OnShip;
                n.PlayerStateUpdated += OnPlayerHealth;
                n.SpaceEntityDestroyed += _ => Cue("asteroid_break");
                n.SpaceClosed += m => { if (m.ShipDisabled) Cue("space_death"); };
                n.WorldEnvironmentReceived += OnEnvironment;
                n.StationBoardedReceived += _ => Cue("station_board");
                Game.HyperjumpStarted += () => Cue("hyperspace_jump");
                _subscribed = true;
            }

            float sfx = SfxVol();

            // Ship interior hum swells while aboard, fades out on foot.
            if (_hum != null)
            {
                float target = (Game != null && Game.Aboard) ? sfx * 0.45f : 0f;
                _hum.volume = Mathf.MoveTowards(_hum.volume, target, Time.deltaTime * 0.6f);
            }

            // Weather bed fades to a level scaled by weather intensity; rain/storm beds are fully
            // silenced when the player is under a roof or in a cave (no weather effects underground).
            if (_ambience != null && _ambience.clip != null)
            {
                float intensity = Game?.Environment?.Intensity ?? 0.4f;
                float target = sfx * Mathf.Clamp(0.2f + 0.45f * intensity, 0.18f, 0.6f);
                bool weatherBed = _ambienceId is "rain_loop" or "storm_loop" or "sandstorm_loop" or "ash_loop" or "wind_strong";
                if (weatherBed && Game != null && !Game.ExposedToSky)
                {
                    target = 0f;
                }

                _ambience.volume = Mathf.MoveTowards(_ambience.volume, target, Time.deltaTime * 0.8f);
            }

            // Occasional thunder during a rain thunderstorm (only with open sky overhead).
            if (Game?.Environment != null && Game.Environment.Weather == "storm"
                && Game.Environment.Precipitation == "rain" && Game.ExposedToSky)
            {
                _thunderTimer -= Time.deltaTime;
                if (_thunderTimer <= 0f)
                {
                    _thunderTimer = 8f + (float)_rng.NextDouble() * 12f;
                    Cue("thunder_" + (1 + _rng.Next(3)));
                }
            }

            // Drill loop while mining (PlayerController calls DrillTick each frame it drills).
            if (_drill != null && _drill.clip != null)
            {
                bool on = Time.time - _drillRefresh < 0.15f;
                _drill.volume = Mathf.MoveTowards(_drill.volume, on ? sfx * 0.4f : 0f, Time.deltaTime * 4f);
            }

            // Jetpack thrust loop while firing (PlayerController calls JetTick each frame it thrusts).
            if (_jet != null && _jet.clip != null)
            {
                bool on = Time.time - _jetRefresh < 0.15f;
                _jet.volume = Mathf.MoveTowards(_jet.volume, on ? sfx * 0.5f : 0f, Time.deltaTime * 5f);
            }

            // Hover-speeder engine loop while driving (PlayerController calls SpeederTick each frame). Volume +
            // pitch track the throttle; boost lifts the pitch.
            if (_speeder != null && _speeder.clip != null)
            {
                bool on = Time.time - _speederRefresh < 0.15f;
                _speeder.volume = Mathf.MoveTowards(_speeder.volume, on ? sfx * (0.34f + 0.30f * _speederIntensity) : 0f, Time.deltaTime * 6f);
                float targetPitch = on ? (0.78f + 0.5f * _speederIntensity) * (_speederBoost ? 1.18f : 1f) : 0.78f;
                _speeder.pitch = Mathf.MoveTowards(_speeder.pitch, targetPitch, Time.deltaTime * 2.5f);
            }

            // Underwater muffle: sweep the low-pass toward a heavy cut while the head is submerged, back to
            // open above water. Sampled per frame (one block lookup) so a quick dunk responds promptly.
            if (_lowpass != null)
            {
                _submerged = HeadInFluid();
                float target = _submerged ? UnderwaterCutoff : OpenCutoff;
                _lowpass.cutoffFrequency = Mathf.MoveTowards(_lowpass.cutoffFrequency, target, Time.deltaTime * 45000f);
            }

            // Lava/water ambience when near a fluid (throttled block scan around the player).
            _fluidScanTimer -= Time.deltaTime;
            if (_fluidScanTimer <= 0f)
            {
                _fluidScanTimer = 0.5f;
                UpdateFluidAmbience();
            }

            // Swap to the cave bed when the player ducks underground, back to the sky bed when they surface
            // (item 21 — cave echo). Throttled; the underground test is cheap (a sky-exposure flag).
            _caveScanTimer -= Time.deltaTime;
            if (_caveScanTimer <= 0f)
            {
                _caveScanTimer = 0.5f;
                RefreshAmbience();
            }

            if (_fluid != null && _fluid.clip != null)
            {
                _fluid.volume = Mathf.MoveTowards(_fluid.volume, _fluidId.Length == 0 ? 0f : sfx * 0.4f, Time.deltaTime * 0.8f);
            }
        }

        /// <summary>Called by the player controller each frame it is actively drilling; keeps the drill loop alive.</summary>
        public void DrillTick() => _drillRefresh = Time.time;

        /// <summary>Called each frame the jetpack is firing; keeps the thrust loop alive (fades out otherwise).</summary>
        public void JetTick() => _jetRefresh = Time.time;

        /// <summary>One-shot startup chirp when boarding/igniting a speeder.</summary>
        public void SpeederStart() => Cue("vehicle_startup", 0.7f);

        /// <summary>Shuts the engine loop down + plays the power-down one-shot when dismounting.</summary>
        public void SpeederStop()
        {
            _speederRefresh = -10f;
            _speederIntensity = 0f;
            _speederBoost = false;
            Cue("vehicle_shutdown", 0.6f);
        }

        /// <summary>Called each frame while driving: keeps the engine loop alive and feeds it the throttle
        /// (0..1) + boost so its volume/pitch track the speed.</summary>
        public void SpeederTick(float intensity, bool boost)
        {
            _speederRefresh = Time.time;
            _speederIntensity = Mathf.Clamp01(intensity);
            _speederBoost = boost;
        }

        /// <summary>Synthesized jetpack thrust: a low rumble under filtered hiss (used when no recording exists).</summary>
        private static AudioClip JetClip()
        {
            const int rate = 44100;
            const float duration = 1f; // loops seamlessly enough for a continuous hiss
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("jetpack_loop", samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(91);
            float lp = 0f;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float white = (float)(rng.NextDouble() * 2 - 1);
                lp += (white - lp) * 0.25f; // low-pass the noise into an airy hiss
                float rumble = Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.25f + Mathf.Sin(2f * Mathf.PI * 90f * t) * 0.12f;
                data[i] = (lp * 0.6f + rumble) * 0.5f;
            }

            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>True when the player's head (≈ the camera/ears, ~1.5 above the player root) sits inside a
        /// water or lava block — the cue to muffle the world underwater.</summary>
        private bool HeadInFluid()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var p = Game.PlayerPosition;
            string k = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y + 1.5f), Mathf.FloorToInt(p.z)))?.Key ?? string.Empty;
            return k.Contains("water") || k.Contains("lava");
        }

        private void UpdateFluidAmbience()
        {
            if (Game?.World == null || Game.Content == null)
            {
                SetFluid(string.Empty);
                return;
            }

            var p = Game.PlayerPosition;
            int px = Mathf.FloorToInt(p.x), py = Mathf.FloorToInt(p.y), pz = Mathf.FloorToInt(p.z);
            string found = string.Empty;
            for (int dx = -3; dx <= 3 && found.Length == 0; dx++)
            {
                for (int dy = -2; dy <= 2 && found.Length == 0; dy++)
                {
                    for (int dz = -3; dz <= 3 && found.Length == 0; dz++)
                    {
                        string k = Game.Content.BlockById(Game.World.GetBlock(px + dx, py + dy, pz + dz))?.Key ?? string.Empty;
                        if (k == "fire") found = "fire_crackle";       // a fire nearby crackles (item 30)
                        else if (k.Contains("lava")) found = "lava_bubble";
                        else if (k.Contains("water")) found = WaterBedFor(px + dx, py + dy, pz + dz);
                    }
                }
            }

            SetFluid(found);
        }

        /// <summary>Picks the looping water bed for a nearby water cell using the SAME surface
        /// classification the mesher feeds the shader: flowing river/brook → babble, open water →
        /// rolling surf, calm bounded lake/pond → the soft generic shoreline lap.</summary>
        private string WaterBedFor(int wx, int wy, int wz)
        {
            var world = Game.World;
            var id = world.GetBlock(wx, wy, wz);

            // Climb to this column's water surface — the classification reads the surface layer.
            int top = wy;
            for (int i = 0; i < 8 && world.GetBlock(wx, top + 1, wz).Value == id.Value; i++)
            {
                top++;
            }

            if (!world.GetBlock(wx, top + 1, wz).IsAir)
            {
                return "water_shore"; // roofed water (cave pool under rock) — keep the generic bed
            }

            var data = WaterSurface.Classify(world.GetBlock, id, wx, top, wz);
            if (data.x > 2.5f)
            {
                return "water_brook";
            }

            return data.x > 1.5f ? "water_surf" : "water_shore";
        }

        private void SetFluid(string id)
        {
            if (id == _fluidId)
            {
                return;
            }

            _fluidId = id;
            if (id.Length > 0 && _clips.TryGetValue(id, out var clip))
            {
                _fluid.clip = clip;
                _fluid.volume = 0f;
                _fluid.Play();
            }
        }

        // --- public cue API (used by other components via Instance) ---

        /// <summary>Plays a 2D one-shot by clip name (no-op if missing).</summary>
        public void Cue(string id, float vol = 1f)
        {
            if (_clips.TryGetValue(id, out var clip))
            {
                Play2D(clip, vol);
            }
        }

        /// <summary>Plays a 3D positional one-shot by clip name, with an optional pitch (creatures). When
        /// <paramref name="echo"/> is set the source gets a cave reverb tail (item 21 — cave dwellers).</summary>
        public void At(string id, Vector3 pos, float pitch = 1f, float vol = 1f, bool echo = false)
        {
            if (!_clips.TryGetValue(id, out var clip) || clip == null)
            {
                return;
            }

            var go = new GameObject("sfx_" + id);
            go.transform.position = pos;
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.spatialBlend = 1f;
            src.minDistance = 4f;
            src.maxDistance = 20f;                       // B1: creatures fade out much closer (was 45)
            src.rolloffMode = AudioRolloffMode.Linear;   // B1: linear → truly silent past maxDistance
            src.pitch = pitch;
            src.volume = Mathf.Clamp01(vol * SfxVol());
            if (echo)
            {
                var rev = go.AddComponent<AudioReverbFilter>();
                rev.reverbPreset = AudioReverbPreset.Cave; // a dripping-cavern reverberant tail
            }

            if (_submerged)
            {
                go.AddComponent<AudioLowPassFilter>().cutoffFrequency = UnderwaterCutoff; // muffle 3D one-shots too
            }

            src.Play();
            Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.2f);
        }

        /// <summary>Switches the looping weather/ambience bed (crossfades via the volume in Update).</summary>
        public void SetAmbience(string id)
        {
            if (_ambience == null || id == _ambienceId || !_clips.TryGetValue(id, out var clip))
            {
                return;
            }

            _ambienceId = id;
            _ambience.clip = clip;
            _ambience.volume = 0f;
            _ambience.Play();
        }

        private void OnEnvironment(WorldEnvironment e)
        {
            // The bad-weather bed is driven by the precipitation type (snow/hail howl, sandstorm hisses,
            // ash roars); otherwise the planet's biome ambience plays.
            _envBedId = e.Precipitation switch
            {
                "sandstorm" => "sandstorm_loop",
                "ash" => "ash_loop",
                "hail" or "snow" => "wind_strong",
                "rain" => e.Weather == "storm" ? "storm_loop" : "rain_loop",
                _ => BiomeBed(e.Biome),
            };
            RefreshAmbience();
        }

        /// <summary>Chooses the active ambience bed: the cave bed when the player is underground/enclosed on a
        /// planet (item 21 — cave echo), otherwise the weather/biome bed from the last environment update.</summary>
        private void RefreshAmbience()
        {
            bool underground = Game != null && Game.Environment != null && !Game.ExposedToSky
                && !Game.Aboard && !Game.InSpace;
            SetAmbience(underground ? "amb_cave" : _envBedId);
        }

        // The planet's profession key (PlanetType.Key) doubles as the ambience key. New world types (item 21)
        // get their own bed; close relatives reuse an existing one.
        private static string BiomeBed(string biome) => biome switch
        {
            "jungle" or "forest" or "savanna" => "amb_forest",
            "desert" or "salt_flats" => "amb_desert",
            "ice" or "tundra" => "amb_ice",
            "lava" => "amb_lava",
            "ashen" => "amb_ashen",       // volcanic wasteland — heat shimmer + distant bubbling
            "swamp" => "amb_swamp",
            "ocean" => "amb_ocean",        // surf
            "fungal" => "amb_fungal",      // eerie spore-forest hum
            "corrupted" => "amb_corrupted", // distorted murmur
            "skylands" or "highland" => "amb_wind_high", // thin high-altitude wind
            _ => "wind_light", // rocky / crystal / varied / asteroid → light wind
        };

        private void OnBlock(BlockChanged m)
        {
            if (m.Block == 0)
            {
                // Mined → a random material variant for variety (material-accurate later).
                string[] v = { "mine_stone", "mine_metal", "mine_crystal", "mine_dirt" };
                Cue(v[_rng.Next(v.Length)]);
            }
            else
            {
                Cue("place_block");
            }
        }

        private void OnShip(ShipCombatStatus s)
        {
            if (_lastShield >= 0f && s.Shield < _lastShield - 0.1f)
            {
                Cue("ship_shield_hit");
            }
            else if (_lastHull >= 0f && s.Hull < _lastHull - 0.1f)
            {
                Cue("ship_hull_hit");
            }

            _lastHull = s.Hull;
            _lastShield = s.Shield;
        }

        /// <summary>Plays a pain cue when the on-foot player's health drops (not on heal/respawn).</summary>
        private void OnPlayerHealth(BlocksBeyondTheStars.Networking.Messages.PlayerStateUpdate m)
        {
            if (_lastHealth >= 0f && m.Health < _lastHealth - 0.5f && m.Health > 0f)
            {
                Cue("hurt_player");
            }

            _lastHealth = m.Health;
        }

        private void Play2D(AudioClip clip, float vol = 1f)
        {
            if (clip != null)
            {
                _src.PlayOneShot(clip, Mathf.Clamp01(vol * SfxVol()));
            }
        }

        private float SfxVol()
        {
            float master = Settings?.MasterVolume ?? 0.8f;
            float sfx = Settings?.SfxVolume ?? 0.8f;
            return master * sfx;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (_subscribed && Game?.Network != null)
            {
                Game.Network.BlockChanged -= OnBlock;
                Game.Network.ShipCombatStatusChanged -= OnShip;
                Game.Network.PlayerStateUpdated -= OnPlayerHealth;
                Game.Network.WorldEnvironmentReceived -= OnEnvironment;
            }
        }

        /// <summary>Generates a short decaying sine tone as a fallback <see cref="AudioClip"/>.</summary>
        private static AudioClip Tone(float frequency, float duration, float volume)
        {
            const int rate = 44100;
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("tone", samples, 1, rate, false);
            var data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float envelope = Mathf.Exp(-t * 12f);
                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * volume;
            }

            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Generates a 2 s seamless low hull-hum loop (two low sines + faint air hiss).</summary>
        private static AudioClip Hum()
        {
            const int rate = 44100;
            const float duration = 2f;
            int samples = Mathf.CeilToInt(rate * duration);
            var clip = AudioClip.Create("hum", samples, 1, rate, false);
            var data = new float[samples];
            var rng = new System.Random(7);
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                float s = Mathf.Sin(2f * Mathf.PI * 80f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 120f * t) * 0.22f;
                s += (float)(rng.NextDouble() * 2 - 1) * 0.03f;
                data[i] = s * 0.5f;
            }

            clip.SetData(data, 0);
            return clip;
        }
    }
}
