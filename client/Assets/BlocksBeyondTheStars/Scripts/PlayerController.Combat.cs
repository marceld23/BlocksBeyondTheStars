// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using BlocksBeyondTheStars.Shared.Geometry;
using BlocksBeyondTheStars.Shared.State;
using BlocksBeyondTheStars.Shared.World;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Combat: crosshair auto-aim inside a forward cone (<see cref="UpdateEnemyAim"/>), melee and weapon attacks on the
    /// best target (<see cref="AttackNearestEnemy"/>), the line-of-fire traces against terrain, the weapon effect sent to
    /// the server (<see cref="SendFx"/>), the swing cooldowns and the weapon sound (<see cref="PlayWeaponSound"/>).
    /// </summary>
    public sealed partial class PlayerController
    {
        /// <summary>The selected hotbar item is a weapon (PrimaryFire has something better than fists to swing).</summary>
        public bool HoldsWeapon =>
            Game?.Content?.GetItem(Game.ItemInSlot(Game.SelectedHotbarSlot))?.Tool?.Kind
                == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon;

        // Crosshair aiming (#693): auto-aim acquires inside a forward cone only (never behind the back);
        // a melee swing sweeps a wider arc; with the AutoAim world rule OFF a ranged shot needs a genuine
        // crosshair hit — nothing under the reticle means the shot misses.
        private const float AutoAimCone = 0.819f; // cos ~35°
        private const float MeleeCone = 0.5f;     // cos ~60°

        private void AttackNearestEnemy()
        {
            if (Game?.Network == null)
            {
                return;
            }

            PlayWeaponSound();

            // Reach follows the equipped weapon: a ranged weapon must let you hit at its full range, not the bare
            // melee reach — otherwise a "gun" only ever fires point-blank (where the enemy's own bite already
            // reaches you). Melee weapons / fists stay at the default 6-block reach.
            var heldTool = HeldTool();
            float reach = heldTool != null && heldTool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon
                ? Mathf.Max(6f, heldTool.Range)
                : 6f;
            var kind = HeldWeaponFx();
            bool melee = kind == WeaponFxKind.Melee;

            // The entity under the crosshair always wins — it is what the player is looking at.
            string targetId = null;
            Vector3 targetPos = default;
            if (AimEnemy(reach, out var aimId, out var aimPos, out float terrainDist))
            {
                targetId = aimId;
                targetPos = aimPos;
            }
            else if (melee || Game.AutoAimOn)
            {
                // Auto-aim (and every melee swing): nearest target inside the forward cone. The old
                // 360° nearest-anywhere selection is gone — no more kills behind your back.
                targetId = BestConeTarget(reach, melee ? MeleeCone : AutoAimCone, out targetPos);
            }

            var ct = Camera != null ? Camera.transform : transform;
            if (targetId != null)
            {
                var f = ct.forward;
                Game.Network.SendAttackEntity(targetId,
                    new BlocksBeyondTheStars.Shared.Geometry.Vector3f(f.x, f.y, f.z));
                Game.LastShotTargetId = targetId;
                Game.LastShotTime = Time.time;
            }
            else if (!melee && heldTool != null && heldTool.Ignites
                     && TerrainHit(ct.position, ct.forward, reach, out var burnCell, out _))
            {
                // A laser/plasma bolt that hits terrain instead of a creature can set it alight (#788). Only
                // the impact cell travels; the server decides whether anything actually burns there.
                Game.Network.SendShootBlock(burnCell.x, burnCell.y, burnCell.z);
            }

            if (Weapons != null && Camera != null)
            {
                // #2154: the held item's own look (data-driven fx), leaving the real barrel (#2151) — not the screen
                // centre. The swing/shot is mirrored to nearby players as a cosmetic FxIntent (#2158).
                string heldKey = Game.ItemInSlot(Game.SelectedHotbarSlot);
                var look = FxLook.ForItem(Game.Content, heldKey);
                var from = Muzzle(ct);
                if (kind == WeaponFxKind.Melee)
                {
                    // A melee slash sweeps whether or not it connects (whiff still reads). The gloves' blow (#2278) starts
                    // where it peaks: between the palms (the shock push) or in front of the jabbing fist (energy).
                    bool gloves = FxStyleResolver.IsGlove(look.Style);
                    var center = gloves ? from : ct.position + ct.forward * 0.35f - ct.up * 0.18f;
                    var hitPoint = targetId != null ? targetPos + Vector3.up * 0.6f : center + ct.forward;
                    Weapons.Swing(look, center, ct.forward, ct.up, targetId != null, hitPoint, local: true);
                    if (targetId != null)
                    {
                        // the shipped-but-unused hit cue (#2151); the gloves have their own (#2278), the push quieter at the target
                        ClientAudio.Instance?.At(FxStyleResolver.MeleeHitCue(look.Style), hitPoint, 1f,
                            look.Is(BlocksBeyondTheStars.Shared.Definitions.FxStyles.ShockPush) ? 0.5f : 1f);
                    }

                    SendFx(BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Melee, heldKey, center, hitPoint, targetId != null);
                }
                else
                {
                    // A hit flies to the body; a miss still leaves the muzzle and dies on the terrain
                    // (or at max range) — with manual aiming, "wide" has to read as wide.
                    bool terrain = targetId == null && terrainDist < reach - 0.01f;
                    var target = targetId != null
                        ? targetPos + Vector3.up * 0.4f
                        : ct.position + ct.forward * Mathf.Min(reach, terrainDist);
                    var normal = terrain ? FaceNormal(target, ct.forward) : -ct.forward;
                    Weapons.Fire(look, from, target, targetId != null || terrain, normal, local: true);
                    SendFx(BlocksBeyondTheStars.Shared.Definitions.FxActionKinds.Shot, heldKey, from, target, targetId != null || terrain);
                }
            }
        }

        /// <summary>Where the held weapon/tool's effects start: the tip of the first-person viewmodel, the avatar's hand
        /// in third person, else just below the crosshair (#2151).</summary>
        private Vector3 Muzzle(Transform ct)
        {
            if (!ThirdPerson && _viewmodel != null && _viewmodel.TryMuzzle(out var tip))
            {
                return tip;
            }

            if (ThirdPerson && Avatar != null && Avatar.TryMuzzle(out var hand))
            {
                return hand;
            }

            return ct.position + ct.forward * 0.4f - ct.up * 0.15f;
        }

        /// <summary>The outward face normal of the voxel a ray along <paramref name="dir"/> struck at
        /// <paramref name="point"/> — the axis the point sits furthest out on, facing back toward the shooter.</summary>
        private static Vector3 FaceNormal(Vector3 point, Vector3 dir)
        {
            var probe = point + dir * 0.02f; // just inside the struck block
            var local = probe - new Vector3(Mathf.Floor(probe.x) + 0.5f, Mathf.Floor(probe.y) + 0.5f, Mathf.Floor(probe.z) + 0.5f);
            var a = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
            if (a.x >= a.y && a.x >= a.z)
            {
                return new Vector3(Mathf.Sign(local.x), 0f, 0f);
            }

            return a.y >= a.z ? new Vector3(0f, Mathf.Sign(local.y), 0f) : new Vector3(0f, 0f, Mathf.Sign(local.z));
        }

        private float _nextFxSend;

        /// <summary>Mirrors one of the player's own tool actions to nearby players (#2158) — cosmetic only, rate-limited
        /// here as well as on the server.</summary>
        private void SendFx(byte kind, string itemKey, Vector3 from, Vector3 to, bool hit)
        {
            if (Game?.Network == null || Time.time < _nextFxSend)
            {
                return;
            }

            _nextFxSend = Time.time + 0.1f;
            Game.Network.SendFx(kind, itemKey ?? string.Empty, from, to, hit);
        }

        /// <summary>Finds the enemy/creature under the crosshair (#693): an analytic ray-vs-sphere sweep over
        /// the replicated entities — their meshes deliberately carry no colliders — occluded by terrain via a
        /// voxel march (the weapon trace: leaves, grass and torches are no cover, #2533). Hitboxes are deliberately
        /// generous (kid-friendly forgiveness). Also reports how far the ray flies before hitting terrain, for the
        /// miss tracer.</summary>
        private bool AimEnemy(float maxRange, out string id, out Vector3 pos, out float terrainDist)
        {
            id = null;
            pos = default;
            terrainDist = maxRange;
            if (Game == null || Camera == null)
            {
                return false;
            }

            Vector3 o = Camera.transform.position;
            Vector3 dir = Camera.transform.forward;
            terrainDist = TerrainDistance(o, dir, maxRange);

            float best = terrainDist + 0.5f; // a body right at the wall still counts
            foreach (var e in Game.PlanetEnemies)
            {
                var basePos = Game.ScenePos(e.X, e.Y, e.Z); // seam-aware (longitude wraps)
                var center = basePos + Vector3.up * 0.9f;
                float r = 1.1f * Mathf.Max(1f, e.Scale);
                if (RayHitsSphere(o, dir, center, r, out float d) && d < best)
                {
                    best = d;
                    id = e.Id;
                    pos = basePos;
                }
            }

            // #1998: a giant is hit where the ray meets its body — its colliders, not a sphere at its feet.
            if (Physics.Raycast(o, dir, out var giantHit, best, 1 << CreatureView.GiantLayer, QueryTriggerInteraction.Ignore)
                && CreatureView.GiantIdFor(giantHit.collider) is { } giantId)
            {
                best = giantHit.distance;
                id = giantId;
                pos = giantHit.point;
            }

            foreach (var c in Game.Creatures)
            {
                if (c.GiantHeight > 0f || !string.IsNullOrEmpty(c.OwnerId))
                {
                    continue; // a giant is picked by its colliders above; a companion or pet is never a target (#2281)
                }

                float size = Mathf.Clamp(c.Size, 0.4f, 8f);
                var basePos = Game.ScenePos(c.X, c.Y, c.Z);
                var center = basePos + Vector3.up * (0.6f * size);
                float r = Mathf.Max(0.8f, 0.9f * size);
                if (RayHitsSphere(o, dir, center, r, out float d) && d < best)
                {
                    best = d;
                    id = c.Id;
                    pos = basePos;
                }
            }

            AimPlayers(o, dir, ref best, ref id, ref pos); // #2972: another player, when the world's PvP rules allow
            return id != null;
        }

        /// <summary>Nearest attackable entity inside the camera-forward cone (auto-aim / melee sweep).
        /// Point-blank targets (&lt; 1.5 blocks) ignore the cone — something chewing on your boots is hittable
        /// even while you look past it. A target behind a wall is skipped (#2533): the server refuses a hit without a
        /// clear line of fire, so the sweep picking it used to swing at the wall while a target in the open stood
        /// beside it. The line is the server's (feet + 1.5 on both ends) through the weapon trace, so leaves, grass
        /// and torches are no cover here either.</summary>
        private string BestConeTarget(float reach, float cone, out Vector3 pos)
        {
            string bestId = null;
            Vector3 bestPos = default; // out params can't be captured by the local function below
            float bestSq = reach * reach;
            Vector3 eye = Camera != null ? Camera.transform.position : transform.position;
            Vector3 fwd = Camera != null ? Camera.transform.forward : transform.forward;
            Vector3 sightFrom = transform.position + Vector3.up * 1.5f; // the server's eye (ValidateAim), not the chase camera

            void Consider(string cid, Vector3 p, Vector3 sightTo)
            {
                var to = p + Vector3.up * 0.9f - eye;
                float d = to.sqrMagnitude;
                if (d >= bestSq || d < 0.0001f)
                {
                    return;
                }

                if (d > 2.25f && Vector3.Dot(to.normalized, fwd) < cone)
                {
                    return;
                }

                if (!WeaponLineClear(sightFrom, sightTo))
                {
                    return; // behind a wall — the server would refuse the hit
                }

                bestSq = d;
                bestId = cid;
                bestPos = p;
            }

            foreach (var e in Game.PlanetEnemies)
            {
                var ep = Game.ScenePos(e.X, e.Y, e.Z);
                Consider(e.Id, ep, ep + Vector3.up * 1.5f);
            }

            // Creatures (fauna) are attackable too — the server shares the hit path. Never a companion or pet (#2281):
            // nobody may attack one, so the sweep must not pick it (and swing past a wild animal behind it).
            foreach (var c in Game.Creatures)
            {
                if (!string.IsNullOrEmpty(c.OwnerId))
                {
                    continue;
                }

                if (c.GiantHeight > 0f)
                {
                    // #1998: a giant counts from the nearest point of its body (and a buried sandworm not at all).
                    if (CreatureView.GiantNearestPoint(c.Id, eye, out var near))
                    {
                        Consider(c.Id, near - Vector3.up * 0.9f, near); // the server's giant aim point is the body point itself
                    }

                    continue;
                }

                var cp = Game.ScenePos(c.X, c.Y, c.Z);
                Consider(c.Id, cp, cp + Vector3.up * 1.5f);
            }

            foreach (var (pid, pp) in PvpTargetsInScene()) // #2972: another player, when the world's PvP rules allow
            {
                Consider(pid, pp, pp + Vector3.up * 1.5f);
            }

            pos = bestPos;
            return bestId;
        }

        /// <summary>Whether the weapon trace runs from <paramref name="from"/> to <paramref name="to"/> without striking
        /// terrain (#2533) — the auto-aim/melee sweep's mirror of the server's line of fire. Lenient by half a block at
        /// the far end, since the server skips the target's own end of the line.</summary>
        private bool WeaponLineClear(Vector3 from, Vector3 to)
        {
            Vector3 span = to - from;
            float len = span.magnitude;
            if (len < 1f)
            {
                return true; // point-blank — nothing fits between
            }

            return TerrainDistance(from, span / len, len) >= len - 0.5f;
        }

        /// <summary>Distance the WEAPON ray travels before hitting solid terrain (voxel DDA like
        /// <see cref="AimBlock"/>, but with a caller-chosen range — weapon range exceeds block reach).
        /// It stops exactly where the server's line of fire stops (<see cref="StopsWeapon"/>): non-solid blocks (water,
        /// oil, a rug, an open door) and everything the player walks through (#2533: leaves, grass and other flora,
        /// torches, the ladder) are passed — including the cell the camera stands in, so a player inside a tree crown no
        /// longer has a reach of zero. Glass and walls still stop it.</summary>
        private float TerrainDistance(Vector3 o, Vector3 dir, float maxDist)
            => TerrainHit(o, dir, maxDist, out _, out float dist, skipWalkThrough: true) ? dist : maxDist;

        /// <summary>Whether a cell stops the player's shot — the client twin of the server's attack sightline
        /// (<c>GameServer.HasAttackLineOfSight</c>): a block that is <c>Solid</c> (or unknown) and not one the player walks
        /// through. A non-solid decor block would otherwise make auto-aim skip a target the server accepts (#2533).</summary>
        private bool StopsWeapon(BlocksBeyondTheStars.Shared.Primitives.BlockId id)
        {
            if (id.IsAir)
            {
                return false;
            }

            var def = Game.Content?.BlockById(id);
            return def == null || (def.Solid && !BlocksBeyondTheStars.Shared.Definitions.WalkThroughBlocks.IsWalkThrough(def.Key));
        }

        /// <summary>The first solid terrain cell along the aim ray within <paramref name="maxDist"/>, and how
        /// far along the ray it sits. Used for the miss tracer's endpoint (through <see cref="TerrainDistance"/>, with
        /// <paramref name="skipWalkThrough"/>: the server's line-of-fire rule, <see cref="StopsWeapon"/>), and — without it,
        /// any non-air, non-fluid block, so a leaf still counts — for the rope gun's anchor and the cell a missed shot of
        /// an igniting weapon sets alight (#788).</summary>
        private bool TerrainHit(Vector3 o, Vector3 dir, float maxDist, out Vector3Int cell, out float distance, bool skipWalkThrough = false)
        {
            cell = default;
            distance = maxDist;
            if (Game?.World == null)
            {
                return false;
            }

            int x = Mathf.FloorToInt(o.x), y = Mathf.FloorToInt(o.y), z = Mathf.FloorToInt(o.z);
            int sx = dir.x >= 0 ? 1 : -1, sy = dir.y >= 0 ? 1 : -1, sz = dir.z >= 0 ? 1 : -1;
            float invx = Mathf.Abs(dir.x) > 1e-6f ? 1f / Mathf.Abs(dir.x) : float.PositiveInfinity;
            float invy = Mathf.Abs(dir.y) > 1e-6f ? 1f / Mathf.Abs(dir.y) : float.PositiveInfinity;
            float invz = Mathf.Abs(dir.z) > 1e-6f ? 1f / Mathf.Abs(dir.z) : float.PositiveInfinity;
            float tMaxX = float.IsInfinity(invx) ? float.PositiveInfinity : (dir.x > 0 ? (x + 1 - o.x) : (o.x - x)) * invx;
            float tMaxY = float.IsInfinity(invy) ? float.PositiveInfinity : (dir.y > 0 ? (y + 1 - o.y) : (o.y - y)) * invy;
            float tMaxZ = float.IsInfinity(invz) ? float.PositiveInfinity : (dir.z > 0 ? (z + 1 - o.z) : (o.z - z)) * invz;

            float t = 0f;
            for (int i = 0; i < 160 && t <= maxDist; i++)
            {
                var id = Game.World.GetBlock(x, y, z);
                if (skipWalkThrough ? StopsWeapon(id) : !id.IsAir && !IsFluidBlock(id))
                {
                    cell = new Vector3Int(x, y, z);
                    distance = t;
                    return true;
                }

                if (tMaxX <= tMaxY && tMaxX <= tMaxZ) { x += sx; t = tMaxX; tMaxX += invx; }
                else if (tMaxY <= tMaxZ) { y += sy; t = tMaxY; tMaxY += invy; }
                else { z += sz; t = tMaxZ; tMaxZ += invz; }
            }

            return false;
        }

        /// <summary>Ray-vs-sphere with the hit reported at the centre plane — plenty for ordering targets
        /// against each other and the terrain at gameplay scales.</summary>
        private static bool RayHitsSphere(Vector3 o, Vector3 dir, Vector3 center, float radius, out float dist)
        {
            dist = 0f;
            Vector3 to = center - o;
            float along = Vector3.Dot(to, dir);
            if (along < 0f)
            {
                return false;
            }

            if (to.sqrMagnitude - along * along > radius * radius)
            {
                return false;
            }

            dist = along;
            return true;
        }

        /// <summary>Publishes which enemy sits under the crosshair (every frame) — the HUD tints the reticle
        /// and the health-bar layer keeps that entity's bar visible.</summary>
        private void UpdateEnemyAim()
        {
            if (Game == null)
            {
                return;
            }

            var heldTool = HeldTool();
            float reach = heldTool != null && heldTool.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon
                ? Mathf.Max(6f, heldTool.Range)
                : 6f;
            Game.AimedEnemyId = AimEnemy(reach, out var id, out _, out _) ? id : null;
        }

        /// <summary>#2206: the values of the tool in hand. What the bio lab changed on a tool rides in its item key, so
        /// every read of a held tool's range, cooldown or energy goes through here — the call the server makes too.</summary>
        private BlocksBeyondTheStars.Shared.Definitions.ToolProperties HeldTool()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return string.IsNullOrEmpty(held)
                ? null
                : BlocksBeyondTheStars.Shared.Bio.ToolMods.Effective(Game.Content?.GetItem(held), held);
        }

        private enum WeaponFxKind { Beam, Projectile, Melee }

        /// <summary>Classifies the held weapon: melee looks swing (and aim with the wider melee cone), travelling looks
        /// fire a flying bolt, everything else an instant beam. Since #2152 it reads the item's data-driven
        /// <c>fx</c> style (<see cref="FxLook"/>) instead of guessing from the item key.</summary>
        private WeaponFxKind HeldWeaponFx()
        {
            var look = FxLook.ForItem(Game.Content, Game.ItemInSlot(Game.SelectedHotbarSlot));
            return look.Style switch
            {
                "slug" or "plasma" => WeaponFxKind.Projectile,
                "laser" or "rail" or "mining_beam" => WeaponFxKind.Beam,
                _ => WeaponFxKind.Melee, // blades, fists — and a drill or gadget swung at something
            };
        }

        /// <summary>True if the selected hotbar item is a weapon (its primary action attacks, like F).</summary>
        private bool HoldingWeapon()
        {
            string held = Game.ItemInSlot(Game.SelectedHotbarSlot);
            return !string.IsNullOrEmpty(held)
                && Game.Content?.GetItem(held)?.Tool?.Kind == BlocksBeyondTheStars.Shared.Definitions.ToolKind.Weapon;
        }

        private float _nextWeaponSwing; // Time.time when the held weapon may swing again (client-side cooldown)
        private float _nextFistSwing;   // Time.time when the bare hand may punch again — its own timer, like the server's (#2280)
        private const float DefaultMeleeCooldown = 1.5f; // mirrors the server default for energy-free melee (B44)

        /// <summary>Whether the held weapon's swing cooldown has elapsed; if so, arms the next swing. Mirrors the
        /// server cooldown so the swing animation/sound + attack intent are gated too (so the machete's 1.5s
        /// cooldown is actually felt, not just silently dropped server-side).</summary>
        private bool WeaponSwingReady()
        {
            // #2280: the bare hand punches on its own shared cooldown (MeleeRules) — it used to have none here, so the
            // swing played on every press while the server now holds the early punches back. It runs on its own timer,
            // like the server's separate fist entry, so drawing a weapon right after a punch is not held back by it
            // (and vice versa). #2202: a reflex preparation shortens both, like on the server. #2306: a drill, a scanner
            // or a gadget punches like the bare hand too (tools dig, weapons fight) — the old path gave an energy-using
            // tool a cooldown of 0 here, so the titanium drill swung as fast as the player could click.
            var tool = HeldTool();
            if (MeleeRules.IsPunch(tool))
            {
                if (Time.time < _nextFistSwing)
                {
                    return false;
                }

                _nextFistSwing = Time.time + MeleeRules.FistCooldownSeconds * Game.Bio.CooldownFactor;
                return true;
            }

            if (Time.time < _nextWeaponSwing)
            {
                return false;
            }

            float cd = tool.CooldownSeconds > 0f ? tool.CooldownSeconds
                : tool.EnergyPerUse <= 0f ? DefaultMeleeCooldown : 0f;
            _nextWeaponSwing = Time.time + cd * Game.Bio.CooldownFactor;
            return true;
        }

        /// <summary>Plays the firing/swing sound for the currently held weapon (if any).</summary>
        private void PlayWeaponSound()
        {
            var audio = ClientAudio.Instance;
            if (audio == null)
            {
                return;
            }

            // #2152: by the item's data-driven look, so a new gun with a known style sounds right without code.
            var look = FxLook.ForItem(Game.Content, Game.ItemInSlot(Game.SelectedHotbarSlot));
            switch (look.Style)
            {
                case "slug": audio.Cue("weapon_scrap"); break;
                case "rail": audio.Cue("weapon_gauss"); break;
                case "laser": audio.Cue("weapon_laser"); break;
                case "plasma": audio.Cue("weapon_plasma"); break;
                case FxStyles.ShockPush:
                    // #2278: the gloves hum during the short wind-up, then the air blast leaves the palms.
                    audio.Cue("glove_charge", 0.35f);
                    FxKit.Delay(look.Charge, () => ClientAudio.Instance?.Cue("glove_shock_blast"));
                    break;
                default: audio.Cue(FxStyleResolver.MeleeSwingCue(look.Style)); break; // melee weapons, the energy gloves, tools, fists
            }
        }
    }
}
