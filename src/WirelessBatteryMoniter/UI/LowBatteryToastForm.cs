using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    /// <summary>
    /// A self-contained, always-on-top desktop reminder. This is intentionally used in
    /// addition to (or instead of) shell balloons because Windows notification settings
    /// and Focus Assist can suppress balloons, while a low battery warning must be seen.
    /// </summary>
    internal sealed class LowBatteryToastForm : Form
    {
        private static LowBatteryToastForm _current;

        private readonly Timer _timer;
        private readonly Action _openDetails;
        private IList<DeviceBatteryInfo> _devices;
        private int _threshold;

        private LowBatteryToastForm(Action openDetails)
        {
            _openDetails = openDetails;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.Background;
            DoubleBuffered = true;
            Width = S(390);
            Height = S(112);
            Cursor = Cursors.Hand;
            AutoScaleMode = AutoScaleMode.None;

            _timer = new Timer { Interval = 9000 };
            _timer.Tick += (s, e) => Close();
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
                parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return parameters;
            }
        }

        public static void ShowToast(IList<DeviceBatteryInfo> devices, int threshold, Action openDetails)
        {
            if (devices == null || devices.Count == 0) return;

            if (_current != null && !_current.IsDisposed)
            {
                _current.UpdateContent(devices, threshold);
                _current.Reposition();
                _current.Show();
                _current.BringToFront();
                _current._timer.Stop();
                _current._timer.Start();
                return;
            }

            LowBatteryToastForm form = new LowBatteryToastForm(openDetails);
            form.UpdateContent(devices, threshold);
            form.Reposition();
            form.Show();
            form.BringToFront();
            _current = form;
            form._timer.Start();
        }

        public static void CloseCurrent()
        {
            if (_current != null && !_current.IsDisposed)
            {
                _current.Close();
            }
            _current = null;
        }

        private void UpdateContent(IList<DeviceBatteryInfo> devices, int threshold)
        {
            _devices = devices;
            _threshold = threshold;
            Invalidate();
        }

        private static int S(int value) { return (int)Math.Round(value * Theme.DpiScale); }
        private void Reposition()
        {
            Screen screen = Screen.PrimaryScreen;
            Rectangle area = screen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Theme.Prepare(e.Graphics);
            Graphics graphics = e.Graphics;
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (SolidBrush brush = new SolidBrush(Theme.Surface))
            {
                Theme.FillRounded(graphics, brush, rect, 15f);
            }
            using (Pen border = new Pen(Theme.Border))
            {
                Theme.DrawRounded(graphics, border, rect, 15f);
            }

            // Warning medallion.
            RectangleF circle = new RectangleF(S(18), S(26), S(42), S(42));
            using (SolidBrush red = new SolidBrush(Theme.Red))
            {
                graphics.FillEllipse(red, circle);
            }
            Theme.DrawText(graphics, "!", Theme.Font(20f, FontStyle.Bold), Color.White,
                new Rectangle((int)circle.X, (int)circle.Y - 1, (int)circle.Width, (int)circle.Height),
                Theme.CenterMiddle);

            int count = _devices == null ? 0 : _devices.Count;
            string title = count <= 1 ? "低电量提醒" : string.Format("{0} 台设备电量偏低", count);
            Theme.DrawText(graphics, title, Theme.Font(10.5f, FontStyle.Bold), Theme.Text,
                new Rectangle(S(72), S(13), Width - S(112), S(24)), Theme.LeftMiddle);

            string summary = _devices == null
                ? string.Empty
                : string.Join(" · ", _devices.Take(3).Select(d => string.Format("{0} {1}", d.Name, d.LevelText)));
            if (count > 3) summary += string.Format(" 等 {0} 台", count);
            Theme.DrawText(graphics, summary, Theme.Font(8.5f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(S(72), S(38), Width - S(98), S(20)), Theme.LeftMiddle);

            Theme.DrawText(graphics, (_threshold > 0 ? string.Format("电量达到提醒阈值 {0}%，请及时充电", _threshold) : "电量达到各自提醒阈值，请及时充电"),
                Theme.Font(8.5f, FontStyle.Regular), Theme.Amber,
                new Rectangle(S(72), S(59), Width - S(98), S(19)), Theme.LeftMiddle);

            Theme.DrawText(graphics, "点击查看设备详情", Theme.Font(7.6f, FontStyle.Regular), Theme.TextFaint,
                new Rectangle(S(72), S(80), Width - S(98), S(16)), Theme.LeftMiddle);

            Theme.DrawText(graphics, "×", Theme.Font(12f, FontStyle.Regular), Theme.TextMuted,
                new Rectangle(Width - S(38), S(8), S(26), S(24)), Theme.CenterMiddle);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.X >= Width - S(44) && e.Y <= S(38))
            {
                Close();
                return;
            }

            Action action = _openDetails;
            Close();
            if (action != null) action();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            _timer.Dispose();
            if (ReferenceEquals(_current, this)) _current = null;
            base.OnFormClosed(e);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            using (GraphicsPath path = Theme.RoundedRect(new RectangleF(0, 0, Width, Height), 15f))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }
    }
}
