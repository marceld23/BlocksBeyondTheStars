// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>What the gate says about starting a cutscene now.</summary>
    public enum SafeMomentVerdict
    {
        /// <summary>Start it.</summary>
        Play,

        /// <summary>Not now — ask again next frame.</summary>
        Defer,

        /// <summary>Not at all: a "now or never" moment that cannot play now, or a scene that waited too long. The director
        /// gives its text back to the VEGA panel and confirms the scene (or tries it as a card first).</summary>
        Abort,
    }

    /// <summary>The gate's answer with the reason (for the log and the tests).</summary>
    public readonly struct SafeMomentDecision
    {
        public SafeMomentDecision(SafeMomentVerdict verdict, string reason)
        {
            Verdict = verdict;
            Reason = reason ?? string.Empty;
        }

        public SafeMomentVerdict Verdict { get; }

        /// <summary>One of the <see cref="SafeMomentGate"/> reason constants; empty for <see cref="SafeMomentVerdict.Play"/>.</summary>
        public string Reason { get; }

        public static SafeMomentDecision Play => new SafeMomentDecision(SafeMomentVerdict.Play, string.Empty);

        public override string ToString() => Reason.Length == 0 ? Verdict.ToString() : Verdict + " (" + Reason + ")";
    }

    /// <summary>The player's situation as the director reads it each frame (plain values, so the rules are testable).</summary>
    public sealed class SafeMomentInputs
    {
        /// <summary>Other players are in the world (the server's joined count is above one).</summary>
        public bool Multiplayer;

        /// <summary>A menu or modal panel is open.</summary>
        public bool MenuOpen;

        /// <summary>The chat input has focus.</summary>
        public bool ChatTyping;

        /// <summary>The loading veil is up.</summary>
        public bool LoadingVeil;

        /// <summary>The player is still settling after a spawn or a teleport.</summary>
        public bool Settling;

        /// <summary>The death / respawn prompt is up.</summary>
        public bool RespawnPrompt;

        /// <summary>The player watches as an observer.</summary>
        public bool Spectating;

        /// <summary>Another cinematic owns the screen (the ending, the prologue, the shell intro).</summary>
        public bool AnotherScene;

        /// <summary>The argument duel with the Guardian core is open.</summary>
        public bool DuelOpen;

        /// <summary>Falling fast.</summary>
        public bool Falling;

        /// <summary>Climbing a wall.</summary>
        public bool Climbing;

        /// <summary>The head is under water.</summary>
        public bool HeadUnderwater;

        /// <summary>Driving a speeder or riding a train.</summary>
        public bool InVehicle;

        /// <summary>Floating outside the ship in space.</summary>
        public bool InEva;

        /// <summary>Flying the ship by hand (not an automatic transit).</summary>
        public bool PilotingInSpace;

        /// <summary>A hyperspace warp or a wormhole transit plays.</summary>
        public bool Warping;

        /// <summary>Aboard the ship or a station, or on foot next to the parked ship — where a staged scene looks best.</summary>
        public bool AtHome;

        /// <summary>Distance to the nearest hostile creature or machine (metres; very large when none).</summary>
        public float HostileDistance = float.MaxValue;

        /// <summary>Seconds since the player last lost health.</summary>
        public float SecondsSinceDamage = float.MaxValue;

        /// <summary>Seconds since the ship last lost hull or shield in space.</summary>
        public float SecondsSinceSpaceCombat = float.MaxValue;

        /// <summary>Health 0..1.</summary>
        public float Health = 1f;

        /// <summary>Oxygen 0..1.</summary>
        public float Oxygen = 1f;
    }

    /// <summary>The scene the gate is asked about.</summary>
    public sealed class SafeMomentRequest
    {
        /// <summary>The tier (<c>CutsceneTiers</c>).</summary>
        public int Tier = 2;

        /// <summary>Whether the scene wants the camera.</summary>
        public bool TakesCamera;

        /// <summary>A "now or never" moment (<c>cooldownExempt</c>): it never waits — not now means not at all.</summary>
        public bool NowOrNever;

        /// <summary>A replay or a preview the player asked for: no "prefer at home", no giving up.</summary>
        public bool Requested;

        /// <summary>Seconds the scene has been waiting for its moment.</summary>
        public float WaitedSeconds;
    }

    /// <summary>
    /// When a cutscene may start (#2468, synthesis §6.3 "Aufschub", §5.3 "MP-Schutz v1"). Client-side and per player: the
    /// server only sends; each client waits for its own safe moment.
    /// <list type="bullet">
    /// <item>Nothing starts behind the loading veil, over another cinematic or for an observer. A banner (T1) waits only
    /// for those, a menu and the respawn prompt — it never takes anything away from the player.</item>
    /// <item>A card or scene (T2+) also waits for: a menu, the chat input, the respawn prompt, the duel, the spawn settle;
    /// danger — a hostile within <see cref="HostileRadius"/> m, damage in the last <see cref="DamageWindow"/> s, space
    /// combat in the last <see cref="SpaceCombatWindow"/> s, health or oxygen below <see cref="LowVital"/>.</item>
    /// <item>A scene that takes the camera also waits while the player falls, climbs, has the head under water, drives,
    /// floats in EVA, pilots the ship or warps; and it prefers the player at home (aboard, docked, next to the ship) for
    /// <see cref="PreferAtHomeSeconds"/> s.</item>
    /// <item>A "now or never" scene aborts instead of waiting; any other scene gives up after <see cref="CardGiveUpSeconds"/>
    /// (cards) or <see cref="SceneGiveUpSeconds"/> (scenes). A requested replay never gives up.</item>
    /// </list>
    /// While a scene plays: <see cref="ShouldInterrupt"/> (danger ends it — the text goes back to the panel) and
    /// <see cref="CameraCapReached"/> (in multiplayer the camera returns after <see cref="CameraCapSeconds"/> s and the
    /// captions go on as a card).
    /// </summary>
    public static class SafeMomentGate
    {
        /// <summary>A hostile closer than this (metres) is danger.</summary>
        public const float HostileRadius = 20f;

        /// <summary>Damage within this many seconds is danger.</summary>
        public const float DamageWindow = 10f;

        /// <summary>Space combat within this many seconds is danger (the music director's rule).</summary>
        public const float SpaceCombatWindow = 14f;

        /// <summary>Health or oxygen below this share is danger.</summary>
        public const float LowVital = 0.3f;

        /// <summary>How long a camera-taking scene waits for the player to be at home before it plays anywhere safe.</summary>
        public const float PreferAtHomeSeconds = 300f;

        /// <summary>A card gives up after this long without a safe moment.</summary>
        public const float CardGiveUpSeconds = 600f;

        /// <summary>A staged scene gives up after this long (then tries to play as a card).</summary>
        public const float SceneGiveUpSeconds = 1200f;

        /// <summary>In multiplayer a scene keeps the camera at most this long.</summary>
        public const float CameraCapSeconds = 30f;

        public const string ReasonLoading = "loading";
        public const string ReasonScene = "another_scene";
        public const string ReasonObserver = "observer";
        public const string ReasonMenu = "menu";
        public const string ReasonChat = "chat";
        public const string ReasonRespawn = "respawn";
        public const string ReasonDuel = "duel";
        public const string ReasonSettling = "settling";
        public const string ReasonHostile = "hostile_near";
        public const string ReasonDamage = "recent_damage";
        public const string ReasonSpaceCombat = "space_combat";
        public const string ReasonVitals = "low_vitals";
        public const string ReasonMoving = "moving";
        public const string ReasonPreferHome = "prefer_home";
        public const string ReasonGaveUp = "gave_up";

        /// <summary>Whether <paramref name="request"/> may start now.</summary>
        public static SafeMomentDecision Evaluate(SafeMomentRequest request, SafeMomentInputs s)
        {
            if (request is null || s is null)
            {
                return new SafeMomentDecision(SafeMomentVerdict.Defer, ReasonLoading);
            }

            string? why = Blocker(request, s);
            if (why is null)
            {
                return SafeMomentDecision.Play;
            }

            if (request.Tier <= 1)
            {
                return new SafeMomentDecision(SafeMomentVerdict.Defer, why); // a banner simply waits
            }

            if (request.NowOrNever && !request.Requested)
            {
                return new SafeMomentDecision(SafeMomentVerdict.Abort, why);
            }

            float giveUp = request.TakesCamera && request.Tier >= 3 ? SceneGiveUpSeconds : CardGiveUpSeconds;
            if (!request.Requested && request.WaitedSeconds >= giveUp)
            {
                return new SafeMomentDecision(SafeMomentVerdict.Abort, ReasonGaveUp);
            }

            return new SafeMomentDecision(SafeMomentVerdict.Defer, why);
        }

        /// <summary>Danger while a card or scene plays: damage taken since it started, space combat since it started, or —
        /// unless the world is held — a hostile closing in.</summary>
        public static bool ShouldInterrupt(SafeMomentInputs s, float secondsPlaying, bool worldHeld)
        {
            if (s is null)
            {
                return false;
            }

            if (s.SecondsSinceDamage < secondsPlaying || s.SecondsSinceSpaceCombat < secondsPlaying)
            {
                return true;
            }

            return !worldHeld && s.HostileDistance <= HostileRadius;
        }

        /// <summary>Whether a multiplayer scene has had the camera long enough.</summary>
        public static bool CameraCapReached(bool multiplayer, float cameraSeconds) => multiplayer && cameraSeconds >= CameraCapSeconds;

        private static string? Blocker(SafeMomentRequest r, SafeMomentInputs s)
        {
            if (s.LoadingVeil)
            {
                return ReasonLoading;
            }

            if (s.AnotherScene)
            {
                return ReasonScene;
            }

            if (s.Spectating)
            {
                return ReasonObserver;
            }

            if (s.MenuOpen)
            {
                return ReasonMenu;
            }

            if (s.RespawnPrompt)
            {
                return ReasonRespawn;
            }

            if (r.Tier <= 1)
            {
                return null; // a banner never takes the controls or the view
            }

            if (s.ChatTyping)
            {
                return ReasonChat;
            }

            if (s.DuelOpen)
            {
                return ReasonDuel;
            }

            if (s.Settling)
            {
                return ReasonSettling;
            }

            if (s.HostileDistance <= HostileRadius)
            {
                return ReasonHostile;
            }

            if (s.SecondsSinceDamage < DamageWindow)
            {
                return ReasonDamage;
            }

            if (s.SecondsSinceSpaceCombat < SpaceCombatWindow)
            {
                return ReasonSpaceCombat;
            }

            if (s.Health < LowVital || s.Oxygen < LowVital)
            {
                return ReasonVitals;
            }

            if (!r.TakesCamera)
            {
                return null;
            }

            if (s.Falling || s.Climbing || s.HeadUnderwater || s.InVehicle || s.InEva || s.PilotingInSpace || s.Warping)
            {
                return ReasonMoving;
            }

            if (!s.AtHome && !r.Requested && !r.NowOrNever && r.WaitedSeconds < PreferAtHomeSeconds)
            {
                return ReasonPreferHome;
            }

            return null;
        }
    }
}
