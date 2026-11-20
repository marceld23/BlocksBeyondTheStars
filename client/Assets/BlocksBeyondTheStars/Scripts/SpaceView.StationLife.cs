// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Shared.Geometry;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: the signs of life on a lived-in station's hull in the flight view — position lights that blink
    /// (red to port, green to starboard, white fore and aft) and a small dish that turns over its middle
    /// (<see cref="StationLifePlan"/>). A holder under the hull's Extras, built once per design like a derelict's decor: a
    /// handful of small unlit cubes, one transform turned and four toggled per frame. No light, no collider, no protocol.
    /// A derelict gets none (it has its own dying beacon), and neither does a hull too small to tell its sides apart.
    /// </summary>
    public sealed partial class SpaceView
    {
        private static readonly Color[] NavLightColors =
        {
            new Color(1f, 0.2f, 0.18f),   // port
            new Color(0.25f, 1f, 0.4f),   // starboard
            new Color(1f, 0.97f, 0.9f),   // fore
            new Color(1f, 0.97f, 0.9f),   // aft
        };

        /// <summary>A station's name for the dock prompt: with its purpose once its hull design has told it
        /// ("Meridian · Trade station"); the bare name for a station without one, or one not seen yet.</summary>
        private string StationLabel(string name, string id)
        {
            if (string.IsNullOrEmpty(id) || !_structs.TryGetValue(id, out var vs) || string.IsNullOrEmpty(vs.Purpose))
            {
                return name;
            }

            string key = "ui.place.station." + vs.Purpose;
            string text = Game != null && Game.Localizer != null ? Game.Localizer.Get(key) : key;
            return string.IsNullOrEmpty(text) || text == key ? name : name + " · " + text;
        }

        private void AddStationLife(VoxStruct vs)
        {
            if (vs.Root == null || vs.Cells == null || vs.Cells.Count == 0)
            {
                return;
            }

            var plan = StationLifePlan.Build(vs.Cells);
            if (plan.NavLights.Count == 0 && plan.DishCell == null)
            {
                return;
            }

            var holder = new GameObject("StationLife");
            holder.transform.SetParent(vs.Extras != null ? vs.Extras : vs.Root.transform, false);
            Vector3 Local(Vector3i c) => new Vector3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f) - vs.Centre;

            var lights = new GameObject[plan.NavLights.Count];
            for (int i = 0; i < lights.Length; i++)
            {
                var (cell, side) = plan.NavLights[i];
                lights[i] = ModelKit.Cube("NavLight", holder.transform, Local(cell) + new Vector3(0f, 0.72f, 0f), Vector3.one * 0.42f,
                    FxKit.Unlit(NavLightColors[side]));
                lights[i].SetActive(false);
            }

            Transform dish = null;
            if (plan.DishCell is { } top)
            {
                var iron = FxKit.Unlit(new Color(0.62f, 0.65f, 0.7f));
                var mast = ModelKit.Cube("DishMast", holder.transform, Local(top) + new Vector3(0f, 1.0f, 0f), new Vector3(0.16f, 1.0f, 0.16f), iron);
                dish = new GameObject("Dish").transform;
                dish.SetParent(mast.transform.parent, false);
                dish.localPosition = Local(top) + new Vector3(0f, 1.6f, 0f);
                var plate = ModelKit.Cube("DishPlate", dish, new Vector3(0f, 0.12f, 0.2f), new Vector3(1.3f, 0.1f, 1.3f), iron);
                plate.transform.localRotation = Quaternion.Euler(-55f, 0f, 0f); // looking up and out
            }

            holder.AddComponent<StationLife>().Configure(lights, dish);
        }

        /// <summary>Blinks a station's position lights (one short flash each, one after the other, every
        /// <see cref="Cycle"/> seconds) and turns its dish.</summary>
        private sealed class StationLife : MonoBehaviour
        {
            private const float Cycle = 2.4f;
            private const float Flash = 0.16f;
            private const float DishDegreesPerSecond = 14f;

            private GameObject[] _lights = System.Array.Empty<GameObject>();
            private Transform _dish;

            public void Configure(GameObject[] lights, Transform dish)
            {
                _lights = lights;
                _dish = dish;
            }

            private void Update()
            {
                float t = Mathf.Repeat(Time.time, Cycle);
                for (int i = 0; i < _lights.Length; i++)
                {
                    var light = _lights[i];
                    if (light == null)
                    {
                        continue;
                    }

                    float start = i * (Cycle / Mathf.Max(1, _lights.Length)) * 0.5f; // the four within the first half
                    bool on = t >= start && t < start + Flash;
                    if (light.activeSelf != on)
                    {
                        light.SetActive(on);
                    }
                }

                if (_dish != null)
                {
                    _dish.Rotate(0f, DishDegreesPerSecond * Time.deltaTime, 0f, Space.Self);
                }
            }
        }
    }
}
