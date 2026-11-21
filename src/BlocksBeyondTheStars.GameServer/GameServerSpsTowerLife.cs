// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// THE LIFE OF A GUIDE TOWER (#3895, #3896; canon C19 in docs/developer/LORE_STRUCTURE.md, the how-it-works in
/// docs/developer/SPS_TOWERS.md §8) — what the ruin the tower stamp left behind (GameServerSpsTowers*.cs) DOES. Everything
/// here reads and writes the tower's record (<see cref="SpsTowerRecord"/>) through the stamp's accessors.
/// <list type="bullet">
/// <item><b>The relay console without a chip</b> (<see cref="UseTowerConsole"/>, the tower branch of the one
/// <c>sps_console</c> use handler): every use shows the next record of the console's memory the player has not read — lore
/// texts of the site kind <see cref="SpsTowerLoreSite"/> in the VEGA pack's <c>lore_sites.json</c>, read in a fixed order
/// (<see cref="SpsTowerRecordTexts"/>), then ONE piece of ECHO's last call per tower (<see cref="SpsTowerEchoTexts"/> — three
/// pieces, so three towers tell the whole call). A line says that the console is locked red and what a hacking chip does.</item>
/// <item><b>The chip</b> (<see cref="LightSpsTower"/>): with a <see cref="SpsTowerChipItem"/> in the backpack the use takes ONE
/// and lights the tower, once per tower — every lamp of <see cref="SpsTowerRecord.TowerLamps"/> turns from red to blue (real
/// block changes, stored at once; the clients see them sweep from the foot to the top over
/// <see cref="SpsTowerLampSweepSeconds"/>), the cue <see cref="SpsTowerLightsSound"/> plays at the console,
/// <see cref="SpsTowerRecord.Lit"/> is set, <see cref="GameEventKeys.SpsTowerLit"/> is raised, the console plays the tower's
/// message (the pack's cutscene <see cref="SpsTowerMessageCutscene"/> when it has one, else the next record) and the tower
/// shows what it still knows of its world (<see cref="RevealWhatSpsTowerKnows"/>).</item>
/// <item><b>A player who comes later</b> finds the tower blue, reads the same records, and their first use of the lit console
/// counts as their own <see cref="GameEventKeys.SpsTowerLit"/> (once per player and tower — no chip is taken on a lit
/// tower, ever).</item>
/// </list>
/// VEGA's lines, the settlers' words, the story predicates and the guide marker are in GameServerSpsTowerLifeStory.cs; the
/// guard post's capsule tank and the sleeper in GameServerSleeper*.cs.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The lore-site kind of a relay console (the VEGA pack's <c>lore_sites.json</c>) — the argument of
    /// <c>lore:read</c> for every text a console shows.</summary>
    internal const string SpsTowerLoreSite = SpsTowerRules.MapType;

    /// <summary>The key item of the tower console and the capsule tank.</summary>
    internal const string SpsTowerChipItem = "hacking_chip";

    /// <summary>The sound cue of a tower's lamps turning blue (played at the console).</summary>
    internal const string SpsTowerLightsSound = "lights_to_blue";

    /// <summary>The VEGA pack's cutscene of the tower's message, played for the player who lit a tower — only when the pack
    /// defines it; without it the console shows its next record instead.</summary>
    internal const string SpsTowerMessageCutscene = "tower.message";

    /// <summary>How long the blue runs up a tower, from its lowest lamp to its highest (seconds).</summary>
    internal const double SpsTowerLampSweepSeconds = 2.0;

    /// <summary>How far the lighting of a tower is told (blocks from the console): the line and VEGA's words for bystanders.</summary>
    internal const float SpsTowerWitnessRange = 64f;

    /// <summary>The console's four records, in the order it shows them (lore keys of the site <see cref="SpsTowerLoreSite"/>):
    /// the colony ships led in, the recall order, the night the lamps turned red, a guard's last entry.</summary>
    internal static readonly string[] SpsTowerRecordTexts = { "tower_ships", "tower_recall", "tower_red_night", "tower_guard_log" };

    /// <summary>ECHO's last call in three pieces (lore keys). A tower holds ONE piece for a player — the next one they have
    /// not heard — and gives it once its four records are read, or as the tower's message when it is lit.</summary>
    internal static readonly string[] SpsTowerEchoTexts = { "tower_echo_1", "tower_echo_2", "tower_echo_3" };

    /// <summary>Lamp changes still to be told to the clients (the blue running up a tower): due uptime, body, cell, block.</summary>
    private readonly List<(double Due, string LocationId, Vector3i Cell, ushort Block)> _spsLampQueue = new();

    /// <summary>Per world: when the tower life's 1 Hz pass runs next.</summary>
    private readonly Dictionary<string, double> _spsTowerLifeTickAt = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- the per-player once-keys

    /// <summary>The per-player key of "this tower's light counts for me" (the lighter, or a later reader of the lit console).</summary>
    internal static string SpsTowerLitKey(string siteId) => "story:sps_tower:lit:" + siteId;

    /// <summary>The per-player key of "this tower gave me its piece of ECHO's call".</summary>
    internal static string SpsTowerEchoKey(string siteId) => "story:sps_tower:echo:" + siteId;

    /// <summary>The per-player key of VEGA's first words at a tower (the approach).</summary>
    internal static string SpsTowerSeenKey(string siteId) => "story:sps_tower:seen:" + siteId;

    /// <summary>The per-player key of VEGA's words about a tower's records (the first read there).</summary>
    internal static string SpsTowerReadKey(string siteId) => "story:sps_tower:read:" + siteId;

    // ---------------------------------------------------------------- the console

    /// <summary>The guide towers' branch of the <c>sps_console</c> use (declared next to the handler in
    /// GameServerEscortCruiserBridge.cs): a console that is a tower's relay console is the tower's.</summary>
    partial void UseTowerConsole(PlayerSession s, Vector3i cell, ref bool handled)
    {
        if (FindSpsTowerByConsole(cell) is not { } tower)
        {
            return; // a console that belongs to no tower: nothing of ours
        }

        handled = true;
        UseSpsTowerConsole(s, tower);
    }

    /// <summary>One use of a tower's relay console: with a chip an unlit tower is lit; otherwise the console shows its next
    /// record, and says what it is waiting for.</summary>
    private void UseSpsTowerConsole(PlayerSession s, SpsTowerRecord tower)
    {
        if (!tower.Lit)
        {
            if (s.State.Inventory.Has(SpsTowerChipItem, 1))
            {
                LightSpsTower(s, tower);
                return;
            }

            if (ReadSpsTowerConsole(s, tower))
            {
                SpeakSpsTowerRead(s, tower);
            }

            Send(s, new ServerMessage { Text = "@srv.sps_tower.console_locked" });
            return;
        }

        CreditLitSpsTower(s, tower); // a later player's own story step counts — no chip, no second lighting
        if (ReadSpsTowerConsole(s, tower))
        {
            SpeakSpsTowerRead(s, tower);
        }
        else
        {
            Send(s, new ServerMessage { Text = "@srv.sps_tower.console_done" });
        }
    }

    /// <summary>Shows the next record of a console the player has not read: the four records in their order, then this tower's
    /// piece of ECHO's call (once the four are read, or at once at a lit tower). False when the console has nothing new for
    /// them — or the world's story holds no tower texts.</summary>
    private bool ReadSpsTowerConsole(PlayerSession s, SpsTowerRecord tower)
    {
        var read = s.State.Milestones;
        foreach (string key in SpsTowerRecordTexts)
        {
            if (!read.Contains(LoreMilestonePrefix + key) && TryRevealLoreText(s, SpsTowerLoreSite, l => l.Key == key))
            {
                return true;
            }
        }

        return ReadSpsTowerEcho(s, tower);
    }

    /// <summary>This tower's piece of ECHO's last call for the player: the first piece they have not heard, once per tower.</summary>
    private bool ReadSpsTowerEcho(PlayerSession s, SpsTowerRecord tower)
    {
        var read = s.State.Milestones;
        string onceKey = SpsTowerEchoKey(tower.SiteId);
        if (read.Contains(onceKey))
        {
            return false; // this tower told them its piece; the next piece is another tower's
        }

        foreach (string key in SpsTowerEchoTexts)
        {
            if (!read.Contains(LoreMilestonePrefix + key) && TryRevealLoreText(s, SpsTowerLoreSite, l => l.Key == key))
            {
                read.Add(onceKey);
                _repo.SavePlayer(s.State);
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- the chip

    /// <summary>Lights a tower with ONE hacking chip of the player (see the class summary). The caller checked that the tower
    /// is not lit and that the player carries a chip.</summary>
    private void LightSpsTower(PlayerSession s, SpsTowerRecord tower)
    {
        var p = s.State;
        p.Inventory.Remove(SpsTowerChipItem, 1);
        tower.Lit = true;
        p.Milestones.Add(SpsTowerLitKey(tower.SiteId));
        int lamps = 0;
        _repo.RunInTransaction(() =>
        {
            lamps = TurnSpsLampsBlue(tower.TowerLamps, SpsTowerLampSweepSeconds);
            _repo.SaveMetadata(_meta);
            _repo.SavePlayer(p);
        });
        SendInventory(s);

        var at = tower.Console is { } console ? CellCentre(console.ToVector()) : SpsTowerCentre(tower);
        BroadcastToWorld(new SoundFx { SoundId = SpsTowerLightsSound, X = at.X, Y = at.Y, Z = at.Z });
        _log.Info($"'{p.Name}' lit the guide tower '{tower.SiteId}' ({lamps} lamp(s) turned blue).");
        RaiseGameEvent(s, GameEventKeys.SpsTowerLit, tower.SiteId);

        var (settlements, sites) = RevealWhatSpsTowerKnows(tower);
        foreach (var w in SpsTowerWitnesses(s, at, SpsTowerWitnessRange))
        {
            if (w.State.Milestones.Add(SpsTowerSeenKey(tower.SiteId))) // they saw it turn blue: no "somebody was here before us" later
            {
                _repo.SavePlayer(w.State);
            }

            Send(w, new ServerMessage { Text = "@srv.sps_tower.lit" });
            Send(w, new ServerMessage { Text = SpsTowerRevealText(w.Locale, settlements, sites) });
            SendVegaLine(w, SpsTowerVegaKey(w, SpsTowerVegaMoment.Lit), 2);
        }

        RevealSpsTowerInsight(s);
        PlaySpsTowerMessage(s, tower);
    }

    /// <summary>The tower's message for the player who lit it: the pack's cutscene when it defines one (once per player),
    /// else the console's next record — this tower's piece of ECHO's call first.</summary>
    private void PlaySpsTowerMessage(PlayerSession s, SpsTowerRecord tower)
    {
        string packId = StoryRegistry.DefaultStoryId;
        Vector3f? anchor = tower.Console is { } c ? CellCentre(c.ToVector()) : null;
        bool scene = SpsTowerStoryRuns && _content.Stories.TryGetValue(packId, out var pack) && pack.FindCutscene(SpsTowerMessageCutscene) is not null
                     && PlayCutscene(s, packId, SpsTowerMessageCutscene, anchor: anchor);
        if (!scene && !ReadSpsTowerEcho(s, tower))
        {
            ReadSpsTowerConsole(s, tower);
        }
    }

    /// <summary>A later player at a lit tower: their own "the tower is lit" — the event once per player and tower.</summary>
    private void CreditLitSpsTower(PlayerSession s, SpsTowerRecord tower)
    {
        if (!s.State.Milestones.Add(SpsTowerLitKey(tower.SiteId)))
        {
            return;
        }

        _repo.SavePlayer(s.State);
        RaiseGameEvent(s, GameEventKeys.SpsTowerLit, tower.SiteId);
        RevealSpsTowerInsight(s);
    }

    /// <summary>Turns every red lamp of <paramref name="cells"/> into a blue one: the world's blocks at once (stored with the
    /// call), the clients told lamp by lamp from the lowest to the highest over <paramref name="sweepSeconds"/> (0 = at once).
    /// A cell that holds no red lamp any more is left alone. Returns how many turned.</summary>
    private int TurnSpsLampsBlue(IReadOnlyList<SpsTowerCell> cells, double sweepSeconds)
    {
        ushort red = _content.GetBlock(SpsTowerRules.LampBlock)?.NumericId.Value ?? 0;
        ushort redOff = _content.GetBlock(SpsTowerRules.LampBlock + "_off")?.NumericId.Value ?? 0;
        ushort blue = _content.GetBlock(SpsTowerRules.LitLampBlock)?.NumericId.Value ?? 0;
        if (red == 0 || blue == 0 || cells.Count == 0)
        {
            return 0;
        }

        int low = cells.Min(c => c.Y), high = cells.Max(c => c.Y), turned = 0;
        foreach (var lamp in cells)
        {
            var cell = WorldConstants.CanonicalBlock(lamp.ToVector(), _world.Circumference);
            ushort now = _world.GetBlock(cell).Value;
            if (now != red && (redOff == 0 || now != redOff))
            {
                continue;
            }

            _world.SetBlock(cell, new BlockId(blue));
            double delay = sweepSeconds <= 0 || high == low ? 0 : sweepSeconds * (lamp.Y - low) / (high - low);
            _spsLampQueue.Add((_uptime + delay, _world.LocationId, cell, blue));
            turned++;
        }

        FlushSpsLampQueue();
        return turned;
    }

    /// <summary>Tells the clients of the active world the lamp changes that are due (the rest waits for a later tick).</summary>
    private void FlushSpsLampQueue()
    {
        if (_spsLampQueue.Count == 0)
        {
            return;
        }

        string here = _world.LocationId;
        for (int i = _spsLampQueue.Count - 1; i >= 0; i--)
        {
            var (due, location, cell, block) = _spsLampQueue[i];
            if (location == here && due <= _uptime)
            {
                // Moved: a switch, not a hand edit — the clients play no place pop for it.
                BroadcastToWorld(new BlockChanged { X = cell.X, Y = cell.Y, Z = cell.Z, Block = block, Moved = true });
                _spsLampQueue.RemoveAt(i);
            }
        }
    }

    // ---------------------------------------------------------------- what the tower still knows

    /// <summary>
    /// A lit tower shows what it still knows of its world (the active one): every place the Service charted goes on everyone's
    /// map — the landmark, the giant ship wrecks, the sites of the lost civilizations, the world's one-of-a-kind place (the
    /// observatory or the singing shrine), the crashed ship that nobody claimed, and the other guide towers. The settlements
    /// (lived-in and ruined) are on every map from the start; they are only counted for the line. Never revealed: what came
    /// after the towers went dark or is somebody's secret — huts and outposts, treasure chests, bandit camps, fragment
    /// signals, hunter camps — and the guardian giants, which the players find themselves. Returns the settlements of the world
    /// and how many places were newly put on the map (the lit tower itself goes on the map too, uncounted).
    /// </summary>
    private (int Settlements, int Sites) RevealWhatSpsTowerKnows(SpsTowerRecord lit)
    {
        string here = _world.LocationId;
        string own = SpsTowerRevealKey(lit.LocationId, lit.Index);
        bool ownAdded = !_meta.RevealedPois.Contains(own);
        if (ownAdded)
        {
            _meta.RevealedPois.Add(own);
        }

        var keys = new List<string>();
        keys.AddRange(_landmarks.Select(l => l.RevealKey));
        keys.AddRange(_giantWrecks.Select(l => l.RevealKey));
        keys.AddRange(_civSites.Select(l => l.RevealKey));
        keys.AddRange(_spsTowers.Select(l => l.RevealKey));
        if (_wreckStamped && !_wreckClaimed)
        {
            keys.Add(here + "|wreck");
        }

        foreach (string siteKey in new[] { ShrineSiteKey, ObservatorySiteKey })
        {
            string? body = siteKey == ShrineSiteKey ? ShrineBodyId() : ObservatoryBodyId();
            if (body == here && FindPlacementRecord("unique:" + siteKey, 0) is { Placed: true })
            {
                keys.Add(here + "|uniquesite:" + siteKey);
            }
        }

        int added = 0;
        foreach (string key in keys)
        {
            if (key.Length > 0 && !_meta.RevealedPois.Contains(key))
            {
                _meta.RevealedPois.Add(key);
                added++;
            }
        }

        if (added > 0 || ownAdded)
        {
            _repo.SaveMetadata(_meta);
            BroadcastPlanetPois();
        }

        return (_settlements.Count, added);
    }

    /// <summary>The line that says what a lit tower put on the map, in the player's language.</summary>
    private string SpsTowerRevealText(string locale, int settlements, int sites)
        => Localize(locale, sites > 0 ? "srv.sps_tower.revealed" : "srv.sps_tower.revealed_settlements")
            .Replace("{settlements}", settlements.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{count}", sites.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>The player who acted and everybody else on foot on the active world within <paramref name="range"/> of
    /// <paramref name="at"/> (observers left out).</summary>
    private List<PlayerSession> SpsTowerWitnesses(PlayerSession actor, Vector3f at, float range)
    {
        var list = new List<PlayerSession> { actor };
        foreach (var s in JoinedInActiveWorld())
        {
            if (!ReferenceEquals(s, actor) && !s.Spectating && !InSpace(s.State.PlayerId) && WrapDistSq(s.State.Position, at) <= (double)range * range)
            {
                list.Add(s);
            }
        }

        return list;
    }

    private static Vector3f CellCentre(Vector3i cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);

    // ---------------------------------------------------------------- the tick

    /// <summary>
    /// The tower life's per-world step. Costs one compare in a save without towers. Every tick: the lamp changes that are due
    /// and the sleepers' conversations (only while something waits). Once a second, on a world that holds a tower: the
    /// sleepers of the world stand where they belong, VEGA speaks to a player who first comes near a tower, and says what she
    /// thinks of the sleeper to one who talked with him.
    /// </summary>
    private void TickSpsTowerLife(double dt)
    {
        _ = dt;
        if (_meta.SpsTowers.Count == 0 || _worlds.Active is null)
        {
            return;
        }

        FlushSpsLampQueue();
        TickSleeperTalks();

        string world = _world.LocationId;
        if (_spsTowerLifeTickAt.TryGetValue(world, out double due) && _uptime < due)
        {
            return;
        }

        _spsTowerLifeTickAt[world] = _uptime + 1.0;
        if (!AnySpsTowerOn(world))
        {
            return;
        }

        EnsureSleepersPlaced();
        foreach (var s in JoinedInActiveWorld())
        {
            if (s.Watching || !OnFootHere(s))
            {
                continue;
            }

            SpsTowerApproach(s);
            TickSleeperAftermath(s);
        }
    }

    /// <summary>Whether body <paramref name="locationId"/> holds a guide tower (no allocation).</summary>
    private bool AnySpsTowerOn(string locationId)
    {
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId == locationId)
            {
                return true;
            }
        }

        return false;
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam: uses a tower's relay console as the use key aimed at it would (reach is not checked). False when the
    /// tower has no console or does not stand on the active world.</summary>
    internal bool UseSpsTowerConsoleForTest(PlayerSession s, SpsTowerRecord tower)
    {
        if (tower.Console is not { } console || tower.LocationId != _world.LocationId)
        {
            return false;
        }

        Serve(s);
        bool handled = false;
        UseTowerConsole(s, console.ToVector(), ref handled);
        return handled;
    }

    /// <summary>Test seam: one pass of the tower life right now (the 1 Hz part included).</summary>
    internal void TickSpsTowerLifeForTest()
    {
        _spsTowerLifeTickAt.Remove(_world.LocationId);
        TickSpsTowerLife(0);
    }

    /// <summary>Test seam: how many lamp changes still wait to be told to the clients.</summary>
    internal int SpsLampQueueForTest => _spsLampQueue.Count;
}
