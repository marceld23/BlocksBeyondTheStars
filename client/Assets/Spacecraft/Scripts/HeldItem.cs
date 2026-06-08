using UnityEngine;
using Spacecraft.Shared.Content;
using Spacecraft.Shared.Definitions;

namespace Spacecraft.Client
{
    /// <summary>
    /// Builds a small blocky "held item" mesh (the tool/weapon/block currently selected) from cubes in
    /// code, shared by the third-person avatar hand (<see cref="PlayerAvatar.SetHeldItem"/>) and the
    /// first-person <see cref="Viewmodel"/>. The mesh points along +Z (forward) from its holder origin.
    /// </summary>
    public static class HeldItem
    {
        public enum Kind { None, Block, Drill, Gun, Blade, Scanner, Tool, Gadget }

        /// <summary>Maps the selected inventory item to a held-item kind + tint (None = empty hands).</summary>
        public static (Kind kind, Color tint) For(GameContent content, string itemKey)
        {
            if (string.IsNullOrEmpty(itemKey) || content == null)
            {
                return (Kind.None, Color.white);
            }

            var def = content.GetItem(itemKey);
            if (def == null)
            {
                return (Kind.None, Color.white);
            }

            if (!string.IsNullOrEmpty(def.PlacesBlock))
            {
                return (Kind.Block, WorldMap.MapColor(def.PlacesBlock));
            }

            var tool = def.Tool;
            if (tool == null)
            {
                return (Kind.None, Color.white); // raw material — nothing meaningful to hold up
            }

            switch (tool.Kind)
            {
                case ToolKind.Drill: return (Kind.Drill, new Color(0.62f, 0.66f, 0.72f));
                case ToolKind.Scanner: return (Kind.Scanner, new Color(0.45f, 0.85f, 0.95f));
                case ToolKind.Weapon: return IsRanged(itemKey) ? (Kind.Gun, GunTint(itemKey)) : (Kind.Blade, new Color(0.80f, 0.84f, 0.90f));
                case ToolKind.Gadget: return (Kind.Gadget, GadgetTint(itemKey));
                default: return (Kind.Tool, new Color(0.60f, 0.62f, 0.66f));
            }
        }

        /// <summary>The emitter glow colour for a gadget's held model (item 36).</summary>
        private static Color GadgetTint(string key)
        {
            if (key.Contains("medkit")) return new Color(0.35f, 1f, 0.55f);   // green first-aid
            if (key.Contains("stasis")) return new Color(0.4f, 0.8f, 1f);     // cyan stasis
            if (key.Contains("blaster")) return new Color(1f, 0.55f, 0.25f);  // orange blast
            return new Color(0.6f, 0.85f, 0.9f);
        }

        private static bool IsRanged(string key)
            => key.Contains("pistol") || key.Contains("blaster") || key.Contains("gauss")
               || key.Contains("laser") || key.Contains("plasma") || key.Contains("cannon") || key.Contains("gun");

        private static Color GunTint(string key)
        {
            if (key.Contains("plasma")) return new Color(0.85f, 0.5f, 1f);
            if (key.Contains("laser")) return new Color(1f, 0.5f, 0.45f);
            if (key.Contains("gauss")) return new Color(0.55f, 0.9f, 1f);
            return new Color(0.5f, 0.54f, 0.6f);
        }

        /// <summary>Builds the held-item geometry under a new holder parented to <paramref name="parent"/>.</summary>
        public static GameObject Build(Transform parent, Kind kind, Color tint)
        {
            if (kind == Kind.None)
            {
                return null;
            }

            var holder = new GameObject("Held");
            holder.transform.SetParent(parent, false);

            var dark = new Color(0.20f, 0.22f, 0.26f);
            var metal = new Color(0.55f, 0.58f, 0.64f);

            switch (kind)
            {
                case Kind.Block:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.16f), new Vector3(0.22f, 0.22f, 0.22f), tint);
                    break;

                case Kind.Drill:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.16f, 0.26f), metal);
                    Cube(holder.transform, new Vector3(0f, 0f, 0.24f), new Vector3(0.09f, 0.09f, 0.18f), tint);       // bit
                    Cube(holder.transform, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.07f, 0.16f, 0.08f), dark);  // grip
                    break;

                case Kind.Gun:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.10f), new Vector3(0.09f, 0.10f, 0.34f), dark);       // barrel
                    Cube(holder.transform, new Vector3(0f, 0f, 0.30f), new Vector3(0.05f, 0.05f, 0.08f), tint);      // muzzle glow
                    Cube(holder.transform, new Vector3(0f, -0.13f, -0.06f), new Vector3(0.08f, 0.18f, 0.10f), dark); // grip
                    break;

                case Kind.Blade:
                    Cube(holder.transform, new Vector3(0f, -0.04f, 0.0f), new Vector3(0.06f, 0.06f, 0.16f), dark);    // handle
                    Cube(holder.transform, new Vector3(0f, 0.02f, 0.26f), new Vector3(0.03f, 0.18f, 0.34f), tint);   // blade
                    break;

                case Kind.Scanner:
                    Cube(holder.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.16f, 0.12f, 0.18f), metal);     // body
                    Cube(holder.transform, new Vector3(0f, 0.10f, 0.12f), new Vector3(0.03f, 0.10f, 0.03f), dark);   // antenna
                    Cube(holder.transform, new Vector3(0f, 0.16f, 0.12f), new Vector3(0.06f, 0.06f, 0.06f), tint);   // glowing tip
                    break;

                case Kind.Gadget:
                    // A compact handheld emitter: a boxy body, a short barrel, and a glowing emitter tip in the
                    // gadget's tint (green medkit / cyan stasis / orange blaster) — item 36.
                    Cube(holder.transform, new Vector3(0f, 0f, 0.04f), new Vector3(0.16f, 0.13f, 0.20f), metal);     // body
                    Cube(holder.transform, new Vector3(0f, 0f, 0.18f), new Vector3(0.08f, 0.08f, 0.12f), dark);     // barrel
                    Cube(holder.transform, new Vector3(0f, 0f, 0.27f), new Vector3(0.10f, 0.10f, 0.05f), tint);     // emitter glow
                    Cube(holder.transform, new Vector3(0f, -0.12f, -0.04f), new Vector3(0.07f, 0.16f, 0.09f), dark); // grip
                    break;

                default: // Tool
                    Cube(holder.transform, new Vector3(0f, 0f, 0.06f), new Vector3(0.12f, 0.12f, 0.26f), tint);
                    break;
            }

            return holder;
        }

        private static void Cube(Transform parent, Vector3 localPos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Part";
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Object.Destroy(col);
            }

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;

            var shader = Shader.Find("Spacecraft/LitColor") ?? Shader.Find("Unlit/Color");
            go.GetComponent<Renderer>().sharedMaterial = new Material(shader) { color = color };
        }
    }
}
