// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>One chapter lead as resolved for one player (#2475): whether it is feasible where they are, whether they followed
/// it, its chip text and where it points.</summary>
internal sealed class ResolvedStoryLead
{
    /// <summary>The data entry (a stand-in's is made up by <see cref="StoryLeads.EffectiveLeads"/>).</summary>
    public StoryLead Lead { get; init; } = new();

    public string ChapterId { get; init; } = string.Empty;

    /// <summary>1-based in the chapter's effective list — the per-player key's number.</summary>
    public int Number { get; init; }

    public string Kind { get; init; } = string.Empty;

    public string Arg { get; init; } = string.Empty;

    /// <summary>A fallback the chapter does not list itself.</summary>
    public bool StandIn { get; init; }

    /// <summary>It can be followed from where the player is now (a fragment on this world, a reachable site, …).</summary>
    public bool Feasible { get; set; }

    /// <summary>The player followed it (<c>story:&lt;id&gt;:lead:&lt;chapter&gt;:&lt;n&gt;</c>).</summary>
    public bool Done { get; set; }

    /// <summary>The completion already holds without an action (a researched blueprint, a fitted module, a site landed on
    /// before) — the next 1 Hz pass books it.</summary>
    public bool Evidence { get; set; }

    /// <summary>Locale key of the chip / Journal text and its {0} (a display string).</summary>
    public string ObjKey { get; set; } = string.Empty;

    public string ChipArg { get; set; } = string.Empty;

    /// <summary>The body or station it points at, or empty.</summary>
    public string TargetBodyId { get; set; } = string.Empty;

    /// <summary>A spot on the player's current world to ping (this session only), or null.</summary>
    public Vector3f? GroundTarget { get; set; }

    /// <summary>A body or station of the player's current system to set as their space waypoint, or empty.</summary>
    public string WaypointId { get; set; } = string.Empty;

    /// <summary>What the marker was raised for — a change raises it again.</summary>
    public string Identity => ChapterId + ":" + Number.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + TargetBodyId
        + (GroundTarget is { } g ? ":" + ((int)g.X).ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + ((int)g.Z).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty);
}

/// <summary>What a player's lead resolution depends on (#2475) — compared in O(1) at every chip read; the leads are resolved
/// again only when it changed (no per-tick galaxy scans).</summary>
internal readonly record struct StoryLeadSignature(
    int Chapter,
    string Location,
    bool InSpace,
    int WorldFragments,
    int FoundFragments,
    bool Machines,
    bool FinaleOpen,
    bool Won,
    int Epoch,
    int LandedBodies,
    int Blueprints,
    int Scanned,
    string Ship,
    string Locale,
    bool InstantTravel);

/// <summary>A player's resolved leads of their current chapter (#2475), RAM only, per session.</summary>
internal sealed class StoryLeadCache
{
    public bool Valid { get; set; }

    public StoryLeadSignature Signature { get; set; }

    /// <summary>The chapter index the leads were resolved for (-1 = none).</summary>
    public int ChapterIndex { get; set; } = -1;

    /// <summary>The chapter's effective leads, in order.</summary>
    public List<ResolvedStoryLead> Leads { get; } = new();

    /// <summary>The first open, feasible lead — the chip's; null = the explore fallback (or nothing after the win).</summary>
    public ResolvedStoryLead? Current { get; set; }

    /// <summary>The explore fallback's key for the world's rules — empty after the win (no fallback then).</summary>
    public string ExploreKey { get; set; } = string.Empty;

    /// <summary>Bumped by events the signature cannot see (a module fitted, a ship switched, a lead followed).</summary>
    public int Epoch { get; set; }

    /// <summary>The lead the last marker went up for (<see cref="ResolvedStoryLead.Identity"/>).</summary>
    public string MarkedFor { get; set; } = string.Empty;

    /// <summary>#2742: the body the lead's ground marker stands on — empty while there is none. The marker is a persistent
    /// private story marker (no 30-second ping any more): it stays until the lead is followed or the current lead changes.</summary>
    public string MarkerLocationId { get; set; } = string.Empty;

    /// <summary>#2742: where the lead's marker stands.</summary>
    public Vector3f MarkerAt { get; set; }

    /// <summary>#2742: the marker's label — the lead's objective key …</summary>
    public string MarkerKey { get; set; } = string.Empty;

    /// <summary>#2742: … and the value of its <c>{0}</c> (the chip's argument).</summary>
    public string MarkerArg { get; set; } = string.Empty;

    /// <summary>#2742: the <see cref="MarkedFor"/> identity the marker belongs to (a stale one is never shown).</summary>
    public string MarkerFor { get; set; } = string.Empty;

    /// <summary>#2742: drops the lead's marker. Returns whether there was one.</summary>
    public bool ClearMarker()
    {
        if (MarkerLocationId.Length == 0)
        {
            return false;
        }

        MarkerLocationId = string.Empty;
        MarkerKey = string.Empty;
        MarkerArg = string.Empty;
        MarkerFor = string.Empty;
        return true;
    }

    /// <summary>#2476: the lead a story mission's lead bridge on the chip resolves to (its placed text, its marker, the heal tank's
    /// spot) — resolved again only when the step or <see cref="MissionSignature"/> changed.</summary>
    public ResolvedStoryLead? MissionLead { get; set; }

    /// <summary>#2476: the progress key of the step <see cref="MissionLead"/> was resolved for.</summary>
    public string MissionLeadStep { get; set; } = string.Empty;

    /// <summary>#2476: the signature <see cref="MissionLead"/> was resolved under.</summary>
    public StoryLeadSignature MissionSignature { get; set; }
}

/// <summary>
/// The world story's chapter leads, the chapter bonus and the peaceful-world pacing (#2475, synthesis §4.8/§4.9 — option O2:
/// "the score is the clock, the chapter leads are the compass").
/// <list type="bullet">
/// <item><b>Leads</b> are pack data per chapter (<see cref="StoryChapter.Leads"/>, vocabulary <see cref="StoryLeadTypes"/>).
/// For a player whose story track started, their current chapter's leads are resolved per player — feasible here or not, done
/// or not, where they point — lazily at the chip read and only when <see cref="StoryLeadSignature"/> changed (the chapter, the
/// place, the counts that decide feasibility, an event epoch). The chip shows the first open, feasible lead (rank "VEGA lead"
/// of synthesis §5.3), else a stand-in (<see cref="StoryLeadTypes.Fallbacks"/>), else "explore" (rules-aware). A current client
/// reads the <c>story.vega.obj.*</c> keys; an older one keeps today's <c>story.obj.*</c> keys (<see cref="StoryObjectiveKey"/>).
/// Where a lead has a place the player gets a marker of their own: a ping on their world, or their space waypoint for a body
/// of their system (the fragment keeps its shared map signal, as before). The Journal's VEGA page lists the chapter's leads.</item>
/// <item><b>Following a lead</b> is an event on the bus (<see cref="StoryLeadListener"/>: a fragment found, a site landed on,
/// a monument read, a settlement job done, a machine destroyed, a blueprint researched, a module fitted, a relay built) or
/// evidence at the 1 Hz pass (the blueprint already researched, the module already fitted, the site visited before, standing
/// at the heal tank). It writes <c>story:&lt;id&gt;:lead:&lt;chapter&gt;:&lt;n&gt;</c> for that player — in multiplayer each
/// player follows their own leads.</item>
/// <item><b>The chapter bonus</b>: the first lead anyone follows in a chapter adds <see cref="StoryDefinition.LeadBonusMilestones"/>
/// milestones once per save (keys <c>lead:&lt;chapter&gt;:1..n</c> in the story state), and only before the arc's last beat —
/// for VEGA at most six chapters × 6 points = 36 before B12.</item>
/// <item><b>Pacing</b>: every reader of the score goes through ONE wrapper, <see cref="StoryScale"/> — the world's density times
/// <see cref="StoryDriverNormalisation"/>, which on a world without machines makes up the kill share it can never earn
/// (<see cref="StoryEngine.UnavailableKillNormalisation"/>, VEGA ≈ 1.244). The meter, the chip and the knowledge level show
/// <see cref="StoryProgressShown"/> (clamped: never below the last revealed beat, never above the target). An existing peaceful
/// save crosses one or two beats at its first start with this build; the join catch-up (the recap, the Journal line, the
/// cutscene cooldown) absorbs them. There a followed lead or a monument read unlocks the next personal memory
/// (<see cref="StoryDefinition.MemoryTriggers"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close to the ship's heal tank the player must stand for the heal-tank lead (metres).</summary>
    internal const float HealTankLeadReach = 3.5f;

    /// <summary>The bus keys the lead listener needs.</summary>
    private static readonly string[] LeadEventKeys =
    {
        GameEventKeys.StoryFragment, GameEventKeys.VisitBody, GameEventKeys.StationBoarded, GameEventKeys.ScanAny,
        GameEventKeys.MissionTurnedIn, GameEventKeys.DefeatAny, GameEventKeys.ResearchAny, GameEventKeys.ModuleBuilt,
        GameEventKeys.ShipCommissioned, GameEventKeys.RelayCommissioned, GameEventKeys.LaneLinked,
    };

    /// <summary>The lead listener while the world story has leads (null otherwise — the bus then stays at one compare).</summary>
    private StoryLeadListener? _leadListener;

    /// <summary>How many lead resolutions ran (the "no per-tick cost" guard of the tests).</summary>
    private int _leadResolutions;

    /// <summary>The unique sites' bodies (seed-pure — cached per galaxy).</summary>
    private readonly Dictionary<string, string?> _leadSiteBodies = new(StringComparer.Ordinal);
    private object? _leadSiteGalaxy;

    /// <summary>Feeds the bus events that follow a lead into <see cref="OnLeadGameEvent"/>.</summary>
    private sealed class StoryLeadListener : IGameEventListener
    {
        private readonly GameServer _server;

        public StoryLeadListener(GameServer server) => _server = server;

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => _server.OnLeadGameEvent(actor, key, arg);
    }

    /// <summary>Whether the world story has leads at all — without, every client keeps the old story objectives.</summary>
    private bool LeadsActive => _storyHasLeads;

    /// <summary>The active world story lists chapter leads (set with the listener, <see cref="SyncLeadListener"/>).</summary>
    private bool _storyHasLeads;

    // ---------------------------------------------------------------------------------------------
    // Pacing: the one wrapper around the score.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The ONE scale every reader of the story score uses (#2475): the world's density
    /// (<see cref="Shared.Configuration.GameRules.StoryProgressScale"/>) times <see cref="StoryDriverNormalisation"/>. The beat
    /// reveal, the meter, the chip, the knowledge level and the admin's <c>reveal_finale</c> all read it, so they never
    /// disagree.</summary>
    private float StoryScale => Rules.StoryProgressScale * StoryDriverNormalisation;

    /// <summary>The peaceful-world factor: the pack's <see cref="StoryEngine.UnavailableKillNormalisation"/> while the world's rules
    /// field no machine a kill could count against (and the pack asks for it), else 1. Follows the current rules — switching
    /// hostiles on lowers it again; heard beats stay.</summary>
    private float StoryDriverNormalisation
        => _story is { NormaliseUnavailableDrivers: true } def && !StoryMachinesActive ? StoryEngine.UnavailableKillNormalisation(def) : 1f;

    /// <summary>The score now (what the beats reveal by).</summary>
    private int StoryProgressNow() => _story is null ? 0 : StoryEngine.Progress(_story, _storyState, StoryScale);

    /// <summary>The score as the meter, the chip and the knowledge level show it (clamped, <see cref="StoryEngine.DisplayProgress"/>).</summary>
    private int StoryProgressShown() => _story is null ? 0 : StoryEngine.DisplayProgress(_story, _storyState, StoryProgressNow());

    // ---------------------------------------------------------------------------------------------
    // The listener.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Registers the lead listener while the world story has leads — or (#2476) story missions with a lead bridge, which
    /// are followed through the same events — and removes it otherwise (story load, story switch).</summary>
    private void SyncLeadListener()
    {
        bool hasLeads = _story is not null && _story.Chapters.Any(c => c.Leads.Count > 0);
        bool want = hasLeads || (_story is not null && _story.Chapters.Any(c =>
            c.Missions.Any(m => m is not null && m.Steps.Any(st => st.Objective?.IsLead == true))));
        bool changed = hasLeads != _storyHasLeads;
        _storyHasLeads = hasLeads;
        if (want == (_leadListener is not null))
        {
            if (changed)
            {
                foreach (var s in _sessions.Values)
                {
                    s.Story.StoryLeads.Valid = false;
                }
            }

            return;
        }

        if (want)
        {
            _leadListener = new StoryLeadListener(this);
            foreach (string key in LeadEventKeys)
            {
                AddGameEventListener(key, _leadListener);
            }
        }
        else
        {
            foreach (string key in LeadEventKeys)
            {
                RemoveGameEventListener(key, _leadListener!);
            }

            _leadListener = null;
        }

        foreach (var s in _sessions.Values)
        {
            s.Story.StoryLeads.Valid = false;
        }
    }

    /// <summary>A bus event of a player whose track started: the first open lead of their current chapter it fulfils is booked
    /// (one action, one lead). The events are raised before the story records their points, so the lead counts for the chapter
    /// the player was in when they acted. Then (#2476) the same action may finish a story mission's lead bridge.</summary>
    private void OnLeadGameEvent(PlayerSession actor, string key, string arg)
    {
        if (_story is null || !StoryTrackStarted(actor))
        {
            return;
        }

        var cache = LeadsFor(actor);
        if (key is GameEventKeys.ModuleBuilt or GameEventKeys.ShipCommissioned)
        {
            actor.Story.StoryLeads.Epoch++; // a jump generator changes what is reachable and the finale's text; a new ship too
        }

        bool booked = CompleteFirstOpen(actor, cache, l => LeadFulfilledBy(l.Kind, l.Arg, l.TargetBodyId, key, arg));
        if (!booked && key == GameEventKeys.ScanAny && arg == "monument")
        {
            TryAlternativeMemory(actor, StoryMemoryTriggers.Monument); // where no machine can drop one, a monument read gives a memory
        }

        OnMissionLeadEvent(actor, key, arg); // #2476
    }

    /// <summary>Whether a bus event follows a lead of this word (the one rule for chapter leads and the missions' lead bridges,
    /// #2476): a NEW fragment find (a re-gift only re-reads), a landing on / boarding of the site's body, a first-time monument
    /// read, a settlement or chain job that counts for the story, a destroyed machine, the blueprint researched, the module
    /// fitted, a relay or lane completed. The heal tank is followed by standing there (the 1 Hz pass), never by an event.</summary>
    private bool LeadFulfilledBy(string kind, string leadArg, string targetBodyId, string key, string arg) => key switch
    {
        GameEventKeys.StoryFragment => kind == StoryLeadTypes.Fragment && !_storyState.FoundFragmentKeys.Contains(arg),
        GameEventKeys.VisitBody or GameEventKeys.StationBoarded => kind == StoryLeadTypes.UniqueSite && targetBodyId.Length > 0 && targetBodyId == arg,
        GameEventKeys.ScanAny => kind == StoryLeadTypes.Monument && arg == "monument",
        GameEventKeys.MissionTurnedIn => kind == StoryLeadTypes.SettlementHelp
                                         && _missionDefs.TryGetValue(arg, out var def) && def.CountsForStory && MissionAcceptCountsAsTrade(def),
        GameEventKeys.DefeatAny => kind == StoryLeadTypes.Machines && arg is GameEventKeys.DefeatMachine or GameEventKeys.DefeatSpaceMachine,
        GameEventKeys.ResearchAny => kind == StoryLeadTypes.Research && leadArg == arg,
        GameEventKeys.ModuleBuilt => kind == StoryLeadTypes.Module && leadArg == arg,
        GameEventKeys.RelayCommissioned or GameEventKeys.LaneLinked => kind == StoryLeadTypes.Relay,
        _ => false,
    };

    /// <summary>Books the first lead of the cached list that matches and is not done yet. Returns whether one was booked.</summary>
    private bool CompleteFirstOpen(PlayerSession s, StoryLeadCache cache, Func<ResolvedStoryLead, bool> match)
    {
        foreach (var lead in cache.Leads.ToList())
        {
            if (!LeadDone(s, lead) && match(lead))
            {
                return CompleteLead(s, lead);
            }
        }

        return false;
    }

    private bool LeadDone(PlayerSession s, ResolvedStoryLead lead)
        => _story is not null && s.State.Milestones.Contains(StoryPackKeys.Lead(_story.Id, lead.ChapterId, lead.Number));

    // ---------------------------------------------------------------------------------------------
    // Following a lead: the per-player key, the bonus, the memory.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The player followed a lead: their key, the chapter's once-per-save bonus, on a world without machines the next
    /// personal memory, then the chip and the Journal move on. Returns false when it was booked before.</summary>
    private bool CompleteLead(PlayerSession s, ResolvedStoryLead lead)
    {
        var def = _story;
        if (def is null || !s.State.Milestones.Add(StoryPackKeys.Lead(def.Id, lead.ChapterId, lead.Number)))
        {
            return false;
        }

        lead.Done = true;
        s.Story.StoryLeads.Epoch++; // the chip moves on to the next lead
        if (s.Story.StoryLeads.MarkerFor == lead.Identity && s.Story.StoryLeads.ClearMarker())
        {
            SendMarkers(s); // #2742: the followed lead's marker goes at once (the next 1 Hz pass marks the next lead)
        }

        _repo.SavePlayer(s.State);
        _log.Info($"Story lead {lead.ChapterId}:{lead.Number} ({lead.Lead.Type}) followed by '{s.State.Name}'.");
        TryAlternativeMemory(s, StoryMemoryTriggers.Lead);
        PayLeadBonus(lead.ChapterId);
        MarkJournalDirty(s);
        SendVegaObjective(s);
        return true;
    }

    /// <summary>The chapter bonus (synthesis §4.8): <see cref="StoryDefinition.LeadBonusMilestones"/> milestones the first time
    /// anyone follows a lead of the chapter — once per save (<see cref="StoryLeads.BonusKey"/> in the story state), only for a
    /// chapter that lists leads and only before the arc's last beat (VEGA: the six chapters before B12, 36 points at most).
    /// Returns whether it paid.</summary>
    private bool PayLeadBonus(string chapterId)
    {
        var def = _story;
        int milestones = def?.LeadBonusMilestones ?? 0;
        if (def is null || milestones <= 0 || StoryEngine.AllBeatsRevealed(def, _storyState)
            || def.Chapters.FirstOrDefault(c => c.Id == chapterId) is not { Leads.Count: > 0 }
            || _storyState.MilestoneKeys.Contains(StoryLeads.BonusKey(chapterId, 1)))
        {
            return false;
        }

        for (int i = 1; i <= milestones; i++)
        {
            _storyState.MilestoneKeys.Add(StoryLeads.BonusKey(chapterId, i));
        }

        _storyState.Milestones += milestones;
        _log.Info($"Story lead bonus of chapter '{chapterId}': +{milestones} milestones.");
        AdvanceStory(); // reveals what the bonus crossed, persists, re-sends the meter
        return true;
    }

    /// <summary>The second way to a personal memory where no machine can drop one (#2475, synthesis §4.9): the pack names the
    /// trigger (<see cref="StoryDefinition.MemoryTriggers"/>), the world's rules field no machines and the player's story track
    /// has started. Returns whether a memory came.</summary>
    private bool TryAlternativeMemory(PlayerSession s, string trigger)
    {
        if (_story is null || !_story.MemoryTriggers.Contains(trigger) || StoryMachinesActive || !StoryTrackStarted(s)
            || !UnlockNextPlayerMemory(s))
        {
            return false;
        }

        _repo.SavePlayer(s.State);
        return true;
    }

    // ---------------------------------------------------------------------------------------------
    // Resolution.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The player's leads of their current chapter — resolved again only when <see cref="StoryLeadSignature"/> changed.
    /// Empty before the track starts and without leads.</summary>
    private StoryLeadCache LeadsFor(PlayerSession s)
    {
        var cache = s.Story.StoryLeads;
        if (!LeadsActive || _story is null || !StoryTrackStarted(s))
        {
            if (cache.Valid || cache.Leads.Count > 0)
            {
                cache.Valid = false;
                cache.Leads.Clear();
                cache.Current = null;
                cache.ChapterIndex = -1;
                cache.ExploreKey = string.Empty;
            }

            return cache;
        }

        var signature = LeadSignatureOf(s, cache.Epoch);
        if (!cache.Valid || cache.Signature != signature)
        {
            ResolveLeads(s, cache, signature);
        }

        return cache;
    }

    private StoryLeadSignature LeadSignatureOf(PlayerSession s, int epoch)
    {
        var p = s.State;
        bool inSpace = InSpace(p.PlayerId);
        var world = inSpace ? null : _worlds.Find(s.CurrentLocationId ?? string.Empty);
        return new StoryLeadSignature(
            TrackChapterIndex(s),
            s.CurrentLocationId ?? string.Empty,
            inSpace,
            world?.NetFragments.Count ?? -1,
            _storyState.FoundFragmentKeys.Count,
            StoryMachinesActive,
            FinaleOpen,
            _storyState.GuardianDefeated,
            epoch,
            p.LandedBodies.Count,
            p.UnlockedBlueprints.Count,
            p.ScannedNames.Count,
            s.ActiveShipId ?? string.Empty,
            s.Locale ?? string.Empty,
            Rules.InstantTravel);
    }

    private void ResolveLeads(PlayerSession s, StoryLeadCache cache, StoryLeadSignature signature)
    {
        _leadResolutions++;
        cache.Valid = true;
        cache.Signature = signature;
        cache.Leads.Clear();
        cache.Current = null;
        cache.ChapterIndex = signature.Chapter;
        bool won = _storyState.GuardianDefeated;
        cache.ExploreKey = won ? string.Empty : StoryLeads.ExploreObjKey(StoryMachinesActive); // after the win: no fallback
        var chapter = ChapterAt(signature.Chapter);
        if (chapter is null)
        {
            return;
        }

        var effective = StoryLeads.EffectiveLeads(chapter);
        int declared = StoryLeads.DeclaredCount(chapter);
        for (int i = 0; i < effective.Count; i++)
        {
            bool standIn = i >= declared;
            if (standIn && won)
            {
                break; // the post-game has its own leads only (the relays)
            }

            var lead = ResolveLead(s, chapter, effective[i], i + 1, standIn);
            cache.Leads.Add(lead);
            if (cache.Current is null && lead.Feasible && !lead.Done)
            {
                cache.Current = lead;
            }
        }
    }

    /// <summary>One lead for one player: feasible here, done, evidence, its text and its target (synthesis d2 §3.6).</summary>
    private ResolvedStoryLead ResolveLead(PlayerSession s, StoryChapter chapter, StoryLead data, int number, bool standIn)
    {
        var p = s.State;
        string kind = StoryLeadTypes.KindOf(data.Type);
        string arg = StoryLeadTypes.ArgOf(data.Type);
        var lead = new ResolvedStoryLead
        {
            Lead = data,
            ChapterId = chapter.Id,
            Number = number,
            Kind = kind,
            Arg = arg,
            StandIn = standIn,
            Done = p.Milestones.Contains(StoryPackKeys.Lead(_story!.Id, chapter.Id, number)),
        };

        var world = InSpace(p.PlayerId) ? null : _worlds.Find(s.CurrentLocationId ?? string.Empty);
        bool hasPlace = true;
        bool needsJump = false;
        switch (kind)
        {
            case StoryLeadTypes.Fragment:
                lead.Feasible = world is { NetFragments.Count: > 0 };
                if (world is not null && lead.Feasible)
                {
                    lead.TargetBodyId = world.LocationId;
                    lead.ChipArg = GalaxyBodyName(world.LocationId);
                    hasPlace = lead.ChipArg.Length > 0;
                }

                break;
            case StoryLeadTypes.UniqueSite:
                ResolveUniqueSite(s, lead, world);
                break;
            case StoryLeadTypes.Monument:
                if (world is not null && UnreadMonument(s, world) is { } monument)
                {
                    lead.Feasible = true;
                    lead.GroundTarget = monument.Center;
                }

                break;
            case StoryLeadTypes.SettlementHelp:
                if (world is not null && NearestSettlementCentre(s, world) is { } settlement)
                {
                    lead.Feasible = true;
                    lead.GroundTarget = settlement;
                }

                break;
            case StoryLeadTypes.Machines:
                lead.Feasible = StoryMachinesActive; // only where the rules field machines (N14)
                break;
            case StoryLeadTypes.HealTank:
                if (world is not null && world.LandedShips.TryGetValue(p.PlayerId, out var ship) && ship.Placed)
                {
                    lead.Feasible = true;
                    lead.GroundTarget = ship.HealTank;
                }

                break;
            case StoryLeadTypes.Research:
                if (_content.GetBlueprint(arg) is { } blueprint)
                {
                    lead.Feasible = true;
                    lead.Evidence = p.UnlockedBlueprints.Contains(arg);
                    lead.ChipArg = Localize(s.Locale ?? "en", blueprint.NameKey);
                }

                break;
            case StoryLeadTypes.Module:
                if (_content.GetShipModule(arg) is { } module)
                {
                    lead.Feasible = true;
                    lead.Evidence = ShipOf(s).HasModule(arg);
                    lead.ChipArg = Localize(s.Locale ?? "en", module.NameKey);
                }

                break;
            case StoryLeadTypes.Finale:
                lead.Feasible = FinaleOpen;
                needsJump = FinaleNeedsJump(s);
                break;
            case StoryLeadTypes.FinaleJump:
                lead.Feasible = FinaleOpen && FinaleNeedsJump(s);
                needsJump = true;
                break;
            case StoryLeadTypes.Relay:
                lead.Feasible = _storyState.GuardianDefeated;
                break;
            case StoryLeadTypes.Explore:
                lead.Feasible = true; // an explicit "explore" lead is never done — it stands for the open world
                break;
        }

        lead.ObjKey = StoryLeads.ObjKey(data, hasPlace, needsJump);
        return lead;
    }

    /// <summary>A unique-site lead: the site's body (seed-pure), whether the site exists in this save (a world stamped before
    /// the sites were added records a skip, #1129) and whether the player can get there (same system, a jump generator, a
    /// landing there before, instant travel); landing there before is evidence. Its marker: a ping on the site's world, the
    /// space waypoint in its system.</summary>
    private void ResolveUniqueSite(PlayerSession s, ResolvedStoryLead lead, LoadedWorld? world)
    {
        string? target = UniqueSiteTarget(lead.Arg);
        if (target is null)
        {
            return; // the galaxy has no such site
        }

        lead.TargetBodyId = target;
        lead.Evidence = s.State.LandedBodies.Contains(target);
        lead.ChipArg = GalaxyBodyName(lead.Arg == "derelict" ? UniqueSiteTarget("derelict_host") ?? target : target);
        lead.Feasible = UniqueSiteExists(lead.Arg, target) && CanReachBody(s, target);
        if (!lead.Feasible)
        {
            return;
        }

        if (world is not null && world.LocationId == target && UniqueSiteGroundSpot(lead.Arg, target) is { } spot)
        {
            lead.GroundTarget = spot;
        }
        else if (string.Equals(ResolveLocationHostBody(s.CurrentLocationId)?.SystemId, _galaxy?.FindBody(target)?.SystemId, StringComparison.Ordinal)
                 && s.CurrentLocationId != target)
        {
            lead.WaypointId = target;
        }
    }

    /// <summary>Where a surface unique site stands on its world (its pinned placement), or null — the derelict drifts in space,
    /// and a world that has not stamped the site yet has no spot.</summary>
    private Vector3f? UniqueSiteGroundSpot(string kind, string bodyId)
        => kind != "derelict"
           && _meta.Placements.Find(r => r.LocationId == bodyId && r.Kind == "unique:" + UniqueSiteKey(kind) && r.Index == 0) is { Placed: true } record
            ? new Vector3f(record.X + 0.5f, record.GroundY + 1f, record.Z + 0.5f)
            : null;

    /// <summary>The site key of a lead's unique-site word (the shrine's placement kind is <c>alien_shrine</c>).</summary>
    private static string UniqueSiteKey(string kind) => kind == "shrine" ? ShrineSiteKey : kind;

    /// <summary>The body (or, for the derelict, the station) a unique-site lead points at — cached per galaxy, the bodies are
    /// seed-pure. <c>derelict_host</c> = the derelict's host body (its name labels the lead).</summary>
    private string? UniqueSiteTarget(string kind)
    {
        if (!ReferenceEquals(_leadSiteGalaxy, _galaxy))
        {
            _leadSiteBodies.Clear();
            _leadSiteGalaxy = _galaxy;
        }

        if (!_leadSiteBodies.TryGetValue(kind, out string? body))
        {
            body = kind switch
            {
                "observatory" => ObservatoryBodyId(),
                "shrine" => ShrineBodyId(),
                "derelict" => DerelictHostBodyId() is not null && _stationsById.ContainsKey(DerelictStationId) ? DerelictStationId : null,
                "derelict_host" => DerelictHostBodyId(),
                _ => null,
            };
            _leadSiteBodies[kind] = body;
        }

        return body;
    }

    /// <summary>Whether a unique site is (or will be) there: the derelict always (it is registered at start); a surface site
    /// when its world pinned it, or when that world has never been edited (it stamps at the first landing). A world that
    /// recorded a skip — stamped before the sites existed — never gets it.</summary>
    private bool UniqueSiteExists(string kind, string bodyId)
    {
        if (kind == "derelict")
        {
            return _stationsById.ContainsKey(DerelictStationId);
        }

        string placementKind = "unique:" + UniqueSiteKey(kind);
        var record = _meta.Placements.Find(r => r.LocationId == bodyId && r.Kind == placementKind && r.Index == 0);
        return record is not null ? record.Placed : !_repo.HasAnyBlockEdits(bodyId);
    }

    /// <summary>Whether the player can get to a body: instant travel, a landing there before, the same system, or a jump
    /// generator for another system (synthesis d2 §3.6: an unreachable target is infeasible).</summary>
    private bool CanReachBody(PlayerSession s, string bodyId)
    {
        var body = _galaxy?.FindBody(bodyId);
        if (body is null)
        {
            return false;
        }

        if (Rules.InstantTravel || s.State.LandedBodies.Contains(bodyId))
        {
            return true;
        }

        string? here = ResolveLocationHostBody(s.CurrentLocationId)?.SystemId;
        return (here is not null && here == body.SystemId) || HasJumpGenerator(s);
    }

    private bool HasJumpGenerator(PlayerSession s) => ShipOf(s).HasModule("jump_generator");

    /// <summary>The finale's "fit a jump generator first": the player's ship has none and they are not in the Guardian system.</summary>
    private bool FinaleNeedsJump(PlayerSession s) => !HasJumpGenerator(s) && !IsGuardianSystemLocation(s.CurrentLocationId ?? string.Empty);

    /// <summary>The galaxy's name of a body ("Kepler-3 b"), or empty when the galaxy does not know it.</summary>
    private string GalaxyBodyName(string? bodyId)
        => string.IsNullOrEmpty(bodyId) ? string.Empty : _galaxy?.FindBody(bodyId!)?.Name ?? string.Empty;

    /// <summary>The nearest monument on the player's world whose runes they have not read (their scan ledger), or null.</summary>
    private static MonumentInstance? UnreadMonument(PlayerSession s, LoadedWorld world)
    {
        MonumentInstance? best = null;
        double bestSq = double.MaxValue;
        foreach (var m in world.Monuments)
        {
            if (s.State.ScannedNames.ContainsKey($"monument:{world.LocationId}:{m.Archetype}"))
            {
                continue;
            }

            double d = WorldConstants.WrapDistanceSquared(m.Center, s.State.Position, world.World.Circumference);
            if (d < bestSq)
            {
                bestSq = d;
                best = m;
            }
        }

        return best;
    }

    /// <summary>The centre of the settlement nearest to the player on their world, or null when it has none.</summary>
    private static Vector3f? NearestSettlementCentre(PlayerSession s, LoadedWorld world)
    {
        Vector3f? best = null;
        double bestSq = double.MaxValue;
        foreach (var settlement in world.Settlements)
        {
            var centre = new Vector3f((settlement.Min.X + settlement.Max.X) * 0.5f, settlement.GroundY + 1, (settlement.Min.Z + settlement.Max.Z) * 0.5f);
            double d = WorldConstants.WrapDistanceSquared(centre, s.State.Position, world.World.Circumference);
            if (d < bestSq)
            {
                bestSq = d;
                best = centre;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------------------------------------
    // The 1 Hz pass: evidence and markers.
    // ---------------------------------------------------------------------------------------------

    /// <summary>1 Hz per player on a world (from <see cref="TickShipAi"/>): books leads whose completion already holds (a
    /// researched blueprint, a fitted module, a site landed on before, standing at the heal tank) and raises the marker of a
    /// new current lead. Reads the cached resolution — a resolution itself only runs when the signature changed.</summary>
    private void TickStoryLeads(PlayerSession s)
    {
        if (!LeadsActive || _story is null || !StoryTrackStarted(s))
        {
            return;
        }

        var cache = LeadsFor(s);
        ResolvedStoryLead? due = null;
        foreach (var lead in cache.Leads)
        {
            if (!lead.Done && (lead.Evidence || AtHealTank(s, lead)))
            {
                due = lead;
                break;
            }
        }

        if (due is not null)
        {
            CompleteLead(s, due); // one a second — the next pass books the next (no allocation while nothing is due)
        }

        MarkCurrentLead(s, LeadsFor(s));
    }

    /// <summary>The heal-tank lead's completion: the player stands at their landed ship's tank (the position read once a
    /// second, no search).</summary>
    private bool AtHealTank(PlayerSession s, ResolvedStoryLead lead)
        => lead.Kind == StoryLeadTypes.HealTank && lead.GroundTarget is { } tank && !InSpace(s.State.PlayerId)
           && _worlds.Find(s.CurrentLocationId ?? string.Empty) is { } world
           && WorldConstants.WrapDistanceSquared(tank, s.State.Position, world.World.Circumference) <= HealTankLeadReach * HealTankLeadReach;

    /// <summary>The marker of a new current lead, once per lead and place: a story marker on the player's world that only they
    /// see (#2742: it stays until the lead is followed or the current lead changes — it used to be a 30-second ping raised
    /// once, gone before VEGA's line was read), or their space waypoint for a body of their system. Hidden while the VEGA
    /// hints are muted (the story's chip follows that switch — <see cref="LiveLeadMarker"/>, so un-muting brings it back),
    /// never for an older client (its chip shows no lead); a fragment keeps its shared map signal
    /// (<see cref="RevealFragmentSignals"/>).</summary>
    private void MarkCurrentLead(PlayerSession s, StoryLeadCache cache)
    {
        var lead = cache.Current;
        string identity = lead?.Identity ?? string.Empty;
        if (!FinaleOpen && MissionChipStep(s) is { } step)
        {
            // #2476: a story mission on the chip marks its own target — a lead bridge's place where it can be followed from
            // here — and nothing else (the lead's marker would point somewhere the chip does not talk about).
            lead = step.IsLeadBridge && MissionLeadFor(s, step) is { Feasible: true } bridged ? bridged : null;
            identity = "mission:" + step.Id + (lead is null ? string.Empty : ":" + lead.Identity);
        }

        if (identity == cache.MarkedFor)
        {
            return;
        }

        cache.MarkedFor = identity;
        bool hadMarker = cache.ClearMarker(); // a new lead (or none): the old marker goes
        if (lead is not null && s.SupportsPacks) // an older client shows no lead on its chip — a marker without one would puzzle
        {
            if (lead.GroundTarget is { } at && !InSpace(s.State.PlayerId))
            {
                if (s.CurrentLocationId is { Length: > 0 } loc)
                {
                    cache.MarkerLocationId = loc;
                    cache.MarkerAt = at;
                    cache.MarkerKey = lead.ObjKey;
                    cache.MarkerArg = lead.ChipArg;
                    cache.MarkerFor = identity;
                    SendMarkers(s);
                    return;
                }
            }
            else if (lead.WaypointId.Length > 0 && !s.Vega.VegaHintsMuted)
            {
                SendVegaWithObjective(s, new ShipAiLine { Kind = 0, SpaceWaypointId = lead.WaypointId }); // a chip update with the target
            }
        }

        if (hadMarker)
        {
            SendMarkers(s);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The chip and the Journal.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The world story's objective for this player (#2475): an older client (no <c>packs1</c>) or a story without leads
    /// keeps today's <c>story.obj.*</c> keys; before the track starts only the finale (rank 1 — the player stands in the
    /// Guardian system); then the current lead, else "explore" (rules-aware), else nothing (after the win, once the relays are
    /// done). Empty key = no story objective.</summary>
    private (string Key, string Arg, string Source) StoryObjectiveFor(PlayerSession s)
    {
        if (!StoryActive || _story is null)
        {
            return (string.Empty, string.Empty, ObjectiveSources.None);
        }

        // #2476: a running story mission is the chapter's compass while it runs — it takes the lead's place on the chip (its lead
        // bridge with the lead's own placed text). Never over the open finale: that stays the finale's (never muted).
        if (!FinaleOpen && MissionObjectiveFor(s) is { } mission)
        {
            return mission;
        }

        if (!LeadsActive || !s.SupportsPacks)
        {
            string key = StoryObjectiveKey(s.State);
            string arg = key == "story.obj.fragment_on" ? FragmentWorldName() : string.Empty;
            return (key, arg, key == StoryFinaleObjectiveKey ? ObjectiveSources.Finale : key.Length > 0 ? ObjectiveSources.Story : ObjectiveSources.None);
        }

        if (!StoryTrackStarted(s))
        {
            return FinaleOpen
                ? (FinaleNeedsJump(s) ? StoryLeads.FinaleJumpKey : StoryLeads.FinaleKey, string.Empty, ObjectiveSources.Finale)
                : (string.Empty, string.Empty, ObjectiveSources.None);
        }

        var cache = LeadsFor(s);
        if (cache.Current is { } lead)
        {
            return (lead.ObjKey, lead.ChipArg, StoryLeadTypes.IsFinale(lead.Kind) ? ObjectiveSources.Finale : ObjectiveSources.Story);
        }

        return cache.ExploreKey.Length > 0
            ? (cache.ExploreKey, string.Empty, ObjectiveSources.Story)
            : (string.Empty, string.Empty, ObjectiveSources.None);
    }

    /// <summary>Whether a chip key is a fragment objective (old or new) — its signal goes on the map.</summary>
    private static bool IsFragmentObjective(string key)
        => key is "story.obj.fragment_on" or "story.obj.fragment_here" || key == StoryLeads.FragmentOnKey || key == StoryLeads.FragmentHereKey;

    /// <summary>The Journal's lead rows of the VEGA page (#2475): the current chapter's own leads (done or open) and the stand-in
    /// or "explore" while it is on the chip. Empty before the track starts and without leads.</summary>
    private NetStoryLead[] JournalLeads(PlayerSession s)
    {
        if (!LeadsActive || _story is null || !StoryTrackStarted(s))
        {
            return Array.Empty<NetStoryLead>();
        }

        var cache = LeadsFor(s);
        var rows = new List<NetStoryLead>();
        foreach (var lead in cache.Leads)
        {
            if (lead.StandIn && lead != cache.Current)
            {
                continue;
            }

            rows.Add(new NetStoryLead
            {
                ChapterId = lead.ChapterId,
                Index = lead.Number,
                Type = lead.Lead.Type ?? string.Empty,
                Key = lead.ObjKey,
                Arg = lead.ChipArg,
                Done = LeadDone(s, lead),
                Current = lead == cache.Current,
                StandIn = lead.StandIn,
            });
        }

        if (cache.Current is null && cache.ExploreKey.Length > 0 && ChapterAt(cache.ChapterIndex) is { } chapter)
        {
            rows.Add(new NetStoryLead
            {
                ChapterId = chapter.Id,
                Index = 0,
                Type = StoryLeadTypes.Explore,
                Key = cache.ExploreKey,
                Current = true,
                StandIn = true,
            });
        }

        return rows.ToArray();
    }

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: the player's resolved leads (resolves when the signature changed).</summary>
    internal IReadOnlyList<ResolvedStoryLead> LeadsForTest(PlayerSession s) => LeadsFor(s).Leads;

    /// <summary>Test seam: the player's current lead, or null (explore / nothing).</summary>
    internal ResolvedStoryLead? CurrentLeadForTest(PlayerSession s) => LeadsFor(s).Current;

    /// <summary>Test seam: how many lead resolutions ran so far.</summary>
    internal int LeadResolutionsForTest => _leadResolutions;

    /// <summary>Test seam (#2506): the lead listener is on the event bus.</summary>
    internal bool LeadListenerActiveForTest => _leadListener is not null;

    /// <summary>Test seam: the 1 Hz lead pass for one player.</summary>
    internal void TickStoryLeadsForTest(PlayerSession s) => TickStoryLeads(s);

    /// <summary>Test seam: the world story's objective for a player (key, arg, source).</summary>
    internal (string Key, string Arg, string Source) StoryObjectiveForTest(PlayerSession s) => StoryObjectiveFor(s);

    /// <summary>Test seam: the one scale every reader of the score uses.</summary>
    internal float StoryScaleForTest => StoryScale;

    /// <summary>Test seam: the score as shown (clamped).</summary>
    internal int StoryProgressShownForTest => StoryProgressShown();

    /// <summary>Test seam: the Journal's lead rows for a player.</summary>
    internal NetStoryLead[] JournalLeadsForTest(PlayerSession s) => JournalLeads(s);

    /// <summary>Test seam: records that a unique site's world skipped the site (a world stamped before the sites existed,
    /// #1129) — what the first load of such a world writes.</summary>
    internal void RecordUniqueSiteSkipForTest(string bodyId, string siteKind)
    {
        _meta.Placements.Add(new Shared.State.StructurePlacementRecord
        {
            LocationId = bodyId,
            Kind = "unique:" + UniqueSiteKey(siteKind),
            Index = 0,
            Placed = false,
        });
        foreach (var s in _sessions.Values)
        {
            s.Story.StoryLeads.Valid = false;
        }
    }
}
