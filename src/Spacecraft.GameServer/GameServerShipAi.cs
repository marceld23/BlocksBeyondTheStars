using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Configuration;
using Spacecraft.Shared.State;
using Spacecraft.Shared.World;

namespace Spacecraft.GameServer;

/// <summary>
/// The ship AI companion "VEGA" (Vectored Exploration &amp; Guidance Array): a per-player guide that
/// onboards new players through a staged first-hour chain, drops contextual advisor hints afterwards,
/// and grows into a game element — AI-core ship modules (scanner boost / threat callouts / autopilot /
/// evasion) and a memory-fragment story arc that gives wrecks and buried vaults narrative pull.
///
/// Design rules: server-authoritative (every milestone is an existing server event), strictly per-player
/// (each player has their own VEGA; lines go only to that session), bilingual + offline-safe (lines are
/// locale KEYS the client localizes — no LLM in the critical path), and skippable (settings hide hints,
/// <see cref="SkipOnboardingIntent"/> grants the whole chain; veteran saves auto-skip with one line).
/// </summary>
public sealed partial class GameServer
{
    // --- Onboarding stage chain. Order matters: the first incomplete stage is the active objective.
    // Completing a LATER stage early (e.g. trading before docking) is recorded silently; the chain then
    // skips it when reached. Milestone key per stage: "vega:stage:<id>".
    private static readonly (string Id, int Target)[] VegaStages =
    {
        ("mine",   3), // mine a few blocks (the starter drill)
        ("craft",  1), // craft anything
        ("scan",   1), // scan something (knowledge loop)
        ("unlock", 1), // unlock a first blueprint
        ("launch", 1), // lift off into space
        ("dock",   1), // board a space station
        ("trade",  1), // first trade OR accepted board mission
        ("land",   1), // land on a world again
    };

    private const string VegaIntroMilestone = "vega:intro";
    private const string VegaMemoryItem = "ai_memory_fragment";
    private const int VegaMemoryBeats = 10;          // story beats in the fragment arc
    private const int VegaMemoryKnowledge = 3;       // knowledge per restored fragment
    private const string VegaMk3Blueprint = "ai_core_mk3"; // granted when the arc completes
    private const double VegaThreatCooldown = 120.0; // s between threat callouts per player
    private const double VegaEvadeCooldown = 20.0;   // s between evasion callouts per player

    /// <summary>Deterministic roll source for Mk3 evasion (seeded so tests can reason about it).</summary>
    private readonly System.Random _vegaRng = new(7041);

    private static string VegaStageKey(string id) => "vega:stage:" + id;

    /// <summary>Index of the first incomplete onboarding stage, or VegaStages.Length when done.</summary>
    private static int VegaStageIndex(PlayerState p)
    {
        for (int i = 0; i < VegaStages.Length; i++)
        {
            if (!p.Milestones.Contains(VegaStageKey(VegaStages[i].Id)))
            {
                return i;
            }
        }

        return VegaStages.Length;
    }

    private static bool VegaOnboardingDone(PlayerState p) => VegaStageIndex(p) >= VegaStages.Length;

    /// <summary>A save that has clearly played before gets VEGA's onboarding pre-granted — they only hear
    /// a one-line "systems online". (Creative grants count too: creative worlds skip the tutorial.)</summary>
    private static bool VegaIsVeteran(PlayerState p)
        => p.KnowledgePoints > 0 || p.UnlockedBlueprints.Count > 0 || p.Missions.Count > 0;

    private void SendVegaLine(PlayerSession session, string lineKey, byte kind, string arg = "")
        => Send(session, new ShipAiLine
        {
            LineKey = lineKey,
            LineArg = arg,
            ObjectiveKey = VegaObjectiveKey(session.State),
            ObjectiveProgress = VegaObjectiveProgress(session),
            ObjectiveTarget = VegaObjectiveTarget(session.State),
            Kind = kind,
        });

    /// <summary>Objective-chip-only update (no new speech) — e.g. the mining counter ticking up.</summary>
    private void SendVegaObjective(PlayerSession session)
        => SendVegaLine(session, string.Empty, 0);

    private static string VegaObjectiveKey(PlayerState p)
    {
        int i = VegaStageIndex(p);
        return i < VegaStages.Length ? "vega.obj." + VegaStages[i].Id : string.Empty;
    }

    private static int VegaObjectiveTarget(PlayerState p)
    {
        int i = VegaStageIndex(p);
        return i < VegaStages.Length ? VegaStages[i].Target : 0;
    }

    private int VegaObjectiveProgress(PlayerSession session)
    {
        var p = session.State;
        int i = VegaStageIndex(p);
        return i < VegaStages.Length && VegaStages[i].Id == "mine" ? session.VegaMineCount : 0;
    }

    /// <summary>Join hook: boots VEGA for new players (intro + first objective), auto-grants the chain for
    /// veteran saves, and re-shows the current objective on a mid-onboarding rejoin.</summary>
    private void ShipAiOnJoin(PlayerSession session)
    {
        var p = session.State;
        if (!p.Milestones.Contains(VegaIntroMilestone))
        {
            p.Milestones.Add(VegaIntroMilestone);
            if (VegaIsVeteran(p))
            {
                foreach (var (id, _) in VegaStages)
                {
                    p.Milestones.Add(VegaStageKey(id));
                }

                SendVegaLine(session, "vega.veteran", 3);
            }
            else
            {
                SendVegaLine(session, "vega.intro.1", 0);
                SendVegaLine(session, "vega.intro.2", 0);
                SendVegaLine(session, "vega.s.mine.start", 0);
            }

            _repo.SavePlayer(p);
        }
        else if (!VegaOnboardingDone(p))
        {
            SendVegaLine(session, "vega.resume", 0);
        }
    }

    /// <summary>Marks an onboarding stage complete. If it was the ACTIVE stage, VEGA quips the completion
    /// and introduces the next one; later-stage events completed early are recorded silently.</summary>
    private void ShipAiComplete(PlayerSession session, string stageId)
    {
        var p = session.State;
        string key = VegaStageKey(stageId);
        if (p.Milestones.Contains(key))
        {
            return;
        }

        bool wasActive = VegaStageIndex(p) < VegaStages.Length && VegaStages[VegaStageIndex(p)].Id == stageId;
        p.Milestones.Add(key);
        _repo.SavePlayer(p);

        if (!wasActive)
        {
            return; // completed out of order — no fanfare, the chain will skip it later
        }

        SendVegaLine(session, $"vega.s.{stageId}.done", 0);
        int next = VegaStageIndex(p);
        if (next < VegaStages.Length)
        {
            SendVegaLine(session, $"vega.s.{VegaStages[next].Id}.start", 0);
        }
        else
        {
            SendVegaLine(session, "vega.done", 0); // send-off: the galaxy is yours (objective chip clears)
        }
    }

    /// <summary>The player skipped the tutorial (grant the whole chain) — or asked to RESTART it:
    /// wipe the stage milestones + intro flag and boot the chain again from the first lesson.</summary>
    private void HandleSkipOnboarding(PlayerSession session, SkipOnboardingIntent intent)
    {
        var p = session.State;
        if (intent.Restart)
        {
            p.Milestones.Remove(VegaIntroMilestone);
            foreach (var (id, _) in VegaStages)
            {
                p.Milestones.Remove(VegaStageKey(id));
            }

            session.VegaMineCount = 0;
            _repo.SavePlayer(p);
            ShipAiOnJoin(session); // re-runs the intro + first objective like a fresh player
            return;
        }

        p.Milestones.Add(VegaIntroMilestone);
        foreach (var (id, _) in VegaStages)
        {
            p.Milestones.Add(VegaStageKey(id));
        }

        _repo.SavePlayer(p);
        SendVegaLine(session, "vega.skip", 3);
    }

    // --- Stage hooks, called from the existing handlers (one line each at the call site). ---

    private void ShipAiOnMine(PlayerSession session)
    {
        var p = session.State;
        int i = VegaStageIndex(p);
        if (i >= VegaStages.Length || VegaStages[i].Id != "mine")
        {
            return;
        }

        session.VegaMineCount++;
        if (session.VegaMineCount >= VegaStages[i].Target)
        {
            ShipAiComplete(session, "mine");
        }
        else
        {
            SendVegaObjective(session);
        }
    }

    private void ShipAiOnCraft(PlayerSession session) => ShipAiComplete(session, "craft");
    private void ShipAiOnScan(PlayerSession session) => ShipAiComplete(session, "scan");
    private void ShipAiOnBlueprint(PlayerSession session) => ShipAiComplete(session, "unlock");
    private void ShipAiOnEnterSpace(PlayerSession session) => ShipAiComplete(session, "launch");
    private void ShipAiOnStationBoarded(PlayerSession session) => ShipAiComplete(session, "dock");
    private void ShipAiOnTradeOrMission(PlayerSession session) => ShipAiComplete(session, "trade");

    private void ShipAiOnTravelled(PlayerSession session)
    {
        // Only counts once they've actually been to space — the join placement also "travels".
        if (session.State.Milestones.Contains(VegaStageKey("launch")))
        {
            ShipAiComplete(session, "land");
        }

        ShipAiWorldFlavour(session);
    }

    // --- Advisor (Phase B): contextual once-per-save hints with a persisted once-flag. The client can
    // additionally mute them (settings) — these are teaching moments, not the HUD's live warnings. ---

    private void ShipAiHintOnce(PlayerSession session, string hintId, string arg = "")
    {
        var p = session.State;
        string key = "vega:hint:" + hintId;
        if (p.Milestones.Contains(key))
        {
            return;
        }

        p.Milestones.Add(key);
        _repo.SavePlayer(p);
        SendVegaLine(session, "vega.hint." + HintLineId(hintId), 1, arg);
    }

    /// <summary>Per-world-type flavour hints share one line per TYPE but a once-flag per type key.</summary>
    private static string HintLineId(string hintId)
        => hintId.StartsWith("world:", System.StringComparison.Ordinal) ? "world." + hintId["world:".Length..] : hintId;

    /// <summary>First-visit flavour for notable world types (called after a landing).</summary>
    private void ShipAiWorldFlavour(PlayerSession session)
    {
        string type = _world.Planet?.Key ?? string.Empty;
        string id = type switch
        {
            "asteroid" => "asteroid",
            "ocean" => "ocean",
            "corrupted" => "corrupted",
            "fungal" => "fungal",
            "ice" or "tundra" => "ice",
            "volcanic" or "ashen" => "volcanic",
            _ => string.Empty,
        };
        if (id.Length > 0)
        {
            ShipAiHintOnce(session, "world:" + id);
        }
    }

    /// <summary>Per-second advisor poll: vitals coaching, full inventory, nightfall, nearby ruins, and the
    /// memory-fragment redemption loop (VEGA reads fragments once the player is back aboard the ship).</summary>
    private void TickShipAi(double dt)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            session.VegaAdvisorAccum += dt;
            if (session.VegaAdvisorAccum < 1.0)
            {
                continue;
            }

            session.VegaAdvisorAccum = 0.0;
            var p = session.State;

            if (p.Oxygen < 25f)
            {
                ShipAiHintOnce(session, "o2");
            }

            if (p.SuitEnergy < 15f)
            {
                ShipAiHintOnce(session, "energy");
            }

            if (p.Hunger < 25f)
            {
                ShipAiHintOnce(session, "hunger");
            }

            if (p.Inventory.FirstEmptySlot() < 0)
            {
                ShipAiHintOnce(session, "invfull");
            }

            // First nightfall out on a surface (not aboard / docked) — warn about the dark.
            bool night = _dayFraction < 0.15 || _dayFraction > 0.85;
            if (night && !p.AboardShip && !InStation(p.PlayerId))
            {
                ShipAiHintOnce(session, "night");
            }

            // Ruins radar: vault entrances and the wreck give a one-time "structures detected" nudge.
            if (NearVegaPoi(p.Position))
            {
                ShipAiHintOnce(session, "poi");
            }

            TickVegaMemory(session);
            TickVegaBanterFor(session); // LLM smalltalk once onboarding is done (silent without AI)
        }
    }

    private bool NearVegaPoi(Spacecraft.Shared.Geometry.Vector3f pos)
    {
        const double r2 = 48.0 * 48.0;
        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            var v = _vaultEntrances[i];
            if (WrapDistSq(pos, new Spacecraft.Shared.Geometry.Vector3f(v.X, v.Y, v.Z)) <= r2)
            {
                return true;
            }
        }

        if (_wreckStamped)
        {
            var w = _wreckOrigin;
            if (WrapDistSq(pos, new Spacecraft.Shared.Geometry.Vector3f(w.X, w.Y, w.Z)) <= r2)
            {
                return true;
            }
        }

        return false;
    }

    // --- Memory fragments (Phase C story arc): found in wreck/vault data terminals and data caches,
    // redeemed one at a time when the player is back aboard the ship — VEGA "reads" them and recovers
    // a beat of her past. Completing the arc teaches the Mk3 core blueprint. ---

    private void TickVegaMemory(PlayerSession session)
    {
        var p = session.State;
        if (!p.AboardShip || _uptime < session.VegaMemoryReadyAt || !p.Inventory.Has(VegaMemoryItem, 1))
        {
            return;
        }

        int restored = p.Milestones.Count(m => m.StartsWith("vega:mem:", System.StringComparison.Ordinal));
        int beat = restored + 1;
        p.Inventory.Remove(VegaMemoryItem, 1);
        p.Milestones.Add("vega:mem:" + beat);
        p.KnowledgePoints += VegaMemoryKnowledge;
        session.VegaMemoryReadyAt = _uptime + 6.0; // space multiple fragments out so the lines can be read

        SendVegaLine(session, beat <= VegaMemoryBeats ? "vega.mem." + beat : "vega.mem.more", 2);
        if (beat == VegaMemoryBeats && p.UnlockedBlueprints.Add(VegaMk3Blueprint))
        {
            SendVegaLine(session, "vega.sys.mk3bp", 3);
        }

        SendInventory(session);
        _repo.SavePlayer(p);
    }

    // --- AI-core ship modules (Phase C abilities). Tier: 1 = bare VEGA, 2 = Mk2 core, 3 = Mk3 core. ---

    /// <summary>The AI-core tier of this player's ACTIVE ship (session-based — safe in any cursor context).</summary>
    private static int VegaCoreTier(PlayerSession session)
        => session.Ships.TryGetValue(session.ActiveShipId, out var ship)
            ? (ship.HasModule("ai_core_mk3") ? 3 : ship.HasModule("ai_core_mk2") ? 2 : 1)
            : 1;

    /// <summary>Mk2+: the AI core extends the handheld terrain scanner's reach (VEGA crunches the returns).</summary>
    private static int VegaScannerRadiusBonus(PlayerSession session) => VegaCoreTier(session) >= 2 ? 6 : 0;

    /// <summary>Mk2+: hostile contact callout in space, rate-limited per player.</summary>
    private void ShipAiThreatCallout(PlayerSession session)
    {
        if (VegaCoreTier(session) < 2 || _uptime < session.VegaThreatReadyAt)
        {
            return;
        }

        session.VegaThreatReadyAt = _uptime + VegaThreatCooldown;
        SendVegaLine(session, "vega.sys.threat", 3);
    }

    /// <summary>Mk3: evasive-manoeuvre roll — a chance to fully negate one incoming ship-damage event.
    /// Cursor-based on purpose: it guards <c>ApplyShipDamage</c>, which mutates the cursor ship.</summary>
    private bool VegaTryEvade() => (_ship.HasModule("ai_core_mk3") ? 3 : 1) >= 3 && _vegaRng.NextDouble() < 0.12;

    /// <summary>Callout after a successful Mk3 evade (rate-limited so a busy fight doesn't spam).</summary>
    private void ShipAiEvadeCallout(PlayerSession session)
    {
        if (_uptime < session.VegaEvadeReadyAt)
        {
            return;
        }

        session.VegaEvadeReadyAt = _uptime + VegaEvadeCooldown;
        SendVegaLine(session, "vega.sys.evade", 3);
    }

    /// <summary>Welcome line right after an AI core is built into the ship.</summary>
    private void ShipAiOnModuleBuilt(PlayerSession session, string moduleKey)
    {
        if (moduleKey == "ai_core_mk2")
        {
            SendVegaLine(session, "vega.sys.mk2", 3);
        }
        else if (moduleKey == "ai_core_mk3")
        {
            SendVegaLine(session, "vega.sys.mk3", 3);
        }
    }

    // --- LLM banter (the "L-stages" flavour path): rare, contextual smalltalk once onboarding is done.
    // Pure flavour on top of the scripted lines — generated off-thread through the same backend the NPC
    // greetings use (role "ship_ai"), cached per situation bucket, and silently absent when AI is off.

    private const double VegaBanterMinDelay = 420.0; // s between banter checks per player (~7 min)
    private const double VegaBanterMaxDelay = 720.0;

    /// <summary>Banter lines finished off-thread, waiting for the tick to send them.</summary>
    private readonly ConcurrentQueue<(int ConnectionId, ShipAiLine Line)> _vegaBanterOutbox = new();

    /// <summary>One banter line per situation bucket + locale (cost control, like the greeting cache).</summary>
    private readonly ConcurrentDictionary<string, string> _vegaBanterCache = new();

    private readonly ConcurrentDictionary<string, byte> _vegaBanterInFlight = new();

    /// <summary>The compact situation line VEGA comments on (world, day phase, story progress).</summary>
    private string VegaSituation(PlayerSession session)
    {
        var p = session.State;
        string world = _world.Planet?.Key ?? "space";
        string phase = _dayFraction is < 0.15 or > 0.85 ? "night" : _dayFraction is < 0.3 or > 0.7 ? "twilight" : "day";
        int fragments = p.Milestones.Count(m => m.StartsWith("vega:mem:", System.StringComparison.Ordinal));
        string aboard = p.AboardShip ? "aboard the ship" : "on foot";
        return $"on a {world} world, {phase}, pilot is {aboard}, {fragments}/10 of VEGA's memory restored";
    }

    /// <summary>Situation bucket for the banter cache — coarse on purpose so lines get reused.</summary>
    private string VegaBanterKey(PlayerSession session)
    {
        string world = _world.Planet?.Key ?? "space";
        bool night = _dayFraction is < 0.15 or > 0.85;
        return $"banter|{world}|{(night ? "night" : "day")}|{session.Locale}";
    }

    /// <summary>Rolls the per-player banter timer; when due (and AI is on, onboarding done), serves a
    /// cached line or generates one off-thread. Called from <see cref="TickShipAi"/>'s 1 Hz path.</summary>
    private void TickVegaBanterFor(PlayerSession session)
    {
        if (_config.AiLevel == AiLevel.Off || !VegaOnboardingDone(session.State))
        {
            return;
        }

        if (session.VegaBanterNextAt <= 0.0)
        {
            // First arm after join: don't banter into the first minutes of a session.
            session.VegaBanterNextAt = _uptime + VegaBanterMinDelay * 0.75;
            return;
        }

        if (_uptime < session.VegaBanterNextAt)
        {
            return;
        }

        session.VegaBanterNextAt = _uptime + VegaBanterMinDelay
            + _vegaRng.NextDouble() * (VegaBanterMaxDelay - VegaBanterMinDelay);

        string cacheKey = VegaBanterKey(session);
        if (_vegaBanterCache.TryGetValue(cacheKey, out var cached))
        {
            SendVegaText(session, cached);
            return;
        }

        if (!_vegaBanterInFlight.TryAdd(cacheKey, 1))
        {
            return;
        }

        var req = VegaBanterRequest(session);
        int connId = session.ConnectionId;
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                var line = _ai.GenerateNpcLine(req);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _vegaBanterCache[cacheKey] = line!;
                    _vegaBanterOutbox.Enqueue((connId, BuildVegaTextLine(line!)));
                }
            }
            catch
            {
                // flavour only — silence is the offline behaviour anyway
            }
            finally
            {
                _vegaBanterInFlight.TryRemove(cacheKey, out _);
            }
        });
    }

    /// <summary>The ship-AI line request — same backend + contract as NPC greetings, role "ship_ai".</summary>
    private NpcLineRequest VegaBanterRequest(PlayerSession session)
    {
        var p = session.State;
        return new NpcLineRequest
        {
            NpcName = "VEGA",
            Role = "ship_ai",
            IsRobot = true,
            PlayerName = p.Name,
            Relationship = System.Math.Min(100, p.Milestones.Count * 4), // standing grows with shared history
            PastInteractions = p.Milestones.Count,
            Language = session.Locale,
            Persona = "dry, laconic ship AI with deadpan humour; loyal; an old fleet navigation core",
            RecentEvents = string.Empty,
            Situation = VegaSituation(session),
        };
    }

    private static ShipAiLine BuildVegaTextLine(string text)
        => new() { Text = text, Kind = 1 }; // advisor channel: the settings mute applies to banter too

    /// <summary>Sends an LLM banter text directly (keeps the current objective chip fields).</summary>
    private void SendVegaText(PlayerSession session, string text)
        => Send(session, new ShipAiLine
        {
            Text = text,
            ObjectiveKey = VegaObjectiveKey(session.State),
            ObjectiveProgress = VegaObjectiveProgress(session),
            ObjectiveTarget = VegaObjectiveTarget(session.State),
            Kind = 1,
        });

    /// <summary>Drains banter lines finished off-thread. Called once per server tick.</summary>
    private void TickVegaBanter()
    {
        while (_vegaBanterOutbox.TryDequeue(out var pending))
        {
            if (_sessions.TryGetValue(pending.ConnectionId, out var session) && session.Joined)
            {
                SendVegaText(session, pending.Line.Text);
            }
        }
    }

    /// <summary>Test seam: synchronously resolves the banter line VEGA would say right now — the same
    /// request/provider/cache path the off-thread flow uses. Null when AI is off or the provider declines.</summary>
    public string? VegaBanterForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || _config.AiLevel == AiLevel.Off)
        {
            return null;
        }

        string cacheKey = VegaBanterKey(session);
        if (_vegaBanterCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var line = _ai.GenerateNpcLine(VegaBanterRequest(session));
        if (!string.IsNullOrWhiteSpace(line))
        {
            _vegaBanterCache[cacheKey] = line!;
            return line;
        }

        return null;
    }

    /// <summary>Test seam: the player's milestone set (onboarding stages, hints, memory beats).</summary>
    public IReadOnlyCollection<string> MilestonesForTest(string playerId)
        => FindSessionByPlayerId(playerId)?.State.Milestones ?? (IReadOnlyCollection<string>)System.Array.Empty<string>();
}
