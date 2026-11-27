using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal static class TrayIconRenderer
    {
        internal static Color StatusColor(BatterySnapshot snapshot, AppSettings settings)
        {
            var visible=settings.DisplaySnapshot(snapshot);
            var low=visible.Devices.Where(d=>d.IsLowBattery(settings.ThresholdFor(d.Id))).ToList();
            if (low.Any(d=>d.Level.Value<=10)) return Theme.Red;
            if (low.Count>0) return Theme.Amber;
            return visible.MinimumLevel.HasValue ? Theme.Green : Theme.TextFaint;
        }

        public static Icon Create(int deviceCount, int? minimumLevel, int threshold, Color? statusColor=null)
        {
            using (Bitmap bitmap = CreateBitmap(deviceCount, minimumLevel, threshold,statusColor))
            {
                // Embed the PNG in a one-image .ico and let Icon parse it. This gives
                // the shell a self-contained icon handle and avoids lifetime problems
                // that can leave a NotifyIcon blank on some Windows 10 systems.
                using (MemoryStream png = new MemoryStream())
                {
                    bitmap.Save(png, ImageFormat.Png);
                    byte[] pngBytes = png.ToArray();
                    using (MemoryStream ico = new MemoryStream())
                    {
                        using (BinaryWriter writer = new BinaryWriter(ico, System.Text.Encoding.UTF8, true))
                        {
                            writer.Write((ushort)0);          // reserved
                            writer.Write((ushort)1);          // type: icon
                            writer.Write((ushort)1);          // image count
                            writer.Write((byte)32);           // width
                            writer.Write((byte)32);           // height
                            writer.Write((byte)0);            // color count
                            writer.Write((byte)0);            // reserved
                            writer.Write((ushort)1);          // planes
                            writer.Write((ushort)32);         // bits per pixel
                            writer.Write((uint)pngBytes.Length);
                            writer.Write((uint)22);           // offset of PNG data
                            writer.Write(pngBytes);
                        }
                        ico.Position = 0;
                        return new Icon(ico, 32, 32);
                    }
                }
            }
        }

        public static Bitmap CreateBitmap(int deviceCount, int? minimumLevel, int threshold, Color? statusColor=null)
        {
            Bitmap bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                Color outline = deviceCount > 0 ? Color.FromArgb(238, 244, 252) : Color.FromArgb(125, 139, 158);
                Color fillColor = statusColor ?? Theme.BatteryColor(minimumLevel, threshold);

                // Battery shell.
                using (Pen shell = new Pen(outline, 2.2f))
                {
                    shell.LineJoin = LineJoin.Round;
                    using (GraphicsPath body = Theme.RoundedRect(new RectangleF(1.4f, 10.5f, 22f, 13.5f), 3f))
                    {
                        graphics.DrawPath(shell, body);
                    }
                    using (SolidBrush cap = new SolidBrush(outline))
                    {
                        graphics.FillRectangle(cap, 24.2f, 14.0f, 3.2f, 6.5f);
                    }
                }

                if (minimumLevel.HasValue && deviceCount > 0)
                {
                    float width = 17f * Math.Max(1, Math.Min(100, minimumLevel.Value)) / 100f;
                    using (SolidBrush fill = new SolidBrush(fillColor))
                    using (GraphicsPath inner = Theme.RoundedRect(new RectangleF(3.3f, 12.4f, Math.Max(3f, width), 9.7f), 2f))
                    {
                        graphics.FillPath(fill, inner);
                    }
                }
                else if (deviceCount > 0)
                {
                    using (SolidBrush dots = new SolidBrush(Theme.TextFaint))
                    {
                        graphics.FillEllipse(dots, 8f, 15.2f, 2.4f, 2.4f);
                        graphics.FillEllipse(dots, 12.2f, 15.2f, 2.4f, 2.4f);
                        graphics.FillEllipse(dots, 16.4f, 15.2f, 2.4f, 2.4f);
                    }
                }

                // Device-count badge.
                RectangleF badge = new RectangleF(16.5f, 0.5f, 15f, 15f);
                Color badgeFill = deviceCount > 0 ? Theme.Accent : Color.FromArgb(48, 58, 74);
                using (SolidBrush badgeBrush = new SolidBrush(badgeFill))
                {
                    graphics.FillEllipse(badgeBrush, badge);
                }
                using (Pen badgeBorder = new Pen(Color.FromArgb(230, 10, 18, 26), 1.2f))
                {
                    graphics.DrawEllipse(badgeBorder, badge);
                }

                string text = deviceCount > 9 ? "9+" : deviceCount.ToString();
                using (Font font = new Font(UiFonts.Family, deviceCount > 9 ? 6.8f : 8.6f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush textBrush = new SolidBrush(deviceCount > 0 ? Theme.BadgeText : Theme.TextMuted))
                using (StringFormat format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                })
                {
                    graphics.DrawString(text, font, textBrush, new RectangleF(16.5f, 0.5f, 15f, 15f), format);
                }
            }

            return bitmap;
        }
    }
}

