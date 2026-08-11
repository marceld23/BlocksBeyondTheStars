// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-designed block forms (#843) — the geometry sibling of <see cref="GameServerPaint"/>. A form is a
/// micro-voxel bitmap registered ONCE per save (content-hash dedup) under a free shape index, and from then
/// on it IS a shape index: it rides through crafting, the item key, placing, persistence, mining and the
/// wire without a single new field, exactly like <see cref="BlockShape.Slab"/> does.
///
/// Two things differ from paint, both forced by the narrow id space (45 indices, not 65 535):
/// a wipe FREES the id for reuse instead of leaving a tombstone — with 45 slots, never-reuse would strand a
/// long-lived world — and the cap is therefore the id space itself. The consequence is documented for the
/// operator: after a wipe, a block or item still holding that index adopts whatever form claims the slot next.
/// Forms are cosmetic geometry and a wipe is an explicit moderation act, so that trade is the right way round.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>Longest player-chosen form name we store (and echo to other clients).</summary>
    internal const int MaxCustomShapeNameChars = 24;

    private readonly Dictionary<int, StoredCustomShape> _customShapes = new();
    private readonly Dictionary<string, int> _customShapeIdByVoxels = new();

    /// <summary>Restores the form registry at server start. Idempotent — clears first. Malformed rows (a
    /// hand-edited save) are dropped rather than served to clients that would then mesh nonsense.</summary>
    private void LoadCustomShapes()
    {
        _customShapes.Clear();
        _customShapeIdByVoxels.Clear();
        foreach (var shape in _repo.ListCustomShapes())
        {
            if (!ShapeCode.IsCustomShape(shape.Id) || !CustomShape.IsValidVoxels(shape.Voxels) || !CustomShape.FitsBudget(shape.Voxels))
            {
                _log.Warn($"Dropping malformed custom shape #{shape.Id} from the save.");
                continue;
            }

            _customShapes[shape.Id] = shape;
            _customShapeIdByVoxels[shape.Voxels] = shape.Id;
        }
    }

    /// <summary>True when this save has a form registered under that shape index — the registry lookup the
    /// place/craft validators hand to <see cref="ShapeCode.IsPlaceableShape"/>.</summary>
    internal bool HasCustomShape(int shapeIndex) => _customShapes.ContainsKey(shapeIndex);

    /// <summary>Handles the "craft this form out of this material" intent: register (or find) the form, then
    /// run the ordinary free 1:1 shape exchange. Gated on the shaping tool, throttled like painting.</summary>
    private void HandleCustomShapeCraft(PlayerSession session, CustomShapeCraftIntent intent)
    {
        string voxels = (intent.Voxels ?? string.Empty).ToLowerInvariant();
        if (!CustomShape.IsValidVoxels(voxels))
        {
            return; // malformed (wrong length or charset) — drop it, never persist or relay
        }

        // A form that needs more boxes than the budget would cost every client a collider cook it cannot
        // afford, so it is refused here — the one place that decides what geometry may exist in this save.
        if (!CustomShape.FitsBudget(voxels))
        {
            CraftFail(session, "shape", "@srv.shape.too_detailed");
            return;
        }

        // The shaping tool is what unlocks player-designed forms; the built-in forms stay free for everyone.
        // Checked server-side because a greyed-out button is not a gate.
        if (!HasShapeTool(session.State))
        {
            CraftFail(session, "shape", "@srv.shape.needs_tool");
            return;
        }

        // Normally the source is a building material. The one exception is a blank STENCIL (#846): stamping a
        // form onto it produces a giftable "shape_stencil#s<id>" through the very same 1:1 exchange — the item
        // key carries the form index for a stencil exactly as it does for a block.
        bool stencil = ItemKey.Base(intent.SourceItemKey) == "shape_stencil";
        if (!stencil && !IsShapeableSource(session, intent.SourceItemKey, "shape"))
        {
            return;
        }

        // Same anti-spam as painting: registering is rare, and each new form means a disk write plus a
        // world-wide broadcast.
        if (_uptime < session.NextCustomShapeAt)
        {
            return;
        }

        if (!TryRegisterCustomShape(session, voxels, intent.Name ?? string.Empty, out int shapeIndex))
        {
            Send(session, new ServerMessage { Text = "@srv.shape.limit" });
            return;
        }

        session.NextCustomShapeAt = _uptime + 2.0;
        ApplyShapeExchange(session, intent.SourceItemKey, intent.Count, shapeIndex, "shape", intent.Slot);
    }

    /// <summary>True when the player carries the shaping tool (hotbar or backpack — holding it is only
    /// required for the in-world actions, not for crafting from the menu).</summary>
    private static bool HasShapeTool(PlayerState player)
    {
        foreach (var slot in player.Inventory.Slots)
        {
            if (slot is { IsEmpty: false } stack && ItemKey.Base(stack.Item) == "shape_tool")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Resolves a bitmap to an existing form id or registers a new one (persist + broadcast to every
    /// joined session — forms are save-global, a shaped block may be on any world). False = no free slot.</summary>
    private bool TryRegisterCustomShape(PlayerSession session, string voxels, string name, out int shapeIndex)
    {
        if (_customShapeIdByVoxels.TryGetValue(voxels, out shapeIndex))
        {
            return true; // identical geometry already registered — reuse it, whoever designed it first
        }

        shapeIndex = NextFreeCustomShapeId();
        if (shapeIndex == 0)
        {
            return false;
        }

        var stored = new StoredCustomShape
        {
            Id = shapeIndex,
            OwnerId = session.State.PlayerId,
            OwnerName = session.State.Name,
            Name = CleanCustomShapeName(name, shapeIndex),
            Voxels = voxels,
        };

        _customShapes[shapeIndex] = stored;
        _customShapeIdByVoxels[voxels] = shapeIndex;
        _repo.SaveCustomShape(stored);

        var msg = new CustomShapeData { Id = stored.Id, Voxels = stored.Voxels, Name = stored.Name, Owner = stored.OwnerName };
        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined)
            {
                Send(viewer, msg);
            }
        }

        return true;
    }

    /// <summary>The lowest unused custom shape index, or 0 when the save has none left. Wiped ids reappear
    /// here — see the class remarks for why reuse is the right call at 45 slots.</summary>
    private int NextFreeCustomShapeId()
    {
        for (int id = ShapeCode.FirstCustom; id <= ShapeCode.LastCustom; id++)
        {
            if (!_customShapes.ContainsKey(id))
            {
                return id;
            }
        }

        return 0;
    }

    /// <summary>Trims a player-chosen form name to something safe to show to other players: control chars
    /// stripped and length-capped, the same treatment a beacon label gets, and never empty (an unnamed form
    /// falls back to its id so the crafting menu always has something to show).</summary>
    private static string CleanCustomShapeName(string name, int shapeIndex)
    {
        string trimmed = StripControlChars(name).Trim();
        if (trimmed.Length > MaxCustomShapeNameChars)
        {
            trimmed = trimmed.Substring(0, MaxCustomShapeNameChars).Trim();
        }

        return trimmed.Length == 0 ? $"Form {shapeIndex}" : trimmed;
    }

    /// <summary>Pushes the full form registry to a newcomer — before any chunk can arrive, so blocks carrying
    /// a player-designed form in the first streamed chunks mesh immediately instead of flashing as cubes.</summary>
    private void SendCustomShapes(PlayerSession session)
    {
        if (_customShapes.Count == 0)
        {
            return;
        }

        var live = _customShapes.Values.OrderBy(s => s.Id).ToList();
        Send(session, new CustomShapeList
        {
            Ids = live.Select(s => s.Id).ToArray(),
            Voxels = live.Select(s => s.Voxels).ToArray(),
            Names = live.Select(s => s.Name).ToArray(),
            Owners = live.Select(s => s.OwnerName).ToArray(),
        });
    }

    /// <summary>Chat command <c>/reportshape</c>: records the nearest block carrying a player-designed form,
    /// the form-moderation twin of <c>/reportpaint</c>. No client UI beyond typing the command.</summary>
    private void HandleCustomShapeReport(PlayerSession session)
    {
        const int radius = 6;
        var centre = new Vector3i(
            (int)System.Math.Floor(session.State.Position.X),
            (int)System.Math.Floor(session.State.Position.Y),
            (int)System.Math.Floor(session.State.Position.Z));

        Vector3i? found = null;
        int foundShape = 0;
        double bestDistSq = double.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var p = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                    int shapeIndex = ShapeCode.ShapeOf(_world.GetShape(p));
                    if (!ShapeCode.IsCustomShape(shapeIndex) || !_customShapes.ContainsKey(shapeIndex))
                    {
                        continue;
                    }

                    double distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        found = p;
                        foundShape = shapeIndex;
                    }
                }
            }
        }

        if (found is not { } cell)
        {
            Send(session, new ServerMessage { Text = "@srv.shape.report_none" });
            return;
        }

        var design = _customShapes[foundShape];
        _repo.SavePaintReport(new StoredPaintReport
        {
            ReporterId = session.State.PlayerId,
            OwnerId = design.OwnerId,
            DesignId = foundShape,
            Kind = "shape",
            Planet = _worlds.Active.LocationId,
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
            CreatedUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _log.Info($"SHAPE REPORT by '{session.State.Name}' ({session.State.PlayerId}): form {foundShape} " +
                  $"'{design.Name}' owned by '{design.OwnerName}' at {cell.X},{cell.Y},{cell.Z} on {_worlds.Active.LocationId}.");
        ForwardContentReport("shape", session, foundShape, design.OwnerId, design.OwnerName,
            _worlds.Active.LocationId, cell.X, cell.Y, cell.Z);
        Send(session, new ServerMessage { Text = "@srv.shape.report_sent" });
    }

    /// <summary>Admin command <c>/shapewipe &lt;Player|#shapeId&gt;</c>: removes the form(s) — every placed
    /// instance everywhere falls back to a plain cube at once (the registry advantage), persisted, broadcast
    /// live, and the id is released for the next designer.</summary>
    private void AdminCustomShapeWipe(PlayerSession session, string? arg)
    {
        string target = (arg ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            Reject(session, "admin", "Usage: /shapewipe Player  or  /shapewipe #shapeId");
            return;
        }

        var toWipe = new List<StoredCustomShape>();
        if (target.StartsWith('#') && int.TryParse(target.AsSpan(1), out int shapeId))
        {
            if (_customShapes.TryGetValue(shapeId, out var one))
            {
                toWipe.Add(one);
            }
        }
        else
        {
            // A player name: online session first, else match the stored owner id verbatim (operators can
            // paste ids from the report log).
            string ownerId = _sessions.Values.FirstOrDefault(s =>
                    s.Joined && string.Equals(s.State.Name, target, System.StringComparison.OrdinalIgnoreCase))
                ?.State.PlayerId ?? target;
            toWipe.AddRange(_customShapes.Values.Where(s => s.OwnerId == ownerId));
        }

        if (toWipe.Count == 0)
        {
            Send(session, new ServerMessage { Text = $"No custom forms found for '{target}'." });
            return;
        }

        foreach (var shape in toWipe)
        {
            _customShapeIdByVoxels.Remove(shape.Voxels);
            _customShapes.Remove(shape.Id);
            _repo.DeleteCustomShape(shape.Id);

            var msg = new CustomShapeData { Id = shape.Id, Voxels = string.Empty, Name = string.Empty, Owner = string.Empty };
            foreach (var viewer in _sessions.Values)
            {
                if (viewer.Joined)
                {
                    Send(viewer, msg);
                }
            }
        }

        Send(session, new ServerMessage { Text = $"Wiped {toWipe.Count} custom form(s)." });
        CheatLog(session.State, $"wiped {toWipe.Count} custom form(s) ({target})");
    }
}
