// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client.Cutscenes;
using BlocksBeyondTheStars.Networking.Messages;

namespace BlocksBeyondTheStars.Client
{
    public sealed partial class CutsceneDirector
    {
        /// <summary>The VEGA line that tells the player a scene waits for a safe moment (#3387).</summary>
        internal const string WaitingSafeLineKey = "ui.cutscene.waiting_safe";

        /// <summary>#3387 ("the cutscene did not come after I shut down the Guardian core"): a scene that asks for it
        /// (<c>waitHint</c>, the finale's stand-down) and has waited a few seconds for danger or movement to end gets one
        /// VEGA line — "I have something important to show you, as soon as you are safe" — so a held-back scene does not read
        /// as a missing one. Once per scene; never for a replay; a menu, the chat or the duel say nothing (they end by
        /// themselves). The rule itself is <see cref="SafeMomentGate.ShouldHintWaiting"/>.</summary>
        private void HintWaitingScene(Owed owed, SafeMomentRequest request, SafeMomentDecision decision)
        {
            if (owed?.Definition?.Options?.WaitHint != true
                || !SafeMomentGate.ShouldHintWaiting(decision, request, owed.WaitHinted))
            {
                return;
            }

            owed.WaitHinted = true;
            UnityEngine.Debug.Log($"[Cutscene] {owed.Definition.Id} waits ({decision.Reason}) — telling the player.");
            Game?.Views.Vega?.Requeue(new ShipAiLine { LineKey = WaitingSafeLineKey, Kind = 2 });
        }
    }
}
