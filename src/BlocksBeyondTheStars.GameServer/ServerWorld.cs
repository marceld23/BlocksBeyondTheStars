// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The authoritative runtime world for the active planet. Chunks are generated on demand
/// from the seed and have persisted player edits applied on top; only edits are stored
/// (technical requirements §11). Loaded chunks are cached and unloaded when far from all
/// players.
/// </summary>
public sealed class ServerWorld
{
    private readonly GameContent _content;
    private readonly WorldGenerator _generator;
    private readonly IWorldRepository _repo;
    private readonly Dictionary<ChunkCoord, ChunkData> _loaded = new();

    public PlanetType Planet { get; }
    public string PlanetKey { get; }

    /// <summary>
    /// The persistence key for this world's edits/landing-zones/containers. It is the celestial
    /// <b>body id</b> (e.g. "sys0-p1"), not the planet <i>type</i> — so two different bodies of the
    /// same type keep separate edits, and revisiting a planet restores what you left there.
    /// </summary>
    public string LocationId { get; }

    /// <summary>This world's walkable east–west circumference (longitude wrap + the noise circular domain).
    /// Varies by body size (asteroids small, planets large); the seam-free generation + chunk keys all use it.</summary>
    public int Circumference { get; }

    /// <summary>This world's planned landing pads for generation-time terrain levelling (ship-as-object).
    /// Filled by the server's deterministic pad planning right after the world becomes resident — BEFORE
    /// any pad-area chunk generates — and re-applied to the shared generator per chunk generation.</summary>
    public List<LandingPadFlatten> LandingPadFlats { get; } = new();

    /// <summary>Whether this body generates with airless-moon cratering (item 33). Stamped by the server
    /// at world-load so every chunk generation can fully re-configure the shared generator (#424 S13).</summary>
    public bool Cratered { get; set; }

    public ServerWorld(GameContent content, WorldGenerator generator, IWorldRepository repo, PlanetType planet, string locationId, int circumference)
    {
        _content = content;
        _generator = generator;
        _repo = repo;
        Planet = planet;
        PlanetKey = planet.Key;
        LocationId = string.IsNullOrEmpty(locationId) ? planet.Key : locationId;
        Circumference = circumference > 0 ? circumference : WorldConstants.Circumference;
    }

    public int LoadedChunkCount => _loaded.Count;

    /// <summary>Whether a chunk is currently resident in the cache (canonicalized like the cache keys). For
    /// tests/diagnostics — e.g. asserting far-chunk eviction by <see cref="UnloadFarChunks"/>.</summary>
    public bool IsChunkLoaded(ChunkCoord coord) => _loaded.ContainsKey(WorldConstants.CanonicalChunk(coord, Circumference));

    public ChunkData GetOrLoadChunk(ChunkCoord coord)
    {
        // Longitude wraps: a chunk a whole lap away is the same chunk. Canonicalize X so the cache and
        // persistence stay coherent across the seam (terrain itself is generated seam-free, so generating
        // at the canonical coord yields the identical blocks).
        coord = WorldConstants.CanonicalChunk(coord, Circumference);
        if (_loaded.TryGetValue(coord, out var cached))
        {
            return cached;
        }

        // Fully re-configure the SHARED generator for THIS world before generating (#424 S13): size,
        // airless-moon cratering and pad flattening together — a partial set here previously left the
        // cratered flag of whatever world was configured last (wrong terrain once several worlds are hot).
        _generator.SetWorldMode(Circumference, Cratered, LandingPadFlats);
        var chunk = _generator.Generate(Planet, coord);
        foreach (var edit in _repo.LoadChunkEdits(LocationId, coord))
        {
            var local = WorldConstants.WorldToLocal(edit.WorldPosition);
            chunk.Set(local.X, local.Y, local.Z, new BlockId(edit.Block));
            if (edit.Tint != 0 || edit.Glow != 0)
            {
                chunk.SetModifier(local.X, local.Y, local.Z, edit.Tint, edit.Glow); // dyed block / coloured light
            }

            if (edit.Shape != 0)
            {
                chunk.SetShape(local.X, local.Y, local.Z, edit.Shape); // non-cube building form (sphere/ramp/…)
            }
        }

        _loaded[coord] = chunk;
        return chunk;
    }

    public BlockId GetBlock(Vector3i world)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference); // longitude wraps
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        return chunk.Get(local.X, local.Y, local.Z);
    }

    /// <summary>Like <see cref="GetBlock"/> but never loads or generates: cells in unloaded chunks read as
    /// air. For opportunistic periodic scans (e.g. the heal-tank regen field) that must not drag chunks
    /// into the cache — a placed device always sits in a chunk its nearby player keeps resident anyway.</summary>
    public BlockId GetBlockIfLoaded(Vector3i world)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference); // longitude wraps
        if (!_loaded.TryGetValue(WorldConstants.WorldToChunk(world), out var chunk))
        {
            return BlockId.Air;
        }

        var local = WorldConstants.WorldToLocal(world);
        return chunk.Get(local.X, local.Y, local.Z);
    }

    /// <summary>Sets a block (with an optional per-voxel colour modifier + shape descriptor), persists the
    /// edit, and returns the previous block id. <paramref name="tint"/>/<paramref name="glow"/> are 0xRRGGBB
    /// (0 = none); <paramref name="shape"/> is the packed non-cube shape descriptor (0 = plain cube).
    /// <paramref name="owner"/> is the player who caused the change, recorded as the cell's last editor
    /// (issue #490); leave it empty for server-internal writes — worldgen stamps, fluid flow, fire spread,
    /// flora regrowth — which must not claim authorship of a cell a player built.</summary>
    public BlockId SetBlock(Vector3i world, BlockId block, int tint = 0, int glow = 0, int shape = 0, string owner = "")
    {
        world = WorldConstants.CanonicalBlock(world, Circumference); // longitude wraps: store/cache the canonical column
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        var previous = chunk.Get(local.X, local.Y, local.Z);
        chunk.Set(local.X, local.Y, local.Z, block); // clears any old modifier/shape when set to air
        chunk.SetModifier(local.X, local.Y, local.Z, tint, glow);
        chunk.SetShape(local.X, local.Y, local.Z, shape);
        _repo.SetBlock(LocationId, world, block.Value, tint, glow, shape, owner);
        return previous;
    }

    /// <summary>The colour modifier (Tint/Glow as 0xRRGGBB, 0 = none) stamped on a placed cell.</summary>
    public (int Tint, int Glow) GetModifier(Vector3i world)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference);
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        return chunk.GetModifier(local.X, local.Y, local.Z);
    }

    /// <summary>The packed shape descriptor (non-cube building form + orientation; 0 = plain cube) on a cell.</summary>
    public int GetShape(Vector3i world)
    {
        world = WorldConstants.CanonicalBlock(world, Circumference);
        var chunk = GetOrLoadChunk(WorldConstants.WorldToChunk(world));
        var local = WorldConstants.WorldToLocal(world);
        return chunk.GetShape(local.X, local.Y, local.Z);
    }

    public BlockDefinition? Definition(BlockId id) => _content.BlockById(id);

    /// <summary>Drops cached chunks intersecting an axis-aligned box (inclusive) so they regenerate on next
    /// access — used after deleting persisted block edits in that volume (legacy ship-stamp cleanup).</summary>
    public void ForgetChunksIn(Vector3i min, Vector3i max)
    {
        var minC = WorldConstants.WorldToChunk(min);
        var maxC = WorldConstants.WorldToChunk(max);
        var toRemove = new List<ChunkCoord>();
        foreach (var coord in _loaded.Keys)
        {
            // Compare on the canonical X of the box corners (the cache keys are canonical already).
            if (coord.Y < minC.Y || coord.Y > maxC.Y || coord.Z < minC.Z || coord.Z > maxC.Z)
            {
                continue;
            }

            for (int cx = minC.X; cx <= maxC.X; cx++)
            {
                if (WorldConstants.CanonicalChunkX(cx, Circumference) == coord.X)
                {
                    toRemove.Add(coord);
                    break;
                }
            }
        }

        foreach (var coord in toRemove)
        {
            _loaded.Remove(coord);
        }
    }

    /// <summary>Unloads cached chunks that are not within <paramref name="keepRadius"/> of any anchor and returns
    /// the coords that were dropped — the caller also clears them from each player's sent-set so they re-stream
    /// (fresh) if the player returns.</summary>
    public IReadOnlyList<ChunkCoord> UnloadFarChunks(IReadOnlyCollection<ChunkCoord> anchors, int keepRadius)
    {
        if (anchors.Count == 0)
        {
            return System.Array.Empty<ChunkCoord>();
        }

        int keepSq = keepRadius * keepRadius;
        var toRemove = new List<ChunkCoord>();
        foreach (var coord in _loaded.Keys)
        {
            bool near = false;
            foreach (var anchor in anchors)
            {
                if (coord.DistanceSquared(anchor) <= keepSq)
                {
                    near = true;
                    break;
                }
            }

            if (!near)
            {
                toRemove.Add(coord);
            }
        }

        foreach (var coord in toRemove)
        {
            _loaded.Remove(coord);
        }

        return toRemove;
    }
}
