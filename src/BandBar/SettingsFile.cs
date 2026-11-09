using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BandBar
{
    /// <summary>
    /// The only code that writes settings.json (docs/DESIGN.md §8.1), and only on a user action. It changes just the
    /// key concerned and keeps every other key, unknown keys and their order. An invalid or oversized file is never
    /// overwritten. Writes go to a temporary file in the same folder that is then swapped in, so the file is always
    /// either the old or the new version.
    /// </summary>
    internal static class SettingsFile
    {
        internal const string TempSuffix = ".tmp";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Sets a top-level number key, or sets it to null when <paramref name="value"/> is null. A file that does not
        /// exist yet is created with every key at its default, so the user finds all of them in it.
        /// </summary>
        /// <returns>False when the file could not be changed (the reason is logged).</returns>
        public static bool SetNumber(string dataDir, string key, int? value) =>
            SetMember(dataDir, key, "number", value?.ToString(CultureInfo.InvariantCulture));

        /// <summary>Like <see cref="SetNumber"/>, for a text value.</summary>
        public static bool SetString(string dataDir, string key, string value) => SetMember(dataDir, key, "string", value);

        private static bool SetMember(string dataDir, string key, string type, string text)
        {
            string path = Settings.FilePath(dataDir);
            if (path == null)
                return false;
            try
            {
                if (!TryReadTree(path, out XElement root, out bool isNew))
                    return false;

                XElement member = root.Elements().FirstOrDefault(e => MemberName(e) == key);
                if (text == null)
                {
                    // Already unset (no file, no key, or null): leave the file, or its absence, as it is.
                    if (isNew || member == null || (string)member.Attribute("type") == "null")
                        return true;
                    member.RemoveNodes();
                    member.SetAttributeValue("type", "null");
                }
                else if (member == null)
                {
                    root.Add(new XElement(key, new XAttribute("type", type), text));
                }
                else
                {
                    member.ReplaceNodes(text);
                    member.SetAttributeValue("type", type);
                }

                WriteTree(path, root);
                if (isNew)
                    Log.Info("Created settings.json with the default settings");
                return true;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warn("settings.json could not be written", ex);
                return false;
            }
        }

        /// <summary>Creates settings.json with every key at its default when there is none yet.</summary>
        /// <returns>True when the file exists afterwards.</returns>
        public static bool EnsureExists(string dataDir)
        {
            string path = Settings.FilePath(dataDir);
            if (path == null)
                return false;
            try
            {
                if (!File.Exists(path))
                {
                    WriteTree(path, DefaultsTree());
                    Log.Info("Created settings.json with the default settings");
                }
                return true;
            }
            catch (Exception ex) when (IsFileError(ex))
            {
                Log.Warn("settings.json could not be created", ex);
                return false;
            }
        }

        // JSON as XML (the .NET Framework's own JSON reader and writer): <root type="object"><key type="number">1</key>…
        /// <param name="isNew">True when there is no file yet (or it is blank): the tree then holds every default.</param>
        private static bool TryReadTree(string path, out XElement root, out bool isNew)
        {
            root = null;
            isNew = false;
            switch (Settings.ReadText(path, out string json))
            {
                case SettingsLoad.Loaded:
                    break;
                case SettingsLoad.Invalid:
                    Log.Warn("Not changing settings.json, because it cannot be used as it is");
                    return false;
                default:
                    throw new IOException("settings.json could not be read.");
            }

            if (json == null)
            {
                root = DefaultsTree();
                isNew = true;
                return true;
            }

            try
            {
                using (var stream = new MemoryStream(Utf8NoBom.GetBytes(json)))
                using (XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(stream, XmlDictionaryReaderQuotas.Max))
                    root = XElement.Load(reader);
            }
            catch (XmlException ex)
            {
                Log.Warn("Not changing settings.json, because it cannot be parsed", ex);
                return false;
            }

            if ((string)root.Attribute("type") != "object")
                return false;

            // The reader gives null values and empty objects/arrays an empty text node, which the writer refuses.
            foreach (XElement e in root.DescendantsAndSelf())
            {
                string type = (string)e.Attribute("type");
                if ((type == "null" || type == "object" || type == "array") && !e.HasElements)
                    e.RemoveNodes();
            }
            return true;
        }

        private static void WriteTree(string path, XElement root)
        {
            string temp = path + TempSuffix;
            try
            {
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (XmlDictionaryWriter writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Utf8NoBom, false, true, "  "))
                    {
                        root.WriteTo(writer);
                        writer.Flush();
                    }
                    byte[] newline = Utf8NoBom.GetBytes(Environment.NewLine);
                    stream.Write(newline, 0, newline.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temp, path, null, ignoreMetadataErrors: true);
                else
                    File.Move(temp, path);
            }
            finally
            {
                try
                {
                    if (File.Exists(temp))
                        File.Delete(temp);
                }
                catch (Exception ex) when (IsFileError(ex))
                {
                    Log.Debug("Temporary settings file not removed", ex);
                }
            }
        }

        // Every key, in the documented order (docs/DESIGN.md §8.1).
        private static XElement DefaultsTree()
        {
            var d = new Settings();
            return Object("root",
                Number("schemaVersion", d.SchemaVersion.ToString(CultureInfo.InvariantCulture)),
                Null("zoneFilterId"),
                Number("unitBase", d.UnitBase.ToString(CultureInfo.InvariantCulture)),
                Number("decimals", d.Decimals.ToString(CultureInfo.InvariantCulture)),
                String("theme", d.Theme),
                Object("colors",
                    String("upload", Settings.ColorSettings.DefaultUpload),
                    String("download", Settings.ColorSettings.DefaultDownload),
                    Null("text"),
                    Null("background")),
                Number("opacity", d.Opacity.ToString("R", CultureInfo.InvariantCulture)),
                Null("anchorOffsetDip"),
                Boolean("hideOnFullscreen", d.HideOnFullscreen),
                new XElement("fullscreenExeList", new XAttribute("type", "array")),
                Boolean("followAutoHideTaskbar", d.FollowAutoHideTaskbar),
                Null("hotkey"),
                String("logLevel", d.LogLevel));
        }

        // A member whose name is not a valid XML name is mapped to <item item="name"> by the JSON reader.
        private static string MemberName(XElement e) =>
            e.Name.LocalName == "item" && e.Attribute("item") != null ? (string)e.Attribute("item") : e.Name.LocalName;

        private static XElement Object(string name, params XElement[] members) =>
            new XElement(name, new XAttribute("type", "object"), members);

        private static XElement Number(string name, string value) => new XElement(name, new XAttribute("type", "number"), value);

        private static XElement String(string name, string value) => new XElement(name, new XAttribute("type", "string"), value);

        private static XElement Boolean(string name, bool value) =>
            new XElement(name, new XAttribute("type", "boolean"), value ? "true" : "false");

        private static XElement Null(string name) => new XElement(name, new XAttribute("type", "null"));

        private static bool IsFileError(Exception ex) =>
            ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException;
    }
}
