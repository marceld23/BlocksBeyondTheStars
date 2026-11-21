// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The other side on the client (#3906, #3908, epic #3891): the server's state (<c>OtherSideState</c>) and gate list
    /// (<c>OtherSideGates</c>) feed <see cref="OtherSide"/>, the Unity-free model everything else asks — the grey look and
    /// the gates' lights (<see cref="OtherSideFx"/>), the HUD mark (<c>HudUi.OtherSide.cs</c>), the player rig's guard and
    /// the Resonance Blaster's shaping (<c>PlayerController.OtherSide.cs</c>). The server is the truth: this only draws
    /// what it says and keeps the client from starting what it would refuse.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        private OtherSideFx _otherSideFx;

        /// <summary>Where the player stands — on the other side or not — with the eased look, the entry gate, the gates
        /// around, the sound-block meter and the "may I start this?" questions.</summary>
        public OtherSideView OtherSide { get; } = new OtherSideView();

        /// <summary>The Resonance Blaster's two keys over there and their pace (#3908).</summary>
        public SoundShaper SoundShaper { get; } = new SoundShaper();

        /// <summary>The melody bar as the palette (#3908): the tone played last is the next sound block's tone.</summary>
        public SoundPalette SoundPalette { get; } = new SoundPalette();

        /// <summary>The tones of a walk over sound blocks (#3908).</summary>
        public SoundStepTones SoundSteps { get; } = new SoundStepTones();

        /// <summary>The other side's subscriptions; called from <see cref="WireKnowledge"/>.</summary>
        private void WireOtherSide()
        {
            if (_otherSideFx == null)
            {
                _otherSideFx = gameObject.AddComponent<OtherSideFx>();
                _otherSideFx.Game = this;
            }

            Network.OtherSideStateReceived += OnOtherSideState;
            Network.OtherSideGatesReceived += m => OtherSide.Gates.Apply(m);
            Network.ActionRejected += m =>
            {
                if (m != null && m.Action == "gadget" && OtherSide.Over)
                {
                    SoundShaper.Hold(); // the server holds the blaster for a second after a refusal — so does the trigger
                }
            };
            Network.JoinAccepted += _ => ForgetOtherSide();
        }

        /// <summary>A fresh join: our side until the server says otherwise (it does, right after); the palette starts at
        /// tone 0 again, as the server's does with a new session.</summary>
        private void ForgetOtherSide()
        {
            OtherSide.Reset();
            SoundShaper.Reset();
            SoundPalette.Reset();
            SoundSteps.Reset();
        }

        /// <summary>Tells the client world's light index that a sound block's per-cell glow is only how it looks — its
        /// tone's brightness, drawn as emission by the mesher — and never a light source: a player may shape up to 2000 of
        /// them, and every indexed light is a flood fill. Called where the world's other light rules are set
        /// (<see cref="RebuildPlantLights"/>).</summary>
        private void RegisterSoundBlockLook()
        {
            var sound = Content?.GetBlock(BlocksBeyondTheStars.Shared.Sounders.OtherSideRules.SoundBlockKey);
            World?.SetGlowLookBlocks(sound != null ? new[] { sound.NumericId.Value } : null);
        }

        private void OnOtherSideState(OtherSideState m)
        {
            bool was = OtherSide.Over;
            var cue = OtherSide.Apply(m);
            if (was != OtherSide.Over)
            {
                SoundSteps.Reset(); // the side changed: the feet start anew (a count alone must not replay a tone)
            }

            if (cue != OtherSideCue.None && _otherSideFx != null)
            {
                _otherSideFx.Stage(cue);
            }
        }
    }
}
