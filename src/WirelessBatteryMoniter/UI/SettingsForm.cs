using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Models;
using WirelessBatteryMoniter.Services;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _source;
        private readonly Action<AppSettings> _save;
        private readonly Action _test;

        private readonly NumberStepper _pollSeconds;
        private readonly ToggleSwitch _showDisconnected;
        private readonly ToggleSwitch _notifications;
        private readonly NumberStepper _threshold;
        private readonly ToggleSwitch _sound;
        private readonly FlatButton _testButton;
        private readonly ToggleSwitch _startup;
        private readonly ToggleSwitch _startMinimized;

        public SettingsForm(AppSettings settings, Action<AppSettings> save, Action test)
        {
            _source = settings.Clone();
            _save = save;
            _test = test;

            Text = "设置 · WirelessBatteryMoniter";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(456, 696);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
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

            AddHeader();

            AddSection("监控", 92);
            AddRow("刷新间隔", "轮询蓝牙电量的时间间隔", 120);
            _pollSeconds = new NumberStepper
            {
                Location = new Point(306, 118),
                Size = new Size(126, 38),
                Minimum = 5,
                Maximum = 120,
                Value = _source.PollSeconds,
                Suffix = " 秒"
            };
            Controls.Add(_pollSeconds);

            AddRow("显示未连接设备", "保留上次电量并以灰色显示", 184);
            _showDisconnected = new ToggleSwitch
            {
                Location = new Point(384, 188),
                Checked = _source.ShowDisconnected
            };
            Controls.Add(_showDisconnected);

            AddDivider(236);
            AddSection("低电量提醒", 254);
            AddRow("启用桌面提醒", "低于阈值时推送一次系统通知", 282);
            _notifications = new ToggleSwitch
            {
                Location = new Point(384, 286),
                Checked = _source.NotificationsEnabled
            };
            _notifications.CheckedChanged += (s, e) => UpdateNotificationState();
            Controls.Add(_notifications);

            AddRow("电量阈值", "达到或低于该值时提醒", 336);
            _threshold = new NumberStepper
            {
                Location = new Point(306, 334),
                Size = new Size(126, 38),
                Minimum = 5,
                Maximum = 95,
                Value = _source.LowBatteryThreshold,
                Suffix = " %"
            };
            Controls.Add(_threshold);

            AddRow("提示音", "显示通知时播放系统提示音", 390);
            _sound = new ToggleSwitch
            {
                Location = new Point(384, 394),
                Checked = _source.NotificationSound
            };
            Controls.Add(_sound);

            _testButton = new FlatButton
            {
                Text = "发送测试提醒",
                Location = new Point(24, 434),
                Size = new Size(132, 36)
            };
            _testButton.Click += (s, e) =>
            {
                if (_test != null) _test();
            };
            Controls.Add(_testButton);

            AddDivider(488);
            AddSection("启动", 506);
            AddRow("开机自动启动", "登录 Windows 后自动在托盘运行", 534);
            _startup = new ToggleSwitch
            {
                Location = new Point(384, 538),
                Checked = StartupService.IsEnabled()
            };
            Controls.Add(_startup);

            AddRow("启动时最小化", "启动后直接进入系统托盘", 588);
            _startMinimized = new ToggleSwitch
            {
                Location = new Point(384, 592),
                Checked = _source.StartMinimized
            };
            Controls.Add(_startMinimized);


            FlatButton cancel = new FlatButton
            {
                Text = "取消",
                Location = new Point(240, 640),
                Size = new Size(88, 38)
            };
            cancel.Click += (s, e) =>
            {
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);

            FlatButton saveButton = new FlatButton
            {
                Text = "保存设置",
                Location = new Point(336, 640),
                Size = new Size(96, 38),
                Accent = true
            };
            saveButton.Click += (s, e) => SaveAndClose();
            Controls.Add(saveButton);

            LinkLabel folder = new LinkLabel
            {
                Text = "打开配置文件夹",
                Location = new Point(24, 650),
                Size = new Size(150, 20),
                Font = Theme.Font(8f),
                LinkColor = Theme.TextMuted,
                ActiveLinkColor = Theme.Accent,
                VisitedLinkColor = Theme.TextMuted,
                LinkBehavior = LinkBehavior.NeverUnderline,
                BackColor = Color.Transparent,
                TabStop = false
            };
            folder.LinkClicked += (s, e) =>
            {
                try
                {
                    System.IO.Directory.CreateDirectory(AppLog.DirectoryPath);
                    Process.Start("explorer.exe", AppLog.DirectoryPath);
                }
                catch (Exception ex)
                {
                    AppLog.Error("Failed to open settings folder.", ex);
                }
            };
            Controls.Add(folder);

            Label version = new Label
            {
                Text = "WirelessBatteryMoniter  v" + Application.ProductVersion,
                Location = new Point(24, 675),
                Size = new Size(260, 16),
                Font = Theme.Font(7.5f),
                ForeColor = Theme.TextFaint,
                BackColor = Color.Transparent
            };
            Controls.Add(version);

            Shown += (s, e) => NativeMethods.ApplyDarkWindow(Handle);
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                }
            };

            UpdateNotificationState();
        }

        private void AddHeader()
        {
            Label title = new Label
            {
                Text = "设置",
                Location = new Point(24, 18),
                Size = new Size(200, 32),
                Font = Theme.Font(16f, FontStyle.Bold),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(title);

            Label subtitle = new Label
            {
                Text = "WirelessBatteryMoniter · 无线设备电量监控",
                Location = new Point(26, 52),
                Size = new Size(400, 20),
                Font = Theme.Font(8.5f),
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(subtitle);
        }

        private void AddSection(string text, int y)
        {
            Label label = new Label
            {
                Text = text,
                Location = new Point(24, y),
                Size = new Size(300, 22),
                Font = Theme.Font(9.5f, FontStyle.Bold),
                ForeColor = Theme.Accent,
                BackColor = Color.Transparent
            };
            Controls.Add(label);
        }

        private void AddRow(string title, string description, int y)
        {
            Label titleLabel = new Label
            {
                Text = title,
                Location = new Point(24, y),
                Size = new Size(260, 22),
                Font = Theme.Font(9.5f, FontStyle.Regular),
                ForeColor = Theme.Text,
                BackColor = Color.Transparent
            };
            Controls.Add(titleLabel);

            Label descriptionLabel = new Label
            {
                Text = description,
                Location = new Point(24, y + 22),
                Size = new Size(270, 18),
                Font = Theme.Font(8f, FontStyle.Regular),
                ForeColor = Theme.TextMuted,
                BackColor = Color.Transparent
            };
            Controls.Add(descriptionLabel);
        }

        private void AddDivider(int y)
        {
            Panel divider = new Panel
            {
                Location = new Point(24, y),
                Size = new Size(408, 1),
                BackColor = Theme.BorderSoft
            };
            Controls.Add(divider);
        }

        private void UpdateNotificationState()
        {
            bool enabled = _notifications.Checked;
            _threshold.Enabled = enabled;
            _sound.Enabled = enabled;
            _testButton.Enabled = enabled;
        }

        private void SaveAndClose()
        {
            AppSettings updated = _source.Clone();
            updated.PollSeconds = _pollSeconds.Value;
            updated.ShowDisconnected = _showDisconnected.Checked;
            updated.NotificationsEnabled = _notifications.Checked;
            updated.LowBatteryThreshold = _threshold.Value;
            updated.NotificationSound = _sound.Checked;
            updated.StartMinimized = _startMinimized.Checked;
            updated.StartWithWindows = _startup.Checked;
            updated.Normalize();

            if (_save != null) _save(updated);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
