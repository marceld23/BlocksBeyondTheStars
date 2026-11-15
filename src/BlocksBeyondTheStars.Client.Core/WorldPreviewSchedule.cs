// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Why this moment would make a bad "last played" picture (<see cref="WorldPreviewRules.Blocker"/>) — or
    /// <see cref="None"/> for a good one. The first four are "hard": the view itself is wrong or still building, and it needs
    /// <see cref="WorldPreviewSchedule.SettleSeconds"/> once they are gone. A menu and the chat are "soft": the world behind
    /// them is fine, but an open screen-level menu blurs it (the menu blur of the post stack), so the picture waits until it
    /// closed and the blur faded.</summary>
    public enum WorldPreviewBlocker
    {
        None = 0,

        /// <summary>No view camera, or the window does not show it (the player is elsewhere).</summary>
        NoCamera,

        /// <summary>The loading veil, a world still streaming in, the player held until the floor arrived, a landing or
        /// docking transition, or a hyperjump moments ago.</summary>
        Loading,

        /// <summary>A cutscene, a cinematic camera or a scene that hides the HUD owns the screen.</summary>
        Cutscene,

        /// <summary>The death / ship-destroyed prompt is up.</summary>
        Death,

        /// <summary>An in-game panel, the console, the star chart or the pause menu is open.</summary>
        Menu,

        /// <summary>The player is typing in the chat.</summary>
        Chat,
    }

    /// <summary>What the world shows right now, as far as the picture cares — filled by <c>WorldPreviewCapture</c> every
    /// frame and judged by <see cref="WorldPreviewRules.Blocker"/>.</summary>
    public struct WorldPreviewMoment
    {
        /// <summary>The view camera is there and enabled, and the window shows it.</summary>
        public bool HasCamera { get; set; }

        /// <summary>The loading veil is up, the world is not ready, the player is still settling, or a transition runs.</summary>
        public bool Loading { get; set; }

        /// <summary>Seconds since the last hyperjump began; null when there was none.</summary>
        public double? SecondsSinceJump { get; set; }

        /// <summary>A cutscene, a cinematic camera or a HUD-hiding scene owns the screen.</summary>
        public bool Cutscene { get; set; }

        /// <summary>The death / ship-destroyed prompt is up.</summary>
        public bool Dead { get; set; }

        /// <summary>Any in-game panel (the console, a dialogue, the pause menu, the star chart …) is open.</summary>
        public bool MenuOpen { get; set; }

        /// <summary>The player is typing in the chat.</summary>
        public bool ChatOpen { get; set; }
    }

    /// <summary>"Is this moment good for a picture?" — the rules behind the "last played" picture of a local world.</summary>
    public static class WorldPreviewRules
    {
        /// <summary>How long after a hyperjump began no picture is taken: the warp effect and the new system building up.</summary>
        public const double JumpQuietSeconds = 10;

        /// <summary>The first reason this moment makes a bad picture, hard reasons first; <see cref="WorldPreviewBlocker.None"/>
        /// when it is a good one.</summary>
        public static WorldPreviewBlocker Blocker(WorldPreviewMoment moment)
        {
            if (!moment.HasCamera)
            {
                return WorldPreviewBlocker.NoCamera;
            }

            if (moment.Loading || (moment.SecondsSinceJump.HasValue && moment.SecondsSinceJump.Value < JumpQuietSeconds))
            {
                return WorldPreviewBlocker.Loading;
            }

            if (moment.Cutscene)
            {
                return WorldPreviewBlocker.Cutscene;
            }

            if (moment.Dead)
            {
                return WorldPreviewBlocker.Death;
            }

            if (moment.MenuOpen)
            {
                return WorldPreviewBlocker.Menu;
            }

            return moment.ChatOpen ? WorldPreviewBlocker.Chat : WorldPreviewBlocker.None;
        }

        /// <summary>True for the reasons that leave the world behind them intact (a menu, the chat) — they hold the picture
        /// back without restarting the settle time.</summary>
        public static bool IsSoft(WorldPreviewBlocker blocker)
            => blocker == WorldPreviewBlocker.Menu || blocker == WorldPreviewBlocker.Chat;
    }

    /// <summary>
    /// When the "last played" picture of a local world is taken. The clock is the player's clear view: seconds without a
    /// blocker. The first picture comes after <see cref="FirstSeconds"/> of it, then one every <see cref="IntervalSeconds"/>;
    /// a picture also comes when the player leaves (the pause menu opening, the app quitting) unless one was taken a moment
    /// ago. Never while the view is still settling: <see cref="SettleSeconds"/> after a hard blocker (loading, a cutscene,
    /// death) and <see cref="CalmSeconds"/> after a menu or the chat closed (the menu blur fades). A failed picture is tried
    /// again <see cref="RetrySeconds"/> later.
    /// </summary>
    public sealed class WorldPreviewSchedule
    {
        /// <summary>Seconds of clear view before the first picture of a session.</summary>
        public const double FirstSeconds = 30;

        /// <summary>Seconds of clear view between two pictures.</summary>
        public const double IntervalSeconds = 180;

        /// <summary>Seconds since the last hard blocker before a picture: the world streams in around the player.</summary>
        public const double SettleSeconds = 5;

        /// <summary>Seconds of clear view since the last blocker of any kind (the menu blur fades within a quarter second).</summary>
        public const double CalmSeconds = 1;

        /// <summary>Seconds of clear view since the last picture before a leave picture is worth taking.</summary>
        public const double LeaveGapSeconds = 10;

        /// <summary>Seconds of clear view after a failed picture before the next try.</summary>
        public const double RetrySeconds = 30;

        private double _clear;     // clear-view seconds since the last picture (or since the world opened)
        private double _sinceHard; // seconds since the last hard blocker (menus and chat count — the world streams behind them)
        private double _sinceAny;  // clear-view seconds since the last blocker of any kind

        /// <summary>True once a picture was taken this session.</summary>
        public bool Taken { get; private set; }

        /// <summary>Seconds of clear view the next timed picture waits for (counted from the last picture).</summary>
        public double DueAfter => Taken ? IntervalSeconds : FirstSeconds;

        /// <summary>True when the view has settled: no hard blocker for <see cref="SettleSeconds"/>, no blocker at all for
        /// <see cref="CalmSeconds"/>.</summary>
        public bool Settled => _sinceHard >= SettleSeconds && _sinceAny >= CalmSeconds;

        /// <summary>One frame of <paramref name="seconds"/> with this moment's <paramref name="blocker"/>; true when the timed
        /// picture is due now.</summary>
        public bool Tick(double seconds, WorldPreviewBlocker blocker)
        {
            double dt = seconds > 0 && !double.IsInfinity(seconds) ? seconds : 0; // NaN, infinite and negative steps count as nothing
            if (blocker != WorldPreviewBlocker.None && !WorldPreviewRules.IsSoft(blocker))
            {
                _sinceHard = 0;
                _sinceAny = 0;
                return false;
            }

            _sinceHard += dt;
            if (blocker != WorldPreviewBlocker.None)
            {
                _sinceAny = 0;
                return false;
            }

            _sinceAny += dt;
            _clear += dt;
            return Settled && _clear >= DueAfter;
        }

        /// <summary>The player is leaving the view — the pause menu is about to open, or the app quits: true when a picture
        /// now is wanted. Judged by the frames up to now (the menu that is opening is not one of them): the view has settled
        /// and the last picture is at least <see cref="LeaveGapSeconds"/> of clear view old.</summary>
        public bool WantsLeavePicture() => Settled && (!Taken || _clear >= LeaveGapSeconds);

        /// <summary>A picture was taken: the clock starts again.</summary>
        public void MarkTaken()
        {
            Taken = true;
            _clear = 0;
        }

        /// <summary>A picture failed: the next try comes <see cref="RetrySeconds"/> of clear view later.</summary>
        public void MarkFailed() => _clear = Math.Max(0, DueAfter - RetrySeconds);
    }
}
