using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace BandBar
{
    /// <summary>
    /// Reads, never writes, how Windows colours the taskbar, so the text stays readable (docs/DESIGN.md §4).
    /// Re-read when Windows broadcasts a theme or accent colour change; never polled.
    /// </summary>
    internal static class SystemTheme
    {
        private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string DwmKey = @"Software\Microsoft\Windows\DWM";

        /// <summary>
        /// True when the taskbar is light: the light system theme, or an accent-coloured taskbar whose accent is light.
        /// </summary>
        public static bool TaskbarIsLight()
        {
            try
            {
                using (RegistryKey personalize = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                {
                    if (personalize?.GetValue("ColorPrevalence") is int prevalence && prevalence == 1)
                        return AccentIsLight();
                    return personalize?.GetValue("SystemUsesLightTheme") is int light && light == 1;
                }
            }
            catch (Exception ex) when (ex is SecurityException || ex is IOException || ex is UnauthorizedAccessException)
            {
                Log.Debug("Taskbar theme not readable; assuming dark", ex);
                return false;
            }
        }

        /// <summary>
        /// The text colour of the active Windows contrast theme, or null when no contrast theme is on. Read only:
        /// <c>SPI_GETHIGHCONTRAST</c> and <c>GetSysColor</c>, never a setter.
        /// </summary>
        public static Color? ContrastThemeTextColor()
        {
            var data = new NativeMethods.HIGHCONTRAST { CbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.HIGHCONTRAST)) };
            if (!NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETHIGHCONTRAST, data.CbSize, ref data, 0)
                || (data.DwFlags & NativeMethods.HCF_HIGHCONTRASTON) == 0)
                return null;
            uint bgr = NativeMethods.GetSysColor(NativeMethods.COLOR_WINDOWTEXT); // 0x00BBGGRR
            return Color.FromArgb((int)(bgr & 0xFF), (int)((bgr >> 8) & 0xFF), (int)((bgr >> 16) & 0xFF));
        }

        private static bool AccentIsLight()
        {
            using (RegistryKey dwm = Registry.CurrentUser.OpenSubKey(DwmKey))
            {
                if (!(dwm?.GetValue("AccentColor") is int abgr))
                    return false;
                int r = abgr & 0xFF, g = (abgr >> 8) & 0xFF, b = (abgr >> 16) & 0xFF;
                double luminance = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                return luminance > 0.6;
            }
        }
    }
}
