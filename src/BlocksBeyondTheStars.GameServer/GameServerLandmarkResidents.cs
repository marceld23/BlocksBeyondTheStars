// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Globalization;
using System.Linq;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// INHABITED landmarks (#3499) — a landmark whose template is <c>protected</c> and not a ruin: one building in the open
/// country that somebody keeps, like a lodge (a burger restaurant with a keeper who sells food).
/// <list type="bullet">
/// <item><b>Protection.</b> Its box — down through the plinth — is read-only for everyone, through the one rule every
/// guard already asks (<see cref="IsSettlementBlock"/>: mining, bursts, the blaster, the pump, the drills, fire). A
/// block a player placed there stays theirs to take away again, like in a village (#2727).</item>
/// <item><b>Residents.</b> Re-spawned on every load from the markers, each at its post: a profession post
/// (<see cref="NpcProfessions"/>) is staffed by that profession; a <c>vendor</c> marker by a keeper whose settings
/// name the trade — <c>theme=burgers</c> (the <c>marketTheme</c> of the recipes it sells), <c>inside=1</c> (it only
/// sells to a customer standing in its own closed room, the grocer's rule), <c>name=&lt;locale key&gt;</c> (its
/// nameplate); an <c>npc</c> marker by a plain resident. They keep their post — a restaurant has no beds.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How far a landmark's keeper strays from its post.</summary>
    private const float LandmarkKeeperLeash = 1.5f;

    /// <summary>Whether the cell lies in an inhabited landmark's protected box (the footprint, from the plinth's foot to
    /// the roof).</summary>
    private bool IsLandmarkBlock(Vector3i pos)
    {
        foreach (var lm in _landmarks)
        {
            if (!lm.Protected)
            {
                continue;
            }

            int dx = WorldConstants.WrapDeltaX(pos.X - lm.Origin.X, _world.Circumference);
            if (dx >= 0 && dx < lm.Structure.Width && pos.Z >= lm.Origin.Z && pos.Z < lm.Max.Z
                && pos.Y >= lm.Origin.Y - LandmarkPlinthDepth && pos.Y < lm.Max.Y)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Test seam: <see cref="IsLandmarkBlock"/>.</summary>
    internal bool IsLandmarkBlockForTest(Vector3i pos) => IsLandmarkBlock(pos);

    /// <summary>Test seam: the residents of landmark <paramref name="key"/> — role, market theme, the room rule, nameplate.</summary>
    internal System.Collections.Generic.List<(string Role, string Theme, bool InsideOnly, string NameKey, Vector3f Post)> LandmarkResidentsForTest(string key)
        => _npcs.Where(n => n.LandmarkKey == key).Select(n => (n.Role, n.Theme, n.SellsOnlyInside, n.NameKey, n.Home)).ToList();

    /// <summary>The residents of an inhabited landmark, one per post marker (idempotent per load).</summary>
    private void SpawnLandmarkResidents(LandmarkInstance lm)
    {
        if (!lm.Protected || _npcs.Any(n => n.LandmarkKey == lm.Key))
        {
            return;
        }

        int index = 0;
        foreach (var marker in lm.Structure.Markers)
        {
            var profession = NpcProfessions.ByMarker(marker.Type);
            if (profession is null && marker.Type is not ("vendor" or "npc"))
            {
                continue;
            }

            var cell = WorldConstants.CanonicalBlock(lm.Cell(marker.LocalPos), _world.Circumference);
            var post = new Vector3f(cell.X + 0.5f, cell.Y, cell.Z + 0.5f);
            string person = "landmark:" + _world.LocationId + ":" + lm.Key + "#" + index.ToString(CultureInfo.InvariantCulture);
            var rng = new System.Random(unchecked((int)(_meta.Seed ^ WorldGenerator.StableHash("landmark-resident:" + person))));
            index++;

            ServerNpc npc;
            if (profession is not null)
            {
                npc = MakeNpc(profession.Role, profession.Trades ? profession.Theme : "settlers", robotic: false, post, rng);
                ApplyProfession(npc, profession);
            }
            else if (marker.Type == "vendor")
            {
                var settings = MarkerSettings.Parse(marker.Data);
                string theme = settings.Get("theme", "settlers");
                npc = MakeNpc("vendor", theme, robotic: false, post, rng);
                npc.Theme = theme;
                npc.SellsOnlyInside = settings.GetBool("inside");
                if (settings.Has("name"))
                {
                    npc.NameKey = settings.Get("name");
                }
            }
            else
            {
                npc = MakeNpc("settler", "settlers", robotic: false, post, rng);
            }

            npc.LandmarkKey = lm.Key;
            npc.PersonKey = person;
            npc.Work = post;
            npc.HasWork = true;
            npc.Leash = LandmarkKeeperLeash;
            _npcs.Add(npc);
        }

        if (index > 0)
        {
            BroadcastNpcs();
        }
    }
}
