using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Resolution-independent scaling for the IMGUI parts (HUD, Tab menu, credits). IMGUI is laid out
    /// in raw pixels, so on a high-DPI / 4K display everything turns tiny. Lay out instead in a virtual
    /// 1080p-tall space (<see cref="Width"/> x <see cref="Height"/>) between <see cref="Begin"/> and
    /// <see cref="End"/>; the GUI matrix scales it to fill the real screen, so the UI keeps a consistent
    /// physical size at any resolution. The 3D renders at native resolution (no resolution cap).
    /// </summary>
    public static class UiScale
    {
        public const float RefHeight = 1080f;

        /// <summary>Scale factor from the virtual 1080p space to the real screen (clamped for extremes).</summary>
        public static float Factor => Mathf.Clamp(Screen.height / RefHeight, 0.75f, 4f);

        /// <summary>Virtual screen width to lay out against (use instead of Screen.width inside Begin/End).</summary>
        public static float Width => Screen.width / Factor;

        /// <summary>Virtual screen height to lay out against (use instead of Screen.height inside Begin/End).</summary>
        public static float Height => Screen.height / Factor;

        private static Matrix4x4 _saved;

        public static void Begin()
        {
            _saved = GUI.matrix;
            float f = Factor;
            GUI.matrix = Matrix4x4.Scale(new Vector3(f, f, 1f));
        }

        public static void End() => GUI.matrix = _saved;
    }
}
