using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class SettingsFileTests
    {
        private const string Key = "anchorOffsetDip";

        private static Settings Load(TempFolder folder)
        {
            Assert.AreEqual(SettingsLoad.Loaded, Settings.TryLoad(folder.Path, out Settings settings));
            return settings;
        }

        private static void AssertInOrder(string text, params string[] parts)
        {
            int last = -1;
            foreach (string part in parts)
            {
                int index = text.IndexOf(part, StringComparison.Ordinal);
                Assert.IsTrue(index > last, $"'{part}' missing or out of order in:\n{text}");
                last = index;
            }
        }

        private static void AssertNoTemporaryFile(TempFolder folder) =>
            Assert.IsFalse(File.Exists(folder.SettingsPath + SettingsFile.TempSuffix), "temporary file left behind");

        [TestMethod]
        public void CreatesTheFileWithEveryDefaultWhenThereIsNone()
        {
            using (var folder = new TempFolder())
            {
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, 120));

                // The user opening the file afterwards finds every key, with the new value in its usual place.
                string text = folder.ReadSettings();
                AssertInOrder(text, "\"unitBase\"", "\"decimals\"", "\"opacity\"", "\"anchorOffsetDip\": 120",
                    "\"hideOnFullscreen\"", "\"logLevel\"");
                Settings s = Load(folder);
                Assert.AreEqual(120, s.AnchorOffsetDip);
                Assert.AreEqual(1, s.Decimals);
                AssertNoTemporaryFile(folder);
            }
        }

        [TestMethod]
        public void ClearingWithoutAFileCreatesNone()
        {
            using (var folder = new TempFolder())
            {
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, null));
                Assert.IsFalse(File.Exists(folder.SettingsPath));
            }
        }

        [TestMethod]
        public void ChangesOnlyTheKeyAndKeepsEverythingElse()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"decimals\": 2, \"custom\": { \"list\": [1, \"x\", true, null] }, \"my key\": 1, \"theme\": \"light\" }");

                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, 120));

                string text = folder.ReadSettings();
                AssertInOrder(text, "\"decimals\"", "\"custom\"", "\"list\"", "\"my key\"", "\"theme\"", "\"anchorOffsetDip\"");
                StringAssert.Contains(text, "\"x\"");
                Settings s = Load(folder);
                Assert.AreEqual(2, s.Decimals);
                Assert.AreEqual("light", s.Theme);
                Assert.AreEqual(120, s.AnchorOffsetDip);
                AssertNoTemporaryFile(folder);
            }
        }

        [TestMethod]
        public void ReplacesAnExistingValueInPlace()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"anchorOffsetDip\": null, \"decimals\": 2 }");
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, 75));
                AssertInOrder(folder.ReadSettings(), "\"anchorOffsetDip\"", "\"decimals\"");
                Assert.AreEqual(75, Load(folder).AnchorOffsetDip);

                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, 80));
                Assert.AreEqual(80, Load(folder).AnchorOffsetDip);
            }
        }

        [TestMethod]
        public void NullClearsTheValueAndKeepsTheKey()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"anchorOffsetDip\": 120, \"decimals\": 2 }");
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, null));
                AssertInOrder(folder.ReadSettings(), "\"anchorOffsetDip\": null", "\"decimals\"");
                Settings s = Load(folder);
                Assert.IsNull(s.AnchorOffsetDip);
                Assert.AreEqual(2, s.Decimals);
            }
        }

        [DataTestMethod]
        [DataRow("{ \"decimals\":2 }")]
        [DataRow("{ \"anchorOffsetDip\":null, \"decimals\":2 }")]
        public void ClearingAnUnsetKeyLeavesTheFileUntouched(string json)
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings(json);
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, null));
                Assert.AreEqual(json, folder.ReadSettings());
            }
        }

        [DataTestMethod]
        [DataRow("{ \"decimals\": 2,")]
        [DataRow("[1, 2]")]
        public void NeverOverwritesAnInvalidFile(string json)
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings(json);
                Assert.IsFalse(SettingsFile.SetNumber(folder.Path, Key, 120));
                Assert.AreEqual(json, folder.ReadSettings());
                AssertNoTemporaryFile(folder);
            }
        }

        [TestMethod]
        public void NeverOverwritesAFileLargerThan64KB()
        {
            using (var folder = new TempFolder())
            {
                string json = "{ \"padding\": \"" + new string('x', 70 * 1024) + "\" }";
                folder.WriteSettings(json);
                Assert.IsFalse(SettingsFile.SetNumber(folder.Path, Key, 120));
                Assert.AreEqual(json, folder.ReadSettings());
            }
        }

        [TestMethod]
        public void KeepsNonAsciiText()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"zoneFilterId\": \"Bölge/İç ağ\" }");
                Assert.IsTrue(SettingsFile.SetNumber(folder.Path, Key, 1));
                Assert.AreEqual("Bölge/İç ağ", Load(folder).ZoneFilterId);
            }
        }

        [TestMethod]
        public void NoDataFolderMeansNothingIsWritten()
        {
            Assert.IsFalse(SettingsFile.SetNumber(null, Key, 1));
            Assert.IsFalse(SettingsFile.EnsureExists(null));
        }

        [TestMethod]
        public void TheCreatedFileHoldsEveryKeyAtItsDefault()
        {
            using (var folder = new TempFolder())
            {
                Assert.IsTrue(SettingsFile.EnsureExists(folder.Path));
                string text = folder.ReadSettings();
                AssertInOrder(text, "\"schemaVersion\"", "\"zoneFilterId\"", "\"unitBase\"", "\"decimals\"", "\"theme\"",
                    "\"colors\"", "\"upload\"", "\"download\"", "\"text\"", "\"background\"", "\"opacity\"",
                    "\"anchorOffsetDip\"", "\"hideOnFullscreen\"", "\"fullscreenExeList\": [", "\"followAutoHideTaskbar\"",
                    "\"hotkey\": null", "\"logLevel\"");

                Settings created = Load(folder);
                Settings defaults = Settings.Defaults();
                Assert.AreEqual(defaults.UnitBase, created.UnitBase);
                Assert.AreEqual(defaults.Decimals, created.Decimals);
                Assert.AreEqual(defaults.Theme, created.Theme);
                Assert.AreEqual(defaults.Opacity, created.Opacity);
                Assert.AreEqual(defaults.UploadColor, created.UploadColor);
                Assert.AreEqual(defaults.DownloadColor, created.DownloadColor);
                Assert.IsNull(created.AnchorOffsetDip);
                Assert.IsNull(created.ZoneFilterId);
                Assert.IsTrue(created.HideOnFullscreen);
                Assert.IsTrue(created.FollowAutoHideTaskbar);
                Assert.AreEqual(0, created.ListedApps.Length);
                Assert.IsNull(created.Hotkey);
                AssertNoTemporaryFile(folder);
            }
        }

        [TestMethod]
        public void AnExistingFileIsNotReplacedByTheDefaults()
        {
            using (var folder = new TempFolder())
            {
                const string json = "{ \"decimals\": 3 }";
                folder.WriteSettings(json);
                Assert.IsTrue(SettingsFile.EnsureExists(folder.Path));
                Assert.AreEqual(json, folder.ReadSettings());
            }
        }
    }
}
