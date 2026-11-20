using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Models;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class MainForm : Form
    {
        private readonly Action _openSettings;
        private readonly Func<Task> _refresh;
        private readonly PictureBox _logo;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly FlatButton _settingsButton;
        private readonly StatusCard _statusCard;
        private readonly Label _countLabel;
        private readonly Label _statusText;
        private readonly ToolTip _statusTip = new ToolTip { AutoPopDelay = 30000 };
        private readonly Label _minLabel;
        private readonly Label _demoBadge;
        private readonly FlowLayoutPanel _deviceList;
        private readonly EmptyState _emptyState;
        private readonly Label _footerLabel;
        private readonly FlatButton _refreshButton;

        private BatterySnapshot _snapshot = BatterySnapshot.Empty(false);
        private AppSettings _settings = new AppSettings();
        private bool _allowClose;
        private bool _busy;
        private bool _trayHintShown;

        public MainForm(Action openSettings, Func<Task> refresh)
        {
            _openSettings = openSettings;
            _refresh = refresh;

            Text = "WirelessBatteryMoniter";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(472, 668);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(9f);
            DoubleBuffered = true;
            KeyPreview = true;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch
            {
            }

            _logo = new PictureBox
            {
                Location = new Point(20, 18),
                Size = new Size(44, 44),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = LoadLogo()
            };
            Controls.Add(_logo);

            _title = new Label
            {
                Text = "WirelessBatteryMoniter",
                Location = new Point(76, 16),
                Size = new Size(294, 28),
                Font = Theme.Font(13f, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            Controls.Add(_title);

            _subtitle = new Label
            {
                Text = "无线设备电量监控",
                Location = new Point(77, 44),
                Size = new Size(294, 18),
                Font = Theme.Font(8.5f, FontStyle.Regular),
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(_subtitle);


            _settingsButton = new FlatButton
            {
                Text = "设置",
                Location = new Point(382, 22),
                Size = new Size(70, 34),
                Quiet = true,
                TabIndex = 2
            };
            _settingsButton.Click += (s, e) => _openSettings();
            Controls.Add(_settingsButton);

            _statusCard = new StatusCard
            {
                Location = new Point(16, 86),
                Size = new Size(440, 70)
            };
            Controls.Add(_statusCard);

            _countLabel = new Label
            {
                Text = "正在读取…",
                Location = new Point(32, 8),
                Size = new Size(272, 27),
                Font = Theme.Font(11f, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _statusCard.Controls.Add(_countLabel);

            _statusText = new Label
            {
                Text = "正在扫描蓝牙设备",
                Location = new Point(32, 35),
                Size = new Size(218, 18),
                Font = Theme.Font(8.5f, FontStyle.Regular),
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _statusCard.Controls.Add(_statusText);
            _statusText.Click += (sender, args) => { if (_snapshot != null && !string.IsNullOrEmpty(_snapshot.Error)) MessageBox.Show(this, _snapshot.Error, "读取详情", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            Disposed += (sender, args) => _statusTip.Dispose();

            _minLabel = new Label
            {
                Text = "--",
                Location = new Point(306, 8),
                Size = new Size(116, 27),
                Font = Theme.Font(9.5f, FontStyle.Bold),
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleRight
            };
            _statusCard.Controls.Add(_minLabel);

            _demoBadge = new Label
            {
                Text = "演示模式",
                Location = new Point(342, 36),
                Size = new Size(80, 20),
                Font = Theme.Font(8f, FontStyle.Regular),
                ForeColor = Theme.Accent,
                BackColor = Theme.AccentDark,
                TextAlign = ContentAlignment.MiddleCenter,
                Visible = false
            };
            _statusCard.Controls.Add(_demoBadge);

            _deviceList = new FlowLayoutPanel
            {
                Location = new Point(16, 170),
                Size = new Size(440, 430),
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Theme.Background,
                Padding = new Padding(0),
                Margin = new Padding(0),
                TabStop = false,
                Visible = false
            };
            Controls.Add(_deviceList);

            _emptyState = new EmptyState
            {
                Location = new Point(16, 170),
                Size = new Size(440, 430)
            };
            Controls.Add(_emptyState);

            _footerLabel = new Label
            {
                Text = "等待首次刷新…",
                Location = new Point(16, 618),
                Size = new Size(176, 24),
                Font = Theme.Font(8.3f, FontStyle.Regular),
                ForeColor = Theme.TextFaint,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            Controls.Add(_footerLabel);

            FlatButton aboutButton = new FlatButton
            {
                Text = "关于",
                Location = new Point(196, 610),
                Size = new Size(70, 36),
                Quiet = true,
                TabIndex = 3
            };
            aboutButton.Click += (s, e) =>
            {
                using (AboutForm form = new AboutForm()) form.ShowDialog(this);
            };
            Controls.Add(aboutButton);

            FlatButton exitButton = new FlatButton
            {
                Text = "退出",
                Location = new Point(270, 610),
                Size = new Size(70, 36),
                Quiet = true,
                TabIndex = 4
            };
            exitButton.Click += delegate
            {
                EventHandler handler = ExitRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            Controls.Add(exitButton);

            _refreshButton = new FlatButton
            {
                Text = "立即刷新",
                Location = new Point(352, 610),
                Size = new Size(104, 36),
                TabIndex = 1
            };
            _refreshButton.Click += async (s, e) => await RefreshAsync();
            Controls.Add(_refreshButton);

            FormClosing += MainForm_FormClosing;
            Shown += (s, e) =>
            {
                NativeMethods.ApplyDarkWindow(Handle);
                UpdateCards();
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape && !_allowClose) Hide();
            };
        }

        public event EventHandler MinimizedToTray;
        public event EventHandler ExitRequested;

        public void UpdateSnapshot(BatterySnapshot snapshot, AppSettings settings)
        {
            if (snapshot == null) return;
            _snapshot = snapshot;
            _settings = settings ?? _settings;

            int connected = snapshot.ConnectedCount;
            int disconnected = snapshot.DisconnectedCount;
            _countLabel.Text = connected == 0
                ? "暂无已连接设备"
                : string.Format("{0} 台已连接", connected);

            if (connected > 0 && disconnected > 0 && _settings.ShowDisconnected)
            {
                _countLabel.Text += string.Format("  ·  {0} 台未连接", disconnected);
            }

            int? min = snapshot.MinimumLevel;
            if (min.HasValue)
            {
                _minLabel.Text = string.Format("最低 {0}%", min.Value);
                _minLabel.ForeColor = Theme.BatteryColor(min, _settings.LowBatteryThreshold);
            }
            else
            {
                _minLabel.Text = "暂无电量";
                _minLabel.ForeColor = Theme.TextFaint;
            }

            _statusTip.SetToolTip(_statusText, snapshot.Error ?? string.Empty);
            _statusText.Cursor = string.IsNullOrEmpty(snapshot.Error) ? Cursors.Default : Cursors.Hand;
            if (!string.IsNullOrEmpty(snapshot.Error))
            {
                _statusText.Text = "读取异常 · 点击查看详情";
                _statusText.ForeColor = Theme.Red;
            }
            else
            {
                _statusText.Text = string.Format("正在监控 · 每 {0} 秒刷新", _settings.PollSeconds);
                _statusText.ForeColor = Theme.TextMuted;
            }

            _demoBadge.Visible = snapshot.IsDemo;
            _footerLabel.Text = string.Format(
                "上次更新 {0:HH:mm:ss}",
                snapshot.Timestamp);

            UpdateCards();
        }

        public void SetBusy(bool busy)
        {
            _busy = busy;
            _refreshButton.Enabled = !busy;
            _refreshButton.Text = busy ? "刷新中…" : "立即刷新";
            if (busy)
            {
                _statusText.Text = "正在读取设备电量…";
                _statusText.ForeColor = Theme.Accent;
            }
        }

        public void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        private async Task RefreshAsync()
        {
            if (_busy) return;
            try
            {
                await _refresh();
            }
            catch (Exception ex)
            {
                Services.AppLog.Error("Manual refresh failed.", ex);
            }
        }

        private void UpdateCards()
        {
            if (IsDisposed) return;

            IEnumerable<DeviceBatteryInfo> visible = _snapshot.Devices;
            if (!_settings.ShowDisconnected)
            {
                visible = visible.Where(d => d.IsConnected);
            }
            List<DeviceBatteryInfo> devices = visible.ToList();

            _deviceList.SuspendLayout();
            Control[] oldCards = _deviceList.Controls.Cast<Control>().ToArray();
            _deviceList.Controls.Clear();
            foreach (Control control in oldCards)
            {
                control.Dispose();
            }

            int cardWidth = Math.Max(320, _deviceList.ClientSize.Width - 4);
            foreach (DeviceBatteryInfo device in devices)
            {
                DeviceCard card = new DeviceCard
                {
                    Width = cardWidth
                };
                card.UpdateDevice(device, _settings.LowBatteryThreshold);
                _deviceList.Controls.Add(card);
            }
            _deviceList.ResumeLayout();

            bool hasVisible = devices.Count > 0;
            _deviceList.Visible = hasVisible;
            _emptyState.Visible = !hasVisible;
            if (!hasVisible)
            {
                _emptyState.SetError(_snapshot.Error);
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            NativeMethods.ApplyDarkWindow(Handle);
            if (_allowClose) return;

            if (e.CloseReason == CloseReason.WindowsShutDown
                || e.CloseReason == CloseReason.TaskManagerClosing)
            {
                _allowClose = true;
                return;
            }

            e.Cancel = true;
            Hide();
            if (!_trayHintShown)
            {
                _trayHintShown = true;
                EventHandler handler = MinimizedToTray;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private static Image LoadLogo()
        {
            try
            {
                Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (icon != null)
                {
                    using (icon)
                    {
                        return icon.ToBitmap();
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private sealed class StatusCard : Panel
        {
            public StatusCard()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw,
                    true);
                BackColor = Theme.Background;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Theme.Prepare(e.Graphics);
                RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
                using (SolidBrush brush = new SolidBrush(Theme.Surface))
                {
                    Theme.FillRounded(e.Graphics, brush, rect, 14f);
                }
                using (Pen pen = new Pen(Theme.BorderSoft))
                {
                    Theme.DrawRounded(e.Graphics, pen, rect, 14f);
                }
                using (SolidBrush dot = new SolidBrush(Theme.Accent))
                {
                    e.Graphics.FillEllipse(dot, 16f, 19f, 8f, 8f);
                }
            }
        }

        private sealed class EmptyState : Control
        {
            private string _error;

            public EmptyState()
            {
                SetStyle(
                    ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint
                    | ControlStyles.ResizeRedraw
                    | ControlStyles.SupportsTransparentBackColor,
                    true);
                BackColor = Color.Transparent;
            }

            public void SetError(string error)
            {
                _error = error;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Theme.Prepare(e.Graphics);
                int centerX = Width / 2;
                RectangleF circle = new RectangleF(centerX - 34f, 38f, 68f, 68f);
                using (SolidBrush back = new SolidBrush(Theme.Surface))
                {
                    e.Graphics.FillEllipse(back, circle);
                }
                using (Pen border = new Pen(Theme.BorderSoft))
                {
                    e.Graphics.DrawEllipse(border, circle);
                }
                Theme.DrawText(e.Graphics, "\uE702", Theme.IconFont(27f), Theme.Accent,
                    new Rectangle((int)circle.X, (int)circle.Y, (int)circle.Width, (int)circle.Height),
                    Theme.CenterMiddle);

                Rectangle titleRect = new Rectangle(20, 122, Width - 40, 28);
                Theme.DrawText(e.Graphics, "未发现支持电量服务的设备", Theme.Font(11f, FontStyle.Bold),
                    Theme.Text, titleRect, Theme.CenterMiddle);

                string body = string.IsNullOrEmpty(_error)
                    ? "请先在 Windows 设置中配对蓝牙设备\r\n设备连接后会自动出现在这里"
                    : _error;
                Rectangle bodyRect = new Rectangle(36, 156, Width - 72, 62);
                Theme.DrawText(e.Graphics, body, Theme.Font(8.8f, FontStyle.Regular),
                    string.IsNullOrEmpty(_error) ? Theme.TextMuted : Theme.Amber,
                    bodyRect,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                Rectangle hintRect = new Rectangle(20, 232, Width - 40, 24);
                Theme.DrawText(e.Graphics, "支持标准蓝牙 Battery Service 与常见 2.4G HID 接收器", Theme.Font(8f, FontStyle.Regular),
                    Theme.TextFaint, hintRect, Theme.CenterMiddle);
            }
        }
    }
}

