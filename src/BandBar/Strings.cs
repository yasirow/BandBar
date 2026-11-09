using System.Globalization;
using BandBar.Api;

namespace BandBar
{
    /// <summary>UI text: Turkish when the Windows display language is Turkish, English otherwise.</summary>
    internal static class Strings
    {
        private static readonly bool Tr = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";

        public static string Exit => Tr ? "Çıkış" : "Exit";
        public static string ResetPosition => Tr ? "Konumu sıfırla" : "Reset position";
        public static string OpenSettings => Tr ? "Ayar dosyasını aç" : "Open settings file";
        public static string OpenLogFolder => Tr ? "Log klasörünü aç" : "Open log folder";
        public static string DragHint => Tr ? "Taşımak için sürükleyin, menü için sağ tıklayın" : "Drag to move, right-click for the menu";
        public static string OpenNetLimiter => Tr ? "NetLimiter'ı aç" : "Open NetLimiter";
        public static string ReleasesPage => Tr ? "Sürüm sayfası (GitHub)" : "Releases page (GitHub)";
        public static string NotConnected => Tr ? "(NetLimiter'a bağlı değil)" : "(not connected to NetLimiter)";

        // NetLimiter's own feature names, as in its interface (like "Zone").
        public static string Limiter => "Limiter";
        public static string Blocker => "Blocker";
        public static string Priorities => "Priorities";

        public static string BlockerWaiting(int count) => Tr
            ? "Blocker: " + count + " bağlantı onay bekliyor (tıklayınca NetLimiter açılır)"
            : "Blocker: " + count + (count == 1 ? " connection" : " connections") + " waiting for approval (click to open NetLimiter)";
        public static string Zone => "Zone";
        public static string Elevated => Tr ? "Yönetici olarak çalışıyor (gerekli değil)" : "Running as administrator (not needed)";

        /// <summary>Full sentence for the tooltip.</summary>
        public static string Describe(LinkStatus status)
        {
            switch (status)
            {
                case LinkStatus.Connecting: return Tr ? "Bağlanıyor…" : "Connecting…";
                case LinkStatus.Connected: return Tr ? "Bağlı" : "Connected";
                case LinkStatus.AccessDenied: return Tr ? "NetLimiter erişim izni vermedi (Monitor hakkı gerekli)" : "NetLimiter denied access (Monitor right required)";
                case LinkStatus.NotInstalled: return Tr ? "NetLimiter bulunamadı" : "NetLimiter not found";
                case LinkStatus.ApiUntrusted: return Tr ? "NetLimiter API dosyalarının imzası doğrulanamadı" : "NetLimiter API files failed signature verification";
                case LinkStatus.ApiIncompatible: return Tr ? "Kurulu NetLimiter sürümü uyumsuz" : "Installed NetLimiter version is not compatible";
                default: return Tr ? "NetLimiter servisine bağlanılamıyor" : "Cannot reach the NetLimiter service";
            }
        }

        /// <summary>Short text drawn in the bar instead of the speeds.</summary>
        public static string Short(LinkStatus status)
        {
            switch (status)
            {
                case LinkStatus.Connecting: return "…";
                case LinkStatus.AccessDenied: return Tr ? "izin yok" : "denied";
                case LinkStatus.NotInstalled: return "NL?";
                case LinkStatus.ApiUntrusted:
                case LinkStatus.ApiIncompatible: return "API?";
                default: return "—";
            }
        }
    }
}
