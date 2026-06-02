using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// Space radar (M27 polish): a HUD minimap of nearby space entities while flying — colour-coded
    /// (white = neutral asteroids/NPCs, red = hostile drones/UFOs), placed by bearing relative to the
    /// flight camera (forward = up). Shown only in space; reads the authoritative <c>SpaceState</c>.
    /// </summary>
    public sealed class SpaceRadar : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Camera;

        private const float Radius = 72f;
        private const float Range = 130f; // world units mapped to the radar edge

        private void OnGUI()
        {
            if (Game == null || Camera == null || !Game.InSpace || Game.Space == null || Game.MenuOpen)
            {
                return; // hidden while an in-game menu is open
            }

            UiScale.Begin(); // scale the radar with resolution
            float cx = UiScale.Width / 2f, cy = Radius + 24f;
            var prev = GUI.color;

            // Round radar face (translucent fill + cyan ring).
            var panel = new Rect(cx - Radius - 8f, cy - Radius - 8f, (Radius + 8f) * 2f, (Radius + 8f) * 2f);
            GUI.DrawTexture(panel, UiKit.RadarCircle);

            // Ship at the centre + a forward tick.
            GUI.color = UiKit.TextCol;
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - Radius, 2f, 7f), Texture2D.whiteTexture);

            // The flight camera is parented to the (unrotated) space scene root, so its local
            // position + world right/forward give a stable frame for the entity bearings.
            var camPos = Camera.transform.localPosition;
            var camR = Camera.transform.right;
            var camF = Camera.transform.forward;
            float scale = Radius / Range;

            foreach (var e in Game.Space.Entities)
            {
                var dir = new Vector3(e.X, e.Y, e.Z) - camPos;
                var v = new Vector2(Vector3.Dot(dir, camR), Vector3.Dot(dir, camF)) * scale;
                if (v.magnitude > Radius)
                {
                    v = v.normalized * Radius;
                }

                GUI.color = e.Hostile ? new Color(1f, 0.35f, 0.35f) : new Color(0.9f, 0.95f, 1f);
                GUI.DrawTexture(new Rect(cx + v.x - 3f, cy - v.y - 3f, 6f, 6f), Texture2D.whiteTexture);
            }

            GUI.color = prev;
            UiScale.End();
        }
    }
}
