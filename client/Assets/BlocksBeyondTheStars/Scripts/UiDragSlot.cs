// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// Drag and drop for a slot, with the mouse: press on a filled slot, drag — its icon follows the pointer — and let
    /// go over another slot of the same <see cref="Group"/>; the drop calls <see cref="OnDropFrom"/> on the target
    /// with the source's <see cref="Index"/>. It is a second way in, not a second rule set: the callback is the same
    /// move the click-to-pick sends (#2110), and the server validates it as before. A finger never drags a slot — its
    /// drag scrolls the list as it always did (the events are handed on to the scroll view), and the pad keeps
    /// click-to-pick.
    /// </summary>
    public sealed class UiDragSlot : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        /// <summary>Slots only accept drops from their own group (the personal grid, the cargo list …).</summary>
        public string Group = string.Empty;

        /// <summary>This slot's index within its group.</summary>
        public int Index;

        /// <summary>False for an empty slot: it takes drops but cannot be dragged.</summary>
        public bool Filled;

        /// <summary>Called on the TARGET slot with the source slot's index.</summary>
        public Action<int> OnDropFrom;

        private static UiDragSlot _source;
        private static GameObject _ghost;
        private bool _passToScroll;

        /// <summary>Makes <paramref name="slot"/> draggable within <paramref name="group"/>.</summary>
        public static void Attach(GameObject slot, string group, int index, bool filled, Action<int> onDropFrom)
        {
            if (slot == null)
            {
                return;
            }

            var drag = slot.GetComponent<UiDragSlot>();
            if (drag == null)
            {
                drag = slot.AddComponent<UiDragSlot>();
            }

            drag.Group = group ?? string.Empty;
            drag.Index = index;
            drag.Filled = filled;
            drag.OnDropFrom = onDropFrom;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            // A finger scrolls; an empty slot has nothing to carry: both hand the drag on to the list behind.
            _passToScroll = eventData.pointerId >= 0 || !Filled;
            if (_passToScroll)
            {
                Pass(eventData, ExecuteEvents.beginDragHandler);
                return;
            }

            _source = this;
            ClearGhost();
            var icon = GetComponentInChildren<RawImage>();
            var canvas = GetComponentInParent<Canvas>();
            if (icon == null || canvas == null)
            {
                return;
            }

            _ghost = new GameObject("DragGhost", typeof(RectTransform));
            _ghost.transform.SetParent(canvas.rootCanvas.transform, false);
            var raw = _ghost.AddComponent<RawImage>();
            raw.texture = icon.texture;
            raw.uvRect = icon.uvRect;
            raw.color = new Color(icon.color.r, icon.color.g, icon.color.b, 0.85f);
            raw.raycastTarget = false; // the drop must reach the slot under the pointer
            var rt = (RectTransform)_ghost.transform;
            rt.sizeDelta = icon.rectTransform.rect.size;
            _ghost.AddComponent<UiDragGhost>().Source = this;
            MoveGhost(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_passToScroll)
            {
                Pass(eventData, ExecuteEvents.dragHandler);
                return;
            }

            MoveGhost(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_passToScroll)
            {
                _passToScroll = false;
                Pass(eventData, ExecuteEvents.endDragHandler);
                return;
            }

            ClearGhost();
            _source = null;
        }

        public void OnDrop(PointerEventData eventData)
        {
            var from = _source;
            if (from == null || from == this || from.Group != Group || OnDropFrom == null)
            {
                return;
            }

            ClearGhost();
            _source = null;
            OnDropFrom(from.Index);
        }

        private static void MoveGhost(PointerEventData eventData)
        {
            if (_ghost != null)
            {
                _ghost.transform.position = eventData.position; // an overlay canvas: world space is screen space
            }
        }

        internal static void ClearGhost()
        {
            if (_ghost != null)
            {
                Destroy(_ghost);
                _ghost = null;
            }
        }

        private void Pass<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> function)
            where T : IEventSystemHandler
        {
            if (transform.parent != null)
            {
                ExecuteEvents.ExecuteHierarchy(transform.parent.gameObject, eventData, function);
            }
        }
    }

    /// <summary>The icon that follows the pointer during a slot drag. It removes itself when the slot it came from is
    /// gone (the list was rebuilt under the drag) or the mouse button is up without an end-drag having arrived.</summary>
    public sealed class UiDragGhost : MonoBehaviour
    {
        public UiDragSlot Source;

        private void Update()
        {
            if (Source == null || !Input.GetMouseButton(0))
            {
                UiDragSlot.ClearGhost();
            }
        }
    }
}
