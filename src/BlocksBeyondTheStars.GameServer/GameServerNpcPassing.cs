// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #2593 — NPCs greet and wave when the player walks past (and #2595: an acquaintance who has not seen the player for a
/// long time says "welcome back" instead). Once a second per world: for every player on foot, the nearest awake NPC within
/// <see cref="PassingGreetRange"/> with a clear line of sight — standing or sitting, not walking a route (the patrolling
/// guard included), never a G.D.S. guardian or the streamer (who has a call-out of its own) — says a short line with a
/// wave and pauses for a moment to look at them.
/// <list type="bullet">
/// <item>At most once per NPC and player per in-game day (<see cref="MarketDay"/>, E2); a talk via E counts as that day's
/// hello, and an NPC greeted via E within the last 25 s is only marked.</item>
/// <item>At most one passing greeting every <see cref="PassingGreetPlayerGap"/> per player — <see cref="PassingGreetCrowdGap"/>
/// when more than <see cref="PassingCrowdThreshold"/> NPCs who could greet stand within <see cref="PassingCrowdRange"/>
/// (no chorus on a station deck or in a full tavern — the NO SOUND JUMBLE rule).</item>
/// <item>No side effects: story threads and treasure hints are never consumed (that is the E-greeting's), strangers gain
/// nothing; a villager the player already met gets the passing hello's +1 once per session below "friend"
/// (<see cref="TryHelloStanding"/>), and the NPC's "last seen" stamp moves on.</item>
/// <item>Lines by stage — stranger "Hello!", known "Hello, {name}!", friend "{name}! Great to see you!", the player's own
/// residents "Welcome home, {name}!" or a line about their job. With AI personalisation on, known+ NPCs and residents get
/// a re-worded line from the shared AI path (<c>passing_hello</c> / <c>resident_hello</c> / <c>welcome_back</c>) when one
/// is already cached — never for strangers (E14). A miss shows the authored line and asks for one in the background;
/// there is no late swap of a passing bubble.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close the player passes for an NPC to greet them (the NPC face range).</summary>
    private const float PassingGreetRange = 6f;

    /// <summary>The gap between two passing greetings to one player.</summary>
    private const double PassingGreetPlayerGap = 4.0;

    /// <summary>The gap in a crowd (more than <see cref="PassingCrowdThreshold"/> NPCs within <see cref="PassingCrowdRange"/>).</summary>
    private const double PassingGreetCrowdGap = 8.0;

    /// <summary>The radius a crowd is counted in.</summary>
    private const float PassingCrowdRange = 10f;

    /// <summary>More NPCs than this within <see cref="PassingCrowdRange"/> make a crowd.</summary>
    private const int PassingCrowdThreshold = 6;

    /// <summary>How long a greeting NPC stands still and looks at the player.</summary>
    private const double PassingAttendSeconds = 2.5;

    /// <summary>Most characters of an AI-worded passing line (a bubble, one sentence).</summary>
    private const int PassingLineMaxChars = 90;

    /// <summary>The jobs a resident has a line of their own for (<c>npc.hello.resident.&lt;job&gt;</c>).</summary>
    private static readonly HashSet<string> ResidentHelloJobs = new(StringComparer.Ordinal)
    {
        "gardener", "craftsman", "guard", "vendor", "quartermaster",
    };

    private readonly Dictionary<string, double> _passingTickAt = new(StringComparer.Ordinal);
    private readonly List<(ServerNpc Npc, double DistSq)> _passingCandidates = new();
    private readonly Random _passingRng = new(0x9A55E5);

    /// <summary>The 1 Hz per-world tick (registered after <c>TickNpcs</c>).</summary>
    private void TickPassingGreetings(double dt)
    {
        if (_npcs.Count == 0)
        {
            return;
        }

        string world = _world.LocationId;
        if (_passingTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _passingTickAt[world] = _uptime + 1.0;
        RunPassingGreetings();
    }

    /// <summary>One pass over the active world's players: at most one passing greeting each.</summary>
    private void RunPassingGreetings()
    {
        long day = MarketDay;
        foreach (var session in JoinedInActiveWorld())
        {
            var p = session.State;
            if (session.Spectating || (_shipPlaced && p.AboardShip) || InSpace(p.PlayerId)
                || _uptime < session.Npc.PassingGreetReadyAt || InConversationNearby(session))
            {
                continue; // aboard / in flight / in a conversation already — nobody calls out across it
            }

            _passingCandidates.Clear();
            int crowd = 0;
            foreach (var npc in _npcs)
            {
                if (!MayGreetInPassing(npc))
                {
                    continue;
                }

                double d = WrapDistSq(p.Position, npc.Pos);
                if (d <= PassingCrowdRange * PassingCrowdRange)
                {
                    crowd++;
                }

                if (d <= PassingGreetRange * PassingGreetRange && !GreetedInPassing(npc, p.PlayerId, day)
                    && !NpcInPlayerDialog(npc)) // #2616: busy talking with someone else — no hello across that talk
                {
                    _passingCandidates.Add((npc, d));
                }
            }

            if (_passingCandidates.Count == 0)
            {
                continue;
            }

            _passingCandidates.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
            foreach (var (npc, _) in _passingCandidates)
            {
                if (!HasLineOfSight(npc.Pos, p.Position))
                {
                    continue; // behind a wall: they cannot see the player walk by
                }

                string npcKey = NpcKeyForNpc(session, npc);
                if (RelationshipTier(RelationshipValue(p, npcKey)) == "hostile")
                {
                    continue;
                }

                MarkPassingGreeted(npc, p.PlayerId);
                if (_lastGreetAt.TryGetValue(p.PlayerId + "|" + npcKey, out double last) && _uptime - last < GreetCooldownSeconds)
                {
                    continue; // just greeted via E — that was today's hello
                }

                bool welcomeBack = NpcWelcomeBackDue(p, npcKey); // #2595: read the old stamp before stamping it below
                SendPassingGreeting(session, npc, npcKey, welcomeBack);
                TryHelloStanding(session, npcKey); // #2598: +1 once per session for a villager already met, below "friend"
                StampNpcSeen(p, npcKey);
                if (IsOwnBaseResident(session, npc))
                {
                    WarmDialogOpening(session, npc, npcKey); // #2594: the resident's talk, re-worded for the next E
                }

                npc.AttendUntil = _uptime + PassingAttendSeconds;
                npc.AttendPos = p.Position;
                session.Npc.PassingGreetReadyAt = _uptime
                    + (crowd > PassingCrowdThreshold ? PassingGreetCrowdGap : PassingGreetPlayerGap);
                break;
            }
        }
    }

    /// <summary>Whether an NPC may greet a passer-by at all: awake, not walking a route (the patrolling guard included),
    /// not a G.D.S. guardian, not the streamer (it has its own call-out), not in the middle of a chat with a neighbour
    /// (#2600 — two voices at once would be the jumble the voice budget exists to prevent), not away visiting a base and
    /// not a guest at one (#2602 — a guest greets with its own line), not cheering in a festival ring (#2604).</summary>
    private bool MayGreetInPassing(ServerNpc npc)
        => npc.Pose != 2 && npc.Goal is null && npc.Role != "guardian" && npc.Job != "streamer"
           && (npc.TalkWith is null || _uptime >= npc.TalkUntil)
           && !npc.VisitingAway && !IsGuestJob(npc.Job) && npc.Phase != NpcPhase.Festival;

    /// <summary>Whether the player is in a conversation — #2616: the one validity rule (a talk the player walked away
    /// from is over, even before the 1 Hz sweep ended it).</summary>
    private bool InConversationNearby(PlayerSession session) => InLiveConversation(session);

    /// <summary>Whether this NPC already greeted the player on in-game day <paramref name="day"/>.</summary>
    private static bool GreetedInPassing(ServerNpc npc, string playerId, long day)
        => npc.GreetedDay is { } days && days.TryGetValue(playerId, out long d) && d == day;

    /// <summary>Marks today's hello between an NPC and a player as said (a passing greeting or a talk via E).</summary>
    private void MarkPassingGreeted(ServerNpc npc, string playerId)
        => (npc.GreetedDay ??= new Dictionary<string, long>(StringComparer.Ordinal))[playerId] = MarketDay;

    /// <summary>
    /// Sends a passing greeting (or, with <paramref name="welcomeBack"/>, the welcome-back line) to one player: a new
    /// bubble id, kind <see cref="NpcGreetingKinds.Passing"/> / <see cref="NpcGreetingKinds.Welcome"/>, a wave, and the
    /// text server-localized (older clients show it as it is). Pure send — no stamps, no standing.
    /// </summary>
    private void SendPassingGreeting(PlayerSession session, ServerNpc npc, string npcKey, bool welcomeBack)
    {
        Send(session, new NpcGreeting
        {
            NpcId = npc.Id,
            Name = npc.Name,
            Role = npc.Role,
            Text = PassingLineText(session, npc, npcKey, welcomeBack),
            GreetingId = NextGreetingId(),
            Kind = welcomeBack ? NpcGreetingKinds.Welcome : NpcGreetingKinds.Passing,
            Gesture = NpcGestures.Wave,
        });
    }

    /// <summary>The line an NPC says in passing to this player: an AI re-wording when one is cached for known+ NPCs and
    /// residents, else an authored line of the stage's pool with the player's name filled in.</summary>
    private string PassingLineText(PlayerSession session, ServerNpc npc, string npcKey, bool welcomeBack)
    {
        var p = session.State;
        string tier = RelationshipTier(RelationshipValue(p, npcKey));
        bool ownResident = IsOwnBaseResident(session, npc);
        string subject = npc.Job.Length > 0 ? npc.Job : npc.Role;
        string purpose;
        List<string> pool;
        string? jobLine = null;
        int awayDays = 0;
        if (welcomeBack)
        {
            purpose = NpcLinePurposes.WelcomeBack;
            string who = ownResident ? "resident" : tier == "trusted" ? "trusted" : "known";
            pool = KeyPool("npc.welcome." + who + ".", 2);
            awayDays = NpcDaysAway(p, npcKey);
            subject += "." + AwayBucket(awayDays);
        }
        else if (ownResident)
        {
            purpose = NpcLinePurposes.ResidentHello;
            pool = KeyPool("npc.hello.resident.", 3);
            if (ResidentHelloJobs.Contains(npc.Job))
            {
                jobLine = "npc.hello.resident." + npc.Job;
            }
        }
        else
        {
            purpose = NpcLinePurposes.PassingHello;
            pool = KeyPool("npc.hello." + (tier is "known" or "trusted" ? tier : "stranger") + ".", 3);
        }

        string name = p.Name ?? string.Empty;
        string chosen = jobLine is not null && _passingRng.Next(2) == 0 ? jobLine : pool[_passingRng.Next(pool.Count)];
        string authored = Localize(session.Locale, chosen).Replace("{0}", name);

        // AI only for someone who knows the player (E14: no AI hellos for strangers — they would cost the most).
        bool knows = welcomeBack || ownResident || tier is "known" or "trusted";
        if (!knows || !AiLinesEnabled)
        {
            return authored;
        }

        if (jobLine is not null)
        {
            pool.Add(jobLine);
        }

        var anchors = pool.Select(k => Localize(session.Locale, k).Replace("{0}", AiLineTokens.Player)).ToList();
        string cacheKey = AiLineCacheKey(purpose, subject, PersonaIndexFor(npcKey, npc.IsRobot, npc.Role), session.Locale, tier);
        var ai = AiLinesFor(cacheKey, AiLinePriority.Personal, () => new NpcLinesRequest
        {
            Purpose = purpose,
            Language = session.Locale,
            Theme = npc.Theme,
            Stage = tier,
            AwayDays = awayDays,
            Situation = ownResident ? "the player's own base, where the speaker lives" : string.Empty,
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
            Authored = anchors,
            LineCount = 1,
            MaxChars = PassingLineMaxChars,
        }, requiredTokens: AiLineText.TokensIn(anchors));

        return ai is { Length: > 0 } && FillAiLine(ai[0], session) is { Length: > 0 } text ? text : authored;
    }

    /// <summary>"prefix1" … "prefixN".</summary>
    private static List<string> KeyPool(string prefix, int count)
    {
        var keys = new List<string>(count + 1);
        for (int i = 1; i <= count; i++)
        {
            keys.Add(prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return keys;
    }

    /// <summary>A coarse "how long away" bucket for AI cache keys: a day, a few days, a week or more.</summary>
    private static string AwayBucket(int days) => days <= 1 ? "1" : days <= 6 ? "2-6" : "7+";

    // ---------------- Test seams ----------------

    /// <summary>Test seam (#2593): one passing-greeting pass now (skips the 1 Hz throttle).</summary>
    public void TickPassingGreetingsForTest() => RunPassingGreetings();

    /// <summary>Test seam (#2593): moves the in-game day clock (<see cref="MarketDay"/>) on.</summary>
    public void AdvanceSystemDaysForTest(double days) => _systemTimeDays += days;

    /// <summary>Test seam (#2593): puts an NPC in a known state — where it stands (home and position), its pose, and
    /// optionally its job and role. Any walk goal is dropped.</summary>
    public void ConfigureNpcForTest(int npcId, Vector3f pos, byte pose = 0, string? job = null, string? role = null)
    {
        if (_npcs.FirstOrDefault(n => n.Id == npcId) is not { } npc)
        {
            return;
        }

        npc.Pos = pos;
        npc.Home = pos;
        npc.Pose = pose;
        npc.Goal = null;
        npc.Path = null;
        if (job is not null)
        {
            npc.Job = job;
        }

        if (role is not null)
        {
            npc.Role = role;
        }
    }

    /// <summary>Test seam (#2593): whether an NPC is pausing to look at a player it just greeted.</summary>
    public bool NpcAttendingForTest(int npcId)
        => _npcs.FirstOrDefault(n => n.Id == npcId) is { } npc && _uptime < npc.AttendUntil;

    /// <summary>Test seam (#2593): adds a plain villager of <paramref name="settlement"/> standing at
    /// <paramref name="pos"/> (a crowd for the passing-greeting gap). Returns its id.</summary>
    public int AddNpcForTest(Vector3f pos, string settlement = "Testville", string role = "settler")
    {
        var npc = MakeNpc(role, "settlers", robotic: false, pos, new Random(_nextNpcId));
        npc.Settlement = settlement;
        npc.Leash = 0f; // stands where it was put
        _npcs.Add(npc);
        return npc.Id;
    }

    /// <summary>Test seam (#2596): drops an NPC from the world, as a world reload does (a base resident then respawns).</summary>
    public void RemoveNpcForTest(int npcId) => _npcs.RemoveAll(n => n.Id == npcId);
}
