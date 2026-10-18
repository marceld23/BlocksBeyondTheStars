// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
using System;
using System.Runtime.InteropServices;
#endif

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The gamepad's rumble (#2746). The effects of the local player's game — a hit taken, the own shot or swing, an
    /// explosion, a ship hit, a hyperspace jump, a hard landing, the own block breaking — add pulses to one
    /// <see cref="RumbleMixer"/>; once a frame this component steps it and sends the two motor levels to the pad.
    /// <para>It rumbles only while it may: the <see cref="ClientSettings.PadVibration"/> switch is on, the player is
    /// playing on the pad (<see cref="InputMap.ActiveDevice"/>), a pad is connected, the window has the focus, no menu
    /// holds the game and the player is not dead. The motors stop at once when any of that ends, when the app loses the
    /// focus or pauses, when the world is torn down (<see cref="FxCamera.Reset"/>, this component's destruction) and on
    /// quit — a pad must never buzz on by itself. It is its own setting, independent of the camera-motion comfort switch.</para>
    /// <para>The game runs on the legacy Input Manager, which has no motor API. On Windows (and a Windows build under
    /// Proton) the motors are driven through XInput directly (<c>XInputSetState</c>, every pad slot): Xbox pads and
    /// anything that presents itself as one. WebGL and the native Linux build have no backend —
    /// <see cref="Supported"/> is false there and the settings row says so.</para>
    /// </summary>
    public sealed class PadRumble : MonoBehaviour
    {
        public GameBootstrap Game;

        private static readonly RumbleMixer Mixer = new RumbleMixer();
        private static PadRumble _live;

        /// <summary>The motor levels last sent, in 1/64 steps (−1 = nothing sent yet).</summary>
        private static int _sentLow = -1, _sentHigh = -1;

        /// <summary>Test seam: receives the motor levels (0..1) instead of the pad — the build machine has none. Also makes
        /// <see cref="Supported"/> true.</summary>
        public static System.Action<float, float> BackendOverrideForTest;

        /// <summary>Test seams: pin the window focus, whether a pad counts as connected, and the vibration setting (used
        /// when the component has no <see cref="Game"/>).</summary>
        public static bool? FocusOverrideForTest;
        public static bool? PadConnectedOverrideForTest;
        public static bool? SettingOverrideForTest;

        /// <summary>Whether this platform can rumble a pad at all.</summary>
        public static bool Supported
        {
            get
            {
                if (BackendOverrideForTest != null)
                {
                    return true;
                }
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
                return XInput.Available;
#else
                return false;
#endif
            }
        }

        // ------------------------------------------------------------------------------------------------
        // The triggers (the local player's own effects)
        // ------------------------------------------------------------------------------------------------

        /// <summary>Camera trauma — an explosion, a ship hit, hyperspace, a giant's stomp (<see cref="FxCamera.AddTrauma"/>).
        /// Under <see cref="RumbleMixer.MinTrauma"/> nothing.</summary>
        public static void Trauma(float trauma)
        {
            if (_live != null)
            {
                Mixer.PulseTrauma(trauma);
            }
        }

        /// <summary>A hit the player took (the damage feedback's trauma, 0..<see cref="DamageFeedback.MaxTrauma"/>): even a
        /// chip of damage gives a short thud, a heavy hit a strong one.</summary>
        public static void Damage(float trauma)
        {
            if (_live == null || float.IsNaN(trauma) || float.IsInfinity(trauma))
            {
                return;
            }

            float t = Mathf.Clamp(trauma, 0f, DamageFeedback.MaxTrauma);
            Mixer.Pulse(0.25f + (1.8f * t), 0.3f + t, 0.12f + (0.8f * t));
        }

        /// <summary>The own shot or swing (<see cref="FxCamera.Kick"/>, 0.4–2.2 degrees): a short buzz on the light motor.</summary>
        public static void WeaponKick(float degrees)
        {
            if (_live == null || float.IsNaN(degrees) || float.IsInfinity(degrees))
            {
                return;
            }

            float d = Mathf.Clamp(Mathf.Abs(degrees), 0f, 3f);
            Mixer.Pulse(0.08f * d, 0.25f * d, 0.06f + (0.03f * d));
        }

        /// <summary>A landing: <paramref name="hard"/> (fall damage) a heavy thump, otherwise a light one.</summary>
        public static void Landing(bool hard)
        {
            if (_live != null)
            {
                Mixer.Pulse(hard ? 0.85f : 0.35f, hard ? 0.45f : 0.12f, hard ? 0.35f : 0.14f);
            }
        }

        /// <summary>The block the player was mining breaks.</summary>
        public static void BlockBroken()
        {
            if (_live != null)
            {
                Mixer.Pulse(0.18f, 0.32f, 0.09f);
            }
        }

        /// <summary>Stops the motors at once and forgets every pulse (world teardown, death, focus loss, quit).</summary>
        public static void StopAll()
        {
            Mixer.Clear();
            Send(0f, 0f);
        }

        // ------------------------------------------------------------------------------------------------
        // The frame
        // ------------------------------------------------------------------------------------------------

        /// <summary>Whether the motors may run: the setting is on, the player plays on a connected pad, the window has the
        /// focus, no menu holds the game and the player is not dead.</summary>
        public static bool Allowed(bool setting, InputDeviceKind device, bool padConnected, bool focused, bool menuOpen, bool dead)
            => setting && device == InputDeviceKind.Gamepad && padConnected && focused && !menuOpen && !dead;

        private void OnEnable()
        {
            _live = this;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Test seam: one frame of <paramref name="dt"/> seconds, exactly as <c>Update</c> runs it.</summary>
        public void TickForTest(float dt) => Tick(dt);

        /// <summary>Test seam: forgets every pulse and what was sent, and clears the overrides.</summary>
        public static void ResetForTest()
        {
            Mixer.Clear();
            _live = null;
            _sentLow = -1;
            _sentHigh = -1;
            BackendOverrideForTest = null;
            FocusOverrideForTest = null;
            PadConnectedOverrideForTest = null;
            SettingOverrideForTest = null;
        }

        private void Tick(float dt)
        {
            if (_live == null)
            {
                _live = this; // the ticking instance takes the pulses (an EditMode test never runs OnEnable)
            }

            bool setting = Game != null && Game.Settings != null ? Game.Settings.PadVibration : (SettingOverrideForTest ?? false);
            bool focused = FocusOverrideForTest ?? Application.isFocused;
            bool connected = PadConnectedOverrideForTest ?? InputMap.GamepadConnected;
            bool menuOpen = Game != null && Game.MenuOpen;
            bool dead = Game != null && (Game.AwaitingRespawnConfirm || Game.Health <= 0f);
            var (low, high) = Mixer.Step(dt, Allowed(setting, InputMap.ActiveDevice, connected, focused, menuOpen, dead));
            Send(low, high);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                StopAll();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                StopAll();
            }
        }

        private void OnApplicationQuit() => StopAll();

        private void OnDisable()
        {
            if (_live == this)
            {
                _live = null;
            }

            StopAll();
        }

        private void OnDestroy()
        {
            if (_live == this)
            {
                _live = null;
            }

            StopAll();
        }

        /// <summary>Sends the levels to the motors when they changed (in 1/64 steps; zero always exactly).</summary>
        private static void Send(float low, float high)
        {
            int ql = Mathf.Clamp(Mathf.CeilToInt(low * 64f), 0, 64);
            int qh = Mathf.Clamp(Mathf.CeilToInt(high * 64f), 0, 64);
            if (ql == _sentLow && qh == _sentHigh)
            {
                return;
            }

            if (_sentLow < 0 && ql == 0 && qh == 0)
            {
                _sentLow = 0; // nothing ever rumbled: no need to touch the pad
                _sentHigh = 0;
                return;
            }

            _sentLow = ql;
            _sentHigh = qh;
            SetMotors(ql / 64f, qh / 64f);
        }

        private static void SetMotors(float low, float high)
        {
            if (BackendOverrideForTest != null)
            {
                BackendOverrideForTest(low, high);
                return;
            }
#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
            XInput.Set(low, high);
#endif
        }

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN) && !UNITY_WEBGL
        /// <summary>The XInput motors (Windows, and a Windows build under Proton): <c>xinput1_4.dll</c>, else the older
        /// <c>xinput9_1_0.dll</c>; a missing library or entry point is remembered and never asked again. Every error is
        /// swallowed — rumble is never worth a crash.</summary>
        private static class XInput
        {
            private const uint MaxPads = 4;

            [StructLayout(LayoutKind.Sequential)]
            private struct Vibration
            {
                public ushort LeftMotorSpeed;
                public ushort RightMotorSpeed;
            }

            [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
            private static extern uint SetState14(uint userIndex, ref Vibration vibration);

            [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
            private static extern uint SetState910(uint userIndex, ref Vibration vibration);

            /// <summary>0 = xinput1_4, 1 = xinput9_1_0, 2 = none found.</summary>
            private static int _api;
            private static bool _probed;

            /// <summary>Whether an XInput library answers (probed once with "motors off" on the first pad slot).</summary>
            public static bool Available
            {
                get
                {
                    if (!_probed)
                    {
                        _probed = true;
                        var off = default(Vibration);
                        Call(0, ref off);
                    }

                    return _api < 2;
                }
            }

            public static void Set(float low, float high)
            {
                var v = new Vibration { LeftMotorSpeed = Motor(low), RightMotorSpeed = Motor(high) };
                for (uint i = 0; i < MaxPads; i++)
                {
                    if (!Call(i, ref v))
                    {
                        return;
                    }
                }
            }

            private static ushort Motor(float level) => (ushort)Mathf.Clamp(Mathf.RoundToInt(level * 65535f), 0, 65535);

            /// <summary>Sets one pad slot's motors (a slot without a pad just answers "not connected"). False when no
            /// XInput library is there.</summary>
            private static bool Call(uint index, ref Vibration v)
            {
                while (_api < 2)
                {
                    try
                    {
                        if (_api == 0)
                        {
                            SetState14(index, ref v);
                        }
                        else
                        {
                            SetState910(index, ref v);
                        }

                        return true;
                    }
                    catch (DllNotFoundException)
                    {
                        _api++;
                    }
                    catch (EntryPointNotFoundException)
                    {
                        _api++;
                    }
                    catch (Exception)
                    {
                        _api = 2; // anything else: give rumble up quietly for this session
                    }
                }

                return false;
            }
        }
#endif
    }
}
