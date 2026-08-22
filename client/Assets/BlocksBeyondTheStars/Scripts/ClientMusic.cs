// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Client.Music;
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
    ///   • <b>Synth</b> — a generative ambient engine (<see cref="SynthComposer"/>, #1176): every piece is
    ///     composed fresh from a seed per mood (menu / planet / space / combat), rendered in code over a few
    ///     frames, 40–110 s long; the root and mode of a planet piece come from the biome, so every ice planet
    ///     shares one flavour while progressions and patterns keep changing. No assets, no download.
    ///   • <b>Tracks</b> — the AI-composed library shipped as raw MP3s under <c>StreamingAssets/music</c>
    ///     (source: <c>client/Music</c>), mapped to contexts by <see cref="MusicLibrary"/> (biomes, ship
    ///     interior, orbit, station, star chart, …).
    ///
    /// Variety (#1172–#1174): <see cref="MusicPicker"/> plays every track of a pool once before anything
    /// repeats, blends the neutral all-round beds into the biome pools at a minority share (the biome keeps
    /// its identity), and remembers recent picks across contexts. After a track ends in a long-stay context
    /// the music may take a <b>rest</b> (<see cref="MusicRestPolicy"/>) — ambience only for a minute or
    /// three — before the next piece fades in. The time of day tints the filler set (sunrise at dawn, the
    /// nocturnal track at night); a storm or a hostile creature nearby ducks the music; a long dive switches
    /// to the deep-water bed; the open star chart and the crafting / tech tabs bring their own beds; the
    /// first landing on a planet in a session opens with the sunrise track.
    ///
    /// Tracks are <b>streamed on demand</b> (#1167): a track is fetched with
    /// <see cref="UnityWebRequestMultimedia"/> the first time it is needed — over HTTP in the browser, from
    /// disk on desktop — and the director keeps whatever is playing until the file has arrived, then
    /// cross-fades. Loaded clips (and rendered synth pieces) are released again once nothing plays them (a
    /// decoded multi-minute track is ~80 MB of PCM in the browser); the successor is chosen and prefetched
    /// 45 s before the current piece ends so the seam needs no wait and the browser fetches it exactly once.
    /// A track that fails to load is dropped from its pool and the matching synth mood takes over.
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

        // Context keys. The library's keys (menu, planet_ice, …) are reused verbatim so they double as the
        // picker's bag keys; the director adds the ones that never pick from a pool.
        private const string Silent = "silent";
        private const string Combat = "combat";
        // Finale (P6): the staged Guardian-core confrontation. These override every other context and
        // always play their dedicated boss track (even in Synth mode / combat) — a scripted set-piece.
        private const string FinaleApproach = "finale_approach";
        private const string FinaleGauntlet = "finale_gauntlet";
        private const string FinaleHack = "finale_hack";
        private const string FinaleDialogue = "finale_dialogue";
        private const string FinaleResolution = "finale_resolution";

        private const string SynthPrefix = "synth:";

        private const float CrossfadeRate = 0.4f;   // volume units / s → ~2.5 s for a full fade
        private const float RerollLead = 3.0f;       // re-roll this many seconds before a piece ends
        private const float PrefetchLead = 45f;      // plan + fetch the successor this early (browser bandwidth)
        private const float DecodeTimeoutSeconds = 60f; // browser-side MP3 decode must finish within this, else the track is skipped
        private const float UnderwaterCutoff = 680f;  // Hz — music muffles while the head is submerged
        private const float TensionCutoff = 1400f;    // Hz — a hostile creature is close: darker, ducked
        private const float OpenCutoff = 22000f;
        private const float TensionRadiusSqr = 20f * 20f; // hostile within 20 m counts as "close"
        private const float DiveSeconds = 8f;         // submerged this long → the deep-water bed …
        private const float SurfaceSeconds = 5f;      // … and back to the surface pool after this long in air
        private const float MenuTabSeconds = 30f;     // crafting / tech tab open this long → its bed
        private const float DuckRate = 0.5f;          // ducking slew, units / s
        private const float SynthRenderBudget = 0.006f; // seconds of main-thread time per frame for synth rendering

        /// <summary>Where the track library lives inside StreamingAssets (synced from <c>client/Music</c>
        /// by scripts/sync-client-libs; kept OUT of <c>data/</c>, whose manifest the browser prefetches).</summary>
        public const string MusicFolder = "music";

        private GameObject _bus;          // child GO carrying the two music sources + the music-only low-pass
        private AudioSource _active, _fading;
        private AudioListener _listener;
        private AudioLowPassFilter _lowpass;

        private string _context;           // current context key (null before the first frame)
        private MusicMode _mode = (MusicMode)(-1);
        private string _activeName;        // current clip key (track name or synth:… id)
        private string _fadingName;        // the clip fading out (kept loaded until it has gone quiet)
        private string _lastPlayedName;    // the last piece that played (the re-roll after a rest avoids it)
        private bool _activeLoops = true;  // single-track contexts / finale loop in place (no re-roll)

        private bool _lastInGame = true;   // forces a menu-listener reconcile on the first (shell) frame
        private readonly System.Random _rng = new System.Random();
        private MusicPicker _picker;       // created in Awake (a field initializer may not reference _rng)

        // Clip cache (#1167): holds the few clips currently in use (active, fading, planned successor) —
        // TrimMusicCache releases the rest, a decoded track is tens of MB of PCM in the browser.
        private readonly Dictionary<string, AudioClip> _musicCache = new();
        private readonly Dictionary<string, List<Action<AudioClip>>> _inFlight = new();
        private readonly HashSet<string> _missingTracks = new();   // failed to load → dropped from the pools
        private readonly Dictionary<string, (SynthMood Mood, string Flavor, int Seed)> _synthSpecs = new();
        private string _prefetchedName;    // the successor fetched / rendered ahead of the current piece's end
        private string _plannedFor;        // …the piece it was planned for (one plan per piece)
        private string _plannedNext;       // the successor the picker already chose (so re-roll == prefetch)
        private string _plannedKey;        // …and the context it was chosen for
        private float _pendingRest;        // >0 = the current piece is followed by a rest of this many seconds
        private string _rerolledFrom;      // the piece whose end-of-piece re-roll was already requested
        private int _switchSerial;         // bumps per SwitchTo; a load finishing for an older request only caches

        // Rest window (#1173): nothing plays, the ambience beds carry the scene.
        private bool _resting;
        private float _restUntil;

        // Context signals (#1174).
        private float _lastIntegrity = -1f;   // combat detection: hull+shield drops while in space arm a tense window
        private float _combatUntil;
        private float _submergedSince = -1f, _surfacedSince = -1f;
        private bool _deep;
        private string _menuTabSeen;
        private float _menuTabSince;
        private readonly HashSet<string> _visitedBodies = new();
        private bool _arrivalPending;
        private float _duck = 1f;             // smoothed ducking multiplier (weather / hostile nearby)
        private bool _tension;

        // Finale: a one-shot resolution window after the Guardian core falls (then normal music resumes).
        private bool _finaleResolved;
        private float _resolutionUntil;

        private void Awake()
        {
            _picker = new MusicPicker(_rng);

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
            string want = CurrentContext(game);

            if (want != _context || mode != _mode)
            {
                _mode = mode;
                _resting = false;
                _plannedNext = null;    // chosen for the old context; its clip is released by the next trim
                _prefetchedName = null;
                _pendingRest = 0f;
                SwitchTo(want, mode, game, reroll: false);
            }
            else if (_resting)
            {
                if (Time.time >= _restUntil)
                {
                    _resting = false;
                    SwitchTo(want, mode, game, reroll: true);
                }
            }
            else if (!_activeLoops && _active.clip != null && _active.isPlaying
                     && _active.clip.length > 0f) // length 0 = still decoding (browser), not "over"
            {
                float remaining = _active.clip.length - _active.time;
                if (remaining <= RerollLead && _rerolledFrom != _activeName)
                {
                    // Once per piece: either the planned rest begins, or the planned successor fades in. If
                    // the successor still has to download, the current piece simply runs out and the
                    // successor fades in when it arrives (no per-frame re-requests).
                    _rerolledFrom = _activeName;
                    if (_pendingRest > 0f)
                    {
                        BeginRest(_pendingRest);
                    }
                    else
                    {
                        SwitchTo(want, mode, game, reroll: true);
                    }
                }
                else if (remaining <= PrefetchLead && _plannedFor != _activeName)
                {
                    // Decide what follows while the current piece still plays: a rest, or the next piece —
                    // chosen now and fetched / rendered ahead, so the seam needs no wait and the browser
                    // downloads it exactly once.
                    _plannedFor = _activeName;
                    PlanNext(want, mode, game);
                }
            }

            // Bus volume = music volume (master is applied globally by the AudioListener). Silence over the
            // splash and during a rest. Ducking (storm / hostile nearby) rides on top, slewed so it swells
            // and relaxes instead of stepping.
            float wantDuck = DuckFor(game);
            _duck = Mathf.MoveTowards(_duck, wantDuck, Time.deltaTime * DuckRate);
            float target = want == Silent || _resting ? 0f : Mathf.Clamp01(settings?.MusicVolume ?? 0.6f) * _duck;
            _active.volume = Mathf.MoveTowards(_active.volume, target, Time.deltaTime * CrossfadeRate);
            _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, Time.deltaTime * CrossfadeRate);
            if (_fading.volume <= 0f && _fading.isPlaying)
            {
                _fading.Stop();
                _fading.clip = null;
                _fadingName = null;
                TrimMusicCache(); // the faded-out piece is no longer referenced — release it
            }

            // Underwater muffle: while in-game and the player's head is submerged, sweep the music low-pass
            // down (ClientAudio already does the same for SFX on its own object). A hostile creature close
            // by darkens the music a little less. Open again otherwise.
            bool submerged = game != null && ClientAudio.Instance != null && ClientAudio.Instance.Submerged;
            float cutoff = submerged ? UnderwaterCutoff : _tension ? TensionCutoff : OpenCutoff;
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

        private string CurrentContext(GameBootstrap game)
        {
            if (game == null)
            {
                _visitedBodies.Clear(); // a new session: the first landing opens with the sunrise track again
                _arrivalPending = false;
                return Shell == null ? Silent : Shell.Phase switch
                {
                    ShellPhase.Loading => MusicLibrary.Loading,
                    ShellPhase.Splash or ShellPhase.Studio => Silent, // leave the splash stings alone
                    _ => MusicLibrary.Menu,                             // main menu / settings / credits / editors
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
            string finale = FinaleContext(game, inSpace);
            if (finale != null)
            {
                return finale;
            }

            if (inSpace)
            {
                if (Time.time < _combatUntil)
                {
                    return Combat;
                }

                return game.StarChartOpen ? MusicLibrary.StarChart : MusicLibrary.Space;
            }

            // The Tab menu's crafting / tech tabs, held open for a while, bring their own beds (#1174).
            string tab = game.MenuTabKey;
            if (tab != _menuTabSeen)
            {
                _menuTabSeen = tab;
                _menuTabSince = Time.time;
            }

            if (tab != null && Time.time - _menuTabSince >= MenuTabSeconds)
            {
                if (tab == "crafting")
                {
                    return MusicLibrary.Workshop;
                }

                if (tab == "tech")
                {
                    return MusicLibrary.Research;
                }
            }

            if (game.NearVendor)
            {
                return MusicLibrary.Station;        // a trade vendor / station hub nearby — the cooperative bed
            }

            if (game.Aboard)
            {
                return MusicLibrary.ShipInterior;   // inside the ship (not flying) — the calm cabin bed
            }

            if (!game.ExposedToSky)
            {
                return MusicLibrary.PlanetCave;     // underground / enclosed on a planet
            }

            // A long dive switches to the deep-water bed; a short splash only muffles (#1174). Hysteresis on
            // both edges, so an ocean swim does not flip the music with every wave.
            bool submerged = ClientAudio.Instance != null && ClientAudio.Instance.Submerged;
            if (submerged)
            {
                _surfacedSince = -1f;
                if (_submergedSince < 0f)
                {
                    _submergedSince = Time.time;
                }
                else if (!_deep && Time.time - _submergedSince >= DiveSeconds)
                {
                    _deep = true;
                }
            }
            else
            {
                _submergedSince = -1f;
                if (_surfacedSince < 0f)
                {
                    _surfacedSince = Time.time;
                }
                else if (_deep && Time.time - _surfacedSince >= SurfaceSeconds)
                {
                    _deep = false;
                }
            }

            if (_deep)
            {
                return MusicLibrary.PlanetDeep;
            }

            string surface = MusicLibrary.ContextForBiome(game.Environment?.Biome);

            // First landing on a planet in this session (#1174): open with the sunrise track once.
            if (MusicLibrary.IsPlanet(surface))
            {
                string body = game.StarMap?.ActiveLocationId;
                if (!string.IsNullOrEmpty(body) && _visitedBodies.Add(body))
                {
                    _arrivalPending = true;
                }
            }

            return surface;
        }

        /// <summary>The finale music phase, or null when the player is not engaging the Guardian core. Priority:
        /// resolution sting (just won) → dialogue duel → core hack → space gauntlet → approach. Resolved from the
        /// shared story flags (<see cref="GameBootstrap.Story"/>), the current location id (the finale system is
        /// <c>guardian_finale*</c>) and the live <see cref="FinaleView"/> phase.</summary>
        private string FinaleContext(GameBootstrap game, bool inSpace)
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

                return Time.time < _resolutionUntil ? FinaleResolution : null;
            }

            _finaleResolved = false;
            if (!story.GuardianSystemRevealed)
            {
                return null;
            }

            var fv = FinaleView.Instance;
            if (fv != null && fv.DuelActive)
            {
                return FinaleDialogue;
            }

            if (fv != null && fv.Hacking)
            {
                return FinaleHack;
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
                return FinaleGauntlet; // the elite wave is engaged
            }

            return FinaleApproach;
        }

        private static bool IsFinale(string c)
            => c is FinaleApproach or FinaleGauntlet or FinaleHack or FinaleDialogue or FinaleResolution;

        private static string FinaleTrack(string c) => c switch
        {
            FinaleApproach => "music_boss_approach",
            FinaleGauntlet => "music_boss_gauntlet",
            FinaleHack => "music_boss_hack",
            FinaleDialogue => "music_boss_dialogue",
            FinaleResolution => "music_boss_resolution",
            _ => null,
        };

        /// <summary>The ducking multiplier the scene asks for: a violent weather episode and a hostile creature
        /// close by (on foot) both pull the music down under the ambience; aboard / in space nothing ducks.</summary>
        private float DuckFor(GameBootstrap game)
        {
            _tension = false;
            if (game == null || game.Aboard || game.SpaceViewActive || game.InSpace)
            {
                return 1f;
            }

            float duck = 1f;
            var env = game.Environment;
            if (env != null && game.ExposedToSky)
            {
                float intensity = Mathf.Clamp01(game.WeatherIntensity);
                switch (env.WeatherFamily)
                {
                    case "violent": duck = 1f - 0.45f * intensity; break;            // storm / blizzard / sandstorm
                    case "wet":
                    case "obscuring":
                    case "windy": duck = 1f - 0.2f * intensity; break;               // rain, fog, gale: a touch
                }
            }

            if (game.NearestHostileSqr < TensionRadiusSqr)
            {
                _tension = true;
                duck = Mathf.Min(duck, 0.6f);
            }

            return duck;
        }

        /// <summary>Cross-fades to a clip for <paramref name="want"/>: the next piece from the context's pool
        /// (Tracks mode) or a freshly composed synth piece. <paramref name="reroll"/> keeps the same context but
        /// avoids repeating the piece that just ended.</summary>
        private void SwitchTo(string want, MusicMode mode, GameBootstrap game, bool reroll)
        {
            string exclude = reroll ? (_activeName ?? _lastPlayedName) : null;
            _context = want;
            int serial = ++_switchSerial; // any load still in flight for an older request only caches

            if (want == Silent)
            {
                BeginFade(null, null, loop: true);
                return; // nothing plays; the old source fades out
            }

            var (name, loop) = Resolve(want, mode, game, exclude);
            if (_musicCache.TryGetValue(name, out var cached) && cached != null)
            {
                BeginFade(cached, name, loop);
                return;
            }

            // Not ready yet: keep whatever is playing and fade over once the clip exists. If a track file turns
            // out to be missing/unreachable, re-resolve — the pool has dropped it by then, so this lands on
            // another track or the synth mood.
            EnsureClip(name, clip =>
            {
                if (serial != _switchSerial)
                {
                    return; // a newer switch superseded this request; the clip stays cached for later
                }

                if (clip != null)
                {
                    BeginFade(clip, name, loop);
                }
                else
                {
                    SwitchTo(want, mode, game, reroll);
                }
            });
        }

        /// <summary>Swaps the sources and starts <paramref name="clip"/> on the new active one (fading up in
        /// Update while the previous piece fades down). <c>null</c> = silence.</summary>
        private void BeginFade(AudioClip clip, string name, bool loop)
        {
            (_active, _fading) = (_fading, _active);
            _fadingName = _activeName;
            if (_activeName != null)
            {
                _lastPlayedName = _activeName;
            }

            _activeName = name;
            _activeLoops = loop;
            _active.clip = clip;
            _active.loop = loop;
            _active.volume = 0f; // fades up in Update while the old piece fades down
            if (clip != null)
            {
                _active.Play();
            }

            TrimMusicCache();
        }

        /// <summary>Starts a rest window (#1173): the current piece fades out, nothing follows until the
        /// timer runs out (or the context changes); then the next piece fades in.</summary>
        private void BeginRest(float seconds)
        {
            _pendingRest = 0f;
            _resting = true;
            _restUntil = Time.time + seconds;
            BeginFade(null, null, loop: true);
            Debug.Log($"[Music] Rest for {seconds:0} s in '{_context}'.");
        }

        /// <summary>Plans what follows the current piece: a rest, or the successor — chosen now (so the later
        /// re-roll lands on exactly this piece) and fetched / rendered ahead of need.</summary>
        private void PlanNext(string want, MusicMode mode, GameBootstrap game)
        {
            _pendingRest = IsFinale(want) ? 0f : MusicRestPolicy.RollRest(want, _rng);
            if (_pendingRest > 0f)
            {
                _plannedNext = null;
                return;
            }

            var (name, _) = Resolve(want, mode, game, _activeName);
            _plannedNext = name;
            _plannedKey = want;
            _prefetchedName = name;
            if (!_musicCache.ContainsKey(name) && !_inFlight.ContainsKey(name))
            {
                EnsureClip(name, _ => { });
            }
        }

        /// <summary>What should play: a library track name or a <c>synth:</c> piece id, and whether it loops in
        /// place (single-track contexts, the finale) or runs out and re-rolls.</summary>
        private (string name, bool loop) Resolve(string want, MusicMode mode, GameBootstrap game, string exclude)
        {
            // The finale set-piece always plays its dedicated boss track, regardless of music mode (it is a
            // scripted moment). Falls through to the synth mood only if the track file is missing.
            if (IsFinale(want))
            {
                string trackName = FinaleTrack(want);
                if (!_missingTracks.Contains(trackName))
                {
                    return (trackName, true); // single looping track for the phase
                }
            }

            // The successor chosen at plan time (already fetched / rendered) — re-roll == prefetch.
            if (_plannedNext != null && _plannedKey == want)
            {
                string planned = _plannedNext;
                _plannedNext = null;
                if (!_missingTracks.Contains(planned))
                {
                    return (planned, false);
                }
            }

            if (mode == MusicMode.Tracks && want != Combat)
            {
                if (_arrivalPending && MusicLibrary.IsPlanet(want) && !_missingTracks.Contains(MusicLibrary.ArrivalTrack))
                {
                    _arrivalPending = false;
                    return (MusicLibrary.ArrivalTrack, false); // once; the pool takes over at the re-roll
                }

                var phase = MusicLibrary.PhaseOf(game != null ? game.LocalTimeOfDay : 0.5f);
                var primary = Shipping(MusicLibrary.PrimaryTracks(want));
                var fillers = Shipping(MusicLibrary.FillerTracks(want, phase));
                if (primary.Count + fillers.Count > 0)
                {
                    string name = _picker.Next(want, primary, fillers, MusicLibrary.FillerShare(want), exclude);
                    return (name, primary.Count + fillers.Count <= 1);
                }
            }

            // Synth path: Synth mode, combat (always synth), or a Tracks pool whose files are missing.
            var mood = MoodFor(want);
            string flavor = MusicLibrary.IsPlanet(want) ? want : null;
            int seed = _rng.Next();
            string id = $"{SynthPrefix}{mood}:{flavor}:{seed}";
            _synthSpecs[id] = (mood, flavor, seed);
            return (id, false);
        }

        /// <summary>The pool minus the tracks whose file failed to load this session.</summary>
        private List<string> Shipping(IReadOnlyList<string> names)
        {
            var list = new List<string>(names.Count);
            foreach (string n in names)
            {
                if (!_missingTracks.Contains(n))
                {
                    list.Add(n);
                }
            }

            return list;
        }

        private static SynthMood MoodFor(string want) => want switch
        {
            MusicLibrary.Menu or MusicLibrary.Loading or FinaleResolution => SynthMood.Menu,
            MusicLibrary.Space or MusicLibrary.StarChart or FinaleApproach or FinaleDialogue => SynthMood.Space,
            Combat or FinaleGauntlet or FinaleHack => SynthMood.Combat,
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

        /// <summary>Makes the clip for <paramref name="name"/> available: renders a synth piece or streams a
        /// library track; <paramref name="onDone"/> gets the clip (or null when a track is missing).</summary>
        private void EnsureClip(string name, Action<AudioClip> onDone)
        {
            if (name.StartsWith(SynthPrefix, StringComparison.Ordinal))
            {
                StartCoroutine(RenderSynth(name, onDone));
            }
            else
            {
                StartCoroutine(LoadTrack(name, onDone));
            }
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

        /// <summary>Composes and renders a synth piece (<see cref="SynthComposer"/>) over a few frames — a
        /// time budget per frame keeps the main thread smooth — then wraps it in an <see cref="AudioClip"/>.
        /// Shares one render between concurrent requests like <see cref="LoadTrack"/>.</summary>
        private IEnumerator RenderSynth(string id, Action<AudioClip> onDone)
        {
            if (_musicCache.TryGetValue(id, out var cached) && cached != null)
            {
                onDone(cached);
                yield break;
            }

            if (_inFlight.TryGetValue(id, out var waiters))
            {
                waiters.Add(onDone);
                yield break;
            }

            waiters = new List<Action<AudioClip>> { onDone };
            _inFlight[id] = waiters;

            if (!_synthSpecs.TryGetValue(id, out var spec))
            {
                spec = (SynthMood.Planet, null, _rng.Next()); // unknown id (should not happen): a generic planet piece
            }

            var score = SynthComposer.Compose(spec.Mood, spec.Seed, spec.Flavor);
            var data = new float[score.TotalSamples];
            const int Chunk = 4096;
            var buffer = new float[Chunk];
            float started = Time.realtimeSinceStartup;
            float frameStart = started;
            for (int start = 0; start < data.Length; start += Chunk)
            {
                int count = Mathf.Min(Chunk, data.Length - start);
                SynthComposer.Render(score, buffer, start, count);
                Array.Copy(buffer, 0, data, start, count);
                if (Time.realtimeSinceStartup - frameStart >= SynthRenderBudget)
                {
                    yield return null;
                    frameStart = Time.realtimeSinceStartup;
                }
            }

            // Every piece lands at the same, deliberately modest level (~7 dB under the track library) with a
            // hard peak ceiling — the Synth style must never be the loud one.
            float gain = SynthComposer.Normalize(data);
            var clip = AudioClip.Create(id, data.Length, 1, score.SampleRate, false);
            clip.SetData(data, 0);
            _musicCache[id] = clip;
            Debug.Log($"[Music] Composed synth piece {spec.Mood}/{spec.Flavor ?? "-"} ({score.ModeName}, {score.Tempo:0} bpm, {score.Seconds:0} s, gain {gain:0.00}) in {Time.realtimeSinceStartup - started:0.0} s.");

            _inFlight.Remove(id);
            foreach (var waiter in waiters)
            {
                waiter(clip);
            }

            TrimMusicCache();
        }

        /// <summary>Releases every cached clip that is neither playing, fading out, nor the planned successor.
        /// A decoded multi-minute track is tens of MB (PCM in the browser), so the cache must not grow with
        /// every context the player visits.</summary>
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
                _synthSpecs.Remove(name);
                if (clip != null)
                {
                    Destroy(clip); // UnityWebRequest-created / generated clips are plain objects, not Resources assets
                }
            }
        }
    }
}
