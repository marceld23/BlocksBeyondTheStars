// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The HUD companion panel for the ship AI "VEGA": shows her lines with a typewriter effect (queued,
    /// radio blip per line) and a persistent objective chip (the basic training's step, a story lead …), with a
    /// skip button. A revealed page stays until the player presses the continue key — there is no
    /// auto-dismiss timeout (#1011). Instead, a revealed (non-prologue) page nobody advanced for a while folds
    /// into a small "VEGA" tab counting the waiting lines (#2126, <see cref="VegaCollapse"/>); the continue
    /// control or a click/tap on the tab opens it again with the text intact. The continue hint is a
    /// clickable pill too — for the mouse wherever the cursor is free (space flight, open prompts) and for
    /// touch. Lines arrive as locale KEYS (<see cref="ShipAiLine"/>) and are
    /// localized here, so the companion is fully bilingual and offline-safe. Advisor hints (Kind 1)
    /// respect the settings mute. Waiting lines follow <see cref="VegaLineQueue"/> (#2551): a client lesson bound to a
    /// moment goes unseen once it waited 30 s, a story pack's step line goes ahead of waiting hints, story lines keep
    /// their order.
    /// </summary>
    public sealed class VegaPanel : MonoBehaviour
    {
        public GameBootstrap Game;
        public ClientSettings Settings;

        /// <summary>Cinematic staging for the first-spawn prologue (#760), wired by WorldRig. When it can
        /// stage (aboard, on foot), the Kind-4 pages get letterbox + a camera orbit of the player's ship
        /// instead of the plain dim; the panel keeps showing the text, subtitle-style.</summary>
        public PrologueCinematic Cinematic;

        /// <summary>Flashback treatment for Kind-2 memory beats (#762), wired by WorldRig.</summary>
        public MemoryFlashback Flashback;

        private const float CharsPerSecond = 42f;

        // Left-column layout in HUD reference units (1536×864). The column is full: vitals end at y 260,
        // the toast sits at 268, the scan panel starts at 650 and the hotbar backplate owns y 742…834 /
        // x 400…1136. These two constants are what a layout tweak should move (#482). They are public
        // because the chat overlay shares the lane and yields to whichever of the two is up (ChatUi).
        public const float SpeechY = 396f, SpeechH = 190f;
        public const float ChipY = 594f;

        // The speech body's text rect — the page splitter (#736) measures wrapped lines against exactly
        // this box, so a page can never be taller than what VerticalWrapMode.Truncate would show.
        private const float SpeechTextW = 612f, SpeechTextH = 116f;

        // The continue pill (#2126): bottom-right in the speech panel, sized to its text; the collapsed tab sits on the
        // objective chip's row — at the chip's spot when no objective shows, else just right of the chip.
        private const float ContinueY = 156f, ContinueH = 28f, ContinuePad = 28f, ContinueRight = 14f;
        private const float TabH = 40f, TabGap = 8f, TabMinW = 150f, TabMaxW = 480f, TabTextX = 46f;

        private Canvas _canvas;
        private GameObject _speech;
        private TMPro.TMP_Text _speechText;
        private TMPro.TMP_Text _continueHint;
        private RectTransform _continuePill; // the clickable frame around _continueHint (#2126)
        private TMPro.TMP_Text _skipHint;    // "Skip · [Esc]" / "(B)" / tap on prologue pages, inside the skip pill (#2461)
        private RectTransform _skipPill;     // the clickable frame around _skipHint, left of the continue pill (#2461)
        private bool _skipClicked;           // the skip pill was clicked/tapped since the last frame
        private GameObject _chip;
        private TMPro.TMP_Text _chipText;

        // The collapsed tab (#2126) and its state: the fold rule is Unity-free (Client.Core, tested headless).
        private readonly VegaCollapse _collapse = new VegaCollapse();
        private GameObject _tab;
        private CanvasGroup _speechGroup; // #2497: hides the speech panel under a card (the chip stays)
        private CanvasGroup _tabGroup;
        private bool _speechYielded;
        private RectTransform _tabRect;
        private TMPro.TMP_Text _tabText;
        private int _tabCount = -1;                    // the count last drawn, to flash the tab when a line arrives
        private InputDeviceKind _tabDevice = (InputDeviceKind)(-1);
        private bool _tabBesideChip;
        private bool _clicked;                         // the pill or the tab was clicked/tapped since the last frame

        // Each queued line keeps its locale key (empty for a client-side reminder) so the ending can withdraw the epilogue
        // line it shows itself (#2461, WithdrawLine) — and since #2499 its pack and its buttons (VegaQueuedLine, Client.Core).
        // #2551: the queue's rules live in Client.Core (VegaLineQueue, tested headless): a client lesson that waited unseen for
        // 30 s is dropped (its once-flag stays unspent), a step line goes ahead of waiting hints, story lines keep their order.
        private readonly VegaLineQueue _queue = new VegaLineQueue();

        /// <summary>The queue's clock (#2551): real seconds, so a lesson's wait runs on through a pause or a scene — its
        /// moment passes either way.</summary>
        private static double Now => Time.unscaledTimeAsDouble;

        private string _current = string.Empty;  // the page being typed/read (not the whole line)
        private string _currentKey = string.Empty; // the locale key of the line on screen (#2461)

        // #2499: the buttons of the line on screen (None = a plain line) — the primary on the continue pill, the secondary on
        // the skip pill — and the continue control's press/hold on them (a long press picks the secondary; Client.Core).
        private VegaChoiceSet _choices = VegaChoiceSet.None;
        private readonly VegaChoiceInput _choiceInput = new VegaChoiceInput();
        private bool _skipPillChoice; // the skip pill currently shows the secondary button, not the prologue's skip
        private float _shown;     // characters revealed so far
        private int _shownChars = -1; // #1554: the character count last pushed into the text (skip equal frames)

        // Long lines are split into panel-sized pages, advanced with the same continue key (#736). German
        // runs 12–20 % longer than English, so the bandit briefing and several hints exceed the ~4 visible
        // lines — they used to be silently truncated.
        private readonly List<string> _pages = new List<string>();
        private int _page;
        // (Page measurement now uses TMP's own line info — see SplitPages.)

        // First-spawn narrative prologue (#738, reworked in #754): Kind-4 lines run through the SAME speech
        // panel as every other VEGA line (same measure, same paging, same user UI scale) — they used to get
        // a bespoke full-screen dialog whose near-black plate read as "text across the whole screen". The
        // only prologue extras now are a dim behind the panel and the skip (Esc, the pad's cancel button or the skip
        // pill — every device since #2461). Since #2470 this is the legacy path only: a current server plays the prologue
        // as the VEGA pack's intro cutscene for this client (it announces cutscenes1) and sends no Kind-4 page; a server
        // from before still sends the pages, and they still play here. A stray page the director's owed prologue shows
        // itself is claimed by it first (replaceLineKeys), so the prologue never plays twice.
        private Image _dim;
        private bool _currentIsPrologue;
        private bool _chromeOn; // edge detection for the chrome side effects (chip hide/restore)

        // Staged-prologue state (#760): whether the cinematic took the stage for this prologue run, and
        // which of the Kind-4 lines is up (drives the camera legs). Reset when the prologue ends, so a
        // deliberately restarted onboarding stages again.
        private bool _prologueStarted;
        private bool _prologueStaged;
        private int _prologueLineIndex;

        // A failed staging is retried for a few seconds while the orbit ring streams in (#2461, B19) — it used to be
        // tried once, and a fresh world (WebGL above all) then played the whole prologue as dim + panel.
        private readonly StagingRetry _stagingRetry = new StagingRetry();

        private string _objectiveKey = string.Empty;
        private string _objectiveArg = string.Empty; // {0} of the objective line (#1859: the fragment world's name)
        private string _objectiveSource = string.Empty; // tutorial / story / finale / mission; empty from an older server (#2462)
        private string _objectivePackId = string.Empty; // #2471: the story whose objective is on the chip — its short name labels it
        private int _objProgress, _objTarget;
        private RectTransform _chipRect; // grows from 48 to 72 for a three-line objective (#1859)

        // #2471 "Show all objectives": the objective list's other rows (at most two) in small strips under the chip, hidden
        // while the chat is open. Redrawn only when something they show changed (_extraSig).
        private const float ExtraRowH = 30f, ExtraRowGap = 4f;
        private readonly GameObject[] _extraRows = new GameObject[ObjectiveList.MaxItems - 1];
        private readonly TMPro.TMP_Text[] _extraTexts = new TMPro.TMP_Text[ObjectiveList.MaxItems - 1];
        private int _extraSig = int.MinValue;

        // VEGA's non-verbal vocoder voice (#761): short ElevenLabs chatter variants chained while a
        // page types out. No words → language-independent, nothing to re-record when texts change.
        // #2495: the shared NarratorVoice (CutsceneScreen.cs) — the cutscene director's speaker box speaks with it too.
        private NarratorVoice _voice;
        private WorldLoadingOverlay _worldVeil; // chatter never plays behind the loading curtain

        // How many break reminders have fired this session — drives the escalating wording and the next due time.
        private int _remindersSent;

        private System.Func<string, string> _localize; // L as a cached delegate for the skip label (#2461)

        private void Start()
        {
            Instance = this;
            _localize = L;

            // The HUD reference (1536×864), NOT the 1920×1080 default — VEGA was missed by the 2026-06-07
            // "bigger HUD" pass (#482), so her lines rendered 25 % smaller than every other HUD element at
            // every resolution. Subtitle-class text has to read while the eye is on the crosshair.
            _canvas = UiKit.CreateDiegeticCanvas("VegaPanel", UiKit.HudRefW, UiKit.HudRefH);
            _canvas.sortingOrder = 11; // just above HudUi (10) — a story line must never be occluded

            // Prologue dim (#754): a soft full-screen shade BEHIND the speech panel while a Kind-4 story
            // page is up, so the moment reads cinematic without a separate dialog stealing the layout.
            _dim = UiKit.AddImage(_canvas.transform, 0, 0, UiKit.HudRefW, UiKit.HudRefH, UiKit.SolidSprite,
                new Color(0f, 0f, 0f, 0.55f));
            _dim.gameObject.SetActive(false);

            // Speech panel: left side above the vitals, out of the crosshair's way. VEGA gets a small
            // generated avatar chip beside her name (uGUI icon pass). Coordinates are in HUD reference
            // units; the left column is tight (vitals → speech → chip → scan panel → hotbar), see #482.
            _speech = UiHolo.AddPanel(_canvas.transform, 24, SpeechY, 640, SpeechH, new Color(0.05f, 0.10f, 0.16f, 0.82f), 12f, 1.5f, 1.2f).gameObject;
            var avatar = UiKit.Icon("icon_vega");
            float nameX = 14f;
            if (avatar != null)
            {
                UiKit.AddImage(_speech.transform, 12, 6, 34, 34, avatar, UiKit.Cyan);
                nameX = 54f;
            }

            UiText.Add(_speech.transform, nameX, 6, 320, 30, L("ui.vega.name"), 22, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, UiText.Look.Glow);
            _speechText = UiText.Add(_speech.transform, 14, 44, SpeechTextW, SpeechTextH, string.Empty, 22, UiKit.TextCol, TextAnchor.UpperLeft, FontStyle.Normal, UiText.Look.Outline);
            // Truncate, NOT Overflow: an LLM-authored line has no length bound on the wire, and an
            // over-long one used to run over the continue hint and out of the panel background (#482).
            UiText.Wrap(_speechText, truncate: true); // readable over bright terrain / snow / sky (underlay look)
            // Lines advance on a KEYPRESS (they queued straight through each other before — unreadable). The hint is
            // a clickable pill since #2126: brighter and bolder than the old dim 16 px line, and a mouse click (where
            // the cursor is free) or a tap does what the continue control does.
            var pill = UiHolo.AddPanel(_speech.transform, 640f - ContinueRight - 200f, ContinueY, 200f, ContinueH,
                new Color(0.08f, 0.26f, 0.38f, 0.92f), 9f, 1.4f, 1.3f);
            pill.gameObject.name = "VegaContinue";
            _continuePill = pill.rectTransform;
            MakeClickable(pill);
            _continueHint = UiText.Add(pill.transform, 0, 0, 200f, ContinueH, L("ui.vega.next"), 18, UiKit.Cyan, TextAnchor.MiddleCenter, FontStyle.Bold, UiText.Look.Outline);

            // The skip pill (#2461): the prologue's way out on every device. Esc and the pad's cancel button skip, and so
            // does a click or a tap on the pill itself — touch players have neither key, and the touch layer (with its
            // NEXT button) steps aside under the cutscene. Bottom-left in the panel, apart from the continue pill.
            var skipPill = UiHolo.AddPanel(_speech.transform, 14f, ContinueY, 160f, ContinueH,
                new Color(0.10f, 0.14f, 0.20f, 0.90f), 9f, 1.2f, 1.0f);
            skipPill.gameObject.name = "VegaSkip";
            _skipPill = skipPill.rectTransform;
            MakeClickable(skipPill, skip: true);
            _skipHint = UiText.Add(skipPill.transform, 0, 0, 160f, ContinueH, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleCenter, FontStyle.Normal, UiText.Look.Outline);
            _skipPill.gameObject.SetActive(false);
            SetContinueVisible(false);
            _speech.SetActive(false);

            // The collapsed tab (#2126): VEGA's icon, her name and the waiting lines, on the chip row; click/tap opens it.
            var tab = UiHolo.AddPanel(_canvas.transform, 24, ChipY, TabMinW, TabH, new Color(0.05f, 0.10f, 0.16f, 0.86f), 10f, 1.5f, 1.3f);
            tab.gameObject.name = "VegaTab";
            _tab = tab.gameObject;
            _tabRect = tab.rectTransform;
            MakeClickable(tab);
            if (avatar != null)
            {
                UiKit.AddImage(_tab.transform, 8, 5, 30, 30, avatar, UiKit.Cyan);
            }

            _tabText = UiText.Add(_tab.transform, TabTextX, 0, TabMaxW - TabTextX, TabH, string.Empty, 17, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Bold, UiText.Look.Glow);
            _tab.SetActive(false);

            // Objective chip: small persistent strip below the speech spot. (Skipping/restarting the
            // tutorial lives in the Character tab — the mouse is captured for camera control out here,
            // so a button on the chip was unreachable.)
            _chip = UiHolo.AddPanel(_canvas.transform, 24, ChipY, 640, VegaObjectiveChip.HeightNormal, new Color(0.05f, 0.10f, 0.16f, 0.66f), 10f, 1.2f, 0.9f).gameObject;
            _chipRect = _chip.GetComponent<RectTransform>();
            _chipText = UiText.Add(_chip.transform, 14, 0, VegaObjectiveChip.TextWidth, VegaObjectiveChip.HeightNormal, string.Empty, VegaObjectiveChip.FontLarge, UiKit.Cyan, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);
            // Wrap + truncate as a safety net — the UiKit default (Overflow) would let an over-long
            // objective label spill outside the chip background (#736 side finding). The objective is fitted
            // first (FitChip, #1859): a long line drops to the small font, the counter moves to its own line
            // and the chip grows a row, so truncation only ever bites a pathological string.
            UiText.Wrap(_chipText, truncate: true);
            _chip.SetActive(false);

            // #2471: the "Show all objectives" strips under the chip (placed per frame below the chip's current height).
            for (int i = 0; i < _extraRows.Length; i++)
            {
                float rowY = ChipY + VegaObjectiveChip.HeightNormal + ExtraRowGap + (i * (ExtraRowH + ExtraRowGap));
                var row = UiHolo.AddPanel(_canvas.transform, 24, rowY, 640, ExtraRowH, new Color(0.05f, 0.10f, 0.16f, 0.55f), 8f, 1.0f, 0.8f).gameObject;
                row.name = "VegaObjectiveExtra" + i;
                _extraTexts[i] = UiText.Add(row.transform, 14, 0, VegaObjectiveChip.TextWidth, ExtraRowH, string.Empty, 16, UiKit.CyanDim, TextAnchor.MiddleLeft, FontStyle.Normal, UiText.Look.Outline);
                UiText.Wrap(_extraTexts[i], truncate: true);
                row.SetActive(false);
                _extraRows[i] = row;
            }

            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived += OnLine;
            }

            // VEGA's chatter clips (vega_chatter_1..3, #2495: the one default list in Client.Core); none bundled → silent typewriter.
            _voice = new NarratorVoice(gameObject, () => Settings);
            _voice.SetClips(BlocksBeyondTheStars.Client.Cutscenes.CutsceneVoice.VegaDefault);
            _worldVeil = FindAnyObjectByType<WorldLoadingOverlay>();
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private bool HasKey(string key) => Game?.Localizer != null && !string.IsNullOrEmpty(key) && Game.Localizer.Has(key);

        /// <summary>#2500: a story key's text for the panel and the chip — a key of a story this client does not have (a newer
        /// build's pack) reads as the neutral "needs a newer version" note, never as a raw <c>[key]</c>
        /// (<see cref="UnknownStoryPacks.Text"/>).</summary>
        private string StoryKeyText(string key, string packId)
            => Game != null && Game.Localizer != null
                ? Game.UnknownStories.Text(key, packId, L, HasKey, Game.HasStoryPack)
                : L(key);

        /// <summary>#2471: the label before an objective — the story's short name ("Training", "VEGA") from the server's story
        /// list or this client's copy of the story, else the generic "Objective".</summary>
        private string ObjectivePrefix(string packId)
            => ObjectiveTracker.Prefix(ObjectiveTracker.ShortNameKey(packId, Game?.StoryPackStates, Game?.Content?.Stories.Values), L, HasKey);

        /// <summary>The live panel (one per rig), for client-side one-shot hints (<see cref="SayLocal"/>).</summary>
        public static VegaPanel Instance { get; private set; }

        /// <summary>Whether the speech panel (y <see cref="SpeechY"/>…) is currently drawn. The chat overlay
        /// polls this to keep its scrollback out of the same left-column band.</summary>
        public bool SpeechVisible => _speech != null && _speech.activeSelf;

        /// <summary>Whether the objective chip row (y <see cref="ChipY"/>…) is taken — by the objective chip or by the
        /// collapsed tab (#2126), which sits on that row — see <see cref="SpeechVisible"/>. A folded panel frees the
        /// speech band for the chat, the tab keeps the chat's input row off the chip row.</summary>
        public bool ChipVisible => (_chip != null && _chip.activeSelf) || (_tab != null && _tab.activeSelf);

        /// <summary>A client-side hint spoken in VEGA's voice without a server round-trip (#1663) — for UI
        /// lessons only the client knows the moment for (the first time a screen opens). Follows the advisor
        /// rules of a Kind-1 server line: the VegaHints mute, <c>{key:Action}</c> glyphs, the same speech
        /// queue, and the tips log for the session (a rejoin rebuilds the log from server milestones, so a
        /// purely local lesson drops out of it then — by design, it is a one-liner). Returns whether the line was
        /// queued (or is already waiting or on screen). A caller with a once-flag passes its setter as
        /// <paramref name="onShown"/> (#2551): it runs when the line comes up on screen — or at once when a running training
        /// or course counts the lesson as given — and never for a line muted by the hints switch (#2462, B31: it must still
        /// come once the hints are back on) or dropped unseen: a lesson is bound to its moment, so one still waiting after
        /// <see cref="VegaLineQueue.LessonExpirySeconds"/> goes (the player has walked on) and may come again the next time
        /// the situation happens. Saying a lesson that already waits keeps its place and starts its wait over.
        /// <paramref name="keepInLog"/> false keeps a passing remark out of the tips log (the bed's good night, #2823).</summary>
        public bool SayLocal(string lineKey, System.Action onShown = null, bool keepInLog = true)
        {
            if (string.IsNullOrEmpty(lineKey))
            {
                return false;
            }

            // #2551: the situation asks again for a lesson VEGA is already holding — no second copy; the wait starts over.
            if (_queue.RefreshLesson(lineKey, Now))
            {
                return true;
            }

            // ...or she is saying it right now (another anomaly in range): it counts as given.
            if (_current.Length > 0 && string.Equals(_currentKey, lineKey, System.StringComparison.Ordinal))
            {
                onShown?.Invoke();
                return true;
            }

            // #2469 (E11, G12): while the basic training runs, its flight steps teach this lesson themselves — per player, on
            // the server — so VEGA does not say it a second time, and the lesson counts as given (the caller's one-shot flag
            // is set; the tutorial line was never muted, so this holds with the hints off too).
            if (VegaText.LessonCoveredByTutorial(lineKey) && Game != null && Game.OnboardingActive)
            {
                onShown?.Invoke();
                return true;
            }

            // #2459: the same for a running deep-dive course whose step (active or still ahead) names this lesson in its
            // hintRefs — the course's line teaches it, so the lesson counts as given and VEGA does not say it twice.
            if (Game != null && VegaText.LessonCoveredByStoryStep(lineKey, Game.StoryPackStates, Game.Content?.Stories.Values))
            {
                onShown?.Invoke();
                return true;
            }

            if (Settings is { VegaHints: false })
            {
                return false;
            }

            _queue.Enqueue(new VegaQueuedLine(VegaText.Compose(L(lineKey), null, KeyGlyphFor), false, lineKey), VegaLineClass.Lesson, Now, onShown);
            if (keepInLog && Game != null && !Game.VegaLogKeys.Contains(lineKey))
            {
                Game.VegaLogKeys.Add(lineKey);
            }

            Refresh();
            return true;
        }

        /// <summary>Resolver for <c>{key:Action}</c> tokens in VEGA lines — the same rule the HUD prompts use
        /// (<see cref="HudUi.KeyTokenGlyph"/>, #2463): the bound key or pad button for a known action, and on touch the
        /// layout's own button (USE, MAP, ≡ …) or else the on-screen action menu, which reaches every other toggle.
        /// Unknown action names fall through as-is (VegaText drops to the name).</summary>
        private string KeyGlyphFor(string action) => HudUi.KeyTokenGlyph(Game != null ? Game.Localizer : null, action);

        /// <summary>A VEGA line composed the way the panel shows it — key glyphs resolved, slots filled or, without an
        /// argument, neutralised (#2364: the tips log re-reads lines whose argument the milestones no longer carry).</summary>
        public string ComposeLine(string text, string lineArg) => VegaText.Compose(text, lineArg, KeyGlyphFor);

        /// <summary>Capture hook (<see cref="ScreenshotDirector"/>): drop any queued VEGA speech and hide the
        /// panel, so an unattended screenshot run never catches the onboarding/greeting dialog in the frame.
        /// (Previously a fresh world needed a throwaway run just to get past the intro lines.)</summary>
        public void DismissSpeechForCapture()
        {
            _queue.Clear();
            _pages.Clear();
            _current = string.Empty;
            _currentKey = string.Empty;
            _choices = VegaChoiceSet.None; // #2499: an unattended run answers nothing
            _choiceInput.Reset();
            if (_speech != null)
            {
                _speech.SetActive(false);
            }

            HideTab(); // a folded line goes with the rest (#2126)
            SetPrologueChrome(false); // never hold an unattended capture run hostage on a story page
            EndStagedPrologue("capture dismiss");
        }

        /// <summary>Takes a line back before it is read (#2461, B16): the ending shows its epilogue on its own card a
        /// moment after the same text arrived here as a VEGA story line — it must not type into the panel a second
        /// time. Drops every queued line with this key and clears it from the screen if it is already up. The story
        /// log is unaffected (GameBootstrap records kind-2 lines on arrival).</summary>
        public void WithdrawLine(string lineKey)
        {
            if (string.IsNullOrEmpty(lineKey))
            {
                return;
            }

            _queue.Withdraw(lineKey);

            if (_speechText != null && _current.Length > 0 && string.Equals(_currentKey, lineKey, System.StringComparison.Ordinal))
            {
                _current = string.Empty;
                _currentKey = string.Empty;
                _choices = VegaChoiceSet.None;
                _choiceInput.Reset();
                _pages.Clear();
                _speechText.text = string.Empty;
                SetContinueVisible(false);
                StopChatter();
                HideTab();
            }
        }

        /// <summary>Starts/chains a chatter segment while a page is typing (#761): random variant (#2495: never the same twice
        /// in a row), a touch of pitch jitter so the babble never sounds looped, volume from the audio settings — the shared
        /// <see cref="NarratorVoice"/>. The existing per-line "ai_blip" stays as the radio keying transient. A line typing
        /// behind the loading curtain (veteran greeting, a landing transition) must not be audible before anything is
        /// visible — VEGA speaks only once the world does.</summary>
        private void UpdateChatter() => _voice?.Tick(true, _worldVeil != null && _worldVeil.VeilActive);

        /// <summary>Cuts the chatter the moment the reveal completes, is fast-forwarded or dismissed —
        /// trailing babble under a finished page reads as a glitch.</summary>
        private void StopChatter() => _voice?.Stop();

        /// <summary>True when any queued line still carries the prologue flag.</summary>
        private bool QueueHasPrologue() => _queue.HasPrologue;

        /// <summary>Winds down the staged prologue (#760), if it is running, and resets the staging state
        /// so a deliberately restarted onboarding can stage again.</summary>
        private void EndStagedPrologue(string reason)
        {
            if (!_prologueStarted && !_prologueStaged)
            {
                return;
            }

            Debug.Log($"[Cinematic] Prologue wind-down ({reason}).");
            if (_prologueStaged)
            {
                Cinematic?.End();
            }

            _prologueStaged = false;
            _prologueStarted = false;
            _prologueLineIndex = 0;
        }

        private void OnLine(ShipAiLine m)
        {
            _objectiveKey = m.ObjectiveKey ?? string.Empty;
            _objectiveArg = m.ObjectiveArg ?? string.Empty;
            _objectiveSource = m.ObjectiveSource ?? string.Empty;
            _objectivePackId = m.ObjectivePackId ?? string.Empty;
            _objProgress = m.ObjectiveProgress;
            _objTarget = m.ObjectiveTarget;

            // A story-pack cutscene that shows this very line itself (its replaceLineKeys, #2468) keeps it out of the
            // panel — the scene arrived just before its line. The director gives it back (Requeue) if the scene is
            // interrupted or given up, so the text is never lost; the story log recorded it on arrival either way.
            if (CutsceneDirector.Instance != null && CutsceneDirector.Instance.TryClaimLine(m))
            {
                Refresh();
                return;
            }

            EnqueueLine(m, flashback: true);
        }

        /// <summary>A narrator line a cutscene had taken over comes back as a normal page (#2468): the scene was interrupted
        /// (danger, multiplayer) or never found its moment. No flashback pulse — the moment has passed.</summary>
        public void Requeue(ShipAiLine m)
        {
            if (m != null)
            {
                EnqueueLine(m, flashback: false);
            }
        }

        private void EnqueueLine(ShipAiLine m, bool flashback)
        {
            if (m.Kind == 2 && flashback)
            {
                Flashback?.Trigger(); // a recovered memory gets the brief flashback treatment (#762)
            }

            // Advisor hints (1) and repeated context tips (5, #1077) share the settings mute; a repeated tip
            // is also dropped outright while VEGA still has lines waiting — situational advice must never
            // pile up behind a story beat or a lesson (the situation will still be there next time).
            bool muted = m.Kind is 1 or 5 && Settings is { VegaHints: false };
            if (m.Kind == 5 && _queue.Count >= 2)
            {
                muted = true;
            }

            Debug.Log($"[Cinematic] VEGA line kind={m.Kind} key={m.LineKey} textLen={m.Text?.Length ?? 0} muted={muted}");

            // #2499: the line's buttons (the first-start question, a course offer) — read once here; a line this client cannot
            // act on stays a plain line. The pack rides along so a skip can drop that pack's waiting lines.
            var choices = m.Kind == 4 ? VegaChoiceSet.None : VegaChoiceSet.Parse(m.Choices);

            // #2551: a story pack's line (the current tutorial or course step) goes ahead of the hints still waiting; hints wait
            // behind it; story beats, dialogue and given-back cutscene narration keep their order and are never dropped.
            var lineClass = VegaLineQueue.ClassOf(m.Kind, m.PackId);
            if (!string.IsNullOrEmpty(m.Text) && !muted)
            {
                // LLM-authored line (already in the player's language). The prologue flag keys on Kind,
                // NOT on the text source — an authored Kind-4 page must not lose its staging (#760).
                _queue.Enqueue(new VegaQueuedLine(m.Text, m.Kind == 4, m.LineKey ?? string.Empty, m.PackId, choices), lineClass, Now);
            }
            else if (!string.IsNullOrEmpty(m.LineKey) && !muted)
            {
                // {key:Action} → the control bound to that action on the device in hand (#1077) — so "your
                // suit lamp is on L" reads right after a rebind, on a pad, and on touch — expanded BEFORE the
                // packed {0}… arguments (#1079) are filled in: string.Format reads "{key:" as a broken placeholder
                // and threw the debris-field tip out of the poll for every pilot in 2026.10.8 (#2363).
                // #2500: a line of a story this client does not have reads as the neutral note, not as its raw key.
                string text = VegaText.Compose(StoryKeyText(m.LineKey, m.PackId), m.LineArg, KeyGlyphFor);

                // Prologue pages (Kind 4, #754) ride the normal queue with a flag — same panel, same
                // paging, plus the dim + the skip (Esc, pad cancel, the skip pill) while one is showing.
                _queue.Enqueue(new VegaQueuedLine(text, m.Kind == 4, m.LineKey, m.PackId, choices), lineClass, Now);
            }

            Refresh();
        }

        private void Refresh()
        {
            if (_chip == null)
            {
                return;
            }

            bool hasObjective = !string.IsNullOrEmpty(_objectiveKey);

            // The post-tutorial story objective (#1110) is optional coaching: unlike the onboarding chip
            // (which always shows) it respects the VEGA-hints mute, so a player who turned guidance off
            // gets their clean HUD back. The server names the source (#2462); the tutorial, the finale and
            // a mission are never muted — the old prefix rule hid "confront the Guardian core" as well.
            if (hasObjective && ObjectiveSourceRules.HiddenByHintsMute(
                    ObjectiveSourceRules.Effective(_objectiveSource, _objectiveKey), Settings is not { VegaHints: false }))
            {
                hasObjective = false;
            }

            _chip.SetActive(hasObjective);
            if (hasObjective)
            {
                // #2471: the story's short name replaces "Objective:" ("Training: Dock at a station", "VEGA: A signal on …").
                string counter = ObjectiveTracker.Counter(_objProgress, _objTarget);
                FitChip(ObjectiveTracker.ChipBody(ObjectivePrefix(_objectivePackId),
                    VegaObjectiveChip.Format(StoryKeyText(_objectiveKey, _objectivePackId), _objectiveArg)), counter); // #2500
            }

            _extraSig = int.MinValue; // the strips under the chip follow it
        }

        /// <summary>#2471 "Show all objectives": the objective list's other rows under the chip (<see cref="ObjectiveTracker.ExtraRows"/>
        /// — none while the setting is off, the chip is hidden or the chat is open; a story row hides with the VEGA hints like
        /// the chip). Cheap per frame: redrawn only when the list, the chip, the chat or a setting changed.</summary>
        private void UpdateExtraRows()
        {
            if (_extraRows[0] == null)
            {
                return;
            }

            var list = Game?.Objectives;
            bool chipOn = _chip != null && _chip.activeSelf;
            bool chatOpen = Game != null && Game.ChatTyping;
            bool showAll = Settings != null && Settings.ShowAllObjectives;
            bool hintsOn = Settings is not { VegaHints: false };
            float chipH = _chipRect != null ? _chipRect.sizeDelta.y : VegaObjectiveChip.HeightNormal;
            int sig;
            unchecked
            {
                sig = list?.GetHashCode() ?? 0; // reference identity: a new list is a new message
                sig = (sig * 31) + (Game?.StoryPackStates?.GetHashCode() ?? 0);
                sig = (sig * 31) + (chipOn ? 1 : 0) + (chatOpen ? 2 : 0) + (showAll ? 4 : 0) + (hintsOn ? 8 : 0);
                sig = (sig * 31) + (int)chipH;
            }

            if (sig == _extraSig)
            {
                return;
            }

            _extraSig = sig;
            var rows = chipOn ? ObjectiveTracker.ExtraRows(list, showAll, chatOpen, hintsOn) : null;
            for (int i = 0; i < _extraRows.Length; i++)
            {
                bool on = rows != null && i < rows.Count;
                if (_extraRows[i].activeSelf != on)
                {
                    _extraRows[i].SetActive(on);
                }

                if (!on)
                {
                    continue;
                }

                var row = rows[i];
                string body = ObjectiveTracker.ChipBody(ObjectivePrefix(row.PackId), VegaObjectiveChip.Format(StoryKeyText(row.Key, row.PackId), row.Arg));
                string counter = ObjectiveTracker.Counter(row.Progress, row.Target);
                _extraTexts[i].text = counter.Length > 0 ? body + "  " + counter : body;
                _extraRows[i].GetComponent<RectTransform>().anchoredPosition =
                    new Vector2(24f, -(ChipY + chipH + ExtraRowGap + (i * (ExtraRowH + ExtraRowGap))));
            }
        }

        /// <summary>Fits the objective into the chip (#1859). One line at the large font when it fits the text
        /// width; otherwise the small font, the counter on its own line (so "(128/204)" is never the part the
        /// truncation eats), and the chip grows a row when the wrapped text needs three lines. Measured with
        /// TMP's preferred sizes: <c>preferredWidth</c> is the unwrapped single-line width, <c>preferredHeight</c>
        /// the wrapped height at the rect's width.</summary>
        private void FitChip(string body, string counter)
        {
            _chipText.fontSize = VegaObjectiveChip.FontLarge;
            _chipText.text = VegaObjectiveChip.Compose(body, counter, ownLine: false);
            bool oneLine = _chipText.preferredWidth <= VegaObjectiveChip.TextWidth;
            if (!oneLine)
            {
                _chipText.fontSize = VegaObjectiveChip.FontSmall;
                _chipText.text = VegaObjectiveChip.Compose(body, counter, ownLine: true);
            }

            float height = VegaObjectiveChip.Height(oneLine ? 0f : _chipText.preferredHeight);
            if (_chipRect != null && !Mathf.Approximately(_chipRect.sizeDelta.y, height))
            {
                _chipRect.sizeDelta = new Vector2(_chipRect.sizeDelta.x, height);
                _chipText.rectTransform.sizeDelta = new Vector2(VegaObjectiveChip.TextWidth, height);
            }
        }

        /// <summary>Real-world "take a break" nudge: once the player has been in this session for the chosen
        /// number of minutes (and again every interval after), VEGA enqueues a break reminder. This is driven
        /// purely client-side from the session wall-clock — deliberately NOT a server <see cref="ShipAiLine"/>,
        /// so the meta hint never lands in the Story Log. Gated on the comfort setting; the wording escalates.</summary>
        private void CheckPlaytimeReminder()
        {
            if (Game == null || Settings is not { PlaytimeReminder: true })
            {
                return;
            }

            float interval = Mathf.Max(1, Settings.ReminderMinutes) * 60f;
            if (Game.SessionSeconds < (_remindersSent + 1) * interval)
            {
                return;
            }

            _remindersSent++;
            string key = _remindersSent switch
            {
                1 => "ui.reminder.break",
                2 => "ui.reminder.break.again",
                _ => "ui.reminder.break.long",
            };
            _queue.Enqueue(new VegaQueuedLine(L(key), false, key), VegaLineClass.Story, Now); // shows via the normal typewriter path; bypasses the VegaHints mute by design
        }

        /// <summary>#2500: this world plays a story the client does not have (GameBootstrap noted it from the story list or the
        /// story state) — the panel says so once per world session: "update the game to follow it". Like the break reminder it
        /// bypasses the VegaHints mute (it explains why that story's lines read "needs a newer version") and is not kept in the
        /// notes.</summary>
        private void CheckUnknownStoryNotice()
        {
            if (Game == null || !Game.UnknownStories.TakeNotice())
            {
                return;
            }

            _queue.Enqueue(new VegaQueuedLine(L(UnknownStoryPacks.NoticeKey), false, UnknownStoryPacks.NoticeKey), VegaLineClass.Story, Now);
            Refresh();
        }

        /// <summary>True when the continue control should be ignored: the in-game menu is open, or a text
        /// field (chat, beacon label) currently has keyboard focus. Only a focused INPUT FIELD counts —
        /// uGUI also leaves an ordinary Button selected after any click, and with pad focus
        /// (<see cref="UiNavFocus"/>) something is selected most of the time; treating that as "captured"
        /// left the panel stuck after the first HUD click / on a pad (#1041). And only a field that is
        /// ACTIVE and FOCUSED: uGUI keeps a closed InputField as the selected object (nothing deselects on
        /// deactivate), so the chat box closed with Esc or Enter counted as "captured" until the next
        /// world click — N did nothing on a VEGA line that arrived mid-chat (#1634).</summary>
        private bool InputCaptured()
        {
            if (Game != null && Game.MenuOpen)
            {
                return true;
            }

            var selected = UnityEngine.EventSystems.EventSystem.current?.currentSelectedGameObject;
            if (selected == null || !selected.activeInHierarchy)
            {
                return false;
            }

            var field = selected.GetComponent<InputField>();
            return field != null && field.isFocused;
        }

        /// <summary>True while a VEGA line is on screen (typing or waiting for continue) — gates the touch
        /// NEXT button and the context-actions entry (#1041); the panel itself stays non-modal.</summary>
        public bool LineShowing => _speechText != null && _current.Length > 0;

        private InputDeviceKind _hintDevice = (InputDeviceKind)(-1);

        private void Update()
        {
            if (_speechText == null)
            {
                return;
            }

            CheckPlaytimeReminder();
            CheckUnknownStoryNotice();
            UpdateExtraRows(); // #2471: "Show all objectives" (cheap unless something changed)

            // #2551: a lesson the client raised for a moment that has passed — it waited unseen behind other lines (or a folded
            // tab, or a scene) for 30 s — goes; its once-flag stays unspent, so it comes again when the situation does.
            int expired = _queue.Expire(Now);
            if (expired > 0)
            {
                Debug.Log($"[VEGA] {expired} waiting lesson(s) expired unseen (queued={_queue.Count}).");
            }

            // The ending (or any later staged scene) owns the screen (#2461, B16): VEGA neither types nor talks under
            // it, and no line starts in the moment before it takes over. The queue simply waits and picks up where it
            // stopped. The prologue is the one scene VEGA presents herself, so it never silences her — and a pending
            // ending waits for a running prologue instead (ResolutionCinematic.CanStart).
            // A director card (T2, free input) does not open the shared state, but it speaks in the frame — VEGA waits for it
            // too (#2468), so the two never talk at once and the continue key reaches only the card.
            // #2497: under a card (T2, free input) she only falls silent — her speech panel hides, the objective chip stays, as the
            // HUD does under a card; every other scene takes the whole panel away.
            var ending = ResolutionCinematic.Instance;
            bool hideAll = GameBootstrap.Cutscenes.ActiveOtherThan(CutsceneGate.Prologue)
                           || (ending != null && ending.Pending && !_prologueStarted)
                           || (CutsceneDirector.HoldsScreen && !CutsceneDirector.ShowsCard);
            bool yieldToScene = hideAll || CutsceneDirector.HoldsScreen;
            if (_canvas.enabled == hideAll)
            {
                _canvas.enabled = !hideAll;
            }

            SetSpeechYielded(yieldToScene && !hideAll);

            if (yieldToScene)
            {
                StopChatter();
                _clicked = false;
                _skipClicked = false;
                _choiceInput.Reset(); // #2499: a hold under a scene is no answer
                return;
            }

            if (_current.Length == 0 && _queue.Count > 0)
            {
                // No line starts behind the loading curtain (#760/#761): a page typing into blackness
                // is inaudible-invisible. Bounded inside HoldQueue.
                if (Cinematic != null && Cinematic.HoldQueue)
                {
                    return;
                }

                // Story pages open the narrative: a line that arrives in FRONT of the prologue on join —
                // the join's beat catch-up (B0 "systems online", Kind 2) is sent before ShipAiOnJoin queues the
                // prologue — would otherwise show first and stall the cinematic behind a keypress. Stable
                // partition — prologue pages first, everything else after, both in their original order.
                if (!_prologueStarted && _queue.PromotePrologue())
                {
                    Debug.Log($"[Cinematic] Reordered queue: story pages ahead of the other line(s) ({_queue.Count} queued).");
                }

                // Coming up on screen now: a lesson's once-flag is spent here (#2551, the shown callback), not when it queued.
                _queue.TryDequeue(out var queued);
                string text = queued.Text;
                bool prologue = queued.Prologue;
                _currentIsPrologue = prologue;
                _currentKey = queued.Key ?? string.Empty;
                _choices = prologue ? VegaChoiceSet.None : queued.Choices ?? VegaChoiceSet.None; // #2499
                _choiceInput.Reset();
                Debug.Log($"[Cinematic] Dequeued line (prologue={prologue}, started={_prologueStarted}, staged={_prologueStaged}, queued={_queue.Count})");
                if (prologue)
                {
                    if (!_prologueStarted)
                    {
                        _prologueStarted = true;
                        _prologueLineIndex = 0;
                        _stagingRetry.Reset();
                        _prologueStaged = TryStagePrologue();
                    }
                    else
                    {
                        _prologueLineIndex++;
                    }

                    if (_prologueStaged)
                    {
                        Cinematic.OnPrologueLine(_prologueLineIndex);
                    }
                }
                else if (_prologueStarted && !QueueHasPrologue())
                {
                    // The story pages are over but the queue continues straight into the normal
                    // onboarding lines (they arrive together on join) — the idle branch below would
                    // never run, so wind the staging down here (#760). Only once NO further prologue
                    // page is queued: a stray non-prologue line interleaved between story pages must
                    // display over the running stage, not kill it.
                    EndStagedPrologue("non-prologue dequeue");
                }

                SetPrologueChrome(prologue);
                _pages.Clear();
                _pages.AddRange(SplitPages(text));
                _speech.SetActive(true);
                ClientAudio.Instance?.Cue("ai_blip"); // VEGA's radio chirp
                ShowPage(0);
            }

            if (_current.Length == 0)
            {
                if (_speech.activeSelf)
                {
                    _speech.SetActive(false);
                }

                HideTab();
                _clicked = false;
                _skipClicked = false;
                _currentKey = string.Empty;
                _choices = VegaChoiceSet.None; // #2499
                _choiceInput.Reset();
                SetPrologueChrome(false);
                _currentIsPrologue = false;
                EndStagedPrologue("queue idle"); // finished or skipped — camera back, letterbox out (#760)
                return;
            }

            // A staging that failed on the first page is tried again while the orbit ring streams in (#2461, B19) —
            // only on the two camera pages (the third releases the camera anyway).
            if (_prologueStarted && !_prologueStaged && _currentIsPrologue && _prologueLineIndex < 2
                && _stagingRetry.Due(Time.unscaledTime))
            {
                _prologueStaged = TryStagePrologue();
                if (_prologueStaged)
                {
                    Debug.Log($"[Cinematic] Prologue staged on a retry (line {_prologueLineIndex}).");
                    Cinematic.OnPrologueLine(_prologueLineIndex);
                    SetPrologueChrome(true); // the letterbox is the chrome now — the dim goes
                }
            }

            UpdateSkipPill();

            // Skip the whole prologue (#754) — on every device since #2461: Esc or the pad's cancel button
            // (InputAction.UiCancel), or a click/tap on the skip pill. Drops the current story page and every queued
            // one, so a returning player isn't forced through the narration again. Normal lines keep Esc for the menu.
            bool skipClicked = _skipClicked;
            _skipClicked = false;
            if (_currentIsPrologue && ((CinematicFrame.SkipPressed() && !InputCaptured()) || skipClicked))
            {
                Game?.MarkMenuInputHandled(); // this Esc is spent here, whatever runs later this frame
                _queue.DropLeadingPrologue();

                _current = string.Empty;
                _currentKey = string.Empty;
                _pages.Clear();
                _speechText.text = string.Empty;
                SetContinueVisible(false);
                StopChatter();
                return;
            }

            // Continue is a rebindable action (#1041): the bound key (default N), the pad's Back button, the
            // touch NEXT button and the context-actions list all land here — the panel used to poll the
            // raw N key, which no pad or touch device could ever press. A click/tap on the continue pill or the
            // folded tab counts the same (#2126) — an explicit press, so it is honoured even over a menu.
            bool captured = InputCaptured();
            bool clicked = _clicked;
            _clicked = false;
            bool keyDown = InputMap.Down(InputAction.VegaContinue) && !captured;
            bool pressed = keyDown || clicked;

            // Folded into the tab (#2126): the continue control opens it again — it never skips the line unread.
            if (_collapse.Collapsed)
            {
                _choiceInput.Reset(); // #2499: opening the tab is no answer
                if (pressed)
                {
                    ExpandFromTab();
                }
                else
                {
                    UpdateTab();
                }

                return;
            }

            // The hint names the control for the device in hand; re-render it when the player switches
            // device mid-line (picks up the pad, taps the screen).
            if (ContinueVisible && InputMap.ActiveDevice != _hintDevice)
            {
                ShowContinueHint();
            }

            if (_shown < _current.Length)
            {
                // Still typing: the continue key fast-completes the reveal instead of skipping the page.
                _shown = pressed ? _current.Length : Mathf.Min(_current.Length, _shown + Time.deltaTime * CharsPerSecond);
                int visible = (int)_shown;
                if (visible != _shownChars) // #1554: one Substring per revealed character, not per frame
                {
                    _shownChars = visible;
                    _speechText.text = _current.Substring(0, visible);
                }

                if (_shown >= _current.Length)
                {
                    StopChatter();
                    ShowContinueHint();
                }
                else
                {
                    UpdateChatter();
                }

                return;
            }

            // Fully revealed: wait for the continue key — and ONLY the key (#1011). The old 25 s
            // auto-advance made hints and story pages vanish before slow readers got through them;
            // nothing gameplay-critical blocks on the panel (later lines just queue), and unattended
            // capture runs clear it via DismissSpeechForCapture. A page left waiting folds into the tab
            // instead (#2126) — out of the way, but never gone.
            // #2499: a line with buttons, on its last page: a click or tap picks that pill; the continue control picks the
            // primary when it is let go, and held for VegaChoiceInput.HoldSeconds the secondary — the deliberate route on a
            // keyboard (the cursor is captured on foot) and on a pad (no stock button is free). Esc never answers.
            if (ChoicesOnPage)
            {
                bool hasSecondary = _choices.Secondary != null;
                var pick = VegaChoicePick.None;
                if (skipClicked && hasSecondary)
                {
                    pick = VegaChoicePick.Secondary;
                }
                else if (clicked)
                {
                    pick = VegaChoicePick.Primary;
                }
                else if (captured)
                {
                    _choiceInput.Reset(); // a menu or a text field took the keys — a press there answers nothing
                }
                else
                {
                    pick = _choiceInput.Tick(keyDown, InputMap.Held(InputAction.VegaContinue), Time.unscaledDeltaTime, hasSecondary);
                }

                if (pick != VegaChoicePick.None)
                {
                    Choose(pick);
                }
                else
                {
                    TintHold(_choiceInput.HoldProgress);
                    if (!_choiceInput.Holding
                        && _collapse.Tick(Time.unscaledDeltaTime, fullyRevealed: true, prologue: false, hudHidden: captured))
                    {
                        CollapseToTab();
                    }
                }

                return;
            }

            if (pressed)
            {
                if (_page < _pages.Count - 1)
                {
                    ShowPage(_page + 1); // the line continues on the next page (#736)
                }
                else
                {
                    EndLine();
                }
            }
            else if (_collapse.Tick(Time.unscaledDeltaTime, fullyRevealed: true, prologue: _currentIsPrologue, hudHidden: captured))
            {
                CollapseToTab();
            }
        }

        /// <summary>The line on screen goes, and the next one may come up. <paramref name="readThrough"/>: the player continued
        /// past it — off for an answer to a line's buttons (#2499), which is a choice, not a lesson read through (a "read" step
        /// waiting behind the question must not tick off unseen).</summary>
        private void EndLine(bool readThrough = true)
        {
            _current = string.Empty;
            _pages.Clear();
            _speechText.text = string.Empty;
            SetContinueVisible(false);
            _choices = VegaChoiceSet.None;
            _choiceInput.Reset();
            if (readThrough)
            {
                // #2469: the player read the line through — a tutorial "read" step waits for exactly this. Teaching only;
                // the server ignores it unless a step listens (and an older server drops the tag).
                Game?.Network?.SendUiTutorialEvent(BlocksBeyondTheStars.Shared.Story.GameEventKeys.UiVegaContinueId);
            }
        }

        // ---- A line's buttons (#2499) ---------------------------------------------------------------------------

        /// <summary>The line on screen has buttons and its last page is up.</summary>
        private bool ChoicesOnPage => _choices.HasAny && _pages.Count > 0 && _page == _pages.Count - 1 && _current.Length > 0;

        /// <summary>The secondary button shows: the line's last page has typed out (it appears with the primary).</summary>
        private bool SecondaryChoiceShowing => ChoicesOnPage && _choices.Secondary != null && _shown >= _current.Length;

        /// <summary>Acts on the picked button: its intent goes to the server (a plain continue sends none), a skip drops the
        /// skipped pack's lines still waiting here — they teach what the player just said they know — and the line ends
        /// (without the "read through" flag). The server answers a skip of the first-start question with one more line.</summary>
        private void Choose(VegaChoicePick pick)
        {
            var choice = pick == VegaChoicePick.Secondary ? _choices.Secondary : _choices.Primary;
            var pill = pick == VegaChoicePick.Secondary ? _skipPill : _continuePill;
            if (pill != null)
            {
                UiHolo.Flash(pill.GetComponent<UiHolo.Shape>());
            }

            if (choice != null && choice.SendsIntent)
            {
                Game?.Network?.SendStoryPack(choice.PackId, choice.Action, choice.ChapterId);
                if (choice.DropsPackLines)
                {
                    int dropped = _queue.DropPack(choice.PackId);
                    Debug.Log($"[VEGA] Choice '{choice.Action}' for '{choice.PackId}': dropped {dropped} waiting line(s) of the pack.");
                }
            }

            TintHold(0f);
            EndLine(readThrough: false);
        }

        /// <summary>The secondary pill brightens while the continue control is held towards it.</summary>
        private void TintHold(float progress)
        {
            if (_skipHint != null && _skipPillChoice)
            {
                _skipHint.color = Color.Lerp(UiKit.Cyan, Color.white, Mathf.Clamp01(progress));
            }
        }

        private void ShowPage(int index)
        {
            _page = index;
            _current = _pages.Count > 0 ? _pages[index] : string.Empty;
            _shown = 0f;
            _shownChars = -1;
            _speechText.text = string.Empty;
            SetContinueVisible(false);
            _choiceInput.Reset(); // #2499: a press from the page before is no answer
            _collapse.Reset(); // every page gets its own wait before it may fold (#2126)
        }

        // ---- The continue pill and the folded tab (#2126) ------------------------------------------------------

        /// <summary>Makes a holo panel a click/tap target that acts as the continue control — or, with
        /// <paramref name="skip"/>, as the prologue's skip (#2461). No pad stop (the pad has its own continue and cancel
        /// buttons) and no selection on click — a selected button would also fire on Space/Enter, and Space is jump.</summary>
        private void MakeClickable(Image target, bool skip = false)
        {
            target.raycastTarget = true;
            var button = target.gameObject.AddComponent<Button>();
            button.targetGraphic = target;
            button.transition = Selectable.Transition.None; // the holo shape carries the look (its glow flashes on click)
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.onClick.AddListener(UiSound.Click);
            button.onClick.AddListener(() =>
            {
                if (skip)
                {
                    _skipClicked = true;
                }
                else
                {
                    _clicked = true;
                }

                UiHolo.Flash(target.GetComponent<UiHolo.Shape>());
            });
        }

        /// <summary>Tries to stage the prologue and books the attempt for the retry schedule (#2461, B19).</summary>
        private bool TryStagePrologue()
        {
            bool staged = Cinematic != null && Cinematic.Begin();
            _stagingRetry.Record(Time.unscaledTime, staged);
            return staged;
        }

        private InputDeviceKind _skipDevice = (InputDeviceKind)(-1);
        private string _skipLabelKey = string.Empty; // #2499: the secondary button's label key last drawn on the pill

        /// <summary>Shows the skip pill on every prologue page — from its first character, not only once it has typed out —
        /// labelled for the device in hand (#2461): "[Esc]", the pad's cancel button, or a plain tap target on touch. Since
        /// #2499 the same pill carries a line's secondary button once its last page has typed out ("Hold [N]: I know the game
        /// — skip"; on touch the label alone — the pill is tapped).</summary>
        private void UpdateSkipPill()
        {
            if (_skipPill == null)
            {
                return;
            }

            bool choice = !_currentIsPrologue && SecondaryChoiceShowing;
            bool want = (_currentIsPrologue && _current.Length > 0) || choice;
            if (_skipPill.gameObject.activeSelf != want)
            {
                _skipPill.gameObject.SetActive(want);
                _skipDevice = (InputDeviceKind)(-1); // re-measure on the next show
            }

            string labelKey = choice ? _choices.Secondary.LabelKey : string.Empty;
            if (!want || (InputMap.ActiveDevice == _skipDevice && choice == _skipPillChoice && labelKey == _skipLabelKey))
            {
                return;
            }

            _skipDevice = InputMap.ActiveDevice;
            _skipPillChoice = choice;
            _skipLabelKey = labelKey;
            _skipHint.text = choice ? SecondaryLabel(_choices.Secondary) : CinematicFrame.SkipLabel(_localize);
            _skipHint.color = choice ? UiKit.Cyan : UiKit.CyanDim;
            float width = Mathf.Clamp(_skipHint.preferredWidth + ContinuePad, 90f, choice ? SecondaryMaxW : 300f);
            _skipPill.sizeDelta = new Vector2(width, ContinueH);
            _skipHint.rectTransform.sizeDelta = new Vector2(width, ContinueH);
        }

        // #2499: the two choice pills share the panel's bottom edge (640 wide, 14 in from each side) — the secondary left, the
        // primary right — so neither may grow into the other.
        private const float SecondaryMaxW = 360f, PrimaryMaxW = 230f;

        /// <summary>The control name on a pill for the device in hand: the bound key, the pad glyph, nothing on touch (the pill
        /// itself is tapped).</summary>
        private static string ChoiceControl(InputDeviceKind device) => device switch
        {
            InputDeviceKind.Touch => string.Empty,
            InputDeviceKind.Gamepad => InputMap.Glyph(InputAction.VegaContinue),
            _ => "[" + InputMap.Key(InputAction.VegaContinue) + "]",
        };

        /// <summary>"Hold [N]: I know the game — skip" (#2499) — the label alone on touch.</summary>
        private string SecondaryLabel(VegaChoice choice)
            => VegaChoiceLabels.Secondary(L(choice.LabelKey), L("ui.vega.choice.hold"), ChoiceControl(InputMap.ActiveDevice));

        /// <summary>A touch tap the full-screen touch look pad caught first (its canvas sits above the HUD): counts as a
        /// tap on the continue pill or the folded tab when it lands on one (#2126).</summary>
        public void TapAt(Vector2 screenPos)
        {
            if (_canvas == null)
            {
                return;
            }

            // Same camera the canvas's own GraphicRaycaster uses: none for an overlay, the visor HUD camera otherwise.
            var cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            if (_skipPill != null && _skipPill.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_skipPill, screenPos, cam))
            {
                _skipClicked = true; // the prologue's skip pill (#2461)
                UiSound.Click();
                return;
            }

            bool onTab = _tab != null && _tab.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_tabRect, screenPos, cam);
            bool onPill = _continuePill != null && _continuePill.gameObject.activeInHierarchy
                && RectTransformUtility.RectangleContainsScreenPoint(_continuePill, screenPos, cam);
            if (onTab || onPill)
            {
                _clicked = true;
                UiSound.Click();
            }
        }

        private bool ContinueVisible => _continuePill != null && _continuePill.gameObject.activeSelf;

        private void SetContinueVisible(bool on)
        {
            if (_continuePill != null && _continuePill.gameObject.activeSelf != on)
            {
                _continuePill.gameObject.SetActive(on);
            }
        }

        /// <summary>Folds the waiting page into the tab: the speech panel goes, the text stays where it is.</summary>
        private void CollapseToTab()
        {
            StopChatter();
            _speech.SetActive(false);
            _tabCount = -1; // draw fresh, without the arrival flash
            _tab.SetActive(true);
            UpdateTab();
        }

        /// <summary>Opens the tab again: the same page, fully revealed, with a fresh wait.</summary>
        private void ExpandFromTab()
        {
            _collapse.Expand();
            HideTab();
            _speech.SetActive(true);
            ShowContinueHint();
            ClientAudio.Instance?.Cue("ai_blip");
        }

        private void HideTab()
        {
            if (_tab != null && _tab.activeSelf)
            {
                _tab.SetActive(false);
            }

            _collapse.Reset();
            _tabCount = -1;
        }

        /// <summary>#2497: under a card VEGA's speech panel, her folded tab and the prologue dim step aside (transparent, no
        /// clicks) while the objective chip and its extra rows stay — the player keeps playing under a card. Their own
        /// active state is left alone, so the line she was saying shows again as it was once the card is gone.</summary>
        private void SetSpeechYielded(bool yielded)
        {
            if (yielded == _speechYielded || _speech == null)
            {
                return;
            }

            _speechYielded = yielded;
            if (_speechGroup == null)
            {
                _speechGroup = _speech.AddComponent<CanvasGroup>();
            }

            if (_tabGroup == null && _tab != null)
            {
                _tabGroup = _tab.AddComponent<CanvasGroup>();
            }

            foreach (var group in new[] { _speechGroup, _tabGroup })
            {
                if (group != null)
                {
                    group.alpha = yielded ? 0f : 1f;
                    group.blocksRaycasts = !yielded;
                    group.interactable = !yielded;
                }
            }

            if (_dim != null)
            {
                _dim.enabled = !yielded;
            }
        }

        /// <summary>Keeps the tab's text, size and row spot current: the waiting lines (flashing when one more arrives),
        /// the control that opens it for the device in hand, and beside the objective chip when that is up.</summary>
        private void UpdateTab()
        {
            int count = VegaCollapse.WaitingLines(_queue.Count);
            bool besideChip = _chip != null && _chip.activeSelf;
            if (count == _tabCount && InputMap.ActiveDevice == _tabDevice && besideChip == _tabBesideChip)
            {
                return;
            }

            if (_tabCount > 0 && count > _tabCount)
            {
                UiHolo.Flash(_tab.GetComponent<UiHolo.Shape>(), 3f, 1.3f, 0.6f); // another line is waiting
                ClientAudio.Instance?.Cue("ai_blip");
            }

            _tabCount = count;
            _tabDevice = InputMap.ActiveDevice;
            _tabBesideChip = besideChip;
            string control = _tabDevice switch
            {
                InputDeviceKind.Touch => L("ui.touch.next"),
                InputDeviceKind.Gamepad => InputMap.Glyph(InputAction.VegaContinue),
                _ => "[" + InputMap.Key(InputAction.VegaContinue) + "]",
            };
            _tabText.text = string.Format(L(count == 1 ? "ui.vega.tab_one" : "ui.vega.tab_many"), count, control);

            float width = Mathf.Clamp(TabTextX + _tabText.preferredWidth + 14f, TabMinW, TabMaxW);
            float x = besideChip ? 24f + 640f + TabGap : 24f;
            _tabRect.sizeDelta = new Vector2(width, TabH);
            _tabRect.anchoredPosition = new Vector2(x, -ChipY);
        }

        private void ShowContinueHint()
        {
            // The control name follows the active device (#1041): the bound key on keyboard, the pad glyph
            // on a gamepad, the on-screen NEXT button on touch. "ui.vega.next" (the old fixed "[N]" text)
            // stays in the locale files for community locales that still carry it.
            _hintDevice = InputMap.ActiveDevice;
            string next = _hintDevice switch
            {
                InputDeviceKind.Touch => L("ui.vega.next_touch"),
                InputDeviceKind.Gamepad => string.Format(L("ui.vega.next_pad"), InputMap.Glyph(InputAction.VegaContinue)),
                _ => string.Format(L("ui.vega.next_key"), InputMap.Key(InputAction.VegaContinue)),
            };

            // Multi-page lines get a page indicator so "Continue" visibly means "next page", not "dismiss".
            string hint = _pages.Count > 1
                ? next + "  " + string.Format(L("ui.vega.page"), _page + 1, _pages.Count)
                : next;

            // #2499: on a line's last page its primary button takes the pill ("Let's go · [N]", "Later · (Back)"); a line
            // whose first button this client could not read keeps the plain "Continue".
            bool primaryChoice = ChoicesOnPage && _choices.Primary.LabelKey.Length > 0;
            if (primaryChoice)
            {
                hint = VegaChoiceLabels.Primary(L(_choices.Primary.LabelKey), ChoiceControl(_hintDevice));
            }

            // The skip lives in its own pill on prologue pages (UpdateSkipPill, #2461) — clicking this one continues.

            // The pill hugs its text (#2126), right-aligned at the panel's bottom edge (measured live — TMP lays out
            // against an active object).
            SetContinueVisible(true);
            _continueHint.text = hint;
            bool sharesEdge = primaryChoice || (ChoicesOnPage && _choices.Secondary != null); // the secondary pill sits left of it
            float width = Mathf.Min(sharesEdge ? PrimaryMaxW : SpeechTextW, _continueHint.preferredWidth + ContinuePad);
            _continuePill.sizeDelta = new Vector2(width, ContinueH);
            _continuePill.anchoredPosition = new Vector2(640f - ContinueRight - width, -ContinueY);
            _continueHint.rectTransform.sizeDelta = new Vector2(width, ContinueH);
        }

        /// <summary>Shows/hides the story-page chrome (#754): the dim behind the speech panel, and the
        /// <see cref="GameBootstrap.VegaPrologueActive"/> flag AppShell checks so Esc skips the story
        /// instead of opening the leave-game menu. Since #2461 the prologue is also a cutscene in the shared
        /// <see cref="GameBootstrap.Cutscenes"/> state for as long as a story page shows — staged or not: the controls
        /// freeze, and the HUD, the touch controls, the chat and the nameplates step aside.</summary>
        private void SetPrologueChrome(bool on)
        {
            // With the cinematic staging up (#760) the letterbox is the chrome — the dim would just
            // darken the very scene the camera is orbiting.
            bool dimOn = on && !_prologueStaged;
            if (_dim != null && _dim.gameObject.activeSelf != dimOn)
            {
                _dim.gameObject.SetActive(dimOn);
            }

            if (Game != null)
            {
                Game.VegaPrologueActive = on;
            }

            GameBootstrap.Cutscenes.Set(CutsceneGate.Prologue, on);

            // The objective chip is HUD, not story: the onboarding objective arrives with the queued
            // lines while the prologue still shows, and would float over the letterboxed shot. Hide it
            // for the duration; Refresh() restores it from the stored objective afterwards. Edge-gated —
            // this runs every idle frame with on=false, and Refresh() builds strings.
            if (on != _chromeOn)
            {
                _chromeOn = on;
                if (_chip != null)
                {
                    if (on)
                    {
                        _chip.SetActive(false);
                    }
                    else
                    {
                        Refresh();
                    }
                }
            }
        }

        /// <summary>Splits a line into pages that fit the speech box, cutting only on wrap-line boundaries.
        /// TMP lays the text out against the label's own rect (HUD reference units, independent of the canvas
        /// scale) without rendering it; each line's first character maps back to its index in the raw string,
        /// so pages cut on the same boundaries the legacy TextGenerator measurement did.</summary>
        private List<string> SplitPages(string text)
        {
            bool wasActive = _speech.activeSelf;
            if (!wasActive)
            {
                _speech.SetActive(true); // TMP needs a live rect to lay out against
            }

            var prevOverflow = _speechText.overflowMode;
            _speechText.overflowMode = TMPro.TextOverflowModes.Overflow;
            var info = _speechText.GetTextInfo(text);
            _speechText.overflowMode = prevOverflow;
            if (!wasActive)
            {
                _speech.SetActive(false);
            }

            int lineCount = info != null ? info.lineCount : 0;
            var starts = new List<int>(lineCount);
            var heights = new List<float>(lineCount);
            for (int i = 0; i < lineCount; i++)
            {
                var line = info.lineInfo[i];
                int first = line.firstCharacterIndex;
                int raw = first >= 0 && first < info.characterCount ? info.characterInfo[first].index : 0;
                starts.Add(Mathf.Clamp(raw, 0, text.Length));
                heights.Add(Mathf.Max(line.lineHeight, 1f));
            }

            var pages = new List<string>();
            foreach (var (start, length) in VegaText.PageRanges(starts, heights, text.Length, SpeechTextH))
            {
                string page = text.Substring(start, length).Trim();
                if (page.Length > 0)
                {
                    pages.Add(page);
                }
            }

            if (pages.Count == 0)
            {
                pages.Add(text); // measurement produced nothing usable — behave like the unpaged panel
            }

            return pages;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            if (Game?.Network != null)
            {
                Game.Network.ShipAiLineReceived -= OnLine;
            }

            SetPrologueChrome(false); // clears GameBootstrap.VegaPrologueActive with the rig teardown

            // The panel canvas is a root-level object (CreateDiegeticCanvas) — destroy it with the rig,
            // or the objective chip would keep floating over the main menu after leaving the world.
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }

    /// <summary>The objective chip's pure text/layout rules (#1859), kept free of any canvas so the EditMode
    /// tests can pin them: the {0} substitution with its fallback, the counter placement, and the chip height.
    /// The geometry constants are the chip's: 640×48 at x 24 / y <see cref="VegaPanel.ChipY"/> in the left
    /// column; the tall variant (72) reaches 16 px into the scan panel's lane (y 650) — accepted for the rare
    /// three-line objective, since the scan panel only shows while scanning.</summary>
    public static class VegaObjectiveChip
    {
        public const float FontLarge = 20f;
        public const float FontSmall = 17f;
        public const float HeightNormal = 48f;
        public const float HeightTall = 72f;
        public const float TextWidth = 614f;

        /// <summary>The objective line with its {0} argument filled in — the template as-is when there is no
        /// argument, and also when the template is not a valid format string (a stray brace in a translation
        /// must never blank the chip).</summary>
        public static string Format(string template, string arg)
        {
            if (string.IsNullOrEmpty(template) || string.IsNullOrEmpty(arg))
            {
                return template ?? string.Empty;
            }

            try
            {
                return string.Format(template, arg);
            }
            catch (System.FormatException)
            {
                return template;
            }
        }

        /// <summary>Objective text + counter: inline after two spaces on a short line, on its own line when the
        /// objective already needs wrapping (the counter must never be the part that gets cut).</summary>
        public static string Compose(string body, string counter, bool ownLine)
            => string.IsNullOrEmpty(counter) ? body ?? string.Empty : (body ?? string.Empty) + (ownLine ? "\n" : "  ") + counter;

        /// <summary>Chip height for a wrapped text height: the normal row while two small-font lines fit, the
        /// tall row (three lines) beyond that.</summary>
        public static float Height(float wrappedTextHeight)
            => wrappedTextHeight <= HeightNormal ? HeightNormal : HeightTall;
    }
}
