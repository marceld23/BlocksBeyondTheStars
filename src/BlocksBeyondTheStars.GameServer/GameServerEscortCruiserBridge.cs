// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The bridge console of an escort cruiser's wreck, and ECHO (#3902, canon C22) — "red turns blue again".
/// <list type="bullet">
/// <item><b>The use</b>: the block <c>sps_console</c> has ONE use handler (<see cref="UseSpsConsole"/>). It asks the cruiser's
/// bridge first (<see cref="TryUseBridgeConsole"/>: the player stands aboard a cruiser's wreck) and otherwise hands the use
/// to the guide towers' hook <see cref="UseTowerConsole"/> (#3896).</item>
/// <item><b>Locked red</b>: without a hacking chip the console only says what opens it. With one, the chip is used up, every
/// red lamp of the bridge turns blue — real block changes of the interior, mirrored into the hull seen from space and stored
/// with the wreck's changes — and the record remembers it (a second chip is never taken).</item>
/// <item><b>ECHO's cruiser</b> (<see cref="CruiserWreckRecord.CarriesEcho"/>): VEGA's sister core, held silent since the red
/// night, speaks once with VEGA and says goodbye — the VEGA pack's cutscene <see cref="CruiserWreckRules.EchoCutscene"/> for
/// everyone aboard, and her lines as VEGA-panel lines with it (#3899: a client that plays the scene keeps them out of the
/// panel; one without cutscenes, or a player who was shown the scene already, reads them there). Everyone aboard gets <see cref="GameEventKeys.EchoFreed"/> once and ONE memory core
/// (<see cref="CruiserWreckRules.EchoCoreItem"/>, once per player and wreck; a fighter who comes later gets theirs at the
/// boarding); carried, VEGA reads its record (<see cref="CruiserWreckRules.EchoLoreKey"/>) into the Journal. ECHO never
/// becomes a companion.</item>
/// <item><b>A later cruiser</b>: the lamps turn blue and the console shows one of the ship's logs
/// (lore site <see cref="CruiserWreckRules.LoreSite"/>, never ECHO's record), no core.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The per-player once-key of <see cref="GameEventKeys.EchoFreed"/>.</summary>
    internal const string EchoFreedMilestone = "story:echo_freed";

    /// <summary>ECHO's lines (the VEGA pack's locale), in the order she speaks them; VEGA answers after the second and at the
    /// very end (<see cref="EchoVegaLines"/>).</summary>
    internal static readonly string[] EchoLines =
    {
        "story.vega.echo.1", "story.vega.echo.2", "story.vega.echo.3", "story.vega.echo.4", "story.vega.echo.5",
    };

    /// <summary>VEGA's two answers to ECHO.</summary>
    internal static readonly string[] EchoVegaLines = { "story.vega.echo.vega.1", "story.vega.echo.vega.2" };

    /// <summary>The locale key of ECHO's name on VEGA's panel.</summary>
    private const string SisterCoreNameKey = "chr.echo.name";

    /// <summary>The guide towers' relay console (#3896, implemented by the tower life): called for every
    /// <c>sps_console</c> use that is not a cruiser's bridge. Set <paramref name="handled"/> when the use was the tower's.</summary>
    partial void UseTowerConsole(PlayerSession s, Vector3i cell, ref bool handled);

    /// <summary>The use handler of the block <c>sps_console</c>: a cruiser's bridge first, else the guide towers'.</summary>
    private void UseSpsConsole(PlayerSession session, Vector3i cell)
    {
        if (TryUseBridgeConsole(session, cell))
        {
            return;
        }

        bool handled = false;
        UseTowerConsole(session, cell, ref handled);
    }

    /// <summary>The console on the bridge of a cruiser's wreck (the active world is its interior): locked red without a
    /// hacking chip; with one the lamps turn blue, once. False when the active world is no cruiser's wreck.</summary>
    private bool TryUseBridgeConsole(PlayerSession session, Vector3i cell)
    {
        string location = _world.LocationId;
        if (!location.StartsWith(StationLocationIdPrefix, System.StringComparison.Ordinal)
            || !_cruiserWrecks.TryGetValue(location.Substring(StationLocationIdPrefix.Length), out var site)
            || !_stationsById.TryGetValue(site.Id, out var station))
        {
            return false;
        }

        var record = site.Record;
        var p = session.State;
        if (record.ConsoleOpened)
        {
            Send(session, new ServerMessage { Text = "@srv.cruiser.console_done" });
            return true;
        }

        if (!p.Inventory.Has(CruiserWreckRules.ChipItem, 1))
        {
            Send(session, new ServerMessage { Text = "@srv.cruiser.console_locked" });
            return true;
        }

        p.Inventory.Remove(CruiserWreckRules.ChipItem, 1);
        record.ConsoleOpened = true;
        record.EchoFreed = record.CarriesEcho;
        _repo.RunInTransaction(() =>
        {
            _repo.SaveMetadata(_meta);
            _repo.SavePlayer(p);
        });
        SendInventory(session);
        int lamps = TurnBridgeLampsBlue(station, cell);
        BroadcastToWorld(new SoundFx { SoundId = CruiserWreckRules.LightsToBlueSound, X = cell.X + 0.5f, Y = cell.Y + 0.5f, Z = cell.Z + 0.5f });
        var aboard = JoinedInActiveWorld().Where(s => !s.Spectating).ToList();
        foreach (var s in aboard)
        {
            Send(s, new ServerMessage { Text = "@srv.cruiser.console_blue" });
        }

        _log.Info($"'{p.Name}' opened the bridge console of '{site.Id}' ({lamps} lamp(s) turned blue, ECHO: {record.CarriesEcho}).");
        if (record.CarriesEcho)
        {
            FreeEcho(record, aboard);
        }
        else
        {
            TryRevealLoreText(session, CruiserWreckRules.LoreSite, l => l.Key != CruiserWreckRules.EchoLoreKey); // one of the ship's logs
        }

        return true;
    }

    /// <summary>Every red lamp of the bridge — within <see cref="CruiserWreckRules.BridgeLampReach"/> of the console — becomes
    /// a blue one: in the interior world (told to everyone aboard), in the build (the hull seen from space) and so in the
    /// wreck's stored changes. Returns how many turned.</summary>
    private int TurnBridgeLampsBlue(BoardableStation station, Vector3i console)
    {
        ushort red = _content.GetBlock("light_red")?.NumericId.Value ?? 0, blue = _content.GetBlock("light_blue")?.NumericId.Value ?? 0;
        if (red == 0 || blue == 0)
        {
            return 0;
        }

        int turned = 0;
        string? changed = null;
        int circ = _world.Circumference;
        const int reach = CruiserWreckRules.BridgeLampReach, reachY = CruiserWreckRules.BridgeLampReachY;
        for (int dx = -reach; dx <= reach; dx++)
            for (int dy = -reachY; dy <= reachY; dy++)
                for (int dz = -reach; dz <= reach; dz++)
                {
                    var c = WorldConstants.CanonicalBlock(new Vector3i(console.X + dx, console.Y + dy, console.Z + dz), circ);
                    if (_world.GetBlock(c).Value == red)
                    {
                        changed = PutGiantWreckCell(c, new Shared.Primitives.BlockId(blue)) ?? changed;
                        turned++;
                    }
                }

        if (changed is not null)
        {
            PublishStationCells(changed);
        }

        return turned;
    }

    // ---------------------------------------------------------------- ECHO

    /// <summary>ECHO is free: she speaks once to everyone aboard (the cutscene, and her lines with it), each of them gets the
    /// event once and one memory core.</summary>
    private void FreeEcho(CruiserWreckRecord record, List<PlayerSession> aboard)
    {
        string packId = StoryRegistry.DefaultStoryId;
        bool sceneDefined = _content.Stories.TryGetValue(packId, out var pack) && pack.FindCutscene(CruiserWreckRules.EchoCutscene) is not null;
        foreach (var s in aboard)
        {
            if (sceneDefined)
            {
                PlayCutscene(s, packId, CruiserWreckRules.EchoCutscene); // once per player
            }

            // #3899: her words always go out, after the scene. A client that plays the scene keeps them out of VEGA's panel
            // (they are its replaceLineKeys) and gets them back when the scene is cut short (the wreck's robots do not wait
            // for a goodbye) — they reach the story log either way; any other client reads them in the panel.
            SpeakEchoLines(s);

            if (s.State.Milestones.Add(EchoFreedMilestone))
            {
                _repo.SavePlayer(s.State);
                RaiseGameEvent(s, GameEventKeys.EchoFreed);
            }

            GiveEchoCore(s, record);
        }
    }

    /// <summary>ECHO's goodbye as lines: she speaks under her own name in VEGA's panel, VEGA answers twice.</summary>
    private void SpeakEchoLines(PlayerSession s)
    {
        string echo = Localize(s.Locale, SisterCoreNameKey);
        for (int i = 0; i < EchoLines.Length; i++)
        {
            SendVegaWithObjective(s, new ShipAiLine { LineKey = EchoLines[i], Kind = 2, SpeakerName = echo });
            if (i == 1)
            {
                SendVegaLine(s, EchoVegaLines[0], 2);
            }
        }

        SendVegaLine(s, EchoVegaLines[1], 2);
    }

    /// <summary>ONE memory core of ECHO for a player, once per player and wreck: into the backpack, and onto the deck at
    /// their feet when it is full — never lost.</summary>
    private void GiveEchoCore(PlayerSession s, CruiserWreckRecord record)
    {
        string playerId = s.State.PlayerId;
        if (record.CoresGiven.Contains(playerId) || _content.GetItem(CruiserWreckRules.EchoCoreItem) is null)
        {
            return;
        }

        var items = new List<ItemStack> { new(CruiserWreckRules.EchoCoreItem, 1) };
        void Settle()
        {
            record.CoresGiven.Add(playerId);
            _repo.SaveMetadata(_meta);
        }

        if (!GiveOwnerCrateIntoBackpack(s, items, Settle))
        {
            _repo.RunInTransaction(Settle);
            SpillToGround(s.State.Position.ToBlock(), CruiserWreckRules.EchoCoreItem, 1);
        }

        Send(s, new ServerMessage { Text = "@srv.cruiser.echo_core" });
    }

    /// <summary>1 Hz per player from the story pass: VEGA reads a carried memory core of ECHO — its record opens in the
    /// reader and stays in the Journal, once per player, only with a story whose lore holds it.</summary>
    private void ReadEchoMemoryCore(PlayerSession s)
    {
        if (_story is null || s.State.Milestones.Contains(LoreMilestonePrefix + CruiserWreckRules.EchoLoreKey)
            || !s.State.Inventory.Has(CruiserWreckRules.EchoCoreItem, 1))
        {
            return;
        }

        TryRevealLoreText(s, CruiserWreckRules.LoreSite, l => l.Key == CruiserWreckRules.EchoLoreKey);
    }

    /// <summary>Test seam (#3902): the 1 Hz read of a carried memory core of ECHO.</summary>
    internal void ReadEchoMemoryCoreForTest(PlayerSession s) => ReadEchoMemoryCore(s);

    /// <summary>Test seam (#3902): uses the bridge console of the cruiser wreck the player stands aboard, as the use key
    /// aimed at it would (reach is not checked). False when the player is aboard no cruiser's wreck or it has no console.</summary>
    public bool UseBridgeConsoleForTest(string playerId)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || !_boardedStation.TryGetValue(playerId, out var id)
            || !_cruiserWrecks.TryGetValue(id, out var site) || site.Console is not { } console
            || !_stationsById.TryGetValue(id, out var station) || !_worlds.SetActive(StationLocationIdPrefix + id))
        {
            return false;
        }

        SetCurrent(session);
        UseSpsConsole(session, WorldConstants.CanonicalBlock(StationCellToWorld(station, console), _world.Circumference));
        return true;
    }
}
