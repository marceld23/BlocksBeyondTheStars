using System.Collections.Generic;
using UnityEngine;

namespace Spacecraft.Client
{
    /// <summary>
    /// A blocky humanoid avatar (head, torso, two arms, two legs) built from cubes in code
    /// (M23b) — no art asset needed. Per-part colours come from <see cref="ClientSettings"/>
    /// and are shown in third-person. Designed so each part is individually re-coloured, which
    /// is where equipped armor will later override the matching part.
    /// </summary>
    public sealed class PlayerAvatar : MonoBehaviour
    {
        private readonly List<Renderer> _renderers = new List<Renderer>();
        private Material _skin, _torso, _arms, _legs;

        public void Build(ClientSettings s)
        {
            _skin = Unlit(s.SkinColor);
            _torso = Unlit(s.TorsoColor);
            _arms = Unlit(s.ArmColor);
            _legs = Unlit(s.LegColor);

            // Local positions are relative to the player's feet (CharacterController origin).
            AddPart("Head", new Vector3(0f, 1.65f, 0f), new Vector3(0.5f, 0.5f, 0.5f), _skin);
            AddPart("Torso", new Vector3(0f, 1.15f, 0f), new Vector3(0.55f, 0.7f, 0.3f), _torso);
            AddPart("ArmLeft", new Vector3(-0.4f, 1.15f, 0f), new Vector3(0.18f, 0.7f, 0.18f), _arms);
            AddPart("ArmRight", new Vector3(0.4f, 1.15f, 0f), new Vector3(0.18f, 0.7f, 0.18f), _arms);
            AddPart("LegLeft", new Vector3(-0.15f, 0.45f, 0f), new Vector3(0.22f, 0.85f, 0.22f), _legs);
            AddPart("LegRight", new Vector3(0.15f, 0.45f, 0f), new Vector3(0.22f, 0.85f, 0.22f), _legs);
        }

        private void AddPart(string partName, Vector3 localPos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col); // visual only — must not interfere with the CharacterController
            }

            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            _renderers.Add(go.GetComponent<Renderer>());
        }

        /// <summary>Re-applies the per-part colours (e.g. after the player changed them in settings).</summary>
        public void ApplyColors(ClientSettings s)
        {
            if (_skin == null)
            {
                return;
            }

            _skin.color = s.SkinColor;
            _torso.color = s.TorsoColor;
            _arms.color = s.ArmColor;
            _legs.color = s.LegColor;
        }

        public void SetVisible(bool visible)
        {
            foreach (var r in _renderers)
            {
                r.enabled = visible;
            }
        }

        private static Material Unlit(Color color)
        {
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Spacecraft/VertexColorOpaque");
            return new Material(shader) { color = color };
        }
    }
}
