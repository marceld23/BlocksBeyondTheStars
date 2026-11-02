// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Numerics;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>#3095: the little show an item puts on once it is in the hand.</summary>
    public enum HeldFlourishKind : byte
    {
        /// <summary>Nothing — every kind without a flourish.</summary>
        None,

        /// <summary>A drill: lifted a little and turned toward the eye while the bit spins up and winds down again.</summary>
        DrillSpinUp,

        /// <summary>A gun: tipped onto its side and racked once — a cosmetic reload (the game has no ammunition).</summary>
        GunReload,

        /// <summary>A blade: the wrist flicks it up and the blade spins once round its own length.</summary>
        BladeTwirl,
    }

    /// <summary>
    /// One frame of a flourish (<see cref="HeldFlourishes.PoseAt"/>). <see cref="Offset"/> (metres) and <see cref="Euler"/>
    /// (degrees, Unity's pitch/yaw/roll, negative pitch = tip up) are added to the first-person holder's rest pose;
    /// <see cref="Lift"/> (0..1) raises an avatar's arm to show the item; <see cref="Twirl"/> turns the item's parts round its
    /// long axis (degrees); <see cref="BitDriven"/> keeps a drill's bit spinning and <see cref="Buzz"/> (0..1) shakes the
    /// hand with it. All of them are 0 at rest.
    /// </summary>
    public readonly struct HeldFlourishPose
    {
        public HeldFlourishPose(Vector3 offset, Vector3 euler, float lift, float twirl, bool bitDriven, float buzz)
        {
            Offset = offset;
            Euler = euler;
            Lift = lift;
            Twirl = twirl;
            BitDriven = bitDriven;
            Buzz = buzz;
        }

        public Vector3 Offset { get; }

        public Vector3 Euler { get; }

        public float Lift { get; }

        public float Twirl { get; }

        public bool BitDriven { get; }

        public float Buzz { get; }

        /// <summary>True when nothing moves.</summary>
        public bool IsRest => Offset == Vector3.Zero && Euler == Vector3.Zero && Lift == 0f && Twirl == 0f && !BitDriven && Buzz == 0f;
    }

    /// <summary>
    /// #3095 ("when you take a drill in your hand … a short test drill; ranged weapons reload once, knife-like weapons are
    /// twirled in the hand at random or on select"): the equip flourishes of the held items, Unity-free so the headless suite
    /// pins them. <c>Viewmodel</c> (first person) and <c>PlayerAvatar</c> (third person, other players, the robbers) play
    /// them after a draw (<see cref="HeldSwap"/>), and a blade now and then while it is just held (<see cref="IdleRepeats"/>).
    /// <list type="bullet">
    /// <item>Purely cosmetic and short (at most <see cref="MaxSeconds"/>): using the item cuts a flourish off at once — the
    /// server never hears of it, and nothing waits for it.</item>
    /// <item>Every pose starts and ends at rest, so it blends over whatever plays underneath without a snap.</item>
    /// </list>
    /// </summary>
    public static class HeldFlourishes
    {
        /// <summary>No flourish is longer than this.</summary>
        public const float MaxSeconds = 0.8f;

        /// <summary>The drill's test spin.</summary>
        public const float DrillSeconds = 0.7f;

        /// <summary>The gun's cosmetic reload.</summary>
        public const float GunSeconds = 0.75f;

        /// <summary>The blade's twirl.</summary>
        public const float BladeSeconds = 0.6f;

        /// <summary>A held blade is twirled again after this many seconds of standing still with it, at the soonest …</summary>
        public const float IdleMinSeconds = 9f;

        /// <summary>… and at the latest.</summary>
        public const float IdleMaxSeconds = 18f;

        /// <summary>The flourish cue's volume (2D in first person; a third-person figure plays it in the world).</summary>
        public const float CueVolume = 0.45f;

        /// <summary>The flourish of an item held as <paramref name="heldKind"/> (<c>HeldItem.Kind</c> by name).</summary>
        public static HeldFlourishKind ForKind(string? heldKind) => heldKind switch
        {
            "Drill" => HeldFlourishKind.DrillSpinUp,
            "Gun" => HeldFlourishKind.GunReload,
            "Blade" => HeldFlourishKind.BladeTwirl,
            _ => HeldFlourishKind.None,
        };

        /// <summary>How long a flourish lasts (0 for none).</summary>
        public static float Seconds(HeldFlourishKind kind) => kind switch
        {
            HeldFlourishKind.DrillSpinUp => DrillSeconds,
            HeldFlourishKind.GunReload => GunSeconds,
            HeldFlourishKind.BladeTwirl => BladeSeconds,
            _ => 0f,
        };

        /// <summary>The sound a flourish plays as it starts (a clip in <c>Resources/audio</c>), null for none.</summary>
        public static string? Cue(HeldFlourishKind kind) => kind switch
        {
            HeldFlourishKind.DrillSpinUp => "drill_spinup",
            HeldFlourishKind.GunReload => "gun_reload",
            HeldFlourishKind.BladeTwirl => "blade_twirl",
            _ => null,
        };

        /// <summary>True for the flourish that also plays now and then while the item is just held (the blade's twirl).</summary>
        public static bool IdleRepeats(HeldFlourishKind kind) => kind == HeldFlourishKind.BladeTwirl;

        /// <summary>The pause before the next idle twirl, from a uniform random <paramref name="unit"/> in 0..1.</summary>
        public static float IdleGap(double unit)
            => IdleMinSeconds + ((IdleMaxSeconds - IdleMinSeconds) * (float)Math.Max(0.0, Math.Min(1.0, unit)));

        /// <summary>The pose of flourish <paramref name="kind"/> at progress <paramref name="t"/> (0 = start, 1 = end); rest
        /// outside 0..1.</summary>
        public static HeldFlourishPose PoseAt(HeldFlourishKind kind, float t)
        {
            if (!(t > 0f && t < 1f))
            {
                return default;
            }

            return kind switch
            {
                HeldFlourishKind.DrillSpinUp => Drill(t),
                HeldFlourishKind.GunReload => Gun(t),
                HeldFlourishKind.BladeTwirl => Blade(t),
                _ => default,
            };
        }

        /// <summary>The drill comes up a little and turns toward the middle of the view, the bit runs from 12 % to 72 % of the
        /// flourish (spinning up, then coasting down on its own) with a light buzz in the hand, and the drill settles back.</summary>
        private static HeldFlourishPose Drill(float t)
        {
            float env = Envelope(t, 0.2f, 0.7f);
            bool driven = t >= 0.12f && t < 0.72f;
            float buzz = Envelope(t, 0.18f, 0.62f);
            return new HeldFlourishPose(new Vector3(-0.03f, 0.03f, -0.03f) * env, new Vector3(-12f, -12f, -6f) * env, env, 0f,
                driven, buzz);
        }

        /// <summary>The gun tips onto its side and up, is racked once — pulled back and let snap forward with a little kick —
        /// and comes back level.</summary>
        private static HeldFlourishPose Gun(float t)
        {
            float env = Envelope(t, 0.25f, 0.72f);
            float back = Bump(t, 0.35f, 0.52f);  // the pull
            float kick = Bump(t, 0.5f, 0.64f);   // the snap forward
            var offset = (new Vector3(-0.04f, 0.03f, -0.02f) * env) + new Vector3(0f, 0f, -0.04f * back);
            var euler = (new Vector3(-10f, -6f, 38f) * env) + new Vector3(-8f * kick, 0f, 0f);
            return new HeldFlourishPose(offset, euler, env, 0f, false, 0f);
        }

        /// <summary>The wrist flicks the blade up and rolls it outward while the blade spins once round its own length,
        /// then everything settles back.</summary>
        private static HeldFlourishPose Blade(float t)
        {
            float env = Envelope(t, 0.2f, 0.75f);
            float turn = SmoothStep((t - 0.1f) / 0.75f) * 360f;
            float twirl = turn >= 360f ? 0f : turn; // one full turn is the rest pose again
            return new HeldFlourishPose(new Vector3(-0.03f, 0.035f, 0.02f) * env, new Vector3(-18f, -8f, -28f) * env, env, twirl,
                false, 0f);
        }

        /// <summary>0 → 1 smoothly over [0, <paramref name="inEnd"/>], 1 until <paramref name="outStart"/>, back to 0 by 1.</summary>
        internal static float Envelope(float t, float inEnd, float outStart)
        {
            if (t <= 0f || t >= 1f)
            {
                return 0f;
            }

            if (t < inEnd)
            {
                return SmoothStep(t / inEnd);
            }

            return t > outStart ? SmoothStep((1f - t) / (1f - outStart)) : 1f;
        }

        /// <summary>A half sine from <paramref name="from"/> to <paramref name="to"/>, 0 outside.</summary>
        internal static float Bump(float t, float from, float to)
            => t <= from || t >= to ? 0f : (float)Math.Sin(Math.PI * (t - from) / (to - from));

        internal static float SmoothStep(float x)
        {
            x = Math.Max(0f, Math.Min(1f, x));
            return x * x * (3f - (2f * x));
        }
    }

    /// <summary>
    /// #3095: the flourish an item in the hand plays — one per holder (<c>Viewmodel</c>, <c>PlayerAvatar</c>). Started after a
    /// draw, cut short by any use of the item, and for a blade (<see cref="HeldFlourishes.IdleRepeats"/>) started again now
    /// and then while the hand is idle.
    /// </summary>
    public sealed class HeldFlourish
    {
        private float _elapsed;
        private float _seconds;
        private float _idleLeft = -1f; // seconds to the next idle twirl; −1 = not armed

        /// <summary>The flourish playing, <see cref="HeldFlourishKind.None"/> when none.</summary>
        public HeldFlourishKind Current { get; private set; }

        /// <summary>True while a flourish plays.</summary>
        public bool Active => Current != HeldFlourishKind.None;

        /// <summary>How far it has played, 0..1.</summary>
        public float Progress => _seconds > 0f ? Math.Min(1f, _elapsed / _seconds) : 0f;

        /// <summary>The pose at the current progress (rest when none plays).</summary>
        public HeldFlourishPose Pose => HeldFlourishes.PoseAt(Current, Progress);

        /// <summary>Starts <paramref name="kind"/> from the beginning (none = <see cref="Cancel"/>). False for a kind without
        /// a flourish.</summary>
        public bool Begin(HeldFlourishKind kind)
        {
            float seconds = HeldFlourishes.Seconds(kind);
            if (seconds <= 0f)
            {
                Cancel();
                return false;
            }

            Current = kind;
            _seconds = seconds;
            _elapsed = 0f;
            _idleLeft = -1f;
            return true;
        }

        /// <summary>Cuts the flourish short — the item is used, put away, or the hand is busy. Back at rest at once.</summary>
        public void Cancel()
        {
            Current = HeldFlourishKind.None;
            _elapsed = 0f;
            _seconds = 0f;
        }

        /// <summary>Advances by <paramref name="dt"/> seconds; the flourish ends (back at rest) once it has played.</summary>
        public void Step(float dt)
        {
            if (!Active || dt <= 0f)
            {
                return;
            }

            _elapsed += dt;
            if (_elapsed >= _seconds)
            {
                Cancel();
            }
        }

        /// <summary>
        /// The idle repeat: true once the hand has been <paramref name="idle"/> with an item of <paramref name="held"/> for the
        /// random gap (<see cref="HeldFlourishes.IdleGap"/> of <paramref name="unit"/>, read when the wait starts) — then the
        /// caller begins the flourish. Any moment that is not idle (walking, using, a flourish playing) restarts the wait.
        /// </summary>
        public bool IdleDue(float dt, bool idle, HeldFlourishKind held, double unit)
        {
            if (!idle || Active || !HeldFlourishes.IdleRepeats(held))
            {
                _idleLeft = -1f;
                return false;
            }

            if (_idleLeft < 0f)
            {
                _idleLeft = HeldFlourishes.IdleGap(unit);
            }

            _idleLeft -= Math.Max(0f, dt);
            if (_idleLeft > 0f)
            {
                return false;
            }

            _idleLeft = -1f;
            return true;
        }
    }
}
