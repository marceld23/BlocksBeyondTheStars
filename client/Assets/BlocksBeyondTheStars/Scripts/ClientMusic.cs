// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

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
    ///   • <b>Tracks</b> — the granular AI-composed library shipped as raw MP3s under
    ///     <c>StreamingAssets/music</c> (source: <c>client/Music</c>), mapped to many contexts (biomes,
    ///     ship interior, orbit, station, …). When several tracks fit one context the choice is random,
    ///     and a long stay re-rolls to another fitting track at the loop seam for variety.
    ///
    /// Tracks are <b>streamed on demand</b> (#1167): a track is fetched with
    /// <see cref="UnityWebRequestMultimedia"/> the first time its context comes up — over HTTP in the
    /// browser, from disk on desktop — and the director keeps whatever is playing until the file has
    /// arrived, then cross-fades. They used to live under <c>Resources/</c>, which baked all 40 songs
    /// (164 MB) into the WebGL player data file that every browser visitor downloads before the first
    /// frame. Loaded clips are released again once nothing plays them (a decoded multi-minute track is
    /// ~80 MB of PCM in the browser), and the next re-roll candidate is prefetched shortly before the
    /// current track ends so the seam stays seamless. A track that fails to load is dropped from its pool
    /// and the matching synth mood takes over.
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
        private const float PrefetchLead = 45f;      // start fetching the re-roll candidate this early (browser bandwidth)
        private const float DecodeTimeoutSeconds = 60f; // browser-side MP3 decode must finish within this, else the track is skipped
        private const float UnderwaterCutoff = 680f;  // Hz — music muffles while the head is submerged
        private const float OpenCutoff = 22000f;

        /// <summary>Where the track library lives inside StreamingAssets (synced from <c>client/Music</c>
        /// by scripts/sync-client-libs; kept OUT of <c>data/</c>, whose manifest the browser prefetches).</summary>
        public const string MusicFolder = "music";

        private GameObject _bus;          // child GO carrying the two music sources + the music-only low-pass
        private AudioSource _active, _fading;
        private AudioListener _listener;
        private AudioLowPassFilter _lowpass;

        private Context _context = (Context)(-1);
        private MusicMode _mode = (MusicMode)(-1);
        private List<string> _pool;       // current Tracks-mode candidate pool (null on the synth path)
        private string _activeName;        // current clip key (so a re-roll can avoid an immediate repeat)
        private string _fadingName;        // the clip fading out (kept loaded until it has gone quiet)
        private bool _activeLoops = true;  // single-track pools / synth loops loop in place (no re-roll)

        private bool _lastInGame = true;   // forces a menu-listener reconcile on the first (shell) frame
        private readonly System.Random _rng = new System.Random();
        private readonly Dictionary<SynthMood, AudioClip> _synthCache = new();

        // Streamed track library (#1167). _musicCache holds the few clips currently in use (active, fading,
        // prefetched) — TrimMusicCache releases the rest, a decoded track is tens of MB of PCM in the browser.
        private readonly Dictionary<string, AudioClip> _musicCache = new();
        private readonly Dictionary<string, List<Action<AudioClip>>> _inFlight = new();
        private readonly HashSet<string> _missingTracks = new();   // failed to load → dropped from the pools
        private string _prefetchedName;    // the re-roll candidate fetched ahead of the current track's end
        private string _prefetchedFor;     // …and the track it was fetched for (one prefetch per track)
        private string _rerolledFrom;      // the track whose end-of-track re-roll was already requested
        private int _switchSerial;         // bumps per SwitchTo; a load finishing for an older request only caches

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
            foreach (var al in FindObjectsByType<AudioListener>())
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
                     && _active.clip.length > 0f) // length 0 = still decoding (browser), not "over"
            {
                float remaining = _active.clip.length - _active.time;
                if (remaining <= RerollLead && _rerolledFrom != _activeName)
                {
                    // Once per track: if the candidate still has to download, the current track simply
                    // runs out and the successor fades in when it arrives (no per-frame re-requests).
                    _rerolledFrom = _activeName;
                    SwitchTo(want, mode, game, reroll: true);
                }
                else if (remaining <= PrefetchLead && _pool != null && _pool.Count > 1 && _prefetchedFor != _activeName)
                {
                    // Fetch the re-roll candidate while the current track still plays, so the seam needs
                    // no download wait. PickFrom prefers loaded clips, so the re-roll lands on this one.
                    _prefetchedFor = _activeName;
                    Prefetch(PickFrom(_pool, _activeName));
                }
            }

            // Bus volume = music volume (master is applied globally by the AudioListener). Silence over splash.
            float target = want == Context.Silent ? 0f : Mathf.Clamp01(settings?.MusicVolume ?? 0.6f);
            _active.volume = Mathf.MoveTowards(_active.volume, target, Time.deltaTime * CrossfadeRate);
            _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, Time.deltaTime * CrossfadeRate);
            if (_fading.volume <= 0f && _fading.isPlaying)
            {
                _fading.Stop();
                _fading.clip = null;
                _fadingName = null;
                TrimMusicCache(); // the faded-out track is no longer referenced — release it
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
                    foreach (var al in FindObjectsByType<AudioListener>())
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
            int serial = ++_switchSerial; // any track load still in flight for an older request only caches

            if (want == Context.Silent)
            {
                BeginFade(null, null, null, loop: true);
                return; // nothing plays; the old source fades out
            }

            var (track, synthClip, name, pool, loop) = Resolve(want, mode, game, exclude);
            if (track == null)
            {
                BeginFade(synthClip, name, null, loop);
                return;
            }

            if (_musicCache.TryGetValue(track, out var cached) && cached != null)
            {
                BeginFade(cached, track, pool, loop);
                return;
            }

            // Not loaded yet: keep whatever is playing and fade over once the file has arrived. If the
            // file turns out to be missing/unreachable, re-resolve — the pool has dropped it by then, so
            // this lands on another track or the synth mood.
            StartCoroutine(LoadTrack(track, clip =>
            {
                if (serial != _switchSerial)
                {
                    return; // a newer switch superseded this request; the clip stays cached for later
                }

                if (clip != null)
                {
                    BeginFade(clip, track, pool, loop);
                }
                else
                {
                    SwitchTo(want, mode, game, reroll);
                }
            }));
        }

        /// <summary>Swaps the sources and starts <paramref name="clip"/> on the new active one (fading up in
        /// Update while the previous track fades down). <c>null</c> = silence.</summary>
        private void BeginFade(AudioClip clip, string name, List<string> pool, bool loop)
        {
            (_active, _fading) = (_fading, _active);
            _fadingName = _activeName;
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

            TrimMusicCache();
        }

        /// <summary>What should play: a library track (by name, loaded lazily by the caller), or a synth clip.</summary>
        private (string track, AudioClip synthClip, string name, List<string> pool, bool loop) Resolve(
            Context want, MusicMode mode, GameBootstrap game, string exclude)
        {
            // The finale set-piece always plays its dedicated boss track, regardless of music mode (it is a
            // scripted moment). Falls through to the synth mood only if the track file is missing.
            if (IsFinale(want))
            {
                string trackName = FinaleTrack(want);
                if (!_missingTracks.Contains(trackName))
                {
                    return (trackName, null, trackName, null, true); // single looping track for the phase
                }
            }

            if (mode == MusicMode.Tracks && want != Context.Combat)
            {
                var pool = PoolFor(want, game);
                if (pool.Count > 0)
                {
                    string name = PickFrom(pool, exclude);
                    return (name, null, name, pool, pool.Count <= 1);
                }
            }

            // Synth path: Synth mode, combat (always synth), or a Tracks pool whose files are missing.
            var mood = MoodFor(want);
            return (null, SynthClip(mood), "synth:" + mood, null, true);
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

            names.RemoveAll(_missingTracks.Contains); // drop any whose file failed to load (e.g. not shipped)
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

        /// <summary>Random pick from the pool, avoiding <paramref name="exclude"/>. Tracks that are already
        /// loaded win over ones that would need a download first (so a prefetched re-roll candidate or a
        /// recently played track starts without a wait); variety comes from the pool rotating over time.</summary>
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

            var loaded = choices.FindAll(n => _musicCache.TryGetValue(n, out var c) && c != null);
            if (loaded.Count > 0)
            {
                choices = loaded;
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

        /// <summary>Where a library track is fetched from: <c>StreamingAssets/music/&lt;name&gt;.mp3</c> —
        /// an HTTP URL in the browser (StreamingAssets is served next to the player), a <c>file://</c> URI
        /// on desktop / in the Editor (percent-encoded, so installs under paths with spaces work).</summary>
        public static string TrackUrl(string name)
        {
            string root = Application.streamingAssetsPath;
            string relative = MusicFolder + "/" + name + ".mp3";
            if (root.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || root.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return root.TrimEnd('/') + "/" + relative;
            }

            string path = Path.Combine(root, MusicFolder, name + ".mp3");
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        /// <summary>Fetches a track (once; concurrent requests for the same name share one download) and
        /// hands the clip to <paramref name="onDone"/> — <c>null</c> when the file is missing or unreachable,
        /// in which case the track is dropped from the pools for this session.</summary>
        private IEnumerator LoadTrack(string name, Action<AudioClip> onDone)
        {
            if (_missingTracks.Contains(name))
            {
                onDone(null);
                yield break;
            }

            if (_musicCache.TryGetValue(name, out var cached) && cached != null)
            {
                onDone(cached);
                yield break;
            }

            if (_inFlight.TryGetValue(name, out var waiters))
            {
                waiters.Add(onDone);
                yield break;
            }

            waiters = new List<Action<AudioClip>> { onDone };
            _inFlight[name] = waiters;

            AudioClip clip = null;
            string url = TrackUrl(name);
            using (var request = UnityWebRequestMultimedia.GetAudioClip(url, AudioType.MPEG))
            {
                // Keep the MP3 compressed in memory on desktop (multi-minute songs). The browser decodes
                // on its side regardless; streaming playback is not a WebGL option. Order matters: the
                // handler starts with streamAudio on, and `compressed` is rejected while it is.
                var handler = (DownloadHandlerAudioClip)request.downloadHandler;
                handler.streamAudio = false;
                handler.compressed = true;
                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        clip = handler.audioClip;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[Music] Track '{name}' could not be decoded: {ex.Message}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[Music] Track '{name}' could not be loaded from '{url}': {request.error}");
                }
            }

            // The browser decodes the MP3 asynchronously: right after the download the clip has length 0
            // (and its loadState is not meaningful for web-request clips there — it stays Unloaded). Seen on
            // glitch.fun: the director mistook "length 0" for "track over" and re-rolled straight into a
            // second download. Hand the clip out only once it is really usable (length known); desktop /
            // Editor clips are complete immediately, so this never waits there.
            if (clip != null && clip.length <= 0f)
            {
                var before = clip.loadState;
                float started = Time.realtimeSinceStartup;
                while (clip != null && clip.length <= 0f && clip.loadState != AudioDataLoadState.Failed
                       && Time.realtimeSinceStartup - started < DecodeTimeoutSeconds)
                {
                    yield return null;
                }

                if (clip != null && clip.length > 0f)
                {
                    Debug.Log($"[Music] Track '{name}' decoded after {Time.realtimeSinceStartup - started:0.0} s (loadState {before} → {clip.loadState}).");
                }
            }

            if (clip != null && clip.length <= 0f)
            {
                Debug.LogWarning($"[Music] Track '{name}' did not finish decoding ({clip.loadState}); skipping it.");
                Destroy(clip);
                clip = null;
            }

            if (clip != null)
            {
                clip.name = name;
                _musicCache[name] = clip;
                Debug.Log($"[Music] Loaded track '{name}' ({clip.length:0} s).");
            }
            else
            {
                _missingTracks.Add(name);
            }

            _inFlight.Remove(name);
            foreach (var waiter in waiters)
            {
                waiter(clip);
            }

            // Nobody may be using it any more (e.g. the context moved on while it downloaded).
            TrimMusicCache();
        }

        /// <summary>Loads a track into the cache ahead of need (the re-roll candidate); no-op if known.</summary>
        private void Prefetch(string name)
        {
            if (string.IsNullOrEmpty(name) || _missingTracks.Contains(name) || _inFlight.ContainsKey(name)
                || (_musicCache.TryGetValue(name, out var clip) && clip != null))
            {
                return;
            }

            _prefetchedName = name;
            StartCoroutine(LoadTrack(name, _ => { }));
        }

        /// <summary>Releases every loaded track that is neither playing, fading out, nor the prefetched
        /// re-roll candidate. A decoded multi-minute track is tens of MB (PCM in the browser), so the cache
        /// must not grow with every context the player visits.</summary>
        private void TrimMusicCache()
        {
            if (_musicCache.Count == 0)
            {
                return;
            }

            List<string> drop = null;
            foreach (var entry in _musicCache)
            {
                string name = entry.Key;
                if (name == _activeName || name == _fadingName || name == _prefetchedName || _inFlight.ContainsKey(name))
                {
                    continue;
                }

                (drop ??= new List<string>()).Add(name);
            }

            if (drop == null)
            {
                return;
            }

            foreach (string name in drop)
            {
                var clip = _musicCache[name];
                _musicCache.Remove(name);
                if (clip != null)
                {
                    Destroy(clip); // UnityWebRequest-created clips are plain objects, not Resources assets
                }
            }
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
