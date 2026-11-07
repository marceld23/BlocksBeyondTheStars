// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

public sealed partial class CombatEntity
{
    /// <summary>#3366: a Soundeling's light task and its memory of the dark (null on every other animal, and on a Soundeling
    /// nobody has met yet). Server-only runtime state, never persisted.</summary>
    internal SoundelingState? Soundeling { get; set; }
}

/// <summary>#3366: what one Soundeling is doing about light — the lamp it carries and where to, and whether it stands in the dark.</summary>
internal sealed class SoundelingState
{
    /// <summary>The light item it carries ("" = none).</summary>
    public string Item = string.Empty;

    /// <summary>Who handed it the light — the placed block is theirs, and their base is no "somebody else's base".</summary>
    public string GiverId = string.Empty;

    /// <summary>The dark cell it carries the light to.</summary>
    public Vector3i Target;

    /// <summary>Uptime it sets off (it thinks the gift over until then, cooing).</summary>
    public double SetOffAt;

    /// <summary>Uptime it stops trying to reach <see cref="Target"/> and puts the light down where it got to.</summary>
    public double GiveUpAt;

    /// <summary>Uptime of its next look around for darkness.</summary>
    public double NextDarkCheckAt;

    /// <summary>It stands in the dark (by day: under a roof, no light near).</summary>
    public bool InDark;

    /// <summary>In the dark: the light it walks to, or null — then it curls up and hides.</summary>
    public Vector3f? LightNear;
}

/// <summary>
/// The Soundelings carry lights into the dark (#3366, canon from the school club's creature notes) — see
/// <see cref="SoundelingRules"/> for the numbers:
/// <list type="bullet">
/// <item><b>Night</b>: a Soundeling sleeps curled into a fluff ball and nothing wakes it (the sleeper path; the wake rule skips it).</item>
/// <item><b>The dark by day</b>: under a roof with no light near, it walks to a light within reach, or curls up and hides where
/// it is (<c>NetCreature.Asleep</c>), looking again every <see cref="SoundelingRules.DarkCheckSeconds"/> s.</item>
/// <item><b>A light handed over</b> (the Feed key with a lamp in the slot, <see cref="TryGiveLightToSoundeling"/>): by day, to an
/// awake Soundeling within <see cref="SoundelingRules.GiveRange"/> that carries nothing yet, and only when a dark free spot lies
/// near it. It coos, thinks it over for <see cref="SoundelingRules.PonderSeconds"/> s, carries the lamp there and places it — on
/// a free cell with solid ground under it, never inside somebody else's base, a settlement, a station or a landing pad
/// (<see cref="SoundelingMayPlaceAt"/>) — and coos again. If the spot is gone by then it takes the nearest good cell beside it;
/// with none at all the lamp is set down as a ground drop: a gift is never lost.</item>
/// </list>
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Block id → how far its light reaches, for every block type that lights (built once from the content).</summary>
    private Dictionary<ushort, int>? _lightBlockRadii;

    // --- The hand-over (the Feed key with a lamp) ---------------------------------------------------------------------------

    /// <summary>The Feed action with a light in the slot (#3366): false when the item is no lamp (the food rules go on);
    /// otherwise handled — the lamp goes to a Soundeling, or the player hears why not.</summary>
    private bool TryGiveLightToSoundeling(PlayerSession session, string item)
    {
        var itemDef = _content.GetItem(item);
        var block = itemDef?.PlacesBlock is { Length: > 0 } key ? _content.GetBlock(key) : null;
        if (!SoundelingRules.IsLightItem(itemDef, block))
        {
            return false;
        }

        var p = session.State;
        var taker = SoundelingTakingLight(p.Position, out string? refusal);
        if (taker is null)
        {
            if (refusal is null)
            {
                return false; // no Soundeling near at all: the Feed key's own answer
            }

            Reject(session, "feed", refusal);
            return true;
        }

        if (FindDarkCell(taker.Position, p.PlayerId, SoundelingRules.DarkSearchRadius, SoundelingRules.MinCarryDistance) is not { } spot)
        {
            Reject(session, "feed", "@srv.soundeling.no_dark");
            return true;
        }

        if (!p.Inventory.Remove(item, 1))
        {
            return true;
        }

        SendInventory(session);
        var st = taker.Soundeling ?? new SoundelingState();
        taker.Soundeling = st;
        st.Item = item;
        st.GiverId = p.PlayerId;
        st.Target = WorldConstants.CanonicalBlock(spot, _world.Circumference);
        st.SetOffAt = _uptime + SoundelingRules.PonderSeconds;
        st.GiveUpAt = st.SetOffAt + SoundelingRules.CarryTimeoutSeconds;
        st.InDark = false;
        st.LightNear = null;
        Coo(taker);
        BroadcastCreatures();
        _log.Info($"'{p.Name}' handed 1x {item} to a Soundeling, which carries it to {st.Target}.");
        return true;
    }

    /// <summary>The nearest Soundeling within <see cref="SoundelingRules.GiveRange"/> of <paramref name="at"/> that takes a light
    /// now, or null — with <paramref name="refusal"/> saying why when one is near but cannot (asleep or hiding, or busy).</summary>
    private CombatEntity? SoundelingTakingLight(Vector3f at, out string? refusal)
    {
        refusal = null;
        CombatEntity? best = null;
        double rangeSq = SoundelingRules.GiveRange * SoundelingRules.GiveRange, bestSq = rangeSq;
        foreach (var c in _creatures)
        {
            if (c.IsCompanion || !_speciesById.TryGetValue(c.SpeciesId, out var sp) || !SoundelingRules.IsSoundeling(sp))
            {
                continue;
            }

            double d = WrapDistSq(c.Position, at);
            if (d > rangeSq)
            {
                continue;
            }

            bool carrying = c.Soundeling is { Item.Length: > 0 };
            if (!SoundelingRules.TakesLight(IsNightAt(c.Position), SoundelingCurled(c) || c.FrozenTimer > 0, carrying))
            {
                refusal ??= carrying ? "@srv.soundeling.busy" : "@srv.soundeling.sleeping";
                continue;
            }

            if (d <= bestSq)
            {
                bestSq = d;
                best = c;
            }
        }

        return best;
    }

    // --- The tick ----------------------------------------------------------------------------------------------------------

    /// <summary>Whether a Soundeling holds still this tick (#3366): thinking over a light it was handed, or curled up hiding in
    /// the dark by day. At night the sleeper path curls it up (false here); a lamp it still carries then is put down first.</summary>
    private bool SoundelingHolds(CombatEntity c, CreatureSpecies sp, MotionClass motion, in LocomotionProfile profile, double dt, bool asleep)
    {
        if (c.IsCompanion)
        {
            return false;
        }

        var st = c.Soundeling;
        if (st is { Item.Length: > 0 })
        {
            if (asleep || IsNightAt(c.Position))
            {
                FinishCarry(c, st); // night falls: it puts the light down where it is
                return false;
            }

            if (_uptime >= st.SetOffAt)
            {
                return false; // on its way — TrySoundelingIntent leads it
            }

            HoldStill(c, sp, motion, profile, dt); // thinking the gift over
            return true;
        }

        if (asleep)
        {
            return false;
        }

        if (st is null)
        {
            st = new SoundelingState();
            c.Soundeling = st;
        }

        UpdateSoundelingDark(c, st);
        if (!st.InDark || st.LightNear is not null)
        {
            return false;
        }

        HoldStill(c, sp, motion, profile, dt); // hiding: curled up in the dark
        return true;
    }

    /// <summary>A Soundeling's own intent (#3366): carrying a light to its spot — placed on arrival or when the time is up — or,
    /// in the dark by day, walking to the light it saw. False leaves the usual intents in charge.</summary>
    private bool TrySoundelingIntent(CombatEntity c, CreatureSpecies sp, ref MoveMode intent, ref Vector3f? target)
    {
        if (c.IsCompanion || !SoundelingRules.IsSoundeling(sp) || c.Soundeling is not { } st)
        {
            return false;
        }

        if (st.Item.Length > 0)
        {
            var goal = Center(st.Target);
            if (FlatDistSq(c.Position, goal) <= SoundelingRules.PlaceReach * SoundelingRules.PlaceReach || _uptime >= st.GiveUpAt)
            {
                FinishCarry(c, st);
                return false;
            }

            intent = MoveMode.Seek;
            target = new Vector3f(goal.X, c.Position.Y, goal.Z);
            return true;
        }

        if (st.InDark && st.LightNear is { } light
            && FlatDistSq(c.Position, light) > SoundelingRules.LightRestDistance * SoundelingRules.LightRestDistance)
        {
            intent = MoveMode.Seek;
            target = light;
            return true;
        }

        return false;
    }

    /// <summary>Holds the animal where it is: only its vertical state runs (it still falls when its floor is dug away).</summary>
    private void HoldStill(CombatEntity c, CreatureSpecies sp, MotionClass motion, in LocomotionProfile profile, double dt)
    {
        c.Position = ResolveVertical(c, sp, motion, c.Position, 0f, profile, dt, asleep: false, MoveMode.Roam, moving: false);
        LiftEmbeddedHoverer(c, sp, motion);
    }

    /// <summary>A Soundeling curled up hiding in the dark by day — on the wire it is drawn like a sleeper.</summary>
    private static bool SoundelingCurled(CombatEntity e)
        => !e.IsCompanion && e.Soundeling is { InDark: true, LightNear: null, Item.Length: 0 };

    /// <summary>Looks around for darkness every <see cref="SoundelingRules.DarkCheckSeconds"/> s: under open sky never dark;
    /// under a roof dark unless a light reaches it — and then the nearest light within
    /// <see cref="SoundelingRules.LightSeekRange"/> is where it goes. A change of the curl shows at once.</summary>
    private void UpdateSoundelingDark(CombatEntity c, SoundelingState st)
    {
        if (_uptime < st.NextDarkCheckAt)
        {
            return;
        }

        st.NextDarkCheckAt = _uptime + SoundelingRules.DarkCheckSeconds;
        bool wasCurled = st.InDark && st.LightNear is null;
        var feet = c.Position.ToBlock();
        if (CellOpenToSky(feet))
        {
            st.InDark = false;
            st.LightNear = null;
        }
        else
        {
            var (lit, nearest) = LightAround(feet, (int)SoundelingRules.LightSeekRange);
            st.InDark = !lit;
            st.LightNear = lit ? null : nearest;
        }

        if (wasCurled != (st.InDark && st.LightNear is null))
        {
            BroadcastCreatures();
        }
    }

    // --- Putting the light down -------------------------------------------------------------------------------------------

    /// <summary>The end of the carry: the light goes onto its spot (or the nearest good cell beside the Soundeling), it coos and
    /// VEGA explains it once to the giver; with no good cell at all the lamp is set down as a ground drop.</summary>
    private void FinishCarry(CombatEntity c, SoundelingState st)
    {
        string item = st.Item, giver = st.GiverId;
        st.Item = string.Empty;
        st.GiverId = string.Empty;
        float slack = SoundelingRules.PlaceReach + 1f;
        Vector3i? cell = FlatDistSq(c.Position, Center(st.Target)) <= slack * slack && SoundelingMayPlaceAt(st.Target, giver)
            ? st.Target
            : FindDarkCell(c.Position, giver, 3, 0f) ?? FreeCellBeside(c.Position, giver);
        if (cell is { } spot && PlaceSoundelingLight(spot, item, giver))
        {
            Coo(c);
            if (FindSessionByPlayerId(giver) is { } s)
            {
                ShipAiHintOnce(s, "soundeling_light"); // VEGA: it carried your light into the dark
            }
        }
        else
        {
            SpillToGround(c.Position.ToBlock(), new[] { new ItemAmount(item, 1) }); // nowhere to put it: a gift is never lost
        }

        BroadcastCreatures();
    }

    /// <summary>Places the lamp block of <paramref name="item"/> in <paramref name="cell"/>, as the giver's block.</summary>
    private bool PlaceSoundelingLight(Vector3i cell, string item, string giverId)
    {
        var block = _content.GetItem(item)?.PlacesBlock is { Length: > 0 } key ? _content.GetBlock(key) : null;
        if (block is null || !SoundelingMayPlaceAt(cell, giverId))
        {
            return false;
        }

        var at = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        _world.SetBlock(at, block.NumericId, owner: giverId);
        BroadcastToWorld(new BlockChanged { X = at.X, Y = at.Y, Z = at.Z, Block = block.NumericId.Value });
        _log.Info($"A Soundeling placed {block.Key} at {at} for '{giverId}'.");
        return true;
    }

    /// <summary>
    /// Where a Soundeling may put a light (#3366, server-validated): inside the build band, in a loaded air cell (no fluid, no
    /// door) with solid ground under it — not a fluid, a crown or a prop — and never where the giver could not build: somebody
    /// else's base, a settlement, a station, a ship, a stone monument, a factory or a landing pad.
    /// </summary>
    private bool SoundelingMayPlaceAt(Vector3i cell, string giverId)
    {
        cell = WorldConstants.CanonicalBlock(cell, _world.Circumference);
        if (!WithinBuildHeight(cell.Y) || !_world.IsChunkLoaded(WorldConstants.WorldToChunk(cell))
            || !_world.GetBlockIfLoaded(cell).IsAir)
        {
            return false;
        }

        var below = _world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y - 1, cell.Z));
        return IsCollidingBlock(below, fluidsPass: true, foliagePasses: true)
               && !DoorFillsCell(cell) && !IsOnLandingPad(cell)
               && !IsBreakProtected(cell, BlockId.Air, giverId, actorIsAdmin: false);
    }

    /// <summary>The nearest cell beside <paramref name="at"/> (3×3×3 round the feet) a light may go into, or null.</summary>
    private Vector3i? FreeCellBeside(Vector3f at, string giverId)
    {
        var feet = at.ToBlock();
        foreach (int dy in new[] { 0, 1, -1 })
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var cell = new Vector3i(feet.X + dx, feet.Y + dy, feet.Z + dz);
                    if (SoundelingMayPlaceAt(cell, giverId))
                    {
                        return cell;
                    }
                }
            }
        }

        return null;
    }

    // --- Darkness -----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The dark spot nearest to <paramref name="from"/> (but at least <paramref name="minDist"/> blocks away) within
    /// <paramref name="radius"/> blocks and <see cref="SoundelingRules.DarkSearchDepth"/> up and down where a light may go: a
    /// cell under a roof that no light reaches. A lower or higher cell counts a little further away. Null when there is none.
    /// One look at the blocks per call — a hand-over, not a tick.
    /// </summary>
    private Vector3i? FindDarkCell(Vector3f from, string giverId, int radius, float minDist)
    {
        var origin = from.ToBlock();
        int depth = SoundelingRules.DarkSearchDepth;
        var lights = LightsAround(origin, radius + SoundelingRules.LitRadius, depth + SoundelingRules.LitRadius);
        Vector3i? best = null;
        float bestScore = float.MaxValue;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                float fx = origin.X + dx + 0.5f - from.X, fz = origin.Z + dz + 0.5f - from.Z;
                float flat = (float)System.Math.Sqrt((fx * fx) + (fz * fz));
                if (flat > radius || flat < minDist || flat >= bestScore)
                {
                    continue;
                }

                for (int dy = depth; dy >= -depth; dy--)
                {
                    var cell = new Vector3i(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    float score = flat + (System.Math.Abs(dy) * 0.5f);
                    if (score < bestScore && SoundelingMayPlaceAt(cell, giverId) && !CellOpenToSky(cell) && !LitBy(cell, lights))
                    {
                        best = cell;
                        bestScore = score;
                    }
                }
            }
        }

        return best;
    }

    /// <summary>Nothing that a body collides with stands in the <see cref="SoundelingRules.SkyScan"/> cells over the cell (tree
    /// crowns and fluids let the sky through; an unloaded cell counts as open).</summary>
    private bool CellOpenToSky(Vector3i cell)
    {
        for (int dy = 1; dy <= SoundelingRules.SkyScan; dy++)
        {
            if (IsCollidingBlock(_world.GetBlockIfLoaded(new Vector3i(cell.X, cell.Y + dy, cell.Z)), fluidsPass: true, foliagePasses: true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Every light source in the box of half-width <paramref name="radius"/> and half-height <paramref name="depth"/>
    /// around <paramref name="origin"/>, with its reach.</summary>
    private List<(Vector3i Cell, int Radius)> LightsAround(Vector3i origin, int radius, int depth)
    {
        var radii = LightBlockRadii();
        var found = new List<(Vector3i Cell, int Radius)>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dy = -depth; dy <= depth; dy++)
                {
                    var cell = new Vector3i(origin.X + dx, origin.Y + dy, origin.Z + dz);
                    var id = _world.GetBlockIfLoaded(cell);
                    if (!id.IsAir && radii.TryGetValue(id.Value, out int r))
                    {
                        found.Add((cell, r));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>Whether one of <paramref name="lights"/> reaches <paramref name="cell"/> (<see cref="SoundelingRules.Lights"/>).</summary>
    private static bool LitBy(Vector3i cell, List<(Vector3i Cell, int Radius)> lights)
    {
        foreach (var (at, r) in lights)
        {
            int d = System.Math.Max(System.Math.Abs(at.X - cell.X), System.Math.Max(System.Math.Abs(at.Y - cell.Y), System.Math.Abs(at.Z - cell.Z)));
            if (SoundelingRules.Lights(d, r))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a light reaches <paramref name="feet"/>, and the nearest light within <paramref name="range"/> (its cell's
    /// centre) — one box scan.</summary>
    private (bool Lit, Vector3f? Nearest) LightAround(Vector3i feet, int range)
    {
        var lights = LightsAround(feet, range, System.Math.Min(range, SoundelingRules.DarkSearchDepth));
        Vector3f? nearest = null;
        long bestSq = long.MaxValue;
        foreach (var (at, _) in lights)
        {
            long dx = at.X - feet.X, dy = at.Y - feet.Y, dz = at.Z - feet.Z;
            long d = (dx * dx) + (dy * dy) + (dz * dz);
            if (d < bestSq)
            {
                bestSq = d;
                nearest = Center(at);
            }
        }

        return (LitBy(feet, lights), nearest);
    }

    /// <summary>Block id → light reach for every block type that lights (<see cref="BlockLight.NaturalColorOf"/>), built once.</summary>
    private Dictionary<ushort, int> LightBlockRadii()
    {
        if (_lightBlockRadii is { } known)
        {
            return known;
        }

        var map = new Dictionary<ushort, int>();
        foreach (var def in _content.Blocks.Values)
        {
            if (BlockLight.NaturalColorOf(def) != 0)
            {
                map[def.NumericId.Value] = BlockLight.RadiusOf(def);
            }
        }

        _lightBlockRadii = map;
        return map;
    }

    /// <summary>The horizontal distance² between two points, the short way round the seams.</summary>
    private double FlatDistSq(Vector3f a, Vector3f b) => WrapDistSq(new Vector3f(a.X, 0f, a.Z), new Vector3f(b.X, 0f, b.Z));

    /// <summary>A soft coo at the Soundeling.</summary>
    private void Coo(CombatEntity c)
    {
        int roll = StableStringHash(c.Id) ^ (int)(_uptime * 11.0);
        BroadcastToWorld(new SoundFx { SoundId = SoundelingRules.CooSound(roll), X = c.Position.X, Y = c.Position.Y + 0.4f, Z = c.Position.Z });
    }

    // --- Test seams (#3366) ----------------------------------------------------------------------------------------------

    /// <summary>Test seam: the light the Soundeling <paramref name="id"/> carries ("" = none).</summary>
    public string SoundelingCarryingForTest(string id) => _creatures.Find(c => c.Id == id)?.Soundeling?.Item ?? string.Empty;

    /// <summary>Test seam: where the Soundeling <paramref name="id"/> carries its light, or null.</summary>
    public Vector3i? SoundelingTargetForTest(string id)
        => _creatures.Find(c => c.Id == id)?.Soundeling is { Item.Length: > 0 } st ? st.Target : null;

    /// <summary>Test seam: whether the Soundeling <paramref name="id"/> is curled up hiding in the dark by day.</summary>
    public bool SoundelingCurledForTest(string id) => _creatures.Find(c => c.Id == id) is { } c && SoundelingCurled(c);

    /// <summary>Test seam: <see cref="FindDarkCell"/> round a point (the hand-over's search).</summary>
    public Vector3i? FindDarkCellForTest(Vector3f from, string giverId)
        => FindDarkCell(from, giverId, SoundelingRules.DarkSearchRadius, SoundelingRules.MinCarryDistance);
}
