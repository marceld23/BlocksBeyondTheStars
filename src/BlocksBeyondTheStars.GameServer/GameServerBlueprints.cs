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
/// Whole-build share codes (#1117): the blueprint tool copies a placed region (≤ 16³) into a
/// <c>BBTS1-B-…</c> code — the structures' counterpart to the form/paint codes (#846) — and pastes one back
/// block by block. Every pasted cell passes the SAME validation as a hand-placed block (protection zones,
/// build height, special blocks) and is paid from the player's inventory (free in creative/instant-build);
/// what could not be placed is tallied honestly in the result. The blueprint credits its author.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How close the player must be to the region they copy from / paste to (blocks). Generous —
    /// the corners were aim-selected anyway — but stops remote copying of other people's builds.</summary>
    private const int BlueprintRangeBlocks = 48;

    /// <summary>Paste cooldown (seconds): a paste is up to 4096 placements in one intent.</summary>
    private const double BlueprintPasteCooldown = 3.0;

    /// <summary>Blocks a paste never places: they found server entities on placement (doors, containers,
    /// beacons, pads, bases, ship keels) — a pasted copy would be a dead block that looks like one.</summary>
    private static bool IsBlueprintSpecialBlock(string key)
        => key is "base_core" or "radio_beacon" or "beam_block";

    private void HandleCopyBuild(PlayerSession session, CopyBuildIntent intent)
    {
        int sx = System.Math.Abs(intent.X2 - intent.X1) + 1;
        int sy = System.Math.Abs(intent.Y2 - intent.Y1) + 1;
        int sz = System.Math.Abs(intent.Z2 - intent.Z1) + 1;
        if (sx > BlueprintCode.MaxEdge || sy > BlueprintCode.MaxEdge || sz > BlueprintCode.MaxEdge)
        {
            Send(session, new BuildCodeResult { Success = false, Reason = "@srv.blueprint.too_big" });
            return; // NOTE: a region spanning the world seam reads as huge here — copying across it is not supported
        }

        var min = new Vector3i(
            System.Math.Min(intent.X1, intent.X2),
            System.Math.Min(intent.Y1, intent.Y2),
            System.Math.Min(intent.Z1, intent.Z2));
        if (!WithinBlueprintRange(session, min, sx, sy, sz))
        {
            Send(session, new BuildCodeResult { Success = false, Reason = "@out_of_reach" });
            return;
        }

        var cells = new BlueprintCell[sx * sy * sz];
        bool any = false;
        for (int x = 0; x < sx; x++)
            for (int y = 0; y < sy; y++)
                for (int z = 0; z < sz; z++)
                {
                    var pos = WorldConstants.CanonicalBlock(new Vector3i(min.X + x, min.Y + y, min.Z + z), _world.Circumference);
                    if (!WithinBuildHeight(pos.Y))
                    {
                        continue;
                    }

                    var block = _world.GetBlock(pos);
                    if (block.IsAir || IsFluid(block.Value))
                    {
                        continue; // fluids don't copy — a pasted lake would be a griefing tool, not a build
                    }

                    var def = _content.BlockById(block);
                    if (def is null)
                    {
                        continue;
                    }

                    var (tint, glow) = _world.GetModifier(pos);
                    cells[BlueprintCode.CellIndex(x, y, z, sy, sz)] = new BlueprintCell
                    {
                        Key = def.Key,
                        Shape = _world.GetShape(pos), // design bits are stripped by the encoder (paint ids are save-local)
                        Tint = tint,
                        Glow = glow,
                    };
                    any = true;
                }

        if (!any)
        {
            Send(session, new BuildCodeResult { Success = false, Reason = "@srv.blueprint.empty" });
            return;
        }

        string name = StripControlChars(intent.Name);
        name = name.Length > 24 ? name[..24] : name;
        string code = BlueprintCode.Encode(sx, sy, sz, session.State.Name, name, cells);
        if (code.Length == 0)
        {
            Send(session, new BuildCodeResult { Success = false, Reason = "@srv.blueprint.too_big" });
            return;
        }

        Send(session, new BuildCodeResult { Success = true, Code = code });
    }

    private void HandlePasteBuild(PlayerSession session, PasteBuildIntent intent)
    {
        if (_uptime < session.NextBlueprintPasteAt)
        {
            Send(session, new BuildPasteResult { Success = false, Reason = "@srv.blueprint.cooldown" });
            return;
        }

        if (!BlueprintCode.TryDecode(intent.Code, out int sx, out int sy, out int sz, out string author, out _, out var cells))
        {
            Send(session, new BuildPasteResult { Success = false, Reason = "@srv.blueprint.bad_code" });
            return;
        }

        var origin = new Vector3i(intent.X, intent.Y, intent.Z);
        if (!WithinBlueprintRange(session, origin, sx, sy, sz))
        {
            Send(session, new BuildPasteResult { Success = false, Reason = "@out_of_reach" });
            return;
        }

        session.NextBlueprintPasteAt = _uptime + BlueprintPasteCooldown;
        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        int placed = 0, skippedMaterials = 0, skippedProtected = 0, skippedSpecial = 0;

        for (int x = 0; x < sx; x++)
            for (int y = 0; y < sy; y++)
                for (int z = 0; z < sz; z++)
                {
                    var cell = cells[BlueprintCode.CellIndex(x, y, z, sy, sz)];
                    if (string.IsNullOrEmpty(cell.Key))
                    {
                        continue; // air
                    }

                    var def = _content.GetBlock(cell.Key!);
                    if (def is null || IsBlueprintSpecialBlock(def.Key) || IsDoorBlock(def.Key) || IsContainerBlock(def.Key)
                        || def.Key == ShipCoreBlock)
                    {
                        skippedSpecial++; // unknown in this save, or a block that founds an entity on placement
                        continue;
                    }

                    var pos = WorldConstants.CanonicalBlock(new Vector3i(origin.X + x, origin.Y + y, origin.Z + z), _world.Circumference);
                    if (!WithinBuildHeight(pos.Y) || !_world.GetBlock(pos).IsAir
                        || (!session.State.IsAdmin && IsOnLandingPad(pos))
                        || IsStationBlock(pos)
                        || IsFactoryProtected(pos, session.State.PlayerId, session.State.IsAdmin)
                        || IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin)
                        || ShipInteriorContains(new Vector3f(pos.X, pos.Y, pos.Z))
                        || ConstructionContains(new Vector3f(pos.X, pos.Y, pos.Z)))
                    {
                        skippedProtected++; // occupied, protected, or outside the build band — same rules as by hand
                        continue;
                    }

                    if ((def.Key == "torch" && !AtmospherePresent)
                        || (IsFlora(def.NumericId.Value) && (!IsValidFloraHost(def.NumericId.Value, pos) || !IsFloraEnclosedForVoidWorld(pos))))
                    {
                        skippedSpecial++; // a dud here by hand too (airless torch, plant without its host block)
                        continue;
                    }

                    if (!free)
                    {
                        var item = _content.GetItem(def.Key);
                        if (item is null || item.PlacesBlock != def.Key || pool.Count(def.Key) < 1)
                        {
                            skippedMaterials++; // the ghost stays a gap until the builder can afford the block
                            continue;
                        }

                        pool.Remove(new[] { new ItemAmount(def.Key, 1) });
                    }

                    // A custom-form descriptor (#843) references THIS save's registry; a foreign index falls back
                    // to a plain cube rather than stamping geometry nobody can mesh. Tint/glow only on tintables.
                    int shape = cell.Shape;
                    if (ShapeCode.IsCustomDescriptor(shape) && !HasCustomShape(ShapeCode.ShapeOf(shape)))
                    {
                        shape = 0;
                    }

                    int tint = def.Tintable ? cell.Tint : 0;
                    int glow = def.Tintable ? cell.Glow : 0;
                    _world.SetBlock(pos, def.NumericId, tint, glow, shape, session.State.PlayerId);
                    BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = def.NumericId.Value, Tint = tint, Glow = glow, Shape = shape });
                    OnBlockPlaced(session, def, pos); // build missions advance like any hand-placed block (#1116)
                    placed++;
                }

        if (placed > 0)
        {
            Advance(session, AchievementCounters.BuildAny, placed);
            SendInventory(session);
        }

        Send(session, new BuildPasteResult
        {
            Success = placed > 0,
            Placed = placed,
            SkippedMaterials = skippedMaterials,
            SkippedProtected = skippedProtected,
            SkippedSpecial = skippedSpecial,
            Author = StripControlChars(author),
            Reason = placed > 0 ? string.Empty : "@srv.blueprint.nothing_placed",
        });
    }

    /// <summary>The player must stand near the region they copy or paste (Chebyshev, generous).</summary>
    private bool WithinBlueprintRange(PlayerSession session, Vector3i min, int sx, int sy, int sz)
    {
        var p = session.State.Position;
        double cx = min.X + sx / 2.0, cy = min.Y + sy / 2.0, cz = min.Z + sz / 2.0;
        double dx = System.Math.Abs(WorldConstants.WrapDeltaX(p.X - cx, _world.Circumference));
        double dy = System.Math.Abs(p.Y - cy);
        double dz = System.Math.Abs(WorldConstants.WrapDeltaZ(p.Z - cz, _world.Circumference));
        return System.Math.Max(dx, System.Math.Max(dy, dz)) <= BlueprintRangeBlocks;
    }
}
