using System.Collections.Generic;
using System.Linq;
using Spacecraft.Shared.Geometry;
using Spacecraft.Shared.Primitives;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// Stamps a rare crashed-ship wreck (see <see cref="WreckGenerator"/>) onto the start planet's
/// surface, away from the landing zone and any settlement. Whether a planet has one is derived
/// deterministically from the world seed + planet, so wrecks are uncommon. The decayed hull is
/// stamped slightly sunk into the ground (half-buried crash pose). Wrecks are <b>not</b> protected
/// — they're left scavengeable; their loot/module/data-terminal markers become interaction points,
/// and the intact-hull repair mask is kept so the wreck can later be rebuilt into a flyable ship.
/// </summary>
public sealed partial class GameServer
{
    private bool _wreckStamped;
    private Vector3i _wreckOrigin;
    private WreckStructure? _wreck;
    private string _wreckName = string.Empty;
    private readonly List<(string Type, Vector3f Pos)> _wreckMarkers = new();

    /// <summary>Interaction points inside the stamped wreck (loot / module / data_terminal).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> WreckMarkers => _wreckMarkers;

    /// <summary>Name of the stamped wreck (empty if none on this world).</summary>
    public string WreckName => _wreckName;

    /// <summary>Whether a wreck was stamped on this world.</summary>
    public bool HasWreck => _wreckStamped;

    private void StampWreck()
    {
        var planet = _world.Planet;

        long wSeed = _meta.Seed ^ WorldGenerator.StableHash("wreck:" + planet.Key);
        var rng = new System.Random(unchecked((int)(wSeed ^ (wSeed >> 32))));

        if (rng.NextDouble() > WreckChance(planet))
        {
            return; // no wreck on this world
        }

        // Pick a ship design to have crashed (any in content; weighted to the smaller hulls).
        var designs = _content.Ships.Values.ToList();
        if (designs.Count == 0)
        {
            return;
        }

        var design = designs[rng.Next(designs.Count)];
        var structure = WreckGenerator.Generate(design, wSeed, _content);

        // Anchor offset from the landing zone — and offset differently from settlements so the two
        // don't overlap on the same world.
        int ax = -56, az = 56;
        foreach (var zone in _landingZones.Values)
        {
            ax = zone.CenterX - 56;
            az = zone.CenterZ + 56;
            break;
        }

        int groundY = _generator.SurfaceHeight(planet, ax, az);
        int baseY = groundY - 1; // half-buried crash pose: sink the hull one block into the ground
        _wreckOrigin = new Vector3i(ax, baseY, az);

        // Stamp only the wreck's solid blocks (breaches stay as the existing terrain/air).
        for (int x = 0; x < structure.Width; x++)
        for (int y = 0; y < structure.Height; y++)
        for (int z = 0; z < structure.Length; z++)
        {
            ushort b = structure.Get(x, y, z);
            if (b != 0)
            {
                _world.SetBlock(new Vector3i(ax + x, baseY + y, az + z), new BlockId(b));
            }
        }

        _wreck = structure;
        _wreckName = WreckDisplayName(structure.Origin, design, rng);

        _wreckMarkers.Clear();
        foreach (var m in structure.Markers)
        {
            var pos = new Vector3f(ax + m.LocalPos.X + 0.5f, baseY + m.LocalPos.Y + 0.5f, az + m.LocalPos.Z + 0.5f);
            _wreckMarkers.Add((m.Type, pos));

            // Loot caches, the recoverable module and the data terminal are all scavengeable.
            if (m.Type is "loot" or "module" or "data_terminal")
            {
                SpawnStructureLoot("wreck", m.Type, pos, rng);
            }
        }

        _wreckStamped = true;
        _log.Info($"Wreck '{_wreckName}' ({structure.Origin} {design.Key}) stamped at ({ax}, {baseY}, {az}) with {_wreckMarkers.Count} markers, {structure.BreachCount()} breaches.");
    }

    /// <summary>Per-planet probability of a wreck (rare everywhere; a touch likelier on lived-in worlds).</summary>
    private static double WreckChance(Shared.Definitions.PlanetType planet)
    {
        // Airless asteroids can still have a crash; keep it rare across the board.
        return (planet.CreatureAbundance ?? "few").ToLowerInvariant() switch
        {
            "many" => 0.30,
            "none" => 0.15,
            _ => 0.20,
        };
    }

    private static string WreckDisplayName(string origin, Shared.Definitions.ShipDefinition design, System.Random rng)
    {
        string[] tags = { "SC", "RV", "ISV", "XN", "KV" };
        int num = 100 + rng.Next(900);
        string prefix = origin == "alien" ? "Derelict" : "Wreck of the";
        return $"{prefix} {tags[rng.Next(tags.Length)]}-{num}";
    }
}
