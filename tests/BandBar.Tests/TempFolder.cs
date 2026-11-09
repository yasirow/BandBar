using System;
using System.IO;
using System.Text;

namespace BandBar.Tests
{
    /// <summary>A fresh folder under the temp path (build.ps1 points it inside the repository), deleted afterwards.</summary>
    internal sealed class TempFolder : IDisposable
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BandBar.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string SettingsPath => System.IO.Path.Combine(Path, AppPaths.SettingsFileName);

        public void WriteSettings(string json) => File.WriteAllText(SettingsPath, json, Utf8NoBom);

        public string ReadSettings() => File.ReadAllText(SettingsPath, Utf8NoBom);

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort.
            }
        }
    }
}
