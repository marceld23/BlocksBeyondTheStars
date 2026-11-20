// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The lively places: the signs over the doors. A building that offers something (the market, the workshop, the tavern,
    /// the clinic …) hangs a painted board beside its door, so the player reads a street from the outside instead of opening
    /// every door. The server names them per place (<see cref="NetPlace.Signs"/>: the door's cell, the wall it is in, the
    /// building's function); this view hangs a board for every sign near the player — a thin lit slab on an iron arm, at
    /// right angles to the wall like an inn sign, showing the icon <c>place_sign_&lt;function&gt;</c> (a plain board in
    /// the place's colour when the icon is not bundled). No collider, no interaction: it is a picture.
    /// </summary>
    public sealed class PlaceSignView : MonoBehaviour
    {
        public GameBootstrap Game;

        /// <summary>Blocks around the player within which signs hang (beyond it a board is a few pixels).</summary>
        private const float Range = 64f;

        /// <summary>The most boards at once (a metropolis street corner).</summary>
        private const int MaxSigns = 32;

        private const float LookSeconds = 0.7f;

        /// <summary>The board: edge length, thickness, and how high its middle hangs over the door's floor.</summary>
        private const float BoardSize = 0.64f, BoardThick = 0.07f, BoardHeight = 2.42f;

        private static readonly Color Iron = new Color(0.16f, 0.15f, 0.15f);
        private static readonly Color PlainBoard = new Color(0.62f, 0.47f, 0.3f);
        private static Shader _lit;

        private readonly Dictionary<long, GameObject> _boards = new Dictionary<long, GameObject>();
        private readonly HashSet<long> _wanted = new HashSet<long>();
        private readonly List<long> _stale = new List<long>();
        private float _lookAt;

        private void Update()
        {
            if (Game == null || Time.unscaledTime < _lookAt)
            {
                return;
            }

            _lookAt = Time.unscaledTime + LookSeconds;
            _wanted.Clear();
            var places = Game.Places;
            bool show = Game.World != null && !Game.InSpace && !Game.SpaceViewActive && places != null;
            if (show)
            {
                var player = Game.PlayerPosition;
                foreach (var place in places)
                {
                    if (place.Signs == null || place.Signs.Length == 0
                        || !PlaceTracker.Contains(place, player.x, player.y, player.z, Game.Circumference, (int)Range))
                    {
                        continue;
                    }

                    foreach (var sign in place.Signs)
                    {
                        Consider(place, sign, player);
                    }
                }
            }

            _stale.Clear();
            foreach (var pair in _boards)
            {
                if (!_wanted.Contains(pair.Key))
                {
                    _stale.Add(pair.Key);
                }
            }

            foreach (long key in _stale)
            {
                Destroy(_boards[key]);
                _boards.Remove(key);
            }
        }

        /// <summary>Hangs (or keeps, re-placed across the world's seam) the board of one sign when it is near enough.</summary>
        private void Consider(NetPlace place, NetPlaceSign sign, Vector3 player)
        {
            var at = Game.ScenePos(sign.X + 0.5f, sign.Y, sign.Z + 0.5f); // the copy nearest the player across the seam
            if ((at - player).sqrMagnitude > Range * Range || (_wanted.Count >= MaxSigns && !_boards.ContainsKey(KeyOf(sign))))
            {
                return;
            }

            long key = KeyOf(sign);
            _wanted.Add(key);
            if (!_boards.TryGetValue(key, out var board) || board == null)
            {
                board = Build(place, sign);
                _boards[key] = board;
            }

            board.transform.position = BoardPosition(at, sign.Side);
        }

        private static long KeyOf(NetPlaceSign s)
            => ((long)(s.X & 0xFFFFF) << 44) | ((long)(s.Z & 0xFFFFF) << 24) | ((long)(s.Y & 0xFFF) << 12) | s.Side;

        /// <summary>The way out of a wall, by its side (the wire's 0 −Z, 1 +Z, 2 −X, 3 +X).</summary>
        internal static Vector3 Outward(byte side) => side switch
        {
            0 => new Vector3(0f, 0f, -1f),
            1 => new Vector3(0f, 0f, 1f),
            2 => new Vector3(-1f, 0f, 0f),
            _ => new Vector3(1f, 0f, 0f),
        };

        /// <summary>
        /// Where a board's middle hangs for a door whose floor cell is centred at <paramref name="door"/>: one step out of
        /// the wall and one step beside the (two-wide) doorway on its lower side — the cell under the end of the awning —
        /// above head height, so nobody walks into the picture.
        /// </summary>
        internal static Vector3 BoardPosition(Vector3 door, byte side)
        {
            var along = side < 2 ? new Vector3(-1f, 0f, 0f) : new Vector3(0f, 0f, -1f);
            return door + Outward(side) + along + new Vector3(0f, BoardHeight, 0f);
        }

        private GameObject Build(NetPlace place, NetPlaceSign sign)
        {
            var root = new GameObject("place_sign_" + sign.Fn);
            root.transform.SetParent(transform, false);
            // The board stands at right angles to the wall: its faces look along the street.
            root.transform.rotation = sign.Side < 2 ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

            var icon = UiKit.Icon("place_sign_" + sign.Fn);
            var board = Part(root.transform, new Vector3(0f, 0f, 0f), new Vector3(BoardThick, BoardSize, BoardSize));
            board.sharedMaterial = icon != null
                ? Lit("icon|" + sign.Fn, Color.white, icon.texture)
                : Lit("plain|" + place.Color, place.Color > 0 ? HudUi.PlaceBannerColor(place.Color) * 0.8f : PlainBoard, null);

            // The arm it hangs from: from the wall out over the board (local Z is the way out of the wall or back into it —
            // the arm is symmetric, so one shape serves all four sides).
            var arm = Part(root.transform, new Vector3(0f, (BoardSize / 2f) + 0.06f, 0f), new Vector3(0.05f, 0.05f, 1.0f));
            arm.sharedMaterial = Lit("iron", Iron, null);
            return root;
        }

        private static Renderer Part(Transform parent, Vector3 local, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>()); // a picture: nobody bumps into it and no ray stops at it
            go.transform.SetParent(parent, false);
            go.transform.localPosition = local;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return r;
        }

        private static Material Lit(string key, Color srgb, Texture tex)
        {
            if (_lit == null)
            {
                _lit = Shader.Find("BlocksBeyondTheStars/LitColor") ?? Shader.Find("Unlit/Color");
            }

            return FxKit.Cached(_lit.name, "place_sign|" + key, m =>
            {
                m.color = ShaderColor.Srgb(new Color(srgb.r, srgb.g, srgb.b, 1f));
                if (tex != null && m.HasProperty("_MainTex"))
                {
                    m.mainTexture = tex;
                }
            });
        }

        private void OnDisable()
        {
            foreach (var board in _boards.Values)
            {
                if (board != null)
                {
                    Destroy(board);
                }
            }

            _boards.Clear();
        }
    }
}
