// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>What a state message asks the client to stage besides the look.</summary>
    public enum OtherSideCue
    {
        /// <summary>Nothing — the state only (the join, a count, a forced or silent return).</summary>
        None = 0,

        /// <summary>The player just crossed: the soft veil and the crossing sound.</summary>
        Cross,

        /// <summary>The player just came back at a gate: the soft veil and the return sound.</summary>
        Return,
    }

    /// <summary>
    /// The other side as the client sees it (#3906, epic #3891), Unity-free: the state the server sent
    /// (<see cref="OtherSideState"/>), the 0..1 blend the grey look, the gates' lights and the HUD mark ease by, the entry
    /// gate, the sound-block meter, and the questions the player rig asks before it STARTS something ("may I mine here?").
    /// The server is the truth — it refuses everything but moving, talking, rune stones, the Echoes, the Resonance Blaster
    /// and the melody bar over there; the predicates only keep the client from showing a swing, a ghost or a prompt the
    /// server would take back a moment later.
    /// <para>The blend eases over <see cref="BlendSeconds"/> when the side changes while playing (a crossing, a return, a
    /// forced or silent return) and is set at once by a state that is no change of side as the player sees it: the join
    /// (<c>sync</c>, <c>rejoined</c>) and a reason this client does not know.</para>
    /// </summary>
    public sealed class OtherSideView
    {
        /// <summary>Seconds the look takes from one side to the other.</summary>
        public const float BlendSeconds = 1.2f;

        /// <summary>True while the player is on the other side (the server's word).</summary>
        public bool Over { get; private set; }

        /// <summary>0 (our side) … 1 (over there), moving linearly; <see cref="Look"/> is the eased value to draw with.</summary>
        public float Blend { get; private set; }

        /// <summary>The blend, eased in and out (smoothstep) — what the grade and the lights use.</summary>
        public float Look => Blend * Blend * (3f - (2f * Blend));

        /// <summary>The entry gate is known (exactly while over there).</summary>
        public bool HasGate { get; private set; }

        /// <summary>The entry gate's centre, world coordinates. Only meaningful while <see cref="HasGate"/>.</summary>
        public float GateX { get; private set; }

        /// <summary>See <see cref="GateX"/>.</summary>
        public float GateY { get; private set; }

        /// <summary>See <see cref="GateX"/>.</summary>
        public float GateZ { get; private set; }

        /// <summary>The player's sound blocks standing in this world.</summary>
        public int SoundBlocksUsed { get; private set; }

        /// <summary>How many one player may have standing in one world (0: the server has no sound layer).</summary>
        public int SoundBlockCap { get; private set; }

        /// <summary>How many more the player may shape.</summary>
        public int SoundBlocksLeft => Math.Max(0, SoundBlockCap - SoundBlocksUsed);

        /// <summary>The cap is reached: one more is refused until some are dissolved.</summary>
        public bool AtSoundBlockCap => SoundBlockCap > 0 && SoundBlocksUsed >= SoundBlockCap;

        /// <summary>Moves with every state the server sent — a cheap "anything new?" for the HUD.</summary>
        public int Version { get; private set; }

        /// <summary>The gates around the player while over there (<see cref="OtherSideGates"/>, through
        /// <see cref="ApplyGates"/>) — and, after a return, the last of them until the look has eased back.</summary>
        public OtherSideGateBook Gates { get; } = new OtherSideGateBook();

        // ---------------- What may I start? ----------------

        /// <summary>Mining (a swing, progress, sparks) — never over there.</summary>
        public bool MayMine => !Over;

        /// <summary>Placing a block, and the ghost that previews it.</summary>
        public bool MayPlace => !Over;

        /// <summary>Loot, deposit, crates, the ship's hold.</summary>
        public bool MayOpenContainers => !Over;

        /// <summary>A swing or a shot at anything.</summary>
        public bool MayAttack => !Over;

        /// <summary>Doors, machines, stations, beds, seats, pads, boards — everything the use key works but a rune stone.</summary>
        public bool MayUseDevices => !Over;

        /// <summary>Consumables, the scanner, the rope gun and every gadget but the Resonance Blaster.</summary>
        public bool MayUseItems => !Over;

        /// <summary>The Resonance Blaster: the sonic push on our side, shaping sound over there — always the player's to use.</summary>
        public bool MayUseBlaster => true;

        /// <summary>A rune stone: it sings on both sides, and a gate's stone is the way across and back.</summary>
        public bool MayUseRuneStone => true;

        /// <summary>The melody bar.</summary>
        public bool MayPlayMelody => true;

        /// <summary>Whether a talk may start: on our side with anybody; over there only with somebody who lives there.</summary>
        public bool MayTalkTo(bool livesOverThere) => !Over || livesOverThere;

        /// <summary>Hunger, air and temperature warnings: nothing drains and nothing hurts over there, so nothing warns.</summary>
        public bool ShowsNeedWarnings => !Over;

        /// <summary>While over there the Resonance Blaster shapes sound (its two keys), instead of pushing.</summary>
        public bool ShapesSound(bool blasterInHand) => Over && blasterInHand;

        // ---------------- The state ----------------

        /// <summary>Takes a state message. Returns what to stage with it (a veil and a sound at a crossing or a return at a
        /// gate; nothing for the join, a count, or a forced or silent return).</summary>
        public OtherSideCue Apply(OtherSideState? m)
        {
            if (m is null)
            {
                return OtherSideCue.None;
            }

            Version++;
            bool flipped = m.Over != Over;
            Over = m.Over;
            HasGate = m.Over && m.HasGate;
            GateX = m.GateX;
            GateY = m.GateY;
            GateZ = m.GateZ;
            SoundBlocksUsed = Math.Max(0, m.SoundBlocksUsed);
            SoundBlockCap = Math.Max(0, m.SoundBlockCap);
            var cue = OtherSideCue.None;
            if (flipped) // else a count moved: the look keeps easing where it is
            {
                string reason = m.Reason ?? string.Empty;
                if (!Eases(reason))
                {
                    Blend = Over ? 1f : 0f;
                }
                else if (HasFanfare(reason))
                {
                    cue = Over ? OtherSideCue.Cross : OtherSideCue.Return;
                }
            }

            DropGatesOnOurSide(); // at once when the look does not ease; else when it has eased back (Tick)
            return cue;
        }

        /// <summary>Takes the server's gate list. Over there it replaces the book. On our side nothing is listed — the
        /// server's empty list says "the lights go out": the last gates stay in the book while the look eases back, so
        /// their lights fade with it instead of popping off, and are dropped when it has arrived
        /// (<see cref="Tick"/>).</summary>
        public void ApplyGates(OtherSideGates? m)
        {
            if (Over || (m?.Gates is { Length: > 0 }))
            {
                Gates.Apply(m);
                return;
            }

            DropGatesOnOurSide();
        }

        /// <summary>One frame: the blend moves toward the side the player is on.</summary>
        public void Tick(float dt)
        {
            float target = Over ? 1f : 0f;
            if (Blend == target || dt <= 0f)
            {
                return;
            }

            float step = dt / BlendSeconds;
            Blend = Blend < target ? Math.Min(target, Blend + step) : Math.Max(target, Blend - step);
            DropGatesOnOurSide();
        }

        /// <summary>On our side with the look fully back, no gate carries a light — whether or not the server's empty
        /// list arrived. Until then the last gates stay, fading with the look.</summary>
        private void DropGatesOnOurSide()
        {
            if (!Over && Blend <= 0f)
            {
                Gates.Clear();
            }
        }

        /// <summary>A new session or a new world: our side, at once (the server says otherwise right after, if so).</summary>
        public void Reset()
        {
            Version++;
            Over = false;
            Blend = 0f;
            HasGate = false;
            SoundBlocksUsed = 0;
            Gates.Clear();
        }

        /// <summary>The reasons the look eases for: every change of side that happens while the player plays.</summary>
        private static bool Eases(string reason)
            => reason == OtherSideReasons.Crossed || reason == OtherSideReasons.Returned || reason == OtherSideReasons.Admin
               || reason == OtherSideReasons.Forced || reason == OtherSideReasons.Moved;

        /// <summary>The reasons that are the player's own step through a gate (or the QA verb): a veil and a sound.</summary>
        private static bool HasFanfare(string reason)
            => reason == OtherSideReasons.Crossed || reason == OtherSideReasons.Returned || reason == OtherSideReasons.Admin;

        // ---------------- The use key ----------------

        /// <summary>
        /// Over there the use key works a rune stone and talks to those who live there — nothing else. Clears every other
        /// fact of this frame's interact pick, so neither the prompt line nor the key offers what the server refuses.
        /// On our side the facts stay as they are.
        /// </summary>
        /// <param name="facts">This frame's facts, gathered as always.</param>
        /// <param name="aimedBlockIsRuneStone">The usable block under the crosshair is a rune stone.</param>
        /// <param name="aimedNpcLivesOverThere">The person under the crosshair lives over there.</param>
        /// <param name="nearNpcLivesOverThere">The nearest person in talk reach lives over there.</param>
        public void FilterInteraction(InteractFacts facts, bool aimedBlockIsRuneStone, bool aimedNpcLivesOverThere, bool nearNpcLivesOverThere)
        {
            if (!Over || facts is null)
            {
                return;
            }

            bool rune = facts.UsableBlock && aimedBlockIsRuneStone;
            bool aimed = facts.AimedNpc && aimedNpcLivesOverThere;
            bool near = facts.TalkNear && nearNpcLivesOverThere;
            facts.Clear();
            facts.UsableBlock = rune;
            facts.AimedNpc = aimed;
            facts.TalkNear = near;
        }
    }
}
