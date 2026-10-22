// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Shared.Definitions;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The screenshot / clip tooling's pose search (<see cref="ScreenshotDirector"/>, <see cref="ClipDirector"/>) — tooling,
    /// not gameplay: it finds a standing spot, a view direction or a vista for a shot (<see cref="PlaceForCapturePose"/>,
    /// <see cref="PlaceForCaptureNear"/>) and answers the voxel sight questions of the subject camera
    /// (<see cref="IsCaptureCellOpen"/>, <see cref="CaptureSightDistance"/>). Created on first use by
    /// <see cref="PlayerController.Capture"/> on the player's GameObject, so normal play never adds it; it moves the body
    /// only through the controller's capture hooks (<see cref="PlayerController.SetCapturePose"/>,
    /// <see cref="PlayerController.SetLookAngles"/>).
    /// </summary>
    public sealed class PlayerCapture : MonoBehaviour
    {
        /// <summary>The player this search poses (set by <see cref="PlayerController.Capture"/>).</summary>
        public PlayerController Player;

        /// <summary>Capture hook (<see cref="ScreenshotDirector"/>): place the on-foot player on REAL terrain near
        /// an anchor (the landed ship) and face back toward it, for a per-planet surface shot. Unlike the blind
        /// <see cref="PlayerController.SetCapturePose"/>, this is terrain-aware. It probes a ring of spots around the ship (radii
        /// chosen to clear the ship's footprint — the hull is stamped into the world voxels, so it can't be told
        /// apart from terrain by collider) and for each:
        /// <list type="bullet">
        ///   <item>raycasts DOWN from high above to find the true surface Y (a probe over the void or an unbaked
        ///   far chunk simply misses → the player is never dropped through the floor / off a floating island),</item>
        ///   <item>rejects deep water (chest-height block is water → would be a submerged murk),</item>
        ///   <item>requires OPEN SKY above the spot (a short up-ray that hits a ceiling means we're under the ship
        ///   hull / in a cave / under an overhang → reject), which is what reliably keeps the shot OUTDOORS.</item>
        /// </list>
        /// Returns false when no safe, open, dry footing exists near the ship (tiny island / all-water world) so
        /// the caller can skip the shot instead of writing a broken frame.</summary>
        public bool PlaceForCaptureNear(Vector3 anchor, float pitch)
        {
            if (Player.Body == null)
            {
                return false;
            }

            float half = Player.Body.height * 0.5f + Player.Body.skinWidth;
            // Stand a good way BACK from the ship on OPEN terrain, then face AWAY from the hull so the shot shows the
            // planet's landscape (the world-variety point) instead of the ship's wall/door filling the frame. The
            // spawn sits the player INSIDE the ship interior, whose glass skylight leaves "open sky" overhead — so the
            // sky check alone can't tell inside-the-hull from outside; the enclosure check below (walls on most sides →
            // indoors) is what reliably rejects an interior spot. Radii stay within the streamed-in chunk ring around
            // spawn (a far spot lands over an unbaked chunk and the down-ray simply misses); nearer radii are fallbacks
            // for small floating islands.
            float[] dists = { 20f, 17f, 24f, 14f, 28f, 12f, 32f };
            for (int di = 0; di < dists.Length; di++)
            {
                for (int a = 0; a < 8; a++)
                {
                    float ang = a * 45f * Mathf.Deg2Rad;
                    float x = anchor.x + Mathf.Sin(ang) * dists[di];
                    float z = anchor.z + Mathf.Cos(ang) * dists[di];

                    // Surface under this spot? Start the ray well above any local terrain so a hill doesn't make us
                    // start inside a collider.
                    if (!Physics.Raycast(new Vector3(x, anchor.y + 60f, z), Vector3.down, out var hit, 120f, PlayerController.WorldRayMask, QueryTriggerInteraction.Ignore)
                        || hit.collider == Player.Body)
                    {
                        continue;
                    }

                    var stand = new Vector3(x, hit.point.y + half + 0.05f, z);
                    if (Player.BlockKeyAt(stand + Vector3.up * 1.1f) == "water")
                    {
                        continue; // solid floor but chest-deep underwater — not a usable surface shot
                    }

                    // Lava has no collider either, so the down-ray hits the rock UNDER a lava lake and the spot
                    // would leave the player standing in lava — burning, with the damage flash in the frame.
                    string feet = Player.BlockKeyAt(stand + Vector3.up * 0.3f);
                    if (feet == "lava" || Player.BlockKeyAt(stand + Vector3.up * 1.1f) == "lava")
                    {
                        continue;
                    }

                    // Open sky overhead? A hit means a solid ceiling above us (cave / overhang / hull under a solid
                    // roof) → indoors. (The ship's glass skylight has no collider, so this passes for an interior spot
                    // under it — the enclosure check below is what catches those.)
                    if (Physics.Raycast(stand + Vector3.up * 0.3f, Vector3.up, out var up, 5f, PlayerController.WorldRayMask, QueryTriggerInteraction.Ignore)
                        && up.collider != Player.Body)
                    {
                        continue;
                    }

                    // Boxed in by walls? Cast head-height rays outward on all four sides; a spot inside the ship's
                    // interior room hits a wall on (almost) every side within a couple of blocks, while an outdoor
                    // spot sees open space (a single hit — e.g. the hull on the ship-facing side — is fine). This is
                    // the check that keeps the shot OUTSIDE, since the skylight fools the open-sky test above.
                    Vector3 eye = stand + Vector3.up * (half * 0.8f);
                    int walls = 0;
                    Vector3[] sides = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
                    foreach (var s in sides)
                    {
                        if (Physics.Raycast(eye, s, out var w, 5f, PlayerController.WorldRayMask, QueryTriggerInteraction.Ignore) && w.collider != Player.Body)
                        {
                            walls++;
                        }
                    }
                    if (walls >= 3)
                    {
                        continue; // enclosed on three+ sides → inside the hull, not out on the surface
                    }

                    Player.SnapTo(stand);
                    // Face AWAY from the ship (from hull → player), so the camera looks out over the terrain and the
                    // ship falls behind the player, out of frame.
                    Vector3 d = transform.position - anchor;
                    float yaw = (Mathf.Abs(d.x) + Mathf.Abs(d.z) > 0.01f)
                        ? Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg
                        : transform.eulerAngles.y;
                    Player.SetLookAngles(yaw, pitch);

                    Debug.Log($"[Capture] PlaceForCaptureNear: placed at dist={dists[di]} ang={a * 45}° walls={walls} y={stand.y:F1}");
                    return true;
                }
            }

            Debug.LogWarning("[Capture] PlaceForCaptureNear: no open outdoor spot found around the ship.");
            return false;
        }

        /// <summary>Capture hook (#2405): put the on-foot player at a spot of the requested kind near the anchor — a
        /// covered room underground ("cave"), under three blocks of water ("underwater"), in dense flora ("forest"),
        /// on dry ground beside water ("shore") or on the highest open spot around ("ridge"). "spawn" (and any unknown
        /// kind) is the plain outdoor placement of <see cref="PlaceForCaptureNear"/>. The search walks the streamed
        /// client chunks rather than the physics scene so it can look underground and into water; the caller keeps
        /// polling <see cref="PlayerController.IsCaptureGrounded"/> / <see cref="PlayerController.IsHeadUnderwater"/> (and calls this again) until the
        /// chunk collider under the spot has been cooked, because a snap onto a not-yet-cooked chunk drops the player
        /// through it. Returns false when no spot of that kind is loaded near the anchor, so the caller can fall back.</summary>
        public bool PlaceForCapturePose(string pose, Vector3 anchor, float pitch)
        {
            string kind = (pose ?? string.Empty).Trim().ToLowerInvariant();
            if (kind.Length == 0 || kind == "spawn")
            {
                return PlaceForCaptureNear(anchor, pitch);
            }

            if (Player.Body == null || Player.Game?.World == null || Player.Game.Content == null)
            {
                return false;
            }

            if (kind == "vista")
            {
                return PlaceForCaptureVista(anchor, pitch);
            }

            if (kind == "here")
            {
                return PlaceForCaptureHere(anchor, pitch);
            }

            Player.RefreshLiquidKeys();
            const int Radius = 60;
            int ax = Mathf.FloorToInt(anchor.x), ay = Mathf.FloorToInt(anchor.y), az = Mathf.FloorToInt(anchor.z);
            int yLo = kind is "cave" or "lava" ? ay - 70 : ay - 32;
            int yHi = kind == "cave" ? ay + 8 : ay + 48;
            float bestScore = float.NegativeInfinity;
            Vector3Int best = default;
            float bestYaw = 0f;
            int loaded = 0, candidates = 0;

            for (int dx = -Radius; dx <= Radius; dx += 2)
            {
                for (int dz = -Radius; dz <= Radius; dz += 2)
                {
                    float horiz = Mathf.Sqrt(dx * dx + dz * dz);
                    if (horiz < 7f)
                    {
                        continue; // the ship's footprint — its hull is stamped into the voxels
                    }

                    int x = ax + dx, z = az + dz;
                    for (int y = yLo; y <= yHi; y++)
                    {
                        if (!Player.Game.World.TryGetBlock(x, y, z, out _))
                        {
                            continue;
                        }

                        loaded++;
                        float score = CapturePoseScore(kind, x, y, z, ax, az, horiz, out float yaw);
                        if (float.IsNaN(score))
                        {
                            continue;
                        }

                        candidates++;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new Vector3Int(x, y, z);
                            bestYaw = yaw;
                        }
                    }
                }
            }

            if (float.IsNegativeInfinity(bestScore))
            {
                Debug.LogWarning($"[Capture] PlaceForCapturePose: no '{kind}' spot streamed in near the spawn (cells loaded={loaded}, y {yLo}..{yHi}, radius {Radius}).");
                return false;
            }

            Debug.Log($"[Capture] PlaceForCapturePose: '{kind}' candidates={candidates} of {loaded} loaded cells.");

            float half = Player.Body.height * 0.5f + Player.Body.skinWidth;
            var stand = new Vector3(best.x + 0.5f, best.y + half + (kind == "underwater" ? 0.1f : 0.05f), best.z + 0.5f);
            Player.SetCapturePose(stand, bestYaw, pitch);
            Debug.Log($"[Capture] PlaceForCapturePose: '{kind}' at ({best.x},{best.y},{best.z}) score={bestScore:F1} yaw={bestYaw:F0}°");
            return true;
        }

        /// <summary>Score a candidate feet cell for <see cref="PlaceForCapturePose"/>: NaN = not that kind of spot,
        /// otherwise higher is better. <paramref name="yaw"/> is where the player should look from there.</summary>
        private float CapturePoseScore(string kind, int x, int y, int z, int ax, int az, float horiz, out float yaw)
        {
            yaw = 0f;
            if (!CaptureKeyAt(x, y, z, out string feet) || !CaptureKeyAt(x, y + 1, z, out string head)
                || !CaptureKeyAt(x, y - 1, z, out string floor))
            {
                return float.NaN; // not streamed in
            }

            if (kind == "underwater")
            {
                // Feet and head in water puts the camera under the surface; more water overhead scores higher.
                if (feet != "water" || head != "water")
                {
                    return float.NaN;
                }

                int depth = 0;
                for (int i = 2; i < 12 && CaptureKeyAt(x, y + i, z, out string w) && w == "water"; i++)
                {
                    depth++;
                }

                // Open water over the bed, not the inside of a kelp wall: the plants around the spot count against it.
                int kelp = CaptureFloraCount(x, y, z, out _);
                yaw = CaptureYawAway(x, z, ax, az);
                return depth * 2f - kelp * 0.4f - horiz * 0.08f;
            }

            if (!PlayerController.IsCollidingKey(floor) || !CaptureFree(feet) || !CaptureFree(head))
            {
                return float.NaN;
            }

            switch (kind)
            {
                case "cave":
                {
                    int solidAbove = CaptureSolidAbove(x, y + 2, z, 48);
                    if (solidAbove < 4)
                    {
                        return float.NaN; // a hull roof or an overhang, not underground
                    }

                    int room = CaptureFreeCount(x, y, z);
                    if (room < 14)
                    {
                        return float.NaN; // a crack, not a passage or a room
                    }

                    yaw = CaptureYawToOpen(x, y, z);
                    return solidAbove + room * 0.25f - horiz * 0.1f;
                }

                case "forest":
                {
                    if (!CaptureOpenSky(x, y + 2, z))
                    {
                        return float.NaN;
                    }

                    int flora = CaptureFloraCount(x, y, z, out yaw);
                    return flora < 24 ? float.NaN : flora - horiz * 0.2f;
                }

                case "shore":
                {
                    if (!CaptureOpenSky(x, y + 2, z))
                    {
                        return float.NaN;
                    }

                    float d = CaptureNearestWater(x, y, z, out yaw);
                    return float.IsNaN(d) ? float.NaN : 10f - d - horiz * 0.1f;
                }

                case "ridge":
                {
                    if (!CaptureOpenSky(x, y + 2, z))
                    {
                        return float.NaN;
                    }

                    yaw = CaptureYawAway(x, z, ax, az);
                    return y - horiz * 0.05f;
                }

                case "lava":
                {
                    // Dry footing a few blocks from lava, facing it — the spot to judge the emitter light (#2407);
                    // a roof over it (a lava cave) scores higher than an open lake, and a little room to look.
                    float d = CaptureNearestKey(x, y, z, "lava", 5, -2, 0, out yaw);
                    if (float.IsNaN(d) || d < 2f)
                    {
                        return float.NaN;
                    }

                    int covered = CaptureSolidAbove(x, y + 2, z, 24) >= 3 ? 4 : 0;
                    return 8f - d + covered + CaptureFreeCount(x, y, z) * 0.1f - horiz * 0.05f;
                }

                default:
                    return float.NaN;
            }
        }

        /// <summary>The block key at a cell of the streamed client world; false when its chunk is not loaded.</summary>
        private bool CaptureKeyAt(int x, int y, int z, out string key)
        {
            if (!Player.Game.World.TryGetBlock(x, y, z, out var id))
            {
                key = null;
                return false;
            }

            key = id.IsAir ? "air" : (Player.Game.Content.BlockById(id)?.Key ?? "air");
            return true;
        }

        /// <summary>A cell the capsule can occupy: air, or a prop it walks through — never a liquid.</summary>
        private static bool CaptureFree(string key)
            => key == "air" || (!PlayerController.IsCollidingKey(key) && key != "water" && key != "lava" && !PlayerController.LiquidKeys.Contains(key));

        /// <summary>How many colliding blocks sit in the column from <paramref name="y"/> up to <paramref name="maxUp"/>
        /// cells higher (unloaded cells count as open).</summary>
        private int CaptureSolidAbove(int x, int y, int z, int maxUp)
        {
            int solid = 0;
            for (int i = 0; i < maxUp; i++)
            {
                if (CaptureKeyAt(x, y + i, z, out string k) && PlayerController.IsCollidingKey(k))
                {
                    solid++;
                }
            }

            return solid;
        }

        /// <summary>No colliding block in the 40 cells above and no parked ship over the spot (foliage is walk-through,
        /// so a tree crown still counts as open sky — that is what "forest" wants).</summary>
        private bool CaptureOpenSky(int x, int y, int z)
            => CaptureSolidAbove(x, y, z, 40) == 0 && !Player.Game.LandedShipCovers(x, y, z);

        /// <summary>Free cells in the 5×3×5 box around the feet cell — a room reads as ≥ 22 of 75.</summary>
        private int CaptureFreeCount(int x, int y, int z)
        {
            int free = 0;
            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dy = 0; dy <= 2; dy++)
                {
                    for (int dz = -2; dz <= 2; dz++)
                    {
                        if (CaptureKeyAt(x + dx, y + dy, z + dz, out string k) && CaptureFree(k))
                        {
                            free++;
                        }
                    }
                }
            }

            return free;
        }

        /// <summary>Flora and tree-crown cells within 6 blocks (ground to 9 up); the yaw points at the densest side.</summary>
        private int CaptureFloraCount(int x, int y, int z, out float yaw)
        {
            int count = 0;
            float sx = 0f, sz = 0f;
            for (int dx = -6; dx <= 6; dx++)
            {
                for (int dz = -6; dz <= 6; dz++)
                {
                    for (int dy = -1; dy <= 9; dy++)
                    {
                        if (!Player.Game.World.TryGetBlock(x + dx, y + dy, z + dz, out var id) || id.IsAir)
                        {
                            continue;
                        }

                        var def = Player.Game.Content.BlockById(id);
                        if (def != null && (def.Category == "flora" || TreeFoliage.IsKey(def.Key)))
                        {
                            count++;
                            sx += dx;
                            sz += dz;
                        }
                    }
                }
            }

            yaw = Mathf.Abs(sx) + Mathf.Abs(sz) > 0.5f ? Mathf.Atan2(sx, sz) * Mathf.Rad2Deg : 0f;
            return count;
        }

        /// <summary>Distance to the nearest water cell at foot level or one below within 4 blocks (NaN = none); the yaw
        /// faces it.</summary>
        private float CaptureNearestWater(int x, int y, int z, out float yaw)
            => CaptureNearestKey(x, y, z, "water", 4, -2, -1, out yaw);

        /// <summary>Horizontal distance to the nearest cell of <paramref name="key"/> within <paramref name="radius"/>
        /// blocks and the vertical band <paramref name="dyLo"/>..<paramref name="dyHi"/> around the feet (NaN = none);
        /// the yaw faces it.</summary>
        private float CaptureNearestKey(int x, int y, int z, string key, int radius, int dyLo, int dyHi, out float yaw)
        {
            float bestD = float.NaN;
            yaw = 0f;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    if (dx == 0 && dz == 0)
                    {
                        continue;
                    }

                    for (int dy = dyLo; dy <= dyHi; dy++)
                    {
                        if (!CaptureKeyAt(x + dx, y + dy, z + dz, out string k) || k != key)
                        {
                            continue;
                        }

                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (float.IsNaN(bestD) || d < bestD)
                        {
                            bestD = d;
                            yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
                        }
                    }
                }
            }

            return bestD;
        }

        /// <summary>The yaw of the longest free run at head height among eight directions — look down the cave, not
        /// into its wall.</summary>
        private float CaptureYawToOpen(int x, int y, int z)
        {
            int bestRun = -1;
            float yaw = 0f;
            for (int a = 0; a < 8; a++)
            {
                float ang = a * 45f * Mathf.Deg2Rad;
                int sx = Mathf.RoundToInt(Mathf.Sin(ang)), sz = Mathf.RoundToInt(Mathf.Cos(ang));
                int run = 0;
                while (run < 14 && CaptureKeyAt(x + sx * (run + 1), y + 1, z + sz * (run + 1), out string k) && CaptureFree(k))
                {
                    run++;
                }

                if (run > bestRun)
                {
                    bestRun = run;
                    yaw = a * 45f;
                }
            }

            return yaw;
        }

        /// <summary>Face away from the anchor (the landed ship), like <see cref="PlaceForCaptureNear"/>.</summary>
        private static float CaptureYawAway(int x, int z, int ax, int az)
        {
            float dx = x - ax, dz = z - az;
            return Mathf.Abs(dx) + Mathf.Abs(dz) > 0.01f ? Mathf.Atan2(dx, dz) * Mathf.Rad2Deg : 0f;
        }

        /// <summary>Capture hook (#2765): the third-person camera — the player's avatar in the world, for scale in a still —
        /// or back to first person, exactly as the view key switches it.</summary>
        public void SetCaptureThirdPerson(bool on)
        {
            if (Player.ThirdPerson == on)
            {
                return;
            }

            Player.ThirdPerson = on;
            Player.ApplyCameraMode();
        }

        /// <summary>Capture hook (#2765): the first-person eye in world space, whichever camera mode is on — a still's
        /// recording camera is placed relative to it.</summary>
        public Vector3 CaptureEyePosition => transform.TransformPoint(PlayerController.FirstPersonEye);

        /// <summary>Capture hook (#2765): the direction toward the sun that the "vista" pose keeps beside or behind the camera
        /// (null = no preference). The capture director sets it once the time of day is pinned.</summary>
        public Vector3? CaptureSunDirection { get; set; }

        /// <summary>Capture hook (#2765): the "vista" pose also counts built blocks in view (houses, lamps, machines) — for a
        /// still of a village or another site the player was sent to.</summary>
        public bool CaptureVistaFavorsBuilt { get; set; }

        private bool _vistaCached;
        private Vector3 _vistaAnchor, _vistaStand;
        private float _vistaYaw;

        /// <summary>The "vista" pose (#2765): a raised, open, dry spot near the anchor with the best view. Every column of the
        /// streamed radius gives its standable top; the highest and most prominent tops are kept, and from each of them
        /// sixteen view directions are scored through the voxels (<see cref="CaptureViewScore"/>). The pick is cached for the
        /// anchor, so the settle loop's re-snaps keep the same spot and view instead of jumping as more chunks arrive.</summary>
        private bool PlaceForCaptureVista(Vector3 anchor, float pitch)
        {
            if (_vistaCached && (anchor - _vistaAnchor).sqrMagnitude < 1f)
            {
                Player.SetCapturePose(_vistaStand, _vistaYaw, pitch);
                return true;
            }

            Player.RefreshLiquidKeys();
            const int Radius = 56;
            int ax = Mathf.FloorToInt(anchor.x), ay = Mathf.FloorToInt(anchor.y), az = Mathf.FloorToInt(anchor.z);
            var heights = new System.Collections.Generic.Dictionary<long, int>();
            var tops = new System.Collections.Generic.List<Vector3Int>();
            for (int dx = -Radius; dx <= Radius; dx += 2)
            {
                for (int dz = -Radius; dz <= Radius; dz += 2)
                {
                    float horiz = Mathf.Sqrt(dx * dx + dz * dz);
                    if (horiz > Radius)
                    {
                        continue;
                    }

                    int x = ax + dx, z = az + dz;
                    int top = CaptureColumnTop(x, z, ay - 32, ay + 48, out bool dry);
                    if (top == int.MinValue)
                    {
                        continue;
                    }

                    heights[CaptureColumnKey(x, z)] = top;
                    // Clear of the ship's footprint (its hull is stamped into the voxels) and out under the sky.
                    if (dry && horiz >= 9f && !Player.Game.LandedShipCovers(x, top, z) && CaptureOpenSky(x, top + 2, z))
                    {
                        tops.Add(new Vector3Int(x, top, z));
                    }
                }
            }

            if (tops.Count == 0)
            {
                Debug.LogWarning($"[Capture] PlaceForCaptureVista: no open, dry top streamed in near the spawn ({heights.Count} columns).");
                return false;
            }

            // Prominence: how far a top stands above the ground around it (a knoll, a ridge, a mesa edge) — the view from a
            // hill beats the view from a hollow at the same height.
            var ranked = new System.Collections.Generic.List<(Vector3Int Top, float Prominence, float Rank)>(tops.Count);
            foreach (var t in tops)
            {
                float sum = 0f;
                int n = 0;
                foreach (var (ox, oz) in ProminenceRing)
                {
                    if (heights.TryGetValue(CaptureColumnKey(t.x + ox, t.z + oz), out int h))
                    {
                        sum += h;
                        n++;
                    }
                }

                float prominence = n > 0 ? t.y - sum / n : 0f;
                float horiz = Mathf.Sqrt((t.x - ax) * (t.x - ax) + (t.z - az) * (t.z - az));
                ranked.Add((t, prominence, t.y + prominence * 1.5f - horiz * 0.04f));
            }

            ranked.Sort((a, b) => b.Rank.CompareTo(a.Rank));
            float half = Player.Body.height * 0.5f + Player.Body.skinWidth;
            float bestScore = float.NegativeInfinity, bestYaw = 0f;
            Vector3Int best = default;
            int evaluated = Mathf.Min(24, ranked.Count);
            for (int i = 0; i < evaluated; i++)
            {
                var (t, prominence, _) = ranked[i];
                // Score from about where a still's raised camera will be: a few metres above the eye.
                var eye = new Vector3(t.x + 0.5f, t.y + 1.6f + 2.5f, t.z + 0.5f);
                for (int a = 0; a < 16; a++)
                {
                    float yaw = a * 22.5f;
                    float score = CaptureViewScore(eye, yaw) + Mathf.Clamp(prominence, -4f, 12f) + (t.y - ay) * 0.15f;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestYaw = yaw;
                        best = t;
                    }
                }
            }

            _vistaStand = new Vector3(best.x + 0.5f, best.y + half + 0.05f, best.z + 0.5f);
            _vistaYaw = bestYaw;
            _vistaAnchor = anchor;
            _vistaCached = true;
            Player.SetCapturePose(_vistaStand, _vistaYaw, pitch);
            Debug.Log($"[Capture] PlaceForCaptureVista: ({best.x},{best.y},{best.z}) yaw={bestYaw:F0}° score={bestScore:F1} "
                      + $"of {evaluated} tops ({tops.Count} open, {heights.Count} columns)");
            return true;
        }

        /// <summary>The "here" pose (#2765): the player stays where they are — the spot a <c>/tp village</c> put them, in the
        /// middle of the site — and only turns to the heading whose view from a few metres up scores best (built blocks
        /// count when <see cref="CaptureVistaFavorsBuilt"/>). A raised "rig" still camera behind the player then looks
        /// over the site.</summary>
        private bool PlaceForCaptureHere(Vector3 anchor, float pitch)
        {
            Player.RefreshLiquidKeys();
            var eye = new Vector3(anchor.x, anchor.y + 1.6f + 8f, anchor.z);
            float bestScore = float.NegativeInfinity, bestYaw = 0f;
            for (int a = 0; a < 16; a++)
            {
                float score = CaptureViewScore(eye, a * 22.5f, back: 0f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestYaw = a * 22.5f;
                }
            }

            Player.SetLookAngles(bestYaw, pitch);
            Debug.Log($"[Capture] PlaceForCaptureHere: ({anchor.x:F0},{anchor.y:F0},{anchor.z:F0}) yaw={bestYaw:F0}° score={bestScore:F1}");
            return true;
        }

        /// <summary>Column offsets (even, like the scan grid) that sample the ground around a top for its prominence.</summary>
        private static readonly (int X, int Z)[] ProminenceRing =
        {
            (6, 0), (-6, 0), (0, 6), (0, -6), (4, 4), (-4, 4), (4, -4), (-4, -4),
            (12, 0), (-12, 0), (0, 12), (0, -12), (8, 8), (-8, 8), (8, -8), (-8, -8),
        };

        private static long CaptureColumnKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        /// <summary>The feet cell on top of a column (the first cell above its highest colliding block), scanning down from
        /// <paramref name="yHi"/>; <paramref name="dry"/> says a player can stand there (two free cells, no liquid on top).
        /// A sea, a lava lake or a tree crown still reports its top — a height for the relief — but not as dry.
        /// <see cref="int.MinValue"/> when nothing of the column is streamed in.</summary>
        private int CaptureColumnTop(int x, int z, int yLo, int yHi, out bool dry)
        {
            dry = false;
            for (int y = yHi; y >= yLo; y--)
            {
                if (!CaptureKeyAt(x, y, z, out string k) || k == "air")
                {
                    continue;
                }

                // A sea, a lava lake or a tree crown: a height for the relief, not a place to stand (leaves are walked
                // through, so the scan would otherwise land on the trunk inside the crown).
                if (k == "water" || k == "lava" || PlayerController.LiquidKeys.Contains(k) || TreeFoliage.IsKey(k))
                {
                    return y + 1;
                }

                if (PlayerController.IsCollidingKey(k))
                {
                    dry = CaptureKeyAt(x, y + 1, z, out string feet) && CaptureFree(feet)
                          && CaptureKeyAt(x, y + 2, z, out string head) && CaptureFree(head);
                    return y + 1;
                }
            }

            return int.MinValue;
        }

        /// <summary>How good the view along <paramref name="yaw"/> is (#2765), seen from <paramref name="back"/> metres behind
        /// <paramref name="eye"/> — where a still's rig camera sits. A ±45° fan (the width of a 65° still) at 3°, 9° and 18°
        /// below the horizon marches through the streamed voxels. Rewarded: depth (how far the rays get), relief (how much the
        /// heights they land on differ) and variety (different ground, water, lava, flora; built blocks when
        /// <see cref="CaptureVistaFavorsBuilt"/>), and the sun beside or behind the camera. Punished: a wall, a pillar or a
        /// tree crown in the first metres (two more fans, level and a little up, look for those), the landed ship in view,
        /// and looking into the sun.</summary>
        private float CaptureViewScore(Vector3 eye, float yaw, float back = 2f)
        {
            float depth = 0f, sumY = 0f, sumY2 = 0f;
            int hits = 0, near = 0, water = 0, lava = 0, flora = 0, built = 0, ship = 0;
            var kinds = new System.Collections.Generic.HashSet<string>();
            eye -= new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad)) * back;
            for (int yi = -3; yi <= 3; yi++)
            {
                float ryaw = (yaw + yi * 15f) * Mathf.Deg2Rad;
                // Something tall beside or in front of the lens (a trunk, a pillar, a wall) that the downward fan passes under.
                for (int ui = 0; ui < 2; ui++)
                {
                    float up = (ui == 0 ? 0f : 6f) * Mathf.Deg2Rad;
                    var ray = new Vector3(Mathf.Sin(ryaw) * Mathf.Cos(up), Mathf.Sin(up), Mathf.Cos(ryaw) * Mathf.Cos(up));
                    if (CaptureMarch(eye, ray, 9f, out _, out _, out _) < (Mathf.Abs(yi) <= 1 ? 9f : 6f))
                    {
                        near++;
                    }
                }

                for (int pi = 0; pi < 3; pi++)
                {
                    float rpitch = (pi == 0 ? 3f : pi == 1 ? 9f : 18f) * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Sin(ryaw) * Mathf.Cos(rpitch), -Mathf.Sin(rpitch), Mathf.Cos(ryaw) * Mathf.Cos(rpitch));
                    float d = CaptureMarch(eye, dir, 120f, out var def, out Vector3Int cell, out bool shipHit);
                    depth += Mathf.Log(1f + d);
                    if (shipHit)
                    {
                        ship++; // the landed ship in view (its own model, not voxels)
                        continue;
                    }

                    if (def == null)
                    {
                        continue; // ran out of the streamed world: open view to the horizon
                    }

                    hits++;
                    sumY += cell.y;
                    sumY2 += cell.y * cell.y;
                    kinds.Add(def.Key);
                    if (pi < 2 && d < (Mathf.Abs(yi) <= 1 ? 10f : 7f))
                    {
                        near++; // something big right in front of the lens
                    }

                    if (def.Key == "water" || def.Liquid)
                    {
                        water++;
                    }
                    else if (def.Key == "lava")
                    {
                        lava++;
                    }
                    else if (def.Category == "flora" || TreeFoliage.IsKey(def.Key) || TreeWood.IsKey(def.Key)
                             || def.Key.EndsWith("_log", System.StringComparison.Ordinal))
                    {
                        flora++; // a trunk is a tree, not a house (logs carry the "building" category; #2851: wood_log_b too)
                    }
                    else if (def.Category is "building" or "machine" or "door" or "light")
                    {
                        built++;
                    }
                }
            }

            float relief = 0f;
            if (hits > 1)
            {
                float mean = sumY / hits;
                relief = Mathf.Sqrt(Mathf.Max(0f, sumY2 / hits - mean * mean));
            }

            float score = depth
                          + Mathf.Min(relief, 10f) * 2f
                          + Mathf.Min(kinds.Count, 6) * 2f
                          + Mathf.Min(water, 4) * 2.5f
                          + Mathf.Min(lava, 3) * 3f
                          + Mathf.Min(flora, 6)
                          + (CaptureVistaFavorsBuilt ? Mathf.Min(built, 8) * 3f : 0f)
                          - near * 8f
                          - ship * 12f;

            // The landed ship anywhere in the frame (a still is about ±50° wide) and near enough to read: the few rays that
            // hit it are not penalty enough, it would still become the subject of the picture.
            if (Player.Game.LandedShipInView(eye, yaw, 50f, 90f))
            {
                score -= 60f;
            }

            if (CaptureSunDirection is Vector3 sun)
            {
                var view = new Vector2(Mathf.Sin(yaw * Mathf.Deg2Rad), Mathf.Cos(yaw * Mathf.Deg2Rad));
                var toSun = new Vector2(sun.x, sun.z);
                if (toSun.sqrMagnitude > 1e-4f)
                {
                    float c = Vector2.Dot(view, toSun.normalized);
                    score += c > 0.6f ? -15f : c < 0.2f ? 4f : 0f; // into the sun washes the frame out; side or back light models it
                }
            }

            return score;
        }

        /// <summary>March a view ray through the streamed voxels: metres to the first block that hides what lies behind it
        /// (a colliding block, a liquid or a tree crown — the crown is walk-through but not see-through), with that block and
        /// its cell. Returns <paramref name="max"/> and a null block when the ray gets through or leaves the streamed world.
        /// <paramref name="throughWater"/>: water is the medium, not a wall (a camera under the sea).</summary>
        private float CaptureMarch(Vector3 from, Vector3 dir, float max, out BlockDefinition hit, out Vector3Int cell,
            out bool ship, bool throughWater = false)
        {
            hit = null;
            cell = default;
            ship = false;
            for (float d = 0.5f; d <= max; d += d < 12f ? 0.5f : 1f)
            {
                var p = from + dir * d;
                int x = Mathf.FloorToInt(p.x), y = Mathf.FloorToInt(p.y), z = Mathf.FloorToInt(p.z);
                if (Player.Game.LandedShipBodyAt(x, y, z))
                {
                    ship = true; // a parked ship is its own model, not voxels — it hides what lies behind it all the same
                    cell = new Vector3Int(x, y, z);
                    return d;
                }

                if (!Player.Game.World.TryGetBlock(x, y, z, out var id))
                {
                    if (d > 24f)
                    {
                        return max; // past the streamed ring
                    }

                    continue;
                }

                if (id.IsAir)
                {
                    continue;
                }

                var def = Player.Game.Content.BlockById(id);
                if (def == null || (throughWater && def.Key == "water"))
                {
                    continue;
                }

                if (def.Liquid || def.Key == "water" || def.Key == "lava" || TreeFoliage.IsKey(def.Key) || PlayerController.IsCollidingKey(def.Key))
                {
                    hit = def;
                    cell = new Vector3Int(x, y, z);
                    return d;
                }
            }

            return max;
        }

        /// <summary>Capture hook (#2765): the cell at <paramref name="p"/> is streamed in and a camera may sit in it — air or a
        /// walk-through prop, no liquid, no solid block and no tree crown. <paramref name="inWater"/>: water counts as open (a
        /// camera under the sea).</summary>
        public bool IsCaptureCellOpen(Vector3 p, bool inWater = false)
            => Player.Game?.World != null && Player.Game.Content != null
               && CaptureKeyAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z), out string k)
               && ((inWater && k == "water") || (CaptureFree(k) && !TreeFoliage.IsKey(k)))
               && !Player.Game.LandedShipBodyAt(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));

        /// <summary>Capture hook (#2765): metres from <paramref name="from"/> along <paramref name="dir"/> to the first block
        /// that hides the view (solid, liquid or a tree crown), up to <paramref name="max"/> — also returned when the ray gets
        /// through or leaves the streamed world. <paramref name="throughWater"/>: water does not block (under the sea).</summary>
        public float CaptureSightDistance(Vector3 from, Vector3 dir, float max, bool throughWater = false)
        {
            if (Player.Game?.World == null || Player.Game.Content == null || dir.sqrMagnitude < 1e-6f)
            {
                return max;
            }

            Player.RefreshLiquidKeys();
            return CaptureMarch(from, dir.normalized, max, out _, out _, out _, throughWater);
        }
    }
}
