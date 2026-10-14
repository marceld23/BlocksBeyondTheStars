// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Configuration;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The ship AI companion "VEGA" (Vectored Exploration &amp; Guidance Array): a per-player voice that presents the
/// basic training (since #2469 the tutorial story pack, <c>data/stories/tutorial/</c>, run by the player-pack runtime —
/// the hard-coded nine-stage onboarding chain is gone), drops contextual advisor hints, and grows into a game element —
/// AI-core ship modules (scanner boost / threat callouts / autopilot / evasion) and a memory-fragment story arc that
/// gives wrecks and buried vaults narrative pull.
///
/// Design rules: server-authoritative (every milestone is an existing server event), strictly per-player
/// (each player has their own VEGA; lines go only to that session), bilingual + offline-safe (lines are
/// locale KEYS the client localizes — no LLM in the critical path), and skippable (settings hide hints,
/// <see cref="SkipOnboardingIntent"/> skips the basic training; veterans are migrated silently).
/// </summary>
public sealed partial class GameServer
{
    private const string VegaMemoryItem = "ai_memory_fragment";
    private const int VegaMemoryBeats = 10;          // story beats in the fragment arc
    private const int VegaMemoryKnowledge = 3;       // knowledge per restored fragment
    private const string VegaMk3Blueprint = "ai_core_mk3"; // granted when the arc completes
    private const double VegaThreatCooldown = 120.0; // s between threat callouts per player
    private const double VegaEvadeCooldown = 20.0;   // s between evasion callouts per player

    /// <summary>Deterministic roll source for Mk3 evasion (seeded so tests can reason about it).</summary>
    private readonly System.Random _vegaRng = new(7041);

    /// <summary>Whether this world pre-grants blueprints (singleplayer Creative option or Creative mode). Reads
    /// the SAVED world option (#2460, B04): the launch flag is only set by the session that created the world —
    /// a relaunched head-start world came back without it, and its newcomers were read as veterans (#1461).</summary>
    private bool CreativeGrantsActive
        => _meta.CreativeUnlockAllBlueprints || Rules.GameMode == GameMode.Creative;

    /// <summary>#2460 (B01): the player as they arrive — taken at the very top of the join, before
    /// <c>SetupPlayerShip</c>, the Creative grants and the story catch-up can pay anything into the state. "Veteran" is the
    /// evidence of earlier play the state carried (knowledge, blueprints, missions) on a world that does not hand
    /// blueprints out — a Creative world never counts (#1461: its join grants read as experience, and a school browser
    /// session is a first session every time). Since #2469 nothing decides the tutorial from it alone: the tutorial's
    /// one-time migration reads it only for a player without any onboarding state (a save from before VEGA's lessons),
    /// and the prologue plays only for a newcomer.</summary>
    private JoinSnapshot TakeJoinSnapshot(PlayerState p)
        => new(!CreativeGrantsActive && (p.KnowledgePoints > 0 || p.UnlockedBlueprints.Count > 0 || p.Missions.Count > 0),
            p.Milestones.Contains(StoryLegacy.IntroMilestone),
            p.Milestones.Where(m => m.StartsWith("story:", System.StringComparison.Ordinal)));

    /// <summary>The objective chip as last sent, per connection (#1859): key, {0} argument, progress, target and — since
    /// #2462 — its source and whether the tutorial runs.
    /// <see cref="RefreshVegaObjective"/> compares the live objective against this once a second and re-sends on
    /// any change — before, the chip was only refreshed on landing / join / a story beat, so "a net fragment
    /// lies on this world" survived the launch, the station and the pickup. A connection id may be reused
    /// after a disconnect; the worst case is one redundant chip refresh, so no cleanup hook is needed.</summary>
    private readonly Dictionary<int, VegaChip> _vegaObjectiveSent = new();

    /// <summary>The objective chip's fields as one comparable value (#2462; the pack of a pack objective since #2466; the
    /// world story's id on its own chip and the step behind a pack objective since #2471).</summary>
    private readonly record struct VegaChip(string Key, string Arg, int Progress, int Target, string Source, bool TutorialActive, string PackId = "", string StepId = "");

    private void SendVegaLine(PlayerSession session, string lineKey, byte kind, string arg = "")
    {
        var line = new ShipAiLine
        {
            LineKey = lineKey,
            LineArg = arg,
            Kind = kind,
        };
        SendVegaWithObjective(session, line);
    }

    /// <summary>Fills the objective chip of a VEGA line from the live objective, remembers it for the 1 Hz refresh and
    /// sends the line. Every line carries the chip, so this is the one place the chip — and its source — is set. A line
    /// that a cutscene still waiting in the player's queue shows itself waits with that scene (#2473).</summary>
    private void SendVegaWithObjective(PlayerSession session, ShipAiLine line)
    {
        if (HoldLineForQueuedCutscene(session, line))
        {
            return; // #2473: it goes right after the scene that shows it (GameServerCutscenes.SendQueuedCutscene)
        }

        var chip = LiveVegaChip(session);
        line.ObjectiveKey = chip.Key;
        line.ObjectiveArg = chip.Arg;
        line.ObjectiveProgress = chip.Progress;
        line.ObjectiveTarget = chip.Target;
        line.ObjectiveSource = chip.Source;
        line.TutorialActive = chip.TutorialActive;
        line.ObjectivePackId = chip.PackId;
        _vegaObjectiveSent[session.ConnectionId] = chip;
        Send(session, line);
        MarkJournalDirty(session); // #2471: whatever moved the chip or made VEGA speak may change the Journal too
    }

    /// <summary>
    /// The objective chip this player should see right now — the chip order of synthesis §5.3 (#2466), first match wins:
    /// <list type="number">
    /// <item>the finale while the player is in the Guardian system (it beats even a pin — the #1832 lesson);</item>
    /// <item>the pinned story (<c>PlayerState.TrackedStoryId</c>), if it has an objective;</item>
    /// <item>basic training: the tutorial pack's mandatory chapters (#2469 — the old onboarding chain is gone);</item>
    /// <item>the VEGA finale (Guardian system revealed) — once the player's story track has started (#2470);</item>
    /// <item>the VEGA lead — likewise (#2475: the chapter lead as <c>story.vega.obj.*</c>; an older client <c>story.obj.*</c>);</item>
    /// <item>a tutorial deep-dive step or a mission-bridge step;</item>
    /// <item>the other player packs by priority;</item>
    /// <item>nothing.</item>
    /// </list>
    /// An older client (no <c>packs1</c>) sees pack objectives only as a tutorial step's old <c>vega.obj.&lt;stage&gt;</c>.
    /// </summary>
    private VegaChip LiveVegaChip(PlayerSession session)
    {
        bool tutorialActive = TutorialPackRunning(session);
        foreach (var chip in ChipCandidates(session, tutorialActive))
        {
            return chip; // the first match of the order (lazy: the later ranks are not evaluated)
        }

        return new VegaChip(string.Empty, string.Empty, 0, 0, ObjectiveSources.None, tutorialActive);
    }

    /// <summary>The player's objectives in the chip order of synthesis §5.3 (see <see cref="LiveVegaChip"/>): the first is
    /// the chip; the Journal's <see cref="ObjectiveList"/> takes the first row of each story from the rest (#2471). A story
    /// may come up more than once (a pin repeats its own rank). Lazy, so the chip alone costs what it always did.</summary>
    private IEnumerable<VegaChip> ChipCandidates(PlayerSession session, bool tutorialActive)
    {
        var p = session.State;
        if (FinaleObjectiveOverridesTutorial(p))
        {
            yield return StoryChip(session, tutorialActive);
        }

        if (p.TrackedStoryId.Length > 0 && PinnedChip(session, tutorialActive) is { } pinned)
        {
            yield return pinned;
        }

        if (BasicTrainingObjective(session) is { } basic)
        {
            yield return PackChip(basic, tutorialActive);
        }

        // Ranks 4/5 only once the player's story track has started (#2470): before that the story is held for them — the
        // chip never shows the story during the prologue, a read step's line or the gap between two lessons. Rank 5 is the
        // chapter lead since #2475 (the finale is the core chapter's lead).
        if (StoryTrackStarted(session) && StoryChip(session, tutorialActive) is { Key.Length: > 0 } story)
        {
            yield return story;
        }

        if (DeepDiveObjective(session) is { } deepDive)
        {
            yield return PackChip(deepDive, tutorialActive);
        }

        // Rank 7: the other player packs by priority (the chip takes the first; the list goes on to the others).
        foreach (var pack in PlayerPacks)
        {
            if (!pack.IsTutorial && PackObjectiveOf(session, pack) is { } other)
            {
                yield return PackChip(other, tutorialActive);
            }
        }
    }

    /// <summary>The world story's chip (#1110): the finale or the current lead (#2475: the chapter lead, <see cref="StoryObjectiveFor"/>
    /// — an older client keeps the old keys), the way to the next beat as its counter (#2537, <see cref="StoryEngine.NextBeatProgress"/>
    /// over the clamped <see cref="StoryProgressShown"/>; hidden once every beat is heard — the journal's meter keeps the
    /// whole arc). Carries the story's id as its pack (#2471: the client labels it with the story's short name, "VEGA: …").</summary>
    private VegaChip StoryChip(PlayerSession session, bool tutorialActive)
    {
        var (key, arg, source) = StoryObjectiveFor(session);
        if (key.Length == 0)
        {
            return new VegaChip(string.Empty, string.Empty, 0, 0, ObjectiveSources.None, tutorialActive);
        }

        // #2537: the arc's score next to one task ("Find the net fragment (8/204)") read as "8 of 204 fragments".
        var (progress, target) = _story is null ? (0, 0) : StoryEngine.NextBeatProgress(_story, _storyState, StoryProgressShown());
        return new VegaChip(key, arg, progress, target, source, tutorialActive, _story?.Id ?? string.Empty);
    }

    /// <summary>The pinned story's chip, or null when it has no objective now (the order then goes on).</summary>
    private VegaChip? PinnedChip(PlayerSession session, bool tutorialActive)
    {
        string pinned = session.State.TrackedStoryId;
        if (StoryActive && _story is not null && string.Equals(_story.Id, pinned, System.StringComparison.OrdinalIgnoreCase))
        {
            return StoryTrackStarted(session) && StoryChip(session, tutorialActive) is { Key.Length: > 0 } story ? story : null;
        }

        return TryGetPlayerPack(pinned, out var pack) && PackObjectiveOf(session, pack) is { } objective
            ? PackChip(objective, tutorialActive)
            : null;
    }

    private static VegaChip PackChip(PackObjective objective, bool tutorialActive)
        => new(objective.Key, string.Empty, objective.Progress, objective.Target, objective.Source, tutorialActive, objective.PackId, objective.StepId);

    /// <summary>Objective-chip-only update (no new speech) — e.g. the mining counter ticking up.</summary>
    private void SendVegaObjective(PlayerSession session)
        => SendVegaLine(session, string.Empty, 0);

    /// <summary>1 Hz per player (from <see cref="TickShipAi"/>): re-sends the chip whenever the live objective
    /// differs from the one last sent (#1859) — so launching, boarding a station, quick-travelling and picking
    /// up the fragment all move the chip on without a dedicated hook at each site. While the fragment objective
    /// is up on the player's current world, the fragment signal is also put on the map unconditionally: the
    /// chip says "follow VEGA's signal", so there has to be one — before, the marker only appeared once the
    /// tip-gated <c>fragment_signal</c> hint happened to win a cadence slot.</summary>
    private void RefreshVegaObjective(PlayerSession session)
    {
        var live = LiveVegaChip(session);
        if (!_vegaObjectiveSent.TryGetValue(session.ConnectionId, out var sent) || sent != live)
        {
            SendVegaObjective(session);
        }

        if (IsFragmentObjective(live.Key))
        {
            RevealFragmentSignals();
        }
    }

    /// <summary>Marks every unread net fragment of the active world on the map (persisted reveal, broadcast to
    /// the world's players), once per fragment. Same reveal key the <c>fragment_signal</c> tip uses, so the two
    /// paths never double-mark; the pickup removes the fragment and the marker drops off on its own.</summary>
    private void RevealFragmentSignals()
    {
        foreach (var f in _netFragments)
        {
            string revealKey = _worlds.Active.LocationId + "|frag:" + f.Key;
            if (!_meta.RevealedPois.Contains(revealKey))
            {
                RevealPoi(revealKey);
            }
        }
    }

    /// <summary>Display name of the active world when it is a galaxy body ("Kepler-3 b"); empty for a location
    /// the galaxy does not know (<c>LocationNamesFor</c> then falls back to the planet TYPE id, which would read
    /// as "a net fragment lies on rocky" — the unnamed <c>fragment_here</c> wording is the better chip there).</summary>
    private string FragmentWorldName()
    {
        var (system, planet) = LocationNamesFor(_worlds.Active.LocationId);
        return system.Length > 0 ? planet : string.Empty;
    }

    /// <summary>Whether the finale is open: the Guardian system is on the map and its core not yet won.</summary>
    private bool FinaleOpen
        => StoryActive && _story is not null && _storyState.GuardianSystemRevealed && !_storyState.GuardianDefeated;

    /// <summary>#1832: once the Guardian system is revealed the finale IS the objective — the tutorial chip
    /// ("craft an item") must not sit over it while the player stands in the core chamber. The steps still
    /// complete in the background. Since #2462 (B11) only there: in the Guardian system (the core world is one of
    /// its bodies). The override used to be world-wide, so a newcomer joining a world whose finale was open got
    /// "confront the Guardian core" on the chip in their first minute while the lessons kept talking about mining.
    /// Elsewhere the running tutorial keeps the chip; once it is done the story objective is the finale anyway.</summary>
    private bool FinaleObjectiveOverridesTutorial(PlayerState p)
        => FinaleOpen && IsGuardianSystemLocation(p.CurrentLocationId);

    private const string StoryFinaleObjectiveKey = "story.obj.finale";

    /// <summary>The post-tutorial story objective on the HUD chip (#1110): once VEGA's onboarding is done the
    /// chip no longer goes blank — it says what the story wants next. Keys use the <c>story.obj.</c> prefix so
    /// the client can gate them behind the hints setting (the tutorial chip always shows). Empty when no story
    /// is active or the finale is won (the chip then clears, as before). Since #2475 this is the legacy objective — an
    /// older client (no <c>packs1</c>) and a story without chapter leads; a current client reads the chapter leads
    /// (<see cref="StoryObjectiveFor"/>). Its keys stay unchanged.</summary>
    private string StoryObjectiveKey(PlayerState p)
    {
        if (!StoryActive || _story is null || _storyState.GuardianDefeated)
        {
            return string.Empty;
        }

        if (_storyState.GuardianSystemRevealed)
        {
            return StoryFinaleObjectiveKey; // confront the Guardian core
        }

        if (p.CurrentLocationId == _worlds.Active.LocationId && _netFragments.Count > 0)
        {
            // A net fragment is on the player's world — follow the signal. Named after the body (#1859) so the
            // chip still reads right from orbit or a station above it ("lies on Kepler-3 b", not "on this
            // world"); the unnamed wording stays for a location the galaxy has no name for.
            return FragmentWorldName().Length > 0 ? "story.obj.fragment_on" : "story.obj.fragment_here";
        }

        if (_story.Fragments.Any(f => !_storyState.FoundFragmentKeys.Contains(f.Key)))
        {
            return "story.obj.search"; // ruins, wrecks and terminals still hold fragments somewhere
        }

        // Fragments done — settlements, systems and kills carry the rest. A peaceful world has no machines to
        // drive back (#2460, N14), so its chip names only what the world actually offers.
        return StoryMachinesActive ? "story.obj.help" : "story.obj.help_peaceful";
    }

    /// <summary>Whether this world's rules field any Guardian machines a kill could count against: walking robots
    /// and scan-drones on the surface, or drones and UFOs in space (with space combat on).</summary>
    private bool StoryMachinesActive
        => PlanetEnemiesActive
           || (Rules.SpaceCombat is SpaceCombatMode.PvE or SpaceCombatMode.Both
               && (Rules.SpaceNpcEnemies != AlienActivity.Off || Rules.AlienUfos != AlienActivity.Off));

    /// <summary>Test seam: the objective-chip key this player would be sent right now (#1110).</summary>
    public string? ObjectiveKeyForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session ? LiveVegaChip(session).Key : null;

    /// <summary>Test seam: the objective chip's {0} argument for this player right now (#1859) — the body name
    /// while the fragment objective is up, empty otherwise.</summary>
    public string? ObjectiveArgForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session ? LiveVegaChip(session).Arg : null;

    /// <summary>Test seam: the objective chip's source for this player right now (#2462).</summary>
    public string? ObjectiveSourceForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session ? LiveVegaChip(session).Source : null;

    /// <summary>Test seam: the pack whose objective is on the chip for this player right now (#2466), empty for none.</summary>
    public string? ObjectivePackForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session ? LiveVegaChip(session).PackId : null;

    /// <summary>Test seam: the objective chip's counter for this player right now (#2537); (0, 0) = no counter.</summary>
    public (int Progress, int Target) ObjectiveCounterForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } session && LiveVegaChip(session) is var chip ? (chip.Progress, chip.Target) : (0, 0);

    /// <summary>Join hook: the context tips stay quiet for a moment, then the chip. The cold open — VEGA's prologue — is the
    /// world story's intro cutscene since #2470 and plays earlier in the join (<see cref="StoryOnJoin"/>, before the beat
    /// catch-up); the basic training is the tutorial pack (<see cref="StoryPacksOnJoin"/>, right after). A tutorial restart
    /// never comes here, so it never replays the prologue (E17).</summary>
    private void ShipAiOnJoin(PlayerSession session)
    {
        session.VegaTipReadyAt = _uptime + VegaTipJoinQuiet; // context tips (#1077) stay quiet right after a join
        SendVegaObjective(session); // the chip survives a rejoin (#1110)
    }

    /// <summary>The join snapshot behind the client's "VEGA tips" log (#737): every persisted vega:* milestone, and since
    /// #2469 the tutorial pack's done steps (<c>story:&lt;pack&gt;:step:&lt;id&gt;</c>, not the skipped ones) — the client maps
    /// them to locale keys through its copy of the pack, so a lesson stays re-readable. An older client ignores the
    /// <c>story:</c> keys.</summary>
    private void SendVegaJournal(PlayerSession session)
    {
        var tutorialSteps = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var pack in PlayerPacks)
        {
            if (pack.IsTutorial)
            {
                foreach (var step in pack.StepsById.Values)
                {
                    tutorialSteps.Add(StoryPackKeys.StepDone(pack.Id, step.Id));
                }
            }
        }

        Send(session, new VegaJournal
        {
            Milestones = session.State.Milestones
                .Where(m => m.StartsWith("vega:", System.StringComparison.Ordinal) || tutorialSteps.Contains(m))
                .OrderBy(m => m, System.StringComparer.Ordinal)
                .ToArray(),
        });
    }

    /// <summary>The Settings button (<see cref="SkipOnboardingIntent"/>, #2469): skip = skip the tutorial pack's mandatory
    /// chapters ("I know the game" — nothing granted, deep-dive offers stay on) with VEGA's confirmation; restart = replay
    /// them from part 1 (keys stay; no veteran check, never the prologue — E17).</summary>
    private void HandleSkipOnboarding(PlayerSession session, SkipOnboardingIntent intent)
    {
        SkipOrRestartTutorialPacks(session, intent.Restart);
        if (!intent.Restart)
        {
            SendVegaLine(session, "vega.skip", 3);
        }

        SendVegaJournal(session); // the log follows at once, not at the next join
    }

    /// <summary>After a landing: the world-type flavour hints (the old "land" lesson is a tutorial step now, #2469).</summary>
    private void ShipAiOnTravelled(PlayerSession session) => ShipAiWorldFlavour(session);

    // --- Advisor (Phase B): contextual once-per-save hints with a persisted once-flag. The client can
    // additionally mute them (settings) — these are teaching moments, not the HUD's live warnings. ---

    /// <summary>Speaks a once-per-save hint. <paramref name="kind"/> 1 is the advisor channel the client mutes with
    /// the hints setting; a line the story cannot do without passes 3 (system — never muted). A muted player's
    /// advisor hint is held back WITHOUT burning its once-flag (#2462, B31): it used to go out, be dropped by the
    /// muted panel and never come again, so switching the hints back on found every lesson already spent.</summary>
    private void ShipAiHintOnce(PlayerSession session, string hintId, string arg = "", byte kind = 1)
    {
        var p = session.State;
        string key = "vega:hint:" + hintId;
        if (p.Milestones.Contains(key) || HintMutedFor(session, kind) || HintHeldByTutorial(session, hintId))
        {
            return; // told — muted (kept for later, #2462) — or a tutorial step teaches it (#2469, G12)
        }

        p.Milestones.Add(key);
        _repo.SavePlayer(p);
        if (OfferTellsHint(session, hintId))
        {
            return; // #2459: told by the course offer due at this very moment (the comm radio's Crystal Net line, the bio lab …)
        }

        SendVegaLine(session, "vega.hint." + HintLineId(hintId), kind, arg);
    }

    /// <summary>Whether a line of this kind would only reach a muted panel (#2462, B31): the advisor hints (1) and the
    /// repeated context tips (5) follow the client's VEGA hints switch; every other kind always shows.</summary>
    private static bool HintMutedFor(PlayerSession session, byte kind)
        => session.VegaHintsMuted && (kind is 1 or VegaTipRepeatKind);

    /// <summary>Per-world-type flavour hints share one line per TYPE but a once-flag per type key; a toxic world's scan
    /// (#2032) shares one line per hazard combination but a once-flag per WORLD ("toxic:&lt;location&gt;:&lt;scan&gt;").</summary>
    private static string HintLineId(string hintId)
        => hintId.StartsWith("world:", System.StringComparison.Ordinal) ? "world." + hintId["world:".Length..]
            : hintId.StartsWith(ToxicScanHintPrefix, System.StringComparison.Ordinal) ? "toxic_scan_" + hintId[(hintId.LastIndexOf(':') + 1)..]
            : hintId;

    /// <summary>First-visit flavour for notable world types (called after a landing).</summary>
    private void ShipAiWorldFlavour(PlayerSession session)
    {
        string type = _world.Planet?.Key ?? string.Empty;
        // Every asteroid FAMILY (stony, metallic, icy, carbonaceous, crystalline — #515) shares the one
        // "you're standing on a rock in the void" line, and the once-flag with it.
        string id = WorldConstants.IsAsteroidType(type) ? "asteroid" : type switch
        {
            "ocean" => "ocean",
            "corrupted" => "corrupted",
            "fungal" => "fungal",
            "ice" or "tundra" => "ice",
            "volcanic" or "ashen" => "volcanic",
            "gds_desert" => "gds", // #1793: the lava desert with the one guarded city
            "titas" => "titas", // 2026-09: Justus' frozen planet
            "sand_sea" => "sand_sea", // #2018 follow-up: the dunes carry every step — walk them and the worm comes (Marcel, 2026-09-26)
            "toxica_maxima" => "toxica_maxima", // #2068: Justus' poisoned landmark
            "arena_nigra" => "arena_nigra", // #2078: Theo's black-sand landmark — the Ignivermis hunts here
            "gas_giant" => "gas_giant", // #2112: Justus' gas giant — no ground, only gas under the islands
            "crystal" or "crystal_living" => "crystal", // #2257: crystal everywhere — enough conduit for a whole base
            _ => string.Empty,
        };
        if (id.Length > 0)
        {
            ShipAiHintOnce(session, "world:" + id);
        }

        ShipAiToxicScan(session); // #2032: a toxic world reports ITS rolled hazards, once per world

        // Parked on the sea floor (#1455): the shaft is dry, the walls are not to be mined, and the way off
        // the planet is E at the cockpit — the one landing a first-time player cannot read on their own.
        if (PlayerPad(session).Molten)
        {
            // Only when every other pad was taken (old saves keep their lava pads): the walls are lava.
            ShipAiHintOnce(session, "lava_pad");
        }
        else if (PlayerPad(session).Wet)
        {
            ShipAiHintOnce(session, "seabed");
        }

        // The finale body (#1792): the core chamber sits under ONE iron-ringed aperture, thousands of blocks from
        // most pads — a player who dug 50 blocks straight down under the ship never had a chance. Point at the map
        // marker once, on landing. Sent on the never-muted system channel (#2460): it is the finale's only "walk
        // there, do not dig here", and a player with hints off used to lose it for good — its flag burned unseen.
        // The VEGA pack's line (#2470): silent while no story runs (a chamber left from an earlier story in a "none" world).
        if (StoryActive && _worlds.Active.HasCoreChamber)
        {
            // #2478: the descent set piece shows this very hint (its replaceLineKeys) over the shaft — committed first, so a
            // client that plays it keeps the line out of the panel (and gets it back on an early skip). Not while the duel
            // already runs (whoever lands then goes straight to the core's dialogue).
            if (FinaleOpen && !_coreHacked && !session.Spectating)
            {
                PlayFinaleCutscene(session, StoryFinaleStages.Descent, CoreShaftAnchor());
            }

            ShipAiHintOnce(session, "guardian_core", kind: 3);
        }
    }

    /// <summary>Per-second advisor poll: vitals coaching, full inventory, nightfall, nearby ruins, and the
    /// memory-fragment redemption loop (VEGA reads fragments once the player is back aboard the ship).</summary>
    private void TickShipAi(double dt)
    {
        foreach (var session in JoinedInActiveWorld())
        {
            if (session.Spectating)
            {
                continue; // an invisible observer is staff, not a pilot: no hints, tips, banter or redemptions (#2460, N1)
            }

            session.VegaAdvisorAccum += dt;
            if (session.VegaAdvisorAccum < 1.0)
            {
                continue;
            }

            session.VegaAdvisorAccum = 0.0;
            var p = session.State;

            TickStoryPass(session); // #2477: the same pass reaches the players outside a loaded world (TickStoryOutsideWorlds)

            // Vitals coaching (o2 / energy / hunger / cold / heat) moved to the throttled context tips
            // (#1082): the first occurrence still behaves like the old once-hint, later ones repeat with
            // a long cooldown — see GameServerShipAiContext.cs.

            // New players never notice the oxygen system on the deliberately breathable start world — the bar
            // just sits full and the < 25% warning never fires. Teach it once, the first time they're outside
            // on foot breathing free air, so the mechanic isn't a nasty surprise off-world (Severin playtest).
            if (AtmosphereBreathable && !p.AboardShip && !p.InEva && !ShipInteriorContains(p.Position) && !InStation(p.PlayerId))
            {
                ShipAiHintOnce(session, "breathe");
            }

            // Aboard with a less-than-full belly: teach that life support tops hunger up — but only slowly, so
            // eating is still the fast route. Fires once, and only while actually aboard/docked and noticeably hungry.
            if ((p.AboardShip || InStation(p.PlayerId)) && p.Hunger < 70f)
            {
                ShipAiHintOnce(session, "shipfood");
            }

            if (p.Inventory.FirstEmptySlot() < 0)
            {
                RaiseGameEvent(session, GameEventKeys.InventoryFull); // #2469: the tutorial's cargo lesson — before the hint it absorbs
                ShipAiHintOnce(session, "invfull");
            }

            TutorialStateEvents(session); // #2469: walked back to the ship, a data fragment carried — only while a step waits

            // First nightfall out on a surface (not aboard / docked) — warn about the dark.
            bool night = LocalDayFraction(session.State.Position) is < 0.15 or > 0.85; // #1865: the local sun
            if (night && !p.AboardShip && !InStation(p.PlayerId))
            {
                ShipAiHintOnce(session, "night");
            }

            // Ruins radar: vault entrances and the wreck give a one-time "structures detected" nudge.
            if (NearVegaPoi(p.Position))
            {
                RaiseGameEvent(session, GameEventKeys.PoiReached); // #2482: the explorer course — before the hint it absorbs
                ShipAiHintOnce(session, "poi");
            }

            TickVegaMemory(session);
            TickVegaContextTips(session); // throttled situational tips (#1077): lamp, ore, places, space …
            TickVegaBanterFor(session); // LLM smalltalk once onboarding is done (silent without AI)
        }
    }

    /// <summary>
    /// The 1 Hz story pass of one joined, non-observing player: the player packs (start, advance, offers, help escalation),
    /// the world story's start rule, the chapter leads (evidence, the heal tank, the marker), VEGA's story missions (hand-out,
    /// letting go, evidence, re-checks) and the objective chip. Run by <see cref="TickShipAi"/> for the players of the world
    /// being ticked (the active cursor is theirs — a marker on their world needs it) and, since #2477, by
    /// <see cref="TickStoryOutsideWorlds"/> for everyone else. Queued cutscenes are flushed by <c>TickCutscenes</c> (#2468).
    /// </summary>
    private void TickStoryPass(PlayerSession session)
    {
        TickStoryPacksFor(session); // #2466: player packs — start, advance, offers, help escalation (active play time)
        TryStartStoryTrack(session); // #2470: the world story's start rule (also with no player pack installed)
        TickStoryLeads(session); // #2475: leads followed by evidence (research, module, a site, the heal tank) + the marker
        TickStoryMissions(session); // #2476: VEGA's story missions — handed out, let go, followed by evidence, help escalation
        RefreshVegaObjective(session); // #1859: the chip follows launches, stations, pickups and travel
        SyncStoryMarkers(session); // #2570: a step done or skipped takes its help marker off the map and the compass
    }

    /// <summary>The worlds the per-world loop ticks this server tick (filled by <see cref="Tick"/>): their players get the story
    /// pass from <see cref="TickShipAi"/>, everyone else from <see cref="TickStoryOutsideWorlds"/>.</summary>
    private readonly HashSet<string> _storyTickedWorlds = new(System.StringComparer.Ordinal);

    /// <summary>
    /// #2477: the story pass for the joined, non-observing players no loaded world reaches — a pilot cruising after an
    /// in-flight hyperjump occupies a body whose world was never loaded, so <see cref="TickShipAi"/> (per loaded world) never
    /// saw them: evidence, markers and mission hand-outs waited for the landing (events still booked leads and steps). The
    /// mirror of step 9's <c>TickCutscenes</c>: every server tick, outside the per-world loop, at 1 Hz per player (the same
    /// accumulator — a player is in one place at a time, so nobody is passed twice). Everything in the pass reads the player's
    /// own world by id or guards against the cursor (a ground marker needs the player's world loaded — none in space), so it
    /// is safe with any cursor; cheap: one set lookup per player, the leads stay lazily resolved.
    /// </summary>
    private void TickStoryOutsideWorlds(double dt)
    {
        foreach (var session in _sessions.Values)
        {
            if (!session.Joined || session.Spectating || _storyTickedWorlds.Contains(session.CurrentLocationId ?? string.Empty))
            {
                continue;
            }

            session.VegaAdvisorAccum += dt;
            if (session.VegaAdvisorAccum < 1.0)
            {
                continue;
            }

            session.VegaAdvisorAccum = 0.0;
            TickStoryPass(session);
        }
    }

    private bool NearVegaPoi(BlocksBeyondTheStars.Shared.Geometry.Vector3f pos)
    {
        const double r2 = 48.0 * 48.0;
        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            var v = _vaultEntrances[i];
            if (WrapDistSq(pos, new BlocksBeyondTheStars.Shared.Geometry.Vector3f(v.X, v.Y, v.Z)) <= r2)
            {
                return true;
            }
        }

        if (_wreckStamped)
        {
            var w = _wreckOrigin;
            if (WrapDistSq(pos, new BlocksBeyondTheStars.Shared.Geometry.Vector3f(w.X, w.Y, w.Z)) <= r2)
            {
                return true;
            }
        }

        return false;
    }

    // --- Memory fragments (Phase C story arc): found in wreck/vault data terminals and data caches,
    // redeemed one at a time when the player is back aboard the ship — VEGA "reads" them and recovers
    // a beat of her past. Completing the arc hands over the Mk3 core's research materials (#1104): the
    // blueprint itself still has to be researched at the cockpit behind its knowledge threshold — the arc
    // used to add it straight to the archive, which made the tree's second-most-expensive unlock free.
    // Since #2470 the arc is the VEGA pack's (synthesis §4.4, R3a): VEGA's memories (vega.mem.N) only while the VEGA story
    // runs; in a world without it the same redemptions read as neutral archive lines — knowledge and the Mk3 parts are
    // unchanged, the arc never feeds the story score and has no cutscenes. A player-triggered reader: never held. ---

    /// <summary>The neutral archive lines a memory fragment reads as without the VEGA story (#2470), in rotation.</summary>
    private const int VegaArchiveLines = 3;

    /// <summary>Whether the VEGA story is the world's story — its memory arc and lore lines speak only then.</summary>
    private bool VegaStoryActive => StoryActive && _story is not null
                                    && string.Equals(_story.Id, StoryRegistry.DefaultStoryId, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>The line of memory beat <paramref name="beat"/>: VEGA's memory under the VEGA story, a neutral archive line
    /// otherwise (#2470).</summary>
    private string VegaMemoryLine(int beat)
        => !VegaStoryActive ? "vega.mem.archive." + (((beat - 1) % VegaArchiveLines) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : beat <= VegaMemoryBeats ? "vega.mem." + beat.ToString(System.Globalization.CultureInfo.InvariantCulture) : "vega.mem.more";

    private void TickVegaMemory(PlayerSession session)
    {
        var p = session.State;
        if (!p.AboardShip || _uptime < session.VegaMemoryReadyAt || !p.Inventory.Has(VegaMemoryItem, 1))
        {
            return;
        }

        int beat = LastVegaMemoryBeat(p) + 1;

        // The final beat comes with the Mk3 parts list stowed in the hold; if there is no room for them the
        // fragment simply waits — nothing is consumed and nothing is lost (the same rule the achievement
        // rewards follow). Cargo counts because the player is aboard by definition here.
        MaterialPool? pool = null;
        var mk3Cost = beat == VegaMemoryBeats ? _content.GetBlueprint(VegaMk3Blueprint)?.UnlockCost : null;
        if (mk3Cost is { Count: > 0 })
        {
            session.Ships.TryGetValue(session.ActiveShipId, out var ownShip); // this player's ship, not the cursor's
            pool = new MaterialPool(_content, p, ownShip ?? _ship);
            if (!pool.CanFit(mk3Cost))
            {
                if (session.VegaMemoryHoldFullWarned)
                {
                    return;
                }

                session.VegaMemoryHoldFullWarned = true;
                SendVegaLine(session, "vega.sys.mk3parts_full", 3);
                return;
            }
        }

        p.Inventory.Remove(VegaMemoryItem, 1);
        p.Milestones.Add("vega:mem:" + beat);
        p.KnowledgePoints += VegaMemoryKnowledge;
        session.VegaMemoryReadyAt = _uptime + 6.0; // space multiple fragments out so the lines can be read

        SendVegaLine(session, VegaMemoryLine(beat), 2);
        if (mk3Cost is { Count: > 0 } && pool is not null && p.Milestones.Add("vega:mem:mk3parts"))
        {
            foreach (var part in mk3Cost)
            {
                pool.Add(part.Item, part.Count);
            }

            SendVegaLine(session, "vega.sys.mk3bp", 3);
        }

        SendInventory(session);
        _repo.SavePlayer(p);
    }

    /// <summary>The beat number of a restored-memory key (<c>vega:mem:&lt;n&gt;</c>), or -1 for any other key —
    /// notably <c>vega:mem:mk3parts</c>, the "parts handed over" flag that shares the prefix (#2460, N13: counting
    /// it showed "11/10" and numbered the next redemption 12).</summary>
    private static int VegaMemoryBeatOf(string key)
    {
        const string prefix = "vega:mem:";
        if (!key.StartsWith(prefix, System.StringComparison.Ordinal) || key.Length == prefix.Length)
        {
            return -1;
        }

        int n = 0;
        for (int i = prefix.Length; i < key.Length; i++)
        {
            char c = key[i];
            if (c < '0' || c > '9' || n > 100000)
            {
                return -1;
            }

            n = (n * 10) + (c - '0');
        }

        return n;
    }

    /// <summary>How many of VEGA's memory beats this player has restored.</summary>
    private static int RestoredVegaMemories(PlayerState p) => p.Milestones.Count(m => VegaMemoryBeatOf(m) > 0);

    /// <summary>The highest memory beat this player has restored (0 = none) — the next redemption is this + 1.</summary>
    private static int LastVegaMemoryBeat(PlayerState p)
    {
        int last = 0;
        foreach (var m in p.Milestones)
        {
            last = System.Math.Max(last, VegaMemoryBeatOf(m));
        }

        return last;
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
        double localDay = LocalDayFraction(session.State.Position); // #1865: the local sun
        string phase = localDay is < 0.15 or > 0.85 ? "night" : localDay is < 0.3 or > 0.7 ? "twilight" : "day";
        // VEGA's memory is the VEGA story's subject (#2470): no memory talk in a world without it.
        string memory = VegaStoryActive
            ? $", {System.Math.Min(VegaMemoryBeats, RestoredVegaMemories(p))}/10 of VEGA's memory restored"
            : string.Empty;
        string aboard = p.AboardShip ? "aboard the ship" : "on foot";
        // Temperature awareness (#671): only mentioned while the suit is actually fighting the climate,
        // so VEGA's banter can react to a freezing night march or a scorching noon without noise otherwise.
        string climate = p.SuitClimateActive
            ? session.EffectiveTemperatureC < ComfortLowC
                ? ", the suit's heater is fighting dangerous cold"
                : ", the suit's cooler is fighting dangerous heat"
            : string.Empty;
        return $"on a {world} world, {phase}, pilot is {aboard}{climate}{memory}";
    }

    /// <summary>Situation bucket for the banter cache — coarse on purpose so lines get reused.</summary>
    private string VegaBanterKey(PlayerSession session)
    {
        string world = _world.Planet?.Key ?? "space";
        bool night = LocalDayFraction(session.State.Position) is < 0.15 or > 0.85; // #1865: the local sun
        return $"banter|{world}|{(night ? "night" : "day")}|{session.Locale}";
    }

    /// <summary>Rolls the per-player banter timer; when due (and AI is on, the basic training through), serves a
    /// cached line or generates one off-thread. Called from <see cref="TickShipAi"/>'s 1 Hz path. "Training through" is
    /// the tutorial pack's <c>banter</c> gate (#2469, synthesis §5.3: basic training done or skipped, or the tutorial
    /// off or opted out); without a tutorial pack banter is open.</summary>
    private void TickVegaBanterFor(PlayerSession session)
    {
        if (_config.AiLevel == AiLevel.Off || TutorialGate(session, StoryGateNames.Banter) == false)
        {
            return;
        }

        if (session.VegaBanterNextAt <= 0.0)
        {
            // First arm after join: don't banter into the first minutes of a session.
            session.VegaBanterNextAt = _uptime + VegaBanterMinDelay * 0.75;
            return;
        }

        if (_uptime < session.VegaBanterNextAt || _uptime < session.VegaTipReadyAt)
        {
            return; // not due — or a context tip just went out (banter shares the tip cadence, #1077)
        }

        session.VegaBanterNextAt = _uptime + VegaBanterMinDelay
            + _vegaRng.NextDouble() * (VegaBanterMaxDelay - VegaBanterMinDelay);
        session.VegaTipReadyAt = _uptime + VegaTipGap;

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
        _ = System.Threading.Tasks.Task.Run(() =>
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

    /// <summary>Longest VEGA line the speech panel can show. The panel holds ~4 wrapped lines and now
    /// TRUNCATES rather than overflowing (#482), so a rambling LLM answer would be cut off mid-word with
    /// no indication. Clamping here means the player at least sees an ellipsis. Keep in sync with the
    /// panel geometry in <c>VegaPanel</c>.</summary>
    private const int VegaMaxTextChars = 240;

    /// <summary>Bounds an LLM-authored line to what the speech panel can display, ending on a word
    /// boundary where possible. Locale-key lines are authored by us and need no clamp.</summary>
    private static string ClampVegaText(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= VegaMaxTextChars)
        {
            return text;
        }

        // netstandard2.1: no string.Concat(ReadOnlySpan<char>, string) overload — plain Substring.
        int cut = text.LastIndexOf(' ', VegaMaxTextChars - 1);
        return text.Substring(0, cut > VegaMaxTextChars / 2 ? cut : VegaMaxTextChars - 1).TrimEnd() + "…";
    }

    private static ShipAiLine BuildVegaTextLine(string text)
        => new() { Text = ClampVegaText(text), Kind = 1 }; // advisor channel: the settings mute applies to banter too

    /// <summary>Sends an LLM banter text directly (keeps the current objective chip fields).</summary>
    private void SendVegaText(PlayerSession session, string text)
    {
        var line = new ShipAiLine
        {
            Text = ClampVegaText(text),
            Kind = 1,
        };
        SendVegaWithObjective(session, line);
    }

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
