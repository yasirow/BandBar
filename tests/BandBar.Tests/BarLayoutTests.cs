using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class BarLayoutTests
    {
        private static readonly string[] Units = { "B/s", "KB/s", "MB/s", "GB/s" };
        private static readonly string[] Statuses = { "…", "—", "NL?", "API?", "WFP?" };

        private static BarLayout Create(bool vertical = false, int thickness = 48, string family = "Segoe UI",
                                        float points = 9f, bool oneLine = false) =>
            new BarLayout(96, vertical, thickness, "8888.8", Units, Statuses, family, points, oneLine);

        [TestMethod]
        public void OneLineIsOneLineTallAndWider()
        {
            using (BarLayout two = Create())
            using (BarLayout one = Create(oneLine: true))
            {
                Assert.IsTrue(one.OneLine);
                Assert.IsTrue(one.Size.Height < two.Size.Height, $"{one.Size} vs {two.Size}");
                Assert.IsTrue(one.Size.Width > two.Size.Width, $"{one.Size} vs {two.Size}");
            }
        }

        [TestMethod]
        public void VerticalTaskbarsIgnoreOneLine()
        {
            using (BarLayout layout = Create(vertical: true, thickness: 200, oneLine: true))
                Assert.IsFalse(layout.OneLine);
        }

        [TestMethod]
        public void TheBarFitsAThinTaskbar()
        {
            using (BarLayout two = Create(thickness: 30))
                Assert.IsTrue(two.Size.Height <= 30, two.Size.ToString());
            using (BarLayout one = Create(thickness: 16, oneLine: true))
                Assert.IsTrue(one.Size.Height <= 16, one.Size.ToString());
        }

        [TestMethod]
        public void ALargerFontMakesALargerBar()
        {
            using (BarLayout normal = Create(thickness: 200, oneLine: true))
            using (BarLayout large = Create(thickness: 200, points: 14f, oneLine: true))
                Assert.IsTrue(large.Size.Width > normal.Size.Width && large.Size.Height > normal.Size.Height);
        }

        [TestMethod]
        public void AMissingFontFallsBackToSegoeUi()
        {
            using (BarLayout fallback = Create(family: "No Such Font 12345"))
            using (BarLayout normal = Create())
                Assert.AreEqual(normal.Size, fallback.Size);
        }
    }
}
