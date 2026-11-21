using System;
using System.Drawing;
using System.Windows.Forms;

namespace WirelessBatteryMoniter.UI
{
    // Text-only tooltip: the shell's 128 WCHAR buffer cannot hold full device names.
    internal sealed class TrayTextTip : Form
    {
        private readonly NotifyIcon _icon;
        private readonly Timer _timer = new Timer { Interval = 100 };
        private readonly Label _label;
        private Rectangle _anchor;
        private DateTime _entered;
        private bool _tracking;
        private string _content = "WirelessBatteryMoniter\n正在扫描…";

        internal TrayTextTip(NotifyIcon icon)
        {
            _icon = icon;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = SystemColors.Info;
            Padding = new Padding(8);
            _label = new Label { AutoSize = true, ForeColor = SystemColors.InfoText,
                BackColor = SystemColors.Info, Font = Theme.Font(9f), Location = new Point(8, 8), UseMnemonic = false };
            Controls.Add(_label);
            _icon.MouseMove += OnTrayMove;
            _icon.MouseDown += OnTrayDown;
            _timer.Tick += OnTick;
        }

        internal string Content
        {
            get { return _content; }
            set { _content = value ?? ""; if (Visible) PositionTip(); }
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var p = base.CreateParams; p.ExStyle |= 0x08000000 | 0x80; return p; }
        }

        private void OnTrayMove(object sender, MouseEventArgs e)
        {
            if (_tracking || Control.MouseButtons != MouseButtons.None ||
                (_icon.ContextMenuStrip != null && _icon.ContextMenuStrip.Visible)) return;
            Point cursor = Cursor.Position;
            string diagnostic;
            if (!TrayIconProbe.TryGetRect(_icon, out _anchor, out diagnostic) || !_anchor.Contains(cursor))
                _anchor = new Rectangle(cursor.X - 8, cursor.Y - 8, 16, 16);
            _entered = DateTime.UtcNow;
            _tracking = true;
            _timer.Start();
        }

        private void OnTrayDown(object sender, MouseEventArgs e) { Dismiss(); }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_icon.Visible || !_anchor.Contains(Cursor.Position) ||
                Control.MouseButtons != MouseButtons.None ||
                (_icon.ContextMenuStrip != null && _icon.ContextMenuStrip.Visible))
            { Dismiss(); return; }
            if (!Visible && (DateTime.UtcNow - _entered).TotalMilliseconds >= 500)
            { PositionTip(); Show(); }
        }

        private void PositionTip()
        {
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            _label.MaximumSize = new Size(Math.Max(1, work.Width - 32), 0);
            _label.Text = _content;
            ClientSize = new Size(_label.Width + 16, _label.Height + 16);
            int x = Math.Max(work.Left, Math.Min(_anchor.Left, work.Right - Width));
            int y = _anchor.Top - Height - 4;
            if (y < work.Top) y = _anchor.Bottom + 4;
            Location = new Point(x, Math.Max(work.Top, Math.Min(y, work.Bottom - Height)));
        }

        private void Dismiss() { _timer.Stop(); _tracking = false; Hide(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _icon.MouseMove -= OnTrayMove;
                _icon.MouseDown -= OnTrayDown;
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}

