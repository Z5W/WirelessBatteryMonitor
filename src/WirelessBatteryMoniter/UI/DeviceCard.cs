using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class DeviceCard : Control
    {
        private DeviceBatteryInfo _device;
        private int _threshold = 20;
        private bool _hover;

        public DeviceCard()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw
                | ControlStyles.SupportsTransparentBackColor,
                true);
            BackColor = Color.Transparent;
            Height = 92;
            Margin = new Padding(0, 0, 0, 10);
            Cursor = Cursors.Default;
            AccessibleRole = AccessibleRole.ListItem;
        }

        public DeviceBatteryInfo Device
        {
            get { return _device; }
        }

        public void UpdateDevice(DeviceBatteryInfo device, int threshold)
        {
            _device = device;
            _threshold = threshold;
            AccessibleName = device == null ? string.Empty : device.Name + " " + device.LevelText;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_device == null) return;
            Theme.Prepare(e.Graphics);
            Graphics graphics = e.Graphics;
            RectangleF card = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            Color surface = _device.IsConnected
                ? (_hover ? Theme.SurfaceHover : Theme.Surface)
                : Theme.WithAlpha(Theme.Surface, 175);
            Color border = _hover ? Theme.Border : Theme.BorderSoft;

            using (SolidBrush brush = new SolidBrush(surface))
            {
                Theme.FillRounded(graphics, brush, card, 14f);
            }
            using (Pen pen = new Pen(border))
            {
                Theme.DrawRounded(graphics, pen, card, 14f);
            }

            Color levelColor = _device.IsConnected
                ? Theme.BatteryColor(_device.Level, _threshold)
                : Theme.TextFaint;

            RectangleF iconCircle = new RectangleF(16f, 24f, 40f, 40f);
            using (SolidBrush iconBack = new SolidBrush(_device.IsConnected ? Theme.SurfaceRaised : Theme.WithAlpha(Theme.SurfaceRaised, 130)))
            {
                graphics.FillEllipse(iconBack, iconCircle);
            }

            string glyph = GlyphFor(_device.Name);
            Rectangle iconRect = new Rectangle((int)iconCircle.X, (int)iconCircle.Y, (int)iconCircle.Width, (int)iconCircle.Height);
            Theme.DrawText(graphics, glyph, Theme.IconFont(15f), _device.IsConnected ? Theme.Accent : Theme.TextFaint,
                iconRect, Theme.CenterMiddle);

            Rectangle nameRect = new Rectangle(70, 13, Width - 70 - 92, 25);
            Theme.DrawText(graphics, _device.Name, Theme.Font(10.5f, FontStyle.Bold),
                _device.IsConnected ? Theme.Text : Theme.TextMuted, nameRect, Theme.LeftMiddle);

            Rectangle percentRect = new Rectangle(Width - 92, 11, 76, 28);
            Theme.DrawText(graphics, _device.LevelText, Theme.Font(12.5f, FontStyle.Bold),
                levelColor, percentRect, Theme.RightMiddle);

            string state = _device.Kind + " · " + _device.StateText;
            if (_device.IsConnected && _device.Level.HasValue && _device.Level.Value <= _threshold)
            {
                state += " · 低电量";
            }
            Rectangle stateRect = new Rectangle(70, 38, Width - 70 - 18, 19);
            Theme.DrawText(graphics, state, Theme.Font(8.5f, FontStyle.Regular),
                _device.IsConnected ? Theme.TextMuted : Theme.TextFaint, stateRect, Theme.LeftMiddle);

            RectangleF bar = new RectangleF(70f, 67f, Width - 70f - 18f, 6f);
            Color track = _device.IsConnected ? Theme.BorderSoft : Theme.WithAlpha(Theme.BorderSoft, 110);
            Theme.DrawBatteryBar(graphics, bar, _device.Level, levelColor, track);

            if (_hover)
            {
                using (SolidBrush dot = new SolidBrush(_device.IsConnected ? Theme.Accent : Theme.TextFaint))
                {
                    graphics.FillEllipse(dot, Width - 13f, Height / 2f - 2f, 4f, 4f);
                }
            }
        }

        private static string GlyphFor(string name)
        {
            string type = TrayTextFormatter.DeviceType(name);
            if (type == "鼠标") return "\uE962";
            if (type == "键盘") return "\uE765";
            if (type == "耳机") return "\uE7F6";
            if (type == "手柄") return "\uE7FC";
            return "\uE702";
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }
    }
}

