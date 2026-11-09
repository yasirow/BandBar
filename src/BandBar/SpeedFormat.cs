using System;
using System.Collections.Generic;
using System.Globalization;

namespace BandBar
{
    /// <summary>A value as drawn: the number and its unit, kept apart for the stacked layout (docs/DESIGN.md §4).</summary>
    internal readonly struct SpeedText : IEquatable<SpeedText>
    {
        public SpeedText(string number, string unit)
        {
            Number = number ?? "";
            Unit = unit ?? "";
        }

        public string Number { get; }

        /// <summary>Empty for a status text.</summary>
        public string Unit { get; }

        public static SpeedText Status(string text) => new SpeedText(text, "");

        public bool Equals(SpeedText other) => Number == other.Number && Unit == other.Unit;
        public override bool Equals(object obj) => obj is SpeedText other && Equals(other);
        public override int GetHashCode() => (Number?.GetHashCode() ?? 0) * 31 + (Unit?.GetHashCode() ?? 0);

        /// <summary>One line: "12.3 KB/s".</summary>
        public override string ToString() => Unit.Length == 0 ? Number : Number + " " + Unit;
    }

    /// <summary>
    /// Dynamic B/s → KB/s → MB/s → GB/s with one decimal (docs/DESIGN.md).
    /// Values are truncated, not rounded, so the bar never shows more than was measured and never flips to "1024,0".
    /// </summary>
    internal static class SpeedFormat
    {
        internal static readonly IReadOnlyList<string> Units = new[] { "B/s", "KB/s", "MB/s", "GB/s" };

        public static SpeedText Format(uint bytesPerSecond, Settings settings)
        {
            double unit = settings.UnitBase;
            double value = bytesPerSecond;
            int index = 0;
            while (value >= unit && index < Units.Count - 1)
            {
                value /= unit;
                index++;
            }
            return new SpeedText(Truncate(value, settings.Decimals), Units[index]);
        }

        /// <summary>A number at least as wide as any <see cref="Format"/> can return; used to fix the bar width.</summary>
        public static string WidestNumber(Settings settings)
        {
            string separator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
            return "8888" + (settings.Decimals > 0 ? separator + new string('8', settings.Decimals) : "");
        }

        private static string Truncate(double value, int decimals)
        {
            double scale = Math.Pow(10, decimals);
            double truncated = Math.Floor(value * scale + 1e-9) / scale;
            return truncated.ToString("F" + decimals.ToString(CultureInfo.InvariantCulture), CultureInfo.CurrentCulture);
        }
    }
}
