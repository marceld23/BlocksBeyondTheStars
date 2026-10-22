// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Build;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Shared.Feedback;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Player feedback ("Spieler Feedback"): the <see cref="Hotkey"/> (F1 on desktop, F2 in browsers — F1 is the
    /// browser's own help key) opens a modal dialog where any player can send a bug report OR a feature wish — one
    /// form, no type distinction: a title, a description, an optional e-mail and a short note that game data + a
    /// screenshot are attached. (The key is advertised in the on-foot HUD controls hint and in the space-flight
    /// cruise hint via the <c>{feedback_key}</c> placeholder; it works in both modes.)
    ///
    /// On send we grab a full-frame screenshot WITH the HUD but WITHOUT this dialog (captured at the moment the
    /// dialog opens, while the live HUD is still on screen), gather a small client-side diagnostic snapshot,
    /// and fire BOTH paths:
    ///   • a client-direct HTTPS POST to the report inbox (<see cref="FeedbackUploader"/>; UnityWebRequest on
    ///     WebGL, where HttpClient/threads don't exist) — reaches the devs on any server, even someone else's
    ///     dedicated server;
    ///   • the existing <c>/bump</c> message (<see cref="NetworkClient.SendBumpReport"/>) so the server also
    ///     writes its rich local snapshot (inventory/position/surroundings) when on an own/singleplayer server —
    ///     carrying the same reply key as the direct upload, so the inbox can pair the two rows (#1359).
    ///
    /// While the dialog is open the world is HELD exactly like behind the Esc menu (#1330): the same server intent
    /// (<see cref="NetworkClient.SendPause"/>, group decision per #973 — a lone writer in multiplayer pauses nobody
    /// else) with the same keep-alive, so typing a report in singleplayer no longer costs hunger, daylight or a
    /// creature sneaking up behind the form. The screenshot is taken BEFORE the hold, so it still shows live play.
    ///
    /// <b>Reply inbox (#1328).</b> The same component is the receiving end of the channel: every report carries a
    /// one-way <c>replyKey</c> (<see cref="FeedbackReplyKey"/>), successfully sent reports are remembered in
    /// <see cref="SentReportsLog"/>, and — ONLY while that memory holds a recent report — the client asks the inbox
    /// for developer answers shortly after the world loads and every <see cref="PollIntervalSeconds"/>. An answer
    /// shows as a HUD line plus a modal with the thread; a developer <i>question</i> adds an input so the player
    /// can answer from inside the game. Shown entries are acknowledged so they appear once.
    ///
    /// Wired by <see cref="WorldRig"/> next to <see cref="HudUi"/> / <see cref="ChatUi"/>.
    ///
    /// <b>Shell mode (#2523).</b> <see cref="AppShell"/> adds a second, lean instance to itself with <see cref="Shell"/>
    /// set instead of <see cref="Game"/>, so the hotkey also works in the main menu and the menu screens around it
    /// (<see cref="IsShellFeedbackScreen"/>) — a tester found a bug while creating a world and had nowhere to report
    /// it. It sends title, description and optional e-mail plus a screenshot of the menu as the same client-direct
    /// upload, under the same reply credential, but there is no world: no <c>/bump</c> snapshot, no world hold, no
    /// game stats in the report. It shows developer replies in the menu too; the world's own instance polls
    /// separately once a world is loaded (the shell one sleeps then), and both read the same
    /// <see cref="SentReportsLog"/>. The in-world instance is unchanged.
    /// </summary>
    public sealed class FeedbackUi : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        /// <summary>Set on the shell's own instance (#2523) — and only there; the world rig sets <see cref="Game"/>.</summary>
        public AppShell Shell;

        /// <summary>The shell instance: no world behind it, so no game data, no <c>/bump</c> and no world hold.</summary>
        private bool ShellMode => Game == null && Shell != null;

        // The shell instance's modal state, for AppShell's own cancel key and pad focus (#2523).
        private static FeedbackUi _shellInstance;
        private static int _shellCancelFrame = -1;
        private object _builtWithLocalizer; // shell mode: the Localizer the dialogs were built with

        private const float W = 1920f, H = 1080f;

        /// <summary>How often the inbox is asked for replies while playing (only with recent sent reports).</summary>
        public const float PollIntervalSeconds = 600f;

        /// <summary>Delay before the first poll after the world loads — keep the startup network quiet.</summary>
        public const float FirstPollDelaySeconds = 12f;

        private FeedbackUploader _uploader;
        private FeedbackSpool _spool;
        private FeedbackReplyClient _replies;
        private SentReportsLog _sentLog;
        private string _replyKey = string.Empty;
        private string _sessionId = string.Empty;
        private string _pendingJson;  // the in-flight dialog send's body, spooled if the upload fails
        private string _pendingTitle; // the title of that send, remembered next to the inbox id on success

        // Dialog (built lazily on first open).
        private Canvas _dialogCanvas;
        private GameObject _dialog;
        private InputField _titleInput, _descInput, _emailInput;
        private Text _status;
        private Text _descCounter;
        private bool _limitWarned; // the "text reached the limit" hint was shown; the next Send goes through

        /// <summary>Description and answer length cap. Stays below the inbox's 5000-character description limit even
        /// with the /bump twin's "[feedback] " + 80-character title + " — " prefix, so the two rows of one report
        /// still contain each other and pair up in the inbox. Before 2026-09 it was 1500 and cut long idea texts
        /// silently mid-sentence.</summary>
        public const int DescLimit = 4800;
        private Button _sendBtn, _cancelBtn;

        private bool _open;
        private bool _sending;
        private byte[] _shotJpg;                 // screenshot captured when the dialog opened
        private Task<FeedbackUploadResult> _uploadTask;

        // The dialog's and the reply's "hold the world while I'm open" requests go through the world's hold arbiter
        // (#2461), each under its own name: closing one of them — or the Esc menu — no longer releases the hold the
        // other still needs. The arbiter owns the 15 s keep-alive the server sweeps dead clients by (#973).
        private void HoldWorld(string holder) => Game?.WorldHold.Hold(holder, Time.realtimeSinceStartup);

        private void ReleaseWorld(string holder) => Game?.WorldHold.Release(holder);

        // Reply inbox state.
        private float _nextPollAt = -1f;         // < 0 = polling off (no API key)
        private bool _polling;                   // a poll request is in flight (task or WebGL coroutine)
        private Task<FeedbackReplyResult> _pollTask;
        private Task<FeedbackReplyResult> _answerTask;
        private readonly Queue<FeedbackReplyThread> _inbox = new Queue<FeedbackReplyThread>();
        private readonly FeedbackReplyTracker _inboxTracker = new FeedbackReplyTracker(); // by reply id (#1351)
        private readonly object _replyOwner = new object(); // its own menu owner — independent of the F1 dialog

        // Reply window (built lazily). The thread body is a scrollable stack of UiTextChunks pieces (#1368):
        // developer answers and the player's own replies are unbounded data-driven text, and one uGUI Text
        // silently drops its mesh past ~16k glyphs (#1097) — the old 1400-character cut + Truncate hid the
        // rest of a long thread with no hint that there was more.
        private Canvas _replyCanvas;
        private GameObject _replyOverlay;
        private Text _replyTitle, _replyStatus, _answerLabel;
        private ScrollRect _replyBodyScroll;
        private RectTransform _replyBodyContent;
        private readonly List<Text> _replyBodyChunks = new List<Text>();
        private float _replyBodyW; // viewport width the chunk Texts wrap at (set when the scroll is built)
        private const float ReplyBodyH = 296f, ReplyBodyScrollbarW = 12f, ReplyBodyGutter = 8f;
        private InputField _answerInput;
        private Button _replyOkBtn, _replyAnswerBtn;
        private FeedbackReplyThread _shown;
        private bool _replyOpen;
        private bool _answering;

        /// <summary>The key that opens the feedback dialog: F2 in browser builds (F1 would fight the browser's
        /// own help shortcut), F1 everywhere else.</summary>
        public static KeyCode Hotkey =>
            Application.platform == RuntimePlatform.WebGLPlayer ? KeyCode.F2 : KeyCode.F1;

        /// <summary>Player-visible name of <see cref="Hotkey"/> — substituted for the <c>{feedback_key}</c>
        /// placeholder in locale hint strings.</summary>
        public static string HotkeyName => Hotkey == KeyCode.F2 ? "F2" : "F1";

        private string L(string key)
        {
            if (Game != null)
            {
                return Game.Localizer?.Get(key) ?? key;
            }

            return Shell != null ? Shell.L(key) : key;
        }

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static string SentLogPath => Path.Combine(AppPaths.Root, "feedback", "sent.json");

        /// <summary>The sent-reports memory. <see cref="SentReportsLog"/> caches its file on first use, which suits the
        /// per-world instance; the shell instance lives as long as the app while the world's instance writes the same
        /// file in between, so it reads the file fresh each time (as <see cref="TextureSubmitDialog"/> does).</summary>
        private SentReportsLog SentLog => ShellMode ? new SentReportsLog(SentLogPath) : _sentLog;

        /// <summary>True while the shell instance's feedback dialog or reply window is on screen (#2523) — AppShell then
        /// suspends the menu screen's pad focus.</summary>
        public static bool ShellModalOpen => _shellInstance != null && (_shellInstance._open || _shellInstance._replyOpen);

        /// <summary>True while the shell dialog owns the cancel key — including the frame it closed in, so the same
        /// Esc / pad B press does not also back AppShell out of the screen underneath, whichever Update runs first.</summary>
        public static bool ShellOwnsCancel => ShellModalOpen || _shellCancelFrame == Time.frameCount;

        /// <summary>The shell screens the menu form opens on (#2523): the main menu and the plain menu screens around it.
        /// Not splash, intro or loading (the hand-off into a world must not wait on a form), and not the editors,
        /// whose own keys and canvases own the screen. A loaded world is excluded separately — Settings opened from
        /// the pause menu belongs to the world.</summary>
        public static bool IsShellFeedbackScreen(ShellPhase phase) =>
            phase == ShellPhase.MainMenu || phase == ShellPhase.SaveSelect || phase == ShellPhase.Settings
            || phase == ShellPhase.Credits || phase == ShellPhase.Editors;

        private void Start()
        {
            // A random id groups several reports from one sitting (varies per session; no Unity-restricted Date use).
            _sessionId = Guid.NewGuid().ToString("N");
            _uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);
            _spool = new FeedbackSpool(Path.Combine(AppPaths.Root, "feedback"));
            _replies = new FeedbackReplyClient(FeedbackReplyClient.EndpointFor(FeedbackUploader.DefaultEndpoint), BugReportBuildSecrets.ApiKey);
            _sentLog = new SentReportsLog(SentLogPath);
            _replyKey = ComputeReplyKey();
            if (ShellMode)
            {
                _shellInstance = this;
            }

            if (_uploader.IsConfigured)
            {
                // The spool is the world instance's to flush: two instances draining it at once could send a queued
                // report twice, and the shell's queued reports go out with the next world session.
                if (!ShellMode)
                {
                    FlushSpool(); // deliver what an earlier session couldn't (bounded attempts, see FeedbackSpool)
                }

                _nextPollAt = Time.unscaledTime + FirstPollDelaySeconds;
            }
        }

        /// <summary>
        /// Both dialogs are TOP-LEVEL canvases (UiKit.CreateCanvas parents nothing), while this component lives on
        /// the world rig root — so they outlived the rig (#1789). A reply overlay open while the world was torn down
        /// (ReturnToMenu) stayed on screen in the main menu; its OK button then ran <see cref="AcknowledgeAndClose"/>
        /// on the destroyed component and <c>CancelInvoke</c> threw <c>ArgumentNullException: self</c>. And every
        /// world join leaked two hidden canvases. Same treatment as <c>ChatUi.OnDestroy</c>.
        /// </summary>
        private void OnDestroy()
        {
            if (_open || _replyOpen)
            {
                // Balance the hold taken by Open/ShowThread; the network is gone by now, so the pause message is a
                // no-op, but the local counter must not carry into the next world.
                _open = false;
                _replyOpen = false;
                ReleaseWorld(WorldHoldArbiter.Feedback);
                ReleaseWorld(WorldHoldArbiter.FeedbackReply);
            }

            if (_dialogCanvas != null)
            {
                Destroy(_dialogCanvas.gameObject);
                _dialogCanvas = null;
            }

            if (_replyCanvas != null)
            {
                Destroy(_replyCanvas.gameObject);
                _replyCanvas = null;
            }

            if (_shellInstance == this)
            {
                _shellInstance = null;
            }
        }

        /// <summary>The install's reply-thread credential: a one-way hash of the install secret. Desktop and
        /// play.* builds hash the name-claim token (stable per install — the <c>/play/</c> path never changes);
        /// the glitch.fun arcade hashes the Glitch install id instead, because the browser-local token there
        /// resets with every deployment (#1177) while the install id follows the player across deployments
        /// and browsers. Whoever learns the key can read replies — never claim a name.</summary>
        private string ComputeReplyKey() => ReplyKeyFor(Settings);

        /// <summary>The same credential for senders outside a world — the texture editor's submit dialog (#1965)
        /// runs in the main menu, and the answer must arrive in the thread this component polls later.</summary>
        public static string ReplyKeyFor(ClientSettings settings)
        {
            string secret = GlitchIntegration.ArcadeInstallId; // empty everywhere except the arcade
            if (string.IsNullOrEmpty(secret) && settings != null)
            {
                secret = settings.PlayerToken;
            }

            return FeedbackReplyKey.Derive(secret);
        }

        private void Update()
        {
            if (Game == null)
            {
                if (Shell != null)
                {
                    UpdateShell(); // the menu's own instance (#2523); the world rig's instance never has a Shell
                }

                return;
            }

            // The hotkey opens feedback during normal play — on foot AND in space flight (both HUDs advertise
            // it); not while a menu/chat modal or the death prompt already owns the screen — nor under a cutscene,
            // whose frame would cover the dialog (#2461; skipping it is one key away).
            bool canLaunch = !Game.MenuOpen && !Game.ChatTyping && !Game.AwaitingRespawnConfirm && !GameBootstrap.CutsceneActive;

            if (!_open && canLaunch && Input.GetKeyDown(Hotkey))
            {
                Open();
            }

            if (_open && !_sending && Input.GetKeyDown(KeyCode.Escape))
            {
                Game.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }

            // The keep-alive that tells the server we are still here while the dialog holds the world runs in the
            // world's hold arbiter (GameBootstrap.WorldHold, #2461).

            PumpUploadResult();
            TickReplyInbox(canLaunch);
        }

        /// <summary>
        /// The shell instance's frame (#2523): the hotkey on the menu screens, Esc / pad B to close, the upload result
        /// and the reply inbox. It sleeps — and closes whatever it had open — while a world is loaded or a phase that is
        /// no menu screen owns the display (splash, loading, the editors); the world's own instance takes over there.
        /// </summary>
        private void UpdateShell()
        {
            bool onMenuScreen = Shell.CurrentBoot == null && IsShellFeedbackScreen(Shell.Phase) && Shell.Localizer != null;
            if (!onMenuScreen)
            {
                if (_open)
                {
                    Close();
                }

                if (_replyOpen)
                {
                    CloseReply(); // not acknowledged, so the world's own instance shows the thread again
                }

                // Threads still waiting here are the world instance's to show now; back in the menu they would only
                // repeat what it already showed and acknowledged.
                _inbox.Clear();

                if (_nextPollAt >= 0f)
                {
                    // The world's instance polls while a world is loaded; this one resumes a little after the menu is back.
                    _nextPollAt = Mathf.Max(_nextPollAt, Time.unscaledTime + FirstPollDelaySeconds);
                }
            }

            // Not while the player types (a world name, a server address — F1 there belongs to the field), not while
            // Settings waits for "press a key" (F1 may be the key being bound), and not over the other window.
            bool canLaunch = onMenuScreen && !UiKit.TextFieldFocused() && !UiSettings.CapturingKey;

            if (!_open && !_replyOpen && canLaunch && Input.GetKeyDown(Hotkey))
            {
                Open();
            }

            if (_open && !_sending && CancelPressed())
            {
                Close(); // stamps the cancel frame, so AppShell does not also leave the screen underneath on this press
            }

            PumpUploadResult();
            TickReplyInbox(canLaunch);
        }

        /// <summary>The key that closes the dialog and the reply window: Escape in a world (pad B is crouch there),
        /// the menu's cancel — Escape or pad B — in the shell.</summary>
        private bool CancelPressed() => ShellMode ? InputMap.Down(InputAction.UiCancel) : Input.GetKeyDown(KeyCode.Escape);

        /// <summary>Marshals the background upload result back onto the main thread.</summary>
        private void PumpUploadResult()
        {
            if (_uploadTask != null && _uploadTask.IsCompleted)
            {
                var task = _uploadTask;
                _uploadTask = null;
                FeedbackUploadResult result;
                try { result = task.Result; }
                catch (Exception e) { result = new FeedbackUploadResult { Error = e.GetType().Name }; }
                OnUploadFinished(result);
            }
        }

        // --- Open / close ----------------------------------------------------------------------------------

        /// <summary>Opens the feedback dialog (the <see cref="Hotkey"/>'s target). Captures the gameplay frame first — HUD
        /// visible, dialog not yet shown — then dims the screen and shows the form.</summary>
        public void Open()
        {
            if (_open || (Game == null && !ShellMode))
            {
                return;
            }

            _open = true;

            // Modal: free the cursor + pause player/flight control (mirrors GameMenu / BeamPadUi; SpaceView
            // holds position while MenuOpen). Registered HERE, on the frame the hotkey fires, not at the end of
            // it with the screenshot (#1858): the gap between _open and the owner flag was a frame in which
            // every gameplay verb still read its key. The arbiter recomputes on close, so a flight sub-screen
            // the dialog opened over (e.g. the landing-pad chooser) keeps its free cursor without us having to
            // save/restore the prior state by hand (#413).
            //
            // #2622: but only as a HUD-level owner until the screenshot is taken. A screen-level owner hides the HUD —
            // HudUi decides that in its LateUpdate, which runs after every Update of this very frame, so the old comment's
            // "HudUi has already updated" never held: since #1858 every F1 shot came without crosshair, hotbar, vitals
            // and prompts (only VEGA's chip, on its own canvas), and the menu blur came on with it. A HUD-level owner
            // pauses control all the same but leaves ScreenMenuOpen — the HUD and the blur — alone; the dialog takes the
            // screen over the moment the frame is copied (TakeOverScreen).
            //
            // The shell instance (#2523) has no world and no menu-owner arbiter: its modality is the dialog's own scrim,
            // ShellOwnsCancel for AppShell's cancel key and ShellModalOpen for the menu's pad focus.
            Game?.SetHudMenuOwner(this, true);
            StartCoroutine(OpenRoutine());
        }

        /// <summary>#2622: the screenshot holds the frame the player saw — now the dialog becomes a screen-level menu owner
        /// (the HUD steps aside, the blur comes on) instead of the HUD-level one <see cref="Open"/> registered.</summary>
        private void TakeOverScreen()
        {
            if (!_open || Game == null)
            {
                return;
            }

            Game.SetHudMenuOwner(this, false); // leaves the HUD-level set (and the owner set) …
            Game.SetMenuOwner(this, true);     // … and owns the screen: the same frame, nothing runs in between
        }

        private IEnumerator OpenRoutine()
        {
            // Capture at end of frame, before the dialog is shown: the shot is the full frame WITH the HUD but
            // WITHOUT this dialog (the requested look).
            yield return new WaitForEndOfFrame();
            if (!_open)
            {
                // Closed in the very frame it was opened (the hotkey and Esc together): Close() already ran, so
                // building the dialog now would show it with _open == false — holding the world with no Esc
                // able to close it (#1368). Nothing to show, nothing to hold.
                yield break;
            }

            // Full frame with the HUD, downscaled on the GPU, read back through GpuReadback (#2390: WebGPU has no
            // synchronous reads, so on the browser the dialog appears a frame or two after the hotkey). #2622: the dialog
            // owns the screen from the moment the frame is copied, before the readback's extra frames.
            Texture2D shot = null;
            yield return GpuReadback.CaptureScreen(1600, TextureFormat.RGB24, t => shot = t, TakeOverScreen);
            if (!_open)
            {
                if (shot != null) { Destroy(shot); }
                yield break;
            }

            _shotJpg = EncodeJpg(shot);

            if (ShellMode)
            {
                RebuildIfLanguageChanged();
            }

            EnsureDialog();
            ResetFields();
            _dialog.SetActive(true);
            UiKit.OpenModal(_dialog); // #2302: the scrim fades in place, the form (the scrim's first child) rises

            // Hold the world like the Esc menu does (#1330) — after the screenshot, so the shot shows live play.
            // The server decides what it means (#973): alone, the world stops right here; with others joined it
            // only counts as "this player is in a menu" until everyone else is too.
            HoldWorld(WorldHoldArbiter.Feedback);
        }

        private void Close()
        {
            CancelInvoke(nameof(Close));
            _open = false;
            _sending = false;
            _shotJpg = null;
            ReleaseInputFocus(_titleInput, _descInput, _emailInput);
            if (_dialog != null) _dialog.SetActive(false);

            ReleaseWorld(WorldHoldArbiter.Feedback); // every close path ends here (Esc, Cancel, the auto-close after a send)
            // Arbiter re-locks only once NO other owner is open (#413). #2622: through the HUD-level call, which leaves both
            // sets — a dialog closed before its screenshot (hotkey and Esc together) is still a HUD-level owner.
            Game?.SetHudMenuOwner(this, false);
            if (ShellMode)
            {
                _shellCancelFrame = Time.frameCount;
            }
        }

        /// <summary>
        /// Shell mode: the dialogs are built once, but the shell instance lives through language changes in Settings,
        /// each of which makes a new Localizer — so a dialog built in the old language is dropped and rebuilt. Called
        /// only right before a window is shown, while it is still hidden. (The world's instance dies with its world.)
        /// </summary>
        private void RebuildIfLanguageChanged()
        {
            object current = Shell != null ? Shell.Localizer : null;
            if (ReferenceEquals(current, _builtWithLocalizer))
            {
                return;
            }

            _builtWithLocalizer = current;
            if (_dialogCanvas != null && (_dialog == null || !_dialog.activeSelf))
            {
                Destroy(_dialogCanvas.gameObject);
                _dialogCanvas = null;
                _dialog = null; // Destroy is deferred to the end of the frame — EnsureDialog must build anew right now
            }

            if (_replyCanvas != null && !_replyOpen)
            {
                Destroy(_replyCanvas.gameObject);
                _replyCanvas = null;
                _replyOverlay = null;
            }
        }

        /// <summary>
        /// Hands focus back before a dialog is hidden (#1683). uGUI never deselects a deactivated
        /// <see cref="InputField"/> on its own: the caret keeps its place in the canvas rebuild queue, and
        /// rebuilding it after the field's own objects have gone throws inside <c>InputField.GenerateCaret</c>
        /// — a client crash report arrived from exactly that window, seconds after a send auto-closed the
        /// dialog while the reply overlay was opening its own field. Same treatment the chat box got in #1634.
        /// </summary>
        private static void ReleaseInputFocus(params InputField[] fields)
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            foreach (var field in fields)
            {
                if (field == null)
                {
                    continue;
                }

                if (field.isFocused)
                {
                    field.DeactivateInputField();
                }

                if (es != null && es.currentSelectedGameObject == field.gameObject)
                {
                    es.SetSelectedGameObject(null);
                }
            }
        }

        private void ResetFields()
        {
            if (_titleInput != null) _titleInput.text = string.Empty;
            if (_descInput != null) _descInput.text = string.Empty;
            if (_emailInput != null) _emailInput.text = string.Empty;
            if (_status != null) { _status.text = string.Empty; _status.color = UiKit.CyanDim; }
            _limitWarned = false;
            UpdateDescCounter(string.Empty);
            SetSendInteractable(true);
        }

        /// <summary>Live "used / limit" count next to the description label, in warning colour from 90 % on — uGUI
        /// simply stops accepting keys (and pasted text) at the limit, which a player could not see before.</summary>
        private void UpdateDescCounter(string text)
        {
            int used = text != null ? text.Length : 0;
            if (used < DescLimit)
            {
                _limitWarned = false;
            }

            if (_descCounter == null)
            {
                return;
            }

            _descCounter.text = used.ToString(System.Globalization.CultureInfo.InvariantCulture) + " / "
                + DescLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _descCounter.color = used >= DescLimit * 9 / 10 ? UiKit.Warn : UiKit.CyanDim;
        }

        private void SetSendInteractable(bool on)
        {
            if (_sendBtn != null) _sendBtn.interactable = on;
        }

        // --- Dialog construction ---------------------------------------------------------------------------

        private void EnsureDialog()
        {
            if (_dialog != null)
            {
                return;
            }

            _dialogCanvas = UiKit.CreateCanvas("FeedbackDialog");
            _dialogCanvas.sortingOrder = 60; // above the HUD and the in-game menu
            UiNav.Enable(_dialogCanvas.gameObject); // gamepad can drive the dialog (inert on KB/mouse)
            var dim = UiKit.AddModalDim(_dialogCanvas.transform);
            _dialog = dim.gameObject;

            const float pw = 760f, ph = 648f;
            var panel = UiKit.AddDialogPanel(_dialog.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph);
            const float m = 36f, innerW = pw - 2f * m;

            UiKit.AddText(panel, m, 22, innerW, 34, L("ui.feedback.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);

            UiKit.AddText(panel, m, 74, innerW, 20, L("ui.feedback.title_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _titleInput = UiKit.AddInput(panel, m, 96, innerW, 40, string.Empty, null, L("ui.feedback.title_placeholder"), 80);

            UiKit.AddText(panel, m, 146, innerW, 20, L("ui.feedback.desc_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _descCounter = UiKit.AddText(panel, m, 146, innerW, 20, string.Empty, 14, UiKit.CyanDim, TextAnchor.MiddleRight);
            _descInput = UiKit.AddInput(panel, m, 168, innerW, 150, string.Empty, UpdateDescCounter, L("ui.feedback.desc_placeholder"), DescLimit);
            UpdateDescCounter(string.Empty);
            _descInput.lineType = InputField.LineType.MultiLineNewline;
            if (_descInput.textComponent != null) _descInput.textComponent.alignment = TextAnchor.UpperLeft;

            UiKit.AddText(panel, m, 330, innerW, 20, L("ui.feedback.email_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _emailInput = UiKit.AddInput(panel, m, 352, innerW, 40, string.Empty, null, L("ui.feedback.email_placeholder"), 120);

            // The menu form sends no game data — its note says what it does send (#2523).
            var hint = UiKit.AddText(panel, m, 404, innerW, 116, L(ShellMode ? "ui.feedback.hint_menu" : "ui.feedback.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Truncate;

            _status = UiKit.AddText(panel, m, 524, innerW, 24, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);

            _sendBtn = UiKit.AddButton(panel, m, 560, 330, 56, L("ui.feedback.send"), OnSendClicked, "btn_feedback");
            _cancelBtn = UiKit.AddButton(panel, m + 358, 560, 330, 56, L("ui.menu.back"), Close, "btn_exit");

            _dialog.SetActive(false);
        }

        // --- Send ------------------------------------------------------------------------------------------

        private void OnSendClicked()
        {
            if (_sending)
            {
                return;
            }

            string raw = _descInput != null ? _descInput.text ?? string.Empty : string.Empty;
            string desc = raw.Trim();
            if (desc.Length < 3)
            {
                if (_status != null) { _status.text = L("ui.feedback.need_text"); _status.color = UiKit.Warn; }
                return;
            }

            // A text that filled the field to the brim was most likely cut (a long paste stops silently at the
            // limit): say so once, the next click sends as it is.
            if (raw.Length >= DescLimit && !_limitWarned)
            {
                _limitWarned = true;
                if (_status != null) { _status.text = L("ui.feedback.limit_reached"); _status.color = UiKit.Warn; }
                return;
            }

            _sending = true;
            SetSendInteractable(false);
            if (_status != null) { _status.text = L("ui.feedback.sending"); _status.color = UiKit.CyanDim; }

            // Build the report on the MAIN thread (Unity APIs must not run off-thread); only the HTTP POST is
            // backgrounded.
            string title = _titleInput != null ? (_titleInput.text ?? string.Empty).Trim() : string.Empty;
            string email = _emailInput != null ? (_emailInput.text ?? string.Empty).Trim() : string.Empty;
            var report = BuildReport(title, desc, email);
            byte[] jpg = _shotJpg;

            // Path A — rich server snapshot via the existing /bump pipeline (meaningful on own/SP servers). The
            // reply key rides along (#1359) so the server's forwarded row carries the same thread credential as
            // the direct upload below: the inbox pairs the two rows by it, and an answer on either one reaches us.
            string serverNote = string.IsNullOrEmpty(title) ? desc : title + " — " + desc;
            Game?.Network?.SendBumpReport("[feedback] " + serverNote, jpg ?? Array.Empty<byte>(), AppShell.Version, _replyKey);

            // Path B — client-direct upload to the report inbox. The body is serialized ONCE here on the
            // main thread; only the POST leaves the game loop.
            if (_uploader != null && _uploader.IsConfigured)
            {
                string json = FeedbackUploader.Serialize(report, jpg);
                _pendingJson = json;
                _pendingTitle = string.IsNullOrEmpty(title) ? Shorten(desc, 60) : title;
#if UNITY_WEBGL && !UNITY_EDITOR
                // WASM has neither sockets nor threads — HttpClient/Task.Run can't run in the browser, so
                // the WebGL player posts the identical body via UnityWebRequest on a coroutine.
                StartCoroutine(FeedbackWebGlTransport.PostJson(json, OnUploadFinished));
#else
                _uploadTask = Task.Run(() => _uploader.UploadRawJson(json));
#endif
            }
            else if (ShellMode)
            {
                // A build without the inbox key (a local or fork build) — and in the menu there is no world to keep a
                // /bump snapshot either, so nothing went anywhere. Say so instead of a reassuring "saved".
                if (_status != null) { _status.text = L("ui.feedback.not_configured"); _status.color = UiKit.Warn; }
                _sending = false;
            }
            else
            {
                // Dev build without an API key: the local /bump snapshot was still written.
                if (_status != null) { _status.text = L("ui.feedback.sent_local"); _status.color = UiKit.Ok; }
                Game?.ShowMessage(L("ui.feedback.sent_local"));
                _sending = false;
                Invoke(nameof(Close), 1.4f);
            }
        }

        private void OnUploadFinished(FeedbackUploadResult result)
        {
            _sending = false;
            string body = _pendingJson;
            string title = _pendingTitle ?? string.Empty;
            _pendingJson = null;
            _pendingTitle = null;

            if (result != null && result.Ok)
            {
                if (_status != null) { _status.text = L("ui.feedback.sent"); _status.color = UiKit.Ok; }
                Game?.ShowMessage(L("ui.feedback.sent"));
                RememberSent(result.ReportId, title);
                Invoke(nameof(Close), 1.2f);
            }
            else if (result != null && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                // The inbox actively rejected this body (bad key, validation, too large) — retrying the identical
                // payload can never succeed, so don't spool it and don't show a reassuring "queued".
                if (_status != null) { _status.text = L("ui.feedback.failed"); _status.color = UiKit.Warn; }
                SetSendInteractable(true);
            }
            else if (_spool != null && !string.IsNullOrEmpty(body) && _spool.Write(body) != null)
            {
                // Offline / inbox down: the report is queued on disk and retried on later sessions with
                // bounded attempts — nothing to re-type, so tell the player and close.
                WebGlStorage.Sync(); // browser: the queued report must survive a reload (no-op elsewhere)
                if (_status != null) { _status.text = L("ui.feedback.queued"); _status.color = UiKit.Ok; }
                Game?.ShowMessage(L("ui.feedback.queued"));
                Invoke(nameof(Close), 1.6f);
            }
            else
            {
                if (_status != null) { _status.text = L("ui.feedback.failed"); _status.color = UiKit.Warn; }
                SetSendInteractable(true); // allow a retry
            }
        }

        /// <summary>Remembers an accepted report so the reply poll has a reason to run (#1328). The inbox id is
        /// what the poll's threads are keyed by in the UI; a missing id (legacy backend) just means no memory.</summary>
        private void RememberSent(string reportId, string title)
        {
            var log = SentLog;
            if (log == null || string.IsNullOrEmpty(reportId))
            {
                return;
            }

            if (log.Record(reportId, title, NowUnix()))
            {
                WebGlStorage.Sync(); // browser: make the memory durable across reloads (no-op elsewhere)
            }

            if (_nextPollAt >= 0f)
            {
                _nextPollAt = Mathf.Min(_nextPollAt, Time.unscaledTime + PollIntervalSeconds);
            }
        }

        /// <summary>The graphics node of a report (<see cref="GraphicsReportInfo"/>). Main thread.</summary>
        private static GraphicsReportInfo GraphicsInfo(ClientSettings settings)
        {
            var info = new GraphicsReportInfo
            {
                Api = SystemInfo.graphicsDeviceType.ToString(),
                UvStartsAtTop = SystemInfo.graphicsUVStartsAtTop,
                Preset = settings != null ? settings.Preset.ToString() : string.Empty,
                PresetAuto = settings != null && settings.PresetAuto,
                VisorEffects = settings == null || settings.VisorEffects,
                VisorPipeline = UiKit.HudCamera != null,
                HudOrientation = VisorHud.HudOrientation,
                ScreenWidth = Screen.width,
                ScreenHeight = Screen.height,
                LastCaptureCover = GpuReadback.LastCaptureCover,
            };
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline
                is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset urp)
            {
                info.RenderScale = urp.renderScale;
                info.Msaa = urp.msaaSampleCount;
                info.Hdr = urp.supportsHDR;
            }

            var cameraData = ClientSettings.ActiveCameraData;
            info.Antialiasing = cameraData != null ? cameraData.antialiasing.ToString() : string.Empty;
            return info;
        }

        private FeedbackReport BuildReport(string title, string desc, string email)
        {
            var reportJson = new Dictionary<string, object>
            {
                ["location"] = Game != null ? Game.LocationName : string.Empty,
                ["station"] = Game != null ? Game.StationName : string.Empty,
                ["worldSeed"] = Game != null ? Game.WorldSeed : 0L,
                ["health"] = Game != null ? Mathf.RoundToInt(Game.Health) : 0,
                ["oxygen"] = Game != null ? Mathf.RoundToInt(Game.Oxygen) : 0,
                ["energy"] = Game != null ? Mathf.RoundToInt(Game.SuitEnergy) : 0,
                ["hunger"] = Game != null ? Mathf.RoundToInt(Game.Hunger) : 0,
                ["sessionSeconds"] = Game != null ? Mathf.RoundToInt(Game.SessionSeconds) : 0,
                ["language"] = Settings != null ? Settings.Language : string.Empty,
            };
            if (ShellMode)
            {
                // Sent from the menu (#2523): there is no world, so a place, a seed and zeroed stats would only
                // mislead — the screen the player was on says where instead.
                foreach (string worldKey in new[] { "location", "station", "worldSeed", "health", "oxygen", "energy", "hunger", "sessionSeconds" })
                {
                    reportJson.Remove(worldKey);
                }

                reportJson["source"] = "menu";
                reportJson["shellScreen"] = Shell.Phase.ToString();
            }

            // Machine facts + "did the last session die?" — the same keys the crash reporter sends, so a blue
            // screen / driver-reset report can be judged without asking the player (#1564). Main thread here.
            DeviceInfo.Get().WriteTo(reportJson);
            SessionMarker.WriteTo(reportJson);
            // Which textures were NOT the game's own (#1964): "the grass looks wrong" means something else when
            // the player — or the world's admin — repainted the grass. Keys only, never pixels.
            GameTextures.ReportInfo.WriteTo(reportJson);
            // How the frame was drawn (preset, visor HUD pipeline and which way its texture lay, render scale, the size the
            // screen claimed, #2844): the upside-down browser HUD (#2512) could not be pinned down from a report without it.
            GraphicsInfo(Settings).WriteTo(reportJson);

            var report = new FeedbackReport
            {
                Title = title,
                Description = desc,
                Email = email,
                GameVersion = AppShell.Version,
                BuildNumber = Application.buildGUID ?? string.Empty,
                PlayerId = Settings != null ? Settings.PlayerToken : string.Empty,
                PlayerName = Settings != null && !string.IsNullOrEmpty(Settings.PlayerName) ? Settings.PlayerName
                    : Game != null ? Game.PlayerName : (Shell != null ? (Shell.PlayerName ?? string.Empty) : string.Empty),
                ReplyKey = _replyKey,
                SessionId = _sessionId,
                Platform = Application.platform.ToString(),
                ClientTimestamp = DateTime.UtcNow.ToString("o"),
                ScreenshotFileName = "feedback_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".jpg",
                ReportJson = reportJson,
            };
            return report;
        }

        // --- Spool retry -------------------------------------------------------------------------------------

        /// <summary>Retries reports queued by earlier sessions whose upload failed. Each report gets a
        /// bounded number of attempts (<see cref="FeedbackSpool.MaxAttempts"/>); on the first failure the
        /// rest wait for the next session — the inbox is likely down or we're offline.</summary>
        private void FlushSpool()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(FlushSpoolWebGl());
#else
            var spool = _spool;
            var uploader = _uploader;
            _ = Task.Run(() =>
            {
                try
                {
                    foreach (var path in spool.ListPending())
                    {
                        string json = spool.Read(path);
                        if (string.IsNullOrEmpty(json))
                        {
                            continue;
                        }

                        var result = uploader.UploadRawJson(json);
                        if (result != null && result.Ok)
                        {
                            spool.MarkSent(path);
                        }
                        else
                        {
                            spool.RegisterFailedAttempt(path);
                            break;
                        }
                    }
                }
                catch
                {
                    // best-effort startup catch-up — never disturb the game
                }
            });
#endif
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>WebGL flavor of <see cref="FlushSpool"/>: one UnityWebRequest at a time on a coroutine
        /// (persistentDataPath is IndexedDB-backed in the browser, so the queue survives page reloads).</summary>
        private IEnumerator FlushSpoolWebGl()
        {
            foreach (var path in _spool.ListPending())
            {
                string json = _spool.Read(path);
                if (string.IsNullOrEmpty(json))
                {
                    continue;
                }

                FeedbackUploadResult result = null;
                yield return FeedbackWebGlTransport.PostJson(json, r => result = r);
                if (result != null && result.Ok)
                {
                    _spool.MarkSent(path);
                }
                else
                {
                    _spool.RegisterFailedAttempt(path);
                    yield break;
                }
            }
        }
#endif

        // --- Reply inbox (#1328) ---------------------------------------------------------------------------

        private void TickReplyInbox(bool canLaunch)
        {
            // Marshal background results onto the main thread.
            if (_pollTask != null && _pollTask.IsCompleted)
            {
                var task = _pollTask;
                _pollTask = null;
                _polling = false;
                FeedbackReplyResult result;
                try { result = task.Result; }
                catch (Exception e) { result = new FeedbackReplyResult { Error = e.GetType().Name }; }
                OnPollFinished(result);
            }

            if (_answerTask != null && _answerTask.IsCompleted)
            {
                var task = _answerTask;
                _answerTask = null;
                FeedbackReplyResult result;
                try { result = task.Result; }
                catch (Exception e) { result = new FeedbackReplyResult { Error = e.GetType().Name }; }
                OnAnswerFinished(result);
            }

            // Scheduled poll — only ever fires with an API key, a key of our own and recent sent reports.
            if (_nextPollAt >= 0f && Time.unscaledTime >= _nextPollAt && !_polling)
            {
                _nextPollAt = Time.unscaledTime + PollIntervalSeconds;
                StartPoll();
            }

            // Show the next queued thread as soon as nothing else owns the screen.
            if (!_replyOpen && !_open && canLaunch && _inbox.Count > 0)
            {
                ShowThread(_inbox.Dequeue());
            }

            if (_replyOpen && !_answering && CancelPressed())
            {
                Game?.MarkMenuInputHandled();
                AcknowledgeAndClose();
            }
        }

        private void StartPoll()
        {
            var log = SentLog;
            if (_replies == null || !_replies.IsConfigured || string.IsNullOrEmpty(_replyKey) || log == null)
            {
                return;
            }

            if (!log.ShouldPoll(NowUnix()))
            {
                return; // nothing sent recently → no request at all (no phone-home without a reason)
            }

            // The ids we still remember ride along (#1369): the inbox names the ones it no longer has for
            // our key as `gone`, and OnPollFinished forgets them — otherwise a deleted or pruned report
            // would be polled for up to 90 days.
            var known = new List<string>();
            foreach (var sent in log.List(NowUnix()))
            {
                known.Add(sent.Id);
            }

            _polling = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(FeedbackWebGlTransport.Request(_replies.FetchUrl(_replyKey, 0, known), "GET", null, (res, body) =>
            {
                _polling = false;
                var result = new FeedbackReplyResult { Ok = res.Ok, StatusCode = res.StatusCode, Error = res.Error };
                if (res.Ok)
                {
                    result.Threads.AddRange(FeedbackReplyClient.ParseThreads(body));
                    result.Gone.AddRange(FeedbackReplyClient.ParseGone(body));
                }

                OnPollFinished(result);
            }));
#else
            var replies = _replies;
            string key = _replyKey;
            _pollTask = Task.Run(() => replies.Fetch(key, 0, known));
#endif
        }

        private void OnPollFinished(FeedbackReplyResult result)
        {
            if (result == null || !result.Ok)
            {
                if (result != null && result.Error.Length > 0)
                {
                    Debug.Log($"[Feedback] reply poll skipped: {result.Error}");
                }

                return;
            }

            // Reports the inbox no longer has for our key are forgotten, so the poll gate closes once the
            // last remembered report is gone (#1369).
            var log = result.Gone.Count > 0 ? SentLog : null;
            foreach (string goneId in result.Gone)
            {
                log?.Forget(goneId);
            }

            FeedbackReplyThread first = null;
            foreach (var thread in result.Threads)
            {
                // Keyed by reply id, not report id (#1351): a second answer or follow-up question on a report
                // whose first answer was already acknowledged opens the window again instead of waiting for
                // the next world restart.
                if (!_inboxTracker.Offer(thread))
                {
                    continue; // nothing new, or every unseen entry was already queued/shown this session
                }

                _inbox.Enqueue(thread);
                first ??= thread;
            }

            if (first != null)
            {
                Game?.ShowMessage(string.Format(L("ui.feedback.reply.toast"), first.Title));
            }
        }

        private void ShowThread(FeedbackReplyThread thread)
        {
            if (thread == null || (Game == null && !ShellMode))
            {
                return;
            }

            if (ShellMode)
            {
                RebuildIfLanguageChanged();
            }

            EnsureReplyDialog();
            _shown = thread;
            _replyOpen = true;
            _answering = false;

            _replyTitle.text = string.Format(L("ui.feedback.reply.report"), thread.Title.Length > 0 ? thread.Title : "…");
            _replyStatus.text = string.Empty;
            _replyStatus.color = UiKit.CyanDim;
            _answerInput.text = string.Empty;

            bool canAnswer = thread.AwaitsAnswer;
            _answerLabel.gameObject.SetActive(canAnswer);
            _answerInput.gameObject.SetActive(canAnswer);
            _replyAnswerBtn.gameObject.SetActive(canAnswer);
            _replyOkBtn.interactable = true;
            _replyAnswerBtn.interactable = true;

            _replyOverlay.SetActive(true);
            SetReplyBody(ThreadText(thread)); // after activation — the chunk heights are measured against live rects
            UiKit.OpenModal(_replyOverlay); // #2302: the open effect — ShowThread only runs while no reply is open
            HoldWorld(WorldHoldArbiter.FeedbackReply); // reading/answering holds the world like the F1 dialog (#1330)
            Game?.SetMenuOwner(_replyOwner, true); // none in the shell (#2523): the overlay's scrim is the modality there
        }

        /// <summary>Fills the scrollable body with the thread text: one plain (non-rich) Text per
        /// <see cref="UiTextChunks"/> piece, stacked top-down, the content sized to the sum so the viewport can
        /// scroll to the very end; the scroll starts at the top (#1368).</summary>
        private void SetReplyBody(string text)
        {
            foreach (var old in _replyBodyChunks)
            {
                if (old != null)
                {
                    Destroy(old.gameObject);
                }
            }

            _replyBodyChunks.Clear();

            float textW = _replyBodyW - ReplyBodyScrollbarW - ReplyBodyGutter;
            float y = 0f;
            foreach (var chunk in UiTextChunks.Split(text))
            {
                var t = UiKit.AddText(_replyBodyContent, 0f, y, textW, 20f, chunk, 16, UiKit.TextCol, TextAnchor.UpperLeft);
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.supportRichText = false; // developer/player text is shown verbatim — no <color>/<b> interpretation
                float h = Mathf.Ceil(t.preferredHeight) + 2f;
                t.rectTransform.sizeDelta = new Vector2(textW, h);
                y += h;
                _replyBodyChunks.Add(t);
            }

            _replyBodyContent.sizeDelta = new Vector2(0f, Mathf.Max(ReplyBodyH, y + 8f));
            _replyBodyScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>The thread as one readable block: who said what, oldest first, plus the "fixed in" line.
        /// Unbounded — the body renders it chunked and scrollable (#1368).</summary>
        private string ThreadText(FeedbackReplyThread thread)
        {
            var sb = new StringBuilder();
            foreach (var reply in thread.Replies)
            {
                string who = reply.IsDev
                    ? (reply.IsQuestion ? L("ui.feedback.reply.dev_question") : L("ui.feedback.reply.dev"))
                    : L("ui.feedback.reply.you");
                sb.Append(who).Append(' ').Append(reply.Text.Trim()).Append("\n\n");
            }

            if (!string.IsNullOrEmpty(thread.FixedInVersion))
            {
                sb.Append(string.Format(L("ui.feedback.reply.fixed_in"), thread.FixedInVersion));
            }

            return sb.ToString().TrimEnd();
        }

        private void EnsureReplyDialog()
        {
            if (_replyOverlay != null)
            {
                return;
            }

            _replyCanvas = UiKit.CreateCanvas("FeedbackReplyDialog");
            _replyCanvas.sortingOrder = 61; // above the F1 dialog, which it never overlaps in practice
            UiNav.Enable(_replyCanvas.gameObject);

            const float pw = 900f, ph = 660f;
            var (overlay, panel) = UiKit.AddModalOverlay(_replyCanvas.transform, (W - pw) / 2f, (H - ph) / 2f, pw, ph);
            _replyOverlay = overlay;
            const float m = 36f, innerW = pw - 2f * m;

            UiKit.AddText(panel, m, 22, innerW, 34, L("ui.feedback.reply.title"), 26, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            _replyTitle = UiKit.AddText(panel, m, 66, innerW, 26, string.Empty, 17, UiKit.TextCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            _replyTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            _replyTitle.verticalOverflow = VerticalWrapMode.Truncate;
            _replyTitle.supportRichText = false; // the report title is player text (#1368)

            BuildReplyBodyScroll(panel, m, 100f, innerW, ReplyBodyH);

            _answerLabel = UiKit.AddText(panel, m, 404, innerW, 20, L("ui.feedback.reply.answer_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _answerInput = UiKit.AddInput(panel, m, 426, innerW, 110, string.Empty, null, L("ui.feedback.reply.answer_placeholder"), DescLimit);
            _answerInput.lineType = InputField.LineType.MultiLineNewline;
            if (_answerInput.textComponent != null) _answerInput.textComponent.alignment = TextAnchor.UpperLeft;

            _replyStatus = UiKit.AddText(panel, m, 546, innerW, 24, string.Empty, 15, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Bold);

            _replyOkBtn = UiKit.AddButton(panel, m, 582, 400, 56, L("ui.feedback.reply.ok"), AcknowledgeAndClose, "btn_join");
            _replyAnswerBtn = UiKit.AddButton(panel, m + 428, 582, 400, 56, L("ui.feedback.reply.send"), OnAnswerClicked, "btn_feedback");

            _replyOverlay.SetActive(false);
        }

        /// <summary>The thread body's vertical ScrollRect (the credits/what's-new pattern): a masked viewport
        /// with a near-transparent hit surface for the wheel, a top-anchored content rect the chunk Texts stack
        /// on, and a permanent scrollbar along the right edge (#1368).</summary>
        private void BuildReplyBodyScroll(Transform panel, float x, float y, float w, float h)
        {
            _replyBodyContent = UiKit.AddScrollViewport(panel, "ReplyBodyScroll", x, y, w, h, out _replyBodyScroll, hitSprite: UiKit.SolidSprite);
            _replyBodyW = w;
            _replyBodyContent.sizeDelta = new Vector2(0f, h);

            UiKit.AddVerticalScrollbar(panel, _replyBodyScroll, x + w - ReplyBodyScrollbarW, y, ReplyBodyScrollbarW, h);
        }

        /// <summary>OK / Esc: the entries were shown, so acknowledge them (fire-and-forget) and close.</summary>
        private void AcknowledgeAndClose()
        {
            if (_answering)
            {
                return;
            }

            AckShown();
            CloseReply();
        }

        private void AckShown()
        {
            if (_shown == null || _shown.UnseenIds.Count == 0 || _replies == null || !_replies.IsConfigured)
            {
                return;
            }

            var ids = new List<long>(_shown.UnseenIds);
            _shown.UnseenIds.Clear(); // never ack twice
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(FeedbackWebGlTransport.Request(_replies.AckUrl, "POST", FeedbackReplyClient.AckBody(_replyKey, ids), (r, b) => { }));
#else
            var replies = _replies;
            string key = _replyKey;
            _ = Task.Run(() => replies.Ack(key, ids));
#endif
        }

        private void CloseReply()
        {
            CancelInvoke(nameof(CloseReply));
            _replyOpen = false;
            _answering = false;
            _shown = null;
            ReleaseInputFocus(_answerInput);
            if (_replyOverlay != null) _replyOverlay.SetActive(false);
            ReleaseWorld(WorldHoldArbiter.FeedbackReply);
            Game?.SetMenuOwner(_replyOwner, false);
            if (ShellMode)
            {
                _shellCancelFrame = Time.frameCount;
            }
        }

        private void OnAnswerClicked()
        {
            if (_answering || _shown == null)
            {
                return;
            }

            string text = ((_answerInput != null ? _answerInput.text : null) ?? string.Empty).Trim();
            if (text.Length < 2)
            {
                _replyStatus.text = L("ui.feedback.reply.need_text");
                _replyStatus.color = UiKit.Warn;
                return;
            }

            _answering = true;
            _replyOkBtn.interactable = false;
            _replyAnswerBtn.interactable = false;
            _replyStatus.text = L("ui.feedback.sending");
            _replyStatus.color = UiKit.CyanDim;

            string reportId = _shown.ReportId;
#if UNITY_WEBGL && !UNITY_EDITOR
            StartCoroutine(FeedbackWebGlTransport.Request(_replies.AnswerUrl, "POST", FeedbackReplyClient.AnswerBody(_replyKey, reportId, text),
                (res, body) => OnAnswerFinished(new FeedbackReplyResult { Ok = res.Ok, StatusCode = res.StatusCode, Error = res.Error })));
#else
            var replies = _replies;
            string key = _replyKey;
            _answerTask = Task.Run(() => replies.Answer(key, reportId, text));
#endif
        }

        private void OnAnswerFinished(FeedbackReplyResult result)
        {
            _answering = false;
            if (!_replyOpen)
            {
                return;
            }

            if (result != null && result.Ok)
            {
                _replyStatus.text = L("ui.feedback.reply.answer_sent");
                _replyStatus.color = UiKit.Ok;
                Game?.ShowMessage(L("ui.feedback.reply.answer_sent"));
                AckShown();
                Invoke(nameof(CloseReply), 1.2f);
            }
            else
            {
                _replyStatus.text = L("ui.feedback.reply.failed");
                _replyStatus.color = UiKit.Warn;
                _replyOkBtn.interactable = true;
                _replyAnswerBtn.interactable = true;
            }
        }

        private static string Shorten(string s, int max)
            => string.IsNullOrEmpty(s) || s.Length <= max ? s ?? string.Empty : s.Substring(0, max) + "…";

        // --- Screenshot ------------------------------------------------------------------------------------

        /// <summary>JPG-encodes the (already downscaled) screenshot and destroys it; null when the capture failed —
        /// the report then goes out without an image, as before.</summary>
        private static byte[] EncodeJpg(Texture2D shot)
        {
            if (shot == null)
            {
                Debug.LogWarning("[Feedback] screenshot failed: readback returned nothing.");
                return null;
            }

            try
            {
                return ImageConversion.EncodeToJPG(shot, 70);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Feedback] screenshot failed: {e.Message}");
                return null;
            }
            finally
            {
                Destroy(shot);
            }
        }
    }
}
