// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin's guide tower command (#3893): <c>/spstower</c> (verb <c>spstower</c>, behind the world's cheat rule) — a play test
/// tool, like <c>/giantwreck</c>. <c>/spstower list</c> names every tower ruin of the content; <c>/spstower &lt;key&gt;</c> stands
/// that ruin six blocks in front of the admin, whatever the body's roll and conditions said, with a fresh seed each time —
/// <c>post</c> or <c>nopost</c> after the key forces the guard post, otherwise the seed decides. It is pinned like the rolled
/// one under the next free record index, stored as a structure overlay on any world, protected, and gets its tower record;
/// it never spends the save's guarantee. Not on a void or gas world or in space. <c>/tp tower [n]</c> goes to one.
/// </summary>
public sealed partial class GameServer
{
    private void AdminSpsTower(PlayerSession session, string? argument)
    {
        Serve(session);
        string[] words = (argument ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || words[0].Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListSpsTowers(session);
            return;
        }

        var template = _content.GetSpsTower(words[0]);
        if (template is null)
        {
            Reject(session, "admin", "@srv.spstower.unknown");
            return;
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || planet.IsGasWorld || InSpace(session.State.PlayerId) || session.CurrentLocationId != _world.LocationId)
        {
            Reject(session, "admin", "@srv.spstower.not_here");
            return;
        }

        bool? post = words.Length < 2 ? null
            : words[1].Equals("post", StringComparison.OrdinalIgnoreCase) ? true
            : words[1].Equals("nopost", StringComparison.OrdinalIgnoreCase) ? false
            : null;
        var tower = ForceSpsTower(template, session.State.Position.ToBlock(), post);
        CheatLog(session.State, $"stamped guide tower '{template.Key}' on {_world.LocationId} at ({tower.Origin.X}, {tower.GroundY}, {tower.Origin.Z})");
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.spstower.stamped").Replace("{name}", LandmarkName(tower, session.Locale)),
        });
    }

    private void ListSpsTowers(PlayerSession session)
    {
        var towers = _content.SpsTowers;
        if (towers.Count == 0)
        {
            Reject(session, "admin", "@srv.spstower.none");
            return;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.spstower.list_header").Replace("{count}", towers.Count.ToString(CultureInfo.InvariantCulture)),
        });
        foreach (var t in towers.Take(TeleportListMaxLines))
        {
            Send(session, new ServerMessage
            {
                Text = FormattableString.Invariant($"/spstower {t.Key} · {TemplateDisplayName(t, session.Locale)} · {t.Width}×{t.Height}×{t.Length}"),
            });
        }
    }

    /// <summary>Stands the ruin of <paramref name="template"/> in front of <paramref name="at"/> on the active world, whatever
    /// its roll and its conditions say, under the next free record index. <paramref name="guardPost"/> forces the guard post
    /// on or off (null = the seed decides). Returns the new instance.</summary>
    internal LandmarkInstance ForceSpsTower(StructureTemplate template, Vector3i at, bool? guardPost = null)
    {
        int index = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == SpsTowerRules.RecordKind)
            .Select(r => r.Index + 1).DefaultIfEmpty(0).Max();
        string lane = FormattableString.Invariant($"spstower:admin:{_world.LocationId}:{index}:{at.X}:{at.Z}");
        long seed = unchecked((long)StableHashing.Mixed(lane, _meta.Seed));
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = seed,
            Turns = StableHashing.Index(lane + ":turn", _meta.Seed, 4),
            Revision = SpsTowerGenerator.RuinRevision,
            GuardPost = guardPost ?? SpsTowerGenerator.RollsGuardPost(template, seed),
            Index = index,
            Fresh = true,
        };
        plan.Layout = BuildSpsTowerLayout(plan, !_world.Planet.IsAirless);
        var s = plan.Layout.Structure;
        plan.OriginX = at.X - (s.Width / 2);
        plan.OriginZ = at.Z + LandmarkAdminGap;
        plan.GroundY = _generator.SurfaceHeight(_world.Planet, plan.OriginX + (s.Width / 2), plan.OriginZ + (s.Length / 2));
        plan.Seat = "flat";
        CommitSpsTower(plan);
        BroadcastLandmarks(); // the silhouette, for everyone already here
        return _spsTowers[^1];
    }

    /// <summary>The <c>/tp tower [n]</c> targets: the guide towers of the current body, on the ground in front of their
    /// footprint, named after their ruin.</summary>
    private void AppendSpsTowerTargets(List<TeleportTarget> list, PlayerSession session)
    {
        for (int i = 0; i < _spsTowers.Count; i++)
        {
            var (cx, _, _, _) = _spsTowers[i].Rect;
            list.Add(new TeleportTarget("tower", i + 1, Named("tower", i + 1, LandmarkName(_spsTowers[i], session.Locale)),
                SurfaceSpot(WorldConstants.WrapX(cx, _world.Circumference), _spsTowers[i].Origin.Z - 3)));
        }
    }
}
