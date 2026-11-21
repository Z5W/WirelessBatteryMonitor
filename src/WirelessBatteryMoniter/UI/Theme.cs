using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    internal static class Theme
    {
        public static readonly Color Background = Color.FromArgb(242, 243, 247);
        public static readonly Color BackgroundAlt = Color.FromArgb(242, 243, 247);
        public static readonly Color Surface = Color.FromArgb(255, 255, 255);
        public static readonly Color SurfaceHover = Color.FromArgb(245, 248, 253);
        public static readonly Color SurfaceRaised = Color.FromArgb(233, 237, 245);
        public static readonly Color Border = Color.FromArgb(180, 189, 204);
        public static readonly Color BorderSoft = Color.FromArgb(233, 237, 245);
        public static readonly Color Text = Color.FromArgb(32, 38, 48);
        public static readonly Color TextMuted = Color.FromArgb(88, 98, 117);
        public static readonly Color TextFaint = Color.FromArgb(88, 98, 117);
        public static readonly Color Accent = Color.FromArgb(20, 92, 205);
        public static readonly Color AccentDark = Color.FromArgb(228, 237, 255);
        public static readonly Color Green = Color.FromArgb(20, 92, 205);
        public static readonly Color Amber = Color.FromArgb(138, 72, 0);
        public static readonly Color Red = Color.FromArgb(179, 38, 30);
        public static readonly Color BadgeText = Color.FromArgb(255, 255, 255);

        // The manifest opts into system DPI awareness; GDI reports the native system DPI.
        // Mutable only for the offline layout harness; production uses the system value.
        public static float DpiScale = ReadDpiScale();
        private static float ReadDpiScale() { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f; }
        private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();
        private static readonly object FontGate = new object();

        public static Font Font(float size, FontStyle style = FontStyle.Regular)
        {
            return GetFont("WBM Mixed Sans", size, style);
        }

        public static Font IconFont(float size)
        {
            return GetFont("Segoe MDL2 Assets", size, FontStyle.Regular);
        }

        private static Font GetFont(string family, float size, FontStyle style)
        {
            string key = family + "|" + size.ToString("0.##", CultureInfo.InvariantCulture) + "|" + (int)style;
            lock (FontGate)
            {
                Font font;
                if (Fonts.TryGetValue(key, out font)) return font;
                font = family == "WBM Mixed Sans" ? new Font(UiFonts.Family,size,style,GraphicsUnit.Point) : new Font(family, size, style, GraphicsUnit.Point);
                Fonts[key] = font;
                return font;
            }
        }

        public static Color BatteryColor(int? level, int threshold)
        {
            if (!level.HasValue) return TextFaint;
            if (level.Value <= 10) return Red;
            if (level.Value <= threshold) return Amber;
            return Green;
        }

        public static Color WithAlpha(Color color, int alpha)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), color.R, color.G, color.B);
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            float r = Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2f);
            float diameter = r * 2f;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics graphics, Brush brush, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                graphics.FillPath(brush, path);
            }
        }

        public static void DrawRounded(Graphics graphics, Pen pen, RectangleF rect, float radius)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            {
                graphics.DrawPath(pen, path);
            }
        }

        public static void DrawBatteryBar(
            Graphics graphics,
            RectangleF rect,
            int? level,
            Color fill,
            Color track)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            using (SolidBrush trackBrush = new SolidBrush(track))
            {
                FillRounded(graphics, trackBrush, rect, rect.Height / 2f);
            }

            if (!level.HasValue || level.Value <= 0) return;
            float width = rect.Width * Math.Max(0, Math.Min(100, level.Value)) / 100f;
            RectangleF fillRect = new RectangleF(rect.X, rect.Y, Math.Min(rect.Width, width), rect.Height);
            using (SolidBrush fillBrush = new SolidBrush(fill))
            {
                FillRounded(graphics, fillBrush, fillRect, rect.Height / 2f);
            }
        }

        public static void DrawText(
            Graphics graphics,
            string text,
            Font font,
            Color color,
            Rectangle bounds,
            TextFormatFlags flags)
        {
            TextRenderer.DrawText(graphics, text ?? string.Empty, font, bounds, color, flags);
        }

        public static TextFormatFlags LeftMiddle = TextFormatFlags.Left | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        public static TextFormatFlags RightMiddle = TextFormatFlags.Right | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;

        public static TextFormatFlags CenterMiddle = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        public static void DrawFocus(Graphics graphics,Rectangle bounds) {
            float inset=2*DpiScale;
            using(var pen=new Pen(Accent,2*DpiScale)) DrawRounded(graphics,pen,new RectangleF(inset,inset,bounds.Width-inset*2,bounds.Height-inset*2),(bounds.Height-inset*2)/2);
        }
        public static void Prepare(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        }
    }
}



