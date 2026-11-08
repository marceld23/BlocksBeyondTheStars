// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Weather;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3405: the robot stands still while its owner talks with it, until this uptime (renewed by the 1 Hz pass).</summary>
    public double TalkHoldUntil { get; set; }

    /// <summary>#3408: "Wait here!" — the robot stays where it stands until "Follow me!". RAM only: a new arrival (another
    /// world, a rejoin) has it follow again.</summary>
    public bool WaitHere { get; set; }
}

/// <summary>
/// The robot conversation (#3405-#3408, Justus' report "Ich meinte mit": "…and that you can communicate with it!"). E at the
/// own robot opens the NPC dialogue panel (<see cref="NpcDialogState.RobotNpcId"/>) with a greeting and four replies:
/// <list type="bullet">
/// <item><b>How are you?</b> — an answer to the moment (#3407, <see cref="RobotDialogs.MoodCandidates"/>);</item>
/// <item><b>Tell me something!</b> — one of its own story lines, now and then a remembered answer;</item>
/// <item><b>Ask me something!</b> — it asks one of its questions, the player picks one of three answers, it reacts and
/// remembers the answer (#3406, milestone <c>robot:ans:q:a</c>); once all were answered it recalls one;</item>
/// <item><b>Follow me! / Wait here!</b> — it waits on the spot or follows again (#3408, <see cref="CombatEntity.WaitHere"/>).</item>
/// </list>
/// Each reply's answer is the conversation's closing line; E again starts the next one. The robot holds still while the
/// panel is open; walking away, another world, a fight or a knock-out ends the talk. Only for a client announcing
/// <c>robottalk1</c> — an older one keeps the single line in VEGA's panel (<see cref="SpeakRobotChatter"/>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Seconds between two E presses that open a conversation (a held key never floods the panel).</summary>
    internal const double RobotDialogCooldownSeconds = 0.5;

    /// <summary>The talk stays open this far past the talk reach (the player shuffles a little while reading).</summary>
    private const float RobotTalkKeepMargin = 2f;

    /// <summary>How long one hold keeps the robot still; the 1 Hz pass renews it while the talk lives.</summary>
    private const double RobotTalkHoldSeconds = 2.0;

    /// <summary>A robot that fought this recently answers "How are you?" about the fight.</summary>
    private const double RobotMoodFightSeconds = 30.0;

    /// <summary>A fight this fresh ends an open talk (the robot has work to do).</summary>
    private const double RobotTalkFightEndsSeconds = 2.0;

    /// <summary>The owner is "badly hurt" below this much health.</summary>
    private const float RobotMoodHurtHealth = 35f;

    /// <summary>Another companion this close counts as company.</summary>
    private const float RobotMoodPetsRange = 12f;

    /// <summary>E at the own robot from a client with the dialogue: the greeting and the four replies.</summary>
    private void OpenRobotConversation(PlayerSession session, TamedCreature tc)
    {
        EndNpcConversation(session, tellClient: false); // a person's open talk ends — the panel now shows the robot
        session.Vega.RobotTalk = new RobotConversation(tc.Id, _world.LocationId, question: 0);
        HoldRobotForTalk(session, tc);
        int n = 1 + StableHashing.Index("robot:hello:" + session.State.PlayerId + ":" + session.Vega.RobotGreetings++, RobotDialogs.GreetingCount);
        string greeting = Localize(session.Locale, RobotDialogs.GreetingKey(n)).Replace("{0}", session.State.Name ?? string.Empty);
        SendRobotDialog(session, tc, greeting, HubChoices(session, tc), end: false);
    }

    /// <summary>The hub's four reply labels — the last one names the follow state the robot would switch to.</summary>
    private string[] HubChoices(PlayerSession session, TamedCreature tc)
    {
        bool waiting = LiveRobotOf(session.State.PlayerId, tc)?.WaitHere == true;
        return new[]
        {
            Localize(session.Locale, RobotDialogs.HowLabelKey),
            Localize(session.Locale, RobotDialogs.TellLabelKey),
            Localize(session.Locale, RobotDialogs.AskLabelKey),
            Localize(session.Locale, waiting ? RobotDialogs.FollowLabelKey : RobotDialogs.WaitLabelKey),
        };
    }

    private void SendRobotDialog(PlayerSession session, TamedCreature tc, string text, string[] choices, bool end)
        => Send(session, new NpcDialogState
        {
            NpcId = NpcDialogState.RobotNpcId,
            Name = CompanionDisplayName(tc),
            Text = text,
            Choices = choices,
            End = end,
        });

    /// <summary>A reply picked in the robot's panel (the choice carries <see cref="NpcDialogState.RobotNpcId"/>); a negative
    /// index is the player closing it.</summary>
    private void HandleRobotDialogChoice(PlayerSession session, NpcDialogChoiceIntent intent)
    {
        if (session.Vega.RobotTalk is not { } talk)
        {
            return;
        }

        if (intent.ChoiceIndex < 0)
        {
            EndRobotConversation(session, tellClient: false); // the leave — the panel is already gone
            return;
        }

        if (ConversationRobot(session, talk) is not { } tc)
        {
            EndRobotConversation(session, tellClient: true);
            return;
        }

        if (talk.Question > 0)
        {
            AnswerRobotQuestion(session, tc, talk.Question, intent.ChoiceIndex);
            return;
        }

        switch (intent.ChoiceIndex)
        {
            case RobotDialogs.HowAreYou:
                FinishRobotTalk(session, tc, RobotMoodLine(session, tc));
                break;
            case RobotDialogs.TellMe:
                FinishRobotTalk(session, tc, RobotTellLine(session, tc));
                break;
            case RobotDialogs.AskMe:
                AskRobotQuestion(session, tc, talk);
                break;
            case RobotDialogs.FollowOrWait:
                ToggleRobotWait(session, tc);
                break;
            default:
                EndRobotConversation(session, tellClient: true);
                break;
        }
    }

    /// <summary>The robot's answer closes this conversation: the panel shows it with "Leave", the robot may walk again, and
    /// the progress (counters, remembered answers) is saved.</summary>
    private void FinishRobotTalk(PlayerSession session, TamedCreature tc, string text)
    {
        EndRobotConversation(session, tellClient: false);
        SendRobotDialog(session, tc, text, Array.Empty<string>(), end: true);
        _repo.SavePlayer(session.State);
    }

    /// <summary>"Ask me something!": the next unanswered question with its three answers — or, once all were answered, the
    /// robot remembers one of them.</summary>
    private void AskRobotQuestion(PlayerSession session, TamedCreature tc, RobotConversation talk)
    {
        var milestones = session.State.Milestones;
        int q = RobotDialogs.NextQuestion(session.State.PlayerId, n => RobotDialogs.StoredAnswer(milestones, n) > 0);
        if (q == 0)
        {
            string done = Localize(session.Locale, RobotDialogs.AskDoneKey);
            FinishRobotTalk(session, tc, (done + " " + RobotRecallLine(session, tc)).Trim());
            return;
        }

        talk.Question = q;
        var answers = Enumerable.Range(1, RobotDialogs.AnswerCount)
            .Select(a => Localize(session.Locale, RobotDialogs.AnswerKey(q, a)))
            .ToArray();
        SendRobotDialog(session, tc, Localize(session.Locale, RobotDialogs.QuestionKey(q)), answers, end: false);
    }

    /// <summary>The player answered question <paramref name="q"/>: remembered, and the robot reacts to exactly that answer.</summary>
    private void AnswerRobotQuestion(PlayerSession session, TamedCreature tc, int q, int index)
    {
        if (index >= RobotDialogs.AnswerCount)
        {
            EndRobotConversation(session, tellClient: true);
            return;
        }

        int a = index + 1;
        if (RobotDialogs.StoredAnswer(session.State.Milestones, q) == 0)
        {
            session.State.Milestones.Add(RobotDialogs.AnswerMilestone(q, a));
        }

        FinishRobotTalk(session, tc, Localize(session.Locale, RobotDialogs.ReactionKey(q, a)));
    }

    /// <summary>"Tell me something!": its next own story line (no repeats until all were told) — every third time a
    /// remembered answer instead, when there is one.</summary>
    private string RobotTellLine(PlayerSession session, TamedCreature tc)
    {
        var counters = session.State.AchievementCounters;
        counters.TryGetValue(RobotDialogs.TellsCounter, out int told);
        counters[RobotDialogs.TellsCounter] = told + 1;
        if ((told + 1) % RobotDialogs.RecallEvery == 0 && RobotDialogs.AnsweredQuestions(session.State.Milestones).Count > 0)
        {
            return RobotRecallLine(session, tc);
        }

        int slot = RobotChatter.ShuffledEntry(session.State.PlayerId, "tell", told, RobotDialogs.TellLines.Count);
        return Localize(session.Locale, RobotChatter.LineKey(RobotDialogs.TellLines[slot])).Replace("{0}", CompanionDisplayName(tc));
    }

    /// <summary>One remembered answer, the answered questions in turn; empty when none was answered yet.</summary>
    private string RobotRecallLine(PlayerSession session, TamedCreature tc)
    {
        var answered = RobotDialogs.AnsweredQuestions(session.State.Milestones);
        if (answered.Count == 0)
        {
            return string.Empty;
        }

        var counters = session.State.AchievementCounters;
        counters.TryGetValue(RobotDialogs.RecallsCounter, out int recalled);
        counters[RobotDialogs.RecallsCounter] = recalled + 1;
        int q = answered[recalled % answered.Count];
        int a = RobotDialogs.StoredAnswer(session.State.Milestones, q);
        return Localize(session.Locale, RobotDialogs.RecallKey(q, a)).Replace("{0}", CompanionDisplayName(tc));
    }

    /// <summary>"How are you?": the most fitting mood of the moment, rotating among the ones that fit.</summary>
    private string RobotMoodLine(PlayerSession session, TamedCreature tc)
    {
        var candidates = RobotDialogs.MoodCandidates(RobotMoodFactsFor(session, tc));
        var counters = session.State.AchievementCounters;
        counters.TryGetValue(RobotDialogs.MoodsCounter, out int asked);
        counters[RobotDialogs.MoodsCounter] = asked + 1;
        string mood = candidates[asked % candidates.Count];
        string line = Localize(session.Locale, RobotDialogs.MoodKey(mood, asked));
        return mood == RobotDialogs.MoodWeapon ? line.Replace("{0}", RobotWeaponName(session, tc)) : line;
    }

    /// <summary>What the server knows about the moment (#3407).</summary>
    private RobotMoodFacts RobotMoodFactsFor(PlayerSession session, TamedCreature tc)
    {
        var p = session.State;
        var robot = LiveRobotOf(p.PlayerId, tc);
        string planet = _world.Planet?.Key ?? string.Empty;
        return new RobotMoodFacts
        {
            JustFought = robot is not null && _uptime - robot.RobotLastFightAt < RobotMoodFightSeconds,
            OwnerHurt = p.Health < RobotMoodHurtHealth,
            HasWeapon = !string.IsNullOrEmpty(tc.GearWeapon),
            HasJetpack = !string.IsNullOrEmpty(tc.GearJetpack),
            Night = IsNightAt(p.Position),
            Storm = WeatherCatalog.Find(BiomeWeatherAt(p.Position).State)?.Family == WeatherFamily.Violent,
            WorldMood = RobotDialogs.WorldMoodOf(planet, WorldConstants.IsAsteroidType(planet)),
            PetsNear = _creatures.Any(c => c.IsCompanion && c.OwnerId == p.PlayerId && !IsRobotEntity(c)
                                           && WrapDistSq(c.Position, p.Position) <= RobotMoodPetsRange * RobotMoodPetsRange),
        };
    }

    /// <summary>The localized name of the weapon the robot carries (its item key without an upgrade suffix).</summary>
    private string RobotWeaponName(PlayerSession session, TamedCreature tc)
    {
        string key = (tc.GearWeapon ?? string.Empty).Split('#')[0];
        return LocalizedName(session.Locale, _content.GetItem(key)?.NameKey, key);
    }

    /// <summary>"Wait here!" / "Follow me!": the live robot stays on its spot or follows again, and says so.</summary>
    private void ToggleRobotWait(PlayerSession session, TamedCreature tc)
    {
        if (LiveRobotOf(session.State.PlayerId, tc) is not { } robot)
        {
            EndRobotConversation(session, tellClient: true);
            return;
        }

        robot.WaitHere = !robot.WaitHere;
        robot.Loco = default; // a fresh start from where it stands
        FinishRobotTalk(session, tc, Localize(session.Locale, robot.WaitHere ? RobotDialogs.WaitLineKey : RobotDialogs.FollowLineKey));
    }

    /// <summary>The conversation's robot while the talk is still valid: same world, the owner alive and on foot, the robot
    /// switched on, close by and not in a fresh fight. Null ends the talk.</summary>
    private TamedCreature? ConversationRobot(PlayerSession session, RobotConversation talk)
    {
        var p = session.State;
        if (!session.Joined || session.CurrentLocationId != talk.LocationId || _world.LocationId != talk.LocationId
            || InSpace(p.PlayerId) || p.Health <= 0f)
        {
            return null;
        }

        var tc = p.TamedCreatures.FirstOrDefault(t => t.Id == talk.CompanionId && t.Kind == TamedCreature.RobotKind && !t.KnockedOut);
        if (tc is null || LiveRobotOf(p.PlayerId, tc) is not { } robot || _uptime - robot.RobotLastFightAt < RobotTalkFightEndsSeconds)
        {
            return null;
        }

        float keep = InteractionReach.CompanionTalk + RobotTalkReachSlack + RobotTalkKeepMargin;
        return WrapDistSq(robot.Position, p.Position) <= keep * keep ? tc : null;
    }

    /// <summary>Holds the robot still for a moment (renewed while the talk lives).</summary>
    private void HoldRobotForTalk(PlayerSession session, TamedCreature tc)
    {
        if (LiveRobotOf(session.State.PlayerId, tc) is { } robot)
        {
            robot.TalkHoldUntil = _uptime + RobotTalkHoldSeconds;
        }
    }

    /// <summary>Ends the robot conversation, if any, and lets the robot walk again. <paramref name="tellClient"/>: the panel
    /// still shows replies — close it (only for a client that takes the close, <c>dialogclose1</c>).</summary>
    private void EndRobotConversation(PlayerSession session, bool tellClient)
    {
        if (session.Vega.RobotTalk is not { } talk)
        {
            return;
        }

        session.Vega.RobotTalk = null;
        var tc = session.State.TamedCreatures.FirstOrDefault(t => t.Id == talk.CompanionId);
        if (tc is not null && LiveRobotOf(session.State.PlayerId, tc) is { } robot)
        {
            robot.TalkHoldUntil = 0;
        }

        if (tellClient && session.SupportsDialogClose)
        {
            Send(session, new NpcDialogState { NpcId = NpcDialogState.RobotNpcId, End = true, Action = NpcDialogState.ActionClose });
        }
    }

    /// <summary>The 1 Hz pass of one player: a dead talk ends (and closes the panel), a live one keeps the robot still.</summary>
    private void TickRobotConversation(PlayerSession session)
    {
        if (session.Vega.RobotTalk is not { } talk)
        {
            return;
        }

        if (ConversationRobot(session, talk) is { } tc)
        {
            HoldRobotForTalk(session, tc);
        }
        else
        {
            EndRobotConversation(session, tellClient: session.Joined);
        }
    }

    // ---------------- Test seams ----------------

    /// <summary>Test seam: the 1 Hz robot-conversation pass of a player right now.</summary>
    public void TickRobotConversationForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            TickRobotConversation(session);
        }
    }

    /// <summary>Test seam: the server clock (a test dates the robot's last fight against it).</summary>
    public double UptimeForTest => _uptime;

    /// <summary>Test seam: whether the player's robot conversation is open.</summary>
    public bool RobotConversationOpenForTest(string playerId) => FindSessionByPlayerId(playerId)?.Vega.RobotTalk is not null;
}
