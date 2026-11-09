using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Xml;

namespace BandBar
{
    internal enum SettingsLoad
    {
        /// <summary>The file was read, or there is none (the defaults).</summary>
        Loaded,
        /// <summary>The file is not valid JSON, not an object, or larger than 64 KB.</summary>
        Invalid,
        /// <summary>The file could not be opened right now (for example an editor is still writing it).</summary>
        Unreadable,
    }

    /// <summary>
    /// settings.json (docs/DESIGN.md §8.1). Read at start-up and whenever the file changes; the few values the app
    /// writes itself go through <see cref="SettingsFile"/>, which changes only the key concerned.
    /// </summary>
    [DataContract]
    internal sealed class Settings
    {
        internal const long MaxFileBytes = 64 * 1024;

        [DataMember(Name = "schemaVersion")] public int SchemaVersion;
        [DataMember(Name = "zoneFilterId")] public string ZoneFilterId;
        [DataMember(Name = "unitBase")] public int UnitBase;
        [DataMember(Name = "decimals")] public int Decimals;
        [DataMember(Name = "theme")] public string Theme;
        [DataMember(Name = "colors")] public ColorSettings Colors;
        [DataMember(Name = "opacity")] public double Opacity;
        /// <summary>Null: just before the notification area (docs/DESIGN.md §5.1).</summary>
        [DataMember(Name = "anchorOffsetDip")] public int? AnchorOffsetDip;
        [DataMember(Name = "hideOnFullscreen")] public bool HideOnFullscreen;
        /// <summary>With an auto-hiding taskbar, show the bar only while the taskbar is shown (docs/DESIGN.md §5.2).</summary>
        [DataMember(Name = "followAutoHideTaskbar")] public bool FollowAutoHideTaskbar;
        [DataMember(Name = "logLevel")] public string LogLevel;

        public Settings() => SetDefaults();

        public Color DownloadColor { get; private set; }
        public Color UploadColor { get; private set; }

        /// <summary>Text colour from <c>colors.text</c>; null means it follows <see cref="Theme"/>.</summary>
        public Color? TextColorOverride { get; private set; }

        /// <summary>Null: transparent, the taskbar shows through (default).</summary>
        public Color? BackgroundColor { get; private set; }

        public static string FilePath(string dataDir) =>
            dataDir == null ? null : Path.Combine(dataDir, AppPaths.SettingsFileName);

        /// <summary>The built-in defaults, ready to use.</summary>
        public static Settings Defaults()
        {
            var settings = new Settings();
            settings.Normalize();
            return settings;
        }

        /// <summary>Start-up: a file that cannot be used means the defaults.</summary>
        public static Settings Load(string dataDir)
        {
            if (TryLoad(dataDir, out Settings settings) == SettingsLoad.Loaded)
                return settings;
            Log.Warn("Using the default settings");
            return Defaults();
        }

        /// <param name="settings">The file's settings, or the defaults when there is no file; null unless loaded.</param>
        public static SettingsLoad TryLoad(string dataDir, out Settings settings)
        {
            settings = null;
            SettingsLoad state = ReadText(FilePath(dataDir), out string json);
            if (state != SettingsLoad.Loaded)
                return state;
            if (json == null)
            {
                settings = Defaults();
                return SettingsLoad.Loaded;
            }

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    settings = new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream) as Settings;
            }
            catch (Exception ex) when (ex is SerializationException || ex is XmlException)
            {
                // Valid JSON, but a value has the wrong type (for example "decimals": "two").
                Log.Warn("settings.json has a value of the wrong type", ex);
                return SettingsLoad.Invalid;
            }
            if (settings == null)
                return SettingsLoad.Invalid;
            settings.Normalize();
            return SettingsLoad.Loaded;
        }

        /// <summary>
        /// The file's text when it is one complete JSON object of at most 64 KB; null text (and Loaded) when there is no
        /// file or it is blank. Logs why a file cannot be used. Shared by reading and by <see cref="SettingsFile"/>.
        /// </summary>
        internal static SettingsLoad ReadText(string path, out string json)
        {
            json = null;
            try
            {
                if (path == null || !File.Exists(path))
                    return SettingsLoad.Loaded;

                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length > MaxFileBytes)
                    {
                        Log.Warn("settings.json is larger than 64 KB");
                        return SettingsLoad.Invalid;
                    }
                    // UTF-8 unless a byte order mark says otherwise (Notepad's "Unicode" is UTF-16).
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true))
                        json = reader.ReadToEnd();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                Log.Warn("settings.json could not be read", ex);
                return SettingsLoad.Unreadable;
            }

            if (StrictJson.IsBlank(json))
            {
                json = null;
                return SettingsLoad.Loaded;
            }
            if (!StrictJson.IsObject(json))
            {
                json = null;
                Log.Warn("settings.json is not one complete JSON object");
                return SettingsLoad.Invalid;
            }
            return SettingsLoad.Loaded;
        }

        // DataContractJsonSerializer skips constructors, so members missing from the file would stay zero/null.
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) => SetDefaults();

        private void SetDefaults()
        {
            SchemaVersion = 1;
            ZoneFilterId = null;
            UnitBase = 1024;
            Decimals = 1;
            Theme = "auto";
            Colors = new ColorSettings();
            Opacity = 0.85;
            AnchorOffsetDip = null;
            HideOnFullscreen = true;
            FollowAutoHideTaskbar = true;
            LogLevel = "info";
        }

        private void Normalize()
        {
            if (UnitBase != 1000)
                UnitBase = 1024;
            Decimals = Clamp(Decimals, 0, 3);
            Opacity = double.IsNaN(Opacity) ? 0.85 : Math.Max(0.3, Math.Min(1.0, Opacity));
            if (AnchorOffsetDip.HasValue)
                AnchorOffsetDip = Clamp(AnchorOffsetDip.Value, 0, 10000);
            Colors = Colors ?? new ColorSettings();

            string theme = (Theme ?? "").Trim().ToLowerInvariant();
            Theme = theme == "dark" || theme == "light" ? theme : "auto";
            DownloadColor = ParseColor(Colors.Download) ?? Color.FromArgb(0x2E, 0xCC, 0x71);
            UploadColor = ParseColor(Colors.Upload) ?? Color.FromArgb(0xE7, 0x4C, 0x3C);
            TextColorOverride = ParseColor(Colors.Text);
            Color? background = ParseColor(Colors.Background);
            BackgroundColor = background.HasValue ? Color.FromArgb((int)Math.Round(Opacity * 255), background.Value) : (Color?)null;
        }

        /// <summary>Text colour for the current theme: dark text on a light taskbar, white text otherwise.</summary>
        public Color ResolveTextColor()
        {
            if (TextColorOverride.HasValue)
                return TextColorOverride.Value;
            bool light = Theme == "light" || (Theme == "auto" && SystemTheme.TaskbarIsLight());
            return light ? Color.FromArgb(0x1A, 0x1A, 0x1A) : Color.White;
        }

        private static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static Color? ParseColor(string value)
        {
            if (value != null && value.Length == 7 && value[0] == '#'
                && int.TryParse(value.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
                return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            return null;
        }

        [DataContract]
        internal sealed class ColorSettings
        {
            internal const string DefaultDownload = "#2ECC71";
            internal const string DefaultUpload = "#E74C3C";

            [DataMember(Name = "download")] public string Download = DefaultDownload;
            [DataMember(Name = "upload")] public string Upload = DefaultUpload;
            // Null means "from the theme"; only the JSON file sets them.
            [DataMember(Name = "text")] public string Text = null;
            [DataMember(Name = "background")] public string Background = null;
        }
    }
}
