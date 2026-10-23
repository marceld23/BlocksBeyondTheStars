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
    /// The hand-held optics: the scanner's target in its cone (<see cref="TryFindScanTarget"/>), the binoculars
    /// (<see cref="BinocularOptic"/>, added on first use by <see cref="EnsureOptic"/>) and the photo camera
    /// (<see cref="CameraTool"/>, added on first use by <see cref="EnsureCameraTool"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The binoculars are raised (ToggleThermal applies).</summary>
        public bool BinocularsRaised => _optic != null && _optic.Raised;

        /// <summary>True if the selected hotbar item is a handheld scanner (its primary action scans).</summary>
        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Scanner;
        }

        private const float ScanConeDegrees = 25f;      // generous — creatures move; still excludes behind/beside
        private const float ScanPointBlankSq = 2f * 2f; // this close the angle test degenerates — always scannable

        /// <summary>Whether a scan target is inside the aim cone around the view direction, or point-blank
        /// (standing on it). Proximity alone must not qualify a target: a creature idling anywhere within
        /// reach — behind the player, through a wall — used to capture every scan press, which read as
        /// "the scanner is stuck on the last readout" (#1005).</summary>
        private static bool InScanCone(Vector3 eye, Vector3 fwd, Vector3 at)
        {
            var to = at - eye;
            return to.sqrMagnitude <= ScanPointBlankSq || Vector3.Angle(fwd, to) <= ScanConeDegrees;
        }

        /// <summary>Resolves what the scanner would read right now: the creature whose BODY the view ray hits,
        /// else the nearest aimed critter, else the block in view. Creatures are tested against the same
        /// Size-scaled sphere the attack path uses — the old test measured the creature's origin (its feet at
        /// the body centre) against an 8-block reach and a 25° cone, which a titan spanning ten blocks never
        /// satisfied while you looked at its flank or head ("Titanen können nicht gescannt werden", #1458).
        /// Proximity alone still never qualifies (#1005): the ray has to hit the body.</summary>
        private bool TryFindScanTarget(out string kind, out string key, out Vector3 at)
        {
            kind = null;
            key = null;
            at = default;
            _scanEntityId = null;
            if (Game == null || Camera == null)
            {
                return false;
            }

            Vector3 eye = Camera.transform.position;
            Vector3 fwd = Camera.transform.forward;

            float best = float.MaxValue;
            // #1998: a giant is scanned where the ray meets its body (a head 60 blocks up is out of arm's reach — the
            // scanner reads a giant's flank from further away).
            if (Physics.Raycast(eye, fwd, out var giantHit, Reach * 6f, 1 << CreatureView.GiantLayer, QueryTriggerInteraction.Ignore)
                && CreatureView.GiantIdFor(giantHit.collider) is { } giantId
                && System.Array.Find(Game.Creatures, g => g.Id == giantId) is { } giant)
            {
                best = giantHit.distance;
                kind = "creature";
                key = giant.SpeciesId;
                _scanEntityId = giant.Id;
                at = giantHit.point;
            }

            foreach (var c in Game.Creatures)
            {
                if (c.GiantHeight > 0f)
                {
                    continue; // picked by its colliders above
                }

                float size = Mathf.Clamp(c.Size, 0.4f, 8f);
                var basePos = Game.ScenePos(c.X, c.Y, c.Z); // seam-aware (longitude wraps)
                var center = basePos + Vector3.up * (0.6f * size);
                float r = Mathf.Max(0.8f, 0.9f * size);
                float d;
                if ((center - eye).sqrMagnitude <= r * r)
                {
                    d = 0f; // standing inside the body (a titan's legs) — point-blank, no angle test
                }
                else if (!RayHitsSphere(eye, fwd, center, r, out d) || d - r > Reach)
                {
                    continue; // the ray misses the body, or the body surface is beyond reach
                }

                if (d < best)
                {
                    best = d;
                    kind = "creature";
                    key = c.SpeciesId;
                    _scanEntityId = c.Id; // #1926: the server reads THIS individual (the Sreekmakra's disguise)
                    at = basePos + Vector3.up * (0.5f * size);
                }
            }

            if (kind != null)
            {
                return true;
            }

            // Micro-fauna (#757): when no real creature is in view, the nearest aimed-at critter answers.
            // Critters are client-local, so the kind is resolved here and the server only validates that it
            // exists (same trust level as the creature scan above). Shorter reach — they're tiny.
            if (Game.Views.MicroFauna != null
                && Game.Views.MicroFauna.NearestCritter(Game.PlayerPosition, 5f, out string critterKey, out var critterAt,
                    world => InScanCone(eye, fwd, Game.ScenePos(world.x, world.y, world.z))))
            {
                kind = "microfauna";
                key = critterKey;
                at = Game.ScenePos(critterAt.x, critterAt.y, critterAt.z);
                return true;
            }

            // Voxel ray-march INCLUDING fluids, so you can scan a water/lava block too (they have no collider, so
            // a Physics.Raycast passes straight through them — that's why water "couldn't be scanned", B26).
            if (AimBlock(out var b, out _, includeFluids: true)
                && Game.Content?.BlockById(Game.World.GetBlock(b.x, b.y, b.z)) is { } def)
            {
                kind = "block";
                key = def.Key;
                at = new Vector3(b.x + 0.5f, b.y + 0.5f, b.z + 0.5f);
                return true;
            }

            return false;
        }

        private string _scanEntityId; // the creature TryFindScanTarget last picked (#1926)

        /// <summary>Scans the aimed-at creature (threat assessment) or, failing that, the block in view.</summary>
        private void ScanTarget()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            if (TryFindScanTarget(out string kind, out string key, out var at))
            {
                Game.Network.SendScan(kind, key, kind == "creature" ? _scanEntityId : null);
                // #2153: a light fan, a holographic bracket box with a sweeping scan plane, data motes streaming back
                // and a short local wave — the scanner's own look instead of the shared ring of cubes.
                string heldKey = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var look = FxLook.ForItem(Game.Content, heldKey);
                var bounds = kind == "creature" && CreatureView.TryBounds(_scanEntityId, out var cb)
                    ? cb
                    : new Bounds(at, Vector3.one * 1.02f);
                var device = Camera != null ? Muzzle(Camera.transform) : transform.position + Vector3.up;
                FxGadgets.HandScan(look, device, bounds, look.Is("scan_pro"));
                SendFx(BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Scan, heldKey, device, bounds.center, true);
                return;
            }

            // Nothing scannable in view — say so. The scan panel stays pinned on the previous readout while
            // the scanner is held, so a silent miss looks like the scanner stopped working (#1005).
            Game.ShowMessage(Game.Localizer?.Get("ui.scan.no_target") ?? "Scanner: no target in view.");
        }

        private BinocularOptic _optic;

        /// <summary>Lazily builds the client-side binocular optic (zoom + the thermal overlay it drives).</summary>
        private BinocularOptic EnsureOptic()
        {
            if (_optic == null)
            {
                _optic = gameObject.AddComponent<BinocularOptic>();
                _optic.Game = Game;
                _optic.Thermal = Thermal;
                _optic.SetHeldItem(Game?.ItemInSlot(Game.SelectedHotbarSlot));
            }

            return _optic;
        }

        private CameraTool _cameraTool;

        /// <summary>Takes a HUD-free photo now (the streamer's "photo together", 2026-09) — the same capture the camera item
        /// does on right-click. Returns whether a capture started.</summary>
        public bool TakePhoto() => EnsureCameraTool().TryCapture();

        /// <summary>Lazily builds the client-side camera tool (HUD-free photo capture), wired to the view camera.</summary>
        private CameraTool EnsureCameraTool()
        {
            if (_cameraTool == null)
            {
                _cameraTool = gameObject.AddComponent<CameraTool>();
                _cameraTool.Game = Game;
                _cameraTool.Source = Camera;
            }

            return _cameraTool;
        }
    }
}
