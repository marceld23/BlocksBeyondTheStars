// Blocks Beyond the Stars — Copyright (c) 2026 Justus Dütscher & Marcel Dütscher (JuMaVe Games)
// SPDX-License-Identifier: AGPL-3.0-or-later
// This file is part of Blocks Beyond the Stars. See LICENSE for the full AGPL-3.0 text.
using System.IO;
using BlocksBeyondTheStars.Client;
using NUnit.Framework;
using UnityEngine;

namespace BlocksBeyondTheStars.Client.Tests.EditMode
{
    /// <summary>
    /// Pins the settings-persistence hardening from issue #410: a corrupt <c>client_settings.json</c> must
    /// never destroy the name-claim <see cref="ClientSettings.PlayerToken"/> (which would permanently lock
    /// the player out of their own multiplayer name). Saves are atomic (temp + rename) and keep a .bak;
    /// loads recover from the .bak, then from the separate token backup, and preserve the corrupt file.
    /// All tests run against a scratch directory via <see cref="ClientSettings.StorageDirOverride"/> so the
    /// developer's real settings are never touched.
    /// </summary>
    public sealed class ClientSettingsPersistenceEditModeTests
    {
        private string _dir;

        private static string SettingsPath(string dir) => Path.Combine(dir, "client_settings.json");
        private static string BackupPath(string dir) => Path.Combine(dir, "client_settings.json.bak");
        private static string CorruptPath(string dir) => Path.Combine(dir, "client_settings.json.corrupt");
        private static string TokenPath(string dir) => Path.Combine(dir, "player_token.txt");

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "bbts_settings_tests_" + System.Guid.NewGuid().ToString("N"));
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
        public void SaveThenLoad_RoundTripsAndWritesTokenBackup()
        {
            var settings = new ClientSettings { PlayerName = "Justus", PlayerToken = "token-one", MouseSensitivity = 3.5f };
            settings.Save();

            var loaded = ClientSettings.Load();

            Assert.AreEqual("Justus", loaded.PlayerName);
            Assert.AreEqual("token-one", loaded.PlayerToken);
            Assert.AreEqual(3.5f, loaded.MouseSensitivity);
            Assert.IsTrue(File.Exists(TokenPath(_dir)), "Save must mirror the token into its own backup file");
            Assert.AreEqual("token-one", File.ReadAllText(TokenPath(_dir)).Trim());
            Assert.IsFalse(File.Exists(SettingsPath(_dir) + ".tmp"), "the atomic-write temp file must not linger");
            Assert.AreEqual("", ClientSettings.LoadNoticeKey, "a clean load must not raise a recovery notice");
        }

        [Test]
        public void Save_KeepsThePreviousFileAsBackup()
        {
            var settings = new ClientSettings { PlayerName = "First", PlayerToken = "token-one" };
            settings.Save();
            settings.PlayerName = "Second";
            settings.Save();

            Assert.IsTrue(File.Exists(BackupPath(_dir)), "the second save must keep the previous file as .bak");
            var backup = JsonUtility.FromJson<ClientSettings>(File.ReadAllText(BackupPath(_dir)));
            Assert.AreEqual("First", backup.PlayerName);
            var main = JsonUtility.FromJson<ClientSettings>(File.ReadAllText(SettingsPath(_dir)));
            Assert.AreEqual("Second", main.PlayerName);
        }

        [Test]
        public void Load_WithCorruptFile_RecoversFromBackupAndPreservesTheEvidence()
        {
            var settings = new ClientSettings { PlayerName = "Pilot", PlayerToken = "token-one" };
            settings.Save();
            settings.Save(); // second save stamps the good state into .bak
            File.WriteAllText(SettingsPath(_dir), "{\"PlayerName\":\"Pilo"); // truncated mid-write

            var loaded = ClientSettings.Load();

            Assert.AreEqual("Pilot", loaded.PlayerName, "settings must come back from the .bak");
            Assert.AreEqual("token-one", loaded.PlayerToken, "the name-claim token must survive the corruption");
            Assert.AreEqual("ui.settings.recovered_backup", ClientSettings.LoadNoticeKey);
            Assert.IsTrue(File.Exists(CorruptPath(_dir)), "the corrupt file must be preserved, not clobbered");
            var rewritten = JsonUtility.FromJson<ClientSettings>(File.ReadAllText(SettingsPath(_dir)));
            Assert.AreEqual("token-one", rewritten.PlayerToken, "the recovered state must be persisted again");
        }

        [Test]
        public void Load_WithCorruptFileAndNoBackup_StillRestoresTheTokenFromItsOwnBackup()
        {
            // First save ever (no .bak yet), then the only settings file gets corrupted: the worst case
            // from #410. Settings reset to defaults, but the token backup must save the name claim.
            new ClientSettings { PlayerName = "Pilot", PlayerToken = "token-one" }.Save();
            File.WriteAllText(SettingsPath(_dir), "not json at all");

            var loaded = ClientSettings.Load();

            Assert.AreEqual("", loaded.PlayerName, "settings fall back to defaults without a .bak");
            Assert.AreEqual("token-one", loaded.PlayerToken, "the token must be restored from player_token.txt");
            Assert.AreEqual("ui.settings.reset_defaults", ClientSettings.LoadNoticeKey);
            Assert.IsTrue(File.Exists(CorruptPath(_dir)));
        }

        [Test]
        public void Load_OnFreshInstall_GeneratesAndBacksUpAToken()
        {
            var loaded = ClientSettings.Load();

            Assert.IsNotEmpty(loaded.PlayerToken);
            Assert.IsTrue(File.Exists(SettingsPath(_dir)), "the minted token must be persisted immediately");
            Assert.AreEqual(loaded.PlayerToken, File.ReadAllText(TokenPath(_dir)).Trim());
            Assert.AreEqual("", ClientSettings.LoadNoticeKey, "a fresh install is not a recovery");
        }

        [Test]
        public void Load_HealsAMissingTokenBackup_WithoutRewritingSettings()
        {
            new ClientSettings { PlayerToken = "token-one" }.Save();
            File.Delete(TokenPath(_dir));

            var loaded = ClientSettings.Load();

            Assert.AreEqual("token-one", loaded.PlayerToken);
            Assert.AreEqual("token-one", File.ReadAllText(TokenPath(_dir)).Trim(),
                "an install that predates the token backup must gain one on the next load");
        }
    }
}
