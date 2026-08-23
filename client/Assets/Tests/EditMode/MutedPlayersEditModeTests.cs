// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// The per-player mute list (#1209). Before this, <c>MutedVoicePlayers</c> was read by the voice mixer
    /// and written by nothing at all, while the manual promised a feature that did not exist.
    ///
    /// The list is local to the player, survives a restart, and matches on EITHER the stable player id the
    /// server stamps on a chat line or the display name a person actually sees — the two are the same value
    /// today, but the list must keep working when they stop being.
    /// </summary>
    public sealed class MutedPlayersEditModeTests
    {
        private string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "bbts_mute_tests_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            ClientSettings.StorageDirOverride = _dir;
            ClientSettings.LoadNoticeKey = "";
        }

        [TearDown]
        public void TearDown()
        {
            ClientSettings.StorageDirOverride = null;
            ClientSettings.LoadNoticeKey = "";
            try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
        }

        [Test]
        public void MuteAndUnmute_AreIdempotent()
        {
            var s = new ClientSettings();

            Assert.IsTrue(s.Mute("Bandit42"));
            Assert.IsFalse(s.Mute("Bandit42"), "muting twice must not duplicate the entry");
            Assert.AreEqual(1, s.MutedPlayers.Count);

            Assert.IsTrue(s.Unmute("Bandit42"));
            Assert.IsFalse(s.Unmute("Bandit42"), "unmuting someone who is not muted changes nothing");
            Assert.IsEmpty(s.MutedPlayers);
        }

        [Test]
        public void MuteIgnoresBlankInput()
        {
            var s = new ClientSettings();

            Assert.IsFalse(s.Mute(""));
            Assert.IsFalse(s.Mute("   "));
            Assert.IsFalse(s.Mute(null));
            Assert.IsEmpty(s.MutedPlayers);
        }

        [Test]
        public void IsMuted_MatchesTheIdExactlyAndTheNameCaseInsensitively()
        {
            var s = new ClientSettings();
            s.Mute("Justus");

            Assert.IsTrue(s.IsMuted("Justus", "Justus"));
            Assert.IsTrue(s.IsMuted(string.Empty, "justus"), "a display name is what the player actually sees");
            Assert.IsTrue(s.IsMuted(string.Empty, "JUSTUS"));
            Assert.IsFalse(s.IsMuted("other-id", "Somebody"));
            Assert.IsFalse(s.IsMuted(string.Empty, string.Empty), "an empty line must never count as muted");
        }

        [Test]
        public void IsMuted_IsFalseOnAnEmptyList()
        {
            var s = new ClientSettings();
            Assert.IsFalse(s.IsMuted("anyone", "Anyone"));
        }

        [Test]
        public void UnmuteRemovesAnEntryAddedUnderTheOtherKey()
        {
            // Muted from a chat row (which carries the id), unmuted from the settings list (which shows text).
            var s = new ClientSettings();
            s.Mute("Robber");

            Assert.IsTrue(s.Unmute("robber"));
            Assert.IsEmpty(s.MutedPlayers);
        }

        [Test]
        public void TheListSurvivesASaveAndLoad()
        {
            var s = new ClientSettings { PlayerName = "Marcel" };
            s.Mute("Loudmouth");
            s.Mute("Spammer");
            s.Save();

            var reloaded = ClientSettings.Load();

            Assert.AreEqual(2, reloaded.MutedPlayers.Count);
            Assert.IsTrue(reloaded.IsMuted(string.Empty, "Loudmouth"));
            Assert.IsTrue(reloaded.IsMuted(string.Empty, "Spammer"));
        }

        [Test]
        public void ALegacyVoiceOnlyListIsAdoptedOnLoad()
        {
            // The old field was voice-only and nothing ever wrote it — but a hand-edited settings file may
            // still carry one, and losing a mute list is worse than never having had the feature.
            var s = new ClientSettings { PlayerName = "Marcel" };
            s.MutedVoicePlayers.Add("OldEntry");
            s.Save();

            var reloaded = ClientSettings.Load();

            Assert.IsTrue(reloaded.IsMuted(string.Empty, "OldEntry"), "the legacy list must be folded in");
            Assert.IsEmpty(reloaded.MutedVoicePlayers, "…and then left empty, so it is adopted exactly once");
        }
    }
}
