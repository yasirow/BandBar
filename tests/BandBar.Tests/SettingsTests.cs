using System.Drawing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class SettingsTests
    {
        private static Settings Load(TempFolder folder)
        {
            Assert.AreEqual(SettingsLoad.Loaded, Settings.TryLoad(folder.Path, out Settings settings));
            return settings;
        }

        [TestMethod]
        public void NoFileMeansTheDefaults()
        {
            using (var folder = new TempFolder())
            {
                Settings s = Load(folder);
                Assert.IsNull(s.ZoneFilterId);
                Assert.AreEqual(1024, s.UnitBase);
                Assert.AreEqual(1, s.Decimals);
                Assert.AreEqual("auto", s.Theme);
                Assert.IsNull(s.AnchorOffsetDip);
                Assert.AreEqual("main", s.Taskbars);
                Assert.IsNull(s.OtherAnchorOffsetDip);
                Assert.IsTrue(s.HideOnFullscreen);
                Assert.IsTrue(s.FollowAutoHideTaskbar);
                Assert.AreEqual(0, s.ListedApps.Length);
                Assert.IsNull(s.Hotkey);
                Assert.AreEqual("info", s.LogLevel);
                Assert.AreEqual(Color.FromArgb(0xE7, 0x4C, 0x3C), s.UploadColor);
                Assert.AreEqual(Color.FromArgb(0x2E, 0xCC, 0x71), s.DownloadColor);
                Assert.IsNull(s.BackgroundColor);
                Assert.IsNull(s.TextColorOverride);
            }
        }

        [TestMethod]
        public void MissingKeysKeepTheirDefaults()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"decimals\": 2, \"colors\": { \"download\": \"#00C853\" } }");
                Settings s = Load(folder);
                Assert.AreEqual(2, s.Decimals);
                Assert.AreEqual(1024, s.UnitBase);
                Assert.IsTrue(s.HideOnFullscreen);
                Assert.IsTrue(s.FollowAutoHideTaskbar);
                Assert.AreEqual(Color.FromArgb(0x00, 0xC8, 0x53), s.DownloadColor);
                Assert.AreEqual(Color.FromArgb(0xE7, 0x4C, 0x3C), s.UploadColor);
            }
        }

        [TestMethod]
        public void OutOfRangeValuesAreCorrected()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"unitBase\": 999, \"decimals\": 9, \"opacity\": 5, \"theme\": \" DARK \", "
                                     + "\"anchorOffsetDip\": -5, \"otherAnchorOffsetDip\": 99999, \"taskbars\": \"sideways\", "
                                     + "\"colors\": { \"download\": \"green\" } }");
                Settings s = Load(folder);
                Assert.AreEqual(1024, s.UnitBase);
                Assert.AreEqual(3, s.Decimals);
                Assert.AreEqual(1.0, s.Opacity);
                Assert.AreEqual("dark", s.Theme);
                Assert.AreEqual(0, s.AnchorOffsetDip);
                Assert.AreEqual(10000, s.OtherAnchorOffsetDip);
                Assert.AreEqual("main", s.Taskbars);
                Assert.AreEqual(Color.FromArgb(0x2E, 0xCC, 0x71), s.DownloadColor);
            }
        }

        [TestMethod]
        public void ReadsEveryKey()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"zoneFilterId\": \"zone-1\", \"unitBase\": 1000, \"anchorOffsetDip\": 300, "
                                     + "\"hideOnFullscreen\": false, \"followAutoHideTaskbar\": false, \"logLevel\": \"debug\", "
                                     + "\"opacity\": 0.5, \"colors\": { \"text\": \"#101010\", \"background\": \"#000000\" }, "
                                     + "\"fullscreenExeList\": [\"Game.exe\", \"other\"], \"hotkey\": \" Ctrl+Alt+B \", "
                                     + "\"taskbars\": \" Others \", \"otherAnchorOffsetDip\": 150 }");
                Settings s = Load(folder);
                Assert.AreEqual("others", s.Taskbars);
                Assert.AreEqual(150, s.OtherAnchorOffsetDip);
                CollectionAssert.AreEqual(new[] { "game", "other" }, s.ListedApps);
                Assert.AreEqual("Ctrl+Alt+B", s.Hotkey);
                Assert.AreEqual("zone-1", s.ZoneFilterId);
                Assert.AreEqual(1000, s.UnitBase);
                Assert.AreEqual(300, s.AnchorOffsetDip);
                Assert.IsFalse(s.HideOnFullscreen);
                Assert.IsFalse(s.FollowAutoHideTaskbar);
                Assert.AreEqual("debug", s.LogLevel);
                Assert.AreEqual(Color.FromArgb(0x10, 0x10, 0x10), s.TextColorOverride);
                Assert.AreEqual(Color.FromArgb(128, 0, 0, 0), s.BackgroundColor);
            }
        }

        [DataTestMethod]
        [DataRow("main", "main")]
        [DataRow("OTHERS", "others")]
        [DataRow(" all ", "all")]
        [DataRow("", "main")]
        [DataRow("primary", "main")]
        public void TaskbarsKnowsThreeValues(string value, string expected)
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"taskbars\": \"" + value + "\" }");
                Assert.AreEqual(expected, Load(folder).Taskbars);
            }
        }

        [DataTestMethod]
        [DataRow("{ \"decimals\": 2,")]           // cut off half-way through a save
        [DataRow("{ \"decimals\": 2, }")]         // trailing comma
        [DataRow("{ \"decimals\": 2 } x")]        // something after the object
        [DataRow("{ decimals: 2 }")]              // unquoted name
        [DataRow("{ \"decimals\": \"two\" }")]    // wrong type
        [DataRow("not json")]
        [DataRow("null")]
        [DataRow("[1, 2]")]
        public void AnInvalidFileIsReported(string json)
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings(json);
                Assert.AreEqual(SettingsLoad.Invalid, Settings.TryLoad(folder.Path, out Settings s));
                Assert.IsNull(s);

                // At start-up an invalid file means the defaults.
                Assert.AreEqual(1, Settings.Load(folder.Path).Decimals);
            }
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("  \r\n")]
        public void ABlankFileMeansTheDefaults(string json)
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings(json);
                Assert.AreEqual(1, Load(folder).Decimals);
            }
        }

        [TestMethod]
        public void ReadsUtf16WithByteOrderMark()
        {
            using (var folder = new TempFolder())
            {
                System.IO.File.WriteAllText(folder.SettingsPath, "{ \"decimals\": 3 }", System.Text.Encoding.Unicode);
                Assert.AreEqual(3, Load(folder).Decimals);
            }
        }

        [TestMethod]
        public void AFileLargerThan64KBIsIgnored()
        {
            using (var folder = new TempFolder())
            {
                folder.WriteSettings("{ \"decimals\": 2, \"padding\": \"" + new string('x', 70 * 1024) + "\" }");
                Assert.AreEqual(SettingsLoad.Invalid, Settings.TryLoad(folder.Path, out _));
            }
        }
    }
}
