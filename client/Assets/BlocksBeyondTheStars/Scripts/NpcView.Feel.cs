// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The villagers in the feel pass: whoever speaks moves the mouth. Each clip of an NPC's babble opens a
    /// Sounder's mouth as before (#3355) and now moves a villager's stock mouth for as long as the clip plays, so a
    /// speech bubble has a speaker one can see.
    /// </summary>
    public sealed partial class NpcView
    {
        private void PulseMouth(Npc n, float clipSeconds)
        {
            n.SounderRig?.Pulse(0.8f); // #3355: a Sounder's mouth opens as it hums
            if (n.Avatar != null)
            {
                n.Avatar.Talk(Mathf.Max(0.25f, clipSeconds));
            }
        }

        /// <summary>The two arms-up gestures (#2593 the cheer, #3769 the startle); the NPC's work swings wait meanwhile.</summary>
        private static void PlayArmsUp(Npc n, byte gesture)
        {
            bool startled = gesture == BlocksBeyondTheStars.Networking.Messages.NpcGestures.HandsUp;
            if (startled)
            {
                n.Avatar.Startle();
            }
            else
            {
                n.Avatar.Cheer();
            }

            n.GestureTimer = Mathf.Max(n.GestureTimer, startled ? 2f : 1.5f);
        }

        /// <summary>The lively places: an NPC's gesture without a line (two villagers nodding in passing, a dancer at a
        /// festival) arrives on its own message and plays like one that came with a line.</summary>
        private void OnEmote(BlocksBeyondTheStars.Networking.Messages.NpcEmote m) => Gesture(m.NpcId, m.Gesture);

        /// <summary>The lively places: the emotes a player's figure already knows — point, shake the head, dance, clap,
        /// bow — played by an NPC that stands (nobody dances from a chair or in bed). Its work swings wait meanwhile.
        /// A gesture this client does not know does nothing.</summary>
        private static void PlayNpcEmote(Npc n, byte gesture)
        {
            if (n.Pose != 0)
            {
                return;
            }

            byte emote;
            float seconds;
            switch (gesture)
            {
                case BlocksBeyondTheStars.Networking.Messages.NpcGestures.Point:
                    emote = BlocksBeyondTheStars.Networking.Messages.PlayerEmotes.Point;
                    seconds = BlocksBeyondTheStars.Client.Feel.EmoteCurves.PointSeconds;
                    break;
                case BlocksBeyondTheStars.Networking.Messages.NpcGestures.HeadShake:
                    emote = BlocksBeyondTheStars.Networking.Messages.PlayerEmotes.HeadShake;
                    seconds = BlocksBeyondTheStars.Client.Feel.EmoteCurves.HeadShakeSeconds;
                    break;
                case BlocksBeyondTheStars.Networking.Messages.NpcGestures.Dance:
                    emote = BlocksBeyondTheStars.Networking.Messages.PlayerEmotes.Dance;
                    seconds = BlocksBeyondTheStars.Client.Feel.EmoteCurves.DanceSeconds;
                    break;
                case BlocksBeyondTheStars.Networking.Messages.NpcGestures.Clap:
                    emote = BlocksBeyondTheStars.Networking.Messages.PlayerEmotes.Clap;
                    seconds = BlocksBeyondTheStars.Client.Feel.EmoteCurves.ClapSeconds;
                    break;
                case BlocksBeyondTheStars.Networking.Messages.NpcGestures.Bow:
                    emote = BlocksBeyondTheStars.Networking.Messages.PlayerEmotes.Bow;
                    seconds = BlocksBeyondTheStars.Client.Feel.EmoteCurves.BowSeconds;
                    break;
                default:
                    return;
            }

            n.Avatar.PlayEmote(emote);
            n.GestureTimer = Mathf.Max(n.GestureTimer, seconds + 0.3f);
        }

        // ---------------------------------------------------------------- the lively places: tools one can see and hear

        /// <summary>A swing is felt only from this close (metres): sparks and a broom's stroke are small things.</summary>
        private const float SwingFxRadius = 18f;

        private static readonly Color LanternLight = new Color(1f, 0.8f, 0.45f);
        private static readonly Color AnvilSpark = new Color(1f, 0.72f, 0.3f);
        private float _sweepHeardAt;

        /// <summary>The night watch's lantern shines: a small warm light at the hand that walks with it (it lights the
        /// blocks around through the FX lights, which keep their own budget). Switched with the thing in the hand.</summary>
        private static void SetLantern(Npc n, bool on)
        {
            if (!on)
            {
                if (n.Lantern != null)
                {
                    n.Lantern.enabled = false;
                }

                return;
            }

            if (n.Lantern == null && n.Go != null)
            {
                var go = new GameObject("Lantern");
                go.transform.SetParent(n.Go.transform, false);
                go.transform.localPosition = new Vector3(0.35f, 1.05f, 0.3f);
                n.Lantern = go.AddComponent<Light>();
                n.Lantern.type = LightType.Point;
                n.Lantern.range = 7f;
                n.Lantern.intensity = 1.6f;
                n.Lantern.color = LanternLight;
                n.Lantern.shadows = LightShadows.None;
                FxLightBridge.Mirror(n.Lantern, flicker: true);
            }

            if (n.Lantern != null)
            {
                n.Lantern.enabled = true;
            }
        }

        /// <summary>What a work swing shows and sounds like near the player, when the blow lands: sparks off the
        /// craftsman's hammer, the stroke of a broom. Everything else swings silently as before.</summary>
        private void SwingFx(Npc n)
        {
            bool hammer = n.Held == "npc_hammer", broom = n.Held == NpcWorkTools.Broom;
            if ((!hammer && !broom) || n.Go == null || ListenerDistance(n) > SwingFxRadius)
            {
                return;
            }

            var go = n.Go;
            FxKit.Delay(NpcWorkTools.ChopImpactSeconds, () =>
            {
                if (go == null)
                {
                    return;
                }

                float s = go.transform.localScale.y;
                if (hammer)
                {
                    var at = go.transform.position + (go.transform.forward * (0.55f * s)) + (Vector3.up * (1.0f * s));
                    FxKit.Burst(FxKit.Kind.Sparks, at, 5, Vector3.up, 60f, 1.5f, 3.5f, 0.02f, 0.035f, 0.15f, 0.35f, AnvilSpark, Color.white);
                }
                else if (Time.unscaledTime - _sweepHeardAt >= NpcWorkTools.ChopGapSeconds && !NarratorBusy)
                {
                    _sweepHeardAt = Time.unscaledTime; // two sweepers never stack
                    var at = go.transform.position + (go.transform.forward * (0.6f * s)) + (Vector3.up * 0.1f);
                    ClientAudio.Instance?.At(NpcWorkTools.SweepClip, at, Random.Range(0.9f, 1.1f), NpcWorkTools.SweepVolume);
                }
            }, go);
        }

        /// <summary>The top of the pilot's crate in the figure's own space: the underside of a seated figure's thighs.</summary>
        private const float CrateSeatTop = 0.8f;

        private static readonly Color CrateWood = new Color(0.56f, 0.39f, 0.21f);
        private static readonly Color CrateBand = new Color(0.34f, 0.23f, 0.13f);

        /// <summary>The lively places: the crate a landed trader's pilot sits on in front of its ship
        /// (<see cref="NpcWorkTools.CrateSeat"/>). Render-only, a child of the figure: it stands on the ground under the
        /// seated figure — whose root the seated pose lowers by <see cref="PlayerAvatar.SeatDrop"/> — and reaches up to
        /// the underside of its thighs.</summary>
        private static void SetCrateSeat(Npc n, bool on)
        {
            if (n.CrateSeat == null)
            {
                if (!on || n.Go == null)
                {
                    return;
                }

                float s = Mathf.Max(0.2f, n.Go.transform.localScale.y);
                float bottom = (PlayerAvatar.SeatDrop - 0.02f) / s;
                var scope = ModelKit.BeginOwned();
                try
                {
                    n.CrateSeat = ModelKit.Cube("CrateSeat", n.Go.transform, new Vector3(0f, (CrateSeatTop + bottom) / 2f, 0.1f),
                        new Vector3(0.6f / s, Mathf.Max(0.1f, CrateSeatTop - bottom), 0.6f / s), ModelKit.Lit(CrateWood));
                    ModelKit.Cube("Band", n.CrateSeat.transform, new Vector3(0f, 0.34f, 0f), new Vector3(1.05f, 0.16f, 1.05f), ModelKit.Lit(CrateBand));
                }
                finally
                {
                    ModelKit.EndOwned(scope, n.CrateSeat);
                }
            }

            if (n.CrateSeat.activeSelf != on)
            {
                n.CrateSeat.SetActive(on);
            }
        }
    }
}
