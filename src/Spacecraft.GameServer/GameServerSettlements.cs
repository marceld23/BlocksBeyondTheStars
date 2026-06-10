using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// Stamps a procedural settlement (see <see cref="SettlementGenerator"/>) onto the start planet's
/// surface, away from the player's landing zone. Whether a planet has one — and whether it's a
/// village/town and intact/ruined — is derived deterministically from the world seed + planet, so
/// barren/hostile worlds get none. A flattened foundation is laid under the build so it sits cleanly
/// on uneven terrain. Intact settlements are mining-protected (like the ship); ruins are left
/// scavengeable. Interactive markers (vendor/mission_board/npc/loot) become interaction points.
/// </summary>
public sealed partial class GameServer
{
    private bool _settlementStamped { get => _worlds.Active.SettlementStamped; set => _worlds.Active.SettlementStamped = value; }
    private Vector3i _settlementMin { get => _worlds.Active.SettlementMin; set => _worlds.Active.SettlementMin = value; }
    private Vector3i _settlementMax { get => _worlds.Active.SettlementMax; set => _worlds.Active.SettlementMax = value; }
    private bool _settlementRuined { get => _worlds.Active.SettlementRuined; set => _worlds.Active.SettlementRuined = value; }
    private string _settlementName { get => _worlds.Active.SettlementName; set => _worlds.Active.SettlementName = value; }
    private string _settlementInhabitant { get => _worlds.Active.SettlementInhabitant; set => _worlds.Active.SettlementInhabitant = value; }
    private List<(string Type, Vector3f Pos)> _settlementMarkers => _worlds.Active.SettlementMarkers;

    /// <summary>Mission ids offered by this settlement's board (only acceptable/turn-in-able there).</summary>
    private HashSet<string> _settlementMissionIds => _worlds.Active.SettlementMissionIds;

    /// <summary>Interaction/spawn points inside the stamped settlement (vendor/mission_board/npc/loot).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> SettlementMarkers => _settlementMarkers;

    /// <summary>Name of the stamped settlement (empty if none on this world).</summary>
    public string SettlementName => _settlementName;

    /// <summary>Whether a settlement was stamped on this world.</summary>
    public bool HasSettlement => _settlementStamped;

    /// <summary>Whether the stamped settlement is a ruin (abandoned — no NPCs, scavengeable loot).</summary>
    public bool SettlementRuined => _settlementRuined;

    /// <summary>Sends the planet's points of interest (the settlement) for the world map.</summary>
    private void SendPlanetPois(PlayerSession session)
    {
        var pois = new List<NetPoi>();
        if (_settlementStamped)
        {
            pois.Add(new NetPoi
            {
                Type = _settlementRuined ? "settlement_ruin" : "settlement",
                Name = _settlementName,
                X = (_settlementMin.X + _settlementMax.X) * 0.5f,
                Z = (_settlementMin.Z + _settlementMax.Z) * 0.5f,
            });
        }

        // Buried vault ruins (W-R3): the surface pillar rings show on the map as discovery targets.
        for (int i = 0; i < _vaultEntrances.Count; i++)
        {
            pois.Add(new NetPoi
            {
                Type = "vault_ruin",
                Name = "Ruin " + (char)('A' + i),
                X = _vaultEntrances[i].X,
                Z = _vaultEntrances[i].Z,
            });
        }

        Send(session, new PlanetPoiList { Pois = pois.ToArray() });
    }

    private void StampSettlement()
    {
        var planet = _world.Planet;

        // Deterministic per planet + seed. Lifeless/airless worlds never have settlements; lush
        // ones are most likely. A separate roll decides ruined vs inhabited, and village vs town.
        long sSeed = _meta.Seed ^ WorldGenerator.StableHash("settlement:" + planet.Key);
        var rng = new System.Random(unchecked((int)(sSeed ^ (sSeed >> 32))));

        double chance = SettlementChance(planet);
        if (chance <= 0 || rng.NextDouble() > chance)
        {
            return; // this world has no settlement
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        string tier;
        bool ruined;
        SettlementStructure structure;

        // Chance to stamp a hand-designed template from the pool (when one exists); otherwise procedural.
        // The template roll is only drawn when the pool is non-empty, so default worlds are unchanged.
        var pool = _content.SettlementTemplates;
        if (pool.Count > 0 && rng.NextDouble() < StructureTemplateChance)
        {
            var template = pool[rng.Next(pool.Count)];
            tier = template.Tier;
            ruined = false;
            structure = SettlementGenerator.FromTemplate(template, _content);
        }
        else
        {
            // Four size tiers, tiny hamlets most-to-least common up to rare sprawling cities.
            double tierRoll = rng.NextDouble();
            tier = tierRoll < 0.15 ? "hamlet"
                 : tierRoll < 0.55 ? "village"
                 : tierRoll < 0.85 ? "town"
                 : "city";
            ruined = rng.NextDouble() < 0.30;
            structure = SettlementGenerator.Generate(tier, ruined, sSeed, surface, _content);
        }

        // Anchor it a fixed offset from the first landing pad so it doesn't overlap a pad/ship (item 38).
        int ax = 64, az = 64;
        if (_landingPads.Count > 0)
        {
            ax = _landingPads[0].CenterX + 48;
            az = _landingPads[0].CenterZ + 48;
        }

        int groundY = _generator.SurfaceHeight(planet, ax, az);
        var origin = new Vector3i(ax, groundY, az); // structure y=0 sits at ground level
        var foundationId = _content.GetBlock(surface)?.NumericId ?? BlockId.Air;

        // 1) Flatten a foundation slab at ground level across the footprint (so it sits flush).
        if (!foundationId.IsAir)
        {
            for (int x = 0; x < structure.Width; x++)
            for (int z = 0; z < structure.Length; z++)
            {
                _world.SetBlock(new Vector3i(origin.X + x, groundY, origin.Z + z), foundationId);
            }
        }

        // 2) Stamp the structure above the foundation (y=0 of the structure is the foundation row).
        for (int x = 0; x < structure.Width; x++)
        for (int y = 0; y < structure.Height; y++)
        for (int z = 0; z < structure.Length; z++)
        {
            ushort b = structure.Get(x, y, z);
            if (b != 0)
            {
                _world.SetBlock(new Vector3i(origin.X + x, groundY + y, origin.Z + z), new BlockId(b));
            }
        }

        // 3) Record bounds, markers (in world space) + metadata.
        _settlementMin = origin;
        _settlementMax = new Vector3i(origin.X + structure.Width - 1, groundY + structure.Height - 1, origin.Z + structure.Length - 1);
        _settlementRuined = ruined;
        _settlementInhabitant = structure.Inhabitant;
        _settlementName = SettlementDisplayName(tier, ruined, rng);

        _settlementMarkers.Clear();
        _settlementMissionIds.Clear();
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(origin.X + m.LocalPos.X + 0.5f, groundY + m.LocalPos.Y + 0.5f, origin.Z + m.LocalPos.Z + 0.5f);
            _settlementMarkers.Add((m.Type, pos));

            // Ruined settlements leave scavengeable loot caches at their loot markers.
            if (m.Type == "loot")
            {
                SpawnStructureLoot("settlement", m.Type, pos, rng);
            }
        }

        // An inhabited settlement's mission board offers an endless rolling set of gather missions: seed the
        // first window now, then the per-player mission-giver window (item 13) slides it so it never runs dry.
        if (!ruined && _settlementMarkers.Any(m => m.Type == "mission_board"))
        {
            string prefix = $"settle_{(uint)WorldGenerator.StableHash(_settlementName) % 100000u}_";
            StockBoard(prefix, _settlementName, _settlementMissionIds, CoinGiverName(_settlementName));
        }

        _settlementStamped = true;

        // Inhabited settlements are populated with NPCs at their vendor/board/npc markers.
        SpawnSettlementNpcs(rng);

        // Doorways get real doors: sci-fi sliders for cities/towns, hinged doors for villages/hamlets.
        RegisterDoors();

        _log.Info($"Settlement '{_settlementName}' ({tier}{(ruined ? ", ruined" : "")}) stamped at ({ax}, {groundY}, {az}) with {_settlementMarkers.Count} markers.");
    }

    /// <summary>Per-planet probability of having a settlement (lush worlds high, barren/airless none).</summary>
    private static double SettlementChance(Shared.Definitions.PlanetType planet)
    {
        if (string.Equals(planet.Atmosphere, "none", System.StringComparison.OrdinalIgnoreCase))
        {
            return 0; // airless bodies (asteroids) are uninhabited
        }

        return (planet.CreatureAbundance ?? "few").ToLowerInvariant() switch
        {
            "many" => 0.8,
            "none" => 0.1,
            _ => 0.4, // "few"
        };
    }

    private static string SettlementDisplayName(string tier, bool ruined, System.Random rng)
    {
        string[] roots = { "Karth", "Vega", "Mira", "Dorn", "Ysel", "Tarn", "Olun", "Reth", "Sabik", "Cael" };
        string[] citySuffix = { " City", " Metropolis", " Prime", " Central" };
        string[] townSuffix = { " Town", " Colony", " Outpost", " Heights" };
        string[] hamletSuffix = { " Hamlet", " Cross", " Camp", " Rest" };
        string[] villageSuffix = { " Village", " Hollow", " Glen", " Stead", " End" };
        string[] suffixes = tier switch
        {
            "city" => citySuffix,
            "town" => townSuffix,
            "hamlet" => hamletSuffix,
            _ => villageSuffix,
        };
        string root = roots[rng.Next(roots.Length)];
        string suffix = suffixes[rng.Next(suffixes.Length)];
        return ruined ? $"Ruins of {root}{suffix}" : $"{root}{suffix}";
    }

    private const float SettlementVendorReach = 4f;
    private const float SettlementBoardReach = 4f;

    /// <summary>The four settlement trade professions. A settlement's profession (derived deterministically from
    /// its name) themes its NPCs and decides which market goods its vendor posts.</summary>
    private static readonly string[] SettlementTrades = { "miners", "traders", "researchers", "settlers" };

    /// <summary>Deterministic trade profession for a settlement (stable from its name), so a mining village and a
    /// trade city always offer their own distinct barter — and both the NPC theme and the market filter agree.</summary>
    private static string SettlementTradeFor(string name)
        => string.IsNullOrEmpty(name)
            ? "settlers"
            : SettlementTrades[(uint)WorldGenerator.StableHash(name) % (uint)SettlementTrades.Length];

    /// <summary>The trade profession for one vendor among several at a location (B55): the first vendor keeps the
    /// location's own theme (so the place keeps its identity), and each additional vendor gets its own
    /// deterministic profession — so a station/settlement with multiple vendors offers several distinct barters
    /// (and visibly distinct crew) instead of every vendor selling the same goods.</summary>
    private static string VendorThemeFor(string locationName, int vendorIndex, string baseTheme)
        => vendorIndex <= 0
            ? baseTheme
            : SettlementTrades[(uint)WorldGenerator.StableHash(locationName + ":vendor:" + vendorIndex) % (uint)SettlementTrades.Length];

    /// <summary>Test seam for the per-vendor theme derivation (B55).</summary>
    public static string VendorThemeForTest(string locationName, int vendorIndex, string baseTheme)
        => VendorThemeFor(locationName, vendorIndex, baseTheme);

    /// <summary>The trade theme of the vendor the player is standing at (settlement or boarded station), or empty
    /// when none is in reach (B55). Drives which themed market goods the server accepts — per actual vendor, not
    /// one theme per location — so different vendors at one place trade different goods.</summary>
    private string VendorThemeAt(Shared.State.PlayerState player)
        => (NearSettlementVendor(player) || NearSpaceStationVendor(player)) && NearestNpc(player, "vendor") is { } v
            ? v.Theme
            : string.Empty;

    /// <summary>True if the player is standing next to a settlement vendor (enables market barter there).</summary>
    public bool NearSettlementVendor(Shared.State.PlayerState player)
        => NearMarker(player, "vendor", SettlementVendorReach);

    /// <summary>True if the player is standing next to the settlement's mission board.</summary>
    public bool NearSettlementMissionBoard(Shared.State.PlayerState player)
        => NearMarker(player, "mission_board", SettlementBoardReach);

    private bool NearMarker(Shared.State.PlayerState player, string type, float reach)
    {
        foreach (var (markerType, pos) in _settlementMarkers)
        {
            if (markerType == type && WrapDistSq(player.Position, pos) <= reach * reach)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True if a mission is one offered by the settlement board (board-gated accept/turn-in).</summary>
    public bool IsSettlementMission(string missionId) => _settlementMissionIds.Contains(missionId);

    /// <summary>Mission ids offered by the settlement board (test/inspection).</summary>
    public IReadOnlyCollection<string> SettlementMissionIds => _settlementMissionIds;

    /// <summary>True if the block belongs to an intact (protected) settlement — ruins are scavengeable.</summary>
    public bool IsSettlementBlock(Vector3i pos)
    {
        if (!_settlementStamped || _settlementRuined)
        {
            return false;
        }

        return pos.X >= _settlementMin.X && pos.X <= _settlementMax.X
            && pos.Y >= _settlementMin.Y && pos.Y <= _settlementMax.Y
            && pos.Z >= _settlementMin.Z && pos.Z <= _settlementMax.Z;
    }
}
