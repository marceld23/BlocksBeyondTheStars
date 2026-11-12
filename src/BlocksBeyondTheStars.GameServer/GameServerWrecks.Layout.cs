// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// #3521: the planet wreck built from its ship's real layout (terrain generation 38). A fresh wreck draws its design by hash
/// from the layout-wreck pool (<see cref="WreckGenerator.LayoutDesigns"/>: shipped ships with a layout that fits the crash
/// site, then user ships that allow wrecks and fit a landing pad), centres the hull on the crash-site anchor and records the
/// ship key as the template plus <c>src=layout;rev=1;fp=&lt;layout hash&gt;</c> as the kit layout. A record with an empty
/// template is a classic box wreck and replays as one. When the recorded layout is gone or changed, the blocks stay where
/// they are but there is no mask to repair to: repair and claim are refused (<c>srv.wreck.unknown_design</c>).
/// </summary>
public sealed partial class GameServer
{
    /// <summary>The kit-layout source tag of a wreck built from a ship layout.</summary>
    private const string WreckLayoutSource = "layout";

    /// <summary>What a body's wreck is built from and how it is recorded. <see cref="Structure"/> is null for a layout wreck
    /// whose layout is gone or changed; <see cref="Template"/> is the ship key of a layout wreck, empty for the box.</summary>
    private sealed record WreckPlan(ShipDefinition? Design, WreckStructure? Structure, string Origin, string Template, string KitLayout)
    {
        public bool FromLayout => Template.Length > 0;
    }

    /// <summary>The wreck of this body: a recorded layout wreck replays from its layout; a fresh wreck of a generation-38
    /// world is built from a drawn layout (the box when the pool is empty); everything else is the classic box of the drawn
    /// design (the recorded one where the record names it, #3513).</summary>
    private WreckPlan PlanWreck(StructurePlacementRecord? pinned, ShipDefinition drawn, long seed)
    {
        if (pinned is { Placed: true, Template.Length: > 0 })
        {
            return ReplayLayoutWreck(pinned, seed);
        }

        if (pinned is not { Placed: true } && _generator.TerrainGeneration >= WorldDescription.StructurePipelineGeneration
            && LayoutWreckPick() is { } pick)
        {
            var built = WreckGenerator.GenerateFromLayout(pick.Design, pick.Layout, seed, _content);
            return new WreckPlan(pick.Design, built, built.Origin, pick.Design.Key, WreckLayoutPin(pick.Layout));
        }

        var design = PinnedWreckDesign(pinned, drawn);
        var box = WreckGenerator.Generate(design, seed, _content);
        return new WreckPlan(design, box, box.Origin, string.Empty, string.Empty);
    }

    /// <summary>The layout a fresh wreck of this body is built from — a hash of seed and body over the layout-wreck pool, null
    /// when the pool is empty. A user ship joins only from a pack the world enables. The crash-site reservation (#3514) is
    /// sized by the same pick.</summary>
    private WreckDesign? LayoutWreckPick()
        => WreckGenerator.PickDesign(WreckGenerator.LayoutDesigns(_content, claimable: true, _meta.Description.EnabledStructurePacks),
            StableHashing.Mixed("wreck:design:" + _world.LocationId, _meta.Seed));

    /// <summary>The kit-layout line a layout wreck is recorded with: source, revision and the layout's fingerprint.</summary>
    private static string WreckLayoutPin(ShipLayout layout)
        => new MarkerSettings()
            .Set("src", WreckLayoutSource)
            .Set("rev", WreckGenerator.LayoutRevision)
            .Set("fp", WreckGenerator.LayoutFingerprint(layout))
            .ToString();

    /// <summary>A recorded layout wreck, rebuilt from its layout — or, when the ship or its layout is gone, the layout
    /// changed (another fingerprint) or the record comes from a newer revision, without a structure: the blocks stay, the
    /// repair and the claim are off, and the log says why.</summary>
    private WreckPlan ReplayLayoutWreck(StructurePlacementRecord rec, long seed)
    {
        var pin = MarkerSettings.Parse(rec.KitLayout);
        var design = _content.GetShip(rec.Template);
        var layout = design is null ? null : _content.GetShipLayout(design.Layout);
        string why = design is null ? "the ship is no longer in the content"
            : layout is null || layout.Cells.Count == 0 ? "its layout is gone"
            : pin.Get("src") != WreckLayoutSource || pin.GetInt("rev", 0) != WreckGenerator.LayoutRevision
                ? $"the record '{rec.KitLayout}' is not one this build can rebuild"
            : pin.Get("fp") != WreckGenerator.LayoutFingerprint(layout) ? "its layout has changed since"
            : string.Empty;
        if (why.Length > 0)
        {
            _log.Warn($"The wreck on '{_world.LocationId}' was built from the layout of '{rec.Template}', but {why} — its blocks stay where they are; it can no longer be repaired or claimed.");
            return new WreckPlan(design, null, WreckGenerator.OriginOf(rec.Template, seed), rec.Template, rec.KitLayout);
        }

        var built = WreckGenerator.GenerateFromLayout(design!, layout!, seed, _content);
        return new WreckPlan(design, built, built.Origin, rec.Template, rec.KitLayout);
    }

    /// <summary>
    /// Where a layout wreck stands: the pinned origin and ground height, or — fresh — its hull centred on the crash-site
    /// anchor (a fixed offset from landing pad 0, nudged off any settlement), moved to dry ground when the footprint stands
    /// in water or sea ice (#2756). Returns the hull's origin corner and the ground height it is sunk into.
    /// </summary>
    private (int X, int Z, int GroundY) LayoutWreckSite(PlanetType planet, StructurePlacementRecord? pinned, int w, int l)
    {
        if (pinned is { Placed: true })
        {
            return (pinned.X, pinned.Z, pinned.GroundY);
        }

        var (ax, az) = WreckAnchorFor(_landingPads);
        for (int nudge = 0; nudge < 12 && WreckSiteBlocked(ax, az, System.Math.Max(w, l) / 2); nudge++)
        {
            ax = WorldConstants.WrapX(ax - 24, _world.Circumference);
            az += 16;
        }

        int ox = ax - (w / 2), oz = az - (l / 2);
        int? restY = null;
        if (SeaIceIsWet && FootprintWet(planet, ox, oz, w, l))
        {
            (ox, oz, restY) = DryWreckSite(planet, ox, oz, w, l);
        }

        return (ox, oz, restY ?? _generator.SurfaceHeight(planet, ox + (w / 2), oz + (l / 2)));
    }

    /// <summary>Test seam (#3521): the active world's wreck structure — null without a wreck, or for a layout wreck whose
    /// layout is gone or changed.</summary>
    public WreckStructure? WreckForTest => _wreck;

    /// <summary>Test seam (#3521): the flight hull of a ship type as an NPC flies it (the server's ship voxel pipeline).</summary>
    internal SpaceStructure BuildNpcShipStructureForTest(string shipTypeKey) => BuildNpcShipStructure("test:" + shipTypeKey, shipTypeKey);

    /// <summary>Test seam (#3521): the pinned space-wreck designs (body id → ship key).</summary>
    public IReadOnlyDictionary<string, string> SpaceWreckDesignsForTest => _meta.SpaceWreckDesigns;

    /// <summary>The dye, glow and shape a repaired wreck cell takes (#3521; all 0 for the box wreck).</summary>
    private (int Tint, int Glow, int Shape) WreckIntactLook(Vector3i world)
    {
        if (_wreck is null)
        {
            return (0, 0, 0);
        }

        int lx = WorldConstants.WrapDeltaX(world.X - _wreckOrigin.X, _world.Circumference);
        int ly = world.Y - _wreckOrigin.Y;
        int lz = world.Z - _wreckOrigin.Z;
        if (!_wreck.InBounds(lx, ly, lz))
        {
            return (0, 0, 0);
        }

        var (tint, glow) = _wreck.IntactModifierAt(lx, ly, lz);
        return (tint, glow, _wreck.IntactShapeAt(lx, ly, lz));
    }
}
