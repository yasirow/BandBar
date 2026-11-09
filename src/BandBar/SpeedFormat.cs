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
    /// Dynamic B/s → KB/s → MB/s → GB/s with one decimal, or bit/s → kbit/s → Mbit/s → Gbit/s (docs/DESIGN.md §4).
    /// Values are truncated, not rounded, so the bar never shows more than was measured and never flips to "1024,0".
    /// </summary>
    internal static class SpeedFormat
    {
        internal static readonly IReadOnlyList<string> ByteUnits = new[] { "B/s", "KB/s", "MB/s", "GB/s" };
        internal static readonly IReadOnlyList<string> BitUnits = new[] { "bit/s", "kbit/s", "Mbit/s", "Gbit/s" };
        private static readonly string[] AmountUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

        /// <summary>Every unit <see cref="Format"/> can return with these settings; used to fix the bar width.</summary>
        public static IReadOnlyList<string> UnitsFor(Settings settings) => settings.Bits ? BitUnits : ByteUnits;

        public static SpeedText Format(uint bytesPerSecond, Settings settings)
        {
            IReadOnlyList<string> units = UnitsFor(settings);
            // Bits are counted in steps of 1000, as networks do; unitBase applies to bytes only.
            double unit = settings.Bits ? 1000 : settings.UnitBase;
            double value = settings.Bits ? bytesPerSecond * 8.0 : bytesPerSecond;
            int index = 0;
            while (value >= unit && index < units.Count - 1)
            {
                value /= unit;
                index++;
            }
            return new SpeedText(Truncate(value, settings.Decimals), units[index]);
        }

        /// <summary>An amount of data such as "1.2 GB" (the tooltip's totals): bytes, unitBase, the chosen decimals.</summary>
        public static string FormatAmount(ulong bytes, Settings settings)
        {
            double value = bytes;
            int index = 0;
            while (value >= settings.UnitBase && index < AmountUnits.Length - 1)
            {
                value /= settings.UnitBase;
                index++;
            }
            return Truncate(value, index == 0 ? 0 : settings.Decimals) + " " + AmountUnits[index];
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
