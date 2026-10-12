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
    /// menu sees it; the camera itself is written by <see cref="CutsceneCameraRig"/> at order 211.
    /// </summary>
    [DefaultExecutionOrder(-20)]
    public sealed class CutsceneDirector : MonoBehaviour
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
        private const float AtHomeShipRadius = 24f;
        private const float FallingSpeed = -6f;

        /// <summary>The live director (one per world rig), or null.</summary>
        public static CutsceneDirector Instance { get; private set; }

        /// <summary>True while the director shows a card or a scene (T2+): VEGA's panel and the ending wait for it.</summary>
        public static bool HoldsScreen => Instance != null && Instance._play != null && Instance._play.Plan.Tier >= CutsceneTiers.Card;

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
        private readonly List<(CutsceneScreen Screen, float Left, float Letterbox)> _closing = new List<(CutsceneScreen, float, float)>();
        private readonly StagingRetry _stagingRetry = new StagingRetry();

        private bool _subscribed;
        private CutscenePlayback _play;
        private QueuedCutscene _current;
        private CutsceneScreen _screen;
        private CutsceneCameraRig _rig;
        private WorldLoadingOverlay _veil;
        private System.Func<string, string> _localize;

        private bool _gate;
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
        private float _flash;

        private float _lastHealth = -1f;
        private float _lastDamageAt = float.NegativeInfinity;
        private float _lastIntegrity = -1f;
        private float _lastSpaceCombatAt = float.NegativeInfinity;

        private void Awake()
        {
            Instance = this;
            _localize = L;
            _rig = gameObject.AddComponent<CutsceneCameraRig>();
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
        /// has — the world story first, then the others by priority.</summary>
        public IReadOnlyList<CutsceneReplayEntry> ReplayEntries()
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
            return CutsceneCatalog.ReplayEntries(packs, _serverSeen.Concat(_localSeen));
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
            else
            {
                StartNext();
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

                    Begin(item, owed.Downgraded ? plan.AsCard() : plan);
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

            // A card keeps the input free, so the Tab menu may open over it: the card waits (hidden) until it closes.
            if ((card || banner) && !_gate && Game.MenuOpen)
            {
                _screen.Visible = false;
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
        /// to 4 s for streaming"), showing the player's view meanwhile.</summary>
        private void StageShot()
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
            var staged = CutsceneStaging.Stage(shot.Camera, path, spot, center.x, center.y, center.z, scale, Game.PlayerYaw, skyOpen, chart);
            bool worldShot = shot.Camera != CutsceneCameras.Chart && shot.Camera != CutsceneCameras.Sky;
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
                ShowChart();
                return;
            }

            _screen.HideChart();
            _chartShown = false;
            _rig.Apply(staged, path, spot);
        }

        private void ShowPlayerView()
        {
            _rig.Release();
            _screen.HideChart();
            _chartShown = false;
        }

        /// <summary>The galaxy chart (<c>chart</c> camera): every star of the map, the known ones by name, the current one
        /// ringed, the relay lanes.</summary>
        private void ShowChart()
        {
            var map = Game.StarMap;
            var stars = new List<GalaxyChartWidget.Star>();
            var lanes = new List<(string A, string B)>();
            string here = SystemOfBody(map, map?.ActiveLocationId);
            foreach (var sys in map?.Systems ?? System.Array.Empty<NetStarSystem>())
            {
                bool current = sys.Id == here;
                bool known = current || Game.KnowsSystem(sys.Id);
                stars.Add(new GalaxyChartWidget.Star
                {
                    Id = sys.Id,
                    MapX = sys.MapX,
                    MapY = sys.MapY,
                    Label = known ? sys.Name : "?",
                    Color = sys.StarColor != 0 ? Rgb(sys.StarColor) : new Color(1f, 0.86f, 0.55f),
                    Current = current,
                    Known = known,
                    Finale = sys.Id != null && sys.Id.StartsWith("guardian_finale", System.StringComparison.Ordinal),
                    Players = string.Empty,
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

            _screen.ShowChart(stars, lanes);
            _chartShown = true;
        }

        private static string SystemOfBody(StarMapData map, string bodyId)
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

        private static Color Rgb(int rgb) => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        /// <summary>The camera may only be taken by a staged scene (T3/T4). The shared cutscene state — HUD away, controls
        /// frozen, Esc to the director — is on while such a scene takes the camera or freezes the controls, and not after
        /// the multiplayer cap. A singleplayer world is held only while the camera is actually taken (synthesis §5.3
        /// "Stufen"); in multiplayer a hold would be a group vote, so the director never asks.</summary>
        private void UpdateGateAndHold(CutscenePlan plan, bool cameraTaken)
        {
            bool gate = plan.Tier >= CutsceneTiers.Scene && !_capped && (plan.LockInput || cameraTaken);
            if (gate != _gate)
            {
                _gate = gate;
                GameBootstrap.Cutscenes.Set(GateReason, gate);
                Game.SetCursorOwner(this, gate); // the continue and skip buttons are clickable while the scene owns the screen
            }

            bool hold = plan.HoldWorld && cameraTaken && !_inputs.Multiplayer;
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
            float fade;
            if (plan.OpensFromBlack)
            {
                fade = Mathf.Clamp01(1f - _play.Elapsed / 1.2f); // out of black
                if (reader)
                {
                    fade = Mathf.Max(fade, 0.55f);
                }
            }
            else
            {
                fade = banner ? 0f : reader ? 0.55f : 0.18f; // a card dims the live view a little
            }

            _screen.SetFade(fade);

            // Speaker: the first speaker caption on screen names who talks; its line-start cue plays once per page.
            CutsceneSpeaker speaker = null;
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
                        if (!string.IsNullOrEmpty(speaker.LineStartCue))
                        {
                            ClientAudio.Instance?.Cue(speaker.LineStartCue);
                        }
                    }

                    break;
                }
            }

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

        /// <summary>Who a speaker box shows: the pack's narrator (name key, icon, colour, line-start cue from the pack
        /// manifest), or for VEGA without a manifest entry her defaults; another speaker id reads as its own name.</summary>
        private CutsceneSpeaker SpeakerFor(StoryDefinition pack, string speakerId)
        {
            var narrator = pack?.Narrator;
            string id = string.IsNullOrEmpty(speakerId) ? (narrator?.Id ?? StoryNarrator.DefaultId) : speakerId;
            bool fromPack = narrator != null && id == narrator.Id;
            bool vega = id == StoryNarrator.DefaultId;
            string nameKey = fromPack && !string.IsNullOrEmpty(narrator.NameKey) ? narrator.NameKey : vega ? "ui.vega.name" : string.Empty;
            string icon = fromPack && !string.IsNullOrEmpty(narrator.Icon) ? narrator.Icon : vega ? "icon_vega" : string.Empty;
            var color = vega ? UiKit.Cyan : new Color(1f, 0.82f, 0.55f);
            if (fromPack && !string.IsNullOrEmpty(narrator.Color) && ColorUtility.TryParseHtmlString(narrator.Color, out var parsed))
            {
                color = parsed;
            }

            return new CutsceneSpeaker
            {
                Name = nameKey.Length > 0 ? L(nameKey) : id,
                Icon = icon.Length > 0 ? UiKit.Icon(icon) : null,
                Color = color,
                LineStartCue = fromPack && !string.IsNullOrEmpty(narrator.LineStart) ? narrator.LineStart : vega ? "ai_blip" : string.Empty,
            };
        }

        /// <summary>Ends the playing scene: the camera, the hold, the shared state, the music and the grade go back; the
        /// receipt goes out (not for a replay, a local play or the recap); an interrupted scene gives its narrator lines back
        /// to the panel and leaves a banner pointing to the Story tab.</summary>
        private void Finish(Outcome outcome)
        {
            var item = _current;
            var plan = _play?.Plan;
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

            if (!item.Requested && !plan.IsLocalBanner && !plan.IsRecap)
            {
                Game?.Network?.SendCutsceneSeen(item.PackId, new[] { item.Id }, skipped: outcome == Outcome.Skipped);
                _localSeen.Add(CutsceneCatalog.SeenKey(item.PackId, item.Id));
            }
        }

        /// <summary>A waiting scene that will not play (a "now or never" moment that found the screen busy, or a scene that
        /// waited too long even as a card): its lines go back to the panel and it is confirmed, so the server does not
        /// offer it again — the Story tab can still replay it.</summary>
        private void AbortWaiting(QueuedCutscene item, string reason)
        {
            _queue.Remove(item);
            Debug.Log($"[Cutscene] Dropped {item.Key} ({reason}).");
            if (item.Payload is Owed owed)
            {
                GiveLinesBack(owed);
            }

            if (!item.Requested && item.DefinitionId != StoryCutsceneIds.Recap)
            {
                Game?.Network?.SendCutsceneSeen(item.PackId, new[] { item.Id });
                _localSeen.Add(CutsceneCatalog.SeenKey(item.PackId, item.Id));
            }
        }

        private static void GiveLinesBack(Owed owed)
        {
            var panel = VegaPanel.Instance;
            foreach (var line in owed.Lines)
            {
                panel?.Requeue(line);
            }

            owed.Lines.Clear();
        }

        private void QueueSavedBanner(string packId)
        {
            var plan = CutscenePlan.LocalBanner(packId, string.Empty, L("ui.cutscene.saved"));
            var item = new QueuedCutscene(packId, StoryCutsceneIds.BuiltInPrefix + "saved", CutsceneTiers.Banner)
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
            if (_gate)
            {
                _gate = false;
                GameBootstrap.Cutscenes.End(GateReason);
                Game?.SetCursorOwner(this, false);
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
                _closing.Add((_screen, ScreenCloseSeconds, _letterbox));
                _screen = null;
            }
        }

        /// <summary>Lets the letterbox and the dim of ended scenes ease out instead of vanishing in one frame.</summary>
        private void TickClosingScreens()
        {
            for (int i = _closing.Count - 1; i >= 0; i--)
            {
                var (screen, left, box) = _closing[i];
                left -= Time.deltaTime;
                float k = Mathf.Clamp01(left / ScreenCloseSeconds);
                screen.SetLetterbox(CinematicTimeline.EaseInOut(box * k));
                screen.SetFade(0f);
                if (left <= 0f)
                {
                    screen.Destroy();
                    _closing.RemoveAt(i);
                }
                else
                {
                    _closing[i] = (screen, left, box);
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
            var ending = ResolutionCinematic.Instance;
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
            s.DuelOpen = FinaleView.Instance != null && FinaleView.Instance.DuelActive;
            s.Falling = Game.PlayerVerticalVelocity < FallingSpeed && !Game.Aboard;
            s.Climbing = Player != null && Player.ClimbingWall;
            s.HeadUnderwater = Player != null && Player.IsHeadUnderwater();
            s.InVehicle = !string.IsNullOrEmpty(Game.InSpeeder) || !string.IsNullOrEmpty(Game.InTrain);
            s.InEva = Game.InEva;
            s.PilotingInSpace = Game.SpaceViewActive && !Game.SpaceAutomaticTransit;
            s.Warping = WormholeTransitFx.Busy || Game.SpaceAutomaticTransit;
            bool nearShip = Game.ShipPosition is { } ship && (Game.PlayerPosition - ship).sqrMagnitude <= AtHomeShipRadius * AtHomeShipRadius;
            s.AtHome = Game.Aboard || !string.IsNullOrEmpty(Game.StationName) || nearShip;
            s.HostileDistance = Game.NearestHostileSqr >= float.MaxValue ? float.MaxValue : Mathf.Sqrt(Game.NearestHostileSqr);
            s.SecondsSinceDamage = now - _lastDamageAt;
            s.SecondsSinceSpaceCombat = now - _lastSpaceCombatAt;
            s.Health = Mathf.Clamp01(Game.Health / 100f);
            s.Oxygen = Mathf.Clamp01(Game.Oxygen / Mathf.Max(1f, Game.SuitOxygenMax));
        }

        /// <summary>Where a shot's anchor is on this client (falls back to the player).</summary>
        private Vector3 AnchorPosition(string anchor)
        {
            Vector3 player = Game.PlayerPosition != Vector3.zero ? Game.PlayerPosition
                : Camera != null ? Camera.transform.position : Vector3.zero;
            switch (anchor)
            {
                case CutsceneAnchors.Ship:
                    return Game.ShipPosition ?? player;
                case CutsceneAnchors.HealTank:
                    foreach (var st in Game.Stations ?? System.Array.Empty<NetShipStation>())
                    {
                        if (st != null && st.Type == "medbay")
                        {
                            return new Vector3(st.X, st.Y, st.Z);
                        }
                    }

                    return Game.ShipPosition ?? player;
                case CutsceneAnchors.Core:
                case CutsceneAnchors.Custom:
                    return _current != null && _current.HasAnchor ? new Vector3(_current.AnchorX, _current.AnchorY, _current.AnchorZ) : player;
                default:
                    return player; // player, planet, station, star_map
            }
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
            if (Instance == this)
            {
                Instance = null;
            }

            if (_subscribed && Game?.Network != null)
            {
                Game.Network.CutscenePlayReceived -= OnCutscenePlay;
                Game.Network.StoryStateReceived -= OnStoryState;
            }

            TearDownScene();
            foreach (var (screen, _, _) in _closing)
            {
                screen.Destroy();
            }

            _closing.Clear();
            _queue.Clear();
            ClientMusic.CutsceneTrack = null;
        }
    }
}
