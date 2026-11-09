using Microsoft.VisualStudio.TestTools.UnitTesting;
using static BandBar.NativeMethods;

namespace BandBar.Tests
{
    [TestClass]
    public class HotkeyTests
    {
        [DataTestMethod]
        [DataRow("Ctrl+Alt+B", MOD_CONTROL | MOD_ALT, 0x42u)]           // 'B'
        [DataRow(" control + shift + 7 ", MOD_CONTROL | MOD_SHIFT, 0x37u)] // '7'
        [DataRow("Win+F12", MOD_WIN, 0x7Bu)]
        [DataRow("alt+f1", MOD_ALT, 0x70u)]
        [DataRow("Ctrl+F24", MOD_CONTROL, 0x87u)]
        [DataRow("Windows+Alt+Z", MOD_WIN | MOD_ALT, 0x5Au)]            // 'Z'
        public void ParsesCombinations(string text, uint modifiers, uint key)
        {
            Assert.IsTrue(Hotkey.TryParse(text, out uint m, out uint k));
            Assert.AreEqual(modifiers, m);
            Assert.AreEqual(key, k);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("B")]                // no modifier: would take a plain key from every program
        [DataRow("Ctrl+Alt")]         // no key
        [DataRow("Ctrl+B+C")]         // two keys
        [DataRow("Ctrl+Ctrl+B")]      // the same modifier twice
        [DataRow("Ctrl+F25")]
        [DataRow("Ctrl+F0")]
        [DataRow("Ctrl+F01")]
        [DataRow("Ctrl+Space")]
        [DataRow("Ctrl+ö")]
        [DataRow("Ctrl++B")]
        public void RejectsAnythingElse(string text)
        {
            Assert.IsFalse(Hotkey.TryParse(text, out _, out _));
        }
    }
}
