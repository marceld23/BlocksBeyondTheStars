// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What the flight view should play when it opens after a departure (#3141/#3142).</summary>
    public struct TransitionEmergence
    {
        /// <summary>True when a departure handed over to the flight view (the cover is shut and waits to open).</summary>
        public bool Active;

        /// <summary>The ship leaves a station's hangar (doors), not a planet's atmosphere.</summary>
        public bool FromStation;

        /// <summary>The departure was airless (the glare, no clouds, no sky to fade).</summary>
        public bool Airless;

        /// <summary>The sky the ship climbs out of (the flight view fades it to black).</summary>
        public Color Sky;

        /// <summary>The lift-off already played on the surface with its roar (the flight view does not roar again).</summary>
        public bool RoarPlayed;
    }

    /// <summary>
    /// The seamless, cinematic transitions between the surface (or a station) and the flight view (#3139–#3142). Before it
    /// every landing ended in a hard cut to a black loading screen, every launch cut straight from the ground to black space,
    /// and docking cut to the same veil. Now the picture never cuts:
    /// <list type="bullet">
    /// <item><b>Landing</b>: the flight view flies the descent and the atmospheric entry, then closes the
    /// <see cref="TransitionCover"/> (the cloud layer, or a glare on an airless body) and hands over behind it. The cover
    /// holds — the clouds keep drifting — until the server has set the player down on the destination and the ground has
    /// settled (<see cref="TransitionGate"/>), then opens onto the own ship sinking onto its pad with fire and dust; the
    /// camera glides into the player's eyes when it has touched down.</item>
    /// <item><b>Launch</b>: when the server opens the flight, the flight view waits (<see cref="ClaimDeparture"/>) while the
    /// ship lifts off on the real surface — ignition, dust, lift, the climb with its vapour cone — until it punches into the
    /// clouds; behind them the flight view opens (<see cref="TakeEmergence"/>) with the ship breaking out above the cloud
    /// layer as the sky fades to black.</item>
    /// <item><b>Stations</b>: the hangar doors close at the end of the dock approach and open inside; undocking closes them
    /// in the station and opens them on the ship sliding out of the hangar mouth.</item>
    /// </list>
    /// Purely presentation: the server moves the player exactly as before; this only decides what is on screen meanwhile.
    /// While a cinematic owns the camera it opens the <see cref="CutsceneGate"/> reason <see cref="Reason"/> (the HUD, the
    /// chat and the touch layer step aside, the controls freeze), and Esc / the pad's cancel skips it.
    /// </summary>
    [DefaultExecutionOrder(212)] // after every camera writer (the cutscene rig is 211): the shot is the last word
    public sealed partial class TransitionDirector : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public TransitionCover Cover;

        /// <summary>The cutscene-gate reason held while a transition cinematic owns the screen.</summary>
        public const string Reason = "transition";

        private const string TouchdownToken = "@srv.land.touchdown";
        private const string StationType = "orbital_station";
        private const string InteriorType = "ship_interior";

        private enum Stage { Idle, Arrival, Touchdown, Liftoff, DepartureCover, Handoff }

        private readonly TransitionGate _gate = new TransitionGate();
        private Stage _stage;
        private float _stageTime;
        private bool _subscribed;
        private TransitionEmergence _emergence;
        private TransitionCoverStyle _arrivalStyle;

        // The world types around the last world reset — an undock's return world can land in the same network batch as
        // the flight, so "did we leave a station?" must look at the world BEFORE that reset.
        private string _worldType = string.Empty;
        private string _worldTypeBefore = string.Empty;
        private float _worldResetAt = -10f;

        // The arrival signals, numbered in the order they came in. Since #3139 the server sends SpaceClosed, the world reset
        // and the touchdown snap in one batch — all of them land before the flight view even starts its descent, so the
        // gate (which only begins when the cover closes) counts every signal numbered after the flight closed.
        private int _signal;
        private int _flightClosedAt = int.MaxValue;
        private int _worldLoadAt = -1;
        private int _placedAt = -1;

        /// <summary>True while an arrival is covered — the loading veil stays down (the cover is the loading screen).</summary>
        public bool OwnsArrival => _gate.Active;

        /// <summary>True while any transition (a cover or a cinematic) is running.</summary>
        public bool Busy => _stage != Stage.Idle || (Cover != null && Cover.Visible);

        private void Update()
        {
            if (Game == null)
            {
                return;
            }

            Subscribe();
            DropUnclaimedHull();
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _stageTime += dt;
            switch (_stage)
            {
                case Stage.Arrival: UpdateArrival(dt); break;
                case Stage.Touchdown: UpdateTouchdown(dt); break;
                case Stage.Liftoff: UpdateLiftoff(dt); break;
                case Stage.DepartureCover: UpdateDepartureCover(); break;
            }
        }

        private void LateUpdate()
        {
            if (_stage == Stage.Touchdown || _stage == Stage.Liftoff)
            {
                WriteCamera();
            }
        }

        private void Subscribe()
        {
            if (_subscribed || Game.Network == null)
            {
                return;
            }

            _subscribed = true;
            Game.WorldLoadStarted += OnWorldLoadStarted;
            Game.Network.RespawnNoticeReceived += OnRespawnNotice;
            Game.Network.SpaceClosed += OnSpaceClosed;
            _worldType = Game.LoadingPlanetType ?? string.Empty;
        }

        private void OnWorldLoadStarted()
        {
            _worldTypeBefore = _worldType;
            _worldType = Game.LoadingPlanetType ?? string.Empty;
            _worldResetAt = Time.time;
            _worldLoadAt = ++_signal;
            _gate.NoteWorldLoad();
        }

        private void OnSpaceClosed(BlocksBeyondTheStars.Networking.Messages.SpaceClosed m) => _flightClosedAt = ++_signal;

        private void OnRespawnNotice(BlocksBeyondTheStars.Networking.Messages.RespawnNotice m)
        {
            if (m != null && m.Reason == TouchdownToken)
            {
                _placedAt = ++_signal;
                _gate.NotePlaced();
            }
        }

        // ---- arrival -------------------------------------------------------------------------------------------------

        /// <summary>The flight view's landing descent has reached the cloud layer (or the retro-burn glare): close the cover
        /// and hold it until the server has set the player down. <paramref name="airless"/> picks the glare.</summary>
        public void CoverLanding(string name, string sub, bool airless, Color sky, Color cloud, float seconds)
        {
            if (Cover == null)
            {
                return;
            }

            _arrivalStyle = airless ? TransitionCoverStyle.Glare : TransitionCoverStyle.Clouds;
            var tint = airless ? new Color(1f, 0.93f, 0.82f) : cloud;
            Cover.Close(_arrivalStyle, tint, sky, 1f, name, sub, seconds);
            BeginArrival(TransitionArrival.Landing);
            if (Game != null && Game.InSpace)
            {
                // #3152: a descent started on the pad pick reaches the clouds before the server closed the flight — every
                // signal still to come arrives live (the handlers note it), and the counters still hold the PREVIOUS
                // close: counting them would open the cover on the old spot.
                return;
            }

            if (_worldLoadAt > _flightClosedAt)
            {
                _gate.NoteWorldLoad(); // came in with the flight's close, before the descent began
            }

            if (_placedAt > _flightClosedAt)
            {
                _gate.NotePlaced();
            }
        }

        /// <summary>The dock approach has reached the hangar mouth: the doors close; the station loads behind them.</summary>
        public void CoverDocking(string stationName, float seconds)
        {
            if (Cover == null)
            {
                return;
            }

            _arrivalStyle = TransitionCoverStyle.Doors;
            Cover.Close(TransitionCoverStyle.Doors, Color.white, Color.white, 0f, stationName, Loc("ui.transition.docking", "Docking"), seconds);
            BeginArrival(TransitionArrival.Docking);
        }

        /// <summary>The transition did not happen (a refused docking): open the cover on what is there.</summary>
        public void CancelArrival() => _gate.Cancel();

        /// <summary>True once the arrival cover is fully shut — the flight view may hand over now.</summary>
        public bool CoverShut => Cover == null || Cover.Shut;

        private void BeginArrival(TransitionArrival kind)
        {
            EndCinematic();
            _gate.Begin(kind);
            HoldScreen(true); // nobody walks around behind the clouds (or on the old world while the new one builds)
            SetStage(Stage.Arrival);
        }

        private void UpdateArrival(float dt)
        {
            _gate.Tick(dt);
            bool settled = Game.WorldReady && !Game.RespawnTarget.HasValue && !Game.PlayerSettling && !Game.SpaceViewActive;
            if (!_gate.CanOpen(settled) || (Cover != null && Cover.Closing && !Cover.Shut))
            {
                return;
            }

            bool landed = _gate.Kind == TransitionArrival.Landing && !_gate.Cancelled && !Game.SpaceViewActive;
            _gate.End();
            if (landed && BeginTouchdown())
            {
                Cover?.Open(1.1f, 1f);
                return;
            }

            Cover?.Open(_arrivalStyle == TransitionCoverStyle.Doors ? 1.0f : 0.8f, 1f);
            HoldScreen(false);
            SetStage(Stage.Idle);
        }

        // ---- departure -----------------------------------------------------------------------------------------------

        /// <summary>
        /// The flight view asks this every frame while the server has the player in flight but the view has not opened yet.
        /// True holds the view back: the lift-off is playing on the surface (or the hangar doors are closing). False lets it
        /// open — at once when there is nothing to play (taking the helm, an EVA resume), or once the cover is shut.
        /// </summary>
        public bool ClaimDeparture()
        {
            if (Game == null || Cover == null)
            {
                return false;
            }

            switch (_stage)
            {
                case Stage.Liftoff:
                case Stage.DepartureCover:
                    return true;
                case Stage.Handoff:
                    SetStage(Stage.Idle);
                    return false;
            }

            if (_stage != Stage.Idle || Game.SpaceSkipLaunch || PreviousWorldType() == InteriorType)
            {
                return false;
            }

            return PreviousWorldType() == StationType ? BeginStationDeparture() : BeginPlanetDeparture();
        }

        /// <summary>The flight view takes the emergence it should play (once).</summary>
        public TransitionEmergence TakeEmergence()
        {
            var e = _emergence;
            _emergence = default;
            return e;
        }

        private string PreviousWorldType()
            => Time.time - _worldResetAt < 1.5f ? _worldTypeBefore : _worldType;

        private bool BeginStationDeparture()
        {
            Cover.Close(TransitionCoverStyle.Doors, Color.white, Color.white, 0f, string.Empty, string.Empty, 0.9f);
            HoldScreen(true);
            _emergence = new TransitionEmergence { Active = true, FromStation = true };
            SetStage(Stage.DepartureCover);
            return true;
        }

        private void UpdateDepartureCover()
        {
            if (!Game.InSpace)
            {
                Cover.Open(0.6f, -1f); // the flight never came (refused) — show the station again
                HoldScreen(false);
                SetStage(Stage.Idle);
                return;
            }

            if (Cover.Shut)
            {
                EndCinematic();
                SetStage(Stage.Handoff);
            }
        }

        /// <summary>Closes the departure cover over the climb (clouds, or the glare off an airless world). The lift-off keeps
        /// its own stage (the hull flies on into the closing clouds); without one the cover stage waits for the shut.</summary>
        private void CloseDepartureCover(float seconds, bool ownStage = true)
        {
            if (Cover.Closing)
            {
                return;
            }

            var env = Game.Environment;
            bool airless = IsAirless(env);
            var sky = env != null ? Rgb(env.SkyColor) : new Color(0.55f, 0.75f, 0.95f);
            var cloud = env != null ? Rgb(env.CloudColor) : Color.white;
            var style = airless ? TransitionCoverStyle.Glare : TransitionCoverStyle.Clouds;
            Cover.Close(style, airless ? new Color(1f, 0.93f, 0.82f) : cloud, sky, -1f, string.Empty, string.Empty, seconds);
            _emergence = new TransitionEmergence { Active = true, Airless = airless, Sky = sky, RoarPlayed = _emergence.RoarPlayed };
            if (ownStage)
            {
                SetStage(Stage.DepartureCover);
            }
        }

        // ---- shared --------------------------------------------------------------------------------------------------

        private void SetStage(Stage stage)
        {
            _stage = stage;
            _stageTime = 0f;
        }

        internal static bool IsAirless(BlocksBeyondTheStars.Networking.Messages.WorldEnvironment env)
            => env != null && (env.SpaceSky || env.AtmosphereDensity < 0.05f);

        internal static Color Rgb(int rgb)
            => new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

        private string Loc(string key, string fallback)
        {
            var loc = Game != null ? Game.Localizer : null;
            return loc != null && loc.Has(key) ? loc.Get(key) : fallback;
        }

        private void OnDisable()
        {
            EndCinematic();
            if (_subscribed && Game != null)
            {
                Game.WorldLoadStarted -= OnWorldLoadStarted;
                if (Game.Network != null)
                {
                    Game.Network.RespawnNoticeReceived -= OnRespawnNotice;
                    Game.Network.SpaceClosed -= OnSpaceClosed;
                }

                _subscribed = false;
            }
        }
    }
}
