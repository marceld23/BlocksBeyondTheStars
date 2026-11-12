// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Marker settings everywhere (#3519): from terrain generation 38 the chest, sound and den markers of a settlement (its whole
/// template, its plot modules, the city's district modules) and of a station template behave exactly as in a landmark — the
/// same code (GameServerLandmarkLife.cs, GameServerLandmarkWorld.cs, GameServerLandmarkStamp.cs):
/// <list type="bullet">
/// <item><b>Chests</b>: <c>items</c> / <c>table</c> / <c>chance</c> / <c>hidden</c> / <c>block</c>. Once ever (a key in
/// <see cref="WorldMetadata.GeneratedLoot"/>): the crate block is written and the <c>salvage</c> container filled at the same
/// moment, so a looted chest never refills and a broken crate never comes back. Every draw is a hash of the world seed, the
/// body and the cell on the lane <c>structure</c> — never the stamp's random stream.</item>
/// <item><b>Sound emitters</b> go into the world's <see cref="Networking.Messages.LandmarkList"/>; <b>dens</b> are ticked with
/// the landmarks' (<see cref="TickLandmarks"/>).</item>
/// </list>
/// The emitters and dens are re-derived with their structure: a settlement's on every load with its markers, a station's when
/// it is stamped for the server run. Older worlds: none of this — their markers carry no settings and a chest marker stays inert.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The hash lane (and loot-key prefix) of a settlement's or station's chest.</summary>
    internal const string StructureChestLane = "structure";

    /// <summary>The marker-life owner of a body's settlements (the city included).</summary>
    private const string SettlementMarkerOwner = "settlements";

    /// <summary>The marker-life owner of a station's world.</summary>
    private const string StationMarkerOwner = "station";

    /// <summary>The sound emitters and dens of one owner's structures on one location.</summary>
    internal sealed class StructureMarkerLife
    {
        public List<LandmarkEmitter> Emitters { get; } = new();

        public List<LandmarkDen> Dens { get; } = new();
    }

    /// <summary>Marker life by <c>location|owner</c>, for the server run.</summary>
    private readonly Dictionary<string, StructureMarkerLife> _structureMarkerLife = new(System.StringComparer.Ordinal);

    /// <summary>Whether this world stamps its templates the generation-38 way (#3518/#3519): marker settings honoured, whole
    /// templates turned and eligible as ruins, station tokens resolved, generator entries in the pools.</summary>
    private bool TemplatePipelineWorld => _meta.Description.TerrainGeneration >= WorldDescription.StructurePipelineGeneration;

    /// <summary>A fresh, empty marker life for one owner on the active location, replacing what it held (its structures are
    /// about to be re-derived).</summary>
    private StructureMarkerLife ResetMarkerLife(string owner)
    {
        var life = new StructureMarkerLife();
        _structureMarkerLife[_world.LocationId + "|" + owner] = life;
        return life;
    }

    private IEnumerable<StructureMarkerLife> MarkerLivesAt(string location)
    {
        string prefix = location + "|";
        return _structureMarkerLife.Where(kv => kv.Key.StartsWith(prefix, System.StringComparison.Ordinal)).Select(kv => kv.Value);
    }

    /// <summary>The settlements' and stations' sound emitters on a location.</summary>
    private IEnumerable<LandmarkEmitter> StructureEmittersAt(string location) => MarkerLivesAt(location).SelectMany(l => l.Emitters);

    /// <summary>The settlements' and stations' dens on a location.</summary>
    private IEnumerable<LandmarkDen> StructureDensAt(string location) => MarkerLivesAt(location).SelectMany(l => l.Dens);

    private bool HasStructureDens(string location) => StructureDensAt(location).Any();

    /// <summary>
    /// One marker of a generation-38 settlement or station at world cell <paramref name="cell"/>: a chest, a sound or a den as
    /// in a landmark. False for every other marker (the caller handles it as before) — and for a chest marker WITHOUT
    /// settings: the shipped settlement templates carry such chests as village furniture, and an inhabited village must not
    /// turn into a free loot crate; only a chest the author gave settings (items, a table, a chance …) becomes one.
    /// </summary>
    private bool ApplyStructureMarker(StructureMarkerLife life, string type, string data, Vector3i cell)
    {
        if (type != LandmarkRules.ChestMarker && type != LandmarkRules.SoundMarker && type != LandmarkRules.DenMarker)
        {
            return false;
        }

        if (type == LandmarkRules.ChestMarker && string.IsNullOrWhiteSpace(data))
        {
            return false;
        }

        var at = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        var settings = MarkerSettings.Parse(data);
        if (type == LandmarkRules.ChestMarker)
        {
            SpawnStructureChest(at, settings);
        }
        else if (type == LandmarkRules.SoundMarker)
        {
            AddMarkerEmitter(life.Emitters, at, settings);
        }
        else
        {
            AddMarkerDen(life.Dens, at, settings);
        }

        return true;
    }

    /// <summary>A settlement's or station's chest, once ever: its crate block and its one-time <c>salvage</c> container.</summary>
    private void SpawnStructureChest(Vector3i cell, MarkerSettings settings)
    {
        string key = $"{StructureChestLane}:chest:{_world.LocationId}:{cell.X}:{cell.Y}:{cell.Z}";
        if (_meta.GeneratedLoot.Contains(key))
        {
            return;
        }

        _meta.GeneratedLoot.Add(key);
        if (!MarkerChestExists(StructureChestLane, cell, settings))
        {
            return;
        }

        WriteMarkerChestBlock(cell, settings);
        var items = MarkerChestItems(StructureChestLane, cell, settings);
        if (items.Count == 0)
        {
            return;
        }

        AddContainer(new StoredContainer
        {
            Id = StructureChestContainerId(key),
            Planet = _world.LocationId,
            Kind = "salvage",
            Position = cell,
            Items = items,
        });
    }

    /// <summary>The container id of a structure chest's loot key.</summary>
    internal static string StructureChestContainerId(string lootKey) => "loot_" + lootKey.Replace(':', '_');

    /// <summary>Test seam (#3519): the settlements' and stations' sound emitters on the active location.</summary>
    internal IReadOnlyList<LandmarkEmitter> StructureEmittersForTest => StructureEmittersAt(_world.LocationId).ToList();

    /// <summary>Test seam (#3519): the settlements' and stations' dens on the active location.</summary>
    internal IReadOnlyList<LandmarkDen> StructureDensForTest => StructureDensAt(_world.LocationId).ToList();
}
