// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The own touchdown on the surface (#3140): the cloud cover parts on the own ship high above its pad, sinking down on
    /// its engine fire — slowing as it nears the ground, the dust in the ground's colour racing out from under it — until
    /// it settles with a thump, the engines spool down and steam vents; then the camera glides into the player's eyes. A
    /// render-only stand-in flies (the real hull keeps its colliders on the pad, hidden meanwhile), so nothing the server
    /// placed ever moves.
    /// </summary>
    public sealed partial class TransitionDirector
    {
        private const float DescendSeconds = 3.3f;
        private const float SettleSeconds = 0.8f;
        private const float EyeSeconds = 1.1f;

        private GameObject _stand;          // the flying stand-in (touchdown) or the adopted hull (lift-off)
        private GameObject _hiddenHull;     // the real parked hull, hidden while its stand-in flies
        private GameObject _standDoor;      // #3159: the stand-in's closed hatch door (its own meshes)
        private List<(Vector3 Local, float Size)> _exhausts;
        private Bounds _hullBox;            // the hull's box at rest (world)
        private Vector3 _restPos;
        private float _drop;
        private Color _dust;
        private int _light;
        private bool _impact;
        private Vector3 _spot;

        private bool BeginTouchdown()
        {
            var model = OwnLandedShip();
            var root = model != null ? Game.Views.LandedShips?.RootOf(model.StructureId) : null;
            if (root == null || Camera == null)
            {
                return false;
            }

            _hullBox = ShipCinematicFx.RenderBounds(root);
            if (_hullBox.size.sqrMagnitude < 0.01f)
            {
                return false;
            }

            _hiddenHull = root;
            _stand = ShipCinematicFx.CloneRenderers(root, "TouchdownShip");
            ShipCinematicFx.SetRenderersEnabled(root, false);
            // #3159: the stand-in flies with the ship's own doors (copied while it still stands at rest), the real ones stay out
            // of the shot until it is down; a ship whose doors are not in yet gets its hatch shut by the plan.
            _standDoor = Game.Views.Doors?.CloneDoorsWithin(DoorBox(), _stand.transform)
                         ?? ShipHatchDoor.Build(_stand.transform, ShipHatchDoor.PlanFor(model), Vector3.zero);
            HideRealHatch(true);
            _restPos = root.transform.position;
            _drop = 34f + _hullBox.size.y * 2.5f;
            _stand.transform.position = _restPos + Vector3.up * _drop;
            PrepareShipFx(model);
            _impact = false;
            _spot = ShotSpot(_hullBox, Mathf.Max(10f, _hullBox.extents.magnitude * 1.6f), 2.2f);
            TakeCamera();
            AimShot(_spot, _stand.transform.position + (_hullBox.center - _restPos), 0f);
            ClientAudio.Instance?.Cue("engine_thrust", 0.7f);
            SetStage(Stage.Touchdown);
            return true;
        }

        private void PrepareShipFx(LandedShipModel model)
        {
            _exhausts = ShipMeshBuilder.ExhaustPoints(Game.Content, model.Cells, Vector3.zero);
            _dust = ShipCinematicFx.GroundColor(Game, CellBelow(model));
            if (_light != 0)
            {
                FxLights.Remove(_light);
            }

            _light = FxLights.Attach(_stand.transform, _hullBox.center - _restPos + Vector3.down * (_hullBox.extents.y + 1.5f),
                new Color(1f, 0.62f, 0.3f), 0f, 18f, flicker: true);
        }

        private void UpdateTouchdown(float dt)
        {
            if (_stand == null)
            {
                FinishTouchdown();
                return;
            }

            if (!_impact)
            {
                HideRealHatch(true); // #3159: a door list that came in meanwhile built the real doors afresh
            }

            if (SkipRequested() && _stageTime < DescendSeconds + SettleSeconds)
            {
                _stageTime = DescendSeconds + SettleSeconds; // straight to the glide home, the ship down on its pad
            }

            float t = Mathf.Clamp01(_stageTime / DescendSeconds);
            float e = 1f - (1f - t) * (1f - t) * (1f - t);
            float height = _drop * (1f - e);
            float sway = (1f - e) * 0.6f;
            _stand.transform.SetPositionAndRotation(
                _restPos + Vector3.up * height + new Vector3(Mathf.Sin(_stageTime * 1.7f), 0f, Mathf.Cos(_stageTime * 1.3f)) * sway,
                Quaternion.Euler(5f * (1f - e), 0f, 2.5f * Mathf.Sin(_stageTime * 2.1f) * (1f - e)));
            DescentFx(t, height);
            if (t >= 1f && !_impact)
            {
                Impact();
            }

            var centre = _stand.transform.position + (_hullBox.center - _restPos);
            float push = Mathf.Clamp01((_stageTime - DescendSeconds) / SettleSeconds);
            MoveShot(Vector3.Lerp(_spot, Vector3.Lerp(_spot, centre, 0.25f), push), centre);
            if (_stageTime >= DescendSeconds + SettleSeconds)
            {
                BlendToEye(EyeSeconds);
            }

            if (AtEye)
            {
                FinishTouchdown();
            }
        }

        private void DescentFx(float t, float height)
        {
            float power = _impact ? Mathf.Clamp01(1f - (_stageTime - DescendSeconds) * 1.6f) : Mathf.Lerp(0.55f, 1f, t);
            var fallback = _hullBox.center - _restPos + Vector3.down * _hullBox.extents.y;
            ShipCinematicFx.EngineFire(_stand.transform, _exhausts, fallback, Vector3.down, power);
            FxLights.Set(_light, new Color(1f, 0.62f, 0.3f), 3.2f * power);
            var ground = new Vector3(_hullBox.center.x, _hullBox.min.y, _hullBox.center.z);
            float near = Mathf.Clamp01(1f - height / 14f);
            ShipCinematicFx.DustSwirl(ground, _hullBox.extents.magnitude + 2f, near * power, _dust);
            if (_impact)
            {
                ShipCinematicFx.Steam(_hullBox, Mathf.Clamp01(1.4f - (_stageTime - DescendSeconds)));
            }
            else
            {
                FxCamera.AddTrauma(0.15f * near * Time.deltaTime * 4f, rumble: false);
            }
        }

        private void Impact()
        {
            _impact = true;
            // #3159: the stand-in stands exactly at rest now — the real doors take over from its copies (so the glide into the
            // cabin passes the real inner doors, not a gap where they were).
            HideRealHatch(false);
            ShipCinematicFx.DestroyWithMeshes(_standDoor);
            _standDoor = null;
            var ground = new Vector3(_hullBox.center.x, _hullBox.min.y, _hullBox.center.z);
            ShipCinematicFx.DustBurst(ground, _hullBox.extents.magnitude + 1f, _dust);
            FxCamera.AddTrauma(0.32f);
            PadRumble.Landing(true);
            ClientAudio.Instance?.Cue("ship_touchdown", 0.95f);
        }

        private void FinishTouchdown()
        {
            EndCinematic();
            SetStage(Stage.Idle);
        }

        /// <summary>Drops the stand-in and the engine light and shows the real hull again.</summary>
        private void EndShipCinematic()
        {
            if (_light != 0)
            {
                FxLights.Remove(_light);
                _light = 0;
            }

            if (_stand != null)
            {
                if (_hiddenHull != null)
                {
                    ShipCinematicFx.DestroyWithMeshes(_standDoor); // its door copies' meshes are the stand-in's only own ones
                    Destroy(_stand); // a stand-in shares the parked hull's meshes — they stay
                }
                else
                {
                    ShipCinematicFx.DestroyWithMeshes(_stand); // an adopted hull owns its meshes (its doors' copies too)
                }
            }

            ShipCinematicFx.SetRenderersEnabled(_hiddenHull, true);
            HideRealHatch(false);
            _hullBox = default; // #3159: the next cinematic measures its own — no later restore reaches into this one's box
            _stand = null;
            _standDoor = null;
            _hiddenHull = null;
        }

        /// <summary>#3159: the box the parked ship's doors stand in — its hull box, a little wider (a door sits in the outer wall).</summary>
        private Bounds DoorBox()
        {
            var box = _hullBox;
            box.Expand(1.5f);
            return box;
        }

        /// <summary>#3159: the parked ship's real doors (<see cref="DoorView"/> entities, not part of the hull) stay out of a shot
        /// that flies the ship — they would stand alone on the pad while the stand-in comes down or the hull rises.</summary>
        private void HideRealHatch(bool hidden)
        {
            if (_hullBox.size.sqrMagnitude < 0.01f)
            {
                return;
            }

            Game?.Views.Doors?.SetDoorsHiddenWithin(DoorBox(), hidden);
        }

        /// <summary>The world cell just under the middle of a parked hull (the hull box is meshed at its model's origin).</summary>
        private Vector3i CellBelow(LandedShipModel model)
        {
            var local = _hullBox.center - _restPos;
            int bottom = Mathf.FloorToInt(_hullBox.min.y - _restPos.y);
            return new Vector3i(model.Origin.X + Mathf.FloorToInt(local.x), model.Origin.Y + bottom - 1, model.Origin.Z + Mathf.FloorToInt(local.z));
        }

        private LandedShipModel OwnLandedShip()
        {
            foreach (var m in Game.LandedShips.Values)
            {
                if (m.OwnerId == Game.LocalPlayerId)
                {
                    return m;
                }
            }

            return null;
        }
    }
}
