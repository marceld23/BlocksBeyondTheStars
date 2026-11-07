// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>Where a companion is in the Companions tab (#3344).</summary>
    public enum CompanionFollowState
    {
        /// <summary>It follows its owner.</summary>
        Follows,

        /// <summary>A planet pet left at home: it potters about its home spot on its world.</summary>
        StaysHome,

        /// <summary>A travelling companion (robot, Rudolpho, the Soundi) that waits on board — it appears nowhere.</summary>
        OnBoard,
    }

    /// <summary>
    /// The Companions tab's per-row decisions (#3344), Unity-free so they are tested headless: the state line ("Follows me /
    /// Stays home / On board"), the follow toggle's label, whether "set home here" is offered, which rows the "On this
    /// world" page lists, and what the release button says. The server sends <see cref="NetCompanion.Roams"/> and
    /// <see cref="NetCompanion.Kind"/>; an older server sends neither, so the robot and Rudolpho are still recognised by
    /// their body plan.
    /// </summary>
    public static class CompanionSelection
    {
        /// <summary>The companion kinds of <c>TamedCreature.Kind</c> the tab words differently.</summary>
        public const string RobotKind = "robot";

        /// <summary>Rudolpho's kind.</summary>
        public const string ReindeerKind = "reindeer";

        /// <summary>The hunter robot (by its kind, or its body plan from an older server).</summary>
        public static bool IsRobot(NetCompanion c)
            => c.Kind == RobotKind || (string.IsNullOrEmpty(c.Kind) && c.BodyPlan == "Robot");

        /// <summary>Rudolpho (by his kind, or his body plan from an older server).</summary>
        public static bool IsReindeer(NetCompanion c)
            => c.Kind == ReindeerKind || (string.IsNullOrEmpty(c.Kind) && c.BodyPlan == "Reindeer");

        /// <summary>A companion a story granted (the Soundi): never released — it waits on board instead.</summary>
        public static bool IsStory(NetCompanion c) => !string.IsNullOrEmpty(c.Kind) && !IsRobot(c) && !IsReindeer(c);

        /// <summary>A travelling companion: it follows to every world, and waits on board when it does not follow.</summary>
        public static bool Travels(NetCompanion c) => c.Roams || IsRobot(c) || IsReindeer(c);

        /// <summary>Where the companion is.</summary>
        public static CompanionFollowState StateOf(NetCompanion c)
            => c.Following ? CompanionFollowState.Follows
                : Travels(c) ? CompanionFollowState.OnBoard
                : CompanionFollowState.StaysHome;

        /// <summary>The locale key of the row's state line.</summary>
        public static string StateKey(NetCompanion c) => StateOf(c) switch
        {
            CompanionFollowState.StaysHome => "ui.companions.stays_home",
            CompanionFollowState.OnBoard => "ui.companions.on_board",
            _ => "ui.companions.follows",
        };

        /// <summary>The locale key of the follow toggle: take it along, or leave it at home / let it wait on board.</summary>
        public static string ToggleKey(NetCompanion c)
            => !c.Following ? "ui.companions.take_along"
                : Travels(c) ? "ui.companions.wait_on_board"
                : "ui.companions.leave_home";

        /// <summary>"Set home here" is offered for a planet pet that stays home while it is here.</summary>
        public static bool CanSetHome(NetCompanion c) => !Travels(c) && !c.Following && c.Present;

        /// <summary>Whether the "On this world" page lists the companion: it is here — or it waits on board, which is with
        /// the player wherever they are.</summary>
        public static bool ShownHere(NetCompanion c) => c.Present || StateOf(c) == CompanionFollowState.OnBoard;

        /// <summary>The release button's locale key — the robot switches off, Rudolpho goes home, a story companion has none
        /// (null: it is never released).</summary>
        public static string? ReleaseKey(NetCompanion c)
            => IsRobot(c) ? "ui.companions.power_down"
                : IsReindeer(c) ? "ui.companions.send_home"
                : IsStory(c) ? null
                : "ui.companions.release";
    }
}
