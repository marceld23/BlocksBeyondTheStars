// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Geometry;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>The package "sky-lights &amp; other side" (epic #3891): the capability word for the other side and the
    /// held-beam weapon's intent (#3898).</summary>
    public sealed partial class NetworkClient
    {
        /// <summary>Whether this client announces <c>otherside1</c> (#3904): set by the Unity client, which can show the
        /// other side (the grey grade, the gate shimmer, the HUD mark, the sound layer). A headless client leaves it off —
        /// the server then refuses a crossing at a rune gate with one clear line. Toggles the word in
        /// <see cref="Features"/>, keeping the rest of the list as it is.</summary>
        public bool ShowsOtherSide
        {
            get => HasFeature(ClientFeatures.OtherSide1);
            set => SetFeature(ClientFeatures.OtherSide1, value);
        }

        /// <summary>One tick of a held-beam weapon (#3898): the entity under the beam as this client sees it ("" = none),
        /// the camera ray, and — when the beam ends on terrain — the struck cell. The server resolves at most one tick
        /// per the weapon's <c>beamTick</c> and validates everything like a shot.</summary>
        public void SendBeamWeaponTick(string entityId, Vector3f dir, bool hasCell = false, int x = 0, int y = 0, int z = 0)
            => Send(new BeamWeaponIntent
            {
                Active = true,
                EntityId = entityId ?? string.Empty,
                DirX = dir.X,
                DirY = dir.Y,
                DirZ = dir.Z,
                HasCell = hasCell,
                X = x,
                Y = y,
                Z = z,
            });

        /// <summary>The fire control was released: the held beam is over (#3898).</summary>
        public void SendBeamWeaponStop() => Send(new BeamWeaponIntent { Active = false });
    }
}
