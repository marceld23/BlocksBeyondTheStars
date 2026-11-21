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
/// Life aboard an escort cruiser's wreck (#3902) — see GameServerEscortCruiserWreck.cs for the wreck itself.
/// <list type="bullet">
/// <item><b>Only robots</b>: the guardian bay stands where the ship-mode decay put it, and the derelict machinery releases
/// its guardians (<see cref="DerelictSpawnerSpotWorld"/> finds the bay through the wreck's site).</item>
/// <item><b>Chests</b>: the template's <c>chest</c> markers fill once ever at the first boarding — the giant wrecks' path
/// (<see cref="SpawnGiantWreckChest"/>, the same ledger and container names).</item>
/// <item><b>A crate for every pilot who fought</b>: owner-locked, near the way in, laid the first time that pilot comes
/// aboard — guardian plates and sometimes a hacking chip (<see cref="CruiserWreckCrateFor"/>); into the backpack when there
/// is no spot, onto the deck when the backpack is full. Once per pilot and wreck.</item>
/// <item><b>The event</b> <see cref="GameEventKeys.CruiserBoarded"/> for every player who enters it — argument
/// <see cref="CruiserWreckRules.BoardedEcho"/> for the wreck that carries ECHO, else <see cref="CruiserWreckRules.BoardedPlain"/>.</item>
/// <item><b>ECHO's core for a late fighter</b>: a pilot who fought and comes aboard after ECHO was freed still gets theirs.</item>
/// <item><b>The scanner</b> (#3915): subject <see cref="CruiserWreckScanSubject"/> — a ship's wreck, never "a derelict
/// station": the ship's name and size as traits, knowledge once per wreck (ledger <see cref="CruiserWreckScanKey"/>), in
/// range of the hull box like a giant wreck (<see cref="ScanDistanceSq"/>).</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>#3915: the space readout's subject (kind <c>station</c>) of an escort cruiser's wreck — and its title word: the
    /// readout's display is this key, which the client localizes through <c>ui.scan.subject.cruiser_wreck</c> (the contact's
    /// name on the server is the English one).</summary>
    internal const string CruiserWreckScanSubject = "cruiser_wreck";

    /// <summary>#3915: the space readout's info line of an escort cruiser's wreck.</summary>
    internal const string CruiserWreckScanInfoKey = "ui.scan.cruiser_wreck";

    /// <summary>#3915: the scan-ledger key of an escort cruiser's wreck — once per wreck (a wreck that replaces another never
    /// reuses its id, so the next cruiser's wreck is worth its knowledge again).</summary>
    internal static string CruiserWreckScanKey(string id) => "space:cruiserwreck:" + id;

    /// <summary>The fighters' crate: this many guardian plates at least, and up to this many more.</summary>
    private const int CruiserCratePlates = 6, CruiserCratePlateSpread = 5;

    /// <summary>The chance of a hacking chip in a fighter's crate.</summary>
    private const double CruiserCrateChipChance = 0.35;

    /// <summary>How far round the arrival (blocks) a fighter's crate looks for a free deck cell.</summary>
    private const int CruiserCrateReach = 6;

    /// <summary>The cruiser half of <see cref="OnDerelictBoarded"/> (the interior world is active, the build stamped).</summary>
    private void OnCruiserWreckBoarded(PlayerSession session, BoardableStation station)
    {
        if (!_cruiserWrecks.TryGetValue(station.Id, out var site) || _world.LocationId != StationLocationIdPrefix + station.Id)
        {
            return;
        }

        SpawnCruiserWreckChests(station, site);
        if (site.Sounds.Count > 0)
        {
            var life = ResetMarkerLife(StationMarkerOwner);
            foreach (var m in site.Sounds)
            {
                AddMarkerEmitter(life.Emitters, StationCellToWorld(station, m.LocalPos), MarkerSettings.Parse(m.Data));
            }

            SendLandmarks(session); // the list went out before the hook — again, with the wreck's own sounds
        }

        var record = site.Record;
        string playerId = session.State.PlayerId;
        if (record.Fighters.Contains(playerId) && !record.CratesGiven.Contains(playerId))
        {
            GiveCruiserWreckCrate(session, station, record);
        }

        if (record.EchoFreed && record.Fighters.Contains(playerId))
        {
            GiveEchoCore(session, record); // a fighter who comes later still gets ECHO's keepsake
        }

        RaiseGameEvent(session, GameEventKeys.CruiserBoarded, record.CarriesEcho ? CruiserWreckRules.BoardedEcho : CruiserWreckRules.BoardedPlain);
    }

    private void SpawnCruiserWreckChests(BoardableStation station, CruiserWreckSite site)
    {
        string? changed = null;
        _repo.RunInTransaction(() =>
        {
            for (int n = 0; n < site.Chests.Count; n++)
            {
                changed = SpawnGiantWreckChest(station, site.Chests[n], n) ?? changed;
            }
        });

        if (changed is not null)
        {
            PublishStationCells(changed); // the stored changes (and every pilot outside) keep the crates
        }
    }

    /// <summary>One fighter's crate: near the way in when a deck cell is free there, else into the backpack, else on the deck
    /// at their feet — never lost.</summary>
    private void GiveCruiserWreckCrate(PlayerSession session, BoardableStation station, CruiserWreckRecord record)
    {
        var p = session.State;
        var items = CruiserWreckCrateFor(record, p.PlayerId);
        void Settle()
        {
            record.CratesGiven.Add(p.PlayerId);
            _repo.SaveMetadata(_meta);
        }

        bool placed = TryFindCruiserCrateSpot(station, p.PlayerId, out var cell) && PlaceOwnerCrate(session, cell, items, Settle);
        if (!placed && !GiveOwnerCrateIntoBackpack(session, items, Settle))
        {
            _repo.RunInTransaction(Settle);
            SpillToGround(station.Spawn.ToBlock(), items.Select(i => new ItemAmount(i.Item, i.Count)).ToList());
        }

        Send(session, new ServerMessage { Text = "@srv.cruiser.crate" });
    }

    /// <summary>A fighter's share aboard: guardian plates (a roll per wreck and player) and, sometimes, a hacking chip.</summary>
    internal List<ItemStack> CruiserWreckCrateFor(CruiserWreckRecord record, string playerId)
    {
        var rng = new System.Random(StableHashing.Seed32("cruiser-wreck-crate:" + record.Id + ":" + playerId, record.Seed));
        var items = new List<ItemStack> { new(EnemyCatalog.GuardianPlate, CruiserCratePlates + rng.Next(CruiserCratePlateSpread + 1)) };
        if (rng.NextDouble() < CruiserCrateChipChance && _content.GetItem(CruiserWreckRules.ChipItem) is not null)
        {
            items.Add(new ItemStack(CruiserWreckRules.ChipItem, 1));
        }

        return items;
    }

    /// <summary>A free deck cell near the arrival for an owner-locked crate: the nearest one within <see cref="CruiserCrateReach"/>
    /// on the arrival's deck (one row up or down allowed), never the arrival cell or one right beside it (the way in stays free).</summary>
    private bool TryFindCruiserCrateSpot(BoardableStation station, string ownerId, out Vector3i cell)
    {
        cell = default;
        var at = station.Spawn.ToBlock();
        long best = long.MaxValue;
        for (int dx = -CruiserCrateReach; dx <= CruiserCrateReach; dx++)
            for (int dz = -CruiserCrateReach; dz <= CruiserCrateReach; dz++)
                for (int dy = -1; dy <= 1; dy++)
                {
                    long dist = (dx * dx) + (dz * dz) + (dy * dy * 4);
                    var c = new Vector3i(at.X + dx, at.Y + dy, at.Z + dz);
                    if (dist < 2 || dist >= best || !OwnerCrateSpotFree(c, ownerId) || !_world.GetBlock(new Vector3i(c.X, c.Y + 1, c.Z)).IsAir)
                    {
                        continue;
                    }

                    best = dist;
                    cell = c;
                }

        return best != long.MaxValue;
    }

    /// <summary>#3915: the scanner's readout of an escort cruiser's wreck (from <see cref="ScanNewSpaceObject"/>, before the
    /// derelict station's): a ship's wreck — broken and airless, the ship's name and size as traits — worth a giant wreck's
    /// knowledge once per wreck.</summary>
    private ScanResult ScanCruiserWreck(PlayerSession session, CombatEntity target)
    {
        var traits = new List<string> { "ui.scan.trait.derelict_breached" };
        _cruiserWrecks.TryGetValue(target.Id, out var site);
        if (CruiserWreckTemplate(site?.Record.Template) is { } t)
        {
            traits.Add(string.IsNullOrEmpty(t.NameKey) ? GiantShipRules.NameKey(t.Key) : t.NameKey);
        }

        if (site is { Built: true })
        {
            traits.Add(GiantWreckSizeTrait(site.Half));
        }

        var readout = new ScanReadout
        {
            Kind = "station",
            SubjectKey = CruiserWreckScanSubject,
            Display = CruiserWreckScanSubject, // the client localizes it via ui.scan.subject.* (target.Name is the English name)
            InfoKey = CruiserWreckScanInfoKey,
            TraitKeys = traits.ToArray(),
            LegacyInfo = "The wreck of a Guardian escort cruiser — beaten not long ago, it now drifts dark in space. "
                + "Only robots are left inside. Dock to explore it: there is no air aboard.",
        };
        return Award(session, CruiserWreckScanKey(target.Id), readout, KnowledgeGiantWreck);
    }

    /// <summary>Test seam (#3902): the record of a cruiser wreck (null for an id that is none).</summary>
    public CruiserWreckRecord? CruiserWreckRecordForTest(string id) => _meta.CruiserWrecks.FirstOrDefault(r => r.Id == id);
}
