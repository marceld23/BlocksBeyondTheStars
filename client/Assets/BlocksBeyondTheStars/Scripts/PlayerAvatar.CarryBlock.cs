// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// A block carried over the head with both hands (#3766 — a young player's idea). Both arms go straight up on the
    /// arms-up slot of the cheer and the startle (<see cref="ArmsUp"/>), and the held block — the one in the hand anyway —
    /// is drawn bigger and centred above the head (<see cref="BlockCarryPose"/>). Never while lying, seated, climbing or
    /// gliding: those poses win, and the server ends the carry there too.
    /// </summary>
    public sealed partial class PlayerAvatar
    {
        private bool _carryingBlock;
        private float _carryBlend;
        private GameObject _carried;        // the held model the carry has resized
        private Vector3 _carriedRestScale;
        private Vector3 _carriedRestPos;
        private Renderer[] _carriedParts;

        /// <summary>Starts or ends the overhead carry (the own figure: the controller; another player: the presence).</summary>
        public void SetCarryingBlock(bool on)
        {
            if (_carryingBlock == on)
            {
                return;
            }

            _carryingBlock = on;
            if (!on)
            {
                _carryBlend = 0f;
                RestoreCarried();
            }
        }

        /// <summary>True while the arms are up for a carried block (the pose shows).</summary>
        private bool CarryShows => _carryingBlock && !_lying && !_seated && !_climbing && !_gliding;

        /// <summary>The arms-up blend of the carry this frame.</summary>
        private float CarryBlend()
        {
            _carryBlend = BlockCarryPose.Ease(_carryBlend, true, Time.deltaTime);
            return _carryBlend;
        }

        /// <summary>Puts the held block above the head, upright and bigger; true if it did (the palm pose is skipped then).</summary>
        private bool PoseCarriedBlock()
        {
            if (!CarryShows || _held == null)
            {
                RestoreCarried();
                return false;
            }

            var t = _held.transform;
            if (_carried != _held)
            {
                RestoreCarried();
                _carried = _held;
                _carriedRestScale = t.localScale;
                _carriedRestPos = t.localPosition;
                _carriedParts = _held.GetComponentsInChildren<Renderer>();
                t.rotation = Quaternion.identity; // measured square to the world: a turned cube's bounds are wider
                t.localScale = _carriedRestScale * BlockCarryPose.ScaleFor(LargestEdge(_carriedParts) / Mathf.Max(0.01f, transform.lossyScale.y));
            }

            // The block stands upright whatever the hands do, its centre above the head. The model's own origin is not its
            // centre (a held cube sits ahead of the palm), so the bounds say where it is.
            t.rotation = transform.rotation;
            float height = Mathf.Lerp(BlockCarryPose.BlockStartHeight, BlockCarryPose.BlockHeight, _carryBlend);
            var want = transform.TransformPoint(new Vector3(0f, height, BlockCarryPose.BlockForward));
            t.position += want - Centre(_carriedParts, t.position);
            return true;
        }

        private void RestoreCarried()
        {
            if (_carried != null)
            {
                _carried.transform.localScale = _carriedRestScale;
                _carried.transform.localPosition = _carriedRestPos;
            }

            _carried = null;
            _carriedParts = null;
        }

        private static float LargestEdge(Renderer[] parts)
        {
            if (parts == null || parts.Length == 0)
            {
                return 0f;
            }

            var b = parts[0].bounds;
            for (int i = 1; i < parts.Length; i++)
            {
                b.Encapsulate(parts[i].bounds);
            }

            return Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        }

        private static Vector3 Centre(Renderer[] parts, Vector3 fallback)
        {
            if (parts == null || parts.Length == 0 || parts[0] == null)
            {
                return fallback;
            }

            var b = parts[0].bounds;
            for (int i = 1; i < parts.Length; i++)
            {
                if (parts[i] != null)
                {
                    b.Encapsulate(parts[i].bounds);
                }
            }

            return b.center;
        }
    }
}
