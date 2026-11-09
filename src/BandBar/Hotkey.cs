using System;
using static BandBar.NativeMethods;

namespace BandBar
{
    /// <summary>
    /// The optional show/hide hotkey (docs/DESIGN.md §6.1.1), written like "Ctrl+Alt+B": modifiers Ctrl, Alt, Shift,
    /// Win (at least one), then one letter, digit or F1–F24.
    /// </summary>
    internal static class Hotkey
    {
        public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            foreach (string raw in text.Split('+'))
            {
                string part = raw.Trim().ToUpperInvariant();
                uint modifier = ModifierOf(part);
                if (modifier != 0)
                {
                    if ((modifiers & modifier) != 0)
                        return false; // the same modifier twice
                    modifiers |= modifier;
                    continue;
                }

                uint key = KeyOf(part);
                if (key == 0 || virtualKey != 0)
                    return false; // unknown part, or a second key
                virtualKey = key;
            }

            // A plain key would be taken away from games and every other program.
            return modifiers != 0 && virtualKey != 0;
        }

        private static uint ModifierOf(string part)
        {
            switch (part)
            {
                case "CTRL": case "CONTROL": return MOD_CONTROL;
                case "ALT": return MOD_ALT;
                case "SHIFT": return MOD_SHIFT;
                case "WIN": case "WINDOWS": return MOD_WIN;
                default: return 0;
            }
        }

        private static uint KeyOf(string part)
        {
            if (part.Length == 1 && ((part[0] >= 'A' && part[0] <= 'Z') || (part[0] >= '0' && part[0] <= '9')))
                return part[0]; // the virtual-key codes of letters and digits are their ASCII codes
            if (part.Length >= 2 && part[0] == 'F' && int.TryParse(part.Substring(1), out int n) && n >= 1 && n <= 24
                && part.Substring(1) == n.ToString(System.Globalization.CultureInfo.InvariantCulture))
                return (uint)(0x70 + n - 1); // VK_F1 = 0x70
            return 0;
        }
    }
}
