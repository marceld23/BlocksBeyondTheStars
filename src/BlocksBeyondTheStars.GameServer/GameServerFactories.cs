// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Procedurally stamped <b>factories</b> — rare industrial halls with animated machines and a production
/// terminal. Each factory offers only a seeded <b>roster</b> (a subset of the factory recipes), so one
/// factory might make a single thing while another makes several — never everything. Factory recipes turn
/// cheaper, less-rare raw into the same output as the base recipe, but more of it (see <c>recipes.json</c>).
///
/// Factories are <b>protected</b> (read-only) until claimed with an access code (see the claiming feature).
/// Like settlements they re-derive deterministically from the seed each session, so the per-world list is
/// rebuilt on load; re-stamping their (protected) blocks is idempotent.
/// </summary>
public sealed partial class GameServer
{
    private const int FactoryHardCap = 2;
    private const float FactoryTerminalReach = 4f;

    private List<FactoryInstance> _factories => _worlds.Active.Factories;

    /// <summary>Number of factories stamped on this world (test seam).</summary>
    public int FactoryCount => _factories.Count;

    /// <summary>Per-factory id + bounds + terminal + roster + claim state (test seam).</summary>
    public IReadOnlyList<(int Id, Vector3f Terminal, Vector3i Min, Vector3i Max, IReadOnlyList<string> Roster, int MachineCount, bool Claimable, string OwnerId)> FactoriesForTest
        => _factories.Select(f => (f.Id, f.TerminalPos, f.Min, f.Max, (IReadOnlyList<string>)f.Roster, f.Machines.Count, f.Claimable, f.OwnerId)).ToList();

    /// <summary>Whether a factory cell is protected against the given actor (test seam for claim/owner/ally rights).</summary>
    public bool FactoryProtectedForTest(Vector3i pos, string actorId, bool isAdmin = false) => IsFactoryProtected(pos, actorId, isAdmin);

    private void StampFactories()
    {
        _factories.Clear();

        var planet = _world.Planet;
        if (planet.IsAirless)
        {
            return; // factories belong to once-industrialised worlds, not bare airless rocks
        }

        double factor = _meta.Description.Settlements.StructureFactor();
        if (factor <= 0)
        {
            return;
        }

        var factoryRecipes = _content.Recipes.Values
            .Where(x => x.Station == CraftingStation.Factory)
            .Select(x => x.Key)
            .OrderBy(k => k, System.StringComparer.Ordinal)
            .ToList();
        if (factoryRecipes.Count == 0)
        {
            return;
        }

        long fSeed = _meta.Seed ^ WorldGenerator.StableHash("factory:" + planet.Key);
        var rng = new System.Random(unchecked((int)(fSeed ^ (fSeed >> 32))));

        // Rare: most worlds get none.
        double r = rng.NextDouble();
        int count = r < 0.70 ? 0 : r < 0.92 ? 1 : 2;
        count = System.Math.Min(FactoryHardCap, (int)System.Math.Round(count * System.Math.Clamp(factor, 0.0, 2.0)));
        if (count <= 0)
        {
            return;
        }

        var surface = planet.Biomes.Count > 0 ? planet.Biomes[0].SurfaceBlock : planet.SurfaceBlock;

        // Reserve pads, the wreck zone and every settlement so factories land clear of them.
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        int pad0X = _landingPads.Count > 0 ? _landingPads[0].CenterX : 0;
        int pad0Z = _landingPads.Count > 0 ? _landingPads[0].CenterZ : 0;
        reserved.Add((pad0X - 56, pad0Z + 56, 14, 14)); // wreck zone
        foreach (var s in _settlements)
        {
            reserved.Add(((s.Min.X + s.Max.X) / 2, (s.Min.Z + s.Max.Z) / 2,
                (s.Max.X - s.Min.X) / 2 + 1, (s.Max.Z - s.Min.Z) / 2 + 1));
        }

        var placed = new List<(PlacedSettlement P, List<string> Roster)>();
        var usedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var s in _settlements)
        {
            usedNames.Add(s.Name);
        }

        for (int i = 0; i < count; i++)
        {
            long instSeed = fSeed ^ unchecked((long)(i + 1) * (long)0x9E3779B97F4A7C15);
            var ir = new System.Random(unchecked((int)(instSeed ^ (instSeed >> 32))));

            // The roster: a seeded subset of the factory recipes — between one and a handful, never all.
            int rosterSize = 1 + ir.Next(0, System.Math.Min(4, factoryRecipes.Count));
            var roster = factoryRecipes.OrderBy(_ => ir.Next()).Take(rosterSize).ToList();

            var structure = FactoryGenerator.Generate(instSeed, roster.Count, _content);
            if (!TryPlaceSettlement(structure, ir, reserved, wantIsland: false, out var origin, out int groundY, out _))
            {
                continue;
            }

            string name = UniqueName(FactoryName(ir), usedNames);
            placed.Add((new PlacedSettlement
            {
                Structure = structure,
                Origin = origin,
                GroundY = groundY,
                Tier = "factory",
                Ruined = false,
                OnIsland = false,
                Name = name,
                Rng = ir,
            }, roster));
            reserved.Add((origin.X + structure.Width / 2, origin.Z + structure.Length / 2,
                structure.Width / 2 + 1, structure.Length / 2 + 1));
        }

        if (placed.Count == 0)
        {
            return;
        }

        _repo.RunInTransaction(() =>
        {
            foreach (var (p, _) in placed)
            {
                StampSettlementBlocks(p, surface);
            }
        });

        foreach (var (p, roster) in placed)
        {
            var inst = new FactoryInstance
            {
                Id = _worlds.Active.NextFactoryId++,
                Min = p.Origin,
                Max = new Vector3i(p.Origin.X + p.Structure.Width - 1, p.GroundY + p.Structure.Height - 1, p.Origin.Z + p.Structure.Length - 1),
                Name = p.Name,
                Key = $"{_world.LocationId}|factory|{p.Origin.X}|{p.Origin.Y}|{p.Origin.Z}",
                Claimable = true, // factories are rare; the access code is the scarce gate
            };
            inst.Roster.AddRange(roster);

            // Re-apply any persisted claim over this factory (the structure re-derives each session; the claim persists).
            var claim = _meta.Claims.Find(c => c.Key == inst.Key);
            if (claim is not null)
            {
                inst.OwnerId = claim.OwnerId;
                inst.Name = string.IsNullOrEmpty(claim.Name) ? inst.Name : claim.Name;
            }

            foreach (var m in p.Structure.Markers)
            {
                var pos = new Vector3f(p.Origin.X + m.LocalPos.X + 0.5f, p.GroundY + m.LocalPos.Y + 0.5f, p.Origin.Z + m.LocalPos.Z + 0.5f);
                if (m.Type == "factory_terminal")
                {
                    inst.TerminalPos = pos;
                }
                else if (m.Type.StartsWith("machine:", System.StringComparison.Ordinal))
                {
                    inst.Machines.Add((m.Type.Substring("machine:".Length), pos));
                }
                else if (m.Type == "loot")
                {
                    SpawnStructureLoot("factory", "loot", pos, p.Rng);
                }
            }

            _factories.Add(inst);
        }

        _log.Info($"Stamped {_factories.Count} factory(ies) on '{_world.LocationId}'.");
    }

    /// <summary>True when a cell lies inside any stamped factory's footprint — used by the mine/place guards to
    /// keep factories read-only until they are claimed.</summary>
    public bool IsFactoryBlock(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var f in _factories)
        {
            if (pos.Y < f.Min.Y || pos.Y > f.Max.Y)
            {
                continue;
            }

            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(pos.X - (f.Min.X + f.Max.X) / 2, circ));
            int dz = System.Math.Abs(pos.Z - (f.Min.Z + f.Max.Z) / 2);
            if (dx <= (f.Max.X - f.Min.X) / 2 && dz <= (f.Max.Z - f.Min.Z) / 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a cell is a protected (non-editable) factory block for this actor. Admins always edit.
    /// A claimed factory defers to its owner/ally access (see the claiming feature); an unclaimed factory is
    /// read-only for everyone.</summary>
    private bool IsFactoryProtected(Vector3i pos, string actorId, bool actorIsAdmin)
    {
        if (actorIsAdmin || !IsFactoryBlock(pos))
        {
            return false;
        }

        // A claim over this factory grants its owner + allies full edit rights; otherwise it stays read-only.
        // (Claiming is layered on in the access-code feature; until then every factory block is protected.)
        return !FactoryClaimGrantsEdit(pos, actorId);
    }

    /// <summary>Whether a claim over the factory containing this cell grants the actor edit rights (owner or an
    /// ally). Unclaimed factories grant no one — they stay read-only.</summary>
    private bool FactoryClaimGrantsEdit(Vector3i pos, string actorId)
    {
        var f = FactoryAt(pos);
        if (f is null || string.IsNullOrEmpty(f.OwnerId))
        {
            return false;
        }

        return f.OwnerId == actorId || AreAllied(f.OwnerId, actorId);
    }

    /// <summary>The factory whose footprint contains a cell (or null).</summary>
    private FactoryInstance? FactoryAt(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var f in _factories)
        {
            if (pos.Y < f.Min.Y || pos.Y > f.Max.Y)
            {
                continue;
            }

            int dx = System.Math.Abs(WorldConstants.WrapDeltaX(pos.X - (f.Min.X + f.Max.X) / 2, circ));
            int dz = System.Math.Abs(pos.Z - (f.Min.Z + f.Max.Z) / 2);
            if (dx <= (f.Max.X - f.Min.X) / 2 && dz <= (f.Max.Z - f.Min.Z) / 2)
            {
                return f;
            }
        }

        return null;
    }

    /// <summary>Claims the factory the player is standing at by spending one access (SPS) code: it becomes the
    /// player's base — they and their allies can rebuild it freely. One code claims one structure.</summary>
    public void ClaimFactory(string playerId, int factoryId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return;
        }

        var f = _factories.FirstOrDefault(x => x.Id == factoryId);
        if (f is null || !f.Claimable)
        {
            Reject(session, "claim", "This structure cannot be claimed.");
            return;
        }

        if (!string.IsNullOrEmpty(f.OwnerId))
        {
            Reject(session, "claim", "This structure is already claimed.");
            return;
        }

        if (WrapDistSq(session.State.Position, f.TerminalPos) > (FactoryTerminalReach + 1.5f) * (FactoryTerminalReach + 1.5f))
        {
            Reject(session, "claim", "Stand at the factory terminal to claim it.");
            return;
        }

        if (session.State.Inventory.CountOf("access_code") < 1)
        {
            Reject(session, "claim", "You need an access code to claim this.");
            return;
        }

        session.State.Inventory.Remove("access_code", 1);
        f.OwnerId = playerId;
        _meta.Claims.RemoveAll(c => c.Key == f.Key);
        _meta.Claims.Add(new StructureClaim { Key = f.Key, OwnerId = playerId, Name = f.Name });
        _repo.SaveMetadata(_meta);

        SendInventory(session);
        BroadcastFactories();
        Send(session, new ServerMessage { Text = $"Claimed {f.Name}. It is now your base." });
    }

    private void HandleClaimStructure(PlayerSession session, ClaimStructureIntent intent)
        => ClaimFactory(session.State.PlayerId, intent.FactoryId);

    private void BroadcastFactories()
        => BroadcastToWorld(new FactoryList { Factories = _factories.Select(ToNetFactory).ToArray() });

    /// <summary>The factory whose production terminal the player is standing at (within reach), or null.</summary>
    private FactoryInstance? FactoryTerminalNear(PlayerState player)
    {
        FactoryInstance? best = null;
        double bestSq = (double)FactoryTerminalReach * FactoryTerminalReach;
        foreach (var f in _factories)
        {
            double d = WrapDistSq(player.Position, f.TerminalPos);
            if (d <= bestSq)
            {
                bestSq = d;
                best = f;
            }
        }

        return best;
    }

    private static NetMachine ToNetMachine((string Archetype, Vector3f Pos) m)
        => new() { Archetype = m.Archetype, X = m.Pos.X, Y = m.Pos.Y, Z = m.Pos.Z };

    private static NetFactory ToNetFactory(FactoryInstance f) => new()
    {
        Id = f.Id,
        Name = f.Name,
        TerminalX = f.TerminalPos.X,
        TerminalY = f.TerminalPos.Y,
        TerminalZ = f.TerminalPos.Z,
        Roster = f.Roster.ToArray(),
        Active = true, // ambient-running (machines animate continuously); per-craft speed-up is a later polish
        Claimable = f.Claimable,
        OwnerId = f.OwnerId,
        Machines = f.Machines.Select(ToNetMachine).ToArray(),
    };

    /// <summary>Sends this world's factories to a session (on world entry) so the client renders the machines
    /// and can open the roster-filtered production UI at a terminal.</summary>
    private void SendFactories(PlayerSession session)
        => Send(session, new FactoryList { Factories = _factories.Select(ToNetFactory).ToArray() });

    /// <summary>A short, flavourful factory name (uniqued by the caller).</summary>
    private static string FactoryName(System.Random rng)
    {
        string[] kinds = { "Foundry", "Fabricator", "Assembly Plant", "Forgeworks", "Refinery Works", "Manufactory" };
        return kinds[rng.Next(kinds.Length)];
    }
}
