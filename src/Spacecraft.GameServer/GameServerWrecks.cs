using System.Collections.Generic;
using System.Linq;
using Spacecraft.Networking.Messages;
using Spacecraft.Shared.Definitions;
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
    private bool _wreckClaimed;
    private List<(string Type, Vector3f Pos)> _wreckMarkers => _worlds.Active.WreckMarkers;

    /// <summary>Interaction points inside the stamped wreck (loot / module / data_terminal).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> WreckMarkers => _wreckMarkers;

    /// <summary>Name of the stamped wreck (empty if none on this world).</summary>
    public string WreckName => _wreckName;

    /// <summary>Whether a wreck was stamped on this world.</summary>
    public bool HasWreck => _wreckStamped;

    /// <summary>Remaining hull cells that differ from the wreck's intact repair mask.</summary>
    public int WreckRepairRemaining => CountWreckRepairRemaining();

    /// <summary>Total hull cells in the intact repair mask.</summary>
    public int WreckRepairTotal => _wreck?.IntactHullCount() ?? 0;

    /// <summary>Whether the stamped wreck has already been claimed into the owned fleet.</summary>
    public bool WreckClaimed => _wreckClaimed;

    /// <summary>World-space cells that still need repair, with the required block key.</summary>
    public IReadOnlyList<(Vector3i Pos, string BlockKey)> WreckRepairCells()
    {
        var result = new List<(Vector3i Pos, string BlockKey)>();
        if (!_wreckStamped || _wreck is null)
        {
            return result;
        }

        foreach (var (pos, block) in EnumerateWreckRepairCells())
        {
            result.Add((pos, block.Key));
        }

        return result;
    }

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
        _wreckClaimed = false;

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

    /// <summary>Repairs one wreck hull cell if the player has the exact matching block item.</summary>
    public bool RepairWreck(string playerId, int x, int y, int z, string itemKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return false;
        }

        if (!_wreckStamped || _wreck is null)
        {
            Reject(session, "wreck_repair", "There is no repairable wreck on this world.");
            return false;
        }

        if (_wreckClaimed)
        {
            Reject(session, "wreck_repair", "This wreck has already been claimed.");
            return false;
        }

        var pos = new Vector3i(x, y, z);
        if (!TryGetWreckRepairTarget(pos, out var required))
        {
            Reject(session, "wreck_repair", "That cell is not part of the wreck's repair mask.");
            return false;
        }

        if (_world.GetBlock(pos) == required.NumericId)
        {
            Reject(session, "wreck_repair", "That hull cell is already repaired.");
            return false;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "wreck_repair", "Out of reach.");
            return false;
        }

        var item = _content.GetItem(itemKey);
        if (item is null || item.PlacesBlock != required.Key)
        {
            Reject(session, "wreck_repair", $"Repair requires a {required.Key} block item.");
            return false;
        }

        bool free = !Rules.CraftingCostsMaterials || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(itemKey) < 1)
            {
                Reject(session, "wreck_repair", "You do not have the required repair block.");
                return false;
            }

            pool.Remove(new[] { new ItemAmount(itemKey, 1) });
        }

        _world.SetBlock(pos, required.NumericId);
        Broadcast(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = required.NumericId.Value });
        SendInventory(session);
        SendWreckRepairStatus(session);
        return true;
    }

    /// <summary>Claims a fully repaired wreck into the player's owned ship fleet.</summary>
    public (bool Ok, string ShipId) ClaimWreck(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return (false, string.Empty);
        }

        if (!_wreckStamped || _wreck is null)
        {
            Reject(session, "wreck_claim", "There is no wreck to claim.");
            return (false, string.Empty);
        }

        if (_wreckClaimed)
        {
            Reject(session, "wreck_claim", "This wreck has already been claimed.");
            return (false, string.Empty);
        }

        if (CountWreckRepairRemaining() > 0)
        {
            Reject(session, "wreck_claim", "The wreck hull is not fully repaired.");
            SendWreckRepairStatus(session);
            return (false, string.Empty);
        }

        var def = _content.GetShip(_wreck.ShipType);
        if (def is null)
        {
            Reject(session, "wreck_claim", "The wreck's ship design is unknown.");
            return (false, string.Empty);
        }

        string id = AddOwnedShipFromDefinition(def, "wreck");
        _wreckClaimed = true;
        Send(session, new ServerMessage { Text = $"Claimed repaired wreck: {def.Key}" });
        SendWreckRepairStatus(session);
        return (true, id);
    }

    private int CountWreckRepairRemaining() => EnumerateWreckRepairCells().Count();

    private IEnumerable<(Vector3i Pos, BlockDefinition Block)> EnumerateWreckRepairCells()
    {
        if (!_wreckStamped || _wreck is null)
        {
            yield break;
        }

        for (int x = 0; x < _wreck.Width; x++)
        for (int y = 0; y < _wreck.Height; y++)
        for (int z = 0; z < _wreck.Length; z++)
        {
            ushort intact = _wreck.IntactAt(x, y, z);
            if (intact == 0)
            {
                continue;
            }

            var world = new Vector3i(_wreckOrigin.X + x, _wreckOrigin.Y + y, _wreckOrigin.Z + z);
            if (_world.GetBlock(world).Value == intact)
            {
                continue;
            }

            if (_content.BlockById(new BlockId(intact)) is { } required)
            {
                yield return (world, required);
            }
        }
    }

    private bool TryGetWreckRepairTarget(Vector3i world, out BlockDefinition required)
    {
        required = null!;
        if (!_wreckStamped || _wreck is null)
        {
            return false;
        }

        int lx = world.X - _wreckOrigin.X;
        int ly = world.Y - _wreckOrigin.Y;
        int lz = world.Z - _wreckOrigin.Z;
        if (!_wreck.InBounds(lx, ly, lz))
        {
            return false;
        }

        ushort intact = _wreck.IntactAt(lx, ly, lz);
        if (intact == 0)
        {
            return false;
        }

        required = _content.BlockById(new BlockId(intact))!;
        return required is not null;
    }

    private void SendWreckRepairStatus(PlayerSession session)
        => Send(session, new WreckRepairStatus
        {
            WreckName = _wreckName,
            ShipType = _wreck?.ShipType ?? string.Empty,
            Remaining = WreckRepairRemaining,
            Total = WreckRepairTotal,
            Claimable = _wreckStamped && !_wreckClaimed && WreckRepairRemaining == 0,
            Claimed = _wreckClaimed,
            Needs = string.Join(",", WreckRepairCells().Select(c => c.BlockKey).Distinct()),
        });

    private void HandleRepairWreck(PlayerSession session, RepairWreckIntent intent)
        => RepairWreck(session.State.PlayerId, intent.X, intent.Y, intent.Z, intent.ItemKey);

    private void HandleClaimWreck(PlayerSession session)
        => ClaimWreck(session.State.PlayerId);
}
