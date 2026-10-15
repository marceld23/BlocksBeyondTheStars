// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Missions;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC dialogues (#1127) — the long-reserved "item 15" Dialog backend — and the story pack's authored
/// recurring characters (#1128). Press E near an NPC (outside any station block's reach) to talk: the
/// server picks the best matching dialogue (an authored character's own first, then role dialogues),
/// walks the node graph fully server-authoritatively, records the dormant <see cref="NpcInteractionKind.Dialog"/>
/// interaction, persists the choice as a milestone and applies the choice's consequence (standing bump,
/// story fragment, small gift, or a later radio call). All text goes out RESOLVED per player — dialogue
/// content is stage-gated and once-flagged, so it must never touch the shared greeting cache. Works fully
/// with <c>AiLevel.Off</c>; a future LLM hook may rephrase prose but never decides a branch.
/// </summary>
public sealed partial class GameServer
{
    private const float NpcTalkRange = 5f;

    private const string DialogDoneMilestonePrefix = "dialog:";      // "dialog:<key>:done" — once-per-player gate
    private const string DialogFlagMilestonePrefix = "dialogflag:";  // "dialogflag:<key>:<node>:<choice>" — the persisted decision

    /// <summary>A repeatable dialogue pays its relationship reward at most this often per NPC (#1162).</summary>
    private const double DialogRewardCooldownSeconds = 3600.0;

    /// <summary>#2616: how far past <see cref="NpcTalkRange"/> a conversation survives (the player shuffling a step back).</summary>
    private const float NpcTalkKeepMargin = 2f;

    /// <summary>#2616: the longest one talk holds an NPC's day — then its routine may walk it on (and walking out of
    /// reach ends the talk).</summary>
    private const double NpcTalkHoldSeconds = 60.0;

    /// <summary>#2616: how long one refresh keeps the talking NPC still and facing the player (the 1 Hz sweep renews it,
    /// so a talk that ends without a word — a lost connection — lets go on its own).</summary>
    private const double NpcTalkAttendSeconds = 2.0;

    /// <summary>#2616: per-world 1 Hz throttle of <see cref="TickNpcConversations"/>.</summary>
    private readonly Dictionary<string, double> _conversationTickAt = new(StringComparer.Ordinal);

    /// <summary>
    /// #2616: one player's conversation with an NPC, kept on <see cref="PlayerSession.Conversation"/>. It exists only while
    /// the client shows reply choices: a leave, an end node, a new talk, walking away, a world change, the NPC leaving the
    /// world or a disconnect end it. The NPC is held by REFERENCE — NPC ids restart at 1 on every world (and on every
    /// respawn), so an id alone could name a different person after travel or a reload. Runtime-only; the once-flag is
    /// written when the dialogue ENDS.
    /// </summary>
    internal sealed class NpcConversation
    {
        public NpcConversation(ServerNpc npc, string locationId, DialogDefinition dialog, double startedAt)
        {
            Npc = npc;
            LocationId = locationId;
            Dialog = dialog;
            StartedAt = startedAt;
            LastActiveAt = startedAt;
        }

        public ServerNpc Npc { get; }

        /// <summary>The world the talk happens on (the NPC's).</summary>
        public string LocationId { get; }

        public DialogDefinition Dialog { get; }

        /// <summary>The node whose choices the client shows.</summary>
        public int Node { get; set; }

        public double StartedAt { get; }

        /// <summary>Uptime of the last step (the opening or a choice).</summary>
        public double LastActiveAt { get; set; }
    }

    /// <summary>"Later radio call" consequences waiting to fire (runtime-only — a restart drops the
    /// courtesy call, nothing of value is lost).</summary>
    private readonly List<(string PlayerId, string NpcKey, string NpcName, string Place, string BodyId, string LineKey, double Due, double GiveUpAt)> _dialogRadioPending = new();

    private const double DialogRadioDelaySeconds = 90.0;

    /// <summary>A promised call deferred by a soft radio gate (cadence, quiet period, reach) retries this
    /// often — the dialogue armed it exactly once, so losing it would break the promise for good (#1149).</summary>
    private const double DialogRadioRetrySeconds = 60.0;

    /// <summary>…but not forever: after this long the NPC gives up (the player stayed offline/off-radio).</summary>
    private const double DialogRadioGiveUpSeconds = 1800.0;

    /// <summary>Engine dialogues plus the active story pack's own (authored characters). Order matters:
    /// pack dialogues come FIRST so a character's authored dialogue wins over any generic role match.</summary>
    private IEnumerable<DialogDefinition> AllDialogs()
    {
        if (StoryActive && _story is not null)
        {
            foreach (var d in _story.Dialogs)
            {
                yield return d;
            }
        }

        foreach (var d in _content.Dialogs)
        {
            yield return d;
        }
    }

    /// <summary>The relationship-memory key for dialogue purposes: an authored character remembers the
    /// player GLOBALLY ("char:&lt;id&gt;" — the whole point of a recurring face), everyone else keeps the
    /// per-place role key.</summary>
    private string DialogNpcKey(PlayerSession session, ServerNpc npc)
        => !string.IsNullOrEmpty(npc.CharacterId) ? "char:" + npc.CharacterId : NpcKeyForNpc(session, npc);

    /// <summary>Best dialogue for this NPC + player right now, or null (→ the ordinary greeting bubble).
    /// An authored character offers their own dialogue first; once that is done they fall back to the
    /// generic dialogues of the role they occupy.</summary>
    private DialogDefinition? PickDialog(PlayerSession session, ServerNpc npc, string npcKey)
    {
        var p = session.State;
        int rel = RelationshipValue(p, npcKey); // #2598: a villager without an own entry reads the floor
        int stage = StageRank(RelationshipTier(rel));

        // #2598: a villager friend first hands over a little present (at most every half hour of playtime) — not while
        // visiting the player's base (#2602): the guest's own talk brings its present.
        bool guest = IsGuestJob(npc.Job);
        if (!guest && FriendGiftDue(session, npc, npcKey) && FriendGiftDialog() is { Nodes.Count: > 0 } friendGift)
        {
            return friendGift;
        }

        bool ownResident = IsOwnBaseResident(session, npc); // #2594: "welcome home" talk vs. a visitor's talk
        foreach (var d in AllDialogs())
        {
            bool profession = guest || NpcProfessions.ByJob(npc.Job) != null // #2602: a guest_* job talks like a profession
                || IsLodgeJob(npc.Job); // #2641: so do the wilderness folk (their own lodge_<job> talk, never the vendor's)
            bool matches = !string.IsNullOrEmpty(d.Character)
                ? string.Equals(d.Character, npc.CharacterId, StringComparison.OrdinalIgnoreCase)
                : !string.IsNullOrEmpty(d.Job)
                    ? string.Equals(d.Job, npc.Job, StringComparison.OrdinalIgnoreCase) // 2026-09: a profession's own talk
                    : !profession && (string.IsNullOrEmpty(d.Role) || string.Equals(d.Role, npc.Role, StringComparison.OrdinalIgnoreCase));
            if (!matches
                || !DialogHomeMatches(d.Home, ownResident, npc.LodgeKey.Length > 0)
                || d.Nodes.Count == 0
                || (d.PlanetTypes.Count > 0 && !d.PlanetTypes.Contains(_worlds.Active.PlanetType, StringComparer.OrdinalIgnoreCase)) // #1793
                || StageRank(d.MinStage) > stage
                || (d.OncePerPlayer && p.Milestones.Contains(DialogDoneMilestonePrefix + d.Key + ":done")))
            {
                continue;
            }

            // #1212: a dialogue that hands out a mission is offered only while that mission can be taken from
            // this NPC (and not again this session once declined) — otherwise it falls through to the next match.
            if (!DialogOffersTakeableMission(session, npcKey, d, out bool offersMission) && offersMission)
            {
                continue;
            }

            return d;
        }

        return null;
    }

    /// <summary>#2594: whether the NPC is a resident of a base the talking player owns — "welcome home" rather than a
    /// visitor's smalltalk. An ally's residents are someone else's home.</summary>
    private bool IsOwnBaseResident(PlayerSession session, ServerNpc npc)
    {
        if (npc.BaseId <= 0)
        {
            return false;
        }

        foreach (var b in _bases)
        {
            if (b.Id == npc.BaseId)
            {
                return b.OwnerId == session.State.PlayerId;
            }
        }

        return false;
    }

    /// <summary>#2594: a dialogue's <see cref="DialogDefinition.Home"/> against where the NPC lives for this player (#2641:
    /// a lodge dialogue only for a lodge's resident).</summary>
    private static bool DialogHomeMatches(string? home, bool ownResident, bool lodgeResident = false) => home switch
    {
        DialogDefinition.HomeOwnBase => ownResident,
        DialogDefinition.HomeAway => !ownResident,
        DialogDefinition.HomeLodge => lodgeResident,
        _ => true,
    };

    /// <summary>The player talks to an NPC (E in reach). A matching dialogue opens the panel; none →
    /// the ordinary greeting bubble, which makes settlers greet-able for the first time too. The reach is measured the
    /// short way round both seams (#2613: the home pad lies on X = 0, so a resident often stands on the other side).
    /// #2616: a new talk ends the conversation the player was in.</summary>
    private void HandleTalkToNpc(PlayerSession session, TalkToNpcIntent intent)
    {
        var npc = _npcs.FirstOrDefault(n => n.Id == intent.NpcId);
        if (npc is null || npc.VisitingAway || WrapDistSq(npc.Pos, session.State.Position) > NpcTalkRange * NpcTalkRange)
        {
            return; // nobody there (a villager away visiting a base is hidden at home — the guest is the one to talk to)
        }

        EndNpcConversation(session, tellClient: true);
        if (npc.Pose == 2)
        {
            // #1867: someone asleep in bed mumbles and stays there — no dialogue, no job talk at midnight.
            // #2593: a bubble of its own (id) — the client mumbles it lower and quieter.
            Send(session, new NpcGreeting { NpcId = npc.Id, Name = npc.Name, Role = npc.Role, Text = Localize(session.Locale, "npc.greet.sleepy"), GreetingId = NextGreetingId() });
            return;
        }

        RaiseGameEvent(session, GameEventKeys.NpcTalked, npc.Role); // #2482: the neighbours course
        StopVillageChat(npc); // #2600: a villager chatting with a neighbour turns to the player who spoke to them
        string npcKey = DialogNpcKey(session, npc);
        MarkPassingGreeted(npc, session.State.PlayerId); // #2593: a talk is today's hello — no passing "hi" right after it
        if (TryGiveOwedFestivalGift(session, npc))
        {
            return; // #2604: the festival present that waited for room in the bag — this talk is its hand-over
        }

        // #2595: an acquaintance who has not seen the player for a long time says so — read BEFORE anything below
        // stamps "seen" (the dialogue choice's interaction stamps too).
        bool welcomeBack = NpcWelcomeBackDue(session.State, npcKey);

        // #2601: a villager's personal wish comes before any other dialogue — the offer, the hand-over or the "no hurry"
        // reminder (it sends the welcome bubble first itself); then "seen", as on every path below.
        if (TryWishConversation(session, npc, npcKey, welcomeBack))
        {
            StampNpcSeen(session.State, npcKey);
            return;
        }

        var dialog = PickDialog(session, npc, npcKey);
        if (dialog is null)
        {
            if (welcomeBack)
            {
                SendPassingGreeting(session, npc, npcKey, welcomeBack: true); // the welcome IS the greeting
            }
            else
            {
                EmitGreeting(session, npc); // nothing to decide — the familiar over-head line
            }

            StampNpcSeen(session.State, npcKey); // #2595: AFTER the greeting — a welcome-back check reads the old stamp
            return;
        }

        if (welcomeBack)
        {
            SendPassingGreeting(session, npc, npcKey, welcomeBack: true); // #2595: the welcome bubble alongside the panel
        }

        var node = dialog.Nodes[0];
        string text = DialogOpeningText(session, npc, npcKey, dialog, node);
        if (node.Choices.Count > 0)
        {
            StartNpcConversation(session, npc, dialog);
        }

        Send(session, new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = text,
            Choices = node.Choices.Select(c => Localize(session.Locale, c.TextKey)).ToArray(),
            End = node.Choices.Count == 0,
        });
        StampNpcSeen(session.State, npcKey); // #2595: an acquaintance saw the player now (existing entries only)
    }

    // ---------------- #2616: the conversation's life ----------------

    /// <summary>
    /// #2616 — the ONE validity rule of a conversation, used by every reader (the choice, the 1 Hz sweep, the passing
    /// greeting's "in a conversation", village chatter, the visiting-neighbour pick): the player is joined, not in space,
    /// on the world the talk happens on (which is the active world), the NPC is still that very object on it (by
    /// reference — ids are reused per world), not away visiting a base, and within <see cref="NpcTalkRange"/> +
    /// <see cref="NpcTalkKeepMargin"/> measured the short way round both seams.
    /// </summary>
    private bool NpcConversationValid(PlayerSession session, NpcConversation c)
    {
        if (!session.Joined || session.CurrentLocationId != c.LocationId || _worlds.Active?.LocationId != c.LocationId
            || InSpace(session.State.PlayerId) || c.Npc.VisitingAway || !_npcs.Contains(c.Npc))
        {
            return false;
        }

        float keep = NpcTalkRange + NpcTalkKeepMargin;
        return WrapDistSq(session.State.Position, c.Npc.Pos) <= keep * keep;
    }

    /// <summary>#2616: the player is in a live conversation (an open panel with replies, the NPC still in reach).</summary>
    private bool InLiveConversation(PlayerSession session)
        => session.Conversation is { } c && NpcConversationValid(session, c);

    /// <summary>#2616: opens a conversation (only for a step that offers replies) — a running one ends first — and holds
    /// the NPC still, facing the player.</summary>
    private void StartNpcConversation(PlayerSession session, ServerNpc npc, DialogDefinition dialog)
    {
        EndNpcConversation(session, tellClient: false);
        var c = new NpcConversation(npc, _world.LocationId, dialog, _uptime);
        session.Conversation = c;
        HoldNpcForTalk(session, c);
    }

    /// <summary>#2616: the talking NPC stands still and looks at the player (the attend fields), and its walk and routine
    /// wait — never longer than <see cref="NpcTalkHoldSeconds"/> after the talk began. Renewed by the 1 Hz sweep in short
    /// steps, so a talk that ends without a word lets the NPC go on its own.</summary>
    private void HoldNpcForTalk(PlayerSession session, NpcConversation c)
    {
        var npc = c.Npc;
        double until = _uptime + NpcTalkAttendSeconds;
        npc.AttendUntil = Math.Max(npc.AttendUntil, until);
        npc.AttendPos = session.State.Position;
        npc.TalkHoldUntil = Math.Max(npc.TalkHoldUntil, Math.Min(until, c.StartedAt + NpcTalkHoldSeconds));
    }

    /// <summary>#2616: ends the player's conversation, if any, and lets the NPC go (unless another player still talks with
    /// it). <paramref name="tellClient"/>: the panel still shows replies — close it (<see cref="NpcDialogState.ActionClose"/>,
    /// only to a client announcing <c>dialogclose1</c>: an older one had its panel closed already or keeps the dead replies
    /// as before); false when the client closed it itself (the leave) or the step being sent closes it anyway.</summary>
    private void EndNpcConversation(PlayerSession session, bool tellClient)
    {
        if (session.Conversation is not { } c)
        {
            return;
        }

        session.Conversation = null;
        var npc = c.Npc;
        bool stillTalking = false;
        foreach (var other in _sessions.Values)
        {
            if (other.Conversation is { } oc && ReferenceEquals(oc.Npc, npc))
            {
                stillTalking = true;
                break;
            }
        }

        if (!stillTalking)
        {
            npc.TalkHoldUntil = 0;
            npc.AttendUntil = Math.Min(npc.AttendUntil, _uptime + 1.0); // a last look, then back to its day
        }

        if (tellClient && session.SupportsDialogClose) // an older panel would pop open again for it (dialogclose1)
        {
            Send(session, new NpcDialogState
            {
                NpcId = npc.Id,
                Name = npc.Name,
                End = true,
                Action = NpcDialogState.ActionClose,
            });
        }
    }

    /// <summary>#2616: the per-world 1 Hz conversation sweep (registered after <c>TickNpcs</c>).</summary>
    private void TickNpcConversations(double dt)
    {
        _ = dt;
        if (_worlds.Active is null)
        {
            return;
        }

        string world = _world.LocationId;
        if (_conversationTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _conversationTickAt[world] = _uptime + 1.0;
        SweepNpcConversations();
    }

    /// <summary>#2616: ends every dead conversation and tells the client; holds every live one's NPC a little longer. A
    /// talk whose player left its world is dead whichever world ticks (the world it was on may no longer tick at all);
    /// the full rule is checked on the talk's own world.</summary>
    private void SweepNpcConversations()
    {
        string world = _world.LocationId;
        List<PlayerSession>? dead = null;
        foreach (var s in _sessions.Values)
        {
            if (s.Conversation is not { } c)
            {
                continue;
            }

            bool otherWorld = c.LocationId != world;
            if (otherWorld && s.Joined && s.CurrentLocationId == c.LocationId)
            {
                continue; // the talk's own world checks it
            }

            if (otherWorld || !NpcConversationValid(s, c))
            {
                (dead ??= new List<PlayerSession>()).Add(s);
            }
            else
            {
                HoldNpcForTalk(s, c);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var s in dead)
        {
            EndNpcConversation(s, tellClient: s.Joined);
        }
    }

    /// <summary>Most characters an AI re-wording of a dialogue opening may take (#2594) — the authored openings are
    /// one or two sentences.</summary>
    private const int DialogOpenMaxChars = 140;

    /// <summary>
    /// The opening prompt of a dialogue (node 0) for this player (#2594): the authored line — or, for a dialogue marked
    /// <see cref="DialogDefinition.AiPrompt"/> with AI personalisation on, a re-wording in the NPC's voice when one is
    /// ALREADY cached (purpose <c>dialog_open</c>, warmed while the player walked past — <see cref="WarmDialogOpening"/>).
    /// A miss starts the request for next time; the panel is never swapped after it opened, and the choices,
    /// responses and branches always stay authored.
    /// </summary>
    private string DialogOpeningText(PlayerSession session, ServerNpc npc, string npcKey, DialogDefinition dialog, DialogNode node)
    {
        string authored = Localize(session.Locale, node.PromptKey);
        if (!dialog.AiPrompt || !AiLinesEnabled)
        {
            return authored;
        }

        var ai = DialogOpeningAiLines(session, npc, npcKey, dialog, authored);
        return ai is { Length: > 0 } && FillAiLine(ai[0], session) is { Length: > 0 } text ? text : authored;
    }

    /// <summary>#2594: warms the <c>dialog_open</c> re-wording of the dialogue this NPC would open for the player now, so
    /// the next E-talk can show it (called from the passing greeting; a no-op without AI or for an authored opening).</summary>
    private void WarmDialogOpening(PlayerSession session, ServerNpc npc, string npcKey)
    {
        if (!AiLinesEnabled || PickDialog(session, npc, npcKey) is not { AiPrompt: true, Nodes.Count: > 0 } dialog)
        {
            return;
        }

        DialogOpeningAiLines(session, npc, npcKey, dialog, Localize(session.Locale, dialog.Nodes[0].PromptKey));
    }

    /// <summary>The cached <c>dialog_open</c> variant for this NPC's voice (null on a miss, which starts a request).
    /// Keyed by dialogue, persona, language and stage — never by player. A guest at the base (#2602) asks for its offer
    /// (<c>guest_offer</c>: a present, goods or the way) on the same path — warmed when it spawns.</summary>
    private string[]? DialogOpeningAiLines(PlayerSession session, ServerNpc npc, string npcKey, DialogDefinition dialog, string authored)
    {
        string stage = RelationshipTier(RelationshipValue(session.State, npcKey));
        bool guest = IsGuestJob(npc.Job);
        string purpose = guest ? NpcLinePurposes.GuestOffer : NpcLinePurposes.DialogOpen;
        string cacheKey = AiLineCacheKey(purpose, dialog.Key,
            PersonaIndexFor(npcKey, npc.IsRobot, npc.Role), session.Locale, stage);
        return AiLinesFor(cacheKey, AiLinePriority.Personal, () => new NpcLinesRequest
        {
            Purpose = purpose,
            Language = session.Locale,
            Theme = npc.Theme,
            Stage = stage,
            Situation = guest ? "visiting the player's own base, offering something"
                : IsOwnBaseResident(session, npc) ? "the player's own base, where the speaker lives" : string.Empty,
            Speakers =
            {
                new NpcLinesSpeaker
                {
                    Role = npc.Role,
                    Job = npc.Job,
                    IsRobot = npc.IsRobot,
                    Persona = PersonaFor(npcKey, npc.Theme, npc.IsRobot, npc.Role),
                },
            },
            Tokens = { AiLineTokens.Player },
            Authored = { authored },
            LineCount = 1,
            MaxChars = DialogOpenMaxChars,
        });
    }

    /// <summary>The player picks a reply. The server owns the walk: validate against ITS node, record the
    /// (finally producing) Dialog interaction, persist the decision, apply the consequence, then answer
    /// with the response — plus the follow-up node's prompt when the dialogue continues.
    /// <para>#2616: a negative index is the leave (the client closed the panel). A choice or leave naming another NPC than
    /// the conversation's is a late one for an earlier panel and changes nothing. A choice in a conversation that is no
    /// longer valid (walked away, another world, the NPC gone — <see cref="NpcConversationValid"/>) applies nothing: the
    /// conversation ends and the client's panel closes.</para></summary>
    private void HandleNpcDialogChoice(PlayerSession session, NpcDialogChoiceIntent intent)
    {
        if (session.Conversation is not { } active || (intent.NpcId != 0 && intent.NpcId != active.Npc.Id))
        {
            return;
        }

        if (intent.ChoiceIndex < 0)
        {
            EndNpcConversation(session, tellClient: false); // the leave — the panel is already gone
            return;
        }

        var npc = active.Npc;
        var node = active.Node >= 0 && active.Node < active.Dialog.Nodes.Count ? active.Dialog.Nodes[active.Node] : null;
        if (!NpcConversationValid(session, active) || node is null || intent.ChoiceIndex >= node.Choices.Count)
        {
            EndNpcConversation(session, tellClient: true);
            return;
        }

        active.LastActiveAt = _uptime;
        var choice = node.Choices[intent.ChoiceIndex];
        string npcKey = DialogNpcKey(session, npc);
        var p = session.State;

        // Repeatable smalltalk pays its relationship reward at most once per hour per NPC (#1162): the
        // dialogue itself stays walkable any time, but spam-talking no longer farms stranger → trusted in
        // twenty seconds. Once-per-player dialogues keep their own guard (the done-flag).
        bool paysReward = active.Dialog.OncePerPlayer || DialogRewardOffCooldown(session, active.Dialog.Key, npcKey);
        if (paysReward)
        {
            // #2602: a villager lives in their village even when the talk happens at the player's base (a neighbour's visit).
            string place = npc.PersonKey.Length > 0 && npc.BaseId == 0 && npc.Settlement.Length > 0 ? npc.Settlement : NpcPlaceFor(p);
            RecordNpcInteraction(p, npcKey, npc.Name, npc.Role, NpcInteractionKind.Dialog, place);
        }

        p.Milestones.Add(DialogFlagMilestonePrefix + active.Dialog.Key + ":" + active.Node + ":" + intent.ChoiceIndex);
        _dialogAction = string.Empty;
        _dialogExtraText = string.Empty;
        _dialogResponseOverride = string.Empty;
        ApplyDialogConsequence(session, npc, npcKey, choice.Consequence, paysReward);
        RaiseGameEvent(session, GameEventKeys.NpcAnswered, npc.Role); // #2482: the neighbours course

        bool end = choice.Next < 0 || choice.Next >= active.Dialog.Nodes.Count;
        if (end && active.Dialog.OncePerPlayer && !choice.KeepOpen)
        {
            p.Milestones.Add(DialogDoneMilestonePrefix + active.Dialog.Key + ":done");
        }

        // #1212: ending a mission-offering dialogue WITHOUT taking the job = "not today" for this session.
        if (end && DialogOfferedMissions(active.Dialog).Any()
            && !choice.Consequence.StartsWith(MissionChains.DialogConsequence + ":", StringComparison.Ordinal))
        {
            session.DeclinedMissionDialogs.Add(active.Dialog.Key);
        }

        _repo.SavePlayer(p);
        SendNpcStandings(session); // the Dialog interaction (and a standing consequence) may have moved the tier

        if (end)
        {
            EndNpcConversation(session, tellClient: false); // the closing line below ends the panel
            Send(session, new NpcDialogState
            {
                NpcId = npc.Id,
                Name = npc.Name,
                Text = (_dialogResponseOverride.Length > 0 ? _dialogResponseOverride : Localize(session.Locale, choice.ResponseKey))
                    + _dialogExtraText, // #2598: a consequence may replace the authored answer (a present that doesn't fit)
                End = true,
                Action = _dialogAction,
            });
            return;
        }

        var next = active.Dialog.Nodes[choice.Next];
        string text = Localize(session.Locale, choice.ResponseKey) + "\n\n" + Localize(session.Locale, next.PromptKey);
        if (next.Choices.Count > 0)
        {
            active.Node = choice.Next;
            HoldNpcForTalk(session, active);
        }
        else
        {
            EndNpcConversation(session, tellClient: false); // a node without replies is a closing line
        }

        Send(session, new NpcDialogState
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Text = text,
            Choices = next.Choices.Select(c => Localize(session.Locale, c.TextKey)).ToArray(),
            End = next.Choices.Count == 0,
        });
    }

    /// <summary>Whether the repeatable-dialogue reward is off cooldown for this (dialogue, NPC) — arming the
    /// cooldown when it is (#1162). Session-scoped, mirroring the radio repeat-cooldown scale.</summary>
    private bool DialogRewardOffCooldown(PlayerSession session, string dialogKey, string npcKey)
    {
        string key = dialogKey + "|" + npcKey;
        if (session.DialogRewardCooldownUntil.TryGetValue(key, out double until) && _uptime < until)
        {
            return false;
        }

        // #2598: talking to a PERSON pays every 10 minutes (a child gets to "known" in one session); the shared role
        // keys of the stall and the board keep their hour.
        session.DialogRewardCooldownUntil[key] = _uptime
            + (IsPersonKey(npcKey) ? PersonDialogRewardCooldownSeconds : DialogRewardCooldownSeconds);
        return true;
    }

    /// <summary>Applies a choice's consequence. Unknown/malformed strings are ignored — a data typo must
    /// never break the conversation itself. <paramref name="payStanding"/> false suppresses only the standing
    /// bump (#1162, repeatable-dialogue cooldown) — gifts/fragments/calls carry their own once-guards.</summary>
    private void ApplyDialogConsequence(PlayerSession session, ServerNpc npc, string npcKey, string consequence,
        bool payStanding = true)
    {
        if (string.IsNullOrEmpty(consequence))
        {
            return;
        }

        var parts = consequence.Split(':');
        switch (parts[0])
        {
            case "standing" when !payStanding:
                break; // on reward cooldown (#1162)

            case "standing" when parts.Length >= 2 && int.TryParse(parts[1], out int bump):
                AddStanding(session.State, npcKey, bump, "dialog"); // #2598: a villager's new entry starts at the floor
                break;

            case FriendGiftConsequence:
                GiveFriendGift(session, npcKey); // #2598: a friend's present (a full bag keeps it for next time)
                break;

            case WishConsequence when parts.Length >= 2:
                ApplyWishConsequence(session, npc, npcKey, parts[1]); // #2601: "Sure, I'll help!" / "Maybe later."
                break;

            case GuestConsequence:
                ApplyGuestConsequence(session, npc, parts); // #2602: a guest's present at the base (once per guest and player)
                break;

            case "fragment" when parts.Length >= 2 && StoryActive && _story is not null:
                if (_story.Fragments.FirstOrDefault(f => f.Key == parts[1]) is { } frag)
                {
                    Send(session, new NetFragmentRevealed { Category = frag.Category, TextKey = frag.TextKey });
                    RaiseGameEvent(session, GameEventKeys.StoryFragment, frag.Key); // #2465 (#2475: before the record — see the pickup)
                    RecordStoryFragment(frag.Key); // shared-arc dedupe — a re-gifted fragment only re-reads
                }

                break;

            case "gift" when parts.Length >= 3 && int.TryParse(parts[2], out int count) && count > 0
                && _content.GetItem(parts[1]) is not null:
                Serve(session);
                var pool = new MaterialPool(_content, session.State, _ship);
                pool.Add(parts[1], count);
                SendInventory(session);
                break;

            case "radio" when parts.Length >= 2:
                // "I'll call you" — and a little later, they do (through the radio gates; see the tick).
                _dialogRadioPending.Add((session.State.PlayerId, npcKey, npc.Name, NpcPlaceFor(session.State),
                    session.CurrentLocationId, parts[1], _uptime + DialogRadioDelaySeconds,
                    _uptime + DialogRadioGiveUpSeconds));
                break;

            case MissionChains.DialogConsequence when parts.Length >= 2 && parts[1].Length > 0:
                GrantMissionFromDialog(session, npc, npcKey, parts[1]); // #1212: the conversation IS the acceptance
                break;

            // 2026-09 professions.
            case "photo":
                StartStreamerPhoto(session, npc);
                break;

            case "photo_never":
                session.State.Milestones.Add(StreamerNeverMilestone);
                break;

            case "interview":
                session.PendingInterviewNpcId = npc.Id;
                _dialogAction = "interview";
                break;

            case TreatConsequence:
                ApplyTreatConsequence(session); // #2628: the doctor heals you, once per in-game day
                break;

            case "news":
                _dialogExtraText = "\n\n" + NewsDigest(session, npc);
                break;

            // #2641: the wilderness folk.
            case LodgeGiftConsequence:
                ApplyLodgeGift(session, npc); // a friend's present from the store crate, once per in-game day
                break;

            case LodgeTipConsequence:
                ApplyLodgeTip(session); // the nearest unrevealed find goes on the map, else the way to the village
                break;

            case LodgePoisonConsequence:
                ApplyLodgePoison(session); // the gatherer names this world's poisonous plants
                break;
        }
    }

    /// <summary>Fires due "later radio call" consequences (piggybacks on the greeting tick's cadence).</summary>
    private void TickDialogRadio()
    {
        if (_dialogRadioPending.Count == 0)
        {
            return;
        }

        for (int i = _dialogRadioPending.Count - 1; i >= 0; i--)
        {
            var pending = _dialogRadioPending[i];
            if (_uptime < pending.Due)
            {
                continue;
            }

            if (_uptime >= pending.GiveUpAt)
            {
                _dialogRadioPending.RemoveAt(i); // the NPC gives up — player stayed offline/unreachable
                continue;
            }

            // The dialogue itself was the personal contact, so the promised call skips the KNOWN gate; a
            // soft gate (cadence, join quiet, reach) defers the one-shot instead of losing it — only the
            // player's own preference (calls off / missions only) drops it for good (#1149).
            var owner = FindSessionByPlayerId(pending.PlayerId);
            bool sent = owner is { Joined: true }
                && TryNpcRadioCall(owner, pending.NpcKey, pending.NpcName, pending.Place, pending.BodyId,
                    "dialog:" + pending.LineKey, pending.LineKey, string.Empty,
                    isMission: pending.LineKey == ChainRadioLineKey, // #1212: the chain nudge is a mission call
                    requireKnown: false);
            if (sent || (owner is { Joined: true } && owner.State.NpcCallsMode != NpcCallsMode.All))
            {
                _dialogRadioPending.RemoveAt(i);
            }
            else
            {
                _dialogRadioPending[i] = (pending.PlayerId, pending.NpcKey, pending.NpcName, pending.Place,
                    pending.BodyId, pending.LineKey, _uptime + DialogRadioRetrySeconds, pending.GiveUpAt);
            }
        }
    }

    /// <summary>Slots an authored character already claimed in the current spawn pass of a place — a
    /// winning place casts the character exactly once; every other eligible marker stays a regular NPC
    /// (#1150: without this, a market with three npc markers spawned three identical Yara Senns).</summary>
    private readonly HashSet<string> _claimedCharacterSlots = new();

    /// <summary>Call before (re)spawning a place's NPCs so its authored-character claim resets with it.
    /// Spawn order within a place is deterministic (template marker order), so the same slot wins on
    /// every reload.</summary>
    private void BeginAuthoredCasting(string placeKey)
        => _claimedCharacterSlots.RemoveWhere(k => k.EndsWith("|" + placeKey, StringComparison.Ordinal));

    /// <summary>Lets a story pack's authored character (#1128) claim a freshly spawned NPC: a deterministic
    /// hash over (seed, place, character) picks roughly one in <see cref="StoryCharacter.OneIn"/> eligible
    /// places, and within a winning place the first eligible slot in spawn order gets them (#1150). The
    /// claim fixes name, face, body and outfit — the same person everywhere — and never touches
    /// quartermasters (their coined name is baked into their board's mission texts).</summary>
    private void ApplyAuthoredCharacter(ServerNpc npc, string placeKind, string placeKey)
    {
        if (!StoryActive || _story is null)
        {
            return;
        }

        foreach (var ch in _story.Characters)
        {
            if (string.IsNullOrEmpty(ch.Id) || string.IsNullOrEmpty(ch.Name)
                || npc.Role == "quartermaster"
                || !ch.Roles.Contains(npc.Role)
                || (!string.IsNullOrEmpty(ch.Places) && ch.Places != placeKind))
            {
                continue;
            }

            uint h = (uint)(_meta.Seed ^ WorldGenerator.StableHash("char:" + ch.Id + "|" + placeKey));
            if (h % (uint)Math.Max(1, ch.OneIn) != 0)
            {
                continue;
            }

            if (!_claimedCharacterSlots.Add(ch.Id + "|" + placeKey))
            {
                continue; // the place already has them — one Yara per market, one Sel-9 per bazaar (#1150)
            }

            var look = new Random(unchecked((int)WorldGenerator.StableHash("charlook:" + ch.Id)));
            npc.CharacterId = ch.Id;
            npc.Name = ch.Name;
            npc.IsRobot = ch.Robot;
            npc.Size = 1.0f;
            npc.SkinRgb = ch.Robot
                ? new uint[] { 0xBFC7CF, 0xD5DBE1, 0xA8B2BC, 0xC9CCB8 }[look.Next(4)]
                : new uint[] { 0xFFE0C4, 0xD9A066, 0xA9713A, 0x8D5524 }[look.Next(4)];
            npc.OutfitRgb = new uint[] { 0x3D7EBF, 0x8A63BF, 0x2FA48E, 0xC24B5A, 0xB3A05C, 0x6B8FA3 }[look.Next(6)];
            npc.LegsRgb = new uint[] { 0x4A4E57, 0x5C5346, 0x3E4A5C, 0x6B5C4A }[look.Next(4)];
            return; // first matching character wins — at most one face per slot
        }
    }

    /// <summary>The stable face seed an authored character carries onto the wire (0 = not authored).</summary>
    private static int CharacterFaceVariant(ServerNpc npc)
        => string.IsNullOrEmpty(npc.CharacterId) ? 0 : unchecked((int)WorldGenerator.StableHash("charface:" + npc.CharacterId));

    // ---------------- Test hooks ----------------

    /// <summary>Test seam: talks to the given NPC as the player (mirrors <see cref="TalkToNpcIntent"/>), on the player's
    /// own world as the dispatcher would.</summary>
    public void TalkToNpcForTest(string playerId, int npcId)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleTalkToNpc(s, new TalkToNpcIntent { NpcId = npcId });
        }
    }

    /// <summary>Test seam: picks a reply in the player's active dialogue (#2616: <paramref name="npcId"/> 0 = the
    /// current conversation, like an older client).</summary>
    public void ChooseDialogForTest(string playerId, int choiceIndex, int npcId = 0)
    {
        if (FindSessionByPlayerId(playerId) is { } s)
        {
            Serve(s);
            HandleNpcDialogChoice(s, new NpcDialogChoiceIntent { ChoiceIndex = choiceIndex, NpcId = npcId });
        }
    }

    /// <summary>Test seam (#2616): the player closes the panel of <paramref name="npcId"/> (the client's leave).</summary>
    public void LeaveDialogForTest(string playerId, int npcId)
        => ChooseDialogForTest(playerId, NpcDialogChoiceIntent.Leave, npcId);

    /// <summary>Test/inspection: the key + node of the player's active dialogue, or null.</summary>
    public (string Key, int Node)? ActiveDialogForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.Conversation is { } c ? (c.Dialog.Key, c.Node) : null;

    /// <summary>Test/inspection (#2616): the NPC id the player is talking with, or null.</summary>
    public int? ConversationNpcForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.Conversation?.Npc.Id;

    /// <summary>Test seam (#2616): runs the active world's conversation sweep now (skips the 1 Hz throttle).</summary>
    public void SweepNpcConversationsForTest() => SweepNpcConversations();

    /// <summary>Test/inspection (#2616): whether a player's talk holds this NPC (standing still, the routine waiting).</summary>
    public bool NpcTalkHeldForTest(int npcId) => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && _uptime < npc.TalkHoldUntil;

    /// <summary>Test/inspection: NPC ids + names + roles + character ids on the active world.</summary>
    public IReadOnlyList<(int Id, string Name, string Role, string CharacterId)> NpcRosterForTest()
        => _npcs.Select(n => (n.Id, n.Name, n.Role, n.CharacterId)).ToList();

    /// <summary>Test seam: runs the due-radio drain immediately.</summary>
    public void TickDialogRadioForTest() => TickDialogRadio();

    /// <summary>Test seam: queues a dialog-promised radio call that is due right away.</summary>
    public void QueueDialogRadioForTest(string playerId, string npcKey, string npcName, string place,
        string bodyId, string lineKey)
        => _dialogRadioPending.Add((playerId, npcKey, npcName, place, bodyId, lineKey, _uptime,
            _uptime + DialogRadioGiveUpSeconds));

    /// <summary>Test/inspection: how many dialog-promised calls are still waiting to fire.</summary>
    public int DialogRadioPendingForTest => _dialogRadioPending.Count;

    /// <summary>Test seam: clears the repeatable-dialogue reward cooldowns (#1162), as if an hour passed.</summary>
    public void SkipDialogRewardCooldownForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is { } session)
        {
            session.DialogRewardCooldownUntil.Clear();
        }
    }

    /// <summary>Test/inspection: authored-character instances grouped per place on the active world.</summary>
    public IReadOnlyList<(string CharacterId, string Place, int Count)> AuthoredCastingForTest()
        => _npcs.Where(n => !string.IsNullOrEmpty(n.CharacterId))
            .GroupBy(n => (n.CharacterId, n.Settlement))
            .Select(g => (g.Key.CharacterId, g.Key.Settlement, g.Count()))
            .ToList();
}
