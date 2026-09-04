// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Gamepad backend (legacy Input Manager). Reads the joystick axes added to <c>InputManager.asset</c>
    /// and the standard <see cref="KeyCode.JoystickButton0"/>… buttons. Mapping targets the common case —
    /// an <b>Xbox / XInput</b> pad on Windows; the axis numbers (right stick = 4th/5th axis, d-pad = 6th/7th)
    /// and the A/B/X/Y/LB/RB button order follow that layout. Other pad families (DirectInput / PlayStation
    /// via some drivers) report different numbers, so the axis names and this button table are the single
    /// place to retune — see issue #195 (needs a pass on real hardware; not verifiable in CI).
    ///
    /// The left stick is NOT read here: the project's InputManager already maps it onto the shared
    /// "Horizontal"/"Vertical" axes, so movement flows through <see cref="DesktopInputSource.MoveX"/> for
    /// free. This source therefore contributes the RIGHT stick (look), the face/shoulder buttons, and the
    /// d-pad (hotbar). <see cref="InputMap"/> combines it with the keyboard/mouse source, so both are always
    /// live; with no pad connected every getter here returns zero/false.
    /// </summary>
    /// <summary>A pad button by NAME, for screens that need more of the pad than the rebindable
    /// <see cref="InputAction"/> set covers — the ship / face editors' viewport verbs (#1198) and, later,
    /// the minigame host (#1218). Reading these through <see cref="InputMap.PadDown"/> keeps the
    /// XInput button numbers in this one file: before #1198 nine screens spelled out
    /// <c>KeyCode.JoystickButton1</c> and a reader had to know that "1" means B.</summary>
    public enum PadButton
    {
        A, B, X, Y, Lb, Rb, Back, Start, L3, R3,
    }

    public sealed class GamepadInputSource : IInputSource
    {
        // Axis names — must match the entries appended to client/ProjectSettings/InputManager.asset.
        private const string AxisRightStickX = "RightStickX";
        private const string AxisRightStickY = "RightStickY"; // inverted in the asset so up = look up (like Mouse Y)
        private const string AxisDpadX = "DPadX";
        private const string AxisDpadY = "DPadY";        // #1220: 7th axis, positive = up (same sign rule as DPadX)
        private const string AxisTriggers = "Triggers";  // #1220: combined XInput trigger axis — LT positive, RT negative

        // XInput button layout (Windows).
        private const KeyCode BtnA = KeyCode.JoystickButton0;   // jump / submit
        private const KeyCode BtnB = KeyCode.JoystickButton1;   // crouch / cancel
        private const KeyCode BtnX = KeyCode.JoystickButton2;   // interact
        private const KeyCode BtnY = KeyCode.JoystickButton3;   // toggle third-person
        private const KeyCode BtnLb = KeyCode.JoystickButton4;  // place block
        private const KeyCode BtnRb = KeyCode.JoystickButton5;  // mine / attack
        private const KeyCode BtnBack = KeyCode.JoystickButton6; // Back / View — VEGA continue (#1041)
        private const KeyCode BtnStart = KeyCode.JoystickButton7; // Start / Menu (InputAction.UiMenu, #1198)
        private const KeyCode BtnL3 = KeyCode.JoystickButton8;  // left-stick click — context-actions list (#1043)
        private const KeyCode BtnR3 = KeyCode.JoystickButton9;  // right-stick click

        // Tunables (see issue #195). Look is a rate (deg/sec at sensitivity 1) turned into a per-frame delta
        // so it lands in the same space as a mouse delta — the caller still multiplies by MouseSensitivity,
        // so pad turn speed also scales with that slider. Deadzone rejects stick drift at rest.
        private const float DefaultStickDeadzone = 0.2f;
        private const float LookYawSpeed = 75f;
        private const float LookPitchSpeed = 60f;
        private const float DpadRepeatSeconds = 0.25f;

        // The two shoulder buttons ARE place and mine, so "holding one" and "aiming carefully" are the same
        // moment: while either is down the pad looks at half rate, which is the precision help #1220 asked
        // for without spending another button on it. Mouse look is untouched (this scales the pad only).
        private const float PrecisionLookScale = 0.5f;

        // How far the combined trigger axis must travel before it counts as a press. Deliberately generous:
        // the resting value of that axis is the one thing that differs between XInput, Proton and the
        // browser Gamepad API, which is also why the feature ships switched off (see ClientSettings).
        private const float TriggerThreshold = 0.5f;

        private float _dpadCooldownUntil;
        private float _dpadYCooldownUntil;
        private int _dpadYStep;
        private int _dpadYFrame = -1;
        private int _triggerNow;
        private int _triggerPrev;
        private int _triggerFrame = -1;

        public InputDeviceKind Kind => InputDeviceKind.Gamepad;

        private static bool _connected;
        private static int _connectedFrame = -1;

        /// <summary>Whether at least one joystick is connected (a non-empty name). Evaluated once per frame
        /// (#1512 — <c>Input.GetJoystickNames()</c> allocates a fresh string array, and this used to run on
        /// every ActionDown/Held/Up poll); when false the source stays fully inert so an unplugged pad costs
        /// nothing.</summary>
        public static bool Connected()
        {
            int frame = Time.frameCount;
            if (frame == _connectedFrame)
            {
                return _connected;
            }

            _connectedFrame = frame;
            _connected = false;
            var names = Input.GetJoystickNames();
            if (names == null)
            {
                return false;
            }

            for (int i = 0; i < names.Length; i++)
            {
                if (!string.IsNullOrEmpty(names[i]))
                {
                    _connected = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>The player's stick dead zone (#1219), clamped to a range that can neither swallow the
        /// whole stick nor let drift through. Falls back to the shipped constant with no settings loaded.</summary>
        private static float Deadzone => Settings != null
            ? Mathf.Clamp(Settings.PadDeadzone, ClientSettings.PadDeadzoneMin, ClientSettings.PadDeadzoneMax)
            : DefaultStickDeadzone;

        private static float Deadzoned(float v) => Mathf.Abs(v) < Deadzone ? 0f : v;

        /// <summary>Extra pad-only look scaling: the player's relative sensitivity (#1219) times the
        /// precision slow-down while a shoulder button is held. It is RELATIVE on purpose — the two call
        /// sites that consume the merged look value scale it by two different constants
        /// (MouseSensitivity on foot, LookSpeed in flight), so an absolute pad rate would have to be
        /// divided back out at one of them and would drift apart from the other.</summary>
        private static float LookScale(float setting)
        {
            float sens = Settings != null ? Mathf.Clamp(setting, ClientSettings.PadLookMin, ClientSettings.PadLookMax) : 1f;
            bool precision = Input.GetKey(BtnLb) || Input.GetKey(BtnRb);
            return precision ? sens * PrecisionLookScale : sens;
        }

        // Left stick already feeds the shared Horizontal/Vertical axes — don't double-count it here.
        public float MoveX() => 0f;
        public float MoveY() => 0f;

        public float LookX()
        {
            if (!Connected())
            {
                return 0f;
            }

            return Deadzoned(Input.GetAxis(AxisRightStickX)) * LookYawSpeed * Time.deltaTime
                   * LookScale(Settings != null ? Settings.PadLookX : 1f);
        }

        public float LookY()
        {
            if (!Connected())
            {
                return 0f;
            }

            float invert = Settings != null && Settings.PadInvertY ? -1f : 1f;
            return Deadzoned(Input.GetAxis(AxisRightStickY)) * LookPitchSpeed * Time.deltaTime
                   * LookScale(Settings != null ? Settings.PadLookY : 1f) * invert;
        }

        /// <summary>D-pad left/right cycles the hotbar, edge-/repeat-gated so a held press steps at a steady
        /// rate rather than flying through all nine slots in one frame. Sign matches the mouse wheel:
        /// &gt;0 = previous slot, &lt;0 = next slot.</summary>
        public float HotbarScroll()
        {
            if (!Connected())
            {
                return 0f;
            }

            float dx = Deadzoned(Input.GetAxis(AxisDpadX));
            if (Mathf.Abs(dx) < 0.5f)
            {
                _dpadCooldownUntil = 0f; // released → ready to fire immediately on next press
                return 0f;
            }

            if (Time.unscaledTime < _dpadCooldownUntil)
            {
                return 0f;
            }

            _dpadCooldownUntil = Time.unscaledTime + DpadRepeatSeconds;
            return dx > 0f ? -1f : 1f; // right = next (<0), left = previous (>0)
        }

        /// <summary>One repeat-gated d-pad up/down step: +1 up, -1 down, 0 while the d-pad rests or its
        /// repeat is on cooldown. Same gating as the left/right hotbar cycle.
        ///
        /// The result is cached PER FRAME because it drives discrete actions (<see cref="ActionDown"/>):
        /// several screens poll the same action in one frame and every read must agree, which a raw
        /// cooldown - where the first caller consumes the step - would not give them.</summary>
        public int DpadYStep()
        {
            if (_dpadYFrame == Time.frameCount)
            {
                return _dpadYStep;
            }

            _dpadYFrame = Time.frameCount;
            _dpadYStep = 0;
            if (!Connected())
            {
                return 0;
            }

            float dy = Deadzoned(Input.GetAxis(AxisDpadY));
            if (Mathf.Abs(dy) < 0.5f)
            {
                _dpadYCooldownUntil = 0f; // released -> ready to fire immediately on next press
                return 0;
            }

            if (Time.unscaledTime < _dpadYCooldownUntil)
            {
                return 0;
            }

            _dpadYCooldownUntil = Time.unscaledTime + DpadRepeatSeconds;
            _dpadYStep = dy > 0f ? 1 : -1;
            return _dpadYStep;
        }

        private const int TriggerMine = 1;  // RT - the negative half of the combined axis
        private const int TriggerPlace = 2; // LT - the positive half

        /// <summary>Samples the combined trigger axis once per frame and remembers the previous frame's
        /// state, so a trigger can report a press EDGE (mine/place fires once per pull, not every frame).
        /// Fully inert unless the player switched the option on - see <see cref="TriggerThreshold"/>.</summary>
        private void SampleTriggers()
        {
            if (_triggerFrame == Time.frameCount)
            {
                return;
            }

            _triggerFrame = Time.frameCount;
            _triggerPrev = _triggerNow;
            _triggerNow = 0;
            if (!Connected() || Settings == null || !Settings.PadTriggersMinePlace)
            {
                return;
            }

            float t = Input.GetAxis(AxisTriggers);
            if (t <= -TriggerThreshold)
            {
                _triggerNow |= TriggerMine;
            }
            else if (t >= TriggerThreshold)
            {
                _triggerNow |= TriggerPlace;
            }
        }

        private bool TriggerDown(int bit)
        {
            SampleTriggers();
            return (_triggerNow & bit) != 0 && (_triggerPrev & bit) == 0;
        }

        private bool TriggerHeld(int bit)
        {
            SampleTriggers();
            return (_triggerNow & bit) != 0;
        }

        public bool JumpHeld() => Connected() && Input.GetKey(BtnA);
        public bool JumpDown() => Connected() && Input.GetKeyDown(BtnA);
        public bool CrouchHeld() => Connected() && Input.GetKey(BtnB);

        // Mine / place: the shoulder buttons always, plus the triggers once the player switches them on
        // (#1220). The two paths are ORed, so enabling triggers ADDS a way to mine rather than moving it.
        public bool PrimaryDown() => (Connected() && Input.GetKeyDown(BtnRb)) || TriggerDown(TriggerMine);
        public bool PrimaryHeld() => (Connected() && Input.GetKey(BtnRb)) || TriggerHeld(TriggerMine);
        public bool SecondaryDown() => (Connected() && Input.GetKeyDown(BtnLb)) || TriggerDown(TriggerPlace);

        // No direct 1..9 pick on a pad — the hotbar is cycled via HotbarScroll (d-pad) instead.
        public int HotbarSlotDown() => -1;

        /// <summary>The <see cref="KeyCode"/> behind a named pad button (XInput layout, see the class summary).</summary>
        public static KeyCode CodeOf(PadButton button) => button switch
        {
            PadButton.A => BtnA,
            PadButton.B => BtnB,
            PadButton.X => BtnX,
            PadButton.Y => BtnY,
            PadButton.Lb => BtnLb,
            PadButton.Rb => BtnRb,
            PadButton.Back => BtnBack,
            PadButton.Start => BtnStart,
            PadButton.L3 => BtnL3,
            PadButton.R3 => BtnR3,
            _ => KeyCode.None,
        };

        /// <summary>A named pad button pressed this frame. False with no pad connected, like every getter here.</summary>
        public static bool Down(PadButton button) => Connected() && Input.GetKeyDown(CodeOf(button));

        /// <summary>A named pad button held this frame.</summary>
        public static bool Held(PadButton button) => Connected() && Input.GetKey(CodeOf(button));

        /// <summary>Raw left stick, deadzoned. The gameplay path does NOT use this — the project's
        /// InputManager already folds the left stick into the shared "Horizontal"/"Vertical" axes, so
        /// <see cref="MoveX"/> stays 0 to avoid double-counting. Screens that drive something OTHER than the
        /// player (an editor's fly-cam, a paint cursor) need the stick on its own, and that is what these
        /// two are for (#1198).</summary>
        public static float RawStickX() => Connected() ? Deadzoned(Input.GetAxis("Horizontal")) : 0f;

        /// <summary>Raw left stick vertical, deadzoned — see <see cref="RawStickX"/>.</summary>
        public static float RawStickY() => Connected() ? Deadzoned(Input.GetAxis("Vertical")) : 0f;

        /// <summary>Raw right stick vertical (positive = up, the asset inverts it), deadzoned, with NONE of
        /// the look rate / sensitivity that <see cref="LookY"/> applies — menus scroll a pane with it.</summary>
        public static float RawRightStickY() => Connected() ? Deadzoned(Input.GetAxis(AxisRightStickY)) : 0f;

        /// <summary>D-pad X as a raw −1/0/1-ish axis (deadzoned), for the minigame host's own edge+repeat
        /// logic (#1218) — unlike <see cref="DpadStep"/> this applies NO cooldown of its own.</summary>
        public static float RawDpadX() => Connected() ? Deadzoned(Input.GetAxis(AxisDpadX)) : 0f;

        /// <summary>D-pad Y raw (positive = up), same contract as <see cref="RawDpadX"/>.</summary>
        public static float RawDpadY() => Connected() ? Deadzoned(Input.GetAxis(AxisDpadY)) : 0f;

        /// <summary>The player's pad-binding source (set from <see cref="InputMap.Use"/>). Null = defaults only.</summary>
        public static ClientSettings Settings;

        /// <summary>The BUILT-IN pad button for a discrete action, or <see cref="KeyCode.None"/> if the action
        /// has no stock pad button (it then stays keyboard-only unless the player binds one — sources are
        /// combined, so nothing is lost).</summary>
        // NOTE: FlightEnterInterior deliberately has NO default pad button. ToggleThirdPerson (Y) is polled
        // during flight too, so sharing Y would fire BOTH on one press at the helm (switch view AND walk the
        // interior). It stays keyboard-F by default; bindable to a free pad button in the settings.
        public static KeyCode DefaultButtonFor(InputAction action) => action switch
        {
            InputAction.Interact => BtnX,
            InputAction.ToggleThirdPerson => BtnY,
            // R3 (#940): free on the stock layout, and "click the stick" mirrors the action's default
            // middle-CLICK. D-pad-down would read nicer next to the d-pad hotbar cycle, but the project
            // only defines a DPadX axis — d-pad Y isn't readable without touching ProjectSettings.
            InputAction.HotbarAction => BtnR3,
            // The last two free buttons on the stock layout (#1043). LS mirrors RS: click a stick, get a
            // menu — RS = slot actions, LS = the context-actions list that reaches every other verb. Back
            // advances VEGA (#1041): the one verb a pad player presses often enough to deserve its own button.
            InputAction.ContextActions => BtnL3,
            InputAction.VegaContinue => BtnBack,
            // Menu verbs (#1198): the two buttons nine screens already polled raw, now bound like every
            // other action. B doubles as crouch on foot and Start as nothing else — neither is read while a
            // screen owns the input, so the roles never collide.
            InputAction.UiCancel => BtnB,
            InputAction.UiMenu => BtnStart,
            _ => KeyCode.None,
        };

        /// <summary>The pad button bound to a discrete action — the player's override from the pad-rebinding
        /// UI (<see cref="ClientSettings.PadBindings"/>) if set, else <see cref="DefaultButtonFor"/>.</summary>
        public static KeyCode ButtonFor(InputAction action)
        {
            if (Settings == null)
            {
                return DefaultButtonFor(action);
            }

            // #1512: per-action table instead of action.ToString() + string scan + Enum.TryParse per poll.
            if (_padTable == null || !ReferenceEquals(_padTableSettings, Settings) || _padTableVersion != Settings.BindingsVersion)
            {
                RebuildPadTable();
            }

            int i = (int)action;
            return i >= 0 && i < _padTable.Length ? _padTable[i] : ResolveButton(action);
        }

        private static KeyCode[] _padTable;
        private static ClientSettings _padTableSettings;
        private static int _padTableVersion = -1;

        /// <summary>The uncached resolution — the player's override if set, else the default.</summary>
        private static KeyCode ResolveButton(InputAction action)
        {
            var def = DefaultButtonFor(action);
            if (Settings == null)
            {
                return def;
            }

            string name = Settings.BoundPadName(action.ToString());
            return !string.IsNullOrEmpty(name) && System.Enum.TryParse<KeyCode>(name, out var kc) ? kc : def;
        }

        private static void RebuildPadTable()
        {
            var values = (InputAction[])System.Enum.GetValues(typeof(InputAction));
            int max = 0;
            foreach (var v in values)
            {
                max = Mathf.Max(max, (int)v);
            }

            var table = new KeyCode[max + 1];
            foreach (var v in values)
            {
                table[(int)v] = ResolveButton(v);
            }

            _padTable = table;
            _padTableSettings = Settings;
            _padTableVersion = Settings.BindingsVersion;
        }

        /// <summary>The two verbs that live on the d-pad's vertical axis (#1220). An axis cannot be
        /// expressed as a <see cref="KeyCode"/>, so these two are NOT part of the rebinding table - they are
        /// fixed, and the same verbs stay bindable to a face button and to a key as usual.
        ///
        /// Up opens the chat / on-screen keyboard, which is what makes text entry reachable at all on a pad
        /// (#1211). Down rotates the held building shape: the issue asked both for that and for a second
        /// home for the context-actions list, and with up taken and left/right on the hotbar there is
        /// exactly one direction left - so it goes to the verb that has NO stock pad button, while the
        /// context list keeps the one it already owns (L3).</summary>
        private bool DpadActionDown(InputAction action)
        {
            if (action != InputAction.OpenChat && action != InputAction.RotateShape)
            {
                return false;
            }

            int step = DpadYStep();
            return action == InputAction.OpenChat ? step > 0 : step < 0;
        }

        public bool ActionDown(InputAction action)
        {
            if (DpadActionDown(action))
            {
                return true;
            }

            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKeyDown(b);
        }

        public bool ActionHeld(InputAction action)
        {
            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKey(b);
        }

        public bool ActionUp(InputAction action)
        {
            var b = ButtonFor(action);
            return b != KeyCode.None && Connected() && Input.GetKeyUp(b);
        }

        public bool HadActivityThisFrame()
        {
            if (!Connected())
            {
                return false;
            }

            // Any of our mapped buttons, or a stick/d-pad pushed past the deadzone.
            if (Input.GetKey(BtnA) || Input.GetKey(BtnB) || Input.GetKey(BtnX) || Input.GetKey(BtnY)
                || Input.GetKey(BtnLb) || Input.GetKey(BtnRb) || Input.GetKey(BtnBack) || Input.GetKey(BtnStart)
                || Input.GetKey(BtnL3) || Input.GetKey(BtnR3))
            {
                return true;
            }

            // Left stick shows up on the shared Horizontal/Vertical axes; sample those too so simply
            // steering with the stick flips the glyphs to the pad set.
            bool moved = Mathf.Abs(Deadzoned(Input.GetAxis("Horizontal"))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis("Vertical"))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisRightStickX))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisRightStickY))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisDpadX))) > 0f
                      || Mathf.Abs(Deadzoned(Input.GetAxis(AxisDpadY))) > 0f;
            if (moved)
            {
                return true;
            }

            // The triggers count only while the player has them switched on: their RESTING value is the one
            // reading that differs between pad families, and a pad that idles at plus/minus 1 would
            // otherwise flip every glyph in the game to the pad set and keep it there (#1220).
            return Settings != null && Settings.PadTriggersMinePlace
                   && Mathf.Abs(Input.GetAxis(AxisTriggers)) >= TriggerThreshold;
        }
    }
}
