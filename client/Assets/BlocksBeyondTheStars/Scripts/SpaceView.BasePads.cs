// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Networking.Messages;
using UnityEngine;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// #3070: the bases' landing pads on the land chooser. A base's pads stand within a few dozen blocks of its core — one
    /// pixel on this map — so each base gets ONE marker at its core, coloured by whose base it is (teal own, green an
    /// ally's, amber someone else's) and captioned with how many of its pads take this player right now ("Home · 2/3").
    /// Picking it lists the base's pads over the chooser; a pad the player may land on lands there. The communal pads keep
    /// their numbered markers (and the number keys) — a base pad never becomes "Pad 0".
    /// </summary>
    public sealed partial class SpaceView
    {
        private const float BaseMarkerSize = 40f;
        private const float BasePadRowH = 60f;

        private GameObject _basePadListGo; // the open base's pad list over the chooser, or null

        private string PadLoc(string key) => Loc(key, key);

        /// <summary>Draws one clickable marker per base with pads in <paramref name="pads"/>, at its core on the chooser's
        /// equirect strip — the same projection as the communal pads (<paramref name="left"/>/<paramref name="top"/> are
        /// the strip's corner in the panel, <paramref name="circ"/> the body's circumference, <paramref name="latP"/> its
        /// latitude period).</summary>
        private void DrawBaseMarkers(Transform panel, NetLandingPad[] pads, float left, float top, float mapW, float mapH, int circ, int latP)
        {
            foreach (var g in BasePadView.GroupByBase(pads))
            {
                float u = Mathf.Repeat(g.BaseX / (float)circ, 1f);
                float vNorm = Mathf.Clamp01(0.5f + g.BaseZ / (float)latP);
                float mx = Mathf.Clamp(left + u * mapW - BaseMarkerSize * 0.5f, left, left + mapW - BaseMarkerSize);
                float my = Mathf.Clamp(top + (1f - vNorm) * mapH - BaseMarkerSize * 0.5f, top, top + mapH - BaseMarkerSize);
                var group = g; // captured by the click
                var col = BasePadText.RelationColor(g.Relation);
                var btn = UiKit.AddButton(panel, mx, my, BaseMarkerSize, BaseMarkerSize, "⌂", () => OpenBasePadList(group));
                if (btn != null)
                {
                    if (btn.TryGetComponent<Image>(out var img))
                    {
                        img.color = new Color(col.r * 0.45f, col.g * 0.45f, col.b * 0.45f, 0.98f);
                    }

                    if (btn.GetComponentInChildren<Text>() is { } glyph)
                    {
                        UiKit.Place(glyph.gameObject, 0f, 0f, BaseMarkerSize, BaseMarkerSize);
                        glyph.alignment = TextAnchor.MiddleCenter;
                    }
                }

                string caption = $"{BasePadText.BaseName(g.BaseName, PadLoc)} · {g.Landable}/{g.Pads.Count}";
                UiKit.AddText(panel, mx - 60f, my + BaseMarkerSize, BaseMarkerSize + 120f, 18f, caption, 12, col, TextAnchor.UpperCenter);
            }
        }

        /// <summary>Whether any base pad in the list takes this player right now (the chooser's "all taken" line).</summary>
        private static bool AnyBasePadLandable(NetLandingPad[] pads)
        {
            foreach (var g in BasePadView.GroupByBase(pads))
            {
                if (g.Landable > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Lists one base's pads over the chooser: name, state and who may land; a pad this player may land on
        /// carries a Land button. Built last on the chooser panel, so it draws and takes clicks over every widget of it;
        /// the chooser beneath goes non-interactable meanwhile, so the pad's stick walks only this list.</summary>
        private void OpenBasePadList(BasePadView.Group g)
        {
            CloseBasePadList();
            if (_landMapGo == null || g == null)
            {
                return;
            }

            g = FreshBaseGroup(g);
            SetLandMapInteractable(false);
            var size = ((RectTransform)_landMapGo.transform).sizeDelta;
            var scrim = UiKit.AddImage(_landMapGo.transform, 0f, 0f, size.x, size.y, UiKit.SolidSprite, new Color(0f, 0f, 0f, 0.6f));
            scrim.raycastTarget = true; // swallow clicks behind the list
            _basePadListGo = scrim.gameObject;
            var own = _basePadListGo.AddComponent<CanvasGroup>();
            own.ignoreParentGroups = true; // the chooser's group is switched off; this list stays live

            const float w = 560f;
            float h = 70f + g.Pads.Count * BasePadRowH + 70f;
            var dialog = UiKit.AddDialogPanel(_basePadListGo.transform, (size.x - w) * 0.5f, Mathf.Max(8f, (size.y - h) * 0.5f), w, h);

            string title = "⌂ " + BasePadText.BaseName(g.BaseName, PadLoc);
            if (g.Relation != NetBase.RelationOwn && !string.IsNullOrEmpty(g.OwnerName))
            {
                title += "  —  " + g.OwnerName;
            }

            UiKit.AddText(dialog, 20f, 14f, w - 40f, 30f, title, 20, BasePadText.RelationColor(g.Relation), TextAnchor.MiddleLeft, FontStyle.Bold);
            UiKit.AddText(dialog, 20f, 44f, w - 40f, 20f, PadLoc("ui.basepad.list_hint"), 13, UiKit.CyanDim, TextAnchor.MiddleLeft);
            for (int i = 0; i < g.Pads.Count; i++)
            {
                AddBasePadRow(dialog, g.Pads[i], i + 1, 70f + i * BasePadRowH, w);
            }

            UiKit.AddButton(dialog, (w - 200f) * 0.5f, h - 58f, 200f, 44f, PadLoc("ui.action.close"), () => CloseBasePadList());
        }

        /// <summary>The base's pads as the latest list for the chooser's body has them — the map is built once per body, and
        /// a pad may have been taken, freed or locked since (the server re-sends the list then).</summary>
        private BasePadView.Group FreshBaseGroup(BasePadView.Group g)
        {
            var pads = Game != null && Game.LandingPadsBody == _choosePadBody ? Game.LandingPads : null;
            foreach (var fresh in BasePadView.GroupByBase(pads))
            {
                if (fresh.BaseX == g.BaseX && fresh.BaseY == g.BaseY && fresh.BaseZ == g.BaseZ)
                {
                    return fresh;
                }
            }

            return g;
        }

        /// <summary>One pad's row in the open base's list.</summary>
        private void AddBasePadRow(Transform dialog, NetLandingPad pad, int number, float y, float w)
        {
            bool landable = BasePadView.CanLand(pad);
            var row = UiKit.AddPanel(dialog, 16f, y, w - 32f, BasePadRowH - 6f, new Color(0.07f, 0.14f, 0.24f, 0.95f));
            float textW = w - 32f - 196f;
            UiKit.AddText(row.transform, 12f, 4f, textW, 26f, BasePadText.Name(pad, number, PadLoc), 18,
                landable ? UiKit.TextCol : BasePadText.LockedCol, TextAnchor.MiddleLeft, FontStyle.Bold);
            string line = $"{BasePadText.State(pad, PadLoc)}  ·  {BasePadText.Access(pad.Access, PadLoc)}";
            UiKit.AddText(row.transform, 12f, 28f, textW, 22f, line, 13, BasePadText.MarkerColor(pad), TextAnchor.MiddleLeft);
            if (landable)
            {
                var target = pad; // captured by the click
                UiKit.AddButton(row.transform, w - 32f - 182f, 7f, 172f, 40f, PadLoc("ui.basepad.land"), () => LandOnBasePad(target));
            }
        }

        /// <summary>Closes the open base's pad list (Esc / pad B / its Close button). False when none was open.</summary>
        private bool CloseBasePadList()
        {
            if (_basePadListGo == null)
            {
                return false;
            }

            Destroy(_basePadListGo);
            _basePadListGo = null;
            SetLandMapInteractable(true);
            return true;
        }

        /// <summary>Switches the chooser's own controls on or off (a canvas group on its panel) while a base's list is open.</summary>
        private void SetLandMapInteractable(bool on)
        {
            if (_landMapGo == null)
            {
                return;
            }

            if (!_landMapGo.TryGetComponent<CanvasGroup>(out var group))
            {
                group = _landMapGo.AddComponent<CanvasGroup>();
            }

            group.interactable = on;
        }

        /// <summary>Touches down on a base pad and tears the chooser down — <see cref="LandOnPad"/> for a pad addressed by
        /// its cell. The descent flies at the body, as for a communal pad; the server parks the ship on the pad.</summary>
        private void LandOnBasePad(NetLandingPad pad)
        {
            // As in LandOnPad: remember the destination before _choosePadBody is cleared — the descent reads it a frame later.
            _landDestBody = _choosePadBody;
            Game.Network?.SendLeaveSpaceToBasePad(_choosePadBody, pad.CellX, pad.CellY, pad.CellZ);
            _confirmLand = false;
            _choosePadBody = null;
            HideLandMap();
        }
    }
}
