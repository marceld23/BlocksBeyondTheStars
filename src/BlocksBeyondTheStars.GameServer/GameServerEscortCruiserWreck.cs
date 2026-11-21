// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// What an escort cruiser leaves behind (#3902, canon C22) — the hook the boss fight calls when the boss falls. The fight
/// itself (#3900) ends in <c>GameServerSpaceBossCredit.cs</c>; here the beaten ship goes dark and STAYS: a wreck the players
/// can board, registered at runtime where it fell.
/// <list type="bullet">
/// <item><b>What it is</b>: a derelict of its own family (<see cref="CruiserWreckRules.IdPrefix"/>), so every derelict rule
/// holds by its id (boardable by anyone, no air, no shield, nobody home, falling out is a spacewalk, the guardian bay). Its
/// hull is the pin-only giant ship <see cref="CruiserWreckRules.TemplateKey"/> through the giant wrecks' build —
/// <see cref="GiantShipGenerator.BuildSpace"/> and <see cref="DerelictGenerator.GenerateShip"/>, both with the FRESH option
/// (holes and broken glass, no soot, no scrap, a share of red lamps still burning) — as a DERIVED station
/// (<see cref="RegisterDerivedStation"/>): the save keeps only the players' changes.</item>
/// <item><b>Where</b>: where the boss fell, raised clear of the planet spheres and pushed clear of the launch point and of
/// every station hull the server knows there (<see cref="CruiserWreckFlightPosition"/>); a contact of every orbit of that
/// system, at once in the flights that are open and in every later one.</item>
/// <item><b>One per system</b>: a new wreck replaces the old one (<see cref="RetireCruiserWrecksIn"/>) — off the star map at
/// once, and removed for good (record, stored changes, interior world, chest marks) as soon as nobody is aboard.</item>
/// <item><b>The record</b> (<see cref="WorldMetadata.CruiserWrecks"/>): id, system, place, seed, what happened on the bridge,
/// who fought. Every start re-registers the wrecks from it (<see cref="RegisterCruiserWrecks"/>); the hull is rebuilt from the
/// seed on first need, and its base hash is checked against the record like a giant wreck's pin.</item>
/// </list>
/// Life aboard (chests, the fighters' crates, the boarding event): GameServerEscortCruiserWreckLife.cs. The bridge console and
/// ECHO: GameServerEscortCruiserBridge.cs. See docs/developer/GIANT_SHIPS.md § "The escort cruiser's wreck".
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The wreck's lowest block floats at least this high over the flight plane — clear of every planet sphere,
    /// like a station hull.</summary>
    private const float CruiserWreckFloorY = StationHullFloorY;

    /// <summary>Clear space between the wreck's box and every station hull it is pushed clear of.</summary>
    private const float CruiserWreckGap = 30f;

    /// <summary>Clear space (sideways) between the wreck's box and the launch column of an orbit (x = z = 0).</summary>
    private const float CruiserWreckLaunchClear = 20f;

    /// <summary>A registered cruiser wreck: its record, its body on the star map and — once built — what its markers became.</summary>
    private sealed class CruiserWreckSite
    {
        public CruiserWreckRecord Record { get; init; } = null!;

        public CelestialBody Body { get; init; } = null!;

        public string Id => Record.Id;

        public bool Built { get; set; }

        /// <summary>It cannot be shown this run (its ship left the content, or its base no longer comes out as stored while the
        /// save holds the players' changes): no body, no contact; everything stored is kept.</summary>
        public bool Missing { get; set; }

        /// <summary>Half the base build's cell box.</summary>
        public Vector3f Half { get; set; }

        public IReadOnlyList<SettlementMarker> Chests { get; set; } = System.Array.Empty<SettlementMarker>();

        public IReadOnlyList<SettlementMarker> Sounds { get; set; } = System.Array.Empty<SettlementMarker>();

        public Vector3i? SpawnerSpot { get; set; }

        /// <summary>The bridge console and its lamps, in build cells.</summary>
        public Vector3i? Console { get; set; }

        public IReadOnlyList<Vector3i> Lamps { get; set; } = System.Array.Empty<Vector3i>();
    }

    private readonly Dictionary<string, CruiserWreckSite> _cruiserWrecks = new(System.StringComparer.Ordinal);

    /// <summary>The ship a cruiser's wreck is built from, or null when the content holds none (then no wreck is left).</summary>
    private StructureTemplate? CruiserWreckTemplate(string? key = null)
        => _content.GetGiantShip(string.IsNullOrEmpty(key) ? CruiserWreckRules.TemplateKey : key!);

    /// <summary>True for a wreck of a giant ship or of a cruiser — a SHIP, where the derelict texts say "station".</summary>
    private static bool IsShipWreckId(string? id) => GiantShipRules.IsGiantWreckId(id) || CruiserWreckRules.IsCruiserWreckId(id);

    // ---------------------------------------------------------------- the hook

    /// <summary>
    /// An escort cruiser was destroyed. Called once, inside the kill (after the credit and the events, before the loot
    /// and before the instance's last state broadcast of that kill — an entity or structure added here goes out with it).
    /// <paramref name="boss"/> is already out of <c>instance.Entities</c>; its <see cref="CombatEntity.Position"/> and
    /// <see cref="CombatEntity.Yaw"/> are where it fell. <paramref name="fighterIds"/> are the player ids of every pilot
    /// who hurt it (sorted; some may have gone home or left the game since). <paramref name="carriesEcho"/> is true for
    /// the story's cruiser (canon C22). The wreck is registered here: its record, its hull (built now — a mid-sized ship
    /// takes a few dozen milliseconds), its body on the star map and its contact in every open flight of the system.
    /// </summary>
    private void OnEscortCruiserDefeated(SpaceInstance instance, CombatEntity boss, IReadOnlyCollection<string> fighterIds, bool carriesEcho)
    {
        string systemId = instance.Anchor?.SystemId ?? SystemIdOfInstance(instance);
        var template = CruiserWreckTemplate();
        if (template is null || string.IsNullOrEmpty(systemId) || _galaxy.Systems.All(s => s.Id != systemId))
        {
            _log.Warn($"Escort cruiser '{boss.Id}' fell in '{instance.Id}', but it leaves no wreck (no '{CruiserWreckRules.TemplateKey}' ship or no system).");
            return;
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        int serial = _meta.CruiserWrecks.Where(r => r.SystemId == systemId).Select(r => r.Serial).DefaultIfEmpty(0).Max() + 1;
        RetireCruiserWrecksIn(systemId);
        string id = CruiserWreckRules.IdOf(systemId, serial);
        var record = new CruiserWreckRecord
        {
            Id = id,
            SystemId = systemId,
            Serial = serial,
            Yaw = boss.Yaw,
            Seed = unchecked((long)StableHashing.Mixed("cruiser-wreck:" + id, _meta.Seed)),
            Template = template.Key,
            CarriesEcho = carriesEcho,
            Fighters = fighterIds.ToList(),
        };
        var site = AddCruiserWreckSite(record);
        var parts = BuildCruiserWreckParts(template, record.Seed, site.Body.Name);
        AdoptCruiserWreckParts(site, parts);
        var spot = CruiserWreckFlightPosition(boss.Position, site.Half, CruiserWreckObstacles(instance, systemId));
        (record.X, record.Y, record.Z) = (spot.X, spot.Y, spot.Z);
        record.BaseHash = parts.Wreck.BaseHash;
        _meta.CruiserWrecks.Add(record);
        _repo.SaveMetadata(_meta);
        RegisterCruiserWreckStation(site, parts);
        ShowCruiserWreck(site, instance);
        _log.Info($"Escort cruiser '{boss.Id}' fell in '{instance.Id}': its wreck '{id}' drifts at {spot.X:0},{spot.Y:0},{spot.Z:0} "
            + $"({parts.Build.Cells.Count} blocks, {fighterIds.Count} fighter(s), carries ECHO: {carriesEcho}; built in {watch.ElapsedMilliseconds} ms).");
    }

    // ---------------------------------------------------------------- registration

    /// <summary>Every start (from <see cref="RegisterDerelicts"/>, after the giant wrecks): the wrecks of the save's records
    /// are put back on the star map. A record a newer wreck replaced is removed for good now (nobody is aboard at a start);
    /// one whose ship left the content stays away with everything stored kept. The hull is built on first need.</summary>
    private void RegisterCruiserWrecks()
    {
        _cruiserWrecks.Clear();
        foreach (var record in _meta.CruiserWrecks.Where(r => r.Replaced).ToList())
        {
            PurgeCruiserWreck(record);
        }

        foreach (var record in _meta.CruiserWrecks)
        {
            if (CruiserWreckTemplate(record.Template) is null || _galaxy.Systems.All(s => s.Id != record.SystemId))
            {
                _log.Warn($"Cruiser wreck '{record.Id}': its ship '{record.Template}' or its system is gone — it stays away; its changes are kept.");
                continue;
            }

            AddCruiserWreckSite(record);
        }
    }

    /// <summary>The wreck's site and its body on the star map (a station body of its system, charted as the save remembers).</summary>
    private CruiserWreckSite AddCruiserWreckSite(CruiserWreckRecord record)
    {
        var sys = _galaxy.Systems.First(s => s.Id == record.SystemId);
        var beside = sys.Bodies.FirstOrDefault(b => !string.IsNullOrEmpty(b.PlanetType)) ?? sys.Bodies.FirstOrDefault();
        var body = new CelestialBody
        {
            Id = record.Id,
            Name = Localize("en", CruiserWreckRules.NameKey), // the per-player name rides the star map and the contacts
            Kind = CelestialKind.SpaceStation,
            SystemId = sys.Id,
            Status = _repo.LoadLocationStatuses().TryGetValue(record.Id, out var s) && System.Enum.TryParse<GenerationStatus>(s, out var status)
                ? status
                : GenerationStatus.Discovered,
            SystemX = (beside?.SystemX ?? 0f) + 420f, // cosmetic: a spot beside a body of the system
            SystemY = beside?.SystemY ?? 0f,
            SystemZ = (beside?.SystemZ ?? 0f) + 260f,
            ParentId = beside?.Id ?? string.Empty,
        };
        sys.Bodies.RemoveAll(b => b.Id == record.Id);
        sys.Bodies.Add(body);
        var site = new CruiserWreckSite { Record = record, Body = body };
        _cruiserWrecks[record.Id] = site;
        return site;
    }

    /// <summary>A cruiser wreck's boardable record, built on the way (from <see cref="EnsureDerelict"/>); null for an id that
    /// is none of this world, or one that has to stay away.</summary>
    private BoardableStation? EnsureCruiserWreck(string id)
    {
        if (!_cruiserWrecks.TryGetValue(id, out var site) || site.Missing)
        {
            return null;
        }

        if (!site.Built)
        {
            RebuildCruiserWreck(site);
        }

        return site.Missing ? null : _stationsById.TryGetValue(id, out var station) ? station : null;
    }

    /// <summary>Whether a station id is a cruiser wreck that is not there (any more) this run — the paths that find a station
    /// by its body skip it instead of making it a plain station.</summary>
    private bool CruiserWreckMissing(string id) => CruiserWreckRules.IsCruiserWreckId(id) && EnsureCruiserWreck(id) is null;

    /// <summary>A later run's build of a recorded wreck: the same template and seed — and the same base, or the wreck stays
    /// away while the save holds the players' changes (a block or the generator changed since; nothing stored is touched).</summary>
    private void RebuildCruiserWreck(CruiserWreckSite site)
    {
        var record = site.Record;
        var template = CruiserWreckTemplate(record.Template);
        if (template is null)
        {
            HideCruiserWreck(site);
            return;
        }

        var parts = BuildCruiserWreckParts(template, record.Seed, site.Body.Name);
        if (record.BaseHash.Length > 0 && record.BaseHash != parts.Wreck.BaseHash)
        {
            if (DerivedHasStoredState(record.Id))
            {
                _log.Warn($"Cruiser wreck '{record.Id}': its base no longer comes out as stored (a block or the generator changed) — "
                    + "it stays away until it does again; its changes are kept.");
                HideCruiserWreck(site);
                return;
            }

            record.BaseHash = parts.Wreck.BaseHash; // nothing was stored on the old base: it is the ship as it comes out now
            _repo.SaveMetadata(_meta);
        }

        AdoptCruiserWreckParts(site, parts);
        RegisterCruiserWreckStation(site, parts);
    }

    /// <summary>Takes a wreck off the star map for this run (its record and everything stored stay).</summary>
    private void HideCruiserWreck(CruiserWreckSite site)
    {
        site.Missing = true;
        _galaxy.Systems.FirstOrDefault(s => s.Id == site.Record.SystemId)?.Bodies.Remove(site.Body);
    }

    /// <summary>The pure part of the build: the fresh wreck, the ship-mode decay, the base build and its visible shell.</summary>
    private GiantBuildParts BuildCruiserWreckParts(StructureTemplate template, long seed, string name)
    {
        var space = GiantShipGenerator.BuildSpace(template, seed, _content, GiantWreckBedsAbandoned, fresh: true);
        var wreck = DerelictGenerator.GenerateShip(space, seed, _content, GiantShipGenerator.IsAlien(template), fresh: true);
        var build = GiantBaseBuild(name, wreck.Layout);
        var (min, max) = build.CellBox();
        var shell = HullShell.Build(min, max, build.Cells.Select(kv => new KeyValuePair<Vector3i, ushort>(kv.Key, kv.Value.Value)),
            p => build.Get(p).Value, p => build.Shapes.TryGetValue(p, out int v) ? v : 0, StationHull.SeeThroughIds(_content));
        return new GiantBuildParts(wreck, build, shell);
    }

    private static void AdoptCruiserWreckParts(CruiserWreckSite site, GiantBuildParts parts)
    {
        var (min, max) = parts.Build.CellBox();
        site.Half = new Vector3f((max.X - min.X + 1) / 2f, (max.Y - min.Y + 1) / 2f, (max.Z - min.Z + 1) / 2f);
        site.Chests = parts.Wreck.Chests;
        site.Sounds = parts.Wreck.Sounds;
        site.SpawnerSpot = parts.Wreck.Layout.SpawnerSpot;
        site.Console = parts.Wreck.Console;
        site.Lamps = parts.Wreck.Lamps;
    }

    /// <summary>The cheap half: the build becomes THE derived station at the recorded place (the stored changes laid over it).</summary>
    private void RegisterCruiserWreckStation(CruiserWreckSite site, GiantBuildParts parts)
    {
        var record = site.Record;
        parts.Build.Position = new Vector3f(record.X, record.Y, record.Z);
        RegisterDerivedStation(record.Id, parts.Build, parts.Wreck.Layout.Arrival, CruiserWreckRules.SizeTier, parts.Wreck.BaseHash);
        AdoptGiantShell(record.Id, parts.Build, parts.Shell);
        site.Built = true;
    }

    // ---------------------------------------------------------------- in flight

    /// <summary>
    /// Where a cruiser's wreck floats (its cell box's centre) with half size <paramref name="half"/>: where the boss
    /// <paramref name="fell"/>, raised until its lowest block is <see cref="CruiserWreckFloorY"/> over the flight plane (clear
    /// of every planet sphere down there), then pushed sideways — along the axis that needs the shorter way — until its box
    /// keeps <see cref="CruiserWreckLaunchClear"/> from the launch column and <see cref="CruiserWreckGap"/> from every
    /// obstacle box (the station hulls of the flight and of the system).
    /// </summary>
    internal static Vector3f CruiserWreckFlightPosition(Vector3f fell, Vector3f half, IReadOnlyList<(Vector3f Centre, Vector3f Half)> obstacles)
    {
        float x = fell.X, z = fell.Z, y = System.MathF.Max(fell.Y, CruiserWreckFloorY + half.Y);
        float side = fell.X < 0f ? -1f : 1f;
        for (int pass = 0; pass < 12; pass++)
        {
            bool moved = false;
            if (System.MathF.Abs(x) < half.X + CruiserWreckLaunchClear && System.MathF.Abs(z) < half.Z + CruiserWreckLaunchClear)
            {
                x = side * (half.X + CruiserWreckLaunchClear);
                moved = true;
            }

            foreach (var (centre, h) in obstacles)
            {
                float needX = half.X + h.X + CruiserWreckGap - System.MathF.Abs(x - centre.X);
                float needY = half.Y + h.Y + CruiserWreckGap - System.MathF.Abs(y - centre.Y);
                float needZ = half.Z + h.Z + CruiserWreckGap - System.MathF.Abs(z - centre.Z);
                if (needX <= 0f || needY <= 0f || needZ <= 0f)
                {
                    continue;
                }

                if (needX <= needZ)
                {
                    x += (x < centre.X ? -1f : 1f) * needX;
                }
                else
                {
                    z += (z < centre.Z ? -1f : 1f) * needZ;
                }

                moved = true;
            }

            if (!moved)
            {
                break;
            }
        }

        return new Vector3f(x, y, z);
    }

    /// <summary>The boxes a new wreck keeps clear of: every station hull of the flight it fell in (NPC stations, derelicts,
    /// giant wrecks, player stations as they stand) plus what the server knows of the system's other orbits — its player
    /// stations and the NPC station hulls already seen this run.</summary>
    private List<(Vector3f Centre, Vector3f Half)> CruiserWreckObstacles(SpaceInstance instance, string systemId)
    {
        var boxes = new List<(Vector3f Centre, Vector3f Half)>();
        foreach (var s in instance.Structures.Values.Where(s => s.Kind == "station" && s.Cells.Count > 0).OrderBy(s => s.Id, System.StringComparer.Ordinal))
        {
            var (min, max) = s.CellBox();
            boxes.Add((s.Position, new Vector3f((max.X - min.X + 1) / 2f, (max.Y - min.Y + 1) / 2f, (max.Z - min.Z + 1) / 2f)));
        }

        boxes.AddRange(PlayerStationBoxesIn(systemId));
        foreach (var st in _stationsById.Values.Where(st => st.Hull is not null).OrderBy(st => st.Id, System.StringComparer.Ordinal))
        {
            if (_galaxy.FindBody(st.Id)?.SystemId == systemId && !instance.Structures.ContainsKey(st.Id))
            {
                boxes.Add((st.SpacePosition, st.HullHalf));
            }
        }

        return boxes;
    }

    /// <summary>A fresh wreck goes into every open flight of its system: its contact and its hull, the design to the pilots.
    /// <paramref name="killInstance"/> (the flight the boss fell in) gets its state with the kill's own broadcast; the others
    /// are told here. The star map is refreshed for everyone.</summary>
    private void ShowCruiserWreck(CruiserWreckSite site, SpaceInstance killInstance)
    {
        if (!_stationsById.TryGetValue(site.Id, out var station) || !_playerStationCells.TryGetValue(site.Id, out var hull))
        {
            return;
        }

        foreach (var instance in _spaceInstances.Values.ToList())
        {
            if (!ReferenceEquals(instance, killInstance) && instance.Anchor?.SystemId != site.Record.SystemId)
            {
                continue;
            }

            instance.Structures[hull.Id] = hull;
            if (!instance.Entities.Any(e => e.Id == site.Id))
            {
                instance.Entities.Add(new CombatEntity
                {
                    Id = site.Id,
                    Kind = CombatEntityKind.SpaceStation,
                    Name = station.Name,
                    Hostile = false,
                    Hull = 1f,
                    HullMax = 1f,
                    Position = station.SpacePosition,
                    Scale = StationModelScale(station.SizeTier),
                });
            }

            SendShipDesignTo(PilotsIn(instance), hull);
            if (!ReferenceEquals(instance, killInstance))
            {
                BroadcastSpaceState(instance);
            }
        }

        BroadcastStarMap();
    }

    /// <summary>Puts the hulls of the anchor system's cruiser wrecks into a fresh space instance (their contacts come with
    /// the station contacts, like a derelict's).</summary>
    private void AddCruiserWreckHulls(SpaceInstance instance, CelestialBody anchor)
    {
        foreach (var site in _cruiserWrecks.Values.Where(s => s.Record.SystemId == anchor.SystemId && !s.Record.Replaced).ToList())
        {
            if (EnsureCruiserWreck(site.Id) is not null && _playerStationCells.TryGetValue(site.Id, out var s))
            {
                instance.Structures[s.Id] = s;
            }
        }
    }

    /// <summary>A cruiser wreck's name in a player's language (the star map and the flight's contacts).</summary>
    private string CruiserWreckNameFor(PlayerSession receiver, string id, string fallback)
        => CruiserWreckRules.IsCruiserWreckId(id) ? Localize(receiver.Locale, CruiserWreckRules.NameKey) : fallback;

    /// <summary>The contacts of a flight with every cruiser wreck named in the receiver's language.</summary>
    private NetCombatEntity[] WithCruiserWreckNames(PlayerSession receiver, NetCombatEntity[] entities)
    {
        if (_cruiserWrecks.Count == 0)
        {
            return entities;
        }

        foreach (var e in entities)
        {
            e.Name = CruiserWreckNameFor(receiver, e.Id, e.Name);
        }

        return entities;
    }

    // ---------------------------------------------------------------- one per system

    /// <summary>A new wreck takes the place of the system's old one: every wreck there leaves the star map and the open
    /// flights at once, and is removed for good as soon as nobody is aboard — now, at the next defeat or at the next start
    /// (<see cref="CruiserWreckRecord.Replaced"/> remembers it).</summary>
    private void RetireCruiserWrecksIn(string systemId)
    {
        foreach (var record in _meta.CruiserWrecks.Where(r => r.SystemId == systemId || r.Replaced).ToList())
        {
            record.Replaced = true;
            if (_cruiserWrecks.TryGetValue(record.Id, out var site))
            {
                _galaxy.Systems.FirstOrDefault(s => s.Id == record.SystemId)?.Bodies.Remove(site.Body);
            }

            foreach (var instance in _spaceInstances.Values)
            {
                bool had = instance.Structures.Remove(record.Id);
                had |= instance.Entities.RemoveAll(e => e.Id == record.Id) > 0;
                if (had)
                {
                    BroadcastToInstance(instance, new SpaceEntityDestroyed { Id = record.Id });
                }
            }

            if (!_boardedStation.Values.Contains(record.Id))
            {
                PurgeCruiserWreck(record);
            }
        }
    }

    /// <summary>Removes a wreck for good: its record, its registrations, the players' stored changes (the delta blob, the
    /// derived record, the door records), its interior world (the overlay, the block edits, the containers) and its chest
    /// marks. Only ever for a wreck nobody is aboard.</summary>
    private void PurgeCruiserWreck(CruiserWreckRecord record)
    {
        string id = record.Id, location = StationLocationIdPrefix + id;
        _cruiserWrecks.Remove(id);
        _derivedDirty.Remove(id);
        _derivedStations.Remove(id);
        _playerStationCells.Remove(id);
        _stationsById.Remove(id);
        _guardianBays.Remove(id);
        _stationAir.Remove(id);
        if (_worlds.Active?.LocationId != location)
        {
            _worlds.Unload(location); // never the world under the cursor (an empty one is dropped by the idle unload anyway)
        }

        const int everywhere = 1 << 28; // the interior world is this wreck's alone: every block edit of it goes
        _repo.RunInTransaction(() =>
        {
            _repo.DeleteStructureOverlay(DerivedDeltaLocation, DerivedDeltaKey(id));
            _repo.DeleteStructureOverlay(location, DerivedOverlayKey(id));
            if (_repo.LoadNamedBlob(DerivedRecordKey(id)) is { Length: > 0 })
            {
                _repo.SaveNamedBlob(DerivedRecordKey(id), string.Empty);
            }

            _repo.DeleteStructureEdits(id);
            _repo.DeleteBlockEdits(location, new Vector3i(-everywhere, -everywhere, -everywhere), new Vector3i(everywhere, everywhere, everywhere));
            foreach (var container in _repo.ListContainers(location))
            {
                _repo.DeleteContainer(container.Id);
            }

            _meta.GeneratedLoot.RemoveAll(k => k.StartsWith(GiantChestLootPrefix + id + ":", System.StringComparison.Ordinal));
            _meta.CruiserWrecks.Remove(record);
            _repo.SaveMetadata(_meta);
        });
        _log.Info($"Cruiser wreck '{id}' was replaced by a newer one — removed.");
    }

    // ---------------------------------------------------------------- test seams

    /// <summary>Test seam (#3902): the cruiser wrecks on the star map (station ids).</summary>
    public IReadOnlyList<string> CruiserWreckIdsForTest
        => _cruiserWrecks.Values.Where(s => !s.Missing && !s.Record.Replaced).Select(s => s.Id).OrderBy(k => k, System.StringComparer.Ordinal).ToList();

    /// <summary>Test seam (#3902): a cruiser wreck, built on the way — its console, lamps, guardian bay and chests (build
    /// cells), its hull's half size and its flight position; null for an id that is none.</summary>
    public (Vector3i? Console, IReadOnlyList<Vector3i> Lamps, Vector3i? Bay, IReadOnlyList<Vector3i> Chests, Vector3f Half, Vector3f Position)? CruiserWreckForTest(string id)
        => EnsureCruiserWreck(id) is { } station && _cruiserWrecks.TryGetValue(id, out var site)
            ? (site.Console, site.Lamps, site.SpawnerSpot, site.Chests.Select(c => c.LocalPos).ToList(), site.Half, station.SpacePosition)
            : null;
}
