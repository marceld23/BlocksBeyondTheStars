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
/// The admin's giant wreck command (#3610): <c>/giantwreck</c> (verb <c>giantwreck</c>, behind the world's cheat rule) — a
/// play test tool for the ship builders, like <c>/landmark</c>. <c>/giantwreck list</c> names every giant ship of the content
/// (the pin-only samples too); <c>/giantwreck &lt;key&gt;</c> lays that ship's wreck six blocks in front of the admin, whatever
/// the planet's roll said, with a fresh seed each time (so several wrecks of one ship can be compared side by side). It is
/// pinned like the rolled one under the next free record index and stored as a structure overlay on any world — never a block
/// edit per cell. Not on a void or gas world. <c>/tp giantwreck [n]</c> goes to one.
/// </summary>
public sealed partial class GameServer
{
    private void AdminGiantWreck(PlayerSession session, string? argument)
    {
        Serve(session);
        string arg = (argument ?? string.Empty).Trim();
        if (arg.Length == 0 || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListGiantShips(session);
            return;
        }

        var template = _content.GetGiantShip(arg);
        if (template is null)
        {
            Reject(session, "admin", "@srv.giantwreck.unknown");
            return;
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || planet.IsGasWorld || InSpace(session.State.PlayerId) || session.CurrentLocationId != _world.LocationId)
        {
            Reject(session, "admin", "@srv.giantwreck.not_here");
            return;
        }

        var wreck = ForceGiantWreck(template, session.State.Position.ToBlock());
        CheatLog(session.State, $"stamped giant wreck '{template.Key}' on {_world.LocationId} at ({wreck.Origin.X}, {wreck.GroundY}, {wreck.Origin.Z})");
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.giantwreck.stamped").Replace("{name}", LandmarkName(wreck, session.Locale)),
        });
    }

    private void ListGiantShips(PlayerSession session)
    {
        var ships = _content.GiantShips;
        if (ships.Count == 0)
        {
            Reject(session, "admin", "@srv.giantwreck.none");
            return;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.giantwreck.list_header").Replace("{count}", ships.Count.ToString(CultureInfo.InvariantCulture)),
        });
        foreach (var t in ships.Take(TeleportListMaxLines))
        {
            Send(session, new ServerMessage
            {
                Text = FormattableString.Invariant($"/giantwreck {t.Key} · {TemplateDisplayName(t, session.Locale)} · {t.Width}×{t.Height}×{t.Length}"),
            });
        }
    }

    /// <summary>Lays the wreck of <paramref name="template"/> in front of <paramref name="at"/> on the active world, whatever its
    /// roll and its conditions say, under the next free record index. Returns the new instance.</summary>
    internal LandmarkInstance ForceGiantWreck(StructureTemplate template, Vector3i at)
    {
        int index = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == GiantShipRules.RecordKind)
            .Select(r => r.Index + 1).DefaultIfEmpty(0).Max();
        string lane = FormattableString.Invariant($"giantwreck:admin:{_world.LocationId}:{index}:{at.X}:{at.Z}");
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = unchecked((long)StableHashing.Mixed(lane, _meta.Seed)),
            Turns = StableHashing.Index(lane + ":turn", _meta.Seed, 4),
            Revision = GiantShipGenerator.RuinRevision,
            Index = index,
            Fresh = true,
        };
        plan.Layout = BuildGiantWreckLayout(plan, !_world.Planet.IsAirless);
        var s = plan.Layout.Structure;
        plan.OriginX = at.X - (s.Width / 2);
        plan.OriginZ = at.Z + LandmarkAdminGap;
        plan.GroundY = _generator.SurfaceHeight(_world.Planet, plan.OriginX + (s.Width / 2), plan.OriginZ + (s.Length / 2));
        plan.Seat = "flat";
        CommitGiantWreck(plan);
        BroadcastLandmarks(); // the silhouette and the emitters, for everyone already here
        return _giantWrecks[^1];
    }

    /// <summary>The <c>/tp giantwreck [n]</c> targets: on the ground right in front of each wreck's footprint.</summary>
    private void AppendGiantWreckTargets(List<TeleportTarget> list)
    {
        for (int i = 0; i < _giantWrecks.Count; i++)
        {
            var (cx, _, _, _) = _giantWrecks[i].Rect;
            list.Add(new TeleportTarget("giantwreck", i + 1, FormattableString.Invariant($"giantwreck{i + 1}"),
                SurfaceSpot(WorldConstants.WrapX(cx, _world.Circumference), _giantWrecks[i].Origin.Z - 3)));
        }
    }
}
