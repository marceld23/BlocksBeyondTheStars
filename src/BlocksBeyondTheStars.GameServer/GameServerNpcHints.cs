// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// NPC treasure hints: a greeted settlement NPC occasionally shares where the world's crashed wreck or a
/// hidden treasure chest lies. The hint both reveals the target as a map POI (world-globally, persisted in
/// <see cref="Shared.State.WorldMetadata.RevealedPois"/>) and replaces the greeting with a spoken line giving
/// rough direction + distance. The wreck is shared with anyone; chest hints are saved for players the NPC
/// already knows (relationship tier "known"+). Hint lines are always deterministic localized text — never
/// LLM-generated — because the greeting cache is shared per relationship tier and a cached line would read
/// another player's coordinates back to the wrong listener.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Chance per (non-cooled-down) greeting that the NPC shares a location it knows of.</summary>
    private const double HintChance = 0.35;

    /// <summary>Container-id prefix of standalone treasure chests (see <c>SpawnStructureLoot</c> key encoding).</summary>
    private const string ChestContainerIdPrefix = "loot_chest_";

    private readonly System.Random _hintRng = new(0x2B9D51);

    /// <summary>Rolls for and emits an NPC location hint: reveals the target POI on every player's map and
    /// returns the localized spoken line, or empty when nothing (new) is worth sharing. Game-thread only.</summary>
    private string TryEmitHint(PlayerSession session, string npcKey, bool forceRoll = false)
    {
        if (!forceRoll && _hintRng.NextDouble() > HintChance)
        {
            return string.Empty;
        }

        // (a) The wreck — the landmark find of a world, shared with any visitor while still unclaimed.
        string wreckKey = _world.LocationId + "|wreck";
        if (_wreckStamped && !_wreckClaimed && !_meta.RevealedPois.Contains(wreckKey))
        {
            RevealPoi(wreckKey);
            var (wx, wz) = WreckPoiCenter();
            return HintLine(session, "npc.hint.wreck", wx, wz);
        }

        // (b) A hidden chest — a secret kept for players this NPC already knows (tier "known"+).
        int rel = session.State.NpcMemory.TryGetValue(npcKey, out var r) ? r.Value : 0;
        if (RelationshipTier(rel) is not ("known" or "trusted"))
        {
            return string.Empty;
        }

        // (c) The galaxy's legends (#1129) — a FRIEND shares where a one-of-a-kind place stands. Ahead of
        // the chest so a trusted regular eventually hears the big stories, not only the local ones.
        if (RelationshipTier(rel) == "trusted")
        {
            string site = TryEmitUniqueSiteHint(session);
            if (!string.IsNullOrEmpty(site))
            {
                return site;
            }
        }

        StoredContainer? best = null;
        double bestSq = double.MaxValue;
        foreach (var cont in _containers)
        {
            if (!cont.Id.StartsWith(ChestContainerIdPrefix, System.StringComparison.Ordinal)
                || _meta.RevealedPois.Contains(ChestRevealKey(cont)))
            {
                continue;
            }

            double d = WrapDistSq(session.State.Position, cont.Position);
            if (d < bestSq)
            {
                bestSq = d;
                best = cont;
            }
        }

        if (best is null)
        {
            return string.Empty; // no wreck left to share and every chest is revealed (or looted away)
        }

        RevealPoi(ChestRevealKey(best));
        return HintLine(session, "npc.hint.treasure", best.Position.X + 0.5f, best.Position.Z + 0.5f);
    }

    /// <summary>Persists a reveal and pushes the updated POI list to everyone on the world.</summary>
    private void RevealPoi(string revealKey)
    {
        _meta.RevealedPois.Add(revealKey);
        _repo.SaveMetadata(_meta);
        BroadcastPlanetPois();
    }

    /// <summary>Stable reveal key of a treasure-chest container ("{locationId}|chest:{x}:{y}:{z}").</summary>
    private string ChestRevealKey(StoredContainer chest)
        => $"{_world.LocationId}|chest:{chest.Position.X}:{chest.Position.Y}:{chest.Position.Z}";

    /// <summary>Map-marker centre of the stamped wreck (hull midpoint, wrapped east–west).</summary>
    private (float X, float Z) WreckPoiCenter()
    {
        var o = _wreckOrigin;
        return _wreck is { } s
            ? (WorldConstants.WrapX(o.X + s.Width / 2, _world.Circumference), o.Z + s.Length / 2)
            : ((float)o.X, (float)o.Z);
    }

    /// <summary>Formats the spoken hint: rough distance (nearest 10 m) + 8-way compass direction from the
    /// player to the target, the short way round both wrap seams.</summary>
    private string HintLine(PlayerSession session, string textKey, float targetX, float targetZ)
    {
        double dx = WorldConstants.WrapDeltaX((double)(targetX - session.State.Position.X), _world.Circumference);
        double dz = WorldConstants.WrapDeltaZ((double)(targetZ - session.State.Position.Z), _world.Circumference);
        int dist = System.Math.Max(10, (int)(System.Math.Round(System.Math.Sqrt((dx * dx) + (dz * dz)) / 10.0) * 10));
        string direction = Localize(session.Locale, DirectionKey(dx, dz));
        return string.Format(Localize(session.Locale, textKey), dist, direction);
    }

    /// <summary>8-way compass direction locale key for a wrap-resolved delta (+Z = north, +X = east — the
    /// same convention as the HUD compass and the map's player arrow).</summary>
    private static string DirectionKey(double dx, double dz)
    {
        double ang = System.Math.Atan2(dx, dz) * 180.0 / System.Math.PI; // 0 = north, 90 = east
        return (int)System.Math.Floor(((ang + 360.0 + 22.5) % 360.0) / 45.0) switch
        {
            0 => "dir.n",
            1 => "dir.ne",
            2 => "dir.e",
            3 => "dir.se",
            4 => "dir.s",
            5 => "dir.sw",
            6 => "dir.w",
            _ => "dir.nw",
        };
    }

    /// <summary>Test seam: run the hint flow for the nearest in-reach NPC of <paramref name="role"/> exactly
    /// like a greeting would — <paramref name="forceRoll"/> skips the probability roll. Null when no NPC is in
    /// reach; empty when the NPC has nothing (new) to share; else the localized spoken line.</summary>
    public string? HintLineForTest(string playerId, string role, bool forceRoll = true)
    {
        if (FindSessionByPlayerId(playerId) is not { } session)
        {
            return null;
        }

        if (NearestNpc(session.State, role) is not { } npc
            || WrapDistSq(session.State.Position, npc.Pos) > NpcGreetRange * NpcGreetRange)
        {
            return null;
        }

        var (_, npcKey, _) = NpcContext(session, npc);
        return TryEmitHint(session, npcKey, forceRoll);
    }

    /// <summary>Test seam: the 8-way direction key for a wrap-resolved delta.</summary>
    public static string DirectionKeyForTest(double dx, double dz) => DirectionKey(dx, dz);

    /// <summary>Test seam: marks the wreck claimed/unclaimed (the real path needs a full hull repair).</summary>
    public void SetWreckClaimedForTest(bool claimed) => _wreckClaimed = claimed;

    /// <summary>Test seam: pins a player's relationship score with the nearest NPC of <paramref name="role"/>
    /// (the hint gate keys on the resulting tier).</summary>
    public void SetNpcRelationshipForTest(string playerId, string role, int value)
    {
        if (FindSessionByPlayerId(playerId) is not { } session || NearestNpc(session.State, role) is not { } npc)
        {
            return;
        }

        var (_, npcKey, _) = NpcContext(session, npc);
        if (!session.State.NpcMemory.TryGetValue(npcKey, out var rel))
        {
            rel = new Shared.State.NpcRelationship { Name = npc.Name, Role = npc.Role };
            session.State.NpcMemory[npcKey] = rel;
        }

        rel.Value = value;
    }
}
