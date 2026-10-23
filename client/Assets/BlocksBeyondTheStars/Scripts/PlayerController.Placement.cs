// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Block placement: the orientation the next shaped block takes (cycled with the RotateShape key, ladders mount on
    /// walls — <see cref="PendingPlacement"/>), the translucent placement ghost (<see cref="UpdatePlacementGhost"/>), light
    /// and door forms, door jambs in the world and in a parked ship, and the facing a placed device takes.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The selected hotbar item has a placement orientation to cycle (RotateShape applies).</summary>
        public bool HeldRotatable => Game != null && HeldOrientable(Game.ItemInSlot(Game.SelectedHotbarSlot), out _);

        // Placement orientation override for shaped building blocks: -1 = auto (the server orients from the
        // surface built against), 0..5 = a forced up-face cycled with the RotateShape key. Sent on each place.
        private int _placeUpFace = -1;

        /// <summary>Explicit quarter-turn (0..3) for the next shaped placement; -1 = derive it from facing.</summary>
        private int _placeYaw = -1;

        // The translucent placement preview (#863) + the frame it was last refreshed. The on-foot update
        // stamps the frame; LateUpdate hides the ghost whenever a frame went by without a stamp (menu open,
        // space view, driving, seated, …) so no stale hologram lingers over the world.
        private PlacementGhost _placementGhost;
        private int _ghostFrame = -1;

        /// <summary>#2267: the direction a directional Crystal Net device (a logic block, a piston, a bridge motor, …) is
        /// placed with: looking steeply down points it down, steeply up points it up, otherwise the rotate key's quarter
        /// turn — or −1, and the server takes the player's facing as before. The device menu's Turn button changes it
        /// later.</summary>
        private int DeviceDirFor(string blockKey)
        {
            var kind = CrystalNetRules.KindOfKey(blockKey ?? string.Empty);
            if (!CrystalNetRules.IsDirectional(kind))
            {
                return -1;
            }

            if (_pitch > 50f)
            {
                return CrystalNetRules.YawDown;
            }

            if (_pitch < -50f)
            {
                return CrystalNetRules.YawUp;
            }

            return _placeYaw >= 0 && _placeYaw <= 3 ? _placeYaw : -1;
        }

        /// <summary>The shape index the held item would place: the crafted form carried in the item key
        /// (slabs, stairs, player-designed forms, …), or a prop block's server-stamped default form
        /// (bed/campfire/rug/pot, the ladder's wall plate, the crafted staircase). 0 = places a plain cube
        /// or no block at all, i.e. nothing the rotate key or the placement ghost should react to.
        /// <paramref name="cycle"/> says how far this item's orientation may be steered — the rotate key and
        /// the ghost must offer exactly what the server will honour.</summary>
        private int HeldPlaceShape(string held, out PropOrientation cycle)
        {
            cycle = PropOrientation.None;
            if (string.IsNullOrEmpty(held))
            {
                return 0;
            }

            int shape = BlocksBeyondTheStars.Shared.State.ItemKey.Shape(held);
            if (shape > 0)
            {
                // #1834: the server honours a carried form only on a Shapeable block (HandlePlace); on anything else
                // the item places a plain cube — so the ghost and the rotate key must say cube too, not pillar.
                string placed = Game?.Content?.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(held))?.PlacesBlock;
                if (!string.IsNullOrEmpty(placed) && Game.Content.GetBlock(placed) is { Shapeable: false })
                {
                    return 0;
                }

                cycle = PropOrientation.Full; // a crafted form is a building block: all 24 orientations
                return shape;
            }

            var def = Game?.Content?.GetItem(held);
            if (def == null || string.IsNullOrEmpty(def.PlacesBlock))
            {
                return 0;
            }

            cycle = PropShapes.OrientationOf(def.PlacesBlock);
            return cycle == PropOrientation.None ? 0 : PropShapes.DefaultPlaceShape(def.PlacesBlock);
        }

        /// <summary>The <see cref="BlockDefinition.Facing"/> of the plain cube block the held item places (#2124) — a
        /// vending machine, a forge, a watcher — or null when it places no block with a front (a form, a prop, an
        /// ordinary block, nothing at all).</summary>
        private string HeldFacing(string held)
        {
            if (string.IsNullOrEmpty(held) || Game?.Content == null
                || BlocksBeyondTheStars.Shared.State.ItemKey.Shape(held) > 0)
            {
                return null;
            }

            string placed = Game.Content.GetItem(BlocksBeyondTheStars.Shared.State.ItemKey.Base(held))?.PlacesBlock;
            if (string.IsNullOrEmpty(placed) || PropShapes.DefaultPlaceShape(placed) != 0)
            {
                return null;
            }

            return Game.Content.GetBlock(placed)?.Facing;
        }

        /// <summary>True when the held item places something the rotate key and the ghost can orient: a form or prop
        /// (<see cref="HeldPlaceShape"/>), or a cube with a front (#2124), which turns yaw-only like furniture.</summary>
        private bool HeldOrientable(string held, out PropOrientation cycle)
        {
            if (HeldPlaceShape(held, out cycle) > 0)
            {
                return true;
            }

            if (HeldFacing(held) != null)
            {
                cycle = PropOrientation.YawOnly;
                return true;
            }

            return false;
        }

        /// <summary>The ladder's rotate-key states, in cycle order: the four walls it can hug, then
        /// free-standing (#909). Auto sits in front of them as index -1. Its plate is a square Panel, so the
        /// quarter turns the other shapes cycle through would be four identical states here, and the two
        /// vertical up-faces are the two a ladder has no use for.</summary>
        private static readonly int[] LadderCycle = { 2, 3, 4, 5, ShapeCode.UpPlusY };

        /// <summary>One step of the rotate-key cycle. Shaped blocks and the crafted staircase walk
        /// Auto → the 24 up-face × quarter-turn orientations → Auto; furniture walks Auto → the four quarter
        /// turns → Auto (its up-face is pinned to +Y server-side, see the rotate-key comment); the ladder
        /// walks its own five mount states. <paramref name="backwards"/> reverses the walk.</summary>
        private void StepPlaceOrientation(bool backwards, PropOrientation cycle)
        {
            if (cycle == PropOrientation.LadderMount)
            {
                _placeYaw = 0; // meaningless for both ladder forms — never send a turn the server drops
                int at = _placeUpFace < 0 ? -1 : System.Array.IndexOf(LadderCycle, _placeUpFace);
                int next = at + (backwards ? -1 : 1);
                if (next >= LadderCycle.Length) { next = -1; }        // past the last state → back to Auto
                else if (next < -1) { next = LadderCycle.Length - 1; } // before Auto → wrap to the last state
                _placeUpFace = next < 0 ? -1 : LadderCycle[next];
                return;
            }

            bool yawOnly = cycle == PropOrientation.YawOnly;
            int lastUpFace = yawOnly ? 0 : 5;
            if (_placeUpFace < 0)
            {
                // Leaving Auto: forwards starts at the first orientation, backwards at the last.
                _placeUpFace = backwards ? lastUpFace : 0;
                _placeYaw = backwards ? 3 : 0;
                return;
            }

            if (yawOnly)
            {
                _placeUpFace = 0; // a stale up-face from a previously held shaped block never sticks to furniture
            }

            if (!backwards)
            {
                if (_placeYaw < 3) { _placeYaw++; }
                else if (_placeUpFace < lastUpFace) { _placeUpFace++; _placeYaw = 0; }
                else { _placeUpFace = -1; _placeYaw = -1; } // back to Auto
            }
            else
            {
                if (_placeYaw > 0) { _placeYaw--; }
                else if (_placeUpFace > 0) { _placeUpFace--; _placeYaw = 3; }
                else { _placeUpFace = -1; _placeYaw = -1; } // back to Auto
            }
        }

        /// <summary>
        /// The exact form + orientation the next place would carry, for the aim the player is holding right
        /// now. Returns false when the held item places nothing orientable, in which case the place sends the
        /// raw override fields and the server decides — same as before #909.
        /// <para>
        /// In Auto the client answers the orientation question ITSELF instead of leaving it to the server,
        /// because it knows one thing the intent cannot carry: which block FACE the player clicked. That is
        /// what makes a ladder hug the wall you aimed at rather than whichever neighbour wins a fixed scan
        /// order. The server keeps its own derivation for intents that carry no up-face (older clients, its
        /// own internal placements), so nothing depends on this being sent.
        /// </para>
        /// </summary>
        private bool PendingPlacement(string held, Vector3Int hitCell, Vector3Int placeCell,
            out int shape, out int upFace, out int yaw)
        {
            shape = HeldPlaceShape(held, out var cycle);
            upFace = _placeUpFace;
            yaw = _placeYaw;
            if (cycle == PropOrientation.LightMount)
            {
                // #2683: a torch or lantern takes the face it is placed against — the clicked face is the whole choice
                // (no rotate state, no turn; a stale rotate-key state from another block must never ride along). It is
                // sent as the up-face; the server re-checks the block there and stamps the form the ghost shows
                // (PropShapes.LightForm on both sides), or refuses a torch under a ceiling with a hint.
                shape = 0;
                yaw = 0;
                upFace = ShapeCode.FaceFromDirection(
                    placeCell.x - hitCell.x, placeCell.y - hitCell.y, placeCell.z - hitCell.z);
                return true;
            }

            if (shape <= 0)
            {
                // A cube with a front (#2124): the one thing to orient is which side the front looks at. The quarter turn
                // is a look HEADING here (CubeFacing.FrontForPlacement): the rotate key's turn, or in Auto the player's
                // own heading, settled on the client and sent explicitly so the ghost's arrow and the placed front
                // cannot disagree (the server would otherwise re-derive it from its own copy of the player's yaw).
                if (HeldFacing(held) == null)
                {
                    return false;
                }

                shape = 0;
                upFace = ShapeCode.UpPlusY;
                yaw = yaw >= 0 && yaw <= 3 ? yaw : CubeFacing.HeadingOfYaw(transform.eulerAngles.y);
                return true;
            }

            // The face the player clicked: the step from the block they aimed at to the cell being filled.
            int clicked = ShapeCode.FaceFromDirection(
                placeCell.x - hitCell.x, placeCell.y - hitCell.y, placeCell.z - hitCell.z);

            if (yaw < 0)
            {
                yaw = ((int)Mathf.Round(transform.eulerAngles.y / 90f)) & 3;
            }

            switch (cycle)
            {
                case PropOrientation.LadderMount:
                {
                    if (upFace < 0)
                    {
                        upFace = PropShapes.DeriveLadderMount(face => IsLadderMountWall(placeCell, face), clicked);
                    }

                    var form = PropShapes.LadderForm(upFace);
                    shape = form.Shape;
                    upFace = form.UpFace;
                    yaw = 0;
                    break;
                }

                case PropOrientation.YawOnly:
                    upFace = ShapeCode.UpPlusY; // the server pins it; promising anything else would be a lie
                    if (shape == (int)BlockShape.BedHead && _placeYaw < 0)
                    {
                        // Auto: the server puts the bed's foot in the cell the player faces (#1846), which for
                        // ±X is not the raw heading yaw — mirror that here or the ghost's headboard flips.
                        yaw = ShapeCode.YawFacingForward(yaw);
                    }

                    break;

                default:
                    if (upFace < 0)
                    {
                        upFace = DeriveAutoUpFace(placeCell, clicked);
                    }

                    break;
            }

            return true;
        }

        /// <summary>True when the cell on the far side of <paramref name="upFace"/> is a wall a ladder plate
        /// can hang on. The up-face points AWAY from the plate's support, so the wall sits at the opposite
        /// offset. Uses the mesher's own classification, so the preview and the drawn ladder cannot disagree.</summary>
        private bool IsLadderMountWall(Vector3Int cell, int upFace)
        {
            var dir = ShapeCode.FaceDirection(upFace);
            return ChunkMesher.IsLadderMountWall(
                Game.Content, Game.World.GetBlock(cell.x - dir.X, cell.y - dir.Y, cell.z - dir.Z));
        }

        /// <summary>Refreshes the placement ghost (#863): while a rotatable block is held and the aim ray
        /// has a place cell, the exact form + pending orientation hovers there translucently. Runs only on
        /// the on-foot path — every other state (menus, space view, driving, seated) is caught by the
        /// frame-stamp check in <see cref="LateUpdate"/>, which hides the ghost after any frame that never
        /// reached this method. Parked-ship cells get no block ghost: placing there routes through a structure
        /// edit whose own rules (hull protection) this preview cannot promise. A held DOOR shows there too (#2607).</summary>
        private void UpdatePlacementGhost()
        {
            _ghostFrame = Time.frameCount;
            string held = Game != null ? Game.ItemInSlot(Game.SelectedHotbarSlot) : null;
            bool rotatable = HeldOrientable(held, out _);
            if (Game != null)
            {
                // Drives the HUD's "R — rotate" control hint. Answered from the held item alone, so the hint
                // does not flicker while the crosshair sweeps past the sky.
                Game.HoldingRotatableBlock = rotatable;
            }

            // #2147: a closed door under the crosshair takes the right-click (a how-to hint), so nothing hovers behind it.
            if ((rotatable || HeldDoorKind(held) != null || HeldLightBlock(held) != null) && TryAimDoor(out int aimedDoor, out _)
                && Game?.Views.Doors != null && !Game.Views.Doors.IsDoorOpen(aimedDoor))
            {
                _placementGhost?.Hide();
                return;
            }

            // A held door (#1975): nothing to rotate, but the server hangs a real door — show it in the target cell,
            // turned by the wall beside it (the shared placed-door rule), so its facing is no surprise. #2607: aboard a
            // parked ship too (its cabin, the walkable interior in space) — "preview everywhere": a door built there is a
            // real door (#2119), and the ghost reads the jambs from the grid the place click will build into.
            if (!rotatable && HeldDoorKind(held) is { } doorKind)
            {
                bool intoShip = false;
                bool aimed = AimTarget(out _, out var doorCell, out var doorShip);
                if (aimed && doorShip != null && DoorGoesIntoShip(doorShip, doorCell))
                {
                    intoShip = true;
                }

                // The server builds into a parked ship (or a construction site) for its owner only — on someone else's
                // the place is refused, so nothing hovers there either.
                if (!aimed || (intoShip && doorShip.OwnerId != Game.LocalPlayerId))
                {
                    _placementGhost?.Hide();
                    return;
                }

                System.Func<int, int, int, bool> jamb = IsDoorJambCell;
                if (intoShip)
                {
                    jamb = (x, y, z) => IsShipDoorJambCell(doorShip, x, y, z);
                }

                bool axisX = DoorProbe.AxisForPlacedDoor(jamb, doorCell.x, doorCell.y, doorCell.z, transform.eulerAngles.y);
                _placementGhost ??= new PlacementGhost();
                _placementGhost.ShowDoor(doorCell, doorKind, axisX);
                return;
            }

            // #2683: a held torch or lantern previews the form the face under the crosshair gives it — on the wall,
            // standing, or (a lantern) hanging from the ceiling. A torch aimed at a ceiling shows nothing: the server
            // refuses it with a hint. A light is not "rotatable" (nothing to cycle), so this comes before that gate.
            if (!rotatable && HeldLightBlock(held) is { } lightKey)
            {
                if (!AimTarget(out var lightHit, out var lightCell, out var lightShip, PlaceFluidAim()) || lightShip != null)
                {
                    _placementGhost?.Hide();
                    return;
                }

                int lightForm = LightFormFor(lightKey, lightHit, lightCell);
                if (lightForm == PropShapes.LightRefused)
                {
                    _placementGhost?.Hide();
                    return;
                }

                bool lantern = PropShapes.IsLanternKey(lightKey);
                _placementGhost ??= new PlacementGhost();
                _placementGhost.ShowLight(lightCell, lantern, PropShapes.LightMountOf(lightForm, lantern));
                return;
            }

            // Same fluid-aware target as the place click (#1353): a held slab/stair gets its ghost over water
            // and lava, where the click sets the block into the fluid cell.
            if (!rotatable || !AimTarget(out var hitCell, out var placeCell, out var aimedShip, PlaceFluidAim()) || aimedShip != null
                || !PendingPlacement(held, hitCell, placeCell, out int shape, out int upFace, out int yaw))
            {
                _placementGhost?.Hide();
                return;
            }

            // Shows exactly what the place will send (PendingPlacement feeds both), so the hologram cannot
            // promise a form or an orientation the placed block then contradicts.
            _placementGhost ??= new PlacementGhost();
            if (shape == 0)
            {
                // A cube with a front (#2124): the arrow stands on the face the server will make the front — #2267: on the top
                // or bottom face for a Crystal Net device placed while looking steeply up or down.
                int deviceDir = DeviceDirFor(Game.Content?.GetItem(held)?.PlacesBlock);
                _placementGhost.ShowFacingCube(placeCell, deviceDir == CrystalNetRules.YawUp ? ShapeCode.UpPlusY
                    : deviceDir == CrystalNetRules.YawDown ? 1
                    : CubeFacing.FrontForPlacement(HeldFacing(held), yaw, transform.eulerAngles.y));
            }
            else
            {
                _placementGhost.Show(placeCell, shape, yaw, upFace);
            }
        }

        /// <summary>#2683: the torch or lantern block the held item places, or null when it places no light.</summary>
        private string HeldLightBlock(string held)
        {
            if (string.IsNullOrEmpty(held) || Game?.Content == null)
            {
                return null;
            }

            string placed = Game.Content.GetItem(held)?.PlacesBlock;
            return PropShapes.IsLightMountKey(placed) ? placed : null;
        }

        /// <summary>#2683: the descriptor a light placed into <paramref name="placeCell"/> by clicking
        /// <paramref name="hitCell"/> will be stamped with — <see cref="PropShapes.LightForm"/> over the client's world, the
        /// same function and the same "can this block hold a light" reading the server stamps with.</summary>
        private int LightFormFor(string lightKey, Vector3Int hitCell, Vector3Int placeCell)
        {
            int clicked = ShapeCode.FaceFromDirection(placeCell.x - hitCell.x, placeCell.y - hitCell.y, placeCell.z - hitCell.z);
            return PropShapes.LightForm(lightKey, clicked, face =>
            {
                var dir = ShapeCode.FaceDirection(face); // the up-face points AWAY from the block holding the light
                var id = Game.World.GetBlock(placeCell.x - dir.X, placeCell.y - dir.Y, placeCell.z - dir.Z);
                return !id.IsAir && PropShapes.HoldsLight(Game.Content.BlockById(id));
            });
        }

        /// <summary>The door kind the held item places, or null when it places no door (#1975).</summary>
        private string HeldDoorKind(string held)
        {
            if (string.IsNullOrEmpty(held) || Game?.Content == null)
            {
                return null;
            }

            string placed = Game.Content.GetItem(held)?.PlacesBlock;
            return DoorBlocks.IsDoorBlock(placed) ? DoorBlocks.KindForBlock(placed) : null;
        }

        /// <summary>The world grid as the door probe reads it: a real wall is a jamb, a flower is not (#2146) — the
        /// server's own rule, so the ghost shows the door as it will hang.</summary>
        private bool IsDoorJambCell(int x, int y, int z)
        {
            var id = Game.World.GetBlock(x, y, z);
            return !id.IsAir && DoorProbe.IsJamb(Game.Content?.BlockById(id));
        }

        /// <summary>#2607: a parked ship's grid as the door probe reads it for a door built into the ship — every block
        /// of the ship is a jamb (the server's ship rule, <c>GameServer.ShipDoorAxis</c>).</summary>
        private bool IsShipDoorJambCell(LandedShipModel ship, int x, int y, int z)
            => !Game.LandedShipBlockAt(x, y, z, out var at, out _).IsAir && at == ship;

        /// <summary>#2607: whether a door aimed at a parked ship's cell goes INTO the ship (a structure edit) rather than
        /// into the world beside it — the place click's own routing: the target cell inside the ship's bounds, the ship
        /// a construction site, or the own ship grown from inside it (#2120).</summary>
        private bool DoorGoesIntoShip(LandedShipModel ship, Vector3Int placeCell)
            => Game.LandedShipBoundsAt(placeCell.x, placeCell.y, placeCell.z, out _) == ship
               || ship.StructureId.StartsWith("shipyard:", System.StringComparison.Ordinal)
               || (ship.OwnerId == Game.LocalPlayerId && StandingInShip(ship));

        /// <summary>The up-face for an Auto placement: the shape's base rests on the surface it was built
        /// against — the floor first (→ +Y up, the common case of laying a slab on the ground), then the WALL
        /// THE PLAYER CLICKED, then the remaining walls in the server's fixed scan order, then the ceiling.
        /// Fluids are no surface to build against, same as the server.
        /// <para>
        /// The clicked-face preference (#909) is the one thing the client can answer better than
        /// <c>GameServer.DeriveShapeUpFace</c>, whose intent carries no aim: it is what lets you build a ramp
        /// against the wall you are looking at instead of whichever neighbour the scan order happens to reach
        /// first. The floor still wins over it — extending a floor by clicking the side of the last slab must
        /// keep laying it flat, not stand it up against that slab.
        /// </para></summary>
        private int DeriveAutoUpFace(Vector3Int cell, int clickedFace)
        {
            bool Supported(int face)
            {
                var dir = ShapeCode.FaceDirection(face);
                var id = Game.World.GetBlock(cell.x - dir.X, cell.y - dir.Y, cell.z - dir.Z);
                return !id.IsAir && !IsFluidBlock(id);
            }

            if (Supported(ShapeCode.UpPlusY))
            {
                return ShapeCode.UpPlusY; // floor below → upright
            }

            if (clickedFace >= 2 && clickedFace <= 5 && Supported(clickedFace))
            {
                return clickedFace; // the wall under the crosshair
            }

            foreach (int face in ShapeCode.WallFaces)
            {
                if (Supported(face))
                {
                    return face;
                }
            }

            return Supported(1) ? 1 : ShapeCode.UpPlusY; // ceiling, else nothing to rest on
        }

        /// <summary>Localized, kid-readable label for the pending orientation: "Auto", or an up-face word
        /// (upright / upside down / on its side) plus the quarter-turn in degrees — instead of the old
        /// axis-speak ("+X · 90°") nobody without a maths degree could predict (#863). The placement ghost
        /// shows the exact result; this label just needs to say roughly what changed.</summary>
        private string OrientationLabel(PropOrientation cycle)
        {
            var loc = Game?.Localizer;
            if (_placeUpFace < 0)
            {
                return loc?.Get("hud.shape.auto") ?? "Auto";
            }

            if (cycle == PropOrientation.LadderMount)
            {
                // Which of the four walls it is naming would need compass words nobody can map to a key press —
                // the ghost hanging on that exact wall says it better than any label could.
                return _placeUpFace >= 2
                    ? loc?.Get("hud.shape.on_wall") ?? "On the wall"
                    : loc?.Get("hud.shape.free_standing") ?? "Free-standing";
            }

            int upFace = cycle == PropOrientation.YawOnly ? 0 : _placeUpFace;
            string word = upFace switch
            {
                0 => loc?.Get("hud.shape.upright") ?? "Upright",
                1 => loc?.Get("hud.shape.upside_down") ?? "Upside down",
                _ => loc?.Get("hud.shape.sideways") ?? "On its side",
            };
            return _placeYaw > 0 ? $"{word} · {_placeYaw * 90}°" : word;
        }
    }
}
