// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Net fragments (implementation plan P2): text-only story finds scattered on a body's surface, modelled on
/// the data cubes (<see cref="StampDataCubes"/>) — deterministic from the world seed, server entities the
/// client renders + lightly collides, picked up by walking up and pressing E. Unlike dataqubes (knowledge
/// mini-games), a fragment opens a text reader and advances the shared story (<see cref="RecordStoryFragment"/>).
/// Each spot draws a fragment (weighted) from the active pack's still-needed pool, so an already-found
/// fragment never reappears. The same <c>RecordStoryFragment</c> hook is fed by structure-placed fragments
/// later; this file covers the surface, datacube-style source.
/// </summary>
public sealed partial class GameServer
{
    private const float NetFragmentReach = 3.2f; // how close to stand to pick up a fragment (press E)

    /// <summary>A net fragment living in the world. Server-only; the client sees a <see cref="NetStoryFragment"/>.</summary>
    internal sealed class ServerNetFragment
    {
        public int Id;
        public Vector3f Pos;
        public string Key = string.Empty;      // dedupe key (StoryState.FoundFragmentKeys)
        public string Category = string.Empty; // lore category (for the client icon/tint)
        public string TextKey = string.Empty;  // archive text revealed on pickup
    }

    private List<ServerNetFragment> _netFragments => _worlds.Active.NetFragments;
    private int _nextNetFragmentId { get => _worlds.Active.NextNetFragmentId; set => _worlds.Active.NextNetFragmentId = value; }

    /// <summary>Net fragments on the active world (id/key/category/pos) for tests + inspection.</summary>
    public IReadOnlyList<(int Id, string Key, string Category, Vector3f Pos)> NetFragmentSnapshots
        => _netFragments.Select(f => (f.Id, f.Key, f.Category, f.Pos)).ToList();

    /// <summary>Number of net fragments on the active world.</summary>
    public int NetFragmentCount => _netFragments.Count;

    /// <summary>Scatters this world's net fragments (idempotent per resident world). Deterministic from the
    /// world seed; skips Void worlds, an inactive story, and a pack with no fragments. Each spot draws a
    /// not-yet-found fragment from the active pack (weighted), so found ones never reappear; most bodies carry
    /// none, the rest one or two — a meaningful, rarer find than data cubes.</summary>
    private void StampNetFragments()
    {
        if (!StoryActive || _story is null || _story.Fragments.Count == 0)
        {
            return;
        }

        if (_netFragments.Count > 0)
        {
            return; // already stamped for this resident world
        }

        var planet = _world.Planet;
        if (planet.Void)
        {
            return; // stations have no surface to scatter on
        }

        long fSeed = _meta.Seed ^ WorldGenerator.StableHash("netfragment:" + _world.LocationId);
        var rng = new System.Random(unchecked((int)(fSeed ^ (fSeed >> 32))));

        double r = rng.NextDouble();
        int count = r < 0.5 ? 0 : r < 0.85 ? 1 : 2;

        for (int i = 0; i < count; i++)
        {
            var frag = PickNetFragment(rng);
            if (frag is null)
            {
                break; // nothing left to offer (all found / already placed here)
            }

            int ax = WorldConstants.WrapX((60 + rng.Next(360)) * (rng.Next(2) == 0 ? 1 : -1), _world.Circumference);
            int az = (60 + rng.Next(360)) * (rng.Next(2) == 0 ? 1 : -1);
            int surfaceY = _generator.SurfaceHeight(planet, ax, az);

            _netFragments.Add(new ServerNetFragment
            {
                Id = _nextNetFragmentId++,
                Pos = new Vector3f(ax + 0.5f, surfaceY + 1f, az + 0.5f),
                Key = frag.Key,
                Category = frag.Category,
                TextKey = frag.TextKey,
            });
        }

        if (_netFragments.Count > 0)
        {
            _log.Info($"Scattered {_netFragments.Count} net fragment(s) on '{_world.LocationId}'.");
        }
    }

    /// <summary>Weighted pick of a fragment not yet found and not already placed on this world (or null).</summary>
    private StoryFragment? PickNetFragment(System.Random rng)
    {
        var placed = new HashSet<string>(_netFragments.Select(f => f.Key));
        var candidates = _story!.Fragments
            .Where(f => !string.IsNullOrEmpty(f.Key)
                        && !_storyState.FoundFragmentKeys.Contains(f.Key)
                        && !placed.Contains(f.Key))
            .ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        int total = candidates.Sum(f => System.Math.Max(1, f.Weight));
        int roll = rng.Next(total);
        foreach (var f in candidates)
        {
            roll -= System.Math.Max(1, f.Weight);
            if (roll < 0)
            {
                return f;
            }
        }

        return candidates[candidates.Count - 1];
    }

    private void SendNetFragments(PlayerSession session)
        => Send(session, new NetFragmentList { Fragments = _netFragments.Select(ToNetFragment).ToArray() });

    private static NetStoryFragment ToNetFragment(ServerNetFragment f) => new()
    {
        Id = f.Id,
        X = f.Pos.X,
        Y = f.Pos.Y,
        Z = f.Pos.Z,
        Category = f.Category,
    };

    /// <summary>Re-sends the active world's fragment list to everyone on that body (after a pickup removes one).</summary>
    private void BroadcastNetFragments()
    {
        foreach (var session in _sessions.Values.Where(s => s.Joined && s.CurrentLocationId == _world.LocationId))
        {
            SendNetFragments(session);
        }
    }

    /// <summary>A player picks up a net fragment they're standing at (press E). Validates it exists and is in
    /// reach, reveals its archive text, advances the shared story, then removes it (gone for everyone).</summary>
    private void HandleNetFragmentFound(PlayerSession session, NetFragmentFoundIntent intent)
    {
        var frag = _netFragments.FirstOrDefault(f => f.Id == intent.FragmentId);
        if (frag is null)
        {
            return; // unknown fragment (stale client / wrong world)
        }

        if (WrapDistSq(session.State.Position, frag.Pos) > NetFragmentReach * NetFragmentReach)
        {
            return; // too far to reach
        }

        _netFragments.Remove(frag);
        Send(session, new NetFragmentRevealed { Category = frag.Category, TextKey = frag.TextKey });
        RecordStoryFragment(frag.Key); // dedupes + advances the arc + broadcasts the meter
        BroadcastNetFragments();
    }

    // ---------------- Test hook ----------------

    /// <summary>Test hook: pick up a fragment by id without the reach/network path (mirrors the gameplay
    /// event). Returns false if no such fragment is on the active world.</summary>
    public bool PickUpNetFragmentForTest(int fragmentId)
    {
        var frag = _netFragments.FirstOrDefault(f => f.Id == fragmentId);
        if (frag is null)
        {
            return false;
        }

        _netFragments.Remove(frag);
        RecordStoryFragment(frag.Key);
        return true;
    }
}
