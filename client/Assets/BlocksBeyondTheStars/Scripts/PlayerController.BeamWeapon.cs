// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The held beam of a weapon (#3898, the Guardian laser), the player's own side. A press of the fire control — the
    /// fire key, the left click, the pad's fire button or trigger, the touch attack button — is the weapon's ordinary
    /// shot, fired by the code that always fired it. This file only watches the control stay DOWN: the Unity-free
    /// <see cref="BeamTrigger"/> decides when a hold has become a beam, when a tick goes to the server and when it ends;
    /// here the control is read, the beam is aimed where the player looks, drawn (<see cref="HeldBeamLine"/>) and the
    /// ticks are sent. Nothing here decides a hit or spends energy — the server does (<c>GameServerBeamWeapons</c>).
    /// </summary>
    public sealed partial class PlayerController
    {
        private readonly BeamTrigger _beamTrigger = new BeamTrigger();
        private HeldBeamLine _beamLine;
        private int _beamFrame = -1;
        private bool _beamHintShown;
        private string _beamAutoTargetId;
        private Vector3 _beamAutoTargetPos;

        /// <summary>Once a frame from the on-foot update: feeds the fire control to the trigger and does what it says.</summary>
        private void HandleBeamWeapon()
        {
            _beamFrame = Time.frameCount;
            if (Game == null)
            {
                return;
            }

            var tool = HeldTool();
            bool beamWeapon = BeamWeaponRules.IsBeamWeapon(tool);
            bool held = InputMap.Held(InputAction.PrimaryFire) || InputMap.PrimaryHeld();
            bool canBeam = beamWeapon && Camera != null && Game.Network != null && !CarryHoldsHands && !BinocularsRaised && Game.Health > 0f
                && !OverThere; // #3906: no weapon fires over there (the server refuses the beam)
            bool canPay = beamWeapon && BeamWeaponRules.CanPayTick(tool, Game.SuitEnergy, Game.SuitEnergyMax);
            float tick = beamWeapon ? BeamWeaponRules.Tick(tool) : BeamWeaponRules.DefaultTick;
            var step = _beamTrigger.Update(Time.deltaTime, held, canBeam, canPay, tick);

            if (beamWeapon && !_beamHintShown)
            {
                // Once per world: the weapon in the hand does something no other weapon does.
                _beamHintShown = true;
                Game.ShowMessage(HudUi.ExpandKeyTokens(Game.Localizer, Game.Localizer?.Get("ui.hud.beam_hold")
                    ?? "{key:PrimaryFire}: tap for a shot, hold to fire the beam. The beam drains suit energy."));
            }

            if (step.Starved)
            {
                Game.ShowMessage(Game.Localizer?.Get("srv.attack.beam_reserve")
                    ?? "The beam stops here — your suit keeps a reserve of energy so you are never stranded.");
            }

            if (step.Stopped)
            {
                EndOwnBeam(tellServer: true);
            }

            if (_beamTrigger.Beaming && beamWeapon && Camera != null)
            {
                DriveOwnBeam(tool, step.Tick);
            }
        }

        /// <summary>The beam is on: aims it along the camera ray this frame, draws it from the muzzle, and — on a tick —
        /// sends the server what it meets (the entity under it, else the block it ends on).</summary>
        private void DriveOwnBeam(ToolProperties tool, bool tick)
        {
            var ct = Camera.transform;
            float range = BeamWeaponRules.Range(tool);
            string targetId = null;
            Vector3 targetPos = default;
            bool aimed = AimEnemy(range, out var aimId, out var aimPos, out float terrainDist);
            if (aimed)
            {
                targetId = aimId; // what the crosshair is on always wins
                targetPos = aimPos;
            }

            if (tick)
            {
                // Auto-aim is asked once per tick (its line-of-fire walks are not for every frame) and holds until the next.
                _beamAutoTargetId = !aimed && Game.AutoAimOn ? BestConeTarget(range, AutoAimCone, out _beamAutoTargetPos) : null;
            }

            if (!aimed && _beamAutoTargetId != null)
            {
                targetId = _beamAutoTargetId;
                targetPos = _beamAutoTargetPos;
            }

            bool terrain = targetId == null && terrainDist < range - 0.01f;
            Vector3 end = targetId != null
                ? targetPos + (Vector3.up * 0.4f)
                : ct.position + (ct.forward * Mathf.Min(range, terrainDist));
            Vector3 normal = terrain ? FaceNormal(end, ct.forward) : Vector3.zero;
            if (_beamLine == null)
            {
                _beamLine = new HeldBeamLine(FxLook.ForItem(Game.Content, Game.ItemInSlot(Game.SelectedHotbarSlot)), local: true);
            }

            _beamLine.Draw(Muzzle(ct), end, targetId != null || terrain, normal);
            if (!tick || Game.Network == null)
            {
                return;
            }

            var f = ct.forward;
            bool hasCell = false;
            Vector3Int cell = default;
            if (targetId == null)
            {
                // The cell a shot would set alight (#788): the first block of any kind, so a leaf counts.
                hasCell = TerrainHit(ct.position, f, range, out cell, out _);
            }

            Game.Network.SendBeamWeaponTick(targetId ?? string.Empty, new Vector3f(f.x, f.y, f.z), hasCell, cell.x, cell.y, cell.z);
            if (targetId != null)
            {
                Game.LastShotTargetId = targetId;
                Game.LastShotTime = Time.time;
            }

            TriggerSwing(); // the pistol's short kick with every tick
        }

        /// <summary>Takes the own beam away; with <paramref name="tellServer"/> the release goes to the server too.</summary>
        private void EndOwnBeam(bool tellServer)
        {
            if (_beamLine != null)
            {
                _beamLine.End();
                _beamLine = null;
            }

            _beamAutoTargetId = null;
            if (tellServer)
            {
                Game?.Network?.SendBeamWeaponStop();
            }
        }

        /// <summary>From <c>OnDestroy</c>: the world is going away — the beam's line and sound go without a fade.</summary>
        private void DropOwnBeam()
        {
            if (_beamLine != null)
            {
                _beamLine.Drop();
                _beamLine = null;
            }
        }

        /// <summary>The server ended the beam (the suit's reserve, a seat, a crossing …): it is off until the control is
        /// released and pressed again.</summary>
        public void OnBeamStoppedByServer()
        {
            if (_beamTrigger.ServerStopped())
            {
                EndOwnBeam(tellServer: false);
            }
        }

        /// <summary>From <c>LateUpdate</c>: the on-foot update did not run this frame (a menu, a seat, a vehicle, the
        /// ship's view) — a beam that was on ends, and a control still down is no new press when the input comes back.</summary>
        private void LateBeamWeapon()
        {
            if (_beamFrame != Time.frameCount && _beamTrigger.Cancel())
            {
                EndOwnBeam(tellServer: true);
            }
        }
    }
}
