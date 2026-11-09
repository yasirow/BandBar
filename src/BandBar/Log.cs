using System;
using System.IO;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;

namespace BandBar
{
    /// <summary>
    /// Small rotating text log (256 KB + one backup). Messages are scrubbed of profile paths, the user name and the
    /// machine name because users may attach the log to a public issue (docs/DESIGN.md).
    /// </summary>
    internal static class Log
    {
        private enum Level { Debug, Info, Warn, Error }

        private const long MaxBytes = 256 * 1024;
        private static readonly object Gate = new object();
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static string path;
        private static Level minLevel = Level.Info;
        private static LogScrubber scrubber = new LogScrubber(Array.Empty<(string, string)>(), null, null);

        /// <summary>The log file, or null when there is none.</summary>
        public static string FilePath => path;

        /// <summary>Whether debug lines are written; lets callers skip work that only feeds a debug line.</summary>
        public static bool IsDebugEnabled => path != null && minLevel <= Level.Debug;

        public static void Init(string dataDir)
        {
            path = dataDir == null ? null : Path.Combine(dataDir, AppPaths.LogFileName);
            scrubber = LogScrubber.ForCurrentUser();
        }

        public static void SetLevel(string level)
        {
            switch ((level ?? "").Trim().ToLowerInvariant())
            {
                case "debug": minLevel = Level.Debug; break;
                case "warn": minLevel = Level.Warn; break;
                case "error": minLevel = Level.Error; break;
                default: minLevel = Level.Info; break;
            }
        }

        public static void Debug(string message, Exception ex = null) => Write(Level.Debug, message, ex);
        public static void Info(string message, Exception ex = null) => Write(Level.Info, message, ex);
        public static void Warn(string message, Exception ex = null) => Write(Level.Warn, message, ex);
        public static void Error(string message, Exception ex = null) => Write(Level.Error, message, ex);

        private static void Write(Level level, string message, Exception ex)
        {
            if (level < minLevel || path == null)
                return;

            var sb = new StringBuilder(128);
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" [").Append(level.ToString().ToUpperInvariant()).Append("] ").Append(message);
            if (ex != null)
                sb.Append(" | ").Append(Describe(ex, withStack: level == Level.Error));
            string line = scrubber.Scrub(sb.ToString()) + Environment.NewLine;

            lock (Gate)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string backup = path + ".1";
                        File.Delete(backup);
                        File.Move(path, backup);
                    }
                    File.AppendAllText(path, line, Utf8NoBom);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is SecurityException)
                {
                    // Logging must never take the app down.
                }
            }
        }

        private static string Describe(Exception ex, bool withStack)
        {
            var sb = new StringBuilder();
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (sb.Length > 0)
                    sb.Append(" --> ");
                sb.Append(e.GetType().FullName).Append(": ").Append(e.Message);
            }
            if (withStack && ex.StackTrace != null)
                sb.Append(Environment.NewLine).Append(ex.StackTrace);
            return sb.ToString();
        }

    }

    /// <summary>
    /// Masks personal details before a log line is written (docs/DESIGN.md §8.2): profile, AppData and temp paths,
    /// the computer name as a whole word, and the user name as a path segment.
    /// </summary>
    internal sealed class LogScrubber
    {
        private readonly (string Find, string Replace)[] pairs;
        private readonly Regex machine;
        private readonly Regex userSegment;

        /// <param name="folders">Paths and their placeholders, most specific first.</param>
        public LogScrubber(System.Collections.Generic.IEnumerable<(string Find, string Replace)> folders, string machineName, string userName)
        {
            var list = new System.Collections.Generic.List<(string, string)>();
            foreach ((string find, string replace) in folders)
            {
                string trimmed = (find ?? "").TrimEnd('\\', '/');
                if (trimmed.Length > 0)
                    list.Add((trimmed, replace));
            }
            pairs = list.ToArray();

            // The computer name only as a whole word, so a name such as "WIN" does not turn "Windows" into "<machine>dows".
            if (!string.IsNullOrEmpty(machineName))
                machine = new Regex(@"(?<![A-Za-z0-9_-])" + Regex.Escape(machineName) + @"(?![A-Za-z0-9_-])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            // The user name only where it can be one: a path segment or DOMAIN\user. A short name such as "A"
            // must not be replaced inside ordinary words.
            if (!string.IsNullOrEmpty(userName))
                userSegment = new Regex(@"(?<=[\\/])" + Regex.Escape(userName) + @"(?=[\\/]|$|\s)",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        public static LogScrubber ForCurrentUser()
        {
            var folders = new System.Collections.Generic.List<(string, string)>
            {
                (Path.GetTempPath(), "%TEMP%"),
                (Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
                (Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "%APPDATA%"),
                (Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "%USERPROFILE%"),
            };
            return new LogScrubber(folders, Environment.MachineName, Environment.UserName);
        }

        public string Scrub(string text)
        {
            foreach ((string find, string replace) in pairs)
                text = Replace(text, find, replace);
            if (machine != null)
                text = machine.Replace(text, "<machine>");
            return userSegment == null ? text : userSegment.Replace(text, "<user>");
        }

        private static string Replace(string text, string find, string replace)
        {
            int i = text.IndexOf(find, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
                return text;
            var sb = new StringBuilder(text.Length);
            int start = 0;
            while (i >= 0)
            {
                sb.Append(text, start, i - start).Append(replace);
                start = i + find.Length;
                i = text.IndexOf(find, start, StringComparison.OrdinalIgnoreCase);
            }
            return sb.Append(text, start, text.Length - start).ToString();
        }
    }
}
