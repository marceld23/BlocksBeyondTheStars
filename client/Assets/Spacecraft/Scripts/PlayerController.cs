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
            // Snap to the server's authoritative spawn once it is known, then take over.
            if (!_spawned && Game != null && Game.ServerSpawn.HasValue)
            {
                _controller.enabled = false;
                transform.position = Game.ServerSpawn.Value;
                _controller.enabled = true;
                _verticalVelocity = 0f;
                _spawned = true;
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
                Game.Network.SendLootContainer(nearest);
            }
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
                default: Game.Network?.SendUseStation(Game.NearbyStation); break; // medbay, quarters
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

            if (_controller.isGrounded)
            {
                _verticalVelocity = Input.GetButton("Jump") ? JumpSpeed : -1f;
            }
            else
            {
                _verticalVelocity -= Gravity * Time.deltaTime;
            }

            move.y = _verticalVelocity;
            _controller.Move(move * Time.deltaTime);
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
