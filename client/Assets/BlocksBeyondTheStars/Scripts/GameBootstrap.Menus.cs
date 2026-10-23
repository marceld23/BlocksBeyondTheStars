// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's menu and cursor ownership (#2912): menu input handling, the world hold, the hotbar selection,
    /// the menu / HUD / cursor owner sets and LateUpdate's cursor lock.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        /// <summary>Frame marker used when the in-game menu consumes Esc/Tab, so the app shell
        /// does not also open the quit prompt on the same key press.</summary>
        public int MenuInputHandledFrame { get; private set; } = -1;
        public bool MenuInputHandledThisFrame => MenuInputHandledFrame == Time.frameCount;
        public void MarkMenuInputHandled() => MenuInputHandledFrame = Time.frameCount;

        /// <summary>Every reason this client has to hold the world — the pause menu, the feedback dialog, its reply and
        /// later a held cutscene — combined into the one pause intent the server knows (#2461). Before, each panel sent
        /// its own hold/release into the server's single flag, and closing one released the other's hold.</summary>
        public WorldHoldArbiter WorldHold => _worldHold ??= new WorldHoldArbiter(paused => Network?.SendPause(paused));

        private WorldHoldArbiter _worldHold;

        /// <summary>Hotbar slot the player has selected — the network client's own record (#2525). The player controller
        /// and the EVA view select through <see cref="NetworkClient.SendSelectHotbar"/>, and the client re-sends that
        /// slot after every join and world change, so the hand the HUD shows is the hand the server mines with.</summary>
        public int SelectedHotbarSlot => Network != null ? Network.SelectedHotbarSlot : 0;

        /// <summary>True while an in-game UI panel is open; the player controller pauses look/move.
        /// Derived from the menu-owner set — panels register via <see cref="SetMenuOwner"/> instead of
        /// writing a shared bool, so overlapping panels can no longer clobber each other's state (#413).</summary>
        public bool MenuOpen => _menuOwners.Count > 0;

        // Signals the music director reads (#1174): they are written by the panels / entity views that know,
        // so ClientMusic never has to find and poke at those components itself.

        /// <summary>True while the flight system chart (<see cref="SpaceMap"/>) is open — the star-map bed.</summary>
        public bool StarChartOpen { get; set; }

        /// <summary>Lower-case name of the open Tab-menu tab ("crafting", "tech", …) or null while the menu is
        /// closed. The music director waits a while on the crafting / tech tabs before switching beds.</summary>
        public string MenuTabKey { get; set; }

        /// <summary>Squared distance (world units) to the nearest hostile planet creature this frame, or
        /// <see cref="float.MaxValue"/> when none is near / the player is aboard. Written by <see cref="WorldEntities"/>.</summary>
        public float NearestHostileSqr { get; set; } = float.MaxValue;

        // The cursor/menu arbiter (#413): every open panel registers itself as an owner, and the cursor
        // lock state is recomputed each frame in LateUpdate from ALL owners — no panel writes
        // Cursor.lockState directly anymore, so "last close wins" can't strand a locked cursor under a
        // still-open UI (or an unlocked one over live gameplay).
        private readonly System.Collections.Generic.HashSet<object> _menuOwners = new();
        private readonly System.Collections.Generic.HashSet<object> _cursorOwners = new();
        private readonly System.Collections.Generic.HashSet<object> _hudMenuOwners = new(); // #2530: a subset of _menuOwners

        /// <summary>Registers/unregisters <paramref name="owner"/> as an open menu-style panel: frees the
        /// cursor AND pauses player/flight control (via <see cref="MenuOpen"/>). Idempotent per owner.</summary>
        public void SetMenuOwner(object owner, bool open)
        {
            if (owner == null)
            {
                return;
            }

            if (open)
            {
                _menuOwners.Add(owner);
            }
            else
            {
                _menuOwners.Remove(owner);
            }
        }

        /// <summary>Registers/unregisters <paramref name="owner"/> as a HUD-level menu owner (#2530 — the creature
        /// translator's taming panel): it frees the cursor and pauses player control like any menu owner (it counts in
        /// <see cref="MenuOpen"/>), but the HUD stays up and the Tab menu may still open over it — see
        /// <see cref="ScreenMenuOpen"/>. Idempotent per owner.</summary>
        public void SetHudMenuOwner(object owner, bool open)
        {
            if (owner == null)
            {
                return;
            }

            SetMenuOwner(owner, open);
            if (open)
            {
                _hudMenuOwners.Add(owner);
            }
            else
            {
                _hudMenuOwners.Remove(owner);
            }
        }

        /// <summary>True while a screen-level menu is open — any menu owner other than the HUD-level ones
        /// (<see cref="SetHudMenuOwner"/>). Only these hide the HUD and keep the Tab menu shut (#2530).</summary>
        public bool ScreenMenuOpen
        {
            get
            {
                foreach (var owner in _menuOwners)
                {
                    if (!_hudMenuOwners.Contains(owner))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>Registers/unregisters <paramref name="owner"/> as a cursor-only owner: frees the cursor
        /// WITHOUT pausing gameplay (e.g. the landing-pad chooser, the respawn prompt, a maintenance
        /// notice — their systems hold position through their own flags). Idempotent per owner.</summary>
        public void SetCursorOwner(object owner, bool open)
        {
            if (owner == null)
            {
                return;
            }

            if (open)
            {
                _cursorOwners.Add(owner);
            }
            else
            {
                _cursorOwners.Remove(owner);
            }
        }

        /// <summary>True while anything wants the OS cursor free (any menu panel or cursor-only owner).</summary>
        public bool CursorFreeWanted => _menuOwners.Count > 0 || _cursorOwners.Count > 0;

        /// <summary>The single place the in-game cursor lock is decided (#413): recomputed every frame from
        /// all open-UI owners, after every panel's Update has run. Assigned unconditionally — Windows drops
        /// a hardware cursor lock on Alt-Tab without telling us, so re-asserting each frame also re-locks
        /// on focus regain (in flight and on foot alike) with no special focus handling.</summary>
        private void LateUpdate()
        {
            bool free = CursorFreeWanted;
            Cursor.lockState = free ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = free;
        }
    }
}
