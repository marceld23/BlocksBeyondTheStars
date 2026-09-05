// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>One minigame's local personal best, stored as a flat pair so Unity's JsonUtility can persist
    /// it (it can't serialize a Dictionary). Highscores are LOCAL and per-player — there is no leaderboard.</summary>
    [Serializable]
    public sealed class MinigameScore
    {
        public string Key = "";
        public int Best;
    }

    /// <summary>One remapped control, stored as a flat (action-name, KeyCode-name) pair so Unity's JsonUtility
    /// can persist the bindings as a list (it can't serialize a Dictionary) — mirrors <see cref="MinigameScore"/>.</summary>
    [Serializable]
    public sealed class KeyBinding
    {
        public string Action = "";
        public string Key = ""; // a UnityEngine.KeyCode name, e.g. "E"
    }

    /// <summary>
    /// One coloured mark a player put on a body in the star map. Asked for by a player: "Ich will Planeten im
    /// Weltraum markieren, aber mit verschiedenen Farben machen können" — several planets marked at once, each
    /// in its own colour, unlike the single planet-surface waypoint. Purely local (no server involvement), and
    /// stored as a flat list because JsonUtility cannot persist a Dictionary (as with
    /// <see cref="MinigameScore"/> / <see cref="KeyBinding"/>).
    /// </summary>
    [Serializable]
    public sealed class PlanetMarker
    {
        /// <summary>World (save) name — body ids like "sys0-p5" repeat across saves, so marks are per world.</summary>
        public string World = "";
        public string BodyId = "";

        /// <summary>Index into <see cref="PlanetMarkerPalette.Colors"/>; a fixed named set rather than a free
        /// colour picker, so it stays usable for a child and the labels can be translated.</summary>
        public int Color;
    }

    /// <summary>The fixed set of mark colours offered in the star map, with locale keys for their names.</summary>
    public static class PlanetMarkerPalette
    {
        public static readonly Color[] Colors =
        {
            new Color(1.00f, 0.30f, 0.30f), // red
            new Color(1.00f, 0.66f, 0.20f), // orange
            new Color(1.00f, 0.92f, 0.35f), // yellow
            new Color(0.40f, 0.85f, 0.45f), // green
            new Color(0.40f, 0.70f, 1.00f), // blue
            new Color(0.80f, 0.50f, 1.00f), // purple
        };

        public static readonly string[] NameKeys =
        {
            "ui.marker.color_red",
            "ui.marker.color_orange",
            "ui.marker.color_yellow",
            "ui.marker.color_green",
            "ui.marker.color_blue",
            "ui.marker.color_purple",
        };

        public static int Count => Colors.Length;
    }

    /// <summary>Graphics quality presets, including a Potato profile for weak / low-power machines.</summary>
    public enum QualityPreset { Potato, Low, Medium, High }

    /// <summary>
    /// Which background-music source the player prefers. <see cref="Synth"/> = the original code-synth
    /// ambient pads (the short bundled <c>music_*</c> loops with synthesized fallbacks); <see cref="Tracks"/>
    /// = the granular AI-composed track library streamed on demand from <c>StreamingAssets/music</c>.
    /// SFX/ambience are unaffected.
    /// </summary>
    public enum MusicMode { Synth, Tracks }

    /// <summary>
    /// How the in-game chat overlay behaves while the player is not typing. <see cref="Auto"/> = lines fade
    /// out a few seconds after they arrive (the default); <see cref="Always"/> = the scrollback stays up;
    /// <see cref="Off"/> = it never shows on its own. Opening the input always brings the recent lines back,
    /// whatever the mode — /help and /report replies would be unreadable otherwise (#636).
    /// </summary>
    public enum ChatVisibility { Auto, Always, Off }

    /// <summary>
    /// How the standalone window is presented. <see cref="Windowed"/> = a normal resizable, draggable
    /// window (can be moved to another monitor and maximized via the OS title bar); <see cref="Borderless"/>
    /// = borderless fullscreen filling the monitor the window currently sits on; <see cref="Exclusive"/> =
    /// true exclusive fullscreen at the display's native resolution. Maps to <see cref="FullScreenMode"/>.
    /// </summary>
    public enum WindowMode { Windowed, Borderless, Exclusive }

    /// <summary>Which layout the on-screen button NAMES follow (#1219). Purely cosmetic: a pad reports the
    /// same button numbers whatever is printed on it, so this only picks the wording, matched by physical
    /// position — see <see cref="InputMap.PadGlyph"/>.</summary>
    public enum PadGlyphSet { Xbox, PlayStation, Nintendo }

    /// <summary>
    /// Local, client-only settings (display, audio, input, comfort). These never affect the
    /// authoritative server rules (PvP, aliens, weapons stay server-decided). Persisted as JSON
    /// in <c>AppPaths.Root/client_settings.json</c>. See
    /// <c>docs/developer/CLIENT_SHELL_AND_ASSETS.md</c>.
    /// </summary>
    /// <summary>One saved avatar outfit (#1047): a named copy of everything the Avatar Designer produces for a
    /// look — the four body colours, the pixel face and the four body-paint parts. Purely local (the server
    /// only ever sees the look that is currently applied), stored as a flat class so JsonUtility can persist
    /// it. Gear toggles are NOT part of an outfit: in the game the shown gear follows the equipped items, the
    /// designer's toggles are only a preview.</summary>
    [Serializable]
    public sealed class AvatarOutfit
    {
        public string Name = "";
        public Color SkinColor;
        public Color TorsoColor;
        public Color ArmColor;
        public Color LegColor;
        public string FacePixels = "";
        public string TorsoPixels = "";
        public string ArmPixels = "";
        public string LegPixels = "";
        public string HelmetPixels = "";

        /// <summary>The body painting for a BodyPaint part index (empty for unknown parts).</summary>
        public string GetBodyPaint(int part)
        {
            switch (part)
            {
                case 0: return TorsoPixels ?? "";
                case 1: return ArmPixels ?? "";
                case 2: return LegPixels ?? "";
                case 3: return HelmetPixels ?? "";
                default: return "";
            }
        }

        /// <summary>Stores the body painting for a BodyPaint part index (unknown parts ignored).</summary>
        public void SetBodyPaint(int part, string pixels)
        {
            switch (part)
            {
                case 0: TorsoPixels = pixels ?? ""; break;
                case 1: ArmPixels = pixels ?? ""; break;
                case 2: LegPixels = pixels ?? ""; break;
                case 3: HelmetPixels = pixels ?? ""; break;
            }
        }

        /// <summary>A detached copy (so a saved outfit and the designer's scratch state never share pixels).</summary>
        public AvatarOutfit Clone() => new AvatarOutfit
        {
            Name = Name ?? "",
            SkinColor = SkinColor,
            TorsoColor = TorsoColor,
            ArmColor = ArmColor,
            LegColor = LegColor,
            FacePixels = FacePixels ?? "",
            TorsoPixels = TorsoPixels ?? "",
            ArmPixels = ArmPixels ?? "",
            LegPixels = LegPixels ?? "",
            HelmetPixels = HelmetPixels ?? "",
        };
    }

    [Serializable]
    public sealed class ClientSettings
    {
        // Graphics
        public QualityPreset Preset = QualityPreset.Medium;

        /// <summary>True while <see cref="Preset"/> is auto-managed: set on a browser/mobile first run and
        /// consumed by <see cref="AutoQualityCalibrator"/>, which steps the preset by measured frame time.
        /// Cleared the moment the player picks a preset by hand — a manual choice is never overridden.
        /// Existing installs load this as false, so their persisted preset stays untouched.</summary>
        public bool PresetAuto;

        /// <summary>How the window is presented (windowed / borderless / exclusive). Default borderless so the
        /// game opens fullscreen on whichever monitor it launches on; switch to Windowed (in the settings menu)
        /// for a draggable, maximizable window, or to Exclusive for true exclusive fullscreen.</summary>
        public WindowMode Window = WindowMode.Borderless;

        /// <summary>Windowed-mode size, persisted so toggling back from fullscreen restores it. Default fits a
        /// 1080p display with room for the title bar; clamped to the display in <see cref="Apply"/>.</summary>
        public int WindowedWidth = 1600;
        public int WindowedHeight = 900;

        // Default render distance in 16-block chunks (slider range 1–8 in the settings menu). Raised from the old
        // default of 2 (≈32 m — a near, foggy horizon) to 4 (≈64 m) so the world reads farther out of the box; the
        // per-planet/weather haze still scales off this (Sky.ApplyFog), so denser-atmosphere worlds stay hazier.
        // Singleplayer forwards this to the bundled server as the streaming radius (AppShell → --view-distance),
        // and the server now reclaims out-of-range chunks (far-chunk sweep), so the larger radius stays bounded.
        public int ViewDistanceChunks = 4;

        /// <summary>Player UI-scale multiplier for the HUD (0.8–1.6, 1 = shipped default). Applied in
        /// <see cref="Apply"/> via <see cref="UiKit.SetUserScale"/>, which divides the HUD canvases'
        /// reference resolution — a smaller reference draws the same layout bigger. Menus deliberately do
        /// NOT follow it: they lay out in absolute 1920 coordinates and would run off-screen (#483).
        /// (Declared since the first settings pass but dead until then — nothing ever read it.)</summary>
        public float UiScale = 1f;

        // Frame pacing. Exposed as its own switch instead of being baked into the quality preset: with VSync
        // on, the frame rate syncs to the display and tearing is gone, but on some setups — notably the
        // Windows client run through Proton/Wine on Linux — a GPU that just misses the refresh gets locked
        // to a hard 30 fps and the game feels sluggish. Turning VSync off lets those players run uncapped (or
        // at a chosen cap) for smoother frame times. Applied in Apply() AFTER SetQualityLevel, which would
        // otherwise stamp the preset's own vSyncCount.
        /// <summary>Sync frames to the display refresh (no tearing). Off lets the frame rate run free, capped
        /// only by <see cref="FrameRateCap"/> — the recommended setting for the Linux/Proton client.</summary>
        public bool VSync = true;

        /// <summary>Frame-rate cap in fps applied when <see cref="VSync"/> is off; 0 = unlimited. One of the
        /// values in <see cref="UiSettings"/>'s cap cycle (30/60/72/90/120/144/240).</summary>
        public int FrameRateCap = 0;

        // Look effects (the "professional / sci-fi look" layer). Each is also preset-gated at runtime — these
        // toggles only matter from Medium upward; Potato/Low force the expensive ones off regardless.
        /// <summary>Subpixel-morphological anti-aliasing (SMAA) on top of MSAA — smooths the shader/specular
        /// edges MSAA can't (voxel highlights, normal-map relief). A post-pass, so it needs camera post-processing
        /// on; gated to Medium+ (Potato/Low skip it for the frame-time budget). Applied in <see cref="ApplyCameraLook"/>.</summary>
        public bool Smaa = true;
        /// <summary>Global scene brightness, applied as a post-exposure lift on the colour grade so it affects every
        /// world uniformly (and is tunable per display). 1.0 = neutral; the default sits a touch above neutral so the
        /// ACES-tonemapped scene isn't too dark. Driven into <see cref="UrpScenePost"/>.</summary>
        public float Brightness = 1.15f;
        /// <summary>Screen-space lens flare on the sun + bright emitters (cheap, very sci-fi).</summary>
        public bool LensFlare = true;
        /// <summary>Subtle camera motion blur while flying the ship / driving the speeder (High+ only).</summary>
        public bool MotionBlur = true;
        /// <summary>Volumetric fog + god-rays (light shafts). Needs the depth texture (Medium+).</summary>
        public bool VolumetricFog = true;
        /// <summary>Screen-space reflections on water / glossy hull / metal. Needs depth + opaque (High+).</summary>
        public bool Reflections = true;

        // Audio (0..1)
        public float MasterVolume = 0.8f;
        public float MusicVolume = 0.6f;
        public float SfxVolume = 0.8f;
        public bool MenuAudio = true;

        /// <summary>Background-music source: the AI-composed track library (default) or the original
        /// code-synth ambient pads. SFX/ambience volumes are independent (<see cref="SfxVolume"/>).</summary>
        public MusicMode MusicMode = MusicMode.Tracks;

        // Controls
        public float MouseSensitivity = 2f;
        public bool InvertY = false;

        /// <summary>Vertical field of view of the first-person camera in degrees (#1589/#1590). The camera used
        /// to run on Unity's 60° default, which drew every block about a fifth larger on screen than the 70°
        /// most first-person block games use — "the blocks feel huge". 70 is the new default; the setting
        /// steps 50–100 (a wider view shows more chunks, so the cap keeps weak machines honest). An older
        /// settings file without the field simply gets the default.</summary>
        public float FieldOfView = FieldOfViewDefault;
        public const float FieldOfViewDefault = 70f;
        public const float FieldOfViewMin = 50f;
        public const float FieldOfViewMax = 100f;
        public const float FieldOfViewStep = 5f;

        /// <summary>The stored field of view clamped to the supported range (a hand-edited file can hold anything).</summary>
        public float ClampedFieldOfView => Mathf.Clamp(FieldOfView, FieldOfViewMin, FieldOfViewMax);

        // Controller (#1219). All of these are pad-only: with no pad connected they change nothing, and the
        // mouse/keyboard path never reads them. The shipped values are the constants the code used before
        // they were settings, so an existing client_settings.json behaves exactly as it did.
        public const float PadDeadzoneMin = 0.05f;
        public const float PadDeadzoneMax = 0.45f;
        public const float PadLookMin = 0.25f;
        public const float PadLookMax = 3f;

        /// <summary>Stick dead zone: how far a stick must leave centre before it counts. Raise it on a worn
        /// pad that drifts, lower it for finer aim.</summary>
        public float PadDeadzone = 0.2f;

        /// <summary>Pad look speed left/right, as a MULTIPLIER on top of <see cref="MouseSensitivity"/>
        /// (1 = exactly the speed the pad had before this setting existed). Relative rather than absolute
        /// because the merged look value is scaled by a different constant on foot than in flight.</summary>
        public float PadLookX = 1f;

        /// <summary>Pad look speed up/down — see <see cref="PadLookX"/>.</summary>
        public float PadLookY = 1f;

        /// <summary>Inverts the PAD's up/down look on top of the global <see cref="InvertY"/>, for players
        /// who want a flight-style stick without inverting the mouse as well.</summary>
        public bool PadInvertY = false;

        /// <summary>Controller vibration. STORED ONLY — the game runs on the legacy Input Manager, which has
        /// no rumble API, so nothing reads this yet. It is kept (and labelled as not-yet-working in the
        /// settings screen) so a player's choice survives until the day rumble can be wired up.</summary>
        public bool PadVibration = true;

        /// <summary>Which pad layout the button NAMES in hints and settings rows follow.</summary>
        public PadGlyphSet PadGlyphs = PadGlyphSet.Xbox;

        /// <summary>Mine and place on the triggers as well as the shoulder buttons. OFF by default on
        /// purpose: the trigger axis is the one reading that genuinely differs between XInput, Proton and
        /// the browser Gamepad API, and a pad whose triggers idle at full deflection would mine
        /// continuously. Off means a wrong axis number is a setting nobody switched on (#1220).</summary>
        public bool PadTriggersMinePlace = false;

        // Voice chat. Shipped on by default; this master switch turns the whole feature off (no capture, no
        // playback). The server must also have voice enabled, and you still need a radio. Push-to-talk by
        // default — hold the key to transmit. VoiceInputEnabled keeps playback while never transmitting.
        public bool VoiceEnabled = true;
        public float VoiceVolume = 1f;
        public bool VoiceInputEnabled = true;
        /// <summary>Push-to-talk key, stored as a <see cref="UnityEngine.KeyCode"/> name (default "V").</summary>
        public string PushToTalkKey = "V";

        /// <summary>Remapped controls (Stream C): per-<see cref="InputAction"/> key overrides resolved by
        /// <see cref="InputMap"/>. An action absent here uses its built-in default, so an empty list = stock
        /// controls. Stored as a flat list so JsonUtility can persist it.</summary>
        public List<KeyBinding> KeyBindings = new List<KeyBinding>();

        /// <summary>Remapped GAMEPAD buttons: per-<see cref="InputAction"/> pad-button overrides (stored as
        /// <see cref="UnityEngine.KeyCode"/> names like "JoystickButton2"), resolved by
        /// <see cref="GamepadInputSource"/>. Mirrors <see cref="KeyBindings"/>: absent = the built-in pad
        /// default, empty list = stock pad controls.</summary>
        public List<KeyBinding> PadBindings = new List<KeyBinding>();
        /// <summary>Optional named microphone device ("" = the system default).</summary>
        public string MicrophoneDevice = "";
        /// <summary>Players the local player has muted — **text chat and voice** (#1209). Entries are player
        /// ids where the server sends one (<see cref="BlocksBeyondTheStars.Networking.Messages.ChatMessage.SenderId"/>)
        /// and display names otherwise; both are matched, so a list built against an older server keeps working.
        /// <para>Purely local: the server is never told. Muting therefore leaks no social signal, and the voice
        /// fan-out stays a single broadcast (per-recipient filtering there would cost Raspberry-Pi tick).</para></summary>
        public System.Collections.Generic.List<string> MutedPlayers = new System.Collections.Generic.List<string>();

        /// <summary>Legacy name of <see cref="MutedPlayers"/> — voice only, and never written by anything until
        /// #1209 gave muting a UI. Kept as a field so JsonUtility still reads an existing settings file; it is
        /// folded into <see cref="MutedPlayers"/> on load and then left empty.</summary>
        public System.Collections.Generic.List<string> MutedVoicePlayers = new System.Collections.Generic.List<string>();

        /// <summary>Language code that drives the localizer: "en" or "de".</summary>
        public string Language = "en";

        /// <summary>Last singleplayer world the player launched (pre-selected in the world picker).</summary>
        public string LastWorld = "singleplayer";

        /// <summary>The official Velopack update feed: the GitHub repository, whose release assets carry
        /// the feed manifest + payload (read via Velopack's GithubSource — see <see cref="ClientUpdater"/>).</summary>
        public const string DefaultUpdateFeedUrl = "https://github.com/marceld23/BlocksBeyondTheStars";

        /// <summary>Velopack auto-update feed URL. Defaults to the official GitHub feed
        /// (<see cref="DefaultUpdateFeedUrl"/>); self-hosters can point it at their server's update
        /// endpoint instead (e.g. <c>http://192.168.1.50:31416/updates</c>, shown on that server's
        /// <c>/portal</c> page). An empty value is re-defaulted on load (#543) — to skip the startup
        /// check, turn off <see cref="UpdateCheckOnStart"/>. Only effective in an installed build.</summary>
        public string UpdateFeedUrl = DefaultUpdateFeedUrl;

        /// <summary>Quiet update check on launch (#543): when on, the main menu shows a once-per-session
        /// notice if the feed carries a newer release. Off = updates only via the manual settings button.</summary>
        public bool UpdateCheckOnStart = true;

        /// <summary>The game version whose "What's new?" the player has seen (#543). Differs from
        /// <see cref="AppShell.Version"/> exactly once after an update — the menu then auto-opens the
        /// release notes and stamps this. Empty = fresh install / pre-feature settings: stamped silently,
        /// because there is nothing "new" to catch a brand-new player up on.</summary>
        public string LastSeenVersion = "";

        /// <summary>Whether the once-per-install generic intro cinematic has played (#759). Stamped when
        /// it finishes or is skipped; replays from the Credits screen never re-stamp it. False on fresh
        /// installs AND on existing installs that predate the feature — both see the intro exactly once.</summary>
        public bool IntroSeen = false;

        /// <summary>The player's name — shown to other players and keying the server-side player state.
        /// Empty by default ON PURPOSE (#221): the main menu forces a choice before playing — a silent
        /// "Pilot" default made everyone collide as Pilot in multiplayer. Existing installs keep the
        /// name their settings file already carries.</summary>
        public string PlayerName = "";

        /// <summary>Per-install secret backing name verification: sent with every join; the first join
        /// under a name claims it, later joins must match. Generated once on load, never shown in UI.</summary>
        public string PlayerToken = "";

        /// <summary>Base URL of the official worlds portal (hosted-worlds control plane). Kept as a setting
        /// so self-hosters can point the menu at their own WorldHost; empty = the official default.</summary>
        public string PortalUrl = "";

        /// <summary>Bearer session for the worlds portal, saved after a successful sign-in so the menu stays
        /// signed in across launches. Only the session is stored — never the password.</summary>
        public string PortalSessionToken = "";

        /// <summary>Account name belonging to <see cref="PortalSessionToken"/> (display only).</summary>
        public string PortalAccountName = "";

        // Accessibility
        public bool ReducedEffects = false;

        /// <summary>Legacy one-shot "large UI" flag. Superseded by the continuous <see cref="UiScale"/>
        /// setting (#483); kept only so an existing settings file that has it on is migrated once, in
        /// <see cref="Apply"/>'s caller, rather than silently losing the player's preference.</summary>
        public bool LargeUi = false;

        /// <summary>Camera motion comfort toggle: head bob, the moving FOV kick and impact camera
        /// shake. Off = a steady camera for motion-sensitive players; sounds are unaffected.</summary>
        public bool CameraMotion = true;

        /// <summary>Holographic visor HUD styling (curvature + chromatic fringe + scanlines + glow). On = the
        /// stylised visor look; off = a clean, flat HUD overlay (better readability). Default on but subtle.</summary>
        public bool VisorEffects = true;

        // Avatar appearance (M23b). Per-part colours; later armor overrides the matching part.
        public Color SkinColor = new Color(0.85f, 0.68f, 0.55f);
        public Color TorsoColor = new Color(0.20f, 0.45f, 0.80f);
        public Color ArmColor = new Color(0.20f, 0.45f, 0.80f);
        public Color LegColor = new Color(0.25f, 0.25f, 0.32f);

        /// <summary>Ship hull colour (item 32) — tints the player's ship. Default = the steel tint the hull
        /// used before hull colours existed, so an unchanged ship looks the same.</summary>
        public Color HullColor = new Color(0.82f, 0.84f, 0.88f);

        /// <summary>The player's custom pixel face drawn in the in-game face editor, as a 16×16 palette-index
        /// string (see <see cref="FacePalette"/>); empty = the default procedural face. Shown on this player's
        /// avatar and sent to the server so other players see it. The server also persists it (the face follows
        /// the player), but this local copy is the source re-sent on each join/edit.</summary>
        public string FacePixels = "";

        // Avatar body paint (#874): the face's siblings for torso, arms, legs and the suit helmet, drawn
        // in the per-part unfolded strip editors (BodyPaintKit format). Same lifecycle as FacePixels:
        // local source of truth, re-sent per painted part on each join/edit; the server persists + relays.
        public string TorsoPixels = "";
        public string ArmPixels = "";
        public string LegPixels = "";
        public string HelmetPixels = "";

        /// <summary>The body painting for a BodyPaint part index (empty for unknown parts).</summary>
        public string GetBodyPaint(int part)
        {
            switch (part)
            {
                case 0: return TorsoPixels ?? "";
                case 1: return ArmPixels ?? "";
                case 2: return LegPixels ?? "";
                case 3: return HelmetPixels ?? "";
                default: return "";
            }
        }

        /// <summary>Stores the body painting for a BodyPaint part index (unknown parts ignored).</summary>
        public void SetBodyPaint(int part, string pixels)
        {
            switch (part)
            {
                case 0: TorsoPixels = pixels ?? ""; break;
                case 1: ArmPixels = pixels ?? ""; break;
                case 2: LegPixels = pixels ?? ""; break;
                case 3: HelmetPixels = pixels ?? ""; break;
            }
        }

        /// <summary>Upper bound on saved outfits (#1047) — keeps the designer's outfit list on one panel without
        /// scrolling and the settings file small (each painted part is a few KB of pixels).</summary>
        public const int MaxOutfits = 8;

        /// <summary>Saved avatar outfits (#1047), in the order they were created. The applied look above stays
        /// the source of truth for the game; an outfit only becomes it through the designer's Apply. Absent in
        /// older settings files, which therefore load with an empty list.</summary>
        public List<AvatarOutfit> Outfits = new List<AvatarOutfit>();

        /// <summary>The currently applied look as an outfit named <paramref name="name"/> (a detached copy).</summary>
        public AvatarOutfit CaptureOutfit(string name) => new AvatarOutfit
        {
            Name = name ?? "",
            SkinColor = SkinColor,
            TorsoColor = TorsoColor,
            ArmColor = ArmColor,
            LegColor = LegColor,
            FacePixels = FacePixels ?? "",
            TorsoPixels = TorsoPixels ?? "",
            ArmPixels = ArmPixels ?? "",
            LegPixels = LegPixels ?? "",
            HelmetPixels = HelmetPixels ?? "",
        };

        /// <summary>Makes <paramref name="outfit"/> the applied look (colours, face, body paint). Does not save;
        /// the caller decides when to persist, exactly like the designer's Apply.</summary>
        public void ApplyOutfit(AvatarOutfit outfit)
        {
            if (outfit == null)
            {
                return;
            }

            SkinColor = outfit.SkinColor;
            TorsoColor = outfit.TorsoColor;
            ArmColor = outfit.ArmColor;
            LegColor = outfit.LegColor;
            FacePixels = outfit.FacePixels ?? "";
            for (int part = 0; part < 4; part++)
            {
                SetBodyPaint(part, outfit.GetBodyPaint(part));
            }
        }

        /// <summary>Start in third-person (showing your own figure) instead of first-person.</summary>
        public bool ThirdPerson = false;

        /// <summary>Show the ship AI's (VEGA) advisor hints and story lines. The onboarding objective chip
        /// always shows until the tutorial is finished or skipped; this mutes the optional coaching.</summary>
        public bool VegaHints = true;

        /// <summary>Show floating health bars over enemies and creatures in combat (#692) — planet surface
        /// and space flight alike. Purely cosmetic (the values are replicated either way); off hides them.</summary>
        public bool ShowEnemyHealthBars = true;

        // Comfort / wellbeing (playtime). Purely client-side: the session timer counts real wall-clock from
        // the moment you enter a world; the reminder is VEGA gently suggesting a break (a real-world nudge, not
        // an in-fiction event). Both default on but unobtrusive.
        /// <summary>Show a small "session / total playtime" readout in the in-game HUD.</summary>
        public bool ShowSessionTime = true;

        /// <summary>Let VEGA remind you to take a break after a long unbroken session, repeating each interval.</summary>
        public bool PlaytimeReminder = true;

        /// <summary>Minutes of continuous session play between break reminders (also the first reminder's delay).</summary>
        public int ReminderMinutes = 60;

        /// <summary>How the chat overlay behaves when you are not typing — see <see cref="Client.ChatVisibility"/>.
        /// Auto (the default) keeps the HUD clear without hiding what other players say.</summary>
        public ChatVisibility ChatVisibility = ChatVisibility.Auto;

        /// <summary>NPC radio calls (#1119): 0 all · 1 missions only · 2 off. Mirrored to the SERVER on
        /// join and on change — the server initiates the calls, so a client-only mute could not silence
        /// them. 0 is what an absent field deserializes to, so old files default to "all".</summary>
        public int NpcCalls;

        /// <summary>Auto-stow loose materials/components into the ship's cargo hold the moment you board (tools
        /// and weapons stay on you). Off by default so boarding never silently empties your inventory; opt in to
        /// keep your personal pack clear for exploring on foot.</summary>
        public bool AutoStowOnBoard = false;

        /// <summary>Local personal-best scores for the bundled arcade minigames, keyed by game key. Local only —
        /// no server leaderboard. Stored as a flat list so JsonUtility can persist it.</summary>
        public List<MinigameScore> MinigameScores = new List<MinigameScore>();

        /// <summary>The player's best recorded score for a minigame (0 if never played).</summary>
        public int GetMinigameBest(string key)
        {
            if (string.IsNullOrEmpty(key) || MinigameScores == null) return 0;
            for (int i = 0; i < MinigameScores.Count; i++)
            {
                if (MinigameScores[i].Key == key) return MinigameScores[i].Best;
            }

            return 0;
        }

        /// <summary>Records a minigame score, keeping only the personal best. Returns true if it was a new best
        /// (so the caller can save + celebrate).</summary>
        public bool RecordMinigameScore(string key, int score)
        {
            if (string.IsNullOrEmpty(key) || score <= 0) return false;
            MinigameScores ??= new List<MinigameScore>();
            for (int i = 0; i < MinigameScores.Count; i++)
            {
                if (MinigameScores[i].Key == key)
                {
                    if (score <= MinigameScores[i].Best) return false;
                    MinigameScores[i].Best = score;
                    return true;
                }
            }

            MinigameScores.Add(new MinigameScore { Key = key, Best = score });
            return true;
        }

        /// <summary>Coloured marks the player put on bodies in the star map, per world. Local only.</summary>
        public List<PlanetMarker> PlanetMarkers = new List<PlanetMarker>();

        /// <summary>The mark colour index on a body, or -1 when it is unmarked.</summary>
        public int GetPlanetMarker(string world, string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId) || PlanetMarkers == null) return -1;
            for (int i = 0; i < PlanetMarkers.Count; i++)
            {
                if (PlanetMarkers[i].BodyId == bodyId && PlanetMarkers[i].World == (world ?? ""))
                {
                    return PlanetMarkers[i].Color;
                }
            }

            return -1;
        }

        /// <summary>Marks a body in a colour, or clears the mark with a negative index. Caller saves.</summary>
        public void SetPlanetMarker(string world, string bodyId, int color)
        {
            if (string.IsNullOrEmpty(bodyId)) return;
            PlanetMarkers ??= new List<PlanetMarker>();
            world ??= "";

            for (int i = 0; i < PlanetMarkers.Count; i++)
            {
                if (PlanetMarkers[i].BodyId == bodyId && PlanetMarkers[i].World == world)
                {
                    if (color < 0)
                    {
                        PlanetMarkers.RemoveAt(i);
                    }
                    else
                    {
                        PlanetMarkers[i].Color = color;
                    }

                    return;
                }
            }

            if (color >= 0)
            {
                PlanetMarkers.Add(new PlanetMarker { World = world, BodyId = bodyId, Color = color });
            }
        }

        /// <summary>#1512: bumped whenever a key or pad binding changes, so <see cref="InputMap"/> and the gamepad
        /// backend can keep a per-action lookup table instead of resolving <c>action.ToString()</c> + a string scan
        /// + <c>Enum.TryParse</c> on EVERY poll (~25–35 polls per frame ⇒ ~100 allocations per frame). Not
        /// persisted; a freshly loaded settings object starts at 0 and the tables rebuild on first use.</summary>
        [NonSerialized] public int BindingsVersion;

        /// <summary>Clears every key AND pad override (the settings screen's "reset controls"), bumping
        /// <see cref="BindingsVersion"/> so the lookup tables rebuild.</summary>
        public void ResetBindings()
        {
            KeyBindings?.Clear();
            PadBindings?.Clear();
            BindingsVersion++;
        }

        /// <summary>The bound KeyCode NAME for an input action (empty = the action uses its default key). String-
        /// keyed so this stays decoupled from the <see cref="InputAction"/> enum; <see cref="InputMap"/> parses it.</summary>
        public string BoundKeyName(string action)
        {
            if (string.IsNullOrEmpty(action) || KeyBindings == null) return "";
            for (int i = 0; i < KeyBindings.Count; i++)
            {
                if (KeyBindings[i].Action == action) return KeyBindings[i].Key;
            }

            return "";
        }

        /// <summary>Sets (or, when <paramref name="keyName"/> is empty, clears) the binding for an action. Returns
        /// true if anything changed (so the caller can Save).</summary>
        public bool SetBoundKey(string action, string keyName)
        {
            if (string.IsNullOrEmpty(action)) return false;
            KeyBindings ??= new List<KeyBinding>();
            for (int i = 0; i < KeyBindings.Count; i++)
            {
                if (KeyBindings[i].Action == action)
                {
                    if (KeyBindings[i].Key == keyName) return false;
                    if (string.IsNullOrEmpty(keyName)) KeyBindings.RemoveAt(i);
                    else KeyBindings[i].Key = keyName;
                    BindingsVersion++;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(keyName)) return false;
            KeyBindings.Add(new KeyBinding { Action = action, Key = keyName });
            BindingsVersion++;
            return true;
        }

        /// <summary>The bound gamepad-button NAME for an input action (empty = the pad default). Mirrors
        /// <see cref="BoundKeyName"/> for the pad binding list.</summary>
        public string BoundPadName(string action)
        {
            if (string.IsNullOrEmpty(action) || PadBindings == null) return "";
            for (int i = 0; i < PadBindings.Count; i++)
            {
                if (PadBindings[i].Action == action) return PadBindings[i].Key;
            }

            return "";
        }

        /// <summary>Sets (or, when <paramref name="keyName"/> is empty, clears) the gamepad binding for an
        /// action. Returns true if anything changed. Mirrors <see cref="SetBoundKey"/>.</summary>
        public bool SetBoundPad(string action, string keyName)
        {
            if (string.IsNullOrEmpty(action)) return false;
            PadBindings ??= new List<KeyBinding>();
            for (int i = 0; i < PadBindings.Count; i++)
            {
                if (PadBindings[i].Action == action)
                {
                    if (PadBindings[i].Key == keyName) return false;
                    if (string.IsNullOrEmpty(keyName)) PadBindings.RemoveAt(i);
                    else PadBindings[i].Key = keyName;
                    BindingsVersion++;
                    return true;
                }
            }

            if (string.IsNullOrEmpty(keyName)) return false;
            PadBindings.Add(new KeyBinding { Action = action, Key = keyName });
            BindingsVersion++;
            return true;
        }

        /// <summary>Directory the settings/token files live in; null = Unity's persistent data path. A static
        /// seam (Load/Save are static) so the edit-mode tests can run against a scratch directory instead of
        /// the developer's real settings.</summary>
        public static string StorageDirOverride = null;

        /// <summary>Locale KEY of a one-shot "settings were recovered / reset" notice set by <see cref="Load"/>.
        /// Load runs before the localizer exists, so the main menu localizes it into
        /// <see cref="AppShell.MenuNotice"/> at build time and clears this.</summary>
        public static string LoadNoticeKey = "";

        private static string StorageDir => string.IsNullOrEmpty(StorageDirOverride)
            ? AppPaths.Root
            : StorageDirOverride;

        private static string FilePath => Path.Combine(StorageDir, "client_settings.json");

        /// <summary>The previous settings file, kept by every atomic <see cref="Save"/> as the recovery source
        /// when the main file is lost/corrupted (crash mid-write, disk hiccup, AV interference).</summary>
        private static string BackupPath => FilePath + ".bak";

        /// <summary>Where an unreadable settings file is moved (never deleted) so nothing is clobbered and a
        /// bug report can include the evidence.</summary>
        private static string CorruptPath => FilePath + ".corrupt";

        /// <summary>Separate copy of just <see cref="PlayerToken"/>. The token is irreplaceable (it IS the
        /// name claim on every server), so it gets its own tiny file that no settings rewrite ever touches —
        /// the last line of defence when both the settings file and its .bak are gone (#410).</summary>
        private static string TokenPath => Path.Combine(StorageDir, "player_token.txt");

        public static ClientSettings Load()
        {
            // Browser builds (#1177): a new glitch.fun deployment starts with an EMPTY storage folder, while
            // the previous deployment's settings still sit in a sibling folder under the same IDBFS mount.
            // Adopt them first (name, language, intro-seen flag, the claim token's own backup file), so a
            // returning player is not treated as a fresh install. No-op outside WebGL / with an override.
            if (string.IsNullOrEmpty(StorageDirOverride) && !File.Exists(FilePath) && !File.Exists(BackupPath))
            {
                // feedback/sent.json = the "which reports did I send" memory that gates reply polling (#1328).
                WebGlStorage.TryAdoptFromPreviousDeployment("client_settings.json", "player_token.txt", "feedback/sent.json");
            }

            // Capture before touching the files: a genuine first run is the only time we auto-pick the
            // language from the OS. Returning players keep whatever they chose (even an explicit "en").
            // Any surviving file — main, backup or token — counts as an existing install.
            bool freshInstall = !File.Exists(FilePath) && !File.Exists(BackupPath) && !File.Exists(TokenPath);

            ClientSettings settings = TryReadSettingsFile(FilePath);
            bool recovering = settings == null && File.Exists(FilePath);
            if (recovering)
            {
                // The settings file exists but can't be parsed. Never reset it silently — that used to
                // destroy the only copy of the name-claim token and permanently lock the player out of
                // their own name (#410). Preserve the evidence, then fall back to the last good backup.
                PreserveCorruptFile();
                settings = TryReadSettingsFile(BackupPath);
                LoadNoticeKey = settings != null ? "ui.settings.recovered_backup" : "ui.settings.reset_defaults";
            }

            settings ??= new ClientSettings();

            // One-shot migration (#483): LargeUi was an accessibility flag that never did anything — no
            // code ever read it. It is now superseded by the continuous UiScale setting, so a player who
            // had ticked it lands on a comparable scale instead of silently losing the preference.
            if (settings.LargeUi)
            {
                settings.LargeUi = false;
                if (Mathf.Approximately(settings.UiScale, 1f))
                {
                    settings.UiScale = 1.3f;
                }
            }

            settings.UiScale = Mathf.Clamp(settings.UiScale, UiKit.UserScaleMin, UiKit.UserScaleMax);

            // Migration (#1209): MutedVoicePlayers becomes MutedPlayers (voice AND text). Nothing ever wrote
            // the old field, so in practice this list is empty — but a hand-edited settings file must not
            // silently lose it, and losing a mute list is worse than never having had one.
            if (settings.MutedVoicePlayers is { Count: > 0 })
            {
                foreach (var who in settings.MutedVoicePlayers)
                {
                    if (!string.IsNullOrWhiteSpace(who) && !settings.MutedPlayers.Contains(who))
                    {
                        settings.MutedPlayers.Add(who);
                    }
                }

                settings.MutedVoicePlayers.Clear();
            }

            // Migration (#543): until v0.9.1 there was no official feed, so every install carries an
            // empty URL and in-app updates were effectively self-host-only. An empty value now means
            // "official feed" — JsonUtility writes the stored "" over the field default, so re-default
            // here. Opting out of the startup check is UpdateCheckOnStart's job, not this URL's.
            if (string.IsNullOrWhiteSpace(settings.UpdateFeedUrl))
            {
                settings.UpdateFeedUrl = DefaultUpdateFeedUrl;
            }

            if (freshInstall)
            {
                // Match the OS language when we ship it (near-)complete; everything else falls back to
                // English. The chosen value is persisted by the Save below, so the pre-engine launcher
                // splash picks it up next launch.
                settings.Language = Application.systemLanguage switch
                {
                    SystemLanguage.German => "de",
                    SystemLanguage.French => "fr",
                    SystemLanguage.Spanish => "es",
                    SystemLanguage.Italian => "it",
                    SystemLanguage.Portuguese => "pt",
                    SystemLanguage.Polish => "pl",
                    SystemLanguage.Turkish => "tr",
                    SystemLanguage.Dutch => "nl",
                    SystemLanguage.Russian => "ru",
                    SystemLanguage.Ukrainian => "uk",
                    SystemLanguage.ChineseSimplified => "zh",
                    SystemLanguage.Chinese => "zh",
                    SystemLanguage.Japanese => "ja",
                    SystemLanguage.Korean => "ko",
                    _ => "en",
                };

                // Tablets and the browser build are GPU-weak next to a desktop, and the scene is heavy (custom
                // URP, SSAO, SMAA, PBR). Start those on a Lite preset so the first run is playable; the player
                // can still raise it in Settings. Only on a genuine first run — a returning player keeps theirs.
                // PresetAuto hands the preset to the shell frame-time calibration (#1423), which steps it up on
                // capable devices (a desktop browser is not stuck on Low forever) and down on struggling ones.
                if (Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer)
                {
                    settings.Preset = QualityPreset.Low;
                    settings.PresetAuto = true;

                    // Phone/tablet-class browser (#1424): shorter horizon, and the synth music engine instead
                    // of the MP3 library — no download and no browser-side decodeAudioData, whose ~80 MB PCM
                    // per track is what tips a 3–4 GB tablet into EncodingError memory failures (#1419).
                    if (BrowserDevice.IsMobileBrowser)
                    {
                        settings.ViewDistanceChunks = 3;
                        settings.MusicMode = MusicMode.Synth;
                    }
                }
            }

            bool tokenChanged = false;
            if (string.IsNullOrEmpty(settings.PlayerToken))
            {
                // Both the settings and their backup lost the token (or a fresh install): restore it from
                // its own backup file before ever minting a new one.
                settings.PlayerToken = TryReadTokenBackup();
                tokenChanged = !string.IsNullOrEmpty(settings.PlayerToken);
            }

            if (string.IsNullOrEmpty(settings.PlayerToken))
            {
                settings.PlayerToken = Guid.NewGuid().ToString("N");
                tokenChanged = true;
            }

            if (recovering || tokenChanged)
            {
                settings.Save(); // persist the recovered state / claim secret right away so it survives a crash
            }
            else
            {
                EnsureTokenBackup(settings.PlayerToken); // heal installs that predate the token backup file
            }

            return settings;
        }

        public void Save()
        {
            try
            {
                // Atomic write (#410): a crash mid-write may corrupt only the .tmp file, never the live
                // settings. The previous file survives as .bak — Load's recovery source.
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(this, prettyPrint: true));
                if (File.Exists(FilePath))
                {
                    try
                    {
                        File.Replace(tmp, FilePath, BackupPath);
                    }
                    catch (Exception)
                    {
                        // File.Replace can be unavailable (platform FS quirks) — same net result, just not atomic.
                        File.Copy(FilePath, BackupPath, overwrite: true);
                        File.Delete(FilePath);
                        File.Move(tmp, FilePath);
                    }
                }
                else
                {
                    File.Move(tmp, FilePath);
                }

                EnsureTokenBackup(PlayerToken);
                WebGlStorage.Sync(); // IDBFS writes are in-memory until synced (#1179) — no-op off WebGL
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not save client settings: {e.Message}");
            }
        }

        private static ClientSettings TryReadSettingsFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                return JsonUtility.FromJson<ClientSettings>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read client settings from '{Path.GetFileName(path)}': {e.Message}");
                return null;
            }
        }

        /// <summary>Moves the unreadable settings file aside to <see cref="CorruptPath"/> (keeping only the
        /// latest incident). Moving — not deleting — means the next Save can't shove corrupt content into
        /// the .bak slot, and the player still has the raw file.</summary>
        private static void PreserveCorruptFile()
        {
            try
            {
                if (File.Exists(CorruptPath)) File.Delete(CorruptPath);
                File.Move(FilePath, CorruptPath);
                Debug.LogWarning($"Preserved unreadable client settings as '{Path.GetFileName(CorruptPath)}'.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not preserve the corrupt client settings file: {e.Message}");
            }
        }

        private static string TryReadTokenBackup()
        {
            try
            {
                return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : "";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not read the player-token backup: {e.Message}");
                return "";
            }
        }

        private static void EnsureTokenBackup(string token)
        {
            if (string.IsNullOrEmpty(token)) return;
            try
            {
                if (!File.Exists(TokenPath) || !string.Equals(File.ReadAllText(TokenPath).Trim(), token, StringComparison.Ordinal))
                {
                    // Flush all the way to the platter (#964). A plain WriteAllText only reaches the OS
                    // cache: a power loss or BSOD seconds later can leave the file present but EMPTY, and
                    // an empty token means the player mints a new one and is locked out of their own name
                    // on every server they have ever played on — there is no recovery path for that.
                    using var stream = new FileStream(TokenPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    var bytes = System.Text.Encoding.UTF8.GetBytes(token);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not write the player-token backup: {e.Message}");
            }
        }

        /// <summary>Applies engine-owned settings. View distance feeds <see cref="GameBootstrap"/>; audio buses land later.</summary>
        public void Apply()
        {
            ApplyWindowMode();
            AudioListener.volume = Mathf.Clamp01(MasterVolume); // master bus (M26)
            UiKit.ReducedMotion = ReducedEffects; // UI transitions snap instantly for reduced-effects users
            UiKit.SetUserScale(UiScale);          // HUD canvases + the IMGUI leftovers follow the UI-scale setting

            int levels = QualitySettings.names != null ? QualitySettings.names.Length : 0;
            if (levels > 0)
            {
                QualitySettings.SetQualityLevel(Mathf.Clamp((int)Preset, 0, levels - 1), applyExpensiveChanges: true);
            }

            // Frame pacing — the player's own switch, applied AFTER SetQualityLevel (which stamps the preset's
            // baked vSyncCount). VSync on = sync to the display; off = uncapped unless FrameRateCap limits it.
            // Application.targetFrameRate only takes effect when vSyncCount == 0, so clear it (−1) under VSync.
            QualitySettings.vSyncCount = VSync ? 1 : 0;
            Application.targetFrameRate = (!VSync && FrameRateCap > 0) ? FrameRateCap : -1;

            // URP: one pipeline asset serves every quality level, so scale the expensive part — shadow reach —
            // by preset here (Potato: shadows off entirely; High: the full tuned distance).
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                urp.shadowDistance = Preset switch
                {
                    QualityPreset.Potato => 0f,
                    QualityPreset.Low => 40f,
                    QualityPreset.Medium => 70f,
                    _ => 90f,
                };

                // MSAA / HDR / render scale live in the same single URP asset, so without this they were baked
                // on for EVERY preset (4x MSAA + HDR even on Potato and WebGL). Scale them with the preset:
                // MSAA off on Potato/Low, 2x on Medium, the tuned 4x on High; HDR off on Potato only (bloom/
                // grading fall back to LDR there); Potato additionally renders the 3D view at 75% resolution
                // via URP renderScale — unlike a devicePixelRatio cap this keeps the UI text crisp.
                urp.msaaSampleCount = Preset switch
                {
                    QualityPreset.Potato or QualityPreset.Low => 1,
                    QualityPreset.Medium => 2,
                    _ => 4,
                };
                // On WebGL, Low must actually be low (#1424): the wasm main thread also runs the in-process
                // singleplayer server and all chunk meshing, so Low additionally drops HDR and renders the 3D
                // view at 80 % — between Potato (75 %) and the desktop presets (100 %). Desktop Low is unchanged.
                bool webglLow = Application.platform == RuntimePlatform.WebGLPlayer && Preset == QualityPreset.Low;
                urp.supportsHDR = Preset > QualityPreset.Potato && !webglLow;
                UrpScenePost.Instance?.SetTonemapForHdr(urp.supportsHDR); // LDR gets Neutral, not ACES (#1457)
                urp.renderScale = Preset == QualityPreset.Potato ? 0.75f : webglLow ? 0.8f : 1f;

                // The shared asset bakes a 4096 main-light shadowmap for every level, but only High reaches far
                // enough (90 m) to justify it — Medium and below cover 70 m or less, where 2048 is visually
                // indistinguishable yet halves the shadow render cost. Keep High on 4096, drop the rest to 2048
                // (irrelevant on Potato, which turns shadows off via shadowDistance 0 above).
                urp.mainLightShadowmapResolution = Preset >= QualityPreset.High ? 4096 : 2048;

                // Depth + opaque copies feed the screen-space effects (volumetric fog, SSR, water refraction).
                // They cost a prepass + a colour copy, so the two weakest presets turn them off entirely —
                // which also disables every dependent effect for free (the features early-out without the textures).
                bool wantsScreenSpace = Preset >= QualityPreset.Medium;
                urp.supportsCameraDepthTexture = wantsScreenSpace;
                // #1520: the opaque colour copy (an MSAA resolve + a half-res blit EVERY frame at Medium+) is
                // requested per camera by its consumers instead of by the asset (RequestOpaqueTexture), still
                // gated to the presets that had it. The heat-haze and thermal-vision quads hold a request only
                // while they are visible. #1577: the screen-space WATER (BlockAtlasTransparent, Medium+) is the
                // third consumer — it composites the bed from the opaque copy for refraction, and without the
                // texture it rendered a milky opaque surface — so the water's request is held for as long as the
                // preset enables screen-space water. (A finer gate — only while water is in view — is open.)
                urp.supportsCameraOpaqueTexture = false;
                _opaqueTexturePresetAllows = wantsScreenSpace;
                RequestOpaqueTexture(wantsScreenSpace, ref _waterOpaqueHeld);
                ApplyOpaqueTextureRequest();

                // Tell the shaders whether the depth/opaque textures exist this preset. The water shader uses it to
                // fall back to the simple alpha look on Potato/Low (otherwise its depth colour / refraction / SSR
                // would sample unbound textures and render wrong/black).
                UnityEngine.Shader.SetGlobalFloat("_Sc_ScreenFx", wantsScreenSpace ? 1f : 0f);
            }

            ApplyCameraLook();
        }

        /// <summary>The active gameplay camera's URP data, set by <see cref="WorldRig"/> so graphics changes made in
        /// the pause menu push live (SMAA + the SSAO-renderer choice). Null in the main menu (no world camera yet);
        /// the camera reads the settings on creation, so menu changes still apply on entry. Static (not serialized).</summary>
        public static UniversalAdditionalCameraData ActiveCameraData
        {
            get => _activeCameraData;
            set
            {
                _activeCameraData = value;
                ApplyOpaqueTextureRequest();
            }
        }

        private static UniversalAdditionalCameraData _activeCameraData;
        private static bool _opaqueTexturePresetAllows;
        private static int _opaqueTextureRequests;
        private static bool _waterOpaqueHeld; // #1577: the water shader's standing request at Medium+

        /// <summary>#1520: a screen-space effect that samples <c>_CameraOpaqueTexture</c> (heat haze, thermal
        /// vision) holds a request while it is visible; the camera copies the opaque colour only while at least
        /// one request is open — and only on the presets that had the copy before (Medium+). Balanced on/off
        /// calls per requester; idempotent for repeated calls with the same state.</summary>
        public static void RequestOpaqueTexture(bool on, ref bool held)
        {
            if (on == held)
            {
                return;
            }

            held = on;
            _opaqueTextureRequests += on ? 1 : -1;
            if (_opaqueTextureRequests < 0)
            {
                _opaqueTextureRequests = 0;
            }

            ApplyOpaqueTextureRequest();
        }

        private static void ApplyOpaqueTextureRequest()
        {
            if (_activeCameraData == null)
            {
                return;
            }

            bool want = _opaqueTexturePresetAllows && _opaqueTextureRequests > 0;
            var option = want ? CameraOverrideOption.On : CameraOverrideOption.Off;
            if (_activeCameraData.requiresColorOption != option)
            {
                _activeCameraData.requiresColorOption = option;
            }
        }

        /// <summary>Pushes the per-camera look settings to the gameplay camera: post-processing on (the global
        /// Volume — bloom/tonemap/grade — and SMAA both need it), SMAA from <see cref="Smaa"/> (Medium+), and the
        /// renderer choice — index 0 = full-res SSAO (High), index 2 = half-res SSAO (Medium), index 1 = SSAO-free
        /// (Potato/Low). SSAO was the measured Low→Medium frame-time cliff (#374).</summary>
        public void ApplyCameraLook() => ApplyCameraLook(ActiveCameraData);

        /// <summary>Same as <see cref="ApplyCameraLook()"/> for an explicit camera — the shell scenes
        /// (intro cinematic, menu backdrop) pass their own camera data, which exists before
        /// <see cref="ActiveCameraData"/> is ever assigned (#1421).</summary>
        public void ApplyCameraLook(UniversalAdditionalCameraData cd)
        {
            if (cd == null)
            {
                return;
            }

            cd.renderPostProcessing = true;
            // Renderer index carries the SSAO cost tier: 0 = full-resolution SSAO (High), 2 = half-resolution
            // SSAO (Medium — Downsample renderer), 1 = SSAO-free (Potato/Low). Measured (#374): SSAO was the
            // whole Low→Medium frame-time cliff on the reference laptop, so Medium keeps ambient occlusion but
            // at half resolution — most of the look, ~half the cost — while High stays untouched at full res.
            cd.SetRenderer(Preset switch
            {
                QualityPreset.High => 0,
                QualityPreset.Medium => 2,
                _ => 1,
            });

            bool smaa = Smaa && Preset >= QualityPreset.Medium;
            cd.antialiasing = smaa ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
            cd.antialiasingQuality = AntialiasingQuality.High;
        }

        /// <summary>Applies the chosen <see cref="Window"/> mode. Windowed uses the persisted
        /// <see cref="WindowedWidth"/>/<see cref="WindowedHeight"/> (clamped to the display) so the window has a
        /// title bar and can be dragged to another monitor and maximized; Borderless/Exclusive fill the current
        /// display at its native resolution. The standalone window must be resizable (Player Settings) for the
        /// OS maximize/resize affordances to appear in Windowed mode.</summary>
        private void ApplyWindowMode()
        {
            // WebGL has no window to manage: the template owns the canvas and keeps the render target
            // matched to the DOM size (index.html). Screen.SetResolution here fought that by forcing the
            // backing store to Screen.currentResolution — wrong size AND wrong owner on tablets (#1420).
            if (Application.platform == RuntimePlatform.WebGLPlayer)
            {
                return;
            }

            var native = Screen.currentResolution;
            switch (Window)
            {
                case WindowMode.Borderless:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.FullScreenWindow);
                    break;
                case WindowMode.Exclusive:
                    Screen.SetResolution(native.width, native.height, FullScreenMode.ExclusiveFullScreen);
                    break;
                default: // Windowed
                    int w = Mathf.Clamp(WindowedWidth, 640, Mathf.Max(640, native.width));
                    int h = Mathf.Clamp(WindowedHeight, 480, Mathf.Max(480, native.height));
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    break;
            }
        }

        // ---- Per-player mute (#1209) ------------------------------------------------------------
        // One list, two keys. The server stamps a stable player id on chat lines and voice frames, but a
        // player only ever SEES a display name — so an entry may be either, and both are matched. Names are
        // compared case-insensitively; ids are opaque and compared exactly.

        /// <summary>Whether lines/voice from this player should be hidden. Either argument may be empty.</summary>
        public bool IsMuted(string playerId, string displayName)
        {
            if (MutedPlayers.Count == 0)
            {
                return false;
            }

            foreach (var entry in MutedPlayers)
            {
                if (!string.IsNullOrEmpty(playerId) && entry == playerId)
                {
                    return true;
                }

                if (!string.IsNullOrEmpty(displayName)
                    && string.Equals(entry, displayName, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Adds a player to the mute list. Returns false when they were already on it.</summary>
        public bool Mute(string who)
        {
            if (string.IsNullOrWhiteSpace(who) || IsMuted(who, who))
            {
                return false;
            }

            MutedPlayers.Add(who.Trim());
            return true;
        }

        /// <summary>Removes every entry matching a player (id or name). Returns false when none did.</summary>
        public bool Unmute(string who)
        {
            if (string.IsNullOrWhiteSpace(who))
            {
                return false;
            }

            string t = who.Trim();
            int removed = MutedPlayers.RemoveAll(e =>
                e == t || string.Equals(e, t, System.StringComparison.OrdinalIgnoreCase));
            return removed > 0;
        }
    }
}
