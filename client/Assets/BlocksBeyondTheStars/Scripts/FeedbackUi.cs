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
    /// </summary>
    public sealed class FeedbackUi : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

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
        private Button _sendBtn, _cancelBtn;

        private bool _open;
        private bool _sending;
        private byte[] _shotJpg;                 // screenshot captured when the dialog opened
        private Task<FeedbackUploadResult> _uploadTask;

        // The dialog's "hold the world while I'm open" request — one class shared with the Esc menu so the
        // intent, the release and the 15 s keep-alive the server sweeps dead clients by (#973) have one copy.
        private WorldHoldIntent _worldHoldOrNull;

        private WorldHoldIntent WorldHold =>
            _worldHoldOrNull ??= new WorldHoldIntent(paused => Game?.Network?.SendPause(paused));

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

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private void Start()
        {
            // A random id groups several reports from one sitting (varies per session; no Unity-restricted Date use).
            _sessionId = Guid.NewGuid().ToString("N");
            _uploader = new FeedbackUploader(FeedbackUploader.DefaultEndpoint, BugReportBuildSecrets.ApiKey);
            _spool = new FeedbackSpool(Path.Combine(AppPaths.Root, "feedback"));
            _replies = new FeedbackReplyClient(FeedbackReplyClient.EndpointFor(FeedbackUploader.DefaultEndpoint), BugReportBuildSecrets.ApiKey);
            _sentLog = new SentReportsLog(Path.Combine(AppPaths.Root, "feedback", "sent.json"));
            _replyKey = ComputeReplyKey();

            if (_uploader.IsConfigured)
            {
                FlushSpool(); // deliver what an earlier session couldn't (bounded attempts, see FeedbackSpool)
                _nextPollAt = Time.unscaledTime + FirstPollDelaySeconds;
            }
        }

        /// <summary>The install's reply-thread credential: a one-way hash of the install secret. Desktop and
        /// play.* builds hash the name-claim token (stable per install — the <c>/play/</c> path never changes);
        /// the glitch.fun arcade hashes the Glitch install id instead, because the browser-local token there
        /// resets with every deployment (#1177) while the install id follows the player across deployments
        /// and browsers. Whoever learns the key can read replies — never claim a name.</summary>
        private string ComputeReplyKey()
        {
            string secret = GlitchIntegration.ArcadeInstallId; // empty everywhere except the arcade
            if (string.IsNullOrEmpty(secret) && Settings != null)
            {
                secret = Settings.PlayerToken;
            }

            return FeedbackReplyKey.Derive(secret);
        }

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            // The hotkey opens feedback during normal play — on foot AND in space flight (both HUDs advertise
            // it); not while a menu/chat modal or the death prompt already owns the screen.
            bool canLaunch = !Game.MenuOpen && !Game.ChatTyping && !Game.AwaitingRespawnConfirm;

            if (!_open && canLaunch && Input.GetKeyDown(Hotkey))
            {
                Open();
            }

            if (_open && !_sending && Input.GetKeyDown(KeyCode.Escape))
            {
                Game.MarkMenuInputHandled(); // this Esc is consumed — don't also pop the quit prompt (#413 N1)
                Close();
            }

            // Keep telling the server we are still here while the dialog holds the world (no-op while closed).
            WorldHold.Tick(Time.realtimeSinceStartup);

            // Marshal the background upload result back onto the main thread.
            if (_uploadTask != null && _uploadTask.IsCompleted)
            {
                var task = _uploadTask;
                _uploadTask = null;
                FeedbackUploadResult result;
                try { result = task.Result; }
                catch (Exception e) { result = new FeedbackUploadResult { Error = e.GetType().Name }; }
                OnUploadFinished(result);
            }

            TickReplyInbox(canLaunch);
        }

        // --- Open / close ----------------------------------------------------------------------------------

        /// <summary>Opens the feedback dialog (the <see cref="Hotkey"/>'s target). Captures the gameplay frame first — HUD
        /// visible, dialog not yet shown — then dims the screen and shows the form.</summary>
        public void Open()
        {
            if (_open || Game == null)
            {
                return;
            }

            _open = true;
            StartCoroutine(OpenRoutine());
        }

        private IEnumerator OpenRoutine()
        {
            // Capture at end of frame, before the dialog is shown and before MenuOpen hides the HUD: the shot
            // is the full frame WITH the HUD but WITHOUT this dialog (the requested look).
            yield return new WaitForEndOfFrame();
            if (!_open)
            {
                // Closed in the very frame it was opened (the hotkey and Esc together): Close() already ran, so
                // building the dialog now would show it with _open == false — holding the world with no Esc
                // able to close it (#1368). Nothing to show, nothing to hold.
                yield break;
            }

            _shotJpg = TryCaptureJpg();

            EnsureDialog();
            ResetFields();
            _dialog.SetActive(true);

            // Modal: free the cursor + pause player/flight control (mirrors GameMenu / BeamPadUi; SpaceView
            // holds position while MenuOpen). The arbiter recomputes on close, so a flight sub-screen the
            // dialog opened over (e.g. the landing-pad chooser) keeps its free cursor without us having to
            // save/restore the prior state by hand (#413).
            Game.SetMenuOwner(this, true);

            // Hold the world like the Esc menu does (#1330) — after the screenshot, so the shot shows live play.
            // The server decides what it means (#973): alone, the world stops right here; with others joined it
            // only counts as "this player is in a menu" until everyone else is too.
            WorldHold.Hold(Time.realtimeSinceStartup);
        }

        private void Close()
        {
            CancelInvoke(nameof(Close));
            _open = false;
            _sending = false;
            _shotJpg = null;
            if (_dialog != null) _dialog.SetActive(false);

            WorldHold.Release(); // every close path ends here (Esc, Cancel, the auto-close after a send) — sends once
            Game?.SetMenuOwner(this, false); // arbiter re-locks only once NO other owner is open (#413)
        }

        private void ResetFields()
        {
            if (_titleInput != null) _titleInput.text = string.Empty;
            if (_descInput != null) _descInput.text = string.Empty;
            if (_emailInput != null) _emailInput.text = string.Empty;
            if (_status != null) { _status.text = string.Empty; _status.color = UiKit.CyanDim; }
            SetSendInteractable(true);
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
            _descInput = UiKit.AddInput(panel, m, 168, innerW, 150, string.Empty, null, L("ui.feedback.desc_placeholder"), 1500);
            _descInput.lineType = InputField.LineType.MultiLineNewline;
            if (_descInput.textComponent != null) _descInput.textComponent.alignment = TextAnchor.UpperLeft;

            UiKit.AddText(panel, m, 330, innerW, 20, L("ui.feedback.email_label"), 15, UiKit.TextCol, TextAnchor.MiddleLeft);
            _emailInput = UiKit.AddInput(panel, m, 352, innerW, 40, string.Empty, null, L("ui.feedback.email_placeholder"), 120);

            var hint = UiKit.AddText(panel, m, 404, innerW, 116, L("ui.feedback.hint"), 14, UiKit.CyanDim, TextAnchor.UpperLeft);
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

            string desc = _descInput != null ? (_descInput.text ?? string.Empty).Trim() : string.Empty;
            if (desc.Length < 3)
            {
                if (_status != null) { _status.text = L("ui.feedback.need_text"); _status.color = UiKit.Warn; }
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
            if (_sentLog == null || string.IsNullOrEmpty(reportId))
            {
                return;
            }

            if (_sentLog.Record(reportId, title, NowUnix()))
            {
                WebGlStorage.Sync(); // browser: make the memory durable across reloads (no-op elsewhere)
            }

            if (_nextPollAt >= 0f)
            {
                _nextPollAt = Mathf.Min(_nextPollAt, Time.unscaledTime + PollIntervalSeconds);
            }
        }

        private FeedbackReport BuildReport(string title, string desc, string email)
        {
            var report = new FeedbackReport
            {
                Title = title,
                Description = desc,
                Email = email,
                GameVersion = AppShell.Version,
                BuildNumber = Application.buildGUID ?? string.Empty,
                PlayerId = Settings != null ? Settings.PlayerToken : string.Empty,
                PlayerName = Settings != null && !string.IsNullOrEmpty(Settings.PlayerName) ? Settings.PlayerName : (Game != null ? Game.PlayerName : string.Empty),
                ReplyKey = _replyKey,
                SessionId = _sessionId,
                Platform = Application.platform.ToString(),
                ClientTimestamp = DateTime.UtcNow.ToString("o"),
                ScreenshotFileName = "feedback_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + ".jpg",
                ReportJson = new Dictionary<string, object>
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
                },
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

            if (_replyOpen && !_answering && Input.GetKeyDown(KeyCode.Escape))
            {
                Game.MarkMenuInputHandled();
                AcknowledgeAndClose();
            }
        }

        private void StartPoll()
        {
            if (_replies == null || !_replies.IsConfigured || string.IsNullOrEmpty(_replyKey) || _sentLog == null)
            {
                return;
            }

            if (!_sentLog.ShouldPoll(NowUnix()))
            {
                return; // nothing sent recently → no request at all (no phone-home without a reason)
            }

            // The ids we still remember ride along (#1369): the inbox names the ones it no longer has for
            // our key as `gone`, and OnPollFinished forgets them — otherwise a deleted or pruned report
            // would be polled for up to 90 days.
            var known = new List<string>();
            foreach (var sent in _sentLog.List(NowUnix()))
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
            foreach (string goneId in result.Gone)
            {
                _sentLog?.Forget(goneId);
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
            if (thread == null || Game == null)
            {
                return;
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
            WorldHold.Hold(Time.realtimeSinceStartup); // reading/answering holds the world like the F1 dialog (#1330)
            Game.SetMenuOwner(_replyOwner, true);
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
            _answerInput = UiKit.AddInput(panel, m, 426, innerW, 110, string.Empty, null, L("ui.feedback.reply.answer_placeholder"), 1500);
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
            var viewGo = new GameObject("ReplyBodyScroll", typeof(RectTransform));
            viewGo.transform.SetParent(panel, false);
            UiKit.Place(viewGo, x, y, w, h);
            _replyBodyW = w;

            _replyBodyScroll = viewGo.AddComponent<ScrollRect>();
            _replyBodyScroll.horizontal = false;
            _replyBodyScroll.vertical = true;
            _replyBodyScroll.movementType = ScrollRect.MovementType.Clamped;
            _replyBodyScroll.scrollSensitivity = 28f;
            viewGo.AddComponent<RectMask2D>();

            var hit = viewGo.AddComponent<Image>();
            hit.sprite = UiKit.SolidSprite;
            hit.color = new Color(0f, 0f, 0f, 0.001f);

            var contentGo = new GameObject("Content", typeof(RectTransform));
            _replyBodyContent = (RectTransform)contentGo.transform;
            _replyBodyContent.SetParent(viewGo.transform, false);
            _replyBodyContent.anchorMin = new Vector2(0f, 1f);
            _replyBodyContent.anchorMax = new Vector2(1f, 1f);
            _replyBodyContent.pivot = new Vector2(0.5f, 1f);
            _replyBodyContent.anchoredPosition = Vector2.zero;
            _replyBodyContent.sizeDelta = new Vector2(0f, h);

            _replyBodyScroll.viewport = (RectTransform)viewGo.transform;
            _replyBodyScroll.content = _replyBodyContent;

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
            if (_replyOverlay != null) _replyOverlay.SetActive(false);
            WorldHold.Release();
            Game?.SetMenuOwner(_replyOwner, false);
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

        private byte[] TryCaptureJpg()
        {
            try
            {
                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                try { return EncodeDownscaledJpg(shot, 1600, 70); }
                finally { Destroy(shot); }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Feedback] screenshot failed: {e.Message}");
                return null;
            }
        }

        /// <summary>JPG-encodes a screenshot, downscaled so its longest side is at most <paramref name="maxDim"/>
        /// (keeps the upload small). Mirrors <see cref="ChatUi"/>'s /bump encoder.</summary>
        private static byte[] EncodeDownscaledJpg(Texture2D src, int maxDim, int quality)
        {
            int w = src.width, h = src.height;
            float scale = Mathf.Min(1f, (float)maxDim / Mathf.Max(w, h));
            int tw = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            int th = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            if (tw == w && th == h)
            {
                return ImageConversion.EncodeToJPG(src, quality);
            }

            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var small = new Texture2D(tw, th, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            small.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            byte[] jpg = ImageConversion.EncodeToJPG(small, quality);
            UnityEngine.Object.Destroy(small);
            return jpg;
        }
    }
}
