// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Persistence;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// The farms at a grown settlement's edge (#3595–#3597):
/// <list type="bullet">
/// <item><b>Fields</b> — a crop harvested on a field regrows after <see cref="FieldRegrowSeconds"/> (a greenhouse bed and
/// wild flora keep their own clocks): a field is larger than a bed, and a free food source beside every village must not
/// replace farming.</item>
/// <item><b>Pens</b> — two or three tame animals of a passive land species of the world stand in every pen, like the tamer's
/// pet: owned by the pen (<see cref="PenOwnerPrefix"/>), so the town rule leaves them be and no player's roster lists them;
/// they amble about the pen and are put back in when they wander out.</item>
/// <item><b>Products</b> — every <see cref="PenProductSeconds"/> of uptime a pen lays one product of its animals (an egg from
/// a winged or two-legged kind, milk from a large one, wool from the rest) into its crate, up to
/// <see cref="PenProductCap"/>; players collect them like from any crate.</item>
/// </list>
/// Rebuilt from the settlements' markers on every load; the pens' animals live for the server run.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>How long a field crop takes to grow back (a greenhouse bed: 30 s).</summary>
    internal const double FieldRegrowSeconds = 240.0;

    /// <summary>How often a pen lays a product, in seconds of uptime.</summary>
    internal const double PenProductSeconds = 180.0;

    /// <summary>The most products a pen's crates hold before it stops laying.</summary>
    internal const int PenProductCap = 12;

    /// <summary>A pen's animal carries this owner prefix + the pen's key — never a player id.</summary>
    internal const string PenOwnerPrefix = "pen:";

    /// <summary>One field or pen of an intact planned settlement: its area (world columns, feet level), its key, and for a
    /// pen its product crates and the next time it lays.</summary>
    internal sealed class SettlementFarmArea
    {
        public string Key { get; init; } = string.Empty;
        public int MinX { get; init; }
        public int MinZ { get; init; }
        public int W { get; init; }
        public int L { get; init; }
        public int FeetY { get; init; }
        public List<Vector3i> Boxes { get; } = new();
        public double NextProductAt { get; set; }

        public bool Contains(int x, int z, int circ, int grow = 0)
        {
            int dx = WorldConstants.WrapDeltaX(x - MinX, circ);
            return dx >= -grow && dx < W + grow && z >= MinZ - grow && z < MinZ + L + grow;
        }
    }

    private bool IsPenAnimal(CombatEntity c) => c.OwnerId.StartsWith(PenOwnerPrefix, System.StringComparison.Ordinal);

    /// <summary>Whether a cell lies on a field of an intact planned settlement.</summary>
    private bool InSettlementField(Vector3i pos)
    {
        int circ = _world.Circumference;
        foreach (var f in _worlds.Active.SettlementFields)
        {
            if (f.Contains(pos.X, pos.Z, circ) && System.Math.Abs(pos.Y - f.FeetY) <= 2)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Reads the fields, pens and product crates of the intact settlements from their markers, and gives every
    /// product crate its container.</summary>
    private void RegisterSettlementFarms()
    {
        var fields = _worlds.Active.SettlementFields;
        var pens = _worlds.Active.SettlementPens;
        fields.Clear();
        pens.Clear();
        foreach (var s in _settlements.Where(s => !s.Ruined))
        {
            foreach (var (type, pos, data) in s.DataMarkers)
            {
                if (type == SettlementPlanBaker.FieldMarker || type == SettlementPlanBaker.PenMarker)
                {
                    var area = FarmArea(pos, data);
                    (type == SettlementPlanBaker.FieldMarker ? fields : pens).Add(area);
                }
            }

            foreach (var (type, pos) in s.Markers.Where(m => m.Type == SettlementPlanBaker.PenBoxMarker))
            {
                var cell = WorldConstants.CanonicalBlock(pos.ToBlock(), _world.Circumference);
                if (pens.FirstOrDefault(p => p.Contains(cell.X, cell.Z, _world.Circumference, grow: 2)) is { } pen)
                {
                    pen.Boxes.Add(cell);
                    EnsurePenBox(cell);
                }
            }
        }
    }

    private static SettlementFarmArea FarmArea(Vector3f pos, string data)
    {
        var settings = MarkerSettings.Parse(data);
        int x = (int)System.Math.Floor(pos.X), z = (int)System.Math.Floor(pos.Z);
        return new SettlementFarmArea
        {
            Key = $"{x}:{z}",
            MinX = x,
            MinZ = z,
            W = System.Math.Max(1, settings.GetInt("w", 1)),
            L = System.Math.Max(1, settings.GetInt("l", 1)),
            FeetY = (int)System.Math.Floor(pos.Y),
        };
    }

    /// <summary>A product crate's container (kind <c>crate</c>, so players open it like any crate) — added once, when the
    /// crate block stands and no container does.</summary>
    private void EnsurePenBox(Vector3i cell)
    {
        if (ContainerAt(cell) is not null || !ContainerSystem.IsContainerBlock(_content.BlockById(_world.GetBlock(cell))?.Key ?? string.Empty))
        {
            return;
        }

        AddContainer(new StoredContainer
        {
            Id = $"penbox_{_world.LocationId}_{cell.X}_{cell.Y}_{cell.Z}",
            Planet = _world.LocationId,
            Kind = "crate",
            Position = cell,
        });
    }

    /// <summary>Once a second: every pen keeps its animals and lays its products.</summary>
    private void TickSettlementFarms()
    {
        var pens = _worlds.Active.SettlementPens;
        if (pens.Count == 0)
        {
            return;
        }

        bool changed = false;
        foreach (var pen in pens)
        {
            changed |= SyncPenAnimals(pen);
            LayPenProduct(pen);
        }

        if (changed)
        {
            BroadcastCreatures();
        }
    }
}
