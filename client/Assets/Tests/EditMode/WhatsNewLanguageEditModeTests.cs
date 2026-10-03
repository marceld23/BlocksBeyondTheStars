// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The "What's new?" feed carries German and English; every other language is laid over it from its own
    /// file (<c>data-online/whatsnew/&lt;code&gt;.json</c>), matched by version. These pin the two rules a
    /// player depends on: a translated release reads in their language, and everything the language file
    /// does not have — a release not translated yet, a broken entry, a missing file — reads English instead
    /// of going blank.
    /// </summary>
    public sealed class WhatsNewLanguageEditModeTests
    {
        // A code no real locale uses, so the tests never touch texts a running editor session loaded.
        private const string Code = "zz";

        private static WhatsNewEntry Entry(string version) => new WhatsNewEntry
        {
            version = version,
            title_de = "Titel " + version,
            title_en = "Title " + version,
            body_de = "Text " + version,
            body_en = "Body " + version,
        };

        [TearDown]
        public void TearDown() => WhatsNew.SetLanguageTexts(Code, null);

        [Test]
        public void TranslatedRelease_ReadsInThePlayersLanguage_TheRestInEnglish()
        {
            var texts = WhatsNew.ParseLanguageFile(
                "{\"language\":\"zz\",\"entries\":[{\"version\":\"2026.10.3\",\"title\":\"Titre\",\"body\":\"Corps\"}]}");
            Assert.IsNotNull(texts);
            WhatsNew.SetLanguageTexts(Code, texts);

            var translated = Entry("2026.10.3");
            Assert.AreEqual("Titre", WhatsNew.Title(translated, Code));
            Assert.AreEqual("Corps", WhatsNew.Body(translated, Code));

            var notYet = Entry("2026.10.2");
            Assert.AreEqual("Title 2026.10.2", WhatsNew.Title(notYet, Code), "an untranslated release falls back to English");
            Assert.AreEqual("Body 2026.10.2", WhatsNew.Body(notYet, Code));
        }

        [Test]
        public void GermanAndEnglish_ComeFromTheFeedItself()
        {
            var entry = Entry("2026.10.3");
            Assert.AreEqual("Titel 2026.10.3", WhatsNew.Title(entry, "de"));
            Assert.AreEqual("Body 2026.10.3", WhatsNew.Body(entry, "en"));
            Assert.IsFalse(WhatsNew.HasLanguageFile("de"));
            Assert.IsFalse(WhatsNew.HasLanguageFile("en"));
            Assert.IsTrue(WhatsNew.HasLanguageFile("fr"));
            Assert.IsTrue(WhatsNew.LanguageReady("de"), "German never waits for a language file");
        }

        [Test]
        public void LanguageWithoutTexts_ReadsEnglish()
        {
            var entry = Entry("2026.10.3");
            Assert.AreEqual("Title 2026.10.3", WhatsNew.Title(entry, Code), "no file loaded for the language");

            WhatsNew.SetLanguageTexts(Code, new System.Collections.Generic.Dictionary<string, WhatsNewLanguageEntry>());
            Assert.IsTrue(WhatsNew.LanguageReady(Code), "a language found empty is settled, not fetched again");
            Assert.AreEqual("Title 2026.10.3", WhatsNew.Title(entry, Code));
        }

        [Test]
        public void HalfFilledEntries_AreDropped_SoTheyCannotBlankOutTheEnglishText()
        {
            var texts = WhatsNew.ParseLanguageFile(
                "{\"language\":\"zz\",\"entries\":["
                + "{\"version\":\"2026.10.3\",\"title\":\"Titre\",\"body\":\"\"},"
                + "{\"version\":\"\",\"title\":\"Titre\",\"body\":\"Corps\"},"
                + "{\"version\":\"2026.10.1\",\"title\":\"Bon\",\"body\":\"Texte\"}]}");
            Assert.IsNotNull(texts);
            Assert.AreEqual(1, texts.Count);
            Assert.IsTrue(texts.ContainsKey("2026.10.1"));
        }

        [Test]
        public void UnusableJson_IsReportedAsNoFile()
        {
            // The parser logs a warning (not an error), so the test runner does not count it as a failure.
            Assert.IsNull(WhatsNew.ParseLanguageFile("not json at all"));
        }
    }
}
