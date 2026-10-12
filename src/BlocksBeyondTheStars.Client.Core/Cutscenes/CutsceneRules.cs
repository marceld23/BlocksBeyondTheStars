// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using System.Collections.Generic;
using System.Linq;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Story;
using BlocksBeyondTheStars.Shared.World;

namespace BlocksBeyondTheStars.Client.Cutscenes
{
    /// <summary>
    /// The cutscene director's decisions that need no Unity (#2474), kept here so they are unit-tested headless: whether a
    /// playing scene holds a singleplayer world or freezes the controls, where the player's real heal tank is, and when a
    /// staged scene has to play as a card because the object it frames is not on this world (synthesis §6.2 "Abstieg": VEGA's
    /// "What You Are" at the heal tank, played far from the ship) or because its only pictures are synthetic stages the
    /// quality preset skips (#2477).
    /// </summary>
    public static class CutsceneRules
    {
        /// <summary>How far from the player the own ship's heal tank may be to frame it (the ship parks on the world the player
        /// is on; a station list from another world is never used).</summary>
        public const float HealTankReach = 64f;

        /// <summary>Whether a shot's anchor names the object the shot is about — without it the shot means nothing (the heal
        /// tank). The other anchors fall back to the player.</summary>
        public static bool NeedsObject(string? anchor) => anchor == CutsceneAnchors.HealTank;

        /// <summary>The lowest quality preset that builds the synthetic stages (#2477; the client's <c>QualityPreset</c> order
        /// Potato 0, Low 1, Medium 2, High 3): on Potato and Low a space vista or a diorama is skipped (synthesis §6.6 "keine
        /// zusätzlichen Lichter auf Kartoffel/Niedrig").</summary>
        public const int SyntheticStagesMinPreset = 2;

        /// <summary>#2477: whether the player's quality preset (<paramref name="presetIndex"/>, the client's <c>QualityPreset</c>
        /// as a number) builds synthetic stages.</summary>
        public static bool SyntheticStagesAllowed(int presetIndex) => presetIndex >= SyntheticStagesMinPreset;

        /// <summary>Whether a staged scene plays as a card (<see cref="CutscenePlan.AsCard"/>): a shot that takes the camera is
        /// anchored on an object (<see cref="NeedsObject"/>) this client cannot find (<paramref name="objectFound"/>) — or
        /// (#2477) synthetic stages are not allowed here (<paramref name="syntheticAllowed"/>, the low presets) and every shot
        /// that takes the camera is one (a scene whose only picture is a space vista or a diorama reads best as a card then;
        /// a scene with another camera shot keeps it and lets the synthetic shot fall back to the sky).</summary>
        public static bool NeedsCardFallback(CutscenePlan? plan, Func<string, bool>? objectFound, bool syntheticAllowed = true)
        {
            if (plan is null || !plan.TakesCamera)
            {
                return false;
            }

            if (!syntheticAllowed && plan.Shots.Where(s => s.TakesCamera).All(s => CutsceneCameras.IsSynthetic(s.Camera)))
            {
                return true;
            }

            return plan.Shots.Any(s => s.TakesCamera && NeedsObject(s.Anchor) && !(objectFound?.Invoke(s.Anchor) ?? false));
        }

        /// <summary>Whether the playing scene holds the world now (synthesis §5.3 "Stufen"): only a scene that may
        /// (<see cref="CutscenePlan.HoldWorld"/>, a T3/T4 taking the camera), only while the camera is actually taken, and
        /// never in multiplayer — a hold there would make the other players' pause dialogs wait for this one.</summary>
        public static bool HoldsWorld(CutscenePlan? plan, bool cameraTaken, bool multiplayer)
            => plan is not null && plan.HoldWorld && cameraTaken && !multiplayer;

        /// <summary>Whether the shared cutscene state is on (HUD away, controls frozen, Esc to the director): a staged scene
        /// (T3/T4) that freezes the controls or has the camera now — not after the multiplayer camera cap.</summary>
        public static bool FreezesControls(CutscenePlan? plan, bool capped, bool cameraTaken)
            => plan is not null && plan.Tier >= CutsceneTiers.Scene && !capped && (plan.LockInput || cameraTaken);

        /// <summary>
        /// The player's real heal tank (the <c>heal_tank</c> anchor): the nearest <c>medbay</c> station of the own ship
        /// (<see cref="ShipStations"/>, world coordinates of the tank's cell: x/z its centre, y its floor) within
        /// <see cref="HealTankReach"/> of the player, measured the short way round the world's seams. False when the ship is not
        /// on this world or has no medbay (the code-box starter) — the scene then plays as a card.
        /// </summary>
        public static bool TryFindHealTank(IEnumerable<NetShipStation>? stations, float px, float py, float pz, int circumference,
            out float x, out float y, out float z)
        {
            x = y = z = 0f;
            double best = HealTankReach * (double)HealTankReach;
            bool found = false;
            foreach (var s in stations ?? Array.Empty<NetShipStation>())
            {
                if (s is null || s.Type != "medbay")
                {
                    continue;
                }

                double dx = circumference > 0 ? WorldConstants.WrapDeltaX(s.X - (double)px, circumference) : s.X - (double)px;
                double dz = circumference > 0 ? WorldConstants.WrapDeltaZ(s.Z - (double)pz, circumference) : s.Z - (double)pz;
                double dy = s.Y - (double)py;
                double d = dx * dx + dy * dy + dz * dz;
                if (d <= best)
                {
                    best = d;
                    x = s.X;
                    y = s.Y;
                    z = s.Z;
                    found = true;
                }
            }

            return found;
        }
    }
}
