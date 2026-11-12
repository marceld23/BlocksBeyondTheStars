// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using BlocksBeyondTheStars.WorldGeneration;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Stamps a rare crashed-ship wreck (see <see cref="WreckGenerator"/>) onto the start planet's
/// surface, away from the landing zone and any settlement. Whether a planet has one is derived
/// deterministically from the world seed + planet, so wrecks are uncommon. The decayed hull is
/// stamped slightly sunk into the ground (half-buried crash pose). Wrecks are <b>not</b> protected
/// — they're left scavengeable; their loot/module/data-terminal markers become interaction points,
/// and the intact-hull repair mask is kept so the wreck can later be rebuilt into a flyable ship.
/// </summary>
public sealed partial class GameServer
{
    private bool _wreckStamped { get => _worlds.Active.WreckStamped; set => _worlds.Active.WreckStamped = value; }
    private Vector3i _wreckOrigin { get => _worlds.Active.WreckOrigin; set => _worlds.Active.WreckOrigin = value; }
    private WreckStructure? _wreck { get => _worlds.Active.Wreck; set => _worlds.Active.Wreck = value; }
    private string _wreckName { get => _worlds.Active.WreckName; set => _worlds.Active.WreckName = value; }
    private bool _wreckClaimed { get => _worlds.Active.WreckClaimed; set => _worlds.Active.WreckClaimed = value; }
    private List<(string Type, Vector3f Pos)> _wreckMarkers => _worlds.Active.WreckMarkers;

    /// <summary>Interaction points inside the stamped wreck (loot / module / data_terminal).</summary>
    public IReadOnlyList<(string Type, Vector3f Pos)> WreckMarkers => _wreckMarkers;

    /// <summary>Name of the stamped wreck (empty if none on this world).</summary>
    public string WreckName => _wreckName;

    /// <summary>Whether a wreck was stamped on this world.</summary>
    public bool HasWreck => _wreckStamped;

    /// <summary>Remaining hull cells that differ from the wreck's intact repair mask.</summary>
    public int WreckRepairRemaining => CountWreckRepairRemaining();

    /// <summary>Total hull cells in the intact repair mask.</summary>
    public int WreckRepairTotal => _wreck?.IntactHullCount() ?? 0;

    /// <summary>Test/diagnostic: the stamped wreck's world origin (#1684).</summary>
    public Vector3i WreckOriginForTest => _wreckOrigin;

    /// <summary>Whether the stamped wreck has already been claimed into the owned fleet.</summary>
    public bool WreckClaimed => _wreckClaimed;

    /// <summary>World-space cells that still need repair, with the required block key.</summary>
    public IReadOnlyList<(Vector3i Pos, string BlockKey)> WreckRepairCells()
    {
        var result = new List<(Vector3i Pos, string BlockKey)>();
        if (!_wreckStamped || _wreck is null)
        {
            return result;
        }

        foreach (var (pos, block) in EnumerateWreckRepairCells())
        {
            result.Add((pos, block.Key));
        }

        return result;
    }

    private void StampWreck()
    {
        var planet = _world.Planet;

        long wSeed = _meta.Seed ^ WorldGenerator.StableHash("wreck:" + _world.LocationId); // per body (#478)
        var rng = new System.Random(unchecked((int)(wSeed ^ (wSeed >> 32))));

        // World options: the chosen wreck frequency scales the per-world chance (Off ⇒ none).
        double factor = _meta.Description.PlanetWrecks.StructureFactor();
        bool rolled = rng.NextDouble() <= WreckChance(planet) * factor;

        // #3513: whether the body has a wreck is pinned like every count (drawn above as always) — and a wreck that has its
        // record stands whatever the roll says now. The option switched off still means none.
        var pinned = FindPlacementRecord("wreck", 0);
        bool exists = PinnedCount("wreck", rolled ? 1 : 0) > 0 || pinned is { Placed: true };
        if (!exists || factor <= 0)
        {
            return; // no wreck on this world
        }

        // Pick a ship design to have crashed (any in content; weighted to the smaller hulls).
        var designs = _content.Ships.Values.ToList();
        if (designs.Count == 0)
        {
            return;
        }

        // #3513: the pick is drawn as always (the name and the loot follow it in the stream); a recorded design replaces it —
        // the list is in dictionary order, so a new ship moved the pick under an existing wreck. #3521: a generation-38
        // wreck is built from a ship layout instead (PlanWreck); a record with an empty template keeps the classic box.
        var drawn = designs[rng.Next(designs.Count)];
        var plan = PlanWreck(pinned, drawn, wSeed);
        var structure = plan.Structure;

        // #1684: where the wreck stands is PINNED like every other structure. It used to be re-derived from
        // landing pad 0 on every world load while its blocks were written exactly once — and pads are not
        // persisted, only the rule that recomputes them is. Any change to that rule (the ocean-pad wave
        // changed it twice inside one release) would have slid the origin, the markers and the repair mask off
        // the blocks lying in the world, and every repair on a visible wreck cell would answer "not part of
        // the repair mask".
        var (ax, az, groundY) = plan.FromLayout
            ? LayoutWreckSite(planet, pinned, structure?.Width ?? 1, structure?.Length ?? 1)
            : ClassicWreckSite(planet, pinned, structure!);
        // Half-buried crash pose: sink the hull one block into the ground (a layout wreck's decay spares the rows up to
        // WreckGenerator.PlanetGroundRow, the one at the surface).
        int baseY = groundY - (plan.FromLayout ? WreckGenerator.PlanetGroundRow : 1);
        _wreckOrigin = new Vector3i(ax, baseY, az);

        // Stamp only the wreck's solid blocks (breaches stay as the existing terrain/air) — and only ONCE
        // per world (#467): on re-entry the runtime state (structure, markers, name) is re-derived from the
        // same deterministic rolls, but the blocks are the player's now — repairs stay repaired, mined
        // hull stays mined, nothing resurrects.
        bool write = !FeatureStamped("wreck") && structure is not null;
        if (write)
        {
            StampWreckBlocks(structure!, ax, baseY, az);
        }

        _wreck = structure; // #3521: null for a layout wreck whose layout is gone or changed — its repair and claim are off
        _wreckName = WreckDisplayName(plan.Origin, plan.Design ?? drawn, rng);
        // #466: the claim is persisted — travelling away and back no longer resets it (that minted a free
        // ship per lap: repair once, then re-claim on every re-entry).
        _wreckClaimed = _meta.Claims.Exists(c => c.Key == _world.LocationId + "|wreck");

        _wreckMarkers.Clear();
        foreach (var m in structure?.Markers ?? System.Array.Empty<WreckMarker>())
        {
            var pos = new Vector3f(ax + m.LocalPos.X + 0.5f, baseY + m.LocalPos.Y + 0.5f, az + m.LocalPos.Z + 0.5f);
            _wreckMarkers.Add((m.Type, pos));

            // Loot caches, the recoverable module and the data terminal are all scavengeable.
            if (m.Type is "loot" or "module" or "data_terminal")
            {
                SpawnStructureLoot("wreck", m.Type, pos, rng);
            }
        }

        _wreckStamped = true;
        RegisterWreckFootprint(structure, ax, az); // #3514: the hull's box replaces the crash-site reservation
        // Pin it — on the first stamp, and once for a save whose wreck predates pinning (it keeps exactly the
        // spot the current rule derives, which is where its blocks are: the pre-generation-2 pad rule was
        // frozen by #1665 precisely so existing pads never move).
        if (pinned is not { Placed: true })
        {
            RecordPlacement("wreck", 0, _wreckOrigin, groundY, onIsland: false, seat: "crash", name: _wreckName,
                template: plan.Template, kitLayout: plan.KitLayout);
            SavePlacementRecords();
        }

        if (plan.Design is { } design)
        {
            PinWreckDesign(design.Key); // #3513: once — at the first stamp, or on the next load of an older record
        }

        PinWreckBox(structure); // #3514: the crash site the later loads reserve is the box that stands, not a fresh pick

        if (write)
        {
            MarkFeatureStamped("wreck");
            _log.Info($"Wreck '{_wreckName}' ({structure!.Origin} {structure.ShipType}{(plan.FromLayout ? ", from its layout" : string.Empty)}) stamped at ({ax}, {baseY}, {az}) with {_wreckMarkers.Count} markers, {structure.BreachCount()} breaches.");
        }
    }

    /// <summary>
    /// Where a classic box wreck stands (the rule as it always was): the pinned spot, or a fixed offset from landing pad 0
    /// nudged off any settlement, moved to dry ground when the footprint stands in water or sea ice (#2756). The box
    /// extends +X/+Z from that spot. Returns the spot and the ground height the hull is sunk into.
    /// </summary>
    private (int X, int Z, int GroundY) ClassicWreckSite(Shared.Definitions.PlanetType planet, Shared.State.StructurePlacementRecord? pinned,
        WreckStructure structure)
    {
        bool seaIceIsWet = SeaIceIsWet; // #2756: from generation 27 the crash site is never under water or sea ice
        int ax, az;
        int? restY = null;
        if (pinned is { Placed: true })
        {
            ax = pinned.X;
            az = pinned.Z;
            if (seaIceIsWet)
            {
                restY = pinned.GroundY; // a generation-27 wreck may rest on the ice: its pinned height, not the seabed
            }
        }
        else
        {
            // Anchor offset from the first landing pad — its zone is reserved during settlement placement so the
            // two never overlap. If a settlement still sits here (e.g. a hand-placed one), nudge the wreck outward.
            (ax, az) = WreckAnchorFor(_landingPads);
            for (int nudge = 0; nudge < 12 && WreckSiteBlocked(ax, az, System.Math.Max(structure.Width, structure.Length) / 2); nudge++)
            {
                ax = WorldConstants.WrapX(ax - 24, _world.Circumference);
                az += 16;
            }

            if (seaIceIsWet && FootprintWet(planet, ax, az, structure.Width, structure.Length))
            {
                (ax, az, restY) = DryWreckSite(planet, ax, az, structure.Width, structure.Length);
            }
        }

        return (ax, az, restY ?? _generator.SurfaceHeight(planet, ax, az));
    }

    /// <summary>Writes the wreck's blocks once (with a layout wreck's dye, glow and shape) and, for a layout wreck, clears
    /// the inside of its hull, so a hull sunk into the ground is walkable on its own floor (#3521). Breached hull cells keep
    /// what the world had there.</summary>
    private void StampWreckBlocks(WreckStructure structure, int ax, int baseY, int az)
    {
        for (int x = 0; x < structure.Width; x++)
        {
            for (int y = 0; y < structure.Height; y++)
            {
                for (int z = 0; z < structure.Length; z++)
                {
                    ushort b = structure.Get(x, y, z);
                    var pos = new Vector3i(ax + x, baseY + y, az + z);
                    if (b != 0)
                    {
                        var (tint, glow) = structure.ModifierAt(x, y, z);
                        _world.SetBlock(pos, new BlockId(b), tint, glow, structure.ShapeAt(x, y, z));
                    }
                    else if (structure.IsInterior(x, y, z))
                    {
                        _world.SetBlock(pos, BlockId.Air);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The planned wreck anchor for a pad set: a fixed offset from landing pad 0, or a spot beside the world
    /// origin when a body has no pads at all. Shared with the settlement stamper (#1684), which reserves this
    /// zone up-front — the constant used to be written out twice and kept in step by hand.
    /// </summary>
    internal static (int X, int Z) WreckAnchorFor(IReadOnlyList<LandingPad> pads)
    {
        int pad0X = pads.Count > 0 ? pads[0].CenterX : 0;
        int pad0Z = pads.Count > 0 ? pads[0].CenterZ : 0;
        return (pad0X - WreckAnchorOffset, pad0Z + WreckAnchorOffset);
    }

    /// <summary>How far from pad 0 the wreck crash site sits, on both axes.</summary>
    private const int WreckAnchorOffset = 56;

    /// <summary>#2756: how far (Chebyshev, blocks) and on what grid the crash site may move off a wet anchor.</summary>
    private const int WreckDrySearchReach = 96, WreckDrySearchStep = 8;

    /// <summary>#2756 (generation 27+): the crash site for a wreck whose pad-relative spot stands in water, lava or sea ice
    /// (<see cref="FootprintWet"/>). The pad-relative spot sat at <c>SurfaceHeight</c> — under a sea the seabed — so on a
    /// frozen world the first goal of a new game could lie under the ice, out of sight and reach. Walks a grid around the
    /// anchor nearest first (squared distance, then a fixed order — integers only, so every platform picks the same spot)
    /// and takes the first footprint that is dry and clear of the pads and of every structure stamped before the wreck.
    /// Without one, the wreck stays at the anchor and rests on the surface: the ice top, or the water line on open water
    /// (<c>restY</c>, the height the stamp sinks the hull one block into); lava or a gas sea keep the classic seat.</summary>
    private (int X, int Z, int? RestY) DryWreckSite(Shared.Definitions.PlanetType planet, int ax, int az, int w, int l)
    {
        var reserved = WreckSiteReservations();
        int hw = w / 2 + 1, hl = l / 2 + 1;
        var offsets = new List<(int Dx, int Dz)>();
        for (int dx = -WreckDrySearchReach; dx <= WreckDrySearchReach; dx += WreckDrySearchStep)
        {
            for (int dz = -WreckDrySearchReach; dz <= WreckDrySearchReach; dz += WreckDrySearchStep)
            {
                if (dx != 0 || dz != 0)
                {
                    offsets.Add((dx, dz));
                }
            }
        }

        offsets.Sort((a, b) =>
        {
            int c = (a.Dx * a.Dx + a.Dz * a.Dz).CompareTo(b.Dx * b.Dx + b.Dz * b.Dz);
            return c != 0 ? c : a.Dx != b.Dx ? a.Dx.CompareTo(b.Dx) : a.Dz.CompareTo(b.Dz);
        });

        foreach (var (dx, dz) in offsets)
        {
            int x = WorldConstants.WrapX(ax + dx, _world.Circumference), z = az + dz;
            bool blocked = UsesFootprintRegistry // #3514: generation 38+ asks the registry (the crash-site reservation aside)
                ? Footprints.Overlaps(x + w / 2, z + l / 2, hw, hl, KeepClearMargins(StructureTypeRules.Wreck), StructureTypeRules.WreckSite)
                : OverlapsFootprint(x + w / 2, z + l / 2, hw, hl, reserved, SettlementCollisionMargin)
                  || OverlapsAnySettlement(x + w / 2, z + l / 2, System.Math.Max(w, l) / 2);
            if (!blocked && !FootprintWet(planet, x, z, w, l))
            {
                return (x, z, null);
            }
        }

        // No dry ground in reach (a frozen ocean world): rest on whatever covers the anchor's footprint.
        int rest = int.MinValue;
        foreach (var (x, z) in FootprintSamples(ax, az, w, l))
        {
            if (_generator.TryGetIceSurface(planet, x, z, out int iceTop))
            {
                rest = System.Math.Max(rest, iceTop);
            }
            else if (_generator.TryGetWaterSurface(planet, x, z, out int waterTop, out _))
            {
                rest = System.Math.Max(rest, waterTop);
            }
        }

        return (ax, az, rest == int.MinValue ? null : System.Math.Max(rest, _generator.SurfaceHeight(planet, ax, az)));
    }

    /// <summary>#2756: the footprints a moved crash site keeps clear of — the pads and every structure stamped before the wreck
    /// (ruins, camps, monuments, factories, SPS labs; settlements, the intercity line, rail ruins and lodges are asked through
    /// <see cref="OverlapsAnySettlement"/>). They all reserved the pad-relative anchor, not the spot the wreck moves to.</summary>
    private List<(int Cx, int Cz, int Hw, int Hl)> WreckSiteReservations()
    {
        var reserved = new List<(int Cx, int Cz, int Hw, int Hl)>();
        foreach (var pad in _landingPads)
        {
            reserved.Add((pad.CenterX, pad.CenterZ, LandingPadRadius + 2, LandingPadRadius + 2));
        }

        reserved.AddRange(_worlds.Active.RuinFootprints);
        foreach (var (min, max) in _banditCamps.Select(c => (c.Min, c.Max))
                     .Concat(_monuments.Select(m => (m.Min, m.Max)))
                     .Concat(_factories.Select(f => (f.Min, f.Max)))
                     .Concat(_spsLabs.Select(s => (s.Min, s.Max))))
        {
            reserved.Add(((min.X + max.X) / 2, (min.Z + max.Z) / 2, (max.X - min.X) / 2 + 1, (max.Z - min.Z) / 2 + 1));
        }

        return reserved;
    }

    /// <summary>Half-extent of the zone settlements keep clear for the wreck.</summary>
    internal const int WreckReservedHalfExtent = 14;

    /// <summary>Per-planet probability of a wreck (rare everywhere; a touch likelier on lived-in worlds).</summary>
    private static double WreckChance(Shared.Definitions.PlanetType planet)
    {
        // Airless asteroids can still have a crash; keep it rare across the board.
        return (planet.CreatureAbundance ?? "few").ToLowerInvariant() switch
        {
            "many" => 0.30,
            "none" => 0.15,
            _ => 0.20,
        };
    }

    private static string WreckDisplayName(string origin, Shared.Definitions.ShipDefinition design, System.Random rng)
    {
        string[] tags = { "SC", "RV", "ISV", "XN", "KV" };
        int num = 100 + rng.Next(900);
        string prefix = origin == "alien" ? "Derelict" : "Wreck of the";
        return $"{prefix} {tags[rng.Next(tags.Length)]}-{num}";
    }

    /// <summary>Repairs one wreck hull cell if the player has the exact matching block item.</summary>
    public bool RepairWreck(string playerId, int x, int y, int z, string itemKey)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return false;
        }

        if (!_wreckStamped)
        {
            Reject(session, "wreck_repair", "@srv.wreck.none");
            return false;
        }

        if (_wreck is null)
        {
            // #3521: a layout wreck whose layout is gone or changed — its blocks stand, but there is no mask to repair to.
            Reject(session, "wreck_repair", "@srv.wreck.unknown_design");
            return false;
        }

        if (_wreckClaimed)
        {
            Reject(session, "wreck_repair", "@srv.wreck.claimed");
            return false;
        }

        var pos = WorldConstants.CanonicalBlock(new Vector3i(x, y, z), _world.Circumference); // wraps at THIS world's seam
        if (!TryGetWreckRepairTarget(pos, out var required))
        {
            Reject(session, "wreck_repair", "@srv.wreck.not_mask");
            return false;
        }

        if (_world.GetBlock(pos) == required.NumericId)
        {
            Reject(session, "wreck_repair", "@srv.wreck.cell_done");
            return false;
        }

        if (!WithinReach(session.State, pos))
        {
            Reject(session, "wreck_repair", "@out_of_reach");
            return false;
        }

        var item = _content.GetItem(itemKey);
        if (item is null || item.PlacesBlock != required.Key)
        {
            Reject(session, "wreck_repair", Localize(session.Locale, "srv.wreck.need_block")
                .Replace("{name}", LocalizedName(session.Locale, required.NameKey, required.Key)));
            return false;
        }

        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(itemKey) < 1)
            {
                Reject(session, "wreck_repair", "@srv.wreck.no_block");
                return false;
            }

            pool.Remove(new[] { new ItemAmount(itemKey, 1) });
        }

        var (tint, glow, shape) = WreckIntactLook(pos); // #3521: a layout wreck's cell comes back dyed and shaped
        _world.SetBlock(pos, required.NumericId, tint, glow, shape);
        BroadcastToWorld(new BlockChanged { X = pos.X, Y = pos.Y, Z = pos.Z, Block = required.NumericId.Value, Tint = tint, Glow = glow, Shape = shape });
        SendInventory(session);
        SendWreckRepairStatus(session);
        return true;
    }

    /// <summary>Claims a fully repaired wreck into the player's owned ship fleet.</summary>
    public (bool Ok, string ShipId) ClaimWreck(string playerId)
    {
        var session = FindSessionByPlayerId(playerId);
        if (session is null)
        {
            return (false, string.Empty);
        }

        if (!_wreckStamped)
        {
            Reject(session, "wreck_claim", "@srv.wreck.nothing_claim");
            return (false, string.Empty);
        }

        if (_wreck is null)
        {
            Reject(session, "wreck_claim", "@srv.wreck.unknown_design"); // #3521: a layout wreck without its layout
            return (false, string.Empty);
        }

        if (_wreckClaimed)
        {
            Reject(session, "wreck_claim", "@srv.wreck.claimed");
            return (false, string.Empty);
        }

        if (CountWreckRepairRemaining() > 0)
        {
            Reject(session, "wreck_claim", "@srv.wreck.incomplete");
            SendWreckRepairStatus(session);
            return (false, string.Empty);
        }

        var def = _content.GetShip(_wreck.ShipType);
        if (def is null)
        {
            Reject(session, "wreck_claim", "@srv.wreck.unknown_design");
            return (false, string.Empty);
        }

        string id = AddOwnedShipFromDefinition(def, "wreck");
        _wreckClaimed = true;
        // #466: persist the claim — without this record, every re-entry offered the wreck again.
        _meta.Claims.RemoveAll(c => c.Key == _world.LocationId + "|wreck");
        _meta.Claims.Add(new Shared.State.StructureClaim
        {
            Key = _world.LocationId + "|wreck",
            OwnerId = playerId,
            Name = _wreckName,
        });
        _repo.SaveMetadata(_meta);
        Send(session, new ServerMessage
        {
            Text = Localize(session.Locale, "srv.wreck.claim_done")
                .Replace("{name}", def.DisplayName ?? LocalizedName(session.Locale, def.NameKey, def.Key)), // #3520: a user ship's own name
        });
        SendWreckRepairStatus(session);
        return (true, id);
    }

    private int CountWreckRepairRemaining() => EnumerateWreckRepairCells().Count();

    private IEnumerable<(Vector3i Pos, BlockDefinition Block)> EnumerateWreckRepairCells()
    {
        if (!_wreckStamped || _wreck is null)
        {
            yield break;
        }

        for (int x = 0; x < _wreck.Width; x++)
            for (int y = 0; y < _wreck.Height; y++)
                for (int z = 0; z < _wreck.Length; z++)
                {
                    ushort intact = _wreck.IntactAt(x, y, z);
                    if (intact == 0)
                    {
                        continue;
                    }

                    // The wreck anchor sits at a raw offset that can be negative; the actual blocks live in the
                    // canonical longitude space, so emit canonical breach positions (matches what the client renders).
                    var world = WorldConstants.CanonicalBlock(new Vector3i(_wreckOrigin.X + x, _wreckOrigin.Y + y, _wreckOrigin.Z + z), _world.Circumference);
                    if (_world.GetBlock(world).Value == intact)
                    {
                        continue;
                    }

                    if (_content.BlockById(new BlockId(intact)) is { } required)
                    {
                        yield return (world, required);
                    }
                }
    }

    private bool TryGetWreckRepairTarget(Vector3i world, out BlockDefinition required)
    {
        required = null!;
        if (!_wreckStamped || _wreck is null)
        {
            return false;
        }

        // Longitude wraps: measure the local X the short way round so a canonical repair coordinate maps
        // back onto the (possibly negative-anchored) wreck mask.
        int lx = WorldConstants.WrapDeltaX(world.X - _wreckOrigin.X, _world.Circumference);
        int ly = world.Y - _wreckOrigin.Y;
        int lz = world.Z - _wreckOrigin.Z;
        if (!_wreck.InBounds(lx, ly, lz))
        {
            return false;
        }

        ushort intact = _wreck.IntactAt(lx, ly, lz);
        if (intact == 0)
        {
            return false;
        }

        required = _content.BlockById(new BlockId(intact))!;
        return required is not null;
    }

    private void SendWreckRepairStatus(PlayerSession session)
        => Send(session, new WreckRepairStatus
        {
            WreckName = _wreckName,
            ShipType = _wreck?.ShipType ?? string.Empty,
            Remaining = WreckRepairRemaining,
            Total = WreckRepairTotal,
            Claimable = _wreckStamped && _wreck is not null && !_wreckClaimed && WreckRepairRemaining == 0,
            Claimed = _wreckClaimed,
            Needs = string.Join(",", WreckRepairCells().Select(c => c.BlockKey).Distinct()),
        });

    private void HandleRepairWreck(PlayerSession session, RepairWreckIntent intent)
        => RepairWreck(session.State.PlayerId, intent.X, intent.Y, intent.Z, intent.ItemKey);

    private void HandleClaimWreck(PlayerSession session)
        => ClaimWreck(session.State.PlayerId);
}
