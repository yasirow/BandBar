using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class SpeedFormatTests
    {
        private CultureInfo previousCulture;

        [TestInitialize]
        public void SetUp()
        {
            previousCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        }

        [TestCleanup]
        public void TearDown() => Thread.CurrentThread.CurrentCulture = previousCulture;

        private static Settings With(int unitBase = 1024, int decimals = 1, string unit = "bytes")
        {
            Settings settings = Settings.Defaults();
            settings.UnitBase = unitBase;
            settings.Decimals = decimals;
            settings.Unit = unit;
            return settings;
        }

        [DataTestMethod]
        [DataRow(0u, "0.0 bit/s")]
        [DataRow(124u, "992.0 bit/s")]
        [DataRow(125u, "1.0 kbit/s")]
        [DataRow(125000u, "1.0 Mbit/s")]
        [DataRow(12500000u, "100.0 Mbit/s")]
        [DataRow(125000000u, "1.0 Gbit/s")]
        [DataRow(uint.MaxValue, "34.3 Gbit/s")] // 34.359 Gbit/s, truncated
        public void BitsStepInThousands(uint bytesPerSecond, string expected)
        {
            // unitBase 1024 does not apply to bits.
            Assert.AreEqual(expected, SpeedFormat.Format(bytesPerSecond, With(unit: "bits")).ToString());
        }

        [TestMethod]
        public void UnitsFollowTheSetting()
        {
            CollectionAssert.AreEqual(new[] { "B/s", "KB/s", "MB/s", "GB/s" }, SpeedFormat.UnitsFor(With()).ToArray());
            CollectionAssert.AreEqual(new[] { "bit/s", "kbit/s", "Mbit/s", "Gbit/s" }, SpeedFormat.UnitsFor(With(unit: "bits")).ToArray());
        }

        [DataTestMethod]
        [DataRow(0ul, 1024, "0 B")]
        [DataRow(512ul, 1024, "512 B")]
        [DataRow(1536ul, 1024, "1.5 KB")]
        [DataRow(5368709120ul, 1024, "5.0 GB")]
        [DataRow(1099511627776ul, 1024, "1.0 TB")]
        [DataRow(1500000000ul, 1000, "1.5 GB")]
        public void FormatsAmounts(ulong bytes, int unitBase, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.FormatAmount(bytes, With(unitBase)));
        }

        [DataTestMethod]
        [DataRow(0u, "0.0 B/s")]
        [DataRow(1023u, "1023.0 B/s")]
        [DataRow(1024u, "1.0 KB/s")]
        [DataRow(1536u, "1.5 KB/s")]
        [DataRow(1048576u, "1.0 MB/s")]
        [DataRow(1073741824u, "1.0 GB/s")]
        public void StepsThroughTheUnits(uint bytesPerSecond, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.Format(bytesPerSecond, With()).ToString());
        }

        [DataTestMethod]
        [DataRow(1048575u, "1023.9 KB/s")]   // 1023.999 KB/s: never rounded up to "1024.0"
        [DataRow(1945u, "1.8 KB/s")]         // 1.899 KB/s
        [DataRow(uint.MaxValue, "3.9 GB/s")] // 3.999 GB/s
        public void TruncatesInsteadOfRounding(uint bytesPerSecond, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.Format(bytesPerSecond, With()).ToString());
        }

        [DataTestMethod]
        [DataRow(999u, "999.0 B/s")]
        [DataRow(1000u, "1.0 KB/s")]
        [DataRow(1500000u, "1.5 MB/s")]
        public void Base1000(uint bytesPerSecond, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.Format(bytesPerSecond, With(unitBase: 1000)).ToString());
        }

        [DataTestMethod]
        [DataRow(0, "1 KB/s")]
        [DataRow(2, "1.50 KB/s")]
        [DataRow(3, "1.500 KB/s")]
        public void Decimals(int decimals, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.Format(1536, With(decimals: decimals)).ToString());
        }

        [TestMethod]
        public void SplitsNumberAndUnit()
        {
            SpeedText text = SpeedFormat.Format(1536, With());
            Assert.AreEqual("1.5", text.Number);
            Assert.AreEqual("KB/s", text.Unit);
        }

        [TestMethod]
        public void UsesTheRegionalDecimalSeparator()
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("tr-TR");
            Assert.AreEqual("1,5 KB/s", SpeedFormat.Format(1536, With()).ToString());
            Assert.AreEqual("8888,8", SpeedFormat.WidestNumber(With()));
        }

        [TestMethod]
        public void StatusTextHasNoUnit()
        {
            SpeedText status = SpeedText.Status("NL?");
            Assert.AreEqual("NL?", status.ToString());
            Assert.AreEqual("", status.Unit);
        }

        [DataTestMethod]
        [DataRow(0, "8888")]
        [DataRow(1, "8888.8")]
        [DataRow(3, "8888.888")]
        public void WidestNumber(int decimals, string expected)
        {
            Assert.AreEqual(expected, SpeedFormat.WidestNumber(With(decimals: decimals)));
        }

        [TestMethod]
        public void NoNumberIsLongerThanTheWidestSample()
        {
            foreach (string unit in new[] { "bytes", "bits" })
            {
                foreach (int unitBase in new[] { 1000, 1024 })
                {
                    for (int decimals = 0; decimals <= 3; decimals++)
                    {
                        Settings settings = With(unitBase, decimals, unit);
                        int widest = SpeedFormat.WidestNumber(settings).Length;
                        foreach (uint value in new[] { 0u, 124u, 999u, 1000u, 1023u, 1024u, 1048575u, 999999u, 1023999999u, uint.MaxValue })
                        {
                            string number = SpeedFormat.Format(value, settings).Number;
                            Assert.IsTrue(number.Length <= widest, $"{number} ({unit}, base {unitBase}, {decimals} decimals)");
                        }
                    }
                }
            }
        }
    }
}
