// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>The hostiles of <c>data/enemies.json</c> (#2971): what the server builds them from and what the Codex lists.</summary>
public sealed partial class GameContent
{
    private List<EnemyDefinition> _enemies = new(EnemyCatalog.Defaults());
    private Dictionary<string, EnemyDefinition> _enemyByKey = Index(EnemyCatalog.Defaults());

    /// <summary>True once <c>data/enemies.json</c> was installed — only then does <see cref="Validate"/> check its loot (the
    /// built-in defaults name items a minimal test content may not define).</summary>
    private bool _enemiesFromData;

    /// <summary>Every enemy in the Codex's order (the file's order).</summary>
    public IReadOnlyList<EnemyDefinition> Enemies => _enemies;

    /// <summary>The enemy with that key — from the file, else the built-in default (<see cref="EnemyCatalog"/>), else an
    /// empty definition whose tiers fall back to <see cref="EnemyTier"/>'s defaults (never null).</summary>
    public EnemyDefinition Enemy(string key)
    {
        if (_enemyByKey.TryGetValue(key, out var def))
        {
            return def;
        }

        foreach (var fallback in EnemyCatalog.Defaults())
        {
            if (fallback.Key == key)
            {
                return fallback;
            }
        }

        return new EnemyDefinition { Key = key };
    }

    /// <summary>Installs the enemies (called by the content loader); an empty list keeps the built-in defaults.</summary>
    public void SetEnemies(IReadOnlyList<EnemyDefinition>? enemies)
    {
        if (enemies is null || enemies.Count == 0)
        {
            return;
        }

        _enemies = new List<EnemyDefinition>(enemies);
        _enemyByKey = Index(enemies);
        _enemiesFromData = true;
    }

    private static Dictionary<string, EnemyDefinition> Index(IReadOnlyList<EnemyDefinition> enemies)
    {
        var map = new Dictionary<string, EnemyDefinition>(System.StringComparer.Ordinal);
        foreach (var e in enemies)
        {
            map[e.Key] = e;
        }

        return map;
    }

    /// <summary>The enemy rows of <see cref="Validate"/>: unique keys, a normal tier, positive hull, known loot items.</summary>
    private void ValidateEnemies(List<string> problems)
    {
        if (!_enemiesFromData)
        {
            return;
        }

        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var e in _enemies)
        {
            if (string.IsNullOrWhiteSpace(e.Key) || !seen.Add(e.Key))
            {
                problems.Add($"Enemy '{e.Key}' has an empty or duplicate key.");
            }

            if (!e.Tiers.ContainsKey(EnemyTiers.Normal))
            {
                problems.Add($"Enemy '{e.Key}' has no '{EnemyTiers.Normal}' tier.");
            }

            if (e.Where is not (EnemyWhere.Planet or EnemyWhere.Space))
            {
                problems.Add($"Enemy '{e.Key}' has an unknown 'where' '{e.Where}' (planet or space).");
            }

            foreach (var (name, tier) in e.Tiers)
            {
                if (tier.Hull <= 0f || tier.DamagePerSecond < 0f || (tier.HullMax > 0f && tier.HullMax < tier.Hull))
                {
                    problems.Add($"Enemy '{e.Key}' tier '{name}' needs a hull above 0, no negative damage and a HullMax of 0 or at least the hull.");
                }

                foreach (var loot in tier.Loot)
                {
                    if (!_items.ContainsKey(loot.Item) || loot.Count <= 0)
                    {
                        problems.Add($"Enemy '{e.Key}' tier '{name}' drops unknown item '{loot.Item}' (or a count below 1).");
                    }
                }
            }
        }
    }
}
