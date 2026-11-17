// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The way a window leaves: it fades out over <see cref="Seconds"/> instead of vanishing in one frame — the mirror
    /// of the open effect. The panel itself is closed at once (its state, the cursor, the input); only the picture
    /// lingers, and it takes no clicks while it does. At the end the root is deactivated (a panel kept for its next
    /// open) or destroyed (a panel built per open). Instant under "Reduced effects" and outside play mode.
    /// </summary>
    public sealed class UiClosing : MonoBehaviour
    {
        /// <summary>How long a window takes to fade out.</summary>
        public const float Seconds = 0.12f;

        private CanvasGroup _group;
        private bool _destroy;
        private float _left;

        /// <summary>Fades <paramref name="root"/> out, then deactivates it — or destroys it with <paramref name="destroy"/>.</summary>
        public static void FadeOut(GameObject root, bool destroy)
        {
            if (root == null)
            {
                return;
            }

            if (UiKit.ReducedMotion || !Application.isPlaying || !root.activeInHierarchy)
            {
                Finish(root, destroy);
                return;
            }

            var group = root.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = root.AddComponent<CanvasGroup>();
            }

            group.interactable = false;   // a closing window takes no more clicks …
            group.blocksRaycasts = false; // … and lets them through to what is behind it
            var closing = root.GetComponent<UiClosing>();
            if (closing == null)
            {
                closing = root.AddComponent<UiClosing>();
            }

            closing._group = group;
            closing._destroy = destroy;
            closing._left = Seconds;
            closing.enabled = true;
        }

        /// <summary>Stops a fade in progress and makes <paramref name="root"/> a normal, clickable window again — the
        /// panel was re-opened before it had gone.</summary>
        public static void Cancel(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var closing = root.GetComponent<UiClosing>();
            if (closing != null)
            {
                closing.enabled = false;
            }

            var group = root.GetComponent<CanvasGroup>();
            if (group != null)
            {
                group.alpha = 1f;
                group.interactable = true;
                group.blocksRaycasts = true;
            }
        }

        private void Update()
        {
            _left -= Time.unscaledDeltaTime;
            if (_group != null)
            {
                _group.alpha = Mathf.Clamp01(_left / Seconds);
            }

            if (_left <= 0f)
            {
                enabled = false;
                Finish(gameObject, _destroy);
            }
        }

        private static void Finish(GameObject root, bool destroy)
        {
            if (destroy)
            {
                if (Application.isPlaying)
                {
                    Destroy(root);
                }
                else
                {
                    DestroyImmediate(root);
                }

                return;
            }

            root.SetActive(false);
            Cancel(root); // the next open finds it whole
        }
    }
}
