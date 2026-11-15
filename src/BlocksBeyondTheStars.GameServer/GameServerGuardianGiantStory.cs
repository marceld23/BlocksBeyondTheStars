// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The guardian giants in the story "The VEGA Protocol" (#3685; canon C18 in docs/developer/LORE_STRUCTURE.md: the Service's own
/// construction giants, taken by the Guardian core in the cull). The giants' runtime is GameServerGuardianGiants*.cs; this part
/// only ties them into the story:
/// <list type="bullet">
/// <item><b>The score and a memory.</b> <see cref="GameEventKeys.GuardianGiantDefeated"/> (raised once per fighter) counts the
/// giant as <see cref="GuardianGiantStoryKills"/> machine kills — once per giant, inside the pack's kill cap like every kill —
/// and always unlocks that fighter's next personal memory (C11; an ordinary machine drops one by chance).</item>
/// <item><b>The scanner.</b> Any block of a lying or fallen giant — and the awake giant itself — reads as the giant (a relic's
/// knowledge and a Codex discovery once per giant; <c>scan:any</c> / <c>scan:performed</c> with <see cref="GuardianGiantScanKind"/>),
/// and VEGA remembers the builders (the insight <see cref="GuardianGiantInsightEvent"/>, after the Guardian's beat).</item>
/// <item><b>The mission</b> "The Fallen Giant" (chapter 4, pack data): <see cref="GameEventKeys.GuardianGiantFound"/> when a
/// player comes within the found range (once per approach, measured only while a step listens), the predicates
/// <c>world:guardian_giants</c> and <c>world:guardian_giant_near</c>.</item>
/// <item><b>The memory core</b> (<see cref="GuardianGiantCoreItem"/>): carried, VEGA reads its last order — the field record of
/// <see cref="GuardianGiantCoreLoreSite"/> — into the Journal, once per player.</item>
/// <item><b>VEGA's giant lines by what the player's story knows</b> (<see cref="StoryGuardianGiantVegaKey"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How many machine kills a defeated guardian giant counts for toward the story score (once per giant). The pack's
    /// kill cap (<c>killContributionCap</c>) still bounds what all kills together bring.</summary>
    internal const int GuardianGiantStoryKills = 5;

    /// <summary>The story insight a scanned guardian giant speaks (VEGA remembers the Service's builders).</summary>
    internal const string GuardianGiantInsightEvent = "guardian_giant";

    /// <summary>The scan readout's kind of a guardian giant — the argument of its <c>scan:any</c> and <c>scan:performed</c>.</summary>
    internal const string GuardianGiantScanKind = GuardianGiantRules.StructureKind;

    /// <summary>The lore-site kind of the memory core's field record (<c>lore_sites.json</c> of the VEGA pack).</summary>
    internal const string GuardianGiantCoreLoreSite = "guardian_giant_core";

    /// <summary>The VEGA beat after which a player's story knows the Service (B2, "The Service").</summary>
    private const int GuardianGiantServiceBeat = 2;

    /// <summary>The VEGA beat after which a player's story knows the Guardian (B5, "The Guardian").</summary>
    private const int GuardianGiantGuardianBeat = 5;

    /// <summary>Per player: the giant whose found range they stand in right now (the edge of <c>guardian_giant:found</c>).</summary>
    private readonly Dictionary<string, string> _guardianGiantNearBy = new(StringComparer.Ordinal);

    private GuardianGiantStoryListener? _guardianGiantStoryListener;

    /// <summary>What a player's story knows for VEGA's giant lines (#3685).</summary>
    internal enum GuardianGiantLore
    {
        /// <summary>No VEGA story here: the neutral lines.</summary>
        Neutral,

        /// <summary>The player's story has not reached the Service (B2): VEGA has no record of machines this big.</summary>
        Puzzled,

        /// <summary>The Service is known (B2): she recognises one of its builders.</summary>
        Service,

        /// <summary>The Guardian is known (B5): she names its hand in it.</summary>
        Guardian,

        /// <summary>The Guardian core is shut down: the core is silent, but this one never heard.</summary>
        Silent,
    }

    // ---------------------------------------------------------------------------------------------
    // The score and the memory.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The bus listener of the defeat event — registered while a world story runs (with the lead listener's sync).</summary>
    private void SyncGuardianGiantStoryListener()
    {
        bool want = _story is not null;
        if (want == (_guardianGiantStoryListener is not null))
        {
            return;
        }

        if (want)
        {
            _guardianGiantStoryListener = new GuardianGiantStoryListener(this);
            AddGameEventListener(GameEventKeys.GuardianGiantDefeated, _guardianGiantStoryListener);
        }
        else
        {
            RemoveGameEventListener(GameEventKeys.GuardianGiantDefeated, _guardianGiantStoryListener!);
            _guardianGiantStoryListener = null;
        }
    }

    /// <summary>A fighter of a defeated giant: the giant's share of the score once (the first fighter's event books it), and the
    /// fighter's next personal memory — once per giant and player.</summary>
    private void OnGuardianGiantDefeatedForStory(PlayerSession s, string siteId)
    {
        if (_story is null || string.IsNullOrEmpty(siteId))
        {
            return;
        }

        if (_storyState.MilestoneKeys.Add(GuardianGiantStoryKey(siteId)))
        {
            _storyState.MachineKills += GuardianGiantStoryKills;
            AdvanceStory(); // reveals what the share crossed, persists the state (with the once-key), re-sends the meter
        }

        if (s.State.Milestones.Add(GuardianGiantMemoryKey(siteId)))
        {
            UnlockNextPlayerMemory(s); // C11: a giant always releases one (none left: nothing more to remember)
            _repo.SavePlayer(s.State);
        }
    }

    /// <summary>The once-key of a giant's score share (<see cref="StoryState.MilestoneKeys"/>).</summary>
    internal static string GuardianGiantStoryKey(string siteId) => "guardian_giant:" + siteId;

    /// <summary>The per-player once-key of a giant's memory.</summary>
    internal static string GuardianGiantMemoryKey(string siteId) => "story:giant_memory:" + siteId;

    /// <summary>The defeat event's listener on the game-event bus.</summary>
    private sealed class GuardianGiantStoryListener : IGameEventListener
    {
        private readonly GameServer _server;

        public GuardianGiantStoryListener(GameServer server) => _server = server;

        public void OnGameEvent(PlayerSession actor, string key, string arg, int amount) => _server.OnGuardianGiantDefeatedForStory(actor, arg);
    }

    // ---------------------------------------------------------------------------------------------
    // The 1 Hz part: found, and the memory core.
    // ---------------------------------------------------------------------------------------------

    /// <summary>1 Hz per player from the story pass: a giant found (while a step listens) and a carried memory core read.</summary>
    private void TickGuardianGiantStory(PlayerSession s)
    {
        TickGuardianGiantFound(s);
        ReadGuardianGiantCore(s);
    }

    /// <summary>Raises <see cref="GameEventKeys.GuardianGiantFound"/> when the player comes within the found range of a giant
    /// (the nearest one changes from none or another) — measured only while a step listens, so it costs nothing otherwise.</summary>
    private void TickGuardianGiantFound(PlayerSession s)
    {
        string playerId = s.State.PlayerId;
        if (!HasGameEventListener(GameEventKeys.GuardianGiantFound))
        {
            _guardianGiantNearBy.Remove(playerId);
            return;
        }

        string? site = NearestGuardianGiantInFoundRange(s)?.SiteId;
        if (site is null)
        {
            _guardianGiantNearBy.Remove(playerId);
            return;
        }

        if (_guardianGiantNearBy.TryGetValue(playerId, out string? before) && before == site)
        {
            return;
        }

        _guardianGiantNearBy[playerId] = site;
        RaiseGameEvent(s, GameEventKeys.GuardianGiantFound, site);
    }

    /// <summary>The nearest guardian giant within <see cref="GuardianGiantRules.FoundRange"/> of the player on their own body (the
    /// awake one where it stands, a fallen one where it fell), or null — none there, or the player is in flight.</summary>
    private GuardianGiantRecord? NearestGuardianGiantInFoundRange(PlayerSession s)
    {
        if (_meta.GuardianGiants.Count == 0 || InSpace(s.State.PlayerId))
        {
            return null;
        }

        string location = s.CurrentLocationId;
        _guardianGiantWorlds.TryGetValue(location, out var gw);
        GuardianGiantRecord? nearest = null;
        double best = GuardianGiantRules.FoundRange * GuardianGiantRules.FoundRange;
        foreach (var rec in _meta.GuardianGiants.Values)
        {
            if (rec.LocationId != location)
            {
                continue;
            }

            double d = FlatDistSq(s.State.Position, GuardianGiantCentre(rec, gw));
            if (d <= best)
            {
                best = d;
                nearest = rec;
            }
        }

        return nearest;
    }

    /// <summary><c>world:guardian_giants</c>: a giant lies in this save already, or the world is of the giants' terrain generation
    /// and their structure kind is not switched off — a body stamped from now on may carry one.</summary>
    private bool GuardianGiantsInWorld()
        => _meta.GuardianGiants.Count > 0
           || (_meta.Description.TerrainGeneration >= WorldDescription.GuardianGiantsGeneration
               && _content.StructureTypes.Get(GuardianGiantRules.StructureKind).Enabled);

    /// <summary>VEGA reads a carried memory core: the core's field record opens in the reader and stays in the Journal — once per
    /// player (the lore ledger dedupes), only with a story whose lore holds one.</summary>
    private void ReadGuardianGiantCore(PlayerSession s)
    {
        if (_story is not null && UnreadStoryLoreAt(s, GuardianGiantCoreLoreSite) && s.State.Inventory.Has(GuardianGiantCoreItem, 1))
        {
            TryRevealLoreText(s, GuardianGiantCoreLoreSite);
        }
    }

    /// <summary>Whether the world story holds a field record of <paramref name="site"/> this player has not read (a few string
    /// compares — the 1 Hz check stays cheap once the core is read).</summary>
    private bool UnreadStoryLoreAt(PlayerSession s, string site)
    {
        foreach (var lore in _story?.LoreSites ?? new List<LoreSite>())
        {
            if (string.Equals(lore.Site, site, StringComparison.OrdinalIgnoreCase) && !s.State.Milestones.Contains(LoreMilestonePrefix + lore.Key))
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------------------------------------
    // The scanner.
    // ---------------------------------------------------------------------------------------------

    /// <summary>The lying or fallen giant whose body holds the aimed cell on the player's (active) world, or null. The cell must
    /// be loaded, hold one of the giant's blocks and lie in its lying box.</summary>
    private GuardianGiantRecord? GuardianGiantForScan(PlayerSession session, Vector3i? cell)
    {
        if (_meta.GuardianGiants.Count == 0 || cell is not { } aimed || session.CurrentLocationId != _worlds.Active.LocationId
            || !IsGuardianGiantBlock(_content.BlockById(_world.GetBlockIfLoaded(aimed))?.Key))
        {
            return null;
        }

        foreach (var rec in _meta.GuardianGiants.Values)
        {
            if (rec.LocationId == _world.LocationId && rec.State != GuardianGiantRecord.Awake && InGuardianGiantBody(rec, aimed))
            {
                return rec;
            }
        }

        return null;
    }

    /// <summary>Whether a cell lies in the lying box of a giant's body (where it rests, or where it fell).</summary>
    private bool InGuardianGiantBody(GuardianGiantRecord rec, Vector3i cell)
    {
        bool dead = rec.State == GuardianGiantRecord.Defeated;
        int x = dead ? rec.CorpseX : rec.X, y = dead ? rec.CorpseY : rec.Y, z = dead ? rec.CorpseZ : rec.Z;
        var (minX, minZ, maxX, maxZ) = GuardianGiantBody.LyingFootprint(dead ? rec.CorpseFacing : rec.Facing);
        int dx = WorldConstants.WrapDeltaX(cell.X - x, _world.Circumference), dy = cell.Y - y, dz = cell.Z - z;
        return dx >= minX && dx <= maxX && dz >= minZ && dz <= maxZ
               && dy >= GuardianGiantBody.LyingBottom && dy <= GuardianGiantBody.LyingTop;
    }

    /// <summary>Whether a block key is one the giant's body is built of.</summary>
    private static bool IsGuardianGiantBlock(string? key)
        => key is GuardianGiantBody.Plate or GuardianGiantBody.Rust or GuardianGiantBody.Dark or GuardianGiantBody.Trim
            or GuardianGiantBody.LightOn or GuardianGiantBody.LightOff;

    /// <summary>The awake giant of the player's (active) world when they stand within its fight range, or null — the subject of a
    /// scan of kind <see cref="GuardianGiantScanKind"/> (a client aiming at the standing giant).</summary>
    private GuardianGiantRecord? AwakeGuardianGiantForScan(PlayerSession session)
    {
        if (session.CurrentLocationId != _worlds.Active.LocationId
            || !_guardianGiantWorlds.TryGetValue(_world.LocationId, out var gw) || gw.Fight is not { Entity: { } giant } fight)
        {
            return null;
        }

        const float r = GuardianGiantRules.FightRange;
        return FlatDistSq(session.State.Position, giant.Position) <= r * r ? fight.Rec : null;
    }

    /// <summary>Fills the scanner's readout for a giant and returns its ledger key (once per giant): the giant by name, what its
    /// state says (lying, awake, fallen) and its threat.</summary>
    private string FillGuardianGiantReadout(ScanReadout readout, GuardianGiantRecord rec, string locale)
    {
        readout.Kind = GuardianGiantScanKind;
        readout.SubjectKey = GuardianGiantScanKind;
        readout.Display = Localize(locale, "ui.scan.subject.guardian_giant");
        (readout.InfoKey, readout.ThreatKey, readout.LegacyInfo) = rec.State switch
        {
            GuardianGiantRecord.Awake => ("ui.scan.guardian_giant_awake", "ui.scan.threat.hostile", "It is awake."),
            GuardianGiantRecord.Defeated => ("ui.scan.guardian_giant_defeated", "ui.scan.threat.inert", "Its lights are out for good."),
            _ => ("ui.scan.guardian_giant", string.Empty, "A huge fallen machine."), // whether it can wake stays its secret
        };
        return "guardian_giant:" + rec.SiteId;
    }

    /// <summary>After a giant's scan: VEGA remembers the Service's builders (the insight — held until the Guardian's beat, once).</summary>
    private void OnGuardianGiantScanned(PlayerSession session) => RevealStoryInsight(session, GuardianGiantInsightEvent);

    // ---------------------------------------------------------------------------------------------
    // VEGA's lines.
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The story's choice of a giant line (#3685) — <paramref name="neutralKey"/> is the giants' own line of the moment. With the
    /// VEGA story (its data pack) and a moment that has variants, the line follows what THIS player's story knows: before the
    /// Service (B2) VEGA is puzzled ("Machines this big? I have no record of them …"), knowing the Service she recognises one of
    /// its builders, knowing the Guardian (B5) she names its hand in it, and after the core is shut down "the core is silent, but
    /// this one never heard". The Guardian is never named before the player's own story heard of it. Key:
    /// <c>story.vega.giant.&lt;moment&gt;.&lt;puzzled|service|guardian|silent&gt;</c> in the pack's locale.
    /// </summary>
    internal string StoryGuardianGiantVegaKey(PlayerSession s, GuardianGiantVegaMoment moment, string neutralKey)
    {
        var lore = GuardianGiantLoreOf(s);
        return lore == GuardianGiantLore.Neutral || !GuardianGiantMomentHasStoryLines(moment)
            ? neutralKey
            : "story.vega.giant." + GuardianGiantMomentWord(moment) + "." + lore.ToString().ToLowerInvariant();
    }

    /// <summary>What the player's story knows for the giant lines: the VEGA data pack's beats this player heard, and the finale.</summary>
    internal GuardianGiantLore GuardianGiantLoreOf(PlayerSession s)
    {
        if (_story is null || ReferenceEquals(_story, StoryRegistry.Default)
            || !string.Equals(_story.Id, StoryRegistry.DefaultStoryId, StringComparison.OrdinalIgnoreCase))
        {
            return GuardianGiantLore.Neutral; // no VEGA story — or only its built-in stand-in, which carries no pack texts
        }

        var heard = s.State.Milestones;
        if (heard.Contains(StoryPackKeys.Beat(_story.Id, GuardianGiantGuardianBeat)))
        {
            return _storyState.GuardianDefeated ? GuardianGiantLore.Silent : GuardianGiantLore.Guardian;
        }

        return heard.Contains(StoryPackKeys.Beat(_story.Id, GuardianGiantServiceBeat)) ? GuardianGiantLore.Service : GuardianGiantLore.Puzzled;
    }

    /// <summary>The moments the story tells its own way (the others keep the neutral line).</summary>
    internal static bool GuardianGiantMomentHasStoryLines(GuardianGiantVegaMoment moment)
        => moment is GuardianGiantVegaMoment.Found or GuardianGiantVegaMoment.Stir or GuardianGiantVegaMoment.Rise
            or GuardianGiantVegaMoment.Victory;

    /// <summary>A moment's word in the story keys.</summary>
    internal static string GuardianGiantMomentWord(GuardianGiantVegaMoment moment) => moment switch
    {
        GuardianGiantVegaMoment.Found => "found",
        GuardianGiantVegaMoment.Stir => "stir",
        GuardianGiantVegaMoment.Rise => "rise",
        GuardianGiantVegaMoment.Rage => "rage",
        GuardianGiantVegaMoment.Victory => "victory",
        _ => "lie_down",
    };

    // ---------------------------------------------------------------------------------------------
    // Test seams.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Test seam: the 1 Hz giant part of the story pass for one player.</summary>
    internal void TickGuardianGiantStoryForTest(PlayerSession s) => TickGuardianGiantStory(s);

    /// <summary>Test seam: a giant line's key for a player as the story picks it.</summary>
    internal string StoryGuardianGiantVegaKeyForTest(PlayerSession s, GuardianGiantVegaMoment moment, string neutralKey)
        => StoryGuardianGiantVegaKey(s, moment, neutralKey);
}
