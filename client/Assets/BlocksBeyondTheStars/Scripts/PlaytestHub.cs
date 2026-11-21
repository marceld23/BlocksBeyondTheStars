// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BlocksBeyondTheStars.Client.Feedback;
using BlocksBeyondTheStars.Client.Playtests;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Playtests;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>A playtest report the developers confirmed — what the "confirmed" dialog shows.</summary>
    public sealed class PlaytestConfirmation
    {
        public string TaskId = string.Empty;
        public string Text = string.Empty;
        public string FixedInVersion = string.Empty;
        public List<long> ReplyIds = new List<long>();
    }

    /// <summary>
    /// The Unity side of the "Test Pilot" page: one component on the shell that owns this install's playtest state
    /// (<see cref="PlaytestTracker"/> over <see cref="PlaytestProgress"/>), fetches the catalogue from the report inbox
    /// off the main thread, feeds events into the task being worked on — from the editors and the client's UI
    /// (<see cref="PlaytestSignals"/>) and, in a world, from the server (the playtest watch) — and sends the closing
    /// report through the ordinary feedback channel. All logic lives in Client.Core and is tested headless; this class
    /// is the wiring. Desktop only: a browser build never creates it (<see cref="Available"/>).
    /// </summary>
    public sealed class PlaytestHub : MonoBehaviour
    {
        /// <summary>The one hub, or null before the shell created it (and always in a browser build).</summary>
        public static PlaytestHub Instance { get; private set; }

        /// <summary>The Test Pilot page exists on desktop builds only.</summary>
        public static bool Available => Application.platform != RuntimePlatform.WebGLPlayer;

        public AppShell Shell;

        private PlaytestCatalogClient _client;
        private PlaytestWatchSync _watch;
        private Task<FetchOutcome> _fetch;
        private NetworkClient _boundNet;
        private float _boundAt;
        private bool _commandsChecked;
        private float _commandsCheckedAt;
        private bool _announced;
        private string _sessionId = Guid.NewGuid().ToString("N");

        private sealed class FetchOutcome
        {
            public PlaytestFetchResult Catalog;
            public List<PlaytestOwnReport> Mine;
        }

        /// <summary>This install's tasks, progress and badges.</summary>
        public PlaytestTracker Tracker { get; private set; }

        /// <summary>The progress file behind <see cref="Tracker"/>.</summary>
        public PlaytestProgress Progress => Tracker.Progress;

        /// <summary>The report inbox this build talks to, and its spam key.</summary>
        public string IngestEndpoint { get; private set; } = FeedbackUploader.DefaultEndpoint;

        /// <summary>See <see cref="IngestEndpoint"/>.</summary>
        public string ApiKey { get; private set; } = string.Empty;

        /// <summary>False in a build without the inbox key (a local or forked build): the page says so.</summary>
        public bool Configured => _client != null && _client.IsConfigured;

        /// <summary>True while a catalogue fetch is running.</summary>
        public bool Fetching => _fetch != null;

        /// <summary>When the shown catalogue was fetched (unix seconds; 0 = never).</summary>
        public long FetchedUnix { get; private set; }

        /// <summary>Counts up whenever something the screens show changed — they rebuild when it moved.</summary>
        public int Revision { get; private set; }

        /// <summary>Confirmed reports waiting for their "confirmed" dialog (shown in a menu, never over the world).</summary>
        public Queue<PlaytestConfirmation> Confirmations { get; } = new Queue<PlaytestConfirmation>();

        /// <summary>#3848: titles of tasks whose last open step the game just recognised in a world — the HUD's
        /// celebration banner says "all steps done" for each (HudUi.Playtest.cs).</summary>
        public Queue<string> ReadyBanners { get; } = new Queue<string>();

        /// <summary>#3849: when the game last recognised a step (<see cref="Time.realtimeSinceStartup"/>) — an editor has
        /// no status line for it, so the list's chip says it for a few seconds.</summary>
        public float RecognisedAt { get; private set; } = -100f;

        /// <summary>Whether the server of the current world reports playtest events: null = not known yet (or no world),
        /// false = an older server — the player ticks those steps off by hand.</summary>
        public bool? ServerReportsEvents => _boundNet != null ? _watch.Supported : null;

        /// <summary>Whether the server of the current world reports this game event: false = it reports nothing, or it
        /// left this event out of its answer (an event newer than that server) — the player ticks the step off by hand
        /// (#3833). Null = not known (no world, no answer yet, or the step is not being waited for).</summary>
        public bool? ServerReports(string eventPattern) => _boundNet != null ? _watch.Reports(eventPattern) : null;

        private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        /// <summary>Creates the hub on the shell's object (desktop builds only).</summary>
        public static void Create(AppShell shell)
        {
            if (!Available || Instance != null)
            {
                return;
            }

            var hub = shell.gameObject.AddComponent<PlaytestHub>();
            hub.Shell = shell;
            hub.Init();
        }

        /// <summary>A hub for an edit-mode test: the given catalogue, progress in memory — no inbox, no file, no fetch.
        /// <paramref name="planetsKnown"/> answers every "does this build know the planet" question (the test shell has
        /// no content), so both the start button and the refusal of a test world can be built.</summary>
        public static PlaytestHub CreateForTest(AppShell shell, PlaytestCatalog catalog, bool planetsKnown)
        {
            var hub = shell.gameObject.AddComponent<PlaytestHub>();
            hub.Shell = shell;
            hub.Tracker = new PlaytestTracker(new PlaytestProgress(string.Empty));
            hub._watch = new PlaytestWatchSync(_ => { });
            hub._planetKnownForTest = planetsKnown;
            hub.Tracker.SetCatalog(catalog, AppShell.Version);
            Instance = hub;
            return hub;
        }

        private bool? _planetKnownForTest;

        /// <summary>For an edit-mode test of the editor chip: as if the game had recognised a step just now.</summary>
        public void MarkRecognisedForTest() => RecognisedAt = Time.realtimeSinceStartup;

        private void Init()
        {
            Instance = this;
            string dir = Path.Combine(AppPaths.Root, "playtests");
            Tracker = new PlaytestTracker(new PlaytestProgress(Path.Combine(dir, "progress.json")));
            Tracker.StepRecognised += OnStepRecognised;
            Tracker.TaskReady += OnTaskReady;

            // A development build may point at the developer's own inbox; a release build always uses the official one.
            if (PlaytestDevInbox.TryRead(AppShell.Version, Environment.GetEnvironmentVariable, out string devEndpoint, out string devKey))
            {
                IngestEndpoint = devEndpoint;
                ApiKey = devKey;
            }
            else
            {
                ApiKey = BlocksBeyondTheStars.Build.BugReportBuildSecrets.ApiKey ?? string.Empty;
            }

            _client = new PlaytestCatalogClient(PlaytestCatalogClient.EndpointFor(IngestEndpoint), ApiKey, Path.Combine(dir, "catalog.json"));
            _watch = new PlaytestWatchSync(patterns => _boundNet?.SendPlaytestWatch(patterns));
            var cached = _client.Cached();
            FetchedUnix = cached.FetchedUnix;
            Tracker.SetCatalog(cached.Catalog, AppShell.Version);

            // Nothing phones home without a reason: the start-up fetch belongs to installs whose player has opened
            // the Test Pilot page at least once. Everybody else asks the inbox first when they open it.
            if (Progress.Visited)
            {
                Refresh(playerAsked: false);
            }
        }

        private void OnEnable() => PlaytestSignals.Raised += OnSignal;

        private void OnDisable() => PlaytestSignals.Raised -= OnSignal;

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>Asks the inbox for the catalogue when one is due — always when the player opened the page.</summary>
        public void Refresh(bool playerAsked)
        {
            if (_fetch != null || _client == null || !_client.ShouldFetch(Now, playerAsked))
            {
                return;
            }

            var client = _client;
            string replyKey = FeedbackUi.ReplyKeyFor(Shell.Settings);
            long now = Now;
            _fetch = Task.Run(() => new FetchOutcome { Catalog = client.Fetch(now), Mine = client.FetchMine(replyKey) });
            Revision++;
        }

        private void Update()
        {
            if (_fetch != null && _fetch.IsCompleted)
            {
                var task = _fetch;
                _fetch = null;
                if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                {
                    ApplyFetch(task.Result);
                }

                Revision++;
            }

            BindToWorld();
            if (_boundNet != null && _boundNet.Connected)
            {
                _watch.Update(Tracker.WatchPatterns(), Time.realtimeSinceStartupAsDouble);
                RunStartCommandsOnce();
                AnnounceTaskOnce();
            }
        }

        private void ApplyFetch(FetchOutcome outcome)
        {
            if (outcome.Catalog != null)
            {
                Tracker.SetCatalog(outcome.Catalog.Catalog, AppShell.Version);
                FetchedUnix = _client.Cached().FetchedUnix;
            }

            if (outcome.Mine == null)
            {
                return;
            }

            foreach (string taskId in Tracker.ApplyOwnReports(outcome.Mine, Now))
            {
                // Confirmed while this install was not looking (the thank-you was read on another start, or the progress
                // file is new): the badge is gold now; the dialog needs no text of its own.
                if (!HasConfirmationFor(taskId))
                {
                    Confirmations.Enqueue(new PlaytestConfirmation { TaskId = taskId });
                }
            }
        }

        private bool HasConfirmationFor(string taskId)
        {
            foreach (var waiting in Confirmations)
            {
                if (waiting.TaskId == taskId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A developer reply thread arrived (the feedback poll). When it is the thank-you to one of this install's
        /// playtest reports and needs no answer, the hub takes it: the badge turns gold and the "confirmed" dialog
        /// shows the text. Returns whether it was taken — the feedback window then does not show the thread itself.
        /// </summary>
        public bool TakeConfirmation(FeedbackReplyThread thread)
        {
            if (thread == null || thread.Status != "done" || thread.AwaitsAnswer)
            {
                return false;
            }

            string taskId = TaskOfReport(thread.ReportId);
            if (taskId == null)
            {
                return false;
            }

            Progress.MarkConfirmed(thread.ReportId);
            string text = string.Empty;
            for (int i = thread.Replies.Count - 1; i >= 0 && text.Length == 0; i--)
            {
                if (thread.Replies[i].IsDev)
                {
                    text = thread.Replies[i].Text;
                }
            }

            Confirmations.Enqueue(new PlaytestConfirmation
            {
                TaskId = taskId,
                Text = text,
                FixedInVersion = thread.FixedInVersion,
                ReplyIds = new List<long>(thread.UnseenIds),
            });
            Revision++;
            Shell.CurrentBoot?.ShowMessage(Shell.L("ui.testpilot.confirmed.toast"));
            return true;
        }

        private string TaskOfReport(string reportId)
        {
            foreach (var task in Tracker.Tasks)
            {
                var entry = Progress.Get(task.Id);
                if (entry != null && entry.ReportId == reportId)
                {
                    return task.Id;
                }
            }

            return null;
        }

        /// <summary>The "confirmed" dialog was read: its developer entries count as seen.</summary>
        public void Acknowledge(PlaytestConfirmation confirmation)
        {
            if (confirmation == null || confirmation.ReplyIds.Count == 0 || !Configured)
            {
                return;
            }

            var replies = new FeedbackReplyClient(FeedbackReplyClient.EndpointFor(IngestEndpoint), ApiKey);
            string key = FeedbackUi.ReplyKeyFor(Shell.Settings);
            var ids = confirmation.ReplyIds;
            _ = Task.Run(() => replies.Ack(key, ids));
        }

        // ---------------------------------------------------------------- events

        private void OnSignal(string key, string arg, int amount)
        {
            if (Tracker.OnEvent(key, arg, amount, Now) > 0)
            {
                Revision++;
            }
        }

        private void OnStepRecognised(PlaytestTask task, PlaytestStep step)
        {
            string line = string.Format(Shell.L("ui.testpilot.step_recognised"), step.Text.For(Shell.Settings.Language));
            Shell.CurrentBoot?.ShowMessage(line);
            RecognisedAt = Time.realtimeSinceStartup;
            Revision++;
        }

        /// <summary>#3848: the game recognised the last open step. In a world the HUD's banner says so (an editor's list
        /// and the page show it themselves).</summary>
        private void OnTaskReady(PlaytestTask task)
        {
            if (Shell.CurrentBoot != null && task.Area != PlaytestAreas.Editor)
            {
                ReadyBanners.Enqueue(task.Title.For(Shell.Settings.Language));
            }
        }

        /// <summary>The player closed (or re-opened) a step by hand.</summary>
        public void SetStep(PlaytestTask task, string stepId, string state, string note)
        {
            if (Progress.SetStep(task, stepId, state, note, Now))
            {
                Revision++;
            }
        }

        /// <summary>Starts or goes on with a task; false when it cannot be worked on (its report is sent).</summary>
        public bool Activate(string taskId)
        {
            bool ok = Tracker.Activate(taskId, Now);
            Revision++;
            return ok;
        }

        /// <summary>Puts the task being worked on aside.</summary>
        public void Deactivate()
        {
            Tracker.Deactivate();
            Revision++;
        }

        // ---------------------------------------------------------------- the world's server

        private void BindToWorld()
        {
            var net = Shell.CurrentBoot != null ? Shell.CurrentBoot.Network : null;
            if (ReferenceEquals(net, _boundNet))
            {
                return;
            }

            Unbind();
            _boundNet = net;
            _watch.Reset();
            _commandsChecked = false;
            _announced = false;
            ReadyBanners.Clear(); // a banner belongs to the world its step closed in
            _boundAt = Time.realtimeSinceStartup;
            if (net != null)
            {
                net.PlaytestWatchAckReceived += OnWatchAck;
                net.PlaytestEventReceived += OnServerEvent;
            }

            Revision++;
        }

        private void Unbind()
        {
            if (_boundNet != null)
            {
                _boundNet.PlaytestWatchAckReceived -= OnWatchAck;
                _boundNet.PlaytestEventReceived -= OnServerEvent;
                _boundNet = null;
            }
        }

        private void OnWatchAck(PlaytestWatchAck ack)
        {
            bool known = _watch.Supported == true;
            _watch.OnAck(ack.Patterns);
            if (!known || _watch.LeftSomethingOut)
            {
                Revision++; // a step this server will never report shows its two buttons now (#3833)
            }
        }

        private void OnServerEvent(PlaytestEvent e)
        {
            if (Tracker.OnEvent(e.Key, e.Arg, e.Amount, Now) > 0)
            {
                Revision++;
            }
        }

        // ---------------------------------------------------------------- test worlds

        /// <summary>Why the task's test world cannot be created here, or <see cref="PlaytestWorldRefusal.None"/>.</summary>
        public PlaytestWorldRefusal WorldRefusal(PlaytestTask task) => PlanFor(task).Refusal;

        private PlaytestWorldPlan PlanFor(PlaytestTask task)
            => PlaytestWorldPlan.For(task, key => _planetKnownForTest ?? (Shell.Content != null && Shell.Content.GetPlanet(key) != null),
                Shell.Settings.Language);

        /// <summary>Creates (or opens) the task's test world and starts it; the task becomes the one being worked on.
        /// False when the world cannot be created by this build.</summary>
        public bool StartTestWorld(PlaytestTask task)
        {
            var plan = PlanFor(task);
            if (plan.Refusal != PlaytestWorldRefusal.None)
            {
                return false;
            }

            var entry = Progress.Get(task.Id);
            bool sameWorld = entry != null && entry.World == plan.WorldName;
            Progress.SetWorld(task, plan.WorldName, sameWorld && entry.CommandsRan, Now);
            Tracker.Activate(task.Id, Now);
            Revision++;

            bool sandbox = plan.Preset == PlaytestWorldPresets.Sandbox;
            Shell.StartSingleplayerWorld(plan.WorldName, plan.Seed, creativeUnlockAll: sandbox, creativeAllShips: sandbox,
                creativeKit: sandbox, sandbox: sandbox, worldOptions: PlaytestWorldOptions.From(plan), creativeFlight: sandbox);
            return true;
        }

        /// <summary>
        /// Runs a test world's start commands, once: only in a local world this install created for a task (its name
        /// carries the prefix AND the progress file knows it), a few seconds after the world came up. Every command is
        /// on the fixed list — the plan dropped everything else.
        /// </summary>
        private void RunStartCommandsOnce()
        {
            if (_commandsChecked || Time.realtimeSinceStartup - _boundAt < 4f)
            {
                return;
            }

            _commandsChecked = true;
            _commandsCheckedAt = Time.realtimeSinceStartup;
            string world = Shell.Settings.LastWorld;
            if (!Shell.PlayingLocalWorld || !PlaytestWorldPlan.IsTestWorldName(world))
            {
                return;
            }

            string taskId = Progress.TaskOfWorld(world);
            var task = Tracker.Find(taskId);
            var entry = task != null ? Progress.Get(task.Id) : null;
            if (task == null || entry == null || entry.CommandsRan)
            {
                return;
            }

            foreach (var command in PlanFor(task).Commands)
            {
                _boundNet.SendAdminCommand(command.Cmd, command.Arg.Length > 0 ? command.Arg : null, command.Count,
                    null, command.X, command.Y, command.Z);
            }

            Progress.SetWorld(task, world, commandsRan: true, Now);
        }

        /// <summary>
        /// #3848: says once per world which task is being worked on, as soon as the world is on the screen — the start
        /// card in the world this install created for the task, one status line in any other world
        /// (<see cref="PlaytestGuide.StartNoticeFor"/>). It waits for the start commands: the card holds the world, and
        /// a held singleplayer server would drop a command sent meanwhile (the lamp a task hands out).
        /// </summary>
        private void AnnounceTaskOnce()
        {
            var boot = Shell.CurrentBoot;
            if (_announced || !_commandsChecked || boot == null || Time.realtimeSinceStartup - _commandsCheckedAt < 1.5f
                || Shell.Phase != ShellPhase.InGame || !boot.WorldReady || boot.MenuOpen || boot.ChatTyping
                || GameBootstrap.CutsceneActive || boot.VegaPrologueActive || PlaytestModal.AnyOpen)
            {
                return;
            }

            _announced = true;
            var notice = PlaytestGuide.StartNoticeFor(Tracker, Shell.PlayingLocalWorld, Shell.Settings.LastWorld);
            if (notice == PlaytestStartNotice.Card)
            {
                PlaytestStartCard.Open(Shell, Tracker.Active);
            }
            else if (notice == PlaytestStartNotice.Line)
            {
                boot.ShowMessage(string.Format(Shell.L("ui.testpilot.running"), Tracker.Active.Title.For(Shell.Settings.Language)));
            }
        }

        // ---------------------------------------------------------------- the report

        /// <summary>
        /// Builds the closing report of <paramref name="task"/> — or says why not (an open step, an unanswered
        /// question). The caller uploads <see cref="PlaytestReportResult.Report"/> with <see cref="Upload"/>.
        /// </summary>
        public PlaytestReportResult BuildReport(PlaytestTask task, PlaytestReportForm form)
        {
            form.Locale = Shell.Settings.Language;
            // #3838: how the game was drawn rides along from inside a world only — the privacy text promises a bug
            // report's technical data for exactly that case, and a menu or an editor does not depend on it.
            var net = Shell.CurrentBoot != null ? Shell.CurrentBoot.Network : null;
            var graphics = net != null && net.Connected ? FeedbackUi.GraphicsInfo(Shell.Settings) : null;
            return PlaytestReport.Build(task, Progress, form, AppShell.Version, FeedbackUi.ReplyKeyFor(Shell.Settings), _sessionId,
                Application.platform.ToString(), DateTime.UtcNow, graphics);
        }

        /// <summary>Sends the report (blocking — call from a background task) and returns the uploader's result.</summary>
        public FeedbackUploadResult Upload(string json) => new FeedbackUploader(IngestEndpoint, ApiKey).UploadRawJson(json);

        /// <summary>
        /// The upload finished: an accepted report earns the badge and is remembered for the reply poll; a report the
        /// inbox could not be reached for is queued like any feedback and the badge is earned all the same (the report
        /// goes out with a later start). Returns whether the task counts as sent.
        /// </summary>
        public bool OnUploaded(PlaytestTask task, FeedbackReport report, string json, FeedbackUploadResult result)
        {
            if (result != null && result.Ok && result.ReportId.Length > 0)
            {
                Progress.RecordSent(task, result.ReportId, Now);
                new SentReportsLog(Path.Combine(AppPaths.Root, "feedback", "sent.json")).Record(result.ReportId, report.Title, Now);
            }
            else if (result != null && result.StatusCode >= 400 && result.StatusCode < 500)
            {
                return false; // the inbox refused this body — sending the same again cannot work
            }
            else if (new FeedbackSpool(Path.Combine(AppPaths.Root, "feedback")).Write(json) != null)
            {
                Progress.RecordSent(task, PlaytestProgress.QueuedPrefix + Guid.NewGuid().ToString("N"), Now);
            }
            else
            {
                return false;
            }

            // In a world the server's rich snapshot goes out too, as with an F1 report; the inbox pairs the two rows.
            var net = Shell.CurrentBoot != null ? Shell.CurrentBoot.Network : null;
            if (net != null && net.Connected)
            {
                net.SendBumpReport("[feedback] " + report.Title + " — " + report.Description, Array.Empty<byte>(), AppShell.Version,
                    report.ReplyKey, report.Title);
            }

            if (Tracker.Active != null && Tracker.Active.Id == task.Id)
            {
                Tracker.Deactivate();
            }

            Revision++;
            return true;
        }
    }

    /// <summary>Turns a task's world plan into the create-world options. Only the names on the plan's own list reach
    /// here; each is set explicitly (no reflection — the player build strips unused members).</summary>
    public static class PlaytestWorldOptions
    {
        public static WorldCreationOptions From(PlaytestWorldPlan plan)
        {
            var options = plan.Preset == PlaytestWorldPresets.Peaceful ? WorldCreationOptions.Peaceful()
                : plan.Preset == PlaytestWorldPresets.Hard ? WorldCreationOptions.HardSurvival(WorldCreationOptions.Standard())
                : WorldCreationOptions.Standard();
            options.StartPlanetType = plan.StartPlanet;

            // A test world is there for its task: no story unless the task asks for it ("story": true — a task about
            // the story's beginning), so no opening scene to sit through and no story missions between the tester and
            // the steps. (The first capture run showed the awakening scene over the task.)
            if (!plan.Story)
            {
                options.Stories.StoryId = BlocksBeyondTheStars.Shared.Story.StoryRegistry.NoneStoryId;
            }

            options.Stories.Tutorial = Math.Max(0, Array.IndexOf(WorldStoryOptions.TutorialCliWords, plan.Tutorial));
            foreach (var pair in plan.Options)
            {
                Set(options, pair.Key, pair.Value);
            }

            return options;
        }

        private static void Set(WorldCreationOptions o, string name, int value)
        {
            switch (name)
            {
                case "Creatures": o.Creatures = value; break;
                case "PlanetEnemies": o.PlanetEnemies = value; break;
                case "SpaceNpcs": o.SpaceNpcs = value; break;
                case "Ufos": o.Ufos = value; break;
                case "Bandits": o.Bandits = value; break;
                case "Flora": o.Flora = value; break;
                case "Ore": o.Ore = value; break;
                case "Settlements": o.Settlements = value; break;
                case "Wrecks": o.Wrecks = value; break;
                case "Vaults": o.Vaults = value; break;
                case "Stations": o.Stations = value; break;
                case "Wormholes": o.Wormholes = value; break;
                case "DebrisFields": o.DebrisFields = value; break;
                case "Derelicts": o.Derelicts = value; break;
                case "GiantWrecks": o.GiantWrecks = value; break;
                case "SpsTowers": o.SpsTowers = value; break;
                case "Exotic": o.Exotic = value; break;
                case "Oxygen": o.Oxygen = value; break;
                case "Hunger": o.Hunger = value; break;
                case "Hazards": o.Hazards = value; break;
                case "DeathPenalty": o.DeathPenalty = value; break;
                case "UniverseSize": o.UniverseSize = value; break;
            }
        }
    }
}

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// An editor that can hand over the thing being worked on for a playtest report — the report dialog's "send my work
    /// along" box. All seven player editors are one: a ship, a station or settlement module and a figure go as the bundle
    /// their Export writes, a texture, a form and a tool look as their draft (what is on the canvas, saved or not).
    /// </summary>
    public interface IPlaytestWorkSource
    {
        /// <summary>The work as one attachment, or null when there is nothing to send (an empty bench, a file above the
        /// inbox's size cap). Called on the main thread at the moment the player sends the report.</summary>
        BlocksBeyondTheStars.Client.Feedback.FeedbackAttachment WorkForPlaytest();
    }

    /// <summary>
    /// What the seven player editors report to the playtest tracker — one short call at the place where an editor has
    /// finished the thing (a save written, an export written, a submission sent, a design loaded). The editors run in
    /// the menu without a server, so these reach the task being worked on through <see cref="BlocksBeyondTheStars.Client.Playtests.PlaytestSignals"/>.
    /// A call with nobody listening costs a null check.
    /// </summary>
    public static class EditorPlaytest
    {
        public const string Ship = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Ship;
        public const string Avatar = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Avatar;
        public const string Station = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Station;
        public const string Settlement = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Settlement;
        public const string Texture = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Texture;
        public const string Form = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Form;
        public const string ToolLook = BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.ToolLook;

        public static void Opened(string editor) => Raise(BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Opened, editor);

        public static void Saved(string editor) => Raise(BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Saved, editor);

        /// <summary><see cref="Saved"/> when <paramref name="ok"/> — for a save that can fail.</summary>
        public static void SavedIf(bool ok, string editor)
        {
            if (ok)
            {
                Saved(editor);
            }
        }

        public static void Exported(string editor) => Raise(BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Exported, editor);

        public static void Submitted(string editor) => Raise(BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Submitted, editor);

        public static void Loaded(string editor) => Raise(BlocksBeyondTheStars.Shared.Playtests.PlaytestEditorEvents.Loaded, editor);

        /// <summary>The editor a submission came from: a texture, a ship, a station or settlement module, a skin or a
        /// HUD frame (both from the character editor).</summary>
        public static string OfSubmission(CreationSubmitRequest request)
        {
            string kind = request != null ? request.Kind : null;
            if (kind == BlocksBeyondTheStars.Client.Feedback.CreationSubmission.KindShip)
            {
                return Ship;
            }

            if (kind == BlocksBeyondTheStars.Client.Feedback.CreationSubmission.KindStructure)
            {
                var shell = PlaytestHub.Instance != null ? PlaytestHub.Instance.Shell : null;
                return shell != null ? shell.StructureEditorPlaytestKind : Station;
            }

            return kind == BlocksBeyondTheStars.Client.Feedback.CreationSubmission.KindAvatar
                   || kind == BlocksBeyondTheStars.Client.Feedback.CreationSubmission.KindHud
                ? Avatar
                : Texture;
        }

        private static void Raise(string key, string editor)
        {
            if (!string.IsNullOrEmpty(editor))
            {
                BlocksBeyondTheStars.Client.Playtests.PlaytestSignals.Raise(key, editor);
            }
        }
    }
}
