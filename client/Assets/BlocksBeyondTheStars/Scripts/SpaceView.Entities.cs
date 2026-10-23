// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.Primitives;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;
using UnityEngine.UI;
using static BlocksBeyondTheStars.Client.ModelKit;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// What else moves in space: other players' ships and spacewalking suits (#2709) and the server's space entities
    /// (drones, UFOs, cruisers, raiders, salvage), drawn with snapshot interpolation (#756).
    /// </summary>
    public sealed partial class SpaceView
    {
        /// <summary>Draws the other players in this space instance — a ship for a pilot, a small suit for an
        /// EVA — from the server's per-player poses (R2). Pooled by player id; stale ones are removed.</summary>
        private void SyncRemotePlayers()
        {
            _remoteSeen.Clear();
            var players = Game.Space?.Players;
            // A fresh SpaceState reference == a new snapshot arrived — push one interpolation sample per
            // pose (#756); every frame in between renders from the buffer instead of snapping.
            bool fresh = players != null && !ReferenceEquals(Game.Space, _lastPlayersSnapshot);
            if (fresh)
            {
                _lastPlayersSnapshot = Game.Space;
            }

            double now = Time.timeAsDouble;
            if (players != null && _root != null)
            {
                foreach (var rp in players)
                {
                    if (string.IsNullOrEmpty(rp.PlayerId))
                    {
                        continue;
                    }

                    _remoteSeen.Add(rp.PlayerId);
                    if (!_remotePlayers.TryGetValue(rp.PlayerId, out var av) || av.Root == null)
                    {
                        av = BuildRemoteAvatar();
                        _remotePlayers[rp.PlayerId] = av;
                    }

                    av.Name = rp.Name ?? string.Empty; // shown as a floating nameplate (item 385); NPC trader poses carry their pilot's name, so they get one too
                    if (fresh)
                    {
                        av.LastSeen = now;
                        if (!_remoteLerp.TryGetValue(rp.PlayerId, out var push))
                        {
                            // Pilot poses are broadcast every 0.2 s — the delay must exceed that
                            // (interpolator doc), or the buffer starves every cycle: hold-then-snap
                            // jitter, the "flickering" remote ship of #955.
                            _remoteLerp[rp.PlayerId] = push = new RemoteEntityInterpolator(0.3);
                        }

                        // #2709: stepping out (or back in) swaps the body the pose describes — the suit starts at the
                        // hatch instead of gliding out of the ship's centre, and the ship never glides after the suit.
                        if (rp.Eva != av.Eva)
                        {
                            av.Eva = rp.Eva;
                            push.Clear();
                        }

                        push.Push(now, new Vector3f(rp.X, rp.Y, rp.Z), rp.Yaw);
                    }

                    if (_remoteLerp.TryGetValue(rp.PlayerId, out var lerp) && lerp.Sample(now, SpaceNoWrap, out var lp, out float lyaw))
                    {
                        av.Root.transform.localPosition = new Vector3(lp.X, lp.Y, lp.Z);
                        av.Root.transform.localRotation = Quaternion.Euler(0f, lyaw, 0f);
                    }
                    else
                    {
                        av.Root.transform.localPosition = new Vector3(rp.X, rp.Y, rp.Z);
                        av.Root.transform.localRotation = Quaternion.Euler(0f, rp.Yaw, 0f);
                    }

                    // Upgrade the generic hull to the pilot's REAL voxel ship once its design arrived
                    // (the server cross-sends every instance member's design as "ship_remote"), and re-mesh
                    // it when the pilot repaints the hull mid-flight (the paint lives in the mesh, item 32).
                    int hullRgb = rp.Hull != 0 ? rp.Hull : 0xD1D6E0;
                    if ((!av.Voxel || av.HullRgb != hullRgb)
                        && Game.RemoteShipDesignFor(rp.PlayerId) is { } rd && ShipMeshBuilder.HasDesign(rd))
                    {
                        var vox = ShipMeshBuilder.BuildVoxelShip(Game, av.Root.transform, rd, out _, Rgb(hullRgb));
                        if (vox != null)
                        {
                            Destroy(av.Ship);
                            vox.transform.localScale = Vector3.one * FlightShipScale; // same compact flight scale as the own ship
                            av.Ship = vox;
                            av.PlumesOff = false; // #2431: a new hull brings new plumes — the parked state is re-applied below
                            // #2157/#2162: a plume on each of their engines (it used to sit on their rear door).
                            av.Plume = null;
                            foreach (var (exLocal, exSize) in ShipMeshBuilder.ExhaustPoints(Game.Content, rd))
                            {
                                var exPlume = SpaceFx.EnginePlume(vox.transform, exLocal, exSize, new Color(0.6f, 0.82f, 1f));
                                if (av.Plume == null)
                                {
                                    av.Plume = exPlume;
                                }
                            }
                            av.HullMat = null; // a voxel ship carries its real block textures — paint is meshed in
                            av.Voxel = true;
                            av.HullRgb = hullRgb;
                        }
                    }

                    // #2709: on a spacewalk the pose is the suit — drawn as the partner's own suited figure — and their
                    // ship stays where it floats (the server sends that pose too). It used to vanish, and the suit was
                    // a bare cube, so two friends building together could not see each other or the other's ship.
                    bool shipOut = rp.Eva && rp.HasShipPose;
                    PlaceRemoteShip(av, rp, shipOut);
                    av.Ship.SetActive(!rp.Eva || shipOut);
                    SyncRemoteSuit(av, rp);
                    if (av.HullMat != null)
                    {
                        av.HullMat.color = ShaderColor.Srgb(Rgb(hullRgb)); // their chosen hull colour (item 32)
                    }

                    // #2431: a parked ship (its pilot walks inside) keeps its hull but cuts its engines — the plumes go out,
                    // and the nameplate says so. Re-applied when the hull was just swapped for the voxel design, whose
                    // plumes are new particle systems.
                    av.Parked = rp.Parked;
                    bool plumesOff = (rp.Parked && !rp.Eva) || shipOut; // #2709: nobody at the helm of a ship left for a spacewalk
                    if (plumesOff != av.PlumesOff)
                    {
                        av.PlumesOff = plumesOff;
                        foreach (var ps in av.Ship.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var emission = ps.emission;
                            emission.enabled = !plumesOff;
                        }
                    }
                }
            }

            if (_remotePlayers.Count > _remoteSeen.Count)
            {
                _remoteRemove.Clear();
                foreach (var kv in _remotePlayers)
                {
                    // Grace period (#955): poses are only written by ShipMove, so a player can drop out
                    // of a single snapshot without having left — destroying instantly caused visible
                    // avatar popping (rebuilt as a generic cube ship first). 2 s ≈ ten missed snapshots.
                    if (!_remoteSeen.Contains(kv.Key) && now - kv.Value.LastSeen > 2.0)
                    {
                        _remoteRemove.Add(kv.Key);
                    }
                }

                foreach (var id in _remoteRemove)
                {
                    var gone = _remotePlayers[id];
                    if (gone.Ship != null && gone.Root != null && gone.Ship.transform.parent != gone.Root.transform)
                    {
                        Destroy(gone.Ship); // #2709: a ship left floating during a spacewalk hangs off the scene root
                    }

                    if (gone.Root != null)
                    {
                        Destroy(gone.Root);
                    }

                    _remotePlayers.Remove(id);
                    _remoteLerp.Remove(id);
                }
            }
        }

        /// <summary>#2709: the suit pose sits at the eyes (the spacewalker's camera) — the figure hangs below it.</summary>
        private const float RemoteSuitEyeHeight = 1.85f;

        /// <summary>#2709: where another pilot's ship is drawn. Flying (or parked with its pilot inside) it rides the pilot's
        /// interpolated pose under the avatar root, at the compact flight scale. While its pilot is on a spacewalk it hangs
        /// off the scene root at the floating ship pose the server sends, at the full 1:1 scale the spacewalker builds on —
        /// the same switch the own ship makes on an EVA. The pose is static while nobody is at the helm, so it is set as is.</summary>
        private void PlaceRemoteShip(RemoteAvatar av, BlocksBeyondTheStars.Networking.Messages.NetSpacePlayer rp, bool shipOut)
        {
            var ship = av.Ship.transform;
            var parent = shipOut ? _root.transform : av.Root.transform;
            if (ship.parent != parent)
            {
                ship.SetParent(parent, false);
                ship.localPosition = Vector3.zero;
                ship.localRotation = Quaternion.identity;
            }

            if (shipOut)
            {
                ship.localPosition = new Vector3(rp.ShipX, rp.ShipY, rp.ShipZ);
                ship.localRotation = Quaternion.Euler(0f, rp.ShipYaw, 0f);
            }

            if (av.Voxel)
            {
                float scale = shipOut ? 1f : FlightShipScale;
                if (ship.localScale.x != scale)
                {
                    ship.localScale = Vector3.one * scale;
                }
            }
        }

        /// <summary>#2709: shows another pilot's spacewalk as their own suited figure (the avatar the planet view draws for
        /// them, in their suit colours) instead of a bare cube. Built on the first spacewalk only — NPC traders never get
        /// one — then kept and just hidden while the pilot is aboard; the colours are re-applied only when they change.</summary>
        private void SyncRemoteSuit(RemoteAvatar av, BlocksBeyondTheStars.Networking.Messages.NetSpacePlayer rp)
        {
            if (!rp.Eva)
            {
                if (av.Suit != null && av.Suit.activeSelf)
                {
                    av.Suit.SetActive(false);
                }

                return;
            }

            // An unset look (0 everywhere) is the default suit the server gives every new player.
            bool unset = (rp.Skin | rp.Torso | rp.Arms | rp.Legs) == 0;
            int skin = unset ? 0xD9AE8C : rp.Skin;
            int torso = unset ? 0x3372CC : rp.Torso;
            int arms = unset ? 0x3372CC : rp.Arms;
            int legs = unset ? 0x40404F : rp.Legs;
            if (av.Body == null)
            {
                var go = new GameObject("Suit");
                go.transform.SetParent(av.Root.transform, false);
                go.transform.localPosition = new Vector3(0f, -RemoteSuitEyeHeight, 0f);
                av.Body = go.AddComponent<PlayerAvatar>();
                av.Body.NeverAirborne = true; // floating — never the leap pose
                av.Body.Build(Rgb(skin), Rgb(torso), Rgb(arms), Rgb(legs), spacesuit: true);
                av.Body.SetVisible(true);
                av.Suit = go;
            }
            else if (skin != av.SuitSkin || torso != av.SuitTorso || arms != av.SuitArms || legs != av.SuitLegs)
            {
                av.Body.ApplyColors(Rgb(skin), Rgb(torso), Rgb(arms), Rgb(legs));
            }

            av.SuitSkin = skin;
            av.SuitTorso = torso;
            av.SuitArms = arms;
            av.SuitLegs = legs;
            if (!av.Suit.activeSelf)
            {
                av.Suit.SetActive(true);
            }
        }

        private RemoteAvatar BuildRemoteAvatar()
        {
            var root = new GameObject("RemotePlayer");
            root.transform.SetParent(_root.transform, false);

            // Ship: a small textured hull + glass cockpit (same block textures as your own ship), so other
            // players read as real ships out here rather than flat cubes.
            var ship = new GameObject("Ship");
            ship.transform.SetParent(root.transform, false);
            // #2938: the placeholder hull owns its materials and tiles — they go when the real voxel hull replaces it or
            // the pilot leaves.
            var owned = BeginOwned();
            Material hullMat;
            try
            {
                hullMat = LitTex("iron_wall", new Color(0.82f, 0.84f, 0.88f)); // re-tinted per-player in SyncRemotePlayers (item 32)
                Cube("Body", ship.transform, Vector3.zero, new Vector3(1.6f, 0.9f, 3.4f), hullMat);
                Cube("Cockpit", ship.transform, new Vector3(0f, 0.45f, 1.1f), new Vector3(0.85f, 0.55f, 0.95f), LitTex("glass", new Color(0.7f, 0.9f, 1f)));
            }
            finally
            {
                EndOwned(owned, ship);
            }

            // #2157: other ships have engines too (they used to glide without any exhaust). The spacewalk figure is built on
            // the first spacewalk (SyncRemoteSuit, #2709).
            var plume = SpaceFx.EnginePlume(ship.transform, new Vector3(0f, 0f, -1.9f), 0.8f, new Color(0.6f, 0.82f, 1f));
            return new RemoteAvatar { Root = root, Ship = ship, HullMat = hullMat, Plume = plume };
        }

        private void SyncEntities()
        {
            var space = Game.Space;
            var seen = _entitySeen;
            seen.Clear();
            bool fresh = space != null && !ReferenceEquals(space, _lastEntitySnapshot);
            if (fresh)
            {
                _lastEntitySnapshot = space;
            }

            double now = Time.timeAsDouble;
            if (space != null)
            {
                foreach (var e in space.Entities)
                {
                    seen.Add(e.Id);

                    // item 20 S3: asteroids render as voxel ore structures (see ReconcileStructs), not cube
                    // entities — but they stay in Space.Entities so the ship's weapons can still target them.
                    // A wreckage fragment (#2353/#2356) is the same kind of thing.
                    if (e.Kind == "Asteroid" || e.Kind == "Debris")
                    {
                        continue;
                    }

                    // #2357: the raider's REAL hull arrives as a "ship_remote" design — possibly after the entity.
                    // Swap the placeholder wedge for the voxel ship as soon as the design is here.
                    if (e.Kind == "BanditShip" && !_voxelRaiders.Contains(e.Id) && RaiderDesignFor(e.Id) != null
                        && Game.ChunkMaterial != null && Game.Atlas != null // the mesher needs both — else the wedge would churn every frame
                        && _entities.TryGetValue(e.Id, out var wedge))
                    {
                        if (wedge != null)
                        {
                            Destroy(wedge);
                        }

                        _entities.Remove(e.Id);
                    }

                    // #1469: a player-built station renders as its own voxel hull (a "station" design in _structs
                    // under the same id) — never ALSO as the generic spinning placeholder model. A placeholder
                    // built before the design arrived (a late SpaceShipDesign) is torn down here. The same
                    // goes for the derelict wreck (#1664), whose hull arrives as a "wreck" design.
                    if ((e.Kind == "SpaceStation" || e.Kind == "Wreck") && _structs.ContainsKey(e.Id))
                    {
                        if (_entities.TryGetValue(e.Id, out var placeholder))
                        {
                            Destroy(placeholder);
                            _entities.Remove(e.Id);
                            _entityLerp.Remove(e.Id);
                        }

                        continue;
                    }

                    if (!_entities.TryGetValue(e.Id, out var go))
                    {
                        go = BuildEntityModel(e);

                        // Stations scale by size tier (server-sent) — a colossal hull dwarfs a small one.
                        if (e.Kind == "SpaceStation" && e.Scale > 0.01f)
                        {
                            go.transform.localScale = Vector3.one * e.Scale;
                        }

                        _entities[e.Id] = go;
                        _entityKinds[e.Id] = e.Kind;
                        AttachHostilePlumes(go, e.Kind);
                    }

                    // Stations (and the derelict wreck, #1664) are static scenery — everything else the server
                    // moves gets the snapshot buffer (#756). Rotation stays driven by Spin (no yaw on the entity wire).
                    if (e.Kind != "SpaceStation" && e.Kind != "Wreck" && !IsStaticEncounter(e.Kind))
                    {
                        if (fresh)
                        {
                            if (!_entityLerp.TryGetValue(e.Id, out var push))
                            {
                                // Hostile poses arrive every 0.15 s — render behind that interval so the
                                // buffer never starves (#955); the default 0.15 delay sat exactly on it.
                                _entityLerp[e.Id] = push = new RemoteEntityInterpolator(0.25);
                            }

                            push.Push(now, new Vector3f(e.X, e.Y, e.Z), 0f);
                        }

                        if (_entityLerp.TryGetValue(e.Id, out var lerp) && lerp.Sample(now, SpaceNoWrap, out var lp, out _))
                        {
                            go.transform.localPosition = new Vector3(lp.X, lp.Y, lp.Z);
                        }
                        else
                        {
                            go.transform.localPosition = new Vector3(e.X, e.Y, e.Z);
                        }

                        if (e.Kind == "BanditShip")
                        {
                            // #2357: the hull faces its course (the server's heading), eased so the turn reads as flown.
                            go.transform.localRotation = Quaternion.Slerp(go.transform.localRotation, Quaternion.Euler(0f, e.Yaw, 0f), Time.deltaTime * 3f);
                        }
                    }
                    else
                    {
                        go.transform.localPosition = new Vector3(e.X, e.Y, e.Z);
                    }

                    if (e.Kind == "ResourceDrop" || e.Kind == "SalvageCapsule")
                    {
                        _dropIds.Add(e.Id); // a capsule (#2353) is pulled in like a drop
                    }

                    // #2156: a hit target flashes white for a moment (the server's hull drop is the confirmation).
                    if (fresh)
                    {
                        if (_entityHull.TryGetValue(e.Id, out float prevHull) && e.Hull < prevHull - 0.05f)
                        {
                            FxDefeat.HitFlash(go, 0.8f);
                        }

                        _entityHull[e.Id] = e.Hull;
                    }
                }
            }

            // #2708: ALWAYS look for views whose entity left the snapshot. This used to run only while there were more
            // views than snapshot ids — but asteroids, debris and hull-backed stations/wrecks are in the snapshot without
            // a view, so with any of them around (almost always) a collected drop, a rescued pod or a destroyed hostile
            // stayed on screen as a ghost. A drop has no "destroyed" message: missing from the snapshot is its only exit.
            var stale = _entityRemove;
            if (BlocksBeyondTheStars.Client.Core.SpaceEntitySync.CollectStale(_entities, seen, stale) > 0)
            {
                foreach (var id in stale)
                {
                    // A resource drop that vanished close to the ship was tractored in → beam it.
                    if (_dropIds.Remove(id) && _ship != null && _entities[id] != null)
                    {
                        var dropPos = _entities[id].transform.localPosition;
                        if ((dropPos - _ship.transform.localPosition).sqrMagnitude < 14f * 14f)
                        {
                            SpawnTractorBeam(_ship.transform.localPosition, dropPos);
                            _cargoFlash = 1f;
                            if (_entityKinds.TryGetValue(id, out var goneKind) && goneKind == "SalvageCapsule")
                            {
                                ClientAudio.Instance?.Cue("salvage_capsule", 0.9f); // #2353: the latch and the chime
                            }
                        }
                    }

                    OnEntityGone(id); // #2241: a rescued life pod is pulled aboard on a tractor beam
                    _voxelRaiders.Remove(id);
                    Destroy(_entities[id]);
                    _entities.Remove(id);
                    _entityKinds.Remove(id);
                    _entityHull.Remove(id);
                    _entityLerp.Remove(id);
                    EnemyHealthBars.Forget(id);
                }
            }
        }

        /// <summary>The model of a space entity seen for the first time: real textured multi-cube models (mirrors the
        /// ship) instead of a flat colour cube. #2938: the model owns the materials and block tiles ModelKit made for it,
        /// so they go with its last part when the entity despawns (they used to pile up until the return to the menu).</summary>
        private GameObject BuildEntityModel(BlocksBeyondTheStars.Networking.Messages.NetCombatEntity e)
        {
            var owned = BeginOwned();
            GameObject go = null;
            try
            {
                go = e.Kind switch
                {
                    "SpaceStation" => BuildStationModel(_root.transform),
                    "Drone" => BuildDroneModel(_root.transform),
                    "Ufo" => BuildUfoModel(_root.transform),
                    "Cruiser" => BuildCruiserModel(_root.transform),
                    "BanditShip" => BuildBanditShipModel(_root.transform, e), // #2357: its real voxel hull once the design is here
                    "EscapePod" => BuildEscapePodModel(_root.transform, e),   // #2241: a little lifeboat, not a red cube
                    "Anomaly" => BuildAnomalyModel(_root.transform, e),       // #2241: the shimmering soap bubble
                    "Wormhole" => BuildWormholeModel(_root.transform, e),     // #2242: the tear in space-time
                    "DebrisField" => BuildDebrisFieldMarkerModel(_root.transform, e), // #2353: the flight recorder's beacon
                    "SalvageCapsule" => BuildSalvageCapsuleModel(_root.transform, e), // #2353: a sealed canister
                    _ => Cube("Entity", _root.transform, Vector3.zero, EntityScale(e.Kind), Unlit(EntityColor(e.Kind))), // ResourceDrop etc.
                };
            }
            finally
            {
                EndOwned(owned, go);
            }

            return go;
        }
    }
}
