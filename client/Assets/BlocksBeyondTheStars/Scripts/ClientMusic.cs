using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Procedural background music (M26): a calm, code-synthesized ambient pad loop (no bundled
    /// audio assets) on the music bus. It plays during gameplay and its volume follows the
    /// master × music settings. A richer, context-cross-faded score (menu / planet / space /
    /// combat) and/or recorded music replaces this later.
    /// </summary>
    public sealed class ClientMusic : MonoBehaviour
    {
        public ClientSettings Settings;

        private AudioSource _src;

        private void Awake()
        {
            _src = gameObject.AddComponent<AudioSource>();
            _src.playOnAwake = true;
            _src.loop = true;
            _src.spatialBlend = 0f; // non-positional background track
            _src.clip = BuildAmbientLoop();
            _src.volume = 0f;
            _src.Play();
        }

        private void Update()
        {
            // Music bus = the music volume (the master bus is applied globally by the AudioListener).
            float target = Mathf.Clamp01(Settings?.MusicVolume ?? 0.6f);
            _src.volume = Mathf.MoveTowards(_src.volume, target, Time.deltaTime * 0.5f);
        }

        /// <summary>
        /// Synthesizes a ~16 s seamless ambient loop: four consonant chords (Am–C–G–Em) of sine
        /// pads plus a low drone, each chord swelling in and out (a raised half-sine envelope that
        /// reaches zero at every boundary), so chord changes and the loop seam are click-free.
        /// </summary>
        private static AudioClip BuildAmbientLoop()
        {
            const int rate = 44100;
            const float chordDur = 4f;
            float[][] chords =
            {
                new[] { 220.00f, 261.63f, 329.63f }, // Am
                new[] { 261.63f, 329.63f, 392.00f }, // C
                new[] { 196.00f, 246.94f, 293.66f }, // G
                new[] { 164.81f, 196.00f, 246.94f }, // Em
            };

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
                data[i] = s * env;
            }

            var clip = AudioClip.Create("ambient_loop", total, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
