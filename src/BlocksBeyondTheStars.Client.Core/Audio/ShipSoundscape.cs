// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3247: the ship as a place, by ear — the rules behind the client's ship soundscape (<c>ShipSoundscapeView</c>),
    /// Unity-free so the headless tests pin them. Pure presentation: the server stays the truth of the ship, these rules
    /// only read what the client already knows (the parked hull's box and engine cells, the station list, the fitted
    /// modules, the hull fraction).
    /// <list type="bullet">
    /// <item><b>Hum from the design</b> (<see cref="HumFor"/>, <see cref="SynthesizeHum"/>): a bigger hull hums deeper, a
    /// small one brighter; the reactor adds a slow pulse, the life support a breathing air cycle, a jump generator a faint
    /// coil shimmer. One seamless procedural loop, so it costs no download.</item>
    /// <item><b>Module emitters</b> (<see cref="EmittersFor"/>, <see cref="PickEmitters"/>, <see cref="EmitterVoices"/>): the
    /// reactor at the engines, the life support at the cabin ceiling, the medbay tank and the cargo hold at their stations —
    /// only the nearest <see cref="MaxEmitters"/> within <see cref="EmitterReach"/> play, quietly, faded by distance.</item>
    /// <item><b>Cooling ticks</b> after a landing (<see cref="CoolingSchedule"/>): sparse metal pings that slow down over
    /// <see cref="CoolingSeconds"/> and then stop.</item>
    /// <item><b>Outside</b> (<see cref="ExteriorIdleVolume"/>): a quiet idle hum within <see cref="ExteriorReach"/> of the
    /// own parked hull, so a child finds the ship by ear at night.</item>
    /// <item><b>States</b> (<see cref="NextHullCreakDelay"/>, <see cref="IsDead"/>): the structure creaks now and then below
    /// <see cref="CreakBelowHull"/> hull; a downed wreck has no power and so no hum.</item>
    /// </list>
    /// </summary>
    public static class ShipSoundscape
    {
        /// <summary>The reactor's positional loop (a bundled recording, seamless).</summary>
        public const string ReactorLoop = "reactor_hum";

        /// <summary>The life support's slow air cycle (a bundled recording, seamless).</summary>
        public const string LifeSupportLoop = "life_support_cycle";

        /// <summary>The medbay's heal tank bubbling (a recording, or the procedural stand-in).</summary>
        public const string MedbayLoop = "clone_tank_bubble";

        /// <summary>The parked ship's idle hum heard from outside (a bundled recording, seamless).</summary>
        public const string ExteriorIdleLoop = "ship_exterior_idle";

        /// <summary>The metal pings of a hull cooling down after a landing (variants).</summary>
        public static readonly string[] CoolingTickClips = { "ship_cooling_tick", "ship_cooling_tick_2" };

        /// <summary>The cargo hold settling (variants) — the cargo module's emitter plays these instead of a loop.</summary>
        public static readonly string[] CargoCreakClips = { "cargo_creak", "cargo_creak_2" };

        /// <summary>The structure groaning under heavy damage (variants).</summary>
        public static readonly string[] HullCreakClips = { "hull_creak", "hull_creak_2" };

        // --- the hum -----------------------------------------------------------------------------------------------

        /// <summary>The hum's share of the ambience level — the same as the router's old two-sine hum.</summary>
        public const float HumBedShare = 0.45f;

        /// <summary>Length of the hum loop. Every partial and envelope is a whole number of cycles in it.</summary>
        public const float HumLoopSeconds = 4f;

        /// <summary>Sample rate of the hum loop — plenty for a low drone, half the memory of 44.1 kHz.</summary>
        public const int HumSampleRate = 22050;

        /// <summary>Frequencies are rounded to this step (1 / <see cref="HumLoopSeconds"/>) so the loop closes seamlessly.</summary>
        public const float HumHzStep = 1f / HumLoopSeconds;

        /// <summary>The loudness (RMS) every synthesized hum is normalised to — about the old hum's.</summary>
        public const float HumRms = 0.19f;

        /// <summary>The bounding-box volume (blocks) of the smallest stock hull (the scout) and of a big self-built one.</summary>
        public const float SmallShipVolume = 100f;

        public const float LargeShipVolume = 2000f;

        /// <summary>The fundamental of the smallest and of the biggest hull: bigger is deeper.</summary>
        public const float SmallShipHz = 96f;

        public const float LargeShipHz = 52f;

        /// <summary>The reactor pulses once per two seconds; the life support breathes once per loop.</summary>
        public const float ReactorPulseHz = 0.5f;

        public const float AirCycleHz = 0.25f;

        // --- module emitters ---------------------------------------------------------------------------------------

        /// <summary>The budget: never more module emitters at once than this.</summary>
        public const int MaxEmitters = 3;

        /// <summary>An emitter is heard only within this distance (metres) …</summary>
        public const float EmitterReach = 8f;

        /// <summary>… at its full level within this one, faded smoothly between the two.</summary>
        public const float EmitterFullWithin = 2f;

        /// <summary>Emitter levels (share of the effects level) — all well below the hum.</summary>
        public const float ReactorGain = 0.24f;

        public const float LifeSupportGain = 0.16f;

        public const float OxygenGeneratorGain = 0.2f;

        public const float MedbayGain = 0.2f;

        public const float CargoGain = 0.35f;

        /// <summary>How fast a module voice fades in or out (level per second).</summary>
        public const float VoiceFadePerSecond = 1.5f;

        /// <summary>An emitter that already plays ranks this much nearer (metres) — no back-and-forth at the budget's edge.</summary>
        public const float EmitterStickiness = 1f;

        // --- cooling, outside, states ------------------------------------------------------------------------------

        /// <summary>The cooling ticks of a landing run this long, then stop.</summary>
        public const float CoolingSeconds = 30f;

        /// <summary>Two cooling ticks are never closer together than this.</summary>
        public const float CoolingMinGap = 0.6f;

        /// <summary>The cooling ticks' level (share of the effects level) at their loudest, right after the thump.</summary>
        public const float CoolingTickLevel = 0.5f;

        /// <summary>The idle hum outside is heard within this distance of the hull (metres) …</summary>
        public const float ExteriorReach = 15f;

        /// <summary>… at its full (quiet) level within this one.</summary>
        public const float ExteriorFullWithin = 3f;

        public const float ExteriorLevel = 0.3f;

        /// <summary>Below this hull fraction the structure creaks now and then.</summary>
        public const float CreakBelowHull = 0.3f;

        /// <summary>Two hull creaks are never closer together than this (seconds).</summary>
        public const float CreakMinGap = 8f;

        /// <summary>A hull creak's level (share of the effects level).</summary>
        public const float HullCreakLevel = 0.4f;

        /// <summary>The design parameters of a ship's hum (<see cref="HumFor"/>).</summary>
        public readonly struct HumParams
        {
            public HumParams(float baseHz, float level, float brightness, bool reactor, float air, bool jumpCoil)
            {
                BaseHz = baseHz;
                Level = level;
                Brightness = brightness;
                Reactor = reactor;
                Air = air;
                JumpCoil = jumpCoil;
            }

            /// <summary>The fundamental (Hz), a multiple of <see cref="HumHzStep"/>.</summary>
            public float BaseHz { get; }

            /// <summary>Multiplier on <see cref="HumBedShare"/> — a big hull hums a touch fuller.</summary>
            public float Level { get; }

            /// <summary>Weight of the third partial: a small hull sounds brighter, a big one darker.</summary>
            public float Brightness { get; }

            /// <summary>The reactor's slow pulse layer.</summary>
            public bool Reactor { get; }

            /// <summary>Strength of the life support's breathing air layer (0 = none).</summary>
            public float Air { get; }

            /// <summary>A jump generator's faint coil shimmer.</summary>
            public bool JumpCoil { get; }

            /// <summary>True when both would synthesize the same loop (the view rebuilds its clip only on a change).</summary>
            public bool SameAs(HumParams other)
                => BaseHz == other.BaseHz && Level == other.Level && Brightness == other.Brightness
                   && Reactor == other.Reactor && Air == other.Air && JumpCoil == other.JumpCoil;
        }

        /// <summary>What a module emitter plays.</summary>
        public enum EmitterKind
        {
            /// <summary>A seamless positional loop (<see cref="Emitter.Clip"/>).</summary>
            Loop,

            /// <summary>Occasional creaks from <see cref="CargoCreakClips"/> at the emitter (<see cref="NextCargoCreakDelay"/>).</summary>
            Creaks,
        }

        /// <summary>One sound source of the ship's modules.</summary>
        public readonly struct Emitter
        {
            public Emitter(string clip, EmitterKind kind, float x, float y, float z, float gain)
            {
                Clip = clip;
                Kind = kind;
                X = x;
                Y = y;
                Z = z;
                Gain = gain;
            }

            /// <summary>The loop's clip id (for <see cref="EmitterKind.Creaks"/> the first creak variant).</summary>
            public string Clip { get; }

            public EmitterKind Kind { get; }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }

            /// <summary>The emitter's level at full volume (share of the effects level).</summary>
            public float Gain { get; }

            /// <summary>The same emitter at another position (the view maps the world spot into the wrapped scene).</summary>
            public Emitter At(float x, float y, float z) => new Emitter(Clip, Kind, x, y, z, Gain);
        }

        /// <summary>An emitter picked for this frame: its index in the emitter list, its target volume and distance (and the
        /// distance it was ranked by — a held emitter ranks a little nearer, see <see cref="PickEmitters"/>).</summary>
        public readonly struct EmitterPick
        {
            public EmitterPick(int index, float volume, float distance, float? rank = null)
            {
                Index = index;
                Volume = volume;
                Distance = distance;
                Rank = rank ?? distance;
            }

            public int Index { get; }

            public float Volume { get; }

            public float Distance { get; }

            public float Rank { get; }
        }

        /// <summary>One interactive station the server lists (type + world position: x/z the cell centre, y its floor).</summary>
        public readonly struct StationSpot
        {
            public StationSpot(string type, float x, float y, float z)
            {
                Type = type;
                X = x;
                Y = y;
                Z = z;
            }

            public string Type { get; }

            public float X { get; }

            public float Y { get; }

            public float Z { get; }
        }

        /// <summary>A parked hull's bounding box in world space (its origin cell and its size in blocks).</summary>
        public readonly struct ShipBox
        {
            public ShipBox(float minX, float minY, float minZ, int width, int height, int length)
            {
                MinX = minX;
                MinY = minY;
                MinZ = minZ;
                Width = Math.Max(1, width);
                Height = Math.Max(1, height);
                Length = Math.Max(1, length);
            }

            public float MinX { get; }

            public float MinY { get; }

            public float MinZ { get; }

            public int Width { get; }

            public int Height { get; }

            public int Length { get; }

            public float CentreX => MinX + Width * 0.5f;

            public float CentreZ => MinZ + Length * 0.5f;
        }

        /// <summary>The hum of a hull of this size (blocks) with these modules fitted. <paramref name="modules"/> null = not
        /// known yet: the mandatory reactor and life support are assumed, nothing optional.</summary>
        public static HumParams HumFor(int width, int height, int length, IReadOnlyList<string>? modules)
        {
            double volume = Math.Max(1, width) * (double)Math.Max(1, height) * Math.Max(1, length);
            float size = Clamp01((float)((Math.Log(volume) - Math.Log(SmallShipVolume))
                / (Math.Log(LargeShipVolume) - Math.Log(SmallShipVolume))));
            float hz = (float)(Math.Round(Lerp(SmallShipHz, LargeShipHz, size) / HumHzStep) * HumHzStep);
            float air = !Fitted(modules, "life_support", assumed: true) ? 0f
                : Fitted(modules, "oxygen_generator", assumed: false) ? 1f : 0.7f;
            return new HumParams(hz, Lerp(0.95f, 1.08f, size), Lerp(0.16f, 0.06f, size),
                Fitted(modules, "reactor", assumed: true), air, Fitted(modules, "jump_generator", assumed: false));
        }

        /// <summary>Synthesizes the hum loop (mono, <paramref name="sampleRate"/>, <see cref="HumLoopSeconds"/> long, RMS
        /// <see cref="HumRms"/>): the fundamental with its octave, a size-weighted third partial and a slowly beating octave,
        /// the reactor's pulse riding on the tone, the life support's air breathing in filtered noise, a faint hiss. Every
        /// partial and envelope closes on a whole cycle and the noise is filtered around the loop, so it repeats without a
        /// seam. Deterministic: the same parameters give the same samples.</summary>
        public static float[] SynthesizeHum(HumParams p, int sampleRate = HumSampleRate)
        {
            int n = (int)Math.Round(sampleRate * (double)HumLoopSeconds);
            var noise = LoopNoise(n, 0.15f, 3247);
            var data = new float[n];
            double w = 2.0 * Math.PI / sampleRate;
            double f = p.BaseHz;
            for (int i = 0; i < n; i++)
            {
                double tone = 0.5 * Math.Sin(w * f * i) + 0.28 * Math.Sin(w * 2 * f * i)
                    + p.Brightness * Math.Sin(w * 3 * f * i) + 0.07 * Math.Sin(w * (2 * f + HumHzStep) * i);
                if (p.JumpCoil)
                {
                    tone += 0.025 * Math.Sin(w * 8 * f * i) * (0.5 + 0.5 * Math.Sin(w * ReactorPulseHz * i));
                }

                double env = 1.0;
                if (p.Reactor)
                {
                    double c = 0.5 + 0.5 * Math.Cos(w * ReactorPulseHz * i); // a short swell once per pulse (c^6)
                    double c2 = c * c;
                    env = 0.88 + 0.24 * c2 * c2 * c2;
                }

                double s = Math.Sin(w * AirCycleHz * 0.5 * i); // sin² breathes once per loop
                data[i] = (float)(tone * env + noise[i] * (0.02 + p.Air * 0.1 * s * s));
            }

            Normalise(data, HumRms, 0.95f);
            return data;
        }

        /// <summary>The ship's module emitters: the reactor at the engines (pulled a little into the hull; the rear quarter
        /// without engines), the life support at the cabin ceiling, every medbay tank and cargo hold at its station. Nothing
        /// in the data marks where the reactor or the life support sit, so they go where a reader of the hull would look.
        /// <paramref name="engines"/> are the engine cells' world x/z.</summary>
        public static List<Emitter> EmittersFor(ShipBox box, IReadOnlyList<StationSpot> stations, IReadOnlyList<string>? modules,
            IReadOnlyList<(float X, float Z)> engines)
        {
            var list = new List<Emitter>();
            if (Fitted(modules, "reactor", assumed: true))
            {
                var (rx, rz) = ReactorSpot(box, engines);
                list.Add(new Emitter(ReactorLoop, EmitterKind.Loop, rx, box.MinY + 1.5f, rz, ReactorGain));
            }

            if (Fitted(modules, "life_support", assumed: true))
            {
                float gain = Fitted(modules, "oxygen_generator", assumed: false) ? OxygenGeneratorGain : LifeSupportGain;
                float vent = box.MinY + Math.Max(2.5f, box.Height - 1.2f);
                list.Add(new Emitter(LifeSupportLoop, EmitterKind.Loop, box.CentreX, vent, box.CentreZ, gain));
            }

            foreach (var s in stations)
            {
                if (s.Type == "medbay")
                {
                    list.Add(new Emitter(MedbayLoop, EmitterKind.Loop, s.X, s.Y + 1.2f, s.Z, MedbayGain));
                }
                else if (s.Type == "cargo")
                {
                    list.Add(new Emitter(CargoCreakClips[0], EmitterKind.Creaks, s.X, s.Y + 0.8f, s.Z, CargoGain));
                }
            }

            return list;
        }

        /// <summary>True when two emitter lists would sound the same (same clips at the same spots) — a re-sent station list
        /// keeps the voices that already play.</summary>
        public static bool SameLayout(IReadOnlyList<Emitter> a, IReadOnlyList<Emitter> b)
        {
            if (a.Count != b.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Clip != b[i].Clip || a[i].Kind != b[i].Kind || Math.Abs(a[i].X - b[i].X) > 0.01f
                    || Math.Abs(a[i].Y - b[i].Y) > 0.01f || Math.Abs(a[i].Z - b[i].Z) > 0.01f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The emitters heard from (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>): those within
        /// <see cref="EmitterReach"/>, nearest first, at most <see cref="MaxEmitters"/>, each with its distance-faded
        /// volume. An emitter a voice already plays (<paramref name="held"/>) ranks <see cref="EmitterStickiness"/> nearer,
        /// so two at about the same distance do not take turns as the player walks. Fills <paramref name="result"/> (cleared
        /// first) so a per-frame caller allocates nothing.</summary>
        public static void PickEmitters(IReadOnlyList<Emitter> emitters, float x, float y, float z, List<EmitterPick> result,
            EmitterVoices? held = null)
        {
            result.Clear();
            for (int i = 0; i < emitters.Count; i++)
            {
                var e = emitters[i];
                float dx = e.X - x, dy = e.Y - y, dz = e.Z - z;
                float d = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d < EmitterReach)
                {
                    float rank = held != null && held.Holds(i) ? d - EmitterStickiness : d;
                    result.Add(new EmitterPick(i, EmitterVolume(e.Gain, d), d, rank));
                }
            }

            result.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Index.CompareTo(b.Index));
            if (result.Count > MaxEmitters)
            {
                result.RemoveRange(MaxEmitters, result.Count - MaxEmitters);
            }
        }

        /// <summary>An emitter's volume at a distance: its gain within <see cref="EmitterFullWithin"/>, smoothly down to
        /// silence at <see cref="EmitterReach"/>.</summary>
        public static float EmitterVolume(float gain, float distance) => gain * Fade(distance, EmitterFullWithin, EmitterReach);

        /// <summary>The outside idle hum's volume at a distance from the hull (metres): <see cref="ExteriorLevel"/> close by,
        /// silent from <see cref="ExteriorReach"/>.</summary>
        public static float ExteriorIdleVolume(float distanceToHull)
            => ExteriorLevel * Fade(distanceToHull, ExteriorFullWithin, ExteriorReach);

        /// <summary>The cooling ticks of one landing, in time order from the touchdown: the first within a second, then gaps
        /// that grow (with random spread, never below <see cref="CoolingMinGap"/>) and pings that soften, until
        /// <see cref="CoolingSeconds"/> — a dozen or so, sparse. Deterministic per <paramref name="seed"/>.</summary>
        public static IReadOnlyList<CoolingTick> CoolingSchedule(int seed)
        {
            var rng = new Random(seed);
            var ticks = new List<CoolingTick>();
            float t = 0.4f + (float)rng.NextDouble() * 0.6f;
            while (t <= CoolingSeconds)
            {
                float age = t / CoolingSeconds;
                ticks.Add(new CoolingTick(t, Lerp(1f, 0.45f, age) * (0.8f + 0.2f * (float)rng.NextDouble()),
                    0.92f + 0.16f * (float)rng.NextDouble(), rng.Next(CoolingTickClips.Length)));
                float gap = Lerp(0.8f, 5f, age) * (0.6f + 0.9f * (float)rng.NextDouble());
                t += Math.Max(CoolingMinGap, gap);
            }

            return ticks;
        }

        /// <summary>One cooling ping: when (seconds after the touchdown), how loud (0..1, times
        /// <see cref="CoolingTickLevel"/>), its pitch and which of <see cref="CoolingTickClips"/>.</summary>
        public readonly struct CoolingTick
        {
            public CoolingTick(float at, float volume, float pitch, int variant)
            {
                At = at;
                Volume = volume;
                Pitch = pitch;
                Variant = variant;
            }

            public float At { get; }

            public float Volume { get; }

            public float Pitch { get; }

            public int Variant { get; }
        }

        /// <summary>The wait (seconds) before the next structure creak at this hull fraction: none at all
        /// (<see cref="float.PositiveInfinity"/>) from <see cref="CreakBelowHull"/> up; below it rare — 16–28 s just under
        /// the line, 8–13 s on a hull near zero, never less than <see cref="CreakMinGap"/>.</summary>
        public static float NextHullCreakDelay(float hullFraction, Random rng)
        {
            if (!(hullFraction < CreakBelowHull))
            {
                return float.PositiveInfinity;
            }

            float u = Clamp01((CreakBelowHull - hullFraction) / CreakBelowHull);
            float min = Lerp(16f, CreakMinGap, u);
            float span = Lerp(12f, 5f, u);
            return min + span * (float)rng.NextDouble();
        }

        /// <summary>The wait (seconds) before the cargo hold's next creak while it is in earshot: rare, 25–55 s — a small cabin
        /// keeps its hold in earshot the whole time, so it must not turn into a rhythm.</summary>
        public static float NextCargoCreakDelay(Random rng) => 25f + 30f * (float)rng.NextDouble();

        /// <summary>A downed wreck (hull at zero) has no power: no hum, no module loops — only its creaks.</summary>
        public static bool IsDead(float hullFraction) => hullFraction <= 0.001f;

        /// <summary>True when the module is fitted; with no module list known yet, <paramref name="assumed"/> answers.</summary>
        public static bool Fitted(IReadOnlyList<string>? modules, string key, bool assumed)
        {
            if (modules == null)
            {
                return assumed;
            }

            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i] == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The voices that play the picked emitters: at most <see cref="MaxEmitters"/>, each keeps its emitter while
        /// it stays picked and takes a new one only once it has faded out — never a jump cut from one loop to another.</summary>
        public sealed class EmitterVoices
        {
            private readonly int[] _emitter;
            private readonly float[] _level;
            private readonly float[] _target;

            public EmitterVoices(int count = MaxEmitters)
            {
                _emitter = new int[count];
                _level = new float[count];
                _target = new float[count];
                Reset();
            }

            /// <summary>The number of voices.</summary>
            public int Count => _emitter.Length;

            /// <summary>The emitter index a voice plays, or -1 when it is free.</summary>
            public int EmitterOf(int voice) => _emitter[voice];

            /// <summary>A voice's current (faded) level, 0..1 times its emitter's gain.</summary>
            public float LevelOf(int voice) => _level[voice];

            /// <summary>True while a voice plays (or still fades out) this emitter.</summary>
            public bool Holds(int emitter) => emitter >= 0 && VoiceOf(emitter) >= 0;

            /// <summary>Frees every voice at once (the emitter list was rebuilt; the indices mean nothing any more).</summary>
            public void Reset()
            {
                for (int v = 0; v < _emitter.Length; v++)
                {
                    _emitter[v] = -1;
                    _level[v] = 0f;
                    _target[v] = 0f;
                }
            }

            /// <summary>One frame: held emitters follow their picked volume, a dropped one fades out, a newly picked one takes
            /// a free voice (listed in <paramref name="started"/>, cleared first, for the caller to give it its clip and
            /// spot) and fades in. A pick that finds no free voice waits until one has faded out.</summary>
            public void Tick(IReadOnlyList<EmitterPick> picks, float dt, List<int> started)
            {
                started.Clear();
                for (int v = 0; v < _emitter.Length; v++)
                {
                    _target[v] = 0f;
                }

                foreach (var pick in picks)
                {
                    int v = VoiceOf(pick.Index);
                    if (v < 0)
                    {
                        v = VoiceOf(-1);
                        if (v < 0)
                        {
                            continue;
                        }

                        _emitter[v] = pick.Index;
                        _level[v] = 0f;
                        started.Add(v);
                    }

                    _target[v] = pick.Volume;
                }

                float step = Math.Max(0f, dt) * VoiceFadePerSecond;
                for (int v = 0; v < _emitter.Length; v++)
                {
                    _level[v] = MoveTowards(_level[v], _target[v], step);
                    if (_target[v] <= 0f && _level[v] <= 0f)
                    {
                        _emitter[v] = -1;
                    }
                }
            }

            private int VoiceOf(int emitter)
            {
                for (int v = 0; v < _emitter.Length; v++)
                {
                    if (_emitter[v] == emitter)
                    {
                        return v;
                    }
                }

                return -1;
            }
        }

        // --- helpers ---------------------------------------------------------------------------------------------------

        private static (float X, float Z) ReactorSpot(ShipBox box, IReadOnlyList<(float X, float Z)> engines)
        {
            if (engines == null || engines.Count == 0)
            {
                return (box.CentreX, box.MinZ + box.Length * 0.25f);
            }

            float ex = 0f, ez = 0f;
            foreach (var e in engines)
            {
                ex += e.X;
                ez += e.Z;
            }

            ex /= engines.Count;
            ez /= engines.Count;
            float dx = box.CentreX - ex, dz = box.CentreZ - ez;
            float len = (float)Math.Sqrt(dx * dx + dz * dz);
            if (len > 0.001f)
            {
                float pull = Math.Min(2f, len) / len;
                ex += dx * pull;
                ez += dz * pull;
            }

            return (Inside(ex, box.MinX, box.Width), Inside(ez, box.MinZ, box.Length));
        }

        /// <summary>A coordinate kept a block inside the hull's span (or at its middle when the span is that narrow).</summary>
        private static float Inside(float v, float min, int size)
            => size <= 2 ? min + size * 0.5f : Math.Max(min + 1f, Math.Min(min + size - 1f, v));

        /// <summary>White noise run through a one-pole low-pass around the loop twice, so its end flows into its start;
        /// normalised to unit RMS.</summary>
        private static float[] LoopNoise(int n, float smoothing, int seed)
        {
            var rng = new Random(seed);
            var white = new float[n];
            for (int i = 0; i < n; i++)
            {
                white[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            }

            var result = new float[n];
            float y = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    y += (white[i] - y) * smoothing;
                    result[i] = y;
                }
            }

            Normalise(result, 1f, float.MaxValue);
            return result;
        }

        private static void Normalise(float[] data, float rms, float peak)
        {
            double sum = 0.0;
            foreach (float s in data)
            {
                sum += s * (double)s;
            }

            double current = data.Length > 0 ? Math.Sqrt(sum / data.Length) : 0.0;
            if (current <= 1e-9)
            {
                return;
            }

            float gain = (float)(rms / current);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = Math.Max(-peak, Math.Min(peak, data[i] * gain));
            }
        }

        /// <summary>1 up to <paramref name="full"/>, a smooth step down to 0 at <paramref name="reach"/>.</summary>
        private static float Fade(float distance, float full, float reach)
        {
            if (distance <= full)
            {
                return 1f;
            }

            if (distance >= reach)
            {
                return 0f;
            }

            float k = (reach - distance) / (reach - full);
            return k * k * (3f - 2f * k);
        }

        private static float MoveTowards(float current, float target, float step)
            => current < target ? Math.Min(target, current + step) : Math.Max(target, current - step);

        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
