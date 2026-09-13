// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The "You are in chat" banner (#1845) fades in with the chat box and out again when it closes, over
    /// ~0.15 s of unscaled time, and is cut to 0 outright while a menu-style dialog is up. These pin
    /// <see cref="ChatUi.ResolveBannerAlpha"/>, the pure per-frame step the banner's CanvasGroup follows.
    /// </summary>
    public sealed class ChatBannerEditModeTests
    {
        private const float Fade = 0.15f;
        private const float Eps = 1e-4f;

        [Test]
        public void Typing_RisesToOne_OverTheFadeTime()
        {
            float a = ChatUi.ResolveBannerAlpha(0f, typing: true, menuOpen: false, unscaledDt: Fade / 3f);
            Assert.That(a, Is.EqualTo(1f / 3f).Within(Eps), "a third of the fade after a third of the time");

            a = ChatUi.ResolveBannerAlpha(a, typing: true, menuOpen: false, unscaledDt: Fade);
            Assert.That(a, Is.EqualTo(1f).Within(Eps), "fully up after a whole fade, never beyond 1");
            Assert.That(ChatUi.ResolveBannerAlpha(a, typing: true, menuOpen: false, unscaledDt: 1f), Is.EqualTo(1f).Within(Eps), "stays up");
        }

        [Test]
        public void NotTyping_FallsToZero_OverTheFadeTime()
        {
            float a = ChatUi.ResolveBannerAlpha(1f, typing: false, menuOpen: false, unscaledDt: Fade / 2f);
            Assert.That(a, Is.EqualTo(0.5f).Within(Eps));

            a = ChatUi.ResolveBannerAlpha(a, typing: false, menuOpen: false, unscaledDt: Fade);
            Assert.That(a, Is.EqualTo(0f).Within(Eps), "gone after a whole fade, never below 0 (the caller deactivates the panel at 0)");
        }

        [Test]
        public void MenuOpen_CutsTheBannerAtOnce()
        {
            Assert.That(ChatUi.ResolveBannerAlpha(1f, typing: true, menuOpen: true, unscaledDt: 0.001f), Is.EqualTo(0f),
                "a dialog opened over the chat must not carry the banner on top of itself, not even for a frame");
            Assert.That(ChatUi.ResolveBannerAlpha(0.4f, typing: false, menuOpen: true, unscaledDt: 0.001f), Is.EqualTo(0f));
        }

        [Test]
        public void ClosingMidFade_ReversesFromWhereItIs()
        {
            float a = ChatUi.ResolveBannerAlpha(0f, typing: true, menuOpen: false, unscaledDt: Fade / 2f);
            Assert.That(a, Is.EqualTo(0.5f).Within(Eps));
            a = ChatUi.ResolveBannerAlpha(a, typing: false, menuOpen: false, unscaledDt: Fade / 4f);
            Assert.That(a, Is.EqualTo(0.25f).Within(Eps), "no snap: the tween turns around from its current alpha");
        }

        [Test]
        public void ZeroOrNegativeDelta_HoldsTheCurrentAlpha()
        {
            Assert.That(ChatUi.ResolveBannerAlpha(0.3f, typing: true, menuOpen: false, unscaledDt: 0f), Is.EqualTo(0.3f).Within(Eps));
            Assert.That(ChatUi.ResolveBannerAlpha(0.3f, typing: false, menuOpen: false, unscaledDt: -1f), Is.EqualTo(0.3f).Within(Eps));
        }

        [Test]
        public void OutOfRangeInput_IsClampedBeforeStepping()
        {
            Assert.That(ChatUi.ResolveBannerAlpha(5f, typing: true, menuOpen: false, unscaledDt: 0f), Is.EqualTo(1f).Within(Eps));
            Assert.That(ChatUi.ResolveBannerAlpha(-5f, typing: false, menuOpen: false, unscaledDt: 0f), Is.EqualTo(0f).Within(Eps));
        }
    }
}
