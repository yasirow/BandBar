using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using static BandBar.NativeMethods;

namespace BandBar
{
    /// <summary>
    /// <c>fullscreenExeList</c> (docs/DESIGN.md §6.1): programs that hide the bar while they are in front. A program is
    /// recognised by its image name from a snapshot of Windows' process list; no handle to it is ever opened.
    /// </summary>
    internal static class AppList
    {
        internal const int MaxEntries = 64;

        /// <summary>File names only, lower case, without ".exe"; empty or unusable entries dropped; at most 64.</summary>
        public static string[] Normalize(IEnumerable<string> entries)
        {
            if (entries == null)
                return Array.Empty<string>();
            var result = new List<string>();
            foreach (string entry in entries)
            {
                string name = NameOf(entry);
                if (name.Length > 0 && !result.Contains(name))
                    result.Add(name);
                if (result.Count == MaxEntries)
                    break;
            }
            return result.ToArray();
        }

        /// <summary>Whether a program's image name (with or without path and ".exe") is in a normalized list.</summary>
        public static bool Contains(string[] normalized, string imageName) =>
            normalized != null && normalized.Length > 0 && imageName != null && normalized.Contains(NameOf(imageName));

        /// <summary>
        /// The image name ("game.exe") of a process, from a snapshot of the process list, or null. The snapshot is our
        /// own object; the process itself is not opened.
        /// </summary>
        public static string ImageNameOf(uint processId)
        {
            IntPtr snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == IntPtr.Zero || snapshot == INVALID_HANDLE_VALUE)
                return null;
            try
            {
                var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf(typeof(PROCESSENTRY32W)) };
                for (bool ok = Process32FirstW(snapshot, ref entry); ok; ok = Process32NextW(snapshot, ref entry))
                {
                    if (entry.th32ProcessID == processId)
                        return entry.szExeFile;
                }
                return null;
            }
            finally
            {
                CloseHandle(snapshot);
            }
        }

        private static string NameOf(string entry)
        {
            string name = (entry ?? "").Trim().Trim('"');
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                return "";
            name = Path.GetFileName(name);
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - 4);
            return name.Trim().ToLowerInvariant();
        }
    }
}
