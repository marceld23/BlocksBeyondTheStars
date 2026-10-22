// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.GameServer;

/// <summary>
/// Block placement (<see cref="PlaceBlockIntent"/>). <see cref="HandlePlace"/> resolves the intent into a
/// <see cref="PlacementRequest"/> (item → block definition → canonical cell), then walks the ordered
/// <see cref="PlacementRules"/> table: the first rule that refuses wins and sends the one
/// <see cref="ActionRejected"/> with action "place" (a rule may also have the pipeline send a VEGA hint right after
/// it), and a rule that handled the placement completely ends it. Only then is the item paid for, a door takes its
/// early exit, and the effect steps run in a fixed order — voxel descriptor, cell writes, flora, block-kind hooks,
/// the Crystal Net and rail graph, the broadcast, devices, and the closing inventory/achievement/event updates.
/// Every rule runs before anything is consumed, so a refused placement costs nothing.
/// </summary>
public sealed partial class GameServer
{
    /// <summary>One placement in flight: the intent, the block it places and the canonical target cell, plus what
    /// the rules found out on the way (the cell's previous block, the bed foot, a multi-cell form's siblings) and
    /// the voxel descriptor the effect steps stamp.</summary>
    private sealed class PlacementRequest
    {
        public PlacementRequest(PlaceBlockIntent intent, BlockDefinition blockDef, Vector3i pos)
        {
            Intent = intent;
            BlockDef = blockDef;
            Pos = pos;
        }

        public PlaceBlockIntent Intent { get; }

        public BlockDefinition BlockDef { get; }

        public Vector3i Pos { get; }

        /// <summary>The target cell's block before the placement, read once by the cell_free rule (the cell holds
        /// the new block once it is stamped).</summary>
        public BlockId Existing { get; set; }

        /// <summary>The target cell holds a fluid the placed block displaces (#851); set by the cell_free rule.</summary>
        public bool IntoFluid { get; set; }

        /// <summary>A bed's foot cell (#1846); set by the bed_room rule.</summary>
        public Vector3i? BedFoot { get; set; }

        /// <summary>A multi-cell form's further cells (#1961); set by the form_room rule.</summary>
        public List<(Vector3i Pos, int Cell)>? FormSiblings { get; set; }

        /// <summary>A multi-cell form's descriptor, settled by the form_room rule (0 = no such form).</summary>
        public int MultiCellDescriptor { get; set; }

        /// <summary>A VEGA hint key the pipeline says right after the refusal (the flora rule's seedling case).</summary>
        public string? HintAfterRefusal { get; set; }

        public int Tint { get; set; }

        public int Glow { get; set; }

        public int Shape { get; set; }

        public int CrystalYaw { get; set; }

        public uint PlantedSeed { get; set; }
    }

    /// <summary>What a placement rule decided: go on (<see cref="Continue"/>), refuse with a reason, or the rule
    /// handled the placement completely itself (<see cref="Done"/>).</summary>
    private readonly record struct PlacementVerdict(string? Refusal, bool Handled)
    {
        public static readonly PlacementVerdict Continue = default;

        public static readonly PlacementVerdict Done = new(null, true);

        public static PlacementVerdict Refuse(string reason) => new(reason, false);
    }

    /// <summary>One named entry of the <see cref="PlacementRules"/> table.</summary>
    private readonly record struct PlacementRule(string Name, Func<GameServer, PlayerSession, PlacementRequest, PlacementVerdict> Check);

    private void HandlePlace(PlayerSession session, PlaceBlockIntent place)
    {
        var request = ResolvePlacement(session, place);
        if (request is null)
        {
            return;
        }

        foreach (var rule in PlacementRules)
        {
            var verdict = rule.Check(this, session, request);
            if (verdict.Refusal is { } refusal)
            {
                Reject(session, "place", refusal);
                if (request.HintAfterRefusal is { } hint)
                {
                    ShipAiHintOnce(session, hint);
                }

                return;
            }

            if (verdict.Handled)
            {
                return;
            }
        }

        if (!TryPayForPlacement(session, request))
        {
            return;
        }

        // A door isn't a voxel block — it fills the (air) cell as a server door entity (Task 5 Stage 3c).
        if (DoorBlocks.IsDoorBlock(request.BlockDef.Key))
        {
            PlaceDoor(session, request.Pos, DoorBlocks.KindForBlock(request.BlockDef.Key));
            SendInventory(session);
            return;
        }

        ResolvePlacedVoxel(session, request);
        StampPlacedCells(session, request);
        ApplyFloraPlacementEffects(session, request);
        ApplyBlockKindPlacementEffect(session, request);
        JoinPlacedBlockToNets(session, request);
        AnnouncePlacedBlock(session, request);
        StartPlacedDevice(session, request);
        FinishPlacement(session, request);
    }

    /// <summary>The intent's item → the block it places → the canonical target cell, or null after a refusal
    /// (not placeable, unknown block). Touches no world cell — the height band is checked before the first.</summary>
    private PlacementRequest? ResolvePlacement(PlayerSession session, PlaceBlockIntent place)
    {
        var item = _content.GetItem(place.ItemKey);
        if (item is null || string.IsNullOrEmpty(item.PlacesBlock))
        {
            Reject(session, "place", "@srv.place.not_placeable");
            return null;
        }

        var blockDef = _content.GetBlock(item.PlacesBlock!);
        if (blockDef is null)
        {
            Reject(session, "place", "@srv.place.unknown_block");
            return null;
        }

        var pos = WorldConstants.CanonicalBlock(new Vector3i(place.X, place.Y, place.Z), _world.Circumference); // wraps at THIS world's seam
        return new PlacementRequest(place, blockDef, pos);
    }

    /// <summary>Rule "height_limit": the target cell lies inside the build band.</summary>
    private PlacementVerdict HeightBandRule(PlacementRequest r)
    {
        // Reject before touching the world: a block edit outside the build band would generate + persist a
        // chunk at an arbitrary height (unbounded RAM/disk DoS from a spoofed-position place spam). See MinBuildY.
        if (!WithinBuildHeight(r.Pos.Y))
        {
            return PlacementVerdict.Refuse("@srv.place.height_limit");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "torch_air": a torch needs air to burn.</summary>
    private PlacementVerdict TorchNeedsAirRule(PlacementRequest r)
    {
        // A torch is an open flame: it needs air to burn. On an airless body (atmosphere "none" — asteroids and
        // the like) there is nothing to sustain it, so it is refused with a reason the player can act on instead
        // of being placed as a dud that mysteriously gives no light. A toxic atmosphere is fine — you need a
        // suit, the flame does not. Checked HERE, before the item is consumed further down, so a refused torch
        // stays in the pack. A sealed base room or station pocket holds air of its own (#1483): a torch burns
        // there too — the old check asked only the WORLD and refused the lamp in an asteroid base that its own
        // core reported as breathable.
        if (r.BlockDef.Key == "torch" && !AtmospherePresent && !BreathableAirAt(r.Pos))
        {
            return PlacementVerdict.Refuse("@no_air");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "cell_free": the target cell holds air or a fluid the block displaces. Reads the cell's
    /// previous block into the request, once.</summary>
    private PlacementVerdict CellFreeRule(PlacementRequest r)
    {
        // Building under water (#851): the target cell is free when it is air OR holds a fluid — a placed block
        // DISPLACES water/lava, exactly like in every other voxel builder. Without this you can't build under
        // water at all: the client's aim march passes THROUGH fluids (they have no collider — you swim into
        // them), so the cell it offers while you're swimming always holds water, and water only yields to a
        // tier-3 mining beam, so there was no way to clear it first either.
        r.Existing = _world.GetBlock(r.Pos);
        r.IntoFluid = IsFluid(r.Existing.Value);
        if (!r.Existing.IsAir && !r.IntoFluid)
        {
            return PlacementVerdict.Refuse("@srv.place.not_empty");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "door_here": no block and no second door goes into a doorway.</summary>
    private PlacementVerdict NoDoorInCellRule(PlacementRequest r)
    {
        // A doorway is air to the voxel grid, but a door stands in it (#2145): no block and no second door goes
        // in. A right-click aimed at a door used to drop the held block into the opening behind it — the door
        // still swung, but a wall stood in the way — or stacked a second door that stayed shut. Refused before
        // anything is consumed.
        if (DoorFillsCell(r.Pos))
        {
            return PlacementVerdict.Refuse("@srv.place.door_here");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "fluid_cell": the two placeables that can't take a fluid cell (a door, a torch in water).</summary>
    private PlacementVerdict FluidCellRule(PlacementRequest r)
    {
        if (r.IntoFluid)
        {
            // Two placeables genuinely can't take a fluid cell, and both are refused before anything is
            // consumed: a door is an ENTITY living in an air cell (the fluid would just flow back around it,
            // leaving a door that holds nothing back), and a torch is an open flame — a submerged one would be
            // the same mysterious dud the airless-body check above exists to prevent.
            if (DoorBlocks.IsDoorBlock(r.BlockDef.Key))
            {
                return PlacementVerdict.Refuse("@srv.place.not_empty");
            }

            if (r.BlockDef.Key == "torch" && r.Existing.Value == _waterId)
            {
                return PlacementVerdict.Refuse("@no_air");
            }
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "above_head": no block in the player's own head cell.</summary>
    private static PlacementVerdict NotAboveOwnHeadRule(PlayerSession session, PlacementRequest r)
    {
        // Don't let the player wall themselves in: refuse a block at HEAD height in their own column. The FEET
        // cell is allowed so you can pillar-jump (place under yourself while jumping) — the client collider just
        // lifts you onto the new block (B3); only the head cell would trap you.
        var feet = session.State.Position;
        int fx = (int)System.Math.Floor(feet.X), fy = (int)System.Math.Floor(feet.Y), fz = (int)System.Math.Floor(feet.Z);
        if (r.Pos.X == fx && r.Pos.Z == fz && r.Pos.Y == fy + 1)
        {
            return PlacementVerdict.Refuse("@srv.place.above_head");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "reach": the target cell is within the player's reach.</summary>
    private PlacementVerdict WithinReachRule(PlayerSession session, PlacementRequest r)
    {
        if (!WithinReach(session.State, r.Pos))
        {
            return PlacementVerdict.Refuse("@out_of_reach");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "landing_pad": the landing pad is kept clear (admins excepted).</summary>
    private PlacementVerdict LandingPadRule(PlayerSession session, PlacementRequest r)
    {
        if (!session.State.IsAdmin && IsOnLandingPad(r.Pos))
        {
            return PlacementVerdict.Refuse("@srv.place.pad_reserved");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "station": a station's own blocks are protected.</summary>
    private PlacementVerdict StationProtectionRule(PlacementRequest r)
    {
        if (IsStationBlock(r.Pos))
        {
            return PlacementVerdict.Refuse("@srv.protect.station");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "factory": a protected factory's cells are refused.</summary>
    private PlacementVerdict FactoryProtectionRule(PlayerSession session, PlacementRequest r)
    {
        if (IsFactoryProtected(r.Pos, session.State.PlayerId, session.State.IsAdmin))
        {
            return PlacementVerdict.Refuse("@srv.protect.factory");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "base": someone else's protected base zone is refused.</summary>
    private PlacementVerdict BaseProtectionRule(PlayerSession session, PlacementRequest r)
    {
        if (IsBaseProtected(r.Pos, session.State.PlayerId, session.State.IsAdmin))
        {
            return PlacementVerdict.Refuse("@srv.protect.base");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "ship_interior": no building inside the ship or a construction site.</summary>
    private PlacementVerdict ShipInteriorRule(PlacementRequest r)
    {
        // No building inside the ship — the cabin is a fixed structure. The construction site (#948) is
        // guarded the same way: its cells are structure cells, never world blocks.
        if (ShipInteriorContains(new Vector3f(r.Pos.X, r.Pos.Y, r.Pos.Z))
            || ConstructionContains(new Vector3f(r.Pos.X, r.Pos.Y, r.Pos.Z)))
        {
            return PlacementVerdict.Refuse("@srv.place.no_ship_interior");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "ship_core": a ship keel is handled completely by <see cref="HandleShipCorePlace"/>, which
    /// sends its own refusals — this rule never refuses.</summary>
    private PlacementVerdict ShipCoreRule(PlayerSession session, PlacementRequest r)
    {
        // A ship keel founds a self-built ship (#948): it never becomes a world block — it seeds a new
        // construction-site structure OBJECT anchored at the cell. Fully handled (incl. material cost).
        if (r.BlockDef.Key == ShipCoreBlock)
        {
            HandleShipCorePlace(session, r.Pos, r.Intent.ItemKey);
            return PlacementVerdict.Done;
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "flora": seeds and plants only take on a suitable host; a bred seedling has its own rules.</summary>
    private PlacementVerdict FloraRule(PlacementRequest r)
    {
        // Seeds / flora only take on a suitable host block (mud, grass, crystal, ...).
        if (IsFlora(r.BlockDef.NumericId.Value))
        {
            // #2209: a seedling has its own rules — any clean plant soil (or a hydro tray, a flower pot) and air.
            if (IsBredPlant(r.BlockDef.NumericId.Value))
            {
                if (BredPlantRefusal(r.Intent.ItemKey, r.Pos) is { } refusal)
                {
                    // VEGA's hint is about the place (clean soil, air, a tray or a pot inside a base), so it is
                    // for a real seedling that found none — the soil, the air, the world's cap. A seedling that
                    // carries no species, or a species that is no single plant, gets its own line and no hint:
                    // no other place would take it, and the hint is said only once. HandlePlace says it right
                    // after the refusal, so the reason arrives first.
                    if (refusal is "@srv.bio.plant_soil" or "@srv.bio.plant_air" or "@srv.bio.plant_cap")
                    {
                        r.HintAfterRefusal = "plant_refused";
                    }

                    return PlacementVerdict.Refuse(refusal);
                }
            }
            else if (!IsValidFloraHost(r.BlockDef.NumericId.Value, r.Pos))
            {
                return PlacementVerdict.Refuse("@srv.place.plant_ground");
            }

            // On a space station (void world) a plant must sit fully inside the hull — solid block below and no
            // side open to space — so it can't be seen or walked through into the void.
            if (!IsFloraEnclosedForVoidWorld(r.Pos))
            {
                return PlacementVerdict.Refuse("@srv.place.plant_enclosed");
            }
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "base_core": a base core founds a base only on a real surface, one per body per player.</summary>
    private PlacementVerdict BaseCoreRule(PlayerSession session, PlacementRequest r)
    {
        // A base core founds a player base (Grundstein) — only on a real surface, and only one per body per player.
        // Checked before any material is consumed so a refused founding costs nothing.
        if (r.BlockDef.Key == BaseCoreBlock)
        {
            var hereBody = _galaxy?.FindBody(_world.LocationId);
            if (hereBody is null
                || (hereBody.Kind != CelestialKind.Planet && hereBody.Kind != CelestialKind.Moon && hereBody.Kind != CelestialKind.AsteroidField))
            {
                return PlacementVerdict.Refuse("@srv.base.surface_only");
            }

            if (PlayerHasBaseOn(session.State.PlayerId, _world.LocationId))
            {
                return PlacementVerdict.Refuse("@srv.base.already_here");
            }
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "light_mount": a light needs a face that can hold it.</summary>
    private PlacementVerdict LightMountRule(PlacementRequest r)
    {
        // #2683: a torch can't hang from the ceiling (the lantern can) — refused with a hint before anything is consumed.
        if (PropShapes.IsLightMountKey(r.BlockDef.Key) && LightFormAt(r.BlockDef.Key, r.Pos, r.Intent.UpFace) == PropShapes.LightRefused)
        {
            return PlacementVerdict.Refuse("@srv.place.torch_ceiling");
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "bed_room": a bed's foot cell must be free and allowed; it is kept in the request.</summary>
    private PlacementVerdict BedFootRule(PlayerSession session, PlacementRequest r)
    {
        // A bed is two cells long (#1846): the foot half lands on the cell the head's yaw points to. The foot
        // cell is put through the same checks the head cell just passed — free, in reach, not the player's own
        // head, no protected ground — BEFORE the item is consumed, so a bed jammed against a wall is refused
        // with a reason instead of leaving a lone head half (or writing into someone else's base).
        if (r.BlockDef.Key == BedBlock)
        {
            if (!TryBedFootCell(session, r.Pos, StampPropShape(session, r.Intent, r.BlockDef.Key, r.Pos), out var foot))
            {
                return PlacementVerdict.Refuse("@srv.place.bed_room");
            }

            r.BedFoot = foot;
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>Rule "form_room": every further cell of a multi-cell form must be free and allowed; the form's
    /// descriptor and sibling cells are kept in the request.</summary>
    private PlacementVerdict MultiCellFormRule(PlayerSession session, PlacementRequest r)
    {
        // A player form over several blocks (#1961): like the bed, every further cell must be free and allowed
        // BEFORE the item is consumed — a wardrobe under a low ceiling is refused with a reason, not cut in half.
        // Such a form turns but never tips, so its descriptor is settled here already (yaw from the rotate key
        // or the player's facing, up-face +Y).
        if (r.BlockDef.Shapeable && ShapeCode.IsCustomShape(ItemKey.Shape(r.Intent.ItemKey))
            && MaterialUnitsOfShape(ItemKey.Shape(r.Intent.ItemKey)) > 1)
        {
            int formYaw = r.Intent.Yaw >= 0 && r.Intent.Yaw <= 3
                ? r.Intent.Yaw
                : ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;
            r.MultiCellDescriptor = ShapeCode.Pack(ItemKey.Shape(r.Intent.ItemKey), formYaw, ShapeCode.UpPlusY);
            if (!TryMultiCellSiblings(session, r.Pos, r.MultiCellDescriptor, out var siblings))
            {
                return PlacementVerdict.Refuse("@srv.place.form_room");
            }

            r.FormSiblings = siblings;
        }

        return PlacementVerdict.Continue;
    }

    /// <summary>The placement checks in the order they run; the first refusal wins. The world is first touched
    /// after the height band (spoofed-Y DoS guard), and every rule runs before anything is consumed.</summary>
    private static readonly PlacementRule[] PlacementRules =
    {
        new("height_limit", static (g, _, r) => g.HeightBandRule(r)),
        new("torch_air", static (g, _, r) => g.TorchNeedsAirRule(r)),
        new("cell_free", static (g, _, r) => g.CellFreeRule(r)),
        new("door_here", static (g, _, r) => g.NoDoorInCellRule(r)),
        new("fluid_cell", static (g, _, r) => g.FluidCellRule(r)),
        new("above_head", static (_, s, r) => NotAboveOwnHeadRule(s, r)),
        new("reach", static (g, s, r) => g.WithinReachRule(s, r)),
        new("landing_pad", static (g, s, r) => g.LandingPadRule(s, r)),
        new("station", static (g, _, r) => g.StationProtectionRule(r)),
        new("factory", static (g, s, r) => g.FactoryProtectionRule(s, r)),
        new("base", static (g, s, r) => g.BaseProtectionRule(s, r)),
        new("ship_interior", static (g, _, r) => g.ShipInteriorRule(r)),
        new("ship_core", static (g, s, r) => g.ShipCoreRule(s, r)),
        new("flora", static (g, _, r) => g.FloraRule(r)),
        new("base_core", static (g, s, r) => g.BaseCoreRule(s, r)),
        new("light_mount", static (g, _, r) => g.LightMountRule(r)),
        new("bed_room", static (g, s, r) => g.BedFootRule(s, r)),
        new("form_room", static (g, s, r) => g.MultiCellFormRule(s, r)),
    };

    /// <summary>The placement rules in the order they run (#A2 ratchet for PlacementRuleOrderTests).</summary>
    internal static IReadOnlyList<string> PlacementRuleNamesForTest => PlacementRules.Select(rule => rule.Name).ToArray();

    /// <summary>Takes the placed item from the material pool, unless the placement is free; false after the
    /// "no block" refusal.</summary>
    private bool TryPayForPlacement(PlayerSession session, PlacementRequest r)
    {
        // Creative mode and admin instant-build place without consuming materials.
        bool free = !Rules.CraftingCostsMaterialsFor(session.State.ModeOverride) || session.State.InstantBuild;
        var pool = new MaterialPool(_content, session.State, _ship);
        if (!free)
        {
            if (pool.Count(r.Intent.ItemKey) < 1)
            {
                Reject(session, "place", "@srv.place.no_block");
                return false;
            }

            pool.Remove(new[] { new ItemAmount(r.Intent.ItemKey, 1) });
        }

        return true;
    }

    /// <summary>Effect step 1: the placed voxel's tint, glow, shape descriptor, planted seed and Crystal Net yaw.</summary>
    private void ResolvePlacedVoxel(PlayerSession session, PlacementRequest r)
    {
        // A dyed/glowing block carries its colour in the item key; stamp it on the placed cell. Only honour
        // it for tintable building materials (the colour came from the always-available dye/glow action).
        if (r.BlockDef.Tintable && ItemKey.HasModifier(r.Intent.ItemKey))
        {
            r.Tint = ItemKey.Tint(r.Intent.ItemKey);
            r.Glow = ItemKey.Glow(r.Intent.ItemKey);
        }

        // #2209: a bred plant's look rides on its voxel — its colour in the tint, its form in the glow channel.
        r.PlantedSeed = IsBredPlant(r.BlockDef.NumericId.Value) ? ItemKey.Seed(r.Intent.ItemKey) : 0;
        if (r.PlantedSeed != 0)
        {
            (r.Tint, r.Glow) = BredStamp(r.PlantedSeed);
        }

        // A shaped block carries its FORM in the item key; the placement ORIENTATION is derived from the
        // player's facing (yaw quantized to one of the four cardinal directions). Together they pack into the
        // per-voxel shape descriptor. Only shapeable building materials honour a shape.
        if (r.BlockDef.Shapeable)
        {
            int shapeIndex = ItemKey.Shape(r.Intent.ItemKey);
            // Built-in forms are always placeable; a player-designed form (#843) only if this save actually
            // has it registered — an item carrying a wiped index falls back to a plain cube rather than
            // stamping geometry nobody can mesh.
            if (ShapeCode.IsPlaceableShape(shapeIndex, HasCustomShape))
            {
                // Yaw: the client may send an explicit quarter-turn (rotate key); otherwise it follows where the
                // player is looking. An explicit turn is what lets you build stairs into a corner without having
                // to stand in a particular direction to get the angle you want.
                int facing = r.Intent.Yaw >= 0 && r.Intent.Yaw <= 3
                    ? r.Intent.Yaw
                    : ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;
                // Orientation: the client may send an explicit rotation override (rotate key); otherwise the
                // shape auto-orients so its base rests on the surface it was built against (floor → +Y, i.e.
                // unchanged; walls/ceiling tilt it). up-face × yaw give the full 24 orientations.
                int upFace = ShapeCode.IsValidUpFace(r.Intent.UpFace) ? r.Intent.UpFace : DeriveShapeUpFace(r.Pos);
                r.Shape = r.MultiCellDescriptor != 0
                    ? r.MultiCellDescriptor // the footprint was checked for exactly this yaw, upright
                    : ShapeCode.Pack(shapeIndex, facing, upFace);
            }
        }

        // Prop blocks (furniture #804/#807/#809, ladder + crafted staircase #909) read as their real
        // silhouette out of the box: stamp their default form on placement — the same per-voxel shape channel
        // the Shape action writes, so the mesher, save and wire all treat it like any player-shaped cell. The
        // quarter-turn honours the rotate key like any shaped block (#863); without one it follows the
        // player's facing. How far the orientation may travel is per prop (PropOrientation), because the
        // cycle the client offers must promise exactly what is honoured here. BreakBlockAt strips the stamped
        // form again so the drop stacks with freshly crafted items.
        if (r.Shape == 0)
        {
            r.Shape = StampPropShape(session, r.Intent, r.BlockDef.Key, r.Pos);
        }

        // A block with a FRONT (#2124) — a vending machine, a forge, a watcher — stays a plain cube but remembers
        // which side its front is on, in the descriptor's otherwise unused up-face field (CubeFacing). "toward" turns
        // it to the player, "away" the way the player looks; an explicit quarter turn (the rotate key) stands in for the
        // look heading — the Crystal Net's convention, so a gate's wire and its drawn front can never disagree. The mined
        // drop keeps only the shape index (0), so the front never rides an item into a stack.
        r.CrystalYaw = r.Intent.Yaw;
        if (r.Shape == 0 && r.BlockDef.Facing != null && PropShapes.DefaultPlaceShape(r.BlockDef.Key) == 0)
        {
            int front = CubeFacing.FrontForPlacement(r.BlockDef.Facing, r.Intent.Yaw, session.State.Yaw);
            r.Shape = CubeFacing.Pack(front);
            r.CrystalYaw = CubeFacing.LookHeadingOf(r.BlockDef.Facing, front); // a gate / watcher signals the way it looks
        }

        // A painted item carries its design id in the key; stamp it into the descriptor's design bits so the
        // placed block shows the texture. Composes with any form above (built-in, custom or prop-stamped).
        // Only a LIVE design is honoured — an item holding a wiped/foreign id places as the plain material,
        // mirroring the custom-shape fallback. Tintable is the gate, like dye (paint is a surface cosmetic).
        if (r.BlockDef.Tintable)
        {
            int placeDesign = ItemKey.Design(r.Intent.ItemKey);
            if (IsLivePaintDesign(placeDesign))
            {
                r.Shape = ShapeCode.WithDesign(r.Shape, placeDesign);
            }
        }
    }

    /// <summary>Effect step 2: writes the placed cell (and a multi-cell form's siblings, a bed's foot half).</summary>
    private void StampPlacedCells(PlayerSession session, PlacementRequest r)
    {
        _world.SetBlock(r.Pos, r.BlockDef.NumericId, r.Tint, r.Glow, r.Shape, session.State.PlayerId);
        WriteBackStationCell(r.Pos, r.BlockDef.NumericId, r.Tint, r.Glow, r.Shape); // #1481: an interior edit is part of the station's build from now on
        if (r.FormSiblings is { Count: > 0 })
        {
            StampMultiCellSiblings(session, r.BlockDef.NumericId, r.Tint, r.Glow, r.Shape, r.FormSiblings); // #1961
        }

        if (r.BedFoot is { } footCell)
        {
            // The foot half (#1846): the same block with the partner form and the head's yaw, written, mirrored
            // and broadcast exactly like the head — the save, the wire, the station grid and the block-id based
            // home-spawn/heal scans all see two bed cells. A fluid it displaces is dropped like the head's.
            int footShape = FurnitureShapes.BedPartnerDescriptor(r.Shape);
            bool footIntoFluid = IsFluid(_world.GetBlock(footCell).Value);
            _world.SetBlock(footCell, r.BlockDef.NumericId, r.Tint, r.Glow, footShape, session.State.PlayerId);
            WriteBackStationCell(footCell, r.BlockDef.NumericId, r.Tint, r.Glow, footShape);
            BroadcastToWorld(new BlockChanged { X = footCell.X, Y = footCell.Y, Z = footCell.Z, Block = r.BlockDef.NumericId.Value, Tint = r.Tint, Glow = r.Glow, Shape = footShape });
            NudgeCreatureBodyChecks(footCell);
            if (footIntoFluid)
            {
                UntrackFluid(footCell);
                OnFluidRemoved(footCell);
            }
        }
    }

    /// <summary>Effect step 3: a sapling's growth clock, the plant event, a bred plant's species.</summary>
    private void ApplyFloraPlacementEffects(PlayerSession session, PlacementRequest r)
    {
        if (IsSapling(r.BlockDef.NumericId.Value))
        {
            ScheduleSaplingGrowth(r.Pos); // #1774: a planted sapling starts its clock
        }

        if (IsFlora(r.BlockDef.NumericId.Value))
        {
            RaiseGameEvent(session, GameEventKeys.PlantAny, r.BlockDef.Key); // #2482: the gardener course
        }

        if (r.PlantedSeed != 0)
        {
            RememberBredPlant(r.Pos, r.PlantedSeed); // #2209: which species stands here
        }
    }

    /// <summary>Effect step 4: the one block-kind hook the placed block has (an exclusive, ordered chain).</summary>
    private void ApplyBlockKindPlacementEffect(PlayerSession session, PlacementRequest r)
    {
        if (IsContainerBlock(r.BlockDef.Key))
        {
            PlaceCrate(r.Pos); // a placed storage crate/wood box becomes a lootable/stash-able container (Task 5 Stage 3b)
        }
        else if (r.BlockDef.Key == "radio_beacon")
        {
            PlaceBeacon(session, r.Pos, r.Intent.Label); // a placed beacon becomes a labelled map/compass waypoint (item 37)
        }
        else if (r.BlockDef.Key == BaseCoreBlock)
        {
            PlaceBase(session, r.Pos); // a placed base core founds a named planet base (Grundstein)
        }
        else if (r.BlockDef.Key == "beam_block")
        {
            PlaceBeam(session, r.Pos, r.Intent.Label); // a placed beam block becomes a named teleporter pad
        }
        else if (r.BlockDef.Key == SentryBlockKey)
        {
            WarnIfSentryOutsideBase(session, r.Pos); // #1699: a post outside a base zone never fires — say so
        }
        else if (r.BlockDef.Key is "station_vendor" or "mission_board" || NpcProfessions.ByPostBlock(r.BlockDef.Key) != null)
        {
            OnBasePostChanged(session, r.Pos, placed: true); // #1865: a post at home is staffed by a resident
        }
        else if (r.BlockDef.Key == BedBlock)
        {
            OnBaseBedPlaced(session, r.Pos); // #2679: a bed at the base says whether it counts (both halves stand by now)
        }
    }

    /// <summary>Effect step 5: the block joins the Crystal Net (with the client's device direction) and the rail graph.</summary>
    private void JoinPlacedBlockToNets(PlayerSession session, PlacementRequest r)
    {
        if (r.Intent.DeviceDir is >= 0 and < CrystalNetRules.DirectionCount)
        {
            r.CrystalYaw = r.Intent.DeviceDir; // #2267: the client's six-way choice (looking up / down, or the rotate cycle)
        }

        OnCrystalBlockPlaced(session, r.Pos, r.BlockDef, r.Intent.Label, r.CrystalYaw); // #2046: a conduit or device joins the Crystal Net
        OnRailBlockPlaced(session, r.Pos, r.BlockDef); // #2113: a pylon joins the rail graph (and auto-links), a stop joins its line
    }

    /// <summary>Effect step 6: broadcasts the placed block and settles the fluids around it. Uses the cell's previous
    /// block as the cell_free rule read it — the cell holds the new block by now.</summary>
    private void AnnouncePlacedBlock(PlayerSession session, PlacementRequest r)
    {
        BroadcastToWorld(new BlockChanged { X = r.Pos.X, Y = r.Pos.Y, Z = r.Pos.Z, Block = r.BlockDef.NumericId.Value, Tint = r.Tint, Glow = r.Glow, Shape = r.Shape });
        NudgeCreatureBodyChecks(r.Pos); // #1357: an animal the block landed in steps aside on its next tick
        if (IsFluid(r.BlockDef.NumericId.Value))
        {
            RegisterFluidSource(r.Pos); // placed water/lava starts flowing
            QuenchPlacedFluid(session, r.Pos, r.BlockDef.NumericId.Value, r.Existing.Value); // water on/beside lava → rock (#1284)
        }
        else if (r.IntoFluid)
        {
            // The block displaced a fluid (#851): drop the cell's flowing state — memory AND the persisted level
            // row — or a reload would resurrect the tongue on top of the new block, and wake the neighbours so a
            // stream cut off here recedes (and the body around a new underwater wall settles again).
            UntrackFluid(r.Pos);
            OnFluidRemoved(r.Pos);
        }
    }

    /// <summary>Effect step 7: a placed waterfall block or thumper starts working.</summary>
    private void StartPlacedDevice(PlayerSession session, PlacementRequest r)
    {
        if (r.BlockDef.Key == WaterSpoutBlockKey)
        {
            StartSpout(session, r.Pos); // #1726: a waterfall block starts pouring the moment it is placed
        }
        else if (r.BlockDef.Key == ThumperBlockKey)
        {
            StartThumper(session, r.Pos); // #2002: a placed thumper starts thumping
        }
    }

    /// <summary>Effect step 8: granular fall, the inventory, achievements, the build events and mission objectives.</summary>
    private void FinishPlacement(PlayerSession session, PlacementRequest r)
    {
        ActivateGranular(r.Pos); // #1319: placed sand with nothing under it drops; over lava it sinks

        SendInventory(session);
        OnAchievementBuild(session);
        RaiseGameEvent(session, GameEventKeys.BuildAny, r.BlockDef.Key); // #2465
        if (r.BlockDef.Key != BaseCoreBlock && HasGameEventListener(GameEventKeys.BaseBuilt) && InOwnOrAlliedBaseZone(session.State.PlayerId, r.Pos))
        {
            RaiseGameEvent(session, GameEventKeys.BaseBuilt, r.BlockDef.Key); // #2480: built at home (the homestead course)
        }

        if (ShapeCode.ShapeOf(r.Shape) != 0 && (r.Intent.Yaw is >= 0 and <= 3 || ShapeCode.IsValidUpFace(r.Intent.UpFace)))
        {
            RaiseGameEvent(session, GameEventKeys.BuildRotated, r.BlockDef.Key); // a form/prop turned with the rotate key
        }

        OnBlockPlaced(session, r.BlockDef, r.Pos); // #1116: advance any matching Build mission objectives
    }

    /// <summary>Auto-orients a placed shape from the surface it was built against: the shape's base rests on
    /// the first solid neighbour, preferring the floor (→ +Y up = the original ground behaviour), then walls,
    /// then the ceiling. Returns a <see cref="ShapeCode"/> up-face index (0..5); no solid neighbour → +Y.</summary>
    private int DeriveShapeUpFace(Vector3i pos)
    {
        bool Solid(int dx, int dy, int dz)
        {
            var p = WorldConstants.CanonicalBlock(new Vector3i(pos.X + dx, pos.Y + dy, pos.Z + dz), _world.Circumference);
            var b = _world.GetBlock(p);
            return !b.IsAir && !IsFluid(b.Value); // water/lava is no surface to build against (#851)
        }

        if (Solid(0, -1, 0)) return 0; // floor → +Y up (unchanged for ground placement)
        if (Solid(-1, 0, 0)) return 2; // wall → +X up
        if (Solid(1, 0, 0)) return 3;  // wall → -X up
        if (Solid(0, 0, -1)) return 4; // wall → +Z up
        if (Solid(0, 0, 1)) return 5;  // wall → -Z up
        if (Solid(0, 1, 0)) return 1;  // ceiling → -Y up
        return 0;
    }

    /// <summary>The packed shape descriptor a prop block is stamped with on placement (#909), or 0 when the
    /// key is no prop. Each <see cref="PropOrientation"/> honours exactly as much of the intent's orientation
    /// as its client-side rotate cycle offers — a pinned-back tip or an ignored quarter turn would make the
    /// placement ghost a liar.</summary>
    private int StampPropShape(PlayerSession session, PlaceBlockIntent place, string blockKey, Vector3i pos)
    {
        var cycle = PropShapes.OrientationOf(blockKey);
        if (cycle == PropOrientation.None)
        {
            return 0;
        }

        if (cycle == PropOrientation.LightMount)
        {
            // #2683: a torch or lantern takes the face the client says was clicked (the intent's up-face) — on a wall,
            // standing on the floor, or a lantern hanging from the ceiling — when the block there can hold it. An intent
            // without a face (an old client, a test, an internal placement) or one whose block is gone keeps the
            // unstamped light: upright, exactly as before. A torch under a real ceiling never gets here — HandlePlace
            // refused it before anything was consumed.
            return System.Math.Max(0, LightFormAt(blockKey, pos, place.UpFace));
        }

        int facing = place.Yaw >= 0 && place.Yaw <= 3
            ? place.Yaw
            : ((int)System.MathF.Round(session.State.Yaw / 90f)) & 3;

        if (cycle == PropOrientation.LadderMount)
        {
            // The ladder's whole orientation IS its mount face, so the intent's up-face carries it: 2..5 hug
            // that wall, anything else means free-standing (the client's fifth cycle state sends +Y). Yaw is
            // dropped on purpose — both ladder forms are square about their own axis.
            int mount = ShapeCode.IsValidUpFace(place.UpFace) ? place.UpFace : DeriveLadderMount(pos);
            var (ladderShape, ladderUp) = PropShapes.LadderForm(mount);
            return ShapeCode.Pack(ladderShape, 0, ladderUp);
        }

        if (cycle == PropOrientation.Full)
        {
            // The crafted staircase is a directional form like any shaped block: it may tip onto walls and
            // ceilings, and auto-orients against the surface it was built on when nothing was chosen.
            int upFace = ShapeCode.IsValidUpFace(place.UpFace) ? place.UpFace : DeriveShapeUpFace(pos);
            return ShapeCode.Pack(PropShapes.DefaultPlaceShape(blockKey), facing, upFace);
        }

        // Furniture turns but never tips: a bed/campfire on a wall would break its sit/heal/warmth checks.
        // A bed placed without an explicit turn (#1846) must put its foot in the cell the player FACES: the
        // raw heading index is mirrored against the geometry yaw for ±X, so it is converted here — the client
        // ghost does the same, an explicit rotate-key yaw is honoured as sent (foot = geometry direction).
        if (blockKey == BedBlock && !(place.Yaw >= 0 && place.Yaw <= 3))
        {
            facing = ShapeCode.YawFacingForward(facing);
        }

        if (blockKey == "trapdoor")
        {
            return PropShapes.TrapdoorClosed(ShapeCode.YawFacingForward(facing)); // #2264: hinged on the far side, flush with the floor
        }

        return ShapeCode.Pack(PropShapes.DefaultPlaceShape(blockKey), facing, ShapeCode.UpPlusY);
    }

    /// <summary>
    /// The cell a bed's foot half takes for a head placed at <paramref name="head"/> with the stamped
    /// <paramref name="headDescriptor"/> (#1846), when it can take it: free (air, or a fluid the bed displaces
    /// like the head does), inside reach, not the player's own head cell, and on none of the protected ground
    /// the head itself is refused on. Same Y as the head, so the build band needs no second look. The seam
    /// wrap is applied, so a bed across the world seam works like any other two-cell edit.
    /// </summary>
    private bool TryBedFootCell(PlayerSession session, Vector3i head, int headDescriptor, out Vector3i foot)
    {
        foot = head;
        if (!FurnitureShapes.TryBedPartnerOffset(headDescriptor, out int dx, out int dz))
        {
            return false;
        }

        foot = WorldConstants.CanonicalBlock(new Vector3i(head.X + dx, head.Y, head.Z + dz), _world.Circumference);
        return IsFreePartnerCell(session, foot); // the same checks a multi-cell form's sibling cells get (#1961)
    }

    /// <summary>Which wall a ladder hugs when the client sent no choice — an old client, a test, or one of the
    /// server's own internal placements. Mirrors the mesher heuristic #803 shipped with (first solid horizontal
    /// neighbour, in <see cref="ShapeCode.WallFaces"/> order), so those placements keep landing where they
    /// always did. The client normally decides this itself and sends the answer, because it can also honour
    /// the wall the player actually aimed at.</summary>
    private int DeriveLadderMount(Vector3i pos) => PropShapes.DeriveLadderMount(
        face =>
        {
            // The up-face points AWAY from the plate's support, so the wall sits at the opposite offset.
            var dir = ShapeCode.FaceDirection(face);
            var p = WorldConstants.CanonicalBlock(
                new Vector3i(pos.X - dir.X, pos.Y - dir.Y, pos.Z - dir.Z), _world.Circumference);
            return IsLadderMountWall(_world.GetBlock(p));
        },
        clickedFace: -1);

    /// <summary>A neighbour a ladder plate can hang on. The mesher additionally rules out see-through walls
    /// (glass, force fields), which the server has no flag for — the divergence only shows for a client old
    /// enough not to send its own mount face, and costs at most a plate where a pole was drawn before.</summary>
    private bool IsLadderMountWall(BlockId id)
    {
        if (id.IsAir || IsFluid(id.Value) || IsFlora(id.Value))
        {
            return false;
        }

        var def = _content.BlockById(id);
        return def is not null && def.Solid && def.Key != "ladder";
    }
}
