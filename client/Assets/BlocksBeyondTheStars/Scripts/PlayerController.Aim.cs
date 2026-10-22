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
    /// What the crosshair points at: the held tool's fluid aim, the aimed-cell outline (<see cref="AimOutlineCell"/>),
    /// doors and their how-to hint (<see cref="TryAimDoor(out Vector3Int)"/>, <see cref="ShowDoorHowTo"/>) and the block
    /// march behind mining and placing (<see cref="AimTarget"/>, <see cref="AimBlock"/>) with its fluid helpers.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>Which fluid cells the aim ray may stop at (#1310, #1353) — a flag per fluid so the
        /// decision is taken against the definition of the fluid actually hit, not "lava stands for both".</summary>
        [System.Flags]
        private enum FluidAim
        {
            None = 0,
            Water = 1,
            Lava = 2,
            Liquid = 4, // a still liquid by block data (#2106: oil) — only the pump harvests it, no block displaces it
            Both = Water | Lava,
            All = Water | Lava | Liquid,
        }

        /// <summary>The fluids the selected hotbar tool can mine by the block data (kind + tier — the server
        /// applies the same rule, so the client never offers a target it would then reject) (#1310). Water and
        /// lava are checked separately: a tool that reaches one but not the other stops only at that one.</summary>
        private FluidAim HeldToolFluidAim()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            var tool = string.IsNullOrEmpty(held) ? null : Game.Content?.GetItem(held)?.Tool;
            if (tool == null)
            {
                return FluidAim.None;
            }

            var aim = FluidAim.None;
            if (ToolMines(tool, Game.Content.GetBlock("water")))
            {
                aim |= FluidAim.Water;
            }

            if (ToolMines(tool, Game.Content.GetBlock("lava")))
            {
                aim |= FluidAim.Lava;
            }

            return aim;
        }

        private static bool ToolMines(BlocksBeyondTheStars.Shared.Definitions.ToolProperties tool,
            BlocksBeyondTheStars.Shared.Definitions.BlockDefinition fluid)
            => fluid != null && fluid.Mineable && tool.Kind == fluid.RequiredTool && tool.Tier >= fluid.MinToolTier;

        /// <summary>Both fluids when the held item places a block (the block displaces either, #851) — minus what
        /// the server refuses in a fluid cell (#1368, mirroring <c>HandlePlace</c>): a door is an entity that
        /// needs an air cell (never offered over water or lava — the block data's <c>door</c> category), and a
        /// torch is an open flame (not offered over water; lava is accepted). Without the gate the client
        /// aimed the door/torch at the fluid, the server rejected, and the player got a reject toast.</summary>
        private FluidAim PlaceFluidAim()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            string placesKey = string.IsNullOrEmpty(held) ? null : Game.Content?.GetItem(held)?.PlacesBlock;
            if (string.IsNullOrEmpty(placesKey))
            {
                return FluidAim.None;
            }

            var block = Game.Content.GetBlock(placesKey);
            if (block != null && string.Equals(block.Category, "door", System.StringComparison.OrdinalIgnoreCase))
            {
                return FluidAim.None;
            }

            return block != null && block.Key == "torch" ? FluidAim.Lava : FluidAim.Both;
        }

        /// <summary>The cell the selection outline belongs on (#1353): the very target the next click would take
        /// — mine with the held tool (including the fluids it can mine) or place the held block (into a fluid) —
        /// so <see cref="MiningFx"/> highlights exactly what <see cref="HandleInteract"/> will accept. Parked-ship
        /// cells count; a menu or missing rig yields no cell.</summary>
        public bool AimOutlineCell(out Vector3Int cell)
        {
            if (Game == null)
            {
                cell = default;
                return false;
            }

            return AimTarget(out cell, out _, out _, HeldToolFluidAim() | PlaceFluidAim());
        }

        /// <summary>#1746: the base cell of a player-built door under the crosshair, when nothing solid stands
        /// between the camera and it. Doors are entities (<see cref="DoorView"/>) in air cells, so the voxel
        /// march in <see cref="AimTarget"/> walks straight through them — which is why mining a door did nothing
        /// (and, until #1710, answered as "ship hull"). The server removes a door for any of its cells
        /// (<c>RemovePlayerDoorAt</c>) and tells the player when a stamped door is protected instead.</summary>
        private bool TryAimDoor(out Vector3Int cell) => TryAimDoor(out _, out cell);

        /// <summary>As <see cref="TryAimDoor(out Vector3Int)"/>, also naming the door (#2147).</summary>
        private bool TryAimDoor(out int doorId, out Vector3Int cell)
        {
            doorId = 0;
            cell = default;
            if (DoorView.Instance == null || Camera == null || Game?.World == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            if (!DoorView.Instance.AimDoor(o, dir, Reach, out doorId, out cell, out float doorDist))
            {
                return false;
            }

            // A wall, a hull or the ground in front of the door wins — only a door the ray reaches first counts.
            if (AimTarget(out var solid, out _, out _, FluidAim.None))
            {
                float solidDist = RayBox.Entry(o.x, o.y, o.z, dir.x, dir.y, dir.z,
                    solid.x, solid.y, solid.z, solid.x + 1f, solid.y + 1f, solid.z + 1f);
                if (solidDist < doorDist)
                {
                    doorId = 0;
                    return false;
                }
            }

            return true;
        }

        /// <summary>#2147: the hand door E works — the aimed door when it swings by hand and its latch is within the
        /// server's reach (3 blocks), else the nearest hand door, else 0. Distance alone used to swing the door
        /// beside the one you were looking at.</summary>
        private int TargetHandDoor()
        {
            var view = DoorView.Instance;
            if (view == null)
            {
                return 0;
            }

            int nearest = view.NearestHinge(transform.position, 3f);
            if (nearest == 0)
            {
                return 0; // no hand door in reach at all — spare the aim test
            }

            return TryAimDoor(out int aimed, out _) && aimed != nearest
                && view.IsHandDoor(aimed) && view.DoorWithin(aimed, transform.position, 3f)
                ? aimed
                : nearest;
        }

        /// <summary>#2147: tells a player who right-clicked a door how it opens — a hand door with the interact key, an
        /// automatic one by walking up to it. Doors stay E-only; the hint replaces the block the click used to drop
        /// into the doorway behind it.</summary>
        private void ShowDoorHowTo(int doorId)
        {
            var view = DoorView.Instance;
            var loc = Game.Localizer;
            string text = view != null && view.IsHandDoor(doorId)
                ? (loc?.Get("ui.door.use_key") ?? "Open doors with {key}.").Replace("{key}", view.InteractKeyName())
                : loc?.Get("ui.door.opens_itself") ?? "This door opens by itself when you walk up to it.";
            Game.ShowMessage(text);
        }

        /// <summary>Like <see cref="AimBlock"/>, but the march also targets the cells of parked ship OBJECTS
        /// (ship-as-object): whichever solid cell the ray reaches first wins. <paramref name="ship"/> is set
        /// when the hit belongs to a parked ship — mine/place then route to a structure edit.
        /// With <paramref name="fluidSurfaces"/> (#1310) the ray also stops at a water/lava cell it ENTERS from
        /// a non-fluid cell — i.e. at the fluid's surface or side — and offers that very cell as both the hit
        /// and the place cell: a placed block displaces the fluid (#851) and a tier-3 drill mines it, which is
        /// what the block data has promised all along. Over an open lava lake the old march found no target at
        /// all. A ray that STARTS inside fluid (the player is swimming) keeps passing through, so building on
        /// the seabed from under water is unchanged. The flags say WHICH fluids stop the ray (#1353): a tool
        /// that mines lava but not water still aims through a pond to the seabed.</summary>
        private bool AimTarget(out Vector3Int hitCell, out Vector3Int placeCell, out LandedShipModel ship, FluidAim fluidSurfaces = FluidAim.None)
        {
            hitCell = default;
            placeCell = default;
            ship = null;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            bool prevFluid = IsFluidBlock(Game.World.GetBlock(x, y, z)); // the eye's own cell: swimming → pass through
            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                var fluidKind = id.IsAir ? FluidAim.None : FluidKindOf(id);
                bool fluid = fluidKind != FluidAim.None;
                if (!id.IsAir && !fluid)
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    return true;
                }

                if (fluid && (fluidSurfaces & fluidKind) != 0 && !prevFluid)
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = hitCell; // into the fluid cell itself — the server displaces it (#851)
                    return true;
                }

                if (!Game.LandedShipBlockAt(x, y, z, out var s, out _).IsAir)
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    ship = s;
                    return true;
                }

                prevFluid = fluid;
                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Ray-marches the voxel grid (Amanatides &amp; Woo) along the aim ray and returns the first
        /// targetable block within <see cref="Reach"/> — <paramref name="hitCell"/> is the block to mine, and
        /// <paramref name="placeCell"/> the empty cell just before its hit face (where a placed block goes).
        /// Fluids (water/lava) are passed through, matching the collider (which excludes them). Cells are in the
        /// same space the dig intents use; the server + <see cref="ClientWorld"/> both wrap X, so the seam is fine.</summary>
        private bool AimBlock(out Vector3Int hitCell, out Vector3Int placeCell, bool includeFluids = false)
        {
            hitCell = default;
            placeCell = default;
            if (Game?.World == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int px = x, py = y, pz = z;

            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            // Parametric distance to the first cell boundary on each axis.
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 80 && t <= Reach; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if (!id.IsAir && (includeFluids || !IsFluidBlock(id)))
                {
                    hitCell = new Vector3Int(x, y, z);
                    placeCell = new Vector3Int(px, py, pz);
                    return true;
                }

                px = x; py = y; pz = z;
                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Water/lava are passed through when aiming (they have no collider — you swim/sink into them).</summary>
        private bool IsFluidBlock(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
            => FluidKindOf(id) != FluidAim.None;

        /// <summary>Which fluid a block id is (one key lookup — the aim march calls this per cell).</summary>
        private FluidAim FluidKindOf(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
        {
            var def = Game.Content?.BlockById(id);
            if (def != null && def.Liquid)
            {
                return FluidAim.Liquid; // #2106: oil — the ray passes through it like water unless the pump asks for it
            }

            return def?.Key switch
            {
                "water" => FluidAim.Water,
                "lava" => FluidAim.Lava,
                _ => FluidAim.None,
            };
        }
    }
}
