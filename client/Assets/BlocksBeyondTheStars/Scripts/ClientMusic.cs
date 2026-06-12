using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Context-aware background music: four moods — in-game menu, planet surface, space flight and
    /// space combat — cross-faded over ~2.5 s on the music bus. Each context prefers a bundled
    /// loop (<c>Resources/audio/music_*</c>, AI-generated) and falls back to a code-synthesized
    /// ambient pad in the same mood, so the game stays musical with no assets. Combat is inferred
    /// client-side: the ship's hull+shield dropped within the last few seconds while in space.
    /// </summary>
    public sealed class ClientMusic : MonoBehaviour
    {
        public ClientSettings Settings;
        public GameBootstrap Game;

        private enum Context { Menu, Planet, Space, Combat }

        private AudioSource _active, _fading;
        private Context _context = (Context)(-1);
        private readonly System.Collections.Generic.Dictionary<Context, AudioClip> _clips = new();

        // Combat detection: hull+shield drops while in space arm a combat window.
        private float _lastIntegrity = -1f;
        private float _combatUntil;

        private void Awake()
        {
            _active = NewSource();
            _fading = NewSource();
        }

        private AudioSource NewSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f; // non-positional background track
            src.volume = 0f;
            return src;
        }

        private void Update()
        {
            var want = CurrentContext();
            if (want != _context)
            {
                SwitchTo(want);
            }

            // Music bus = the music volume (the master bus is applied globally by the AudioListener).
            float target = Mathf.Clamp01(Settings?.MusicVolume ?? 0.6f);
            _active.volume = Mathf.MoveTowards(_active.volume, target, Time.deltaTime * 0.4f);
            _fading.volume = Mathf.MoveTowards(_fading.volume, 0f, Time.deltaTime * 0.4f);
            if (_fading.volume <= 0f && _fading.isPlaying)
            {
                _fading.Stop();
            }
        }

        private Context CurrentContext()
        {
            if (Game == null || Game.MenuOpen)
            {
                return Context.Menu;
            }

            bool inSpace = Game.SpaceViewActive || Game.InSpace;

            // Combat: integrity (hull+shield) fell while in space → tense track for a while.
            var combat = Game.ShipCombat;
            if (combat != null)
            {
                float integrity = combat.Hull + combat.Shield;
                if (_lastIntegrity >= 0f && integrity < _lastIntegrity - 0.01f && inSpace)
                {
                    _combatUntil = Time.time + 14f;
                }

                _lastIntegrity = integrity;
            }

            if (inSpace && Time.time < _combatUntil)
            {
                return Context.Combat;
            }

            return inSpace ? Context.Space : Context.Planet;
        }

        private void SwitchTo(Context context)
        {
            _context = context;
            (_active, _fading) = (_fading, _active);
            _active.clip = ClipFor(context);
            _active.volume = 0f; // fades up in Update while the old track fades down
            _active.Play();
        }

        private AudioClip ClipFor(Context context)
        {
            if (_clips.TryGetValue(context, out var cached) && cached != null)
            {
                return cached;
            }

            string key = context switch
            {
                Context.Menu => "music_menu",
                Context.Planet => "music_planet",
                Context.Space => "music_space",
                _ => "music_combat",
            };

            var clip = Resources.Load<AudioClip>("audio/" + key) ?? BuildLoop(context);
            _clips[context] = clip;
            return clip;
        }

        /// <summary>
        /// Synthesizes a seamless ambient loop in the context's mood: consonant chords of sine pads
        /// plus a low drone, each chord swelling in and out (a half-sine envelope that reaches zero at
        /// every boundary, so chord changes and the loop seam are click-free). Combat adds a slow
        /// amplitude pulse for tension.
        /// </summary>
        private static AudioClip BuildLoop(Context context)
        {
            const int rate = 44100;
            float chordDur;
            bool pulse = false;
            float[][] chords;
            switch (context)
            {
                case Context.Planet: // brighter, major — wonder and discovery
                    chordDur = 4f;
                    chords = new[]
                    {
                        new[] { 261.63f, 329.63f, 392.00f }, // C
                        new[] { 349.23f, 440.00f, 523.25f }, // F
                        new[] { 392.00f, 493.88f, 587.33f }, // G
                        new[] { 220.00f, 261.63f, 329.63f }, // Am
                    };
                    break;
                case Context.Space: // vast, low, sparse — slow two-note dyads
                    chordDur = 6f;
                    chords = new[]
                    {
                        new[] { 110.00f, 164.81f },          // A low dyad
                        new[] { 98.00f, 146.83f },           // G low dyad
                        new[] { 87.31f, 130.81f },           // F low dyad
                        new[] { 110.00f, 164.81f },
                    };
                    break;
                case Context.Combat: // minor, pulsing — tension
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

            var clip = AudioClip.Create("music_" + context, total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
