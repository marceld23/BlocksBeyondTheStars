// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The admin's landmark command (#3486): <c>/landmark</c> (verb <c>landmark</c>, behind the world's cheat rule) — a play
/// test tool for authors. <c>/landmark list</c> names every landmark of the content; <c>/landmark &lt;key&gt;</c> stamps
/// that landmark a few steps in front of the admin, whatever the planet's roll said, with a fresh seed each time (so a
/// ruin can be looked at in several shapes side by side). A stamp made this way is pinned like the rolled one — it takes
/// the next free record index of the body — and re-derived on every later load. <c>/tp landmark [n]</c> goes to one.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far in front of the admin (+Z) the near edge of a hand-stamped landmark lies.</summary>
    private const int LandmarkAdminGap = 6;

    private void AdminLandmark(PlayerSession session, string? argument)
    {
        Serve(session);
        string arg = (argument ?? string.Empty).Trim();
        if (_generator.TerrainGeneration < WorldDescription.LandmarkGeneration)
        {
            Reject(session, "admin", "@srv.landmark.too_old");
            return;
        }

        if (arg.Length == 0 || arg.Equals("list", StringComparison.OrdinalIgnoreCase))
        {
            ListLandmarks(session);
            return;
        }

        var template = _content.LandmarkByKey(arg);
        if (template is null)
        {
            Reject(session, "admin", "@srv.landmark.unknown");
            return;
        }

        var planet = _world.Planet;
        if (planet is null || planet.Void || InSpace(session.State.PlayerId) || session.CurrentLocationId != _world.LocationId)
        {
            Reject(session, "admin", "@srv.landmark.not_here");
            return;
        }

        if (ForceLandmark(template, session.State.Position.ToBlock()) is not { } lm)
        {
            Reject(session, "admin", "@srv.landmark.unknown"); // #3518: a generator entry its source cannot build (warned)
            return;
        }

        CheatLog(session.State, $"stamped landmark '{template.Key}' on {_world.LocationId} at ({lm.Origin.X}, {lm.GroundY}, {lm.Origin.Z})");
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.landmark.stamped").Replace("{name}", LandmarkName(lm, session.Locale)),
        });
    }

    private void ListLandmarks(PlayerSession session)
    {
        if (_content.Landmarks.Count == 0)
        {
            Reject(session, "admin", "@srv.landmark.none");
            return;
        }

        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.landmark.list_header")
                .Replace("{count}", _content.Landmarks.Count.ToString(CultureInfo.InvariantCulture)),
        });
        foreach (var t in _content.Landmarks.Take(TeleportListMaxLines))
        {
            Send(session, new ServerMessage
            {
                // netstandard2.1 (the server's second target) has no string.Create(IFormatProvider, …) — FormattableString does.
                Text = FormattableString.Invariant($"/landmark {t.Key} · {t.Width}×{t.Height}×{t.Length}{(LandmarkRules.IsRuin(t) ? " · ruin" : string.Empty)}"),
            });
        }
    }

    /// <summary>Stamps <paramref name="template"/> in front of <paramref name="at"/> on the active world, whatever its roll
    /// and its conditions say, under the next free record index. Returns the new instance — null when a generator entry's
    /// source cannot build it (#3518).</summary>
    internal LandmarkInstance? ForceLandmark(StructureTemplate template, Vector3i at)
    {
        int index = _meta.Placements.Where(r => r.LocationId == _world.LocationId && r.Kind == LandmarkRules.RecordKind)
            .Select(r => r.Index + 1).DefaultIfEmpty(0).Max();
        string lane = FormattableString.Invariant($"landmark:admin:{_world.LocationId}:{index}:{at.X}:{at.Z}");
        var plan = new LandmarkPlan
        {
            Template = template,
            Seed = unchecked((long)StableHashing.Mixed(lane, _meta.Seed)),
            Turns = template.IsSource ? 0 : StableHashing.Index(lane + ":turn", _meta.Seed, 4), // #3518: an entry stands as built
            Index = index,
            Fresh = true,
            // A giant made for a newer generation (the school club's, gen 39) is one stored blob on any world.
            ForceOverlay = template.MinTerrainGeneration > _meta.Description.TerrainGeneration,
        };
        plan.Layout = BuildLandmarkLayout(plan);
        if (plan.Layout is null)
        {
            return null;
        }

        var s = plan.Layout.Structure;
        plan.OriginX = at.X - (s.Width / 2);
        plan.OriginZ = at.Z + LandmarkAdminGap;
        plan.GroundY = _generator.SurfaceHeight(_world.Planet, plan.OriginX + (s.Width / 2), plan.OriginZ + (s.Length / 2));
        plan.Seat = "flat";
        CommitLandmark(plan);
        BroadcastLandmarks(); // the silhouette and the emitters, for everyone already here
        return _landmarks[^1];
    }
}
