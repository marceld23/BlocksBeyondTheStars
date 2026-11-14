// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client.Core;

/// <summary>
/// #3612: puts a structure design that came in PARTS back together. A giant wreck's hull is far bigger than one network
/// message may be (1 MB), so the server cuts its <see cref="SpaceShipDesign"/> into parts of one
/// <see cref="SpaceShipDesign.Serial"/> (<see cref="SpaceShipDesign.PartIndex"/> of <see cref="SpaceShipDesign.PartCount"/>);
/// the flight view hands every design message to <see cref="Add"/> and builds the hull only from what it returns:
/// <list type="bullet">
/// <item>a whole design (<c>PartCount</c> 0 or 1 — every older server's, and every design that fits one message) comes back
/// at once, unchanged;</item>
/// <item>the parts of a design are kept per structure id until all of them arrived — in any order — and then come back as one
/// whole design: the header (and the doors, the hatch) of part 0, the cells of all parts in part order;</item>
/// <item>a design with a newer serial replaces an unfinished older one of the same structure, and a part of a serial older
/// than the one being collected is dropped (out of date). A malformed part (arrays of different lengths, an index out of
/// range) is dropped.</item>
/// </list>
/// Unity-free; call <see cref="Clear"/> when the flight view closes or the connection is new (the serials restart with a
/// server), <see cref="Forget"/> when a structure leaves.
/// </summary>
public sealed class SpaceDesignAssembler
{
    /// <summary>The most parts one design may announce — far above a 256-block hull's shell, and a guard against a damaged
    /// message making the client allocate without bound.</summary>
    public const int MaxParts = 1024;

    private sealed class Pending
    {
        public int Serial;
        public SpaceShipDesign?[] Parts = Array.Empty<SpaceShipDesign?>();
        public int Received;
    }

    private readonly Dictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>How many structures have a design half arrived.</summary>
    public int PendingCount => _pending.Count;

    /// <summary>Takes one received design message; returns the whole design when it is complete (a single-part design at
    /// once, a parted one when its last part arrived), else null.</summary>
    public SpaceShipDesign? Add(SpaceShipDesign part)
    {
        string id = part.Id ?? string.Empty;
        _pending.TryGetValue(id, out var pending);
        if (part.PartCount <= 1)
        {
            if (pending is not null && part.Serial != 0 && part.Serial < pending.Serial)
            {
                return null; // older than the design being collected
            }

            _pending.Remove(id);
            return part;
        }

        if (part.PartCount > MaxParts || part.PartIndex < 0 || part.PartIndex >= part.PartCount || !CellsConsistent(part))
        {
            return null;
        }

        if (pending is not null && part.Serial < pending.Serial)
        {
            return null; // a part of a design already replaced
        }

        if (pending is null || part.Serial > pending.Serial || pending.Parts.Length != part.PartCount)
        {
            pending = new Pending { Serial = part.Serial, Parts = new SpaceShipDesign?[part.PartCount] };
            _pending[id] = pending;
        }

        if (pending.Parts[part.PartIndex] is null)
        {
            pending.Parts[part.PartIndex] = part;
            pending.Received++;
        }

        if (pending.Received < pending.Parts.Length)
        {
            return null;
        }

        _pending.Remove(id);
        return Join(pending.Parts!);
    }

    /// <summary>Drops a structure's unfinished design (it left the flight).</summary>
    public void Forget(string id) => _pending.Remove(id ?? string.Empty);

    /// <summary>Drops every unfinished design (the flight view closed, or the connection is new).</summary>
    public void Clear() => _pending.Clear();

    private static bool CellsConsistent(SpaceShipDesign d)
    {
        int n = d.Block?.Length ?? 0;
        return (d.X?.Length ?? 0) == n && (d.Y?.Length ?? 0) == n && (d.Z?.Length ?? 0) == n
               && ExtraConsistent(d.Tint, n) && ExtraConsistent(d.Glow, n) && ExtraConsistent(d.Shape, n);
    }

    /// <summary>A dye, glow or shape array is either empty (no modifiers in this part) or one value per cell.</summary>
    private static bool ExtraConsistent(int[]? values, int n) => values is null || values.Length == 0 || values.Length == n;

    /// <summary>One whole design: part 0's header, doors and hatch; every part's cells in part order. The dye, glow and shape
    /// arrays are filled for every cell when any part carries them (0 where a part had none).</summary>
    private static SpaceShipDesign Join(SpaceShipDesign[] parts)
    {
        int total = 0;
        bool tint = false, glow = false, shape = false;
        foreach (var p in parts)
        {
            total += p.Block?.Length ?? 0;
            tint |= p.Tint is { Length: > 0 };
            glow |= p.Glow is { Length: > 0 };
            shape |= p.Shape is { Length: > 0 };
        }

        var whole = Header(parts[0]);
        whole.X = new int[total];
        whole.Y = new int[total];
        whole.Z = new int[total];
        whole.Block = new ushort[total];
        whole.Tint = tint ? new int[total] : Array.Empty<int>();
        whole.Glow = glow ? new int[total] : Array.Empty<int>();
        whole.Shape = shape ? new int[total] : Array.Empty<int>();
        int at = 0;
        foreach (var p in parts)
        {
            int n = p.Block?.Length ?? 0;
            if (n == 0)
            {
                continue;
            }

            Array.Copy(p.X, 0, whole.X, at, n);
            Array.Copy(p.Y, 0, whole.Y, at, n);
            Array.Copy(p.Z, 0, whole.Z, at, n);
            Array.Copy(p.Block, 0, whole.Block, at, n);
            CopyExtra(p.Tint, whole.Tint, at, n);
            CopyExtra(p.Glow, whole.Glow, at, n);
            CopyExtra(p.Shape, whole.Shape, at, n);
            at += n;
        }

        return whole;
    }

    private static void CopyExtra(int[]? from, int[] to, int at, int n)
    {
        if (to.Length > 0 && from is { Length: > 0 })
        {
            Array.Copy(from, 0, to, at, n);
        }
    }

    /// <summary>A fresh message with part 0's header — the input messages are never changed (a loopback hands the same
    /// objects to every listener).</summary>
    private static SpaceShipDesign Header(SpaceShipDesign p) => new()
    {
        Id = p.Id,
        Kind = p.Kind,
        PosX = p.PosX,
        PosY = p.PosY,
        PosZ = p.PosZ,
        Width = p.Width,
        Height = p.Height,
        Length = p.Length,
        HasDock = p.HasDock,
        DockX = p.DockX,
        DockY = p.DockY,
        DockZ = p.DockZ,
        DockOutX = p.DockOutX,
        DockOutZ = p.DockOutZ,
        DoorX = p.DoorX,
        DoorY = p.DoorY,
        DoorZ = p.DoorZ,
        DoorKind = p.DoorKind,
        DoorAxisX = p.DoorAxisX,
        HatchX = p.HatchX,
        HatchY = p.HatchY,
        HatchZ = p.HatchZ,
        HatchKnown = p.HatchKnown,
        OwnerId = p.OwnerId,
        Serial = p.Serial,
        PartIndex = 0,
        PartCount = 1,
    };
}
