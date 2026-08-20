// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// One-of-a-kind sites (#1129, D6): places that exist exactly ONCE per galaxy — something a player can
/// tell a friend about. The Singing Shrine (a ring of rune pillars tended by quiet keepers — the playtest
/// wish: "alien shrines with lots of aliens"), the Sealed Observatory (a glass-domed survey post of the old
/// Service), and "The Long Quiet" — a named, boardable derelict station drifting in one system's space.
/// Site bodies are a PURE function of the seed over the galaxy's FIXED prefix (the frontier-tier model:
/// galaxy growth appends systems, so a grown galaxy can never move an already-chosen site), and the landed
/// position is pinned by the #586 placement registry — worlds stamped before the feature record a skip, so
/// nothing ever materialises retroactively under a player's base. Loot rides the ordinary structure-loot
/// tables (no power creep — the reward is the place, its lore and its Codex entry).
/// </summary>
public sealed partial class GameServer
{
    internal const string ShrineSiteKey = "alien_shrine";
    internal const string ObservatorySiteKey = "observatory";
    internal const string DerelictStationId = "derelict:longquiet";
    internal const string DerelictName = "The Long Quiet";

    private const int ShrineKeeperCount = 6;

    /// <summary>The body a unique surface site stands on — seed-pure over the ordinal-sorted landable
    /// bodies of the galaxy's FIXED prefix (grown systems excluded, so growth never moves a site).
    /// <paramref name="excludeBodyId"/> keeps two sites off the same world.</summary>
    internal string? UniqueSiteBodyId(string siteKey, string? excludeBodyId = null)
    {
        if (_galaxy is null)
        {
            return null;
        }

        int fixedCount = Math.Max(1, _meta.Description.StarSystemCount);
        var bodies = _galaxy.Systems
            .Where(s => s.Id.StartsWith("sys", StringComparison.Ordinal)
                && int.TryParse(s.Id.Substring(3), out int idx) && idx < fixedCount)
            .SelectMany(s => s.Bodies)
            .Where(b => (b.Kind == CelestialKind.Planet || b.Kind == CelestialKind.Moon)
                && !string.IsNullOrEmpty(b.PlanetType) && b.Id != excludeBodyId)
            .OrderBy(b => b.Id, StringComparer.Ordinal)
            .ToList();
        if (bodies.Count == 0)
        {
            return null;
        }

        uint h = (uint)(_meta.Seed ^ WorldGenerator.StableHash("uniquesite:" + siteKey));
        return bodies[(int)(h % (uint)bodies.Count)].Id;
    }

    private string? ShrineBodyId() => UniqueSiteBodyId(ShrineSiteKey);

    private string? ObservatoryBodyId() => UniqueSiteBodyId(ObservatorySiteKey, ShrineBodyId());

    /// <summary>The derelict's HOST body (its space instance holds the wreck) — salted separately, and it
    /// may share a body with a surface site (they never meet: one is on the ground, one is in orbit).</summary>
    private string? DerelictHostBodyId() => UniqueSiteBodyId("derelict_host");

    // ---------------- Surface sites: stamp / re-derive ----------------

    /// <summary>Stamps (or re-derives) this world's unique site, if it carries one. Runs at the end of the
    /// stamp chain so the search can avoid everything else that already landed.</summary>
    private void StampUniqueSites()
    {
        if (_world.LocationId == ShrineBodyId())
        {
            StampUniqueSite(ShrineSiteKey, 17, WriteShrine, AfterShrine);
        }
        else if (_world.LocationId == ObservatoryBodyId())
        {
            StampUniqueSite(ObservatorySiteKey, 11, WriteObservatory, AfterObservatory);
        }
    }

    /// <summary>The shared once-per-world placement dance (the vault_frontier pattern): replay a pinned
    /// record, skip on pre-feature worlds, else search a clear spot, write the blocks once and pin it.
    /// <paramref name="afterStamp"/> re-runs EVERY load (loot spawn is idempotent, NPCs are transient).</summary>
    private void StampUniqueSite(string siteKey, int footprint, Action<int, int, int> writeBlocks, Action<int, int, int> afterStamp)
    {
        string kind = "unique:" + siteKey;
        var record = FindPlacementRecord(kind, 0);
        if (record is not null)
        {
            if (record.Placed)
            {
                afterStamp(record.X, record.GroundY, record.Z);
            }

            return;
        }

        if (!_worlds.Active.VirginAtLoad)
        {
            // A world stamped before the feature keeps its ground untouched — the site simply "was
            // elsewhere all along" (the body choice is galaxy-wide; this record only remembers the skip).
            RecordPlacementSkip(kind, 0);
            SavePlacementRecords();
            return;
        }

        var planet = _world.Planet;
        long seed = _meta.Seed ^ WorldGenerator.StableHash("uniquesite:" + siteKey + ":" + _world.LocationId);
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        int half = footprint / 2 + 2;

        bool Fits(int x, int z) => !OverlapsAnySettlement(x, z, half + 4) && !_generator.IsSurfaceLava(planet, x, z)
            && !_generator.TryGetWaterSurface(planet, x, z, out _, out _);

        int cx = 0, cz = 0;
        bool found = false;
        for (int attempt = 0; attempt < 140 && !found; attempt++)
        {
            double ang = rng.NextDouble() * Math.PI * 2.0;
            int dist = 90 + rng.Next(260);
            int x = WorldConstants.WrapX(pad0X + (int)(Math.Cos(ang) * dist), _world.Circumference);
            int z = pad0Z + (int)(Math.Sin(ang) * dist);
            if (Fits(x, z))
            {
                cx = x;
                cz = z;
                found = true;
            }
        }

        if (!found)
        {
            RecordPlacementSkip(kind, 0); // an all-lava/ocean surface — the galaxy pick was unlucky here
            SavePlacementRecords();
            return;
        }

        int groundY = _generator.SurfaceHeight(planet, cx, cz);
        writeBlocks(cx, groundY, cz);
        RecordPlacement(kind, 0, new Vector3i(cx, groundY, cz), groundY, onIsland: false, "flat", string.Empty);
        SavePlacementRecords();
        ReportStamp(kind, 1, 1);
        _log.Info($"Unique site '{siteKey}' stamped on {_world.LocationId} at ({cx},{groundY},{cz}).");
        afterStamp(cx, groundY, cz);
    }

    /// <summary>The Singing Shrine: a brick plinth, a ring of rune pillars, a humming green beacon — and
    /// glowing strip-light "voices" between the stones.</summary>
    private void WriteShrine(int cx, int groundY, int cz)
    {
        var brick = _content.GetBlock("ancient_brick")?.NumericId ?? BlockId.Air;
        var rune = _content.GetBlock("rune_stone")?.NumericId ?? BlockId.Air;
        var beam = _content.GetBlock("beam_block")?.NumericId ?? BlockId.Air;
        var light = _content.GetBlock("strip_light_cyan")?.NumericId ?? BlockId.Air;
        if (brick.IsAir || rune.IsAir)
        {
            return;
        }

        for (int dx = -6; dx <= 6; dx++)
            for (int dz = -6; dz <= 6; dz++)
            {
                if (dx * dx + dz * dz <= 36)
                {
                    SetSiteBlock(cx + dx, groundY, cz + dz, brick);
                }
            }

        for (int i = 0; i < 8; i++)
        {
            double a = i * Math.PI / 4.0;
            int px = cx + (int)Math.Round(Math.Cos(a) * 5.0);
            int pz = cz + (int)Math.Round(Math.Sin(a) * 5.0);
            for (int dy = 1; dy <= 3; dy++)
            {
                SetSiteBlock(px, groundY + dy, pz, rune);
            }

            if (!light.IsAir && i % 2 == 0)
            {
                SetSiteBlock(px, groundY + 4, pz, light); // the "voices": four softly glowing crowns
            }
        }

        SetSiteBlock(cx, groundY + 1, cz, rune);
        SetSiteBlock(cx, groundY + 2, cz, rune);
        if (!beam.IsAir)
        {
            SetSiteBlock(cx, groundY + 3, cz, beam); // the shrine's green song, visible from afar
        }
    }

    /// <summary>Runs every load on the shrine world: the relic cache (idempotent) + the keepers.</summary>
    private void AfterShrine(int cx, int groundY, int cz)
    {
        var lootRng = new Random(unchecked((int)WorldGenerator.StableHash("shrine-loot:" + _world.LocationId)));
        SpawnStructureLoot(ShrineSiteKey, "relic_cache", new Vector3f(cx + 2.5f, groundY + 1f, cz + 0.5f), lootRng);
        SpawnShrineKeepers(cx, groundY, cz);
    }

    /// <summary>"Lots of aliens": the shrine's quiet keepers — transient like every NPC, respawned per
    /// load, deterministic per world. They wander the ring, greet, and can hold dialogues (#1127).</summary>
    private void SpawnShrineKeepers(int cx, int groundY, int cz)
    {
        if (_npcs.Any(n => n.Settlement == "singing-shrine"))
        {
            return;
        }

        var rng = new Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("shrine-keepers:" + _world.LocationId))));
        for (int i = 0; i < ShrineKeeperCount; i++)
        {
            double a = i * Math.PI * 2.0 / ShrineKeeperCount + 0.4;
            var home = new Vector3f(
                cx + (float)Math.Cos(a) * 8f + 0.5f,
                groundY + 1f,
                cz + (float)Math.Sin(a) * 8f + 0.5f);
            var npc = MakeNpc("settler", "settlers", robotic: rng.Next(3) == 0, home, rng);
            npc.Settlement = "singing-shrine"; // keys their memory/greetings to the shrine, not a settlement
            _npcs.Add(npc);
        }

        BroadcastNpcs();
    }

    /// <summary>The Sealed Observatory: a granite drum under a glass dome, one narrow entrance, and the
    /// old Service's survey terminal still blinking inside.</summary>
    private void WriteObservatory(int cx, int groundY, int cz)
    {
        var stone = _content.GetBlock("granite")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? BlockId.Air;
        if (stone.IsAir || glass.IsAir)
        {
            return;
        }

        for (int dx = -4; dx <= 4; dx++)
            for (int dz = -4; dz <= 4; dz++)
            {
                int r2 = dx * dx + dz * dz;
                if (r2 > 16)
                {
                    continue;
                }

                SetSiteBlock(cx + dx, groundY, cz + dz, stone); // floor disc
                if (r2 >= 12) // the drum wall, with a 2-tall south doorway
                {
                    bool doorway = dx == 0 && dz > 0;
                    for (int dy = 1; dy <= 2 && !doorway; dy++)
                    {
                        SetSiteBlock(cx + dx, groundY + dy, cz + dz, stone);
                    }

                    SetSiteBlock(cx + dx, groundY + 3, cz + dz, stone); // lintel ring closes over the door
                }
            }

        // The dome: shrinking glass rings up to the cap.
        for (int dy = 4; dy <= 6; dy++)
        {
            int r = 6 - dy + 1; // 3, 2, 1
            for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx * dx + dz * dz >= (r - 1) * (r - 1) && dx * dx + dz * dz <= r * r)
                    {
                        SetSiteBlock(cx + dx, groundY + dy, cz + dz, glass);
                    }
                }
        }

        SetSiteBlock(cx, groundY + 7, cz, glass); // the cap
    }

    /// <summary>Runs every load on the observatory world: the survey terminal's cache (idempotent).</summary>
    private void AfterObservatory(int cx, int groundY, int cz)
    {
        var lootRng = new Random(unchecked((int)WorldGenerator.StableHash("observatory-loot:" + _world.LocationId)));
        SpawnStructureLoot(ObservatorySiteKey, "data_terminal", new Vector3f(cx + 0.5f, groundY + 1f, cz + 0.5f), lootRng);
    }

    private void SetSiteBlock(int x, int y, int z, BlockId id)
        => _world.SetBlock(new Vector3i(WorldConstants.WrapX(x, _world.Circumference), y, z), id);

    // ---------------- Map + hints ----------------

    /// <summary>Appends this world's unique-site marker to the POI list once an NPC has shared it.</summary>
    private void AppendUniqueSitePois(PlayerSession session, List<NetPoi> pois)
    {
        foreach (var siteKey in new[] { ShrineSiteKey, ObservatorySiteKey })
        {
            string bodyId = siteKey == ShrineSiteKey ? ShrineBodyId() ?? string.Empty : ObservatoryBodyId() ?? string.Empty;
            if (bodyId != _world.LocationId
                || !_meta.RevealedPois.Contains(_world.LocationId + "|uniquesite:" + siteKey)
                || FindPlacementRecord("unique:" + siteKey, 0) is not { Placed: true } record)
            {
                continue;
            }

            pois.Add(new NetPoi
            {
                Type = siteKey,
                Name = Localize(session.Locale, "poi." + siteKey),
                X = record.X + 0.5f,
                Z = record.Z + 0.5f,
            });
        }
    }

    /// <summary>An NPC who TRUSTS the player shares the legend of a unique site — galaxy-wide: the hint
    /// names the world it stands on (and gives a bearing when it is this very world). Returns empty when
    /// everything is already shared.</summary>
    private string TryEmitUniqueSiteHint(PlayerSession session)
    {
        foreach (var siteKey in new[] { ShrineSiteKey, ObservatorySiteKey })
        {
            string? bodyId = siteKey == ShrineSiteKey ? ShrineBodyId() : ObservatoryBodyId();
            if (bodyId is null)
            {
                continue;
            }

            string revealKey = bodyId + "|uniquesite:" + siteKey;
            if (_meta.RevealedPois.Contains(revealKey))
            {
                continue;
            }

            RevealPoi(revealKey);
            if (bodyId == _world.LocationId && FindPlacementRecord("unique:" + siteKey, 0) is { Placed: true } record)
            {
                return HintLine(session, "npc.hint.site_here", record.X + 0.5f, record.Z + 0.5f);
            }

            string bodyName = _galaxy?.FindBody(bodyId)?.Name ?? bodyId;
            return Localize(session.Locale, "npc.hint.site").Replace("{name}", bodyName);
        }

        return string.Empty;
    }

    // ---------------- The Long Quiet (boardable derelict) ----------------

    /// <summary>Registers the galaxy's one derelict at start: interior cells built in code, boardable by
    /// anyone (no owner), a star-map body in its host system. Purely derived — nothing persisted.</summary>
    private void RegisterUniqueDerelict()
    {
        string? host = DerelictHostBodyId();
        if (host is null || _stationsById.ContainsKey(DerelictStationId))
        {
            return;
        }

        var s = new SpaceStructure
        {
            Id = DerelictStationId,
            Kind = "station",
            OwnerId = string.Empty, // ownerless → anyone may board (CanBoardStation's rule)
            Name = DerelictName,
            Boardable = true,
            Position = new Vector3f(74f, 8f, -52f),
        };
        BuildDerelictCells(s);

        _playerStationCells[s.Id] = s;
        _stationHostBody[s.Id] = host;
        _stationsById[s.Id] = new BoardableStation
        {
            Id = s.Id,
            Name = DerelictName,
            SizeTier = "small",
            SpacePosition = s.Position,
            Origin = new Vector3i(8, 64, 8),
        };

        var sys = _galaxy?.Systems.FirstOrDefault(x => x.Bodies.Any(b => b.Id == host));
        if (sys is not null && sys.Bodies.All(b => b.Id != DerelictStationId))
        {
            sys.Bodies.Add(new CelestialBody
            {
                Id = DerelictStationId,
                Name = DerelictName,
                Kind = CelestialKind.SpaceStation,
                SystemId = sys.Id,
                Status = GenerationStatus.Discovered,
            });
        }

        _log.Info($"Unique derelict '{DerelictName}' drifts in the space of {host}.");
    }

    /// <summary>The wreck's interior: a breached hull box, dark and quiet — floor, walls with torn gaps,
    /// a viewport band, one airlock door. Loot containers spawn separately on first boarding.</summary>
    private void BuildDerelictCells(SpaceStructure s)
    {
        var wall = _content.GetBlock("iron_wall")?.NumericId ?? BlockId.Air;
        var glass = _content.GetBlock("glass")?.NumericId ?? BlockId.Air;
        var door = _content.GetBlock("door_slide")?.NumericId ?? BlockId.Air;
        if (wall.IsAir)
        {
            return;
        }

        for (int x = 0; x <= 10; x++)
            for (int z = 0; z <= 6; z++)
            {
                s.Set(new Vector3i(x, 0, z), wall); // deck
                bool breach = x >= 8 && z >= 4; // the torn stern corner — open to the void
                if (!breach)
                {
                    s.Set(new Vector3i(x, 4, z), wall); // ceiling
                }

                for (int y = 1; y <= 3; y++)
                {
                    bool edge = x == 0 || x == 10 || z == 0 || z == 6;
                    if (!edge || breach)
                    {
                        continue;
                    }

                    if (z == 0 && y == 2 && x >= 3 && x <= 7 && !glass.IsAir)
                    {
                        s.Set(new Vector3i(x, y, z), glass); // the viewport band
                    }
                    else if (!(x == 0 && z == 3 && y <= 2))
                    {
                        s.Set(new Vector3i(x, y, z), wall); // hull — with a 2-tall airlock gap at (0,*,3)
                    }
                }
            }

        if (!door.IsAir)
        {
            s.Set(new Vector3i(0, 1, 3), door); // the airlock leaf itself
        }

        s.Width = 11;
        s.Height = 5;
        s.Length = 7;
    }

    /// <summary>Adds the derelict + its dock contact to a freshly created space instance of its host body.</summary>
    private void AddDerelictToInstance(SpaceInstance instance)
    {
        string loc = instance.Id.StartsWith("space:", StringComparison.Ordinal) ? instance.Id.Substring(6) : instance.Id;
        if (loc != DerelictHostBodyId() || !_playerStationCells.TryGetValue(DerelictStationId, out var s))
        {
            return;
        }

        instance.Structures[s.Id] = s;
        if (instance.Entities.All(e => e.Id != s.Id))
        {
            instance.Entities.Add(new CombatEntity
            {
                Id = s.Id,
                Kind = CombatEntityKind.SpaceStation,
                Name = DerelictName,
                Hostile = false,
                Hull = 1f,
                HullMax = 1f,
                Position = s.Position,
            });
        }
    }

    /// <summary>First boarding of the derelict fills its salvage (idempotent via the loot ledger) — the
    /// ordinary structure-loot tables, so the find is flavour + lore, never power creep.</summary>
    private void SpawnDerelictLoot(BoardableStation station)
    {
        if (station.Id != DerelictStationId)
        {
            return;
        }

        var rng = new Random(unchecked((int)WorldGenerator.StableHash("derelict-loot:" + _meta.Seed)));
        var o = station.Origin;
        SpawnStructureLoot("derelict", "module", new Vector3f(o.X + 2.5f, o.Y + 1f, o.Z + 1.5f), rng);
        SpawnStructureLoot("derelict", "data_terminal", new Vector3f(o.X + 8.5f, o.Y + 1f, o.Z + 1.5f), rng);
        SpawnStructureLoot("derelict", "chest", new Vector3f(o.X + 5.5f, o.Y + 1f, o.Z + 5.5f), rng);
    }

    // ---------------- Test hooks ----------------

    public string? UniqueSiteBodyForTest(string siteKey) => siteKey switch
    {
        ShrineSiteKey => ShrineBodyId(),
        ObservatorySiteKey => ObservatoryBodyId(),
        "derelict_host" => DerelictHostBodyId(),
        _ => null,
    };

    public string UniqueSiteHintForTest(string playerId)
        => FindSessionByPlayerId(playerId) is { } s ? TryEmitUniqueSiteHint(s) : string.Empty;

    /// <summary>Test/inspection: how many shrine keepers currently populate the active world.</summary>
    public int ShrineKeepersForTest => _npcs.Count(n => n.Settlement == "singing-shrine");
}
