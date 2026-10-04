// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The peaceful things out in space get their own look (#2241, #2242): the life pod is a little lifeboat with a
    /// blinking beacon and SOS rings, the anomaly a shimmering soap bubble, the wormhole a tear in space-time — none of
    /// them the red fallback cube any more. A rescued pod is pulled aboard on a tractor beam; a wormhole within reach
    /// takes the Interact key ("fly through"), and the rift transit plays instead of the hyperspace warp.
    /// </summary>
    public sealed partial class SpaceView
    {
        private string _nearWormholeId;
        private float _wormholeSendCd;

        // ---------------- Models ----------------

        /// <summary>The life pod: a chubby white capsule with rescue-orange bands, a lit porthole with someone waving
        /// in it, an antenna with a blinking orange beacon — tumbling gently (<see cref="EscapePodView"/>).</summary>
        private GameObject BuildEscapePodModel(Transform parent, NetCombatEntity e)
        {
            var root = new GameObject("EscapePod");
            root.transform.SetParent(parent, false);
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            var hull = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            hull.name = "Hull";
            StripCollider(hull);
            hull.transform.SetParent(body, false);
            hull.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hull.transform.localScale = new Vector3(1.2f, 1.4f, 1.2f);
            hull.GetComponent<Renderer>().sharedMaterial = Lit(new Color(0.93f, 0.94f, 0.96f));

            var orange = Lit(new Color(1f, 0.5f, 0.12f));
            foreach (float z in new[] { -0.75f, 0.75f })
            {
                var band = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                band.name = "Band";
                StripCollider(band);
                band.transform.SetParent(body, false);
                band.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                band.transform.localPosition = new Vector3(0f, 0f, z);
                band.transform.localScale = new Vector3(1.26f, 0.09f, 1.26f);
                band.GetComponent<Renderer>().sharedMaterial = orange;
            }

            // The porthole: warm light, and a small figure in it (head + body + a waving arm).
            var port = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            port.name = "Porthole";
            StripCollider(port);
            port.transform.SetParent(body, false);
            port.transform.localPosition = new Vector3(0.56f, 0.1f, 0.15f);
            port.transform.localScale = new Vector3(0.1f, 0.5f, 0.5f);
            port.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(1f, 0.86f, 0.5f));

            var figure = Unlit(new Color(0.18f, 0.2f, 0.26f));
            Cube("Head", body, new Vector3(0.62f, 0.2f, 0.15f), new Vector3(0.03f, 0.13f, 0.13f), figure);
            Cube("Torso", body, new Vector3(0.62f, 0.0f, 0.15f), new Vector3(0.03f, 0.2f, 0.16f), figure);
            var armPivot = new GameObject("ArmPivot").transform;
            armPivot.SetParent(body, false);
            armPivot.localPosition = new Vector3(0.625f, 0.06f, 0.26f);
            Cube("Arm", armPivot, new Vector3(0f, 0.09f, 0f), new Vector3(0.025f, 0.18f, 0.04f), figure);

            Cube("Antenna", body, new Vector3(0f, 0.72f, -0.55f), new Vector3(0.05f, 0.45f, 0.05f), Lit(new Color(0.6f, 0.62f, 0.66f)));
            var beacon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            beacon.name = "Beacon";
            StripCollider(beacon);
            beacon.transform.SetParent(body, false);
            beacon.transform.localPosition = new Vector3(0f, 1.0f, -0.55f);
            beacon.transform.localScale = Vector3.one * 0.22f;
            beacon.GetComponent<Renderer>().sharedMaterial = Unlit(new Color(1f, 0.55f, 0.1f));

            root.transform.localScale = Vector3.one * 1.3f;
            root.AddComponent<EscapePodView>().Init(body, beacon.GetComponent<Renderer>(), armPivot, Game?.Settings, e.Id);
            return root;
        }

        /// <summary>The anomaly: an iridescent soap bubble with cubes blinking inside and blocks that jump around it —
        /// calm already when this pilot scanned it on an earlier visit (<see cref="AnomalyView"/>).</summary>
        private GameObject BuildAnomalyModel(Transform parent, NetCombatEntity e)
        {
            var root = new GameObject("Anomaly");
            root.transform.SetParent(parent, false);
            var ids = Game?.Space?.ScannedIds;
            bool calm = ids != null && System.Array.IndexOf(ids, e.Id) >= 0;
            root.AddComponent<AnomalyView>().Init(e.Id, calm, Game?.Settings);
            return root;
        }

        /// <summary>The wormhole end: a jagged tear in space-time, its inside tinted by the twin's system (violet while
        /// unknown) — see <see cref="WormholeView"/>.</summary>
        private GameObject BuildWormholeModel(Transform parent, NetCombatEntity e)
        {
            var root = new GameObject("Wormhole");
            root.transform.SetParent(parent, false);
            root.AddComponent<WormholeView>().Init(e.Id, LinkedSystemOf(e.Id), Camera, Game?.Settings);
            return root;
        }

        /// <summary>The twin's system id of a wormhole end, empty while the player does not know it.</summary>
        private string LinkedSystemOf(string wormholeId)
        {
            var list = Game?.StarMap?.Wormholes;
            if (list != null)
            {
                foreach (var w in list)
                {
                    if (w.Id == wormholeId)
                    {
                        return w.LinkedSystemId ?? string.Empty;
                    }
                }
            }

            return string.Empty;
        }

        // ---------------- Life pod rescue ----------------

        /// <summary>An entity left the instance. A life pod that vanished next to a ship was rescued — a tractor beam
        /// pulls it in (the server already took the survivor aboard).</summary>
        private void OnEntityGone(string id)
        {
            if (!_entityKinds.TryGetValue(id, out var kind) || kind != "EscapePod" || !_entities.TryGetValue(id, out var go) || go == null)
            {
                return;
            }

            Vector3 podLocal = go.transform.localPosition;
            Transform rescuer = null;
            float best = 30f * 30f;
            if (_ship != null && (_ship.transform.localPosition - podLocal).sqrMagnitude < best)
            {
                rescuer = _ship.transform;
                best = (_ship.transform.localPosition - podLocal).sqrMagnitude;
            }

            foreach (var kv in _remotePlayers)
            {
                var av = kv.Value;
                if (av.Root != null && (av.Root.transform.localPosition - podLocal).sqrMagnitude < best)
                {
                    rescuer = av.Root.transform;
                    best = (av.Root.transform.localPosition - podLocal).sqrMagnitude;
                }
            }

            if (rescuer == null)
            {
                return;
            }

            Vector3 from = rescuer.position;
            Vector3 to = go.transform.position;
            SpaceFx.Tractor(from, to, FxLook.ForModule(Game.Content, "tractor_beam").Color);
            FxKit.Flash(from, new Color(1f, 0.8f, 0.45f), 2f * FxKit.FlashScale, 0.25f);
            int sparks = FxKit.Scaled(14);
            for (int i = 0; i < sparks; i++)
            {
                Vector3 p = to + Random.insideUnitSphere * 1.5f;
                float life = Random.Range(0.35f, 0.6f);
                FxKit.Emit(FxKit.Kind.Motes, p, (from - p) / life, Random.Range(0.08f, 0.15f), life, new Color(1f, 0.75f, 0.35f));
            }

            ClientAudio.Instance?.Cue("pod_rescue", 0.8f);
        }

        // ---------------- Wormholes ----------------

        /// <summary>The wormhole within "fly through" reach of the ship (null when none).</summary>
        private void UpdateNearWormhole(Vector3 shipLocal)
        {
            _nearWormholeId = null;
            _wormholeSendCd -= Time.deltaTime;
            var space = Game?.Space;
            if (space == null || WormholeTransitFx.Busy)
            {
                return;
            }

            float reach = Game.Content?.Wormholes?.TransitRange ?? 26f;
            float best = reach * reach;
            foreach (var e in space.Entities)
            {
                if (e.Kind != "Wormhole")
                {
                    continue;
                }

                float sq = (new Vector3(e.X, e.Y, e.Z) - shipLocal).sqrMagnitude;
                if (sq <= best)
                {
                    best = sq;
                    _nearWormholeId = e.Id;
                }
            }
        }

        /// <summary>E at the rift: ask the server to fly through, and start the transit look at once (the arrival half
        /// plays when the twin system's flight state arrives; a refusal lets it fade).</summary>
        private void BeginWormholeTransit()
        {
            if (_nearWormholeId == null || _wormholeSendCd > 0f)
            {
                return;
            }

            _wormholeSendCd = 2f;
            Game.Network?.SendWormholeTransit(_nearWormholeId);
            WormholeTransitFx.PlayEnter(Game?.Settings);
        }

        /// <summary>The flight state of the twin system arrived through a wormhole: tear the old view down without a
        /// landing descent (like a hyperjump) and finish the transit look.</summary>
        private void OnWormholeArrived()
        {
            _hyperjumping = true;
            WormholeTransitFx.PlayArrival();
        }
    }
}
