using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BandBar.Tests
{
    [TestClass]
    public class LogScrubberTests
    {
        // A made-up profile: user "A" on a computer named "WIN" (short names are the hard case).
        private static readonly LogScrubber Scrubber = new LogScrubber(
            new[]
            {
                (@"X:\Profiles\A\AppData\Local\Temp\", "%TEMP%"),
                (@"X:\Profiles\A\AppData\Local", "%LOCALAPPDATA%"),
                (@"X:\Profiles\A\AppData\Roaming", "%APPDATA%"),
                (@"X:\Profiles\A", "%USERPROFILE%"),
            },
            machineName: "WIN",
            userName: "A");

        [DataTestMethod]
        [DataRow(@"X:\Profiles\A\AppData\Local\BandBar\bandbar.log", @"%LOCALAPPDATA%\BandBar\bandbar.log")]
        [DataRow(@"x:\profiles\a\appdata\local\BandBar", @"%LOCALAPPDATA%\BandBar")]
        [DataRow(@"X:\Profiles\A\AppData\Local\Temp\x.tmp", @"%TEMP%\x.tmp")]
        [DataRow(@"X:\Profiles\A\AppData\Roaming\x", @"%APPDATA%\x")]
        [DataRow(@"X:\Profiles\A\Desktop\BandBar\BandBar.exe", @"%USERPROFILE%\Desktop\BandBar\BandBar.exe")]
        public void MasksProfilePaths(string input, string expected)
        {
            Assert.AreEqual(expected, Scrubber.Scrub(input));
        }

        [DataTestMethod]
        [DataRow(@"D:\Data\A\file.txt", @"D:\Data\<user>\file.txt")]
        [DataRow(@"WORKGROUP\A", @"WORKGROUP\<user>")]
        [DataRow(@"D:/home/A/x", @"D:/home/<user>/x")]
        public void MasksTheUserNameAsAPathSegment(string input, string expected)
        {
            Assert.AreEqual(expected, Scrubber.Scrub(input));
        }

        [DataTestMethod]
        [DataRow("Connected to WIN.", "Connected to <machine>.")]
        [DataRow(@"\\WIN\share", @"\\<machine>\share")]
        public void MasksTheComputerNameAsAWholeWord(string input, string expected)
        {
            Assert.AreEqual(expected, Scrubber.Scrub(input));
        }

        [DataTestMethod]
        [DataRow("Windows taskbar recreated")]
        [DataRow("A short message about A user")]
        [DataRow("WIN-2 and WIN_3 are other machines")]
        [DataRow("NetLimiter link: connected (service 5.3.28.0)")]
        public void LeavesOrdinaryTextAlone(string input)
        {
            Assert.AreEqual(input, Scrubber.Scrub(input));
        }

        [TestMethod]
        public void WithoutDetailsNothingChanges()
        {
            var empty = new LogScrubber(new (string, string)[0], null, null);
            Assert.AreEqual(@"X:\Profiles\A\x on WIN", empty.Scrub(@"X:\Profiles\A\x on WIN"));
        }
    }
}
