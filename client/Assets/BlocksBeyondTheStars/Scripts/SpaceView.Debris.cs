// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Client.Core;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Debris fields (#2353) and raiders with real hulls (#2357) in the flight view. The field's fragments arrive as
    /// "debris" voxel designs and render through the same static-structure path as asteroids and the wreck; this file
    /// holds what is new: the field marker (the flight recorder's beacon), the sealed salvage capsule, the raider's
    /// voxel hull built from the "ship_remote" design the server sends for <c>ship:bandit:&lt;id&gt;</c>, and the two
    /// questions the shield-tap clank asks (am I inside a field, is anyone shooting at me).
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Raider entity ids whose model is the real voxel hull (not the placeholder wedge).</summary>
        private readonly HashSet<string> _voxelRaiders = new HashSet<string>();

        private static readonly Color RaiderPaint = new Color(0.20f, 0.19f, 0.22f);  // the dye on the hull cells wins; this paints the rest
        private static readonly Color RaiderPlume = new Color(1f, 0.35f, 0.25f);
        private static readonly Color DebrisCopper = new Color(0.85f, 0.55f, 0.3f);
        private static readonly Color CapsuleTeal = new Color(0.4f, 0.95f, 0.85f);

        // ---------------- The raider's hull (#2357) ----------------

        /// <summary>The raider's "ship_remote" design once it arrived (the server keys it <c>ship:bandit:&lt;id&gt;</c>).</summary>
        private SpaceShipDesign RaiderDesignFor(string entityId)
        {
            var d = Game != null ? Game.RemoteShipDesignFor("bandit:" + entityId) : null;
            return d != null && ShipMeshBuilder.HasDesign(d) ? d : null;
        }

        /// <summary>The bracket radius of a raider: half its hull's longest side at flight scale, else the wedge's.</summary>
        private float RaiderDesignRadius(NetCombatEntity e)
        {
            var d = RaiderDesignFor(e.Id);
            if (d == null)
            {
                return 2.5f * Mathf.Max(1f, e.Scale);
            }

            float longest = Mathf.Max(d.Width, Mathf.Max(d.Height, d.Length));
            return Mathf.Max(2.5f, longest * FlightShipScale * 0.55f);
        }

        /// <summary>The raider: its REAL voxel hull in raider livery (dyed near-black with a rust band by the server) with a
        /// red plume on every engine, or — until the design is here — the hand-built wedge. The hull is meshed the way a
        /// remote pilot's ship is, at the same compact flight scale.</summary>
        private GameObject BuildBanditShipModel(Transform parent, NetCombatEntity e)
        {
            var design = RaiderDesignFor(e.Id);
            if (design != null)
            {
                var root = new GameObject("BanditShip");
                root.transform.SetParent(parent, false);
                var vox = ShipMeshBuilder.BuildVoxelShip(Game, root.transform, design, out _, RaiderPaint);
                if (vox != null)
                {
                    vox.transform.localScale = Vector3.one * FlightShipScale;
                    foreach (var (exLocal, exSize) in ShipMeshBuilder.ExhaustPoints(Game.Content, design))
                    {
                        SpaceFx.EnginePlume(vox.transform, exLocal, exSize, RaiderPlume);
                    }

                    _voxelRaiders.Add(e.Id);
                    return root;
                }

                Destroy(root);
            }

            _voxelRaiders.Remove(e.Id);
            return BuildBanditShipModel(parent);
        }

        // ---------------- The debris field (#2353) ----------------

        /// <summary>The field marker: a scorched flight recorder — a small dark box with a copper band and a beacon that
        /// pulses slowly — turning gently in the middle of the field (<see cref="DebrisBeaconView"/>).</summary>
        private GameObject BuildDebrisFieldMarkerModel(Transform parent, NetCombatEntity e)
        {
            var root = new GameObject("DebrisField");
            root.transform.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            var dark = Lit(new Color(0.22f, 0.21f, 0.23f));
            var copper = Lit(DebrisCopper);
            Cube("Box", body, Vector3.zero, new Vector3(1.4f, 0.9f, 1.0f), dark);
            Cube("Band", body, new Vector3(0f, 0f, 0f), new Vector3(1.46f, 0.22f, 1.06f), copper);
            Cube("Mast", body, new Vector3(0f, 0.85f, 0f), new Vector3(0.08f, 0.8f, 0.08f), Lit(new Color(0.6f, 0.62f, 0.66f)));

            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon";
            StripCollider(beacon);
            beacon.transform.SetParent(body, false);
            beacon.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            beacon.transform.localScale = Vector3.one * 0.3f;
            beacon.GetComponent<Renderer>().sharedMaterial = Unlit(DebrisCopper);

            root.AddComponent<DebrisBeaconView>().Init(body, beacon.transform, 0.5f, 1.6f, 6f);
            return root;
        }

        /// <summary>The sealed salvage capsule: a short steel canister with two teal bands and a teal light — a sibling of
        /// the life pod, deliberately not orange (nobody needs rescuing in it).</summary>
        private GameObject BuildSalvageCapsuleModel(Transform parent, NetCombatEntity e)
        {
            var root = new GameObject("SalvageCapsule");
            root.transform.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            var can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            can.name = "Canister";
            StripCollider(can);
            can.transform.SetParent(body, false);
            can.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            can.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f);
            can.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.62f, 0.66f, 0.72f));

            var teal = Lit(CapsuleTeal);
            foreach (float z in new[] { -0.45f, 0.45f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                band.name = "Band";
                StripCollider(band);
                band.transform.SetParent(body, false);
                band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                band.transform.localPosition = new Vector3(0f, 0f, z);
                band.transform.localScale = new Vector3(0.96f, 0.06f, 0.96f);
                band.GetComponent<Renderer>().sharedMaterial = teal;
            }

            var light = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            light.name = "Beacon";
            StripCollider(light);
            light.transform.SetParent(body, false);
            light.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            light.transform.localScale = Vector3.one * 0.18f;
            light.GetComponent<Renderer>().sharedMaterial = Unlit(CapsuleTeal);

            root.AddComponent<DebrisBeaconView>().Init(body, light.transform, 0.7f, 1.3f, 14f);
            return root;
        }

        /// <summary>True while the own ship sits inside a debris field (within the content's field radius of a marker).</summary>
        private bool InDebrisFieldNow()
        {
            if (_ship == null || Game?.Space?.Entities == null)
            {
                return false;
            }

            float r = Game.Content != null ? Game.Content.SpaceSalvage.Fields.Radius : 28f;
            var pos = _ship.transform.localPosition;
            foreach (var e in Game.Space.Entities)
            {
                if (e.Kind == "DebrisField" && (new Vector3(e.X, e.Y, e.Z) - pos).sqrMagnitude <= r * r)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True while a hostile is inside its damage aura around the own ship — then a shield drop is its fire,
        /// not the field's rubble.</summary>
        private bool HostileInAttackRange()
        {
            if (_ship == null || Game?.Space?.Entities == null)
            {
                return false;
            }

            var pos = _ship.transform.localPosition;
            float r = SpaceTargeting.AttackRange;
            foreach (var e in Game.Space.Entities)
            {
                if (e.Hostile && (new Vector3(e.X, e.Y, e.Z) - pos).sqrMagnitude <= r * r)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A slow turn for a drifting body and a soft pulse for its beacon — the debris field's flight recorder and
    /// the salvage capsules (#2353). No material changes: the beacon's scale breathes, so nothing is allocated per frame.</summary>
    public sealed class DebrisBeaconView : MonoBehaviour
    {
        private Transform _body;
        private Transform _beacon;
        private float _beaconScale;
        private float _pulseSpeed;
        private float _spinSpeed;
        private float _phase;

        public void Init(Transform body, Transform beacon, float pulseSpeed, float beaconScale, float spinDegPerSecond)
        {
            _body = body;
            _beacon = beacon;
            _beaconScale = beacon != null ? beacon.localScale.x : 0.3f;
            _pulseSpeed = pulseSpeed;
            _spinSpeed = spinDegPerSecond;
            _phase = Random.value * 6.28f;
            _ = beaconScale;
        }

        private void Update()
        {
            float t = Time.time * _pulseSpeed * 6.28f + _phase;
            if (_body != null)
            {
                _body.Rotate(0f, _spinSpeed * Time.deltaTime, _spinSpeed * 0.35f * Time.deltaTime, Space.Self);
            }

            if (_beacon != null)
            {
                float pulse = 0.65f + 0.35f * Mathf.Max(0f, Mathf.Sin(t));
                _beacon.localScale = Vector3.one * (_beaconScale * pulse);
            }
        }
    }
}
