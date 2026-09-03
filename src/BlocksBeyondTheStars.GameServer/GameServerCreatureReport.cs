// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// <c>/creatures</c> (#1489): the admin's window into creature footing. "Tiere im Boden" reports cannot be
/// checked from a snapshot — it carries positions, not what the ground under them is — so the report lists
/// every animal near the admin with its feet cell, the REAL ground of its column (the same wide probe its own
/// motion uses), the delta between them and the generator's noise surface. A buried animal shows as a negative
/// delta on one line; a floating one as a positive one; a spawn from the noise surface over a dug pit shows the
/// two heights disagreeing. Read-only, like <c>/basewalls</c> (#1452): the role is the gate.
/// </summary>
public sealed partial class GameServer
{
    private const float CreatureReportRange = 48f; // blocks around the admin the report covers
    private const int CreatureReportMaxLines = 24;  // a whole herd still fits the chat

    private void AdminCreatures(PlayerSession session)
    {
        foreach (string line in CreaturesReport(session))
        {
            Send(session, new ServerMessage { Text = line });
        }
    }

    private List<string> CreaturesReport(PlayerSession session)
    {
        var p = session.State;
        string L(string key) => Localize(session.Locale, key);
        var lines = new List<string>();

        var near = new List<(CombatEntity Creature, double DistSq)>();
        foreach (var c in _creatures)
        {
            double d2 = WrapDistSq(p.Position, c.Position);
            if (d2 <= CreatureReportRange * CreatureReportRange)
            {
                near.Add((c, d2));
            }
        }

        if (near.Count == 0)
        {
            lines.Add(L("srv.creatures.none").Replace("{range}", CreatureReportRange.ToString("0")));
            return lines;
        }

        near.Sort((a, b) => a.DistSq.CompareTo(b.DistSq));
        lines.Add(L("srv.creatures.head")
            .Replace("{count}", near.Count.ToString())
            .Replace("{range}", CreatureReportRange.ToString("0")));

        int shown = 0;
        foreach (var (c, d2) in near)
        {
            if (shown++ >= CreatureReportMaxLines)
            {
                lines.Add(L("srv.creatures.more").Replace("{count}", (near.Count - CreatureReportMaxLines).ToString()));
                break;
            }

            int x = (int)System.Math.Floor(c.Position.X);
            int z = (int)System.Math.Floor(c.Position.Z);
            int feet = (int)System.Math.Floor(c.Position.Y);
            string name = c.SpeciesId;
            string cls = "?";
            int ground;
            if (_speciesById.TryGetValue(c.SpeciesId, out var sp))
            {
                name = L(sp.NameKey);
                cls = CreatureMotion.ClassOf(sp).ToString().ToLowerInvariant();
                ground = GroundFeetYAt(sp, x, z, feet);
            }
            else
            {
                ground = GroundFeetYAt(x, z, feet);
            }

            int noise = _generator.SurfaceHeight(_world.Planet, x, z) + 1;
            int delta = feet - ground;
            string verdict = L(delta < 0 ? "srv.creatures.buried" : delta > 1 ? "srv.creatures.floating" : "srv.creatures.ok");
            lines.Add(L("srv.creatures.row")
                .Replace("{name}", name)
                .Replace("{class}", cls)
                .Replace("{dist}", System.Math.Sqrt(d2).ToString("0"))
                .Replace("{x}", x.ToString()).Replace("{z}", z.ToString())
                .Replace("{feet}", feet.ToString())
                .Replace("{ground}", ground.ToString())
                .Replace("{delta}", (delta >= 0 ? "+" : string.Empty) + delta)
                .Replace("{noise}", noise.ToString())
                .Replace("{verdict}", verdict));
        }

        return lines;
    }

    /// <summary>Test seam: the <c>/creatures</c> report lines for a session (localized to its locale).</summary>
    public IReadOnlyList<string> CreaturesReportForTest(PlayerSession session) => CreaturesReport(session);
}
