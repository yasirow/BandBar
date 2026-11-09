using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security;
using Microsoft.Win32;

namespace BandBar.Api
{
    internal enum ApiLoadFailure
    {
        None,
        NotInstalled,
        Untrusted,
        Missing,
    }

    /// <summary>
    /// Loads the NetLimiter API from the installed NetLimiter, read-only, after verifying each file's signature
    /// (docs/DESIGN.md). The build output contains no NetLimiter or third-party DLL, so every one of these names
    /// reaches <see cref="OnResolve"/>.
    /// </summary>
    internal static class NlApiLoader
    {
        private const string LocktimeOrg = "Locktime Software s.r.o.";
        private const string MicrosoftOrg = "Microsoft Corporation";

        // What NetLimiter.dll needs at run time (measured against NetLimiter 5.3.28) and who must have signed it.
        // Nothing else is ever loaded from NetLimiter's folder.
        private static readonly Dictionary<string, string> Allowed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NetLimiter"] = LocktimeOrg,
            ["NLInterop"] = LocktimeOrg,
            ["CoreLibNet"] = LocktimeOrg,
            ["VirusTotalNet"] = LocktimeOrg, // referenced by CoreLibNet's types; loaded, never called
            ["Microsoft.Extensions.Logging.Abstractions"] = MicrosoftOrg,
            ["System.Memory"] = MicrosoftOrg,
            ["System.Buffers"] = MicrosoftOrg,
            ["System.Runtime.CompilerServices.Unsafe"] = MicrosoftOrg,
        };

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Assembly> Loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Refused = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool installDirSearched;
        private static string installDir;

        [ThreadStatic]
        private static string loadingPath;

        public static ApiLoadFailure LastFailure { get; private set; }

        /// <summary>
        /// NetLimiter's own client program, for the menu's "Open NetLimiter" (docs/DESIGN.md §7.4): from the folder
        /// found for the API, and only with a valid Authenticode signature from Locktime — the same check as for the API
        /// files. Null otherwise (the reason is logged).
        /// </summary>
        public static string VerifiedClientAppPath()
        {
            string dir;
            lock (Gate)
                dir = GetInstallDir();
            if (dir == null)
            {
                Log.Warn("NetLimiter installation not found; cannot open NetLimiter");
                return null;
            }

            string path = Path.GetFullPath(Path.Combine(dir, "NLClientApp.exe"));
            if (!File.Exists(path))
            {
                Log.Warn("NLClientApp.exe not present in the NetLimiter folder");
                return null;
            }
            int trust = Authenticode.Verify(path);
            string organization = trust == 0 ? Authenticode.GetSignerOrganization(path) : null;
            if (trust != 0 || !string.Equals(organization, LocktimeOrg, StringComparison.Ordinal))
            {
                Log.Warn("NLClientApp.exe not started: signature not valid or not from Locktime (0x" + trust.ToString("X8") + ")");
                return null;
            }
            return path;
        }

        public static void Install()
        {
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            AppDomain.CurrentDomain.AssemblyResolve += OnResolve;
        }

        private static Assembly OnResolve(object sender, ResolveEventArgs args)
        {
            string name;
            try
            {
                name = new AssemblyName(args.Name).Name;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FileLoadException)
            {
                return null;
            }

            if (!Allowed.TryGetValue(name, out string expectedOrganization))
                return null;

            lock (Gate)
            {
                if (Loaded.TryGetValue(name, out Assembly loaded))
                    return loaded;
                if (Refused.Contains(name))
                    return null;

                Assembly assembly = TryLoad(name, expectedOrganization);
                if (assembly == null)
                    Refused.Add(name);
                else
                    Loaded[name] = assembly;
                return assembly;
            }
        }

        private static Assembly TryLoad(string name, string expectedOrganization)
        {
            string dir = GetInstallDir();
            if (dir == null)
            {
                Refuse(ApiLoadFailure.NotInstalled, "NetLimiter installation not found");
                return null;
            }

            string path = Path.GetFullPath(Path.Combine(dir, name + ".dll"));
            if (!File.Exists(path))
            {
                Refuse(name == "NetLimiter" ? ApiLoadFailure.NotInstalled : ApiLoadFailure.Missing,
                       "API assembly " + name + ".dll not present in the NetLimiter folder");
                return null;
            }

            int trust = Authenticode.Verify(path);
            if (trust != 0)
            {
                Refuse(ApiLoadFailure.Untrusted, "API assembly " + name + ".dll refused: signature not valid (0x" + trust.ToString("X8") + ")");
                return null;
            }

            string organization = Authenticode.GetSignerOrganization(path);
            if (!string.Equals(organization, expectedOrganization, StringComparison.Ordinal))
            {
                Refuse(ApiLoadFailure.Untrusted, "API assembly " + name + ".dll refused: signed by '" + organization
                                                 + "', expected '" + expectedOrganization + "'");
                return null;
            }

            loadingPath = path;
            try
            {
                Assembly assembly = Assembly.LoadFile(path);
                Log.Info("API assembly loaded: " + name + " " + assembly.GetName().Version);
                return assembly;
            }
            catch (Exception ex) when (ex is IOException || ex is BadImageFormatException || ex is SecurityException)
            {
                Refuse(ApiLoadFailure.Missing, "API assembly " + name + ".dll could not be loaded: " + ex.GetType().Name);
                return null;
            }
            finally
            {
                loadingPath = null;
            }
        }

        private static void Refuse(ApiLoadFailure failure, string message)
        {
            LastFailure = failure;
            Log.Warn(message);
        }

        // Defence in depth: an allowed name loaded by anything other than TryLoad (for example a NetLimiter.dll
        // dropped next to the executable) means the check above was bypassed. Stop rather than run it.
        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            Assembly assembly = args.LoadedAssembly;
            if (assembly.IsDynamic || assembly.GlobalAssemblyCache)
                return;
            string name = assembly.GetName().Name;
            if (!Allowed.ContainsKey(name))
                return;
            if (loadingPath != null && string.Equals(assembly.Location, loadingPath, StringComparison.OrdinalIgnoreCase))
                return;

            Log.Error("API assembly " + name + " was loaded from an unexpected location; exiting");
            Environment.Exit(2);
        }

        private static string GetInstallDir()
        {
            if (!installDirSearched)
            {
                installDir = FindInstallDir();
                installDirSearched = true;
            }
            return installDir;
        }

        // Read-only: the NetLimiter uninstall entry's InstallLocation, then the default location.
        private static string FindInstallDir()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                    using (RegistryKey uninstall = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null)
                            continue;
                        foreach (string subKeyName in uninstall.GetSubKeyNames())
                        {
                            using (RegistryKey entry = uninstall.OpenSubKey(subKeyName))
                            {
                                if (entry?.GetValue("DisplayName") is string displayName
                                    && displayName.StartsWith("NetLimiter", StringComparison.OrdinalIgnoreCase)
                                    && entry.GetValue("Publisher") is string publisher
                                    && publisher.IndexOf("Locktime", StringComparison.OrdinalIgnoreCase) >= 0
                                    && entry.GetValue("InstallLocation") is string location
                                    && ContainsApi(location))
                                    return location.Trim().Trim('"');
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
                {
                    Log.Debug("Uninstall registry view not readable", ex);
                }
            }

            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Locktime Software", "NetLimiter");
            return ContainsApi(fallback) ? fallback : null;
        }

        private static bool ContainsApi(string dir)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir.Trim().Trim('"'), "NetLimiter.dll"));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
