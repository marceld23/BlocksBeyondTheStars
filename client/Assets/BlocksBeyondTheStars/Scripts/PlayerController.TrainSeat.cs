// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The view while seated (#3471–#3475, Lyxette's reports of 2026-11-11): a wagon seat turns the body across the aisle and
    /// keeps the seated eye; a sleeper's berth lays the rider down with the pillow view of the beds (#2823) — first person in
    /// either camera mode, the body hidden; and the own avatar sits ON the seat in the third-person view, lowered like every
    /// remote avatar and NPC (<see cref="PlayerAvatar.SeatDrop"/>), instead of hovering half a block above it.
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The look's pitch when lying down in a berth: gently up and out along the berth, like the beds' pillow view.</summary>
        private const float BerthPitch = -15f;

        /// <summary>Whether the pillow view of a berth is on (the camera is placed in world space, the body hidden).</summary>
        private bool _berthView;

        /// <summary>Per frame while seated in a train: the seat holds the body (turned once on sitting down), the berth puts the
        /// eye on its pillow, a bench seat eases it to the seated eye; the own avatar sits on the seat.</summary>
        private void HoldTrainSeatView()
        {
            bool turned = _train.HoldTrainSeat(out bool berth);
            if (turned && berth)
            {
                _pitch = BerthPitch; // lying down: the look up and out along the berth, then it is the player's again
            }

            LookAround();
            if (berth)
            {
                EnterBerthView();
                if (Camera != null && _train.BerthEyeWorld(out var eye))
                {
                    Camera.transform.position = eye;
                }

                return;
            }

            LeaveBerthView();
            if (Camera != null && !ThirdPerson)
            {
                Camera.transform.localPosition = Vector3.Lerp(Camera.transform.localPosition, SeatedEye, Time.deltaTime * 8f);
            }

            ApplySeatedAvatarDrop();
        }

        /// <summary>Lying down in a berth: no body standing in the bunk in the chase view, no tool in front of the face.</summary>
        private void EnterBerthView()
        {
            if (_berthView)
            {
                return;
            }

            _berthView = true;
            Avatar?.SetVisible(false);
            _viewmodel?.SetVisible(false);
        }

        /// <summary>Up from a berth (or never in one): the camera mode gets the eye, the body and the held item back.</summary>
        internal void LeaveBerthView()
        {
            if (!_berthView)
            {
                return;
            }

            _berthView = false;
            ApplyCameraMode();
        }

        /// <summary>#3475: the own avatar drops onto the seat like a seated remote player or NPC — the root goes down by
        /// <see cref="PlayerAvatar.SeatDrop"/> from where the on-foot pose keeps it (the next on-foot frame puts it back).</summary>
        private void ApplySeatedAvatarDrop()
        {
            if (Avatar == null || !_avatarBaseYKnown)
            {
                return;
            }

            var p = Avatar.transform.localPosition;
            p.y = _avatarBaseY - PlayerAvatar.SeatDrop;
            Avatar.transform.localPosition = p;
        }

        /// <summary>#3475: sitting down on a chair or bench turns the body to face away from its backrest (the look stays free
        /// afterwards) — before, the player sat the way they walked up, mostly facing into the backrest.</summary>
        private void FaceAwayFromBackrest(Vector3Int cell)
        {
            if (Game?.World == null)
            {
                return;
            }

            if (FurnitureShapes.TrySeatFacingYawDegrees(Game.World.GetShape(cell.x, cell.y, cell.z), out float yaw))
            {
                transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            }
        }

        /// <summary>#3474: last frame's interaction named someone under the crosshair (resolved after the train's E, so the
        /// previous frame's pick is the one the HUD shows right now).</summary>
        private bool PersonAimed()
            => Game != null && Game.InteractionFrame >= Time.frameCount - 1 && InteractResolver.IsAimedAtSomeone(Game.Interaction);
    }
}
