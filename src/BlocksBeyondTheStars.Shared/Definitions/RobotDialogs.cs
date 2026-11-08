// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BlocksBeyondTheStars.Shared.Definitions;

/// <summary>
/// The robot conversation (#3405-#3408, Justus: "you can communicate with the robot from the endgame"): E at the own robot
/// opens the NPC dialogue panel with four replies — "How are you?", "Tell me something!", "Ask me something!" and "Follow
/// me!" / "Wait here!". This class owns the locale keys and the deterministic picks, so the server and the tests agree:
/// <list type="bullet">
/// <item>the robot's questions (<c>robotdialog.ask.&lt;n&gt;.*</c>) come in a per-player shuffled order, each until it was
/// answered; the answer is remembered (<see cref="AnswerMilestone"/>) and later recalled (<c>.m&lt;answer&gt;</c>);</item>
/// <item>"Tell me something!" plays the robot's own story lines of <see cref="RobotChatter"/> (not its questions — those are
/// asked properly now), and every third time a remembered answer instead;</item>
/// <item>"How are you?" answers to the moment (<see cref="MoodCandidates"/>).</item>
/// </list>
/// Adding a question is ten locale entries (en + de) and one higher <see cref="QuestionCount"/>.
/// </summary>
public static class RobotDialogs
{
    /// <summary>The hub's replies, in the order the panel shows them.</summary>
    public const int HowAreYou = 0, TellMe = 1, AskMe = 2, FollowOrWait = 3;

    /// <summary>The robot's questions: <c>robotdialog.ask.1</c> .. <c>robotdialog.ask.&lt;QuestionCount&gt;</c>.</summary>
    public const int QuestionCount = 6;

    /// <summary>Answers per question.</summary>
    public const int AnswerCount = 3;

    /// <summary>Greetings that open a conversation: <c>robotdialog.hello.1</c> .. (<c>{0}</c> = the player's name).</summary>
    public const int GreetingCount = 4;

    /// <summary>The happy default lines of "How are you?".</summary>
    public const int HappyCount = 3;

    /// <summary>The robot's own <see cref="RobotChatter"/> lines that are questions to the player — asked as real questions
    /// now, so "Tell me something!" skips them.</summary>
    public static readonly IReadOnlyList<int> ChatterQuestionLines = new[] { 3, 4, 5, 6, 11 };

    /// <summary>The <see cref="RobotChatter"/> lines "Tell me something!" plays (1-based).</summary>
    public static readonly IReadOnlyList<int> TellLines =
        Enumerable.Range(1, RobotChatter.LineCount).Where(n => !ChatterQuestionLines.Contains(n)).ToArray();

    /// <summary>Every "Tell me something!" with this index (1-based count) divisible by it recalls an answer, when one exists.</summary>
    public const int RecallEvery = 3;

    /// <summary>Counter: "Tell me something!" answers given (persisted in <c>PlayerState.AchievementCounters</c>).</summary>
    public const string TellsCounter = "robot_dialog_tells";

    /// <summary>Counter: "How are you?" answers given.</summary>
    public const string MoodsCounter = "robot_dialog_moods";

    /// <summary>Counter: recalls of a remembered answer.</summary>
    public const string RecallsCounter = "robot_dialog_recalls";

    /// <summary>The prefix of the remembered answers: <c>robot:ans:&lt;question&gt;:&lt;answer&gt;</c> (both 1-based).</summary>
    public const string AnswerMilestonePrefix = "robot:ans:";

    public const string HowLabelKey = "robotdialog.ui.how";
    public const string TellLabelKey = "robotdialog.ui.tell";
    public const string AskLabelKey = "robotdialog.ui.ask";
    public const string FollowLabelKey = "robotdialog.ui.follow";
    public const string WaitLabelKey = "robotdialog.ui.wait";
    public const string FollowLineKey = "robotdialog.follow.on";
    public const string WaitLineKey = "robotdialog.follow.off";
    public const string AskDoneKey = "robotdialog.ask.done";

    /// <summary>The moods of "How are you?", most urgent first.</summary>
    public const string MoodFight = "fight", MoodHurt = "hurt", MoodWeapon = "weapon", MoodJetpack = "jetpack", MoodNight = "night",
        MoodStorm = "storm", MoodCold = "cold", MoodHot = "hot", MoodToxic = "toxic", MoodGas = "gas", MoodAirless = "airless",
        MoodPets = "pets", MoodHappy = "happy";

    private static string N(int n) => n.ToString(CultureInfo.InvariantCulture);

    public static string GreetingKey(int n) => "robotdialog.hello." + N(n);

    public static string QuestionKey(int q) => "robotdialog.ask." + N(q) + ".q";

    /// <summary>The player's answer <paramref name="a"/> (1-based) to question <paramref name="q"/> — a reply label.</summary>
    public static string AnswerKey(int q, int a) => "robotdialog.ask." + N(q) + ".a" + N(a);

    /// <summary>The robot's reaction to answer <paramref name="a"/>.</summary>
    public static string ReactionKey(int q, int a) => "robotdialog.ask." + N(q) + ".r" + N(a);

    /// <summary>The robot remembering answer <paramref name="a"/> later.</summary>
    public static string RecallKey(int q, int a) => "robotdialog.ask." + N(q) + ".m" + N(a);

    public static string AnswerMilestone(int q, int a) => AnswerMilestonePrefix + N(q) + ":" + N(a);

    /// <summary>The line key of a mood (the happy mood has several: <paramref name="variant"/> picks one, 0-based).</summary>
    public static string MoodKey(string mood, int variant = 0)
        => mood == MoodHappy ? "robotdialog.mood.happy." + N(1 + (Math.Abs(variant) % HappyCount)) : "robotdialog.mood." + mood;

    /// <summary>Every mood in the order <see cref="MoodCandidates"/> lists them.</summary>
    public static readonly IReadOnlyList<string> Moods = new[]
    {
        MoodFight, MoodHurt, MoodWeapon, MoodJetpack, MoodNight, MoodStorm, MoodCold, MoodHot, MoodToxic, MoodGas, MoodAirless,
        MoodPets, MoodHappy,
    };

    /// <summary>Every key the conversation uses — what the locale tables must hold.</summary>
    public static IEnumerable<string> AllKeys()
    {
        foreach (string k in new[] { HowLabelKey, TellLabelKey, AskLabelKey, FollowLabelKey, WaitLabelKey, FollowLineKey, WaitLineKey, AskDoneKey })
        {
            yield return k;
        }

        for (int n = 1; n <= GreetingCount; n++)
        {
            yield return GreetingKey(n);
        }

        foreach (string mood in Moods.Where(m => m != MoodHappy))
        {
            yield return MoodKey(mood);
        }

        for (int n = 0; n < HappyCount; n++)
        {
            yield return MoodKey(MoodHappy, n);
        }

        for (int q = 1; q <= QuestionCount; q++)
        {
            yield return QuestionKey(q);
            for (int a = 1; a <= AnswerCount; a++)
            {
                yield return AnswerKey(q, a);
                yield return ReactionKey(q, a);
                yield return RecallKey(q, a);
            }
        }
    }

    /// <summary>What the robot feels like saying, given the moment. An urgent mood (a fight just now, the owner badly hurt)
    /// is the only candidate; otherwise every matching mood plus the happy default, rotated by the caller.</summary>
    public static IReadOnlyList<string> MoodCandidates(RobotMoodFacts f)
    {
        if (f.JustFought)
        {
            return new[] { MoodFight };
        }

        if (f.OwnerHurt)
        {
            return new[] { MoodHurt };
        }

        var list = new List<string>();
        foreach (var (on, mood) in new[]
        {
            (f.HasWeapon, MoodWeapon), (f.HasJetpack, MoodJetpack), (f.Night, MoodNight), (f.Storm, MoodStorm),
            (!string.IsNullOrEmpty(f.WorldMood), f.WorldMood ?? string.Empty), (f.PetsNear, MoodPets),
        })
        {
            if (on)
            {
                list.Add(mood);
            }
        }

        list.Add(MoodHappy);
        return list;
    }

    /// <summary>The world mood of a planet type key: cold, hot, toxic, gas, airless (asteroids) — or none.</summary>
    public static string WorldMoodOf(string? planetType, bool asteroid)
    {
        if (asteroid)
        {
            return MoodAirless;
        }

        return planetType switch
        {
            "gas_giant" => MoodGas,
            "ice" or "tundra" or "titas" => MoodCold,
            "volcanic" or "ashen" or "gds_desert" or "arena_nigra" or "desert" or "sand_sea" => MoodHot,
            "toxica_maxima" or "corrupted" => MoodToxic,
            _ => string.Empty,
        };
    }

    /// <summary>The question number (1-based) the robot asks next: the first one in this player's shuffled order that has
    /// no answer yet; 0 when every question was answered.</summary>
    public static int NextQuestion(string playerId, Func<int, bool> answered)
    {
        for (int slot = 0; slot < QuestionCount; slot++)
        {
            int q = RobotChatter.ShuffledEntry(playerId, "ask", slot, QuestionCount) + 1;
            if (!answered(q))
            {
                return q;
            }
        }

        return 0;
    }

    /// <summary>The answer (1-based) stored for question <paramref name="q"/> among <paramref name="milestones"/>, or 0.</summary>
    public static int StoredAnswer(ICollection<string> milestones, int q)
    {
        for (int a = 1; a <= AnswerCount; a++)
        {
            if (milestones.Contains(AnswerMilestone(q, a)))
            {
                return a;
            }
        }

        return 0;
    }

    /// <summary>The answered questions (1-based, ascending) — the pool of recalls.</summary>
    public static IReadOnlyList<int> AnsweredQuestions(ICollection<string> milestones)
        => Enumerable.Range(1, QuestionCount).Where(q => StoredAnswer(milestones, q) > 0).ToArray();
}

/// <summary>What the server knows about the moment when the player asks the robot "How are you?" (#3407).</summary>
public sealed class RobotMoodFacts
{
    /// <summary>The robot fought within the last half minute.</summary>
    public bool JustFought { get; set; }

    /// <summary>The owner's health is low.</summary>
    public bool OwnerHurt { get; set; }

    public bool HasWeapon { get; set; }

    public bool HasJetpack { get; set; }

    public bool Night { get; set; }

    public bool Storm { get; set; }

    /// <summary>The world's mood (<see cref="RobotDialogs.WorldMoodOf"/>), or empty.</summary>
    public string? WorldMood { get; set; }

    /// <summary>Another companion of the owner is close by.</summary>
    public bool PetsNear { get; set; }
}
