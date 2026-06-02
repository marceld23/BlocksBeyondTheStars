using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// First-person controller: WASD + mouse look, jump, and left/right-click to mine/place.
    /// Mining and placing only *send intents* — the server validates and the world updates
    /// when the resulting <c>BlockChanged</c> message arrives.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;
        public PlayerAvatar Avatar;
        public GameMenu Menu;

        public float MoveSpeed = 6f;
        public float JumpSpeed = 7f;
        public float Gravity = 20f;
        public float MouseSensitivity = 2f;
        public bool InvertY = false;
        public bool ThirdPerson = false;
        public float Reach = 6f;

        private const int HotbarSlots = 9;

        private static readonly Vector3 FirstPersonEye = new Vector3(0f, 1.6f, 0f);
        private static readonly Vector3 ThirdPersonEye = new Vector3(0f, 1.9f, -3.5f);

        private CharacterController _controller;
        private float _pitch;
        private float _verticalVelocity;
        private float _moveSendTimer;
        private bool _spawned;
        private Vector3 _spawnPos;
        private bool _settling;
        private bool _wasGrounded = true;
        private float _stepTimer;
        private int _lastWorldEpoch;

        private void Awake() => _controller = GetComponent<CharacterController>();

        private void Start() => ApplyCameraMode();

        private void ApplyCameraMode()
        {
            if (Camera != null)
            {
                Camera.transform.localPosition = ThirdPerson ? ThirdPersonEye : FirstPersonEye;
            }

            // Show the avatar only in third-person (otherwise the camera is inside the head).
            Avatar?.SetVisible(ThirdPerson);
        }

        private void Update()
        {
            // On travel the world is rebuilt at a new location: re-run the spawn snap there.
            if (Game != null && Game.WorldEpoch != _lastWorldEpoch)
            {
                _lastWorldEpoch = Game.WorldEpoch;
                _spawned = false;
                _settling = false;
            }

            // Snap to the server's authoritative spawn once it is known, then take over.
            if (!_spawned && Game != null && Game.ServerSpawn.HasValue)
            {
                _spawnPos = Game.ServerSpawn.Value;
                SnapTo(_spawnPos);
                _spawned = true;
                _settling = true; // hold near spawn until the ground/ship chunk streams in
            }

            // While terrain is still streaming the spawn chunk may have no collider yet, so the
            // player would fall straight through. Keep pulling them back to spawn until they land on
            // real ground — prevents spawning underground in a cave when chunks load slowly (builds).
            if (_settling && Game != null)
            {
                if (_controller.isGrounded)
                {
                    _settling = false;
                }
                else if (transform.position.y < _spawnPos.y - 3f)
                {
                    SnapTo(_spawnPos);
                }
            }

            // The space view owns the camera and freezes on-foot control entirely.
            if (Game != null && Game.SpaceViewActive)
            {
                return;
            }

            // A UI panel is open: don't steer/interact, just keep the player settled by gravity.
            if (Game != null && Game.MenuOpen)
            {
                ApplyGravityOnly();
                return;
            }

            if (Input.GetKeyDown(KeyCode.V))
            {
                ThirdPerson = !ThirdPerson;
                ApplyCameraMode();
            }

            if (Input.GetKeyDown(KeyCode.F))
            {
                AttackNearestEnemy();
                Avatar?.Swing();
            }

            if (Input.GetKeyDown(KeyCode.G))
            {
                LootNearestContainer();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                RepairWreckCell();
            }

            HandleHotbar();
            LookAround();
            Move();
            HandleInteract();
            HandleDrillAudio();
            UpdateGearPeriodically();
            SendMovement();

            // Publish local pose for the HUD minimap/compass.
            if (Game != null)
            {
                Game.PlayerPosition = transform.position;
                Game.PlayerYaw = transform.eulerAngles.y;
                HandleStations();
            }
        }

        private void AttackNearestEnemy()
        {
            if (Game?.Network == null)
            {
                return;
            }

            PlayWeaponSound();
            string nearest = null;
            float bestSq = 6f * 6f; // attack reach
            foreach (var e in Game.PlanetEnemies)
            {
                float d = (new Vector3(e.X, e.Y, e.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = e.Id;
                }
            }

            // Creatures (fauna) are attackable too — the server shares the hit path.
            foreach (var c in Game.Creatures)
            {
                float d = (new Vector3(c.X, c.Y, c.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c.Id;
                }
            }

            if (nearest != null)
            {
                Game.Network.SendAttackEntity(nearest);
            }
        }

        private void LootNearestContainer()
        {
            if (Game?.Network == null)
            {
                return;
            }

            string nearest = null;
            float bestSq = 6f * 6f; // loot reach
            foreach (var c in Game.Containers)
            {
                float d = (new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    nearest = c.Id;
                }
            }

            if (nearest != null)
            {
                ClientAudio.Instance?.Cue("loot");
                Game.Network.SendLootContainer(nearest);
            }
        }

        private bool _gearHelmet, _gearChest, _gearLegs, _gearPack;
        private float _gearTimer;

        /// <summary>Mirrors the player's carried gear onto the third-person avatar (helmet/chest/legs/
        /// pack), refreshed a couple of times a second so it tracks pickups/crafts without polling hard.</summary>
        private void UpdateGearPeriodically()
        {
            _gearTimer -= Time.deltaTime;
            if (_gearTimer > 0f || Avatar == null || Game?.Personal == null)
            {
                return;
            }

            _gearTimer = 0.5f;
            bool helmet = HasItem("armor_helmet");
            bool chest = HasItem("armor_chest") || HasItem("stealth_suit");
            bool legs = HasItem("armor_legs");
            bool pack = HasItem("oxygen_tank_2") || HasItem("jetpack");

            if (helmet != _gearHelmet || chest != _gearChest || legs != _gearLegs || pack != _gearPack)
            {
                _gearHelmet = helmet;
                _gearChest = chest;
                _gearLegs = legs;
                _gearPack = pack;
                Avatar.SetGear(helmet, chest, legs, pack);
            }
        }

        private bool HasItem(string key)
        {
            foreach (var s in Game.Personal)
            {
                if (s.Item == key && s.Count > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Keeps the drill loop alive while the player holds mine with a drill aimed at a block.</summary>
        private void HandleDrillAudio()
        {
            if (Camera == null || !Input.GetMouseButton(0) || !HoldingDrill())
            {
                return;
            }

            if (Physics.Raycast(new Ray(Camera.transform.position, Camera.transform.forward), Reach))
            {
                ClientAudio.Instance?.DrillTick();
                Avatar?.Swing(); // keep the mining chop going while the drill is held
            }
        }

        /// <summary>True if the selected hotbar item is a drill (its primary action mines).</summary>
        private bool HoldingDrill()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Drill;
        }

        /// <summary>True if the selected hotbar item is a handheld scanner (its primary action scans).</summary>
        private bool HoldingScanner()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == Spacecraft.Shared.Definitions.ToolKind.Scanner;
        }

        /// <summary>Scans the nearest creature (threat assessment) or, failing that, the block in view.</summary>
        private void ScanTarget()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            string speciesId = null;
            float bestSq = Reach * Reach;
            foreach (var c in Game.Creatures)
            {
                float d = (new Vector3(c.X, c.Y, c.Z) - transform.position).sqrMagnitude;
                if (d < bestSq)
                {
                    bestSq = d;
                    speciesId = c.SpeciesId;
                }
            }

            if (speciesId != null)
            {
                Game.Network.SendScan("creature", speciesId);
                return;
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach))
            {
                return;
            }

            var b = FloorVec(hit.point - hit.normal * 0.5f);
            var def = Game.Content?.BlockById(Game.World.GetBlock(b.x, b.y, b.z));
            if (def != null)
            {
                Game.Network.SendScan("block", def.Key);
            }
        }

        /// <summary>Fills the targeted breach cell of a crashed wreck with the selected hotbar block (server validates).</summary>
        private void RepairWreckCell()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
            if (string.IsNullOrEmpty(item))
            {
                return; // need a block in hand to rebuild the hull
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach))
            {
                return;
            }

            // Fill the empty cell against the hit face — the server checks it against the wreck's intact mask.
            var t = FloorVec(hit.point + hit.normal * 0.5f);
            Game.Network.SendRepairWreck(t.x, t.y, t.z, item);
        }

        private void HandleStations()
        {
            Game.NearbyStation = Game.NearestStationType(transform.position, 3f);
            if (string.IsNullOrEmpty(Game.NearbyStation) || !Input.GetKeyDown(KeyCode.E))
            {
                return;
            }

            // Stations that open a client UI panel; the rest are resolved server-side.
            switch (Game.NearbyStation)
            {
                case "cockpit": Menu?.OpenMap(); break;
                case "workshop": Menu?.OpenCrafting(); break;
                case "cargo": Menu?.OpenInventory(); break;
                default:
                    if (Game.NearbyStation == "medbay") ClientAudio.Instance?.Cue("heal");
                    Game.Network?.SendUseStation(Game.NearbyStation);
                    break; // medbay, quarters
            }
        }

        private void HandleHotbar()
        {
            if (Game == null)
            {
                return;
            }

            for (int i = 0; i < HotbarSlots; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    SelectSlot(i);
                }
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + HotbarSlots - 1) % HotbarSlots);
            }
            else if (scroll < 0f)
            {
                SelectSlot((Game.SelectedHotbarSlot + 1) % HotbarSlots);
            }
        }

        private void SelectSlot(int slot)
        {
            if (slot == Game.SelectedHotbarSlot)
            {
                return;
            }

            Game.SelectedHotbarSlot = slot;
            Game.Network?.SendSelectHotbar(slot);
        }

        /// <summary>Teleports the player (CharacterController toggled so the move isn't blocked) and zeroes fall speed.</summary>
        private void SnapTo(Vector3 pos)
        {
            _controller.enabled = false;
            transform.position = pos;
            _controller.enabled = true;
            _verticalVelocity = 0f;
        }

        private void ApplyGravityOnly()
        {
            if (_controller.isGrounded)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
            }

            _controller.Move(new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);
        }

        private void LookAround()
        {
            float mx = Input.GetAxis("Mouse X") * MouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * MouseSensitivity * (InvertY ? -1f : 1f);
            transform.Rotate(0f, mx, 0f);
            _pitch = Mathf.Clamp(_pitch - my, -89f, 89f);
            if (Camera != null)
            {
                Camera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
            }
        }

        private void Move()
        {
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            Vector3 move = (transform.right * h + transform.forward * v) * MoveSpeed;

            bool grounded = _controller.isGrounded;
            if (grounded)
            {
                if (Input.GetButtonDown("Jump"))
                {
                    ClientAudio.Instance?.Cue("jump", 0.6f);
                }

                _verticalVelocity = Input.GetButton("Jump") ? JumpSpeed : -1f;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);

            // Footsteps while walking on the ground; landing thud after a fall.
            if (grounded && Mathf.Abs(h) + Mathf.Abs(v) > 0.1f)
            {
                _stepTimer -= Time.deltaTime;
                if (_stepTimer <= 0f)
                {
                    _stepTimer = 0.45f;
                    ClientAudio.Instance?.Cue(SurfaceStep(), 0.45f);
                }
            }
            else
            {
                _stepTimer = 0f;
            }

            if (grounded && !_wasGrounded)
            {
                ClientAudio.Instance?.Cue("land", 0.6f);
            }

            _wasGrounded = grounded;
        }

        /// <summary>Picks the footstep clip from the block under the player's feet (key heuristic).</summary>
        private string SurfaceStep()
        {
            if (Game?.World == null || Game.Content == null)
            {
                return "step_rock";
            }

            var p = transform.position;
            var def = Game.Content.BlockById(Game.World.GetBlock(
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y - 0.5f), Mathf.FloorToInt(p.z)));
            string k = def?.Key ?? string.Empty;
            if (k.Contains("iron") || k.Contains("metal") || k.Contains("steel")) return "step_metal";
            if (k.Contains("sand")) return "step_sand";
            if (k.Contains("grass")) return "step_grass";
            if (k.Contains("snow") || k.Contains("ice")) return "step_snow";
            return "step_rock";
        }

        /// <summary>Plays the firing/swing sound for the currently held weapon (if any).</summary>
        private void PlayWeaponSound()
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            switch (Game.ItemInSlot(Game.SelectedHotbarSlot))
            {
                case "gauss_pistol": audio.Cue("weapon_gauss"); break;
                case "laser_pistol": audio.Cue("weapon_laser"); break;
                case "plasma_blaster": audio.Cue("weapon_plasma"); break;
                default: audio.Cue("melee_swing"); break; // melee weapons, tools, fists
            }
        }

        private void HandleInteract()
        {
            if (Game?.Network == null || Camera == null)
            {
                return;
            }

            bool mine = Input.GetMouseButtonDown(0);
            bool place = Input.GetMouseButtonDown(1);
            if (!mine && !place)
            {
                return;
            }

            // Holding a scanner turns the primary action into a scan (select it in the hotbar, then aim + click).
            if (mine && HoldingScanner())
            {
                ScanTarget();
                return;
            }

            var ray = new Ray(Camera.transform.position, Camera.transform.forward);
            if (!Physics.Raycast(ray, out var hit, Reach))
            {
                return;
            }

            // Nudge slightly into / out of the surface to pick the target block cell.
            Vector3 inside = hit.point - hit.normal * 0.5f;
            var b = FloorVec(inside);

            if (mine)
            {
                Game.Network.SendMine(b.x, b.y, b.z);
                Avatar?.Swing();
            }
            else
            {
                // Place the item in the selected hotbar slot — only if it actually places a
                // block (tools like the drill/scanner don't), so we don't spam server rejects.
                string item = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var def = string.IsNullOrEmpty(item) ? null : Game.Content?.GetItem(item);
                if (def != null && !string.IsNullOrEmpty(def.PlacesBlock))
                {
                    var t = FloorVec(hit.point + hit.normal * 0.5f);
                    Game.Network.SendPlace(t.x, t.y, t.z, item);
                    Avatar?.Swing();
                }
            }
        }

        private void SendMovement()
        {
            _moveSendTimer += Time.deltaTime;
            if (_moveSendTimer < 0.1f || Game?.Network == null)
            {
                return; // ~10 position updates per second (unreliable channel)
            }

            _moveSendTimer = 0f;
            Game.Network.SendMove(transform.position, transform.eulerAngles.y, _pitch);
        }

        private static Vector3Int FloorVec(Vector3 v)
            => new Vector3Int(Mathf.FloorToInt(v.x), Mathf.FloorToInt(v.y), Mathf.FloorToInt(v.z));
    }
}
