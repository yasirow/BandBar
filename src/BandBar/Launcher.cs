using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace BandBar
{
    /// <summary>
    /// Starts another program, only for a menu item the user chose and only with targets the app builds itself
    /// (docs/DESIGN.md §7.4). Nothing is elevated, nothing is kept: the started program runs on its own.
    /// </summary>
    internal static class Launcher
    {
        /// <summary>Our own settings.json in the user's .json editor, or Notepad; created with the defaults if missing.</summary>
        public static void OpenSettingsFile(string dataDir)
        {
            string path = Settings.FilePath(dataDir);
            if (path == null || !SettingsFile.EnsureExists(dataDir))
                return;

            if (Start(new ProcessStartInfo(path) { Verb = "open" }, logFailure: false))
                return;

            // No program is associated with .json files: Windows' own Notepad.
            string notepad = Path.Combine(Environment.SystemDirectory, "notepad.exe");
            Start(new ProcessStartInfo(notepad, Quote(path)), logFailure: true);
        }

        /// <summary>File Explorer showing our own data folder, with the log selected when it exists.</summary>
        public static void OpenLogFolder(string dataDir)
        {
            if (dataDir == null)
                return;
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            string log = Log.FilePath;
            string arguments = log != null && File.Exists(log) ? "/select," + Quote(log) : Quote(dataDir);
            Start(new ProcessStartInfo(explorer, arguments), logFailure: true);
        }

        /// <summary>NetLimiter's own client, only from its verified install folder with a valid Locktime signature.</summary>
        public static void OpenNetLimiter()
        {
            string path = Api.NlApiLoader.VerifiedClientAppPath();
            if (path != null)
                Start(new ProcessStartInfo(path), logFailure: true);
        }

        /// <summary>The project's fixed Releases page in the default browser; BandBar itself does not connect.</summary>
        public static void OpenReleasesPage() =>
            Start(new ProcessStartInfo(ReleasesUrl), logFailure: true);

        internal const string ReleasesUrl = "https://github.com/yasirow/BandBar/releases";

        private static bool Start(ProcessStartInfo info, bool logFailure)
        {
            // Through the shell, as a double-click would; no error dialog of its own, never elevated.
            info.UseShellExecute = true;
            info.ErrorDialog = false;
            try
            {
                using (Process.Start(info))
                {
                    // Not kept, not waited for.
                }
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                if (logFailure)
                    Log.Warn("Could not start " + Path.GetFileName(info.FileName), ex);
                else
                    Log.Debug("Could not start " + Path.GetFileName(info.FileName), ex);
                return false;
            }
        }

        private static string Quote(string path) => "\"" + path + "\"";
    }
}
