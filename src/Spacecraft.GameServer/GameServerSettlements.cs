using System.Collections.Generic;
using System.Linq;
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
    private bool _settlementStamped;
    private Vector3i _settlementMin, _settlementMax;
    private bool _settlementRuined;
    private string _settlementName = string.Empty;
    private string _settlementInhabitant = string.Empty;
    private readonly List<(string Type, Vector3f Pos)> _settlementMarkers = new();

    /// <summary>Mission ids offered by this settlement's board (only acceptable/turn-in-able there).</summary>
    private readonly HashSet<string> _settlementMissionIds = new();

    /// <summary>Interaction/spawn points inside the stamped settlement (vendor/mission_board/npc/loot).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> SettlementMarkers => _settlementMarkers;

    /// <summary>Name of the stamped settlement (empty if none on this world).</summary>
    public string SettlementName => _settlementName;

    /// <summary>Whether a settlement was stamped on this world.</summary>
    public bool HasSettlement => _settlementStamped;

    /// <summary>Whether the stamped settlement is a ruin (abandoned — no NPCs, scavengeable loot).</summary>
    public bool SettlementRuined => _settlementRuined;

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

        // Four size tiers, tiny hamlets most-to-least common up to rare sprawling cities.
        double tierRoll = rng.NextDouble();
        string tier = tierRoll < 0.15 ? "hamlet"
                    : tierRoll < 0.55 ? "village"
                    : tierRoll < 0.85 ? "town"
                    : "city";
        bool ruined = rng.NextDouble() < 0.30;

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;
        var structure = SettlementGenerator.Generate(tier, ruined, sSeed, surface, _content);

        // Anchor it a fixed offset from the spawn/landing zone so it doesn't overlap the ship.
        int ax = 64, az = 64;
        foreach (var zone in _landingZones.Values)
        {
            ax = zone.CenterX + 48;
            az = zone.CenterZ + 48;
            break;
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
        bool hasBoard = false;
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(origin.X + m.LocalPos.X + 0.5f, groundY + m.LocalPos.Y + 0.5f, origin.Z + m.LocalPos.Z + 0.5f);
            _settlementMarkers.Add((m.Type, pos));

            // Ruined settlements leave scavengeable loot caches at their loot markers.
            if (m.Type == "loot")
            {
                SpawnStructureLoot("settlement", m.Type, pos, rng);
            }
            else if (m.Type == "mission_board")
            {
                hasBoard = true;
            }
        }

        // An inhabited settlement's mission board offers a couple of local gather missions
        // (only acceptable/turn-in-able while standing at the board).
        if (hasBoard && !ruined)
        {
            GenerateSettlementMissions(rng);
        }

        _settlementStamped = true;

        // Inhabited settlements are populated with NPCs at their vendor/board/npc markers.
        SpawnSettlementNpcs(rng);

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
            if (markerType == type && player.Position.DistanceSquared(pos) <= reach * reach)
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

    /// <summary>Generates a couple of deterministic local gather missions for the settlement board.</summary>
    private void GenerateSettlementMissions(System.Random rng)
    {
        // (deliver item, target, reward item, reward count) pools — all real content items.
        (string Need, int Target, string Reward, int RewardN)[] templates =
        {
            ("iron_ore", 10, "iron_plate", 3),
            ("carbon", 8, "cable", 2),
            ("silicate", 8, "energy_cell_1", 1),
            ("copper_ore", 10, "cable", 3),
            ("crystal", 5, "titanium_plate", 2),
        };

        int count = 1 + rng.Next(2); // 1–2 missions
        var used = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int t = rng.Next(templates.Length);
            if (!used.Add(t))
            {
                continue;
            }

            var tpl = templates[t];
            if (_content.GetItem(tpl.Need) is null || _content.GetItem(tpl.Reward) is null)
            {
                continue;
            }

            var def = new Shared.Missions.MissionDefinition
            {
                Id = $"settle_{(uint)WorldGenerator.StableHash(_settlementName) % 100000u}_{i}",
                Source = Shared.Missions.MissionSource.System,
                NameKey = "mission.settlement.gather.title",
                DescriptionKey = "mission.settlement.gather.desc",
                Objectives =
                {
                    new Shared.Missions.MissionObjective
                    {
                        Type = Shared.Missions.MissionObjectiveType.Deliver,
                        Target = tpl.Need,
                        Required = tpl.Target,
                    },
                },
                Rewards = { new Shared.Definitions.ItemAmount(tpl.Reward, tpl.RewardN) },
                Active = true,
            };

            _missionDefs[def.Id] = def;
            _settlementMissionIds.Add(def.Id);
        }
    }

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
