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

    /// <summary>Interaction/spawn points inside the stamped settlement (vendor/mission_board/npc/loot).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> SettlementMarkers => _settlementMarkers;

    /// <summary>Name of the stamped settlement (empty if none on this world).</summary>
    public string SettlementName => _settlementName;

    /// <summary>Whether a settlement was stamped on this world.</summary>
    public bool HasSettlement => _settlementStamped;

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

        string tier = rng.NextDouble() < 0.35 ? "town" : "village";
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
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(origin.X + m.LocalPos.X + 0.5f, groundY + m.LocalPos.Y + 0.5f, origin.Z + m.LocalPos.Z + 0.5f);
            _settlementMarkers.Add((m.Type, pos));
        }

        _settlementStamped = true;
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
        string[] townSuffix = { " City", " Town", " Colony", " Outpost", " Heights" };
        string[] villageSuffix = { " Village", " Hamlet", " Cross", " Hollow", " Camp" };
        string root = roots[rng.Next(roots.Length)];
        string suffix = (tier == "town" ? townSuffix : villageSuffix)[rng.Next(5)];
        return ruined ? $"Ruins of {root}{suffix}" : $"{root}{suffix}";
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
