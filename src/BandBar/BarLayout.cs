using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace BandBar
{
    /// <summary>
    /// Metrics and drawing for one DPI and taskbar (docs/DESIGN.md §4): upload with a red up arrow above download with
    /// a green down arrow, text left-aligned after the arrow. Normally two lines ("12.3 KB/s"); on a vertical taskbar
    /// that is too narrow for them, four stacked lines (number, then unit); optionally, on a horizontal taskbar, one
    /// line with both values side by side. The text column has a fixed width measured from the widest possible value,
    /// so the bar never resizes as the numbers change.
    /// </summary>
    internal sealed class BarLayout : IDisposable
    {
        private const float DefaultPoints = 9f;
        private const float SmallestShrinkPoints = 7f;
        private const string DefaultFamily = "Segoe UI";
        private static string reportedMissingFamily;

        private readonly string family;
        private readonly Font font;
        private readonly StringFormat format;
        private readonly int padX, arrowWidth, arrowHeight, arrowGap, blockGap, pairGap, lineHeight, textWidth;

        /// <param name="verticalTaskbar">The taskbar is on the left or right edge.</param>
        /// <param name="thickness">
        /// The taskbar's thickness in pixels: the height a horizontal taskbar offers, or the width a vertical one offers.
        /// The font shrinks (down to 7 pt, or the chosen size if smaller) until the bar fits it.
        /// </param>
        /// <param name="widestNumber">A number at least as wide as any value shown.</param>
        /// <param name="units">Every unit that can follow a number.</param>
        /// <param name="statuses">Every status text that can replace the values.</param>
        /// <param name="fontFamily">The font; one that is not installed means Segoe UI.</param>
        /// <param name="fontPoints">Where fitting starts.</param>
        /// <param name="oneLine">Both values side by side; ignored on a vertical taskbar.</param>
        public BarLayout(uint dpi, bool verticalTaskbar, int thickness, string widestNumber, IEnumerable<string> units,
                         IEnumerable<string> statuses, string fontFamily = DefaultFamily, float fontPoints = DefaultPoints,
                         bool oneLine = false)
        {
            Dpi = dpi;
            VerticalTaskbar = verticalTaskbar;
            Thickness = thickness;
            OneLine = oneLine && !verticalTaskbar;
            format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces;

            padX = Scale(3);
            arrowGap = Scale(4);
            arrowWidth = Scale(8);
            blockGap = Scale(2);
            pairGap = Scale(8);

            List<string> unitList = units.ToList();
            List<string> statusList = statuses.ToList();
            string[] lineTexts = unitList.Select(u => widestNumber + " " + u).Concat(statusList).ToArray();
            string[] stackedTexts = new[] { widestNumber }.Concat(unitList).Concat(statusList).ToArray();

            family = InstalledOrDefault(fontFamily);
            float points = fontPoints;
            float minimumPoints = Math.Min(SmallestShrinkPoints, fontPoints);
            int lines = OneLine ? 1 : 2;
            font = CreateFont(points);
            if (!verticalTaskbar)
            {
                while (LineHeightOf(font) * lines > thickness && points > minimumPoints)
                {
                    font.Dispose();
                    points -= 0.5f;
                    font = CreateFont(points);
                }
                textWidth = Measure(font, lineTexts);
            }
            else
            {
                textWidth = Measure(font, lineTexts);
                if (WidthFor(textWidth) > thickness)
                {
                    Stacked = true;
                    textWidth = Measure(font, stackedTexts);
                    while (WidthFor(textWidth) > thickness && points > minimumPoints)
                    {
                        font.Dispose();
                        points -= 0.5f;
                        font = CreateFont(points);
                        textWidth = Measure(font, stackedTexts);
                    }
                }
            }

            lineHeight = LineHeightOf(font);
            arrowHeight = Math.Min(Scale(10), lineHeight - Scale(2));
        }

        public uint Dpi { get; }
        public bool VerticalTaskbar { get; }

        /// <summary>The taskbar thickness this layout was fitted to.</summary>
        public int Thickness { get; }

        /// <summary>Four lines (number, unit, number, unit) instead of two.</summary>
        public bool Stacked { get; }

        /// <summary>Both values side by side on one line (horizontal taskbars only).</summary>
        public bool OneLine { get; }

        public Size Size =>
            OneLine ? new Size(padX * 2 + SegmentWidth * 2 + pairGap, lineHeight)
            : new Size(WidthFor(textWidth), Stacked ? lineHeight * 4 + blockGap : lineHeight * 2);

        public bool Fits(uint dpi, bool verticalTaskbar, int thickness) =>
            Dpi == dpi && VerticalTaskbar == verticalTaskbar && Thickness == thickness;

        public int Scale(int dip) => (int)Math.Round(dip * Dpi / 96.0);

        public void Draw(Graphics g, SpeedText up, SpeedText down, Color upArrow, Color downArrow, Color text)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit; // ClearType would fringe on a transparent background
            if (OneLine)
            {
                DrawLine(g, padX, 0, up.ToString(), pointsUp: true, upArrow, text);
                DrawLine(g, padX + SegmentWidth + pairGap, 0, down.ToString(), pointsUp: false, downArrow, text);
                return;
            }
            if (!Stacked)
            {
                DrawLine(g, padX, 0, up.ToString(), pointsUp: true, upArrow, text);
                DrawLine(g, padX, lineHeight, down.ToString(), pointsUp: false, downArrow, text);
                return;
            }

            int downTop = lineHeight * 2 + blockGap;
            DrawLine(g, padX, 0, up.Number, pointsUp: true, upArrow, text);
            DrawText(g, padX, lineHeight, up.Unit, text);
            DrawLine(g, padX, downTop, down.Number, pointsUp: false, downArrow, text);
            DrawText(g, padX, downTop + lineHeight, down.Unit, text);
        }

        // One arrow and its text column.
        private int SegmentWidth => arrowWidth + arrowGap + textWidth;

        private int WidthFor(int textColumn) => padX * 2 + arrowWidth + arrowGap + textColumn;

        private void DrawLine(Graphics g, int left, int top, string value, bool pointsUp, Color arrowColor, Color textColor)
        {
            int y = top + (lineHeight - arrowHeight) / 2;
            using (var brush = new SolidBrush(arrowColor))
                g.FillPolygon(brush, BlockArrow(left, y, arrowWidth, arrowHeight, pointsUp));
            DrawText(g, left, top, value, textColor);
        }

        private void DrawText(Graphics g, int left, int top, string value, Color textColor)
        {
            if (string.IsNullOrEmpty(value))
                return;
            float textTop = top + (lineHeight - font.GetHeight()) / 2f;
            using (var brush = new SolidBrush(textColor))
                g.DrawString(value, font, brush, new PointF(left + arrowWidth + arrowGap, textTop), format);
        }

        // Triangle head plus a rectangular stem; defined pointing up and mirrored vertically for down.
        private static PointF[] BlockArrow(float x, float y, float width, float height, bool pointsUp)
        {
            float cx = x + width / 2f;
            float head = Math.Min(height, width / 2f + 1f);
            float halfStem = Math.Max(1f, width * 0.2f);
            float Y(float dy) => pointsUp ? y + dy : y + height - dy;

            return new[]
            {
                new PointF(cx, Y(0)),
                new PointF(x + width, Y(head)),
                new PointF(cx + halfStem, Y(head)),
                new PointF(cx + halfStem, Y(height)),
                new PointF(cx - halfStem, Y(height)),
                new PointF(cx - halfStem, Y(head)),
                new PointF(x, Y(head)),
            };
        }

        private int Measure(Font f, IEnumerable<string> texts)
        {
            int width = 0;
            using (var probe = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(probe))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                foreach (string text in texts)
                    width = Math.Max(width, (int)Math.Ceiling(g.MeasureString(text, f, PointF.Empty, format).Width));
            }
            return width;
        }

        private Font CreateFont(float points) =>
            new Font(family, points * Dpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);

        // GDI+ silently substitutes a font that is not installed; use Segoe UI instead and say so once.
        private static string InstalledOrDefault(string requested)
        {
            if (string.IsNullOrWhiteSpace(requested) || string.Equals(requested, DefaultFamily, StringComparison.OrdinalIgnoreCase))
                return DefaultFamily;
            using (var probe = new Font(requested, 9f))
            {
                if (string.Equals(probe.Name, requested, StringComparison.OrdinalIgnoreCase))
                    return probe.Name;
            }
            if (requested != reportedMissingFamily)
            {
                reportedMissingFamily = requested;
                Log.Warn("The font in fontFamily is not installed; using Segoe UI");
            }
            return DefaultFamily;
        }

        private static int LineHeightOf(Font f) => (int)Math.Ceiling(f.GetHeight());

        public void Dispose()
        {
            font.Dispose();
            format.Dispose();
        }
    }
}
