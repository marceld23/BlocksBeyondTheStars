// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Player-painted block designs (issues #817/#821). A design is a 32×32 palette-index bitmap registered ONCE
/// per save (content-hash dedup) and referenced from painted blocks by id, packed into the shape descriptor's
/// design bits (<see cref="ShapeCode.DesignOf"/>) — so the reference rides the ordinary block-edit +
/// <see cref="BlockChanged"/> paths at zero per-block cost, like the up-face field before it. The bitmap is
/// opaque to the server but strictly validated (exact length + hex charset) because it is persisted and
/// rebroadcast, mirroring the pixel-face hardening. Ids are never reused: a moderation wipe leaves an
/// empty-pixels tombstone so old references go blank instead of pointing at somebody else's later design.
/// Since the hotbar paint action, a design can also live on an ITEM (<c>p&lt;id&gt;</c> in the key, see
/// <see cref="ItemKey.Design"/>): <c>HandlePaintCraft</c> mints such items, placing stamps the id into the
/// cell, and <c>BreakBlockAt</c> recovers a LIVE design back into the drop — the same round trip dye and
/// form make. A wiped (tombstoned) design is dropped at every step, so old references degrade to plain.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>32×32 pixels, one hex char per pixel — the wire/storage size of one design bitmap.</summary>
    internal const int PaintPixelCount = 1024;

    /// <summary>Cap on distinct (non-wiped) designs per save: matches one 64 px-tile client atlas page.</summary>
    internal const int MaxPaintDesigns = 256;

    private readonly Dictionary<int, StoredPaintDesign> _paintDesigns = new();
    private readonly Dictionary<string, int> _paintDesignIdByPixels = new();
    private int _nextPaintDesignId = 1;

    /// <summary>Restores the design registry at server start. Idempotent — clears first. Tombstones (empty
    /// pixels) are kept: they hold their id against reuse but don't count toward the cap or the dedup map.</summary>
    private void LoadPaintDesigns()
    {
        _paintDesigns.Clear();
        _paintDesignIdByPixels.Clear();
        _nextPaintDesignId = 1;
        foreach (var design in _repo.ListPaintDesigns())
        {
            _paintDesigns[design.Id] = design;
            if (design.Pixels.Length != 0)
            {
                _paintDesignIdByPixels[design.Pixels] = design.Id;
            }

            if (design.Id >= _nextPaintDesignId)
            {
                _nextPaintDesignId = design.Id + 1;
            }
        }
    }

    // Same shape of check as IsValidFace: the payload is persisted + rebroadcast, so bound and
    // charset-check it before anything else touches it. Empty = "clear the paint" and is always valid.
    private static bool IsValidPaint(string pixels)
    {
        if (pixels.Length == 0)
        {
            return true;
        }

        if (pixels.Length != PaintPixelCount)
        {
            return false;
        }

        return PaintPayload.IsValidSymbols(pixels);
    }

    /// <summary>Paints (or clears, on empty pixels) a design onto a placed block. The bitmap dedups into the
    /// registry; only the design id changes on the block, through the normal SetBlock + BlockChanged path.</summary>
    private void HandlePaintBlock(PlayerSession session, PaintBlockIntent intent)
    {
        string pixels = (intent.Pixels ?? string.Empty).ToLowerInvariant();
        if (!IsValidPaint(pixels))
        {
            return; // malformed (wrong length or non-hex) — drop it, never persist or relay
        }

        // Same anti-spam as the pixel face: repainting is rare, and each accepted paint can mean a disk
        // save + a world-wide rebroadcast.
        if (_uptime < session.NextPaintAt)
        {
            return;
        }

        var pos = new Vector3i(intent.X, intent.Y, intent.Z);
        if (!WithinReach(session.State, pos))
        {
            Reject(session, "paint", "@srv.paint.out_of_reach");
            return;
        }

        var current = _world.GetBlock(pos);
        if (current.Value == BlockId.AirValue || _world.Definition(current) is not { Solid: true })
        {
            Reject(session, "paint", "@srv.paint.solid_only");
            return;
        }

        // Painting is cosmetic, but graffiti on someone's base or factory is still griefing — the same
        // protection that guards mining/placing guards the paintbrush.
        if (IsFactoryProtected(pos, session.State.PlayerId, session.State.IsAdmin)
            || IsBaseProtected(pos, session.State.PlayerId, session.State.IsAdmin))
        {
            Reject(session, "paint", "@srv.paint.protected");
            return;
        }

        int shape = _world.GetShape(pos);
        int newShape;
        if (pixels.Length == 0)
        {
            newShape = ShapeCode.WithoutDesign(shape);
        }
        else
        {
            if (!TryRegisterPaintDesign(session, pixels, out int designId))
            {
                Send(session, new ServerMessage { Text = "@srv.paint.limit" }); // generic token → localized client-side
                return;
            }

            newShape = ShapeCode.WithDesign(shape, designId);
        }

        if (newShape == shape)
        {
            return; // unchanged (same design, or clearing an unpainted block) — no save, no broadcast
        }

        session.NextPaintAt = _uptime + 2.0;
        var (tint, glow) = _world.GetModifier(pos);
        _world.SetBlock(pos, current, tint, glow, newShape, session.State.PlayerId);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = current.Value, Tint = tint, Glow = glow, Shape = newShape });
    }

    /// <summary>True when a design id is registered AND not a moderation tombstone — the liveness test the
    /// item paths (paint craft, place stamp, mine recovery) gate on, so a wiped design never rides an item.</summary>
    internal bool IsLivePaintDesign(int designId)
        => designId != 0 && _paintDesigns.TryGetValue(designId, out var d) && d.Pixels.Length != 0;

    /// <summary>
    /// The hotbar "own texture" action: apply a saved 32×32 design to a HELD building material — the item-key
    /// sibling of <see cref="HandlePaintBlock"/>, structured like the dye/shape exchanges (free 1:1, output
    /// slot pinned to the invoking hotbar slot). The bitmap dedups into the same save-global registry; the
    /// output item carries only the id (<c>p&lt;xxxx&gt;</c>). Empty pixels strip the design from the item.
    /// Gated on <c>Tintable</c> — paint is a surface cosmetic exactly like dye.
    /// </summary>
    private void HandlePaintCraft(PlayerSession session, PaintCraftIntent intent)
    {
        string pixels = (intent.Pixels ?? string.Empty).ToLowerInvariant();
        if (!IsValidPaint(pixels))
        {
            return; // malformed (wrong length or non-hex) — drop it, never persist or relay
        }

        string baseKey = ItemKey.Base(intent.SourceItemKey);
        var item = _content.GetItem(baseKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            CraftFail(session, "paint", "@srv.craft.tint_item");
            return;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null || !blockDef.Tintable)
        {
            CraftFail(session, "paint", "@srv.craft.tint_material");
            return;
        }

        int designId = 0;
        if (pixels.Length != 0)
        {
            // The block-paint anti-spam only bites when this bitmap would actually REGISTER (disk write +
            // world-wide broadcast); re-applying a known design is as cheap as any craft.
            bool isNew = !_paintDesignIdByPixels.ContainsKey(pixels);
            if (isNew && _uptime < session.NextPaintAt)
            {
                return;
            }

            if (!TryRegisterPaintDesign(session, pixels, out designId))
            {
                Send(session, new ServerMessage { Text = "@srv.paint.limit" });
                return;
            }

            if (isNew)
            {
                session.NextPaintAt = _uptime + 2.0;
            }
        }

        // Re-applying the design the item already carries (incl. "none" on an unpainted one) is a no-op.
        if (designId == ItemKey.Design(intent.SourceItemKey))
        {
            CraftFail(session, "paint", "@srv.craft.same_design");
            return;
        }

        int count = System.Math.Clamp(intent.Count, 1, ItemDefinition.DefaultMaxStack);
        // Only the design changes — keep whatever colour/form the source carried.
        string output = ItemKey.Compose(baseKey, ItemKey.Tint(intent.SourceItemKey),
            ItemKey.Glow(intent.SourceItemKey), ItemKey.Shape(intent.SourceItemKey), designId);

        // Creative mode: no material cost — just produce the painted material.
        if (!Rules.CraftingCostsMaterials)
        {
            var freePool = new MaterialPool(_content, session.State, _ship);
            AddCraftOutput(session, freePool, output, count, intent.Slot);
            Send(session, new CraftResult { Success = true, RecipeKey = "paint" });
            SendInventory(session);
            WarnIfPoolOverflowed(session, freePool); // #600
            return;
        }

        var pool = new MaterialPool(_content, session.State, _ship);
        var inputs = new List<ItemAmount> { new ItemAmount(intent.SourceItemKey, count) };
        if (!pool.Has(inputs))
        {
            CraftFail(session, "paint", "@srv.craft.missing_material");
            return;
        }

        // 1:1 exchange — the room the consumed source frees up counts (see the dye/shape twins).
        if (!pool.CanFitAfterRemoving(inputs, new[] { new ItemAmount(output, count) }))
        {
            CraftFail(session, "paint", "@inventory_full");
            return;
        }

        pool.Remove(inputs);
        AddCraftOutput(session, pool, output, count, intent.Slot);
        Send(session, new CraftResult { Success = true, RecipeKey = "paint" });
        SendInventory(session);
        WarnIfPoolOverflowed(session, pool); // #600: painting PART of a stack needs a fresh slot
        ShipAiOnCraft(session);
    }

    /// <summary>Resolves pixels to an existing design id or registers a new one (persist + broadcast to every
    /// joined session — the registry is save-global, painted blocks may be on any world). False = cap hit.</summary>
    private bool TryRegisterPaintDesign(PlayerSession session, string pixels, out int designId)
    {
        if (_paintDesignIdByPixels.TryGetValue(pixels, out designId))
        {
            return true;
        }

        if (_paintDesignIdByPixels.Count >= MaxPaintDesigns || _nextPaintDesignId > ShapeCode.MaxDesignId)
        {
            designId = 0;
            return false;
        }

        designId = _nextPaintDesignId++;
        var design = new StoredPaintDesign
        {
            Id = designId,
            OwnerId = session.State.PlayerId,
            OwnerName = session.State.Name,
            Pixels = pixels,
        };
        _paintDesigns[designId] = design;
        _paintDesignIdByPixels[pixels] = designId;
        _repo.SavePaintDesign(design);

        var msg = new PaintDesignData { Id = designId, Pixels = pixels, Owner = design.OwnerName };
        foreach (var viewer in _sessions.Values)
        {
            if (viewer.Joined)
            {
                Send(viewer, msg);
            }
        }

        return true;
    }

    /// <summary>Pushes the full design registry to a newcomer — before any chunk can arrive, so painted
    /// blocks in the first streamed chunks resolve immediately. Tombstones are skipped (they render blank
    /// by absence).</summary>
    private void SendPaintDesigns(PlayerSession session)
    {
        var live = _paintDesigns.Values.Where(d => d.Pixels.Length != 0).ToList();
        if (live.Count == 0)
        {
            return;
        }

        Send(session, new PaintDesignList
        {
            Ids = live.Select(d => d.Id).ToArray(),
            Pixels = live.Select(d => d.Pixels).ToArray(),
            Owners = live.Select(d => d.OwnerName).ToArray(),
        });
    }

    /// <summary>Chat command <c>/report</c> (paint moderation v1): records the nearest painted block within a
    /// few metres of the reporter — design id, owner, position — as a persisted row + a server log line for
    /// the operator. No client UI beyond typing the command.</summary>
    private void HandlePaintReport(PlayerSession session)
    {
        const int radius = 6;
        var centre = new Vector3i(
            (int)System.Math.Floor(session.State.Position.X),
            (int)System.Math.Floor(session.State.Position.Y),
            (int)System.Math.Floor(session.State.Position.Z));

        Vector3i? found = null;
        int foundDesign = 0;
        double bestDistSq = double.MaxValue;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    var p = new Vector3i(centre.X + dx, centre.Y + dy, centre.Z + dz);
                    int design = ShapeCode.DesignOf(_world.GetShape(p));
                    if (design == 0)
                    {
                        continue;
                    }

                    double distSq = dx * dx + dy * dy + dz * dz;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        found = p;
                        foundDesign = design;
                    }
                }
            }
        }

        if (found is not { } cell)
        {
            Send(session, new ServerMessage { Text = "@srv.paint.report_none" });
            return;
        }

        string owner = _paintDesigns.TryGetValue(foundDesign, out var d) ? d.OwnerId : string.Empty;
        string ownerName = d?.OwnerName ?? string.Empty;
        _repo.SavePaintReport(new StoredPaintReport
        {
            ReporterId = session.State.PlayerId,
            OwnerId = owner,
            DesignId = foundDesign,
            Planet = _worlds.Active.LocationId,
            X = cell.X,
            Y = cell.Y,
            Z = cell.Z,
            CreatedUnix = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        _log.Info($"PAINT REPORT by '{session.State.Name}' ({session.State.PlayerId}): design {foundDesign} " +
                  $"owned by '{owner}' at {cell.X},{cell.Y},{cell.Z} on {_worlds.Active.LocationId}.");
        ForwardContentReport("paint", session, foundDesign, owner, ownerName,
            _worlds.Active.LocationId, cell.X, cell.Y, cell.Z);
        Send(session, new ServerMessage { Text = "@srv.paint.report_sent" });
    }

    /// <summary>Admin command <c>/paintwipe &lt;Player|#designId&gt;</c>: tombstones the design(s) — every placed
    /// instance everywhere goes blank at once (the registry advantage), persisted, broadcast live. Block cells
    /// keep their now-dangling design bits; a dangling id renders as the plain block texture.</summary>
    private void AdminPaintWipe(PlayerSession session, string? arg)
    {
        string target = (arg ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            Reject(session, "admin", "@srv.admin.usage_paintwipe");
            return;
        }

        var toWipe = new List<StoredPaintDesign>();
        if (target.StartsWith('#') && int.TryParse(target.AsSpan(1), out int designId))
        {
            if (_paintDesigns.TryGetValue(designId, out var one) && one.Pixels.Length != 0)
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
            toWipe.AddRange(_paintDesigns.Values.Where(d => d.Pixels.Length != 0 && d.OwnerId == ownerId));
        }

        if (toWipe.Count == 0)
        {
            Send(session, new ServerMessage { Text = "@srv.admin.paint_none:" + target });
            return;
        }

        foreach (var design in toWipe)
        {
            _paintDesignIdByPixels.Remove(design.Pixels);
            design.Pixels = string.Empty; // tombstone: id stays claimed, bitmap gone
            _repo.SavePaintDesign(design);

            var msg = new PaintDesignData { Id = design.Id, Pixels = string.Empty };
            foreach (var viewer in _sessions.Values)
            {
                if (viewer.Joined)
                {
                    Send(viewer, msg);
                }
            }
        }

        Send(session, new ServerMessage { Text = "@srv.admin.paint_wiped:" + toWipe.Count });
        CheatLog(session.State, $"wiped {toWipe.Count} paint design(s) ({target})");
    }
}
