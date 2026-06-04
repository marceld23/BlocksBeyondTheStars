using Spacecraft.Persistence;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;
using Spacecraft.WorldGeneration;

namespace Spacecraft.GameServer;

/// <summary>
/// One loaded voxel world for a celestial body: the <see cref="ServerWorld"/> plus the ids that identify
/// it. This is the unit the <see cref="WorldManager"/> hands out. Per-world runtime state (fauna, flora,
/// fluids, containers, structures) still lives on <c>GameServer</c> today; it moves here in the multi-world
/// phase so several bodies can be resident at once (one per occupied location).
/// </summary>
internal sealed class LoadedWorld
{
    public required ServerWorld World { get; init; }
    public required string LocationId { get; init; }
    public required string PlanetType { get; init; }
}

/// <summary>
/// Owns the voxel world(s) the server has resident. Today it tracks a single <see cref="Active"/> world and
/// (re)builds it on activation — behaviour-identical to the previous single-<c>_world</c> field — but it is
/// the seam the multi-world work grows from: a dictionary of loaded worlds keyed by location, loaded on
/// demand and unloaded when empty, with each player tracking which one they're in.
/// </summary>
internal sealed class WorldManager
{
    private readonly GameContent _content;
    private readonly WorldGenerator _generator;
    private readonly IWorldRepository _repo;

    public WorldManager(GameContent content, WorldGenerator generator, IWorldRepository repo)
    {
        _content = content;
        _generator = generator;
        _repo = repo;
    }

    /// <summary>The currently active world (the one the single shared session pool operates on today).</summary>
    public LoadedWorld Active { get; private set; } = null!;

    /// <summary>(Re)builds the world for a body and makes it active. A fresh <see cref="ServerWorld"/> is
    /// created each call (matching the prior behaviour); caching/unloading arrives with multi-world.</summary>
    public LoadedWorld Activate(PlanetType planet, string locationId)
    {
        Active = new LoadedWorld
        {
            World = new ServerWorld(_content, _generator, _repo, planet, locationId),
            LocationId = locationId,
            PlanetType = planet.Key,
        };
        return Active;
    }
}
