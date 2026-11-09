using System.Diagnostics;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class AppListTests
    {
        [TestMethod]
        public void NormalizesEntries()
        {
            string[] list = AppList.Normalize(new[]
            {
                "Game.exe", @" X:\Games\Other\Other.EXE ", "\"quoted.exe\"", "noext", "", "   ", null, "game", "bad|name.exe",
            });
            CollectionAssert.AreEqual(new[] { "game", "other", "quoted", "noext" }, list);
        }

        [TestMethod]
        public void NothingConfiguredMeansAnEmptyList()
        {
            Assert.AreEqual(0, AppList.Normalize(null).Length);
            Assert.IsFalse(AppList.Contains(AppList.Normalize(null), "game.exe"));
        }

        [TestMethod]
        public void KeepsAtMost64Entries()
        {
            string[] list = AppList.Normalize(Enumerable.Range(0, 100).Select(i => "app" + i + ".exe"));
            Assert.AreEqual(AppList.MaxEntries, list.Length);
        }

        [DataTestMethod]
        [DataRow("game.exe", true)]
        [DataRow("GAME.EXE", true)]
        [DataRow("game", true)]
        [DataRow("game2.exe", false)]
        [DataRow("mygame.exe", false)]
        [DataRow(null, false)]
        public void MatchesImageNames(string imageName, bool expected)
        {
            Assert.AreEqual(expected, AppList.Contains(AppList.Normalize(new[] { "Game.exe" }), imageName));
        }

        [TestMethod]
        public void ReadsAnImageNameFromTheProcessSnapshot()
        {
            // Our own test process: proves the snapshot structure is read correctly, without opening any process.
            using (Process self = Process.GetCurrentProcess())
            {
                string name = AppList.ImageNameOf((uint)self.Id);
                Assert.IsNotNull(name);
                Assert.AreEqual(self.ProcessName + ".exe", name, ignoreCase: true);
            }
        }

        [TestMethod]
        public void AnUnknownProcessIdHasNoName()
        {
            Assert.IsNull(AppList.ImageNameOf(uint.MaxValue - 1));
        }
    }
}
