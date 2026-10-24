// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The client cutscene director (#2468, synthesis §6.2/§6.3, d4 §3.3). The server sends a story pack's scene as an id
    /// (<see cref="CutscenePlay"/>, tag 295, only to a client that announced <c>cutscenes1</c> — this component's existence
    /// makes the join announce it); the director resolves it from its own copy of the pack's <c>cutscenes.json</c>, waits
    /// for the player's safe moment (<see cref="SafeMomentGate"/>), plays it and confirms it (<see cref="CutsceneSeenIntent"/>,
    /// tag 296, with <c>Skipped</c>/<c>Unsupported</c>).
    /// <list type="bullet">
    /// <item><b>Tiers:</b> T1 a top banner (no input lock, no hold); T2 a card in a thin letterbox — title card, speaker box,
    /// reading frame — over the live view with free input; T3/T4 staged scenes: full letterbox, the camera on real objects
    /// (<see cref="CutsceneStaging"/>, flown by <see cref="CutsceneCameraRig"/>), HUD hidden and controls frozen through
    /// <see cref="GameBootstrap.Cutscenes"/>, and a singleplayer world held (<see cref="WorldHoldArbiter.Cutscene"/>) only
    /// while the camera is taken.</item>
    /// <item><b>Text:</b> captions localized with the one key-token resolver (<see cref="HudUi.KeyTokenGlyph"/>); the play's
    /// argument may be a locale key (a chapter title) or a <c>__recap</c> list; "Story text: wait for me" pages subtitles,
    /// speaker boxes and reading frames on the reader's continue (title cards always run on their own). Narrator lines a
    /// scene shows itself (<c>replaceLineKeys</c>) stay out of VEGA's panel while the scene is owed, and go back to it when
    /// the scene is interrupted or given up — text is never lost.</item>
    /// <item><b>Skip:</b> one press of Esc / the pad's cancel button (a camera-taking scene; on a card, where B crouches, the
    /// keyboard's Esc), or a click/tap on the skip button — after the scene's <c>minDisplay</c>, at once on a replay.</item>
    /// <item><b>Multiplayer:</b> damage or a hostile closing in ends a scene (the text goes back to the panel, a banner points
    /// to the Story tab); the camera returns after 30 s and the captions go on as a card; nothing ever holds the world.</item>
    /// <item><b>Small hardware / WebGL:</b> real-time only; no glitch burst in the browser; a shot that cannot be staged
    /// (chunks not streamed after a 4 s wait, space view, nothing clear) falls back to the sky or the player's own view.</item>
    /// </list>
    /// Runs before the app shell (execution order −20), so the Esc that skips a card is marked handled before the pause
    /// menu sees it; the camera itself is written by <see cref="CutsceneCameraRig"/> at order 211. #2478: it also runs the
    /// Guardian core's duel as a dialogue (CutsceneDirectorDuel.cs) whenever no scene plays.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed partial class CutsceneDirector : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public ClientSettings Settings;
        public PlayerController Player;

        /// <summary>The director's reason in <see cref="GameBootstrap.Cutscenes"/> (only while a T3/T4 scene takes the camera
        /// or freezes the controls).</summary>
        public const string GateReason = "director";

        private const int FrameSortingOrder = 67;
        private const float ScreenCloseSeconds = 0.45f;
        private const float CreditsDim = 0.85f; // #2478: the credits' dark stage
        private const float AtHomeShipRadius = 24f;
        private const float FallingSpeed = -6f;

        /// <summary>True while the director shows a card or a scene (T2+): VEGA's panel and the ending wait for it.</summary>
        public bool HoldsScreen => _play != null && _play.Plan.Tier >= CutsceneTiers.Card;

        /// <summary>#2497: true while the screen holds a card (T2, a scene played as one included) — the player keeps playing
        /// under it, so VEGA's panel hides only her speech and keeps the objective chip.</summary>
        public bool ShowsCard => _play != null && _play.Plan.Tier == CutsceneTiers.Card;

        /// <summary>True while any scene (a banner included) plays.</summary>
        public bool Playing => _play != null;

        /// <summary>#2471: a touch tap the touch layer's full-screen look pad caught first — under a card the touch controls
        /// stay up and their canvas (order 100) lies over this frame (67), so the card's skip button, its continue button and
        /// its speaker box were out of reach on a tablet. Returns whether the playing scene took the tap.</summary>
        public bool TapAt(Vector2 screenPos) => _play != null && _screen != null && _screen.TapAt(screenPos);

        private sealed class Owed
        {
            public StoryDefinition Pack;
            public CutsceneDefinition Definition;
            public CutscenePlan ReadyPlan; // a client-made banner brings its plan along
            public readonly List<ShipAiLine> Lines = new List<ShipAiLine>();
            public bool Downgraded;
            public bool TakesCamera => !Downgraded && Definition != null && Definition.TakesCamera;
        }

        private enum Outcome
        {
            Watched,
            Skipped,
            Interrupted,
        }

        private readonly CutsceneQueue _queue = new CutsceneQueue();
        private readonly SafeMomentInputs _inputs = new SafeMomentInputs();
        private readonly HashSet<string> _serverSeen = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly HashSet<string> _localSeen = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly List<(CutsceneScreen Screen, float Left, float Letterbox, float WorldDim)> _closing =
            new List<(CutsceneScreen, float, float, float)>();
        private readonly StagingRetry _stagingRetry = new StagingRetry();

        private bool _subscribed;
        private CutscenePlayback _play;
        private QueuedCutscene _current;
        private CutsceneScreen _screen;
        private CutsceneCameraRig _rig;
        private WorldLoadingOverlay _veil;
        private System.Func<string, string> _localize;

        private bool _gate;
        private bool _hudHidden; // #2496: the scene's hideHud applied (a HUD-only reason in GameBootstrap.Cutscenes)
        private float _worldDim; // #2497: the world dim shown now (eased out with the closing screen)
        private bool _holding;
        private bool _capped;
        private bool _chartShown;
        private bool _restageDue;
        private bool _gradeApplied;
        private float _cameraSeconds;
        private float _letterbox;
        private string _musicTrack;
        private PlannedCaption _lastSpeakerCaption;
        private int _lastSpeakerPage = -1;
        private float _speakerPageAt; // #2495: the scene time the speaker box's page appeared (a timed page's voice window)
        private NarratorVoice _voice;  // #2495: the speaker's vocoder chatter (VEGA's clips) while the box types
        private float _flash;

        private float _lastHealth = -1f;
        private float _lastDamageAt = float.NegativeInfinity;
        private float _lastIntegrity = -1f;
        private float _lastSpaceCombatAt = float.NegativeInfinity;

        private void Awake()
        {
            _localize = L;
            _rig = gameObject.AddComponent<CutsceneCameraRig>();
            _voice = new NarratorVoice(gameObject, () => Settings);
        }

        private void Start()
        {
            _veil = FindAnyObjectByType<WorldLoadingOverlay>();
            _rig.Game = Game;
            _rig.Camera = Camera;
        }

        // ------------------------------------------------------------------------------------------
        // Public API: the server message, local plays, the VEGA panel's lines, the Story tab.
        // ------------------------------------------------------------------------------------------

        /// <summary>Plays a scene on this client alone (the dev palette, the clip recorder): no safe-moment wait beyond a
        /// free screen, no cooldown, no confirmation, nothing recorded as seen.</summary>
        public bool PlayLocal(string packId, string cutsceneId, string arg = "")
        {
            var m = new CutscenePlay { PackId = packId ?? string.Empty, CutsceneId = cutsceneId ?? string.Empty, Arg = arg ?? string.Empty };
            if (Game != null && m.CutsceneId != StoryCutsceneIds.Recap && string.IsNullOrEmpty(m.Arg)
                && Game.Content != null && Game.Content.TryGetStory(m.PackId, out var pack)
                && pack.FindCutscene(m.CutsceneId) is { } def)
            {
                if (def.Template && m.CutsceneId.IndexOf(StoryCutsceneIds.InstanceSeparator) < 0)
                {
                    m.CutsceneId = StoryCutsceneIds.Instance(def.Id, CutsceneArgs.DefaultOwner(pack, def)); // a template plays as an instance
                }

                m.Arg = CutsceneCatalog.PlayArg(pack, def, m.CutsceneId, Game.StarMap); // #2473: the server's argument rule
            }

            if (m.CutsceneId == StoryCutsceneIds.Recap && string.IsNullOrEmpty(m.Arg) && Game?.Content != null
                && Game.Content.TryGetStory(m.PackId, out var recapPack))
            {
                m.Arg = string.Join(BlocksBeyondTheStars.Shared.Localization.LineText.ArgSeparator.ToString(),
                    recapPack.Chapters.Where(c => !string.IsNullOrEmpty(c.TitleKey)).Select(c => c.TitleKey));
            }

            if (Game?.PlayerPosition is { } at)
            {
                m.HasAnchor = true;
                m.AnchorX = at.x;
                m.AnchorY = at.y;
                m.AnchorZ = at.z;
            }

            return Receive(m, local: true);
        }

        /// <summary>A narrator line arrived (from <see cref="VegaPanel"/>): when an owed or playing scene shows it itself
        /// (<c>replaceLineKeys</c>), the director keeps it — out of the panel — and gives it back if the scene is
        /// interrupted or given up. Requested replays and local plays claim nothing.</summary>
        public bool TryClaimLine(ShipAiLine line)
        {
            if (line == null || string.IsNullOrEmpty(line.LineKey))
            {
                return false;
            }

            if (_current != null && Claims(_current, line))
            {
                return true;
            }

            foreach (var item in _queue.Items)
            {
                if (Claims(item, line))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The Story tab's "Watch again" rows: the replayable scenes this player has seen, of the packs this client
        /// has — the world story first, then the others by priority. #2497: <paramref name="withoutEnding"/> leaves each pack's
        /// ending out (the stand-down and the ending — <see cref="CutsceneCatalog.EndingIds"/>) while the Journal shows its
        /// "Watch the ending again" button, which replays both.</summary>
        public IReadOnlyList<CutsceneReplayEntry> ReplayEntries(bool withoutEnding = false)
        {
            var content = Game != null ? Game.Content : null;
            if (content == null)
            {
                return System.Array.Empty<CutsceneReplayEntry>();
            }

            string world = Game.Story?.StoryId ?? string.Empty;
            var packs = content.Stories.Values
                .OrderByDescending(p => p.Id == world)
                .ThenByDescending(p => p.Priority)
                .ThenBy(p => p.Id, System.StringComparer.Ordinal)
                .ToList();
            List<string> exclude = withoutEnding ? packs.SelectMany(p => CutsceneCatalog.EndingKeys(p)).ToList() : null;
            return CutsceneCatalog.ReplayEntries(packs, _serverSeen.Concat(_localSeen), exclude);
        }

        /// <summary>
        /// #2497: the Journal's "Watch" (and "Watch the ending again") while the player flies, warps or floats on an EVA: a
        /// replay of a scene that takes the camera only starts once they have landed (<see cref="SafeMomentGate"/>) — a short
        /// banner "Plays when you land" says so instead of a silent wait. Call it right after asking the server for the
        /// replay; the banner shows once the menu has closed. Returns whether it showed.
        /// </summary>
        public bool NoteReplayWaits(string packId, IEnumerable<string> cutsceneIds)
        {
            if (Game?.Content == null || string.IsNullOrEmpty(packId) || cutsceneIds == null
                || !Game.Content.TryGetStory(packId, out var pack))
            {
                return false;
            }

            foreach (string id in cutsceneIds)
            {
                var def = string.IsNullOrEmpty(id) ? null : pack.FindCutscene(id);
                if (def == null || !CutsceneRules.ReplayWaitsForLanding(def, _inputs, AnchorFound, SyntheticStagesAllowed()))
                {
                    continue;
                }

                string label = CutsceneCatalog.Label(pack, def, id, TextSource());
                QueueLocalBanner(packId, "landing." + id, CutscenePlan.LocalBanner(packId, label, L("ui.cutscene.plays_when_landed")));
                return true;
            }

            return false;
        }

        /// <summary>A row's name for <see cref="ReplayEntries"/>.</summary>
        public string ReplayLabel(CutsceneReplayEntry entry)
            => entry == null ? string.Empty : CutsceneCatalog.Label(entry.Pack, entry.Definition, entry.CutsceneId, TextSource());

        /// <summary>The ids of every scene the dev palette offers: each pack's definitions (a template as its first
        /// chapter's instance) and the built-in recap of each world-scope pack.</summary>
        public IReadOnlyList<(string Pack, string Id)> PaletteEntries()
        {
            var list = new List<(string, string)>();
            if (Game?.Content == null)
            {
                return list;
            }

            foreach (var pack in Game.Content.Stories.Values.OrderBy(p => p.Id, System.StringComparer.Ordinal))
            {
                foreach (var def in pack.Cutscenes)
                {
                    list.Add((pack.Id, def.Id));
                }

                if (string.Equals(StoryRegistry.EffectiveScope(pack), StoryRegistry.ScopeWorld, System.StringComparison.Ordinal)
                    && pack.Chapters.Count > 0)
                {
                    list.Add((pack.Id, StoryCutsceneIds.Recap));
                }
            }

            return list;
        }

        // ------------------------------------------------------------------------------------------
        // Frame loop.
        // ------------------------------------------------------------------------------------------

        private void Update()
        {
            if (!_subscribed && Game?.Network != null)
            {
                Game.Network.CutscenePlayReceived += OnCutscenePlay;
                Game.Network.StoryStateReceived += OnStoryState;
                Game.Network.CoreDialogueReceived += OnCoreDialogue; // #2478: the duel's dialogue mode
                _subscribed = true;
            }

            TickClosingScreens();
            if (Game == null)
            {
                return;
            }

            TrackDanger();
            FillInputs();
            if (_play != null)
            {
                RunScene();
            }
            else if (!TickDuel())
            {
                StartNext(); // no scene, and the duel dialogue does not hold the screen
            }
        }

        private void OnCutscenePlay(CutscenePlay m) => Receive(m, local: false);

        private void OnStoryState(StoryStateMessage m)
        {
            _serverSeen.Clear();
            foreach (string key in m?.SeenCutscenes ?? System.Array.Empty<string>())
            {
                if (!string.IsNullOrEmpty(key))
                {
                    _serverSeen.Add(key);
                }
            }
        }

        private bool Receive(CutscenePlay m, bool local)
        {
            if (m == null || string.IsNullOrEmpty(m.PackId) || string.IsNullOrEmpty(m.CutsceneId))
            {
                return false;
            }

            bool recap = m.CutsceneId == StoryCutsceneIds.Recap;
            StoryDefinition pack = null;
            Game?.Content?.TryGetStory(m.PackId, out pack);
            var def = recap ? null : pack?.FindCutscene(m.CutsceneId);
            bool instance = m.CutsceneId.IndexOf(StoryCutsceneIds.InstanceSeparator) >= 0;
            bool known = recap || (def != null && instance == def.Template);
            if (!known)
            {
                // A scene this build cannot play (an older pack, version skew): confirm it as unsupported so it is not
                // offered forever; its narrator lines were never claimed and show in the panel as usual.
                Debug.Log($"[Cutscene] Unsupported {m.PackId}/{m.CutsceneId} (replay={m.Replay}).");
                if (!m.Replay && !local)
                {
                    Game?.Network?.SendCutsceneSeen(m.PackId, new[] { m.CutsceneId }, unsupported: true);
                }

                return false;
            }

            var options = def?.Options;
            var item = new QueuedCutscene(m.PackId, m.CutsceneId, recap ? CutsceneTiers.Card : def.Tier)
            {
                Arg = m.Arg ?? string.Empty,
                HasAnchor = m.HasAnchor,
                AnchorX = m.AnchorX,
                AnchorY = m.AnchorY,
                AnchorZ = m.AnchorZ,
                Replay = m.Replay,
                Local = local,
                CooldownExempt = options?.CooldownExempt ?? false,
                ChainNext = options?.ChainNext?.ToArray() ?? System.Array.Empty<string>(),
                // #2470: a chapter's scene (not the intro) — "the story so far" leads straight into it.
                ChapterScene = def != null && pack != null && def.Id != pack.IntroCutscene
                               && CutsceneCatalog.ChapterOf(pack, def, m.CutsceneId) != null,
                ReceivedAt = Time.time,
                Payload = new Owed { Pack = pack, Definition = def },
            };
            bool queued = _queue.Enqueue(item);
            Debug.Log($"[Cutscene] Received {m.PackId}/{m.CutsceneId} tier={item.Tier} replay={m.Replay} local={local} queued={queued}.");
            return queued;
        }

        private static bool Claims(QueuedCutscene item, ShipAiLine line)
        {
            if (item.Requested || item.Payload is not Owed owed || owed.Definition?.ReplaceLineKeys == null
                || !owed.Definition.ReplaceLineKeys.Contains(line.LineKey))
            {
                return false;
            }

            if (owed.Lines.Any(l => l.LineKey == line.LineKey))
            {
                return false; // one copy per scene: a second identical line is a new moment
            }

            owed.Lines.Add(line);
            return true;
        }

        /// <summary>Starts the first waiting scene the queue and the safe-moment gate allow.</summary>
        private void StartNext()
        {
            float now = Time.time;
            var candidates = _queue.Candidates(now);
            foreach (var item in candidates)
            {
                var owed = item.Payload as Owed ?? new Owed();
                var request = new SafeMomentRequest
                {
                    Tier = owed.Downgraded ? Mathf.Min(item.Tier, CutsceneTiers.Card) : item.Tier,
                    TakesCamera = owed.TakesCamera,
                    NowOrNever = item.CooldownExempt,
                    Requested = item.Requested,
                    WaitedSeconds = now - item.ReceivedAt,
                };
                var decision = SafeMomentGate.Evaluate(request, _inputs);
                if (decision.Verdict == SafeMomentVerdict.Play)
                {
                    var plan = BuildPlan(item, owed);
                    if (plan == null)
                    {
                        AbortWaiting(item, "unplannable");
                        return;
                    }

                    // #2474: a staged scene about an object this world does not have (VEGA's "What You Are" at the heal tank,
                    // far from the own ship) plays as a card — the same captions, no camera. #2477: so does a scene whose only
                    // pictures are synthetic stages on a preset that skips them (Potato/Low).
                    bool missingObject = !owed.Downgraded && CutsceneRules.NeedsCardFallback(plan, AnchorFound, SyntheticStagesAllowed());
                    if (missingObject)
                    {
                        Debug.Log($"[Cutscene] {item.Key}: the object it frames is not here, or the preset skips its stages — playing it as a card.");
                    }

                    Begin(item, owed.Downgraded || missingObject ? plan.AsCard() : plan);
                    return;
                }

                if (decision.Verdict == SafeMomentVerdict.Abort)
                {
                    if (decision.Reason == SafeMomentGate.ReasonGaveUp && owed.TakesCamera)
                    {
                        owed.Downgraded = true; // the downgrade path: try it as a card before giving up on it
                        item.ReceivedAt = now;
                        Debug.Log($"[Cutscene] {item.Key} found no moment for its camera — trying it as a card.");
                        continue;
                    }

                    AbortWaiting(item, decision.Reason);
                    return;
                }
            }
        }

        private CutscenePlan BuildPlan(QueuedCutscene item, Owed owed)
        {
            if (owed.ReadyPlan != null)
            {
                return owed.ReadyPlan;
            }

            bool wait = Settings == null || Settings.StoryTextWaitForMe;
            var text = TextSource();
            if (item.DefinitionId == StoryCutsceneIds.Recap)
            {
                return CutscenePlan.Recap(item.PackId, item.Arg, owed.Pack?.NameKey ?? string.Empty, text, wait, item.Requested);
            }

            return owed.Definition == null ? null : CutscenePlan.Build(owed.Definition, item.PackId, item.Id, item.Arg, text, wait, item.Requested);
        }

        private CutsceneTextSource TextSource()
        {
            var loc = Game != null ? Game.Localizer : null;
            return new CutsceneTextSource(
                key => loc != null ? loc.Get(key) : key,
                key => loc != null && loc.Has(key),
                action => HudUi.KeyTokenGlyph(loc, action));
        }

        private void Begin(QueuedCutscene item, CutscenePlan plan)
        {
            _queue.Start(item);
            _current = item;
            _play = new CutscenePlayback(plan);
            _capped = false;
            _cameraSeconds = 0f;
            _chartShown = false;
            _restageDue = false;
            _letterbox = 0f;
            _lastSpeakerCaption = null;
            _lastSpeakerPage = -1;
            _speakerPageAt = 0f;
            _flash = 0f;
            _screen = CutsceneScreen.Create("CutsceneFrame", FrameSortingOrder, skipButton: plan.Tier >= CutsceneTiers.Card);
            if (plan.OpensFromBlack)
            {
                _screen.SetFade(1f); // a staged scene opens out of black (synthesis §6.5, prologue shot 1); an overlay does not (#2473)
            }

            _musicTrack = string.IsNullOrEmpty(plan.Music) ? null : plan.Music;
            ClientMusic.CutsceneTrack = _musicTrack;
            Debug.Log($"[Cutscene] Begin {item.Key} tier={plan.Tier} shots={plan.Shots.Count} camera={plan.TakesCamera} replay={item.Replay} local={item.Local}.");
        }

        private void RunScene()
        {
            var plan = _play.Plan;
            float dt = Time.deltaTime;
            bool card = plan.Tier == CutsceneTiers.Card;
            bool banner = plan.Tier <= CutsceneTiers.Banner;

            // A card keeps the input free, so the Tab menu may open over it: the card waits (hidden) until it closes — silent
            // (#2495: no chatter, the ambience back to its level).
            if ((card || banner) && !_gate && Game.MenuOpen)
            {
                _screen.Visible = false;
                _voice.Stop();
                ClientAudio.Instance?.SetCutsceneDuck(1f);
                return;
            }

            _screen.Visible = true;

            if (!banner && SafeMomentGate.ShouldInterrupt(_inputs, _play.Elapsed, _holding && Game.WorldPaused))
            {
                Finish(Outcome.Interrupted);
                return;
            }

            if (!banner && SkipRequested(card))
            {
                if (_play.CanSkip)
                {
                    Finish(Outcome.Skipped);
                    return;
                }
            }

            if (!banner && (InputMap.Down(InputAction.VegaContinue) || _screen.ConsumeContinueClick()))
            {
                _play.Continue();
            }

            _play.Tick(dt);
            if (_play.TakeShotChanged())
            {
                OnShotStarted();
            }
            else if (_restageDue && _stagingRetry.Due(Time.time))
            {
                StageShot();
            }

            bool cameraTaken = _rig.Holding || _chartShown;
            if (cameraTaken)
            {
                _cameraSeconds += dt;
                if (SafeMomentGate.CameraCapReached(_inputs.Multiplayer, _cameraSeconds))
                {
                    _capped = true; // MP: the camera goes back, the captions go on as a card
                    _rig.Release();
                    _screen.HideChart();
                    _chartShown = false;
                    cameraTaken = false;
                    Debug.Log($"[Cutscene] {_current?.Key} reached the multiplayer camera cap — continuing as a card.");
                }
            }

            UpdateGateAndHold(plan, cameraTaken);
            // #2495: the world's ambience beds step back under the scene (a card 0.7, a T3/T4 0.4, a banner not at all); the
            // scene's own cues below, the line-start sounds, the narrator's voice and the music are not ducked.
            ClientAudio.Instance?.SetCutsceneDuck(CutsceneRules.AmbienceDuck(plan, _capped));
            foreach (string cue in _play.TakeDueSfx())
            {
                ClientAudio.Instance?.Cue(cue);
            }

            if (!banner)
            {
                ApplyGrade(_play.Shot.Grade); // a banner leaves the picture alone (a memory flashback may be running)
            }

            Present(plan, dt);
            if (_flash > 0f)
            {
                _flash = Mathf.MoveTowards(_flash, 0f, dt / 0.35f);
                _screen.SetFlash(_flash); // × FxKit.FlashScale inside (Reduce flashes)
            }

            if (_play.Done)
            {
                Finish(Outcome.Watched);
            }
        }

        /// <summary>The speaker box glitches in for the narrator's first words of a scene (#2470, synthesis §6.5 "VEGA boots" —
        /// the prologue's cut to the pilot's seat as the console wakes): a short glitch burst (never in the browser, none with
        /// Reduce effects) and, in a staged scene, a soft flash that follows the Reduce-flashes setting.</summary>
        private void GlitchIn(CutscenePlan plan)
        {
            if (!FxKit.ReducedEffects && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                UrpScenePost.Instance?.Burst(0.25f, 0.3f, 0.5f);
            }

            if (plan.Tier >= CutsceneTiers.Scene)
            {
                _flash = 0.6f;
            }
        }

        /// <summary>Skip input: a camera-taking scene takes Esc and the pad's cancel button (they own the screen); a card,
        /// where the controls stay free and pad B is crouch, takes the keyboard's Esc; the skip button always works. The
        /// press is marked handled so the pause menu does not open with it.</summary>
        private bool SkipRequested(bool card)
        {
            bool clicked = _screen.ConsumeSkipClick();
            bool pressed = card ? Input.GetKeyDown(KeyCode.Escape) && !Game.ChatTyping : CinematicFrame.SkipPressed();
            if (pressed)
            {
                Game.MarkMenuInputHandled();
            }

            return clicked || pressed;
        }

        private void OnShotStarted()
        {
            _stagingRetry.Reset();
            _restageDue = false;
            var shot = _play.Shot;
            if (shot.Grade == CutsceneGrades.Flashback && !FxKit.ReducedEffects && Application.platform != RuntimePlatform.WebGLPlayer)
            {
                UrpScenePost.Instance?.Burst(0.3f, 0.4f, 1.2f); // the recall's glitch, once per shot (never in the browser)
            }

            StageShot();
        }

        /// <summary>Stages the current shot's camera: the requested type or its fallback (sky, the player's view). A
        /// world shot that fails while chunks may still stream is retried for a few seconds first (synthesis §6.3: "wait up
        /// to 4 s for streaming"), showing the player's view meanwhile. #2477: a move (dolly, pan, flyover) is flown over the
        /// shot's length; a synthetic stage (space vista, diorama) needs no chunks — it is shown by the rig's stage, and when
        /// that cannot be built here (<paramref name="allowSynthetic"/> false on the second try) the shot falls back.</summary>
        private void StageShot(bool allowSynthetic = true)
        {
            var shot = _play.Shot;
            float now = Time.time;
            _restageDue = false;
            if (_capped || !shot.TakesCamera || Camera == null)
            {
                ShowPlayerView();
                return;
            }

            Vector3 center = AnchorPosition(shot.Anchor);
            float scale = shot.Anchor == CutsceneAnchors.Ship || shot.Anchor == CutsceneAnchors.HealTank ? OwnShipScale()
                : shot.Anchor == CutsceneAnchors.Planet ? 1.6f : 1f;
            var world = Game.World;
            CinematicStageScan.ClearSampler path = null;
            CinematicStageScan.ClearSampler spot = null;
            if (world != null && !Game.SpaceViewActive)
            {
                path = (x, y, z) => world.TryGetBlock(x, y, z, out var b) && b.IsAir;
                var game = Game;
                var terrain = path;
                spot = (x, y, z) => terrain(x, y, z) && game.LandedShipBlockAt(x, y, z, out _, out _).IsAir;
            }

            bool skyOpen = Game.ExposedToSky && !Game.Aboard && !Game.SpaceViewActive;
            bool chart = Game.StarMap?.Systems != null && Game.StarMap.Systems.Length > 0;
            bool closeUp = CutsceneStaging.WantsCloseUp(shot.Anchor); // #2474: the heal tank, framed inside the medbay
            bool synthetic = allowSynthetic && SyntheticStagesAllowed(); // #2477: not on Potato/Low
            var staged = CutsceneStaging.Stage(shot.Camera, path, spot, center.x, center.y, center.z, scale, Game.PlayerYaw, skyOpen, chart,
                closeUp, synthetic);
            bool worldShot = shot.Camera != CutsceneCameras.Chart && shot.Camera != CutsceneCameras.Sky
                             && !CutsceneCameras.IsSynthetic(shot.Camera); // only these wait for streaming chunks
            _stagingRetry.Record(now, success: !staged.FellBack);
            if (staged.FellBack && worldShot && path != null && !_stagingRetry.GaveUp(now))
            {
                ShowPlayerView(); // the ring may still be streaming in — look again in half a second
                _restageDue = true;
                return;
            }

            if (staged.FellBack)
            {
                Debug.Log($"[Cutscene] {_current?.Key} shot {shot.Index}: '{shot.Camera}' fell back to '{StagedCamera.KindName(staged.Kind)}'.");
            }

            if (staged.Kind == StagedCameraKind.Chart)
            {
                _rig.Release();
                ShowChart(shot.Anchor, shot.Grade);
                return;
            }

            _screen.HideChart();
            _chartShown = false;
            if (staged.Synthetic)
            {
                // #2477: the stage stands far from the world with its own camera — on the ground, in a station or in space.
                var set = SyntheticStages.SetOf(shot.Camera, shot.Anchor);
                bool memory = shot.Grade == CutsceneGrades.Memory || shot.Grade == CutsceneGrades.Flashback;
                if (set.HasValue && _rig.ApplySynthetic(staged, set.Value, memory, shot.Duration))
                {
                    return;
                }

                Debug.Log($"[Cutscene] {_current?.Key} shot {shot.Index}: the '{shot.Camera}' stage could not be built here — staging the shot without it.");
                StageShot(allowSynthetic: false);
                return;
            }

            // A close framing inside the room is flown with the room's own line of sight (the hull counts, the object's cell
            // does not), so the per-frame safety net agrees with the staging. A move travels over the shot's length (#2477).
            _rig.Apply(staged, staged.CloseUp && spot != null ? CutsceneStaging.ObjectLine(spot, center.x, center.y, center.z) : path, spot,
                shot.Duration);
        }

        /// <summary>#2477: whether the player's quality preset builds the synthetic stages (Medium and up).</summary>
        private bool SyntheticStagesAllowed()
            => Settings == null || CutsceneRules.SyntheticStagesAllowed((int)Settings.Preset);

        private void ShowPlayerView()
        {
            _rig.Release();
            _screen.HideChart();
            _chartShown = false;
        }

        /// <summary>The galaxy chart (<c>chart</c> camera): every star of the map, the known ones by name, the current one
        /// ringed, the relay lanes. #2474: anchored at <c>core</c> it shows the Guardian reveal — the revealed Guardian system
        /// named and ringed, a violet route to it from the player's system (VEGA's chapter 7); a <c>memory</c> or
        /// <c>flashback</c> grade tints it sepia (VEGA's memory of the stars, chapter 2).</summary>
        private void ShowChart(string anchor, string grade)
        {
            var map = Game.StarMap;
            var stars = new List<GalaxyChartWidget.Star>();
            var lanes = new List<(string A, string B)>();
            string here = SystemOfBody(map, map?.ActiveLocationId);
            string finale = null;
            // #2478: the players anchor names where the other players are (VEGA's "Many minds" — the live chart).
            bool players = anchor == CutsceneAnchors.Players;
            var others = players ? CutsceneChart.PlayersBySystem(map, Game.PlayerName) : null;
            foreach (var sys in map?.Systems ?? System.Array.Empty<NetStarSystem>())
            {
                bool current = sys.Id == here;
                bool isFinale = sys.Id != null && sys.Id.StartsWith("guardian_finale", System.StringComparison.Ordinal);
                bool known = current || Game.KnowsSystem(sys.Id);
                if (isFinale)
                {
                    finale = sys.Id;
                }

                string there = string.Empty;
                if (others != null && sys.Id != null && others.TryGetValue(sys.Id, out var names))
                {
                    there = names;
                }

                stars.Add(new GalaxyChartWidget.Star
                {
                    Id = sys.Id,
                    MapX = sys.MapX,
                    MapY = sys.MapY,
                    Label = known || isFinale ? sys.Shown() : "?", // the revealed Guardian system is on everyone's map by name
                    Color = sys.StarColor != 0 ? Rgb(sys.StarColor) : new Color(1f, 0.86f, 0.55f),
                    Current = current,
                    Known = known || isFinale,
                    Finale = isFinale,
                    Players = there,
                });
            }

            var net = Game.RelayNetwork;
            if (net?.LaneSystemA != null && net.LaneSystemB != null)
            {
                for (int i = 0; i < Mathf.Min(net.LaneSystemA.Length, net.LaneSystemB.Length); i++)
                {
                    lanes.Add((net.LaneSystemA[i], net.LaneSystemB[i]));
                }
            }

            bool reveal = anchor == CutsceneAnchors.Core && finale != null;
            // The other players' chart is live — it stays in colour under a memory grade (the grade still tints the world).
            bool memory = (grade == CutsceneGrades.Memory || grade == CutsceneGrades.Flashback) && !players;
            _screen.ShowChart(stars, lanes, reveal ? finale : null, reveal ? here : null, reveal ? finale : null, memory);
            _chartShown = true;
        }

        internal static string SystemOfBody(StarMapData map, string bodyId)
        {
            if (map?.Systems == null || string.IsNullOrEmpty(bodyId))
            {
                return null;
            }

            foreach (var sys in map.Systems)
            {
                foreach (var b in sys.Bodies ?? System.Array.Empty<NetBody>())
                {
                    if (b.Id == bodyId)
                    {
                        return sys.Id;
                    }
                }
            }

            return null;
        }

        internal static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>The camera may only be taken by a staged scene (T3/T4). The shared cutscene state — HUD away, controls
        /// frozen, Esc to the director — is on while such a scene takes the camera or freezes the controls, and not after
        /// the multiplayer cap. A singleplayer world is held only while the camera is actually taken (synthesis §5.3
        /// "Stufen"); in multiplayer a hold would be a group vote, so the director never asks.</summary>
        private void UpdateGateAndHold(CutscenePlan plan, bool cameraTaken)
        {
            bool gate = CutsceneRules.FreezesControls(plan, _capped, cameraTaken);
            if (gate != _gate)
            {
                _gate = gate;
                GameBootstrap.Cutscenes.Set(GateReason, gate);
                Game.SetCursorOwner(this, gate); // the continue and skip buttons are clickable while the scene owns the screen
            }

            // #2496: a scene's hideHud (T3/T4, not after the multiplayer cap) — a HUD-only reason: the HUD, the flight HUD, the
            // radar and the name tags step aside, the controls and the touch layer stay (the overlay "Lift-off!").
            bool hud = CutsceneRules.HidesHud(plan, _capped);
            if (hud != _hudHidden)
            {
                _hudHidden = hud;
                GameBootstrap.Cutscenes.SetHud(GateReason, hud);
            }

            bool hold = CutsceneRules.HoldsWorld(plan, cameraTaken, _inputs.Multiplayer); // never in multiplayer (#2474 test)
            if (hold && !_holding)
            {
                Game.WorldHold.Hold(WorldHoldArbiter.Cutscene, Time.realtimeSinceStartup);
            }
            else if (!hold && _holding)
            {
                Game.WorldHold.Release(WorldHoldArbiter.Cutscene);
            }

            _holding = hold;
        }

        private void ApplyGrade(string grade)
        {
            float level = grade switch
            {
                CutsceneGrades.Memory => 0.55f,
                CutsceneGrades.Flashback => 0.85f,
                _ => 0f,
            };
            if (FxKit.ReducedEffects)
            {
                level *= 0.5f;
            }

            UrpScenePost.Instance?.SetFlashback(level);
            _gradeApplied = true;
        }

        private void Present(CutscenePlan plan, float dt)
        {
            bool banner = plan.Tier <= CutsceneTiers.Banner;
            float wantBox = banner ? 0f : plan.Tier == CutsceneTiers.Card ? 0.45f : 1f;
            _letterbox = Mathf.MoveTowards(_letterbox, wantBox, dt / 0.6f);
            _screen.SetLetterbox(CinematicTimeline.EaseInOut(_letterbox));

            var views = _play.Visible();
            bool reader = views.Any(v => v.Caption.Style == CutsceneCaptionStyles.ReadingFrame);
            // #2478: the credits roll on a dark stage (the old ending's 0.88 black) — readable over any picture, faded with it.
            float credits = 0f;
            foreach (var v in views)
            {
                if (v.Caption.Style == CutsceneCaptionStyles.Credits)
                {
                    credits = Mathf.Max(credits, CreditsDim * Mathf.Clamp01(v.Alpha));
                }
            }

            // Out of black for a staged scene, 0.55 behind a reading frame, the credits' dark stage — on the frame, over
            // everything. #2497: a card or an overlay dims only the world (0.18 under the HUD): the player keeps playing.
            var (fade, worldDim) = CutsceneRules.Dims(plan, _play.Elapsed, reader, credits);
            _screen.SetFade(fade);
            _worldDim = worldDim;
            _screen.SetWorldDim(worldDim);

            // Speaker: the first speaker caption on screen names who talks; its line-start cue plays once per page. #2495: the
            // speaker's voice chatters while the box types (a timed page: as long as it would take to type) — subtitles stay
            // silent (the prologue's narration), a closed box falls silent with its fade.
            CutsceneSpeaker speaker = null;
            bool chatter = false;
            foreach (var v in views)
            {
                if (v.Caption.Style == CutsceneCaptionStyles.Speaker || banner)
                {
                    speaker = SpeakerFor(_current?.Payload is Owed o ? o.Pack : null, v.Caption.Speaker);
                    if (v.Caption.Style == CutsceneCaptionStyles.Speaker && (v.Caption != _lastSpeakerCaption || v.Page != _lastSpeakerPage))
                    {
                        if (_lastSpeakerCaption == null)
                        {
                            GlitchIn(plan); // #2470: the narrator's first words of the scene
                        }

                        _lastSpeakerCaption = v.Caption;
                        _lastSpeakerPage = v.Page;
                        _speakerPageAt = _play.Elapsed;
                        if (!string.IsNullOrEmpty(speaker.LineStartCue))
                        {
                            ClientAudio.Instance?.Cue(speaker.LineStartCue);
                        }
                    }

                    chatter = CutsceneVoice.ShouldChatter(v, _play.Elapsed - _speakerPageAt, speaker.Voice.Count > 0);
                    break;
                }
            }

            if (chatter)
            {
                _voice.SetClips(speaker.Voice);
            }

            _voice.Tick(chatter, _inputs.LoadingVeil);

            if (banner && speaker == null)
            {
                speaker = SpeakerFor(_current?.Payload is Owed o2 ? o2.Pack : null, string.Empty);
            }

            _screen.Render(views, speaker, banner, _play.ShotTime);

            bool showContinue = !banner && views.Any(v => v.WaitsForReader);
            _screen.ShowContinue(showContinue ? ContinueLabel() : string.Empty, showContinue);

            bool skipHint = !banner && _play.CanSkip && _play.Elapsed > 1f
                            && !(plan.Tier == CutsceneTiers.Card && InputMap.ActiveDevice == InputDeviceKind.Gamepad);
            _screen.SetHint(skipHint ? CinematicFrame.SkipLabel(_localize) : string.Empty, skipHint ? 0.75f : 0f);
        }

        /// <summary>The continue control's label for the device in hand — VEGA's own continue wording.</summary>
        private string ContinueLabel() => InputMap.ActiveDevice switch
        {
            InputDeviceKind.Touch => L("ui.vega.next_touch"),
            InputDeviceKind.Gamepad => SafeFormat(L("ui.vega.next_pad"), InputMap.Glyph(InputAction.VegaContinue)),
            _ => SafeFormat(L("ui.vega.next_key"), InputMap.Key(InputAction.VegaContinue).ToString()),
        };

        private static string SafeFormat(string template, string arg)
        {
            try
            {
                return string.Format(template ?? string.Empty, arg);
            }
            catch (System.FormatException)
            {
                return template ?? string.Empty;
            }
        }

        /// <summary>Who a speaker box shows: the pack's narrator — or (#2478) one of its other <c>speakers</c>, the Guardian core
        /// in VEGA's pack — with the name key, icon, colour and line-start cue from the pack manifest; for VEGA without a
        /// manifest entry her defaults; another speaker id reads as its own name.</summary>
        private CutsceneSpeaker SpeakerFor(StoryDefinition pack, string speakerId)
        {
            var narrator = pack?.Narrator;
            string id = string.IsNullOrEmpty(speakerId) ? (narrator?.Id ?? StoryNarrator.DefaultId) : speakerId;
            var entry = pack?.FindSpeaker(id);
            bool fromPack = entry != null;
            bool vega = id == StoryNarrator.DefaultId;
            string nameKey = fromPack && !string.IsNullOrEmpty(entry.NameKey) ? entry.NameKey : vega ? "ui.vega.name" : string.Empty;
            string icon = fromPack && !string.IsNullOrEmpty(entry.Icon) ? entry.Icon : vega ? "icon_vega" : string.Empty;
            var color = vega ? UiKit.Cyan : new Color(1f, 0.82f, 0.55f);
            if (fromPack && !string.IsNullOrEmpty(entry.Color) && ColorUtility.TryParseHtmlString(entry.Color, out var parsed))
            {
                color = parsed;
            }

            return new CutsceneSpeaker
            {
                Name = nameKey.Length > 0 ? L(nameKey) : id,
                Icon = icon.Length > 0 ? UiKit.Icon(icon) : null,
                Color = color,
                LineStartCue = fromPack && !string.IsNullOrEmpty(entry.LineStart) ? entry.LineStart : vega ? "ai_blip" : string.Empty,
                Voice = CutsceneVoice.VoiceOf(pack, speakerId), // #2495: the pack's voice, VEGA's chatter by default
            };
        }

        /// <summary>Ends the playing scene: the camera, the hold, the shared state, the music and the grade go back; the
        /// receipt goes out (not for a replay, a local play or the recap); an interrupted scene gives its narrator lines back
        /// to the panel and leaves a banner pointing to the Story tab.</summary>
        private void Finish(Outcome outcome)
        {
            var item = _current;
            var plan = _play?.Plan;
            // #2474: a skip gives back the lines the player must not miss (returnOnSkip) whose captions never showed.
            var returnOnSkip = outcome == Outcome.Skipped && _play != null ? _play.KeysToReturnOnSkip() : null;
            _play?.End();
            _play = null;
            _current = null;
            _queue.Finish(Time.time);
            TearDownScene();

            if (item == null || plan == null)
            {
                return;
            }

            var owed = item.Payload as Owed;
            Debug.Log($"[Cutscene] End {item.Key} ({outcome}).");
            if (outcome == Outcome.Interrupted && owed != null)
            {
                GiveLinesBack(owed);
                if (!plan.IsLocalBanner && (owed.Definition?.Options?.Replayable ?? false))
                {
                    QueueSavedBanner(item.PackId);
                }
            }
            else if (returnOnSkip != null && returnOnSkip.Count > 0 && owed != null)
            {
                GiveLinesBack(owed, returnOnSkip); // VEGA's Guardian reveal: the jump generator, never skipped away unread
            }

            if (!item.Requested && !plan.IsLocalBanner && !plan.IsRecap)
            {
                Game?.Network?.SendCutsceneSeen(item.PackId, new[] { item.Id }, skipped: outcome == Outcome.Skipped);
                _localSeen.Add(CutsceneCatalog.SeenKey(item.PackId, item.Id));
            }
        }

        /// <summary>A waiting scene that will not play (a "now or never" moment that found the screen busy, or a scene that
        /// waited too long even as a card): its lines go back to the panel and it is confirmed, so the server does not
        /// offer it again — the Story tab can still replay it. #2497: a "now or never" moment leaves its title behind as a
        /// short banner (<see cref="CutscenePlan.MissedBanner"/>: "First launch · Lift-off!", "Arrival · The Guardian
        /// Core") instead of nothing.</summary>
        private void AbortWaiting(QueuedCutscene item, string reason)
        {
            _queue.Remove(item);
            Debug.Log($"[Cutscene] Dropped {item.Key} ({reason}).");
            var owed = item.Payload as Owed;
            if (owed != null)
            {
                GiveLinesBack(owed);
            }

            if (!item.Requested && item.DefinitionId != StoryCutsceneIds.Recap)
            {
                Game?.Network?.SendCutsceneSeen(item.PackId, new[] { item.Id });
                _localSeen.Add(CutsceneCatalog.SeenKey(item.PackId, item.Id));
            }

            if (item.CooldownExempt && !item.Requested && item.Tier >= CutsceneTiers.Card && owed != null
                && BuildPlan(item, owed)?.MissedBanner() is { } missed)
            {
                QueueLocalBanner(item.PackId, "missed." + item.Id, missed);
            }
        }

        private void GiveLinesBack(Owed owed)
        {
            var panel = Game?.Views.Vega;
            foreach (var line in owed.Lines)
            {
                panel?.Requeue(line);
            }

            owed.Lines.Clear();
        }

        /// <summary>#2474: gives back only the claimed lines with one of <paramref name="keys"/>.</summary>
        private void GiveLinesBack(Owed owed, IReadOnlyList<string> keys)
        {
            var panel = Game?.Views.Vega;
            foreach (var line in owed.Lines.Where(l => keys.Contains(l.LineKey)).ToList())
            {
                panel?.Requeue(line);
                owed.Lines.Remove(line);
            }
        }

        private void QueueSavedBanner(string packId)
            => QueueLocalBanner(packId, "saved", CutscenePlan.LocalBanner(packId, string.Empty, L("ui.cutscene.saved")));

        /// <summary>Queues a banner this client makes itself (T1, local: nothing to confirm, never in the ledger) under the
        /// built-in id <c>__&lt;name&gt;</c> — "Story moment saved", a missed "now or never" title (#2497), "Plays when you
        /// land" (#2497).</summary>
        private void QueueLocalBanner(string packId, string name, CutscenePlan plan)
        {
            var item = new QueuedCutscene(packId, StoryCutsceneIds.BuiltInPrefix + name, CutsceneTiers.Banner)
            {
                Local = true,
                ReceivedAt = Time.time,
                Payload = new Owed { ReadyPlan = plan },
            };
            _queue.Enqueue(item);
        }

        private void TearDownScene()
        {
            _rig.Release();
            _voice?.Stop(); // #2495: skip, interrupt and the end all come through here
            ClientAudio.Instance?.SetCutsceneDuck(1f);
            if (_gate)
            {
                _gate = false;
                GameBootstrap.Cutscenes.End(GateReason);
                Game?.SetCursorOwner(this, false);
            }

            if (_hudHidden)
            {
                _hudHidden = false;
                GameBootstrap.Cutscenes.SetHud(GateReason, false); // #2496: the HUD comes back with the scene's end
            }

            if (_holding)
            {
                _holding = false;
                Game?.WorldHold.Release(WorldHoldArbiter.Cutscene);
            }

            if (_musicTrack != null)
            {
                _musicTrack = null;
                ClientMusic.CutsceneTrack = null;
            }

            if (_gradeApplied)
            {
                _gradeApplied = false;
                UrpScenePost.Instance?.SetFlashback(0f);
            }

            _chartShown = false;
            _flash = 0f;
            if (_screen != null)
            {
                _screen.SetFlash(0f);
                _screen.HideChart();
                _screen.ShowContinue(string.Empty, false);
                _screen.SetHint(string.Empty, 0f);
                _screen.Render(System.Array.Empty<CaptionView>(), null, false, 0f);
                _closing.Add((_screen, ScreenCloseSeconds, _letterbox, _worldDim));
                _screen = null;
            }

            _worldDim = 0f;
        }

        /// <summary>Lets the letterbox and the world dim of ended scenes ease out instead of vanishing in one frame.</summary>
        private void TickClosingScreens()
        {
            for (int i = _closing.Count - 1; i >= 0; i--)
            {
                var (screen, left, box, dim) = _closing[i];
                left -= Time.deltaTime;
                float k = Mathf.Clamp01(left / ScreenCloseSeconds);
                screen.SetLetterbox(CinematicTimeline.EaseInOut(box * k));
                screen.SetFade(0f);
                screen.SetWorldDim(dim * k); // #2497
                if (left <= 0f)
                {
                    screen.Destroy();
                    _closing.RemoveAt(i);
                }
                else
                {
                    _closing[i] = (screen, left, box, dim);
                }
            }
        }

        // ------------------------------------------------------------------------------------------
        // The player's situation.
        // ------------------------------------------------------------------------------------------

        private void TrackDanger()
        {
            float now = Time.time;
            float health = Game.Health;
            if (_lastHealth >= 0f && health < _lastHealth - 0.01f)
            {
                _lastDamageAt = now;
            }

            _lastHealth = health;
            var combat = Game.ShipCombat;
            if (combat != null)
            {
                float integrity = combat.Hull + combat.Shield;
                if (_lastIntegrity >= 0f && integrity < _lastIntegrity - 0.01f && (Game.SpaceViewActive || Game.InSpace))
                {
                    _lastSpaceCombatAt = now;
                }

                _lastIntegrity = integrity;
            }
        }

        private void FillInputs()
        {
            var s = _inputs;
            float now = Time.time;
            var ending = Game.Views.Resolution;
            s.Multiplayer = Game.PauseJoinedPlayers > 1;
            s.MenuOpen = Game.MenuOpen;
            s.ChatTyping = Game.ChatTyping;
            s.LoadingVeil = !Game.SpaceViewActive && _veil != null && _veil.VeilActive;
            s.Settling = Player != null && Player.IsSettling;
            s.RespawnPrompt = Game.AwaitingRespawnConfirm;
            s.Spectating = Game.Spectating;
            s.AnotherScene = GameBootstrap.Cutscenes.ActiveOtherThan(GateReason) || Game.VegaPrologueActive
                             || (Game.CinematicCameraActive && !_rig.Holding)
                             || (ending != null && (ending.Playing || ending.Pending));
            s.DuelOpen = _duelScreen != null; // #2478: the duel dialogue on screen (a duel elsewhere blocks nothing)
            s.Falling = Game.PlayerVerticalVelocity < FallingSpeed && !Game.Aboard;
            s.Climbing = Player != null && Player.ClimbingWall;
            s.HeadUnderwater = Player != null && Player.IsHeadUnderwater();
            s.InVehicle = !string.IsNullOrEmpty(Game.InSpeeder) || !string.IsNullOrEmpty(Game.InTrain);
            s.InEva = Game.InEva;
            s.PilotingInSpace = Game.SpaceViewActive && !Game.SpaceAutomaticTransit;
            s.Warping = WormholeTransitFx.Busy || Game.SpaceAutomaticTransit;
            // #2613: the ship's placement is canonical and the player's position unbounded — measured at the copy nearest the
            // player, or a player beside a ship parked on the seam (the home pad lies at X = 0) never counted as home.
            bool nearShip = Game.ShipPosition is { } ship
                            && (Game.PlayerPosition - Game.ScenePos(ship.x, ship.y, ship.z)).sqrMagnitude <= AtHomeShipRadius * AtHomeShipRadius;
            s.AtHome = Game.Aboard || !string.IsNullOrEmpty(Game.StationName) || nearShip;
            s.HostileDistance = Game.NearestHostileSqr >= float.MaxValue ? float.MaxValue : Mathf.Sqrt(Game.NearestHostileSqr);
            s.SecondsSinceDamage = now - _lastDamageAt;
            s.SecondsSinceSpaceCombat = now - _lastSpaceCombatAt;
            s.Health = Mathf.Clamp01(Game.Health / 100f);
            s.Oxygen = Mathf.Clamp01(Game.Oxygen / Mathf.Max(1f, Game.SuitOxygenMax));
        }

        /// <summary>Where a shot's anchor is on this client (falls back to the player). #2613: the ship, the heal tank and
        /// an authored anchor are canonical world positions; the camera frames them at the copy nearest the player
        /// (<see cref="GameBootstrap.ScenePos"/>) — across the seam the raw spot lay a whole circumference away.</summary>
        private Vector3 AnchorPosition(string anchor)
        {
            Vector3 player = Game.PlayerPosition != Vector3.zero ? Game.PlayerPosition
                : Camera != null ? Camera.transform.position : Vector3.zero;
            switch (anchor)
            {
                case CutsceneAnchors.Ship:
                    return Game.ShipPosition is { } ship ? Game.ScenePos(ship.x, ship.y, ship.z) : player;
                case CutsceneAnchors.HealTank:
                    // #2474: the own ship's real heal tank on this world (its medbay cell), near enough to frame.
                    if (CutsceneRules.TryFindHealTank(Game.Stations, player.x, player.y, player.z, Game.Circumference, out float tx, out float ty, out float tz))
                    {
                        return Game.ScenePos(tx, ty, tz);
                    }

                    return Game.ShipPosition is { } tankShip ? Game.ScenePos(tankShip.x, tankShip.y, tankShip.z) : player;
                case CutsceneAnchors.Core:
                case CutsceneAnchors.Custom:
                    return _current != null && _current.HasAnchor ? Game.ScenePos(_current.AnchorX, _current.AnchorY, _current.AnchorZ) : player;
                default:
                    return player; // player, planet, station, star_map
            }
        }

        /// <summary>#2474: whether the object an anchor names is on this world for this client (the heal tank — not in the
        /// space view, nothing world-anchored there; #2478: the other players, anywhere on the star map —
        /// <see cref="CutsceneRules.NeedsObject"/>).</summary>
        private bool AnchorFound(string anchor)
        {
            if (anchor == CutsceneAnchors.Players)
            {
                return CutsceneChart.AnyOtherPlayer(Game.StarMap, Game.PlayerName); // #2478: alone, "Many minds" stays a card
            }

            if (anchor != CutsceneAnchors.HealTank)
            {
                return true;
            }

            Vector3 at = Game.PlayerPosition;
            return !Game.SpaceViewActive
                   && CutsceneRules.TryFindHealTank(Game.Stations, at.x, at.y, at.z, Game.Circumference, out _, out _, out _);
        }

        /// <summary>The ship-size factor of the player's own landed ship (<see cref="PrologueOrbit.Scale"/>), 1 until its
        /// structure arrives.</summary>
        private float OwnShipScale()
        {
            string me = Game.LocalPlayerId;
            if (string.IsNullOrEmpty(me))
            {
                return 1f;
            }

            foreach (var ship in Game.LandedShips.Values)
            {
                if (ship.OwnerId == me && ship.StructureId.StartsWith("ship:", System.StringComparison.Ordinal))
                {
                    return PrologueOrbit.Scale(ship.Width, ship.Length);
                }
            }

            return 1f;
        }

        private string L(string key) => Game?.Localizer?.Get(key) ?? key;

        private void OnDestroy()
        {
            if (_subscribed && Game?.Network != null)
            {
                Game.Network.CutscenePlayReceived -= OnCutscenePlay;
                Game.Network.StoryStateReceived -= OnStoryState;
                Game.Network.CoreDialogueReceived -= OnCoreDialogue;
            }

            CloseDuel(); // #2478: its frame is a root-level object, its gate reason and hold must not outlive the world
            TearDownScene();
            foreach (var (screen, _, _, _) in _closing)
            {
                screen.Destroy();
            }

            _closing.Clear();
            _queue.Clear();
            ClientMusic.CutsceneTrack = null;
        }
    }
}
