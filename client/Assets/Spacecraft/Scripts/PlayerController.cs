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

        public float MoveSpeed = 6f;
        public float JumpSpeed = 7f;
        public float Gravity = 20f;
        public float MouseSensitivity = 2f;
        public bool InvertY = false;
        public float Reach = 6f;

        private const int HotbarSlots = 9;

        private CharacterController _controller;
        private float _pitch;
        private float _verticalVelocity;
        private float _moveSendTimer;
        private bool _spawned;

        private void Awake() => _controller = GetComponent<CharacterController>();

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

            // A UI panel is open: don't steer/interact, just keep the player settled by gravity.
            if (Game != null && Game.MenuOpen)
            {
                ApplyGravityOnly();
                return;
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

        private void HandleStations()
        {
            Game.NearbyStation = Game.NearestStationType(transform.position, 3f);
            if (!string.IsNullOrEmpty(Game.NearbyStation) && Input.GetKeyDown(KeyCode.E))
            {
                Game.Network?.SendUseStation(Game.NearbyStation);
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
