using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using WirelessBatteryMoniter.Interop;
using WirelessBatteryMoniter.Services;

namespace WirelessBatteryMoniter.UI
{
    internal sealed class AboutForm : Form
    {
        public AboutForm()
        {
            SuspendLayout();
            Text = "关于 WirelessBatteryMoniter";
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(540, 410);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Theme.Background;
            ForeColor = Theme.Text;
            Font = Theme.Font(9.5f);
            DoubleBuffered = true;
            AutoScroll = false;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(24),
                BackColor = Color.Transparent
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(layout);

            var heading = new TableLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2,
                Margin = new Padding(0, 0, 0, 20), BackColor = Color.Transparent
            };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var logo = new PictureBox
            {
                Size = new Size(52, 52), SizeMode = PictureBoxSizeMode.Zoom,
                Image = LoadLogo(), Margin = new Padding(0), BackColor = Color.Transparent
            };
            heading.Controls.Add(logo, 0, 0);
            heading.SetRowSpan(logo, 2);
            heading.Controls.Add(new Label
            {
                Text = "WirelessBatteryMoniter", AutoSize = true, Dock = DockStyle.Fill,
                Font = Theme.Font(13.5f, FontStyle.Bold), ForeColor = Theme.Text,
                Margin = new Padding(0, 0, 0, 4)
            }, 1, 0);
            heading.Controls.Add(new Label
            {
                Text = "无线设备电量监控 · v" + Application.ProductVersion,
                AutoSize = true, Dock = DockStyle.Fill,
                ForeColor = Theme.TextMuted, Margin = new Padding(0)
            }, 1, 1);
            layout.Controls.Add(heading);
            FormClosed += (s, e) => { if (logo.Image != null) logo.Image.Dispose(); };

            AddParagraph(layout,
                "在系统托盘查看无线鼠标、键盘、耳机和手柄的电量。\r\n"
                + "悬停托盘图标可查看设备名称与百分比，低于阈值时推送提醒。",
                Theme.TextMuted);
            AddParagraph(layout,
                "数据来源\r\n"
                + "· 蓝牙 LE GATT Battery Service（0x180F / 0x2A19）\r\n"
                + "· Windows 缓存的设备电量属性（部分经典蓝牙设备）\r\n"
                + "· 2.4G HID：罗技 HID++、ATK/VXE/CompX、XINMENG",
                Theme.TextMuted);
            AddParagraph(layout,
                "限制\r\n"
                + "· 蓝牙设备需要已配对并处于连接状态\r\n"
                + "· 2.4G 仅覆盖已实现的协议家族，其他型号可能不显示",
                Theme.TextMuted);

            var actions = new TableLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2,
                Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var folder = new FlatButton { Text = "打开配置目录", Size = new Size(140, 38), Margin = new Padding(0) };
            folder.Click += (s, e) => OpenFolder();
            actions.Controls.Add(folder, 0, 0);
            var close = new FlatButton { Text = "关闭", Size = new Size(88, 38), Accent = true, Margin = new Padding(0) };
            close.Click += (s, e) => Close();
            actions.Controls.Add(close, 1, 0);
            layout.Controls.Add(actions);

            AutoScaleDimensions = new SizeF(96,96); ResumeLayout(true);
            Shown += (s, e) => Invalidate();
        }

        private static void AddParagraph(TableLayoutPanel layout, string text, Color color)
        {
            var label = new Label
            {
                Text = text, AutoSize = true, Dock = DockStyle.Top,
                ForeColor = color, BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 18), AutoEllipsis = false
            };
            // Constrain width only: preferred height grows with wrapping and DPI.
            layout.SizeChanged += (s, e) =>
            {
                int width = Math.Max(1, layout.ClientSize.Width - layout.Padding.Horizontal);
                if (label.MaximumSize.Width != width) label.MaximumSize = new Size(width, 0);
            };
            layout.Controls.Add(label);
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

        private static void OpenFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppLog.DirectoryPath);
                Process.Start("explorer.exe", AppLog.DirectoryPath);
            }
            catch (Exception ex)
            {
                AppLog.Error("Failed to open log folder.", ex);
            }
        }
    }
}

