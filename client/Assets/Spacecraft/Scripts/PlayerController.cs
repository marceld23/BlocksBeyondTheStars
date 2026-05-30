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
        public float Reach = 6f;

        /// <summary>Item key used when placing blocks (would come from the selected hotbar slot).</summary>
        public string PlaceItem = "iron_wall";

        private CharacterController _controller;
        private float _pitch;
        private float _verticalVelocity;
        private float _moveSendTimer;

        private void Awake() => _controller = GetComponent<CharacterController>();

        private void Update()
        {
            LookAround();
            Move();
            HandleInteract();
            SendMovement();
        }

        private void LookAround()
        {
            float mx = Input.GetAxis("Mouse X") * MouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * MouseSensitivity;
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
                var t = FloorVec(hit.point + hit.normal * 0.5f);
                Game.Network.SendPlace(t.x, t.y, t.z, PlaceItem);
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
