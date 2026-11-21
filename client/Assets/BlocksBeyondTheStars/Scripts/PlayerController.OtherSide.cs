// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Sounders;
using BlocksBeyondTheStars.Shared.State;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The player rig on the other side (#3906, #3908). Over there the server refuses everything but moving, talking,
    /// rune stones, the Echoes, the Resonance Blaster and the melody bar — so the rig does not START the rest: no mining
    /// swing, no placing ghost, no attack, no container or station prompt (<see cref="OverThere"/> gates the entry
    /// points, <see cref="FilterInteractionOverThere"/> the use key's pick). With the Resonance Blaster in hand the two
    /// hand keys shape and dissolve sound blocks instead (<see cref="OtherSideOwnsHands"/>, paced by
    /// <see cref="SoundShaper"/>), the selection outline shows the cell that would be shaped
    /// (<see cref="OtherSideOutlineCell"/>), and walking onto a sound block sounds its tone (<see cref="OtherSideSteps"/>).
    /// On our side nothing here does anything.
    /// </summary>
    public sealed partial class PlayerController
    {
        private System.Func<int, int, int, bool> _soundSolid; // cached, so the aim allocates nothing per frame
        private SoundAimResult _soundAim;
        private bool _soundAimValid;
        private int _soundAimFrame = -1;
        private string _blasterCheckedKey;
        private bool _blasterHeld;
        private object _soundBlockContent;
        private ushort _soundBlockId;
        private bool _soundStepsRunning;

        /// <summary>True while the player is on the other side (the server's word, <see cref="GameBootstrap.OtherSide"/>).</summary>
        private bool OverThere => Game != null && Game.OtherSide.Over;

        /// <summary>Whether the selected hotbar item is the Resonance Blaster (looked up once per held item).</summary>
        private bool HoldsResonanceBlaster()
        {
            if (!ReferenceEquals(_heldKey, _blasterCheckedKey))
            {
                _blasterCheckedKey = _heldKey;
                _blasterHeld = !string.IsNullOrEmpty(_heldKey) && ItemKey.Base(_heldKey) == SoundersIds.ResonanceBlaster;
            }

            return _blasterHeld;
        }

        /// <summary>
        /// The hand keys over there, from <see cref="HandleInteract"/>: true when the player is over there — the caller then
        /// does nothing else with the click (no mining, no placing, no item use). With the Resonance Blaster in hand the
        /// use key shapes a sound block at the aim and the mine key dissolves the sound block under the crosshair, each at
        /// the server's pace while held.
        /// </summary>
        private bool OtherSideOwnsHands()
        {
            if (!OverThere)
            {
                return false;
            }

            bool aimed = HoldsResonanceBlaster() && ResolveSoundAim(out _);
            var aim = _soundAim;
            bool onSound = aimed && aim.Surface && IsSoundBlockCell(aim.HitX, aim.HitY, aim.HitZ);
            var action = Game.SoundShaper.Tick(Time.deltaTime, aimed, InputMap.SecondaryHeld(), InputMap.PrimaryHeld(), onSound);
            if (action == SoundShapeAction.None)
            {
                return true;
            }

            Game.Network.SendUseGadget(SoundersIds.ResonanceBlaster, new Vector3f(aim.PointX, aim.PointY, aim.PointZ),
                action == SoundShapeAction.Dissolve);
            if (Camera != null)
            {
                // The device's own small glow, as for every gadget; the block and its tone come from the server.
                FxGadgets.Intent(FxLook.ForItem(Game.Content, SoundersIds.ResonanceBlaster), Muzzle(Camera.transform));
            }

            return true;
        }

        /// <summary>The blaster's aim this frame (resolved once a frame, whoever asks): the point to send and the cell it
        /// means, as the server will read it (<see cref="SoundAim"/>).</summary>
        private bool ResolveSoundAim(out SoundAimResult aim)
        {
            if (_soundAimFrame != Time.frameCount)
            {
                _soundAimFrame = Time.frameCount;
                _soundAimValid = false;
                if (Game != null && Game.World != null && Camera != null)
                {
                    if (_soundSolid == null)
                    {
                        _soundSolid = SoundSolidAt;
                    }

                    var tool = Game.Content?.GetItem(SoundersIds.ResonanceBlaster)?.Tool;
                    float reach = tool != null && tool.Range > 0f ? tool.Range : SoundAim.DefaultReach;
                    Vector3 o = Camera.transform.position, d = Camera.transform.forward, feet = transform.position;
                    _soundAim = SoundAim.Resolve(_soundSolid, o.x, o.y, o.z, d.x, d.y, d.z, feet.x, feet.y, feet.z, reach);
                    _soundAimValid = true;
                }
            }

            aim = _soundAim;
            return _soundAimValid;
        }

        /// <summary>A cell the crosshair stops at: any block but a fluid (sound blocks are blocks of the client's world).</summary>
        private bool SoundSolidAt(int x, int y, int z)
        {
            var id = Game.World.GetBlock(x, y, z);
            return !id.IsAir && !IsFluidBlock(id);
        }

        /// <summary>Whether the cell holds a sound block.</summary>
        private bool IsSoundBlockCell(int x, int y, int z)
        {
            if (Game?.World == null || Game.Content == null)
            {
                return false;
            }

            if (!ReferenceEquals(Game.Content, _soundBlockContent))
            {
                _soundBlockContent = Game.Content;
                var def = Game.Content.GetBlock(OtherSideRules.SoundBlockKey);
                _soundBlockId = def != null ? def.NumericId.Value : (ushort)0;
            }

            return _soundBlockId != 0 && Game.World.GetBlock(x, y, z).Value == _soundBlockId;
        }

        /// <summary>The selection outline over there: with the blaster in hand, the cell the next shaping pulse would fill
        /// (in front of the aimed face, or the point in the open air at the blaster's reach). Nothing otherwise — there is
        /// nothing to mine or place.</summary>
        private bool OtherSideOutlineCell(out Vector3Int cell)
        {
            cell = default;
            if (!HoldsResonanceBlaster() || !ResolveSoundAim(out var aim))
            {
                return false;
            }

            cell = new Vector3Int(aim.CellX, aim.CellY, aim.CellZ);
            return true;
        }

        /// <summary>Over there the use key works a rune stone and talks to those who live there (the Echoes) — every other
        /// fact of this frame's pick is dropped, so neither the prompt nor the key offers what the server refuses.</summary>
        private void FilterInteractionOverThere()
        {
            if (!OverThere)
            {
                return;
            }

            bool rune = _aim.BlockKey == "rune_stone";
            bool aimedLivesThere = _facts.AimedNpc && LivesOverThere(VendorReach.ById(Game.Npcs, _aim.Npc));
            bool nearLivesThere = _facts.TalkNear && LivesOverThere(Game.NearestTalkableNpc());
            Game.OtherSide.FilterInteraction(_facts, rune, aimedLivesThere, nearLivesThere);
            _aim.Door = 0;
            _aim.Crate = null;
            _aim.Lift = null;
        }

        /// <summary>Whether a person lives on the other side — an Echo. (The list does not say more; the server refuses a
        /// talk with an Echo of our side.)</summary>
        private static bool LivesOverThere(BlocksBeyondTheStars.Networking.Messages.NetNpc npc)
            => npc != null && npc.Look == SoundersIds.Echo;

        /// <summary>The aim readouts that would offer something the server refuses over there (a station, a device, a base,
        /// a scan): cleared, so the HUD stays quiet. Called at the end of <see cref="PublishAimState"/>.</summary>
        private void QuietAimStateOverThere()
        {
            if (!OverThere)
            {
                return;
            }

            _lookedStation = string.Empty;
            _nearStation = string.Empty;
            Game.NearbyStation = string.Empty;
            Game.AimedStationBlock = null;
            Game.AimedCrystalDevice = null;
            Game.ScanTargetInView = false;
            Game.AimedOwnBase = null;
            Game.AimedBasePadCell = null;
        }

        /// <summary>Per on-foot frame: stepping onto a sound block sounds its tone, softly and only for this player — a
        /// staircase of sound becomes a melody.</summary>
        private void OtherSideSteps(bool grounded)
        {
            if (!OverThere || Game.World == null)
            {
                if (_soundStepsRunning)
                {
                    _soundStepsRunning = false;
                    Game?.SoundSteps.Reset();
                }

                return;
            }

            _soundStepsRunning = true;
            var p = transform.position;
            int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y - 0.2f), z = Mathf.FloorToInt(p.z);
            bool sound = IsSoundBlockCell(x, y, z);
            int glow = sound ? Game.World.GetGlow(x, y, z) : 0;
            if (Game.SoundSteps.Step(Time.deltaTime, grounded, x, y, z, sound, glow, out int tone))
            {
                ClientAudio.Instance?.PlayOwn(NoteSequenceScheduler.ClipId(SoundersIds.InstrumentVoice, tone), 0.34f);
            }
        }
    }
}
