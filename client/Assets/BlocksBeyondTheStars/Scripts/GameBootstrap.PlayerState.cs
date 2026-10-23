// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using System.IO;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Networking.Transport;
using BlocksBeyondTheStars.Shared.Content;
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Localization;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// GameBootstrap's local player state (#2912): vitals, aboard / EVA / return-to-ship, respawn, the Bio lab,
    /// inventory, equipment, cargo, unlocks and achievements, and OnPlayerState, which fills them from the server.
    /// </summary>
    public sealed partial class GameBootstrap
    {
        // Latest authoritative player vitals for the HUD.
        public float Health { get; private set; } = 100f;
        public float Oxygen { get; private set; } = 100f;
        public float SuitEnergy { get; private set; } = 100f;
        public float Hunger { get; private set; } = 100f;

        /// <summary>True while the suit's climate control is fighting extreme heat/cold/vacuum (#666) —
        /// the HUD colors the energy bar and names the drain; server-authoritative.</summary>
        public bool SuitClimateActive { get; private set; }

        /// <summary>Our own stealth cloak is up (#2291) — server-authoritative (it needs the worn suit and energy, and drops
        /// when either runs out). The HUD gear strip lights the cloak from it; its sound plays when this flips.</summary>
        public bool Stealthed { get; private set; }

        /// <summary>The exposure meter 0..1 (2026-09, Titas) and whether it is running / counting heat — server-authoritative.</summary>
        public float Exposure { get; private set; }
        public bool ExposureActive { get; private set; }
        public bool ExposureHot { get; private set; }

        /// <summary>Valuma's mood (2026-09): the music darkens after a long stay — server-authoritative.</summary>
        public bool Uneasy { get; private set; }

        /// <summary>Which life support keeps us breathing (#794): 0 none (own tank / the world's air),
        /// 1 ship cabin, 2 station, 3 base (zone cube or sealed room). Server-authoritative — the client
        /// cannot mirror the sealed-room fill locally; 0 on servers older than this field.</summary>
        public byte LifeSupportSource { get; private set; }

        /// <summary>Our own player id (from the join handshake), used to pick our state out of broadcasts.</summary>
        public string LocalPlayerId { get; private set; }

        /// <summary>Friendly current location ("System · Planet") for the HUD.</summary>
        public string LocationName { get; private set; } = string.Empty;
        public string StationName { get; private set; } = string.Empty; // non-empty while boarded on a station
        public string CurrentStationId { get; private set; } = string.Empty; // id of the boarded station (for in-station rename)

        /// <summary>AI-core tier of the active ship (1 = bare VEGA, 2 = Mk2, 3 = Mk3) — gates the autopilot.</summary>
        public int AiCoreTier { get; private set; } = 1;

        /// <summary>True while the basic training runs (#2469: the tutorial pack) — the Character tab offers "skip tutorial" then,
        /// "restart tutorial" otherwise. Since #2462 the server reports it on every VEGA line
        /// (<c>ShipAiLine.TutorialActive</c>), whatever the chip shows; an older server is read from the chip's key.</summary>
        public bool OnboardingActive { get; private set; }

        /// <summary>Whether we're currently inside the ship (authoritative; enables cargo crafting).</summary>
        public bool Aboard { get; private set; }
        public bool InEva { get; private set; } // server-authoritative: floating outside the ship in space

        /// <summary>When set, the on-foot player floats in zero-g instead of falling — <see cref="PlayerController"/>
        /// drops gravity. Mirrors the server's <c>AboveAtmosphere</c> flag: above a planet's atmosphere line (item 10 —
        /// a tower, or since #578 a high summit) or, on a player station, outside its gravity box (#1485/#1842). See
        /// <see cref="OnFootAbovePlanet"/> for the planet case alone.</summary>
        public bool OnFootInSpace { get; set; }

        /// <summary>#2276: zero-g above a PLANET's atmosphere line — not a station's. Above a planet the suit sinks gently
        /// when no vertical control is held (a way back down that needs no knowledge) and the HUD shows the ZERO-G badge;
        /// a station's zero-g keeps hovering in place.</summary>
        public bool OnFootAbovePlanet => OnFootInSpace && string.IsNullOrEmpty(StationName);

        /// <summary>Zero-g construction mode on the boarded player station (#1842), server-authoritative and
        /// session-only. While set, <see cref="OnFootInSpace"/> is a chosen float rather than a drift over the
        /// deck's edge: the HUD shows a badge and the toggle hints instead of the atmosphere wording.</summary>
        public bool StationZeroG { get; private set; }

        /// <summary>Fleet-admin observer mode (issue #487), server-authoritative. The controller switches to
        /// free flight without collision, the HUD hides the hotbar/viewmodel and shows the SPECTATOR badge.
        /// Never set locally: invisibility is the server's decision, and the client only reflects it.</summary>
        public bool Spectating { get; private set; }

        /// <summary>This world allows free flight (Creative/Sandbox, or the <c>/fly</c> cheat) —
        /// server-authoritative. Only while this is set does double-tapping jump toggle flight.</summary>
        public bool CanFly { get; private set; }

        /// <summary>#2286: the <see cref="WorldTime"/> at which "Back to my ship" is ready again, from the server's last
        /// state update (the server sends the seconds left; the pause menu counts them down between updates). On the
        /// world clock, not <c>Time.time</c>: the server's cooldown runs on its uptime, which stands still while the
        /// world is held — and the pause menu that shows this countdown is exactly what holds it in singleplayer.</summary>
        private float _returnToShipReadyAt;

        /// <summary>Seconds until "Back to my ship" may be used again; 0 when it is ready (#2286). Frozen with the world.</summary>
        public float ReturnToShipCooldownLeft => Mathf.Max(0f, _returnToShipReadyAt - _worldClock.Now);

        /// <summary>#2559: the <c>Time.realtimeSinceStartup</c> at which the fight stops blocking "Back to my ship", from the
        /// server's last state update (or its last refusal). REAL time, unlike the cooldown: the server's fight grace keeps
        /// running while the pause menu holds the world (#2558), and that menu is where this countdown is shown.</summary>
        private float _returnToShipFightUntil;

        /// <summary>Seconds a fight still blocks "Back to my ship"; 0 when none does (#2559). Runs on while the world is held.</summary>
        public float ReturnToShipFightLeft => Mathf.Max(0f, _returnToShipFightUntil - Time.realtimeSinceStartup);

        /// <summary>#2559: a robber waits for the player's answer — "Back to my ship" is blocked without a countdown (the
        /// hold-up ends with the answer, not with a clock). From the server's last state update.</summary>
        public bool ReturnToShipRobberWaiting { get; private set; }

        /// <summary>#2559: bumped when the server puts the player back aboard through "Back to my ship" (the
        /// <c>@srv.return_ship.done</c> snap) — the pause menu closes on it, and only on it.</summary>
        public int ReturnToShipArrivals { get; private set; }

        /// <summary>#2559: bumped on every refused "Back to my ship" (<c>ActionRejected</c> with action "return_ship"); the
        /// open pause menu shows <see cref="ReturnToShipRefusalText"/> instead of closing.</summary>
        public int ReturnToShipRefusals { get; private set; }

        /// <summary>#2559: the last refusal of "Back to my ship", localized — shown inside the pause menu.</summary>
        public string ReturnToShipRefusalText { get; private set; } = string.Empty;

        /// <summary>Whether the pause menu offers "Back to my ship" right now (#2286): the world rule is on (an older server
        /// never sends it and gets no button — it would drop the intent anyway) and the player is on foot — not aboard,
        /// not flying or in EVA, not driving, not riding a train, not observing. The server checks all of it again, plus
        /// the gates the client cannot see (own ship landed here, not in a fight, not falling, the cooldown).</summary>
        public bool ReturnToShipOffered
            => Rules != null && string.Equals(Rules.ReturnToShip, "On", System.StringComparison.Ordinal)
               && !Aboard && !SpaceViewActive && !InEva && string.IsNullOrEmpty(InSpeeder) && string.IsNullOrEmpty(InTrain)
               && !Spectating
               && !Visiting; // #2725: a guest in a friend's ship goes home with "back to my ship" (U), not this landed-ship trip

        /// <summary>World position of the player's ship (for the HUD minimap / compass), once known.</summary>
        public Vector3? ShipPosition { get; private set; }

        /// <summary>The player's achievements with live progress, as last sent by the server (authoritative —
        /// the client never counts anything itself). Empty until the join snapshot arrives.</summary>
        public NetAchievement[] Achievements { get; private set; } = System.Array.Empty<NetAchievement>();

        /// <summary>The player's raw lifetime counters (blocks mined, worlds visited, blueprints researched …)
        /// as last sent with the achievement list — the "Journey" figures on the Progress page (#1103).</summary>
        public System.Collections.Generic.Dictionary<string, int> AchievementCounters { get; private set; } = new();

        /// <summary>The authoritative spawn the server reported for us; the rig snaps to it once.</summary>
        public Vector3? ServerSpawn { get; private set; }

        /// <summary>Set when the server respawns us (death) — the player controller snaps the camera/body
        /// to this point (the ship's heal-tank) on the next frame, then clears it.</summary>
        public Vector3? RespawnTarget;

        /// <summary>True while the "Du bist gestorben" / "Das Schiff wurde zerstört" prompt is up after a
        /// death or ship destruction. The server has already respawned us; the client holds the world
        /// reveal (on-foot settle) and the space-view landing teardown until the player clicks "Weiter",
        /// so the player/ship only re-appears once they confirm. Set/cleared by <see cref="RespawnPrompt"/>.</summary>
        public bool AwaitingRespawnConfirm;

        /// <summary>True while a server maintenance-restart countdown is running (set/cleared by
        /// <see cref="MaintenanceUi"/>). The disconnect screen reads it to tell a planned restart
        /// ("reconnect shortly") apart from an unexpected connection loss.</summary>
        public bool MaintenanceRestartPending;

        /// <summary>The bio lab on the client (#2201-#2206): the sample case, the running effects, the research book.</summary>
        public BioClientState Bio { get; } = new BioClientState();

        /// <summary>What the bio lab last answered, and a counter that goes up with every answer (the lab panel shows it once).</summary>
        public BioLabResult LastBioLabResult { get; private set; }
        public int BioLabResultCount { get; private set; }

        // Latest authoritative inventory (personal + ship cargo) for the UI.
        public NetItemStack[] Personal { get; private set; } = System.Array.Empty<NetItemStack>();

        /// <summary>The worn suit gear by <c>EquipSlot</c> index (#2110) — the only gear that works.</summary>
        public NetItemStack[] Equipment { get; private set; } = System.Array.Empty<NetItemStack>();

        /// <summary>How many personal slots the server keeps (#2110: 36 = quick-bar 9 + backpack 27; a server from
        /// before the slots sends 0 and the classic 24 is assumed).</summary>
        public int PersonalSlots { get; private set; } = 24;

        /// <summary>The item worn in an equipment slot, or empty.</summary>
        public string ItemInEquipSlot(int slot)
        {
            foreach (var s in Equipment)
            {
                if (s.Slot == slot)
                {
                    return s.Item;
                }
            }

            return string.Empty;
        }

        /// <summary>True while the gear is worn in one of the suit's slots (#2110) — what every suit effect reads.</summary>
        public bool Wears(string key)
        {
            foreach (var s in Equipment)
            {
                if (s.Count > 0 && BlocksBeyondTheStars.Shared.State.ItemKey.Base(s.Item) == key)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Maximum suit oxygen with the tank currently worn — the HUD bar's full mark (#1270). The
        /// bar used to divide by a flat 100, so a Tank III's 300 sat pinned at "full" until two thirds were gone.</summary>
        public float SuitOxygenMax { get; private set; } = BlocksBeyondTheStars.Shared.State.SuitEquipment.BaseOxygen;

        /// <summary>Maximum suit energy with the gear currently worn (#2297: 150 with the suit battery) — the HUD energy
        /// bar's full mark, the same formula the server fills and clamps to.</summary>
        public float SuitEnergyMax { get; private set; } = BlocksBeyondTheStars.Shared.State.SuitEquipment.BaseSuitEnergy;

        private void RefreshSuitStats()
        {
            SuitOxygenMax = Content == null
                ? BlocksBeyondTheStars.Shared.State.SuitEquipment.BaseOxygen
                : BlocksBeyondTheStars.Shared.State.SuitEquipment.MaxOxygen(Content.Items.Values, Wears) // #2110: worn, not carried
                  + BlocksBeyondTheStars.Shared.Bio.GearMods.Bonus(WornKeys(), BlocksBeyondTheStars.Shared.Bio.ModStat.Oxygen); // #2206: what the lab changed on a worn piece
            SuitEnergyMax = Content == null
                ? BlocksBeyondTheStars.Shared.State.SuitEquipment.BaseSuitEnergy
                : BlocksBeyondTheStars.Shared.State.SuitEquipment.MaxSuitEnergy(Content.Items.Values, Wears);
        }

        /// <summary>The item keys of the worn gear — what the lab changed on a piece rides in its key (#2206).</summary>
        public IEnumerable<string> WornKeys()
        {
            foreach (var s in Equipment)
            {
                if (s.Count > 0)
                {
                    yield return s.Item;
                }
            }
        }
        public NetItemStack[] Cargo { get; private set; } = System.Array.Empty<NetItemStack>();

        /// <summary>Item gains since the last HUD drain, aggregated per inventory update — the HUD
        /// pickup feed (#745) consumes these. Base item key + amount gained.</summary>
        public readonly Queue<(string Item, int Gained)> PickupGains = new Queue<(string, int)>();

        /// <summary>Blueprint keys whose knowledge threshold a knowledge gain just crossed — the HUD
        /// research toast (#763) consumes these. Unlike pickups they survive menus and flight: newly
        /// being ABLE to research something is rare enough to announce a little late rather than drop.</summary>
        public readonly Queue<string> ResearchAvailable = new Queue<string>();

        /// <summary>#2597: achievement keys just unlocked — the HUD's celebration banner (the research toast's slot) consumes
        /// these after any waiting research, one at a time with the success sound. Like research they survive menus and
        /// flight (the banner waits for the HUD); at most <see cref="AchievementBannerQueueCap"/> wait.</summary>
        public readonly Queue<string> AchievementsUnlocked = new Queue<string>();

        /// <summary>#2597: the most achievement banners that wait at once (a burst beyond that is not replayed one by one).</summary>
        public const int AchievementBannerQueueCap = 6;

        /// <summary>False until the first InventoryUpdate after a join landed — that one is the
        /// baseline snapshot, not a pickup (#745).</summary>
        private bool _inventoryBaselined;

        /// <summary>Total slots in the ship's cargo hold (capacity), so the cargo tab can show "used/total". 0
        /// when not aboard (the cargo list is empty then).</summary>
        public int CargoSlots { get; private set; }

        // Tracks the aboard edge so auto-stow fires once per boarding (not every state packet while aboard).
        private bool _wasAboard;

        /// <summary>Blueprint keys the player has unlocked (synced from the server) — drives craftable/locked UI.</summary>
        public System.Collections.Generic.HashSet<string> UnlockedBlueprints { get; private set; } = new();

        /// <summary>Minigame keys the player has downloaded from data cubes (synced from the server) — drives
        /// the Arcade collection menu. Mirrors <see cref="UnlockedBlueprints"/>.</summary>
        public System.Collections.Generic.HashSet<string> UnlockedGames { get; private set; } = new();

        /// <summary>Set when a new data-cube minigame arrives but the Arcade hasn't been opened since — the menu
        /// badges the Arcade entry while true. Cleared by <see cref="MarkArcadeSeen"/> when the Arcade is opened.</summary>
        public bool NewArcadeUnseen { get; private set; }
        public void MarkArcadeSeen() => NewArcadeUnseen = false;

        /// <summary>Whether a <c>GameUnlocks</c> list has arrived since the last connect. The first one seeds the
        /// collection (empty → N) and must not read as a fresh pickup (#1069); reset with the per-session state.</summary>
        private bool _unlocksSeeded;

        /// <summary>The last cell the player tried to mine (set by the controller) — if the server rejects the
        /// dig as already-empty, the client clears its ghost block there to heal a stale chunk view (B32).</summary>
        public Vector3Int LastMineCell;

        /// <summary>The item key in a personal inventory slot, or empty if none.</summary>
        public string ItemInSlot(int slot)
        {
            foreach (var s in Personal)
            {
                if (s.Slot == slot)
                {
                    return s.Item;
                }
            }

            return string.Empty;
        }

        /// <summary>The stack size in a personal inventory slot, or 0 if the slot is empty (#744).</summary>
        public int CountInSlot(int slot)
        {
            foreach (var s in Personal)
            {
                if (s.Slot == slot)
                {
                    return s.Count;
                }
            }

            return 0;
        }

        private void OnPlayerState(BlocksBeyondTheStars.Networking.Messages.PlayerStateUpdate m)
        {
            // Only our own authoritative state drives the HUD and the one-time spawn snap.
            if (!string.IsNullOrEmpty(LocalPlayerId) && m.PlayerId != LocalPlayerId)
            {
                return;
            }

            Bio.OnPlayerState(m); // #2202: the running status effects and the shield cushion
            LastHit.Observe(m.LastHitSeq, m.LastHitEntityId, m.LastHitFromX, m.LastHitFromY, m.LastHitFromZ, Time.time); // #2564: before the health, which the HUD reads
            Health = m.Health;
            Oxygen = m.Oxygen;
            SuitEnergy = m.SuitEnergy;
            Hunger = m.Hunger;
            SuitClimateActive = m.SuitClimateActive;
            if (m.Stealthed != Stealthed)
            {
                // #2291: the cloak's sound follows the server's answer, not the key — a refused toggle stays silent. Not
                // for the fleet-admin observer, whom the server cloaks with the same flag (#487).
                Stealthed = m.Stealthed;
                if (!m.Spectating && !Spectating)
                {
                    ClientAudio.Instance?.Cue(m.Stealthed ? "stealth_on" : "stealth_off");
                }
            }

            Exposure = m.Exposure;
            ExposureActive = m.ExposureActive;
            ExposureHot = m.ExposureHot;
            Uneasy = m.Uneasy;
            LifeSupportSource = m.LifeSupportSource;
            if (m.LifeSupportSource == 2 && _stationAirWarning.Length > 0 && LastMessage == _stationAirWarning)
            {
                // #1836: the station reports the pocket sealed again — the hull-open banner has no lifetime of its own
                // and would stay pinned over a breathing player until the next server line replaced it.
                LastMessage = string.Empty;
                _stationAirWarning = string.Empty;
            }
            // Comfort: auto-stow loose materials into the cargo hold the moment you board the ship (off by
            // default — opt in via Settings). Fires only on the not-aboard → aboard edge, and reuses the same
            // server-authoritative bulk "stow all" intent the cargo tab's button sends.
            if (m.AboardShip && !_wasAboard && Settings != null && Settings.AutoStowOnBoard)
            {
                Network?.SendMoveCargoItem(toCargo: true, item: string.Empty, bulkAll: true, quiet: true);
            }

            _wasAboard = m.AboardShip;
            Aboard = m.AboardShip;
            InEva = m.InEva;
            InSpeeder = m.InSpeeder ?? string.Empty;
            if (InTrain.Length > 0 && string.IsNullOrEmpty(m.InTrain))
            {
                RespawnTarget = new Vector3(m.X, m.Y, m.Z); // #2113: set down beside the wagon — the body snaps there
            }

            InTrain = m.InTrain ?? string.Empty; // #2113
            TrainSeat = m.TrainSeat;

            // Observer mode toggled by the server (issue #487) — tell the player which mode they are in, since
            // the visual difference (no hotbar, free flight) is easy to mistake for a bug otherwise.
            if (m.Spectating != Spectating)
            {
                Spectating = m.Spectating;
                LastMessage = Localizer?.Get(m.Spectating ? "hud.observer.on" : "hud.observer.off") ?? LastMessage;
            }

            CanFly = m.CanFly;
            _returnToShipReadyAt = _worldClock.Now + m.ReturnToShipCooldownSeconds; // #2286: 0 from the server = ready now; world clock, so it freezes with the hold
            _returnToShipFightUntil = Time.realtimeSinceStartup + Mathf.Max(0f, m.ReturnToShipFightSeconds); // #2559: real time — the grace runs on through the hold (#2558)
            ReturnToShipRobberWaiting = m.ReturnToShipFightSeconds < 0f; // #2559: a robber waits for the answer — blocked, no countdown

            // Zero-g construction mode on a player station (#1842): its own hints, worded for the station, replace
            // the planet's "left the atmosphere" line for the float that comes with the flip.
            bool zeroGChanged = m.StationZeroG != StationZeroG;
            StationZeroG = m.StationZeroG;
            if (zeroGChanged)
            {
                LastMessage = m.StationZeroG
                    ? string.Format(Localizer?.Get("hud.station.zero_g.on") ?? "Construction mode: zero gravity. Press {0} to walk again.",
                        InputMap.Glyph(InputAction.ToggleStationZeroG))
                    : Localizer?.Get("hud.station.zero_g.off") ?? LastMessage;
            }

            // Built/climbed above the atmosphere → zero-g float on foot + space sky (item 10).
            if (m.AboveAtmosphere != OnFootInSpace)
            {
                OnFootInSpace = m.AboveAtmosphere;
                // #2276: the planet's line only. A station's float has its own server line that names the controls
                // (srv.station.zero_g) — the atmosphere wording was wrong there and could bury it.
                if (!zeroGChanged && !m.StationZeroG && string.IsNullOrEmpty(m.StationName))
                {
                    LastMessage = m.AboveAtmosphere
                        ? AtmosphereLeftToast()
                        : Localizer?.Get("hud.atmosphere.entered") ?? LastMessage;
                }
            }

            // Boarding or leaving a space station is a server-side teleport (to the station interior, or
            // back to the ship). Snap the body to the new authoritative position — otherwise the player
            // stayed where they were and appeared to "land on the planet" instead of docking.
            if (m.StationName != StationName)
            {
                RespawnTarget = new Vector3(m.X, m.Y, m.Z);
            }

            StationName = m.StationName;
            if (string.IsNullOrEmpty(StationName))
            {
                CurrentStationId = string.Empty; // left the station — no in-station rename target
            }
            AiCoreTier = m.AiCoreTier;
            ServerSpawn ??= new Vector3(m.X, m.Y, m.Z);
        }

        /// <summary>#2276: the toast for floating up past a planet's atmosphere line — what happened, then the controls
        /// that bring you back (the old line only said "zero gravity", and a child who climbed a summit into space did not
        /// know what to press). Two keys so the long-translated first half stays valid in every language.</summary>
        private string AtmosphereLeftToast()
        {
            string left = Localizer?.Get("hud.atmosphere.left") ?? "You have left the atmosphere — zero gravity";
            string controls = Localizer?.Get("hud.atmosphere.controls")
                              ?? "Let go to drift slowly down · hold {0} to sink faster · {1} rises";
            return left + "  ·  " + string.Format(controls, LocomotionGlyph(jump: false), LocomotionGlyph(jump: true));
        }

        /// <summary>#2276: the on-screen label of the fixed Jump / Crouch control for the device in hand. They are not
        /// rebindable <see cref="InputAction"/>s, so <see cref="InputMap.Glyph"/> cannot name them: the pad's bottom /
        /// right face button (GamepadInputSource), the touch JUMP / DOWN buttons, else the keyboard keys
        /// DesktopInputSource reads (Space, Ctrl or C).</summary>
        private string LocomotionGlyph(bool jump)
        {
            switch (InputMap.ActiveDevice)
            {
                case InputDeviceKind.Gamepad:
                    return InputMap.PadGlyph(jump ? KeyCode.JoystickButton0 : KeyCode.JoystickButton1) ?? string.Empty;
                case InputDeviceKind.Touch:
                    return Localizer?.Get(jump ? "ui.touch.jump" : "ui.touch.down") ?? string.Empty;
                default:
                    return Localizer?.Get(jump ? "ui.key.jump_keys" : "ui.key.crouch_keys") ?? (jump ? "Space" : "Ctrl/C");
            }
        }
    }
}
