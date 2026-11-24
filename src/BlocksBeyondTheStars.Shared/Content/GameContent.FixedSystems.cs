// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Shared.Content;

/// <summary>
/// #3969: the FIXED star systems (<c>data/systems/&lt;key&gt;.json</c>, Sol) — hand-made systems with fixed bodies that join a
/// galaxy by their appearance rule. The server appends them (<c>GameServerFixedSystems.cs</c>).
/// </summary>
public sealed partial class GameContent
{
    private List<FixedSystemDefinition> _fixedSystems = new();

    /// <summary>Every loaded fixed system, in file-name order (the order they are appended in).</summary>
    public IReadOnlyList<FixedSystemDefinition> FixedSystems => _fixedSystems;

    /// <summary>The fixed system with this id, or null.</summary>
    public FixedSystemDefinition? GetFixedSystem(string? id)
        => string.IsNullOrEmpty(id) ? null : _fixedSystems.Find(s => string.Equals(s.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Keeps every fixed system that passes <see cref="FixedSystemRules.Problems"/> against the loaded planet types; a system
    /// with a problem, or with an id another one already took, is dropped with a warning — never a failed load. Call after the
    /// planets are set.
    /// </summary>
    public void SetFixedSystems(IReadOnlyList<FixedSystemDefinition>? systems, Action<string>? warn = null)
    {
        var kept = new List<FixedSystemDefinition>();
        foreach (var def in systems ?? Array.Empty<FixedSystemDefinition>())
        {
            if (def is null)
            {
                continue;
            }

            FixedSystemRules.Normalize(def);
            var problems = FixedSystemRules.Problems(def, _planets.ContainsKey);
            if (kept.Exists(k => string.Equals(k.Id, def.Id, StringComparison.Ordinal)))
            {
                problems.Add("a fixed system with that id is already loaded");
            }

            if (problems.Count > 0)
            {
                warn?.Invoke($"Skipping fixed system '{def.Key}': {string.Join("; ", problems)}.");
                continue;
            }

            kept.Add(def);
        }

        _fixedSystems = kept;
    }
}
