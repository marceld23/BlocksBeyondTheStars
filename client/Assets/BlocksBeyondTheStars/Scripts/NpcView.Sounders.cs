// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.Collections.Generic;
using BlocksBeyondTheStars.Networking.Messages;
using BlocksBeyondTheStars.Shared.Sounders;
using UnityEngine;

namespace BlocksBeyondTheStars.Client
{
    /// <summary>
    /// The Sounders as NPCs (#3355, #3356). An NPC whose look is <see cref="SoundersIds.Sounder"/> or <see cref="SoundersIds.Echo"/>
    /// (the server's <c>NetNpc.Look</c>) wears the Sounder rig (<see cref="CreatureBuilder.BuildSounderNpc"/>) under its root
    /// instead of the human avatar — the avatar is still built, hidden, so every pose, gesture and tool hook of the view keeps
    /// working — at its own height (<see cref="SounderRules.HeightFor"/>, from <c>NetNpc.Size</c>; the root is not scaled). The
    /// rig's animator measures the root's movement, so it walks with the NPC; an Echo floats and drifts (the rig's own pose).
    /// It looks at a player nearby, hums instead of speaking (<see cref="SounderVoice"/> — its bubble shows the hum, not words;
    /// VEGA translates) and opens its mouth as it hums; the nameplate and the bubble sit over its ears.
    /// </summary>
    public sealed partial class NpcView
    {
        /// <summary>A Sounder looks at a player this close.</summary>
        private const float SounderLookRange = 7f;

        /// <summary>Gives a new NPC the Sounder rig and voice when its look asks for one (nothing for anyone else).</summary>
        private void AttachSounder(Npc n, NetNpc nd)
        {
            if (!SounderRules.IsSounderLook(nd.Look))
            {
                return;
            }

            bool echo = SounderRules.IsEcho(nd.Look);
            n.Avatar.SetVisible(false);
            n.Go.transform.localScale = Vector3.one; // the rig is built at the Sounder's own height
            var bodyRoot = new GameObject(echo ? "EchoBody" : "SounderBody");
            bodyRoot.transform.SetParent(n.Go.transform, false);
            var c = new NetCreature
            {
                Id = "npc_" + nd.Id,
                SpeciesId = echo ? "npc_sounder_echo" : "npc_sounder",
                BodyPlan = "Sounder",
                Size = nd.Size > 0f ? nd.Size : 1f,
                ColorRgb = (int)nd.SkinRgb,
                BellyRgb = (int)nd.OutfitRgb,
                Legs = 2,
                Arms = SounderRules.Arms,
                Eyes = 0,
                Habitat = "Land",
                Temperament = "Passive",
                VoiceSeed = nd.FaceVariant,
                Motion = "walker",
            };
            new CreatureBuilder().BuildSounderNpc(bodyRoot, c, echo);
            n.SounderRig = bodyRoot.GetComponent<CreatureAnimator>();
            n.Hums = SounderVoice.HumClips(echo);
            n.SounderHead = (SounderRules.HeightFor(c.Size) * 0.93f) + (echo ? SounderRules.EchoHover : 0f);
            n.Pitch = SounderVoice.Pitch(n.Seed);
            PreloadHums(n.Hums);
        }

        /// <summary>Per frame: the Sounder's head follows a player nearby (the rig's own gaze).</summary>
        private void TickSounder(Npc n)
        {
            if (n.SounderRig == null || Game == null)
            {
                return;
            }

            var d = Game.PlayerPosition - n.Go.transform.position;
            n.SounderRig.SetGazeTarget(Game.PlayerPosition, d.sqrMagnitude <= SounderLookRange * SounderLookRange);
        }

        /// <summary>The clips an NPC speaks with: a Sounder's hums, everyone else's voice bank.</summary>
        private static IReadOnlyList<string> ClipsOf(Npc n) => n.Hums ?? NpcVoice.Clips(n.Voice);

        /// <summary>A Sounder's bubble shows the hum, never the words (VEGA translates); everyone else's line is kept.</summary>
        private string HumBubbleFor(Npc n, string text)
        {
            if (n.Hums == null || text.Length == 0)
            {
                return text;
            }

            string hum = Game?.Localizer?.Get(SounderVoice.HumKey);
            return string.IsNullOrEmpty(hum) || hum == SounderVoice.HumKey ? SounderVoice.HumFallback : hum;
        }

        /// <summary>Loads the hum clips the first time a Sounder shows up, so its first hum is not late in a browser.</summary>
        private void PreloadHums(IReadOnlyList<string> hums)
        {
            var audio = ClientAudio.Instance;
            if (audio == null || hums == null || !_preloadedHums.Add(hums))
            {
                return;
            }

            foreach (string id in hums)
            {
                var clip = audio.Clip(id);
                if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                {
                    clip.LoadAudioData();
                }
            }
        }

        private readonly HashSet<IReadOnlyList<string>> _preloadedHums = new HashSet<IReadOnlyList<string>>();
    }
}
