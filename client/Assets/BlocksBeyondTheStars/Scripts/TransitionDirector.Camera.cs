// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The transition cinematics' camera (#3140/#3141): it takes the player camera like the cutscene rig does,
    /// flies a shot (a spot, a point to look at, a blend from wherever the camera was) and glides back into the player's
    /// eyes before it lets go — never a cut while the world is visible.</summary>
    public sealed partial class TransitionDirector
    {
        private bool _cameraTaken;
        private Vector3 _restLocalPos;
        private Quaternion _restLocalRot;
        private float _restFov = 60f;
        private Vector3 _fromPos;
        private Quaternion _fromRot;
        private Vector3 _shotPos;
        private Vector3 _shotLook;
        private float _blend = 1f;
        private float _blendSeconds = 1f;
        private bool _toEye;
        private float _fovOffset;
        private bool _avatarWasVisible;
        private PlayerController _player;

        /// <summary>Takes the player camera: controls freeze, the HUD and the overlays step aside, the hands go away.</summary>
        private void TakeCamera()
        {
            if (_cameraTaken || Camera == null)
            {
                return;
            }

            _cameraTaken = true;
            var t = Camera.transform;
            _restLocalPos = t.localPosition;
            _restLocalRot = t.localRotation;
            _restFov = Camera.fieldOfView;
            _fromPos = _shotPos = t.position;
            _fromRot = t.rotation;
            _shotLook = t.position + t.forward * 10f;
            _blend = 1f;
            _toEye = false;
            _fovOffset = 0f;
            Game.CinematicCameraActive = true;
            HoldScreen(true);
            _player = Camera.GetComponentInParent<PlayerController>();
            Camera.GetComponent<Viewmodel>()?.SetVisible(false);
            _avatarWasVisible = _player != null && _player.ThirdPerson;
            _player?.Avatar?.SetVisible(false); // the body stands where the ship will be — not in mid-air meanwhile
            Cover?.SetHint(CinematicFrame.SkipLabel(key => Game.Localizer != null ? Game.Localizer.Get(key) : key));
        }

        /// <summary>Frames <paramref name="look"/> from <paramref name="pos"/>, blending there from the current camera over
        /// <paramref name="seconds"/> (0 = a cut, for a shot that starts behind a closed cover).</summary>
        private void AimShot(Vector3 pos, Vector3 look, float seconds)
        {
            if (Camera == null)
            {
                return;
            }

            _fromPos = Camera.transform.position;
            _fromRot = Camera.transform.rotation;
            _shotPos = pos;
            _shotLook = look;
            _toEye = false;
            _blendSeconds = Mathf.Max(0.0001f, seconds);
            _blend = seconds <= 0f ? 1f : 0f;
        }

        /// <summary>Moves the running shot without restarting its blend (a follow, a push-in).</summary>
        private void MoveShot(Vector3 pos, Vector3 look)
        {
            _shotPos = pos;
            _shotLook = look;
        }

        /// <summary>Glides from the shot back into the player's eyes over <paramref name="seconds"/>.</summary>
        private void BlendToEye(float seconds)
        {
            if (Camera == null || _toEye)
            {
                return;
            }

            _fromPos = Camera.transform.position;
            _fromRot = Camera.transform.rotation;
            _toEye = true;
            _blendSeconds = Mathf.Max(0.0001f, seconds);
            _blend = 0f;
        }

        /// <summary>True once a <see cref="BlendToEye"/> has arrived.</summary>
        private bool AtEye => _toEye && _blend >= 1f;

        private void WriteCamera()
        {
            if (!_cameraTaken || Camera == null)
            {
                return;
            }

            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            _blend = Mathf.Min(1f, _blend + dt / _blendSeconds);
            float e = _blend * _blend * (3f - 2f * _blend);
            Vector3 pos;
            Quaternion rot;
            if (_toEye)
            {
                var parent = Camera.transform.parent;
                pos = parent != null ? parent.TransformPoint(_restLocalPos) : _restLocalPos;
                rot = parent != null ? parent.rotation * _restLocalRot : _restLocalRot;
            }
            else
            {
                pos = _shotPos;
                var dir = _shotLook - _shotPos;
                rot = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir, Vector3.up) : _fromRot;
            }

            FxCamera.Sample(dt, out var shake, out float fovPunch);
            Camera.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, pos, e), Quaternion.Slerp(_fromRot, rot, e) * Quaternion.Euler(shake));
            Camera.fieldOfView = _restFov + Mathf.Lerp(_fovOffset, 0f, _toEye ? e : 0f) + fovPunch;
        }

        /// <summary>Gives the camera back exactly as it was (local eye pose, FOV, the hands, the third-person body).</summary>
        private void ReleaseCamera()
        {
            if (!_cameraTaken)
            {
                return;
            }

            _cameraTaken = false;
            if (Camera != null)
            {
                Camera.transform.localPosition = _restLocalPos;
                Camera.transform.localRotation = _restLocalRot;
                Camera.fieldOfView = _restFov;
                Camera.GetComponent<Viewmodel>()?.SetVisible(_player == null || !_player.ThirdPerson);
            }

            _player?.Avatar?.SetVisible(_avatarWasVisible);
            _player = null;
            Cover?.SetHint(string.Empty);
            if (Game != null)
            {
                Game.CinematicCameraActive = false;
            }
        }

        /// <summary>While a transition holds the screen (a shut cover, a cinematic) the controls freeze and the HUD, the chat
        /// and the touch layer step aside — the cutscene gate's reason <see cref="Reason"/>.</summary>
        private static void HoldScreen(bool on) => GameBootstrap.Cutscenes.Set(Reason, on);

        /// <summary>Ends whatever cinematic runs: the camera goes back, the stand-in ship goes, the real one shows again.</summary>
        private void EndCinematic()
        {
            ReleaseCamera();
            EndShipCinematic();
            HoldScreen(false);
        }

        /// <summary>True when the player asked to skip (Esc / the pad's cancel / a tap on the hint) — only while a
        /// cinematic owns the camera.</summary>
        private bool SkipRequested() => _cameraTaken && !Game.ChatTyping && CinematicFrame.SkipPressed();

        /// <summary>
        /// A clear camera spot around a ship standing in <paramref name="box"/>: rear-quarter first (the engines in frame),
        /// then the other quarters and the sides — the first whose spot is free and from which the ship can be seen. Falls
        /// back to straight above the rear when everything around is walled in (a pad in a canyon).
        /// </summary>
        private Vector3 ShotSpot(Bounds box, float distance, float height)
        {
            var c = box.center;
            float reach = box.extents.magnitude + distance;
            for (int i = 0; i < SpotDirs.Length; i++)
            {
                var dir = SpotDirs[i].normalized;
                for (int k = 0; k < 2; k++)
                {
                    float d = k == 0 ? reach : reach * 0.7f;
                    var spot = new Vector3(c.x + dir.x * d, box.min.y + height, c.z + dir.z * d);
                    var edge = c + dir * (box.extents.magnitude + 0.5f);
                    if (!Physics.CheckSphere(spot, 0.7f) && !Physics.Linecast(spot, edge))
                    {
                        return spot;
                    }
                }
            }

            return c + new Vector3(0f, box.extents.y + height + 6f, -box.extents.z - 4f);
        }

        private static readonly Vector3[] SpotDirs =
        {
            new Vector3(0.8f, 0f, -1f), new Vector3(-0.8f, 0f, -1f), new Vector3(1f, 0f, 0.6f), new Vector3(-1f, 0f, 0.6f),
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 0f, 1f),
        };
    }
}
