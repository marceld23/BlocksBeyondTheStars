// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Life aboard and around a giant wreck in space (#3614) — see GameServerGiantDerelicts.cs for the wreck itself.
/// <list type="bullet">
/// <item><b>Chests</b>: every <c>chest</c> marker of the template becomes a one-time salvage container at the first boarding
/// (anyone's): its crate block (setting <c>block</c>, else a metal crate; <c>hidden=1</c> puts a scrap pile on it) is set in the
/// interior AND written into the build like a player edit (so the hull from space and the stored changes keep it), and its
/// contents are the author's (<c>items</c> / <c>table</c> / <c>chance</c>, <see cref="MarkerChestItems"/>), drawn by a hash of
/// the world seed, the interior world and the cell. Once ever per chest: the <see cref="Shared.State.WorldMetadata.GeneratedLoot"/>
/// key <c>giant-chest:&lt;id&gt;:&lt;n&gt;</c>.</item>
/// <item><b>Sounds</b>: the template's <c>sound</c> markers play aboard (the station marker life of the interior world).</item>
/// <item><b>The guardian bay</b> stands where the decay put it; the derelict machinery releases its guardians (survival only).</item>
/// <item><b>VEGA</b>: once per player and wreck when the ship comes within <see cref="GiantWreckApproachRange"/> of the HULL BOX
/// (<c>vega.giantwreck.approach</c>); the first boarding of each giant wreck (<c>vega.giantwreck.boarded</c>; the very first
/// derelict boarding of all still gets the no-air hint).</item>
/// <item><b>The scanner</b>: subject <c>giant_wreck</c> — the ship's name and size as traits, knowledge once per wreck
/// (ledger <c>space:giantwreck:&lt;id&gt;</c>), in range of the hull box.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The <see cref="Shared.State.WorldMetadata.GeneratedLoot"/> key prefix of a giant wreck's chest:
    /// <c>giant-chest:&lt;id&gt;:&lt;n&gt;</c>.</summary>
    internal const string GiantChestLootPrefix = "giant-chest:";

    /// <summary>Container id prefix of a giant wreck's chest (never <c>loot_derelict_…</c>, the story wreck's lore site).</summary>
    internal const string GiantChestContainerPrefix = "loot_giantchest_";

    /// <summary>The hash lane of a giant wreck chest's existence and contents.</summary>
    private const string GiantChestLane = "giantwreck";

    /// <summary>The block a chest stands as when its marker names none: a metal crate (a ship carries no wooden ones).</summary>
    private const string GiantChestBlock = "crate";

    /// <summary>What sits on a hidden chest (<c>hidden=1</c>) aboard — some scrap fallen over it.</summary>
    private const string GiantChestCover = "scrap_pile";

    /// <summary>Range (flight units, to the hull box) at which VEGA first remarks on a giant wreck.</summary>
    private const float GiantWreckApproachRange = 150f;

    /// <summary>Knowledge for the first scan of each giant wreck.</summary>
    private const int KnowledgeGiantWreck = 5;

    /// <summary>The scan-ledger key of a giant wreck: <c>space:giantwreck:&lt;id&gt;#&lt;ship key&gt;</c> — the ship class rides
    /// along (after a <c>#</c>, which no wreck id holds), so the Codex can name the class in every later session too.</summary>
    internal static string GiantWreckScanKey(string id, string? shipKey = null)
        => "space:giantwreck:" + id + (string.IsNullOrEmpty(shipKey) ? string.Empty : "#" + shipKey);

    // ---------------- boarding ----------------

    /// <summary>The giant half of <see cref="OnDerelictBoarded"/> (the interior world is active, the build stamped): the chests
    /// (once ever each) and the sound emitters.</summary>
    private void OnGiantWreckBoarded(PlayerSession session, BoardableStation station)
    {
        if (!_giantDerelicts.TryGetValue(station.Id, out var site) || _world.LocationId != StationLocationIdPrefix + station.Id)
        {
            return;
        }

        SpawnGiantWreckChests(station, site);
        if (site.Sounds.Count > 0)
        {
            var life = ResetMarkerLife(StationMarkerOwner);
            foreach (var m in site.Sounds)
            {
                AddMarkerEmitter(life.Emitters, StationCellToWorld(station, m.LocalPos), MarkerSettings.Parse(m.Data));
            }

            SendLandmarks(session); // the list went out before the hook — again, with the wreck's own sounds
        }
    }

    private void SpawnGiantWreckChests(BoardableStation station, GiantDerelictSite site)
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

    /// <summary>One chest, once ever: its crate in the interior and the build, and its salvage container. A chest whose cell
    /// someone built into first stays without (marked done all the same). Returns the station whose build changed.</summary>
    private string? SpawnGiantWreckChest(BoardableStation station, WorldGeneration.SettlementMarker marker, int n)
    {
        string ledger = GiantChestLootPrefix + station.Id + ":" + n;
        if (_meta.GeneratedLoot.Contains(ledger))
        {
            return null; // filled once already (it may since have been looted away)
        }

        _meta.GeneratedLoot.Add(ledger);
        var settings = MarkerSettings.Parse(marker.Data);
        var cell = StationCellToWorld(station, marker.LocalPos);
        if (!MarkerChestExists(GiantChestLane, cell, settings) || !_world.GetBlock(cell).IsAir)
        {
            return null;
        }

        string key = settings.Get("block", GiantChestBlock);
        var block = _content.GetBlock(ContainerSystem.IsContainerBlock(key) ? key : GiantChestBlock);
        string? changed = null;
        if (block is not null)
        {
            changed = PutGiantWreckCell(cell, block.NumericId);
            var above = new Vector3i(cell.X, cell.Y + 1, cell.Z);
            if (settings.GetBool("hidden") && _world.GetBlock(above).IsAir && _content.GetBlock(GiantChestCover) is { } cover)
            {
                changed = PutGiantWreckCell(above, cover.NumericId) ?? changed;
            }
        }

        var items = MarkerChestItems(GiantChestLane, cell, settings);
        string id = GiantChestContainerPrefix + station.Id.Replace(':', '_') + "_" + n;
        if (items.Count > 0 && !_containers.Exists(c => c.Id == id))
        {
            AddContainer(new StoredContainer { Id = id, Planet = _world.LocationId, Kind = "salvage", Position = cell, Items = items });
        }

        return changed;
    }

    /// <summary>A block set aboard as the wreck's own: in the interior world (told to everyone aboard) and in the build.</summary>
    private string? PutGiantWreckCell(Vector3i cell, BlockId block)
    {
        _world.SetBlock(cell, block);
        var c = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        BroadcastToWorld(new BlockChanged { X = c.X, Y = c.Y, Z = c.Z, Block = block.Value });
        return MirrorStationCell(cell, block);
    }

    // ---------------- flight ----------------

    /// <summary>VEGA's first word about a giant wreck (from <see cref="CheckDerelictApproach"/>, on every ship pose): once per
    /// player and wreck, inside <see cref="GiantWreckApproachRange"/> of its hull box — its centre lies far inside it.</summary>
    private void CheckGiantWreckApproach(PlayerSession session, CombatEntity e, Vector3f pos)
    {
        if (!_playerStationCells.TryGetValue(e.Id, out var build) || build.Cells.Count == 0
            || HullBoxDistance(build, pos) > GiantWreckApproachRange)
        {
            return;
        }

        if (session.State.Milestones.Add("giantwreck:seen:" + e.Id))
        {
            SendVegaLine(session, "vega.giantwreck.approach", 1, e.Name);
            _repo.SavePlayer(session.State);
        }
    }

    /// <summary>A scan target's squared distance from the pilot (#2238): to its centre — for a ship's wreck (a giant wreck, an
    /// escort cruiser's wreck #3915) to its hull box.</summary>
    private float ScanDistanceSq(CombatEntity target, Vector3f pilot)
    {
        if (target.Kind == CombatEntityKind.SpaceStation && IsShipWreckId(target.Id)
            && _playerStationCells.TryGetValue(target.Id, out var build) && build.Cells.Count > 0)
        {
            float d = HullBoxDistance(build, pilot);
            return d * d;
        }

        return target.Position.DistanceSquared(pilot);
    }

    /// <summary>The scanner's readout of a giant wreck: what it is (the ship's name as a trait), how big, that it is broken and
    /// airless — knowledge once per wreck.</summary>
    private ScanResult ScanGiantWreck(PlayerSession session, CombatEntity target)
    {
        var traits = new List<string> { "ui.scan.trait.derelict_breached" };
        if (_giantDerelicts.TryGetValue(target.Id, out var site))
        {
            var t = site.Template;
            traits.Add(string.IsNullOrEmpty(t.NameKey) ? GiantShipRules.NameKey(t.Key) : t.NameKey);
            traits.Add(GiantWreckSizeTrait(site.Half));
        }

        var readout = new ScanReadout
        {
            Kind = "station",
            SubjectKey = "giant_wreck",
            Display = target.Name,
            InfoKey = "ui.scan.giant_wreck_space", // the planet wreck has its own (ui.scan.giant_wreck)
            TraitKeys = traits.ToArray(),
            LegacyInfo = "A giant ship wreck — abandoned long ago, everyone got away. There is no air aboard.",
        };
        return Award(session, GiantWreckScanKey(target.Id, _giantDerelicts.TryGetValue(target.Id, out var s) ? s.Template?.Key : null),
            readout, KnowledgeGiantWreck);
    }

    /// <summary>The size trait of a giant wreck by its longest side: <c>ui.scan.trait.giant_size_large|huge|colossal</c>.</summary>
    internal static string GiantWreckSizeTrait(Vector3f half)
    {
        float longest = 2f * System.MathF.Max(half.X, System.MathF.Max(half.Y, half.Z));
        string size = longest >= 200f ? "colossal" : longest >= 120f ? "huge" : "large";
        return "ui.scan.trait.giant_size_" + size;
    }

    // ---------------- test seams ----------------

    /// <summary>Test seam (#3614): the giant wrecks of this galaxy (body ids).</summary>
    public IReadOnlyList<string> GiantDerelictIdsForTest => _giantDerelicts.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToList();

    /// <summary>Test seam (#3614): a giant wreck, built on the way — its ship, its arrival, chests, guardian bay and doors (build
    /// cells) and its hull's half size; null for an id that is none.</summary>
    public (string Ship, Vector3i Arrival, IReadOnlyList<Vector3i> Chests, Vector3i? Bay, int Doors, Vector3f Half)? GiantWreckForTest(string id)
        => EnsureGiantDerelict(id) is not null && _giantDerelicts.TryGetValue(id, out var site)
            ? (site.Template.Key, site.Arrival, site.Chests.Select(c => c.LocalPos).ToList(), site.SpawnerSpot, site.DoorCount, site.Half)
            : null;

    /// <summary>Test seam (#3614): the chest containers of a giant wreck that are still there (in the active world).</summary>
    public IReadOnlyList<StoredContainer> GiantWreckChestsForTest(string stationId)
    {
        string prefix = GiantChestContainerPrefix + stationId.Replace(':', '_') + "_";
        return _containers.FindAll(c => c.Id.StartsWith(prefix, System.StringComparison.Ordinal));
    }

    /// <summary>Test seam (#3614): whether a giant wreck already carries its shell (handed over with the build, no send needed),
    /// and how many cells a shell built afresh from the wreck as it stands now shows that the carried one does not (0 = the
    /// carried shell shows everything a pilot must see).</summary>
    internal (bool Carried, int Missing) GiantShellCheckForTest(string id)
    {
        if (!_playerStationCells.TryGetValue(id, out var s) || s.Shell is not { } carried)
        {
            return (false, 0);
        }

        var (min, max) = s.CellBox();
        var fresh = HullShell.Build(min, max, s.Cells.Select(kv => new KeyValuePair<Vector3i, ushort>(kv.Key, kv.Value.Value)),
            p => s.Get(p).Value, p => s.Shapes.TryGetValue(p, out int v) ? v : 0, StationHull.SeeThroughIds(_content));
        return (true, s.Cells.Keys.Count(c => fresh.IsVisible(c) && !carried.IsVisible(c)));
    }

    /// <summary>Test seam (#3614): the flight frame's box of every station hull an orbit of the player's flight holds (derelicts,
    /// giant wrecks, NPC station hulls, player stations), by structure id.</summary>
    public IReadOnlyDictionary<string, (Vector3f Centre, Vector3f Half)> StationHullBoxesForTest(string playerId)
    {
        var boxes = new Dictionary<string, (Vector3f, Vector3f)>(System.StringComparer.Ordinal);
        if (!_playerInstance.TryGetValue(playerId, out var iid) || !_spaceInstances.TryGetValue(iid, out var instance))
        {
            return boxes;
        }

        foreach (var s in instance.Structures.Values.Where(s => s.Kind == "station" && s.Cells.Count > 0))
        {
            var (min, max) = s.CellBox();
            boxes[s.Id] = (s.Position, new Vector3f((max.X - min.X + 1) / 2f, (max.Y - min.Y + 1) / 2f, (max.Z - min.Z + 1) / 2f));
        }

        return boxes;
    }
}
