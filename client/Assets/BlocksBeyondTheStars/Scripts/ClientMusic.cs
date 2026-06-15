using System.Collections.Generic;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The game's background-music director. A single persistent component owned by <see cref="AppShell"/>
    /// (so it spans splash → menu → loading → in-game and gives the shell screens music too), it picks a
    /// context from the shell phase and — once in-game — the world state, then cross-fades between two
    /// music sources (~2.5 s, so music never starts abruptly and transitions are smooth).
    ///
    /// Two selectable sources (<see cref="ClientSettings.MusicMode"/>, toggled in the settings menu):
    ///   • <b>Synth</b> — the original four code-synth ambient moods (menu / planet / space / combat),
    ///     each a short bundled <c>Resources/audio/music_*</c> loop with a synthesized fallback.
    ///   • <b>Tracks</b> — the granular AI-composed library under <c>Resources/music</c>, mapped to many
    ///     contexts (biomes, ship interior, orbit, station, …). When several tracks fit one context the
    ///     choice is random, and a long stay re-rolls to another fitting track at the loop seam for variety.
    ///
    /// Combat always uses the tense synth mood (the Tracks library is intentionally all-calm). SFX and
    /// ambience are untouched and stay on their own <see cref="ClientSettings.SfxVolume"/> bus; this bus is
    /// <see cref="ClientSettings.MusicVolume"/> (master is the <see cref="AudioListener"/>). The studio/title
    /// splash stings are one-shots played by AppShell and are left alone (music stays silent over them).
    /// </summary>
    public sealed class ClientMusic : MonoBehaviour
    {
        /// <summary>The owning shell; supplies settings, the current phase and the in-game world (or null).</summary>
        public AppShell Shell;

        private enum Context
        {
            Silent, Menu, Loading,
            ShipInterior, Station,
            PlanetGeneric, PlanetIce, PlanetDesert, PlanetLava, PlanetToxic, PlanetOcean,
            PlanetVerdant, PlanetCrystal, PlanetCave,
            Space, Combat,
            // Finale (P6): the staged Guardian-core confrontation. These override every other context and
            // always play their dedicated boss track (even in Synth mode / combat) — a scripted set-piece.
            FinaleApproach, FinaleGauntlet, FinaleHack, FinaleDialogue, FinaleResolution,
        }

        private enum SynthMood { Menu, Planet, Space, Combat }

        private const float CrossfadeRate = 0.4f;   // volume units / s → ~2.5 s for a full fade
        private const float RerollLead = 3.0f;       // re-roll this many seconds before a track ends
        private const float UnderwaterCutoff = 680f;  // Hz — music muffles while the head is submerged
        private const float OpenCutoff = 22000f;

        private GameObject _bus;          // child GO carrying the two music sources + the music-only low-pass
        private AudioSource _active, _fading;
        private AudioListener _listener;
        private AudioLowPassFilter _lowpass;

        private Context _context = (Context)(-1);
        private MusicMode _mode = (MusicMode)(-1);
        private List<string> _pool;       // current Tracks-mode candidate pool (null on the synth path)
        private string _activeName;        // current clip key (so a re-roll can avoid an immediate repeat)
        private bool _activeLoops = true;  // single-track pools / synth loops loop in place (no re-roll)

        private bool _lastInGame = true;   // forces a menu-listener reconcile on the first (shell) frame
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<string, AudioClip> _musicCache = new();
        private readonly Dictionary<SynthMood, AudioClip> _synthCache = new();

        // Combat detection: hull+shield drops while in space arm a tense window.
        private float _lastIntegrity = -1f;
        private float _combatUntil;

        // Finale: a one-shot resolution window after the Guardian core falls (then normal music resumes).
        private bool _finaleResolved;
        private float _resolutionUntil;

        private void Awake()
        {
            // The two music sources + the underwater low-pass live on a child object — NOT on this object,
            // which carries the AudioListener (a low-pass beside the active listener would muffle the whole
            // mix, SFX included; here it only ever filters the music sources).
            _bus = new GameObject("MusicBus");
            _bus.transform.SetParent(transform, false);
            _active = NewSource();
            _fading = NewSource();
            _lowpass = _bus.AddComponent<AudioLowPassFilter>();
            _lowpass.cutoffFrequency = OpenCutoff;
            _lowpass.lowpassResonanceQ = 1f;

            // Our own listener hears the shell screens (menu/loading). Silence any pre-existing scene
            // listener so there is exactly one active — WorldRig swaps to the world camera's in-game.
            foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                al.enabled = false;
            }

            _listener = gameObject.AddComponent<AudioListener>();
        }

        private AudioSource NewSource()
        {
            var src = _bus.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f; // non-positional background track
            src.volume = 0f;
            return src;
        }

        private void Update()
        {
            var settings = Shell != null ? Shell.Settings : null;
            var game = Shell != null ? Shell.CurrentBoot : null;

            ManageListener(game != null);

            var mode = settings?.MusicMode ?? MusicMode.Tracks;
            var want = CurrentContext(game);

            if (want != _context || mode != _mode)
            {
                _mode = mode;
                SwitchTo(want, mode, game, reroll: false);
            }
            else if (mode == MusicMode.Tracks && !_activeLoops && _active.clip != null && _active.isPlaying
                     && _active.time >= _active.clip.length - RerollLead)
            {
                SwitchTo(want, mode, game, reroll: true);
            }

            // Bus volume = music volume (master is applied globally by the AudioListener). Silence over splash.
            float target = want == Context.Silent ? 0f : Mathf.Clamp01(settings?.MusicVolume ?? 0.6f);
            _active.volume = Mathf.MoveTowards(_active.volume, target, Time.deltaTime * CrossfadeRate);
            _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, Time.deltaTime * CrossfadeRate);
            if (_fading.volume <= 0f && _fading.isPlaying)
            {
                _fading.Stop();
            }

            // Underwater muffle: while in-game and the player's head is submerged, sweep the music low-pass
            // down (ClientAudio already does the same for SFX on its own object). Open again above water.
            bool submerged = game != null && ClientAudio.Instance != null && ClientAudio.Instance.Submerged;
            float cutoff = submerged ? UnderwaterCutoff : OpenCutoff;
            _lowpass.cutoffFrequency = Mathf.MoveTowards(_lowpass.cutoffFrequency, cutoff, Time.deltaTime * 45000f);
        }

        /// <summary>Keeps exactly one <see cref="AudioListener"/> active: ours in the shell screens, the
        /// in-game camera's while playing. WorldRig disables every other listener when it builds the world
        /// and our listener is re-armed (and the others silenced) the moment we return to a menu.</summary>
        private void ManageListener(bool inGame)
        {
            if (inGame != _lastInGame)
            {
                if (!inGame)
                {
                    foreach (var al in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                    {
                        al.enabled = false;
                    }
                }

                _lastInGame = inGame;
            }

            if (_listener != null)
            {
                _listener.enabled = !inGame; // in-game the world camera's listener is the active one
            }
        }

        private Context CurrentContext(GameBootstrap game)
        {
            if (game == null)
            {
                return Shell == null ? Context.Silent : Shell.Phase switch
                {
                    ShellPhase.Loading => Context.Loading,
                    ShellPhase.Splash or ShellPhase.Studio => Context.Silent, // leave the splash stings alone
                    _ => Context.Menu,                                          // main menu / settings / credits / editors
                };
            }

            bool inSpace = game.SpaceViewActive || game.InSpace;

            var combat = game.ShipCombat;
            if (combat != null)
            {
                float integrity = combat.Hull + combat.Shield;
                if (_lastIntegrity >= 0f && integrity < _lastIntegrity - 0.01f && inSpace)
                {
                    _combatUntil = Time.time + 14f;
                }

                _lastIntegrity = integrity;
            }

            // Finale set-piece overrides everything once the player is engaging the Guardian core.
            var finale = FinaleContext(game, inSpace);
            if (finale != null)
            {
                return finale.Value;
            }

            if (inSpace)
            {
                return Time.time < _combatUntil ? Context.Combat : Context.Space;
            }

            if (game.NearVendor)
            {
                return Context.Station;        // a trade vendor / station hub nearby — the cooperative bed
            }

            if (game.Aboard)
            {
                return Context.ShipInterior;   // inside the ship (not flying) — the calm cabin bed
            }

            if (!game.ExposedToSky)
            {
                return Context.PlanetCave;     // underground / enclosed on a planet
            }

            return BiomeContext(game.Environment?.Biome);
        }

        /// <summary>The finale music phase, or null when the player is not engaging the Guardian core. Priority:
        /// resolution sting (just won) → dialogue duel → core hack → space gauntlet → approach. Resolved from the
        /// shared story flags (<see cref="GameBootstrap.Story"/>), the current location id (the finale system is
        /// <c>guardian_finale*</c>) and the live <see cref="FinaleView"/> phase.</summary>
        private Context? FinaleContext(GameBootstrap game, bool inSpace)
        {
            var story = game.Story;
            if (story == null || !story.Active)
            {
                _finaleResolved = false;
                return null;
            }

            // Resolution sting right after the core falls — then normal music resumes for good.
            if (story.GuardianDefeated)
            {
                if (!_finaleResolved)
                {
                    _finaleResolved = true;
                    _resolutionUntil = Time.time + 32f;
                }

                return Time.time < _resolutionUntil ? Context.FinaleResolution : (Context?)null;
            }

            _finaleResolved = false;
            if (!story.GuardianSystemRevealed)
            {
                return null;
            }

            var fv = FinaleView.Instance;
            if (fv != null && fv.DuelActive)
            {
                return Context.FinaleDialogue;
            }

            if (fv != null && fv.Hacking)
            {
                return Context.FinaleHack;
            }

            // Approach + gauntlet only while actually inside the finale system (else: revealed but elsewhere).
            string here = game.StarMap?.ActiveLocationId;
            bool inGuardianSystem = !string.IsNullOrEmpty(here) && here.StartsWith("guardian_finale");
            if (!inGuardianSystem)
            {
                return null;
            }

            if (inSpace && Time.time < _combatUntil)
            {
                return Context.FinaleGauntlet; // the elite wave is engaged
            }

            return Context.FinaleApproach;
        }

        private static bool IsFinale(Context c)
            => c is Context.FinaleApproach or Context.FinaleGauntlet or Context.FinaleHack
                 or Context.FinaleDialogue or Context.FinaleResolution;

        private static string FinaleTrack(Context c) => c switch
        {
            Context.FinaleApproach => "music_boss_approach",
            Context.FinaleGauntlet => "music_boss_gauntlet",
            Context.FinaleHack => "music_boss_hack",
            Context.FinaleDialogue => "music_boss_dialogue",
            Context.FinaleResolution => "music_boss_resolution",
            _ => null,
        };

        // Maps the server's planet/biome key (data/planets.json) to a music context.
        private static Context BiomeContext(string biome)
        {
            switch ((biome ?? string.Empty).ToLowerInvariant())
            {
                case "ice":
                case "tundra":
                case "glacier": return Context.PlanetIce;
                case "desert":
                case "salt_flats": return Context.PlanetDesert;
                case "lava":
                case "ashen":
                case "volcanic": return Context.PlanetLava;
                case "fungal":
                case "corrupted": return Context.PlanetToxic;
                case "ocean": return Context.PlanetOcean;
                case "swamp":
                case "jungle":
                case "forest":
                case "savanna": return Context.PlanetVerdant;
                case "orbital_station": return Context.Station;     // standing on a station hub
                case "ship_interior": return Context.ShipInterior;  // safety net; Aboard usually catches this
                default:
                    // crystal / crystal_living → the sparkling moon track; rocky / varied / highland /
                    // skylands / asteroid → the generic idle pool.
                    return (biome ?? string.Empty).Contains("crystal") ? Context.PlanetCrystal : Context.PlanetGeneric;
            }
        }

        /// <summary>Cross-fades to a clip for <paramref name="want"/>: a fresh random pick from the context's
        /// track pool (Tracks mode) or the matching synth mood. <paramref name="reroll"/> keeps the same
        /// context but avoids repeating the current track.</summary>
        private void SwitchTo(Context want, MusicMode mode, GameBootstrap game, bool reroll)
        {
            string exclude = reroll ? _activeName : null;
            _context = want;
            (_active, _fading) = (_fading, _active);

            if (want == Context.Silent)
            {
                _active.clip = null;
                _activeName = null;
                _pool = null;
                _activeLoops = true;
                return; // nothing plays; the old source fades out
            }

            var (clip, name, pool, loop) = Resolve(want, mode, game, exclude);
            _pool = pool;
            _activeName = name;
            _activeLoops = loop;
            _active.clip = clip;
            _active.loop = loop;
            _active.volume = 0f; // fades up in Update while the old track fades down
            if (clip != null)
            {
                _active.Play();
            }
        }

        private (AudioClip clip, string name, List<string> pool, bool loop) Resolve(
            Context want, MusicMode mode, GameBootstrap game, string exclude)
        {
            // The finale set-piece always plays its dedicated boss track, regardless of music mode (it is a
            // scripted moment). Falls through to the synth mood only if the track file is missing.
            if (IsFinale(want))
            {
                string trackName = FinaleTrack(want);
                var trackClip = LoadMusic(trackName);
                if (trackClip != null)
                {
                    return (trackClip, trackName, null, true); // single looping track for the phase
                }
            }

            if (mode == MusicMode.Tracks && want != Context.Combat)
            {
                var pool = PoolFor(want, game);
                if (pool.Count > 0)
                {
                    string name = PickFrom(pool, exclude);
                    return (LoadMusic(name), name, pool, pool.Count <= 1);
                }
            }

            // Synth path: Synth mode, combat (always synth), or a Tracks pool whose files are missing.
            var mood = MoodFor(want);
            return (SynthClip(mood), "synth:" + mood, null, true);
        }

        /// <summary>The Tracks-mode candidate pool for a context, filtered to files that actually ship.</summary>
        private List<string> PoolFor(Context want, GameBootstrap game)
        {
            List<string> names = want switch
            {
                Context.Menu => new() { "music_main_menu", "music_main_menu_2" },
                Context.Loading => new() { "music_loading", "music_loading_2" },
                Context.ShipInterior => new() { "music_ship_interior", "music_crafting_workshop", "music_research_blueprints" },
                Context.Station => new() { "music_multiplayer_hub", "music_multiplayer_hub_2" },
                Context.Space => new() { "music_space_orbit", "music_deep_space_lonely", "music_mystery_signal", "music_asteroid_mining", "music_cockpit_starmap" },
                Context.PlanetIce => new() { "music_planet_ice", "music_planet_ice_2" },
                Context.PlanetDesert => new() { "music_planet_desert", "music_planet_desert_2" },
                Context.PlanetLava => new() { "music_planet_lava", "music_planet_lava_2" },
                Context.PlanetToxic => new() { "music_planet_toxic", "music_planet_toxic_2" },
                Context.PlanetOcean => new() { "music_planet_ocean", "music_planet_ocean_2" },
                Context.PlanetVerdant => new() { "music_planet_verdant", "music_planet_verdant_2", "music_explore_planet", "music_explore_planet_2" },
                Context.PlanetCrystal => new() { "music_moon_crystal", "music_explore_planet", "music_explore_planet_2" },
                Context.PlanetCave => new() { "music_planet_cave", "music_planet_cave_2" },
                Context.PlanetGeneric => GenericPlanetPool(game),
                _ => new List<string>(),
            };

            names.RemoveAll(n => LoadMusic(n) == null); // drop any not bundled (e.g. an ungenerated track)
            return names;
        }

        /// <summary>Generic-planet idle pool, tinted by the local time of day so dawn brings the sunrise
        /// track and night the nocturnal one.</summary>
        private static List<string> GenericPlanetPool(GameBootstrap game)
        {
            float t = game != null ? game.LocalTimeOfDay : 0.5f;
            bool night = t < 0.23f || t >= 0.78f;
            var list = new List<string>
            {
                "music_explore_planet", "music_explore_planet_2",
                "music_idle_default", "music_idle_default_2",
            };
            list.Add(night ? "music_planet_night" : "music_planet_sunrise");
            return list;
        }

        private string PickFrom(List<string> pool, string exclude)
        {
            if (pool.Count == 1)
            {
                return pool[0];
            }

            var choices = exclude == null ? pool : pool.FindAll(n => n != exclude);
            if (choices.Count == 0)
            {
                choices = pool;
            }

            return choices[_rng.Next(choices.Count)];
        }

        private static SynthMood MoodFor(Context want) => want switch
        {
            Context.Menu or Context.Loading => SynthMood.Menu,
            Context.Space => SynthMood.Space,
            Context.Combat or Context.FinaleGauntlet or Context.FinaleHack => SynthMood.Combat,
            Context.FinaleApproach or Context.FinaleDialogue => SynthMood.Space,
            Context.FinaleResolution => SynthMood.Menu,
            _ => SynthMood.Planet,
        };

        private AudioClip LoadMusic(string name)
        {
            if (!_musicCache.TryGetValue(name, out var clip))
            {
                clip = Resources.Load<AudioClip>("music/" + name);
                _musicCache[name] = clip;
            }

            return clip;
        }

        /// <summary>The synth-mood clip: the short bundled <c>music_*</c> loop, or a synthesized fallback.</summary>
        private AudioClip SynthClip(SynthMood mood)
        {
            if (_synthCache.TryGetValue(mood, out var cached) && cached != null)
            {
                return cached;
            }

            string key = mood switch
            {
                SynthMood.Menu => "music_menu",
                SynthMood.Planet => "music_planet",
                SynthMood.Space => "music_space",
                _ => "music_combat",
            };

            var clip = Resources.Load<AudioClip>("audio/" + key) ?? BuildLoop(mood);
            _synthCache[mood] = clip;
            return clip;
        }

        /// <summary>
        /// Synthesizes a seamless ambient loop in the mood: consonant chords of sine pads plus a low drone,
        /// each chord swelling in and out (a half-sine envelope that reaches zero at every boundary, so chord
        /// changes and the loop seam are click-free). Combat adds a slow amplitude pulse for tension.
        /// </summary>
        private static AudioClip BuildLoop(SynthMood mood)
        {
            const int rate = 44100;
            float chordDur;
            bool pulse = false;
            float[][] chords;
            switch (mood)
            {
                case SynthMood.Planet: // brighter, major — wonder and discovery
                    chordDur = 4f;
                    chords = new[]
                    {
                        new[] { 261.63f, 329.63f, 392.00f }, // C
                        new[] { 349.23f, 440.00f, 523.25f }, // F
                        new[] { 392.00f, 493.88f, 587.33f }, // G
                        new[] { 220.00f, 261.63f, 329.63f }, // Am
                    };
                    break;
                case SynthMood.Space: // vast, low, sparse — slow two-note dyads
                    chordDur = 6f;
                    chords = new[]
                    {
                        new[] { 110.00f, 164.81f },          // A low dyad
                        new[] { 98.00f, 146.83f },           // G low dyad
                        new[] { 87.31f, 130.81f },           // F low dyad
                        new[] { 110.00f, 164.81f },
                    };
                    break;
                case SynthMood.Combat: // minor, pulsing — tension
                    chordDur = 2.5f;
                    pulse = true;
                    chords = new[]
                    {
                        new[] { 164.81f, 196.00f, 246.94f }, // Em
                        new[] { 146.83f, 174.61f, 220.00f }, // Dm
                        new[] { 164.81f, 196.00f, 246.94f }, // Em
                        new[] { 130.81f, 155.56f, 196.00f }, // Cm
                    };
                    break;
                default: // menu — the original calm pad
                    chordDur = 4f;
                    chords = new[]
                    {
                        new[] { 220.00f, 261.63f, 329.63f }, // Am
                        new[] { 261.63f, 329.63f, 392.00f }, // C
                        new[] { 196.00f, 246.94f, 293.66f }, // G
                        new[] { 164.81f, 196.00f, 246.94f }, // Em
                    };
                    break;
            }

            int chordSamples = Mathf.CeilToInt(rate * chordDur);
            int total = chordSamples * chords.Length;
            var data = new float[total];

            for (int i = 0; i < total; i++)
            {
                float t = i / (float)rate;                  // absolute time → continuous phase
                int chord = (i / chordSamples) % chords.Length;
                float localT = (i % chordSamples) / (float)rate;
                float env = Mathf.Sin(Mathf.PI * localT / chordDur); // 0 at the seams ⇒ click-free

                float s = 0f;
                foreach (float f in chords[chord])
                {
                    s += Mathf.Sin(2f * Mathf.PI * f * t) * 0.12f;
                }

                s += Mathf.Sin(2f * Mathf.PI * (chords[chord][0] * 0.5f) * t) * 0.10f; // low drone
                if (pulse)
                {
                    s *= 0.72f + 0.28f * Mathf.Sin(2f * Mathf.PI * 2f * t); // 2 Hz tension throb
                }

                data[i] = s * env;
            }

            var clip = AudioClip.Create("music_" + mood, total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
