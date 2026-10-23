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
    /// Interaction: the device-neutral applicability probes the touch layer and the Actions list poll, the mine / place
    /// click (<see cref="HandleInteract"/>) with the drill's sound and mine hits, looting and depositing at the
    /// nearest container, feeding animals, repairing the own ship or a wreck, gadgets (<see cref="UseGadget"/>), the
    /// crosshair ping (<see cref="PingAtCrosshair"/>) and the hotbar keys (<see cref="HandleHotbar"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        // ---- Applicability probes for device-neutral UI (#1042/#1043) --------------------------------
        // The touch layer and the context-actions list show a verb only while it would do something. Each
        // probe mirrors the precondition its verb's handler checks, so a button never offers a dead action.
        // They are cheap (a few list scans) and are polled once per frame at most.

        /// <summary>A lootable container is within loot reach (LootContainer applies) — #2726: not a cache that belongs to a
        /// village's people (the HUD says whose it is, the server refuses the take), so the Actions list does not offer it.</summary>
        public bool NearContainer => Game != null
            && InteractReach.NearestContainer(Game.Containers, transform.position.x, transform.position.y, transform.position.z,
                Game.Circumference, crateOnly: false) is { } nearest
            && string.IsNullOrEmpty(nearest.OwnedBy);

        /// <summary>#2018: the selected hotbar item is food an animal would eat AND a begging animal is within
        /// <c>HerdRules.FeedProbeRange</c> (FeedCreature applies). The creature list arrives at 2 Hz, so the scan over it
        /// is cached for a quarter second — the probe is polled by the HUD, the touch button and the actions list every frame.</summary>
        public bool CanFeedCreature
        {
            get
            {
                if (Time.time >= _nextFeedProbeAt)
                {
                    _nextFeedProbeAt = Time.time + 0.25f;
                    _canFeedCreature = ComputeCanFeedCreature();
                }

                return _canFeedCreature;
            }
        }

        private float _nextFeedProbeAt;
        private bool _canFeedCreature;

        private bool ComputeCanFeedCreature()
        {
            if (Game?.Content == null || Game.Creatures == null)
            {
                return false;
            }

            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            if (string.IsNullOrEmpty(held) || !BlocksBeyondTheStars.Shared.Definitions.HerdRules.IsFoodForCreatures(Game.Content.GetItem(held)))
            {
                return false;
            }

            const float range = BlocksBeyondTheStars.Shared.Definitions.HerdRules.FeedProbeRange;
            float rangeSq = range * range;
            foreach (var c in Game.Creatures)
            {
                if (c.Begging && (Game.ScenePos(c.X, c.Y, c.Z) - Game.PlayerPosition).sqrMagnitude <= rangeSq)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A storage crate is within reach (DepositToCrate applies).</summary>
        public bool NearCrate => NearestContainerId(crateOnly: true) != null;

        /// <summary>H stows into the ship's cargo hold (#2138): aboard — in the landed
        /// cabin or the floating interior — with no storage crate in reach, the same server "stow all" as the
        /// Inventory tab's button. A crate in reach keeps H for itself. #2725: never as a guest in a docked friend's ship —
        /// its hold is the host's, and ours floats elsewhere.</summary>
        public bool CanStowToCargo
            => Game != null && (Game.Aboard || Game.LoadingPlanetType == "ship_interior") && !NearCrate && !Game.Visiting;

        /// <summary>A wreck is registered for repair (RepairWreck applies — the aim check happens on press).</summary>
        public bool NearWreck => Game != null && Game.Wreck != null;

        /// <summary>At the own ship's cockpit or console while the repair panel is up: RepairWreck repairs the SHIP here
        /// (#1561). Also what puts "Repair" into the Actions list for pads and touch, which have no R.</summary>
        public bool CanRepairOwnShip
            => Game != null && Game.ShipRepair != null && (Game.NearbyStation == "cockpit" || Game.NearbyStation == "console");

        /// <summary>The container G / H work on — the loot / deposit reach the server checks, seam-aware. The HUD's crate line
        /// names the same one (#2613: <see cref="InteractReach.NearestContainer"/> is the one picker for both).</summary>
        private string NearestContainerId(bool crateOnly)
        {
            if (Game == null)
            {
                return null;
            }

            var p = transform.position;
            return InteractReach.NearestContainer(Game.Containers, p.x, p.y, p.z, Game.Circumference, crateOnly)?.Id;
        }

        private void LootNearestContainer()
        {
            if (Game?.Network == null)
            {
                return;
            }

            string nearest = NearestContainerId(crateOnly: false);
            if (nearest != null)
            {
                // Success cue moved to the container-broadcast diff (#751): playing it here, before the
                // request even left, made a rejected/no-op loot sound exactly like a successful one.
                Game.NoteLootRequested(nearest);
                Game.Network.SendLootContainer(nearest);
            }
        }

        /// <summary>Stash loose materials into the nearest storage crate (Task 5 Stage 3b).</summary>
        private void DepositToNearestCrate()
        {
            if (Game?.Network == null)
            {
                return;
            }

            string nearest = NearestContainerId(crateOnly: true);
            if (nearest != null)
            {
                ClientAudio.Instance?.Cue("loot");
                Game.Network.SendDepositContainer(nearest);
            }
            else if (CanStowToCargo)
            {
                // The ship's cargo cell is a plain crate BLOCK, not a storage container — H used to do nothing
                // there. The server answers with what it stowed (or why nothing moved).
                ClientAudio.Instance?.Cue("loot");
                Game.Network.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true);
            }
        }

        /// <summary>Keeps the mining loop alive while the player holds left-click: a drill cuts any block it is
        /// allowed to, while bare hands can only keep digging the soft, hand-mineable blocks (earth, sand,
        /// plants) — hard materials need a drill. (#128)</summary>
        private void HandleDrillAudio()
        {
            if (Camera == null || !InputMap.PrimaryHeld())
            {
                return;
            }

            bool drill = HoldingDrill();
            // A weapon/scanner left-click is an attack/scan (handled as a tap in HandleInteract), never a mine hold.
            if (!drill && (HoldingWeapon() || HoldingScanner()))
            {
                return;
            }

            // Target via the voxel grid, NOT Physics.Raycast: right after a block breaks the chunk collider is
            // rebuilt, and a raycast against it can miss for a frame — which stalled the WHOLE drill (no tick, no
            // mine, no sparks) until it settled, then everything resumed. That stall was the "mining gets stuck,
            // then a block suddenly mines and the stuck ones work too" bug (B32). The voxel world never stalls.
            // Same fluid-aware target as the click (#1353): a tier-3 drill that needs two hits on lava used to
            // tap the lava cell and then, held, march through it to the rock behind — the lava never broke.
            // A parked-ship cell is a structure edit, which stays a per-click action (no hold-drilling hulls).
            // #1746: a player-built door is an entity standing in an AIR cell, so the voxel march never sees it —
            // test the doors first. One the ray reaches before any solid cell is the target, and it comes out by
            // hand (the server hands the item back), so the hand-mineable gate below does not apply to it.
            bool doorAimed = TryAimDoor(out var hitCell);
            var face = Camera != null ? -Camera.transform.forward : Vector3.up; // the struck face's normal (#2155)
            if (!doorAimed)
            {
                if (!AimTarget(out hitCell, out var placeCell, out var aimedShip, HeldToolFluidAim()) || aimedShip != null)
                {
                    return;
                }

                var toPlace = placeCell - hitCell;
                if (toPlace.sqrMagnitude == 1)
                {
                    face = toPlace;
                }

                // By hand, only soft hand-mineable blocks keep digging; hard blocks reject without a drill, so don't
                // hammer the server with a hold the server will only refuse (the initial tap already surfaces the hint).
                if (!drill && !IsHandMineable(hitCell))
                {
                    return;
                }
            }

            TriggerSwing(); // keep the mining chop going while held
            if (drill)
            {
                ClientAudio.Instance?.DrillTick();
            }

            // #2155: chips, sparks, dust or the mining beam come off the STRUCK FACE in the block's own colour and the
            // drill's own look (data-driven fx) — they used to burst out of the middle of the solid block.
            string heldKey = Game.ItemInSlot(Game.SelectedHotbarSlot);
            if (Time.time >= _nextDrillSpark && Camera != null)
            {
                _nextDrillSpark = Time.time + (drill ? 0.07f : 0.09f);
                var look = drill ? FxLook.ForItem(Game.Content, heldKey) : new FxLook("hand", new Color(0.62f, 0.57f, 0.47f), Color.white, 0f, 1f, 0f);
                MiningFx.Instance?.DrillTick(look, hitCell, face, Muzzle(Camera.transform), drill);
            }

            // Hard blocks need several hits — keep sending mine attempts while held (the server accumulates
            // effort until the block breaks); soft hand-digging breaks in one but the loop lets you sweep along.
            if (Time.time >= _nextDrillMine)
            {
                SendMineHit(hitCell, drill);
                var faceCenter = new Vector3(hitCell.x + 0.5f, hitCell.y + 0.5f, hitCell.z + 0.5f) + face * 0.5f;
                if (Camera != null)
                {
                    SendFx(BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Mine, drill ? heldKey : string.Empty, Muzzle(Camera.transform), faceCenter, true);
                }
            }
        }

        /// <summary>Sends ONE mine attempt and arms the shared cooldown. Both mining paths — the click in
        /// <see cref="HandleInteract"/> and the hold loop in <see cref="HandleDrillAudio"/> — go through here
        /// (#965): they ran in the SAME Update, and because only the hold loop armed the cooldown, the first
        /// frame of every click sent the cell TWICE. The second intent hit air on the server, which answered
        /// with a ghost-block heal + a full chunk re-stream — roughly one per block mined.</summary>
        private void SendMineHit(Vector3Int cell, bool drill)
        {
            _nextDrillMine = Time.time + (drill ? 0.28f : 0.22f); // slower, weightier mining (was 0.18)
            Game.LastMineCell = cell; // so an "already empty" rejection can clear the ghost here (B32)
            Game.Network?.SendMine(cell.x, cell.y, cell.z);
        }

        /// <summary>True when the block at a cell can be broken by bare hands — mineable and requiring no tool
        /// (earth, sand, plants). Hard materials declare a required tool and are excluded. (#128)</summary>
        private bool IsHandMineable(Vector3Int cell)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            var def = Game.Content.BlockById(Game.World.GetBlock(cell.x, cell.y, cell.z));
            return def != null && def.Mineable
                && def.RequiredTool == BlocksBeyondTheStars.Shared.Definitions.ToolKind.None;
        }

        private float _nextDrillSpark;
        private float _nextDrillMine;

        /// <summary>True if the selected hotbar item is a drill (its primary action mines).</summary>
        private bool HoldingDrill()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Drill;
        }

        /// <summary>True if the selected hotbar item places a block — the aim ray then stops at a fluid surface
        /// so the block can be set INTO the water/lava cell (#1310).</summary>
        private bool HeldItemPlacesBlock()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && !string.IsNullOrEmpty(Game.Content?.GetItem(held)?.PlacesBlock);
        }

        /// <summary>Right-click use of a held gadget (item 36): sends the use intent at the aim point and plays
        /// the local effect + sound. The server validates suit energy + cooldown and applies the real effect.</summary>
        private void UseGadget(string key)
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            // Aim point: the block under the crosshair, else a point a few metres ahead of the camera.
            // #2268: the remote control pairs with a receiver aboard a parked ship too — its march tests the ships.
            bool aimed = key == "remote_control" ? AimTarget(out var cell, out _, out _) : AimBlock(out cell, out _, includeFluids: true);
            Vector3 target = aimed
                ? new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f)
                : transform.position + Camera.transform.forward * 5f;

            // The blueprint tool (#1117) is fully client-orchestrated (corner marking + its own intents) —
            // it never sends the generic gadget intent, and it needs an aimed BLOCK, not a point in the air.
            if (key == "blueprint_tool")
            {
                if (aimed)
                {
                    Game?.Context.Panel<BlueprintToolUi>()?.UseAt(cell);
                }

                return;
            }

            Game.Network.SendUseGadget(key, target);

            // #2151: only the device's own charge glow plays right away. The heal / freeze / blast / scan wave itself is
            // drawn when the server CONFIRMS the use (ActionFx with the outcome flag, see FxRemote) — a refused use
            // (no energy, cooling down) used to show the full effect anyway.
            var device = Muzzle(Camera.transform);
            FxGadgets.Intent(FxLook.ForItem(Game.Content, key), device);
        }

        /// <summary>R at the cockpit or the ship console while the repair panel is up: asks the server for the
        /// full repair (hull + breach cells, paid in metal — it answers with the material chat line when short).
        /// Returns false when nothing applies, so R keeps its wreck meaning everywhere else (#1561).</summary>
        private bool RepairOwnShipAtConsole()
        {
            if (Game?.Network == null || !CanRepairOwnShip)
            {
                return false;
            }

            Game.Network.SendRepairShip("all");
            return true;
        }

        /// <summary>Fills the targeted breach cell of a crashed wreck with the selected hotbar block (server validates).</summary>
        private void RepairWreckCell()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
            if (string.IsNullOrEmpty(item))
            {
                return; // need a block in hand to rebuild the hull
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach))
            {
                return;
            }

            // Fill the empty cell against the hit face — the server checks it against the wreck's intact mask.
            var t = FloorVec(hit.point + hit.normal * 0.5f);
            Game.Network.SendRepairWreck(t.x, t.y, t.z, item);
        }

        private void HandleHotbar()
        {
            if (Game == null)
            {
                return;
            }

            int pick = InputMap.HotbarSlotDown();
            if (pick >= 0 && pick < HotbarSlots)
            {
                SelectSlot(pick);
            }

            float scroll = InputMap.HotbarScroll();
            if (scroll > 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + HotbarSlots - 1) % HotbarSlots);
            }
            else if (scroll < 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + 1) % HotbarSlots);
            }
        }

        private void SelectSlot(int slot)
        {
            if (slot == Game.SelectedHotbarSlot)
            {
                return;
            }

            // #2525: the network client keeps the slot (Game.SelectedHotbarSlot reads it) and re-sends it on every join.
            Game.Network?.SendSelectHotbar(slot);
        }

        /// <summary>Sends a "look here" ping (#1217) at whatever the crosshair rests on: the aimed voxel when one
        /// is in reach, else a physics hit up to 60 m out, else a spot a dozen metres ahead — so pointing at a
        /// mountainside across the valley works, and pointing at the sky still pings something sensible.</summary>
        private void PingAtCrosshair()
        {
            if (Game?.Network == null)
            {
                return;
            }

            Vector3 at;
            if (AimBlock(out var cell, out _, includeFluids: true))
            {
                at = new Vector3(cell.x + 0.5f, cell.y + 0.5f, cell.z + 0.5f);
            }
            else if (Camera != null
                     && Physics.Raycast(Camera.transform.position, Camera.transform.forward, out var hit, 60f, ~0, QueryTriggerInteraction.Ignore)
                     && hit.collider != _controller)
            {
                at = hit.point;
            }
            else
            {
                at = transform.position + transform.forward * 12f;
            }

            Game.Network.SendMarkerPing(at.x, at.y, at.z);
            Weapons?.Pulse(at, new Color(1f, 0.9f, 0.4f));
            Game.ShowMessage(Game.Localizer?.Get("hud.ping.sent") ?? "Ping!");
        }

        private void HandleInteract()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            bool mine = InputMap.PrimaryDown();
            bool place = InputMap.SecondaryDown();
            if (!mine && !place)
            {
                return;
            }

            // Left-click with the optic raised puts it away instead of swinging at whatever the magnified
            // crosshair happens to cover — the natural "lower the binoculars and get to work" gesture.
            if (mine && _optic != null && _optic.Raised)
            {
                _optic.Lower();
                return;
            }

            // #2321: the energy rope gun shoots with the primary button, like every weapon (it is a pistol) — the one
            // gadget whose use is not the right click. The pull is the held secondary, read in Move().
            if (mine && HoldingRopeGun())
            {
                FireRope();
                return;
            }

            // Holding a scanner turns the primary action into a scan (select it in the hotbar, then aim + click).
            if (mine && HoldingScanner())
            {
                ScanTarget();
                return;
            }

            // Holding a weapon turns the primary action (left-click) into an attack — the same swing as F —
            // so a melee weapon like the machete actually hits creatures instead of trying to mine a block.
            // Gated by the weapon's swing cooldown so it can't be spam-clicked (the machete's 1.5s, etc.).
            if (mine && HoldingWeapon())
            {
                if (WeaponSwingReady())
                {
                    TriggerSwing(); // first: the gloves pick the punching hand the effect starts at (#2278)
                    AttackNearestEnemy();
                }

                return;
            }

            // Right-click a held consumable → eat/use it (no aiming needed); the server applies the effect and
            // the client plays an eat sound. Consumables don't place a block, so right-click is free for this (B16).
            if (place)
            {
                string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var hdef = string.IsNullOrEmpty(held) ? null : Game.Content?.GetItem(held);
                // #2202: a preparation of the bio lab is taken the same way. The FULL key goes out — its compound rides
                // in the key and the server finds the stack by it. (The coating is no consumable: it goes onto a tool.)
                if (hdef != null && hdef.Category.ToString() == "Consumable")
                {
                    Game.Network?.SendConsume(held);
                    ClientAudio.Instance?.Cue("eat");
                    return;
                }

                // Right-click the held suit teleporter → the destination picker (#1056): back to the ship, or
                // to an allied player on this body. (Until #1056 the click recalled instantly — the item had
                // no client-side trigger at all before #414 N17.) The server validates ownership, suit energy,
                // the cooldown and the alliance, and answers with a RespawnNotice snap (or a reject toast).
                if (held == "suit_teleporter")
                {
                    var teleporter = Game?.Context.Panel<TeleporterUi>();
                    if (teleporter != null) teleporter.Open();
                    else Game.Network?.SendTeleportToShip();
                    return;
                }

                // Right-click the camera → photograph the current view (HUD-free), saved to disk locally.
                // Handled entirely on the client (no server round-trip), so it's intercepted before the
                // generic gadget path below.
                if (held == "camera")
                {
                    EnsureCameraTool().TryCapture();
                    return;
                }

                // Right-click the binoculars → raise the optic / step the magnification. Also client-only:
                // the zoom costs nothing and the thermal contacts are all drawn from state the client already
                // has, so there is nothing for the server to validate.
                if (held == "binoculars" || held == "thermal_binoculars")
                {
                    EnsureOptic().Step();
                    return;
                }

                // Right-click the paint tool on a block → open the 32×32 paint editor for that cell (#818).
                // World blocks only for now — parked-ship cells keep their hull paint (structure designs are
                // a follow-up); the server enforces solidity, reach and protection on Apply anyway.
                if (held == "paint_tool")
                {
                    if (AimTarget(out var paintCell, out _, out var paintShip) && paintShip == null)
                    {
                        PaintToolUi.Instance?.OpenFor(paintCell);
                    }

                    return;
                }

                // Right-click the shaping tool → the form editor (#845). Aimed at a block that already
                // carries a player-designed form it opens pre-loaded with THAT form, which is how forms are
                // copied off other people's builds; anywhere else it starts on an empty grid.
                // A stencil hands a form to whoever holds it: right-click puts it in their own library (#846).
                if (BlocksBeyondTheStars.Shared.State.ItemKey.Base(held) == "shape_stencil")
                {
                    ShapeToolUi.Instance?.UseStencil(held);
                    return;
                }

                if (held == "shape_tool")
                {
                    bool copied = AimTarget(out var formCell, out _, out var formShip)
                        && formShip == null
                        && (ShapeToolUi.Instance?.OpenForCell(formCell) ?? false);
                    if (!copied)
                    {
                        ShapeToolUi.Instance?.OpenNew();
                    }

                    return;
                }

                if (held == RopeRules.ItemKey)
                {
                    return; // #2321: the rope gun's right button reels in (held, see Move()) — never a second shot
                }

                // Right-click a held gadget (item 36) → use it: the medkit heals around you, the stasis
                // projector + terrain blaster act at the aim point. The server validates energy + cooldown.
                if (hdef?.Tool != null && hdef.Tool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Gadget)
                {
                    UseGadget(held);
                    return;
                }
            }

            // Target the block under the crosshair by ray-marching the voxel world itself, not a Physics.Raycast.
            // The collider is a mesh that gets rebuilt right after every dig; a raycast against it can silently
            // miss a block that's clearly there (the rebuild's re-cook, a seam, or just its shorter reach) — which
            // is exactly the "I aim at the next block and nothing happens" bug (B32). The voxel grid is the source
            // of truth and always in sync, so this never silently fails when a block is in front of you.
            // Parked ship OBJECTS (ship-as-object) live outside the world grid, so the march tests them too.
            // Water/lava surfaces are targets too (#1310) — for a placeable item (the block displaces the fluid,
            // #851) and for a tool that can actually mine a fluid (a tier-3 drill: mining beam, diamond drill);
            // anything else keeps aiming through them, so a basic drill still reaches the rock under a pond.
            // #1746: a door the player built stands in an air cell the voxel march below cannot see. When the
            // aim ray crosses one before any solid cell, that door is the thing to mine. Mining only — a block is
            // still placed against real geometry, never "on" a door.
            if (mine && TryAimDoor(out var doorCell))
            {
                SendMineHit(doorCell, HoldingDrill());
                TriggerSwing();
                return;
            }

            // #2147: a right-click at a CLOSED door — the Minecraft habit; here doors open with E — builds nothing
            // behind it and says how this door opens instead. An open doorway stays free to build past (the door's
            // own cells are refused by the server either way, #2145).
            if (place && TryAimDoor(out int clickedDoor, out _)
                && DoorView.Instance != null && !DoorView.Instance.IsDoorOpen(clickedDoor))
            {
                ShowDoorHowTo(clickedDoor);
                return;
            }

            if (!AimTarget(out var hitCell, out var placeCell, out var aimedShip,
                    fluidSurfaces: mine ? HeldToolFluidAim() : PlaceFluidAim()))
            {
                return;
            }

            if (mine)
            {
                if (aimedShip != null)
                {
                    // A parked ship's cell: a structure edit, not a world dig. The server enforces the rules
                    // (hull + modules protected; only player-added blocks come out again).
                    var l = ShipLocal(aimedShip, hitCell);
                    Game.Network.SendStructureEdit(aimedShip.StructureId, l.x, l.y, l.z, mine: true);
                    TriggerSwing();
                    return;
                }

                SendMineHit(hitCell, HoldingDrill()); // arms the cooldown too — see SendMineHit (#965)
                TriggerSwing();
            }
            else
            {
                // Place the item in the selected hotbar slot — only if it actually places a
                // block (tools like the drill/scanner don't), so we don't spam server rejects.
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var def = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item);
                if (def != null && !string.IsNullOrEmpty(def.PlacesBlock))
                {
                    // Placing INSIDE a parked ship furnishes the cabin: route to a structure edit (the
                    // block becomes part of the ship and persists with it), not a world place. Only when the
                    // aim ray hit THAT ship, though — the bounding box also covers the ground-level air ring
                    // around the hull, and rerouting a place that targets a world block (the ground beside a
                    // parked ship) made any block "unplaceable" there: a foreign ship answers none_here, the
                    // own hull no_anchor (#1023). Aiming at the ground → world place; the server still guards
                    // the real interior authoritatively.
                    var boundsShip = Game.LandedShipBoundsAt(placeCell.x, placeCell.y, placeCell.z, out var lp);
                    if (boundsShip != null && boundsShip == aimedShip)
                    {
                        // Furnishing a cabin sends the same orientation a world place would (#1943), so a bed
                        // built aboard lies the way its ghost showed instead of stamping as a cube.
                        bool shipOriented = PendingPlacement(item, hitCell, placeCell, out _, out int shipUp, out int shipYaw);
                        Game.Network.SendStructureEdit(boundsShip.StructureId, lp.X, lp.Y, lp.Z, mine: false, item,
                            upFace: shipOriented ? shipUp : _placeUpFace,
                            yaw: shipOriented ? shipYaw : _placeYaw, deviceDir: DeviceDirFor(def.PlacesBlock));
                        TriggerSwing();
                        return;
                    }

                    // Growing a ship CONSTRUCTION SITE (#948): aiming at the half-built hull but the target
                    // cell lies outside its current bounds — still a structure edit (the server re-anchors
                    // the grid and re-broadcasts), never a world place next to it.
                    if (aimedShip != null && aimedShip.StructureId.StartsWith("shipyard:", System.StringComparison.Ordinal))
                    {
                        var gl = ShipLocal(aimedShip, placeCell);
                        bool siteOriented = PendingPlacement(item, hitCell, placeCell, out _, out int siteUp, out int siteYaw);
                        Game.Network.SendStructureEdit(aimedShip.StructureId, gl.x, gl.y, gl.z, mine: false, item,
                            upFace: siteOriented ? siteUp : _placeUpFace,
                            yaw: siteOriented ? siteYaw : _placeYaw, deviceDir: DeviceDirFor(def.PlacesBlock));
                        TriggerSwing();
                        return;
                    }

                    // #2120: building onto your OWN ship past its design box grows the ship (the server keeps it within
                    // 15 × 15 × 15 and out of the ground) — but only from inside it: in the ship interior out in space,
                    // or standing within its cells on a planet. A wall built against the hull from outside stays a
                    // world block.
                    if (aimedShip != null && aimedShip.OwnerId == Game.LocalPlayerId && StandingInShip(aimedShip))
                    {
                        var el = ShipLocal(aimedShip, placeCell);
                        bool extOriented = PendingPlacement(item, hitCell, placeCell, out _, out int extUp, out int extYaw);
                        Game.Network.SendStructureEdit(aimedShip.StructureId, el.x, el.y, el.z, mine: false, item,
                            upFace: extOriented ? extUp : _placeUpFace,
                            yaw: extOriented ? extYaw : _placeYaw, deviceDir: DeviceDirFor(def.PlacesBlock));
                        TriggerSwing();
                        return;
                    }

                    var beaconLabel = Game?.Context.Panel<BeaconLabelUi>();
                    if (def.PlacesBlock == "radio_beacon" && beaconLabel != null)
                    {
                        // Name the beacon before placing it — the typed label travels with the place (item 37).
                        var cell = placeCell;
                        beaconLabel.Open(
                            Game.Localizer?.Get("ui.beacon.name_prompt") ?? "Name this beacon",
                            string.Empty,
                            label => Game.Network.SendPlace(cell.x, cell.y, cell.z, item, label));
                    }
                    else if (def.PlacesBlock == "beam_block" && beaconLabel != null)
                    {
                        // Name the beam block before placing it — the typed name travels with the place (teleporter pad).
                        var cell = placeCell;
                        beaconLabel.Open(
                            Game.Localizer?.Get("ui.beam.name_prompt") ?? "Name this beam block",
                            string.Empty,
                            label => Game.Network.SendPlace(cell.x, cell.y, cell.z, item, label));
                    }
                    else
                    {
                        // Send the orientation the ghost was showing (an explicit rotate-key state, or the
                        // client's own Auto answer — see PendingPlacement). An item with nothing to orient
                        // sends the raw override fields and lets the server derive, exactly as before.
                        bool orientable = PendingPlacement(item, hitCell, placeCell, out _, out int upFace, out int yaw);
                        Game.Network.SendPlace(placeCell.x, placeCell.y, placeCell.z, item,
                            upFace: orientable ? upFace : _placeUpFace,
                            yaw: orientable ? yaw : _placeYaw, deviceDir: DeviceDirFor(def.PlacesBlock));
                    }

                    TriggerSwing();
                }
            }
        }

        /// <summary>#2120: whether the player stands in their ship — anywhere in the ship interior out in space; on a planet
        /// inside the parked ship's design box, or on one of its cells (the floor of an extension built onto it).</summary>
        private bool StandingInShip(LandedShipModel ship)
        {
            if (Game.LoadingPlanetType == "ship_interior")
            {
                return true;
            }

            var p = transform.position;
            var local = ShipLocal(ship, new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y + 0.05f), Mathf.FloorToInt(p.z)));
            bool inBox = local.x >= 0 && local.x < ship.Width && local.y >= 0 && local.y <= ship.Height
                && local.z >= 0 && local.z < ship.Length;
            return inBox || ship.Cells.ContainsKey(new BlocksBeyondTheStars.Shared.Geometry.Vector3i(local.x, local.y - 1, local.z));
        }

        /// <summary>A world cell mapped into a parked ship's structure-local grid (wrap-aware on X).</summary>
        private Vector3Int ShipLocal(LandedShipModel ship, Vector3Int worldCell)
            => new Vector3Int(
                BlocksBeyondTheStars.Shared.World.WorldConstants.WrapDeltaX(worldCell.x - ship.Origin.X, Game.Circumference),
                worldCell.y - ship.Origin.Y,
                worldCell.z - ship.Origin.Z);
    }
}
