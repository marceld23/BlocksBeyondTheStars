// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// THE SPS GUIDE TOWERS (#3893, terrain generation 47, docs/developer/SPS_TOWERS.md) — the ruins of the towers the Scout and
/// Pioneer Service raised on every prepared settlement world ("the sky-lights"). This file is what the rest of the server
/// reads: the towers' records (<see cref="WorldMetadata.SpsTowers"/>) and the questions asked of them. The stamp is in
/// GameServerSpsTowerStamp.cs, the map, the found event and the scanner in GameServerSpsTowersWorld.cs, the admin's commands
/// in GameServerSpsTowersAdmin.cs. What a tower DOES — its console, its lights turning blue, the sleeper in the guard post's
/// tank — belongs to the tower's life (GameServerSpsTowerLife*.cs), which builds on exactly these accessors.
/// </summary>
public sealed partial class GameServer
{
    internal sealed partial class LandmarkInstance
    {
        /// <summary>Whether this is the ruin of an SPS guide tower (#3893) rather than a landmark: it lives in its own list
        /// and has its own map type, record and scan readout.</summary>
        public bool SpsTower { get; init; }

        /// <summary>A guide tower's placement record index on its body (0 = the rolled one).</summary>
        public int SpsTowerIndex { get; init; }
    }

    private List<LandmarkInstance> _spsTowers => _worlds.Active.SpsTowers;

    /// <summary>Test seam: the guide towers of the active world.</summary>
    internal IReadOnlyList<LandmarkInstance> SpsTowersForTest => _spsTowers;

    /// <summary>Whether this save can still gain a guide tower: the world is of the sky-lights' terrain generation
    /// (<see cref="WorldDescription.SkyLightsGeneration"/>, or the kind's raised <c>minGeneration</c>) and the towers are
    /// switched on — the host's <c>PlaceSpsTowers</c>, the world option, the kind in <c>data/structure_types.json</c>.</summary>
    internal bool SpsTowersPossible
        => _config.PlaceSpsTowers && _meta.Description.SpsTowers != Frequency.Off
           && _content.StructureTypes.Get(StructureTypeRules.SpsTower) is { Enabled: true } rules
           && _meta.Description.TerrainGeneration >= System.Math.Max(WorldDescription.SkyLightsGeneration, rules.MinGeneration);

    /// <summary>Whether a guide tower stands anywhere in this save (on any body, visited or not).</summary>
    internal bool AnySpsTowerInSave() => _meta.SpsTowers.Count > 0;

    /// <summary>The guide towers of the active world, in record order.</summary>
    internal IReadOnlyList<SpsTowerRecord> SpsTowersOnWorld() => SpsTowersOnWorld(_world.LocationId);

    /// <summary>The guide towers of body <paramref name="locationId"/>, in record order.</summary>
    internal IReadOnlyList<SpsTowerRecord> SpsTowersOnWorld(string locationId)
        => _meta.SpsTowers.Where(t => t.LocationId == locationId).OrderBy(t => t.Index).ToList();

    /// <summary>The guide tower of the active world nearest to <paramref name="from"/> (the flat gap to its footprint,
    /// wrap-aware; 0 inside it), or null when the world holds none.</summary>
    internal SpsTowerRecord? NearestSpsTower(Vector3f from) => NearestSpsTower(from, _world.LocationId, out _);

    /// <summary>The guide tower of body <paramref name="locationId"/> nearest to <paramref name="from"/> and its flat
    /// <paramref name="gap"/> to the footprint, or null.</summary>
    internal SpsTowerRecord? NearestSpsTower(Vector3f from, string locationId, out float gap)
    {
        SpsTowerRecord? nearest = null;
        gap = float.MaxValue;
        if (_meta.SpsTowers.Count == 0)
        {
            return null;
        }

        int circumference = _worlds.Find(locationId)?.World.Circumference ?? _world.Circumference;
        foreach (var tower in _meta.SpsTowers)
        {
            if (tower.LocationId != locationId)
            {
                continue;
            }

            float d = SpsTowerGap(tower, from, circumference);
            if (d < gap)
            {
                (nearest, gap) = (tower, d);
            }
        }

        return nearest;
    }

    /// <summary>The guide tower of the active world whose relay console stands in <paramref name="cell"/>, or null.</summary>
    internal SpsTowerRecord? FindSpsTowerByConsole(Vector3i cell)
    {
        var at = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        return _meta.SpsTowers.FirstOrDefault(t => t.LocationId == _world.LocationId && t.Console is { } c && c.Is(at));
    }

    /// <summary>The guide tower of the active world whose capsule tank holds <paramref name="cell"/> — either of the tank's
    /// two cells (<see cref="SpsTowerRecord.Tank"/> is the lower one) — or null.</summary>
    internal SpsTowerRecord? FindSpsTowerByTank(Vector3i cell)
    {
        var at = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        return _meta.SpsTowers.FirstOrDefault(t => t.LocationId == _world.LocationId && t.Tank is { } tank
                                                   && tank.X == at.X && tank.Z == at.Z && (at.Y == tank.Y || at.Y == tank.Y + 1));
    }

    /// <summary>The record of tower <paramref name="index"/> of body <paramref name="locationId"/>, or null.</summary>
    internal SpsTowerRecord? FindSpsTower(string locationId, int index)
        => _meta.SpsTowers.FirstOrDefault(t => t.LocationId == locationId && t.Index == index);

    /// <summary>Persists the tower records after a change of their state (<see cref="SpsTowerRecord.Lit"/>, the tank, the
    /// guard) — the world metadata they live in.</summary>
    internal void SaveSpsTowers() => _repo.SaveMetadata(_meta);

    /// <summary>Test seam: the landable bodies of the galaxy (id, planet type), in generation order.</summary>
    internal IReadOnlyList<(string Id, string PlanetType)> LandableBodiesForTest()
        => _galaxy is null
            ? new List<(string, string)>()
            : _galaxy.Systems.SelectMany(s => s.Bodies).Where(b => !string.IsNullOrEmpty(b.PlanetType))
                .Select(b => (b.Id, b.PlanetType!)).ToList();

    /// <summary>Test seam: loads body <paramref name="locationId"/> as a first visit would (its structures boot stage runs)
    /// and makes it the active world. False when the galaxy holds no such body.</summary>
    internal bool VisitBodyForTest(string locationId)
    {
        var body = _galaxy?.Systems.SelectMany(s => s.Bodies).FirstOrDefault(b => b.Id == locationId);
        if (body?.PlanetType is not { Length: > 0 } type)
        {
            return false;
        }

        LoadWorld(type, body.Id);
        return true;
    }

    /// <summary>Test seam: listens for <see cref="Shared.Story.GameEventKeys.SpsTowerFound"/> like a story step would.</summary>
    internal void ListenForSpsTowerFoundForTest(IGameEventListener listener)
        => AddGameEventListener(Shared.Story.GameEventKeys.SpsTowerFound, listener);

    /// <summary>The middle of a tower's footprint at its seat (a point to walk or look to).</summary>
    internal static Vector3f SpsTowerCentre(SpsTowerRecord tower)
        => new(tower.X + (tower.Width / 2f), tower.Y + 1, tower.Z + (tower.Length / 2f));

    /// <summary>The flat gap between a position and a tower's footprint (0 inside it), wrap-aware along X.</summary>
    private static float SpsTowerGap(SpsTowerRecord tower, Vector3f pos, int circumference)
    {
        float relX = WorldConstants.WrapDeltaX((int)System.Math.Floor(pos.X) - tower.X, circumference);
        float dx = relX < 0 ? -relX : relX >= tower.Width ? relX - tower.Width + 1 : 0;
        float dz = pos.Z < tower.Z ? tower.Z - pos.Z : pos.Z >= tower.Z + tower.Length ? pos.Z - (tower.Z + tower.Length) + 1 : 0;
        return (float)System.Math.Sqrt((dx * dx) + (dz * dz));
    }
}
